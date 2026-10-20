namespace SourceSharp.Abi;

/// <summary>A server trace: what the engine wrote, and the entity it names as a handle.</summary>
/// <param name="Trace">The engine's <c>trace_t</c>.</param>
/// <param name="Hit">Its <c>m_pEnt</c> as a handle (<see cref="EntityHandle.None"/> for NULL).</param>
public readonly record struct ServerTrace(EngineGameTrace Trace, EntityHandle Hit);

/// <summary>
/// The server side's traces (plan_server_native_v2 T4f): what its trace
/// members ask, answered by SourceSharp.Server.Interop's ServerTracer over
/// IEngineTrace::TraceRay and the managed trace filter. Declared here, beside
/// <see cref="IServerSoundRoute"/>, because the side's own half holds one.
/// </summary>
public interface IServerTraceRoute
{
    /// <summary><c>UTIL_TraceLine( start, end, mask, pass, collisionGroup, &amp;tr )</c>.</summary>
    ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup);

    /// <summary><c>UTIL_TraceHull</c>: the corners relative to the start.</summary>
    ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass, int collisionGroup);

    /// <summary><c>enginetrace->GetPointContents( point )</c>.</summary>
    int PointContents(Vector point);

    /// <summary>
    /// Opens a trace native carries out itself (the movement loop's hull,
    /// SourceSharp_MoveTraceThrough): names the filter's query --
    /// <c>CTraceFilterSimple( pass, collisionGroup )</c> -- and answers the
    /// <c>ITraceFilter *</c> native hands the engine. Every Enter is closed by
    /// one <see cref="Exit"/>, after the native call returns.
    /// </summary>
    nint Enter(EntityHandle pass, int collisionGroup);

    /// <summary>Closes the innermost <see cref="Enter"/>: the query before it answers again.</summary>
    void Exit();
}

/// <summary>
/// What the server's traces cost in the running game (plan_server_native_v2
/// T4f: "measure on dm_lockdown against the native filter before you delete
/// it"), one <see cref="CostMeter"/> per kind of trace the side makes, timed
/// around the whole managed call. <c>cs_sv_trace_cost</c> prints them.
/// </summary>
public sealed class ServerTraceCost
{
    /// <summary>TraceLine and TraceLineSurface.</summary>
    public CostMeter Line { get; } = new();

    /// <summary>TraceHull.</summary>
    public CostMeter Hull { get; } = new();

    /// <summary>EntityTraceHull (the player shadow's fit test, the push sweep).</summary>
    public CostMeter EntityHull { get; } = new();

    /// <summary>WaterSurfaceBelow (its point-contents ask and its trace).</summary>
    public CostMeter Water { get; } = new();

    /// <summary>Every meter, by the name the report prints.</summary>
    public IEnumerable<(string Name, CostMeter Meter)> Meters()
    {
        yield return ("trace.line", Line);
        yield return ("trace.hull", Hull);
        yield return ("trace.entity_hull", EntityHull);
        yield return ("trace.water", Water);
    }
}

/// <summary>
/// What the side's trace members answer, read off one <see cref="ServerTrace"/>
/// (plan_server_native_v2 T4f): shell_worldquery.cpp's rules for the outs it
/// wrote, which managed now reads itself.
/// </summary>
public static class ServerTraceResults
{
    /// <summary>
    /// Stopped short, or started inside something: an emitter buried in a wall
    /// has not got a clear line, and reporting otherwise would let it fire
    /// through.
    /// </summary>
    public static bool Stopped(in ServerTrace t) => t.Trace.Fraction < 1f || t.Trace.StartSolid != 0;

    /// <summary>TraceLine's answer: the entity of either hierarchy, whatever the trace named.</summary>
    public static TraceResult Line(in ServerTrace t)
        => new(Stopped(t), t.Trace.Fraction, t.Trace.EndPos, t.Trace.PlaneNormal, t.Hit);

    /// <summary>
    /// TraceLineSurface's answer. THE SURFACE ONLY WHEN THE LINE WAS STOPPED BY
    /// ONE (<c>trace_t::DidHit</c>): a trace that ran its whole length carries
    /// whatever surface the engine left there, and -1 (not 0, which is the real
    /// <c>default</c> surface) says "none".
    /// </summary>
    public static SurfaceTraceResult Surface(in ServerTrace t)
    {
        bool hit = t.Trace.DidHit;
        return new SurfaceTraceResult(
            Stopped(t), t.Trace.Fraction, t.Trace.EndPos, t.Trace.PlaneNormal, t.Hit,
            SurfaceProps: hit ? t.Trace.SurfaceProps : -1,
            SurfaceFlags: hit ? t.Trace.SurfaceFlags : 0,
            HitGroup: hit ? (HitGroup)t.Trace.HitGroup : HitGroup.Generic);
    }

    /// <summary>
    /// TraceHull's answer: startsolid separately, because a hull that STRUCK
    /// something names it and one that began inside something often names
    /// nothing.
    /// </summary>
    public static HullResult Hull(in ServerTrace t) => new(Stopped(t), t.Trace.StartSolid != 0, t.Hit);

    /// <summary>EntityTraceHull's answer: where the sweep ended, startsolid and allsolid.</summary>
    public static EntityHullTrace EntityHull(in ServerTrace t)
        => new(t.Trace.EndPos, t.Trace.StartSolid != 0, t.Trace.AllSolid != 0);
}
