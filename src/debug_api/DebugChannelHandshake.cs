using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;


namespace Debug.Api;

/// <summary>
/// The nonces the server has offered and not yet seen used.
///
/// <para>
/// ONE NONCE PER INVITATION, SINGLE USE, AND IT EXPIRES. A channel that accepted
/// any connection on its port would attach to whatever dialled it, which on a
/// dedicated server is anything that can reach the box. The nonce is what ties a
/// connection back to a client this server actually invited — it travels to that
/// client inside the game's own reliable message stream, over the connection it
/// already authenticated to join.
/// </para>
///
/// <para>
/// Bounded, because an invitation is minted per client per level and nothing
/// obliges a client to use one. Oldest first out, so a server that invites for
/// an hour holds a table the size of its player count rather than of its uptime.
/// </para>
/// </summary>
public sealed class DebugChannelNonces
{
    /// <summary>Long enough for a client to load a level and dial; short enough to matter.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Outstanding invitations kept. Past this the oldest is dropped.</summary>
    public const int MaxOutstanding = 32;

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _issued = new(StringComparer.Ordinal);

    // The player slot each nonce was minted for (plan_side_isolation ruling 12):
    // redeeming it ties the session to that slot, so the slot is never the
    // client's own claim. Kept in step with _issued.
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;

    /// <summary>The clock is injected so a test can expire one without waiting two minutes.</summary>
    public DebugChannelNonces(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>How many invitations are outstanding.</summary>
    public int Outstanding
    {
        get { lock (_lock) return _issued.Count; }
    }

    /// <summary>
    /// Mints one for the player in <paramref name="slot"/> (0: no slot). 128
    /// bits from the cryptographic generator, as lower-case hex.
    /// </summary>
    public string Mint(int slot = 0)
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        lock (_lock)
        {
            DropStale();

            while (_issued.Count >= MaxOutstanding)
            {
                string oldest = "";
                DateTime at = DateTime.MaxValue;

                foreach (var (key, issued) in _issued)
                {
                    if (issued >= at)
                        continue;

                    at = issued;
                    oldest = key;
                }

                _issued.Remove(oldest);
            }

            _issued[nonce] = _now();
            _slots[nonce] = slot;

            // Whatever left _issued (aged out, evicted) leaves here too.
            foreach (var key in _slots.Keys.Where(k => !_issued.ContainsKey(k)).ToList())
                _slots.Remove(key);
        }

        return nonce;
    }

    /// <summary>
    /// Spends one. False for a nonce this server never issued, for one already
    /// spent, and for one that has aged out — three different ways of not being
    /// an invited client, all of which end the same way.
    /// </summary>
    public bool Redeem(string? nonce) => Redeem(nonce, out _);

    /// <summary>
    /// Spends one, and says which player slot it was minted for. The slot is
    /// 0 when the redemption fails.
    /// </summary>
    public bool Redeem(string? nonce, out int slot)
    {
        slot = 0;

        if (!DebugChannelProtocol.IsWellFormedNonce(nonce))
            return false;

        lock (_lock)
        {
            DropStale();

            _slots.Remove(nonce!, out int minted);

            // Removed whether or not it was in time: a nonce that has been
            // presented is spent, which is what makes replaying one useless.
            if (!_issued.Remove(nonce!, out DateTime issued))
                return false;

            if (_now() - issued > Lifetime)
                return false;

            slot = minted;
            return true;
        }
    }

    /// <summary>
    /// Forgets every outstanding invitation.
    ///
    /// <para>
    /// CALLED AT LEVEL SHUTDOWN. An invitation names a channel that is about to
    /// stop having the level it was offered for; carrying it across would let a
    /// client attach on the strength of an offer made to the previous map.
    /// </para>
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _issued.Clear();
            _slots.Clear();
        }
    }

    private void DropStale()
    {
        DateTime now = _now();
        List<string>? stale = null;

        foreach (var (key, issued) in _issued)
        {
            if (now - issued <= Lifetime)
                continue;

            (stale ??= new List<string>()).Add(key);
        }

        if (stale is null)
            return;

        foreach (string key in stale)
            _issued.Remove(key);
    }
}

/// <summary>What the handshake decided, and the bytes to answer with.</summary>
/// <param name="Accepted">Whether to upgrade. False means write the response and close.</param>
/// <param name="Response">A complete HTTP response, headers and all.</param>
/// <param name="Reason">Why, for the log and for the status listing.</param>
public readonly record struct DebugChannelHandshakeResult(bool Accepted, string Response, string Reason);

/// <summary>
/// The WebSocket upgrade, decided as a pure function of the request text.
///
/// <para>
/// SEPARATED FROM THE SOCKET FOR THE REASON <c>HttpSocketServer.TryParse</c> is:
/// the decision is the half worth checking and it needs no socket to check, so a
/// test drives it with a literal request rather than by dialling a port and
/// hoping about timing.
/// </para>
///
/// <para>
/// THE NONCE IS CHECKED HERE, BEFORE ANY WEBSOCKET EXISTS. A refused connection
/// costs one parse and a 401; it never reaches the framing layer, never gets a
/// session, and never sees the mirror ring.
/// </para>
/// </summary>
public static class DebugChannelHandshake
{
    /// <summary>RFC 6455's magic string. Not a secret and not a check — it is the accept computation.</summary>
    private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>Longest upgrade request accepted. A handshake is a few hundred bytes.</summary>
    public const int MaxRequestBytes = 8192;

