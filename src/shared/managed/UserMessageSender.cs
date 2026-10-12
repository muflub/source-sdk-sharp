namespace SourceSharp.Abi;

/// <summary>
/// The server's user-message send (plan_side_isolation §2.2a): the key is
/// looked up in this side's registry, and the id and payload go to native
/// through IShimUserMessages, which builds the filter and hands the engine the
/// message. Every refusal is decided here and said here.
/// </summary>
public sealed class UserMessageSender : IUserMessageSender
{
    /// <summary>
    /// MAX_USER_MSG_DATA: the engine's buffer for one user message. A payload
    /// over it would overflow that buffer, so it is refused before it leaves.
    /// </summary>
    public const int MaxPayloadBytes = 255;

    private readonly UserMessageRegistry _registry;
    private readonly IUserMessageOutbox _engine;
    private readonly ILog _log;

    public UserMessageSender(UserMessageRegistry registry, IUserMessageOutbox engine, ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc/>
    public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
    {
        int id = _registry.IdOf(key);
        if (id < 0)
        {
            _log.Warning($"[SourceSharp] user message '{key}' is not registered; not sent\n");
            return;
        }

        if (payload.Length > MaxPayloadBytes)
        {
            _log.Warning($"[SourceSharp] user message '{key}' is {payload.Length} bytes, over the " +
                        $"{MaxPayloadBytes}-byte limit; not sent\n");
            return;
        }

        // A message aimed at one player who has since left is dropped rather
        // than broadcast, and says so: a silent drop is how a missing join
        // handshake went unseen.
        if (!_engine.Send(id, payload, player, reliable) && player > 0)
            _log.Warning($"[SourceSharp] user message '{key}' was aimed at player {player}, " +
                        "who is not in that slot; dropped rather than broadcast\n");
    }
}
