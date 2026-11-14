using System.Globalization;
using System.Text;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The rocket jump's presence in a level (plan_bots_teams_tf2_part_g G.5 steps 2, 6 and 7): the
/// level's book of jumps, which the brains read; the program the applier runs for the ability
/// (<see cref="RocketJumpProgram"/>); the tally of what each jump did; and the planner behind them.
///
/// <para>
/// <b>Where a recipe is found.</b> Never in a decide and never on the game's thread. When a
/// navigation is set, its free space is read on a task (<see cref="VoxelColumns.FromNav"/>) and,
/// the frame after that is done, the level's simulators and planner are made here on the game's
/// thread (the class table and the launcher's script are read once) and handed to the desk. From
/// then on a stretch a brain asked about, or a jump a bot made, cues one task that does the desk's
/// work (<see cref="JumpDesk.Work"/>: the route, the search of at most 160 flights, the flight
/// again); the brain reads the answer at a later decide. One piece of work runs at a time.
/// </para>
///
/// <para>
/// <b>The level's blast links</b> (lane G5c-2a). When the desk opens, a task finds the links a
/// route search may take (<see cref="JumpLinkWork"/>: the cache file beside the graph's, else the
/// place-finding pass from the level's spawns, and the file written) and publishes them
/// (<see cref="LinkSlot"/>, <see cref="LinkSet"/>). Another navigation, or none, cancels a pass
/// under way and empties the slot; the game's end waits for it to stop.
/// </para>
///
/// <para>
/// <b>A Soldier takes them</b> (lane G5c-2b). The set is published for the navigation profile
/// (<see cref="TFBlastLinks"/>), which gives a Soldier's searches the links; each link's plan (its
/// recipe flown once on the same worker) goes to the book; and what each bot's follower says of its
/// route's next link comes here from the main thread (<see cref="Note"/>) and is kept in the book,
/// which the tree reads (<see cref="JumpBook.RouteLink"/>) to begin a link's jump.
/// </para>
///
/// <para>
/// The counters are served at <c>/ai/jumps</c> (the dev API; a route provider of the TF2 binding's
/// own, joined to the providers' chain by implementing the interface): the jumps begun and how each ended,
/// the book's and the worker's counts, and the last jumps in full with the simulator's flight of
/// each beside the game's.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(INavLinkReports)])]
public sealed class TFJumps : IFrameLifecycle, IGameLifecycle, IDebugRouteProvider, IOrderedHook, IAbilityPrograms, INavLinkReports
{
    /// <summary>Its place in the game's lifecycle: only its end matters (<see cref="OnShutdown"/>).</summary>
    public int LifecycleOrder => ModStages.First + 920;

    /// <summary>Its place among the dev API's route providers: after the binding's own (order 0) and the core's.</summary>
    public int HookOrder => ModStages.First + 920;

    /// <summary>After the navigation's own frame work (First + 400), which sets the level's nav, and before the bots' commands (First + 500).</summary>
    public int FrameOrder => ModStages.First + 420;

    /// <summary>The route the counters are served at.</summary>
    public const string Route = "/ai/jumps";

    /// <summary>How fast the planner's own pilot turns its view in the simulator, degrees a second: a middling hand's flick.</summary>
    public const float PlannerTurn = 360f;

    private readonly BlastJumpSims _sims;
    private readonly TFWeaponScripts _scripts;
    private readonly TFClassTable _classes;
    private readonly IAiNavigation _nav;
    private readonly ILog _log;
    private readonly ITFLevelEntities? _placed;
    private readonly JumpDesk _desk;
    private readonly RocketJumpProgram _program;
    private LoadedNav? _level;
    private Task<VoxelColumns>? _reading;
    private float _tick = BlastJumpSims.TickSeconds;
    private double _openMs;
    private string _state = "no navigation";
    private NavLinkSlot _linkSlot = new();
    private volatile JumpLinkSet? _linkSet;
    private volatile JumpLinkOutcome? _linkOutcome;
    private volatile string _linkState = "no navigation";
    private readonly Lock _linkGate = new();
    private CancellationTokenSource? _linkCancel;
    private Task? _linkWork;
    private int _linkSeeds;
    private readonly TFBlastLinks _published;