    /// <param name="resume">
    /// Redeems a <see cref="DebugChannelProtocol.ResumeHeader"/> value
    /// (<c>slot:token</c>). A request that carries that header is decided by it
    /// alone, never by a nonce. Null refuses every resume.
    /// </param>
    /// <param name="issue">
    /// After an accept: the next resume header value to hand this session, or
    /// null for none (a session for no player slot).
    /// </param>
    public static DebugChannelHandshakeResult Decide(string raw, Func<string?, bool> redeem,
                                                     Func<string, bool>? resume = null,
                                                     Func<string?>? issue = null)
    {
        int headerEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (headerEnd < 0)
            return Refuse(400, "the request had no header terminator");

        string[] lines = raw[..headerEnd].Split("\r\n");
        string[] requestLine = lines[0].Split(' ');

        if (requestLine.Length < 3 || !requestLine[0].Equals("GET", StringComparison.Ordinal))
            return Refuse(400, "not a GET");

        // One path, because there is nothing to route. A request for anything
        // else is not a confused client of this channel; it is something else
        // entirely that found the port.
        if (!requestLine[1].Equals(DebugChannelProtocol.Path, StringComparison.Ordinal))
            return Refuse(404, $"'{requestLine[1]}' is not this channel's path");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');

            if (colon <= 0)
                continue;

            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        if (!Header(headers, "Upgrade").Contains("websocket", StringComparison.OrdinalIgnoreCase))
            return Refuse(400, "no websocket upgrade asked for");

        if (!Header(headers, "Connection").Contains("upgrade", StringComparison.OrdinalIgnoreCase))
            return Refuse(400, "the connection header does not ask for an upgrade");

        if (Header(headers, "Sec-WebSocket-Version") != "13")
            return Refuse(400, "only websocket version 13 is spoken here");

        string key = Header(headers, "Sec-WebSocket-Key");

        if (key.Length == 0)
            return Refuse(400, "no Sec-WebSocket-Key");

        // GATE THREE. The client presents the nonce this server minted for it and
        // sent down the game's own reliable message stream. Anything else on this
        // port is refused here, before a WebSocket is negotiated.
        //
        // OR A RESUME: the client's managed module was reloaded and presents the
        // single-use token this listener handed its previous session, bound to
        // that session's slot (DebugChannelResume).
        string resuming = Header(headers, DebugChannelProtocol.ResumeHeader);

        if (resuming.Length > 0)
        {
            if (resume is null || !resume(resuming))
                return Refuse(401, "the resume token was unknown, already spent, or for another slot");
        }
        else if (!redeem(Header(headers, DebugChannelProtocol.NonceHeader) is { Length: > 0 } n ? n : null))
            return Refuse(401, "the nonce was absent, unknown, expired or already spent");

        string accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.UTF8.GetBytes(key + Guid)));

        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n" +
            (issue?.Invoke() is { } next ? $"{DebugChannelProtocol.ResumeHeader}: {next}\r\n" : "") +
            "\r\n";

        return new DebugChannelHandshakeResult(true, response, "accepted");
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out string? value) ? value : "";

    private static DebugChannelHandshakeResult Refuse(int status, string reason)
    {
        string body = $"{{\"error\":{DebugApiServer.Quote(reason)}}}";

        string response =
            $"HTTP/1.1 {status} {Reason(status)}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            "Connection: close\r\n" +
            "\r\n" +
            body;

        return new DebugChannelHandshakeResult(false, response, reason);
    }

    private static string Reason(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        _   => "Unknown",
    };
}

/// <summary>
/// What a status reader wants to know about the channel, as one snapshot.
///
/// <para>
/// A record rather than live properties so the JSON is built from one consistent
/// reading: the counters move on the sessions' own threads, and a report that
/// read each of them separately could say "no sessions" and "3 records this
/// second" in the same breath.
/// </para>
/// </summary>
public readonly record struct DebugChannelStatus(
    bool Listening, string Address, bool Public, int Port,
    int Outstanding, int Sessions, int Accepted, int Refused,
    long RecordsReceived, bool EverAttached);

/// <summary>Where a session's frames are put. Kept small so a test can stand in for the listener.</summary>
public interface IDebugChannelSink
{
    /// <summary>A batch of prediction records arrived from a client.</summary>
    void Prediction(IReadOnlyList<WirePredictionRecord> records);

    /// <summary>A client said hello, naming its protocol and its build.</summary>
    void Hello(int protocol, string build);

    /// <summary>A frame that could not be understood, dropped.</summary>
    void Malformed(string why);

    /// <summary>A client's answer to one query (plan_side_isolation §2.6).</summary>
    void Reply(int requestId, int status, string body);
}

