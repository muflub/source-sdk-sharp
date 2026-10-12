using Debug.Api;
using System.Net.WebSockets;

using SourceSharp.Client.Interface;

namespace Debug.Api;

/// <summary>
/// The client's half of the debug channel: it dials the server that invited it
/// and pushes what only this side knows.
///
/// <para>
/// WHY IT EXISTS. Prediction runs on the client and the development API belongs
/// to the server, which never reads the client's ring (plan_devapi_interfaces
/// §5.8, Q1). The client's prediction reaches the server only through here: pushed
/// up to the server's mirror (<c>/prediction</c>), and answered on request as
/// the client's own report (<c>/client/{slot}/prediction</c>), on a listen server
/// and a dedicated one alike.
/// </para>
///
/// <para>
/// <b>TWO INDEPENDENT OPT-INS, AND THIS CLASS OWNS THE FIRST.</b>
/// <see cref="RequestedBy"/> reads <c>-debugui</c> off THIS PROCESS' command line.
/// Without it <c>ClientModule</c> builds none of this, installs no listener for
/// the invitation message, and there is nothing here that any server can reach.
/// The server's own opt-in is separate and is the server's to make.
/// </para>
///
/// <para>
/// <b>AND THE HOST IS THIS SIDE'S DECISION, NEVER THE SERVER'S.</b> The
/// invitation carries a port and a nonce and structurally cannot carry an
/// address; see <see cref="DebugChannelInvitation.TryRead"/>. Where that port is
/// comes from <see cref="ResolveHost"/> — loopback, or a host named by whoever
/// launched this client. That keeps a game server from being able to aim a
/// player's debug socket at a machine of its choosing, which is what the obvious
/// version of this feature would have been.
/// </para>
/// </summary>
public sealed class DebugChannelClient : IDebugApiClient
{
    /// <summary>
    /// How often the ring is drained while the channel is up.
    ///
    /// <para>
    /// POLLED RATHER THAN SUBSCRIBED, and the reason is load contexts. The ring
    /// lives in <c>SourceSharp.Abi</c>, which <c>GameLoadContext</c> resolves to
    /// the DEFAULT context on purpose — so an event on it holding a delegate to
    /// a method of this class would root the CLIENT's collectible context from a
    /// context that never unloads, and every managed reload would leak the build
    /// it replaced. A poll holds nothing.
    /// </para>
    /// </summary>
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// <see cref="DefaultPollInterval"/> in the game. A test that waits for
    /// polls to happen sets it short, so it waits for the same number of polls
    /// in less time.
    /// </summary>
    internal TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    /// <summary>
    /// How many times a dial is retried before the channel gives up for good.
    ///
    /// <para>
    /// IT GIVES UP RATHER THAN SPINNING. A debugging tool that reconnects
    /// forever against a server that is not listening is a background task
    /// burning a socket and a log line every second for the rest of the session.
    /// Four tries with a doubling delay is about fifteen seconds, after which it
    /// says so once and stops; the next level change invites again.
    /// </para>
    /// </summary>
    public const int MaxDialAttempts = 4;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <summary>How long <see cref="Dispose"/> waits for the loop to come off the stack.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(2);

    private readonly string _host;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;

    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private ClientWebSocket? _socket;

    // What the current session's 101 handed over to resume it (port, slot,
    // token), written on the channel's task and read on the game thread.
    private readonly object _resumeLock = new();
    private DebugChannelResume? _resume;

    // Observable state, for the negative proofs and for a status line. Written
    // from the channel's own task and read from the game thread, so they are
    // interlocked rather than plain fields.
    private int _dialAttempts;
    private int _framesSent;
    private int _commandsHonoured;
    private int _commandsRefused;
    private volatile bool _attached;
    private volatile bool _gaveUp;

    /// <summary>Dial attempts made, which is zero for a channel that never dialled.</summary>
    public int DialAttempts => Volatile.Read(ref _dialAttempts);

    /// <summary>Frames pushed to the server.</summary>
    public int FramesSent => Volatile.Read(ref _framesSent);

    /// <summary>Commands from the server that named a declared operation.</summary>
    public int CommandsHonoured => Volatile.Read(ref _commandsHonoured);

    /// <summary>Commands that did not, and were dropped.</summary>
    public int CommandsRefused => Volatile.Read(ref _commandsRefused);

