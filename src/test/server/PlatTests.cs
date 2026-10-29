using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_plat against stock's CFuncPlat, CBasePlatTrain and CPlatTrigger
/// (trains.cpp): where it starts, the ride trigger that sends it and holds it,
/// the 3 s return from the top, the toggle flag, the crush and the noises.
/// Found beside stock (ss_sandbox census, 2026-10-27): stock's plat_trigger
/// had no counterpart here, so a player stepping onto an unnamed lift went
/// nowhere.
/// </summary>
public class PlatTests
{
    private static readonly EntityHandle Plat = new(0x0006_0101u);
    private static readonly EntityHandle Trigger = new(0x0006_0102u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public readonly ThinkScheduler Thinks = new();
        public float Now = 10f;
        public int Health = 100;

        public FuncPlat Entity => (FuncPlat)Registry.Get(Plat)!;

        public Vector LastMove => Engine.Last("BrushMovement.MoveToPosition")!.Value.ArgAs<Vector>(1);

        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();
    }

    private static Rig Spawned(int spawnFlags = 0, string name = "")
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPlat).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Entities.GetSpawnOrigin", new Vector(0f, 0f, 100f))
              .Returns("Entities.GetOrigin", new Vector(0f, 0f, 100f))
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetBoundsSize", new Vector(128f, 40f, 16f))
              .Returns("Entities.GetBounds", (new Vector(-64f, -20f, -8f), new Vector(64f, 20f, 8f)))
              .Returns("Entities.Create", Trigger)
              .Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player)
              .ReturnsEachCall("Entities.GetHealth", () => rig.Health);

        registry.KeyValue(Plat, EntityNames.FuncPlat, "origin", "0 0 100");
        if (name.Length > 0)
            registry.KeyValue(Plat, EntityNames.FuncPlat, "targetname", name);
        engine.Returns("Entities.GetName", name);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Plat, EntityNames.FuncPlat);
        return rig;
    }

    [Fact]
    public void An_unnamed_plat_starts_at_the_bottom_of_its_drop()
        => Assert.Equal(new Vector(0f, 0f, 92f), Spawned().Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1));

    [Fact]
    public void A_named_plat_starts_at_the_top()
        => Assert.Equal(new Vector(0f, 0f, 100f), Spawned(name: "lift").Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1));

    /// <summary>
    /// CPlatTrigger::SpawnInsideTrigger's box, as stock writes it: the mins in
    /// 25 and the maxs out 25, eight above the top, down over the drop; an arm
    /// 50 wide or less pinched to a unit at its middle.
    /// </summary>
    [Fact]
    public void The_ride_trigger_covers_the_drop_in_stocks_box()
    {
        var rig = Spawned();

        var bounds = rig.Engine.Last("Entities.SetBounds")!.Value;
        Assert.Equal(Trigger, bounds.ArgAs<EntityHandle>(0));
        Assert.Equal((new Vector(-39f, 0f, 0f), new Vector(89f, 1f, 16f)), (bounds.ArgAs<Vector>(1), bounds.ArgAs<Vector>(2)));
        Assert.Equal(SolidFlags.Trigger, rig.Engine.Last("Entities.SetSolidity")!.Value.ArgAs<SolidFlags>(2));
    }

    [Fact]
    public void A_toggle_plat_has_no_ride_trigger()
        => Assert.False(Spawned(FuncPlat.ToggleFlag).Engine.Called("Entities.Create"));

    [Fact]
    public void A_live_player_on_it_sends_it_up_and_holds_it_at_the_top()
    {
        var rig = Spawned();

        rig.Entity.RiderTouched(Player);
        Assert.Equal(new Vector(0f, 0f, 100f), rig.LastMove);

        rig.Entity.OnMoveDone();
        Assert.Equal(13f, rig.Thinks.NextThinkTime(Plat, "PlatDown"), 3);

        rig.Now = 12.5f;
        rig.Entity.RiderTouched(Player);
        Assert.Equal(13.5f, rig.Thinks.NextThinkTime(Plat, "PlatDown"), 3);
    }

    [Fact]
    public void A_dead_player_sends_it_nowhere()
    {
        var rig = Spawned();
        rig.Health = 0;

        rig.Entity.RiderTouched(Player);

        Assert.False(rig.Engine.Called("BrushMovement.MoveToPosition"));
    }

    [Fact]
    public void A_plat_comes_down_three_seconds_after_the_top_unless_it_is_a_toggle()
    {
        var rig = Spawned();
        rig.Entity.RiderTouched(Player);
        rig.Entity.OnMoveDone();

        rig.Entity.OnThink("PlatDown");
        Assert.Equal(new Vector(0f, 0f, 92f), rig.LastMove);

        var toggle = Spawned(FuncPlat.ToggleFlag);
        toggle.Registry.Input(Plat, "GoUp", "", 0f, EntityHandle.None);
        toggle.Entity.OnMoveDone();
        Assert.False(toggle.Thinks.IsScheduled(Plat, "PlatDown"));
    }

    [Fact]
    public void A_blocked_plat_crushes_for_one_and_goes_back()
    {
        var rig = Spawned();
        rig.Entity.RiderTouched(Player);

        rig.Entity.OnBlocked(Player);

        var hurt = rig.Engine.Last("Damage.Apply")!.Value;
        Assert.Equal((Player, Plat, 1f, DamageType.Crush),
                     (hurt.ArgAs<EntityHandle>(0), hurt.ArgAs<EntityHandle>(1), hurt.ArgAs<float>(2), hurt.ArgAs<DamageType>(3)));
        Assert.Equal(new Vector(0f, 0f, 92f), rig.LastMove);
    }

    [Fact]
    public void Its_noises_are_stocks_when_the_map_names_none()
    {
        var rig = Spawned();
        rig.Entity.RiderTouched(Player);
        rig.Entity.OnMoveDone();

        // CBasePlatTrain::PlayMovingSound: a CHAN_STATIC patch at ATTN_NORM, at
        // the volume (0.85 unset), destroyed on arrival; the arrival a script sound.
        var patch = rig.Engine.Last("Sound.SoundPatchCreate")!.Value;
        Assert.Equal(("Plat.DefaultMoving", 6, 75), (patch.ArgAs<string>(2), patch.ArgAs<int>(1), patch.ArgAs<int>(3)));
        Assert.Equal(0.85f, rig.Engine.Last("Sound.SoundPatchEmit")!.Value.ArgAs<float>(1), 3);
        Assert.True(rig.Engine.Called("Sound.SoundPatchStop") && rig.Engine.Called("Sound.SoundPatchFree"));
        Assert.Equal(new[] { "Plat.DefaultArrive" }, rig.Sounds);
    }

    [Fact]
    public void Its_moving_noise_plays_at_its_volume()
    {
        var rig = Spawned();
        rig.Registry.KeyValue(Plat, EntityNames.FuncPlat, "volume", "0.4");
        rig.Entity.RiderTouched(Player);

        Assert.Equal(0.4f, rig.Engine.Last("Sound.SoundPatchEmit")!.Value.ArgAs<float>(1), 3);
    }

    [Fact]
    public void Toggle_sends_a_toggle_plat_to_its_other_end_and_a_plain_one_only_down()
    {
        var toggle = Spawned(FuncPlat.ToggleFlag);
        toggle.Registry.Input(Plat, "Toggle", "", 0f, EntityHandle.None);
        Assert.Equal(new Vector(0f, 0f, 100f), toggle.LastMove);

        var plain = Spawned();
        plain.Registry.Input(Plat, "Toggle", "", 0f, EntityHandle.None);
        Assert.False(plain.Engine.Called("BrushMovement.MoveToPosition"));
    }

    [Fact]
    public void The_ride_trigger_hands_its_platform_every_touch()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPlat).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        registry.KeyValue(Trigger, EntityNames.PlatTrigger, "origin", "0 0 0");
        registry.Spawn(Trigger, EntityNames.PlatTrigger);
        var trigger = (PlatTrigger)registry.Get(Trigger)!;

        Assert.True(trigger.TouchEveryTick);

        var rig = Spawned();
        trigger.Platform = rig.Entity;
        trigger.OnTouch(Player);
        Assert.Equal(new Vector(0f, 0f, 100f), rig.LastMove);
    }
}
