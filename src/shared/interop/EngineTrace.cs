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
}
