namespace SourceSharp.Abi;

/// <summary>
/// The client's user-message dispatch (plan_side_isolation §2.2a). The engine
/// hands IBaseClientDLL an id and a bf_read; native copies the payload out as
/// one BitBlock and forwards both here, once; this maps the id to its declaration and gives the payload to the
/// managed handler. There are no native hooks: the stock HUD's HOOK_MESSAGE
/// sites are ported into the handlers (lane HC, 2026-10-04).
/// </summary>
public sealed class UserMessageReceiver : IUserMessageDispatch
{
    /// <summary>The largest payload copied for a managed handler; the engine's own limit is 255.</summary>
    public const int MaxPayloadBytes = 512;

    private readonly UserMessageRegistry _registry;
    private readonly ILog _log;
    private readonly IEngineStringTable _ids;
    private readonly List<string?> _keys = new();
    private readonly HashSet<int> _warnedIds = new();
    private readonly HashSet<string> _warnedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _arrived = new(StringComparer.Ordinal);

    /// <param name="ids">
    /// The client's copy of the server's SsUserMessages table
    /// (<see cref="UserMessageTable"/>): the key at index <i>i</i> is the key of
    /// engine id <i>i</i>. The ids are the server's; this side's own order
    /// means nothing on the wire.
    /// </param>
    public UserMessageReceiver(UserMessageRegistry registry, IEngineStringTable ids, ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    /// <summary>
    /// The key engine id <paramref name="id"/> travels under, from the table,
    /// or null when the table holds no such id. Read into a map the first time
    /// an id is past it, and read again after <see cref="Forget"/>.
    /// </summary>
    public string? KeyOf(int id)
    {
        if (id < 0)
            return null;

        if (id >= _keys.Count)
        {
            int count = _ids.Count;
            for (int i = _keys.Count; i < count; i++)
                _keys.Add(_ids.GetString(i));
        }

        return id < _keys.Count ? _keys[id] : null;
    }

    /// <summary>
    /// Drops the id map: the next server (or the next map's table) is read
    /// afresh. Called at the client's level shutdown.
    /// </summary>
    public void Forget()
    {
        _keys.Clear();
        _warnedIds.Clear();
        _warnedKeys.Clear();
        _arrived.Clear();
    }

    /// <summary>
    /// Dispatches the engine's message <paramref name="id"/>, its payload
    /// copied out of the engine's bf_read as one <see cref="BitBlock"/> (user
    /// ruling 2026-10-12: one crossing a message, the whole buffer at once):
    /// the table names its key, the key its declaration. False for an id the
    /// table does not hold or a key this client does not declare, which the
    /// engine reports; the id comes off the wire, so it is not trusted. A
    /// negative prefix is native's "too large to copy": minus the block size
    /// it would have needed.
    /// </summary>
    public bool Dispatch(int id, ReadOnlySpan<byte> block)
    {
        string? key = KeyOf(id);
        if (key is null)
        {
            if (_warnedIds.Add(id))
                _log.Warning($"[SourceSharp] user message id {id} is not in the server's " +
                            $"{_ids.Name} table ({_keys.Count} key(s)); dropped\n");
            return false;
        }

        var declaration = _registry.ByKey(key);
        if (declaration is null)
        {
            if (_warnedKeys.Add(key))
                _log.Warning($"[SourceSharp] user message '{key}' (id {id}) is not one this client " +
                            "declares; its messages are dropped\n");
            return false;
        }

        // Counted here, the client's live path, for the dev API's received
        // counts: the registry's own Dispatch is not what the engine calls.
        declaration.NoteReceived();

        if (declaration.Handler is { } handler)
            RunHandler(declaration, handler, block);

        // Once per key per level: where each message went, which is the one
        // readout that shows the id map and the handler at work on a live
        // client.
        if (_arrived.Add(key))
            _log.Msg($"[SourceSharp] user message '{key}' first arrived as id {id}: " +
                    $"{(declaration.Handler is null ? "no managed handler" : "managed handler")}\n");

        return true;
    }

    private void RunHandler(UserMessageDeclaration declaration, IUserMessage handler, ReadOnlySpan<byte> block)
    {
        int bits = BitBlock.Bits(block);
        ReadOnlySpan<byte> payload = BitBlock.Payload(block);
        if (bits < 0 || (bits + 7) / 8 > payload.Length)
        {
            int length = bits < 0 ? -bits - BitBlock.HeaderBytes : (bits + 7) / 8;
            _log.Warning($"[SourceSharp] user message '{declaration.Name}' is {length} bytes, over the " +
                        $"{payload.Length}-byte dispatch limit; its managed handler was skipped\n");
            return;
        }

        // A message ending mid-byte still carries that byte.
        Run(declaration, handler, payload[..((bits + 7) / 8)]);
    }

    private void Run(UserMessageDeclaration declaration, IUserMessage handler, ReadOnlySpan<byte> payload)
    {
        // A handler that throws is said, not propagated into the engine's
        // dispatch.
        try
        {
            if (!handler.OnReceived(new UserMessageReader(payload)))
                _log.Warning($"[SourceSharp] user message '{declaration.Name}' ended mid-member or was malformed; not dispatched\n");
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp] user message '{declaration.Name}' handler threw: {e}\n");
        }
    }
}
