using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using Xunit;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// The ragdolls' engine-facing pieces (plan_physics_bones_native_zero Phase 3):
/// the two IVPhysicsKeyHandler tables built by the real registry and called the
/// way vphysics calls them, the render tail the step writes, and the model
/// name pool.
/// </summary>
public unsafe class RagdollEngineTests
{
    private static void* Slot(ManagedVtable table, int slot) => (void*)(*(nint**)table.Pointer)[slot];

    private sealed class KeySink : IRagdollKeySink
    {
        public readonly List<(string, string)> Heard = [];
        public void ParseKeyValue(string key, string value) => Heard.Add((key, value));
    }

    // ---- the key handler --------------------------------------------------------

    private static (ManagedVtable Table, RagdollKeyHandlerBinding Sink) Keys(ManagedVtables vtables, Crossings crossings,
                                                                            nint defaultName = 0)
    {
        var sink = new RagdollKeyHandlerBinding(crossings, defaultName);
        return (RagdollKeyHandlerBinding.Create(vtables, sink), sink);
    }

    private static void Key(ManagedVtable table, void* data, string key, string value)
    {
        byte[] k = System.Text.Encoding.UTF8.GetBytes(key + "\0"), v = System.Text.Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* pk = k)
        fixed (byte* pv = v)
            ((delegate* unmanaged<void*, void*, byte*, byte*, void>)Slot(table, 0))(table.Pointer, data, pk, pv);
    }

    [Fact]
    public void ASolidsDefaultsAreStocksObjectParamsWrittenIntoItsParams()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var (table, sink) = Keys(vtables, crossings, 0x5150);
        byte* solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);

        try
        {
            sink.SolidInFlight = true;
            ((delegate* unmanaged<void*, void*, void>)Slot(table, 1))(table.Pointer, solid);
            ObjectParams p = *(ObjectParams*)(solid + SolidLayout.Params);

            Assert.Equal(2, table.Entries);
            Assert.True(p.Mass == 1f && p.RotInertiaLimit == 0.05f && p.Name == 0x5150 && p.EnableCollisions == 1);
            Assert.Equal(1, crossings.Read(CrossingOp.ClientRagdollSolidDefaults));
        }
        finally
        {
            NativeMemory.Free(solid);
        }
    }

    [Fact]
    public void ACustomBlocksDefaultsWriteNothing()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var (table, _) = Keys(vtables, crossings, 0x5150);
        byte* block = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);

        try
        {
            ((delegate* unmanaged<void*, void*, void>)Slot(table, 1))(table.Pointer, block);

            Assert.Equal(0f, ((ObjectParams*)(block + SolidLayout.Params))->Mass);
            Assert.Equal(1, crossings.Read(CrossingOp.ClientRagdollKeyDefaults));
        }
        finally
        {
            NativeMemory.Free(block);
        }
    }

    [Fact]
    public void ASolidsContentsKeyIsKept()
    {
        using var vtables = new ManagedVtables();
        var (table, sink) = Keys(vtables, new Crossings());
        sink.SolidInFlight = true;

        Key(table, null, "contents", "33");

        Assert.Equal(33u, sink.ContentsMask);
    }

    [Fact]
    public void ACustomBlocksKeysReachTheParseInFlight()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var (table, sink) = Keys(vtables, crossings);
        var keys = new KeySink();
        sink.Current = keys;

        Key(table, table.Pointer, "collisionpair", "0,2");

        Assert.Equal([("collisionpair", "0,2")], keys.Heard);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRagdollKeyValue));
    }

    [Fact]
    public void ASolidsKeysDoNotReachACustomSink()
    {
        using var vtables = new ManagedVtables();
        var (table, sink) = Keys(vtables, new Crossings());
        var keys = new KeySink();
        sink.Current = keys;
        sink.SolidInFlight = true;

        Key(table, null, "mass", "12");

        Assert.Empty(keys.Heard);
    }

    // ---- the render tail -------------------------------------------------------------

    private sealed class Blocks(EntityHandle holder) : IStandaloneBlocks
    {
        public readonly byte[] Bytes = MakeBlock(holder);

        private static byte[] MakeBlock(EntityHandle holder)
        {
            EngineBlock block = default;
            block.Head.Handle = holder.Value;
            block.Client.RenderAngles = new QAngle(0f, 90f, 0f);
            var bytes = new byte[EngineBlock.SizeBytes];
            MemoryMarshal.Write(bytes, in block);
            return bytes;
        }

        public Span<byte> Block(EntityHandle entity) => Bytes;

        public EngineBlock Read() => MemoryMarshal.Read<EngineBlock>(Bytes);
    }

    private sealed class Store : IStandaloneRagdollStore
    {
        public readonly List<(EntityHandle, bool)> Set = [];
        public void SetRagdoll(EntityHandle entity, bool isRagdoll) => Set.Add((entity, isRagdoll));
    }

    private static readonly EntityHandle Owner = new(0x0002_0004u);

    [Fact]
    public void TheStepWritesTheSolversOriginAndBoxIntoTheTail()
    {
        var blocks = new Blocks(Owner);
        var tails = new ClientRagdollTails(blocks, new Store());

        Assert.True(tails.Write(Owner, new Vector(1f, 2f, 3f), new Vector(-4f, -4f, -4f), new Vector(4f, 4f, 4f)));
        EngineBlock block = blocks.Read();

        Assert.Equal((new Vector(1f, 2f, 3f), new Vector(-4f, -4f, -4f), new Vector(4f, 4f, 4f)),
                     (block.Client.RenderOrigin, block.Client.RenderMins, block.Client.RenderMaxs));
    }

    [Fact]
    public void TheRenderTransformIsBuiltFromTheTailsAnglesAtTheNewOrigin()
    {
        var blocks = new Blocks(Owner);
        new ClientRagdollTails(blocks, new Store()).Write(Owner, new Vector(1f, 2f, 3f), Vector.Zero, Vector.Zero);
        Transform t = blocks.Read().Client.RenderTransform;

        // Yaw 90: x maps to y.
        Assert.Equal((1f, 2f, 3f), (t.M03, t.M13, t.M23));
        Assert.True(MathF.Abs(t.M10 - 1f) < 1e-6f && MathF.Abs(t.M00) < 1e-6f);
    }

    [Fact]
    public void ABlockHeldByAnotherEntityIsNotWritten()
    {
        var blocks = new Blocks(new EntityHandle(0x0003_0004u));
        var tails = new ClientRagdollTails(blocks, new Store());

        Assert.False(tails.Write(Owner, new Vector(1f, 2f, 3f), Vector.Zero, Vector.Zero));
        Assert.Equal(Vector.Zero, blocks.Read().Client.RenderOrigin);
    }

    [Fact]
    public void TheShellIsToldThroughTheStore()
    {
        var store = new Store();
        new ClientRagdollTails(new Blocks(Owner), store).SetRagdoll(Owner, true);

        Assert.Equal([(Owner, true)], store.Set);
    }

    // ---- the names ----------------------------------------------------------------------

    [Fact]
    public void AModelsNameIsOneNativeCopy()
    {
        var names = new ClientRagdollNames();

        try
        {
            nint first = names.NameOf("models/player.mdl");

            Assert.Equal(first, names.NameOf("models/player.mdl"));
            Assert.Equal("models/player.mdl", Marshal.PtrToStringUTF8(first));
        }
        finally
        {
            names.ReleaseAll();
        }
    }

    [Fact]
    public void ReleasingEveryNameEmptiesThePool()
    {
        var names = new ClientRagdollNames();
        names.NameOf("a.mdl");
        names.NameOf("b.mdl");

        names.ReleaseAll();

        Assert.Empty(names.Held);
    }
}
