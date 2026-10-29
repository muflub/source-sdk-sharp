using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// One trace's answer, whole: where it stopped, how far along, what it hit,
/// and whether it started inside something. The world query's
/// <see cref="HullResult"/> keeps only the last two; a caller that places
/// something where a box stopped needs the rest.
/// </summary>
public readonly record struct TraceAnswer(float Fraction, Vector End, EntityHandle Entity, bool StartSolid)
{
    /// <summary>Stopped short of the end, or started in something (stock's DidHit).</summary>
    public bool Hit => Fraction < 1f || StartSolid;
}

/// <summary>
/// <c>UTIL_TraceLine</c> and <c>UTIL_TraceHull</c> with the engine's own
/// <c>MASK_*</c> and a <c>CTraceFilterSimple( pass, COLLISION_GROUP_NONE )</c>,
/// answering the whole trace. For game code whose mask the world query's
/// <see cref="TraceMask"/> cannot spell (<c>MASK_SHOT | CONTENTS_GRATE</c>).
/// </summary>
public interface ITraceQueries
{
    /// <summary>A ray. A side that traces nothing answers "clear" (fraction 1, no entity).</summary>
    TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass);

    /// <summary>A swept box, <paramref name="mins"/> and <paramref name="maxs"/> relative to the ray.</summary>
    TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass);
}

/// <summary>
/// <see cref="ITraceQueries"/> over the side's tracer (ServerTraceRouting),
/// asked per call: the tracer is built after the side composes.
/// </summary>
public sealed class EngineTraceQueries(SideRoute<IServerTraceRoute> tracer) : ITraceQueries
{
    // COLLISION_GROUP_NONE.
    private const int CollisionGroupNone = 0;

    /// <inheritdoc/>
    public TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass)
        => tracer.Target is { } route
               ? Answer(route.Line(start, end, mask, pass, CollisionGroupNone))
               : new TraceAnswer(1f, end, EntityHandle.None, false);

    /// <inheritdoc/>
    public TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass)
        => tracer.Target is { } route
               ? Answer(route.Hull(start, end, mins, maxs, mask, pass, CollisionGroupNone))
               : new TraceAnswer(1f, end, EntityHandle.None, false);

    private static TraceAnswer Answer(in ServerTrace t)
        => new(t.Trace.Fraction, t.Trace.EndPos, t.Hit, t.Trace.StartSolid != 0);
}
