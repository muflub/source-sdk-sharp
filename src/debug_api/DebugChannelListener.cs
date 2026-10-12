using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;


namespace Debug.Api;

/// <summary>
/// The server's half of the debug channel: it listens, and clients it invited
/// dial in.
///
/// <para>
/// <b>A SEPARATE LISTENER, AND THE MEASUREMENT IS IN <see cref="HttpSocketServer"/>
/// ITSELF.</b> That server's accept loop calls <c>Serve(client)</c> INLINE on its
/// one thread and closes the connection after replying — no keep-alive, no
/// pipelining, one connection at a time, by design and stated as such in its own
/// header. A WebSocket is the opposite shape: one connection held open for the
/// life of a session. Upgrading a connection on that loop parks the single
/// accept thread for as long as the channel lives, and every <c>/status</c>,
/// <c>/log</c> and <c>/command</c> after it waits behind it — the debugging tool
/// stops answering the moment the debugging channel is used. There is no version
/// of "extend the existing server" that does not first mean making it
/// concurrent, which is a rewrite of the thing every other endpoint depends on.
/// </para>
///
/// <para>
/// The second reason is the one the whole feature exists for. The dev API's
/// preferred endpoint is a UNIX SOCKET named for the process id, which is what
/// lets several game instances be driven at once — and which by construction
/// cannot be reached from another machine, or even from another user. A
/// dedicated server and a client on someone's desk need TCP. So: TCP, its own
/// thread, its own accept loop, loopback unless somebody said otherwise.
/// </para>
///
/// <para>
/// <b>GATE TWO IS <see cref="RequestedBy"/>.</b> The server opts in for itself,
/// with its own flag, independently of any client. Off by default. A server that
/// has not opted in has no listener, mints no nonces, and
/// <see cref="Invite"/> answers null — so it never invites anybody.
/// </para>
/// </summary>
public sealed class DebugChannelListener : IDisposable
{
    /// <summary>Sessions at once. A debugging channel does not need a crowd.</summary>
    public const int MaxSessions = 8;

    /// <summary>How long <see cref="Dispose"/> waits for the accept thread and the sessions.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(2);

    private readonly Socket _listener;
    private readonly DebugChannelNonces _nonces;
    private readonly DebugChannelResumeTokens _resumes = new();
    private readonly DebugChannelSessions _sessions = new();
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly CancellationTokenSource _cancel = new();

    /// <summary>The live sockets, so <see cref="Ask"/> has something to ask.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, WebSocket> _open = new();

    /// <summary>One send at a time, which is what a WebSocket allows.</summary>
    private readonly SemaphoreSlim _sending = new(1, 1);

    // Which player slot each open session belongs to, from the nonce it
    // redeemed (plan_side_isolation ruling 12), and the queries waiting on a
    // reply, by request id.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _slotOf = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<(int Status, string Body)>> _waiting = new();
    private int _nextRequest;

    private volatile bool _running;
    private Thread? _thread;

    private int _accepted;
    private int _refused;
    private long _recordsReceived;
    private volatile bool _everAttached;

    /// <summary>
    /// WHERE A REMOTE CLIENT'S PREDICTION RING LANDS.
    ///
    /// <para>
    /// A second ring, not the one in <see cref="PredictionErrors"/>. That one is
    /// the ring of a client IN THIS PROCESS, and on a listen server it is the
    /// right and only answer — the direct path, which this channel must not
    /// disturb. This is the ring of a client that is somewhere else, and keeping
    /// them apart is what lets <c>/prediction</c> prefer the local one and never
    /// mix two clients' records into one report.
    /// </para>
    /// </summary>
    public PredictionMirror Mirror { get; } = new();

    /// <summary>Where it is listening, for the log and the status listing.</summary>
    public string Address { get; }

