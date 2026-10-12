using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE ENTRIES THAT MOVED ONTO AN ENGINE BINDING -- plan_native_api.md Phase 3.
//
// Six flat ABI entries stopped going through the god-class and started calling
// a bound engine interface. A span check cannot see any of the ways that goes
// wrong, and that is the whole reason these facts exist:
//
//   * A SLOT OF THE SAME ARITY, wired to its neighbour. GetModelFrameCount and
//     GetModelType both take a model pointer and both answer an int. Nothing
//     faults; the answer is a reinterpretation.
//
//   * A SEMANTIC THAT LIVED IN THE SHELL BODY. -1 for a missing file and never
//     0; -1 for an index that names no model; ModelType.Bad where -1 is not
//     available at the type. Every one of those was a decision made in C++, and
//     moving the call without moving the decision is a conversion that compiles,
//     binds, and answers the wrong thing quietly.
//
// So each converted entry is driven through the IMPL CLASS over a vtable this
// file builds, with the neighbour slots filled with DIFFERENT answers -- so a
// member reaching the wrong one is visible rather than plausible.
//=============================================================================
public unsafe class ConvertedEntryTests
{
    //-------------------------------------------------------------------------
    // IVModelInfo: the by-index pair.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which slot a generated layout puts a method in, read from the struct
    /// itself.
    ///
    /// <para>
    /// <b>DERIVED, NOT TYPED, and the first draft of this file typed them.</b>
    /// It put GetModelType at slot 8; it is at 10. The fake table's slot 10 was
    /// null, the call went through it, and the test host crashed -- which reads
    /// as a broken runtime rather than as a wrong number in a test. Reading the
    /// offset means the fixture cannot disagree with the layout it is standing
    /// in for.
    /// </para>
    ///
    /// <para>
    /// It is NOT a second source for the slot, and does not pretend to be: the
    /// struct is the thing under test on the production side too. What holds
    /// the struct against the compiler is
    /// <c>vtable_index_agreement_test.cpp</c>, natively, on the committed file.
    /// </para>
    /// </summary>
    private static int SlotOf<TVtbl>(string field)
        => (int)Marshal.OffsetOf(typeof(TVtbl), field) / sizeof(void*);

    private static readonly int GetModelSlot = SlotOf<ModelInfoVtbl>("GetModel");
    private static readonly int GetModelTypeSlot = SlotOf<ModelInfoVtbl>("GetModelType");
    private static readonly int GetModelFrameCountSlot =
        SlotOf<ModelInfoVtbl>("GetModelFrameCount");

    private static nint s_ModelForIndex;

    // ANSWERS FOR 7 AND FOR 0, AND THE 0 IS THE POINT.
    //
    // A seam that answered null for index 0 would make the non-positive guard
    // untestable: with the guard removed the call would reach GetModel, get
    // null, and answer -1 anyway -- the same answer, for the wrong reason, and
    // the mutation arm for that guard stayed GREEN until this line changed.
    // A real engine's GetModel is not obliged to answer null for 0 either;
    // index 0 is the world's own slot.
    [UnmanagedCallersOnly]
    private static void* GetModelAnswers(void* self, int index)
        => index is 7 or 0 ? (void*)s_ModelForIndex : null;

    [UnmanagedCallersOnly]
    private static int GetModelTypeAnswers(void* self, void* model) => 3;   // mod_studio

    [UnmanagedCallersOnly]
    private static int GetModelFrameCountAnswers(void* self, void* model) => 42;

