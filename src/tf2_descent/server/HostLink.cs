using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Shared;

namespace TF2.Descent.Server;

/// <summary>How this server runs: against a hosting service, or alone for local testing.</summary>
public enum HostingMode { Hosted, Unhosted }

/// <summary>
/// The one owner of the host SDK (plan_host_sdk §2.9). The SDK lives on its
/// own thread; the game talks to it through this class, whose methods may be
/// called on the game thread and whose tasks complete on the game thread, so
/// the code that awaits them may touch the engine. No SDK object that has
/// state (a lease, a reserve) ever leaves the SDK thread: the game names
/// players by SteamID.
/// </summary>
[SideService(Side.Server, As = [typeof(IHostLink)])]
public sealed partial class HostLink : IHostLink
{
    /// <summary>The flag that runs the server without a service, for local testing only (decision 6.1).</summary>
    public const string UnhostedFlag = "-unhosted";

    /// <summary>The variables that name a hosting service: the pod's three and the local file.</summary>
    public static IReadOnlyList<string> HostSettings => ["SOURCESHARP_HOST_SERVICE", "SOURCESHARP_HOST_INSTANCE_ID", "SOURCESHARP_HOST_INSTANCE_TOKEN", "SOURCESHARP_HOST_LOCAL"];

    private readonly ILocalCommandLine _commandLine;
    private readonly IProcessEnvironment _environment;
    private readonly IGameThreadDispatcher _game;
    private readonly IGameThreadLiveness _liveness;
    private readonly ILog _log;
    private readonly PathModuleSource _module;
    private readonly IHooks<IHostConnectionHook> _connection;
    private readonly IHooks<IHopReadyHold> _readyHolds;
    private readonly IHooks<IHostCommandHook> _commands;
    private readonly IModInfo? _mod;

    // Whether this is a dedicated server (Decide); unknown counts as dedicated,
    // the side that fails loudly.
    private readonly IEngineHost? _engine;
    private readonly HostSdkOptions _options;

    // SDK thread only: the Exec requests the game has not answered yet.
    private readonly Dictionary<string, ExecRequest> _execs = new(StringComparer.Ordinal);

    private SdkThread? _thread;
    private HostSdk? _sdk;
    private TaskCompletionSource<HostSdk>? _started;

    public HostLink(
        ILocalCommandLine commandLine,
        IProcessEnvironment environment,
        IGameThreadDispatcher game,
        IGameThreadLiveness liveness,
        ILog log,
        PathModuleSource module,
        IHooks<IHostConnectionHook> connection,
        IHooks<IHostCommandHook> commands,
        IHooks<ILeaseHook> leases,
        IHooks<IHopReadyHold> readyHolds,
        IEngineHost? engine = null,
        IModInfo? mod = null,
        IGameBuild? build = null)
    {
        _engine = engine;
        _mod = mod;
        _commands = commands;
        _leaseHooks = leases;
        _commandLine = commandLine;
        _environment = environment;
        _game = game;
        _liveness = liveness;
        _log = log;
        _module = module;
        _connection = connection;
        _readyHolds = readyHolds;
        _options = new HostSdkOptions
        {
            ModuleSource = module,

            // What SourceSharp loaded that is not a .NET assembly (the SDK
            // reports those itself at Booting): the mod and the native build.
            DeclaredModules = mod is null || build is null ? null : () => DeclaredModules(mod, build),
        };
    }

    /// <summary>
    /// The modules this server declares to the hosting service at Booting
    /// (SDK 2.3.0): the mods.yaml mod, versioned by the managed build, and
    /// SourceSharp's native build. Information only; never a gate.
    /// </summary>
    internal static IEnumerable<LoadedModuleInfo> DeclaredModules(IModInfo mod, IGameBuild build) =>
    [
        new LoadedModuleInfo(mod.Id, build.Managed, "mod"),
        new LoadedModuleInfo("sourcesharp-native", build.Native, "engine"),
    ];

    /// <summary>mods.yaml's browser block as the SDK takes it (SDK 2.6.0, host plan_steam_auth S-18).</summary>
    internal static BrowserBlock BrowserBlockOf(ModBrowser b) =>
        new(b.Name, b.Description, b.Tags, b.Region, b.MaxPlayers, b.Map,
            new Dictionary<string, string>(b.Rules, StringComparer.Ordinal));

    /// <summary>The options the SDK is started with.</summary>
    internal HostSdkOptions Options => _options;