    /// <param name="placed">The level's placed entities, whose team spawns seed the link pass; null for none (the pass then proposes anywhere).</param>
    /// <param name="published">Where the links are published for the Soldiers' searches (<see cref="TFBlastLinks"/>); null: a holder of its own.</param>
    public TFJumps(BlastJumpSims sims, TFWeaponScripts scripts, TFClassTable classes, IAiNavigation nav, ILog log, ITFLevelEntities? placed = null,
                   TFBlastLinks? published = null)
    {
        _sims = sims;
        _scripts = scripts;
        _classes = classes;
        _nav = nav;
        _log = log;
        _placed = placed;
        _published = published ?? new TFBlastLinks();
        _desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally(), ThreadCpuClock.Micros()) { Cue = Cue };
        _program = new RocketJumpProgram(_desk.Book, classes, _desk.Flew);
    }

    /// <summary>The read of the navigation's free space under way, or null: a fact waits on it.</summary>
    internal Task? Reading => _reading;

    /// <summary>The links' work under way or done, or null: a fact waits on it.</summary>
    internal Task? LinkWork => _linkWork;

    /// <summary>The level's blast links as a route search takes them: empty while they are being found, and for no level.</summary>
    public NavLinkSlot LinkSlot => _linkSlot;

    /// <summary>The level's blast links with their jumps (a link's <see cref="NavLink.Id"/> names its recipe), or null while they are being found.</summary>
    public JumpLinkSet? LinkSet => _linkSet;

    /// <summary>What finding them answered, or null while they are being found.</summary>
    public JumpLinkOutcome? LinkOutcome => _linkOutcome;

    /// <summary>Where the links are published for the Soldiers' searches.</summary>
    public TFBlastLinks Published => _published;

    /// <summary>What a bot's follower said of its route's next link, from the main thread as it takes the drive step back: kept in the book, which the brains read.</summary>
    public void Note(ActorId bot, Tick stamp, in NavLinkStep step) => _desk.Book.NoteRoute(bot.Slot, stamp, step);

    /// <summary>The level's jumps as the brains read them.</summary>
    public JumpBook Book => _desk.Book;

    /// <summary>The desk: the book, the tally and the worker's work.</summary>
    public JumpDesk Desk => _desk;

    public IAbilityProgram? Named(string ability) => ability == TFAbilities.RocketJump ? _program : null;

    /// <summary>There is work: one task does it. From any thread.</summary>
    private void Cue() => Task.Run(_desk.Work);

    /// <summary>Follows the level's navigation: another one (or none) closes the desk, and its simulator opens it when its free space has been read.</summary>
    public void OnPreThink(float frameTime)
    {
        if (frameTime > 0f)
            _tick = frameTime;
        LoadedNav? now = _nav.Current;
        if (!ReferenceEquals(now, _level))
        {
            _level = now;
            _desk.Close();
            StopLinks(wait: false);
            _linkState = now is null ? "no navigation" : "waiting for the simulator";
            _state = now is null ? "no navigation" : "reading the navigation's free space";
            // The reader is immutable; the columns are read off the game's thread and picked up below.
            _reading = now is null ? null : Task.Run(() => VoxelColumns.FromNav(now.File.Source));
        }

        if (_reading is { IsCompleted: true } read)
        {
            _reading = null;
            Open(_level!, read);
        }
    }

    private void Open(LoadedNav level, Task<VoxelColumns> read)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (read.IsFaulted || _scripts.GetQuiet(Trees.TFDecider.SoldierLauncher) is not { } script)
        {
            _state = read.IsFaulted ? $"no free space: {read.Exception?.GetBaseException().Message}" : "no rocket launcher script";
            _log.Warning($"[SourceSharp/Ai] TF2 rocket jumps are off on {level.Map}: {_state}\n");
            return;
        }

        VoxelColumns columns = read.Result;
        JumpLauncher launcher = JumpLauncher.Rocket(script);
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, _classes[TFClass.Soldier].MaxSpeed, _tick, PlannerTurn);
        _desk.Open(JumpPlanner.Of(level.Nav.Graph, _sims.Create(columns, launcher, _tick), _sims.Create(JumpEnvelope.OpenAir(), launcher, _tick), limits,
                                  new JumpSearchSettings(), new JumpPlanSettings()));
        StartLinks(level, columns, launcher, limits);
        _openMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _state = "open";
        _log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] TF2 rocket jumps: the simulator is up for {level.Map} ({columns.Width} by {columns.Depth} columns of {columns.VoxelSize:F0}, tick {_tick:F4} s, {_openMs:F1} ms on the game's thread)\n"));
    }

    /// <summary>The cells the level's team spawns stand in, read here on the game's thread: what the link pass starts from.</summary>
    private int[] Seeds(NavGraph graph)
    {
        if (_placed is null)
            return [];
        var seeds = new List<int>();
        foreach (MapEntity e in _placed.Read().All)
        {
            if (e.ClassName is "info_player_teamspawn" or "info_player_deathmatch" && NavPoints.CellNear(graph, e.Origin, ClearanceClass.Stand) is { IsValid: true } cell)
                seeds.Add(cell.Index);
        }

        return [.. seeds];
    }

    /// <summary>
    /// Starts finding the level's links: the simulators and the seeds made here, on the game's thread, the cache read
    /// or the pass run on a task. The slot is the level's own, so a pass of a level gone publishes nowhere anyone reads.
    /// </summary>
    private void StartLinks(LoadedNav level, VoxelColumns columns, JumpLauncher launcher, in JumpPilotLimits limits)
    {
        StopLinks(wait: false);
        var search = new JumpSearchSettings();
        JumpPlacesSettings places = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        int[] seeds = Seeds(level.Nav.Graph);
        _linkSeeds = seeds.Length;
        string key = JumpLinkCache.InputsKey(launcher, limits, search, places, seeds);
        var cache = new JumpLinkCache();
        // A level named by its files' hashes has a cache; one that is not (a fact's) has none.
        string? path = _nav.CacheDirectory is { } dir && level.BspSha256.Length >= 16 && level.NavSha256.Length >= 16
            ? new NavGraphCache(dir, NavEdgeCosts.Default).SidePathFor(level.Map, level.BspSha256, level.NavSha256, level.NumbersKey, cache.Suffix)
            : null;
        var work = new JumpLinkWork(level.Nav.Graph, columns, _sims.Create(columns, launcher, _tick), _sims.Create(JumpEnvelope.OpenAir(), launcher, _tick), limits, search, places,
                                    seeds, new NavLinkCosts { RunSpeed = limits.RunSpeed }, cache, path, key, ThreadCpuClock.Micros());
        var slot = new NavLinkSlot();
        _linkSlot = slot;
        var cancel = new CancellationTokenSource();
        _linkCancel = cancel;
        _linkState = "finding";
        CancellationToken token = cancel.Token;
        _linkWork = Task.Run(() =>
        {
            try
            {
                JumpLinkOutcome outcome = work.Run(token);
                lock (_linkGate)
                {
                    // Published only while this is still the level's work: a stop takes the gate before it empties the fields.
                    token.ThrowIfCancellationRequested();
                    _linkSet = outcome.Set;
                    _linkOutcome = outcome;
                    slot.Links = outcome.Set.Links;
                    _desk.Book.SetLinks(outcome.Set.Links, outcome.Plans);
                    _published.Set = outcome.Set;
                    _linkState = outcome.FromCache ? "read from the cache" : outcome.CacheError is { } why ? $"found; not cached: {why}" : "found";
                }

                _log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] TF2 blast links for {level.Map}: {outcome.Set.Links.Count} {(outcome.FromCache ? "read from the cache" : "found")} in {outcome.CpuMs:F0} ms of a worker's CPU ({outcome.Candidates} candidates, {outcome.Flights} flights, the pass {outcome.PassMs:F0} ms)\n"));
            }
            catch (OperationCanceledException)
            {
                // A level change: what it found is no level's.
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    _linkState = $"faulted: {e.GetType().Name}: {e.Message}";
                    _log.Warning($"[SourceSharp/Ai] TF2 blast links for {level.Map}: {e.GetType().Name}: {e.Message}\n");
                }
            }
        }, token);
    }

    /// <summary>Cancels the links' work under way (it stops within one candidate's search) and empties the slot; waits for it when asked.</summary>
    private void StopLinks(bool wait)
    {
        lock (_linkGate)
        {
            _linkCancel?.Cancel();
            _linkSlot = new NavLinkSlot();
            _linkSet = null;
            _linkOutcome = null;
            _published.Set = null;
            _desk.Book.SetLinks(null, null);
        }

        if (wait && _linkWork is { } running)
        {
            try
            {
                running.Wait();
            }
            catch (AggregateException e) when (e.InnerExceptions.All(x => x is OperationCanceledException))
            {
                // What Cancel asks for.
            }
        }

        _linkCancel = null;
    }

    /// <summary>The game's end waits for the links' work to stop: a hot reload's unload waits for nothing of ours.</summary>
    public void OnShutdown()
    {
        StopLinks(wait: true);
        _linkWork = null;
    }

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the TF2 bots' rocket jumps: ai.jump.begun and how each ended (landed near the plan's touch, short, elsewhere, called off, fizzled, died, timed out), the book (asked, planned, refused by reason), the worker (flights, floods, cpu), the level's blast links (how many, from the cache or found, what the pass cost), and the last jumps in full (how long each stood for its view, how high it rose) with the simulator's flight of each from the same launch"),
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query) => new(200, Json());

    /// <summary>The level's links, each with its cells, its start and landing, its family, its rise and what it costs: the dev API's list, for the tools.</summary>
    private string LinksJson()
    {
        if (_linkSet is not { } set)
            return "[]";
        var json = new StringBuilder("[");
        for (int i = 0; i < set.Links.Count; i++)
        {
            NavLink l = set.Links[i];
            JumpLink j = set.JumpOf(i);
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"index\":{i},\"from\":{l.From},\"to\":{l.To},\"start\":[{l.Start.X:F1},{l.Start.Y:F1},{l.Start.Z:F1}],\"takeoff\":[{j.Recipe.Takeoff.X:F1},{j.Recipe.Takeoff.Y:F1},{j.Recipe.Takeoff.Z:F1}],"
                + $"\"landing\":[{j.Recipe.Landing.X:F1},{j.Recipe.Landing.Y:F1},{j.Recipe.Landing.Z:F1}],\"family\":\"{j.Family}\",\"rise\":{j.Recipe.Landing.Z - j.Recipe.Takeoff.Z:F0},"
                + $"\"seconds\":{j.Seconds:F3},\"health\":{j.Health:F1},\"robustness\":{j.Robustness:F2},\"walk_seconds\":{(j.WalkSeconds is { } w ? w.ToString("F2", CultureInfo.InvariantCulture) : "null")}}}");
        }

        return json.Append(']').ToString();
    }

    /// <summary>The counters and the last jumps, as <see cref="Route"/> serves them.</summary>
    public string Json()
    {
        JumpTallyCounts t = _desk.Tally.Counts;
        JumpBookCounts b = _desk.Book.Counts;
        JumpDeskCounts d = _desk.Counts;
        var json = new StringBuilder(512);
        json.Append(CultureInfo.InvariantCulture,
            $"{{\"state\":{AiJson.Quote(_state)},\"map\":{AiJson.Quote(_level?.Map ?? "")},\"nav_sha256\":{AiJson.Quote(_level?.NavSha256 ?? "")},"
            + $"\"ai.jump.begun\":{_program.Begun},\"ai.jump.unknown\":{_program.Unknown},\"ai.jump.ended\":{t.Ended},"
            + $"\"ai.jump.landed.near\":{t.LandedNear},\"ai.jump.landed.short\":{t.LandedShort},\"ai.jump.landed.elsewhere\":{t.LandedElsewhere},"
            + $"\"ai.jump.called_off.view\":{t.ViewLate},\"ai.jump.called_off.astray\":{t.Astray},\"ai.jump.fizzled\":{t.Fizzled},\"ai.jump.fizzled.felt\":{t.FizzledFelt},\"ai.jump.died\":{t.Died},\"ai.jump.timed_out\":{t.TimedOut},"
            + $"\"ai.jump.predicted\":{t.Predicted},\"ai.jump.book.advised\":{b.Advised},\"ai.jump.book.asked\":{b.Asked},\"ai.jump.book.dropped\":{b.Dropped},"
            + $"\"ai.jump.book.planned\":{b.Planned},\"ai.jump.book.refused\":{b.Refused},\"ai.jump.book.plans\":{b.Plans},\"ai.jump.book.waiting\":{b.Waiting},");
        foreach (JumpRefusal why in Enum.GetValues<JumpRefusal>())
        {
            if (why != JumpRefusal.None)
                json.Append(CultureInfo.InvariantCulture, $"\"ai.jump.refused.{why.ToString().ToLowerInvariant()}\":{_desk.Book.Refused(why)},");
        }

        json.Append(CultureInfo.InvariantCulture,
            $"\"ai.jump.work.pieces\":{d.Planned + d.Predicted},\"ai.jump.work.flights\":{d.Flights},\"ai.jump.work.floods\":{d.Floods},\"ai.jump.work.cpu.us\":{d.WorkMicros:F0},"
            + $"\"ai.jump.work.faults\":{d.Faults},\"ai.jump.open.ms\":{_openMs:F1},"
            + $"\"links.state\":{AiJson.Quote(_linkState)},\"links\":{(_linkOutcome is { } lo ? lo.Set.Links.Count : 0)},\"links.from_cache\":{(_linkOutcome?.FromCache == true ? "true" : "false")},"
            + $"\"links.cpu.ms\":{_linkOutcome?.CpuMs ?? 0d:F1},\"links.pass.ms\":{_linkOutcome?.PassMs ?? 0d:F1},\"links.candidates\":{_linkOutcome?.Candidates ?? 0},\"links.flights\":{_linkOutcome?.Flights ?? 0},\"links.seeds\":{_linkSeeds},"
            + $"\"ai.jump.link.begun\":{_program.LinksBegun},\"ai.jump.link.reported\":{_desk.Book.RouteCounts.Notes},\"ai.jump.link.advised\":{_desk.Book.RouteCounts.Advised},"
            + $"\"links.list\":{LinksJson()},"
            + $"\"near_units\":{JumpTally.NearUnits:F0},\"last\":{_desk.Tally.LastJson()},\"plans\":[");
        JumpPlan[] plans = _desk.Book.Plans();
        for (int i = 0; i < plans.Length; i++)
        {
            JumpPlan p = plans[i];
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"id\":{p.Id},\"takeoff\":[{p.Recipe.Takeoff.X:F1},{p.Recipe.Takeoff.Y:F1},{p.Recipe.Takeoff.Z:F1}],\"landing\":[{p.Recipe.Landing.X:F1},{p.Recipe.Landing.Y:F1},{p.Recipe.Landing.Z:F1}],"
                + $"\"pitch\":{p.Recipe.Pitch:F1},\"yaw_offset\":{p.Recipe.YawOffset:F1},\"shot_delay\":{p.Recipe.ShotDelay},\"run_up\":{p.Recipe.RunUp:F0},\"slide\":{p.Slide:F0},\"hold_run\":{p.HoldRun:F0},\"hold_seconds\":{p.HoldSeconds:F3},"
                + $"\"seconds\":{p.Seconds:F3},\"air_seconds\":{p.AirSeconds:F3},\"walk_seconds\":{p.WalkSeconds:F3},\"health\":{p.Health:F1},\"robustness\":{p.Robustness:F2},\"apex\":{p.Apex:F1}}}");
        }

        return json.Append("]}").ToString();
    }
}
