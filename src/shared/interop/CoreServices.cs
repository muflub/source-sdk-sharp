using System.Collections.Concurrent;
using System.Diagnostics;

namespace SourceSharp.Abi;

//=============================================================================
// Core services a mod reaches by constructor injection (plan_host_sdk §2.8).
// Each is generic: named for what it does, registered in both sides'
// containers (or the server's, where noted), never for one mod.
//=============================================================================

/// <summary>
/// <see cref="ILog"/> over the side's own engine console: the one each
/// composition registers (EngineServices.AddTo) and hands to what it builds.
/// </summary>
public sealed class ConsoleLog(IEngineConsole console) : ILog
{
    public void Msg(string message) => console.Msg(message);

    public void Warning(string message) => console.Warning(message);
}

/// <summary>
/// Work handed to the game thread from any thread. The side's per-frame path
/// drains it on the game thread every frame, level or no level, so whatever is
/// posted may touch the engine.
/// </summary>
public interface IGameThreadDispatcher
{
    /// <summary>Queues <paramref name="work"/>; safe from any thread.</summary>
    void Post(Action work);

    /// <summary>Items queued and not yet run.</summary>
    int Backlog { get; }
}

/// <summary>When the game thread last ran a frame; readable from any thread.</summary>
public interface IGameThreadLiveness
{
    /// <summary>Monotonic milliseconds (<see cref="Environment.TickCount64"/>) of the last frame; 0 before the first.</summary>
    long LastFrameMs { get; }
}

/// <summary>
/// The game-thread queue and the frame stamp. The module calls
/// <see cref="RunFrame"/> once per frame, on the game thread.
/// </summary>
public sealed class GameThread : IGameThreadDispatcher, IGameThreadLiveness
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly Func<long> _clock;
    private long _lastFrameMs;

    public GameThread() : this(() => Environment.TickCount64) { }

    /// <summary>For tests: a clock of their own.</summary>
    public GameThread(Func<long> clock) => _clock = clock;

    /// <summary>
    /// How long one frame may spend on posted work. What is left waits for the
    /// next frame; a single item is never interrupted.
    /// </summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromMilliseconds(4);

    public int Backlog => _queue.Count;

    public long LastFrameMs => Volatile.Read(ref _lastFrameMs);

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _queue.Enqueue(work);
    }

    /// <summary>
    /// Stamps the frame, then runs posted work within <see cref="Budget"/>.
    /// A throwing item is reported through <paramref name="log"/> and the rest
    /// still run.
    /// </summary>
    /// <returns>How many items ran.</returns>
    public int RunFrame(ILog log)
    {
        Volatile.Write(ref _lastFrameMs, _clock());

        int ran = 0;
        long start = Stopwatch.GetTimestamp();

        while (_queue.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp] posted game-thread work threw: {e}\n");
            }

            ran++;

            if (Stopwatch.GetElapsedTime(start) >= Budget)
                break;
        }

        return ran;
    }
}

/// <summary>
/// Delays that resume on the game thread. Await-only: a mod never registers a
/// callback (plan_host_sdk §2.11). Periodic work is an
/// <see cref="IFrameLifecycle"/> hook that checks its own due time.
/// </summary>
public interface IFrameScheduler
{
    /// <summary>
    /// Completes on the game thread, in the first frame at or after
    /// <paramref name="delay"/> from now; cancelled with the token.
    /// </summary>
    Task Delay(TimeSpan delay, CancellationToken cancellation = default);
}

/// <summary>
/// <see cref="IFrameScheduler"/>, ticked by the module once per frame on the
/// game thread. Completions run inline there: there is no
/// SynchronizationContext on the game thread, so an <c>await</c> continues in
/// <see cref="Tick"/>, on the game thread, and may touch the engine.
/// </summary>
public sealed class FrameScheduler(Func<long> clock) : IFrameScheduler
{
    private readonly List<(long DueMs, TaskCompletionSource Done)> _waiting = new();
    private readonly object _gate = new();

