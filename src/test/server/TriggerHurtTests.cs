using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// trigger_hurt as stock's CTriggerHurt (triggers.cpp): damage per SECOND,
/// dealt every half second as half of it through the damage path (so armour,
/// god mode and the Damage message see it), of the mapper's damagetype, with
/// the trigger as attacker; OnHurtPlayer for players and OnHurt for the rest;
/// negative damage heals; leaving unhurt costs half; damagemodel 1 doubles
/// up to damagecap; a radiation trigger tells nearby players' geiger.
/// </summary>
public class TriggerHurtTests
{
    private static readonly EntityHandle Ent = new(0x0011_0011u);
    private static readonly EntityHandle Player = new(0x0012_0012u);
    private static readonly EntityHandle Crate = new(0x0013_0013u);

    private static (ScriptedEntityRegistry Registry, TriggerHurt Trigger, RecordingEngine Engine) Spawn(
        int health = 100, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerHurt).Assembly);
        var services = Services(out var engine);
        engine.Ignores("Clock.Now", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                       "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.SetHealth")
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetHealth", health)
              .Returns("Host.RandomFloat", 0.25f)
              .Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 0f))
              .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.ReturnsEachCall("Entities.IsPlayer", () => engine.Calls[^1].Args[0] is EntityHandle h && h == Player);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new PvsCheckClient(registry, engine, engine));
        registry.Services = services;
        registry.KeyValue(Ent, EntityNames.TriggerHurt, "wait", "0");
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, EntityNames.TriggerHurt, key, value);
        registry.Spawn(Ent, EntityNames.TriggerHurt);
        engine.Clear();
        return (registry, (TriggerHurt)registry.Get(Ent)!, engine);
    }

    private static List<EngineCall> Hurts(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member is "Damage.Apply" or "Damage.TakeDamage").ToList();

    [Fact]
    public void A_toucher_takes_half_the_damage_per_second_through_the_damage_path_with_the_trigger_as_attacker()
    {
        var (registry, _, engine) = Spawn(keys: [("damage", "20"), ("damagetype", "1048576"), ("nodmgforce", "1")]);

        registry.Touch(Ent, Player);
        registry.Think(Ent);

        var hit = Assert.Single(Hurts(engine));
        Assert.Equal(("Damage.Apply", Player, Ent, 10f, (DamageType)1048576),
                     (hit.Member, (EntityHandle)hit.Args[0]!, (EntityHandle)hit.Args[1]!, (float)hit.Args[2]!, (DamageType)hit.Args[3]!));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetHealth");
    }

    [Fact]
    public void A_player_fires_OnHurtPlayer_and_anything_else_OnHurt()
    {
        var (registry, trigger, _) = Spawn(keys: [("damage", "20")]);

        registry.Touch(Ent, Player);
        registry.Touch(Ent, Crate);
        registry.Think(Ent);

        Assert.Equal((1, 1), (trigger.OnHurtPlayer.FireCount, trigger.OnHurt.FireCount));
    }

    [Fact]
    public void Negative_damage_heals_a_player_up_to_a_hundred()
    {
        var (registry, _, engine) = Spawn(health: 95, keys: [("damage", "-20")]);

        registry.Touch(Ent, Player);
        registry.Think(Ent);

        Assert.Empty(Hurts(engine));
        var heal = Assert.Single(engine.AllOf("Entities.SetHealth"));
        Assert.Equal(100, (int)heal.Args[1]!);
    }

    [Fact]
    public void Leaving_before_ever_being_hurt_costs_half_the_damage()
    {
        var (registry, _, engine) = Spawn(keys: [("damage", "20"), ("nodmgforce", "1")]);
        registry.Touch(Ent, Player);   // the think has not run yet

        registry.EndTouch(Ent, Player);

        var hit = Assert.Single(Hurts(engine));
        Assert.Equal(10f, (float)hit.Args[2]!);
    }

    [Fact]
    public void Double_forgiveness_doubles_the_damage_each_hurt_up_to_the_cap()
    {
        var (registry, _, engine) = Spawn(keys: [("damage", "10"), ("damagecap", "30"), ("damagemodel", "1"), ("nodmgforce", "1")]);
        registry.Touch(Ent, Player);

        registry.Think(Ent);   // 10 * 0.5, then 20
        registry.Think(Ent);   // 20 * 0.5, then 30 (capped)
        registry.Think(Ent);   // 30 * 0.5

        Assert.Equal(new[] { 5f, 10f, 15f }, Hurts(engine).Select(c => (float)c.Args[2]!).ToArray());
    }

    [Fact]
    public void An_empty_trigger_stops_thinking()
    {
        var (registry, _, engine) = Spawn(keys: [("damage", "10")]);
        registry.Touch(Ent, Player);
        registry.EndTouch(Ent, Player);
        engine.Clear();

        registry.Think(Ent);

        Assert.Empty(Hurts(engine));
        Assert.DoesNotContain(engine.Calls, c => c.Member.Contains("NextThink", StringComparison.Ordinal) && c.Args.Length > 1 && c.Args[1] is float f && f > 0f);
    }
}

