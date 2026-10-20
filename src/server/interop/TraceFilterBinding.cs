using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The sink: the filter of the trace in progress. ONE native filter serves
/// every trace, because the engine asks it only inside the TraceRay call it
/// was handed to; <see cref="Use"/> names the query for exactly that call and
/// puts back whatever was named before, so a trace inside a trace is answered
/// right too. Outside a trace nothing is hit.
/// </summary>
public sealed class TraceFilterSink(TraceFilterRules rules, Crossings crossings) : ITraceFilterSink
{
    private readonly TraceFilterRules _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    private TraceFilterQuery? _current;

    /// <summary>The rule every candidate is put to.</summary>
    public TraceFilterRules Rules => _rules;

    /// <summary>The query the engine is asking about now, or null between traces.</summary>
    public TraceFilterQuery? Current => _current;

    /// <summary>Answers for <paramref name="query"/> until the scope is disposed.</summary>
    public Scope Use(in TraceFilterQuery query)
    {
        var scope = new Scope(this, _current);
        _current = query;
        return scope;
    }

    public int ShouldHitEntity(uint hEntity, int nContentsMask)
    {
        crossings.Count(CrossingOp.ServerTraceFilterShouldHitEntity);
        return _current is { } query && _rules.ShouldHitEntity(query, hEntity, nContentsMask) ? 1 : 0;
    }

    /// <summary>The table the native filter calls, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, ITraceFilterSink target)
        => ITraceFilterSink__ManagedVtable.Create(vtables, target);

    /// <summary>Puts the previous query back.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly TraceFilterSink _sink;
        private readonly TraceFilterQuery? _previous;

        internal Scope(TraceFilterSink sink, TraceFilterQuery? previous)
        {
            _sink = sink;
            _previous = previous;
        }

        public void Dispose() => _sink._current = _previous;
    }
}