    /// <summary>
    /// The port an invitation names.
    ///
    /// <para>
    /// READ BACK OFF THE BOUND SOCKET rather than kept from the argument, so
    /// port 0 works and means "any free one". That is what a test binds, and a
    /// test that had to pick a fixed number would fail whenever anything else on
    /// the machine happened to hold it.
    /// </para>
    /// </summary>
    public int Port { get; }

    /// <summary>Whether the bind went beyond loopback, which needs its own flag.</summary>
    public bool Public { get; }

    /// <summary>True once any client has completed a handshake. Distinguishes "quiet" from "nobody".</summary>
    public bool EverAttached => _everAttached;

    /// <summary>
    /// Whether the last <see cref="Dispose"/> saw the accept thread and every
    /// session actually come OFF the stack inside their timeout.
    ///
    /// <para>
    /// NOT THE SAME QUESTION AS "IS THE PORT FREE". Closing the socket releases
    /// the port whether or not the thread that was parked in <c>Accept</c> ever
    /// returned, so a rebind succeeding says nothing about the join — and a
    /// thread still on the stack is what keeps this side's collectible load
    /// context from unloading. This is what the joins actually returned.
    /// </para>
    /// </summary>
    public bool StoppedCleanly { get; private set; }

    /// <summary>GATE TWO. Whether this server opted in. Off unless the flag is on the server's own command line.</summary>
    public static bool RequestedBy(IReadOnlyList<string> args)
        => DebugCommandLine.Has(args, DebugChannelProtocol.ServerParam);

    /// <summary>The port this server would listen on, from its own command line.</summary>
    /// <remarks>
    /// 0 asks for any free port: the listener reports the one it bound and
    /// every invitation carries it, so games sharing a machine (the live gate's
    /// concurrent runs) need not agree on a number.
    /// </remarks>
    public static int PortFor(IReadOnlyList<string> args)
        => int.TryParse(DebugCommandLine.Value(args, DebugChannelProtocol.ServerPortParam, ""),
                        out int asked) && asked is >= 0 and < 65536
            ? asked
            : DebugChannelProtocol.DefaultPort;

    /// <summary>
    /// LOOPBACK UNLESS ASKED OTHERWISE, and the asking is its own flag.
    ///
    /// <para>
    /// A development channel bound to every interface is reachable by anything
    /// that can route to the box. That is a real and sometimes necessary
    /// configuration — a dedicated server with the developer's client on another
    /// machine is the case this whole feature exists for — but it is a different
    /// decision from turning the channel on, so it is a different flag. Nobody
    /// gets it by typing <c>-debuguiserve</c>.
    /// </para>
    /// </summary>
    public static bool PublicFor(IReadOnlyList<string> args)
        => DebugCommandLine.Has(args, DebugChannelProtocol.ServerPublicParam);

    public DebugChannelListener(int port, bool @public, Action<string> log, Action<string> warn,
                                DebugChannelNonces? nonces = null)
    {
        _log = log;
        _warn = warn;
        _nonces = nonces ?? new DebugChannelNonces();

        Public = @public;

        IPAddress address = @public ? IPAddress.Any : IPAddress.Loopback;

        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(address, port));
        _listener.Listen(backlog: MaxSessions);