    public FrameScheduler() : this(() => Environment.TickCount64) { }

    public int Waiting { get { lock (_gate) return _waiting.Count; } }

    public Task Delay(TimeSpan delay, CancellationToken cancellation = default)
    {
        if (cancellation.IsCancellationRequested)
            return Task.FromCanceled(cancellation);

        // NOT RunContinuationsAsynchronously: the continuation is meant to run
        // right here, inside Tick, on the game thread.
        var done = new TaskCompletionSource();
        long due = clock() + (long)Math.Max(0, delay.TotalMilliseconds);

        lock (_gate)
            _waiting.Add((due, done));

        if (cancellation.CanBeCanceled)
            cancellation.Register(() => done.TrySetCanceled(cancellation));

        return done.Task;
    }

    /// <summary>Completes every delay that is due. Game thread only.</summary>
    public int Tick()
    {
        long now = clock();
        List<TaskCompletionSource>? due = null;

        lock (_gate)
        {
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                if (_waiting[i].DueMs > now && !_waiting[i].Done.Task.IsCompleted)
                    continue;

                (due ??= new()).Add(_waiting[i].Done);
                _waiting.RemoveAt(i);
            }
        }

        if (due is null)
            return 0;

        // Oldest first, outside the lock: a continuation may schedule again.
        int completed = 0;
        for (int i = due.Count - 1; i >= 0; i--)
        {
            if (due[i].TrySetResult())
                completed++;
        }

        return completed;
    }
}

/// <summary>Which mod is running, as Bootstrap resolved it from mods.yaml.</summary>
public interface IModInfo
{
    string Id { get; }

    /// <summary>The mod's game directory (<c>-game</c>), absolute.</summary>
    string GameDir { get; }

    /// <summary>Where the managed assemblies are loaded from, absolute.</summary>
    string ManagedDir { get; }

    /// <summary>The assemblies mods.yaml lists for <paramref name="side"/>, in load order.</summary>
    IReadOnlyList<string> AssembliesFor(Side side);

    /// <summary>The mod's hosted server-browser entry from mods.yaml, or null when it has none.</summary>
    ModBrowser? Browser => null;

    /// <summary>
    /// The mod's own UI assemblies (mods.yaml <c>assemblies.client_ui</c>), in
    /// load order, to join the core's three in the UI's collectible context
    /// (stage 0.9); empty when the mod lists none.
    /// </summary>
    IReadOnlyList<string> UiAssemblies => [];

    /// <summary>
    /// The mod's extra language files (mods.yaml <c>localisation</c>), as
    /// engine <c>GAME</c> paths — so a file in a mounted VPK is as reachable as
    /// a loose one. First file wins a shared token; empty when it lists none.
    /// </summary>
    IReadOnlyList<string> Localisation => [];
}

/// <summary>
/// A hosted mod's server-browser entry, as mods.yaml's <c>browser</c> block
/// gives it (host plan_steam_auth S-18): what a hosting service lists the
/// server with. <see cref="Name"/>, <see cref="Description"/>,
/// <see cref="Tags"/>, <see cref="Map"/> and the <see cref="Rules"/> values are
/// templates the host renders; mods.yaml's loader has checked their keys.
/// </summary>
public sealed record ModBrowser(
    string Name,
    string Description,
    string Tags,
    string Region,
    int MaxPlayers,
    string Map,
    IReadOnlyDictionary<string, string> Rules)
{
    /// <summary>The Bootstrap's one-line JSON (LoadFacts.BrowserJson), or a FormatException naming what is wrong.</summary>
    public static ModBrowser Parse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var o = doc.RootElement;
            string Text(string key) => o.GetProperty(key).GetString() ?? throw new FormatException($"browser {key} is null");

            var rules = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rule in o.GetProperty("rules").EnumerateObject())
                rules[rule.Name] = rule.Value.GetString() ?? throw new FormatException($"browser rule {rule.Name} is null");

            return new ModBrowser(Text("name"), Text("description"), Text("tags"), Text("region"),
                                  o.GetProperty("max_players").GetInt32(), Text("map"), rules);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"the browser fact is not the Bootstrap's JSON: {e.Message}", e);
        }
    }
}

