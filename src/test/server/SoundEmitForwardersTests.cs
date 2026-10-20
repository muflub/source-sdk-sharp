using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine calls the managed SoundEmitter makes (plan_server_native_v2 T4a),
/// through the forwarder tables, over fakes: what each wrapper hands the
/// forwarder, and how it reads what comes back. The layouts are held to the
/// headers by vtable_index_agreement_test (SoundParametersInternalLayout,
/// PlayerBitVecLayout); these hold the managed side to the same numbers.
/// </summary>
public sealed unsafe class SoundEmitForwardersTests : IDisposable
{
    // ---- what the fakes saw (the test assembly may keep statics) ----
    private static nint s_Filter;
    private static int s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker;
    private static float s_Volume, s_SoundTime;
    private static string? s_Sample;
    private static nint s_OriginPointer;
    private static Vector s_Origin;
    private static nint s_Direction, s_Origins;
    private static byte s_UpdatePositions;
    private static short s_HandleSeen;
    private static byte s_UsePas;
    private static byte* s_Entry;

    [UnmanagedCallersOnly]
    private static void EmitSound(void* self, void* filter, int ent, int channel, byte* sample, float volume,
                                  int level, int flags, int pitch, int dsp, void* origin, void* direction,
                                  void* origins, byte update, float time, int speaker)
    {
        s_Filter = (nint)filter;
        (s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker) = (ent, channel, level, flags, pitch, dsp, speaker);
        (s_Volume, s_SoundTime, s_UpdatePositions) = (volume, time, update);
        s_Sample = Marshal.PtrToStringUTF8((nint)sample);
        s_OriginPointer = (nint)origin;
        s_Origin = origin == null ? default : *(Vector*)origin;
        (s_Direction, s_Origins) = ((nint)direction, (nint)origins);
    }