    /// <summary>For tests: a clock other than the process'.</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    /// <summary>The SDK thread's managed id, once started: what the thread-affinity facts check.</summary>
    public int? SdkThreadId => _thread?.ManagedThreadId;

    /// <summary>
    /// Hosted when the command line or environment names a service; never both
    /// that and <see cref="UnhostedFlag"/>. With neither, a DEDICATED server is
    /// refused (a pod whose settings are broken must fail loudly), while a
    /// listen server is unhosted: every player's client runs one, and it has
    /// no service.
    /// </summary>
    /// <exception cref="InvalidOperationException">Both; or neither on a dedicated server: the reason names what to do.</exception>
    public static HostingMode Decide(IReadOnlyList<string> args, Func<string, string?> env, bool dedicated)
    {
        bool unhosted = args.Contains(UnhostedFlag, StringComparer.OrdinalIgnoreCase);
        bool hosted = args.Contains("-hostlocal", StringComparer.OrdinalIgnoreCase)
                      || HostSettings.Any(k => !string.IsNullOrEmpty(env(k)));

        return (hosted, unhosted) switch
        {
            (true, true) => throw new InvalidOperationException(
                $"{UnhostedFlag} was given together with host settings (-hostlocal or SOURCESHARP_HOST_*): run one or the other"),
            (true, false) => HostingMode.Hosted,
            (false, true) => HostingMode.Unhosted,
            _ when !dedicated => HostingMode.Unhosted,
            _ => throw new InvalidOperationException(
                $"no hosting service is configured: give -hostlocal <file|inproc> or the SOURCESHARP_HOST_* variables, " +
                $"or {UnhostedFlag} to run alone for local testing"),
        };
    }

    public HostingMode Mode() => Decide(_commandLine.Args, _environment.GetVariable, _engine?.IsDedicatedServer ?? true);

    /// <summary>Starts the SDK thread and the SDK on it. Game thread, once.</summary>
    public void Start()
    {
        if (_thread is not null)
            return;

        // Touch the rules module: the SDK announces the loaded assembly.
        _ = _module.Module;

        StartThread();
        _thread!.Post(StartSdk);
    }

    /// <summary>The SDK thread alone, without the SDK: what Start builds on, and what the facts drive.</summary>
    internal void StartThread()
    {
        _started = new TaskCompletionSource<HostSdk>();
        _thread = new SdkThread("tf2_descent-host-sdk", TimeSpan.FromMilliseconds(16), Pump);
        _thread.Start();
    }

    private async Task StartSdk()
    {
        try
        {
            var sdk = await HostSdk.StartAsync(_commandLine.Args, _environment.GetVariable, _options);
            AssertSdkThread();

            // The server-browser entry before any Booting (sdk-integration.md §4):
            // every Call, Booting's included, waits for _started, set below.
            if (_mod?.Browser is { } browser)
                sdk.Session.Browser = BrowserBlockOf(browser);

            _sdk = sdk;
            sdk.Session.ConnectionChanged += connected =>
                _game.Post(() => _connection.Each(nameof(IHostConnectionHook.OnConnectionChanged), h => h.OnConnectionChanged(connected)));
            Subscribe(sdk.Commands);
            _started!.SetResult(sdk);
        }
        catch (Exception e)
        {
            _started!.SetException(e);
        }
    }

    /// <summary>
    /// Pumps while the game thread is alive. A game thread that stops stamping
    /// its frames stops the pumps, and the SDK withholds its heartbeats, so the
    /// service sees a hung game exactly as the spec intends. Before the first
    /// frame (a long first map load) the stamp is 0 and the pumps go on.
    /// </summary>
    private void Pump()
    {
        if (_sdk is null || !GameThreadAlive())
            return;

        _sdk.Pump();
    }

    public bool GameThreadAlive()
    {
        long last = _liveness.LastFrameMs;
        return last == 0 || Clock() - last < (long)_options.MainThreadStallLimit.TotalMilliseconds;
    }

    private void AssertSdkThread()
    {
        if (_thread is { IsCurrent: false })
            throw new InvalidOperationException("a host SDK call off the SDK thread");
    }

    /// <summary>
    /// Runs <paramref name="call"/> on the SDK thread, once the SDK has started,
    /// and completes the returned task on the game thread. The task source is
    /// not RunContinuationsAsynchronously: the awaiting code continues inside
    /// the game thread's drain, where it may touch the engine.
    /// </summary>
    public Task<T> Call<T>(Func<HostSdk, Task<T>> call)
    {
        if (_started is null)
            throw new InvalidOperationException("the host link is not started");

        var started = _started.Task;
        return OnSdkThread(async () => await call(await started));
    }

