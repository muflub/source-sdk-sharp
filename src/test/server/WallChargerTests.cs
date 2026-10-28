using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The wall chargers, CNewWallHealth (item_healthkit.cpp:529) and CNewRecharge
/// (func_recharge.cpp:583): held +USE gives a point a tenth of a second while
/// the juice lasts, with stock's sounds and outputs, and an empty one refills
/// later. Found beside stock (2026-10-26): here they were touch pickups that
/// topped a player up by fifty on contact, where stock's ask for +USE.
/// </summary>
public class WallChargerTests
{
    private static readonly EntityHandle Charger = new(0x0002_0050);
    private static readonly EntityHandle Player = new(0x0001_0001);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 10f;
        public int Health = 50;

        public WallCharger Entity => (WallCharger)Registry.Get(Charger)!;
        public SsPlayer PlayerEntity => (SsPlayer)Registry.Get(Player)!;

        /// <summary>One command's continuous use, <paramref name="seconds"/> after the last.</summary>
        public void Use(float seconds = 1f / 66f)
        {
            Now += seconds;
            Registry.Input(Charger, "Use", "", 0f, Player);
        }

        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToArray();
        public string[] Stopped => Engine.AllOf("Sound.EntityStopSound").Select(c => (string)c.Args[1]!).ToArray();
    }

    private static Rig Spawned(string className, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(WallCharger).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .ReturnsEachCall("Entities.GetHealth", () => rig.Health)
              .WhenCalled("Entities.SetHealth", c => rig.Health = c.ArgAs<int>(1))
              .Returns("Entities.IsValid", true)
              .Returns("Entities.IsPlayer", true)
              .ReturnsEachCall("Entities.GetSpawnFlags",
                  () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Charger ? spawnFlags : 0)
              .ReturnsEachCall("EntityRender.LookupSequence",
                  () => (string)engine.Calls.Last().Args[1]! == "idle" ? 0 : 1);

        registry.KeyValue(Player, EntityNames.Player, "targetname", "user");
        registry.Spawn(Charger, className);
        return rig;
    }

    private static int Played(Rig rig, int sequence, float rate)
        => rig.Engine.AllOf("EntityRender.PlaySequence")
              .Count(c => c.ArgAs<int>(1) == sequence && c.ArgAs<float>(2) == rate);

    //-------------------------------------------------------------------------
    // The health charger
    //-------------------------------------------------------------------------

    [Fact]
    public void A_health_charger_is_a_static_solid_fixture_shown_full()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "EntityPhysics.SetPhysicsStatic" && c.ArgAs<bool>(1));
        // A brush's solidity is SOLID_BSP, which a studio model is not traced as:
        // the +USE line went through the charger it was set on (live, 2026-10-26).
        Assert.False(rig.Engine.Called("EntityPhysics.SetBrushSolidity"), "the static object is the solidity");
        Assert.Equal(1, Played(rig, 0, 0f));
        Assert.Equal(UseCaps.ContinuousUse, rig.Entity.ObjectCaps);
    }

    [Fact]
    public void Held_use_gives_a_point_a_tenth_of_a_second_with_the_start_then_the_loop()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);
        var charger = (ItemHealthCharger)rig.Entity;

        rig.Use();
        Assert.Equal(51, rig.Health);
        Assert.Equal(1, charger.OnPlayerUse.FireCount);
        Assert.Equal(["WallHealth.Start"], rig.Sounds);

        // Held for a second at 66 commands: ten more points, no faster.
        for (int i = 0; i < 66; i++)
            rig.Use();

        Assert.InRange(rig.Health, 60, 61);
        Assert.Equal(1, charger.OnPlayerUse.FireCount);
        Assert.Contains("WallHealth.LoopingContinueCharge", rig.Sounds);
        Assert.Equal((rig.Health - 50) / 1, (int)ItemHealthCharger.FullJuice - Juice(charger));
    }

    private static int Juice(WallCharger charger)
        => (int)typeof(WallCharger).GetField("Juice", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                   .GetValue(charger)!;

    [Fact]
    public void Letting_go_turns_it_off_and_stops_the_loop()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);

        for (int i = 0; i < 66; i++)
            rig.Use();

        rig.Now += WallCharger.ChargeRate + 0.01f;
        rig.Entity.OnThink("ChargerOff");

        Assert.Equal(["WallHealth.LoopingContinueCharge"], rig.Stopped);
    }

    [Fact]
    public void A_full_player_is_denied_and_must_press_again()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);
        rig.Health = 100;

        rig.Use();

        Assert.Equal(100, rig.Health);
        Assert.Equal(["WallHealth.Deny"], rig.Sounds);
        Assert.Equal(UseCaps.ImpulseUse, rig.Entity.ObjectCaps);
    }

    [Fact]
    public void An_empty_charger_denies_shows_empty_and_refills_a_minute_later()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);
        rig.Health = 1;

        // Fifty points, a tenth of a second apart.
        for (int i = 0; i < 50; i++)
            rig.Use(0.11f);
        Assert.Equal(51, rig.Health);
        Assert.Equal(0, Juice(rig.Entity));

        rig.Engine.Clear();
        rig.Use(0.11f);
        rig.Use(0.11f);

        Assert.Equal(51, rig.Health);
        Assert.Equal(1, Played(rig, 1, 1f));   // "emptyclick", once
        Assert.Contains("WallHealth.Deny", rig.Sounds);
        // Booked on the first empty use, one use (0.11 s) ago.
        Assert.InRange(rig.Entity.NextThinkDelay("ChargerRecharge"), 59.8f, 60f);

        rig.Engine.Clear();
        rig.Entity.OnThink("ChargerRecharge");

        Assert.Equal((int)ItemHealthCharger.FullJuice, Juice(rig.Entity));
        Assert.Equal(["WallHealth.Recharge"], rig.Sounds);
        Assert.Equal(1, Played(rig, 0, 0f));
    }

    [Fact]
    public void The_deny_of_an_empty_charger_is_no_more_often_than_every_062_seconds()
    {
        var rig = Spawned(EntityNames.ItemHealthcharger);
        rig.Health = 1;
        for (int i = 0; i < 50; i++)
            rig.Use(0.11f);

        rig.Engine.Clear();
        for (int i = 0; i < 66; i++)
            rig.Use();

        Assert.Equal(2, rig.Sounds.Count(s => s == "WallHealth.Deny"));
    }

    //-------------------------------------------------------------------------
    // The suit charger
    //-------------------------------------------------------------------------

    [Fact]
    public void A_suit_charger_gives_armour_and_says_when_it_is_half_empty_and_empty()
    {
        var rig = Spawned(EntityNames.ItemSuitcharger);
        var charger = (ItemSuitCharger)rig.Entity;

        for (int i = 0; i < 37; i++)
            rig.Use(0.11f);
        Assert.Equal(37, rig.PlayerEntity.Armour);
        Assert.Equal(0, charger.OnHalfEmpty.FireCount);

        rig.Use(0.11f);
        Assert.Equal(1, charger.OnHalfEmpty.FireCount);

        for (int i = 0; i < 37; i++)
            rig.Use(0.11f);
        Assert.Equal(75, rig.PlayerEntity.Armour);
        Assert.Equal(1, charger.OnEmpty.FireCount);
        Assert.Contains("SuitRecharge.ChargingLoop", rig.Sounds);
    }

    [Fact]
    public void An_empty_suit_charger_refills_thirty_seconds_later_and_says_so()
    {
        var rig = Spawned(EntityNames.ItemSuitcharger);
        var charger = (ItemSuitCharger)rig.Entity;
        for (int i = 0; i < 76; i++)
            rig.Use(0.11f);

        Assert.Equal(30f, rig.Entity.NextThinkDelay("ChargerRecharge"), 2);

        rig.Entity.OnThink("ChargerRecharge");
        Assert.Equal(1, charger.OnFull.FireCount);
        Assert.Equal((int)ItemSuitCharger.FullJuice, Juice(rig.Entity));
    }

    [Fact]
    public void A_citadel_charger_gives_two_armour_and_five_health_up_to_200()
    {
        var rig = Spawned(EntityNames.ItemSuitcharger, spawnFlags: 0x2000);
        rig.Health = 50;

        rig.Use();

        Assert.Equal(2, rig.PlayerEntity.Armour);
        Assert.Equal(55, rig.Health);
        Assert.Equal((int)ItemSuitCharger.CitadelJuice - 2, Juice(rig.Entity));
    }

    [Fact]
    public void Kleiners_charger_holds_twenty_five()
        => Assert.Equal((int)ItemSuitCharger.KleinerJuice, Juice(Spawned(EntityNames.ItemSuitcharger, 0x4000).Entity));

    [Fact]
    public void SetCharge_fills_it_to_a_new_size()
    {
        var rig = Spawned(EntityNames.ItemSuitcharger);

        rig.Registry.Input(Charger, "SetCharge", "10", 10f, EntityHandle.None);

        Assert.Equal(10, Juice(rig.Entity));
    }
}