    [UnmanagedCallersOnly]
    private static byte ParametersEx(void* self, byte* name, void* handle, void* parameters, int gender, byte emitted)
    {
        s_HandleSeen = *(short*)handle;
        if (Marshal.PtrToStringUTF8((nint)name) != "Doors.Move1")
            return 0;

        *(short*)handle = 42;
        ((SoundParametersLayout*)parameters)->Channel = gender + 10 * emitted;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void* InternalParameters(void* self, int index) => index == 42 ? s_Entry : null;

    [UnmanagedCallersOnly]
    private static byte* WaveName(void* self, void* symbol)
        => *(ushort*)symbol switch
        {
            7 => (byte*)Marshal.StringToCoTaskMemUTF8("doors/move1.wav"),
            8 => (byte*)Marshal.StringToCoTaskMemUTF8("doors/move2.wav"),
            _ => null,
        };

    [UnmanagedCallersOnly]
    private static void Multicast(void* self, byte usePas, void* origin, void* bits)
    {
        s_UsePas = usePas;
        s_Origin = *(Vector*)origin;
        ((uint*)bits)[1] |= 1u << 5;   // player slot 38
    }

    private readonly List<nint> _frees = [];

    public SoundEmitForwardersTests()
    {
        s_Filter = s_OriginPointer = s_Direction = s_Origins = -1;
        s_Sample = null;
        s_HandleSeen = 0;
        s_Entry = null;
    }

    public void Dispose()
    {
        foreach (nint p in _frees)
            NativeMemory.Free((void*)p);
    }

    private T* Zeroed<T>() where T : unmanaged
    {
        var p = (T*)NativeMemory.AllocZeroed(1, (nuint)sizeof(T));
        _frees.Add((nint)p);
        return p;
    }

    private byte* Bytes(int count)
    {
        var p = (byte*)NativeMemory.AllocZeroed((nuint)count);
        _frees.Add((nint)p);
        return p;
    }

    // ---- IEngineSoundServer003 ----

    private EngineSoundServer Sound()
    {
        EngineSoundFwd* fwd = Zeroed<EngineSoundFwd>();
        fwd->EmitSound_2 = &EmitSound;
        return new EngineSoundServer(Bytes(8), fwd);
    }

    [Fact]
    public void EmitSoundHandsTheFilterAndEveryArgumentThrough()
    {
        Sound().EmitSound(0x1234, 5, 6, "Doors.Move1", 0.5f, 80, 4, 110, 2, new Vector(1, 2, 3), 7.5f, 9);

        Assert.Equal((nint)0x1234, s_Filter);
        Assert.Equal((5, 6, 80, 4, 110, 2, 9), (s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker));
        Assert.Equal((0.5f, 7.5f), (s_Volume, s_SoundTime));
        Assert.Equal("Doors.Move1", s_Sample);
        Assert.Equal(new Vector(1, 2, 3), s_Origin);
    }

    [Fact]
    public void EmitSoundAsksForNoDirectionAndNoOriginListAndUpdatesPositions()
    {
        Sound().EmitSound(0x1234, 5, 6, "Doors.Move1", 0.5f, 80, 4, 110, 2, new Vector(1, 2, 3), 7.5f, 9);

        Assert.Equal(((nint)0, (nint)0, (byte)1), (s_Direction, s_Origins, s_UpdatePositions));
    }

    [Fact]
    public void ANullOriginIsANullPointer()
    {
        Sound().EmitSound(0x1234, 5, 6, "Doors.Move1", 0.5f, 80, 4, 110, 2, null, 0f, -1);

        Assert.Equal((nint)0, s_OriginPointer);
    }

    [Fact]
    public void EmitSoundRefusesANullFilter()
        => Assert.Throws<ArgumentException>(() => Sound().EmitSound(0, 5, 6, "x", 1f, 75, 0, 100, 0, null, 0f, -1));

    // ---- ISoundEmitterSystemBase ----

    private SoundEmitterBase Emitter()
    {
        SoundEmitterSystemFwd* fwd = Zeroed<SoundEmitterSystemFwd>();
        fwd->GetParametersForSoundEx = &ParametersEx;
        fwd->InternalGetParametersForSound = &InternalParameters;
        fwd->GetWaveName = &WaveName;
        return new SoundEmitterBase(Bytes(8), fwd);
    }

    [Fact]
    public void ParametersExPassesTheHandleAndReadsBackWhatTheTableWrote()
    {
        short handle = -1;
        var parameters = SoundParametersLayout.Stock();

        Assert.True(Emitter().ParametersForSoundEx("Doors.Move1", ref handle, ref parameters, 2, true));
        Assert.Equal((short)-1, s_HandleSeen);
        Assert.Equal((short)42, handle);
        Assert.Equal(12, parameters.Channel);
    }

    [Fact]
    public void ParametersExIsFalseForAScriptTheTableDoesNotHave()
    {
        short handle = -1;
        var parameters = SoundParametersLayout.Stock();

        Assert.False(Emitter().ParametersForSoundEx("No.Such", ref handle, ref parameters, 0, true));
    }

    [Fact]
    public void AnEntryWithOneWaveReadsItFromThePointersOwnBytes()
    {
        s_Entry = Bytes(SoundParametersInternalLayout.SizeInBytes);
        *(ushort*)(s_Entry + 16) = 1;
        *(ushort*)(s_Entry + 0) = 7;
        *(ushort*)(s_Entry + 30) = 3;

        var entry = Emitter().Entry(42);

        Assert.NotNull(entry);
        Assert.Equal(3, entry.Channel);
        Assert.Equal(["doors/move1.wav"], entry.Waves);
    }

    [Fact]
    public void AnEntryWithTwoWavesReadsThemThroughThePointer()
    {
        s_Entry = Bytes(SoundParametersInternalLayout.SizeInBytes);
        byte* waves = Bytes(2 * SoundParametersInternalLayout.SoundFileSize);
        *(ushort*)(waves + 0) = 7;
        *(ushort*)(waves + 4) = 8;
        *(ushort*)(s_Entry + 16) = 2;
        *(byte**)(s_Entry + 0) = waves;
        *(ushort*)(s_Entry + 30) = 1;

        var entry = Emitter().Entry(42);

        Assert.NotNull(entry);
        Assert.Equal(["doors/move1.wav", "doors/move2.wav"], entry.Waves);
    }

    [Fact]
    public void AnIndexTheTableHasNoEntryAtIsNull() => Assert.Null(Emitter().Entry(3));

    [Fact]
    public void ShouldPreloadIsBitThreeOfByte34()
    {
        s_Entry = Bytes(SoundParametersInternalLayout.SizeInBytes);
        var emitter = Emitter();
        Assert.False(emitter.ShouldPreload(42));

        s_Entry[34] = 0x08;
        Assert.True(emitter.ShouldPreload(42));

        // Every other bit of the byte, and its neighbours: still no.
        s_Entry[33] = 0xFF;
        s_Entry[34] = 0xF7;
        s_Entry[35] = 0xFF;
        Assert.False(emitter.ShouldPreload(42));
    }

    [Fact]
    public void AnIndexWithNoEntryDoesNotPreload()
    {
        s_Entry = Bytes(SoundParametersInternalLayout.SizeInBytes);
        s_Entry[34] = 0x08;

        Assert.False(Emitter().ShouldPreload(3));
    }

    [Fact]
    public void ThePreloadFlagIsWhereTheHeaderPutsIt()
        => Assert.Equal((34, (byte)0x08),
                        (SoundParametersInternalLayout.FlagsOffset, SoundParametersInternalLayout.ShouldPreloadMask));

    [Fact]
    public void TheTablesWalkAndLifecycleGoThroughTheirOwnSlots()
    {
        SoundEmitterSystemFwd* fwd = Zeroed<SoundEmitterSystemFwd>();
        fwd->ModInit = &ModInitAnswers;
        fwd->First = &FirstAnswers;
        fwd->Next = &NextAnswers;
        fwd->InvalidIndex = &InvalidAnswers;
        fwd->IsValidIndex = &IsValidAnswers;
        fwd->AddSoundOverrides = &AddOverridesRecords;
        var emitter = new SoundEmitterBase(Bytes(8), fwd);

        Assert.True(emitter.ModInit());
        Assert.Equal((5, 6, -7), (emitter.First(), emitter.Next(5), emitter.InvalidIndex()));
        Assert.True(emitter.IsValidIndex(6));
        Assert.False(emitter.IsValidIndex(-7));
        emitter.AddSoundOverrides("maps/x_level_sounds.txt");
        Assert.Equal(("maps/x_level_sounds.txt", (byte)0), (s_Sample, s_UsePas));
    }

    [UnmanagedCallersOnly] private static byte ModInitAnswers(void* self) => 1;
    [UnmanagedCallersOnly] private static int FirstAnswers(void* self) => 5;
    [UnmanagedCallersOnly] private static int NextAnswers(void* self, int i) => i + 1;
    [UnmanagedCallersOnly] private static int InvalidAnswers(void* self) => -7;
    [UnmanagedCallersOnly] private static byte IsValidAnswers(void* self, int i) => i >= 0 ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void AddOverridesRecords(void* self, byte* file, byte preload)
    {
        s_Sample = Marshal.PtrToStringUTF8((nint)file);
        s_UsePas = preload;
    }

    // ---- IEngineSoundServer003: the precache ----

    [UnmanagedCallersOnly]
    private static byte PrecacheRecords(void* self, byte* sample, byte preload, byte ui)
    {
        s_Sample = Marshal.PtrToStringUTF8((nint)sample);
        (s_UsePas, s_UpdatePositions) = (preload, ui);
        return 1;
    }

    [Fact]
    public void PrecacheSoundPassesPreloadAndIsNeverAUiSound()
    {
        EngineSoundFwd* fwd = Zeroed<EngineSoundFwd>();
        fwd->PrecacheSound = &PrecacheRecords;

        Assert.True(new EngineSoundServer(Bytes(8), fwd).PrecacheSound("doors/move1.wav", true));
        Assert.Equal(("doors/move1.wav", (byte)1, (byte)0), (s_Sample, s_UsePas, s_UpdatePositions));
    }

    // ---- IBaseFileSystem / IFileSystem: the precache log ----

    private static string? s_Written;
    private static nint s_WrittenTo;
    private static int s_ClosedCount;

    [UnmanagedCallersOnly]
    private static int WriteRecords(void* self, void* bytes, int size, void* handle)
    {
        s_Written = Marshal.PtrToStringUTF8((nint)bytes, size);
        s_WrittenTo = (nint)handle;
        return size;
    }

    [UnmanagedCallersOnly]
    private static void CloseCounts(void* self, void* handle) => s_ClosedCount++;

    [UnmanagedCallersOnly]
    private static void CreateDirRecords(void* self, byte* path, byte* pathId)
        => s_Sample = Marshal.PtrToStringUTF8((nint)path) + "|" + Marshal.PtrToStringUTF8((nint)pathId);

    private IFwdBaseFileSystem BaseFiles(BaseFileSystemFwd* fwd)
    {
        ConstructorInfo ctor = typeof(EngineBaseFileSystem).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        ParameterInfo[] parameters = ctor.GetParameters();
        return (IFwdBaseFileSystem)ctor.Invoke([Pointer.Box(Bytes(8), parameters[0].ParameterType),
                                                   Pointer.Box(fwd, parameters[1].ParameterType)]);
    }

    [Fact]
    public void WriteHandsTheBytesTheirLengthAndTheHandle()
    {
        BaseFileSystemFwd* fwd = Zeroed<BaseFileSystemFwd>();
        fwd->Write = &WriteRecords;

        int written = BaseFiles(fwd).Write("\"Doors.Move1\"\n"u8, 0x55);

        Assert.Equal((14, "\"Doors.Move1\"\n", (nint)0x55), (written, s_Written, s_WrittenTo));
    }

    [Fact]
    public void NothingIsWrittenToOrClosedOnTheInvalidHandle()
    {
        BaseFileSystemFwd* fwd = Zeroed<BaseFileSystemFwd>();
        fwd->Write = &WriteRecords;
        fwd->Close = &CloseCounts;
        s_Written = null;
        s_ClosedCount = 0;

        IFwdBaseFileSystem files = BaseFiles(fwd);
        Assert.Equal(0, files.Write("x"u8, 0));
        files.Close(0);
        files.Close(0x55);

        Assert.Equal((null, 1), (s_Written, s_ClosedCount));
    }

    [Fact]
    public void CreateDirHierarchyPassesThePathAndItsPathId()
    {
        FileSystemFwd* fwd = Zeroed<FileSystemFwd>();
        fwd->CreateDirHierarchy = &CreateDirRecords;

        new EngineFileSystemServer(Bytes(8), fwd).CreateDirHierarchy("reslists", "DEFAULT_WRITE_PATH");

        Assert.Equal("reslists|DEFAULT_WRITE_PATH", s_Sample);
    }

    [Fact]
    public void TheInternalLayoutIsTheHeadersPackedOne()
        => Assert.Equal((36, 0, 16, 30, 4),
                        (SoundParametersInternalLayout.SizeInBytes, SoundParametersInternalLayout.SoundNamesOffset,
                         SoundParametersInternalLayout.SoundNameCountOffset, SoundParametersInternalLayout.ChannelOffset,
                         SoundParametersInternalLayout.SoundFileSize));

    // ---- IVEngineServer: the PAS ----

    private static IFwdEngineServer Server(EngineServerFwd* fwd, void* self)
    {
        ConstructorInfo ctor = typeof(EngineServer).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        ParameterInfo[] parameters = ctor.GetParameters();
        return (IFwdEngineServer)ctor.Invoke([Pointer.Box(self, parameters[0].ParameterType),
                                                 Pointer.Box(fwd, parameters[1].ParameterType),
                                                 Pointer.Box(null, parameters[2].ParameterType)]);
    }

    [Fact]
    public void MulticastRecipientsClearsTheWordsThenLetsTheEngineSetThem()
    {
        EngineServerFwd* fwd = Zeroed<EngineServerFwd>();
        fwd->Message_DetermineMulticastRecipients = &Multicast;
        Span<uint> bits = stackalloc uint[IFwdEngineServer.PlayerBitWords];
        bits.Fill(0xFFFFFFFF);

        Server(fwd, Bytes(8)).MulticastRecipients(true, new Vector(4, 5, 6), bits);

        Assert.Equal((byte)1, s_UsePas);
        Assert.Equal(new Vector(4, 5, 6), s_Origin);
        Assert.Equal(new uint[] { 0, 1u << 5, 0, 0, 0, 0, 0, 0 }, bits.ToArray());
    }

    [Fact]
    public void MulticastRecipientsRefusesFewerWordsThanTheBitVec()
    {
        EngineServerFwd* fwd = Zeroed<EngineServerFwd>();
        fwd->Message_DetermineMulticastRecipients = &Multicast;
        var server = Server(fwd, Bytes(8));

        Assert.Throws<ArgumentException>(() => server.MulticastRecipients(true, default, new uint[7]));
    }
}