    /// <summary>
    /// The marshalling under <see cref="Call{T}"/>: <paramref name="work"/> runs
    /// on the SDK thread, with every continuation it awaits back there, and its
    /// result completes the returned task on the game thread.
    /// </summary>
    internal Task<T> OnSdkThread<T>(Func<Task<T>> work)
    {
        if (_thread is null)
            throw new InvalidOperationException("the host link is not started");

        var done = new TaskCompletionSource<T>();
        _thread.Post(async () =>
        {
            try
            {
                AssertSdkThread();
                var result = await work();
                AssertSdkThread();
                _game.Post(() => done.SetResult(result));
            }
            catch (Exception e)
            {
                _game.Post(() => done.SetException(e));
            }
        });

        return done.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the SDK thread and does not wait: for a notice the game needs no answer to.</summary>
    public void Send(Func<HostSdk, Task> work)
    {
        if (_thread is null || _started is null)
            return;

        _thread.Post(async () =>
        {
            try
            {
                var sdk = await _started.Task;
                AssertSdkThread();
                await work(sdk);
            }
            catch (Exception e)
            {
                _log.Warning($"[TF2Descent] host call failed: {e.Message}\n");
            }
        });
    }

    /// <summary>
    /// The service's commands, raised inside Pump on this thread, each handed
    /// to the game thread as a hook call (spec §6, plan_host_sdk §2.11). A hop
    /// is carried out here first, so the game hears of it only once the
    /// character is released and the service told.
    /// </summary>
    private void Subscribe(IHostCommands commands)
    {
        commands.Kick += k => ToGame(nameof(IHostCommandHook.OnKick), h => h.OnKick(k));
        commands.Say += c => ToGame(nameof(IHostCommandHook.OnSay), h => h.OnSay(c));
        commands.Drain += d => ToGame(nameof(IHostCommandHook.OnDrain), h => h.OnDrain(d));
        commands.Shutdown += c => ToGame(nameof(IHostCommandHook.OnShutdown), h => h.OnShutdown(c));
        commands.Retry += r => ToGame(nameof(IHostCommandHook.OnRetry), h => h.OnRetry(r));
        commands.Exec += e =>
        {
            _execs[e.CommandId] = e;
            ToGame(nameof(IHostCommandHook.OnExec), h => h.OnExec(e.CommandId, e.Command));
        };
        commands.NextLevel += next => ToGame(nameof(IHostCommandHook.OnNextLevel), h => h.OnNextLevel(next));
        commands.Reauth += OnReauth;
        commands.PrepareHop += hop => _thread!.Post(async () =>
        {
            bool ready = await Hop(hop);
            var notice = new HopNotice(hop.SteamId, hop.TargetInstance, hop.Map, hop.IsSameProcess,
                                       hop.Target?.LevelFiles ?? [], ready);
            ToGame(nameof(IHostCommandHook.OnHopped), h => h.OnHopped(notice));
        });
    }

    private void ToGame(string what, Action<IHostCommandHook> call) =>
        _game.Post(() => _commands.Each(what, call));

    /// <summary>Answers an Exec with what the command printed.</summary>
    public void ReplyExec(string commandId, string output) => Send(async _ =>
    {
        if (_execs.Remove(commandId, out var exec))
            await exec.Reply(output);
    });

    public Task<HostResult<BootInfo>> Booting(string map) => Call(sdk => sdk.Session.Booting(map));

    public Task<HostResult<int>> MapReady(int port, string map) => Call(sdk => sdk.Session.MapReady(port, map));

    /// <summary>The player count reported on every heartbeat.</summary>
    public void Players(int count) => Send(sdk => { sdk.Session.Players = count; return Task.CompletedTask; });

    /// <summary>Stops the SDK: disposes it on its thread, then ends the thread. Game thread; waits up to 5 s.</summary>
    public void Dispose()
    {
        if (_thread is null)
            return;

        bool stopped = _thread.Stop(async () =>
        {
            // Still pumped while it disposes: the SDK completes its own tasks
            // inside Pump, its disposal's included.
            if (_sdk is { } sdk)
            {
                await sdk.DisposeAsync();
                _sdk = null;
            }
        }, TimeSpan.FromSeconds(5));

        if (!stopped)
            _log.Warning("[TF2Descent] the host SDK thread did not stop within 5 s\n");

        _thread = null;
    }
}
