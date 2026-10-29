using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// phys_hinge against CPhysHinge (physconstraint.cpp). Found beside stock
/// (2026-10-27): SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY was never read, so a
/// hinge to the world left its object free to be picked up and pushed off
/// its axis.
/// </summary>
public class PhysHingeTests
{
    private static readonly EntityHandle Hinge = new(0x0001_00B0);
    private static readonly EntityHandle Door = new(0x0001_00B1);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Built(int spawnFlags,
        params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysHinge).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new MassCenterOverrides());
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Constraints.Create", 0x0001_0030u)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
              .ReturnsEachCall("Entities.FindByName", () => (object?)Door);

        registry.KeyValue(Hinge, EntityNames.PhysHinge, "attach1", "door");
        registry.KeyValue(Hinge, EntityNames.PhysHinge, "hingeaxis", "0 0 64");
        foreach (var (key, value) in keys)
            registry.KeyValue(Hinge, EntityNames.PhysHinge, key, value);
        registry.Spawn(Hinge, EntityNames.PhysHinge);
        registry.Think(Hinge);
        return (registry, engine);
    }

    private static float[] Parameters(RecordingEngine engine)
        => (float[])engine.AllOf("Constraints.Create").Single().Args[3]!;

    [Fact]
    public void Assume_world_geometry_asks_for_a_world_hinge()
        => Assert.Equal(1f, Parameters(Built(spawnFlags: 8).Engine)[9]);

    [Fact]
    public void A_hinge_with_both_ends_named_is_never_a_world_hinge()
        => Assert.Equal(0f, Parameters(Built(spawnFlags: 8, ("attach2", "frame")).Engine)[9]);

    [Fact]
    public void The_hinge_is_made_where_it_stood_and_pointed_at_spawn()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysHinge).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new MassCenterOverrides());
        registry.Services = services;
        engine.Strict = false;
        var at = new Vector(10f, 0f, 0f);
        engine.Returns("Constraints.Create", 0x0001_0030u)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetSpawnFlags", 0)
              .ReturnsEachCall("Entities.GetOrigin", () => at)
              .ReturnsEachCall("Entities.FindByName", () => (object?)Door);

        registry.KeyValue(Hinge, EntityNames.PhysHinge, "attach1", "door");
        registry.KeyValue(Hinge, EntityNames.PhysHinge, "hingeaxis", "10 0 64");
        registry.Spawn(Hinge, EntityNames.PhysHinge);
        at = new Vector(50f, 0f, 0f);
        registry.Think(Hinge);

        float[] p = Parameters(engine);
        Assert.Equal((10f, 0f, 0f), (p[2], p[3], p[4]));
        Assert.Equal((0f, 0f, 1f), (p[5], p[6], p[7]));
    }

    [Fact]
    public void Without_it_the_hinge_is_only_a_joint()
        => Assert.Equal(0f, Parameters(Built(spawnFlags: 0).Engine)[9]);

    [Fact]
    public void SetAngularVelocity_drives_it_with_the_system_load_scale()
    {
        var (registry, engine) = Built(spawnFlags: 0, ("systemloadscale", "2.5"));

        registry.Input(Hinge, "SetAngularVelocity", "30", 30f, EntityHandle.None);

        var call = engine.AllOf("Constraints.SetHingeVelocity").Single();
        Assert.True(call.ArgAs<float>(1) == 30f && call.ArgAs<float>(2) == 2.5f);
    }

    [Fact]
    public void SetHingeFriction_is_the_friction_the_next_joint_is_made_with()
    {
        // SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED: the joint is made by TurnOn, after the input.
        var (registry, engine) = Built(spawnFlags: 0x10);
        Assert.False(engine.Called("Constraints.Create"), "the control: nothing made before TurnOn");

        registry.Input(Hinge, "SetHingeFriction", "3", 3f, EntityHandle.None);
        registry.Input(Hinge, "TurnOn", "", 0f, EntityHandle.None);

        Assert.Equal(3f, ((float[])engine.AllOf("Constraints.Create").Last().Args[3]!)[8]);
    }
}