/// <summary>
/// <see cref="IModInfo"/> from what Bootstrap published (<see cref="Keys"/>).
/// AppContext data is process-wide, so it crosses from Bootstrap's load
/// context into the game's.
/// </summary>
public sealed class ModInfo : IModInfo
{
    public static class Keys
    {
        public const string Id = "SourceSharp.Mod.Id";
        public const string GameDir = "SourceSharp.Mod.GameDir";
        public const string ManagedDir = "SourceSharp.Mod.ManagedDir";
        public const string ServerAssemblies = "SourceSharp.Mod.Assemblies.Server";
        public const string ClientAssemblies = "SourceSharp.Mod.Assemblies.Client";
        public const string Browser = "SourceSharp.Mod.Browser";
        public const string ClientUiAssemblies = "SourceSharp.Mod.Assemblies.ClientUi";
        public const string Localisation = "SourceSharp.Mod.Localisation";
    }

    private readonly Func<string, object?> _data;

    public ModInfo() : this(AppContext.GetData) { }

    /// <summary>For tests: data of their own.</summary>
    public ModInfo(Func<string, object?> data) => _data = data;

    private string Read(string key) =>
        _data(key) as string
        ?? throw new InvalidOperationException($"Bootstrap did not publish {key}: the mod is unknown");

    public string Id => Read(Keys.Id);
    public string GameDir => Read(Keys.GameDir);
    public string ManagedDir => Read(Keys.ManagedDir);

    public IReadOnlyList<string> AssembliesFor(Side side) =>
        Read(side == Side.Server ? Keys.ServerAssemblies : Keys.ClientAssemblies)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Null when the mod has no browser block (Bootstrap published none, or an empty one).</summary>
    public ModBrowser? Browser => _data(Keys.Browser) is string { Length: > 0 } json ? ModBrowser.Parse(json) : null;

    // The two stage-0.9 lists are always published, empty for none (Publish's
    // rule); an absent key is a Bootstrap that predates the row and reads as none.
    private IReadOnlyList<string> List(string key) =>
        _data(key) as string is { Length: > 0 } joined ? joined.Split(';', StringSplitOptions.RemoveEmptyEntries) : [];

    public IReadOnlyList<string> UiAssemblies => List(Keys.ClientUiAssemblies);

    public IReadOnlyList<string> Localisation => List(Keys.Localisation);

    /// <summary>What Bootstrap calls once it has resolved the mod.</summary>
    public static void Publish(string id, string gameDir, string managedDir,
                               IEnumerable<string> server, IEnumerable<string> client, string? browserJson = null,
                               IEnumerable<string>? clientUi = null, IEnumerable<string>? localisation = null)
    {
        // Always set, empty for none: AppContext data is process-wide, and a
        // reload of another mod must not inherit this one's entry.
        AppContext.SetData(Keys.Browser, browserJson ?? "");
        AppContext.SetData(Keys.ClientUiAssemblies, string.Join(';', clientUi ?? []));
        AppContext.SetData(Keys.Localisation, string.Join(';', localisation ?? []));
        AppContext.SetData(Keys.Id, id);
        AppContext.SetData(Keys.GameDir, gameDir);
        AppContext.SetData(Keys.ManagedDir, managedDir);
        AppContext.SetData(Keys.ServerAssemblies, string.Join(';', server));
        AppContext.SetData(Keys.ClientAssemblies, string.Join(';', client));
    }
}

/// <summary>The process environment, injectable.</summary>
public interface IProcessEnvironment
{
    string? GetVariable(string name);
}

public sealed class ProcessEnvironment : IProcessEnvironment
{
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);
}

/// <summary>What level is up, as state. Changes arrive as hooks, never events.</summary>
public interface ILevelInfo
{
    /// <summary>The current map, or null between levels.</summary>
    string? Map { get; }

