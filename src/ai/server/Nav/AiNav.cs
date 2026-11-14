using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>What the bots ask of the level's navigation.</summary>
public interface IAiNavigation
{
    /// <summary>The loaded navigation, or null.</summary>
    LoadedNav? Current { get; }

    /// <summary>Why there is none, or null when there is.</summary>
    string? Refusal { get; }

    /// <summary>The level's navigation file when the refusal is that it is missing, else null.</summary>
    string? MissingFile => null;

    /// <summary>The obstacles as the entities stand now, or null with no navigation.</summary>
    NavObstacleState? Obstacles { get; }

    /// <summary>
    /// The obstacles as <paramref name="player"/> knows them: the map's layout, and what has
    /// moved since only once it was in the player's PVS (ruling 2026-11-02); null with no navigation.
    /// </summary>
    NavObstacleState? ObstaclesFor(EntityHandle player);

    /// <summary>The static sight table between the level's cells, or null with no navigation.</summary>
    NavSight? Sight { get; }

    /// <summary>The level's choke points, empty until a worker has them (§5.5); null with no navigation.</summary>
    ChokeSlot? Chokes { get; }

    /// <summary>What a bot's hands ask of a breakable on its route; null with no navigation.</summary>
    IBreakableAim? Breakables => null;

    /// <summary>Whether the level's navigation is being built (plan_bots_teams_tf2 E.3): bots wait for it rather than being refused.</summary>
    bool Building => false;

    /// <summary>Counts the navigations set: a bot refused for none is asked again when it moves (E.3 step 4).</summary>
    int Version => 0;

    /// <summary>The navigation build in flight (E.3, the dev API's <c>/ai/nav/build</c>), or null.</summary>
    NavBuildProgress? BuildProgress => null;
}

/// <summary>A navigation build as the dev API serves it: its map, what it is doing, how far, how long, on how many threads, and the maps queued after it.</summary>
public sealed record NavBuildProgress(string Map, string Phase, int Percent, double Seconds, int Threads, int Queued, bool ForTheLevel);