    /// <summary>How many of the server's questions this client has answered.</summary>
    public int QueriesAnswered => Volatile.Read(ref _queriesAnswered);
    private int _queriesAnswered;

    /// <summary>True between a successful handshake and the close.</summary>
    public bool Attached => _attached;

    /// <summary>True once the dialer has stopped trying for good.</summary>
    public bool GaveUp => _gaveUp;

    /// <summary>True while a dial loop is on the stack. Level shutdown must make this false.</summary>
    public bool Running => _loop is { IsCompleted: false };

    /// <summary>
    /// Whether the last <see cref="Dispose"/> saw the loop actually come OFF the
    /// stack inside its timeout.
    ///
    /// <para>
    /// SEPARATE FROM <see cref="Running"/> AND NOT DERIVABLE FROM IT. Dispose
    /// drops its reference to the task, so "running" reads false the moment it
    /// is called whether or not anything stopped — which is exactly the reading
    /// that makes a teardown look successful while a task is still holding this
    /// side's collectible load context alive. This is the answer the wait
    /// actually returned.
    /// </para>
    /// </summary>
    public bool StoppedCleanly { get; private set; }

    /// <summary>
    /// GATE ONE. Whether this process asked for a debug channel at all.
    ///
    /// <para>
    /// Read off the CLIENT's own command line, and read before anything else
    /// happens: a false here means the module never constructs this class and
    /// never installs the invitation listener, so no message from any server can
    /// reach a line of this file.
    /// </para>
    ///
    /// <para>
    /// <b>THROUGH ITS OWN ARGV AND NOT <c>IEngineHost</c>,</b>
    /// and that is not a style choice. The engine's command-line slot was
    /// filled only on the server and was null on this side, so asking it
    /// answered a plausible "no" whatever anybody typed. It is retired now, and
    /// both sides read their own argv.
    /// </para>
    /// </summary>
    public static bool RequestedBy(IReadOnlyList<string> args)
        => DebugCommandLine.Has(args, DebugChannelProtocol.ClientParam);

    /// <summary>
    /// WHERE THE PORT IS, decided here and only here.
    ///
    /// <para>
    /// <b>THE SERVER HAS NO SAY IN THIS AND THE INVITATION CANNOT CARRY ONE.</b>
    /// Loopback is the answer for a listen server and for a dedicated server on
    /// the same machine, which is nearly every development case. For a server
    /// across the network the person running the client names it with
    /// <c>-debuguihost</c> — on their own command line, so the address is one they
    /// chose, and by construction the one they already trusted enough to join.
    /// </para>
    ///
    /// <para>
    /// The shape is checked, not the trust: a value with a scheme, a slash, an
    /// at-sign or whitespace in it is a value somebody expected to be a URL, and
    /// pasting one into the middle of a <c>ws://</c> URI is how a host turns into
    /// a path or a userinfo section. Loopback is the answer instead, loudly.
    /// </para>
    /// </summary>
    public static string ResolveHost(IReadOnlyList<string> args, Action<string> warn)
    {
        string named = DebugCommandLine.Value(
            args, DebugChannelProtocol.ClientHostParam, DebugChannelProtocol.DefaultHost);

        if (string.IsNullOrWhiteSpace(named))
            return DebugChannelProtocol.DefaultHost;

        foreach (char c in named)
        {
            if (c is '/' or '\\' or '@' or '?' or '#' or ' ' or '\t' || char.IsControl(c))
            {
                warn($"[SourceSharp/Client] {DebugChannelProtocol.ClientHostParam} '{named}' is not a " +
                     $"bare host; using {DebugChannelProtocol.DefaultHost}\n");
                return DebugChannelProtocol.DefaultHost;
            }
        }

        if (named.Contains(':') && !named.StartsWith('['))
        {
            // A bare IPv6 literal has to be bracketed inside a URI or its colons
            // are read as a port separator.
            named = $"[{named}]";
        }

        return named;
    }

    /// <summary>
    /// The URI the client will dial for one invitation.
    ///
    /// <para>
    /// Built from a host this side resolved and a port the invitation named, and
    /// separated out so a test can hold the composition to the rule: whatever an
    /// invitation says, the host in the URI is the one <see cref="ResolveHost"/>
    /// answered.
    /// </para>
    /// </summary>
    public static Uri UriFor(string host, DebugChannelInvitation invitation)
        => new($"ws://{host}:{invitation.Port}{DebugChannelProtocol.Path}");

