using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// trigger_wind against CTriggerWind::WindThink and CPhysicsWind::Simulate
/// (triggers.cpp). Found beside stock (2026-10-27): here the wind snapped its
/// yaw on arrival, held each gust for HoldTime from the arrival, picked float
/// noise, first thought a tenth of a second in, and set the force once a think
/// rather than each step.
/// </summary>
public class TriggerWindTests
{
    private static readonly EntityHandle Wind = new(0x0011_0011u);
    private static readonly EntityHandle Crate = new(0x0013_0013u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 1f;
        public readonly Queue<int> Ints = new();
        public readonly Queue<float> Floats = new();

        public TriggerWind Entity => (TriggerWind)Registry.Get(Wind)!;

        public Vector[] Forces => Engine.AllOf("MotionControllers.SetForce").Select(c => c.ArgAs<Vector>(1)).ToArray();
    }

    private static Rig Spawned(float yaw, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerWind).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .ReturnsEachCall("Host.RandomInt", () => rig.Ints.Count > 0 ? rig.Ints.Dequeue() : 0)
              .ReturnsEachCall("Host.RandomFloat", () => rig.Floats.Count > 0 ? rig.Floats.Dequeue() : 0f)
              .Returns("Entities.GetSpawnAngles", new Vector(0f, yaw, 0f))
              .Returns("Entities.GetSpawnFlags", (int)TriggerSpawnflags.PhysicsObjects)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", 3)
              .Returns("Networking.EntityGetState", EntityState.None)
              .Returns("Networking.EntityGetState.moveType", MoveType.VPhysics)
              .Returns("MotionControllers.Create", 7u);

        foreach (var (key, value) in keys)
            registry.KeyValue(Wind, EntityNames.TriggerWind, key, value);