/// <summary>
/// The level's navigation (plan_hl2mp_ai §6): loaded at level start from
/// <c>maps/&lt;map&gt;.nav3d</c> through the engine's file seam, derived (or
/// read from the cache in <c>maps/ai-nav/</c>), with its sight
/// table and its obstacles matched to their entities. When no file binds (the
/// one beside the map, then the server's own build in <c>maps/ai-nav/</c>),
/// <c>ai_nav_build</c> is 1 and the game's binding asks for it
/// (<see cref="INavBuildPolicy"/>), one is built off the main thread
/// (plan_bots_teams_tf2 Part E, <see cref="NavBuildJob"/>) and installed at
/// the frame it lands, bots waiting for it; otherwise, or when the build
/// fails, the level is refused by name and no bot is added
/// (<see cref="AiBots.Add"/> answers the refusal). Obstacle state is
/// re-read every <see cref="ObstacleTicks"/> ticks.
/// </summary>
[SideService(Side.Server, As = [typeof(IAiNavigation)])]
public sealed class AiNav(IEngineFiles files, IModInfo mod, IEngineEntities entities, ILog log, IEnginePlayers players, ITraceQueries traces,
                          INavProfile profile, NavBuildKnobs knobs, INavBuildPolicy? builds = null, IEngineEntityRender? render = null)
    : IGameLifecycle, IFrameLifecycle, IAiNavigation
{
    /// <summary>How long a level's end waits for a build to stop before it waits regardless (E.3 step 6).</summary>
    public const int BuildStopSeconds = 2;

    // The build in flight (one at a time), the key it was posted under, and the
    // tens of percent already said; the builds that failed this session (never
    // posted again for the same bsp) and those retried once.
    private NavBuildJob? _job;
    private string _jobKey = "";
    private int _saidPercent;
    private string? _level;
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retried = new(StringComparer.Ordinal);
    private readonly Queue<string> _queue = new();

    public const int ObstacleTicks = 8;

    private NavObstacles? _obstacles;
    private NavObstacleField? _field;
    private NavTeamBlockers _teamBlockers = NavTeamBlockers.None;
    private int _ticks;

    /// <summary>Before the bots, so a bot added at level start finds the nav.</summary>
    public int LifecycleOrder => ModStages.First + 400;

    public int FrameOrder => ModStages.First + 400;

    public LoadedNav? Current { get; private set; }

    public NavSight? Sight { get; private set; }

    public ChokeSlot? Chokes { get; private set; }

    /// <summary>How long the last level's choke points took on their worker, in milliseconds, and whether they came from the cache.</summary>
    public (double Ms, bool FromCache) ChokeCost { get; private set; }

    private CancellationTokenSource? _chokeCancel;
    private Task? _chokeWork;

    /// <summary>Why there is no navigation for this level, or null.</summary>
    public string? Refusal { get; private set; } = "no level is loaded";

    /// <summary>The level's <c>.nav3d</c> when the refusal is that it is missing, else null.</summary>
    public string? MissingFile { get; private set; }

    public NavObstacleState? Obstacles => _obstacles?.State;

    /// <summary>The player's obstacles, carrying the jump reaches its class has now (the game's <see cref="INavProfile"/>).</summary>
    public NavObstacleState? ObstaclesFor(EntityHandle player)
    {
        if (_field?.StateFor(player) is not { } state)
            return null;
        state.Reach = profile.Reach(player);
        state.TeamCells = _teamBlockers.For(players.GetTeam(player));
        return state;
    }

    /// <summary>The solid entities' cells and who knows of them, or null with no navigation.</summary>
    public NavObstacleField? Field => _field;

    public IBreakableAim? Breakables { get; private set; }

    /// <summary>
    /// Beside the maps: a game started with <c>-instance</c> runs from a
    /// private directory per process whose <c>maps</c> is a link to the
    /// tree's, so only there does the cache outlive the game.
    /// </summary>
    public string CacheDirectory => Path.Combine(mod.GameDir, "maps", "ai-nav");

    /// <summary>Where the server keeps its own builds of maps' navigation: the cache directory.</summary>
    public BuiltNavStore Store => new(CacheDirectory);

    /// <summary>The build in flight, or null.</summary>
    public NavBuildJob? Job => _job;

    public bool Building => _job is { } j && j.Map == _level && Current is null;

    public int Version { get; private set; }

    /// <summary>What a level's refusal adds when the game's binding asks for no build (<see cref="INavBuildPolicy"/>): why none started.</summary>
    public const string NotBuilt = "not built: this game's binding builds no navigation in the server";

    /// <summary>The maps <c>ai_nav_prebuild</c> queued, built one at a time after the build in flight.</summary>
    public IReadOnlyCollection<string> Queued => _queue;

    /// <summary>
    /// <c>ai_nav_prebuild</c> (E.4): queues builds of <paramref name="maps"/>,
    /// one at a time on the build's threads, each skipped when it already has
    /// a usable navigation; the queue goes with the level. Answers a line.
    /// </summary>
    public string Prebuild(IReadOnlyList<string> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        int added = 0;
        int refused = 0;
        foreach (string map in maps)
        {
            if (map.Length == 0 || map.Contains("..", StringComparison.Ordinal) || _queue.Contains(map) || _job?.Map == map)
                continue;
            if (builds?.Builds(map) != true)
            {
                // The game's binding has not asked for its maps to be built (TF2's, until P6): never queued.
                refused++;
                continue;
            }

            _queue.Enqueue(map);
            added++;
        }

        return $"ai_nav_prebuild: {added} map(s) queued, {_queue.Count} waiting{(_job is { } j ? $", building {j.Map}" : "")}"
               + (refused > 0 ? $"; {refused} refused: this game's binding builds no navigation for them" : "");
    }

    /// <summary>The next queued map: skipped when it has a usable navigation or no bsp, else its inputs read and its build posted.</summary>
    private void StartQueued()
    {
        string map = _queue.Dequeue();
        if (NavLoader.Usable(map, files.ReadGameFileBytes, Store, out string? refusal, out byte[]? bsp) is { } where)
        {
            log.Msg($"[SourceSharp/Ai] ai_nav_prebuild {map}: navigation is there ({where})\n");
            return;
        }

        if (bsp is null)
        {
            log.Warning($"[SourceSharp/Ai] ai_nav_prebuild {map}: {refusal}\n");
            return;
        }

        StartBuild(map, bsp, new NavGraphCache(CacheDirectory, NavEdgeCosts.Default));
    }

    /// <summary>What <c>ai_nav_status</c> says of the build: its map, phase, percent, seconds and threads; or null with none.</summary>
    public string? BuildStatus => BuildProgress is { } b
        ? string.Create(CultureInfo.InvariantCulture, $"navigation for {b.Map}: {b.Phase} {b.Percent} % ({b.Seconds:F0} s) on {b.Threads} thread(s), {b.Queued} queued")
        : null;

    public NavBuildProgress? BuildProgress => _job is { } j
        ? new NavBuildProgress(j.Map, j.Phase, j.Percent, j.Seconds, j.Settings.Threads, _queue.Count, j.Map == _level)
        : null;

    public void OnLevelInitPostEntity(string mapName) => Load(mapName);

    /// <summary>A hot reload came up under a running level: the nav goes with the composition, so it loads again.</summary>
    public void OnLevelCarried(string mapName) => Load(mapName);

    /// <summary>The worker stops before the composition goes: a hot reload's unload waits for nothing of ours.</summary>
    public void OnShutdown()
    {
        StopChokes();
        StopBuild();
    }

    /// <summary>Cancels the build in flight and joins its threads; no partial file exists, as it writes through a rename.</summary>
    private void StopBuild()
    {
        if (_queue.Count > 0)
        {
            log.Msg($"[SourceSharp/Ai] ai_nav_prebuild: {_queue.Count} queued build(s) dropped with the level\n");
            _queue.Clear();
        }

        if (_job is not { } job)
            return;
        _job = null;
        if (!job.Stop(TimeSpan.FromSeconds(BuildStopSeconds)))
            log.Warning($"[SourceSharp/Ai] navigation for {job.Map}: the build took longer than {BuildStopSeconds} s to stop\n");
        log.Msg($"[SourceSharp/Ai] navigation for {job.Map}: the build was cancelled\n");
    }

    private void StopChokes()
    {
        _chokeCancel?.Cancel();
        try
        {
            _chokeWork?.Wait();
        }
        catch (AggregateException e) when (e.InnerExceptions.All(x => x is OperationCanceledException))
        {
            // What Cancel asks for.
        }

        _chokeCancel?.Dispose();
        _chokeCancel = null;
        _chokeWork = null;
    }

    /// <summary>
    /// The level's choke points, off the main thread: from the cache if it has them,
    /// else worked out and written to it. The slot fills when they are done; a bot is
    /// told at its next senses (<see cref="AiEventKind.ChokesKnown"/>), so the moment
    /// changes nothing a replay decides.
    /// </summary>
    private void StartChokes(LoadedNav nav, NavGraphCache cache)
    {
        var slot = new ChokeSlot();
        Chokes = slot;
        var cancel = new CancellationTokenSource();
        _chokeCancel = cancel;
        CancellationToken token = cancel.Token;
        _chokeWork = Task.Run(() =>
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            ChokeMap? map = cache.TryReadChokes(nav.Map, nav.BspSha256, nav.NavSha256, nav.Nav.Graph, nav.NumbersKey);
            bool cached = map is not null;
            if (map is null)
            {
                map = ChokeMap.Compute(nav.Nav.Graph, nav.Entities.PeoplesPlaces(), cancel: token);
                token.ThrowIfCancellationRequested();
                try
                {
                    cache.WriteChokes(nav.Map, nav.BspSha256, nav.NavSha256, nav.Nav.Graph, map, nav.NumbersKey);
                }
                catch (IOException e)
                {
                    log.Warning($"[SourceSharp/Ai] the choke points for {nav.Map} are not cached: {e.Message}\n");
                }
            }

            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ChokeCost = (ms, cached);
            slot.Map = map;
        }, token);
    }

    public void OnLevelShutdown()
    {
        StopChokes();
        StopBuild();
        _level = null;
        Chokes = null;
        Current = null;
        Sight = null;
        _obstacles = null;
        _field = null;
        _teamBlockers = NavTeamBlockers.None;
        Breakables = null;
        Refusal = "no level is loaded";
        MissingFile = null;
    }

    public void OnPreThink(float frameTime)
    {
        if (_obstacles is not null && ++_ticks % ObstacleTicks == 0)
            _obstacles.Update();
        _field?.Tick();
        if (_job is { } job)
            Poll(job);
        else if (_queue.Count > 0)
            StartQueued();
    }

    /// <summary>The load line's account of the cells closed to one team: "" when none are.</summary>
    private string TeamBlockersText()
    {
        IReadOnlyDictionary<int, int> blocked = _teamBlockers.BlockedCells;
        return blocked.Count == 0 ? ""
            : "; closed to one team: " + string.Join(", ", blocked.OrderBy(p => p.Key).Select(p => string.Create(CultureInfo.InvariantCulture, $"team {p.Key} {p.Value} cells")));
    }

    public void Load(string mapName)
    {
        OnLevelShutdown();
        _level = mapName;
        var cache = new NavGraphCache(CacheDirectory, NavEdgeCosts.Default);
        NavLoadResult result = NavLoader.Load(mapName, files.ReadGameFileBytes, cache, profile, Store);
        if (result.Nav is not { } nav)
        {
            Refusal = result.Refusal;
            MissingFile = result.Missing;
            bool asked = builds?.Builds(mapName) == true;
            if (result.Bsp is not null && knobs.Enabled && !asked)
                Refusal = $"{Refusal}; {NotBuilt}";
            log.Warning($"[SourceSharp/Ai] {Refusal}\n");
            if (result.Bsp is { } bsp && knobs.Enabled && asked)
                StartBuild(mapName, bsp, cache);
            return;
        }

        if (nav.Entities.Unread is { } unread)
            log.Warning($"[SourceSharp/Ai] {mapName}'s entities are not read ({unread}): the bots know none of its spawns, pickups, hurt volumes or team solids\n");
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        NavSight sight = NavSight.Build(nav.Nav.Graph, new Nav3dVoxelSpace(nav.Reader));
        Install(nav, sight, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, cache, built: false);
    }

    /// <summary>
    /// Posts a build of <paramref name="map"/> (E.3 steps 2-3): its inputs read
    /// here, on the main thread, through the engine's file seam, and the build on
    /// threads of its own. A bsp whose build already failed this session is
    /// refused with that reason, not built again.
    /// </summary>
    public void StartBuild(string map, byte[] bsp, NavGraphCache cache)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        string key = $"{map}:{BuiltNavStore.Sha256(bsp)[..16]}";
        if (_failed.TryGetValue(key, out string? why))
        {
            Refuse(map, $"no navigation for {map}: build failed: {why}");
            return;
        }

        NavBuildInputs inputs;
        try
        {
            inputs = NavBuildInputs.Read(map, bsp, files.ReadGameFileBytes);
        }
        catch (Exception e) when (e is SourceSharp.MapFormats.InvalidBspException or InvalidDataException)
        {
            Fail(map, key, e.Message);
            return;
        }

        Post(inputs, key, cache);
        IReadOnlyList<string> missing = inputs.Missing;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] navigation for {map}: building on {_job!.Settings.Threads} thread(s); {inputs.Models.Count} models, {missing.Count} missing, read in {inputs.ReadMs:F0} ms\n"));
        if (missing.Count > 0)
            log.Warning($"[SourceSharp/Ai] navigation for {map}: the content has no model for {missing.Count} prop(s), left out: {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? ", ..." : "")}\n");
    }

    private void Post(NavBuildInputs inputs, string key, NavGraphCache cache)
    {
        _job = NavBuildJob.Start(inputs, knobs.Settings, Store, cache, profile);
        _jobKey = key;
        _saidPercent = 0;
        if (inputs.Map == _level)
            Refusal = $"navigation for {inputs.Map}: building";
    }

    /// <summary>One frame of a build: its progress every ten percent, then its outcome.</summary>
    private void Poll(NavBuildJob job)
    {
        int tens = job.Percent / 10 * 10;
        if (tens > _saidPercent)
        {
            _saidPercent = tens;
            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] navigation for {job.Map}: building {tens} % ({job.Seconds:F0} s)\n"));
        }

        if (!job.Done)
            return;
        _job = null;
        NavBuildOutcome outcome = job.Outcome;
        job.Release();
        switch (outcome)
        {
            case NavBuildOutcome.Built b:
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] navigation for {job.Map}: built in {b.Stamp.Seconds:F1} s on {b.Stamp.Threads} thread(s): {b.Bytes:N0} bytes, {b.Leaves:N0} leaves, "
                    + $"sha256 {b.Stamp.NavSha256[..12]}, {Store.Name(job.Map, b.Stamp.BspSha256)}\n"));
                if (b.CheckLine is { } check)
                    log.Msg($"[SourceSharp/Ai] navigation for {job.Map}: {check}\n");
                if (job.Map == _level && Current is null)
                    Install(b.Nav, b.Sight, b.SightMs, new NavGraphCache(CacheDirectory, NavEdgeCosts.Default), built: true);
                break;
            case NavBuildOutcome.Failed f when f.Retry && _retried.Add(_jobKey):
                log.Warning($"[SourceSharp/Ai] navigation for {job.Map}: build failed: {f.Reason}; trying once more\n");
                Post(job.Inputs, _jobKey, new NavGraphCache(CacheDirectory, NavEdgeCosts.Default));
                break;
            case NavBuildOutcome.Failed f:
                Fail(job.Map, _jobKey, f.Reason);
                break;
        }
    }

    private void Fail(string map, string key, string reason)
    {
        _failed[key] = reason;
        Refuse(map, $"no navigation for {map}: build failed: {reason}");
    }

    private void Refuse(string map, string why)
    {
        if (map == _level)
            Refusal = why;
        log.Warning($"[SourceSharp/Ai] {why}\n");
    }

    private void Install(LoadedNav nav, NavSight sight, double sightMs, NavGraphCache cache, bool built)
    {
        string mapName = nav.Map;
        Sight = sight;
        _obstacles = new NavObstacles(nav.Nav, new EngineObstacleEyes(entities), propsAreTheField: true);
        _obstacles.Resolve();
        _field = new NavObstacleField(nav.Nav.Graph, new EngineObstacleWorld(entities, render is null ? null : render.GetModelName), new EnginePvsSight(players), nav.Nav.Obstacles, _obstacles.State);
        _field.OnLevelStart();
        _teamBlockers = NavTeamBlockers.From(nav.Nav.Graph, profile.TeamSolids(nav.Entities), profile.Numbers(nav.Reader.JumpHeight));
        Breakables = new EngineBreakableAim(_field, traces);
        StartChokes(nav, cache);
        Current = nav;
        Refusal = null;
        Version++;
        NavGraph g = nav.Nav.Graph;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] navigation for {mapName}: {g.CellCount} cells, {g.EdgeCount} edges, {g.ObstacleCount} obstacles "
            + $"({_obstacles.Matched} matched, {_obstacles.Blocking} blocking){(built ? " (built)" : nav.Built ? " (cached)" : "")}; {(nav.FromCache ? "read from the cache" : "derived")} in {nav.DeriveMs:F0} ms; "
            + $"sight {Sight.NodeCount} nodes, {Sight.Pairs} pairs in {sightMs:F0} ms; "
            + $"{_field.Bodies} solid entities take {_field.TruthCells.Blocked} cells and cost {_field.TruthCells.Costly}, {_field.BreakableBodies} a gun can break{TeamBlockersText()}\n"));
    }
}