    /// <param name="hooks">What the client composition serves its dev API (P3).</param>
    public DebugChannelClient(string host, IDebugClientHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        _host = host;
        _hooks = hooks;
        _log = hooks.Log.Msg;
        _warn = hooks.Log.Warning;
    }

    private readonly IDebugClientHooks _hooks;

    /// <summary>
    /// What the client module's loader constructs: the process' argument list
    /// and the composition's hooks. The dev API names no Client or Shared type
    /// (plan_devapi_interfaces §2).
    /// </summary>
    public DebugChannelClient(IReadOnlyList<string> args, IDebugClientHooks hooks)
        : this(ResolveHost(args, hooks.Log.Warning), hooks)
    {
    }

    /// <summary>
    /// Takes an accepted invitation and starts dialling.
    ///
    /// <para>
    /// <b>A FRESH INVITATION WINS.</b> The server invites at a join and again
    /// after its own managed reload, which replaced the listener the current
    /// channel (or a resume in flight, or carried resume data) belonged to. So
    /// whatever is running is stopped and its resume data dropped, and the
    /// invitation is dialled. Still one socket at a time.
    /// </para>
    /// </summary>
    /// <inheritdoc cref="IDebugApiClient.Attach"/>
    public void Attach(ReadOnlySpan<byte> invitation)
    {
        if (!DebugChannelInvitation.TryRead(invitation, out var read, out string refusal))
        {
            _warn($"[SourceSharp/Client] debug channel invitation refused: {refusal}\n");
            return;
        }

        Attach(read);
    }

    /// <summary>Dials the server's dev API on the invitation's port, in the background.</summary>
    public void Attach(DebugChannelInvitation invitation)
    {
        if (Running)
        {
            _log("[SourceSharp/Client] a fresh debug channel invitation replaces the channel this " +
                 "client had\n");
            Stop();
        }

        lock (_resumeLock)
            _resume = null;

        Dial(invitation.Port, DebugChannelProtocol.NonceHeader, invitation.Nonce, MaxDialAttempts);
    }

    /// <summary>
    /// The resume data to carry across this client's managed reload: the port,
    /// slot and token the server handed the current session, as bytes. Null
    /// when no session has been handed one.
    /// </summary>
    public byte[]? SaveResume()
    {
        lock (_resumeLock)
            return _resume?.ToBytes();
    }

    /// <summary>
    /// Redials with carried resume data, without an invitation: the reloaded
    /// module's half of <see cref="SaveResume"/>. One attempt: a token is
    /// single-use, so a refused one is not worth repeating, and a server that
    /// was itself reloaded sends a fresh invitation that replaces this.
    /// </summary>
    public void Resume(ReadOnlySpan<byte> carried)
    {
        if (!DebugChannelResume.TryRead(carried, out var resume, out string refusal))
        {
            _warn($"[SourceSharp/Client] carried debug channel resume data refused: {refusal}\n");
            return;
        }

        if (Running)
            return;

        _log($"[SourceSharp/Client] resuming the debug channel for slot {resume.Slot} after a reload\n");
        Dial(resume.Port, DebugChannelProtocol.ResumeHeader,
             DebugChannelResume.HeaderValue(resume.Slot, resume.Token), attempts: 1);
    }

    private void Dial(int port, string header, string value, int attempts)
    {
        _gaveUp = false;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        _loop = Task.Run(() => DialLoop(port, header, value, attempts, cancel));
    }

    /// <summary>
    /// Closes the channel and WAITS for its task to come off the stack.
    ///
    /// <para>
    /// The wait is the point, and it is the same lesson as
    /// <c>HttpSocketServer.Dispose</c>: a task still running holds every
    /// assembly on its stack alive, so a channel merely signalled would pin the
    /// client's collectible load context and make every managed reload leak the
    /// build it replaced. Aborting the socket is what unblocks a receive that is
    /// parked; the wait is what makes "closed" mean "gone".
    /// </para>
    ///
    /// <para>
    /// CALLED AT LEVEL SHUTDOWN, not only at process exit. A socket, a handler
    /// registration and a background task that survive a level change are
    /// exactly the family of defect that left this tree with a world that
    /// outlived its level and faulted the next map's first trace.
    /// </para>
    /// </summary>
    public void Dispose() => Stop();

