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
        public int ParseSurfaceData() { Asked.Add("manifest"); return 2; }
        public int PrecacheSurfaceSounds() { Asked.Add("sounds"); return 30; }
        public SolidKeyDefaults? Solids { get; } = new(0x5150);
    }

    [Fact]
    public void EachSlotReachesTheWorldAndCountsOneCrossing()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        var crossings = new Crossings();
        var world = new World();
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(crossings, new RecordingLog(), () => world));
        byte* solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);
        byte[] k = "contents\0"u8.ToArray(), v = "3\0"u8.ToArray();

        try
        {
            // Release is slot 0, Validate 1, then the five rows in order.
            Assert.Equal((nint)0x4321, (nint)((delegate* unmanaged<void*, void*>)Slot(table, 2))(table.Pointer));
            Assert.Equal(2, ((delegate* unmanaged<void*, int>)Slot(table, 3))(table.Pointer));
            Assert.Equal(30, ((delegate* unmanaged<void*, int>)Slot(table, 4))(table.Pointer));

            ((delegate* unmanaged<void*, void*, void>)Slot(table, 6))(table.Pointer, solid);
            Assert.Equal((nint)0x5150, ((ObjectParams*)(solid + SolidLayout.Params))->Name);
            Assert.Equal(1u, world.Solids!.ContentsMask);

            fixed (byte* pk = k)
            fixed (byte* pv = v)
                ((delegate* unmanaged<void*, void*, byte*, byte*, void>)Slot(table, 5))(table.Pointer, solid, pk, pv);
            Assert.Equal(3u, world.Solids.ContentsMask);

            Assert.Equal(["world", "manifest", "sounds"], world.Asked);
            Assert.Equal(1, crossings.Read(CrossingOp.ClientPhysicsCreateWorld));
            Assert.Equal(1, crossings.Read(CrossingOp.ClientPhysicsParseSurfaceData));
            Assert.Equal(1, crossings.Read(CrossingOp.ClientPhysicsPrecacheSurfaceSounds));
            Assert.Equal(1, crossings.Read(CrossingOp.ClientSolidKeyValue));
            Assert.Equal(1, crossings.Read(CrossingOp.ClientSolidDefaults));
        }
        finally
        {
            NativeMemory.Free(solid);
        }
    }

    [Fact]
    public void WithNoWorldComposedTheSlotsAnswerNothing()
    {
        using var vtables = new ManagedVtables(side: Side.Client);
        ManagedVtable table = ManagedAreaFactory.Create(vtables, (IManagedPhysicsClient)new PhysicsClientBinding(new Crossings(), new RecordingLog(), () => null));

        Assert.Equal((nint)0, (nint)((delegate* unmanaged<void*, void*>)Slot(table, 2))(table.Pointer));
        Assert.Equal(-1, ((delegate* unmanaged<void*, int>)Slot(table, 3))(table.Pointer));
        Assert.Equal(-1, ((delegate* unmanaged<void*, int>)Slot(table, 4))(table.Pointer));
        ((delegate* unmanaged<void*, void*, void>)Slot(table, 6))(table.Pointer, null);
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