/// <summary><c>ai_nav_status</c>: the level's navigation, or why there is none, and the build in flight.</summary>
[ConCommand("ai_nav_status", Help = "ai_nav_status: the level's AI navigation, or why there is none, and any navigation build in flight")]
public sealed class AiNavStatusCommand(AiNav nav, ILog log)
{
    public void Execute()
    {
        if (nav.BuildStatus is { } building)
            log.Msg($"[SourceSharp/Ai] {building}\n");
        if (nav.Current is not { } n)
        {
            log.Msg($"[SourceSharp/Ai] {nav.Refusal}\n");
            return;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] navigation for {n.Map}: {n.Nav.Graph.CellCount} cells, {n.Nav.Graph.EdgeCount} edges, bsp {n.BspSha256[..12]}, nav {n.NavSha256[..12]}, "
            + $"{(n.Built ? "the server's build" : "shipped")}, MapFormats {NavReaderVersion.Package}, MapTools {MapToolsVersion.Package}\n"));
    }
}

/// <summary>
/// <c>ai_nav_prebuild &lt;map&gt;|all</c> (plan_bots_teams_tf2 E.4): builds
/// navigation ahead for a map, or for every map of the map cycle, one at a
/// time on the build's below-normal threads, skipping a map that has one; the
/// queue goes with the level. (E.4 names it <c>ai_nav_build &lt;map&gt;</c>,
/// but the convar <c>ai_nav_build</c> owns that name.)
/// </summary>
[ConCommand("ai_nav_prebuild", Help = "ai_nav_prebuild <map>|all: build AI navigation ahead for a map, or every map of the map cycle, one at a time")]
public sealed class AiNavPrebuildCommand(AiNav nav, IEngineFiles files, MapCycleFileConVar mapCycle, ILog log)
{
    public void Execute(string map)
    {
        IReadOnlyList<string> maps = map == "all" ? Intermission.ReadCycle(files, mapCycle.Value) : [map];
        log.Msg($"[SourceSharp/Ai] {nav.Prebuild(maps)}\n");
    }
}

