using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4a: the IRecipientFilter IEngineSound::EmitSound
/// calls back is libss_fwd_server's object over managed's table, and the table
/// answers from the recipients the managed SoundEmitter chose for the emit in
/// progress. fwd_recipient_filter_test drives the real native object.
/// </summary>
public sealed unsafe class RecipientFilterObjectTests : IDisposable
{
    private sealed class List(bool reliable, bool init, params int[] indices) : IRecipientList
    {
        public bool IsReliable => reliable;
        public bool IsInitMessage => init;
        public int Count => indices.Length;
        public int IndexAt(int slot) => indices[slot];
    }

    private const nint Marker = 0xF1;

    [UnmanagedCallersOnly] private static int SizeOf16() => 16;
    [UnmanagedCallersOnly] private static int SizeOf32() => 32;

    [UnmanagedCallersOnly]
    private static void* Construct(void* memory, void* target)
    {
        if (memory == null || target == null)
            return null;
        ((nint*)memory)[0] = Marker;
        ((nint*)memory)[1] = (nint)target;
        return memory;
    }

    private readonly RecipientFilterFwd* _table =
        (RecipientFilterFwd*)NativeMemory.AllocZeroed((nuint)sizeof(RecipientFilterFwd));

    public RecipientFilterObjectTests()
    {
        _table->SizeOf = &SizeOf16;
        _table->Construct = &Construct;
    }

    public void Dispose() => NativeMemory.Free(_table);

    // ---- the sink ----

    [Fact]
    public void BetweenEmitsTheFilterIsEmptyAndUnreliable()
    {
        var sink = new RecipientFilterSink();

        Assert.Equal((0, 0, 0, -1), (sink.IsReliable(), sink.IsInitMessage(), sink.GetRecipientCount(), sink.GetRecipientIndex(0)));
    }

    [Fact]
    public void DuringAnEmitItAnswersTheChosenList()
    {
        var sink = new RecipientFilterSink();

        using (sink.Use(new List(true, false, 3, 1, 7)))
        {
            Assert.Equal((1, 0, 3), (sink.IsReliable(), sink.IsInitMessage(), sink.GetRecipientCount()));
            Assert.Equal((3, 1, 7), (sink.GetRecipientIndex(0), sink.GetRecipientIndex(1), sink.GetRecipientIndex(2)));
            Assert.Equal((-1, -1), (sink.GetRecipientIndex(3), sink.GetRecipientIndex(-1)));
        }

        Assert.Equal(0, sink.GetRecipientCount());
    }

    [Fact]
    public void AnEmitInsideAnEmitPutsTheOuterListBack()
    {
        var sink = new RecipientFilterSink();
        var outer = new List(false, true, 2);

        using (sink.Use(outer))
        {
            using (sink.Use(new List(true, false, 5, 6)))
                Assert.Equal(2, sink.GetRecipientCount());

            Assert.Same(outer, sink.Current);
            Assert.Equal((1, 2), (sink.IsInitMessage(), sink.GetRecipientIndex(0)));
        }
    }

    [Fact]
    public void TheTableTheNativeFilterCallsReachesTheSink()
    {
        using var vtables = new ManagedVtables();
        var sink = new RecipientFilterSink();
        ManagedVtable block = RecipientFilterSink.Create(vtables, sink);
        var table = *(ManagedRecipientFilterTable**)block.Pointer;

        using (sink.Use(new List(true, false, 4, 9)))
        {
            Assert.Equal(1, table->IsReliable(block.Pointer));
            Assert.Equal(2, table->GetRecipientCount(block.Pointer));
            Assert.Equal(9, table->GetRecipientIndex(block.Pointer, 1));
        }
    }

    // ---- the native object ----

    [Fact]
    public void TheBlockReservesTheWordsTheFilterIsBuiltIn()
        => Assert.Equal(3, IRecipientFilterSink__ManagedVtable.ReservedWords);   // three since the trace filter holds its type (T4f)

    [Fact]
    public void TheFilterIsBuiltInTheBlocksReservedWordsOverTheBlock()
    {
        using var vtables = new ManagedVtables();
        ManagedVtable block = RecipientFilterSink.Create(vtables, new RecipientFilterSink());

        nint built = new RecipientFilterObject(_table).Build(block);

        Assert.Equal((nint)block.Pointer + sizeof(nint), built);
        Assert.Equal((Marker, block.Address), (((nint*)built)[0], ((nint*)built)[1]));
    }

    [Fact]
    public void ALibraryThatBuildsInMoreThanTheBlockReservesIsRefusedNamingBoth()
    {
        using var vtables = new ManagedVtables();
        _table->SizeOf = &SizeOf32;

        var thrown = Assert.Throws<InvalidOperationException>(
            () => new RecipientFilterObject(_table).Build(RecipientFilterSink.Create(vtables, new RecipientFilterSink())));

        Assert.Contains("32 byte(s)", thrown.Message);
        Assert.Contains("reserves 24", thrown.Message);
    }
}
