using System.Globalization;
using System.Text;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Experience;
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
/// <b>Jumps players showed</b> (lane G6b, plan G.6). Each frame the experience's snapshot is read (a reference); a new one
/// of the level's map, once the shape links are published, starts a learn pass on a task (<see cref="JumpLearning"/>:
/// each Soldier blast jump of the snapshot fitted by the search, its exemplar only a hint), and the links it makes are
/// appended to the shape links and the grown set published the same way, so a jump shown in a round is a link from the
/// next round's snapshot. Nothing of it is cached: it is fitted again at each level's load.
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
    private JumpSkip? _linkSkip;
    private readonly TFBlastLinks _published;

    /// <param name="placed">The level's placed entities, whose team spawns seed the link pass; null for none (the pass then proposes anywhere).</param>
    /// <param name="published">Where the links are published for the Soldiers' searches (<see cref="TFBlastLinks"/>); null: a holder of its own.</param>
    /// <param name="experience">Where each try of a link is written (lane G5e, <see cref="TFLinkExperience.StatOf"/>); null: nowhere.</param>
    /// <param name="skip"><c>ai_tf_links_skip</c>: a place the level's link pass proposes no landing in (lane G6b); null: none.</param>
    /// <param name="snapshots">Where the experience's snapshot is read for the jumps players showed (lane G6b): the experience's own slot when null.</param>
    /// <param name="jumpHealth"><c>ai_tf_feel_jumphealth</c> (lane G9): read when a level's links are published; on (or null), a link that lands by a health pack is published with what a search spends on it (<see cref="JumpHeals.Spends"/>).</param>
    public TFJumps(BlastJumpSims sims, TFWeaponScripts scripts, TFClassTable classes, IAiNavigation nav, ILog log, ITFLevelEntities? placed = null,
                   TFBlastLinks? published = null, ExperienceService? experience = null, TFLinksSkipConVar? skip = null, ExperienceSlot? snapshots = null,
                   TFFeelJumpHealthConVar? jumpHealth = null)
    {
        _jumpHealthSwitch = jumpHealth;
        _sims = sims;
        _scripts = scripts;
        _classes = classes;
        _nav = nav;
        _log = log;
        _placed = placed;
        _published = published ?? new TFBlastLinks();
        _experience = experience;
        _skip = skip;
        _snapshots = snapshots ?? experience?.Slot;
        _desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally(), ThreadCpuClock.Micros()) { Cue = Cue };
        _program = new RocketJumpProgram(_desk.Book, classes, Flew);
    }

    private readonly ExperienceService? _experience;
    private readonly TFFeelJumpHealthConVar? _jumpHealthSwitch;
    private volatile JumpHeals? _heals;
    private NavLinkCosts _linkCosts = new() { RunSpeed = 1f };
    private bool _jumpHealth = true;
    private int _jumpHealthMax = Trees.TFDecider.SoldierHealth;
    private readonly TFLinksSkipConVar? _skip;
    private readonly ExperienceSlot? _snapshots;
    private volatile JumpLinkOutcome? _shapeOutcome;
    private JumpLearning? _learning;
    private Task? _learnWork;
    private MapExperience? _learnedFrom;
    private volatile JumpLearnOutcome? _learnOutcome;
    private volatile string _learnState = "no navigation";
    private long _learnPasses, _learnPublishes, _learnFlights;
    private double _learnCpuMs, _learnPublishUs;
    private long _linkTries, _linkSuccesses, _linkUntried, _linkUnwritten;

    /// <summary>
    /// A jump a bot made, from the applier on the game's thread: tallied and flown again (the desk), and a link's try written to
    /// the experience (<see cref="TFLinkExperience.StatOf"/>) while the feed gathers.
    /// </summary>
    private void Flew(JumpFlown flown)
    {
        _desk.Flew(flown);
        if (NavLinks.LinkOf(flown.Planned.Id) < 0)
            return;
        if (TFLinkExperience.StatOf(flown) is not { } stat)
        {
            _linkUntried++;
            return;
        }

        _linkTries++;
        _linkSuccesses += stat.Successes > 0d ? 1 : 0;
        if (_experience?.NoteLink(TFLinkExperience.KeyOf(flown.Planned.Recipe), stat) != true)
            _linkUnwritten++;
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

        Learn();
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

    /// <summary>
    /// The cells the level's team spawns stand in, read here on the game's thread: what the link pass starts from. In a level
    /// in rooms (plan_bots_teams_tf2_part_g stage A) also each way into the room in play (<see cref="IAiNavigation.RoomWaysIn"/>:
    /// its teleport destinations and kept teleports' landings), after the spawns: a course's room holds no spawn, and a pass
    /// seeded by none proposed takeoffs anywhere and followed no landing (jump_academy2_rc9a's Basic found no link). A level
    /// not in rooms has no way in, so its seeds, and its links' cache key, are as they were.
    /// </summary>
    private int[] Seeds(NavGraph graph)
    {
        var seeds = new List<int>();
        foreach (MapEntity e in _placed?.Read().All ?? [])
        {
            if (e.ClassName is "info_player_teamspawn" or "info_player_deathmatch" && NavPoints.CellNear(graph, e.Origin, ClearanceClass.Stand) is { IsValid: true } cell)
                seeds.Add(cell.Index);
        }

        foreach (AiVector way in _nav.RoomWaysIn)
        {
            if (NavPoints.CellNear(graph, way, ClearanceClass.Stand) is { IsValid: true } cell && !seeds.Contains(cell.Index))
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
        JumpSkip? skip = JumpSkip.Parse(_skip?.Value);
        _linkSkip = skip;
        if (skip is { } told)
            _log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] TF2 blast links for {level.Map}: told to skip landings within {told.Radius:F0} of ({told.At.X:F0} {told.At.Y:F0} {told.At.Z:F0}) (ai_tf_links_skip)\n"));
        string key = JumpLinkCache.InputsKey(launcher, limits, search, places, seeds, skip);
        var cache = new JumpLinkCache();
        // A level named by its files' hashes has a cache; one that is not (a fact's) has none.
        string? path = _nav.CacheDirectory is { } dir && level.BspSha256.Length >= 16 && level.NavSha256.Length >= 16
            ? new NavGraphCache(dir, NavEdgeCosts.Default).SidePathFor(level.Map, level.BspSha256, level.NavSha256, level.NumbersKey, cache.Suffix)
            : null;
        var work = new JumpLinkWork(level.Nav.Graph, columns, _sims.Create(columns, launcher, _tick), _sims.Create(JumpEnvelope.OpenAir(), launcher, _tick), limits, search, places,
                                    seeds, new NavLinkCosts { RunSpeed = limits.RunSpeed }, cache, path, key, ThreadCpuClock.Micros(), skip);
        var costs = new NavLinkCosts { RunSpeed = limits.RunSpeed };
        _linkCosts = costs;
        _learning = _snapshots is null ? null
            : new JumpLearning(level.Nav.Graph, _sims.Create(columns, launcher, _tick), _sims.Create(JumpEnvelope.OpenAir(), launcher, _tick), limits, search,
                               new ShownJumpFitSettings(), costs, ThreadCpuClock.Micros());
        _learnState = _learning is null ? "no experience" : "waiting for the links";
        // The heal places and the switch are read here, on the game's thread; the places' walks are flooded on the task.
        JumpHealPlace[] healPlaces = _placed is null ? [] : JumpHeals.PlacesOf(_placed.Read());
        _jumpHealth = _jumpHealthSwitch?.Value ?? true;
        _jumpHealthMax = _classes[TFClass.Soldier].MaxHealth > 0 ? _classes[TFClass.Soldier].MaxHealth : Trees.TFDecider.SoldierHealth;
        _heals = null;
        var slot = new NavLinkSlot();
        _linkSlot = slot;
        var cancel = new CancellationTokenSource();
        _linkCancel = cancel;
        _linkState = "finding";
        CancellationToken token = cancel.Token;
        NavGraph graph = level.Nav.Graph;
        _linkWork = Task.Run(() =>
        {
            try
            {
                JumpHeals heals = JumpHeals.Of(graph, healPlaces);
                lock (_linkGate)
                {
                    token.ThrowIfCancellationRequested();
                    _heals = heals;
                    _desk.Book.Heals = heals;
                }

                JumpLinkOutcome outcome = work.Run(token);
                (JumpLinkSet published, JumpPlan[] plans) = Publishable(graph, outcome.Set, outcome.Plans, heals, costs);
                lock (_linkGate)
                {
                    // Published only while this is still the level's work: a stop takes the gate before it empties the fields.
                    token.ThrowIfCancellationRequested();
                    _linkSet = published;
                    _linkOutcome = outcome;
                    _shapeOutcome = outcome;
                    slot.Links = published.Links;
                    _desk.Book.SetLinks(published.Links, plans);
                    _published.Set = published;
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

    /// <summary>
    /// A set of links as it is published (lane G9): each link's plan with the heal places by its landing
    /// (<see cref="JumpPlan.Heals"/>), and, with <c>ai_tf_feel_jumphealth</c> on, each link spending what its landing's place
    /// does not give back (<see cref="JumpHeals.Spends"/>, a Soldier's health against the floor) and weighed at what its blast
    /// costs once that place has given health back (lane G9b, <see cref="JumpHeals.Weights"/>), so a route to or past a pack
    /// takes it; off, or with no link that lands by health, the set as found.
    /// </summary>
    private (JumpLinkSet Set, JumpPlan[] Plans) Publishable(NavGraph graph, JumpLinkSet set, IReadOnlyList<JumpPlan> plans, JumpHeals heals, NavLinkCosts costs)
    {
        var withHeals = new JumpPlan[plans.Count];
        for (int i = 0; i < withHeals.Length; i++)
        {
            withHeals[i] = plans[i] with { Heals = heals.NearCell(set.Links[i].To) };
            if (!withHeals[i].Heals.IsEmpty)
                withHeals[i] = withHeals[i] with { JumpWays = heals.JumpWays(withHeals[i], set.Links[i].From, costs.RunSpeed) };
        }

        if (!_jumpHealth)
            return (set, withHeals);
        float[] spends = heals.Spends(set, withHeals, _jumpHealthMax, Trees.TFDecider.RocketJumpHealthFloor);
        float[] weights = heals.Weights(set, withHeals, _jumpHealthMax, costs);
        // No link lands by health: the set as found, its hash and every mask over it as they were.
        for (int i = 0; i < spends.Length; i++)
        {
            if (spends[i] < set.JumpOf(i).Health || weights[i] < set.JumpOf(i).Health)
                return (set.WithSpends(graph, spends, costs, weights), withHeals);
        }

        return (set, withHeals);
    }

    /// <summary>Links of the published set that spend less than their blast costs: those that land by health.</summary>
    private int LinksByHealth()
    {
        if (_linkSet is not { } set)
            return 0;
        int n = 0;
        for (int i = 0; i < set.Links.Count; i++)
            n += set.Links[i].Health < set.JumpOf(i).Health ? 1 : 0;
        return n;
    }

    /// <summary>Links of the published set weighed under their blast's cost (lane G9b): those by a place that gives the health back, worth its walk.</summary>
    private int LinksWeighedByHealth()
    {
        if (_linkSet is not { } set)
            return 0;
        int n = 0;
        for (int i = 0; i < set.Links.Count; i++)
            n += set.Links.CostOf(i) < _linkCosts.Cost(set.Links[i] with { Health = set.JumpOf(i).Health }) - 0.01f ? 1 : 0;
        return n;
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
            _shapeOutcome = null;
            _learning = null;
            _learnedFrom = null;
            _learnOutcome = null;
            _learnState = "no navigation";
            _published.Set = null;
            _heals = null;
            _desk.Book.SetLinks(null, null);
            _desk.Book.Heals = null;
        }

        foreach (Task? running in (ReadOnlySpan<Task?>)[_linkWork, _learnWork])
        {
            if (!wait || running is null)
                continue;
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
        _learnWork = null;
    }

    /// <summary>The learn pass under way or done, or null: a fact waits on it.</summary>
    internal Task? LearnWork => _learnWork;

    /// <summary>What the last learn pass answered, or null before one.</summary>
    public JumpLearnOutcome? LearnOutcome => _learnOutcome;

    /// <summary>
    /// Starts a learn pass (lane G6b) when the experience holds a snapshot of the level's map that no pass has read, the
    /// shape links are published and no pass runs: on a task, publishing a grown set under the gate while it is still
    /// the level's work. The game's thread.
    /// </summary>
    private void Learn()
    {
        if (_learning is not { } learning || _snapshots is null || _level is not { } level || _shapeOutcome is not { } shape || _linkCancel is not { } cancel
            || _learnWork is { IsCompleted: false })
        {
            return;
        }

        MapExperience snapshot = _snapshots.Current;
        if (ReferenceEquals(snapshot, _learnedFrom))
            return;
        _learnedFrom = snapshot;
        if (snapshot.IsEmpty || !string.Equals(snapshot.Key.Map, level.Map, StringComparison.OrdinalIgnoreCase))
            return;
        NavLinkSlot slot = _linkSlot;
        CancellationToken token = cancel.Token;
        _learnState = "learning";
        _learnWork = Task.Run(() =>
        {
            try
            {
                JumpLearnOutcome outcome = learning.Learn(snapshot, shape.Set, shape.Plans, token);
                (JumpLinkSet Set, JumpPlan[] Plans)? publish = outcome.Set is { } learned && _heals is { } heals
                    ? Publishable(level.Nav.Graph, learned, outcome.Plans, heals, _linkCosts)
                    : outcome.Set is { } bare ? (bare, [.. outcome.Plans]) : null;
                lock (_linkGate)
                {
                    // Published only while this is still the level's work, as the shape pass's links are.
                    token.ThrowIfCancellationRequested();
                    _learnOutcome = outcome;
                    _learnPasses++;
                    _learnFlights += outcome.Flights;
                    _learnCpuMs += outcome.CpuMs;
                    if (publish is { } g)
                    {
                        long start = System.Diagnostics.Stopwatch.GetTimestamp();
                        JumpLinkSet set = g.Set;
                        _linkSet = set;
                        slot.Links = set.Links;
                        _desk.Book.SetLinks(set.Links, g.Plans);
                        _published.Set = set;
                        _learnPublishUs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                        _learnPublishes++;
                    }

                    _learnState = outcome.Set is null ? "learned; nothing new" : "learned";
                }

                _log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] TF2 blast links for {level.Map}: {outcome.Practiced} practised of {outcome.Practice.Checks.Count} practice row(s), {outcome.Learned} learned from {outcome.Fits.Fits.Count} shown jump(s) ({outcome.Fits.Searched} searched, {outcome.Flights} flights, {outcome.CpuMs:F0} ms of a worker's CPU){(outcome.Set is { } grown ? $"; {grown.Links.Count} links published" : "; nothing new")}\n"));
            }
            catch (OperationCanceledException)
            {
                // A level change: what it found is no level's.
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    _learnState = $"faulted: {e.GetType().Name}: {e.Message}";
                    _log.Warning($"[SourceSharp/Ai] TF2 learned links for {level.Map}: {e.GetType().Name}: {e.Message}\n");
                }
            }
        }, token);
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
                + $"\"origin\":\"{(j.Practiced ? "practice" : j.Shown ? "shown" : "shape")}\",\"seconds\":{j.Seconds:F3},\"health\":{j.Health:F1},\"spend\":{l.Health:F1},\"cost_seconds\":{set.Links.CostOf(i) / _linkCosts.RunSpeed:F2},\"robustness\":{j.Robustness:F2},\"walk_seconds\":{(j.WalkSeconds is { } w ? w.ToString("F2", CultureInfo.InvariantCulture) : "null")}}}");
        }

        return json.Append(']').ToString();
    }

    /// <summary>
    /// The learn pass's part of <see cref="Json"/>, with its comma (lane G6b): its state, how many passes, publishes, flights
    /// and CPU, the last publish's time under the gate, the learned links published, and the last pass's verdict for each
    /// shown jump (where it took off and landed, its sightings, the flights its search took, the link it gave).
    /// </summary>
    private string LearnJson()
    {
        JumpLearnOutcome? o = _learnOutcome;
        var json = new StringBuilder(256);
        int learned = _linkSet is { } set ? set.Jumps.Count(j => j.Shown) : 0;
        json.Append(CultureInfo.InvariantCulture,
            $"\"links.learned\":{learned},\"links.learn.state\":{AiJson.Quote(_learnState)},\"links.learn.passes\":{_learnPasses},\"links.learn.publishes\":{_learnPublishes},"
            + $"\"links.learn.flights\":{_learnFlights},\"links.learn.cpu.ms\":{_learnCpuMs:F1},\"links.learn.last.cpu.ms\":{o?.CpuMs ?? 0d:F1},\"links.learn.publish.us\":{_learnPublishUs:F1},"
            + $"\"links.learn.searched\":{o?.Fits.Searched ?? 0},");
        foreach (ShownFitVerdict v in Enum.GetValues<ShownFitVerdict>())
            json.Append(CultureInfo.InvariantCulture, $"\"links.learn.{v.ToString().ToLowerInvariant()}\":{o?.Fits.Fits.Count(f => f.Verdict == v) ?? 0},");
        json.Append("\"links.learn.fits\":[");
        IReadOnlyList<ShownFit> fits = o?.Fits.Fits ?? [];
        for (int i = 0; i < fits.Count; i++)
        {
            ShownFit f = fits[i];
            AiVector up = f.Jump.Exemplar?.Takeoff ?? f.Jump.Key.From.Centre, down = f.Jump.Exemplar?.Landing ?? f.Jump.Key.To.Centre;
            int index = f.Link is { } l && _linkSet is { } s ? IndexOf(s, l) : -1;
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"takeoff\":[{up.X:F1},{up.Y:F1},{up.Z:F1}],\"landing\":[{down.X:F1},{down.Y:F1},{down.Z:F1}],\"sightings\":{f.Jump.Stat.Tries:F3},\"exemplar\":{(f.Jump.Exemplar is null ? "false" : "true")},"
                + $"\"verdict\":\"{f.Verdict.ToString().ToLowerInvariant()}\",\"flights\":{f.Flights},\"link\":{index}}}");
        }

        json.Append("],");

        // The practice rows (lane G8): how many links they gave, each verdict's count, and each row's.
        PracticeResult p = o?.Practice ?? PracticeResult.None;
        int practiced = _linkSet is { } now ? now.Jumps.Count(j => j.Practiced) : 0;
        json.Append(CultureInfo.InvariantCulture, $"\"links.practiced\":{practiced},\"links.practice.rows\":{p.Checks.Count},\"links.practice.flights\":{p.Flights},");
        foreach (PracticeVerdict v in Enum.GetValues<PracticeVerdict>())
            json.Append(CultureInfo.InvariantCulture, $"\"links.practice.{v.ToString().ToLowerInvariant()}\":{p.Checks.Count(c => c.Verdict == v)},");
        json.Append("\"links.practice.checks\":[");
        for (int i = 0; i < p.Checks.Count; i++)
        {
            PracticeCheck c = p.Checks[i];
            int index = c.Link is { } l && _linkSet is { } s ? IndexOf(s, l) : -1;
            AiVector up = c.Row.From.Centre, down = c.Row.To.Centre;
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"from_bin\":[{up.X:F0},{up.Y:F0},{up.Z:F0}],\"to_bin\":[{down.X:F0},{down.Y:F0},{down.Z:F0}],\"format\":{(c.Row.Recipe.Bytes.Length > 0 ? c.Row.Recipe.Bytes[0] : -1)},"
                + $"\"tries\":{c.Stat.Tries:F2},\"successes\":{c.Stat.Successes:F2},\"verdict\":\"{c.Verdict.ToString().ToLowerInvariant()}\",\"flights\":{c.Flights},\"link\":{index}}}");
        }

        return json.Append("],").ToString();
    }

    /// <summary>The index in <paramref name="set"/> of the link whose jump is <paramref name="link"/>, or -1.</summary>
    private static int IndexOf(JumpLinkSet set, in JumpLink link)
    {
        for (int i = 0; i < set.Links.Count; i++)
        {
            if (set.JumpOf(i) == link)
                return i;
        }

        return -1;
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
            + $"\"ai.jump.called_off.view\":{t.ViewLate},\"ai.jump.called_off.astray\":{t.Astray},\"ai.jump.fizzled\":{t.Fizzled},\"ai.jump.fizzled.felt\":{t.FizzledFelt},\"ai.jump.died\":{t.Died},\"ai.jump.timed_out\":{t.TimedOut},\"ai.jump.moved\":{t.Moved},\"ai.jump.reset\":{t.Reset},"
            + $"\"ai.jump.predicted\":{t.Predicted},\"ai.jump.book.calls\":{b.Calls},\"ai.jump.book.covered\":{b.Covered},\"ai.jump.book.advised\":{b.Advised},\"ai.jump.book.asked\":{b.Asked},\"ai.jump.book.dropped\":{b.Dropped},"
            + $"\"ai.jump.book.planned\":{b.Planned},\"ai.jump.book.refused\":{b.Refused},\"ai.jump.book.plans\":{b.Plans},\"ai.jump.book.waiting\":{b.Waiting},");
        foreach (JumpRefusal why in Enum.GetValues<JumpRefusal>())
        {
            if (why != JumpRefusal.None)
                json.Append(CultureInfo.InvariantCulture, $"\"ai.jump.refused.{why.ToString().ToLowerInvariant()}\":{_desk.Book.Refused(why)},");
        }

        json.Append(CultureInfo.InvariantCulture,
            $"\"ai.jump.work.pieces\":{d.Planned + d.Predicted},\"ai.jump.work.flights\":{d.Flights},\"ai.jump.work.floods\":{d.Floods},\"ai.jump.work.cpu.us\":{d.WorkMicros:F0},"
            + $"\"ai.jump.work.faults\":{d.Faults},\"ai.jump.open.ms\":{_openMs:F1},"
            + $"\"links.jump_health\":{(_jumpHealth ? "true" : "false")},\"links.by_health\":{LinksByHealth()},\"links.weighed_by_health\":{LinksWeighedByHealth()},\"heals.places\":{_heals?.Places.Count ?? 0},"
            + $"\"links.state\":{AiJson.Quote(_linkState)},\"links\":{_linkSet?.Links.Count ?? 0},\"links.shape\":{(_linkOutcome is { } lo ? lo.Set.Links.Count : 0)},\"links.from_cache\":{(_linkOutcome?.FromCache == true ? "true" : "false")},"
            + $"\"links.cpu.ms\":{_linkOutcome?.CpuMs ?? 0d:F1},\"links.pass.ms\":{_linkOutcome?.PassMs ?? 0d:F1},\"links.candidates\":{_linkOutcome?.Candidates ?? 0},\"links.flights\":{_linkOutcome?.Flights ?? 0},\"links.seeds\":{_linkSeeds},\"links.skip\":{AiJson.Quote(_linkSkip?.ToString() ?? "")},"
            + $"\"ai.jump.link.begun\":{_program.LinksBegun},\"ai.jump.link.reported\":{_desk.Book.RouteCounts.Notes},\"ai.jump.link.advised\":{_desk.Book.RouteCounts.Advised},"
            + $"\"ai.jump.link.tries\":{_linkTries},\"ai.jump.link.successes\":{_linkSuccesses},\"ai.jump.link.untried\":{_linkUntried},\"ai.jump.link.unwritten\":{_linkUnwritten},");
        foreach (JumpLinkRefusal why in Enum.GetValues<JumpLinkRefusal>())
        {
            if (why != JumpLinkRefusal.None)
                json.Append(CultureInfo.InvariantCulture, $"\"ai.jump.link.refused.{why.ToString().ToLowerInvariant()}\":{_desk.Book.LinkRefusals(why)},");
        }

        json.Append(LearnJson());
        json.Append(CultureInfo.InvariantCulture,
            $"\"links.list\":{LinksJson()},"
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
