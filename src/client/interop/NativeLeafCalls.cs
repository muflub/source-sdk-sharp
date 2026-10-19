using SourceSharp.Abi;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="ILeafNative"/> over the client forwarder library's LeafSystem
/// table (src/fwd/native/fwd_leaf_system.cpp, tools/engine-forwarders' HAND
/// row): each batch one call, which loops over the renderables, boxes or
/// leaves it is handed. The engine's questions go to the engine client this
/// side's factory answers. Every call is counted (<see cref="Calls"/>, the L3
/// instrument) with the items it carried.
/// </summary>
public sealed unsafe class NativeLeafCalls : ILeafNative
{
    private readonly LeafSystemFwd* _fwd;
    private readonly void* _engine;

    private NativeLeafCalls(LeafSystemFwd* fwd, void* engine)
    {
        _fwd = fwd;
        _engine = engine;
    }

    /// <summary>The table and the engine client, or null with why.</summary>
    public static NativeLeafCalls? Acquire(ModuleEngine engine, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        EngineSide side = engine.Api;
        if (!Forwarders.TryAcquire(side.Interfaces, out LeafSystemFwd* table, out why))
            return null;
        if (!side.Interfaces.TryCreate(SourceSharpFactoryType.AppSystem, EngineClient.InterfaceVersion, out nint client, out string? error))
        {
            why = error ?? "no engine client";
            return null;
        }
        return new NativeLeafCalls(table, (void*)client);
    }

    /// <summary>The batches made and the items they carried, per kind, since the last reset.</summary>
    public LeafCallCounts Calls { get; } = new();

    public int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending)
    {
        Calls.Count(LeafCall.ComputeFxBlends, renderables.Length);
        fixed (nint* p = renderables)
        fixed (int* q = pending)
            return _fwd->ComputeFxBlends((void*)system, (void**)p, renderables.Length, q, pending.Length / LeafPending.Words);
    }

    public void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts)
    {
        if (what.Length < renderables.Length || facts.Length < renderables.Length * LeafFacts.Words)
            throw new ArgumentException("the facts batch's spans are shorter than its renderables");
        Calls.Count(LeafCall.Facts, renderables.Length);
        fixed (nint* p = renderables)
        fixed (int* w = what)
        fixed (int* f = facts)
            _fwd->Facts((void**)p, w, renderables.Length, f);
    }

    public void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers)
    {
        if (flags.Length < renderables.Length || answers.Length < renderables.Length)
            throw new ArgumentException("the receives batch's spans are shorter than its renderables");
        Calls.Count(LeafCall.Receives, renderables.Length);
        fixed (nint* p = renderables)
        fixed (int* f = flags)
        fixed (int* a = answers)
            _fwd->Receives((void**)p, f, renderables.Length, a);
    }

    public int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output)
    {
        Calls.Count(LeafCall.EnumerateLeaves, boxes.Length / 6);
        fixed (float* b = boxes)
        fixed (int* o = output)
            return _fwd->EnumerateLeaves(_engine, b, boxes.Length / 6, o, output.Length);
    }

    public void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas)
    {
        if (areas.Length < count)
            throw new ArgumentException("the areas batch's answer is shorter than its runs");
        Calls.Count(LeafCall.LeavesAreas, count);
        fixed (int* r = runs)
        fixed (int* a = areas)
            _fwd->LeavesAreas(_engine, r, count, a);
    }

    public void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers)
    {
        if (boxes.Length < 6 * programs.Length || answers.Length < programs.Length)
            throw new ArgumentException("the box batch's spans are shorter than its programs");
        Calls.Count(LeafCall.BoxQueries, programs.Length);
        fixed (float* b = boxes)
        fixed (int* p = programs)
        fixed (int* a = answers)
            _fwd->BoxQueries(_engine, b, p, programs.Length, a);
    }

    public void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models)
    {
        int total = 0;
        for (int i = 1; i < ranges.Length; i += 2)
            total += Math.Max(ranges[i], 0);
        if (models.Length < total)
            throw new ArgumentException("the detail batch's answer is shorter than its ranges");
        Calls.Count(LeafCall.DetailModels, total);
        fixed (int* r = ranges)
        fixed (nint* m = models)
            _fwd->DetailModels((void*)detailSystem, r, ranges.Length, (void**)m);
    }
}

/// <summary>The kinds of batch <see cref="NativeLeafCalls"/> makes.</summary>
public enum LeafCall
{
    ComputeFxBlends,
    Facts,
    Receives,
    EnumerateLeaves,
    LeavesAreas,
    BoxQueries,
    DetailModels,
}

/// <summary>Batches and items per <see cref="LeafCall"/> kind: the leaf system's outbound crossings.</summary>
public sealed class LeafCallCounts
{
    private readonly long[] _calls = new long[Enum.GetValues<LeafCall>().Length];
    private readonly long[] _items = new long[Enum.GetValues<LeafCall>().Length];

    public void Count(LeafCall kind, int items)
    {
        _calls[(int)kind]++;
        _items[(int)kind] += items;
    }

    public long Calls(LeafCall kind) => _calls[(int)kind];

    public long Items(LeafCall kind) => _items[(int)kind];

    public void Reset()
    {
        Array.Clear(_calls);
        Array.Clear(_items);
    }
}
