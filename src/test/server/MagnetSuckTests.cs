using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// When a magnet sucks, and what it will weld.
///
/// Stock CPhysMagnet::VPhysicsCollision calls DoMagnetSuck straight after the
/// trigger check, BEFORE the active / max-objects / material gates
/// (physobj.cpp:1641-1644): a suck magnet resting on concrete still drags loose
/// metal up. And it welds only an object the solver moves
/// (pPhysics->IsMoveable(), physobj.cpp:1705).
/// </summary>
public class MagnetSuckTests
{
    private static readonly EntityHandle Magnet = new(0x0001_00C0);
    private static readonly EntityHandle Slab = new(0x0001_00C1);

    private const int SuckFlag = 0b0100;

    private static (PhysMagnet Magnet, RecordingEngine Engine) Spawn(int spawnFlags)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysMagnet).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Clock.Now", 10f);
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Networking.EntityGetState", EntityState.Physics);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);
        engine.Returns("Constraints.Create", 0x0001_0040u);
        registry.Spawn(Magnet, EntityNames.PhysMagnet);
        return ((PhysMagnet)registry.Get(Magnet)!, engine);
    }

    [Fact]
    public void A_suck_magnet_landing_on_something_it_cannot_hold_still_sucks()
    {
        var (magnet, engine) = Spawn(SuckFlag);

        // Wood: the material gate refuses it, and stock has already sucked.
        magnet.OnPhysicsCollision(Slab, new Vector(0f, 0f, 0f), 200f,
                                  SurfaceMaterial.Wood, SurfaceMaterial.Wood);

        Assert.True(engine.Called("WorldQuery.EntitiesInSphere"), "the suck ran ahead of the material gate");
        Assert.False(engine.Called("Constraints.Create"), "and nothing was welded");
    }

    [Fact]
    public void A_magnet_does_not_weld_what_the_solver_will_not_move()
    {
        var (magnet, engine) = Spawn(0);

        // Metal and VPhysics, but the record says the solver will not move it
        // (a motion-disabled prop): stock's IsMoveable gate refuses the weld.
        magnet.OtherMoveable = false;
        magnet.OnPhysicsCollision(Slab, new Vector(0f, 0f, 0f), 200f,
                                  SurfaceMaterial.Metal, SurfaceMaterial.Metal);
        Assert.False(engine.Called("Constraints.Create"), "nothing pinned is welded");

        // The control: the same contact on something moveable welds.
        magnet.OtherMoveable = true;
        magnet.OnPhysicsCollision(Slab, new Vector(0f, 0f, 0f), 200f,
                                  SurfaceMaterial.Metal, SurfaceMaterial.Metal);
        Assert.True(engine.Called("Constraints.Create"), "a moveable one is");
    }

    [Fact]
    public void The_record_handler_leaves_the_moveable_flag_reset()
    {
        var (magnet, _) = Spawn(0);
        var ev = new PhysicsEvent { Entity0 = 0x0001_00C0, Entity1 = 0x0001_00C1, Flags = PhysicsEvent.FlagMoveable0 };

        magnet.OnVPhysicsCollision(in ev, 0);

        Assert.True(magnet.OtherMoveable, "the flag lives only for one record");
    }
}
