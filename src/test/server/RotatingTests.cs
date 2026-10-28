using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_rotating against stock's CFuncRotating (bmodels.cpp): its axis flags,
/// the spin-up and spin-down of SF_BRUSH_ACCDCC, SetSpeed's fraction, the
/// start-on delay, the running noise's ramp, the hurt touch, the block crush
/// and StopAtStartPos. Found in the contract diff (2026-10-27): ours read flag 4
/// as pitch and 8 as roll, where stock's are roll (Z) and pitch (X), and it
/// took none of fanfriction, Volume, message or dmg.
/// </summary>
public class RotatingTests
{
    private static readonly EntityHandle Fan = new(0x0009_0101u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const int StartOn = 1, Backwards = 2, AxisZ = 4, AxisX = 8, Accelerates = 16, Hurts = 32, NotSolid = 64;

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public readonly ThinkScheduler Thinks = new();
        public Vector Angles = Vector.Zero;

        public FuncRotating Entity => (FuncRotating)Registry.Get(Fan)!;

        public void Input(string name, float number = 0f)
            => Registry.Input(Fan, name, number.ToString(System.Globalization.CultureInfo.InvariantCulture), number, EntityHandle.None);

        public void Move() => Entity.OnThink("RotatingMove");

        public Vector Spin => Engine.Last("BrushMovement.Spin")!.Value.ArgAs<Vector>(1);

        public (float Volume, float Pitch, int Flags)[] Emits
            => Engine.AllOf("Sound.SoundPatchEmit").Select(c => (c.ArgAs<float>(1), c.ArgAs<float>(2), c.ArgAs<int>(3))).ToArray();
    }

    private static Rig Spawned(int spawnFlags = 0, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncRotating).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetSpawnAngles", Vector.Zero)
              .ReturnsEachCall("Entities.GetAngles", () => rig.Angles)
              .Returns("Entities.GetWorldSpaceCenter", Vector.Zero)
              .Returns("Entities.GetOrigin", new Vector(3f, 4f, 0f))
              .Returns("Sound.SoundPatchCreate", 7);

