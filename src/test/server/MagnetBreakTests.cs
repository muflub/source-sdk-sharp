using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A magnet joint the SOLVER broke releases the object it held.
///
/// Stock: CPhysMagnet::ConstraintBroken (physobj.cpp:1849-1872) finds the
/// magnetted entity whose constraint is the broken one, removes it from
/// m_MagnettedEntities, fires m_OnMagnetDetach and destroys the constraint
/// (physenv->DestroyConstraint). Without the override the entry stays in
/// _held forever: Holds() stays true so the object is never re-caught, every
/// break permanently consumes one of maxobjects, and OnDetach never fires.
/// </summary>
public class MagnetBreakTests
{
    private static readonly EntityHandle Magnet = new(0x0001_0050);
    private static readonly EntityHandle Crate = new(0x0001_0051);
    private static readonly EntityHandle Barrel = new(0x0001_0052);

    private const uint FirstJoint = 0x0001_0010u;

    private static (ScriptedEntityRegistry Registry, PhysMagnet Entity, RecordingEngine Engine)
        Caught(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysMagnet).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        uint nextJoint = FirstJoint;
        engine.ReturnsEachCall("Constraints.Create", () => nextJoint++);
        engine.Returns("Constraints.IsValid", true);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Networking.EntityGetState", EntityState.Physics);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);
        engine.Returns("EntityPhysics.GetMass", 10f);

        foreach (var (key, value) in keys)
            registry.KeyValue(Magnet, EntityNames.PhysMagnet, key, value);
        registry.Spawn(Magnet, EntityNames.PhysMagnet);

        var magnet = (PhysMagnet)registry.Get(Magnet)!;

        // Catch the crate the way the solver reports it: a metal impact.
        magnet.OnPhysicsCollision(Crate, new Vector(1f, 2f, 3f), 250f,
                                  SurfaceMaterial.Metal, SurfaceMaterial.Metal);

        Assert.True(engine.Called("Constraints.Create"), "the control: the crate was caught");
        Assert.Equal(1, magnet.HeldCount);
        return (registry, magnet, engine);
    }

    [Fact]
    public void The_solver_breaking_a_held_joint_releases_the_object()
    {
        var (registry, magnet, engine) = Caught();

        Assert.True(registry.ConstraintBroken(new PhysicsId(FirstJoint)),
                    "the magnet owns this joint and says so");

        Assert.True(magnet.HeldCount == 0, "the broken entry is gone, not held forever");
        Assert.True(magnet.OnDetach.FireCount == 1, "stock fires m_OnMagnetDetach on a break");
        Assert.True(engine.Called("Constraints.Destroy"), "and destroys the dead constraint");
    }

    [Fact]
    public void A_released_object_can_be_caught_again()
    {
        // The maxobjects bricking: without removal Holds() stays true and the
        // object is never re-caught, while the stale entry still counts against
        // the limit.
        var (registry, magnet, _) = Caught(("maxobjects", "1"));

        registry.ConstraintBroken(new PhysicsId(FirstJoint));
        Assert.Equal(0, magnet.HeldCount);

        magnet.OnPhysicsCollision(Crate, new Vector(1f, 2f, 3f), 250f,
                                  SurfaceMaterial.Metal, SurfaceMaterial.Metal);

        Assert.True(magnet.HeldCount == 1, "the freed crate is caught a second time");
        Assert.True(magnet.OnAttach.FireCount == 2, "and the attach fires again");
    }

    [Fact]
    public void Another_magnets_broken_joint_is_not_this_magnets()
    {
        var (registry, magnet, _) = Caught();

        Assert.False(registry.ConstraintBroken(new PhysicsId(FirstJoint + 0x20)));

        Assert.True(magnet.HeldCount == 1, "the held crate stays held");
        Assert.True(magnet.OnDetach.FireCount == 0, "and nothing detaches");
    }
}
