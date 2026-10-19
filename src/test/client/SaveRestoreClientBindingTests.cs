using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client save cluster (the client save codec): IBaseClientDLL's SaveInit,
/// SaveWriteFields and SaveReadFields reach the shared SaveBlockSession
/// through IManagedSaveRestoreClient's slots, one crossing each, and the
/// client state file's DECALLIST block round-trips through the engine's own
/// layout (a typedescription_t array at the stride native passes).
/// </summary>
public sealed unsafe class SaveRestoreClientBindingTests : IDisposable
{
    private const int Stride = 96;   // sizeof(typedescription_t) on this ABI; native passes it

    private readonly List<nint> _native = [];
    private readonly CapturingLog _log = new();

    public void Dispose()
    {
        foreach (nint p in _native)
            NativeMemory.Free((void*)p);
        _native.Clear();
    }

    private byte* Alloc(int bytes)
    {
        byte* p = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(bytes, 1));
        _native.Add((nint)p);
        return p;
    }

    private byte* Name(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        byte* p = Alloc(bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    private sealed class CapturingLog : ILog
    {
        public List<string> Lines { get; } = [];
        public List<string> Warnings { get; } = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Warnings.Add(message);
    }

    /// <summary>The engine's allocator: what IVEngineClient::SaveAllocMemory answers, recorded.</summary>
    private sealed class EngineMemory(SaveRestoreClientBindingTests owner) : ISaveMemory
    {
        public int Allocs { get; private set; }

        public void* SaveAlloc(nuint count, nuint size)
        {
            Allocs++;
            return owner.Alloc((int)(count * size));
        }

        public void SaveFree(void* block) { }
    }

    private sealed class NoModule : ISideModule
    {
        public void Initialize(Side side) { }
        public void Frame(float frameTime) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }
    }

    /// <summary>The engine's decallist_t, member for member.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Decal
    {
        public float X, Y, Z;
        public fixed byte Name[128];
        public short EntityIndex;
        public byte Depth;
        public byte Flags;
        public float NormalX, NormalY, NormalZ;
    }

    // decallist_t's datadesc as the engine hands it: a typedescription_t
    // array, the members the codec must not read poisoned.
    private byte* DecalDescriptions()
    {
        (string Name, int Type, int Size, int Offset)[] fields =
        [
            ("position", SaveFieldCodec.FieldPositionVector, 1, 0),
            ("name", SaveFieldCodec.FieldCharacter, 128, 12),
            ("entityIndex", SaveFieldCodec.FieldShort, 1, 140),
            ("flags", SaveFieldCodec.FieldCharacter, 1, 143),
            ("impactPlaneNormal", SaveFieldCodec.FieldVector, 1, 144),
        ];
        byte* array = Alloc(Stride * fields.Length);
        new Span<byte>(array, Stride * fields.Length).Fill(0x77);
        for (int i = 0; i < fields.Length; i++)
        {
            byte* at = array + i * Stride;
            *(int*)(at + SaveRestoreLayout.FieldType) = fields[i].Type;
            *(nint*)(at + SaveRestoreLayout.FieldName) = (nint)Name(fields[i].Name);
            *(int*)(at + SaveRestoreLayout.FieldOffset) = fields[i].Offset;
            *(ushort*)(at + SaveRestoreLayout.FieldSize) = (ushort)fields[i].Size;
        }
        return array;
    }

    private static Decal ADecal()
    {
        var d = new Decal { X = 128f, Y = -64f, Z = 32f, EntityIndex = 0, Flags = 2, NormalX = 0f, NormalY = 0f, NormalZ = 1f };
        "decals/scorch1\0"u8.CopyTo(new Span<byte>(d.Name, 128));
        return d;
    }

    /// <summary>The block as the engine lays the client state file back down: the bytes and the table, cursor at 0.</summary>
    private void* Reload(void* saved, float time)
    {
        int bytes = *(int*)((byte*)saved + SaveRestoreLayout.Size);
        int slots = *(int*)((byte*)saved + SaveRestoreLayout.TokenCount);
        byte** savedTokens = *(byte***)((byte*)saved + SaveRestoreLayout.Tokens);
        void* block = Alloc(SaveRestoreLayout.SizeOf + bytes);
        byte** tokens = (byte**)Alloc(slots * sizeof(nint));
        SaveRestoreBlock.Place(block, bytes, tokens, slots, time);
        Buffer.MemoryCopy((byte*)saved + SaveRestoreLayout.SizeOf, (byte*)block + SaveRestoreLayout.SizeOf, bytes, bytes);
        for (int i = 0; i < slots; i++)
            tokens[i] = savedTokens[i];
        return block;
    }

    private SaveRestoreClientBinding Binding(Crossings crossings, ISaveMemory? memory)
        => new(crossings, _log, new NoModule(), new SaveBlockSession(_log, () => memory, traceBlocks: false));

    [Fact]
    public void TheBindingsSlotsDriveTheSessionAndCountTheirCrossings()
    {
        var crossings = new Crossings();
        var memory = new EngineMemory(this);
        SaveRestoreClientBinding binding = Binding(crossings, memory);
        Decal decal = ADecal();
        byte* fields = DecalDescriptions();

        void* block = binding.SaveInit(0, 40f);
        Assert.True(block != null);
        Assert.Equal(2, memory.Allocs);   // the buffer and the token table, through the engine
        Assert.Equal(1, binding.SaveWriteFields(block, Name("DECALLIST"), &decal, fields, Stride, 5));

        void* loaded = Reload(block, 40f);
        var output = new Decal();
        new Span<byte>(&output, sizeof(Decal)).Fill(0xcd);
        Assert.Equal(1, binding.SaveReadFields(loaded, Name("DECALLIST"), &output, fields, Stride, 5));

        Assert.Equal(128f, output.X);
        Assert.Equal(32f, output.Z);
        Assert.Equal("decals/scorch1", Marshal.PtrToStringUTF8((nint)output.Name));
        Assert.Equal(0, output.EntityIndex);    // the world's decal: zero, not written, emptied on the way in
        Assert.Equal(0xcd, output.Depth);       // not in the datadesc: neither written nor emptied
        Assert.Equal(2, output.Flags);
        Assert.Equal(1f, output.NormalZ);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientSaveInit));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientSaveWriteFields));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientSaveReadFields));
    }

    [Fact]
    public void ABindingSlotThatThrowsAnswersARefusal()
    {
        var binding = new SaveRestoreClientBinding(new Crossings(), _log, new NoModule(),
                                                   new SaveBlockSession(_log, () => throw new InvalidOperationException("boom"),
                                                                        traceBlocks: false));
        Assert.True(binding.SaveInit(0, 0f) == null);
    }

    [Fact]
    public void NoEngineAllocatorRefusesTheClientSaveLoudly()
    {
        Assert.True(Binding(new Crossings(), null).SaveInit(0, 0f) == null);
        Assert.Contains(_log.Warnings, w => w.Contains("SaveInit: no engine interface"));
    }

    // One block per decal: the client's blocks are not narrated, its SaveInit is.
    [Fact]
    public void TheClientsBlocksAreNotNarratedAndItsSaveInitIs()
    {
        SaveRestoreClientBinding binding = Binding(new Crossings(), new EngineMemory(this));
        Decal decal = ADecal();
        byte* fields = DecalDescriptions();
        void* block = binding.SaveInit(0, 0f);
        for (int i = 0; i < 3; i++)
            Assert.Equal(1, binding.SaveWriteFields(block, Name("DECALLIST"), &decal, fields, Stride, 5));

        Assert.Single(_log.Lines, l => l.Contains("SaveInit("));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("SaveWriteFields"));
    }
}
