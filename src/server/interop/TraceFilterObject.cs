using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// <c>ITraceFilter</c>'s managed side (plan_server_native_v2 T4f):
/// IEngineTrace::TraceRay calls the filter it is handed through the filter's
/// own C++ vtable, once per candidate entity, so libss_fwd_server builds that
/// object (fwd_trace_filter.cpp) and its ShouldHitEntity calls this table.
/// Managed decides what a trace may hit; the native object hands it the
/// candidate's handle, and answers GetTraceType with the type managed chose
/// when it built the object.
/// </summary>
[ManagedVtable(typeof(ManagedTraceFilterTable))]
public interface ITraceFilterSink
{
    int ShouldHitEntity(uint hEntity, int nContentsMask);
}

/// <summary>The handle of an entity a trace names (its <c>m_pEnt</c>).</summary>
public interface ITraceEntityHandles
{
    /// <summary>The entity's handle, or <see cref="EntityHandle.None"/> for a NULL <c>m_pEnt</c>.</summary>
    EntityHandle HandleOf(nint entity);
}

/// <summary>
/// The native <c>ITraceFilter</c> libss_fwd_server builds in a managed block's
/// reserved words (<c>SSFwd_TraceFilter</c>), over the block, as
/// <see cref="RecipientFilterObject"/> builds the recipient filter; and its
/// <c>HandleOf</c>, which asks an engine entity for its handle.
/// </summary>
public sealed unsafe class TraceFilterObject : ITraceEntityHandles
{
    private readonly TraceFilterFwd* _fwd;

    internal TraceFilterObject(TraceFilterFwd* fwd) => _fwd = fwd;

    /// <summary>The server's builder, or null with the reason.</summary>
    public static TraceFilterObject? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    internal static TraceFilterObject? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire(factory, out TraceFilterFwd* table, out why)
            ? new TraceFilterObject(table)
            : null;

    /// <summary>
    /// The <c>ITraceFilter *</c> built in <paramref name="block"/>'s reserved
    /// words over the block, answering <c>GetTraceType</c> with
    /// <paramref name="traceType"/> (a <c>TraceType_t</c>). Refuses a block
    /// that reserves fewer bytes than the library builds in, naming both.
    /// </summary>
    public nint Build(ManagedVtable block, int traceType)
    {
        ArgumentNullException.ThrowIfNull(block);

        int needed = _fwd->SizeOf();
        int reserved = block.ReservedWords * sizeof(nint);
        if (needed > reserved)
            throw new InvalidOperationException(
                $"{block.Contract}: libss_fwd builds its trace filter in {needed} byte(s) and the block "
                + $"reserves {reserved}: the library and this assembly were built from different headers");

        void* built = _fwd->Construct((nint*)block.Pointer + 1, block.Pointer, traceType);
        if (built == null)
            throw new InvalidOperationException($"{block.Contract}: libss_fwd built no trace filter over the block");

        return (nint)built;
    }

    /// <inheritdoc/>
    public EntityHandle HandleOf(nint entity)
        => entity == 0 ? EntityHandle.None : new EntityHandle(_fwd->HandleOf((void*)entity));
}
