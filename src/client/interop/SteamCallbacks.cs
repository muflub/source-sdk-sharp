using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One Steam callback's managed side (client Steam, Decision 1a;
/// plan_engine_forwarders.md phase 5). libss_fwd_client builds the
/// <c>CCallbackBase</c> Steam registers, so the compiler, not managed, orders
/// its two <c>Run</c> overloads; both call this one slot. Its table is ours
/// (<c>IManagedSteamCallback</c>), the same on every ABI.
/// </summary>
[ManagedVtable(typeof(ManagedSteamCallbackVtbl))]
public unsafe interface ISteamCallbackSink
{
    /// <summary><c>Run( void *pvParam )</c>: a callback or a call result arrived.</summary>
    void Run(void* pvParam);
}

/// <summary>A callback's payload, as the bytes Steam handed over (<c>GetCallbackSizeBytes</c> of them).</summary>
public delegate void SteamCallbackHandler(ReadOnlySpan<byte> payload);

/// <summary>
/// Registers managed handlers as Steam callbacks: what stock's
/// <c>STEAM_CALLBACK</c> members did (a <c>CCallback</c> per handler,
/// registered in the owner's constructor, unregistered in its destructor).
/// </summary>
public interface ISteamCallbacks : IDisposable
{
    /// <summary>
    /// Builds the callback <paramref name="callbackId"/>, with a payload of
    /// <paramref name="payloadBytes"/>, and registers it. False when there is
    /// no Steam in the process, the id is already taken here, or the native
    /// callback could not be built.
    /// </summary>
    bool Register(string name, int callbackId, int payloadBytes, SteamCallbackHandler handler);

    /// <summary>The names registered and not yet unregistered.</summary>
    IReadOnlyCollection<string> Registered { get; }
}

/// <summary>
/// One registered callback: Run hands the payload to the handler, as
/// <c>CCallback::Run</c> calls its member function. The call-result overload
/// and the payload size Steam checks are the native callback's, which answers
/// both without calling here.
/// </summary>
public sealed unsafe class SteamCallbackSink(int payloadBytes, SteamCallbackHandler handler, ILog log, string name)
    : ISteamCallbackSink
{
    public void Run(void* pvParam)
    {
        if (pvParam == null)
        {
            log.Warning($"[SourceSharp/Steam] {name}: Steam ran the callback with no payload\n");
            return;
        }
        handler(new ReadOnlySpan<byte>(pvParam, payloadBytes));
    }

    /// <summary>The native-callable table over <paramref name="target"/>, one named instance of the contract.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, ISteamCallbackSink target, string instance)
        => ISteamCallbackSink__ManagedVtable.Create(vtables, target, instance);
}

/// <summary>
/// <see cref="ISteamCallbacks"/> over the side's registry, libss_fwd_client's
/// callback table and the flat API. Each registration is three things: the
/// managed block Steam's callback calls (<see cref="SteamCallbackSink"/>), the
/// native callback built over it in memory managed allocates (the library has
/// no heap), and Steam's registration of that callback. Dispose undoes them in
/// reverse: unregisters every callback Steam still holds, frees its memory,
/// then releases its block. It is the module's shutdown, which runs before a
/// reload unloads the context and before the final unload, so Steam never
/// holds a callback whose target has gone and the reload never has to patch one.
/// </summary>
public sealed unsafe class SteamCallbacks : ISteamCallbacks
{
    private sealed class Registration(ManagedVtable block, void* memory, void* callback)
    {
        public ManagedVtable Block { get; } = block;
        public void* Memory { get; } = memory;
        public void* Callback { get; } = callback;
    }

    private readonly ManagedVtables _vtables;
    private readonly ISteamClientApi _steam;
    private readonly ILog _log;
    private readonly SteamCallbackFwd* _fwd;
    private readonly Dictionary<string, Registration> _callbacks = new(StringComparer.Ordinal);
    private readonly HashSet<int> _ids = [];

    /// <summary>Over the callback table a fact or <see cref="Acquire"/> hands in.</summary>
    internal SteamCallbacks(ManagedVtables vtables, ISteamClientApi steam, ILog log, SteamCallbackFwd* fwd)
    {
        _vtables = vtables ?? throw new ArgumentNullException(nameof(vtables));
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _fwd = fwd != null ? fwd : throw new ArgumentNullException(nameof(fwd));
    }

    /// <summary>
    /// The callbacks over the table <paramref name="engine"/>'s interface
    /// factory serves (the client's, which links libss_fwd_client); null, with
    /// the reason, where it serves none.
    /// </summary>
    public static SteamCallbacks? Acquire(ModuleEngine engine, ManagedVtables vtables, ISteamClientApi steam, ILog log, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);

        return Forwarders.TryAcquire(engine.Api.Interfaces, out SteamCallbackFwd* fwd, out why)
            ? new SteamCallbacks(vtables, steam, log, fwd)
            : null;
    }

    public IReadOnlyCollection<string> Registered => _callbacks.Keys;

    public bool Register(string name, int callbackId, int payloadBytes, SteamCallbackHandler handler)
    {
        if (!_steam.Available || _callbacks.ContainsKey(name) || !_ids.Add(callbackId))
            return false;

        var block = SteamCallbackSink.Create(_vtables, new SteamCallbackSink(payloadBytes, handler, _log, name), name);
        void* memory = NativeMemory.Alloc((nuint)_fwd->SizeOf());
        void* callback = _fwd->Construct(memory, block.Pointer, payloadBytes);

        if (callback == null)
        {
            // Nothing was built, so there is nothing for Steam to hold.
            NativeMemory.Free(memory);
            block.Release();
            _ids.Remove(callbackId);
            _log.Warning($"[SourceSharp/Steam] {name}: the native callback was not built\n");
            return false;
        }

        _callbacks.Add(name, new Registration(block, memory, callback));
        _steam.RegisterCallback((nint)callback, callbackId);
        return true;
    }

    /// <summary>The <c>CCallbackBase *</c> Steam was handed for <paramref name="name"/>, for the facts.</summary>
    public nint CallbackOf(string name) => _callbacks.TryGetValue(name, out var r) ? (nint)r.Callback : 0;

    /// <summary>The managed block <paramref name="name"/>'s callback calls, for the facts.</summary>
    public ManagedVtable? BlockOf(string name) => _callbacks.GetValueOrDefault(name)?.Block;

    public void Dispose()
    {
        foreach (var (name, r) in _callbacks)
        {
            // CCallbackImpl's destructor: unregister only what Steam registered.
            if (_fwd->IsRegistered(r.Callback) != 0)
                _steam.UnregisterCallback((nint)r.Callback);
            NativeMemory.Free(r.Memory);
            if (!r.Block.Release())
                _log.Warning($"[SourceSharp/Steam] {name}: its block was already freed\n");
        }
        _callbacks.Clear();
        _ids.Clear();
    }
}