/// <summary>
/// <c>ai_nav_validate [n] [seed]</c> (plan_hl2mp_ai §6.3): the validator
/// against the engine's hull traces, on the main thread; prints the count,
/// each arm's rate against its ceiling (standing and floor 0.5 %,
/// solid 3 %), and the first mismatches.
/// </summary>
[ConCommand("ai_nav_validate", Help = "ai_nav_validate [n=200] [seed]: check the nav against the world's hull traces", Flags = ConVarFlags.Cheat)]
public sealed class AiNavValidateCommand(AiNav nav, ITraceQueries traces, IEngineEntities entities, ILog log)
{
    public void Execute(int count = 200, string seed = "1", float width = NavValidator.Width)
    {
        if (nav.Current is not { } n)
        {
            log.Warning($"[SourceSharp/Ai] ai_nav_validate: {nav.Refusal}\n");
            return;
        }

        if (count <= 0)
            count = 200;
        ulong seedValue = ulong.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s : 1UL;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        NavValidation v = NavValidator.Run(n.Nav, new Nav3dNavSource(n.Reader), new Nav3dVoxelSpace(n.Reader), new EngineWorldProbe(traces, entities), count, seedValue, width);
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] ai_nav_validate {n.Map} n={count} seed={seedValue} width={width:F0}: {Arm(v.Standing)}; {Arm(v.Floor)}; {Arm(v.Solid)}; "
            + $"{v.EntityInTheWay} standing cells had an entity on them; {(v.Passed ? "PASS" : "FAIL")} in {ms:F0} ms\n"));
        foreach (string line in v.Examples)
            log.Msg($"[SourceSharp/Ai]   {line}\n");
    }

    /// <summary>One arm as the live harness reads it: <c>floor 12/1990 (0.60 %, ceiling 0.5 %) FAIL</c>.</summary>
    public static string Arm(NavValidationArm a)
        => string.Create(CultureInfo.InvariantCulture, $"{a.Name} {a.Bad}/{a.Asked} ({a.Rate * 100:F2} %, ceiling {a.Ceiling * 100:0.0} %) {(a.Passed ? "PASS" : "FAIL")}");
}

