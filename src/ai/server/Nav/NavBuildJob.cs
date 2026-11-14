using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using SourceSharp.MapTools.Io;
using SourceSharp.MapTools.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>How a build runs (plan_bots_teams_tf2 E.3 step 3): its threads, its wall-clock cap, its leaf cap, and whether the floor check follows.</summary>
public sealed record NavBuildSettings(int Threads, TimeSpan Timeout, int MaxLeaves, bool Check = false)
{
    /// <summary><c>ai_nav_build_threads 0</c>: half the processors, at least one.</summary>
    public static int DefaultThreads => Math.Max(1, Environment.ProcessorCount / 2);

    public const int DefaultTimeoutSeconds = 600;

    /// <summary>About 25 times dm_lockdown's 156,159 leaves.</summary>
    public const int DefaultMaxLeaves = 4_000_000;
}

/// <summary>
/// What a build is made from, read on the main thread through the engine's
/// file seam (plan_bots_teams_tf2 E.3 step 2): the map's bytes, parsed, and
/// the bytes of every model its props name, the <c>.mdl</c> for each prop's
/// hull and the <c>.phy</c> for each static prop's pieces, as <c>ssmap
/// nav3d</c> asks its mounted content. Paths go through map-tools'
/// <see cref="VPath"/> as ssmap's do, so a path cannot climb out of the game.
/// A model the content lacks is counted and named; the build still runs.
/// </summary>
public sealed class NavBuildInputs
{
    private NavBuildInputs(string map, byte[] bsp, BspData data, IReadOnlyList<string> models, IReadOnlyList<string> staticModels,
                           Dictionary<string, byte[]?> mdl, Dictionary<string, byte[]?> phy, double readMs)
    {
        Map = map;
        Bsp = bsp;
        Data = data;
        Models = models;
        StaticModels = staticModels;
        Mdl = mdl;
        Phy = phy;
        ReadMs = readMs;
    }

    public string Map { get; }

    public byte[] Bsp { get; }

    public BspData Data { get; }

    /// <summary>The model paths whose hulls the build asks for, in the order asked.</summary>
    public IReadOnlyList<string> Models { get; }

    /// <summary>The static props' model paths, whose <c>.phy</c> pieces the build asks for.</summary>
    public IReadOnlyList<string> StaticModels { get; }

    public IReadOnlyDictionary<string, byte[]?> Mdl { get; }

    public IReadOnlyDictionary<string, byte[]?> Phy { get; }

    /// <summary>The models the content has no <c>.mdl</c> for.</summary>
    public IReadOnlyList<string> Missing => [.. Models.Where(m => Mdl.GetValueOrDefault(m) is null)];

    /// <summary>How long the reads took on the main thread, in milliseconds.</summary>
    public double ReadMs { get; }

