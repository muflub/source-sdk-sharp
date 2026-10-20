using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's save cluster in C# (plan_server_native_v2 T4c): SaveInit's
/// block, the engine's own structs through SaveWriteFields/SaveReadFields, and
/// the managed world's body block, over CSaveRestoreData by layout. Stock's
/// ::SaveInit (saverestore.cpp) and shell_savegame.cpp's server half are the
/// behaviour.
/// </summary>
public sealed unsafe class SaveGameSessionTests : IDisposable
{
    private readonly SaveArena _arena = new();
    private readonly CapturingLog _log = new();

    public void Dispose() => _arena.Dispose();

    private sealed class CapturingLog : ILog
    {
        public List<string> Lines { get; } = [];
        public List<string> Warnings { get; } = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Warnings.Add(message);
    }

    /// <summary>The engine's allocator: calloc, and a record of every free.</summary>
    // An engine that refuses IVEngineServer: the registered allocator throws
    // at its first call, naming the interface, as a bound slot does.
    private sealed class RefusedMemory : ISaveMemory
    {
        public void* SaveAlloc(nuint count, nuint size) => throw new InvalidOperationException("boom");
        public void SaveFree(void* block) => throw new InvalidOperationException("boom");
    }

    private sealed class EngineMemory(SaveArena arena) : ISaveMemory
    {
        public int RefuseCall { get; init; } = -1;
        public List<(nuint Count, nuint Size)> Allocs { get; } = [];
        public List<nint> Frees { get; } = [];
        public List<nint> Answered { get; } = [];

        public void* SaveAlloc(nuint count, nuint size)
        {
            Allocs.Add((count, size));
            if (Allocs.Count - 1 == RefuseCall)
                return null;
            void* p = arena.Alloc((int)(count * size));
            new Span<byte>(p, (int)(count * size)).Fill(0xab);   // not calloc: placement must zero
            Answered.Add((nint)p);
            return p;
        }

        public void SaveFree(void* block) => Frees.Add((nint)block);
    }

    private sealed class Entities(byte[] world) : ISavedEntities
    {
        public byte[]? Restored { get; private set; }
        public int CollectCalls { get; private set; }
        public int Lie { get; init; }

        public int Collect(Span<byte> destination)
        {
            CollectCalls++;
            if (destination.Length < world.Length)
                return world.Length;
            world.CopyTo(destination);
            return world.Length + (destination.Length > 0 ? Lie : 0);
        }

        public int Restore(ReadOnlySpan<byte> source)
        {
            Restored = source.ToArray();
            return 3;
        }
    }

    private sealed class Refusing : ISavedEntities
    {
        public int Collect(Span<byte> destination) => -1;
        public int Restore(ReadOnlySpan<byte> source) => throw new InvalidOperationException("never asked");
    }

    //-------------------------------------------------------------------------
    // The layout, one fact per offset (save_restore_layout_test.cpp pins the
    // same numbers against offsetof).
    //-------------------------------------------------------------------------

    [Fact] public void BaseDataIsAt0() => Assert.Equal(0, SaveRestoreLayout.BaseData);
    [Fact] public void CurrentDataIsAt8() => Assert.Equal(8, SaveRestoreLayout.CurrentData);
    [Fact] public void SizeIsAt16() => Assert.Equal(16, SaveRestoreLayout.Size);
    [Fact] public void BufferSizeIsAt20() => Assert.Equal(20, SaveRestoreLayout.BufferSize);
    [Fact] public void TokenCountIsAt24() => Assert.Equal(24, SaveRestoreLayout.TokenCount);
    [Fact] public void TokensIsAt32() => Assert.Equal(32, SaveRestoreLayout.Tokens);
    [Fact] public void ConnectionCountIsAt40() => Assert.Equal(40, SaveRestoreLayout.ConnectionCount);
    [Fact] public void UseLandmarkIsAt1456() => Assert.Equal(1456, SaveRestoreLayout.UseLandmark);
    [Fact] public void LandmarkOffsetIsAt1480() => Assert.Equal(1480, SaveRestoreLayout.LandmarkOffset);
    [Fact] public void TimeIsAt1492() => Assert.Equal(1492, SaveRestoreLayout.Time);
    [Fact] public void TheSaveBlockIs1616Bytes() => Assert.Equal(1616, SaveRestoreLayout.SizeOf);
    [Fact] public void FieldTypeIsAt0() => Assert.Equal(0, SaveRestoreLayout.FieldType);
    [Fact] public void FieldNameIsAt8() => Assert.Equal(8, SaveRestoreLayout.FieldName);
    [Fact] public void FieldOffsetIsAt16() => Assert.Equal(16, SaveRestoreLayout.FieldOffset);
    [Fact] public void FieldSizeIsAt24() => Assert.Equal(24, SaveRestoreLayout.FieldSize);