    private static void* FakeModelInfo(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed(
            (nuint)Vtable.SlotsIn<ModelInfoVtbl>(), (nuint)sizeof(void*));

        table[GetModelSlot] = (delegate* unmanaged<void*, int, void*>)&GetModelAnswers;
        table[GetModelTypeSlot] = (delegate* unmanaged<void*, void*, int>)&GetModelTypeAnswers;
        table[GetModelFrameCountSlot] =
            (delegate* unmanaged<void*, void*, int>)&GetModelFrameCountAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    private static IVtableModelInfo ModelInfoOver(void* self, void** table)
        => (IVtableModelInfo)NewImpl(typeof(EngineModelInfo), self, table);

    /// <summary>
    /// An impl class over a table this test built. The constructors are private
    /// and stay private — production reaches them through <c>Acquire</c> and
    /// nothing else — so this is reflection, which is a thing only a test may
    /// do.
    /// </summary>
    private static object NewImpl(Type impl, void* self, void** table)
    {
        ConstructorInfo ctor = impl
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();

        return ctor.Invoke(new object?[]
        {
            Pointer.Box(self, typeof(void*)),
            Pointer.Box(table, typeof(void**)),
        });
    }

    [Fact]
    public void FrameCountAtResolvesTheIndexAndAsksTheFrameCountSlot()
    {
        s_ModelForIndex = 0x1234;

        void* self = FakeModelInfo(out void** table);
        IVtableModelInfo info = ModelInfoOver(self, table);

        // 42 and not 3: the frame-count slot and not the type slot beside it.
        Assert.Equal(42, info.FrameCountAt(7));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void FrameCountAtAnswersMinusOneForAnIndexThatNamesNoModel()
    {
        // THE SHELL BODY'S OWN RULE, moved rather than reinvented: -1 and NEVER
        // 0, because 0 is a real frame count. shell_worldquery.cpp made this
        // decision in C++ and the conversion has to bring it along.
        s_ModelForIndex = 0x1234;

        void* self = FakeModelInfo(out void** table);
        IVtableModelInfo info = ModelInfoOver(self, table);

        Assert.Equal(-1, info.FrameCountAt(9));   // GetModel answers null

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void FrameCountAtRefusesANonPositiveIndexWithoutAskingTheEngine()
    {
        // Index 0 is the world's own slot and negative is not an index at all.
        // Refused BEFORE GetModel, which is what the shell body did -- asking
        // the engine about a nonsense index is a call nobody needs to make.
        s_ModelForIndex = 0x1234;

        void* self = FakeModelInfo(out void** table);
        IVtableModelInfo info = ModelInfoOver(self, table);

        Assert.Equal(-1, info.FrameCountAt(0));
        Assert.Equal(-1, info.FrameCountAt(-3));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void TypeAtResolvesTheIndexAndAsksTheTypeSlot()
    {
        s_ModelForIndex = 0x1234;

        void* self = FakeModelInfo(out void** table);
        IVtableModelInfo info = ModelInfoOver(self, table);

        // Studio and not 42-reinterpreted: the type slot and not the frame
        // count beside it. Both take a model pointer and both answer an int,
        // which is exactly the misbinding a span check cannot see.
        Assert.Equal(ModelType.Studio, info.TypeAt(7));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void TypeAtAnswersBadForAnIndexThatNamesNoModel()
    {
        // -1 IS NOT AVAILABLE AT THIS TYPE and that is stated rather than
        // worked around: mod_bad is zero, so "no such model" and "that model is
        // broken" are the same answer. A caller that needs to tell them apart
        // asks FrameCountAt, which can.
        s_ModelForIndex = 0x1234;

        void* self = FakeModelInfo(out void** table);
        IVtableModelInfo info = ModelInfoOver(self, table);

        Assert.Equal(ModelType.Bad, info.TypeAt(9));

        // AND 0 IS REFUSED BY THE GUARD RATHER THAN BY THE ENGINE. The seam
        // answers a model for index 0 on purpose, so a TypeAt that dropped its
        // guard would come back Studio here instead of Bad.
        Assert.Equal(ModelType.Bad, info.TypeAt(0));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // IVEngineServer: the three reads.
    //-------------------------------------------------------------------------

    private static readonly int IsDedicatedServerSlot =
        SlotOf<EngineServerVtbl>("IsDedicatedServer");
    private static readonly int GetEntityCountSlot = SlotOf<EngineServerVtbl>("GetEntityCount");
    private static readonly int GetAreaSlot = SlotOf<EngineServerVtbl>("GetArea");

    private static float s_AreaSawX;

    [UnmanagedCallersOnly]
    private static byte IsDedicatedServerAnswers(void* self) => 1;

    [UnmanagedCallersOnly]
    private static int GetEntityCountAnswers(void* self) => 473;

    [UnmanagedCallersOnly]
    private static int GetAreaAnswers(void* self, void* origin)
    {
        s_AreaSawX = ((float*)origin)[0];
        return 255;
    }

    private static readonly int GetClientSteamIdSlot = SlotOf<EngineServerVtbl>("GetClientSteamIDByPlayerIndex");
    private static readonly int GetPlayerNetInfoSlot = SlotOf<EngineServerVtbl>("GetPlayerNetInfo");
    private static readonly int GetAddressSlot = SlotOf<NetChannelInfoVtbl>("GetAddress");

    // Slot 3 holds a client: its SteamID and a network channel. Every other
    // slot is empty, and the engine answers null for it.
    private const int OccupiedSlot = 3;
    private static readonly ulong* s_SteamId = (ulong*)NativeMemory.AllocZeroed(1, sizeof(ulong));
    private static readonly byte* s_Address = Utf8Z("203.0.113.7:27005");
    private static readonly void** s_Channel = Channel();

    private static byte* Utf8Z(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    private static void** ChannelTable()
    {
        var t = (void**)NativeMemory.AllocZeroed((nuint)Vtable.SlotsIn<NetChannelInfoVtbl>(), (nuint)sizeof(void*));
        t[GetAddressSlot] = (delegate* unmanaged<void*, byte*>)&GetAddressAnswers;
        return t;
    }

    private static void** Channel()
    {
        var c = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *c = ChannelTable();
        return c;
    }

    [UnmanagedCallersOnly]
    private static byte* GetAddressAnswers(void* self) => self == s_Channel ? s_Address : null;

    [UnmanagedCallersOnly]
    private static void* GetClientSteamIdAnswers(void* self, int entIndex) => entIndex == OccupiedSlot ? s_SteamId : null;

    [UnmanagedCallersOnly]
    private static void* GetPlayerNetInfoAnswers(void* self, int entIndex) => entIndex == OccupiedSlot ? s_Channel : null;

    private static void* FakeEngineServer(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed(
            (nuint)Vtable.SlotsIn<EngineServerVtbl>(), (nuint)sizeof(void*));

        table[IsDedicatedServerSlot] = (delegate* unmanaged<void*, byte>)&IsDedicatedServerAnswers;
        table[GetEntityCountSlot] = (delegate* unmanaged<void*, int>)&GetEntityCountAnswers;
        table[GetAreaSlot] = (delegate* unmanaged<void*, void*, int>)&GetAreaAnswers;
        table[GetClientSteamIdSlot] = (delegate* unmanaged<void*, int, void*>)&GetClientSteamIdAnswers;
        table[GetPlayerNetInfoSlot] = (delegate* unmanaged<void*, int, void*>)&GetPlayerNetInfoAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    private static IVtableEngineServer ServerOver(void* self, void** table)
        => (IVtableEngineServer)NewImpl(typeof(EngineServer), self, table);

    [Fact]
    public void ClientSteamIdReadsTheCSteamIdAsOneWordAndZeroForAnEmptySlot()
    {
        *s_SteamId = 76561197960287930UL;
        void* self = FakeEngineServer(out void** table);
        var server = ServerOver(self, table);

        Assert.Equal(76561197960287930UL, server.ClientSteamId(OccupiedSlot));
        Assert.Equal(0UL, server.ClientSteamId(OccupiedSlot + 1));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void ClientAddressCallsGetAddressOnTheChannelTheEngineHandsBack()
    {
        void* self = FakeEngineServer(out void** table);
        var server = ServerOver(self, table);

        Assert.Equal("203.0.113.7:27005", server.ClientAddress(OccupiedSlot));
        Assert.Null(server.ClientAddress(OccupiedSlot + 1));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // IVEngineClient::ClientCmd, reached only by Reconnect with a fixed text
    // (plan_host_sdk: a server-asked reconnect).
    //-------------------------------------------------------------------------

    private static readonly int ClientCmdSlot = SlotOf<EngineClientVtbl>("ClientCmd");
    private static string? s_ClientCmdSaw;

    [UnmanagedCallersOnly]
    private static void ClientCmdRecords(void* self, byte* command) =>
        s_ClientCmdSaw = Marshal.PtrToStringUTF8((IntPtr)command);

    [Fact]
    public void ReconnectSendsExactlyRetryThroughSlotSeven()
    {
        var table = (void**)NativeMemory.AllocZeroed((nuint)Vtable.SlotsIn<EngineClientVtbl>(), (nuint)sizeof(void*));
        table[ClientCmdSlot] = (delegate* unmanaged<void*, byte*, void>)&ClientCmdRecords;
        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;
        s_ClientCmdSaw = null;

        var client = (IVtableEngineClient)typeof(EngineClient)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .Invoke([new EngineSide(Side.Client), Pointer.Box(self, typeof(void*)), Pointer.Box(table, typeof(void**))]);
        client.Reconnect();

        Assert.Equal(7, ClientCmdSlot);
        Assert.Equal("retry", s_ClientCmdSaw);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void IsDedicatedServerReadsSlotTwoAsABooleanByte()
    {
        void* self = FakeEngineServer(out void** table);

        Assert.True(ServerOver(self, table).IsDedicatedServer());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void EntityCountReadsSlotSeventeen()
    {
        void* self = FakeEngineServer(out void** table);

        Assert.Equal(473, ServerOver(self, table).EntityCount());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void GetAreaPassesTheOriginByAddressAndPassesTheEnginesOwn255Through()
    {
        // TWO THINGS AT ONCE, and both were decisions in the shell body.
        //
        // `const Vector &` is a POINTER at the ABI, so the three floats have to
        // reach the callee by address and in order -- the seam records what it
        // saw. And 255 is the engine's own answer, reserved further down the
        // pipe to mean "no sky camera": softening it here would leave the
        // managed side unable to say so.
        s_AreaSawX = 0f;

        void* self = FakeEngineServer(out void** table);

        int area = ServerOver(self, table).GetArea(new Vector(11f, 22f, 33f));

        Assert.Equal(255, area);
        Assert.Equal(11f, s_AreaSawX);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // IBaseFileSystem: the -1-for-missing rule, which is the row Appendix B
    // calls the instructive one.
    //-------------------------------------------------------------------------

    private static readonly int FileExistsSlot = SlotOf<BaseFileSystemVtbl>("FileExists");

    // Size_2 AND NOT Size. IBaseFileSystem declares Size twice -- once taking a
    // FileHandle_t and once taking a filename -- and the generated struct
    // spells the second one Size_2, in that order. A name-keyed table would
    // hand a filename to the handle overload.
    private static readonly int SizeByNameSlot = SlotOf<BaseFileSystemVtbl>("Size_2");

    private static byte s_Exists;

    [UnmanagedCallersOnly]
    private static byte FileExistsAnswers(void* self, byte* name, byte* path) => s_Exists;

    [UnmanagedCallersOnly]
    private static uint SizeByNameAnswers(void* self, byte* name, byte* path) => 0;

    private static void* FakeFileSystem(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed(
            (nuint)Vtable.SlotsIn<BaseFileSystemVtbl>(), (nuint)sizeof(void*));

        table[FileExistsSlot] = (delegate* unmanaged<void*, byte*, byte*, byte>)&FileExistsAnswers;
        table[SizeByNameSlot] = (delegate* unmanaged<void*, byte*, byte*, uint>)&SizeByNameAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    private static IVtableBaseFileSystem FilesOver(void* self, void** table)
        => (IVtableBaseFileSystem)NewImpl(typeof(EngineBaseFileSystem), self, table);

    [Fact]
    public void FileExistsReadsSlotTenAndNotTheSizeSlotBesideIt()
    {
        s_Exists = 1;

        void* self = FakeFileSystem(out void** table);

        Assert.True(FilesOver(self, table).FileExists("gameinfo.txt", "GAME"));

        s_Exists = 0;
        Assert.False(FilesOver(self, table).FileExists("nothing.txt", "GAME"));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AnEmptyFileAndAMissingFileAreDistinguishableThroughThePair()
    {
        // THE ROW APPENDIX B CALLS INSTRUCTIVE, and the reason it is: Size
        // answers 0 for BOTH, which is why shell_files.cpp went through
        // Open/Size/Close and why -1 and 0 are different answers in that file's
        // own words. The seam answers 0 from the size slot either way, so this
        // fact fails outright if the exists question is dropped.
        void* self = FakeFileSystem(out void** table);
        IVtableBaseFileSystem files = FilesOver(self, table);

        s_Exists = 1;
        Assert.True(files.FileExists("empty.txt", "GAME"));
        Assert.Equal(0u, files.FileSize("empty.txt", "GAME"));

        s_Exists = 0;
        Assert.False(files.FileExists("absent.txt", "GAME"));
        Assert.Equal(0u, files.FileSize("absent.txt", "GAME"));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // THE AREA MEMBER ITSELF, not only the impl pair under it.
    //-------------------------------------------------------------------------

    [Fact]
    public void GameFileSizeAnswersMinusOneForAMissingFileAndZeroForAnEmptyOne()
    {
        // THE FACT THE IMPL-PAIR ONE COULD NOT BE. A mutation arm that dropped
        // the exists question from IEngineFiles.GameFileSize stayed GREEN
        // against a fact written on IVtableBaseFileSystem, because that fact
        // never reached this member -- so the arm measured nothing at all,
        // which is worse than a red.
        //
        // The seam answers 0 from the size slot either way, exactly as the
        // engine's filesystem does. Only the exists question can tell the two
        // apart, and only this member asks it.
        void* self = FakeFileSystem(out void** table);
        var files = new EngineFiles(new EngineSide(Side.Server), FilesOver(self, table));

        s_Exists = 1;
        Assert.Equal(0, files.GameFileSize("empty.txt"));

        s_Exists = 0;
        Assert.Equal(-1, files.GameFileSize("absent.txt"));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // AND THE SURFACE RULES THAT SURVIVED THE CONVERSION.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEngineServerStillOffersNothingThatChangesTheWorld()
    {
        // Three READS were exposed on this impl by Phase 3 and no setter was.
        // EngineLightStyle is ruled "C -- converts now" by the plan and is NOT
        // converted, because converting it means this class stops being a
        // read-only window and that is the owner's call. The row stays on the
        // flat ABI; EngineServer.cs says so where the method would have been.
        string[] offered = typeof(EngineServer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("LightStyle", offered);
        Assert.Contains("IsDedicatedServer", offered);
        Assert.Contains("EntityCount", offered);
        Assert.Contains("GetArea", offered);
    }

    [Fact]
    public void TheFileSystemStillOffersNoHandleAndNothingThatWrites()
    {
        // FileExists is neither: it opens nothing and writes nothing, which is
        // why it could be exposed where Open, Read and Close could not -- and
        // why FilesReadGameFile did NOT convert with the size slot beside it.
        string[] offered = typeof(EngineBaseFileSystem)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        foreach (string forbidden in new[]
                 { "Open", "Close", "Read", "Write", "WriteFile", "ReadFile",
                   "SetFileWritable", "UnzipFile" })
        {
            Assert.DoesNotContain(forbidden, offered);
        }

        Assert.Contains("FileExists", offered);
    }
}
