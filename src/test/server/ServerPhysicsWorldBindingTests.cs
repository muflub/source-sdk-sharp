using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's rows for physics_shared.cpp's half (plan_physics_bones_native_zero
/// 2.6): IManagedPhysicsFrame's three new slots, built by the real registry and
/// called the way native calls them; the server's solid key table; and the
/// handle the server's precache answers.
/// </summary>
public unsafe class ServerPhysicsWorldBindingTests
{
    private static void* Slot(ManagedVtable table, int slot) => (void*)(*(nint**)table.Pointer)[slot];

    // The key handler's slots by name: its table leads with Release and Validate
    // (plan_forwarders_everywhere.md phase 5), so its order is the struct's.
    private static int KeySlot(string field)
        => (int)System.Runtime.InteropServices.Marshal.OffsetOf(typeof(ManagedVPhysicsKeyHandlerTable), field) / sizeof(nint);

    private sealed class World : IServerPhysicsWorld
    {
        public readonly List<string> Asked = [];
        public nint CreateWorld() { Asked.Add("world"); return 0x1234; }
        public int PrecacheSurfaceSounds() { Asked.Add("sounds"); return 17; }
    }

    [Fact]
    public void TheFrameAreasNewSlotsReachTheWorldAndCountOneCrossingEach()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var world = new World();
        ManagedVtable table = IManagedPhysicsFrame__ManagedVtable.Create(vtables, new PhysicsFrameBinding(crossings, () => world));

        // Release is slot 0, Validate 1, then the two rows in order (Frame,
        // slot 2, left with kServerSystems[] in plan_server_native_v2 T4e;
        // ParseModelSolid with native's InitPhysics).
        void* made = ((delegate* unmanaged<void*, void*>)Slot(table, 2))(table.Pointer);
        int sounds = ((delegate* unmanaged<void*, int>)Slot(table, 3))(table.Pointer);

        Assert.Equal((nint)0x1234, (nint)made);
        Assert.Equal(17, sounds);
        Assert.Equal(["world", "sounds"], world.Asked);
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsCreateWorld));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsPrecacheSurfaceSounds));
    }

    [Fact]
    public void WithNoWorldBuiltTheSlotsAnswerNothing()
    {
        using var vtables = new ManagedVtables();
        ManagedVtable table = IManagedPhysicsFrame__ManagedVtable.Create(vtables, new PhysicsFrameBinding(new Crossings()));

        Assert.Equal((nint)0, (nint)((delegate* unmanaged<void*, void*>)Slot(table, 2))(table.Pointer));
        Assert.Equal(-1, ((delegate* unmanaged<void*, int>)Slot(table, 3))(table.Pointer));
    }

    [Fact]
    public void TheServersSolidTableIsAlwaysTheSolidHandler()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var keys = new SolidKeyHandlerBinding(crossings, new SolidKeyDefaults(0x5150));
        using var callbacks = new FakePhysicsCallbacks();
        ManagedVtable table = SolidKeyHandlerBinding.Create(vtables, keys, callbacks.Objects);
        byte* solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);
        byte[] k = "contents\0"u8.ToArray(), v = "8\0"u8.ToArray();

        try
        {
            // vphysics is handed libss_fwd's IVPhysicsKeyHandler, built in the
            // block's reserved words over the block (plan_forwarders_everywhere.md
            // phase 5), not the block itself.
            Assert.Equal((nint)table.Pointer + sizeof(nint), keys.Table);
            Assert.Equal((FakePhysicsCallbacks.KeyHandler, (nint)table.Pointer), FakePhysicsCallbacks.Built(keys.Table));

            ((delegate* unmanaged<void*, void*, void>)Slot(table, KeySlot("SetDefaults")))(table.Pointer, solid);
            Assert.Equal((nint)0x5150, ((ObjectParams*)(solid + SolidLayout.Params))->Name);
            Assert.Equal(1u, keys.Solids.ContentsMask);

            fixed (byte* pk = k)
            fixed (byte* pv = v)
                ((delegate* unmanaged<void*, void*, byte*, byte*, void>)Slot(table, KeySlot("ParseKeyValue")))(table.Pointer, solid, pk, pv);
            Assert.Equal(8u, keys.Solids.ContentsMask);

            Assert.Equal(1, crossings.Read(CrossingOp.ServerSolidDefaults));
            Assert.Equal(1, crossings.Read(CrossingOp.ServerSolidKeyValue));
        }
        finally
        {
            NativeMemory.Free(solid);
        }
    }

    [Fact]
    public void TheServersPrecacheAnswersTheScriptsIndexAndPrecachesIt()
    {
        var precached = new List<string>();
        Func<string, short> precache = ServerPhysicsWorld.ServerPrecache(precached.Add, name => name == "Metal.ImpactHard" ? 41 : -1);

        Assert.Equal((short)41, precache("Metal.ImpactHard"));
        Assert.Equal((short)-1, precache("Nothing.Here"));
        Assert.Equal(["Metal.ImpactHard", "Nothing.Here"], precached);

        Assert.Equal((short)-1, ServerPhysicsWorld.ServerPrecache(_ => { }, null)("Metal.ImpactHard"));
    }
}