    //-------------------------------------------------------------------------
    // SaveInit.
    //-------------------------------------------------------------------------

    private static T Read<T>(void* block, int offset) where T : unmanaged => *(T*)((byte*)block + offset);

    [Fact]
    public void AZeroSizeGetsTheDefaultBufferAndTheStockTokenTable()
    {
        var memory = new EngineMemory(_arena);
        void* block = new SaveGameSession(_log, memory).Init(0, 42.5f);

        Assert.True(block != null);
        Assert.Equal(((nuint)(SaveRestoreLayout.SizeOf + SaveBlockSession.DefaultBufferBytes), (nuint)1), memory.Allocs[0]);
        Assert.Equal(((nuint)0xfff, (nuint)sizeof(nint)), memory.Allocs[1]);
        Assert.Equal(4 * 1024 * 1024, Read<int>(block, SaveRestoreLayout.BufferSize));
        Assert.Equal(0xfff, Read<int>(block, SaveRestoreLayout.TokenCount));
        Assert.Equal(memory.Answered[1], Read<nint>(block, SaveRestoreLayout.Tokens));
    }

    [Fact]
    public void ASizeTheEngineDoesPassIsHonoured()
    {
        void* block = new SaveGameSession(_log, new EngineMemory(_arena)).Init(5000, 0f);
        Assert.Equal(5000, Read<int>(block, SaveRestoreLayout.BufferSize));
    }

    [Fact]
    public void TheBlockIsPlacedZeroedWithTheBufferRightAfterIt()
    {
        var memory = new EngineMemory(_arena);
        void* block = new SaveGameSession(_log, memory).Init(1024, 42.5f);

        nint data = (nint)block + SaveRestoreLayout.SizeOf;
        Assert.Equal(data, Read<nint>(block, SaveRestoreLayout.BaseData));
        Assert.Equal(data, Read<nint>(block, SaveRestoreLayout.CurrentData));
        Assert.Equal(0, Read<int>(block, SaveRestoreLayout.Size));
        Assert.Equal(42.5f, Read<float>(block, SaveRestoreLayout.Time));
        Assert.Equal(0, Read<int>(block, SaveRestoreLayout.ConnectionCount));
        Assert.Equal(0, Read<int>(block, SaveRestoreLayout.UseLandmark));
        Assert.Equal(0f, Read<float>(block, SaveRestoreLayout.LandmarkOffset));
        // The allocator answered 0xab everywhere: the constructors zero the rest
        // (the entity table, bAsync), the token table is InitSymbolTable's memset.
        Assert.False(new ReadOnlySpan<byte>((byte*)block + 1536, SaveRestoreLayout.SizeOf - 1536).ContainsAnyExcept((byte)0));
        Assert.False(new ReadOnlySpan<byte>((void*)memory.Answered[1], 0xfff * sizeof(nint)).ContainsAnyExcept((byte)0));
    }

