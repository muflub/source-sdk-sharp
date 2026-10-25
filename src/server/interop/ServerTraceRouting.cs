using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Installs the server's traces on its side (plan_server_native_v2 T4f): the
/// cost meters, and the managed tracer over libss_fwd_server's trace filter.
/// </summary>
public static class ServerTraceRouting
{
    /// <summary>
    /// Builds the server's tracer and routes the side's traces to it: the
    /// trace rule over the engine blocks and the server's model info, its sink
    /// in a managed block (returned: the caller keeps it alive for the load),
    /// the native <c>ITraceFilter</c> libss_fwd_server builds in that block,
    /// and EngineTraceServer003. Null, with the reason, when libss_fwd_server
    /// serves no trace filter; the side then traces nothing.
    /// </summary>
    public static ManagedVtable? Build(ModuleEngine engine, IServiceLocator services, ManagedVtables vtables,
                                       uint[] collisionRows, IFwdModelInfo modelInfo, IFwdEngineTrace trace,
                                       out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(vtables);

        TraceFilterObject? filters = TraceFilterObject.Acquire(engine, out why);
        if (filters is null)
            return null;

        EngineSide side = engine.Api;
        INetworkServerArea network = side.Attachment.Areas.Require<INetworkServerArea>();
        var candidates = new EngineTraceCandidates(h => side.GetEntityBlock(network, h), side.EntityGetIndex,
                                                   modelInfo);
        var sink = new TraceFilterSink(TraceFilterRules.Over(candidates, collisionRows), engine.Crossings);
        ManagedVtable block = TraceFilterSink.Create(vtables, sink);
        nint nativeFilter = filters.Build(block, TraceFilterRules.TraceEverything);

        services.Require<SideRoute<IServerTraceRoute>>().Fill(new ServerTracer(trace, sink, filters, nativeFilter));
        return block;
    }

    /// <summary>The side traces nothing any more: its tracer's block is about to be freed.</summary>
    public static void Unroute(IServiceLocator services)
        => services.Require<SideRoute<IServerTraceRoute>>().Fill(null);

    /// <summary>The side's traces timed into <paramref name="cost"/>.</summary>
    public static void RouteCost(IServiceLocator services, ServerTraceCost cost)
        => services.Require<SideRoute<ServerTraceCost>>().Fill(cost ?? throw new ArgumentNullException(nameof(cost)));
}