    /// <summary>True from the level's first ready frame until it shuts down.</summary>
    bool IsReady { get; }
}

/// <summary>Written by the module; read by anyone.</summary>
public sealed class LevelInfo : ILevelInfo
{
    public string? Map { get; set; }
    public bool IsReady { get; set; }
}

/// <summary>
/// Calls every registered implementation of a hook interface a mod defines,
/// as the core's drivers do for <see cref="IGameLifecycle"/>: in a unique
/// order, each call isolated and reported by name (plan_host_sdk §2.11).
/// Resolved lazily on the first dispatch, after the container is complete, so
/// a hook may depend on the service that dispatches it.
/// </summary>
public interface IHooks<T> where T : class
{
    /// <summary>The implementations, in their order.</summary>
    IReadOnlyList<T> All { get; }

    /// <summary>Calls <paramref name="call"/> on each; a throw is logged and the rest still run.</summary>
    void Each(string what, Action<T> call);
}

/// <summary>
/// A hook interface's order: implement this alongside the hook so two
/// implementations never tie. Without it a hook is order 0, and at most one
/// implementation may leave it there.
/// </summary>
public interface IOrderedHook
{
    int HookOrder { get; }
}

public sealed class Hooks<T>(IServiceLocator services, ILog log) : IHooks<T> where T : class
{
    private IReadOnlyList<T>? _all;

    public IReadOnlyList<T> All => _all ??= Collect();

    private IReadOnlyList<T> Collect()
    {
        var found = services is ServiceContainer container
            ? container.ResolveAll<T>()
            : throw new InvalidOperationException("IHooks needs the side's ServiceContainer");

        StageUniqueness.Require(found, h => (h as IOrderedHook)?.HookOrder ?? 0, nameof(IOrderedHook.HookOrder), typeof(T).Name);
        return found.OrderBy(h => (h as IOrderedHook)?.HookOrder ?? 0).ToArray();
    }

    public void Each(string what, Action<T> call)
    {
        foreach (var hook in All)
        {
            try
            {
                call(hook);
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp] {typeof(T).Name}.{what} on {hook.GetType().FullName} threw: {e}\n");
            }
        }
    }
}

/// <summary>Reconnects this client to its server (the console's <c>retry</c>). Client only.</summary>
public interface IClientReconnect
{
    void Reconnect();
}

/// <summary>A Steam auth session ticket and the handle that cancels it.</summary>
public sealed record SteamAuthTicket(uint Handle, byte[] Bytes);

/// <summary>
/// The local Steam user's auth session tickets (ISteamUser::GetAuthSessionTicket
/// and CancelAuthTicket), for a server that validates them itself. Client only.
/// </summary>
public interface ISteamAuthTickets
{
    /// <summary>
    /// A fresh ticket minted for the Steam game server <paramref name="gameServerSteamId"/>,
    /// or null with the reason (no Steam, not logged on, no ticket).
    /// </summary>
    SteamAuthTicket? Create(ulong gameServerSteamId, out string why);

    /// <summary>Cancels a ticket <see cref="Create"/> returned; the server that held it stops trusting it.</summary>
    void Cancel(uint handle);
}

/// <summary>
/// Which build this side is running (<see cref="BuildInfo"/>), for code that
/// reaches the core only by injection, such as a mod reporting its version
/// to a hosting service.
/// </summary>
public interface IGameBuild
{
    /// <summary>The managed half: its stamp hash and source (branch@commit), or "unknown".</summary>
    string Managed { get; }

    /// <summary>The native half, the same way.</summary>
    string Native { get; }
}

/// <inheritdoc cref="IGameBuild"/>
public sealed class GameBuild(BuildInfo build) : IGameBuild
{
    public string Managed => Version(build.Managed);

    public string Native => Version(build.Native);

    /// <summary>"hash branch@commit" for a known half; "unknown" otherwise.</summary>
    public static string Version(BuildHalf half)
        => half.Stamp.IsKnown ? $"{half.Stamp.Hash} {half.Source.Describe()}".TrimEnd() : "unknown";
}

