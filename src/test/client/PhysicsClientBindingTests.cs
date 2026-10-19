using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Physics;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedPhysicsClient (plan_physics_bones_native_zero 2.6): physics_shared.cpp's
/// client half as fork game/client/physics.cpp's forwarders ask it, built by
/// the real registry and called the way native calls it; and the client's
/// precache handle.
/// </summary>
public unsafe class PhysicsClientBindingTests
{
    private static void* Slot(ManagedVtable table, int slot) => (void*)(*(nint**)table.Pointer)[slot];

    private sealed class World : IClientPhysicsWorld
    {
        public readonly List<string> Asked = [];
        public nint CreateWorld() { Asked.Add("world"); return 0x4321; }
        public void CreateStaticProps() => Asked.Add("props");
        public int ParseSurfaceData() { Asked.Add("manifest"); return 2; }
        public int PrecacheSurfaceSounds() { Asked.Add("sounds"); return 30; }
    }

    [Fact]
    public void TheManifestSlotReachesTheWorldAndCountsOneCrossing()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        var crossings = new Crossings();
        var world = new World();
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(crossings, new RecordingLog(), () => world, () => null));

        // Release is slot 0, Validate 1, then ParseSurfaceData: the one world
        // ask native still makes (PhysicsDLLInit). The world, the static props
        // and the precache are ClientPhysicsSystem's own asks now.
        Assert.Equal(2, ((delegate* unmanaged<void*, int>)Slot(table, 2))(table.Pointer));
        Assert.Equal(["manifest"], world.Asked);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientPhysicsParseSurfaceData));
    }

    [Fact]
    public void WithNoWorldComposedTheManifestAnswersMinusOne()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(new Crossings(), new RecordingLog(), () => null, () => null));

        Assert.Equal(-1, ((delegate* unmanaged<void*, int>)Slot(table, 2))(table.Pointer));
    }

    private sealed class Frame : IClientPhysicsFrame
    {
        public readonly List<string> Asked = [];
        public bool Throw;
        public void LevelInitPreEntity() => Asked.Add("init pre");
        public void LevelInitPostEntity() => Asked.Add("init post");
        public void LevelShutdownPreEntity() => Asked.Add("shutdown pre");
        public void LevelShutdownPostEntity() => Asked.Add("shutdown post");
        public void Simulate() { Asked.Add("simulate"); if (Throw) throw new InvalidOperationException("boom"); }
        public void Reset() => Asked.Add("reset");
    }

    [Fact]
    public void TheSystemsSlotsReachTheFrameInOrderEachCountedOnce()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        var crossings = new Crossings();
        var frame = new Frame();
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(crossings, new RecordingLog(), () => null, () => frame));

        // After the manifest row (slot 2): the six system rows.
        for (int slot = 3; slot <= 8; slot++)
            ((delegate* unmanaged<void*, void>)Slot(table, slot))(table.Pointer);

        Assert.Equal(["init pre", "init post", "shutdown pre", "shutdown post", "simulate", "reset"], frame.Asked);
        foreach (CrossingOp op in new[] { CrossingOp.ClientPhysicsLevelInitPreEntity, CrossingOp.ClientPhysicsLevelInitPostEntity,
                                          CrossingOp.ClientPhysicsLevelShutdownPreEntity, CrossingOp.ClientPhysicsLevelShutdownPostEntity,
                                          CrossingOp.ClientPhysicsSimulate, CrossingOp.ClientPhysicsReset })
            Assert.Equal(1, crossings.Read(op));
    }

    [Fact]
    public void AThrowingStepIsLoggedAndDoesNotLeaveManaged()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        var frame = new Frame { Throw = true };
        var log = new RecordingLog();
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(new Crossings(), log, () => null, () => frame));

        ((delegate* unmanaged<void*, void>)Slot(table, 7))(table.Pointer);
        ((delegate* unmanaged<void*, void>)Slot(table, 8))(table.Pointer);

        Assert.Equal(["simulate", "reset"], frame.Asked);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("physics step threw: boom", StringComparison.Ordinal));
    }

    [Fact]
    public void TheClientsPrecacheIsTheScriptsIndexAndPrecachesNothing()
    {
        Func<string, short> precache = ClientPhysicsWorld.ClientPrecache(name => name == "Wood.ImpactSoft" ? 77 : -1);

        Assert.Equal((short)77, precache("Wood.ImpactSoft"));
        Assert.Equal((short)-1, precache("Nothing.Here"));
        Assert.Equal((short)-1, ClientPhysicsWorld.ClientPrecache(null)("Wood.ImpactSoft"));
    }
}
