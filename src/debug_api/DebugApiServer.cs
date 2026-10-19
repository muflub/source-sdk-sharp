using Debug.Api;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>
/// A small HTTP API for driving the running game from outside it.
///
/// Why this exists: the only way to find out whether a change worked was to
/// launch the game, play it, and read the console afterwards. That is a slow
/// loop and a lossy one — anything that needs a console command, a convar, or a
/// look at live entity state could not be checked at all without a person at the
/// keyboard.
///
/// Three things keep it out of the way of a real server:
///
///   - The assembly is not referenced by anything. The server loads it by name
///     only when the process was started with -debugapi, so a server that was not
///     asked for it never brings the code into memory at all.
///   - It listens on loopback, or on a Unix socket the user owns. There is no
///     authentication, because there is nothing remote to authenticate.
///   - With -debugapisocket the socket is named for the process, so several game
///     instances can be driven at once. A fixed TCP port cannot do that: the
///     second game to start silently loses the bind and every request goes to
///     the first, which looks exactly like a change that did not take effect.
///
/// Threading is the interesting part. Requests are served on the listener's own
/// thread and the engine is single-threaded, so anything touching the game is
/// queued and run by <see cref="Pump"/> on the game thread, with the request
/// waiting for the result. Reading the log needs none of that — the ring is
/// already thread-safe — so a log poll never blocks on a frame.
/// </summary>
public sealed class DebugApiServer : IDebugApi
{
    /// <summary>Listen on a per-process Unix socket instead of a fixed TCP port.</summary>
    public const string SocketParam = "-debugapisocket";

    private const string PortParam = "-debugapiport";
    private const int DefaultPort = 27100;

    /// <summary>The socket directory when nothing overrides it.</summary>
    public const string DefaultSocketDirectory = "/tmp/sourcesharp-debug-api";

    /// <summary>The environment variable that moves the socket directory.</summary>
    public const string SocketDirectoryVar = "DEBUG_API_DIR";

    /// <summary>
    /// Where this server's per-process socket lives. Named by pid, which is
    /// what keeps two instances apart WITHIN one directory -- but the directory
    /// itself is one path for the whole machine, so concurrent runs in
    /// different worktrees still meet here. A tool that launches a game and
    /// then adopts "whichever socket appeared" adopts someone else's, which is
    /// exactly what happened: three parallel probes each printed the same six
    /// pids and killed each other's games. Setting DEBUG_API_DIR gives a run
    /// its own directory, so concurrent lanes cannot see each other at all.
    /// Read from the environment once, where the server is composed
    /// (<see cref="DebugApiDirectories.FromEnvironment"/>).
    /// </summary>
    public string SocketDirectory => _directories.SocketDirectory;

    private readonly DebugApiDirectories _directories;

    // Long enough to cover a slow frame, short enough that a wedged game answers
    // with an error instead of leaving the caller hanging.
    private static readonly TimeSpan DefaultWorkTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a game-thread route waits for <see cref="Pump"/> before
    /// answering the timeout. The game always runs with the default; a test on a
    /// server nothing pumps sets it short, so the timeout it observes is real
    /// but does not cost five seconds.
    /// </summary>
    internal TimeSpan WorkTimeout { get; init; } = DefaultWorkTimeout;

    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly LogRing _log = new();

    //-------------------------------------------------------------------------
    // THE DEADLOCK THIS EXISTS TO BREAK, AND IT LEAKED THE LOAD CONTEXT ON
    // EVERY HOT RELOAD.
    //
    // OnGameThread queues work and blocks the LISTENER thread until the GAME
    // thread drains it in Pump(). A managed reload runs on the game thread, and
    // the first thing it does is Dispose() this object -- which closes the HTTP
    // listener and JOINS its accept thread, with a one-second budget.
    //
    // So if a request was in flight when the reload started, the two threads
    // wait for each other: the listener thread is inside
    // done.Wait(WorkTimeout) for a Pump() that will never come, and the game
    // thread is inside Thread.Join waiting for the listener. Nothing breaks it
    // until the five-second work timeout expires, four seconds after the join
    // gave up. Measured on two live runs, both of which print, in this order:
    //
    //   [SourceSharp/DebugApi] the accept loop did not stop; this side's load
    //     context cannot unload and a reload will leak it
    //   [SourceSharp] ServerALC did NOT unload -- something still references
    //     it. Reloading will leak.
    //
    // A LIVE THREAD INSIDE THIS ASSEMBLY IS WHAT PINS THE ALC, which is the
    // same fact HttpSocketServer.Dispose and ConsoleTail.Dispose are both
    // written against; they join the thread they own, and this is the reason
    // the join could not succeed.
    //
    // Cancelling is the right answer rather than a longer join: the game thread
    // is about to stop existing for this load context, so a request waiting for
    // it can NEVER be served. It is answered with the same timeout body it
    // would have got four seconds later.
    //-------------------------------------------------------------------------
    private readonly CancellationTokenSource _shuttingDown = new();

    // Reads map files. Off the game thread entirely, which is what lets the
    // static view work with no game running and never cost a frame.
    private readonly MapSource _maps;

    private readonly EntityEventLog _events = new();

    /// <summary>The event ring, for a test to seed without a game.</summary>
    internal EntityEventLog EventsForTest => _events;
    private readonly EntitySampler _sampler;

    // The game's own console, which the managed sink cannot see. Its own ring
    // rather than the managed one: managed lines reach the console too, and one
    // feed carrying both would show every one of them twice.
    // Deep: the gate mirrors the console through /console instead of
    // re-reading console.log, and a busy level prints thousands of lines
    // between two of its reads (2,241 trace-hull lines in one join, measured
    // 2026-10-04). A reader further behind than this loses lines, and /console
    // says how many (lost).
    private readonly LogRing _console = new(65536);

    // The in-process feed of engine prints (docs/todo.md's console-capture
    // item): once the side's console-display binding has marked its lines as
    // fed, every print — the engine's and the managed side's, the echo of a
    // managed print already deduped — is subscribed straight into _console,
    // and the file tail, the old way in, is retired. Until then the ring is
    // the tail's alone (tests, no-engine runs, and a game started before the
    // areas were acquired).
    private IDisposable? _consoleFeed;

    /// <summary>
    /// Subscribes <see cref="_console"/> to the side's lines once the
    /// console-display binding has marked them fed, and retires the file tail
    /// whose job the in-process feed now does. Idempotent; tried from Start
    /// (covers a side whose forwarder was installed first) and every Pump
    /// (the module composes, and the API starts, before native installs).
    /// </summary>
    private void TryAttachConsoleFeed()
    {
        if (_consoleFeed is not null || !_hooks.Log.EngineFeedAttached)
        {
            return;
        }

        _consoleFeed = _hooks.Log.Subscribe(_console.Add);

        // The tail would now double every engine line the ring already sees
        // in process: stop it. Disposing is how ConsoleTail stops for good.
        _consoleTail?.Dispose();
        _consoleTail = null;
    }

    /// <summary>
    /// When this server started, in Unix ms: a reader mirroring /console sees
    /// it change when a managed reload replaced the server and its ring, whose
    /// sequences start again at zero.
    /// </summary>
    private readonly long _epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private ConsoleTail? _consoleTail;

    // What the last Pump saw, so a level change can be noticed without the
    // server having to tell us about one.
    private string _lastMap = "";

    private HttpSocketServer? _http;
    private string? _socketPath;

    /// <summary>
    /// The client-to-server debug channel, when this server opted in to one.
    ///
    /// <para>
    /// A SEPARATE LISTENER FROM <see cref="_http"/>, and the reason is measured
    /// rather than assumed: <see cref="HttpSocketServer"/>'s accept loop serves
    /// one connection at a time on one thread and closes it after replying, so a
    /// WebSocket held open on it would park that thread and every later dev API
    /// request behind it. See <see cref="DebugChannelListener"/>'s header.
    /// </para>
    /// </summary>
    private DebugChannelListener? _debugChannel;

    // The server's own command line (-debuguiserve, -debuguiport, -debuguipublic),
    // handed over by DebugApiLoader.LoadIfRequested as the plain argument list:
    // the dev API names no Shared type (plan_side_isolation §2.6).
    private readonly IReadOnlyList<string> _args;

    // What the side that loaded this server serves it (plan_devapi_interfaces
    // §5.3): that composition's own hook instances, the engine among them.
    private readonly IDebugServerHooks _hooks;

    /// <summary>This server's feed of managed log lines, from Start to Dispose.</summary>
    private IDisposable? _logFeed;

    /// <summary>This server's feed of fired outputs, from Start to Dispose.</summary>
    private IDisposable? _outputFeed;
    private readonly ModLayout _layout;

    /// <param name="args">The server's own command line.</param>
    /// <param name="hooks">
    /// What the loading server serves this API (plan_devapi_interfaces §5.3):
    /// that composition's own instances.
    /// </param>
    /// <remarks>
    /// The composition root: what the loader constructs (DebugApiLoader, by
    /// reflection over this signature), and the one place the process
    /// environment is read (<see cref="DebugApiDirectories.FromEnvironment"/>).
    /// </remarks>
    public DebugApiServer(IReadOnlyList<string> args, IDebugServerHooks hooks)
        : this(args, hooks, DebugApiDirectories.FromEnvironment())
    {
    }