        registry.KeyValue(Fan, EntityNames.FuncRotating, "origin", "0 0 0");
        foreach (var (key, value) in keys)
            registry.KeyValue(Fan, EntityNames.FuncRotating, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Fan, EntityNames.FuncRotating);
        return rig;
    }

    /// <summary>SF_BRUSH_ROTATE_Z_AXIS (4) is roll and X_AXIS (8) pitch, Z checked first; neither is yaw.</summary>
    [Theory]
    [InlineData(0, 0f, 100f, 0f)]
    [InlineData(AxisZ, 0f, 0f, 100f)]
    [InlineData(AxisX, 100f, 0f, 0f)]
    [InlineData(AxisZ | AxisX, 0f, 0f, 100f)]
    [InlineData(Backwards, 0f, -100f, 0f)]
    public void Its_axis_is_stocks(int flags, float pitch, float yaw, float roll)
    {
        var rig = Spawned(flags);

        rig.Input("Start");

        Assert.Equal(new Vector(pitch, yaw, roll), rig.Spin);
    }

    [Fact]
    public void Start_on_starts_it_a_fifth_of_a_second_in()
    {
        var rig = Spawned(StartOn);
        Assert.True(rig.Thinks.IsScheduled(Fan, "RotatingStart"));
        Assert.False(rig.Engine.Called("BrushMovement.Spin"));

        rig.Entity.OnThink("RotatingStart");

        Assert.Equal(new Vector(0f, 100f, 0f), rig.Spin);
    }

    /// <summary>SpinUpMove: a fifth of its top speed by its friction each tenth, until the target.</summary>
    [Fact]
    public void An_accelerating_fan_spins_up_by_its_friction()
    {
        var rig = Spawned(Accelerates, ("fanfriction", "50"));

        rig.Input("Start");
        Assert.False(rig.Engine.Called("BrushMovement.Spin"));

        rig.Move();
        Assert.Equal(10f, rig.Spin.Y, 3);
        rig.Move();
        Assert.Equal(20f, rig.Spin.Y, 3);
        for (int i = 0; i < 20; i++)
            rig.Move();
        Assert.Equal(100f, rig.Spin.Y, 3);
    }

    /// <summary>SpinDownMove: half the spin-up's step, down to rest.</summary>
    [Fact]
    public void An_accelerating_fan_spins_down_at_half_the_rate()
    {
        var rig = Spawned(Accelerates);
        rig.Input("Start");
        for (int i = 0; i < 10; i++)
            rig.Move();

        rig.Input("Stop");
        rig.Move();

        Assert.Equal(90f, rig.Spin.Y, 3);
        for (int i = 0; i < 20; i++)
            rig.Move();
        Assert.Equal(0f, rig.Spin.Y, 3);
    }

    [Theory]
    [InlineData(0.5f, 50f)]
    [InlineData(-0.25f, -25f)]
    [InlineData(3f, 100f)]
    public void SetSpeed_is_a_fraction_of_its_top_speed_its_sign_the_direction(float value, float yaw)
    {
        var rig = Spawned();

        rig.Input("SetSpeed", value);

        Assert.Equal(yaw, rig.Spin.Y, 3);
    }

    [Fact]
    public void Toggle_stops_a_running_fan_and_Use_starts_a_stopped_one()
    {
        var rig = Spawned(0, ("maxspeed", "-40"));

        rig.Input("Use");
        Assert.Equal(40f, rig.Spin.Y, 3);

        rig.Input("Toggle");
        Assert.Equal(0f, rig.Spin.Y, 3);
    }

    /// <summary>The noise starts at 0.01 and pitch 30, then ramps to the volume key's tenth and pitch 99 at top speed.</summary>
    [Fact]
    public void Its_noise_starts_quiet_and_ramps_with_the_speed()
    {
        var rig = Spawned(0, ("message", "Fan.Hum"), ("Volume", "5"));

        rig.Input("Start");

        var create = rig.Engine.Last("Sound.SoundPatchCreate")!.Value;
        Assert.Equal(("Fan.Hum", 6, 75), (create.ArgAs<string>(2), create.ArgAs<int>(1), create.ArgAs<int>(3)));
        Assert.Equal(new[] { (0.01f, 30f, 0), (0.5f, 99f, 3) }, rig.Emits);

        rig.Input("Stop");
        Assert.True(rig.Engine.Called("Sound.SoundPatchStop"));
    }

    [Theory]
    [InlineData(128, 60)]
    [InlineData(256, 66)]
    [InlineData(512, 75)]
    public void Its_radius_flags_are_its_sound_level(int flags, int level)
    {
        var rig = Spawned(flags);

        rig.Input("Start");

        Assert.Equal(level, rig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<int>(3));
    }

    /// <summary>HurtTouch: a tenth of its turn rate as a crush, and a throw as hard from its middle.</summary>
    [Fact]
    public void A_hurting_fan_crushes_and_throws_what_touches_it()
    {
        var rig = Spawned(Hurts);
        rig.Input("Start");

        rig.Entity.OnTouch(Player);

        var hurt = rig.Engine.Last("Damage.Apply")!.Value;
        Assert.Equal((Player, Fan, 10f, DamageType.Crush),
                     (hurt.ArgAs<EntityHandle>(0), hurt.ArgAs<EntityHandle>(1), hurt.ArgAs<float>(2), hurt.ArgAs<DamageType>(3)));
        Assert.Equal(new Vector(6f, 8f, 0f), rig.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1));
    }

    [Fact]
    public void A_plain_fan_hurts_nothing_by_touch()
    {
        var rig = Spawned();
        rig.Input("Start");

        rig.Entity.OnTouch(Player);

        Assert.False(rig.Engine.Called("Damage.Apply"));
        Assert.False(rig.Entity.TouchEveryTick);
    }

    [Fact]
    public void What_blocks_it_takes_its_dmg_as_a_crush()
    {
        var rig = Spawned(0, ("dmg", "15"));

        rig.Entity.OnBlocked(Player);

        var hurt = rig.Engine.Last("Damage.Apply")!.Value;
        Assert.Equal((15f, DamageType.Crush), (hurt.ArgAs<float>(2), hurt.ArgAs<DamageType>(3)));
    }

    [Fact]
    public void A_not_solid_fan_has_no_shadow()
    {
        Assert.False(Spawned(NotSolid).Engine.Called("EntityPhysics.InitShadowPhysics"));
        Assert.True(Spawned().Engine.Called("EntityPhysics.InitShadowPhysics"));
    }

    /// <summary>StopAtStartPos: once it is within a tick's turn of its spawn angles it rests there.</summary>
    [Fact]
    public void StopAtStartPos_brings_it_to_rest_at_its_spawn_angles()
    {
        var rig = Spawned();
        rig.Input("Start");
        rig.Angles = new Vector(0f, 359.5f, 0f);

        rig.Input("StopAtStartPos");

        Assert.Equal(Vector.Zero, rig.Engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1));
        Assert.Equal(0f, rig.Spin.Y, 3);
    }
}
