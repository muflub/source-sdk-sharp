namespace SourceSharp.Abi;

/// <summary>
/// Steam's <c>CCallbackBase</c>, in Valve's layout and written by managed
/// (client Steam, Decision 1a): one block per callback, which
/// <c>SteamAPI_RegisterCallback</c> files under its callback id and
/// <c>SteamAPI_RunCallbacks</c> (the engine's pump) calls. The block reserves
/// the word after its vptr for the flags and id Steam writes there.
/// </summary>
[ManagedVtable(typeof(SteamCallbackVtbl))]
public unsafe interface ISteamCallbackSink
{
    /// <summary><c>Run( void *pvParam )</c>: a callback arrived.</summary>
    void Run(void* pvParam);

    /// <summary><c>Run( void *pvParam, bool bIOFailure, SteamAPICall_t )</c>: a call result.</summary>
    void Run_2(void* pvParam, byte ioFailure, ulong apiCall);

    /// <summary><c>GetCallbackSizeBytes()</c>: the payload's size, which Steam checks against its own.</summary>
    int GetCallbackSizeBytes();
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
    /// Builds the block for callback <paramref name="callbackId"/> with a
    /// payload of <paramref name="payloadBytes"/> and registers it. False when
    /// there is no Steam in the process, or the id is already taken here.
    /// </summary>
    bool Register(string name, int callbackId, int payloadBytes, SteamCallbackHandler handler);

    /// <summary>The names registered and not yet unregistered.</summary>
    IReadOnlyCollection<string> Registered { get; }
}

/// <summary>
/// One registered callback: Run hands the payload to the handler, as
/// <c>CCallback::Run</c> calls its member function; the call-result overload
/// forwards to Run, as <c>CCallbackImpl</c>'s does.
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

    public void Run_2(void* pvParam, byte ioFailure, ulong apiCall) => Run(pvParam);

    public int GetCallbackSizeBytes() => payloadBytes;

    /// <summary>The native-callable table over <paramref name="target"/>, one named instance of the contract.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, ISteamCallbackSink target, string instance)
        => ISteamCallbackSink__ManagedVtable.Create(vtables, target, instance);
}

/// <summary>
/// <see cref="ISteamCallbacks"/> over the side's registry and the flat API.
/// Dispose unregisters every callback that Steam still holds, then frees its
/// block: the module's shutdown, which runs before a reload unloads the
/// context and before the final unload, so Steam never holds a block whose
/// target has gone and the reload never has to patch one.
/// </summary>
public sealed class SteamCallbacks(ManagedVtables vtables, ISteamClientApi steam, ILog log) : ISteamCallbacks
{
    /// <summary><c>CCallbackBase::k_ECallbackFlagsRegistered</c>, which SteamAPI_RegisterCallback sets.</summary>
    public const byte RegisteredFlag = 0x01;

    private readonly Dictionary<string, ManagedVtable> _blocks = new(StringComparer.Ordinal);
    private readonly HashSet<int> _ids = [];

    public IReadOnlyCollection<string> Registered => _blocks.Keys;

    public bool Register(string name, int callbackId, int payloadBytes, SteamCallbackHandler handler)
    {
        if (!steam.Available || _blocks.ContainsKey(name) || !_ids.Add(callbackId))
            return false;

        var block = SteamCallbackSink.Create(vtables, new SteamCallbackSink(payloadBytes, handler, log, name), name);
        _blocks.Add(name, block);
        steam.RegisterCallback(block.Address, callbackId);
        return true;
    }

    /// <summary>The flags byte Steam keeps in the block's reserved word (CCallbackBase::m_nCallbackFlags, at 8).</summary>
    public static unsafe byte FlagsOf(ManagedVtable block) => ((byte*)block.Pointer)[8];

    /// <summary>The callback id Steam keeps in the block's reserved word (CCallbackBase::m_iCallback, at 12).</summary>
    public static unsafe int CallbackIdOf(ManagedVtable block) => *(int*)((byte*)block.Pointer + 12);

    /// <summary>The block registered under <paramref name="name"/>, for the facts.</summary>
    public ManagedVtable? BlockOf(string name) => _blocks.GetValueOrDefault(name);

    public void Dispose()
    {
        foreach (var (name, block) in _blocks)
        {
            // CCallbackImpl's destructor: unregister only what Steam registered.
            if ((FlagsOf(block) & RegisteredFlag) != 0)
                steam.UnregisterCallback(block.Address);
            if (!block.Release())
                log.Warning($"[SourceSharp/Steam] {name}: its block was already freed\n");
        }
        _blocks.Clear();
        _ids.Clear();
    }
}
