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
    /// Which slot a generated table puts a method in, read from the struct
    /// itself. Every engine interface is called through its forwarders
    /// (plan_engine_forwarders.md), so the table an impl reads is its
    /// <c>*Fwd</c> struct: plain fields, field N at slot N on every ABI.
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
    /// <c>fwd_engine_test.cpp</c>, natively: each forwarder reaches the method
    /// it names through the compiler's own slot.
    /// </para>
    /// </summary>
    // A forwarder's slot: its field's offset in the generated struct.
    private static int ForwarderSlotOf<TFwd>(string field) where TFwd : unmanaged
        => (int)(Marshal.OffsetOf<TFwd>(field) / sizeof(void*));

    // A zeroed forwarder table the size of TFwd, for a fact to fill.
    private static void** ForwarderTable<TFwd>() where TFwd : unmanaged, IForwarderTable
        => (void**)NativeMemory.AllocZeroed((nuint)Forwarders.SlotCount<TFwd>(), (nuint)sizeof(void*));

    private static readonly int GetModelSlot = ForwarderSlotOf<ModelInfoFwd>("GetModel");
    private static readonly int GetModelTypeSlot = ForwarderSlotOf<ModelInfoFwd>("GetModelType");
    private static readonly int GetModelFrameCountSlot = ForwarderSlotOf<ModelInfoFwd>("GetModelFrameCount");

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
        table = ForwarderTable<ModelInfoFwd>();

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
    private static object NewImpl(Type impl, void* self, void** table, params nint[] moreTables)
    {
        ConstructorInfo ctor = impl
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();
        ParameterInfo[] parameters = ctor.GetParameters();

        // Each pointer boxed as the parameter it is passed for: the object,
        // then its forwarders, then any other forwarder table the impl holds.
        var args = new object?[parameters.Length];
        args[0] = Pointer.Box(self, parameters[0].ParameterType);
        args[1] = Pointer.Box(table, parameters[1].ParameterType);
        for (int i = 0; i < moreTables.Length; i++)
            args[2 + i] = Pointer.Box((void*)moreTables[i], parameters[2 + i].ParameterType);

        return ctor.Invoke(args);
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
    // IVEngineClient::ClientCmd, reached only by Reconnect with a fixed text
    // (plan_host_sdk: a server-asked reconnect).
    //-------------------------------------------------------------------------

    private static readonly int ClientCmdSlot = ForwarderSlotOf<EngineClientFwd>("ClientCmd");
    private static string? s_ClientCmdSaw;

    [UnmanagedCallersOnly]
    private static void ClientCmdRecords(void* self, byte* command) =>
        s_ClientCmdSaw = Marshal.PtrToStringUTF8((IntPtr)command);

    [Fact]
    public void ReconnectSendsExactlyRetryThroughClientCmd()
    {
        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        s_ClientCmdSaw = null;
        try
        {
            // Acquired as the client acquires it, over a root that serves the
            // engine client and its table, and held as its interface.
            using var root = new FakeShimRoot()
                .Interface(EngineClient.InterfaceVersion, self)
                .Forwarders<EngineClientFwd>(out EngineClientFwd* table);
            ((nint*)table)[ClientCmdSlot] = (nint)(delegate* unmanaged<void*, byte*, void>)&ClientCmdRecords;
            var side = new EngineSide(Side.Client);
            Assert.True(side.AttachOwnShims(root.Bind(), out string attachWhy), attachWhy);

            IVtableEngineClient? client = EngineClient.Acquire(new ModuleEngine(side), out string why);
            Assert.True(client is not null, why);
            client.Reconnect();

            Assert.Equal("retry", s_ClientCmdSaw);
        }
        finally
        {
            NativeMemory.Free(self);
        }
    }

    //-------------------------------------------------------------------------
    // IBaseFileSystem: the -1-for-missing rule, which is the row Appendix B
    // calls the instructive one.
    //-------------------------------------------------------------------------

    private static readonly int FileExistsSlot = ForwarderSlotOf<BaseFileSystemFwd>("FileExists");

    // Size_2 AND NOT Size. IBaseFileSystem declares Size twice -- once taking a
    // FileHandle_t and once taking a filename -- and the generated struct
    // spells the second one Size_2, in that order. A name-keyed table would
    // hand a filename to the handle overload.
    private static readonly int SizeByNameSlot = ForwarderSlotOf<BaseFileSystemFwd>("Size_2");

    private static byte s_Exists;

    [UnmanagedCallersOnly]
    private static byte FileExistsAnswers(void* self, byte* name, byte* path) => s_Exists;

    [UnmanagedCallersOnly]
    private static uint SizeByNameAnswers(void* self, byte* name, byte* path) => 0;

    private static void* FakeFileSystem(out void** table)
    {
        table = ForwarderTable<BaseFileSystemFwd>();

        table[FileExistsSlot] = (delegate* unmanaged<void*, byte*, byte*, byte>)&FileExistsAnswers;
        table[SizeByNameSlot] = (delegate* unmanaged<void*, byte*, byte*, uint>)&SizeByNameAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    private static IVtableBaseFileSystem FilesOver(void* self, void** table)
        => (IVtableBaseFileSystem)NewImpl(typeof(EngineBaseFileSystem), self, table);

    [Fact]
    public void FileExistsCallsFileExistsAndNotTheSizeForwarder()
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
        // As the side registers it, over a root that serves the filesystem and
        // its forwarder table with the two slots this member asks.
        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        try
        {
            using var root = new FakeShimRoot()
                .Interface("VBaseFileSystem011", self)
                .Forwarders<BaseFileSystemFwd>(out BaseFileSystemFwd* table);
            table->FileExists = &FileExistsAnswers;
            table->Size_2 = &SizeByNameAnswers;
            var side = new EngineSide(Side.Client);
            Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
            var files = SideBindings.Of<IEngineFiles>(side);

            s_Exists = 1;
            Assert.Equal(0, files.GameFileSize("empty.txt"));

            s_Exists = 0;
            Assert.Equal(-1, files.GameFileSize("absent.txt"));
        }
        finally
        {
            NativeMemory.Free(self);
        }
    }

    //-------------------------------------------------------------------------
    // AND THE SURFACE RULES THAT SURVIVED THE CONVERSION.
    //-------------------------------------------------------------------------

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