/// <summary>
/// Stock's RadiationThink tells ONE player their range: UTIL_FindClientInPVS's
/// check client (cycled every tenth of a second over the players), and only
/// when the trigger's box is in that client's PVS.
/// </summary>
public class TriggerHurtRadiationTests
{
    private static readonly EntityHandle Ent = new(0x0011_0011u);
    private static readonly EntityHandle P1 = new(0x0012_0001u);
    private static readonly EntityHandle P2 = new(0x0013_0002u);

    private (ScriptedEntityRegistry Registry, RecordingEngine Engine, SsPlayer A, SsPlayer B) Rig(bool inPvs = true)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerHurt).Assembly);
        var services = TestServices.Services(out var engine);
        float now = 0f;
        engine.Ignores("Networking.EntityGetState", "Networking.EntityGetState.moveType",
                       "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.SetHealth")
              .ReturnsEachCall("Clock.Now", () => now)
              .Returns("Entities.IsValid", true)
              .Returns("Host.RandomFloat", 0.25f)
              .Returns("Players.BoxInPvs", inPvs)
              .ReturnsEachCall("Entities.GetIndex", () => engine.Calls[^1].Args[0] is EntityHandle h ? (h == P1 ? 1 : h == P2 ? 2 : 40) : 40)
              .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
              .Returns("Entities.GetBounds", (new Vector(-10f, -10f, -10f), new Vector(10f, 10f, 10f)))
              .Returns("Entities.GetWorldSpaceCenter", new Vector(110f, 0f, 0f));   // 100 from the box
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new PvsCheckClient(registry, engine, engine));
        registry.Services = services;
        registry.KeyValue(P1, EntityNames.SsPlayer, "targetname", "a");
        registry.KeyValue(P2, EntityNames.SsPlayer, "targetname", "b");
        registry.KeyValue(Ent, EntityNames.TriggerHurt, "damagetype", ((int)DamageType.Radiation).ToString());
        registry.Spawn(Ent, EntityNames.TriggerHurt);
        Clock = t => now = t;
        return (registry, engine, (SsPlayer)registry.Get(P1)!, (SsPlayer)registry.Get(P2)!);
    }

    // This fact's clock (xUnit builds one instance per fact).
    private Action<float> Clock = _ => { };

    [Fact]
    public void Only_the_check_client_is_told_three_times_its_distance()
    {
        var (registry, _, a, b) = Rig();

        Clock(1f);
        registry.Think(Ent);

        // The first check client after slot 1 is slot 2 (stock starts its
        // walk from the slot after the last check, which begins at 1).
        Assert.Equal((SsPlayer.GeigerFar, 300f), (a.GeigerRange, b.GeigerRange));
    }

    [Fact]
    public void The_check_client_moves_on_a_tenth_of_a_second_later()
    {
        var (registry, _, a, _) = Rig();
        Clock(1f);
        registry.Think(Ent);
        Clock(1.25f);
        registry.Think(Ent);
        Assert.Equal(300f, a.GeigerRange);
    }

    [Fact]
    public void A_box_outside_the_check_clients_pvs_tells_nobody()
    {
        var (registry, _, a, b) = Rig(inPvs: false);
        Clock(1f);
        registry.Think(Ent);
        Assert.Equal((SsPlayer.GeigerFar, SsPlayer.GeigerFar), (a.GeigerRange, b.GeigerRange));
    }

    /// <summary>
    /// The PVS is the check client's own, asked from its eyes about the
    /// trigger's WORLD box (spawn origin + local bounds, the box the distance
    /// is measured to), and only the check client's: one query a think, not
    /// one per player.
    /// </summary>
    [Fact]
    public void Only_the_check_clients_pvs_is_asked_about_the_world_box()
    {
        var (registry, engine, _, _) = Rig(inPvs: false);
        Clock(1f);
        engine.Clear();

        registry.Think(Ent);

        var asks = engine.Calls
            .Where(c => c.Member == "Players.BoxInPvs")
            .Select(c => (Player: (EntityHandle)c.Args[0]!, Mins: (Vector)c.Args[1]!, Maxs: (Vector)c.Args[2]!))
            .ToList();
        Assert.Equal(new[] { (P2, new Vector(-10f, -10f, -10f), new Vector(10f, 10f, 10f)) }, asks);
    }

    /// <summary>
    /// A player who cannot see the box is not told even as the check client,
    /// and one who can is told once the check moves on to them.
    /// </summary>
    [Fact]
    public void A_check_client_who_cannot_see_the_box_hears_nothing_and_one_who_can_hears_it()
    {
        var (registry, engine, a, b) = Rig();
        engine.ReturnsEachCall("Players.BoxInPvs", () => (EntityHandle)engine.Calls[^1].Args[0]! == P1);

        Clock(1f);
        registry.Think(Ent);   // the check client is slot 2, which cannot see it
        Assert.Equal((SsPlayer.GeigerFar, SsPlayer.GeigerFar), (a.GeigerRange, b.GeigerRange));

        Clock(1.25f);
        registry.Think(Ent);   // slot 1, which can
        Assert.Equal((300f, SsPlayer.GeigerFar), (a.GeigerRange, b.GeigerRange));
    }
}