/// <summary>
/// Routes one decoded frame to a sink.
///
/// <para>
/// Its own function, off the socket, so the whole "what does the server do with
/// what a client sends" question is answerable by a test with a byte array.
/// </para>
/// </summary>
public static class DebugChannelRouter
{
    public static void Route(ReadOnlySpan<byte> frame, IDebugChannelSink sink)
    {
        switch (DebugChannelCodec.KindOf(frame))
        {
            case DebugChannelMessage.Hello:
                if (DebugChannelCodec.TryReadHello(frame, out int protocol, out string build))
                    sink.Hello(protocol, build);
                else
                    sink.Malformed("a hello that did not parse");
                break;

            case DebugChannelMessage.Prediction:
                if (DebugChannelCodec.TryReadPredictionBatch(frame, out var records))
                    sink.Prediction(records);
                else
                    sink.Malformed("a prediction batch that did not parse");
                break;

            case DebugChannelMessage.Pong:
                break;

            case DebugChannelMessage.Reply:
                if (DebugChannelCodec.TryReadReply(frame, out int requestId, out int status, out string body))
                    sink.Reply(requestId, status, body);
                else
                    sink.Malformed("a reply that did not parse");
                break;

            // Like a command: a query travels server to client only.
            case DebugChannelMessage.Query:
                sink.Malformed("a query from a client, which travels the other way only");
                break;

            // A CLIENT DOES NOT SEND COMMANDS. Commands travel server to client
            // and only server to client; one arriving the other way is a client
            // trying to drive the server, which is not a thing this channel does.
            case DebugChannelMessage.Command:
                sink.Malformed("a command from a client, which travels the other way only");
                break;

            default:
                sink.Malformed("an unknown frame kind");
                break;
        }
    }
}

/// <summary>
/// A concurrent bag of live sessions, so teardown can find every one of them.
///
/// <para>
/// The point of it existing at all is <see cref="DebugChannelListener.Dispose"/>:
/// a session task still on the stack holds the server's collectible load context
/// alive, and a level change that left one running would leak a build. Nothing
/// here may be a static.
/// </para>
/// </summary>
internal sealed class DebugChannelSessions
{
    private readonly ConcurrentDictionary<int, Task> _tasks = new();
    private int _next;

    public int Count => _tasks.Count;

    /// <summary>
    /// Runs a session body and keeps its task until it finishes.
    ///
    /// <para>
    /// THE BODY IS HELD BEHIND A GATE UNTIL THE TASK IS IN THE TABLE, and that
    /// is not ceremony: a session that ran to completion before its task was
    /// stored would remove an id that had not been added yet, and the store
    /// afterwards would leave an entry for a session that had already ended —
    /// which is a teardown that waits two seconds for nothing, every time, and
    /// a Count that never returns to zero.
    /// </para>
    /// </summary>
    public int Start(Func<int, Task> body)
    {
        int id = Interlocked.Increment(ref _next);
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task task = Task.Run(async () =>
        {
            await registered.Task.ConfigureAwait(false);

            try { await body(id).ConfigureAwait(false); }
            finally { _tasks.TryRemove(id, out _); }
        });

        _tasks[id] = task;
        registered.SetResult();

        return id;
    }

    /// <summary>Waits for every session, and says whether they all stopped.</summary>
    public bool WaitAll(TimeSpan timeout)
    {
        var tasks = _tasks.Values.ToArray();

        return tasks.Length == 0 || Task.WaitAll(tasks, timeout);
    }
}

/// <summary>
/// The resume tokens one listener has handed out: at most one per player slot,
/// each single-use.
///
/// <para>
/// Issued in the 101 of every session for a player slot, and redeemed by that
/// client's reloaded module (<see cref="DebugChannelResume"/>). A redeemed
/// token is spent; the session it opens is handed the next one. The table is
/// the listener's own, so a token from a previous listener (before
/// <c>cs_sv_reload</c>) is unknown here and refused.
/// </para>
/// </summary>
public sealed class DebugChannelResumeTokens
{
    private readonly object _lock = new();
    private readonly Dictionary<int, string> _bySlot = new();

    /// <summary>Tokens waiting to be redeemed.</summary>
    public int Outstanding
    {
        get { lock (_lock) return _bySlot.Count; }
    }

    /// <summary>A new token for <paramref name="slot"/>, replacing any it had.</summary>
    public string Issue(int slot)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        lock (_lock)
            _bySlot[slot] = token;

        return token;
    }

    /// <summary>
    /// Redeems a <c>slot:token</c> header value: true once, for the token this
    /// table issued to that slot. A wrong token leaves the slot's real one
    /// standing, so a guess cannot spend it.
    /// </summary>
    public bool Redeem(string? header, out int slot)
    {
        if (!DebugChannelResume.TryReadHeader(header, out slot, out string token))
            return false;

        lock (_lock)
        {
            if (!_bySlot.TryGetValue(slot, out string? issued) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(issued), Encoding.ASCII.GetBytes(token)))
            {
                slot = 0;
                return false;
            }

            _bySlot.Remove(slot);
            return true;
        }
    }

    /// <summary>Every token, at level shutdown and disposal.</summary>
    public void Clear()
    {
        lock (_lock)
            _bySlot.Clear();
    }
}