    /// <param name="args">The server's own command line.</param>
    /// <param name="hooks">What the loading server serves this API.</param>
    /// <param name="directories">The socket and maps directories, decided by the caller.</param>
    internal DebugApiServer(IReadOnlyList<string> args, IDebugServerHooks hooks, DebugApiDirectories directories)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(directories);
        _args = args;
        _hooks = hooks;
        _directories = directories;
        // Resolved when asked, not here: constructing a server does not reach for the engine.
        _layout = new ModLayout(() => _hooks.World.ManagedDirectory,
                                () => DebugCommandLine.Value(_args, "-game", ""));
        _maps = directories.MapDirectory is { } named ? new MapSource(named) : new MapSource(_layout);
        _sampler = new EntitySampler(_events);
    }

    /// <summary>
    /// The group spawner, this server's own. Built on first use rather than
    /// here, so that constructing a server does not reach for the engine.
    /// </summary>
    private IEntityGroupSpawner Groups
        => _groups ??= new EntityGroupSpawner(_hooks.Console.Execute, _hooks.World);

    private IEntityGroupSpawner? _groups;

    /// <summary>Human-readable address. Only meaningful once listening.</summary>
    public string Address { get; private set; } = "(not listening)";

    /// <summary>
    /// Binds and begins listening. The server has already decided from the
    /// command line that this should exist; all that is left is how it listens.
    /// </summary>
    public bool Start()
    {
        try
        {
            EndPoint endPoint = DebugCommandLine.Has(_args, SocketParam)
                ? UnixSocketEndPoint()
                : LoopbackEndPoint();

            _http = new HttpSocketServer(endPoint, Route, message =>
                _hooks.Log.Warning($"[SourceSharp/DebugApi] {message}\n"), IsLongPoll);

            // Every managed log line from here on is captured. Subscribed after
            // the bind, so a failure does not leave a feed pointing at a server
            // that is not running.
            _logFeed = _hooks.Log.Subscribe(_log.Add);

            // Outputs report themselves. This is the only kind of entity event
            // that arrives rather than being sampled for, so it is the only one
            // that cannot be missed between polls.
            _outputFeed = _hooks.EntityEvents.Subscribe(OnOutputFired);

            // Best effort. Without -condebug there is no file, and the managed
            // feed carries on being the only one -- which is what it was before
            // this existed.
            _consoleTail = new ConsoleTail(_console, _layout, _hooks.Log.Warning,
                                           DebugCommandLine.Value(_args, "+con_logfile", ""));
            _consoleTail.Start();
            TryAttachConsoleFeed();

            ArmExitRecord();

            _http.Start();

            _hooks.Log.Msg($"[SourceSharp/DebugApi] listening on {Address} (pid {Environment.ProcessId})\n");

            StartDebugChannel();
            return true;
        }
        catch (Exception e)
        {
            // An address already in use is the common case — another instance,
            // or a previous run that has not let go. Worth saying out loud, and
            // never worth taking the game down over.
            _hooks.Log.Warning($"[SourceSharp/DebugApi] could not listen: {e.Message}\n");
            return false;
        }
    }

    //-------------------------------------------------------------------------
    // The debug channel, which is a SECOND opt-in and a SECOND listener.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Opens the channel, if this server asked for one.
    ///
    /// <para>
    /// TWO FLAGS, NOT ONE. <c>-debugapi</c> is what put this assembly in memory;
    /// <c>-debuguiserve</c> is what says a client may connect back to it. They are
    /// separate because they are different decisions: the dev API listens on
    /// loopback or on a socket only this user can open, and the channel is the
    /// one thing here a machine across the network can reach.
    /// </para>
    ///
    /// <para>
    /// A failure to bind is not fatal to the dev API. The channel is the
    /// addition; the socket that was already working keeps working.
    /// </para>
    /// </summary>
    private void StartDebugChannel()
    {
        IReadOnlyList<string> args = _args;
        if (!DebugChannelListener.RequestedBy(args))
            return;

        Bind(DebugChannelListener.PortFor(args), DebugChannelListener.PublicFor(args));
    }

    /// <summary>
    /// Opens the channel on a named port, as if the command line had asked for
    /// it. Internal so a test can drive the FAILURE path, which is the one with
    /// a reply of its own and no other way to reach.
    /// </summary>
    internal void StartDebugChannelForTest(int port) => Bind(port, @public: false);

    private void Bind(int port, bool @public)
    {
        try
        {
            _debugChannel = new DebugChannelListener(
                port, @public, _hooks.Log.Msg, _hooks.Log.Warning);

            _debugChannel.Start();

            _hooks.Log.Msg($"[SourceSharp/DebugChannel] listening on {_debugChannel.Address}" +
                        (_debugChannel.Public
                            ? " — BEYOND LOOPBACK, because " + DebugChannelProtocol.ServerPublicParam +
                              " was given\n"
                            : "\n"));
        }
        catch (Exception e)
        {
            _debugChannel = null;
            _debugChannelRefusal = e.Message;
            _hooks.Log.Warning($"[SourceSharp/DebugChannel] could not listen on port {port}: " +
                            $"{e.Message}. Give {DebugChannelProtocol.ServerPortParam} " +
                            "another port.\n");
        }
    }

    /// <summary>
    /// Why there is no channel, when one was asked for.
    ///
    /// <para>
    /// KEPT, BECAUSE A NULL CHANNEL HAS TWO CAUSES AND THEY NEED DIFFERENT
    /// ANSWERS. A live run reported "this server did not opt in" to somebody who
    /// had opted in and whose bind had failed on a port a peer's web UI was
    /// holding — which sent them looking at their command line instead of at the
    /// port.
    /// </para>
    /// </summary>
    private string? _debugChannelRefusal;

    /// <summary>
    /// The channel a test hands in, in place of one this process' command line
    /// asked for.
    ///
    /// <para>
    /// Internal and set-only, for the reason <see cref="NativeConsoleSource"/> is
    /// a property: <c>/prediction</c>'s choice between the in-process ring and a
    /// remote client's mirror is the thing worth checking, and checking it must
    /// not require a game, a command line, or a real client on the far end.
    /// </para>
    /// </summary>
    internal DebugChannelListener? DebugChannelForTest
    {
        set => _debugChannel = value;
    }

    /// <summary>
    /// An invitation to this server's debug channel, minted for one player's
    /// slot, or null when this server did not open one. The bytes the game sends
    /// come from <see cref="WriteInvitation"/>; this is the same offer as a value.
    /// </summary>
    public DebugChannelInvitation? InviteDebugChannel(int slot = 0) => _debugChannel?.Invite(slot);

    /// <inheritdoc cref="IDebugApi.WriteInvitation"/>
    public int WriteInvitation(int slot, Span<byte> into)
    {
        if (InviteDebugChannel(slot) is not { } invitation)
            return 0;

        int written = invitation.Write(into);
        if (written > 0)
            _hooks.Log.Msg($"[SourceSharp/DebugApi] offered client {slot} the debug channel on port {invitation.Port}\n");

        return written;
    }

    /// <inheritdoc cref="IDebugApi.LevelShutdown"/>
    public void LevelShutdown() => _debugChannel?.LevelShutdown();

    //-------------------------------------------------------------------------
    // A socket per process, named by pid.
    //-------------------------------------------------------------------------
    private EndPoint UnixSocketEndPoint()
    {
        Directory.CreateDirectory(SocketDirectory);

        string path = SocketPathFor(Environment.ProcessId);

        // A socket file outlives the process that made it, so a previous crash
        // leaves one behind and the bind would fail on it. Nothing else can own
        // this name — it has our pid in it — so removing it is safe.
        if (File.Exists(path))
            File.Delete(path);

        _socketPath = path;
        Address = path;

        return new UnixDomainSocketEndPoint(path);
    }

    private EndPoint LoopbackEndPoint()
    {
        int port = DefaultPort;

        if (int.TryParse(DebugCommandLine.Value(_args, PortParam, ""), out int requested)
            && requested is > 0 and < 65536)
        {
            port = requested;
        }

        Address = $"http://127.0.0.1:{port}/";
        return new IPEndPoint(IPAddress.Loopback, port);
    }

    /// <summary>The socket a given process would listen on, in this server's directory.</summary>
    public string SocketPathFor(int pid) => Path.Combine(SocketDirectory, $"{pid}.sock");

    /// <summary>
    /// An instance that has not been started, for testing the routing on its
    /// own. Routing is the half worth checking and the half that needs no
    /// socket, so this exists rather than a test binding one.
    /// </summary>
    internal static DebugApiServer ForTest(IDebugServerHooks hooks)
        => new(Array.Empty<string>(), hooks, DebugApiDirectories.Default);

    /// <summary>An unstarted test server over directories the test names.</summary>
    internal static DebugApiServer ForTest(IDebugServerHooks hooks, DebugApiDirectories directories)
        => new(Array.Empty<string>(), hooks, directories);

    /// <summary>
    /// A server whose game-thread waits -- the routes' and status' -- last
    /// <paramref name="gameThreadWait"/>, for a test that never pumps it and so
    /// takes the timeout path on purpose.
    /// </summary>
    internal static DebugApiServer ForTest(IDebugServerHooks hooks, TimeSpan gameThreadWait)
        // No arguments: an unstarted test server never opens the channel.
        => new(Array.Empty<string>(), hooks, DebugApiDirectories.Default) { WorkTimeout = gameThreadWait, StatusTimeout = gameThreadWait };

    /// <summary>
    /// Runs whatever the API asked for, on the game thread. Called once per
    /// server think — everything queued is drained, because a caller is blocked
    /// on each item and leaving any until the next frame doubles its latency.
    /// </summary>
    public void Pump()
    {
        // The production ordering: the module composes and this server starts
        // inside LoadSide, and native installs the display func only after
        // LoadSide returns — so the feed usually lands on the first frames,
        // not at Start. Once attached this is a field check.
        TryAttachConsoleFeed();

        while (_pending.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                // The waiting request has already been answered or timed out by
                // its own handler; what must not happen is this escaping into
                // the engine's think.
                _hooks.Log.Warning($"[SourceSharp/DebugApi] queued work threw: {e.Message}\n");
            }
        }

        AdvanceWaits();
        SampleEntities();
        NoteTick();
    }

    //-------------------------------------------------------------------------
    // The exit record: who asked this process to end. See ExitRecord.
    //-------------------------------------------------------------------------

    /// <summary>Where this process's exit record goes: beside its socket, named by pid.</summary>
    public string ExitRecordPathFor(int pid) => Path.Combine(SocketDirectory, $"{pid}.exit.json");

    private string ExitRecordPath => ExitRecordPathOverride ?? ExitRecordPathFor(Environment.ProcessId);

    /// <summary>
    /// A test's own record file: every test server in a test process has the
    /// same pid, and so the same default path.
    /// </summary>
    internal string? ExitRecordPathOverride { get; init; }

    private readonly object _quitLock = new();
    private QuitRequest? _quitRequest;
    private long _commandSeq;
    private readonly Queue<(long Seq, long AtUnixMs, string Command)> _recentCommands = new();
    private const int RecentCommands = 20;
    private int _lastTick;
    private string _lastRecordedMap = "";

    private void NoteTick()
    {
        try
        {
            _lastTick = _hooks.World.Tick;
            _lastRecordedMap = _hooks.Status.CurrentMap;
        }
        catch
        {
            // A diagnostic's diagnostic: the record says tick 0 rather than
            // this taking the frame down.
        }
    }

    /// <summary>
    /// Arms native's recorders and clears this process's previous record. A
    /// record already here was written by the module a managed reload
    /// replaced -- the process did not end -- so it is not a record of anything.
    /// </summary>
    private void ArmExitRecord()
    {
        try
        {
            Directory.CreateDirectory(SocketDirectory);
            if (File.Exists(ExitRecordPath))
                File.Delete(ExitRecordPath);

            _hooks.Process.Arm(ExitRecordPath);
        }
        catch (Exception e)
        {
            _hooks.Log.Warning($"[SourceSharp/DebugApi] the exit record cannot be armed: {e.Message}\n");
        }
    }

    private void NoteCommand(string command)
    {
        lock (_quitLock)
        {
            long seq = ++_commandSeq;
            _recentCommands.Enqueue((seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), command));
            while (_recentCommands.Count > RecentCommands)
                _recentCommands.Dequeue();

            if (_quitRequest is null && ExitRecord.IsQuitCommand(command))
            {
                var facts = _hooks.Process.Read();
                _quitRequest = new QuitRequest(command, seq, facts.Armed ? facts.NowMs : -1);
            }
        }
    }

    /// <summary>The console's echo of a typed quit, the latest one, or null.</summary>
    private string? TypedQuitLine()
    {
        string? found = null;
        foreach (var line in _console.Since(0, 4096))
        {
            if (ExitRecord.IsTypedQuit(line.Text))
                found = line.Text;
        }

        return found;
    }

    /// <summary>
    /// /status's <c>quit</c>: null until something asks the process to end,
    /// then the reason as the record would give it now.
    /// </summary>
    private string QuitJson()
    {
        QuitRequest? api;
        lock (_quitLock)
            api = _quitRequest;

        var facts = _hooks.Process.Read();
        string? typed = TypedQuitLine();
        if (!ExitRecord.AnyRequest(facts, api, typed))
            return "null";

        var (reason, detail) = ExitRecord.Decide(facts, api, typed, ExitRecord.CommandLineOf);
        return $"{{\"reason\":{Quote(reason)},\"detail\":{detail}}}";
    }

    /// <summary>The record, as Dispose writes it. Internal for the tests.</summary>
    internal string ExitRecordJson()
    {
        QuitRequest? api;
        (long Seq, long AtUnixMs, string Command)[] recent;
        lock (_quitLock)
        {
            api = _quitRequest;
            recent = _recentCommands.ToArray();
        }

        var facts = _hooks.Process.Read();
        string? typed = TypedQuitLine();
        var (reason, detail) = ExitRecord.Decide(facts, api, typed, ExitRecord.CommandLineOf);

        var json = new StringBuilder("{\"pid\":").Append(Environment.ProcessId)
            .Append(",\"reason\":").Append(Quote(reason))
            .Append(",\"detail\":").Append(detail)
            .Append(",\"writtenBy\":\"managed shutdown\"")
            .Append(",\"writtenAt\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .Append(",\"map\":").Append(Quote(_lastRecordedMap))
            .Append(",\"tick\":").Append(_lastTick)
            .Append(",\"facts\":").Append(ExitRecord.FactsJson(facts))
            .Append(",\"recentCommands\":[");

        for (int i = 0; i < recent.Length; i++)
        {
            if (i > 0)
                json.Append(',');
            json.Append("{\"seq\":").Append(recent[i].Seq)
                .Append(",\"at\":").Append(recent[i].AtUnixMs)
                .Append(",\"command\":").Append(Quote(recent[i].Command)).Append('}');
        }

        return json.Append("]}").ToString();
    }

    /// <summary>
    /// Written on every Dispose, atomically (a temporary file renamed over).
    /// A reload's is removed by the next module's Start, so the one left after
    /// the process is gone is the record of how it ended. A server that never
    /// started writes none: it armed nothing and has nothing to say.
    /// </summary>
    private void WriteExitRecord()
    {
        if (_http is null)
            return;

        try
        {
            string path = ExitRecordPath;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, ExitRecordJson() + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e)
        {
            _hooks.Log.Warning($"[SourceSharp/DebugApi] the exit record could not be written: {e.Message}\n");
        }
    }

    //-------------------------------------------------------------------------
    // Waits: checks the game thread runs once per server tick.
    //-------------------------------------------------------------------------

    private readonly TickWaits _waits = new();
    private WaitContext? _waitContext;

    private WaitContext WaitContext
        => _waitContext ??= new WaitContext(_hooks, _events, _sampler.NoteInterest);

    /// <summary>The waits in flight, for the tests.</summary>
    internal int WaitsInFlight => _waits.Count;

    /// <summary>
    /// Its own try, for SampleEntities' reason: no request is waiting on Pump
    /// itself, so a throw here would reach the engine's think. A condition that
    /// throws is answered as failed by TickWaits; this catches the tick read.
    /// </summary>
    private void AdvanceWaits()
    {
        if (_waits.Count == 0)
            return;

        try
        {
            _waits.Advance(WaitContext, _hooks.World.Tick);
        }
        catch (Exception e)
        {
            _hooks.Log.Warning($"[SourceSharp/DebugApi] advancing waits threw: {e.Message}\n");
        }
    }

    /// <summary>
    /// The requests served on a thread of their own, because they wait for the
    /// game: see HttpSocketServer's isLongPoll.
    /// </summary>
    internal static bool IsLongPoll(HttpRequest request)
        => request.Path is "/wait" or "/player/teleport" or "/entities/remove"
        || (request.Path == "/command" && request.Query.TryGetValue("capture", out string? capture)
                                       && capture is not ("" or "0"));

    /// <summary>
    /// ~5 s of server ticks at the 15 ms tick: a wait that names no timeout
    /// still ends, and says it timed out rather than hanging its caller.
    /// </summary>
    internal const int DefaultWaitTicks = 330;

    /// <summary>The most ticks a wait may ask for: about 25 minutes of game.</summary>
    internal const int MaxWaitTicks = 100_000;

    /// <summary>
    /// The wall-clock cap when none is given: ten times what the ticks take
    /// at full speed, so a box running the game six times slower (the gate's
    /// measured busy regime) still answers by ticks, and never under 30 s.
    /// The cap is a safety net for a game that stopped ticking, not a timer.
    /// </summary>
    internal static TimeSpan DefaultWall(int ticks)
        => TimeSpan.FromSeconds(Math.Clamp(ticks * 0.015 * 10, 30, 600));

    /// <summary>
    /// Registers a condition and waits for the game thread to answer it, up to
    /// the wall cap. The game thread is never blocked: it runs the check once
    /// a tick and moves on.
    /// </summary>
    internal HttpReply AwaitCondition(WaitCondition condition, int timeoutTicks, TimeSpan wall,
                                      Func<TickWaits.Waiter, string>? extra = null)
    {
        if (_shuttingDown.IsCancellationRequested)
            return new HttpReply(503, "{\"ok\":false,\"status\":\"cancelled\",\"error\":\"the game thread is going away\"}");

        var waiter = _waits.Add(condition, timeoutTicks);
        if (waiter is null)
            return new HttpReply(503, $"{{\"ok\":false,\"status\":\"refused\",\"error\":\"{TickWaits.MaxWaiters} waits are already in flight\"}}");

        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!waiter.Done.Wait(wall, _shuttingDown.Token) && waiter.End("wallclock"))
                _waits.Remove(waiter);
        }
        catch (OperationCanceledException)
        {
            waiter.End("cancelled");
            _waits.Remove(waiter);
        }

        int status = waiter.Status switch
        {
            "met" => 200,
            "timeout" or "wallclock" => 408,
            "failed" or "stuck" => 409,
            _ => 503,
        };

        var json = new StringBuilder("{\"ok\":").Append(status == 200 ? "true" : "false")
            .Append(",\"status\":").Append(Quote(waiter.Status))
            .Append(",\"condition\":").Append(Quote(condition.Describe()))
            .Append(",\"startTick\":").Append(waiter.StartTick)
            .Append(",\"tick\":").Append(waiter.EndTick)
            .Append(",\"waitedTicks\":").Append(waiter.StartTick < 0 ? 0 : waiter.EndTick - waiter.StartTick)
            .Append(",\"timeoutTicks\":").Append(timeoutTicks)
            .Append(",\"wallMs\":").Append(clock.ElapsedMilliseconds)
            // The event it matched, for an event wait; else the head when it
            // ended, so a caller can read what followed from here.
            .Append(",\"seq\":").Append(waiter.Last.Seq ?? _events.NextSequence)
            .Append(",\"value\":").Append(waiter.Last.Value);

        if (waiter.Status == "wallclock")
            json.Append(",\"note\":").Append(Quote(
                $"the game did not finish {timeoutTicks} tick(s) within {wall.TotalSeconds:0.#} s of wall clock; is it running frames?"));

        if (extra is not null)
            json.Append(extra(waiter));

        return new HttpReply(status, json.Append('}').ToString());
    }

    private HttpReply Wait(HttpRequest request)
    {
        if (!WaitParser.TryParse(key => Get(request, key), _events.NextSequence,
                                 out var condition, out int timeout, out TimeSpan wall, out string? error))
        {
            return new HttpReply(400, $"{{\"ok\":false,\"status\":\"error\",\"error\":{Quote(error!)}}}");
        }

        return AwaitCondition(condition!, timeout, wall);
    }

    /// <summary>
    /// Watches the world change, while anyone is looking.
    ///
    /// Wrapped in its own try because it runs unprompted -- no request is
    /// waiting on it, so a throw here would escape into the engine's think with
    /// nobody to catch it.
    /// </summary>
    private void SampleEntities()
    {
        try
        {
            string map = _hooks.Status.CurrentMap;

            if (map != _lastMap)
            {
                // Every entity of the old map is about to look removed and every
                // entity of the new one newly spawned. Neither is worth saying;
                // the level change is.
                _events.Add(EntityEventKind.Level, "", map,
                    _lastMap.Length == 0 ? $"loaded {map}" : $"{_lastMap} -> {map}", _hooks.World.Now);

                _lastMap = map;
                _sampler.Reset();
            }

            _sampler.Update(EnumerateForSampling, _hooks.World.Now);
        }
        catch (Exception e)
        {
            _hooks.Log.Warning($"[SourceSharp/DebugApi] entity sampling threw: {e.Message}\n");
        }
    }

    /// <summary>
    /// Every entity in the map, as the sampler wants them.
    ///
    /// "*" is the engine's own wildcard: its name matcher returns true as soon
    /// as it meets a star, so a query of one matches everything.
    /// </summary>
    private IEnumerable<(uint Handle, int Index, string ClassName)> EnumerateForSampling()
    {
        foreach (uint entity in _hooks.World.FindAllByClassname("*"))
            yield return (entity, _hooks.World.GetIndex(entity), _hooks.World.GetClassName(entity));
    }

    private void OnOutputFired(DebugOutputFired fired)
        => _events.Add(EntityEventKind.Output, fired.ClassName, fired.Output,
            fired.Value is null ? "fired" : $"fired with '{fired.Value}'", _hooks.World.Now,
            fired.Index, fired.Handle);

    public void Dispose()
    {
        // FIRST, BEFORE THE JOIN BELOW. Every request waiting on the game
        // thread is waiting on THIS thread, which is about to block in
        // _http.Dispose()'s Thread.Join. See _shuttingDown for the measurement.
        try { _shuttingDown.Cancel(); }
        catch { /* shutting down; a failure here has nowhere useful to go */ }

        // And every wait: the thread that would have evaluated them is the one
        // tearing this down. Their sessions answer "cancelled" and end, which
        // is what lets HttpSocketServer.Dispose join them.
        _waits.CancelAll();

        // Only the subscription Start took. A server that never started has
        // none, and ending this one leaves every other subscriber's feed alone.
        _logFeed?.Dispose();
        _logFeed = null;

        _outputFeed?.Dispose();
        _outputFeed = null;
        _consoleFeed?.Dispose();
        _consoleFeed = null;
        _consoleTail?.Dispose();

        // After the tail has stopped, so its last lines can be read here: a
        // typed quit reaches the file moments before this runs.
        _consoleTail?.DrainAfterDispose();
        WriteExitRecord();

        // The SIGTERM-exits-139 fix (docs/todo.md): SDL's quit watch and the
        // signal handlers live in code this module owns, and the module can
        // be unmapped right after this returns -- on a hot reload, or when
        // the engine finishes its last frames. Left installed, SDL's next
        // pump calls a freed OnSdlEvent and the process dies with 139. The
        // record above is already written, so the facts are safe to release.
        if (_http is not null)
            _hooks.Process.Disarm();

        _http?.Dispose();

        // Before the socket file below, and for the same reason the HTTP server
        // is disposed rather than abandoned: its accept thread and its session
        // tasks pin this side's collectible load context until they come off the
        // stack.
        _debugChannel?.Dispose();
        _debugChannel = null;

        // The socket file would otherwise stay behind and look like a live
        // instance to anything listing the directory.
        if (_socketPath is not null)
        {
            try { File.Delete(_socketPath); }
            catch { /* shutting down; a leftover file is not worth a fuss */ }
        }

        // LAST, after every waiter it woke has come off its Wait. Disposing a
        // CancellationTokenSource while a Wait still holds its token is the one
        // ordering this class must not get wrong.
        _shuttingDown.Dispose();
    }

    /// <summary>
    /// Whether <see cref="Dispose"/> has told the waiters to give up.
    ///
    /// <para>
    /// Exposed for the tests, which is the whole of its justification: the
    /// deadlock this breaks needs a game thread and a socket to reproduce, and
    /// what CAN be checked without either is that a request made after the
    /// cancellation answers at once instead of waiting for a pump that will
    /// never come.
    /// </para>
    /// </summary>
    internal bool IsShuttingDown => _shuttingDown.IsCancellationRequested;

    /// <summary>
    /// <see cref="OnGameThread"/>, reachable by a test. Same reason as
    /// <see cref="IsShuttingDown"/>.
    /// </summary>
    internal string RunOnGameThreadForTest(Func<string> work, string onTimeout, TimeSpan timeout)
        => OnGameThread(work, onTimeout, timeout);

    //-------------------------------------------------------------------------
    // Routing. Separated from the transport so it can be driven directly by a
    // test rather than only through a socket.
    //-------------------------------------------------------------------------
    internal HttpReply Route(HttpRequest request)
    {
        switch (request.Path)
        {
            case "":
                return Ok(Help());

            case "/status":
                return Ok(Status());

            // Counter groups as JSON numbers: what the console readouts print.
            case "/counters":
                return Ok(Counters(Get(request, "group")));

            // Until something is true, counted in server ticks. See TickWaits.
            case "/wait":
                return Wait(request);

            case "/log":
                return Ok(Log(_log, Get(request, "since"), Get(request, "max")));

            case "/console":
                return Ok(Console(Get(request, "since"), Get(request, "max")));

            case "/command":
                return Ok(Command(request.Body.Length > 0 ? request.Body : Get(request, "c"),
                                  Get(request, "capture") is { } capture && capture is not ("" or "0")));

            case "/convar":
                return Ok(ConVar(Get(request, "name"), Get(request, "value") ?? NullIfEmpty(request.Body)));

            case "/entities":
                return Ok(Entities(Get(request, "class")));

            // One entity, by index, handle or name, with what a placement or
            // a settle check needs and the listing does not carry: velocity,
            // whether its physics object sleeps, and what a player stands on.
            case "/entity":
                return Ok(OneEntity(request));

            // Listings. The first three read managed registries only, so they
            // never touch the game thread — which matters because the UI polls
            // them and a debugging tool must not be able to stall a frame.
            case "/convars":
                return Ok(ConVars(Get(request, "source")));

            case "/commands":
                return Ok(Commands(Get(request, "source")));

            case "/entityclasses":
                return Ok(EntityClasses());

            case "/services":
                return Ok(Services(Get(request, "by")));

            // The schema, and one type's fields. Both read metadata computed at
            // startup, so neither waits for a frame -- which matters because
            // this is the view somebody keeps open beside a game they are
            // debugging.
            //
            // What they replace is cs_sv_dump_nettable, which Step 7 deleted:
            // every prop is named p000..p511 and the real names live only in C#,
            // so a console dump cannot say what byte 12 means any more.
            // What the payload actually costs, by class and by field. The
            // question "which field is costing bandwidth" has never had an
            // answer in this tree, and the generic payload is what makes it
            // answerable: the delta is determined by which words differ between
            // encodes, and that is observable without a packet capture.
            case "/nettraffic":
                if (Get(request, "reset") is not null)
                {
                    _hooks.NetTraffic.Reset();
                }

                // Off the game thread: the profiler accumulates there and this only
                // reads what it has already totalled, so the view never costs a frame.
                return Ok(NetTrafficJson.Report(_hooks.NetTraffic.Report()));

            case "/nettables":
                return Ok(NetTables());

            // WHAT THE CLIENT'S PREDICTION COMPARES SAW, and how far apart the
            // two sides actually were. The console meter can say "10 byte(s)
            // differ"; only this can say whether that is a hair or a metre, and
            // whether it sat inside the field's declared tolerance.
            //
            // Answered from the debug channel's mirror without the game thread:
            // the ring is locked and the records are immutable once made, so a
            // reader watching a game does not wait for a frame -- which matters
            // most exactly when prediction is misbehaving.
            case "/prediction":
                if (Get(request, "reset") is not null)
                {
                    // The mirror, the only ring this server holds (Q1: a client's
                    // own ring is its dev API's to reset, never the server's).
                    _debugChannel?.Mirror.Reset();
                }

                return Ok(Prediction(Get(request, "since"), Get(request, "max")));

            // Whether a client-to-server debug channel exists, and what has come
            // up it. Its own route rather than a field on /status because a
            // server with no channel is the normal case and should not have to
            // carry an object's worth of nulls in every status poll.
            case "/debugchannel":
                return Ok(DebugChannel());

            // The clients whose dev API is attached, by player slot
            // (plan_side_isolation ruling 12): what /client/{slot}/... can ask.
            case "/clients":
                return Ok(AttachedClients());

            case "/instances":
                return Ok(OnGameThread(EntityInstances, TimedOut));

            // The map, read from the file rather than the engine. No game
            // thread, no running game needed -- a map's entities and its I/O
            // graph are properties of the map, not of a session.
            case "/map":
                return Ok(MapInfo(Get(request, "map")));

            case "/map/entities":
                return Ok(MapEntities(Get(request, "map")));

            case "/map/geometry":
                return Ok(MapGeometry(Get(request, "map")));

            // The whole file rather than the entities in it: the header, where
            // every lump sits, and the contents of the ones whose format is
            // known. Its own endpoint because it reads lumps nothing else looks
            // at -- the pakfile and the static props among them -- and the
            // entity views should not pay for that on every poll.
            case "/map/file":
                return Ok(MapFile(Get(request, "map")));

            // One lump, decoded. Separate from the report because these are the
            // lumps with tens of thousands of records in them, and sending all
            // of them with every read would mean paying megabytes to look at
            // something else.
            case "/map/lump":
                return Ok(MapLump(Get(request, "map"), Get(request, "lump")));

            // Writing the map back. A POST because the whole entity list is the
            // payload, and the only thing that can be trusted to describe what
            // the map should now contain.
            case "/map/save":
                return Ok(SaveMap(Get(request, "map"), Get(request, "as"), request.Body));

            case "/events":
                return Ok(Events(request));

            case "/events/head":
                return Ok(Heads());

            // Firing at the world rather than reading it. Both run a console
            // command on the game thread, which is the only way in that does
            // not need an engine call this build does not have.
            case "/entity/input":
                return Ok(FireInput(Get(request, "target"), Get(request, "input"),
                                    Get(request, "param"), Get(request, "delay"), Get(request, "activator")));

            // Which entities this one is touching right now, read from the
            // managed touch directory. It is what a touch EDGE was computed from,
            // so a test can name the pair rather than infer it from a total.
            case "/entity/touching":
                return Ok(Touching(Get(request, "index")));

            case "/entity/output":
                return Ok(FireOutput(Get(request, "map"), Get(request, "entity"), Get(request, "output")));

            // Hurting something, which is NOT a console command and is the
            // point. ent_fire is FCVAR_CHEAT, so /entity/input reports success
            // and does nothing at sv_cheats 0 -- and "the entity ignored my
            // input" and "the command was refused" look identical from here.
            // This calls the damage path directly and cannot be refused.
            case "/entity/damage":
                return Ok(Damage(Get(request, "target"), Get(request, "class"),
                                 Get(request, "amount"), Get(request, "type"),
                                 Get(request, "at"), Get(request, "from")));

            // Bringing a saved group back. A POST because a group is a nested
            // structure, and squeezing keyvalues that can contain any character
            // through a query string would mean inventing an encoding for them.
            case "/entities/spawn":
                return Ok(SpawnGroup(request.Body));

            // Placing the player and getting rid of things, each answering
            // only once the world shows it (settle, removal) -- by ticks.
            case "/player/teleport":
                return Teleport(request);

            case "/entities/remove":
                return RemoveEntities(request);

            case "/player/aim":
                return Ok(OnGameThread(() => PlayerAim(Get(request, "player")), TimedOut));

            case "/entity/connect":
                return Ok(Connect(Get(request, "source"), Get(request, "output"),
                                  Get(request, "target"), Get(request, "input"),
                                  Get(request, "param"), Get(request, "delay"), Get(request, "times")));

            default:
                if (request.Path.StartsWith("/ui", StringComparison.Ordinal))
                    return UiAssets.Serve(request.Path);

                // Paths with something in the middle of them, which the switch
                // above cannot express. Both are named in Part 13 with the id
                // and the index IN the path rather than in a query, and they are
                // spelled the way the plan spells them.
                if (request.Path.StartsWith(NetTablePrefix, StringComparison.Ordinal))
                    return Ok(NetTable(request.Path[NetTablePrefix.Length..]));

                if (TryPayloadPath(request.Path, out int payloadIndex))
                    return Ok(Payload(payloadIndex));

                if (TryClientPath(request.Path, out int slot, out string question))
                    return ClientQuery(slot, question, request.Query);

                return new HttpReply(404,
                    $"{{\"error\":\"no such endpoint\",\"path\":{Quote(request.Path)}}}");
        }
    }

    private const string TimedOut = "{\"error\":\"timed out; is the game running frames?\"}";

    //-------------------------------------------------------------------------
    // A CLIENT'S OWN STATE, proxied over the debug channel (plan_side_isolation
    // §2.6, rulings 11-12). One socket per process, the server's: a request for
    // /client/{slot}/{question} becomes one query to the session bound to that
    // slot, and its reply is this route's answer. The questions are the closed
    // set DebugChannelQuery, so no path a caller writes reaches the client.
    //-------------------------------------------------------------------------

    private const string ClientPrefix = "/client/";

    /// <summary>How long a proxied question waits for the client before giving up.</summary>
    internal TimeSpan ClientQueryTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal static bool TryClientPath(string path, out int slot, out string question)
    {
        slot = 0;
        question = "";

        if (!path.StartsWith(ClientPrefix, StringComparison.Ordinal))
            return false;

        string rest = path[ClientPrefix.Length..];
        int cut = rest.IndexOf('/');
        if (cut <= 0 || !int.TryParse(rest.AsSpan(0, cut), out slot) || slot <= 0 || cut + 1 >= rest.Length)
        {
            slot = 0;
            return false;
        }

        // The rest of the path, which may have segments of its own
        // (entity/{n}, reload/trigger): ClientQuery matches it against the
        // closed table, whole.
        question = rest[(cut + 1)..];
        return true;
    }

    /// <summary>
    /// The client questions by route spelling (plan_client_novalve §4.4), and
    /// the query-string key whose value travels as the question's argument,
    /// if it takes one there.
    /// </summary>
    /// <remarks>
    /// A spelling ending in <c>/{n}</c> takes the path's last segment as its
    /// argument (<c>entity/{n}</c>). In the order a 404 lists them.
    /// </remarks>
    internal static readonly IReadOnlyList<(string Spelling, DebugChannelQuery Query, string? Key)> ClientQuestionTable =
    [
        ("status", DebugChannelQuery.Status, null),
        ("prediction", DebugChannelQuery.Prediction, null),
        ("frame", DebugChannelQuery.Frame, null),
        ("entities", DebugChannelQuery.Entities, "class"),
        ("nettables", DebugChannelQuery.NetTables, null),
        ("entity/{n}", DebugChannelQuery.Entity, null),
        ("reload", DebugChannelQuery.Reload, null),
        ("reload/trigger", DebugChannelQuery.ReloadTrigger, null),
        ("netgraph", DebugChannelQuery.NetGraph, "frames"),
        ("absences", DebugChannelQuery.Absences, null),
        ("ui", DebugChannelQuery.Ui, "svg"),
        ("ui/fonts", DebugChannelQuery.UiFonts, "sweep"),
        ("ui/menu", DebugChannelQuery.UiMenu, "svg"),
        ("ui/event", DebugChannelQuery.UiEvent, "e"),
        ("proxies", DebugChannelQuery.Proxies, "filter"),
        ("viewcam", DebugChannelQuery.ViewCam, "mode"),
        ("render", DebugChannelQuery.Render, "reset"),
        ("render/compare", DebugChannelQuery.RenderCompare, "mutate"),
        ("particles", DebugChannelQuery.Particles, null),
        ("input", DebugChannelQuery.Input, null),
        ("input/inject", DebugChannelQuery.InputInject, "e"),
    ];

    private const string TailPlaceholder = "/{n}";

    private static readonly IReadOnlyDictionary<string, (DebugChannelQuery Query, string? Key)> ClientQuestions =
        ClientQuestionTable.Where(q => !q.Spelling.EndsWith(TailPlaceholder, StringComparison.Ordinal))
                           .ToDictionary(q => q.Spelling, q => (q.Query, q.Key), StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, DebugChannelQuery> ClientQuestionsWithTail =
        ClientQuestionTable.Where(q => q.Spelling.EndsWith(TailPlaceholder, StringComparison.Ordinal))
                           .ToDictionary(q => q.Spelling[..^TailPlaceholder.Length], q => q.Query, StringComparer.Ordinal);

    /// <summary>
    /// The question and its argument a client route names, or false for a
    /// spelling outside the table: nothing a caller writes beyond the table
    /// reaches the client.
    /// </summary>
    internal static bool TryClientQuestion(string question, IReadOnlyDictionary<string, string> query,
                                           out DebugChannelQuery asked, out string argument)
    {
        argument = "";

        if (ClientQuestions.TryGetValue(question, out var named))
        {
            asked = named.Query;
            if (named.Key is { } key && query.TryGetValue(key, out string? value))
                argument = value;
            return true;
        }

        int cut = question.LastIndexOf('/');
        if (cut > 0 && cut + 1 < question.Length
            && ClientQuestionsWithTail.TryGetValue(question[..cut], out asked))
        {
            argument = question[(cut + 1)..];
            return argument.Length <= DebugChannelCodec.MaxStringBytes;
        }

        asked = default;
        return false;
    }

    private HttpReply ClientQuery(int slot, string question, IReadOnlyDictionary<string, string> parameters)
    {
        if (!TryClientQuestion(question, parameters, out var query, out string argument))
            return new HttpReply(404, $"{{\"error\":\"no such client question\",\"question\":{Quote(question)}," +
                                      $"\"expected\":[{string.Join(",", ClientQuestionTable.Select(q => Quote(q.Spelling)))}]}}");

        if (_debugChannel is null)
            return new HttpReply(503, "{\"error\":\"no debug channel: start the server with -debuguiserve\"}");

        if (_debugChannel.Query(slot, query, ClientQueryTimeout, argument) is not { } reply)
            return new HttpReply(404, $"{{\"error\":\"no client attached in that slot, or it did not answer\",\"slot\":{slot}}}");

        return new HttpReply(reply.Status, reply.Body);
    }

    private string AttachedClients()
    {
        var slots = _debugChannel?.AttachedSlots() ?? [];
        return $"{{\"slots\":[{string.Join(",", slots)}]}}";
    }

    private static HttpReply Ok(string json) => new(200, json);

    private static string? Get(HttpRequest request, string key)
        => request.Query.TryGetValue(key, out string? value) ? value : null;

    private static string? NullIfEmpty(string text) => text.Length > 0 ? text : null;

    private static string Help() => """
        {
          "endpoints": {
            "GET  /status":                "map, time, tick count, the ABI crossing counters and consoleLinesLost (engine console lines the native buffer could not hold)",
            "GET  /counters?group=G":      "counter groups as numbers: physics (cs_sv_physics_events), playershadow (cs_sv_playershadow)",
            "GET  /wait?<one condition>&timeout=TICKS&wall=S": "until a condition holds, checked once per server tick: ticks=N, field=F (with index|handle|name, op, value), exists=1, gone=1, event=KIND, settled=1, census=N, io=empty",
            "GET  /log?since=N&max=M":     "managed log lines at or after sequence N",
            "GET  /console?since=N&max=M": "the game's own console, as -condebug writes it",
            "POST /command  (body=cmd)":   "run a console command on the game thread; ?capture=1 answers what it printed",
            "GET  /convar?name=X&value=Y": "read a convar, or set it if value is given",
            "GET  /entities?class=X":      "list entities of a class",
            "GET  /entity?index=|handle=|name=": "one entity: origin, angles, velocity, speed, asleep, ground",
            "GET  /convars?source=X":      "convars: source=managed (default), native, or all",
            "GET  /commands?source=X":     "commands: source=managed (default), native, or all",
            "GET  /entityclasses":         "declared entity classes, with inputs and outputs",
            "GET  /services?by=X":         "the server's service container: by=type (default), name, or all",
            "GET  /instances":             "live scripted entities, with their current values",
            "GET  /nettables":             "the networked schema: every type, its table, props, layout and hashes",
            "GET  /nettraffic?reset=1":    "payload cost by class and by field; needs cs_sv_nettable_profile 1",
            "GET  /prediction?since=N&max=M&reset=1": "client prediction misses: per field, per component, against its declared tolerance",
            "GET  /debugchannel":          "the client-to-server debug channel: whether this server opted in, and what has come up it",

            "GET  /nettable/<id|class>":   "one type's fields: name, C# type, blob offset, size",
            "GET  /entity/<index>/payload": "one entity's blob: hex, decoded through its layout, and what changed",
            "GET  /map":                   "the loaded map, and which others are on disk",
            "GET  /map/entities?map=X":    "entities baked into the map file, with their connections",
            "GET  /map/geometry?map=X":    "world surfaces and brush model bounds, for drawing",
            "GET  /map/file?map=X":        "everything in the map file: lumps, materials, props, pakfile",
            "GET  /map/lump?map=X&lump=N": "one big lump decoded: faces, texinfo, leaves, brushes, brush sides",
            "POST /map/save?map=X&as=Y":   "write an edited entity list back, to the same map or a new one",
            "GET  /events?since=N&max=M&kind=K&name=G&classname=G&handle=H&index=I": "entity events: spawns, removals, inputs, outputs; filtered server-side",
            "GET  /events/head":           "the head sequence of the event ring, the log and the console",
            "GET  /entity/input":          "target, input, param, delay - fires an input at an entity",
            "GET  /entity/output":         "entity, output - fires everything that output connects to",
            "GET  /entity/damage":         "target (name or #index) or class, amount, type, at=x,y,z, from=name|#index - hurts an entity; needs no cheats",
            "GET  /entity/connect":        "source, output, target, input - wires two live entities together",
            "POST /entities/spawn":        "create a saved group, at its own origins or where a player looks",
            "GET  /player/aim":            "where a player is looking, and what they are looking at",
            "GET  /player/teleport?player=N&pos=x,y,z&ang=p,y,r&settle=1": "teleport a player (setpos_exact, no cheats); with settle, answer once it has landed",
            "GET  /entities/remove?name=G|class=G|index=I|handle=H": "remove what matches; answer once it is gone",
            "GET  /ui":                    "the web interface"
          }
        }
        """;

    //-------------------------------------------------------------------------
    // Status answers without the game thread.
    //
    // It used to go through the queue like everything else, which made it
    // useless for the one thing it is for: a game still loading a map is not
    // ticking yet, so asking whether it was alive timed out and looked exactly
    // like a game that was not. Now the parts the listener knows come back
    // immediately, and the parts only the game knows are attempted briefly and
    // reported as absent if it is not running frames.
    //-------------------------------------------------------------------------
    private string Status()
    {
        string game = OnGameThread(
            () => $",\"ticking\":true,\"time\":{F(_hooks.World.Now)}{GameJson(_hooks.Status.Game())}",
            ",\"ticking\":false",
            StatusTimeout);

        var c = _hooks.Status.Counters();

        return $"{{\"ok\":true,\"pid\":{Environment.ProcessId}," +
               $"\"address\":{Quote(Address)},\"build\":{Build()}," +
               $"\"crossings\":{CrossingsJson()},\"think\":{ThinkJson(c.Think)}" +
               $",\"io\":{IoJson(c.Io)},\"touch\":{TouchJson(c.Touch)},\"mapSpawn\":{MapSpawnJson(c.MapSpawn)}" +
               $",\"factories\":{FactoriesJson(c.Factories)}" +
               $",\"logical\":{LogicalJson(c.Logical)},\"reload\":{ReloadJson(_hooks.Status.Reload())}" +
               $",\"overlay\":{OverlayJson(c.Overlay)},\"quit\":{QuitJson()}" +
               $",\"consoleLinesLost\":{_hooks.Log.ConsoleLinesLost}{game}}}";
    }

    /// <summary>
    /// THE CROSSING COUNTERS, on the route that is already polled once a second
    /// — <c>plan_entity.md</c> §3.15 and step 0c.
    ///
    /// <para>
    /// OUTSIDE <c>OnGameThread</c>, deliberately and not by accident of where
    /// the line was put. <see cref="Crossings"/> is a lock-free array of
    /// interlocked longs, so reading it from the listener thread cannot stall a
    /// frame — and a diagnostic that could make the thing it is watching worse
    /// is the failure the listings above this method already argue against. It
    /// also means the numbers still come back from a game that has stopped
    /// ticking, which is exactly the session someone would be reading them in.
    /// </para>
    ///
    /// <para>
    /// ONLY THE OPS THAT HAVE BEEN ENTERED. There are 162 of them and a running
    /// server enters a couple of dozen; 140 zeroes in a poll that happens every
    /// second is how a field stops being read, and every zero op still answers
    /// by name through <see cref="Crossings.Read"/>. An op absent from
    /// <c>ops</c> has a count of zero — the page must not render it as unknown.
    /// </para>
    ///
    /// <para>
    /// <c>serverFrames</c> and <c>clientFrames</c> are repeated out of
    /// <c>ops</c> as their own fields because they are the DENOMINATOR: every
    /// budget in the plan is a per-frame rate, and a consumer that had to know
    /// which op name meant "a frame" would be encoding that here anyway.
    /// <c>resets</c> is what tells a consumer the interval changed under it;
    /// two polls with different <c>resets</c> are not two points on one curve.
    /// </para>
    ///
    /// <para>
    /// INBOUND ONLY, and the field says so rather than leaving the reader to
    /// assume. These are native-&gt;managed thunk entries; the other direction
    /// belongs to step 0e's engine-boundary probes.
    /// </para>
    /// </summary>
    /// <summary>
    /// The server think heap, beside the crossing counters and for the reason
    /// <see cref="IDebugStatus.Counters"/> gives. Absent numbers read zero rather than
    /// the object being missing, so a client can index it unconditionally.
    /// </summary>
    /// <summary>
    /// The server I/O queue, beside the crossing counters and for the reason
    /// <see cref="IDebugStatus.Counters"/> gives. Absent numbers read zero rather than the
    /// object being missing, so a client can index it unconditionally.
    /// </summary>
    /// <summary>
    /// WHAT THE LAST HOT RELOAD ACTUALLY CARRIED, per tier.
    ///
    /// <para>
    /// <b>THE FAILURE MODE THIS EXISTS FOR IS INVISIBLE.</b> A class that
    /// declares state and gets none of it carried looks exactly like a class
    /// with no state; a subsystem whose <c>OnSave</c> stopped being called looks
    /// exactly like one that had nothing to say. Both were true in this tree at
    /// once — the think heap was measured at 8 bookings before a
    /// <c>cs_sv_reload</c> and 0 after it, and the map's pickups were placed a
    /// second time — with <c>make check</c> and the live gate both green.
    /// </para>
    ///
    /// <para>
    /// So the tally is a NUMBER a gate can assert on, in the place the crossing
    /// counters and <c>status.think</c> already are, rather than a log line a
    /// refactor can rename. <c>saved</c> is what the unload put aside;
    /// <c>restored</c> is what the load put back. The two differing is the
    /// signal.
    /// </para>
    ///
    /// <para>
    /// FROM <see cref="IDebugStatus.Reload"/>, WHICH THE SERVER READS STRAIGHT OFF
    /// ITS RELOAD STORE, unlike every other block here, and the difference is the
    /// point: the module that took the save is GONE by the time anyone can ask
    /// about it. A counter the module kept would answer about the wrong half of
    /// the reload. The store is in the default load context and is the only
    /// thing that saw both.
    /// </para>
    ///
    /// <para>
    /// ALL ZEROES BEFORE THE FIRST RELOAD, which is a real reading and not an
    /// absence — a consumer indexes it unconditionally.
    /// </para>
    /// </summary>
    private static string ReloadJson(DebugReload r)
    {

        // WHAT COULD NOT BE PUT BACK, as its own block rather than as the
        // difference of the two above. `saved - restored` is not the same
        // question: a record whose entity died between the save and the restore
        // is dropped, and that is the reload working. This block counts the
        // entity that is STILL STANDING with no managed half -- which stops
        // thinking, stops taking inputs and stops firing outputs, and looks
        // completely normal from every other reading in this status.
        return "{\"saved\":" + TallyJson(r.Saved)
             + ",\"restored\":" + TallyJson(r.Restored)
             + ",\"failed\":{\"entities\":" + r.FailedEntities
             + ",\"first\":" + Quote(r.FailedFirst ?? "")
             + "}"
             + ",\"waiting\":{\"entities\":" + r.WaitingEntities
             + ",\"parts\":" + r.WaitingParts
             + ",\"io\":" + r.WaitingIo
             + ",\"shellLess\":" + r.WaitingShellLess
             + "}}";
    }

    private static string TallyJson(DebugReloadTally t)
        => "{\"entities\":" + t.Entities
         + ",\"classes\":" + t.Classes
         + ",\"fields\":" + t.Fields
         + ",\"bytes\":" + t.Bytes
         + ",\"parts\":" + t.Parts
         + ",\"partFields\":" + t.PartFields
         + ",\"partBytes\":" + t.PartBytes
         + ",\"ioEvents\":" + t.IoEvents
         + ",\"ioConnections\":" + t.IoConnections
         + ",\"shellLess\":" + t.ShellLess
         + "}";

    private static string IoJson(DebugIo r)
    {
        return $"{{\"pending\":{r.Pending},\"connections\":{r.Connections},"
             + $"\"deliveredLastFrame\":{r.DeliveredLastFrame},\"deliveredTotal\":{r.DeliveredTotal},"
             + $"\"unhandled\":{r.Unhandled},\"cyclesRefused\":{r.CyclesRefused},"
             + $"\"unhandledDistinct\":{r.UnhandledDistinct},\"postedAfterThePass\":{r.PostedAfterThePass},"
             + $"\"sdkAsks\":{r.SdkAsks},\"sdkTook\":{r.SdkTook}}}";
    }

    /// <summary>
    /// The native overlay library's four counters and its viewer flag, beside
    /// the crossing counters and for the reason <see cref="IDebugStatus.Counters"/>
    /// gives. Absent numbers read zero rather than the object being missing, so
    /// a client can index it unconditionally — and an absent report reads
    /// <c>viewerSet:false</c>, which is the same thing a server with nobody
    /// looking reports, so the two are told apart by the counters and not by
    /// the object's presence.
    /// </summary>
    private static string OverlayJson(DebugOverlayCounts r)
    {
        return $"{{\"drawn\":{r.Drawn},\"clipped\":{r.Clipped},"
             + $"\"noViewer\":{r.NoViewer},\"noOverlay\":{r.NoOverlay},"
             + $"\"viewerSet\":{(r.ViewerSet ? "true" : "false")}}}";
    }

    /// <summary>
    /// The server touch link table, beside the crossing counters and for the
    /// reason <see cref="IDebugStatus.Counters"/> gives. Absent numbers read zero rather
    /// than the object being missing, so a client can index it unconditionally.
    /// </summary>
    private static string TouchJson(DebugTouch r)
    {
        return $"{{\"pairs\":{r.Pairs},\"links\":{r.Links},"
             + $"\"edgesLastFrame\":{r.EdgesLastFrame},\"edgesTotal\":{r.EdgesTotal},"
             + $"\"capacity\":{r.Capacity},\"regrows\":{r.Regrows},\"refused\":{r.Refused}}}";
    }

    /// The managed map spawner's last load, beside the crossing counters and for
    /// the reason <see cref="IDebugStatus.Counters"/> gives.
    /// </summary>
    private static string MapSpawnJson(DebugMapSpawn r)
    {
        return $"{{\"blocks\":{r.Blocks},\"managedBlocks\":{r.ManagedBlocks},"
             + $"\"nativeBlocks\":{r.NativeBlocks},\"managedKeys\":{r.ManagedKeys},"
             + $"\"createCrossings\":{r.CreateCrossings},\"spawnCrossings\":{r.SpawnCrossings}}}";
    }

    /// <summary>
    /// The native factory dictionary as the managed side sees it. See
    /// <see cref="IDebugStatus.Counters"/> for why <c>available</c> is reported beside
    /// the counts rather than inferred from them.
    /// </summary>
    private static string FactoriesJson(DebugFactories r)
    {
        return $"{{\"available\":{(r.Available ? "true" : "false")},\"native\":{r.Native},"
             + $"\"refused\":{r.Refused},\"refusalTimes\":{r.RefusalTimes},"
             + $"\"unanswered\":{r.Unanswered}}}";
    }

    /// <summary>
    /// The shell-less logical entities, beside the counter that says none of
    /// their handles crossed — the reason <see cref="IDebugStatus.Counters"/> gives.
    /// </summary>
    private static string LogicalJson(DebugLogical r)
    {
        return $"{{\"classes\":{r.Classes},\"live\":{r.Live},"
             + $"\"allocated\":{r.Allocated},\"released\":{r.Released},"
             + $"\"exhausted\":{r.Exhausted},\"nativeReach\":{r.NativeReach}}}";
    }

    private static string ThinkJson(DebugThink r)
    {
        return $"{{\"pending\":{r.Pending},\"heapDepth\":{r.HeapDepth},"
             + $"\"ranLastFrame\":{r.RanLastFrame},\"ranTotal\":{r.RanTotal}}}";
    }

    /// <summary>
    /// The game-thread part of /status: the map, the tick count and the
    /// physics tables, in the order the server's old status fragment wrote them.
    /// </summary>
    private static string GameJson(DebugGameStatus g)
        => $",\"map\":{Quote(g.Map)},\"ticks\":{g.Ticks},\"constraints\":{g.Constraints}"
         + $",\"springs\":{g.Springs},\"motionControllers\":{g.MotionControllers}";

    // The writer both halves share (DebugApiJson), so a client's
    // /client/{slot}/status has this route's shape.
    private string CrossingsJson()
    {
        var c = _hooks.Build.Crossings();
        return DebugJson.Crossings(c.Total, c.Resets, c.OpCount, c.ServerFrames, c.ClientFrames,
                                 c.Ops.Select(o => (o.Name, o.Count)), c.EntityOps.Select(o => (o.Name, o.Count)));
    }

    /// <summary>
    /// WHICH BUILD THIS GAME IS RUNNING, on the route the page already polls.
    ///
    /// <para>
    /// Here rather than on a route of its own because it is not a thing anybody
    /// goes looking for: it is the answer to a question that only occurs to
    /// somebody once they are already confused by what they are looking at, and
    /// it has to be in front of them at that moment. /status is polled once a
    /// second by every open page, so it is.
    /// </para>
    ///
    /// <para>
    /// No game thread. Both halves are stamped at load and the freshness check
    /// is a cached stat, so this cannot stall a frame — which matters for the
    /// same reason the listings above say it: a debugging tool must not be able
    /// to make the thing it is watching worse.
    /// </para>
    /// </summary>
    private string Build()
    {
        var build = _hooks.Build.Build();
        return DebugJson.Build(Half(build.Native), Half(build.Managed));
    }

    /// <summary>One build half, as both halves of the dev API render it.</summary>
    internal static string Half(DebugBuildHalf h)
        => DebugJson.BuildHalf(h.Known, h.Hash, h.BuiltAtUnixMs, h.Parts, h.Branch, h.Commit, h.Dirty,
                             h.Source, h.Freshness, h.Path);

    //-------------------------------------------------------------------------
    // The log feed needs no game thread: the ring is already safe to read from
    // anywhere, so polling it never waits on a frame.
    //-------------------------------------------------------------------------
    private string Log(LogRing ring, string? sinceText, string? maxText)
    {
        long since = long.TryParse(sinceText, out long s) ? s : 0;
        int max = int.TryParse(maxText, out int m) && m > 0 ? Math.Min(m, 2000) : 500;

        var lines = ring.Since(since, max);

        var json = new StringBuilder();
        json.Append("{\"next\":").Append(ring.NextSequence).Append(",\"lines\":[");

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            // Milliseconds since the epoch: a number the page can format in the
            // reader's own timezone, rather than a string it would have to parse
            // and then guess the zone of.
            json.Append("{\"seq\":").Append(lines[i].Sequence)
                .Append(",\"at\":")
                .Append(new DateTimeOffset(lines[i].TimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds())
                .Append(",\"warning\":").Append(lines[i].IsWarning ? "true" : "false")
                .Append(",\"text\":").Append(Quote(lines[i].Text)).Append('}');
        }

        return json.Append("]}").ToString();
    }

    /// <summary>
    /// The game's console: from the in-process display-func feed when one is
    /// attached, else as -condebug's file tail brings it.
    ///
    /// Reports where it is looking and whether it found anything, because the
    /// usual reason this is empty is that the game was started without
    /// -condebug -- and an empty feed does not say that on its own.
    /// </summary>
    private string Console(string? sinceText, string? maxText)
    {
        string body = Log(_console, sinceText, maxText);

        return body.TrimEnd('}')
             + $",\"feed\":{Quote(_consoleFeed is not null ? "display-func" : "file-tail")}"
             + $",\"path\":{Quote(_consoleTail?.Path ?? "")}"
             + $",\"following\":{(_consoleFeed is not null || _consoleTail?.IsFollowing == true ? "true" : "false")}"
             + $",\"epoch\":{_epoch}}}";
    }

    /// <summary>
    /// Follows a console file a test names, in place of -condebug's: what a
    /// capture reads, without a game.
    /// </summary>
    internal void StartConsoleTailForTest(string path)
    {
        _consoleTail = new ConsoleTail(_console, path, _hooks.Log.Warning) { PollInterval = TimeSpan.FromMilliseconds(20) };
        _consoleTail.Start();
    }

    /// <summary>How long a capture waits for the console file to show its end marker.</summary>
    internal TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromSeconds(3);

    private long _captures;

    /// <summary>
    /// <c>/command?capture=1</c>: the command, and the console output it
    /// printed.
    ///
    /// <para>
    /// A MANAGED command — one the registry declares a class for — is captured
    /// by RUNNING THAT CLASS: a fresh instance with a sink for its log and
    /// engine console, executed on the game thread, answered from what the
    /// sink collected (user ruling 2026-10-07: command capture is all managed
    /// logic, no native code). No echo markers, no file tail, no wait: the
    /// print arrives in-process, so the reply is complete by construction. The
    /// sink forwards to the live log on the side, so a bystander at the
    /// console or console.log still sees the output; the reply then leaves the
    /// forwarded lines out of the LOG feed, which holds what else logged — the
    /// <c>] command</c> echo above all — rather than the same line twice.
    /// </para>
    ///
    /// <para>
    /// Everything else — engine commands, native-bodied carriers, refused
    /// classes — runs on the game thread as before, and the console output
    /// comes from one of two feeds. With the side's display func attached
    /// (docs/todo.md's console capture), the prints arrive in the console
    /// ring synchronously while the command runs, so the reply is every ring
    /// line from just before the call: no echo markers, no file tail, no
    /// wait. Without it, the command runs between two <c>echo</c>s of a
    /// marker unique to this capture and the reply waits for the tail to
    /// read the closing marker, so it is never half a report. A
    /// multi-statement line (<c>;</c>) always takes the engine path even
    /// when its first word is managed: the engine tokenizes the whole line,
    /// an instance would be handed <c>"a; ss_bar b"</c> as args and run only
    /// the first command, and the answer would quietly omit the second.
    /// </para>
    ///
    /// <para>
    /// WITHOUT a display func AND without -condebug there is neither feed for
    /// the engine path, and it says so and answers the managed log's lines
    /// alone, rather than an empty list that would read as "the command
    /// printed nothing".
    /// </para>
    /// </summary>
    private string CaptureCommand(string command)
    {
        // The managed attempt, decided inside the hand-off below: a name with
        // no declared class answers null and costs the engine path nothing.
        int at = command.IndexOf(' ');
        string name = at < 0 ? command : command[..at];
        string args = at < 0 ? "" : command[(at + 1)..].TrimStart();
        bool single = command.IndexOf(';') < 0;

        long id = Interlocked.Increment(ref _captures);
        // ONE TOKEN EACH, letters, digits and underscores: echo prints what
        // the engine's tokenizer made of its arguments, and a marker of several
        // words came back reordered once in a live run ("[debug-api] capture
        // begin 281206.1" for "... capture 281206.1 begin"), which no reader
        // could find.
        string begin = $"debugapi_capture_{Environment.ProcessId}_{id}_begin";
        string end = $"debugapi_capture_{Environment.ProcessId}_{id}_end";
        var before = Marks();
        long logAfter = before.Log;
        IReadOnlyList<string>? printed = null;
        bool managedRan = false;
        long consoleAfter = before.Console;
        long consoleEnd = long.MaxValue;
        bool engineFed = false;

        string ran = OnGameThread(() =>
        {
            // Said before it is run, the way a console says what was typed into
            // it — on either path, and once: it goes through the log, not the
            // sink, so it belongs to the LOG feed of the reply, never the
            // console feed.
            _hooks.Log.Msg($"] {command}\n");

            if (single)
                printed = _hooks.CommandRun.TryRunManaged(name, args);

            if (printed is not null)
            {
                managedRan = true;
                logAfter = _log.NextSequence;
                return "{\"ok\":true}";
            }

            if (_consoleFeed is not null)
            {
                // No markers, no wait: SCOPED BY THE CONSOLE SEQUENCE
                // (console-batch). The engine's prints sit in the side's
                // native line buffer until drained; the capture drains what
                // came before the command first, so the ring mark taken
                // inside it is the start of the range, and drains the
                // command's own prints before it returns, so the mark taken
                // after it is the end. The ring receives lines in sequence
                // order, so the ring range IS the sequence range -- and, by
                // the console-batch ruling, it holds a line another thread
                // printed inside it too.
                _hooks.Log.Capture(() =>
                {
                    consoleAfter = _console.NextSequence;
                    _hooks.Console.Execute(command);
                });
                consoleEnd = _console.NextSequence;
                logAfter = _log.NextSequence;
                engineFed = true;
                return "{\"ok\":true}";
            }

            _hooks.Console.Execute($"echo {begin}");
            _hooks.Console.Execute(command);
            logAfter = _log.NextSequence;
            _hooks.Console.Execute($"echo {end}");
            return "{\"ok\":true}";
        }, TimedOut);

        if (ran != "{\"ok\":true}")
            return Stamped(ran, before);

        var json = new StringBuilder("{\"ok\":true,\"captured\":{\"log\":[");
        var managed = _log.Since(before.Log, 2000);
        int written = 0;
        foreach (var line in managed)
        {
            if (line.Sequence >= logAfter)
                break;

            // The sink forwarded its lines to the live log for the bystanders,
            // so they cross the ring too; the reply's console feed already
            // holds them, and holding them twice would make the log feed a
            // worse copy of the console one.
            if (managedRan && printed!.Any(p => p.TrimEnd('\n', '\r') == line.Text))
                continue;

            if (written++ > 0)
                json.Append(',');
            json.Append(Quote(line.Text.TrimEnd('\n')));
        }

        json.Append("],\"console\":[");

        bool following = !managedRan && !engineFed && _consoleTail?.IsFollowing == true;
        bool complete = managedRan || engineFed;
        var lines = new List<string>();

        if (engineFed)
        {
            foreach (var line in _console.Since(consoleAfter, 2000))
            {
                if (line.Sequence >= consoleEnd)
                    break;
                lines.Add(line.Text);
            }
        }
        if (following)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!complete && DateTime.UtcNow < deadline && !_shuttingDown.IsCancellationRequested)
            {
                _consoleTail!.Poke();
                lines.Clear();
                bool inside = false;
                foreach (var line in _console.Since(before.Console, 2000))
                {
                    if (line.Text.Contains(end, StringComparison.Ordinal)) { complete = inside; break; }
                    if (inside) lines.Add(line.Text);
                    else if (line.Text.Contains(begin, StringComparison.Ordinal)) inside = true;
                }

                if (!complete)
                    _shuttingDown.Token.WaitHandle.WaitOne(10);
            }
        }
        else if (managedRan)
        {
            foreach (var line in printed!)
                lines.Add(line.TrimEnd('\n', '\r'));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                json.Append(',');
            json.Append(Quote(lines[i]));
        }

        json.Append("],\"complete\":").Append(complete ? "true" : "false")
            .Append(",\"source\":").Append(Quote(managedRan
                ? "managed command instance run with a captured log"
                : engineFed
                    ? "the in-process display-func feed, this command's lines"
                    : following
                        ? "console.log between this command's markers"
                        : "managed log only: no display func is attached and the game was started without -condebug, so the engine's own lines are not visible"))
            .Append('}');

        if (following && !complete)
            json.Append(",\"note\":").Append(Quote($"the closing marker did not reach the console file within {CaptureTimeout.TotalSeconds:0.#} s"));

        return Stamped(json.Append('}').ToString(), before);
    }

    private string Command(string? command, bool capture = false)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "{\"error\":\"no command given\"}";

        // Recorded before it runs: a quit is acted on by the engine after this
        // returns, and by then nothing of this request is left to ask.
        NoteCommand(command);

        if (capture)
            return CaptureCommand(command);

        // The sequence before running, so the caller can ask for exactly the log
        // this command produced rather than guessing at a tail.
        var before = Marks();

        string result = OnGameThread(() =>
        {
            // Said before it is run, the way a console says what was typed into
            // it. Without this a command that prints nothing is indistinguishable
            // from one that never arrived — and the console is a record of what
            // was done to the game, which should not omit the things done to it
            // from here.
            _hooks.Log.Msg($"] {command}\n");

            _hooks.Console.Execute(command);
            return "{\"ok\":true}";
        }, TimedOut);

        return Stamped(result, before);
    }

    /// <summary>
    /// Counter groups -- what cs_sv_physics_events and cs_sv_playershadow print
    /// -- as numbers by name. The gate ran the command and regex-parsed the
    /// log; a renamed line broke the parse and read as a zero.
    /// </summary>
    private string Counters(string? group)
    {
        var groups = _hooks.CounterGroups;
        var names = string.IsNullOrEmpty(group) ? groups.Names : [group];

        return OnGameThread(() =>
        {
            var json = new StringBuilder("{\"tick\":").Append(_hooks.World.Tick).Append(",\"groups\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (groups.Read(names[i]) is not { } read)
                {
                    return $"{{\"error\":\"no such counter group\",\"group\":{Quote(names[i])}," +
                           $"\"expected\":[{string.Join(",", groups.Names.Select(Quote))}]}}";
                }

                if (i > 0)
                    json.Append(',');

                json.Append("{\"name\":").Append(Quote(read.Name))
                    .Append(",\"readout\":").Append(Quote(read.Readout))
                    .Append(",\"available\":").Append(read.Available ? "true" : "false")
                    .Append(",\"counters\":{");

                for (int c = 0; c < read.Counters.Count; c++)
                {
                    if (c > 0)
                        json.Append(',');
                    json.Append(Quote(read.Counters[c].Name)).Append(':').Append(read.Counters[c].Value);
                }

                json.Append("}}");
            }

            return json.Append("]}").ToString();
        }, TimedOut);
    }

    private string Touching(string? index)
    {
        if (!int.TryParse(index, out int entity))
            return "{\"error\":\"index required\"}";

        if (!_hooks.World.HasTouchDirectory)
            return "{\"error\":\"no touch directory\"}";

        // On the game thread: an entity's index is an engine read.
        return OnGameThread(() =>
            $"{{\"index\":{entity},\"touching\":[{string.Join(",", _hooks.World.Touching(entity))}]}}",
            TimedOut);
    }

    private string ConVar(string? name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "{\"error\":\"no convar named\"}";

        return OnGameThread(() =>
        {
            // Present-but-empty is a write of "", not a read. Only an absent
            // parameter means "just tell me the value" — without that
            // distinction a string convar could never be cleared, which is a
            // thing the UI has a button for.
            if (value is not null)
                _hooks.Console.Execute($"{name} {ConsoleArgument(value)}");

            // Read back rather than echoing what was asked for: a convar can
            // clamp, reject, or not exist, and the difference is the whole point
            // of looking.
            return $"{{\"name\":{Quote(name)},\"value\":{Quote(ReadConVar(name))}}}";
        }, TimedOut);
    }

    /// <summary>
    /// Live entities of a class.
    ///
    /// A class of "*" is every entity in the map: the engine's name matcher
    /// treats a leading star as matching anything, which is what makes a whole-
    /// map live view possible without a new engine call for it.
    ///
    /// The classname is sent per entity rather than echoed once from the query,
    /// because with a wildcard the query does not say what any of them are.
    /// </summary>
    private string Entities(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return "{\"error\":\"no class given\"}";

        return OnGameThread(() =>
        {
            var json = new StringBuilder("{\"class\":").Append(Quote(className)).Append(",\"entities\":[");
            int count = 0;

            foreach (uint entity in _hooks.World.FindAllByClassname(className))
            {
                if (count++ > 0)
                    json.Append(',');

                AppendEntity(json, entity);
            }

            return json.Append("],\"count\":").Append(count).Append('}').ToString();
        }, TimedOut);
    }

    private string OneEntity(HttpRequest request)
    {
        var selector = EntitySelector.Parse(key => Get(request, key), out string? error);
        if (error is not null)
            return $"{{\"error\":{Quote(error)}}}";

        if (selector is null)
            return "{\"error\":\"name an entity: index, handle or name\"}";

        return OnGameThread(() =>
        {
            uint entity = selector.Resolve(_hooks.World);

            // Not an error: "it is not there" is an answer a caller waits on.
            if (entity == IDebugWorld.NoEntity)
                return $"{{\"found\":false,\"selector\":{Quote(selector.ToString())},\"tick\":{_hooks.World.Tick}}}";

            var json = new StringBuilder("{\"found\":true,");
            AppendEntityDetail(json, entity);
            return json.Append(",\"tick\":").Append(_hooks.World.Tick).Append('}').ToString();
        }, TimedOut);
    }

    /// <summary>
    /// One entity's fields beyond the listing's, without the braces: what
    /// /entity answers, and what a settle or a teleport reports at the end.
    /// </summary>
    private void AppendEntityDetail(StringBuilder json, uint entity)
    {
        var world = _hooks.World;
        var origin = world.GetOrigin(entity);
        var angles = world.GetAngles(entity);
        var velocity = world.GetVelocity(entity);
        bool? asleep = world.IsAsleep(entity);
        int? ground = world.GroundIndex(entity);

        json.Append("\"handle\":").Append(entity)
            .Append(",\"index\":").Append(world.GetIndex(entity))
            .Append(",\"classname\":").Append(Quote(world.GetClassName(entity)))
            .Append(",\"name\":").Append(Quote(world.GetName(entity)))
            .Append(",\"health\":").Append(world.GetHealth(entity))
            .Append(",\"origin\":[").Append(F(origin.X)).Append(',').Append(F(origin.Y)).Append(',').Append(F(origin.Z))
            .Append("],\"angles\":[").Append(F(angles.X)).Append(',').Append(F(angles.Y)).Append(',').Append(F(angles.Z))
            .Append("],\"velocity\":[").Append(F(velocity.X)).Append(',').Append(F(velocity.Y)).Append(',').Append(F(velocity.Z))
            .Append("],\"speed\":").Append(F(Speed(velocity)))
            // Three-valued, and null is not false: an object that never
            // reported a state change has no recorded state.
            .Append(",\"asleep\":").Append(asleep is { } a ? (a ? "true" : "false") : "null")
            .Append(",\"ground\":").Append(ground is { } g ? g.ToString(CultureInfo.InvariantCulture) : "null");
    }

    internal static float Speed(DebugVec3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary>
    /// One live entity, as every view of them needs it.
    ///
    /// The handle rather than only the index, because an index is reused the
    /// moment an entity is freed and a debugger that cannot tell "the same
    /// entity" from "the one that took its slot" will report the wrong thing
    /// with total confidence.
    /// </summary>
    private void AppendEntity(StringBuilder json, uint entity)
    {
        var entities = _hooks.World;
        var origin = entities.GetOrigin(entity);
        var angles = entities.GetAngles(entity);

        json.Append("{\"handle\":").Append(entity)
            .Append(",\"index\":").Append(entities.GetIndex(entity))
            .Append(",\"classname\":").Append(Quote(entities.GetClassName(entity)))
            .Append(",\"health\":").Append(entities.GetHealth(entity))
            .Append(",\"origin\":[")
            .Append(F(origin.X)).Append(',').Append(F(origin.Y)).Append(',').Append(F(origin.Z))
            .Append("],\"angles\":[")
            .Append(F(angles.X)).Append(',').Append(F(angles.Y)).Append(',').Append(F(angles.Z))
            .Append("]}");
    }

    //-------------------------------------------------------------------------
    // Listings.
    //
    // Hand-built JSON, like the rest of this file. A serializer would be less
    // code, but it would also pull a dependency and a chunk of reflection into
    // an assembly whose whole design goal is to be absent unless asked for.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which registry a listing is about.
    ///
    /// Managed is the default and stays the default deliberately. It is a
    /// registry of managed objects and answers without a frame; the engine's is
    /// thousands of entries of live state and has to wait for the game thread,
    /// so a UI that polls should ask for it when someone wants it rather than
    /// every few seconds regardless.
    /// </summary>
    private enum Listing
    {
        /// <summary>What this mod declared.</summary>
        Managed,
        /// <summary>What the engine and the game DLLs declared.</summary>
        Native,
        /// <summary>Both, with ours first and no name listed twice.</summary>
        All,
    }

    private static bool TryParseListing(string? source, out Listing listing)
    {
        listing = Listing.Managed;

        switch (source?.Trim().ToLowerInvariant())
        {
            case null or "" or "managed" or "mod": return true;
            case "native" or "engine": listing = Listing.Native; return true;
            case "all" or "both":      listing = Listing.All;    return true;
            default: return false;
        }
    }

    private static string UnknownSource(string? source)
        => $"{{\"error\":\"no such source\",\"source\":{Quote(source ?? "")}," +
            "\"expected\":[\"managed\",\"native\",\"all\"]}";

    private string ConVars(string? source)
    {
        if (!TryParseListing(source, out var listing))
            return UnknownSource(source);

        // The declared list is read for every source, including native — where
        // none of it is listed. It is what says which of the engine's thousands
        // are ours, and "the engine's, minus ours" is what native is for. A host
        // that supplied no list cannot answer that question, and answering the
        // engine's half unfiltered is closer to the ask than refusing outright.
        var declared = _hooks.Console.ConVars();

        if (declared is null)
            return NotSupplied("convars");

        if (listing == Listing.Managed)
            return ConVarsJson(declared, Array.Empty<DebugNativeEntry>(), ours: true);

        bool includeOurs = listing == Listing.All;

        // The engine's registry is live state rather than a managed cache, so
        // unlike every other listing this one waits for a frame.
        return OnGameThread(
            () => ConVarsJson(declared, _hooks.Console.NativeEntries(), includeOurs), TimedOut);
    }

    /// <summary>
    /// The convars of both registries, ours first.
    ///
    /// Every convar this mod declared is IN the engine's table — that is what
    /// registering one does — so the declared names are struck from the native
    /// half either way, and <paramref name="ours"/> only decides whether they
    /// are then listed in their own right.
    ///
    /// Merged, they are: a listing that skipped this would show cs_sv_greeting
    /// as two rows, one typed, editable and ours, and one untyped, anonymous and
    /// apparently the engine's. The typed row is the truthful one, so it is the
    /// one that survives.
    ///
    /// Native alone, they are not. Asking for the engine's convars is asking
    /// which of these came from somewhere other than here, and answering it with
    /// this mod's own sixty in untyped form is answering a question nobody has:
    /// the typed listing is one click away and better in every respect.
    /// </summary>
    private static string ConVarsJson(IReadOnlyList<DebugConVar> declared,
                                      IReadOnlyList<DebugNativeEntry> native,
                                      bool ours)
    {
        var json = new StringBuilder("{\"convars\":[");
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var convar in declared)
        {
            declaredNames.Add(convar.Name);

            if (!ours)
                continue;

            if (count++ > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Quote(convar.Name))
                .Append(",\"value\":").Append(Quote(convar.Value))
                .Append(",\"default\":").Append(Quote(convar.Default))
                .Append(",\"help\":").Append(Quote(convar.Help))
                .Append(",\"type\":").Append(Quote(convar.Type))
                // Through the same describer as a native convar's, so one set of
                // flags reads the same way whoever declared it — and so a bit
                // nobody named comes out as a bit rather than as a decimal
                // number spliced between two names.
                .Append(",\"flags\":").Append(Quote(convar.FlagNames))
                .Append(",\"min\":").Append(Bound(convar.Min))
                .Append(",\"max\":").Append(Bound(convar.Max))
                // A convar the engine refused still reads and still shows a
                // value, but writing to it goes nowhere. The UI greys it out
                // rather than offering an edit that will not take.
                .Append(",\"registered\":").Append(convar.Registered ? "true" : "false")
                // The C# class it came from, which is what "which module is this
                // from" means for one of ours — the same question the native
                // rows answer with the DLL that registered them, so the two
                // kinds of row fill the same column rather than one leaving it
                // blank.
                .Append(",\"declaredBy\":").Append(Quote(convar.DeclaredBy))
                .Append(",\"source\":\"managed\"}");
        }

        foreach (var entry in native)
        {
            if (entry.IsCommand || declaredNames.Contains(entry.Name))
                continue;

            if (count++ > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Quote(entry.Name))
                .Append(",\"value\":").Append(Quote(entry.Value))
                .Append(",\"default\":").Append(Quote(entry.Default))
                .Append(",\"help\":").Append(Quote(entry.Help))
                // "unknown", not a guess. Nobody ever declared a type for these:
                // the engine keeps a string, a float and an int for every convar
                // and lets the reader pick. A 0/1 convar shown as a checkbox
                // would be a debugging tool inventing a rule the game does not
                // have, so the UI offers text and says why.
                .Append(",\"type\":\"unknown\"")
                .Append(",\"flags\":").Append(Quote(entry.FlagNames))
                .Append(",\"min\":").Append(Bound(entry.Min))
                .Append(",\"max\":").Append(Bound(entry.Max))
                .Append(",\"registered\":true")
                .Append(",\"declaredBy\":").Append(Quote(entry.DeclaredBy))
                .Append(",\"source\":\"native\"}");
        }

        return json.Append("],\"count\":").Append(count).Append('}').ToString();
    }

    private string Commands(string? source)
    {
        if (!TryParseListing(source, out var listing))
            return UnknownSource(source);

        // Read for every source, native included: it is what says which of the
        // engine's are ours. See ConVars above.
        var declared = _hooks.Console.Commands();

        if (declared is null)
            return NotSupplied("commands");

        if (listing == Listing.Managed)
            return CommandsJson(declared, Array.Empty<DebugNativeEntry>(), ours: true);

        bool includeOurs = listing == Listing.All;

        return OnGameThread(
            () => CommandsJson(declared, _hooks.Console.NativeEntries(), includeOurs), TimedOut);
    }

    /// <summary>
    /// The commands of both registries — for the same reasons as the convars,
    /// since registering a command also puts it in the engine's table: struck
    /// from the native half always, listed in their own right only when they
    /// were asked for.
    /// </summary>
    internal static string CommandsJson(IReadOnlyList<DebugConCommand> declared,
                                        IReadOnlyList<DebugNativeEntry> native,
                                        bool ours)
    {
        var json = new StringBuilder("{\"commands\":[");
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nativeByName = new Dictionary<string, DebugNativeEntry>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var entry in native)
        {
            if (entry.IsCommand)
                nativeByName[entry.Name] = entry;
        }

        foreach (var command in declared)
        {
            declaredNames.Add(command.Name);

            if (!ours)
                continue;

            if (count++ > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Quote(command.Name))
                .Append(",\"help\":").Append(Quote(command.Help))
                .Append(",\"declaredBy\":").Append(Quote(command.DeclaredBy))
                // THE ENGINE'S FLAGS when its table was read, else the declared
                // ones. This said "" for every C# command, from when the slot
                // carried no flag word; since it does, a Cheat command listed
                // with no flags is a false statement about what a player gets,
                // and plan_server_novalve.md P2.3c's snapshot diff read it as
                // forty-odd commands losing FCVAR_CHEAT.
                .Append(",\"flags\":").Append(Quote(
                    nativeByName.TryGetValue(command.Name, out var registered)
                        ? registered.FlagNames
                        : command.FlagNames))
                // Same as a convar's: the class exists and was built, but the
                // engine would not take the name, so nothing a player types
                // reaches it. Worth saying rather than showing a command that
                // silently is not there.
                .Append(",\"registered\":").Append(command.Registered ? "true" : "false")
                .Append(",\"source\":\"managed\"}");
        }

        foreach (var entry in native)
        {
            if (!entry.IsCommand || declaredNames.Contains(entry.Name))
                continue;

            if (count++ > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Quote(entry.Name))
                .Append(",\"help\":").Append(Quote(entry.Help))
                // Which DLL registered it, which is all the engine records about
                // where a command came from — and the thing worth knowing, since
                // it separates the game's commands from the engine's.
                .Append(",\"declaredBy\":").Append(Quote(entry.DeclaredBy))
                // Flags matter more here than on a convar: a command marked
                // Cheat does nothing at all without sv_cheats, and silently.
                .Append(",\"flags\":").Append(Quote(entry.FlagNames))
                // Read out of the engine's own table, so being there is what
                // registered means. Emitted all the same, so the column has a
                // value in every row rather than only in ours.
                .Append(",\"registered\":true")
                .Append(",\"source\":\"native\"}");
        }

        return json.Append("],\"count\":").Append(count).Append('}').ToString();
    }

    /// <summary>
    /// What the server's container holds — the answer to "what may an entity
    /// ask for", which before this had no answer but reading the module.
    ///
    /// <para>
    /// Filtered the way the convar and command listings are, because it is the
    /// same shape of question: a contract is asked for by TYPE and checked by
    /// the compiler, a convar or a command by the NAME a player types, and a
    /// reader usually wants one or the other rather than both interleaved.
    /// </para>
    ///
    /// <para>
    /// No game thread. The container stops changing when init ends, and a view
    /// somebody keeps open beside a game they are debugging must not be able to
    /// cost that game a frame.
    /// </para>
    /// </summary>
    private string Services(string? by)
    {
        if (!TryParseLookup(by, out string? wanted))
            // NOT UnknownSource, which names managed/native/all — the convar
            // and command listings' options, and not this one's. An error that
            // lists the wrong alternatives is worse than one that lists none.
            return $"{{\"error\":\"no such lookup\",\"by\":{Quote(by ?? "")}," +
                    "\"expected\":[\"type\",\"name\",\"all\"]}";

        var services = _hooks.Status.Services();

        var json = new StringBuilder("{\"services\":[");
        int count = 0;

        foreach (var service in services)
        {
            if (wanted is not null && service.LookedUpBy != wanted)
                continue;

            if (count++ > 0)
                json.Append(',');

            json.Append("{\"key\":").Append(Quote(service.Key))
                .Append(",\"lookedUpBy\":").Append(Quote(service.LookedUpBy))
                .Append(",\"kind\":").Append(Quote(service.Kind))
                // The class that actually answers. A contract says what may be
                // called; only this says WHICH object arrives, which is the
                // half that is wrong when an entity consults something that is
                // not the live system.
                .Append(",\"implementation\":").Append(Quote(service.Implementation))
                // False only for a lazy registration nothing has asked for yet.
                // Not a fault: a service nothing has needed is one nothing has
                // paid for.
                .Append(",\"built\":").Append(service.Built ? "true" : "false")
                .Append('}');
        }

        return json.Append("],\"count\":").Append(count).Append('}').ToString();
    }

    /// <summary>
    /// Which of the container's two keys a listing was asked for. Null means
    /// both, matching how "all" reads on the convar and command listings.
    /// </summary>
    private static bool TryParseLookup(string? by, out string? wanted)
    {
        switch (by?.ToLowerInvariant())
        {
            case null or "" or "type" or "contract": wanted = "type"; return true;
            case "name" or "console":                wanted = "name"; return true;
            case "all" or "both":                    wanted = null;   return true;
            default:                                 wanted = null;   return false;
        }
    }

    private string EntityClasses()
    {
        var classes = _hooks.Catalog.Classes();

        var json = new StringBuilder("{\"classes\":[");

        for (int i = 0; i < classes.Count; i++)
        {
            var info = classes[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"className\":").Append(Quote(info.ClassName))
                .Append(",\"kind\":").Append(Quote(info.Kind))
                .Append(",\"declaredBy\":").Append(Quote(info.TypeName))
                .Append(",\"liveCount\":").Append(info.LiveCount)
                .Append(",\"inputs\":");

            AppendStrings(json, info.Inputs);

            json.Append(",\"outputs\":");
            AppendStrings(json, info.Outputs);

            json.Append(",\"vars\":[");
            for (int v = 0; v < info.Vars.Count; v++)
            {
                if (v > 0)
                    json.Append(',');

                json.Append("{\"name\":").Append(Quote(info.Vars[v].Name))
                    .Append(",\"type\":").Append(Quote(info.Vars[v].TypeName)).Append('}');
            }

            json.Append("]}");
        }

        return json.Append("],\"count\":").Append(classes.Count).Append('}').ToString();
    }

    /// <summary>
    /// Live scripted entities. Runs on the game thread — unlike the other
    /// listings this reads positions and health, which is engine state.
    /// </summary>
    private string EntityInstances()
    {
        var instances = _hooks.Catalog.Instances();

        var json = new StringBuilder("{\"instances\":[");

        for (int i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"className\":").Append(Quote(instance.ClassName))
                .Append(",\"handle\":").Append(instance.Handle)
                .Append(",\"index\":").Append(instance.Index)
                .Append(",\"valid\":").Append(instance.Valid ? "true" : "false")
                .Append(",\"health\":").Append(instance.Health)
                .Append(",\"spawnFlags\":").Append(instance.SpawnFlags)
                .Append(",\"origin\":[")
                .Append(F(instance.Origin.X)).Append(',')
                .Append(F(instance.Origin.Y)).Append(',')
                .Append(F(instance.Origin.Z))
                .Append("],\"vars\":[");

            for (int v = 0; v < instance.Vars.Count; v++)
            {
                if (v > 0)
                    json.Append(',');

                json.Append("{\"name\":").Append(Quote(instance.Vars[v].Name))
                    .Append(",\"type\":").Append(Quote(instance.Vars[v].TypeName))
                    .Append(",\"value\":").Append(Quote(instance.Vars[v].Value)).Append('}');
            }

            json.Append("],\"outputs\":[");

            for (int o = 0; o < instance.Outputs.Count; o++)
            {
                if (o > 0)
                    json.Append(',');

                json.Append("{\"name\":").Append(Quote(instance.Outputs[o].Name))
                    .Append(",\"fireCount\":").Append(instance.Outputs[o].FireCount)
                    .Append(",\"lastValue\":")
                    .Append(instance.Outputs[o].LastValue is { } last ? Quote(last) : "null")
                    .Append('}');
            }

            json.Append("]}");
        }

        return json.Append("],\"count\":").Append(instances.Count).Append('}').ToString();
    }

    //-------------------------------------------------------------------------
    // The networked schema, and one entity's bytes read back through it.
    //
    // These are where a readability the payload plan removed went.
    // cs_sv_dump_nettable printed every prop's name, offset, bit count and type;
    // the props are named p000..p511 now, the names live only in C#, and Step 7
    // deleted the dump. Built against the per-class tables while they still
    // existed, on purpose -- an oracle written after the thing it checks is an
    // oracle written to agree with it, and every row of these was compared
    // against a console dump while there was one.
    //-------------------------------------------------------------------------

    private const string NetTablePrefix = "/nettable/";

    /// <summary>
    /// A path segment as it was typed. The request parser decodes query values
    /// and deliberately leaves the path alone, and a classname never needs an
    /// escape -- but the one that does would otherwise be unaskable.
    /// </summary>
    private static string Unescape(string segment)
    {
        try { return Uri.UnescapeDataString(segment); }
        catch (UriFormatException) { return segment; }
    }

    /// <summary>Where the last blob of each watched entity is kept, for the diff.</summary>
    private readonly NetPayloadSampler _payloads = new();

    private string NetTables()
    {
        // Off the game thread: the schema is metadata fixed at startup.
        var schema = _hooks.NetSchema;
        return NetTableJson.Schema(schema.Types(), schema.Tiers(), schema.Refusals());
    }

    /// <summary>
    /// The client's prediction misses.
    ///
    /// <para>
    /// THE ONE LISTING HERE THAT IS NOT THE SERVER'S. Every other reads a
    /// registry the server module handed over; this reads a static in
    /// SourceSharp.Abi, which is the assembly both sides' load contexts share.
    /// It has to be: prediction runs on the client, there is one dev API per
    /// process and only the server module builds it, and the two sides' types
    /// are otherwise not the same types at all.
    /// </para>
    ///
    /// <para>
    /// A PROCESS WITH NO CLIENT SAYS SO. A dedicated server would otherwise
    /// answer an empty list, which reads identically to a client whose
    /// prediction is holding perfectly and means the opposite. The client
    /// stamps its own build half on load, so whether one was ever here is a
    /// question already answered.
    /// </para>
    /// </summary>
    private string Prediction(string? sinceText, string? maxText)
    {
        long since = long.TryParse(sinceText, out long parsed) && parsed > 0 ? parsed : 0;
        int max = int.TryParse(maxText, out int asked) && asked > 0
            ? Math.Min(asked, PredictionMirror.DefaultCapacity)
            : PredictionMirror.DefaultCapacity;

        // SERVER-SIDE DATA ONLY (plan_devapi_interfaces §5.8, Q1, the user's
        // ruling). Prediction is the client's; what the server holds is what a
        // client pushed up the debug channel it was invited to open -- a remote
        // client on a dedicated server, or the client in this process on a
        // listen server, the same way. The server never reads a client's own
        // ring: under per-side Abi (plan_side_isolation P5) it cannot see it.
        // That view is the client dev API's.
        if (_debugChannel is { EverAttached: true } channel)
            return PredictionJson.Report(channel.Mirror, since, max);

        return PredictionJson.Unavailable(_debugChannel is null
            ? "this server holds no prediction of its own — prediction is client-side; a client's " +
              "own report is at /client/{slot}/prediction, through this server's debug channel " +
              $"(start it with {DebugChannelProtocol.ServerParam}, and the client with " +
              $"{DebugChannelProtocol.ClientParam})"
            : $"no client has attached to the debug channel on {_debugChannel.Address} yet — a client " +
              $"must be started with {DebugChannelProtocol.ClientParam} before it will accept an " +
              "invitation; once one has, its own report is at /client/{slot}/prediction");
    }

    /// <summary>
    /// What the debug channel is and what has come up it.
    ///
    /// <para>
    /// SAYS "OFF" OUT LOUD rather than answering an empty object, for the reason
    /// <see cref="PredictionJson.Unavailable"/> exists: a server that never
    /// opted in and a server nobody has connected to produce the same silence
    /// and mean different things.
    /// </para>
    /// </summary>
    private string DebugChannel()
    {
        if (_debugChannel is null)
        {
            // TWO DIFFERENT ANSWERS, because "off" and "asked for and failed"
            // send a reader to different places. A live run told somebody who
            // HAD opted in that they had not, because the bind lost a port to a
            // peer's web UI — and they went looking at their command line.
            return "{\"listening\":false,\"note\":" + Quote(_debugChannelRefusal is null
                ? $"this server did not opt in — start it with {DebugChannelProtocol.ServerParam} " +
                  "(and -debugapi) for a client to be able to connect back"
                : $"this server opted in and could not listen: {_debugChannelRefusal}. " +
                  $"Give {DebugChannelProtocol.ServerPortParam} another port.") + "}";
        }

        var status = _debugChannel.Status();

        return "{\"listening\":" + (status.Listening ? "true" : "false")
             + ",\"address\":" + Quote(status.Address)
             + ",\"port\":" + status.Port
             + ",\"public\":" + (status.Public ? "true" : "false")
             + ",\"outstandingInvitations\":" + status.Outstanding
             + ",\"sessions\":" + status.Sessions
             + ",\"accepted\":" + status.Accepted
             + ",\"refused\":" + status.Refused
             + ",\"recordsReceived\":" + status.RecordsReceived
             + ",\"everAttached\":" + (status.EverAttached ? "true" : "false")
             + ",\"note\":" + Quote(
                 "an invitation carries a port and a nonce and never an address; the client "
               + "dials the server it already joined")
             + "}";
    }

    private string NetTable(string key)
    {
        string unescaped = Unescape(key);
        return NetTableJson.Type(_hooks.NetSchema.Types(), _hooks.NetSchema.Find(unescaped), unescaped);
    }

    /// <summary>
    /// <c>/entity/&lt;index&gt;/payload</c>, and nothing else that shape.
    ///
    /// Matched rather than routed by a table because it is the only path in this
    /// API with a variable in the middle. An index that does not parse is a 404
    /// and not an error, because the path simply is not this route -- and a
    /// wrong guess here would swallow whatever endpoint is added next.
    /// </summary>
    private static bool TryPayloadPath(string path, out int index)
    {
        index = -1;

        const string prefix = "/entity/";
        const string suffix = "/payload";

        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        int start = prefix.Length;
        int length = path.Length - suffix.Length - start;

        return length > 0
            && int.TryParse(path.AsSpan(start, length), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out index);
    }

    /// <summary>
    /// One entity's payload, three ways.
    ///
    /// The blob is lifted on the game thread and everything else happens off it:
    /// decoding is a pure function of bytes and metadata, and holding the game
    /// thread while a hex dump is formatted would make a debugging view cost
    /// frames on a server somebody is watching.
    /// </summary>
    private string Payload(int index)
    {
        // Read AND decoded on the game thread: the bytes are engine memory and
        // the decode is the server's, through the entity's own layout.
        DebugNetPayload? read = null;
        string queued = OnGameThread(() =>
        {
            read = _hooks.NetSchema.Payload(index);
            return "";
        }, TimedOut);

        if (queued.Length > 0 || read is null)
            return queued;

        return NetTableJson.Payload(read, _payloads.Observe(read, DateTime.UtcNow));
    }

    //-------------------------------------------------------------------------
    // Entity events, and firing at entities.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The entity event history.
    ///
    /// Reading it also declares interest: sampling the whole entity list costs
    /// the game thread something, so it only happens while somebody is watching
    /// the result. A feed nobody reads should cost nothing at all.
    /// </summary>
    private string Events(HttpRequest request)
    {
        _sampler.NoteInterest();

        if (!EventFilter.TryParse(key => Get(request, key), out var filter, out string? error))
            return $"{{\"error\":{Quote(error!)}}}";

        long since = long.TryParse(Get(request, "since"), out long s) ? s : 0;
        int max = int.TryParse(Get(request, "max"), out int m) && m > 0 ? Math.Min(m, 2000) : 500;

        var events = _events.Since(since, max, filter, out long resumeAt);

        // next is where THIS reader continues: past the last event answered when
        // max cut the read short, else the head. It used to be the head always,
        // so a reader that asked for fewer than were waiting silently skipped
        // the rest. head is the ring's own end, for a reader that wants "from
        // now on".
        var json = new StringBuilder("{\"next\":").Append(resumeAt)
            .Append(",\"head\":").Append(_events.NextSequence)
            // Reported rather than assumed: the first poll after a pause has a
            // gap in it, and a view that does not know that will show it as a
            // quiet period.
            .Append(",\"sampling\":").Append(_sampler.IsSampling ? "true" : "false")
            .Append(",\"samplePeriod\":").Append(N((float)EntitySampler.Interval.TotalSeconds))
            .Append(",\"events\":[");

        for (int i = 0; i < events.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            AppendEventJson(json, events[i]);
        }

        return json.Append("],\"count\":").Append(events.Count).Append('}').ToString();
    }

    /// <summary>One entity event, as /events and /wait both answer it.</summary>
    internal static void AppendEventJson(StringBuilder json, in EntityEvent e)
        => json.Append("{\"seq\":").Append(e.Sequence)
            .Append(",\"time\":").Append(N(e.Time))
            .Append(",\"kind\":").Append(Quote(e.Kind.ToString().ToLowerInvariant()))
            .Append(",\"classname\":").Append(Quote(e.ClassName))
            .Append(",\"name\":").Append(Quote(e.Name))
            .Append(",\"detail\":").Append(Quote(e.Detail))
            .Append(",\"index\":").Append(e.Index)
            .Append(",\"handle\":").Append(e.Handle)
            .Append('}');

    /// <summary>
    /// The heads of the three rings, and nothing else: where a reader starts
    /// to see only what happens from now on. It replaces asking for one event
    /// from sequence 999999 and reading the <c>next</c> off an empty answer.
    /// No game thread, so it answers at once.
    /// </summary>
    private string Heads()
    {
        _sampler.NoteInterest();
        return $"{{\"events\":{_events.NextSequence},\"log\":{_log.NextSequence},\"console\":{_console.NextSequence}}}";
    }

    /// <summary>
    /// What every write reply carries: where in the log, the console and the
    /// event ring its effects begin, read BEFORE the work ran. A caller asks
    /// <c>/events?since=eventsFrom</c> for exactly what its own call caused.
    /// </summary>
    private (long Log, long Events, long Console) Marks()
        => (_log.NextSequence, _events.NextSequence, _console.NextSequence);

    // The LAST brace only: TrimEnd('}') would also eat the close of a nested
    // object the reply ends with, and splice the marks inside it.
    internal static string Stamped(string result, (long Log, long Events, long Console) from)
        => result[..result.LastIndexOf('}')] + $",\"logFrom\":{from.Log},\"eventsFrom\":{from.Events},\"consoleFrom\":{from.Console}}}";

    /// <summary>
    /// Fires an input at an entity, the way the console does.
    ///
    /// Calls AcceptInput directly, which is what ent_fire does once the console
    /// command around it is stripped away -- and that wrapper was the problem:
    /// ent_fire is FCVAR_CHEAT, so this reported success and did nothing
    /// whenever sv_cheats was 0. From out here that is indistinguishable from
    /// the entity ignoring the input, and it cost an afternoon of blaming newly
    /// ported C# entities for a gate in the console.
    ///
    /// A delay still needs the console command, because there is no way to
    /// schedule one from here -- so a delayed fire says which path it took.
    /// </summary>
    private string FireInput(string? target, string? input, string? parameter, string? delayText,
                             string? activatorText = null)
    {
        // The player whose input this is, by slot: an input like
        // logic_level_transition's Transition does nothing without one.
        int activatorSlot = 0;
        if (!string.IsNullOrWhiteSpace(activatorText)
            && (!int.TryParse(activatorText, out activatorSlot) || activatorSlot <= 0))
        {
            return "{\"error\":\"activator is a player slot, 1 or more\"}";
        }

        if (string.IsNullOrWhiteSpace(target))
            return "{\"error\":\"no target given\"}";

        if (string.IsNullOrWhiteSpace(input))
            return "{\"error\":\"no input given\"}";

        bool delayed = !string.IsNullOrWhiteSpace(delayText) && delayText != "0";
        string command = BuildEntFire(target, input, parameter, delayText);
        var before = Marks();

        string result = OnGameThread(() =>
        {
            _events.Add(EntityEventKind.Input, "", target, $"{input} <- debug-api", _hooks.World.Now);

            if (delayed)
            {
                _hooks.Console.Execute(command);
                return $"{{\"ok\":true,\"via\":\"ent_fire\",\"cheatGated\":true,\"command\":{Quote(command)}}}";
            }

            // The console's rule: a name if anything carries it, a classname
            // otherwise, and EVERY entity that answers rather than the first.
            // This asked FindByName alone, so a class -- "trigger_multiple",
            // with forty-seven of them alive -- came back "no entity named",
            // and six lights sharing a targetname had one of them lit.
            var targets = EntityTargets.Resolve(_hooks.World, target,
                                                out EntityTargets.MatchKind matched);

            if (targets.Count == 0)
            {
                return "{\"error\":\"nothing is called that and nothing is of that class\"," +
                       $"\"target\":{Quote(target)}}}";
            }

            // Whether the entities ACCEPTED it, not whether it was sent. An
            // input an entity does not implement is the other thing that looks
            // like nothing happening -- so both counts are reported, and a fire
            // that reached fifty entities and was taken by none says so.
            uint? activator = null;
            if (activatorSlot > 0)
            {
                activator = _hooks.World.PlayerHandle(activatorSlot);
                if (!_hooks.World.IsValid(activator.Value))
                    return $"{{\"error\":\"no player in that slot\",\"activator\":{activatorSlot}}}";
            }

            int accepted = 0;
            foreach (uint entity in targets)
            {
                bool took = _hooks.World.FireInput(entity, input, parameter, activator);
                if (took)
                    accepted++;
            }

            return "{\"ok\":true,\"via\":\"AcceptInput\"" +
                   $",\"matchedBy\":{Quote(matched.ToString().ToLowerInvariant())}" +
                   $",\"targets\":{targets.Count}" +
                   $",\"accepted\":{accepted}}}";
        }, TimedOut);

        return Stamped(result, before);
    }

    /// <summary>
    /// Hurts an entity, or every entity of a class.
    ///
    /// This exists because ent_fire is FCVAR_CHEAT, so <see cref="FireInput"/>
    /// reports success and quietly does nothing at sv_cheats 0 -- and from
    /// outside, "the entity ignored my input" and "the command was refused"
    /// look exactly alike. That cost an afternoon of blaming newly ported
    /// entities for a gate in the console.
    ///
    /// Damage is not an input, so this calls the damage path directly. Nothing
    /// can refuse it, and what comes back is what actually happened: how much
    /// health each target had before and after, which is the only honest way to
    /// tell "it took the damage" from "it ignored it".
    /// </summary>
    /// <summary>
    /// "x,y,z" or "x y z", or null for neither. Both separators, because a URL
    /// query is typed by hand as often as it is generated.
    /// </summary>
    private static DebugVec3? ParsePoint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        var v = new float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return null;
        }

        return new DebugVec3(v[0], v[1], v[2]);
    }

    /// <summary>An entity by targetname, or by entity index written "#n".</summary>
    private uint ByNameOrIndex(string text)
    {
        if (text.StartsWith('#') && int.TryParse(text.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            foreach (uint entity in _hooks.World.FindAllByClassname("*"))
            {
                if (_hooks.World.GetIndex(entity) == index)
                    return entity;
            }

            return IDebugWorld.NoEntity;
        }

        return _hooks.World.FindByName(text);
    }

    private string Damage(string? target, string? className, string? amountText, string? typeText,
                          string? atText, string? fromText = null)
    {
        if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(className))
            return "{\"error\":\"give a target name or a class\"}";

        if (!float.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
            amount = 25f;

        // Generic damage by default. A barrel that only answers to explosions
        // would shrug off a bullet, so the type has to be askable.
        // Parsed as a raw number because that is what a debug-api caller sends --
        // the same DMG_ word a map's keyvalue would carry -- and then typed.
        if (!int.TryParse(typeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int damageBits))
            damageBits = _hooks.World.BulletDamageType;

        // WHERE it landed, optionally. Some entities answer differently
        // depending on which part of them was hit — glass shatters the pane the
        // shot found and leaves the rest of the window standing — and without a
        // position the only thing that can be tested from out here is the whole
        // sheet coming down at once.
        DebugVec3? at = ParsePoint(atText);
        var before = Marks();

        return Stamped(OnGameThread(() =>
        {
            var targets = new List<uint>();

            if (!string.IsNullOrWhiteSpace(target))
            {
                uint found = ByNameOrIndex(target);
                if (_hooks.World.IsValid(found))
                    targets.Add(found);
            }

            if (!string.IsNullOrWhiteSpace(className))
                targets.AddRange(_hooks.World.FindAllByClassname(className));

            if (targets.Count == 0)
                return "{\"error\":\"nothing matched\"}";

            // WHO it came from, optionally: a targetname, or #index. A hit with
            // an attacker has a direction, which the player's Damage message
            // carries; without one it comes from nowhere.
            uint from = IDebugWorld.NoEntity;
            if (!string.IsNullOrWhiteSpace(fromText))
            {
                from = ByNameOrIndex(fromText);
                if (!_hooks.World.IsValid(from))
                    return "{\"error\":\"the attacker matched nothing\"}";
            }

            var json = new StringBuilder("{\"ok\":true,\"amount\":")
                .Append(amount.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(",\"type\":").Append(damageBits)
                .Append(",\"hit\":[");

            for (int i = 0; i < targets.Count; i++)
            {
                uint entity = targets[i];

                // Read either side of the call rather than reporting that it was
                // made. An entity treated as scenery takes none of it, and that
                // is exactly the case worth being able to see.
                int before = _hooks.World.GetHealth(entity);

                // At a point: pushed away from it. Otherwise a plain hit.

                _hooks.World.Damage(entity, amount, damageBits, at, from);

                bool gone = !_hooks.World.IsValid(entity);
                int after = gone ? 0 : _hooks.World.GetHealth(entity);

                if (i > 0)
                    json.Append(',');

                json.Append("{\"classname\":").Append(Quote(gone ? "" : _hooks.World.GetClassName(entity)))
                    .Append(",\"healthBefore\":").Append(before)
                    .Append(",\"healthAfter\":").Append(after)
                    .Append(",\"gone\":").Append(gone ? "true" : "false")
                    .Append('}');

                _events.Add(EntityEventKind.Input, "", target ?? className ?? "",
                            $"{amount:0.##} damage <- debug-api", _hooks.World.Now);
            }

            return json.Append("],\"count\":").Append(targets.Count).Append('}').ToString();
        }, TimedOut), before);
    }

    /// <summary>
    /// Fires everything an output is connected to.
    ///
    /// An output cannot be fired directly -- only the entity that owns it
    /// decides to fire it. What a person means by "trigger this output" is
    /// "make its connections happen", so that is what this does: it reads the
    /// map for what the output connects to and fires each of those inputs.
    ///
    /// Done here rather than in the browser so that one click is one request and
    /// the event log records it as a single act with a count, rather than as a
    /// burst of unrelated-looking inputs.
    /// </summary>
    private string FireOutput(string? mapName, string? entityName, string? outputName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            return "{\"error\":\"no entity given\"}";

        if (string.IsNullOrWhiteSpace(outputName))
            return "{\"error\":\"no output given\"}";

        string map = ResolveMapName(mapName);

        if (map.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        BspFile.Contents contents;

        try
        {
            contents = _maps.Read(map, withGeometry: false);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(map)}}}";
        }

        var entity = contents.Entities.FirstOrDefault(
            e => string.Equals(e.TargetName, entityName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
            return $"{{\"error\":\"no entity named that in the map\",\"entity\":{Quote(entityName)}}}";

        var connections = entity.Connections
            .Where(c => string.Equals(c.Output, outputName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (connections.Length == 0)
            return $"{{\"ok\":true,\"fired\":0,\"note\":\"that output is connected to nothing\"}}";

        var before = Marks();

        string result = OnGameThread(() =>
        {
            foreach (var connection in connections)
            {
                _hooks.Console.Execute(BuildEntFire(
                    connection.Target, connection.Input, connection.Parameter,
                    connection.Delay.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            _events.Add(EntityEventKind.Output, entity.ClassName, outputName,
                $"forced, {connections.Length} connection(s)", _hooks.World.Now);

            return $"{{\"ok\":true,\"fired\":{connections.Length}}}";
        }, TimedOut);

        return Stamped(result, before);
    }

    /// <summary>
    /// Wires one live entity's output to another's input.
    ///
    /// Source has a real mechanism for this and it is worth knowing about:
    /// AddOutput takes "&lt;output&gt; &lt;target&gt;:&lt;input&gt;:&lt;param&gt;:&lt;delay&gt;:&lt;times&gt;"
    /// and adds a genuine connection to the running entity, indistinguishable
    /// from one the map compiled in. Colons rather than commas because Hammer
    /// forbids commas inside parameters, and the engine swaps them back.
    ///
    /// It lasts as long as the entity does. Nothing here writes to the map: a
    /// BSP is a compiled artefact, and a debugger that silently edited one would
    /// be a far more alarming tool than this.
    /// </summary>
    private string Connect(string? source, string? output, string? target, string? input,
                           string? parameter, string? delayText, string? timesText)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "{\"error\":\"no source entity given\"}";

        if (string.IsNullOrWhiteSpace(output))
            return "{\"error\":\"no output given\"}";

        if (string.IsNullOrWhiteSpace(target))
            return "{\"error\":\"no target entity given\"}";

        if (string.IsNullOrWhiteSpace(input))
            return "{\"error\":\"no input given\"}";

        // A colon in any field would be read as a field separator and silently
        // shift everything after it along by one.
        foreach (string field in new[] { output, target, input, parameter ?? "" })
        {
            if (field.Contains(':'))
                return $"{{\"error\":\"a colon cannot appear in a connection field\",\"field\":{Quote(field)}}}";
        }

        float delay = float.TryParse(delayText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f;

        int times = int.TryParse(timesText, out int t) ? t : -1;

        string value = $"{output} {target}:{input}:{parameter ?? ""}:" +
                       $"{delay.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{times}";

        string command = BuildEntFire(source, "AddOutput", value, null);
        var before = Marks();

        string result = OnGameThread(() =>
        {
            _hooks.Console.Execute(command);

            _events.Add(EntityEventKind.Input, "", source,
                $"connected {output} -> {target}.{input}", _hooks.World.Now);

            return $"{{\"ok\":true,\"command\":{Quote(command)}}}";
        }, TimedOut);

        return Stamped(result, before);
    }

    /// <summary>
    /// <c>/player/teleport?player=N&amp;pos=x,y,z&amp;ang=p,y,r&amp;settle=1</c>:
    /// setpos_exact's teleport (the player's physics shadow goes with it) and
    /// setang's view snap, on the game thread, without needing sv_cheats; with
    /// settle, answered once the player stands on something and has stopped,
    /// counted in server ticks, instead of after a guessed sleep.
    /// </summary>
    private HttpReply Teleport(HttpRequest request)
    {
        int slot = int.TryParse(Get(request, "player"), out int p) && p > 0 ? p : 1;

        if (ParsePoint(Get(request, "pos")) is not { } pos)
            return new HttpReply(400, "{\"ok\":false,\"error\":\"pos=x,y,z is required\"}");

        DebugVec3? ang = null;
        if (Get(request, "ang") is { Length: > 0 } angText)
        {
            ang = ParsePoint(angText);
            if (ang is null)
                return new HttpReply(400, "{\"ok\":false,\"error\":\"ang is pitch,yaw,roll\"}");
        }

        bool settle = Get(request, "settle") is { Length: > 0 } st && st != "0";
        double eps = double.TryParse(Get(request, "eps"), NumberStyles.Float, CultureInfo.InvariantCulture, out double e) && e >= 0 ? e : 1.0;
        int forTicks = int.TryParse(Get(request, "for"), out int f) && f > 0 ? f : 3;
        int timeout = int.TryParse(Get(request, "timeout"), out int t) && t > 0 ? Math.Min(t, MaxWaitTicks) : DefaultWaitTicks;

        var before = Marks();
        uint player = IDebugWorld.NoEntity;
        int tick = 0;

        string moved = OnGameThread(() =>
        {
            player = _hooks.World.PlayerHandle(slot);
            if (!_hooks.World.IsValid(player))
                return $"{{\"ok\":false,\"error\":\"no player in slot {slot}\"}}";

            _hooks.World.Teleport(player, pos, null, zeroVelocity: true);
            if (ang is { } a)
                _hooks.World.SnapEyeAngles(player, a);

            tick = _hooks.World.Tick;
            return "{\"ok\":true}";
        }, TimedOut);

        if (moved != "{\"ok\":true}")
            return new HttpReply(moved == TimedOut ? 503 : 404, Stamped(moved, before));

        string teleported = $",\"player\":{slot},\"handle\":{player},\"teleportTick\":{tick}";

        if (!settle)
            return Ok(Stamped("{\"ok\":true" + teleported + "}", before));

        var reply = AwaitCondition(new LandedCondition(player, eps, forTicks), timeout, DefaultWall(timeout),
                                   _ => teleported);
        return reply with { Body = Stamped(reply.Body, before) };
    }

    /// <summary>
    /// <c>/entities/remove?name=&lt;glob&gt;</c> (or <c>class=</c>, <c>index=</c>,
    /// <c>handle=</c>): removes what matches and answers once every one of them
    /// is gone from the world, counted in ticks -- not after a guessed sleep
    /// following a Kill.
    /// </summary>
    private HttpReply RemoveEntities(HttpRequest request)
    {
        string? nameGlob = Get(request, "name");
        string? classGlob = Get(request, "class");
        string? error = null;
        var selector = string.IsNullOrEmpty(nameGlob)
            ? EntitySelector.Parse(key => key == "name" ? null : Get(request, key), out error)
            : null;

        if (error is not null)
            return new HttpReply(400, $"{{\"ok\":false,\"error\":{Quote(error)}}}");

        if (string.IsNullOrEmpty(nameGlob) && string.IsNullOrEmpty(classGlob) && selector is null)
            return new HttpReply(400, "{\"ok\":false,\"error\":\"name what to remove: name=<glob>, class=<glob>, index or handle\"}");

        int timeout = int.TryParse(Get(request, "timeout"), out int t) && t > 0 ? Math.Min(t, MaxWaitTicks) : 30;
        var before = Marks();
        var removed = new List<uint>();
        var json = new StringBuilder();
        int removeTick = 0;

        string done = OnGameThread(() =>
        {
            var world = _hooks.World;
            removeTick = world.Tick;

            // A name through the engine's own matcher (a trailing * is its
            // wildcard), the one FindByName and the Kill input use. Not the
            // name read back off each entity: that read can lag a rename made
            // through AddOutput (measured: a spawned crate renamed by
            // /entities/spawn read back its temporary name while FindByName
            // found it by the new one).
            IEnumerable<uint> candidates = selector is not null
                ? [selector.Resolve(world)]
                : !string.IsNullOrEmpty(nameGlob)
                    ? world.FindAllByName(nameGlob)
                    : world.FindAllByClassname("*");

            foreach (uint entity in candidates)
            {
                if (entity == IDebugWorld.NoEntity || !world.IsValid(entity))
                    continue;

                // Never the world or a player: a cleanup glob that matched
                // either would end the session it is cleaning up after.
                if (world.GetIndex(entity) == 0 || world.GetClassName(entity) is "player" or "ss_player")
                    continue;

                if (!string.IsNullOrEmpty(classGlob) && !Glob.Matches(classGlob, world.GetClassName(entity)))
                    continue;

                if (removed.Count > 0)
                    json.Append(',');
                json.Append("{\"handle\":").Append(entity)
                    .Append(",\"index\":").Append(world.GetIndex(entity))
                    .Append(",\"classname\":").Append(Quote(world.GetClassName(entity)))
                    .Append(",\"name\":").Append(Quote(world.GetName(entity))).Append('}');

                removed.Add(entity);
                world.Remove(entity);
            }

            return "{\"ok\":true}";
        }, TimedOut);

        if (done != "{\"ok\":true}")
            return new HttpReply(503, Stamped(done, before));

        string listed = $",\"removeTick\":{removeTick},\"removed\":{removed.Count},\"entities\":[{json}]";

        if (removed.Count == 0)
            return Ok(Stamped("{\"ok\":true,\"status\":\"nothing matched\"" + listed + "}", before));

        var reply = AwaitCondition(new AllGoneCondition(removed), timeout, DefaultWall(timeout), _ => listed);
        return reply with { Body = Stamped(reply.Body, before) };
    }

    /// <summary>
    /// Where a player is looking.
    ///
    /// The UI needs this to show where a group would land before committing to
    /// putting it there, and the spawner needs it to place one.
    /// </summary>
    private string PlayerAim(string? playerText)
    {
        int index = int.TryParse(playerText, out int requested) && requested > 0 ? requested : 1;
        uint player = _hooks.World.PlayerHandle(index);

        if (!_hooks.World.IsValid(player))
            return $"{{\"error\":\"no player in slot {index}\"}}";

        uint hitEntity = _hooks.World.TraceAim(player, 8192f, out var hit);
        var eye = _hooks.World.GetOrigin(player);

        return $"{{\"player\":{index},\"eye\":[{F(eye.X)},{F(eye.Y)},{F(eye.Z)}]," +
               $"\"aim\":[{F(hit.X)},{F(hit.Y)},{F(hit.Z)}]," +
               $"\"hit\":{(_hooks.World.IsValid(hitEntity) ? "true" : "false")}," +
               $"\"hitClass\":{Quote(_hooks.World.IsValid(hitEntity) ? _hooks.World.GetClassName(hitEntity) : "")}}}";
    }

    /// <summary>
    /// Creates a saved group in the running world.
    ///
    /// Every creation is verified and reported individually. ent_create fails
    /// quietly for an unknown class and quietly for everything when no player
    /// has issued a console command this session, and a group that silently
    /// produced nothing would be the worst possible answer here.
    /// </summary>
    private string SpawnGroup(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "{\"error\":\"no group given\"}";

        Placement placement;
        int player;
        string name;
        List<SpawnEntity> entities;

        try
        {
            (placement, player, name, entities) = EntitySpawner.Parse(body);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote("could not read that group: " + e.Message)}}}";
        }

        if (entities.Count == 0)
            return "{\"error\":\"that group has no entities in it\"}";

        var before = Marks();

        string result = OnGameThread(() =>
        {
            var spawned = Groups.Spawn(placement, player, entities);
            int created = spawned.Count(r => r.Created);

            _events.Add(EntityEventKind.Spawned, "", name.Length > 0 ? name : "(group)",
                $"spawned {created} of {spawned.Count} at " +
                (placement == Placement.Look ? "the aim point" : "their own origins"),
                _hooks.World.Now);

            var json = new StringBuilder("{\"ok\":true,\"created\":").Append(created)
                .Append(",\"requested\":").Append(spawned.Count)
                .Append(",\"entities\":[");

            for (int i = 0; i < spawned.Count; i++)
            {
                if (i > 0)
                    json.Append(',');

                json.Append("{\"classname\":").Append(Quote(spawned[i].ClassName))
                    .Append(",\"name\":").Append(Quote(spawned[i].ActualName))
                    .Append(",\"index\":").Append(spawned[i].Index)
                    // The handle as well as the index: an index is reused the
                    // moment the entity is freed, and a caller that goes on to
                    // wait on it or remove it must name THIS entity.
                    .Append(",\"handle\":").Append(spawned[i].Handle)
                    .Append(",\"created\":").Append(spawned[i].Created ? "true" : "false")
                    .Append(",\"error\":").Append(Quote(spawned[i].Error));

                // Where it actually is, read back: the proof that an exact
                // placement was exact, rather than the request echoed.
                if (spawned[i].Created && _hooks.World.IsValid(spawned[i].Handle))
                {
                    var at = _hooks.World.GetOrigin(spawned[i].Handle);
                    var facing = _hooks.World.GetAngles(spawned[i].Handle);
                    json.Append(",\"origin\":[").Append(F(at.X)).Append(',').Append(F(at.Y)).Append(',').Append(F(at.Z))
                        .Append("],\"angles\":[").Append(F(facing.X)).Append(',').Append(F(facing.Y)).Append(',').Append(F(facing.Z))
                        .Append(']');
                }

                json
                    .Append('}');
            }

            return json.Append("]}").ToString();
        }, TimedOut);

        return Stamped(result, before);
    }

    /// <summary>
    /// An ent_fire command line.
    ///
    /// Every field is quoted the same way a convar value is, because a
    /// targetname or a parameter with a space in it would otherwise be read as
    /// two arguments and fire something else entirely -- or nothing.
    /// </summary>
    internal static string BuildEntFire(string target, string input, string? parameter, string? delay)
    {
        var command = new StringBuilder("ent_fire ")
            .Append(ConsoleArgument(target))
            .Append(' ')
            .Append(ConsoleArgument(input));

        bool hasParameter = !string.IsNullOrEmpty(parameter);
        bool hasDelay = !string.IsNullOrWhiteSpace(delay) && delay != "0";

        // A delay cannot be given without a parameter -- ent_fire reads them
        // positionally -- so an empty one is spelled out when a delay follows.
        if (hasParameter || hasDelay)
            command.Append(' ').Append(ConsoleArgument(parameter ?? ""));

        if (hasDelay)
            command.Append(' ').Append(ConsoleArgument(delay!));

        return command.ToString();
    }

    //-------------------------------------------------------------------------
    // The map file.
    //
    // None of this touches the engine, so none of it is queued. That is what
    // makes the static view usable before a map loads and while the game is
    // busy -- and a debugger you cannot open while the game is wedged is a
    // debugger missing its best moment.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which map a request means. The named one, or the one that is loaded.
    /// Empty when neither, which the caller reports rather than guessing at.
    /// </summary>
    private string ResolveMapName(string? requested)
        => !string.IsNullOrWhiteSpace(requested) ? requested : _hooks.Status.CurrentMap;

    private string MapInfo(string? requested)
    {
        string name = ResolveMapName(requested);
        var available = _maps.Available();

        var json = new StringBuilder("{\"current\":")
            .Append(Quote(_hooks.Status.CurrentMap))
            .Append(",\"directory\":").Append(Quote(_maps.MapDirectory))
            .Append(",\"available\":");

        AppendStrings(json, available);

        if (name.Length == 0)
            return json.Append(",\"loaded\":false}").ToString();

        try
        {
            var contents = _maps.Read(name, withGeometry: false);

            json.Append(",\"loaded\":true,\"map\":").Append(Quote(name))
                .Append(",\"path\":").Append(Quote(contents.Path))
                .Append(",\"version\":").Append(contents.Version)
                .Append(",\"entityCount\":").Append(contents.Entities.Count)
                .Append(",\"brushModelCount\":").Append(contents.Models.Count)
                .Append(",\"connectionCount\":").Append(contents.Entities.Sum(e => e.Connections.Count));

            return json.Append('}').ToString();
        }
        catch (Exception e)
        {
            // A map that will not open is worth naming. The common cause is a
            // map the game loaded from somewhere this does not look -- a
            // workshop path, another mod -- and an empty view would not say so.
            return json.Append(",\"loaded\":false,\"error\":").Append(Quote(e.Message)).Append('}').ToString();
        }
    }

    private string MapEntities(string? requested)
    {
        string name = ResolveMapName(requested);

        if (name.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        BspFile.Contents contents;

        try
        {
            contents = _maps.Read(name, withGeometry: false);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(name)}}}";
        }

        var json = new StringBuilder("{\"map\":").Append(Quote(name)).Append(",\"entities\":[");

        for (int i = 0; i < contents.Entities.Count; i++)
        {
            var entity = contents.Entities[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"id\":").Append(entity.Id)
                .Append(",\"classname\":").Append(Quote(entity.ClassName))
                .Append(",\"targetname\":").Append(Quote(entity.TargetName))
                .Append(",\"model\":").Append(Quote(entity.Model))
                .Append(",\"brushModel\":").Append(entity.BrushModel)
                .Append(",\"origin\":");

            AppendVector(json, entity.Origin);
            json.Append(",\"angles\":");
            AppendVector(json, entity.Angles);

            json.Append(",\"keyvalues\":[");
            for (int k = 0; k < entity.KeyValues.Count; k++)
            {
                if (k > 0)
                    json.Append(',');

                json.Append("{\"key\":").Append(Quote(entity.KeyValues[k].Key))
                    .Append(",\"value\":").Append(Quote(entity.KeyValues[k].Value)).Append('}');
            }

            json.Append("],\"connections\":[");
            for (int c = 0; c < entity.Connections.Count; c++)
            {
                var connection = entity.Connections[c];

                if (c > 0)
                    json.Append(',');

                json.Append("{\"output\":").Append(Quote(connection.Output))
                    .Append(",\"target\":").Append(Quote(connection.Target))
                    .Append(",\"input\":").Append(Quote(connection.Input))
                    .Append(",\"param\":").Append(Quote(connection.Parameter))
                    .Append(",\"delay\":").Append(N(connection.Delay))
                    .Append(",\"times\":").Append(connection.TimesToFire)
                    .Append('}');
            }

            json.Append("]}");
        }

        return json.Append("],\"count\":").Append(contents.Entities.Count).Append('}').ToString();
    }

    private string MapGeometry(string? requested)
    {
        string name = ResolveMapName(requested);

        if (name.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        BspFile.Contents contents;

        try
        {
            contents = _maps.Read(name, withGeometry: true);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(name)}}}";
        }

        var geometry = contents.Geometry;

        if (geometry is null)
            return $"{{\"error\":\"this map has no geometry\",\"map\":{Quote(name)}}}";

        var json = new StringBuilder("{\"map\":").Append(Quote(name)).Append(",\"mins\":");
        AppendVector(json, geometry.Mins);
        json.Append(",\"maxs\":");
        AppendVector(json, geometry.Maxs);

        json.Append(",\"vertices\":");
        AppendNumbers(json, geometry.Vertices);

        // Faces are sent as a start and a length into one index array rather
        // than as nested arrays: a map is thousands of faces, and the brackets
        // alone would be a fifth of the payload.
        json.Append(",\"faceStarts\":");
        AppendNumbers(json, geometry.FaceStarts);
        json.Append(",\"faceLengths\":");
        AppendNumbers(json, geometry.FaceLengths);
        json.Append(",\"faceIndices\":");
        AppendNumbers(json, geometry.FaceIndices);

        json.Append(",\"models\":[");
        for (int i = 0; i < contents.Models.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"mins\":");
            AppendVector(json, contents.Models[i].Mins);
            json.Append(",\"maxs\":");
            AppendVector(json, contents.Models[i].Maxs);
            json.Append(",\"origin\":");
            AppendVector(json, contents.Models[i].Origin);
            json.Append('}');
        }

        return json.Append("]}").ToString();
    }

    /// <summary>
    /// Everything in a map file.
    ///
    /// One reply rather than an endpoint per section: the point of the view this
    /// feeds is that the parts are read against each other -- how much of the
    /// file the lightmaps are, whether the material with no faces is also the one
    /// missing from the pakfile -- and a page that fetched nine things would show
    /// nine different moments of the same file.
    ///
    /// Off the game thread like the rest of the map routes. It is a file, and the
    /// engine is not being asked about it.
    /// </summary>
    private string MapFile(string? requested)
    {
        string name = ResolveMapName(requested);

        if (name.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        BspReport report;

        try
        {
            report = _maps.ReadReport(name);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(name)}}}";
        }

        var json = new StringBuilder(1 << 16);

        json.Append("{\"map\":").Append(Quote(report.Map))
            .Append(",\"path\":").Append(Quote(report.Path))
            .Append(",\"bytes\":").Append(report.Bytes)
            .Append(",\"ident\":").Append(Quote(report.Ident))
            .Append(",\"version\":").Append(report.Version)
            .Append(",\"revision\":").Append(report.Revision)
            .Append(",\"unaccounted\":").Append(report.Unaccounted)
            .Append(",\"visClusters\":").Append(report.VisClusters)
            .Append(",\"visAverage\":").Append(report.VisAverage)
            .Append(",\"visMax\":").Append(report.VisMax)
            .Append(",\"occluders\":").Append(report.Occluders)
            .Append(",\"levelFlags\":").Append(Quote(report.LevelFlags))
            .Append(",\"entityCount\":").Append(report.Entities.Count)
            .Append(",\"connectionCount\":").Append(report.Entities.Sum(e => e.Connections.Count));

        json.Append(",\"warnings\":");
        AppendStrings(json, report.Warnings);

        json.Append(",\"lumps\":[");
        for (int i = 0; i < report.Lumps.Count; i++)
        {
            var lump = report.Lumps[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(lump.Index)
                .Append(",\"name\":").Append(Quote(lump.Name))
                .Append(",\"offset\":").Append(lump.Offset)
                .Append(",\"length\":").Append(lump.Length)
                .Append(",\"version\":").Append(lump.Version)
                .Append(",\"uncompressedSize\":").Append(lump.UncompressedSize)
                .Append(",\"stride\":").Append(lump.Stride)
                .Append(",\"records\":").Append(lump.Records)
                .Append(",\"note\":").Append(Quote(lump.Note))
                .Append('}');
        }

        json.Append("],\"gameLumps\":[");
        for (int i = 0; i < report.GameLumps.Count; i++)
        {
            var lump = report.GameLumps[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"id\":").Append(Quote(lump.Id))
                .Append(",\"flags\":").Append(lump.Flags)
                .Append(",\"version\":").Append(lump.Version)
                .Append(",\"offset\":").Append(lump.Offset)
                .Append(",\"length\":").Append(lump.Length)
                .Append('}');
        }

        json.Append("],\"classes\":[");
        for (int i = 0; i < report.Classes.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"classname\":").Append(Quote(report.Classes[i].ClassName))
                .Append(",\"count\":").Append(report.Classes[i].Count).Append('}');
        }

        json.Append("],\"materials\":[");
        for (int i = 0; i < report.TexDatas.Count; i++)
        {
            var data = report.TexDatas[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(data.Index)
                .Append(",\"name\":").Append(Quote(data.Name))
                .Append(",\"width\":").Append(data.Width)
                .Append(",\"height\":").Append(data.Height)
                .Append(",\"viewWidth\":").Append(data.ViewWidth)
                .Append(",\"viewHeight\":").Append(data.ViewHeight)
                .Append(",\"faces\":").Append(data.Faces)
                .Append(",\"area\":").Append(N(data.Area))
                .Append(",\"luxels\":").Append(data.Luxels)
                .Append(",\"reflectivity\":");

            AppendVector(json, data.Reflectivity);
            json.Append('}');
        }

        // The names on their own as well as the decoded records: a map can name a
        // material its texdata never points at, and the difference between the
        // two lists is exactly that.
        json.Append("],\"materialNames\":");
        AppendStrings(json, report.Materials);

        json.Append(",\"propModels\":");
        AppendStrings(json, report.PropModels);

        json.Append(",\"props\":[");
        for (int i = 0; i < report.Props.Count; i++)
        {
            var prop = report.Props[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(prop.Index)
                .Append(",\"model\":").Append(Quote(prop.Model))
                .Append(",\"origin\":");
            AppendVector(json, prop.Origin);
            json.Append(",\"angles\":");
            AppendVector(json, prop.Angles);

            json.Append(",\"skin\":").Append(prop.Skin)
                .Append(",\"fadeMin\":").Append(N(prop.FadeMin))
                .Append(",\"fadeMax\":").Append(N(prop.FadeMax))
                .Append(",\"solid\":").Append(prop.Solid)
                .Append(",\"flags\":").Append(prop.Flags)
                .Append(",\"firstLeaf\":").Append(prop.FirstLeaf)
                .Append(",\"leafCount\":").Append(prop.LeafCount)
                .Append('}');
        }

        json.Append("],\"cubemaps\":[");
        for (int i = 0; i < report.Cubemaps.Count; i++)
        {
            var cubemap = report.Cubemaps[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"origin\":[").Append(cubemap.Origin[0]).Append(',')
                .Append(cubemap.Origin[1]).Append(',').Append(cubemap.Origin[2])
                .Append("],\"size\":").Append(cubemap.Size).Append('}');
        }

        json.Append("],\"worldlights\":[");
        for (int i = 0; i < report.WorldLights.Count; i++)
        {
            var light = report.WorldLights[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(light.Index)
                .Append(",\"origin\":");
            AppendVector(json, light.Origin);
            json.Append(",\"intensity\":");
            AppendVector(json, light.Intensity);
            json.Append(",\"normal\":");
            AppendVector(json, light.Normal);

            json.Append(",\"type\":").Append(Quote(light.Type))
                .Append(",\"style\":").Append(light.Style)
                .Append(",\"radius\":").Append(N(light.Radius))
                .Append(",\"owner\":").Append(light.Owner)
                .Append(",\"hdr\":").Append(light.Hdr ? "true" : "false")
                .Append('}');
        }

        json.Append("],\"overlays\":[");
        for (int i = 0; i < report.Overlays.Count; i++)
        {
            var overlay = report.Overlays[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"id\":").Append(overlay.Id)
                .Append(",\"texinfo\":").Append(overlay.TexInfo)
                .Append(",\"material\":").Append(Quote(overlay.Material))
                .Append(",\"origin\":");
            AppendVector(json, overlay.Origin);
            json.Append(",\"faces\":").Append(overlay.FaceCount).Append('}');
        }

        json.Append("],\"displacements\":[");
        for (int i = 0; i < report.Displacements.Count; i++)
        {
            var disp = report.Displacements[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(disp.Index)
                .Append(",\"start\":");
            AppendVector(json, disp.StartPosition);

            json.Append(",\"power\":").Append(disp.Power)
                .Append(",\"face\":").Append(disp.MapFace)
                .Append(",\"contents\":").Append(disp.Contents)
                .Append(",\"vertices\":").Append(disp.Vertices)
                .Append('}');
        }

        json.Append("],\"pak\":[");
        for (int i = 0; i < report.Pak.Count; i++)
        {
            var entry = report.Pak[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"name\":").Append(Quote(entry.Name))
                .Append(",\"bytes\":").Append(entry.Bytes)
                .Append(",\"compressed\":").Append(entry.Compressed)
                .Append('}');
        }

        json.Append("],\"areas\":[");
        for (int i = 0; i < report.Areas.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(report.Areas[i].Index)
                .Append(",\"firstPortal\":").Append(report.Areas[i].FirstPortal)
                .Append(",\"portals\":").Append(report.Areas[i].Portals)
                .Append('}');
        }

        json.Append("],\"areaportals\":[");
        for (int i = 0; i < report.AreaPortals.Count; i++)
        {
            var portal = report.AreaPortals[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(portal.Index)
                .Append(",\"key\":").Append(portal.Key)
                .Append(",\"otherArea\":").Append(portal.OtherArea)
                .Append(",\"plane\":").Append(portal.Plane)
                .Append(",\"firstVertex\":").Append(portal.FirstVertex)
                .Append(",\"vertices\":").Append(portal.Vertices)
                .Append('}');
        }

        json.Append("],\"water\":[");
        for (int i = 0; i < report.Water.Count; i++)
        {
            var water = report.Water[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(water.Index)
                .Append(",\"surfaceZ\":").Append(N(water.SurfaceZ))
                .Append(",\"minZ\":").Append(N(water.MinZ))
                .Append(",\"texinfo\":").Append(water.TexInfo)
                .Append(",\"material\":").Append(Quote(water.Material))
                .Append('}');
        }

        json.Append("],\"detailModels\":[");
        for (int i = 0; i < report.DetailModels.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(report.DetailModels[i].Index)
                .Append(",\"name\":").Append(Quote(report.DetailModels[i].Name))
                .Append('}');
        }

        json.Append("],\"models\":[");
        for (int i = 0; i < report.Models.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append("{\"mins\":");
            AppendVector(json, report.Models[i].Mins);
            json.Append(",\"maxs\":");
            AppendVector(json, report.Models[i].Maxs);
            json.Append(",\"origin\":");
            AppendVector(json, report.Models[i].Origin);
            json.Append('}');
        }

        return json.Append("]}").ToString();
    }

    /// <summary>
    /// One lump of a map, decoded.
    ///
    /// The lumps answered here are the ones too big to send with everything else:
    /// a medium map has six thousand faces and sixteen thousand brush sides. A
    /// lump this cannot decode is answered as such rather than as an empty list,
    /// because "there is nothing in this lump" and "nothing here reads this lump"
    /// are different answers and only one of them is about the map.
    /// </summary>
    private string MapLump(string? requested, string? lumpText)
    {
        string name = ResolveMapName(requested);

        if (name.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        if (!int.TryParse(lumpText, out int lump) || lump < 0 || lump >= 64)
            return "{\"error\":\"a lump is numbered 0 to 63\"}";

        BspLumpDetail detail;

        try
        {
            detail = _maps.ReadLump(name, lump);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(name)}}}";
        }

        var json = new StringBuilder(1 << 16);

        json.Append("{\"map\":").Append(Quote(detail.Map))
            .Append(",\"lump\":").Append(detail.Lump)
            .Append(",\"name\":").Append(Quote(detail.Name))
            .Append(",\"known\":").Append(detail.Known ? "true" : "false");

        json.Append(",\"faces\":[");
        for (int i = 0; i < detail.Faces.Count; i++)
        {
            var face = detail.Faces[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(face.Index)
                .Append(",\"material\":").Append(Quote(face.Material))
                .Append(",\"surface\":").Append(Quote(face.Surface))
                .Append(",\"area\":").Append(N(face.Area))
                .Append(",\"lightmapWidth\":").Append(face.LightmapWidth)
                .Append(",\"lightmapHeight\":").Append(face.LightmapHeight)
                .Append(",\"luxels\":").Append(face.Luxels)
                .Append(",\"styles\":").Append(face.Styles)
                .Append(",\"texinfo\":").Append(face.TexInfo)
                .Append(",\"plane\":").Append(face.Plane)
                .Append(",\"side\":").Append(face.Side ? "true" : "false")
                .Append(",\"onNode\":").Append(face.OnNode ? "true" : "false")
                .Append(",\"firstEdge\":").Append(face.FirstEdge)
                .Append(",\"edges\":").Append(face.Edges)
                .Append(",\"dispInfo\":").Append(face.DispInfo)
                .Append(",\"origFace\":").Append(face.OrigFace)
                .Append(",\"primitives\":").Append(face.Primitives)
                .Append('}');
        }

        json.Append("],\"texinfos\":[");
        for (int i = 0; i < detail.TexInfos.Count; i++)
        {
            var info = detail.TexInfos[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(info.Index)
                .Append(",\"material\":").Append(Quote(info.Material))
                .Append(",\"surface\":").Append(Quote(info.Surface))
                .Append(",\"lightmapScale\":").Append(N(info.LightmapScale))
                .Append(",\"texdata\":").Append(info.TexData)
                .Append('}');
        }

        json.Append("],\"leaves\":[");
        for (int i = 0; i < detail.Leaves.Count; i++)
        {
            var leaf = detail.Leaves[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(leaf.Index)
                .Append(",\"cluster\":").Append(leaf.Cluster)
                .Append(",\"area\":").Append(leaf.Area)
                .Append(",\"contents\":").Append(Quote(leaf.Contents))
                .Append(",\"firstFace\":").Append(leaf.FirstFace)
                .Append(",\"faces\":").Append(leaf.Faces)
                .Append(",\"firstBrush\":").Append(leaf.FirstBrush)
                .Append(",\"brushes\":").Append(leaf.Brushes)
                .Append(",\"water\":").Append(leaf.Water)
                .Append(",\"mins\":[").Append(leaf.Mins[0]).Append(',').Append(leaf.Mins[1])
                .Append(',').Append(leaf.Mins[2])
                .Append("],\"maxs\":[").Append(leaf.Maxs[0]).Append(',').Append(leaf.Maxs[1])
                .Append(',').Append(leaf.Maxs[2])
                .Append("]}");
        }

        json.Append("],\"brushes\":[");
        for (int i = 0; i < detail.Brushes.Count; i++)
        {
            var brush = detail.Brushes[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(brush.Index)
                .Append(",\"contents\":").Append(Quote(brush.Contents))
                .Append(",\"firstSide\":").Append(brush.FirstSide)
                .Append(",\"sides\":").Append(brush.Sides)
                .Append('}');
        }

        json.Append("],\"brushsides\":[");
        for (int i = 0; i < detail.BrushSides.Count; i++)
        {
            var side = detail.BrushSides[i];

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(side.Index)
                .Append(",\"material\":").Append(Quote(side.Material))
                .Append(",\"plane\":").Append(side.Plane)
                .Append(",\"texinfo\":").Append(side.TexInfo)
                .Append(",\"dispInfo\":").Append(side.DispInfo)
                .Append(",\"bevel\":").Append(side.Bevel ? "true" : "false")
                .Append('}');
        }

        return json.Append("]}").ToString();
    }

    /// <summary>
    /// Writes an edited entity list back into a map file.
    ///
    /// Only the entity lump is touched; everything else in the BSP is copied
    /// through untouched. The map being written is copied into a numbered backup
    /// first, so nothing that was there is lost by saving over it.
    ///
    /// Off the game thread, like the rest of the map routes. It is a file the
    /// engine is not being asked about, and a save that stalled a frame while it
    /// wrote eleven megabytes would be felt.
    /// </summary>
    private string SaveMap(string? sourceName, string? destinationName, string body)
    {
        string source = ResolveMapName(sourceName);

        if (source.Length == 0)
            return "{\"error\":\"no map loaded and none named\"}";

        string destination = string.IsNullOrWhiteSpace(destinationName) ? source : destinationName;

        List<MapEntity> entities;

        try
        {
            MapSource.CheckName(destination, "as");
            entities = EntitySpawner.ParseEntities(body);
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)}}}";
        }

        if (entities.Count == 0)
            return "{\"error\":\"no entities given; refusing to write an empty map\"}";

        try
        {
            var report = BspWriter.Write(_maps.PathFor(source), _maps.PathFor(destination), entities);

            // The cache is keyed on the file's write time, but the map that was
            // just written is the one about to be read back — forgetting it
            // outright removes any chance of answering from the old parse.
            _maps.Forget(destination);

            _events.Add(EntityEventKind.Level, "", destination,
                $"saved {report.EntityCount} entities to {Path.GetFileName(report.Path)}",
                _hooks.World.Now);

            return $"{{\"ok\":true,\"map\":{Quote(destination)},\"path\":{Quote(report.Path)}," +
                   $"\"entityCount\":{report.EntityCount},\"bytes\":{report.Bytes}," +
                   $"\"backup\":{Quote(report.BackupPath ?? "")}," +
                   $"\"overwrote\":{(destination == source ? "true" : "false")}}}";
        }
        catch (Exception e)
        {
            return $"{{\"error\":{Quote(e.Message)},\"map\":{Quote(destination)}}}";
        }
    }

    private static void AppendVector(StringBuilder json, IReadOnlyList<float> vector)
    {
        json.Append('[');

        for (int i = 0; i < vector.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(N(vector[i]));
        }

        json.Append(']');
    }

    private static void AppendNumbers(StringBuilder json, float[] values)
    {
        json.Append('[');

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(N(values[i]));
        }

        json.Append(']');
    }

    private static void AppendNumbers(StringBuilder json, int[] values)
    {
        json.Append('[');

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(values[i]);
        }

        json.Append(']');
    }

    /// <summary>
    /// A number for a payload rather than for a person.
    ///
    /// Trailing zeroes are dropped because geometry is tens of thousands of
    /// these and ".00" on every one is a tenth of the response. Two decimals is
    /// well inside what a map's units mean -- they are inches.
    /// </summary>
    private static string N(float value)
        => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void AppendStrings(StringBuilder json, IReadOnlyList<string> values)
    {
        json.Append('[');

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append(Quote(values[i]));
        }

        json.Append(']');
    }

    /// <summary>
    /// A listing the host never wired up. Only reachable from a host that
    /// implements IDebugApi partially — saying which one is missing beats an empty
    /// array, which reads as "there are none of those".
    /// </summary>
    /// <summary>
    /// A bound, or null where there is none.
    ///
    /// null rather than an omitted field or a sentinel number: zero is a
    /// perfectly ordinary minimum, and any number chosen to mean "unbounded"
    /// would be a number some convar legitimately clamps to.
    /// </summary>
    private static string Bound(float? value)
        => value.HasValue
            ? value.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : "null";

    private static string NotSupplied(string what)
        => $"{{\"error\":\"the host did not supply {what}\"}}";

    /// <summary>
    /// A value as the console must receive it.
    ///
    /// The console splits on whitespace, so "hello there" would arrive as a
    /// convar set to "hello" with a stray argument after it — and a greeting
    /// convar silently losing every word but the first is exactly the sort of
    /// thing this tool exists to make visible, not to cause.
    /// </summary>
    private static string ConsoleArgument(string value)
        => value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')
            ? value
            : $"\"{value.Replace("\"", "")}\"";

    /// <summary>
    /// Reads a convar back.
    ///
    /// Only convars this mod declared can be read. Anything else can still be
    /// SET through the console, and saying so plainly beats reporting an empty
    /// value as though it were the real one.
    /// </summary>
    /// <summary>
    /// A convar's value, ours or the engine's.
    ///
    /// Ours first, because a convar this mod declared is a managed object whose
    /// cached value IS the value, and because the engine holds ours too — asking
    /// it would work but would answer about the copy rather than the original.
    ///
    /// The fallback is what makes the console box's convars readable at all: the
    /// engine's thousands could always be SET from here and, until the registry
    /// could be walked, never read back. A name that is neither is now genuinely
    /// unknown rather than merely not ours, and says so.
    /// </summary>
    private string ReadConVar(string name)
        => _hooks.Console.ValueOf(name)
        ?? "(no convar of that name; a command has no value to read)";

    //-------------------------------------------------------------------------
    // Plumbing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Runs something on the game thread and waits for it.
    ///
    /// Every engine call has to go through here: the engine is single-threaded
    /// and the listener is not, and calling in from the wrong thread is the kind
    /// of mistake that corrupts state rather than crashing cleanly.
    /// </summary>
    /// <summary>How long status waits before reporting the game as not ticking.</summary>
    private static readonly TimeSpan DefaultStatusTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// <see cref="DefaultStatusTimeout"/> in the game; set short by a test on a
    /// server nothing pumps. See <see cref="WorkTimeout"/>.
    /// </summary>
    internal TimeSpan StatusTimeout { get; init; } = DefaultStatusTimeout;

    private string OnGameThread(Func<string> work, string onTimeout) => OnGameThread(work, onTimeout, WorkTimeout);

    private string OnGameThread(Func<string> work, string onTimeout, TimeSpan timeout)
    {
        // NOTHING WILL DRAIN THE QUEUE. Answered here rather than enqueued and
        // waited on, because the thread that would run it is the one tearing
        // this object down. See _shuttingDown.
        if (_shuttingDown.IsCancellationRequested)
            return onTimeout;

        using var done = new ManualResetEventSlim(false);
        string result = onTimeout;

        _pending.Enqueue(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                result = $"{{\"error\":{Quote(e.Message)}}}";
            }
            finally
            {
                // The waiter may have given up and disposed this already -- on
                // the timeout path, and now on the cancelled one. Pump() would
                // catch and log it; saying so here keeps a shutdown quiet.
                try { done.Set(); }
                catch (ObjectDisposedException) { }
            }
        });

        try
        {
            return done.Wait(timeout, _shuttingDown.Token) ? result : onTimeout;
        }
        catch (OperationCanceledException)
        {
            // The reload took the game thread away mid-request. The caller gets
            // the same body a timeout gives, four seconds sooner, and this
            // thread goes back to its accept loop in time for the join.
            return onTimeout;
        }
    }

    private static string F(float value)
        => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Minimal JSON string escaping. Enough for log text and names.</summary>
    internal static string Quote(string text) => DebugJson.Quote(text);
}
