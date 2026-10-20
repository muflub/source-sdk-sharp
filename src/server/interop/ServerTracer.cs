using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// THE SERVER'S TRACES, IN MANAGED CODE (plan_server_native_v2 T4f). Where
/// shell_worldquery.cpp built a <c>CTraceFilterSimple</c> and called
/// <c>UTIL_TraceLine</c> / <c>UTIL_TraceHull</c>, this builds the
/// <c>Ray_t</c>, names the trace's filter to the managed sink, and calls
/// <c>IEngineTrace::TraceRay</c> through its forwarder with libss_fwd's
/// <c>ITraceFilter</c> as the filter: the engine asks the sink about each
/// candidate (<see cref="TraceFilterRules"/>) and writes the trace.
/// </summary>
public sealed class ServerTracer : IServerTraceRoute
{
    private readonly IVtableEngineTrace _trace;
    private readonly TraceFilterSink _sink;
    private readonly ITraceEntityHandles _handles;
    private readonly nint _filter;
    private readonly Stack<TraceFilterSink.Scope> _entered = new();

    /// <param name="trace">EngineTraceServer003 through its forwarders.</param>
    /// <param name="sink">The managed side of <paramref name="filter"/>.</param>
    /// <param name="handles">What turns a trace's <c>m_pEnt</c> into a handle.</param>
    /// <param name="filter">The <c>ITraceFilter *</c> libss_fwd built over the sink's block.</param>
    public ServerTracer(IVtableEngineTrace trace, TraceFilterSink sink, ITraceEntityHandles handles, nint filter)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _filter = filter;
    }

    /// <summary><c>UTIL_TraceLine( start, end, mask, pass, collisionGroup, &amp;tr )</c>.</summary>
    public ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup)
        => Run(EngineRay.Line(start, end), mask, pass, collisionGroup);

    /// <summary>
    /// <c>UTIL_TraceHull( start, end, mins, maxs, mask, pass, collisionGroup, &amp;tr )</c>:
    /// the corners relative to the start.
    /// </summary>
    public ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask,
                            EntityHandle pass, int collisionGroup)
        => Run(EngineRay.Hull(start, end, mins, maxs), mask, pass, collisionGroup);

    /// <summary><c>enginetrace->GetPointContents( point )</c>.</summary>
    public int PointContents(Vector point) => _trace.PointContents(point);

    /// <inheritdoc/>
    public nint Enter(EntityHandle pass, int collisionGroup)
    {
        _entered.Push(_sink.Use(_sink.Rules.Resolve(pass, collisionGroup)));
        return _filter;
    }

    /// <inheritdoc/>
    public void Exit()
    {
        if (_entered.Count == 0)
            throw new InvalidOperationException("ServerTracer.Exit without an Enter: the trace filter's query would be left named");

        _entered.Pop().Dispose();
    }

    private ServerTrace Run(in EngineRay ray, uint mask, EntityHandle pass, int collisionGroup)
    {
        TraceFilterQuery query = _sink.Rules.Resolve(pass, collisionGroup);

        EngineGameTrace trace;
        using (_sink.Use(query))
            trace = _trace.TraceRay(ray, mask, _filter);

        return new ServerTrace(trace, _handles.HandleOf(trace.Entity));
    }
}

/// <summary>
/// <see cref="ITraceCandidates"/> over the engine: a candidate's four words
/// are read from its engine block (one crossing), the world is the entity at
/// index 0, and the model table answers brush-ness.
/// </summary>
public sealed class EngineTraceCandidates : ITraceCandidates
{
    private static readonly int GroupOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.CollisionGroup));
    private static readonly int SolidOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType));
    private static readonly int ModelOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.ModelIndex));
    private static readonly int OwnerOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.OwnerHandle));
    private static readonly int Needed =
        Math.Max(Math.Max(GroupOffset, SolidOffset), Math.Max(ModelOffset, OwnerOffset)) + sizeof(int);

    private readonly Func<EntityHandle, Span<byte>> _block;
    private readonly Func<EntityHandle, int> _index;
    private readonly IVtableModelInfo _models;

    /// <param name="block">The entity's engine block (empty for anything without one).</param>
    /// <param name="index">The entity's edict index (0 for the world).</param>
    /// <param name="models">The server's model info.</param>
    public EngineTraceCandidates(Func<EntityHandle, Span<byte>> block, Func<EntityHandle, int> index, IVtableModelInfo models)
    {
        _block = block ?? throw new ArgumentNullException(nameof(block));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
    {
        Span<byte> block = _block(entity);
        if (block.Length < Needed)
        {
            candidate = default;
            return false;
        }

        candidate = new TraceCandidate(
            BitConverter.ToInt32(block[GroupOffset..]),
            (SolidType)BitConverter.ToInt32(block[SolidOffset..]),
            BitConverter.ToInt32(block[ModelOffset..]),
            new EntityHandle(BitConverter.ToUInt32(block[OwnerOffset..])));
        return true;
    }

    public bool IsWorld(EntityHandle entity) => entity != EntityHandle.None && _index(entity) == 0;

    public bool IsBrushModel(int modelIndex) => _models.TypeAt(modelIndex) == ModelType.Brush;
}
