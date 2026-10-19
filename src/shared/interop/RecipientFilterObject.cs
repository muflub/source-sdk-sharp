namespace SourceSharp.Abi;

/// <summary>
/// <c>IRecipientFilter</c>'s managed side (plan_server_native_v2 T4a):
/// IEngineSound::EmitSound calls the filter it is handed through the filter's
/// own C++ vtable, so libss_fwd_&lt;side&gt; builds that object
/// (fwd_recipient_filter.cpp) and its four overrides call this table. Managed
/// decides who hears; the native object only answers the engine's questions.
/// </summary>
[ManagedVtable(typeof(ManagedRecipientFilterTable))]
public interface IRecipientFilterSink
{
    int IsReliable();

    int IsInitMessage();

    int GetRecipientCount();

    int GetRecipientIndex(int nSlot);
}

/// <summary>
/// The sink: the list of the emit in progress. ONE native filter serves every
/// emit, because the engine reads it only inside the EmitSound call it was
/// handed to; <see cref="Use"/> names the list for exactly that call and puts
/// back whatever was named before, so an emit inside an emit is answered
/// right too. Outside a call the filter is empty and unreliable.
/// </summary>
public sealed class RecipientFilterSink : IRecipientFilterSink
{
    private IRecipientList? _current;

    /// <summary>The list the engine is reading now, or null between emits.</summary>
    public IRecipientList? Current => _current;

    /// <summary>Answers from <paramref name="recipients"/> until the scope is disposed.</summary>
    public Scope Use(IRecipientList recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        var scope = new Scope(this, _current);
        _current = recipients;
        return scope;
    }

    public int IsReliable() => _current is { IsReliable: true } ? 1 : 0;

    public int IsInitMessage() => _current is { IsInitMessage: true } ? 1 : 0;

    public int GetRecipientCount() => _current?.Count ?? 0;

    public int GetRecipientIndex(int nSlot)
        => _current is { } list && nSlot >= 0 && nSlot < list.Count ? list.IndexAt(nSlot) : -1;

    /// <summary>The table the native filter calls, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IRecipientFilterSink target)
        => IRecipientFilterSink__ManagedVtable.Create(vtables, target);

    /// <summary>Puts the previous list back.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly RecipientFilterSink _sink;
        private readonly IRecipientList? _previous;

        internal Scope(RecipientFilterSink sink, IRecipientList? previous)
        {
            _sink = sink;
            _previous = previous;
        }

        public void Dispose() => _sink._current = _previous;
    }
}

/// <summary>
/// The native <c>IRecipientFilter</c> libss_fwd_&lt;side&gt; builds in a managed
/// block's reserved words (<c>SSFwd_RecipientFilter</c>), over the block, as
/// <see cref="PhysicsCallbackObjects"/> builds vphysics' handlers.
/// </summary>
public sealed unsafe class RecipientFilterObject
{
    private readonly RecipientFilterFwd* _fwd;

    internal RecipientFilterObject(RecipientFilterFwd* fwd) => _fwd = fwd;

    /// <summary>The side's builder, or null with the reason.</summary>
    public static RecipientFilterObject? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    internal static RecipientFilterObject? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire(factory, out RecipientFilterFwd* table, out why)
            ? new RecipientFilterObject(table)
            : null;

    /// <summary>
    /// The <c>IRecipientFilter *</c> built in <paramref name="block"/>'s
    /// reserved words over the block. Refuses a block that reserves fewer bytes
    /// than the library builds in, naming both.
    /// </summary>
    public nint Build(ManagedVtable block)
    {
        ArgumentNullException.ThrowIfNull(block);

        int needed = _fwd->SizeOf();
        int reserved = block.ReservedWords * sizeof(nint);
        if (needed > reserved)
            throw new InvalidOperationException(
                $"{block.Contract}: libss_fwd builds its recipient filter in {needed} byte(s) and the block "
                + $"reserves {reserved}: the library and this assembly were built from different headers");

        void* built = _fwd->Construct((nint*)block.Pointer + 1, block.Pointer);
        if (built == null)
            throw new InvalidOperationException($"{block.Contract}: libss_fwd built no recipient filter over the block");

        return (nint)built;
    }
}