        registry.Spawn(Wind, EntityNames.TriggerWind);
        return rig;
    }

    //-------------------------------------------------------------------------
    // The angle helpers
    //-------------------------------------------------------------------------

    [Fact]
    public void AngleMod_wraps_into_one_turn_in_65536ths()
    {
        Assert.Equal(10f, TriggerWind.AngleMod(370f), 2);
        Assert.Equal(350f, TriggerWind.AngleMod(-10f), 2);
        // Under one 65536th of a turn: anglemod drops it.
        Assert.Equal(0f, TriggerWind.AngleMod(0.004f));
    }

    [Fact]
    public void ApproachAngle_steps_the_short_way_and_leaves_the_result_unwrapped()
    {
        Assert.Equal(5f, TriggerWind.ApproachAngle(90f, 0f, 5f), 3);
        Assert.Equal(3f, TriggerWind.ApproachAngle(3f, 0f, 5f), 2);
        // 2 towards 350 goes down through zero, and stock does not wrap the step.
        Assert.Equal(-3f, TriggerWind.ApproachAngle(350f, 2f, 5f), 2);
    }

    //-------------------------------------------------------------------------
    // WindThink
    //-------------------------------------------------------------------------

    [Fact]
    public void The_first_think_is_at_spawn_at_the_baseline()
    {
        var rig = Spawned(90f, ("Speed", "200"));

        Assert.Equal(0f, rig.Entity.NextThinkDelay("WindThink"));
        Assert.Equal(200, rig.Entity.CurrentSpeed);
        Assert.Equal(90, rig.Entity.CurrentYaw);
    }

    [Fact]
    public void A_gust_is_picked_with_integer_noise_and_walked_towards_five_a_think()
    {
        var rig = Spawned(90f, ("Speed", "200"), ("SpeedNoise", "50"), ("DirectionNoise", "30"));
        rig.Ints.Enqueue(40);
        rig.Ints.Enqueue(-20);

        Assert.Equal(0.1f, rig.Entity.WindThink());
        Assert.Equal(new[] { -50, 50, -30, 30 }, rig.Engine.AllOf("Host.RandomInt").SelectMany(c => new[] { c.ArgAs<int>(0), c.ArgAs<int>(1) }));

        rig.Entity.WindThink();
        Assert.Equal(205, rig.Entity.CurrentSpeed);
        Assert.Equal(85, rig.Entity.CurrentYaw);
    }

    [Fact]
    public void Arriving_takes_the_speed_but_not_the_direction()
    {
        // Speed 200 to 202 and yaw 90 to 93: both inside a step, so arrived at once.
        var rig = Spawned(90f, ("Speed", "200"), ("SpeedNoise", "5"), ("DirectionNoise", "5"));
        rig.Ints.Enqueue(2);
        rig.Ints.Enqueue(3);
        rig.Entity.WindThink();

        rig.Entity.WindThink();

        Assert.Equal(202, rig.Entity.CurrentSpeed);
        Assert.Equal(90, rig.Entity.CurrentYaw);
    }

    [Fact]
    public void A_gust_is_held_until_HoldTime_after_the_map_started()
    {
        var rig = Spawned(0f, ("Speed", "200"), ("HoldTime", "5"), ("HoldNoise", "1"));
        rig.Entity.WindThink();
        rig.Floats.Enqueue(0.5f);

        // At one second in: held till 5.5.
        Assert.Equal(4.5f, rig.Entity.WindThink(), 3);
        Assert.Equal(new[] { -1f, 1f }, rig.Engine.AllOf("Host.RandomFloat").SelectMany(c => new[] { c.ArgAs<float>(0), c.ArgAs<float>(1) }));
    }

    [Fact]
    public void A_gust_reached_after_HoldTime_is_changed_on_the_next_tick()
    {
        var rig = Spawned(0f, ("Speed", "200"), ("HoldTime", "5"));
        rig.Now = 60f;
        rig.Entity.WindThink();

        Assert.Equal(0f, rig.Entity.WindThink());
    }

    [Fact]
    public void SetSpeed_picks_a_new_gust_about_the_new_baseline()
    {
        var rig = Spawned(0f, ("Speed", "200"));
        rig.Entity.WindThink();

        rig.Registry.Input(Wind, "SetSpeed", "400", 400f, EntityHandle.None);
        rig.Entity.WindThink();
        rig.Entity.WindThink();

        Assert.Equal(205, rig.Entity.CurrentSpeed);
    }

    [Fact]
    public void A_disabled_wind_keeps_thinking_with_no_speed_and_enabling_thinks_a_tenth_on()
    {
        var rig = Spawned(0f, ("Speed", "200"), ("StartDisabled", "1"));

        rig.Entity.WindThink();
        Assert.Equal(0f, rig.Entity.WindSpeed);
        Assert.Equal(200, rig.Entity.CurrentSpeed);

        rig.Registry.Input(Wind, "Enable", "", 0f, EntityHandle.None);
        Assert.Equal(0.1f, rig.Entity.NextThinkDelay("WindThink"), 3);
        rig.Entity.WindThink();
        Assert.Equal(200f, rig.Entity.WindSpeed);
    }

    //-------------------------------------------------------------------------
    // CPhysicsWind
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_tick_sets_stocks_force_from_the_clock()
    {
        var rig = Spawned(0f, ("Speed", "100"));
        rig.Entity.WindThink();
        rig.Registry.Touch(Wind, Crate);
        rig.Engine.Clear();

        rig.Now = 2f;
        rig.Entity.OnThink("WindBlow");
        rig.Now = 2.015f;
        rig.Entity.OnThink("WindBlow");

        // noise 5 + 3 % 15 = 8; the phase is 8 t + 8.
        Vector[] forces = rig.Forces;
        Assert.Equal(2, forces.Length);
        for (int i = 0; i < 2; i++)
        {
            float t = i == 0 ? 2f : 2.015f;
            float phase = 8f * t + 8f;
            float yaw = 30f * MathF.Cos(phase) * MathF.PI / 180f;
            Assert.Equal(300f * MathF.Cos(yaw), forces[i].X, 2);
            Assert.Equal(300f * MathF.Sin(yaw), forces[i].Y, 2);
            Assert.Equal(300f * (1.1f + MathF.Sin(phase)), forces[i].Z, 2);
        }
        Assert.Equal(0f, rig.Entity.NextThinkDelay("WindBlow"));
    }

    [Fact]
    public void A_still_wind_zeroes_the_force_once()
    {
        var rig = Spawned(0f, ("Speed", "100"));
        rig.Entity.WindThink();
        rig.Registry.Touch(Wind, Crate);
        rig.Registry.Input(Wind, "Disable", "", 0f, EntityHandle.None);
        rig.Entity.WindThink();
        rig.Engine.Clear();

        rig.Entity.OnThink("WindBlow");
        rig.Entity.OnThink("WindBlow");

        Assert.Equal([Vector.Zero], rig.Forces);
    }
}
