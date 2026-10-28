using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// momentary_rot_button under a player's +USE, against CMomentaryRotButton
/// (buttons.cpp:1310-1510). Found beside stock (2026-10-27): here one press
/// turned the wheel the whole way, where stock turns it only while +USE is
/// held, each use re-arming a tenth-of-a-second move-done; and the wheel
/// offered no use caps, so +USE never reached it.
/// </summary>
public class MomentaryRotButtonUseTests
{
    private static readonly EntityHandle Wheel = new(0x0001_00D0);

    private const int UseActivates = 0x400;
    private const int Toggle = 0x20;
    private const int Jiggles = 0x2000;

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 10f;
        public float Yaw;

        public MomentaryRotButton Entity => (MomentaryRotButton)Registry.Get(Wheel)!;

        public void Use() => Registry.Input(Wheel, "Use", "", 0f, EntityHandle.None);

        public Vector[] Spins => Engine.AllOf("BrushMovement.Spin").Select(c => c.ArgAs<Vector>(1)).ToArray();

        public void MoveDone() => Entity.OnThink("MoveDone");
    }

    private static Rig Spawned(int spawnFlags, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(MomentaryRotButton).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .ReturnsEachCall("Entities.GetAngles", () => new Vector(0f, rig.Yaw, 0f))
              .WhenCalled("Entities.SetAngles", c => rig.Yaw = c.ArgAs<Vector>(1).Y)
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetSpawnAngles", Vector.Zero)
              .Returns("Entities.IsValid", true);

        registry.KeyValue(Wheel, EntityNames.MomentaryRotButton, "distance", "90");
        registry.KeyValue(Wheel, EntityNames.MomentaryRotButton, "speed", "100");
        foreach (var (key, value) in keys)
            registry.KeyValue(Wheel, EntityNames.MomentaryRotButton, key, value);
        registry.Spawn(Wheel, EntityNames.MomentaryRotButton);
        return rig;
    }

    [Fact]
    public void A_wheel_use_activates_is_used_while_held_and_in_the_radius()
    {
        Assert.Equal(UseCaps.ImpulseUse | UseCaps.ContinuousUse | UseCaps.UseInRadius, Spawned(UseActivates).Entity.ObjectCaps);
        Assert.Equal(UseCaps.None, Spawned(0).Entity.ObjectCaps);
    }

    [Fact]
    public void The_turn_that_starts_a_use_makes_its_noise_and_the_held_turns_do_not()
    {
        var rig = Spawned(UseActivates, ("sounds", "8"), ("StartPosition", "0.5"));
        string[] Noises() => rig.Engine.AllOf("Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToArray();

        rig.Use();
        rig.Use();
        Assert.Equal(["Buttons.snd8"], Noises());

        rig.MoveDone();
        rig.Use();
        Assert.Equal(["Buttons.snd8", "Buttons.snd8"], Noises());
    }

    [Fact]
    public void A_wheel_the_player_does_not_turn_has_no_noise()
    {
        var rig = Spawned(0, ("sounds", "8"));

        rig.Use();

        Assert.False(rig.Engine.Called("Precache.PrecacheSound"));
        Assert.False(rig.Engine.Called("Sound.EntityEmitSound"));
    }

    [Fact]
    public void A_use_presses_it_and_turns_it_at_its_speed_for_a_tenth_of_a_second()
    {
        var rig = Spawned(UseActivates);

        rig.Use();

        Assert.Equal(1, rig.Entity.OnPressed.FireCount);
        Assert.Equal(new Vector(0f, 100f, 0f), rig.Spins.Last());
        Assert.Equal(0.1f, rig.Entity.NextThinkDelay("MoveDone"), 3);
        Assert.False(rig.Engine.Called("BrushMovement.MoveToAngles"), "no traverse to the far end");
    }

    [Fact]
    public void Holding_it_presses_once_and_keeps_rearming_the_move_done()
    {
        var rig = Spawned(UseActivates);
        rig.Use();
        rig.Now += 0.05f;
        rig.Yaw = 5f;

        rig.Use();

        Assert.Equal(1, rig.Entity.OnPressed.FireCount);
        Assert.Equal(0.1f, rig.Entity.NextThinkDelay("MoveDone"), 3);
    }

    [Fact]
    public void Letting_go_stops_it_where_it_is_unpressed()
    {
        var rig = Spawned(UseActivates);
        rig.Use();
        rig.Yaw = 30f;

        rig.MoveDone();

        Assert.Equal(Vector.Zero, rig.Spins.Last());
        Assert.Equal(1, rig.Entity.OnUnpressed.FireCount);
        Assert.Equal("0.33333334", rig.Entity.Position.LastValue);

        // Nothing is left for the timer to do.
        int spins = rig.Spins.Length;
        rig.MoveDone();
        Assert.True(rig.Spins.Length == spins && rig.Entity.OnUnpressed.FireCount == 1);
    }

    [Fact]
    public void The_next_hold_turns_it_back()
    {
        var rig = Spawned(UseActivates);
        rig.Use();
        rig.Yaw = 30f;
        rig.MoveDone();

        rig.Use();

        Assert.Equal(2, rig.Entity.OnPressed.FireCount);
        Assert.Equal(new Vector(0f, -100f, 0f), rig.Spins.Last());
    }

    [Fact]
    public void Held_at_the_far_end_it_stops_snapped_and_says_fully_closed()
    {
        var rig = Spawned(UseActivates);
        rig.Use();
        rig.Yaw = 95f;

        rig.Use();

        Assert.Equal(Vector.Zero, rig.Spins.Last());
        Assert.Equal(90f, rig.Yaw);
        Assert.Equal(1, rig.Entity.OnFullyClosed.FireCount);
    }

    [Fact]
    public void Let_go_with_a_return_speed_it_winds_back_a_tenth_later_until_it_is_home()
    {
        var rig = Spawned(UseActivates, ("returnspeed", "50"));
        rig.Use();
        rig.Yaw = 30f;
        rig.MoveDone();

        Assert.Equal(0.1f, rig.Entity.NextThinkDelay("MoveDone"), 3);
        rig.MoveDone();
        Assert.Equal(new Vector(0f, -50f, 0f), rig.Spins.Last());

        rig.Yaw = -1f;
        rig.MoveDone();
        Assert.Equal(Vector.Zero, rig.Spins.Last());
        Assert.Equal(0f, rig.Yaw);
        Assert.Equal("0", rig.Entity.Position.LastValue);
    }

    [Fact]
    public void A_toggle_wheel_stays_where_it_was_let_go()
    {
        var rig = Spawned(UseActivates | Toggle, ("returnspeed", "50"));
        rig.Use();
        rig.Yaw = 30f;

        rig.MoveDone();
        int spins = rig.Spins.Length;

        rig.MoveDone();

        Assert.Equal(spins, rig.Spins.Length);
    }

    [Fact]
    public void A_locked_wheel_says_so_at_most_every_half_second()
    {
        var rig = Spawned(UseActivates);
        rig.Registry.Input(Wheel, "Lock", "", 0f, EntityHandle.None);

        rig.Use();
        rig.Now += 0.25f;
        rig.Use();
        rig.Now += 0.3f;
        rig.Use();

        Assert.Equal(2, rig.Entity.OnUseLocked.FireCount);
        Assert.Equal(0, rig.Entity.OnPressed.FireCount);
    }

    [Fact]
    public void A_locked_wheel_that_jiggles_turns_two_degrees_and_back_without_telling_its_target()
    {
        var rig = Spawned(UseActivates | Jiggles);
        rig.Registry.Input(Wheel, "Lock", "", 0f, EntityHandle.None);

        rig.Use();
        rig.Entity.OnThink("Jiggle");
        // Arrived two degrees out: the target is not told.
        rig.Registry.MoveDone(Wheel);
        for (int i = 1; i < 4; i++)
            rig.Entity.OnThink("Jiggle");

        var turns = rig.Engine.AllOf("BrushMovement.MoveToAngles").Select(c => c.ArgAs<Vector>(1).Y).ToArray();
        Assert.Equal(new[] { 2f, 0f, 1f, 0f }, turns.Select(y => MathF.Round(y, 3)));
        Assert.Equal(0.2f, rig.Entity.NextThinkDelay("Jiggle"), 3);
        Assert.Equal(0, rig.Entity.Position.FireCount);
    }

    [Fact]
    public void Unlocking_winds_it_back_a_tenth_later()
    {
        var rig = Spawned(UseActivates, ("returnspeed", "50"));
        rig.Registry.Input(Wheel, "Lock", "", 0f, EntityHandle.None);
        rig.Yaw = 45f;

        rig.Registry.Input(Wheel, "Unlock", "", 0f, EntityHandle.None);
        rig.MoveDone();

        Assert.Equal(new Vector(0f, -50f, 0f), rig.Spins.Last());
    }

    [Fact]
    public void SetPositionImmediately_moves_it_and_tells_nobody()
    {
        var rig = Spawned(UseActivates);

        rig.Registry.Input(Wheel, "SetPositionImmediately", "0.5", 0.5f, EntityHandle.None);

        Assert.Equal(45f, rig.Yaw);
        Assert.Equal(0, rig.Entity.Position.FireCount);
    }
}