/// <summary>
/// <c>ai_nav_probe x y z</c>: what the nav and the world each say about a
/// point (the validator's mismatches, by hand): the cell and its classes,
/// and the tallest and widest box the world's hull traces hold there with
/// its feet on the cell's floor.
/// </summary>
[ConCommand("ai_nav_probe", Help = "ai_nav_probe x y z: the nav's cell and the world's room at a point", Flags = ConVarFlags.Cheat)]
public sealed class AiNavProbeCommand(AiNav nav, ITraceQueries traces, IEngineEntities entities, ILog log)
{
    public void Execute(Vector at)
    {
        (float x, float y, float z) = (at.X, at.Y, at.Z);
        if (nav.Current is not { } n)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_nav_probe x y z (with navigation loaded)\n");
            return;
        }

        CellId c = n.Nav.Graph.FindCell(new AiVector(x, y, z));
        float floor = c.IsValid ? n.Nav.Graph.Cell(c).FloorZ : z;
        var world = new EngineWorldProbe(traces, entities);
        AiVector feet = c.IsValid ? n.Nav.Graph.Cell(c).Centre with { Z = floor + 1f } : new AiVector(x, y, z);
        float tallest = 0f, widest = 0f;
        string tallHit = "", wideHit = "";
        for (float h = 1f; h <= 128f; h += 1f)
        {
            if (!world.BoxClear(feet, 32f, h))
            {
                tallHit = world.LastHit;
                break;
            }

            tallest = h;
        }