    /// <summary>Cancels the loop, aborts the socket and WAITS for the task; see <see cref="Dispose"/>.</summary>
    private void Stop()
    {
        var cancel = _cancel;
        var loop = _loop;

        _cancel = null;
        _loop = null;

        try { cancel?.Cancel(); }
        catch { /* shutting down */ }

        // Abort rather than close: a parked ReceiveAsync does not notice a
        // cancellation on some paths, and a clean close needs a round trip the
        // server may not be there to complete.
        try { _socket?.Abort(); }
        catch { /* shutting down */ }

        StoppedCleanly = loop is null || loop.Wait(TeardownTimeout);

        if (!StoppedCleanly)
        {
            _warn("[SourceSharp/Client] the debug channel did not stop; this side's load context " +
                  "cannot unload and a reload will leak it\n");
        }

        try { _socket?.Dispose(); }
        catch { /* shutting down */ }

        _socket = null;
        _attached = false;

        try { cancel?.Dispose(); }
        catch { /* shutting down */ }
    }

    //-------------------------------------------------------------------------
    // The loop.
    //-------------------------------------------------------------------------
    private async Task DialLoop(int port, string header, string value, int attempts, CancellationToken token)
    {
        var uri = new Uri($"ws://{_host}:{port}{DebugChannelProtocol.Path}");
        TimeSpan backoff = FirstBackoff;

        for (int attempt = 1; attempt <= attempts && !token.IsCancellationRequested; attempt++)
        {
            Interlocked.Increment(ref _dialAttempts);

            try
            {
                await Session(uri, port, header, value, token).ConfigureAwait(false);

                // A session that ended by itself ended because the server said
                // so or because the level went. Neither is worth redialling: the
                // nonce is spent, and a new level brings a new invitation.
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _warn($"[SourceSharp/Client] debug channel dial {attempt}/{attempts} " +
                      $"to {uri} failed: {e.Message}\n");
            }

            if (attempt == attempts)
                break;

            try { await Task.Delay(backoff, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            backoff += backoff;
        }

        _gaveUp = true;

        if (!token.IsCancellationRequested)
        {
            _log($"[SourceSharp/Client] debug channel gave up after {attempts} attempt(s); " +
                 "nothing will be retried until the next invitation\n");
        }
    }

    private async Task Session(Uri uri, int port, string header, string value, CancellationToken token)
    {
        var socket = new ClientWebSocket();

        // THE NONCE (OR THE RESUME TOKEN) TRAVELS IN THE UPGRADE, so a bad one
        // is refused before a WebSocket exists on either side rather than after
        // one is negotiated. The 101's headers are kept: the next resume token
        // comes back in them.
        socket.Options.SetRequestHeader(header, value);
        socket.Options.CollectHttpResponseDetails = true;

        _socket = socket;

        try
        {
            await socket.ConnectAsync(uri, token).ConfigureAwait(false);

            _attached = true;
            _log($"[SourceSharp/Client] debug channel open to {uri}\n");

            KeepResume(socket, port);

            await Send(socket, DebugChannelCodec.Hello(_hooks.Build.ManagedStamp()),
                       token).ConfigureAwait(false);

            var receiving = Receive(socket, token);
            await Push(socket, token).ConfigureAwait(false);
            await receiving.ConfigureAwait(false);
        }
        finally
        {
            _attached = false;

            try { socket.Dispose(); }
            catch { /* the session is over either way */ }

            if (ReferenceEquals(_socket, socket))
                _socket = null;
        }
    }

    /// <summary>Drains the ring on a timer and sends what is new.</summary>
    /// <summary>What this session's 101 handed over to resume it after a reload, kept for <see cref="SaveResume"/>.</summary>
    private void KeepResume(ClientWebSocket socket, int port)
    {
        DebugChannelResume? kept = null;

        if (socket.HttpResponseHeaders is { } headers &&
            headers.TryGetValue(DebugChannelProtocol.ResumeHeader, out var values) &&
            DebugChannelResume.TryReadHeader(values.FirstOrDefault(), out int slot, out string token))
        {
            kept = new DebugChannelResume(DebugChannelProtocol.Version, port, slot, token);
        }

        lock (_resumeLock)
            _resume = kept;
    }

    private async Task Push(ClientWebSocket socket, CancellationToken token)
    {
        long cursor = 0;

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            cursor = await PushOnce(socket, cursor, token).ConfigureAwait(false);

            try { await Task.Delay(PollInterval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<long> PushOnce(ClientWebSocket socket, long cursor, CancellationToken token)
    {
        var records = _hooks.Prediction.Since(cursor, DebugChannelCodec.MaxRecordsPerBatch).Records;

        if (records.Count == 0)
            return cursor;

        await Send(socket, DebugChannelCodec.PredictionBatch(records.Select(Wire).ToList()), token).ConfigureAwait(false);

        return records[^1].Sequence + 1;
    }

    /// <summary>
    /// Reads what the server asks for.
    ///
    /// <para>
    /// THE WHOLE COMMAND SURFACE IS THE SWITCH BELOW. A frame that is not a
    /// command, or a command byte that is not one of the four declared
    /// operations, is counted and dropped — <see cref="DebugChannelCodec"/>
    /// refuses it before it reaches here. There is deliberately no case that
    /// runs a console command, writes a file, or changes a convar: this channel
    /// exists because a server invited it, and a server that can do those things
    /// in a client owns the machine.
    /// </para>
    /// </summary>
    /// <summary>A record as the channel carries it.</summary>
    internal static WirePredictionRecord Wire(DebugPredictionRecord r)
        => new(r.Sequence, r.Command, r.TypeId, r.ClassName, r.BlobBytes, r.StandingBytes, r.FirstDiffOffset,
               r.ToleratedFields, r.TimeUtc,
               r.Fields.Select(f => new WirePredictionField(f.Name, f.Kind, f.Offset, f.Bytes, f.Tolerance, f.Tolerated,
                   f.DifferingBytes, f.Components.Select(c => new WirePredictionComponent(c.Predicted, c.Server, c.Delta)).ToArray()))
                .ToArray());

    private async Task Receive(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;

            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token)
                                     .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return;

            // A question about this client's own state, which the server's dev
            // API is proxying for /client/{slot}/... (plan_side_isolation §2.6).
            if (DebugChannelCodec.TryReadQuery(buffer.AsSpan(0, result.Count), out int request, out var query,
                                               out string argument))
            {
                Interlocked.Increment(ref _queriesAnswered);

                (int status, string body) answer;
                try
                {
                    answer = NeedsGameThread(query)
                        ? await OnGameThread(query, argument, token).ConfigureAwait(false)
                        : (200, Answer(query, argument));
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                await Send(socket, DebugChannelCodec.Reply(request, answer.status, answer.body), token).ConfigureAwait(false);
                continue;
            }

            if (!DebugChannelCodec.TryReadCommand(buffer.AsSpan(0, result.Count), out var command))
            {
                Interlocked.Increment(ref _commandsRefused);
                continue;
            }

            Interlocked.Increment(ref _commandsHonoured);

            switch (command)
            {
                case DebugChannelCommand.Ping:
                    await Send(socket, DebugChannelCodec.Pong(), token).ConfigureAwait(false);
                    break;

                case DebugChannelCommand.ResetPredictionRing:
                    _hooks.Prediction.Reset();
                    break;

                case DebugChannelCommand.SendPredictionNow:
                    await PushOnce(socket, 0, token).ConfigureAwait(false);
                    break;

                case DebugChannelCommand.Disconnect:
                    return;
            }
        }
    }

    /// <summary>
    /// The answer to one question, from this side's own state: the client's
    /// build stamp and the crossing counters this side's ABI holds.
    /// </summary>
    // render/compare's argument: "frozen:PATH" and "frozen-mutate:PATH" compare
    // the next plan frame with a frozen stock frame (C7 step 3). The live
    // stock/plan compare left with the stock path.
    private DebugClientRenderRequest RenderCompare(string? argument)
    {
        const string Frozen = "frozen:", FrozenMutate = "frozen-mutate:";
        if (argument is not null && argument.StartsWith(FrozenMutate, StringComparison.Ordinal))
            return _hooks.Render.CompareFrozen(argument[FrozenMutate.Length..], mutate: true);
        if (argument is not null && argument.StartsWith(Frozen, StringComparison.Ordinal))
            return _hooks.Render.CompareFrozen(argument[Frozen.Length..], mutate: false);
        return new DebugClientRenderRequest(false, "the live stock compare is gone (C7 step 3): ask mutate=frozen:PATH or frozen-mutate:PATH", -1);
    }

    private string PredictionReport()
    {
        var w = _hooks.Prediction.Since(0, PredictionMirror.DefaultCapacity);
        return PredictionJson.Report(w.Capacity, w.Held, w.NextSequence, w.Observed, w.Mispredicted,
                                     w.ToleratedOnly, w.Records.Select(Wire).ToList());
    }

    private static string BuildJson(DebugBuildInfo build)
        => DebugJson.Build(Half(build.Native), Half(build.Managed));

    private static string Half(DebugBuildHalf h)
        => DebugJson.BuildHalf(h.Known, h.Hash, h.BuiltAtUnixMs, h.Parts, h.Branch, h.Commit, h.Dirty,
                             h.Source, h.Freshness, h.Path);

    /// <summary>
    /// The user messages received, as <c>,"userMessages":{"Damage":3}</c>; empty
    /// when none has arrived, so a status with none reads as it always did.
    /// </summary>
    private static string UserMessagesJson(IReadOnlyList<DebugCount> counts)
        => counts.Count == 0 ? "" :
           ",\"userMessages\":{" + string.Join(",", counts.Select(c => $"{DebugJson.Quote(c.Name)}:{c.Count}")) + "}";

    /// <summary><c>,"name":{"key":n,...}</c>, empty when nothing has been counted.</summary>
    private static string CountsJson(string name, IReadOnlyList<DebugCount> counts)
        => counts.Count == 0 ? "" :
           ",\"" + name + "\":{" + string.Join(",", counts.Select(c => $"{DebugJson.Quote(c.Name)}:{c.Count}")) + "}";

    private static string CrossingsJson(DebugCrossings c)
        => DebugJson.Crossings(c.Total, c.Resets, c.OpCount, c.ServerFrames, c.ClientFrames,
                             c.Ops.Select(o => (o.Name, o.Count)), c.EntityOps.Select(o => (o.Name, o.Count)));

    //-------------------------------------------------------------------------
    // THE GAME THREAD. A question that reads the game's state (entities, the
    // frame, the engine's net channel) is not answered on the socket's task:
    // it is queued, and the client module's Pump answers it at its next frame,
    // on the game thread, while the task waits. The status and prediction
    // questions read counters and a locked ring, and stay on the task.
    //-------------------------------------------------------------------------

    /// <summary>How long a question waits for a frame before the reply says none came.</summary>
    internal TimeSpan GameThreadTimeout { get; init; } = TimeSpan.FromSeconds(4);

    private readonly System.Collections.Concurrent.ConcurrentQueue<(DebugChannelQuery Query, string Argument,
        TaskCompletionSource<string> Answer)> _forGameThread = new();

    /// <summary>Questions answered by <see cref="Pump"/>, since construction.</summary>
    public int PumpedAnswers => Volatile.Read(ref _pumpedAnswers);
    private int _pumpedAnswers;

    internal static bool NeedsGameThread(DebugChannelQuery query)
        => query is not (DebugChannelQuery.Status or DebugChannelQuery.Prediction);

    internal const string NoFrameBody = "{\"error\":\"the client ran no frame to answer on; is it loading or paused?\"}";

    private async Task<(int, string)> OnGameThread(DebugChannelQuery query, string argument, CancellationToken token)
    {
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _forGameThread.Enqueue((query, argument, answer));

        try
        {
            return (200, await answer.Task.WaitAsync(GameThreadTimeout, token).ConfigureAwait(false));
        }
        catch (TimeoutException)
        {
            // Left in the queue: the next Pump answers it into a task nobody
            // awaits, which costs one answer and nothing else.
            return (503, NoFrameBody);
        }
    }

    /// <inheritdoc cref="IDebugApiClient.Pump"/>
    public void Pump()
    {
        while (_forGameThread.TryDequeue(out var work))
        {
            string body;
            try
            {
                body = Answer(work.Query, work.Argument);
            }
            catch (Exception e)
            {
                // A question must not take the frame down: the reader sees why.
                body = $"{{\"error\":\"the client failed to answer\",\"exception\":{DebugJson.Quote(e.GetType().Name + ": " + e.Message)}}}";
            }

            Interlocked.Increment(ref _pumpedAnswers);
            work.Answer.TrySetResult(body);
        }
    }

    /// <summary>Packets a netgraph answer carries when the caller does not say: the panel's history.</summary>
    internal const int DefaultNetGraphFrames = 64;

    /// <param name="argument">The question's argument (an index, a filter); empty when it has none.</param>
    internal string Answer(DebugChannelQuery query, string argument = "") => query switch
    {
        DebugChannelQuery.Status =>
            $"{{\"side\":\"client\",\"build\":{BuildJson(_hooks.Build.Build())}," +
            $"\"crossings\":{CrossingsJson(_hooks.Build.Crossings())}{UserMessagesJson(_hooks.Build.UserMessagesReceived())}{CountsJson("gameEvents", _hooks.Build.GameEventsReceived())}}}",
        // The client's own report (Q1): the server proxies it at
        // /client/{slot}/prediction and never reads this ring itself.
        DebugChannelQuery.Prediction => PredictionReport(),
        DebugChannelQuery.Frame => ClientRouteJson.Frame(_hooks.Frame.Read()),
        DebugChannelQuery.Entities => ClientRouteJson.Entities(argument, _hooks.Entities.List(argument)),
        DebugChannelQuery.NetTables => ClientRouteJson.NetTables(_hooks.NetTables.Read()),
        DebugChannelQuery.Reload => ClientRouteJson.Reload(_hooks.Reload.Read()),
        DebugChannelQuery.ReloadTrigger => ClientRouteJson.ReloadRequest(_hooks.Reload.Trigger()),
        DebugChannelQuery.NetGraph => ClientRouteJson.NetGraph(_hooks.NetGraph.Read(
            int.TryParse(argument, out int frames) ? frames : DefaultNetGraphFrames)),
        DebugChannelQuery.Absences => ClientRouteJson.Absences(_hooks.Absences.Read()),
        DebugChannelQuery.Ui => ClientRouteJson.Ui(_hooks.Ui.Read(argument == "1")),
        DebugChannelQuery.UiFonts => ClientRouteJson.UiFonts(_hooks.Ui.Fonts(argument == "1")),
        DebugChannelQuery.UiMenu => ClientRouteJson.Ui(_hooks.Ui.ReadMenu(argument == "1")),
        DebugChannelQuery.UiEvent => _hooks.Ui.Event(argument),
        DebugChannelQuery.Proxies => ClientRouteJson.Proxies(_hooks.Proxies.Read(argument)),
        DebugChannelQuery.ViewCam => ClientRouteJson.ViewCamera(
            argument.Length == 0 ? _hooks.ViewCamera.Read() : _hooks.ViewCamera.Set(argument)),
        DebugChannelQuery.Render => ClientRouteJson.Render(_hooks.Render.Read(argument == "1")),
        DebugChannelQuery.RenderCompare => ClientRouteJson.RenderRequest(RenderCompare(argument)),
        DebugChannelQuery.Particles => ClientRouteJson.Particles(_hooks.Particles.Read()),
        DebugChannelQuery.Input => ClientRouteJson.Input(_hooks.Input.Read()),
        DebugChannelQuery.InputInject => ClientRouteJson.InputInject(_hooks.Input.Inject(argument)),
        DebugChannelQuery.Entity => int.TryParse(argument, out int index) && index >= 0
            ? _hooks.Entities.State(index) is { } state
                ? ClientRouteJson.Entity(state)
                : $"{{\"error\":\"no client entity at that index\",\"index\":{index}}}"
            : $"{{\"error\":\"the entity index is not a number\",\"index\":{DebugJson.Quote(argument)}}}",
        _ => "{\"error\":\"no such question\"}",
    };

    private async Task Send(ClientWebSocket socket, byte[] frame, CancellationToken token)
    {
        await socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary,
                               endOfMessage: true, token).ConfigureAwait(false);

        Interlocked.Increment(ref _framesSent);
    }
}
