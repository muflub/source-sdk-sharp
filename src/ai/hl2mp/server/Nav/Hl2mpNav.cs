using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>What the bots ask of the level's navigation.</summary>
public interface IHl2mpNavigation
{
    /// <summary>The loaded navigation, or null.</summary>
    LoadedNav? Current { get; }

    /// <summary>Why there is none, or null when there is.</summary>
    string? Refusal { get; }

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
}

/// <summary>
/// The level's navigation (plan_hl2mp_ai §6): loaded at level start from
/// <c>maps/&lt;map&gt;.nav3d</c> through the engine's file seam, derived (or
/// read from the cache in <c>maps/ai-nav/</c>), with its sight
/// table and its obstacles matched to their entities; refused, by name, when
/// the file is missing or built from another map, and then no bot is added
/// (<see cref="Hl2mpBots.Add"/> answers the refusal). Obstacle state is
/// re-read every <see cref="ObstacleTicks"/> ticks.
/// </summary>
[SideService(Side.Server, As = [typeof(IHl2mpNavigation)])]
public sealed class Hl2mpNav(IEngineFiles files, IModInfo mod, IEngineEntities entities, ILog log, IEnginePlayers players, ITraceQueries traces, IEngineEntityRender? render = null) : IGameLifecycle, IFrameLifecycle, IHl2mpNavigation
{
    public const int ObstacleTicks = 8;

    private NavObstacles? _obstacles;
    private NavObstacleField? _field;
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

    public NavObstacleState? Obstacles => _obstacles?.State;

    public NavObstacleState? ObstaclesFor(EntityHandle player) => _field?.StateFor(player);

    /// <summary>The solid entities' cells and who knows of them, or null with no navigation.</summary>
    public NavObstacleField? Field => _field;

    public IBreakableAim? Breakables { get; private set; }

    /// <summary>
    /// Beside the maps: a game started with <c>-instance</c> runs from a
    /// private directory per process whose <c>maps</c> is a link to the
    /// tree's, so only there does the cache outlive the game.
    /// </summary>
    public string CacheDirectory => Path.Combine(mod.GameDir, "maps", "ai-nav");

    public void OnLevelInitPostEntity(string mapName) => Load(mapName);

    /// <summary>A hot reload came up under a running level: the nav goes with the composition, so it loads again.</summary>
    public void OnLevelCarried(string mapName) => Load(mapName);

    /// <summary>The worker stops before the composition goes: a hot reload's unload waits for nothing of ours.</summary>
    public void OnShutdown() => StopChokes();

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
            ChokeMap? map = cache.TryReadChokes(nav.Map, nav.BspSha256, nav.NavSha256, nav.Nav.Graph);
            bool cached = map is not null;
            if (map is null)
            {
                map = ChokeMap.Compute(nav.Nav.Graph, nav.Entities.PeoplesPlaces(), cancel: token);
                token.ThrowIfCancellationRequested();
                try
                {
                    cache.WriteChokes(nav.Map, nav.BspSha256, nav.NavSha256, nav.Nav.Graph, map);
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
        Chokes = null;
        Current = null;
        Sight = null;
        _obstacles = null;
        _field = null;
        Breakables = null;
        Refusal = "no level is loaded";
    }

    public void OnPreThink(float frameTime)
    {
        if (_obstacles is not null && ++_ticks % ObstacleTicks == 0)
            _obstacles.Update();
        _field?.Tick();
    }

    public void Load(string mapName)
    {
        OnLevelShutdown();
        var cache = new NavGraphCache(CacheDirectory, NavEdgeCosts.Default);
        NavLoadResult result = NavLoader.Load(mapName, files.ReadGameFileBytes, cache);
        if (result.Nav is not { } nav)
        {
            Refusal = result.Refusal;
            log.Warning($"[SourceSharp/Ai] {Refusal}\n");
            return;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Sight = NavSight.Build(nav.Nav.Graph, new Nav3dVoxelSpace(nav.Reader));
        double sightMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _obstacles = new NavObstacles(nav.Nav, new EngineObstacleEyes(entities), propsAreTheField: true);
        _obstacles.Resolve();
        _field = new NavObstacleField(nav.Nav.Graph, new EngineObstacleWorld(entities, render is null ? null : render.GetModelName), new EnginePvsSight(players), nav.Nav.Obstacles, _obstacles.State);
        _field.OnLevelStart();
        Breakables = new EngineBreakableAim(_field, traces);
        StartChokes(nav, cache);
        Current = nav;
        Refusal = null;
        NavGraph g = nav.Nav.Graph;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] navigation for {mapName}: {g.CellCount} cells, {g.EdgeCount} edges, {g.ObstacleCount} obstacles "
            + $"({_obstacles.Matched} matched, {_obstacles.Blocking} blocking); {(nav.FromCache ? "read from the cache" : "derived")} in {nav.DeriveMs:F0} ms; "
            + $"sight {Sight.NodeCount} nodes, {Sight.Pairs} pairs in {sightMs:F0} ms; "
            + $"{_field.Bodies} solid entities take {_field.TruthCells.Blocked} cells and cost {_field.TruthCells.Costly}, {_field.BreakableBodies} a gun can break\n"));
    }
}

/// <summary><c>ai_nav_status</c>: the level's navigation, or why there is none.</summary>
[ConCommand("ai_nav_status", Help = "ai_nav_status: the level's AI navigation, or why there is none")]
public sealed class AiNavStatusCommand(Hl2mpNav nav, ILog log)
{
    public void Execute()
    {
        if (nav.Current is not { } n)
        {
            log.Msg($"[SourceSharp/Ai] {nav.Refusal}\n");
            return;
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] navigation for {n.Map}: {n.Nav.Graph.CellCount} cells, {n.Nav.Graph.EdgeCount} edges, bsp {n.BspSha256[..12]}, nav {n.NavSha256[..12]}, MapFormats {NavReaderVersion.Package}\n"));
    }
}

/// <summary>
/// <c>ai_nav_validate [n] [seed]</c> (plan_hl2mp_ai §6.3): the validator
/// against the engine's hull traces, on the main thread; prints the count,
/// each arm's rate against its ceiling (standing and floor 0.5 %,
/// solid 3 %), and the first mismatches.
/// </summary>
[ConCommand("ai_nav_validate", Help = "ai_nav_validate [n=200] [seed]: check the nav against the world's hull traces", Flags = ConVarFlags.Cheat)]
public sealed class AiNavValidateCommand(Hl2mpNav nav, ITraceQueries traces, IEngineEntities entities, ILog log)
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
public sealed class AiNavProbeCommand(Hl2mpNav nav, ITraceQueries traces, IEngineEntities entities, ILog log)
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