        for (float w = 2f; w <= 128f; w += 2f)
        {
            if (!world.BoxClear(feet with { Z = feet.Z + NavValidator.StepBand }, w, 72f - NavValidator.StepBand))
            {
                wideHit = world.LastHit;
                break;
            }

            widest = w;
        }

        var top = new Vector(feet.X, feet.Y, floor + 32f);
        TraceAnswer down = traces.Line(top, top with { Z = floor - 64f }, GameMovement.MaskPlayerSolid, EntityHandle.None);
        TraceAnswer slab = traces.Hull(new Vector(feet.X, feet.Y, floor - 0.5f), new Vector(feet.X, feet.Y, floor + 0.5f),
                                       new Vector(-7.9f, -7.9f, -0.4f), new Vector(7.9f, 7.9f, 0.4f), GameMovement.MaskPlayerSolid, EntityHandle.None);
        string ground = down.Hit ? $"the ground below is at {down.End.Z:F1}" : "no ground within 64 below";
        string cell = c.IsValid ? $"cell {c.Index} {n.Nav.Graph.Cell(c).Classes} floor {floor:F1} leaf {n.Nav.LeafOfCell[c.Index]}" : "no cell";
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] ai_nav_probe ({x:F0} {y:F0} {z:F0}): {cell}; the world holds a 32-wide box {tallest:F0} tall (then {tallHit}), "
            + $"a {72f - NavValidator.StepBand:F0}-tall one above the step {widest:F0} wide (then {wideHit}); {ground}; the slab under the floor starts solid: {slab.StartSolid}\n"));
    }
}