    [Fact]
    public void ARefusedBufferAnswersNullAndSaysSo()
    {
        var memory = new EngineMemory(_arena) { RefuseCall = 0 };
        Assert.True(new SaveGameSession(_log, memory).Init(0, 0f) == null);
        Assert.Single(memory.Allocs);
        Assert.Contains(_log.Warnings, w => w.Contains("SaveAllocMemory refused"));
    }

    [Fact]
    public void ARefusedTokenTableFreesTheBufferItAlreadyHas()
    {
        var memory = new EngineMemory(_arena) { RefuseCall = 1 };
        Assert.True(new SaveGameSession(_log, memory).Init(0, 0f) == null);
        Assert.Equal([memory.Answered[0]], memory.Frees);   // stock freed the null instead
        Assert.Contains(_log.Warnings, w => w.Contains("token table"));
    }

    //-------------------------------------------------------------------------
    // The engine's own structs, through typedescription_t arrays.
    //-------------------------------------------------------------------------

    private const int Stride = 96;   // sizeof(typedescription_t) on this ABI; native passes it

    private byte* Descriptions(params (string? Name, int Type, int Size, int Offset)[] fields)
    {
        byte* array = (byte*)_arena.Alloc(Stride * fields.Length);
        new Span<byte>(array, Stride * fields.Length).Fill(0x77);   // the members the codec must not read
        for (int i = 0; i < fields.Length; i++)
        {
            byte* at = array + i * Stride;
            *(int*)(at + SaveRestoreLayout.FieldType) = fields[i].Type;
            *(nint*)(at + SaveRestoreLayout.FieldName) = fields[i].Name is null ? 0 : (nint)_arena.Name(fields[i].Name!);
            *(int*)(at + SaveRestoreLayout.FieldOffset) = fields[i].Offset;
            *(ushort*)(at + SaveRestoreLayout.FieldSize) = (ushort)fields[i].Size;
        }
        return array;
    }

    private byte* HeaderDescriptions() => Descriptions(
        ("skillLevel", SaveFieldCodec.FieldInteger, 1, (int)Marshal.OffsetOf<SaveHeaderShape>("SkillLevel")),
        ("time", SaveFieldCodec.FieldTime, 1, (int)Marshal.OffsetOf<SaveHeaderShape>("Time")),
        ("mapName", SaveFieldCodec.FieldCharacter, 32, (int)Marshal.OffsetOf<SaveHeaderShape>("MapName")));

    private static SaveHeaderShape Header()
    {
        var header = new SaveHeaderShape { SkillLevel = 3, Time = 150f };
        "dm_lockdown\0"u8.CopyTo(new Span<byte>(header.MapName, 32));
        return header;
    }

    [Fact]
    public void AFieldBlockIsWrittenAtTheCursorAndTheCursorMovesPastIt()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* block = session.Init(1024, 100f);
        SaveHeaderShape header = Header();

        Assert.True(session.WriteFields(block, _arena.Name("Save Header"), (byte*)&header, HeaderDescriptions(), Stride, 3));

