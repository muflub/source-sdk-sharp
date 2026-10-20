using System;

namespace SourceSharp.Abi;

/// <summary>
/// The engine's trace interface, as far as managed asks it: what is at a point.
/// Each side binds its own object (EngineTraceServer003 on the server,
/// EngineTraceClient003 on the client): one declaration, two copies of the world.
/// </summary>
public interface IVtableEngineTrace
{
    /// <summary><c>GetPointContents( vecAbsPosition, NULL )</c>: the CONTENTS_* bits at a point.</summary>
    int PointContents(Vector point);

    /// <summary>
    /// <c>TraceRay( ray, fMask, pTraceFilter, pTrace )</c> (plan_server_native_v2
    /// T4f): the engine's trace, asking <paramref name="filter"/> (an
    /// <c>ITraceFilter *</c> libss_fwd built, or 0 for none) about each
    /// candidate entity. Returns the trace the engine wrote.
    /// </summary>
    EngineGameTrace TraceRay(in EngineRay ray, uint mask, nint filter);
}

/// <summary>
/// <see cref="IVtableEngineTrace"/> over EngineTraceFwd (2026-10-19), where the
/// PointContents shim slot made the same call on both sides
/// (UTIL_PointContents).
/// </summary>
public sealed unsafe class EngineTrace : IVtableEngineTrace
{
    public const string ServerInterfaceVersion = "EngineTraceServer003";
    public const string ClientInterfaceVersion = "EngineTraceClient003";

    private readonly void* _self;
    private readonly EngineTraceFwd* _fwd;

    private EngineTrace(void* self, EngineTraceFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>One side's trace interface, by that side's own name, or null with the reason.</summary>
    internal static IVtableEngineTrace? Acquire(NativeInterfaceFactory factory, Side side, out string why)
        => Forwarders.TryAcquire<EngineTraceFwd>(
               factory, SourceSharpFactoryType.AppSystem,
               side == Side.Client ? ClientInterfaceVersion : ServerInterfaceVersion,
               out void* self, out EngineTraceFwd* table, out why)
            ? new EngineTrace(self, table)
            : null;

    /// <inheritdoc/>
    public int PointContents(Vector point) => _fwd->GetPointContents(_self, &point, null);

    /// <inheritdoc/>
    /// <remarks>
    /// The ray is copied into a 16-aligned buffer first: the engine reads its
    /// vectors with aligned SIMD loads, and a managed local carries no such
    /// promise.
    /// </remarks>
    public EngineGameTrace TraceRay(in EngineRay ray, uint mask, nint filter)
    {
        byte* buffer = stackalloc byte[EngineRay.SizeOf + EngineRay.AlignOf];
        var aligned = (EngineRay*)(((nint)buffer + (EngineRay.AlignOf - 1)) & ~(nint)(EngineRay.AlignOf - 1));
        *aligned = ray;

        EngineGameTrace trace = default;
        _fwd->TraceRay(_self, aligned, mask, (void*)filter, &trace);
        return trace;
    }
}
