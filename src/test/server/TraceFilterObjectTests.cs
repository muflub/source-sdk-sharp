using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: the ITraceFilter IEngineTrace::TraceRay calls back
/// is libss_fwd_server's object, built in a managed block over the managed
/// sink with the trace type managed chose, and HandleOf names a trace's entity.
/// fwd_trace_filter_test drives the real native object; these drive the managed
/// half over a fake table.
/// </summary>
public sealed unsafe class TraceFilterObjectTests : IDisposable
{
    private const nint Marker = 0xF2;

    [UnmanagedCallersOnly] private static int SizeOf24() => 24;
    [UnmanagedCallersOnly] private static int SizeOf32() => 32;

    [UnmanagedCallersOnly]
    private static void* Construct(void* memory, void* target, int traceType)
    {
        if (memory == null || target == null)
            return null;
        ((nint*)memory)[0] = Marker;
        ((nint*)memory)[1] = (nint)target;
        ((nint*)memory)[2] = traceType;
        return memory;
    }

    [UnmanagedCallersOnly]
    private static void* ConstructNothing(void* memory, void* target, int traceType) => null;

    [UnmanagedCallersOnly]
    private static uint HandleOf(void* entity) => (uint)(nint)entity ^ 0x5A5Au;

    private readonly TraceFilterFwd* _table =
        (TraceFilterFwd*)NativeMemory.AllocZeroed((nuint)sizeof(TraceFilterFwd));

    public TraceFilterObjectTests()
    {
        _table->SizeOf = &SizeOf24;
        _table->Construct = &Construct;
        _table->HandleOf = &HandleOf;
    }

    public void Dispose() => NativeMemory.Free(_table);

    private static TraceFilterSink Sink()
        => new(new TraceFilterRules(new NoCandidates(), new uint[TraceFilterRules.GroupCount]), new Crossings());

    private sealed class NoCandidates : ITraceCandidates
    {
        public bool TryRead(EntityHandle entity, out TraceCandidate candidate) { candidate = default; return false; }
        public bool IsWorld(EntityHandle entity) => false;
        public bool IsBrushModel(int modelIndex) => false;
    }

    [Fact]
    public void TheBlockReservesTheWordsTheFilterIsBuiltIn()
        => Assert.Equal(3, ITraceFilterSink__ManagedVtable.ReservedWords);

    [Fact]
    public void TheFilterIsBuiltInTheBlocksReservedWordsOverTheBlockWithTheTraceType()
    {
        using var vtables = new ManagedVtables();
        ManagedVtable block = TraceFilterSink.Create(vtables, Sink());

        nint built = new TraceFilterObject(_table).Build(block, 2);

        Assert.Equal((nint)block.Pointer + sizeof(nint), built);
        Assert.Equal((Marker, block.Address, (nint)2), (((nint*)built)[0], ((nint*)built)[1], ((nint*)built)[2]));
    }

    [Fact]
    public void ALibraryThatBuildsInMoreThanTheBlockReservesIsRefusedNamingBoth()
    {
        using var vtables = new ManagedVtables();
        _table->SizeOf = &SizeOf32;

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new TraceFilterObject(_table).Build(TraceFilterSink.Create(vtables, Sink()), 0));

        Assert.Contains("32 byte(s)", thrown.Message);
        Assert.Contains("reserves 24", thrown.Message);
    }

    [Fact]
    public void ALibraryThatBuildsNothingIsRefused()
    {
        using var vtables = new ManagedVtables();
        _table->Construct = &ConstructNothing;

        Assert.Throws<InvalidOperationException>(
            () => new TraceFilterObject(_table).Build(TraceFilterSink.Create(vtables, Sink()), 0));
    }

    [Fact]
    public void ATracesEntityIsNamedByTheLibrary()
        => Assert.Equal(new EntityHandle(0x1234u ^ 0x5A5Au), new TraceFilterObject(_table).HandleOf(0x1234));

    [Fact]
    public void ATraceThatNamesNoEntityIsNoHandleWithoutAsking()
    {
        _table->HandleOf = null;
        Assert.Equal(EntityHandle.None, new TraceFilterObject(_table).HandleOf(0));
    }
}
