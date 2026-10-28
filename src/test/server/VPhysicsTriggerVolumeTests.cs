using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The two CBaseVPhysicsTrigger classes (triggers.cpp:4334): SOLID_VPHYSICS and
/// FSOLID_NOT_SOLID, a static object made a trigger, collisions switched with
/// the volume. Found beside stock (2026-10-27, census): the wind was an engine
/// touch trigger (SOLID_BSP, NOT_SOLID|TRIGGER, no object), which never hears
/// of a crate, and trigger_vphysics_motion kept SOLID_BSP.
/// </summary>
public class VPhysicsTriggerVolumeTests
{
    private static readonly EntityHandle Volume = new(0x0011_0011u);
    private static readonly EntityHandle Crate = new(0x0013_0013u);

    private static (ScriptedEntityRegistry, RecordingEngine) Spawned(string className, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(VPhysicsTriggerVolume).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.IsValid", true)
              .Returns("Networking.EntityGetState", EntityState.None)
              .Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);

        registry.Spawn(Volume, className);
        return (registry, engine);
    }

    [Theory]
    [InlineData(EntityNames.TriggerWind)]
    [InlineData(EntityNames.TriggerVphysicsMotion)]
    public void A_vphysics_trigger_is_a_passable_vphysics_solid_with_a_static_trigger_object(string className)
    {
        var (_, engine) = Spawned(className);

        var solidity = engine.AllOf("Entities.SetSolidity").Last();
        Assert.Equal(SolidType.VPhysics, solidity.ArgAs<SolidType>(1));
        Assert.Equal(SolidFlags.NotSolid, solidity.ArgAs<SolidFlags>(2));
        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.SetPhysicsStatic" && c.ArgAs<bool>(1));
        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.BecomePhysicsTrigger" && c.ArgAs<bool>(1));
        Assert.False(engine.Called("EntityPhysics.InitShadowPhysics"));
    }

    [Theory]
    [InlineData(EntityNames.TriggerWind)]
    [InlineData(EntityNames.TriggerVphysicsMotion)]
    public void A_moveable_one_is_a_shadow(string className)
    {
        var (_, engine) = Spawned(className, VPhysicsTriggerVolume.SpawnFlagMoveable);

        Assert.True(engine.Called("EntityPhysics.InitShadowPhysics"));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityPhysics.SetPhysicsStatic");
    }

    [Theory]
    [InlineData(EntityNames.TriggerWind)]
    [InlineData(EntityNames.TriggerVphysicsMotion)]
    public void Disabling_and_enabling_switch_the_objects_collisions(string className)
    {
        var (registry, engine) = Spawned(className);

        registry.Input(Volume, "Disable", "", 0f, EntityHandle.None);
        registry.Input(Volume, "Enable", "", 0f, EntityHandle.None);

        Assert.Equal(new[] { false, true },
                     engine.AllOf("EntityPhysics.EnableCollisions").Select(c => c.ArgAs<bool>(1)));
    }

    [Fact]
    public void The_wind_never_takes_a_player()
    {
        var (registry, engine) = Spawned(EntityNames.TriggerWind);
        engine.Returns("Networking.EntityGetState", EntityState.Player)
              .Returns("MotionControllers.Create", 7u);

        registry.Touch(Volume, Crate);

        Assert.False(engine.Called("MotionControllers.Create"));
    }

    [Fact]
    public void The_wind_takes_a_physics_object()
    {
        var (registry, engine) = Spawned(EntityNames.TriggerWind);
        engine.Returns("MotionControllers.Create", 7u);

        registry.Touch(Volume, Crate);

        Assert.True(engine.Called("MotionControllers.Create"));
    }
}
