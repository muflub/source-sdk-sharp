using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the magnet holds, is, and last sucked — across a hot reload.
///
/// Stock saves all three: m_MagnettedEntities is FIELD_EMBEDDED (the held
/// entities and their constraint pointers), m_bActive and m_flNextSuckTime
/// are saved fields, and OnRestore re-wires the constraint pointers
/// (physobj.cpp:1494-1512). The native joints survive a cs_sv_reload; before
/// this the managed side forgot them, so TurnOff's DetachAll destroyed
/// nothing and everything the magnet held stayed welded forever.
/// </summary>
public class MagnetReloadTests
{
    private static readonly EntityHandle Magnet = new(0x0001_0070);
    private static readonly EntityHandle Crate = new(0x0001_0071);
    private static readonly EntityHandle Barrel = new(0x0001_0072);

    private readonly ReloadStore _reload = new(Abi.Side.Server);

    private const uint FirstJoint = 0x0001_0020u;

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine)
        Side(float now = 0f, uint firstJoint = FirstJoint, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysMagnet).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Constraints.IsValid", true);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Networking.EntityGetState", EntityState.Physics);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);
        engine.Returns("EntityPhysics.GetMass", 10f);
        uint next = firstJoint;
        engine.ReturnsEachCall("Constraints.Create", () => next++);
        return (registry, engine);
    }

    private static void Catch(PhysMagnet magnet, EntityHandle target)
        => magnet.OnPhysicsCollision(target, new Vector(1f, 2f, 3f), 250f,
                                     SurfaceMaterial.Metal, SurfaceMaterial.Metal);

    [Fact]
    public void What_the_magnet_holds_survives_the_reload()
    {
        var (before, engine) = Side();
        before.Spawn(Magnet, EntityNames.PhysMagnet);
        var magnet = (PhysMagnet)before.Get(Magnet)!;

        Catch(magnet, Crate);
        Assert.Equal(1, magnet.HeldCount);
        Assert.True(engine.Called("Constraints.Create"), "the control: a real joint was made");

        _reload.Clear();
        Assert.Equal(1, before.SaveForReload(_reload));

        var (after, back) = Side();
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        var magnet2 = (PhysMagnet)after.Get(Magnet)!;

        Assert.True(magnet2.HeldCount == 1, "the rebuilt magnet knows what it is holding");

        // And the carried wrapper is the live one: turning off destroys the
        // joint the solver still holds, which is the whole purpose.
        after.Input(Magnet, "TurnOff", "", 0f, EntityHandle.None);

        Assert.True(back.Calls.Any(c => c.Member == "Constraints.Destroy"
                                        && c.ArgAs<uint>(0) == FirstJoint),
                    "DetachAll destroyed the held native joint, not an empty list");
        Assert.True(magnet2.OnDetach.FireCount == 1, "and said so");
    }

    [Fact]
    public void A_magnet_turned_off_stays_off_across_the_reload()
    {
        var (before, _) = Side();
        before.Spawn(Magnet, EntityNames.PhysMagnet);
        Catch((PhysMagnet)before.Get(Magnet)!, Crate);

        before.Input(Magnet, "TurnOff", "", 0f, EntityHandle.None);

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = Side();
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        var magnet2 = (PhysMagnet)after.Get(Magnet)!;
        Assert.True(magnet2.HeldCount == 0, "turning off detached them before the reload");

        // The amnesia shape: an inactive magnet that comes back active catches
        // again — which stock's saved m_bActive forbids.
        Catch(magnet2, Barrel);
        Assert.True(magnet2.HeldCount == 0, "an off magnet is still off after the reload");
        Assert.False(back.Called("Constraints.Create"));
    }

    [Fact]
    public void The_suck_clock_survives_the_reload()
    {
        var (before, engine) = Side(now: 0f, spawnFlags: 4); // SF_MAGNET_SUCK
        before.Spawn(Magnet, EntityNames.PhysMagnet);
        before.Think(Magnet); // consume the spawn think (mass/motion) at t=0

        Catch((PhysMagnet)before.Get(Magnet)!, Crate); // sucks at t=0: next allowed at t=2
        Assert.True(engine.Called("WorldQuery.EntitiesInSphere"),
                    "the control: it really sucked on the first catch");

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = Side(now: 1f, spawnFlags: 4); // one second later, inside the 2 s window
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        var magnet2 = (PhysMagnet)after.Get(Magnet)!;

        Catch(magnet2, Barrel);

        Assert.True(!back.Called("WorldQuery.EntitiesInSphere"),
                    "the saved clock still says the next suck is at t=2");
    }
}
