using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// prop_sphere is stock's CPhysSphere (props.cpp): a twelve-unit sphere
/// object in a SOLID_BBOX of twelve a side, not the model's hull. Found beside
/// stock on ss_sandbox (2026-10-26): the ball rested 10.4 units below stock's.
/// </summary>
public class PropSphereTests
{
    private static readonly EntityHandle Ball = new(0x0002_0060);

    [Fact]
    public void A_prop_sphere_is_made_a_twelve_unit_sphere_in_a_box_of_twelve()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropSphere).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitSpherePhysics", true);

        registry.KeyValue(Ball, EntityNames.PropSphere, "model", "models/props_junk/watermelon01.mdl");
        registry.Spawn(Ball, EntityNames.PropSphere);

        var sphere = Assert.Single(engine.AllOf("EntityPhysics.InitSpherePhysics"));
        Assert.Equal(PropSphere.Radius, sphere.ArgAs<float>(1));
        var bounds = Assert.Single(engine.AllOf("Entities.SetBounds"));
        Assert.Equal((new Vector(-12f, -12f, -12f), new Vector(12f, 12f, 12f)),
                     (bounds.ArgAs<Vector>(1), bounds.ArgAs<Vector>(2)));
        Assert.False(engine.Called("EntityPhysics.InitPhysics"), "the model's hull is not the ball");
    }
}