        int expected = 8 + 2 * (4 + 4) + (4 + 32);
        Assert.Equal(expected, Read<int>(block, SaveRestoreLayout.Size));
        Assert.Equal((nint)block + SaveRestoreLayout.SizeOf + expected, Read<nint>(block, SaveRestoreLayout.CurrentData));
        byte* data = (byte*)block + SaveRestoreLayout.SizeOf;
        Assert.Equal(3, *(int*)(data + 4));
        Assert.Equal(50f, *(float*)(data + 8 + 8 + 4));   // the time record is a delta from the block's base time
    }

    [Fact]
    public void TheEnginesStructComesBackThroughALoadedBlock()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 100f);
        SaveHeaderShape header = Header();
        byte* fields = HeaderDescriptions();
        Assert.True(session.WriteFields(saved, _arena.Name("Save Header"), (byte*)&header, fields, Stride, 3));

        // The load: the engine places a fresh block over the file's bytes and
        // DEFINES the file's tokens at their saved slots, at its own base time.
        void* loaded = Reload(saved, 7f);
        SaveHeaderShape output = default;
        Assert.True(session.ReadFields(loaded, _arena.Name("Save Header"), (byte*)&output, fields, Stride, 3));

        Assert.Equal(3, output.SkillLevel);
        Assert.Equal(57f, output.Time);
        Assert.Equal("dm_lockdown", Marshal.PtrToStringUTF8((nint)output.MapName));
        Assert.Equal(Read<int>(saved, SaveRestoreLayout.Size), Read<int>(loaded, SaveRestoreLayout.Size));
    }

    /// <summary>The engine's load: the save's bytes and token strings, copied into a new block.</summary>
    private void* Reload(void* saved, float time)
    {
        int bytes = Read<int>(saved, SaveRestoreLayout.Size);
        int slots = Read<int>(saved, SaveRestoreLayout.TokenCount);
        byte** savedTokens = (byte**)Read<nint>(saved, SaveRestoreLayout.Tokens);

        void* block = _arena.Alloc(SaveRestoreLayout.SizeOf + bytes);
        byte** tokens = (byte**)_arena.Alloc(slots * sizeof(nint));
        SaveRestoreBlock.Place(block, bytes, tokens, slots, time);
        Buffer.MemoryCopy((byte*)saved + SaveRestoreLayout.SizeOf, (byte*)block + SaveRestoreLayout.SizeOf, bytes, bytes);
        for (int i = 0; i < slots; i++)
        {
            if (savedTokens[i] != null)
                tokens[i] = _arena.Name(Marshal.PtrToStringUTF8((nint)savedTokens[i])!);
        }
        return block;
    }

    [Fact]
    public void AnUnnamedFieldGoesUnderOneNameAndNotAnEmptyToken()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* block = session.Init(1024, 0f);
        int value = 9;

        Assert.True(session.WriteFields(block, _arena.Name("B"), (byte*)&value, Descriptions((null, SaveFieldCodec.FieldInteger, 1, 0)), Stride, 1));

        byte* data = (byte*)block + SaveRestoreLayout.SizeOf;
        var tokens = new SaveRestoreBlock(block).Symbols;
        Assert.Equal("<unnamed>", Marshal.PtrToStringUTF8((nint)tokens.FromSymbol(*(short*)(data + 10))));
    }

    [Fact]
    public void AnOmittedFieldIsNamedInTheWarningAndTheBlockIsStillWritten()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* block = session.Init(1024, 0f);
        int value = 9;

        Assert.True(session.WriteFields(block, _arena.Name("B"), (byte*)&value,
                                        Descriptions(("good", SaveFieldCodec.FieldInteger, 1, 0), ("hEnt", 13, 1, 0), ("f", 1, 1, 0)), Stride, 3));
        Assert.Contains(_log.Warnings, w => w.Contains("short 2 of 3") && w.Contains("hEnt(type 13)") && w.Contains("f(type 1)"));
        Assert.Equal(8 + 8, Read<int>(block, SaveRestoreLayout.Size));
    }

    [Fact]
    public void ABlockThatDoesNotFitIsRefusedAndTheCursorStays()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* block = session.Init(16, 0f);
        SaveHeaderShape header = Header();

        Assert.False(session.WriteFields(block, _arena.Name("Save Header"), (byte*)&header, HeaderDescriptions(), Stride, 3));
        Assert.Equal(0, Read<int>(block, SaveRestoreLayout.Size));
        Assert.Contains(_log.Warnings, w => w.Contains("did not fit"));
    }

    [Fact]
    public void AReadOfAnotherBlockIsRefusedAndTheStructAndCursorStay()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        SaveHeaderShape header = Header();
        byte* fields = HeaderDescriptions();
        session.WriteFields(saved, _arena.Name("Save Header"), (byte*)&header, fields, Stride, 3);

        void* loaded = Reload(saved, 0f);
        *(int*)((byte*)loaded + SaveRestoreLayout.Size) = 0;
        SaveHeaderShape output = new() { SkillLevel = -5 };
        Assert.False(session.ReadFields(loaded, _arena.Name("LIGHTSTYLE"), (byte*)&output, fields, Stride, 3));
        Assert.Equal(-5, output.SkillLevel);
        Assert.Equal(0, Read<int>(loaded, SaveRestoreLayout.Size));
        Assert.Contains(_log.Warnings, w => w.Contains("REFUSED block 'LIGHTSTYLE'"));
    }

    [Fact]
    public void ANullArgumentIsRefusedRatherThanDereferenced()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        int value = 0;
        Assert.False(session.WriteFields(null, _arena.Name("B"), (byte*)&value, HeaderDescriptions(), Stride, 1));
        Assert.False(session.ReadFields(session.Init(64, 0f), _arena.Name("B"), null, HeaderDescriptions(), Stride, 1));
    }

    //-------------------------------------------------------------------------
    // The body block: the managed world.
    //-------------------------------------------------------------------------

    private static byte[] World(int bytes)
    {
        var world = new byte[bytes];
        for (int i = 0; i < bytes; i++)
            world[i] = (byte)(i * 13 + 1);
        return world;
    }

    [Fact]
    public void TheWorldGoesOutAsOneBlockAndComesBackWhole()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(200_000, 0f);
        byte[] world = World(70_000);   // three blob records
        var entities = new Entities(world);

        Assert.True(session.WriteEntities(saved, entities));
        Assert.Equal(2, entities.CollectCalls);   // size, then fill
        Assert.Equal(8 + 3 * 4 + 70_000, Read<int>(saved, SaveRestoreLayout.Size));

        void* loaded = Reload(saved, 0f);
        *(int*)((byte*)loaded + SaveRestoreLayout.Size) = 0;
        var restored = new Entities([]);
        Assert.Equal(3, session.ReadEntities(loaded, restored));
        Assert.Equal(world, restored.Restored);
        Assert.Equal(8 + 3 * 4 + 70_000, Read<int>(loaded, SaveRestoreLayout.Size));
    }

    [Fact]
    public void TheBodyFollowsTheFieldBlocksAtTheCursor()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(4096, 0f);
        SaveHeaderShape header = Header();
        byte* fields = HeaderDescriptions();
        session.WriteFields(saved, _arena.Name("Save Header"), (byte*)&header, fields, Stride, 3);
        Assert.True(session.WriteEntities(saved, new Entities(World(10))));

        void* loaded = Reload(saved, 0f);
        *(int*)((byte*)loaded + SaveRestoreLayout.Size) = 0;
        SaveHeaderShape output = default;
        Assert.True(session.ReadFields(loaded, _arena.Name("Save Header"), (byte*)&output, fields, Stride, 3));
        var restored = new Entities([]);
        Assert.Equal(3, session.ReadEntities(loaded, restored));
        Assert.Equal(World(10), restored.Restored);
    }

    [Fact]
    public void ANothingLiveWorldWritesNoBlock()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        Assert.True(session.WriteEntities(saved, new Entities([])));
        Assert.Equal(0, Read<int>(saved, SaveRestoreLayout.Size));
    }

    [Fact]
    public void ASaveWithoutTheBlockRestoresNothingAndIsNotAnError()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        var restored = new Entities([]);
        Assert.Equal(SaveBlobCodec.NoBlock, session.ReadEntities(saved, restored));
        Assert.Null(restored.Restored);
        Assert.Empty(_log.Warnings);
    }

    [Fact]
    public void AWorldThatCannotBeEncodedRefusesTheBlockLoudly()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        Assert.False(session.WriteEntities(saved, new Refusing()));
        Assert.Equal(0, Read<int>(saved, SaveRestoreLayout.Size));
        Assert.Contains(_log.Warnings, w => w.Contains("refused to encode"));
    }

    [Fact]
    public void ASizeAndAFillThatDisagreeWriteNothing()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        Assert.False(session.WriteEntities(saved, new Entities(World(10)) { Lie = 1 }));
        Assert.Equal(0, Read<int>(saved, SaveRestoreLayout.Size));
        Assert.Contains(_log.Warnings, w => w.Contains("the two calls must agree"));
    }

    [Fact]
    public void AWorldTooBigForTheBufferIsRefusedAndTheCursorStays()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(64, 0f);
        Assert.False(session.WriteEntities(saved, new Entities(World(100))));
        Assert.Equal(0, Read<int>(saved, SaveRestoreLayout.Size));
    }

    [Fact]
    public void ACorruptBodyBlockIsRefusedAndNothingIsRestored()
    {
        var session = new SaveGameSession(_log, new EngineMemory(_arena));
        void* saved = session.Init(1024, 0f);
        session.WriteEntities(saved, new Entities(World(40)));
        void* loaded = Reload(saved, 0f);
        *(int*)((byte*)loaded + SaveRestoreLayout.Size) = 0;
        *(int*)((byte*)loaded + SaveRestoreLayout.SizeOf + 4) = -1;   // the chunk count

        var restored = new Entities([]);
        Assert.Equal(-1, session.ReadEntities(loaded, restored));
        Assert.Null(restored.Restored);
        Assert.Equal(0, Read<int>(loaded, SaveRestoreLayout.Size));
    }

    //-------------------------------------------------------------------------
    // The binding: IManagedSaveRestore's slots reach the session, counted.
    //-------------------------------------------------------------------------

    private sealed class WorldModule(byte[] world) : ISideModule
    {
        public byte[]? Restored { get; private set; }
        public void Initialize(Side side) { }
        public void Frame(float frameTime) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void ConCommandDispatch(int commandId, string args) { }
        public void Shutdown() { }
        public void DispatchGameEvent(string eventName, IntPtr handle) { }

        public int SaveCollect(Span<byte> destination)
        {
            if (destination.Length < world.Length)
                return world.Length;
            world.CopyTo(destination);
            return world.Length;
        }

        public int SaveRestore(ReadOnlySpan<byte> source)
        {
            Restored = source.ToArray();
            return 1;
        }
    }

    [Fact]
    public void TheBindingsSlotsDriveTheSessionAndCountTheirCrossings()
    {
        var crossings = new Crossings();
        var module = new WorldModule(World(25));
        var binding = new SaveRestoreBinding(crossings, _log, module,
                                             new SaveGameSession(_log, new EngineMemory(_arena)));
        SaveHeaderShape header = Header();
        byte* fields = HeaderDescriptions();

        void* block = binding.SaveInit(0, 100f);
        Assert.True(block != null);
        Assert.Equal(1, binding.SaveWriteFields(block, _arena.Name("Save Header"), &header, fields, Stride, 3));
        Assert.Equal(1, binding.SaveWriteBody(block));

        void* loaded = Reload(block, 100f);
        SaveHeaderShape output = default;
        Assert.Equal(1, binding.SaveReadFields(loaded, _arena.Name("Save Header"), &output, fields, Stride, 3));
        Assert.Equal(1, binding.SaveReadBody(loaded));

        Assert.Equal(World(25), module.Restored);
        Assert.Equal(3, output.SkillLevel);
        Assert.Equal(1, crossings.Read(CrossingOp.ServerSaveInit));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerSaveWriteFields));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerSaveReadFields));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerSaveWriteBody));   // the live gate's save-load step reads these two
        Assert.Equal(1, crossings.Read(CrossingOp.ServerSaveReadBody));
    }

    [Fact]
    public void ABindingSlotThatThrowsAnswersARefusal()
    {
        var binding = new SaveRestoreBinding(new Crossings(), _log, new WorldModule([]),
                                             new SaveGameSession(_log, new RefusedMemory()));
        Assert.True(binding.SaveInit(0, 0f) == null);
    }
}