    /// <summary>Reads a map's inputs; an <see cref="InvalidBspException"/> when its bytes are not a bsp the builder reads.</summary>
    public static NavBuildInputs Read(string map, byte[] bsp, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentNullException.ThrowIfNull(read);
        long start = Stopwatch.GetTimestamp();
        BspData data = BspFile.LoadAsync(new MemoryStream(bsp, writable: false)).GetAwaiter().GetResult();
        IReadOnlyList<string> models = LevelNavFromBsp.Models(data);
        IReadOnlyList<string> statics = NavModelBounds.StaticPropModels(data);
        var mdl = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (string m in models)
            mdl[m] = VPath.TryCreate(m, out VPath path) && !path.IsEmpty ? read(path.Value) : null;
        var phy = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (string m in statics)
            phy[m] = VPath.TryCreate(Path.ChangeExtension(m, ".phy"), out VPath path) && !path.IsEmpty ? read(path.Value) : null;
        return new NavBuildInputs(map, bsp, data, models, statics, mdl, phy, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}

/// <summary>How a build ended: the file and the level's navigation from it, or why not.</summary>
public abstract record NavBuildOutcome
{
    /// <summary>The build wrote its file and the navigation was derived from it.</summary>
    public sealed record Built(LoadedNav Nav, NavSight Sight, double SightMs, NavBuildStamp Stamp, int Leaves, int Bytes,
                               IReadOnlyList<string> Warnings, string? CheckLine) : NavBuildOutcome;

    /// <summary>The build failed; <paramref name="Retry"/> when a second try may succeed (an I/O error, a timeout), never for the map itself.</summary>
    public sealed record Failed(string Reason, bool Retry) : NavBuildOutcome;

    /// <summary>The level went (or the server) before it finished; nothing was written.</summary>
    public sealed record Cancelled : NavBuildOutcome;
}

/// <summary>
/// The worker threads a build runs on: its own, at below-normal priority, so
/// the build never takes the game thread or the bots' pool and loses every
/// contest for a core. The builder's parallel loop runs on this scheduler
/// (it schedules on the current one), so its degree is these threads.
/// </summary>
internal sealed class NavBuildThreads : TaskScheduler, IDisposable
{
    private readonly BlockingCollection<Task> _tasks = new();
    private readonly Thread[] _threads;
    private readonly ThreadLocal<bool> _ours = new();

    public NavBuildThreads(int count)
    {
        _threads = new Thread[Math.Max(1, count)];
        for (int i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(Run) { IsBackground = true, Name = $"ai-navbuild-{i}", Priority = ThreadPriority.BelowNormal };
            _threads[i].Start();
        }
    }

    public override int MaximumConcurrencyLevel => _threads.Length;

    public int[] ThreadIds => [.. _threads.Select(t => t.ManagedThreadId)];

    /// <summary>How many of the threads are alive (0 once disposed).</summary>
    public int Alive => _threads.Count(t => t.IsAlive);

    private void Run()
    {
        _ours.Value = true;
        foreach (Task task in _tasks.GetConsumingEnumerable())
            TryExecuteTask(task);
    }

    protected override void QueueTask(Task task) => _tasks.Add(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => _ours.Value && TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

    /// <summary>Lets the threads finish what they hold and joins them.</summary>
    public void Dispose()
    {
        _tasks.CompleteAdding();
        foreach (Thread t in _threads)
            t.Join();
        _tasks.Dispose();
        _ours.Dispose();
    }
}

/// <summary>
/// One build of a map's navigation (plan_bots_teams_tf2 E.3 steps 3-6), off
/// the main thread: the hulls and pieces from the bytes read, map-tools'
/// <see cref="LevelNavFromBsp"/> at <see cref="NavBuildSettings.Threads"/>,
/// the leaf cap, the file written as <c>ssmap nav3d</c> writes it (its default
/// codec) into the store with its stamp, then bound and derived as a load
/// would, and the sight table built: the main thread is handed a level ready
/// to install. The level's shutdown cancels it (<see cref="Stop"/>), a
/// wall-clock cap times it out, and nothing is written unless it finished.
/// Every exception is caught and answered as a reason: a build never takes
/// the server down.
/// </summary>
public sealed class NavBuildJob
{
    private readonly NavBuildInputs _inputs;
    private readonly NavBuildSettings _settings;
    private readonly BuiltNavStore _store;
    private readonly NavGraphCache? _cache;
    private readonly INavProfile? _profile;
    private readonly CancellationTokenSource _cancel = new();
    private readonly CancellationTokenSource _timeout = new();
    private readonly NavBuildThreads _threads;
    private readonly Task<NavBuildOutcome> _task;
    private readonly long _start = Stopwatch.GetTimestamp();
    private int _done;
    private int _total;
    private int _builtOn;
    private volatile string _phase = "starting";

    private NavBuildJob(NavBuildInputs inputs, NavBuildSettings settings, BuiltNavStore store, NavGraphCache? cache, INavProfile? profile)
    {
        _inputs = inputs;
        _settings = settings;
        _store = store;
        _cache = cache;
        _profile = profile;
        _threads = new NavBuildThreads(settings.Threads);
        _timeout.CancelAfter(settings.Timeout);
        _task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.None, _threads);
    }

    /// <summary>Starts a build of <paramref name="inputs"/> on threads of its own; the file is derived by the game's <paramref name="profile"/> (the core's default movement without one).</summary>
    public static NavBuildJob Start(NavBuildInputs inputs, NavBuildSettings settings, BuiltNavStore store, NavGraphCache? cache, INavProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        return new NavBuildJob(inputs, settings, store, cache, profile);
    }

    public string Map => _inputs.Map;

    public NavBuildInputs Inputs => _inputs;

    public NavBuildSettings Settings => _settings;

    public bool Done => _task.IsCompleted;

    /// <summary>The outcome; only once <see cref="Done"/>.</summary>
    public NavBuildOutcome Outcome => _task.Result;

    /// <summary>What it is doing: starting, building, writing, deriving, checking.</summary>
    public string Phase => _phase;

    /// <summary>The cells built and the cells in all (0 of 0 until the grid is laid).</summary>
    public (int Done, int Total) Cells => (Volatile.Read(ref _done), Volatile.Read(ref _total));

    /// <summary>The whole percent of the cells built.</summary>
    public int Percent
    {
        get
        {
            (int done, int total) = Cells;
            return total == 0 ? 0 : (int)(100L * done / total);
        }
    }

    public double Seconds => Stopwatch.GetElapsedTime(_start).TotalSeconds;

    /// <summary>The managed thread id the build ran on (0 until it started): never the caller's.</summary>
    public int BuiltOnThread => Volatile.Read(ref _builtOn);

    /// <summary>The threads the build may use.</summary>
    internal int[] ThreadIds => _threads.ThreadIds;

    /// <summary>How many of its threads are alive: 0 once stopped or released.</summary>
    internal int ThreadsAlive => _threads.Alive;

    /// <summary>Refuses a navigation of more leaves than the cap allows, by name (E.3 step 3).</summary>
    public static void CheckLeaves(int leaves, int max)
    {
        if (leaves > max)
            throw new NavBuildException(string.Create(CultureInfo.InvariantCulture,
                $"the map has {leaves:N0} leaves, over ai_nav_build_max_leaves {max:N0}"));
    }

    /// <summary>
    /// Cancels the build and joins its threads: the builder looks at the token
    /// between cells, so this waits no longer than one cell; a build that has
    /// not stopped within <paramref name="wait"/> is waited for regardless
    /// (an unload must not leave a thread in the assembly), and answers false.
    /// </summary>
    public bool Stop(TimeSpan wait)
    {
        _cancel.Cancel();
        bool inTime = ((IAsyncResult)_task).AsyncWaitHandle.WaitOne(wait);
        if (!inTime)
            _task.Wait();
        _threads.Dispose();
        _cancel.Dispose();
        _timeout.Dispose();
        return inTime;
    }

    /// <summary>Joins the threads of a build that has finished.</summary>
    public void Release()
    {
        if (!_task.IsCompleted)
            throw new InvalidOperationException("a running build is stopped, not released");
        _threads.Dispose();
        _cancel.Dispose();
        _timeout.Dispose();
    }

    private NavBuildOutcome Run()
    {
        Volatile.Write(ref _builtOn, Environment.CurrentManagedThreadId);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, _timeout.Token);
        CancellationToken token = either.Token;
        try
        {
            return Build(token);
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
        {
            return new NavBuildOutcome.Cancelled();
        }
        catch (OperationCanceledException) when (_timeout.IsCancellationRequested)
        {
            return new NavBuildOutcome.Failed(string.Create(CultureInfo.InvariantCulture, $"it took longer than {_settings.Timeout.TotalSeconds:0} s (ai_nav_build_timeout)"), Retry: true);
        }
        catch (NavBuildException e)
        {
            return new NavBuildOutcome.Failed(e.Message, Retry: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new NavBuildOutcome.Failed($"{e.GetType().Name}: {e.Message}", Retry: true);
        }
        catch (OutOfMemoryException)
        {
            return new NavBuildOutcome.Failed("out of memory", Retry: false);
        }
        catch (Exception e)
        {
            return new NavBuildOutcome.Failed($"{e.GetType().Name}: {e.Message}", Retry: false);
        }
    }

    private NavBuildOutcome Build(CancellationToken token)
    {
        _phase = "building";
        long start = Stopwatch.GetTimestamp();
        // The store first: a directory that cannot be made fails now, not after the build.
        System.IO.Directory.CreateDirectory(_store.Directory);
        var hulls = new Dictionary<string, (Vec3, Vec3)?>(StringComparer.Ordinal);
        foreach ((string model, byte[]? mdl) in _inputs.Mdl)
            hulls[model] = mdl is null ? null : NavModelBounds.Hull(mdl);
        var pieces = new Dictionary<string, IReadOnlyList<NavPiece>?>(StringComparer.Ordinal);
        foreach ((string model, byte[]? phy) in _inputs.Phy)
            pieces[model] = phy is null ? null : NavModelBounds.Pieces(phy);
        Func<string, (Vec3 Mins, Vec3 Maxs)?> hull = m => hulls.TryGetValue(m, out (Vec3, Vec3)? h) ? h : null;
        Func<string, IReadOnlyList<NavPiece>?> piece = m => pieces.TryGetValue(m, out IReadOnlyList<NavPiece>? p) ? p : null;

        Nav3dBspBinding binding = Nav3dBspBinding.Of(_inputs.Bsp);
        // ssmap nav3d's grid: the defaults at its voxel (Nav3dCommand.Options).
        var options = new BspNavOptions
        {
            Settings = NavSettings.Default with { VoxelSize = NavSettings.DefaultVoxelSize },
            MaxDegree = Math.Max(1, _settings.Threads),
            ModelPieces = piece,
            Progress = (done, total) =>
            {
                Volatile.Write(ref _total, total);
                int seen;
                while ((seen = Volatile.Read(ref _done)) < done && Interlocked.CompareExchange(ref _done, done, seen) != seen)
                {
                }
            },
        };
        BspNavResult result = LevelNavFromBsp.Build(_inputs.Data, binding, hull, options, token);
        Nav3dLevel level = result.Level;
        CheckLeaves(level.Leaves.Length, _settings.MaxLeaves);

        token.ThrowIfCancellationRequested();
        _phase = "writing";
        byte[] bytes = Nav3dWriter.Write(level, LevelNavFromPack.DefaultCompression);
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        (string check, string? checkLine) = ("not run", null);
        if (_settings.Check)
        {
            _phase = "checking";
            (check, checkLine) = Check(Nav3dReader.Open(bytes), hull, piece, options.MaxDegree, token);
        }

        string bspSha = BuiltNavStore.Sha256(_inputs.Bsp);
        var stamp = new NavBuildStamp(bspSha, BuiltNavStore.Sha256(bytes), MapToolsVersion.Package, NavReaderVersion.Package,
                                      NavBuildStamp.HashModels(_inputs.Models), _inputs.Models.Count, _inputs.Missing.Count, seconds, options.MaxDegree, check);
        token.ThrowIfCancellationRequested();
        _store.Write(_inputs.Map, bspSha, bytes, stamp);

        _phase = "deriving";
        byte[] bsp = _inputs.Bsp;
        string bspPath = NavLoader.BspPath(_inputs.Map);
        NavLoadResult loaded = NavLoader.Load(_inputs.Map, p => p == bspPath ? bsp : null, _cache, _profile, _store);
        if (loaded.Nav is not { } nav)
            return new NavBuildOutcome.Failed($"the built file does not load: {loaded.Refusal}", Retry: false);
        long sightStart = Stopwatch.GetTimestamp();
        NavSight sight = NavSight.Build(nav.Nav.Graph, new Nav3dVoxelSpace(nav.Reader));
        double sightMs = Stopwatch.GetElapsedTime(sightStart).TotalMilliseconds;
        return new NavBuildOutcome.Built(nav, sight, sightMs, stamp, level.Leaves.Length, bytes.Length, result.Warnings, checkLine);
    }

    /// <summary>The share of standable places with no floor under them over which a built file is suspect (E.4): the validator's floor ceiling.</summary>
    public const double SuspectRate = 0.005;

    /// <summary>
    /// <c>ssmap nav3d -check</c>'s work (E.4, step 7): every standable place
    /// looked under for ground, on the build's threads. Answers the stamp's
    /// word (<c>pass</c>, or <c>suspect</c> over <see cref="SuspectRate"/>) and
    /// the console's line; it reports and never refuses the file.
    /// </summary>
    private (string Stamp, string Line) Check(Nav3dReader nav, Func<string, (Vec3 Mins, Vec3 Maxs)?> hull, Func<string, IReadOnlyList<NavPiece>?> piece,
                                              int degree, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp();
        NavFloorReport report = NavFloorCheck.Check(nav, _inputs.Data, hull, piece, new NavFloorCheckOptions { MaxDegree = degree }, token);
        double rate = report.Standable == 0 ? 0 : (double)report.Missing.Count / report.Standable;
        bool suspect = rate > SuspectRate;
        string word = suspect
            ? string.Create(CultureInfo.InvariantCulture, $"suspect: {report.Missing.Count} of {report.Standable} places have no floor")
            : "pass";
        string line = string.Create(CultureInfo.InvariantCulture,
            $"floor check: {report.Standable:N0} places a player stands, {report.Missing.Count:N0} with no ground within {report.Depth:0.##} units ({rate * 100:F2} %, ceiling {SuspectRate * 100:0.0} %) "
            + $"{(suspect ? "SUSPECT" : "PASS")} in {Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s");
        return (word, line);
    }

}

/// <summary>The version of map-tools' <c>SourceSharp.MapTools</c> the server builds with: what a build's stamp names.</summary>
public static class MapToolsVersion
{
    /// <summary>major.minor.patch of the builder assembly.</summary>
    public static string Package
    {
        get
        {
            Version v = typeof(LevelNavFromBsp).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