        Port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
        Address = $"ws://{address}:{Port}{DebugChannelProtocol.Path}";
    }

    /// <summary>Starts accepting, on its own thread for <see cref="HttpSocketServer"/>'s reason.</summary>
    public void Start()
    {
        _running = true;

        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "SourceSharp DebugChannel",
        };

        _thread.Start();
    }

    /// <summary>
    /// Mints an invitation for one joining client.
    ///
    /// <para>
    /// <b>WHAT IT DOES NOT CONTAIN IS THE POINT.</b> A protocol number, a port
    /// and a nonce. No host, no address, and structurally no room for one — see
    /// <see cref="DebugChannelInvitation.TryRead"/>. The client dials the server
    /// it already joined; this only tells it which port and proves the offer
    /// came from here.
    /// </para>
    /// </summary>
    public DebugChannelInvitation? Invite(int slot = 0)
        => _running
            ? new DebugChannelInvitation(DebugChannelProtocol.Version, Port, _nonces.Mint(slot))
            : null;

    /// <summary>The player slots with an open session, ascending.</summary>
    public IReadOnlyList<int> AttachedSlots()
        => _slotOf.Where(p => _open.TryGetValue(p.Key, out var s) && s.State == WebSocketState.Open)
                  .Select(p => p.Value).Where(s => s > 0).Distinct().OrderBy(s => s).ToList();

    /// <summary>
    /// Asks the client in <paramref name="slot"/> one question and waits for its
    /// reply. Null when no session is open for that slot or it does not answer
    /// within <paramref name="timeout"/>.
    /// </summary>
    public (int Status, string Body)? Query(int slot, DebugChannelQuery query, TimeSpan timeout, string? argument = null)
    {
        // The newest session for the slot: a client that redialled replaces
        // the one before it.
        int session = _slotOf.Where(p => p.Value == slot && slot > 0).Select(p => p.Key)
                             .DefaultIfEmpty(-1).Max();

        if (session < 0 || !_open.TryGetValue(session, out var socket) || socket.State != WebSocketState.Open)
            return null;

        int request = Interlocked.Increment(ref _nextRequest);
        var reply = new TaskCompletionSource<(int, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[request] = reply;

        try
        {
            if (!SendTo(socket, DebugChannelCodec.Query(request, query, argument)))
                return null;

            return reply.Task.Wait(timeout) ? reply.Task.Result : null;
        }
        finally
        {
            _waiting.TryRemove(request, out _);
        }
    }

    /// <summary>
    /// ASKS EVERY ATTACHED CLIENT TO DO ONE OF THE FOUR DECLARED OPERATIONS.
    ///
    /// <para>
    /// This is the whole of the command-and-control direction, and its signature
    /// is the bound: it takes a <see cref="DebugChannelCommand"/>, which is an
    /// enumeration of four named operations, and there is no overload that takes
    /// a string. A server cannot ask a client to run a console command, write a
    /// file or set a convar, because there is nothing here to express that with.
    /// </para>
    ///
    /// <para>
    /// If arbitrary command execution is ever wanted it is a separate and
    /// separately-flagged decision, taken by somebody who has weighed what it
    /// means for a client to hand a game server that power. It is not another
    /// case in the enum and not another overload here.
    /// </para>
    /// </summary>
    /// <returns>How many clients were asked.</returns>
    public int Ask(DebugChannelCommand command) => Send(DebugChannelCodec.Command(command));

    /// <summary>
    /// Sends bytes that <see cref="Ask"/> could not have produced.
    ///
    /// <para>
    /// INTERNAL AND FOR ONE TEST. The client's refusal of a command byte that
    /// names no declared operation cannot be checked through <see cref="Ask"/>,
    /// because <see cref="Ask"/>'s parameter is the enum — which is exactly the
    /// property being checked. So the frame is written by hand here, the way
    /// something that is not this server would have to write it.
    /// </para>
    /// </summary>
    internal int AskRawForTest(byte[] frame) => Send(frame);

    /// <summary>One frame to one session, serialised with every other send.</summary>
    private bool SendTo(WebSocket socket, byte[] frame)
    {
        try
        {
            _sending.Wait(TimeSpan.FromSeconds(1));

            try
            {
                socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary,
                                 endOfMessage: true, _cancel.Token).GetAwaiter().GetResult();
                return true;
            }
            finally
            {
                _sending.Release();
            }
        }
        catch (Exception e)
        {
            _warn($"[SourceSharp/DebugChannel] could not reach a client: {e.Message}\n");
            return false;
        }
    }

    private int Send(byte[] frame)
    {
        int asked = 0;

        foreach (var (_, socket) in _open)
        {
            if (socket.State != WebSocketState.Open)
                continue;

            try
            {
                // Serialised, because a WebSocket allows one send at a time and
                // the sessions' own sends share these sockets.
                _sending.Wait(TimeSpan.FromSeconds(1));

                try
                {
                    socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary,
                                     endOfMessage: true, _cancel.Token).GetAwaiter().GetResult();
                    asked++;
                }
                finally
                {
                    _sending.Release();
                }
            }
            catch (Exception e)
            {
                _warn($"[SourceSharp/DebugChannel] could not reach a client: {e.Message}\n");
            }
        }

        return asked;
    }

    /// <summary>
    /// Drops every outstanding invitation, at level shutdown.
    ///
    /// <para>
    /// The sessions themselves go with the listener, which goes with the dev API,
    /// which goes with the module. What survives a level change without this is
    /// the nonce table — an offer made to the previous map's client, still good.
    /// </para>
    /// </summary>
    public void LevelShutdown()
    {
        _nonces.Clear();
        _resumes.Clear();
    }

    /// <summary>One consistent reading of everything a status endpoint reports.</summary>
    public DebugChannelStatus Status()
        => new(_running, Address, Public, Port, _nonces.Outstanding, _sessions.Count,
               Volatile.Read(ref _accepted), Volatile.Read(ref _refused),
               Interlocked.Read(ref _recordsReceived), _everAttached);

    /// <summary>
    /// Closes the listener and WAITS for the accept thread and every session.
    ///
    /// <para>
    /// The wait is not politeness. A thread or a task still on the stack keeps
    /// every assembly on it alive, so a channel that was merely signalled pins
    /// the server's collectible load context and every managed reload leaks the
    /// build it replaced — the same defect <c>HttpSocketServer.Dispose</c> and
    /// <c>ConsoleTail.Dispose</c> were both written to avoid.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        _running = false;

        try { _cancel.Cancel(); }
        catch { /* shutting down */ }

        try { _listener.Close(); }
        catch { /* shutting down */ }

        var thread = _thread;
        _thread = null;

        bool joined = thread is null || thread.Join(TeardownTimeout);

        if (!joined)
        {
            _warn("[SourceSharp/DebugChannel] the accept loop did not stop; this side's load " +
                  "context cannot unload and a reload will leak it\n");
        }

        bool drained = _sessions.WaitAll(TeardownTimeout);

        if (!drained)
        {
            _warn($"[SourceSharp/DebugChannel] {_sessions.Count} session(s) did not stop; this " +
                  "side's load context cannot unload and a reload will leak it\n");
        }

        StoppedCleanly = joined && drained;

        _nonces.Clear();
        _resumes.Clear();
        _open.Clear();

        try { _cancel.Dispose(); }
        catch { /* shutting down */ }

        try { _sending.Dispose(); }
        catch { /* shutting down */ }
    }

    //-------------------------------------------------------------------------
    // Accepting.
    //-------------------------------------------------------------------------
    private void AcceptLoop()
    {
        while (_running)
        {
            Socket client;

            try
            {
                client = _listener.Accept();
            }
            catch
            {
                return; // listener closed
            }

            if (_sessions.Count >= MaxSessions)
            {
                Interlocked.Increment(ref _refused);
                Close(client);
                continue;
            }

            // The handshake happens on THIS thread, synchronously, before a
            // session exists: it is a few hundred bytes and a hash, and doing it
            // here means a connection that fails the nonce never costs a task.
            string? raw = ReadHandshake(client);

            if (raw is null)
            {
                Interlocked.Increment(ref _refused);
                Close(client);
                continue;
            }

            int slot = 0;
            var decision = DebugChannelHandshake.Decide(raw,
                nonce => _nonces.Redeem(nonce, out slot),
                resume => _resumes.Redeem(resume, out slot),
                () => slot > 0 ? DebugChannelResume.HeaderValue(slot, _resumes.Issue(slot)) : null);

            try { client.Send(Encoding.ASCII.GetBytes(decision.Response)); }
            catch { /* the caller hung up mid-handshake */ }

            if (!decision.Accepted)
            {
                Interlocked.Increment(ref _refused);
                _warn($"[SourceSharp/DebugChannel] refused a connection: {decision.Reason}\n");
                Close(client);
                continue;
            }

            Interlocked.Increment(ref _accepted);
            _everAttached = true;

            Socket accepted = client;
            int bound = slot;
            _sessions.Start(id => Session(accepted, id, bound));
        }
    }

    private static void Close(Socket client)
    {
        try { client.Close(); }
        catch { /* nothing useful to do */ }
    }

    /// <summary>Reads the upgrade request, bounded. Null for anything that is not one.</summary>
    private static string? ReadHandshake(Socket client)
    {
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;

        var buffer = new byte[1024];
        var received = new MemoryStream();

        while (true)
        {
            int read;

            try { read = client.Receive(buffer); }
            catch { return null; }

            if (read <= 0)
                return null;

            received.Write(buffer, 0, read);

            if (received.Length > DebugChannelHandshake.MaxRequestBytes)
                return null;

            string text = Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);

            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                return text;
        }
    }

    //-------------------------------------------------------------------------
    // One session.
    //-------------------------------------------------------------------------
    private async Task Session(Socket client, int id, int slot)
    {
        _slotOf[id] = slot;

        var sink = new Sink(this);
        var stream = new NetworkStream(client, ownsSocket: true);

        WebSocket socket = WebSocket.CreateFromStream(
            stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));

        _open[id] = socket;

        var buffer = new byte[64 * 1024];

        try
        {
            while (!_cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var frame = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token)
                                         .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    frame.Write(buffer, 0, result.Count);

                    // A FRAME THAT WILL NOT FIT IS DROPPED WITH THE SESSION, not
                    // grown into. These bytes come off a socket.
                    if (frame.Length > DebugChannelCodec.MaxFrameBytes)
                    {
                        _warn("[SourceSharp/DebugChannel] a client sent a frame past the limit; " +
                              "closing the session\n");
                        return;
                    }
                }
                while (!result.EndOfMessage);

                DebugChannelRouter.Route(frame.GetBuffer().AsSpan(0, (int)frame.Length), sink);
            }
        }
        catch (OperationCanceledException)
        {
            // Level change or shutdown. Not worth a line.
        }
        catch (Exception e)
        {
            _warn($"[SourceSharp/DebugChannel] session {id} ended: {e.Message}\n");
        }
        finally
        {
            _open.TryRemove(id, out _);
            _slotOf.TryRemove(id, out _);

            try { socket.Dispose(); }
            catch { /* the session is over either way */ }

            try { stream.Dispose(); }
            catch { /* likewise */ }
        }
    }

    /// <summary>
    /// What a session does with a frame it understood.
    ///
    /// <para>
    /// An inner class rather than the listener implementing the interface,
    /// because the sink is what a TEST stands in for — and a test that had to
    /// build a listener would have to bind a port to check a decode.
    /// </para>
    /// </summary>
    private sealed class Sink : IDebugChannelSink
    {
        private readonly DebugChannelListener _owner;

        public Sink(DebugChannelListener owner) => _owner = owner;

        public void Hello(int protocol, string build)
            => _owner._log($"[SourceSharp/DebugChannel] a client attached, protocol {protocol}, " +
                           $"built {build}\n");

        public void Prediction(IReadOnlyList<WirePredictionRecord> records)
        {
            foreach (var record in records)
                _owner.Mirror.Add(record);

            Interlocked.Add(ref _owner._recordsReceived, records.Count);
        }

        public void Malformed(string why)
            => _owner._warn($"[SourceSharp/DebugChannel] dropped {why}\n");

        public void Reply(int requestId, int status, string body)
        {
            // A reply nobody waits for (timed out, or never asked) is dropped.
            if (_owner._waiting.TryGetValue(requestId, out var waiting))
                waiting.TrySetResult((status, body));
        }
    }
}
