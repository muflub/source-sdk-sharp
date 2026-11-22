using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// The server's experience feed (plan_bots_teams_tf2 Part G, G.3), the game
/// thread's half. A few times a second it reads where every living player is
/// and looks (humans and bots: the reading of "learn from what players do" is
/// a demo's, Q-G1), and at each hit and death where the two stood, and hands
/// that to the <see cref="ExperienceWorker"/>. It hands over; it never
/// accumulates and never touches a store. It posts a flush at a round's end
/// and every <see cref="FlushSeconds"/>, and closes the store at the level's
/// end (the last flush). The snapshot the brains read is loaded by the worker
/// when the level's key is first known and again at each round's start, and
/// put into <see cref="Slot"/> here, on the game's thread, when it arrives.
///
/// <para>What is handed over says nothing of who: a place, a view, a context
/// (stage, role, class) and whether a bot plays. Off (<c>ai_experience_db</c>
/// empty, a game that keeps none, or the default file in a game a tool
/// started) it starts no thread, opens no file and leaves the empty
/// experience in the slot. <c>ai_experience_learn 0</c>
/// loads snapshots and hands nothing over.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class ExperienceService(IExperienceGame game, IAiNavigation nav, IEnginePlayers players, IEngineEntities entities, IBotPlayers botPlayers,
                                      ActorDirectory directory, IEngineFrame frame, IEngineFiles files, ILog log, AiExperienceDbConVar db,
                                      AiExperienceHalfLifeConVar halfLife, AiExperienceLearnConVar learn, ILocalCommandLine commandLine,
                                      IExperienceStores? stores = null, ExperienceRouteSlot? routeSlot = null, IExperienceLinks? links = null,
                                      IExperienceHolds? holds = null)
    : IGameLifecycle, IFrameLifecycle, IPlayerHurtHook, IPlayerDeathHook, IOrderedHook, IDisposable
{
    /// <summary>Players are sampled every this many ticks: about ten times a second.</summary>
    public const int SampleTicks = 7;

    /// <summary>What was gathered is written at least this often.</summary>
    public const double FlushSeconds = 60d;

    /// <summary>The most one sample counts for: a server that stood still (a pause, a hitch) does not count as presence.</summary>
    public const double MaxSampleSeconds = 1d;

    private readonly ExperienceWorker _worker = new(stores);
    private readonly List<SensedActor> _living = new();
    private string? _path;
    private string _map = "", _mode = "";
    private string? _checksum;
    private byte[]? _bsp;
    private int _lastSample = -1;
    private double _sinceFlush;
    private bool _roundOver;
    private string? _saidRefusal;
    private string? _saidWaiting;
    private readonly ExperienceRouteSlot _routeSlot = routeSlot ?? new ExperienceRouteSlot();
    private MapExperience? _pending;
    private Task<ExperienceRoutes>? _routeWork;
    private NavGraph? _routesTriedOn;
    private ExperienceLinkKeys? _routesTriedLinks;
    private readonly HoldTracker _holdTracker = new();
    private readonly List<HeldSpot> _held = new();
    private ShownJumpJudge? _judge;
    private readonly Func<double>? _clock = ThreadCpuClock.Micros();

    /// <summary>After the session built the level's world (First + 450), which holds <see cref="Slot"/>.</summary>
    public int LifecycleOrder => ModStages.First + 460;

    /// <summary>After the senses and the taps (First + 600 to 655): the last of the AI's frame members.</summary>
    public int FrameOrder => ModStages.First + 660;

    /// <summary>After the combat tap (First + 900).</summary>
    public int HookOrder => ModStages.First + 910;

    /// <summary>Where the brains read the map's experience: the session puts it in the level's world. Empty while off and until the first load lands.</summary>
    public ExperienceSlot Slot { get; } = new();

    /// <summary>
    /// Where the routes the snapshot makes of the level's graph are published (lane G4a): set with the snapshot
    /// they were built from, so the brains never read a snapshot before its routes.
    /// </summary>
    public ExperienceRouteSlot Routes => _routeSlot;

    /// <summary>Routes published since the server started (<c>ai.experience.routes.builds</c>).</summary>
    public int RouteBuilds { get; private set; }

    /// <summary>Route builds that threw (<c>ai.experience.routes.faults</c>): their snapshot is published with no routes.</summary>
    public int RouteFaults { get; private set; }

    /// <summary>The routes' work under way, or null: a fact waits on it.</summary>
    internal Task? RouteWork => _routeWork;

    /// <summary>The worker, for the counters and the facts.</summary>
    public ExperienceWorker Worker => _worker;

    /// <summary>Whether a store is in use this level.</summary>
    public bool Active => _path is not null;

    /// <summary>The file in use this level; null when off.</summary>
    public string? Path => _path;

    /// <summary>Why the feed is off this level, as the log said it; "" while on.</summary>
    public string Off { get; private set; } = "no level";

    /// <summary>The snapshot's key the game thread is on: the map and mode it last posted.</summary>
    public (string Map, string Mode) On => (_map, _mode);

    public void OnLevelInitPostEntity(string mapName)
    {
        Leave();
        _map = mapName.ToLowerInvariant();
        bool toolGame = ExperienceDbPath.IsToolGame(commandLine.Args);
        _path = game.Keeps ? ExperienceDbPath.Resolve(db.Value, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), toolGame) : null;
        if (_path is null)
        {
            Off = !game.Keeps ? "this game keeps none"
                : db.Value.Trim().Length == 0 ? "ai_experience_db is empty"
                : toolGame ? "a tool's game (-toolgame) uses no default file: name a path in ai_experience_db"
                : "there is no user data directory for the default file";
            log.Msg($"[SourceSharp/Ai] experience: off ({Off})\n");
            return;
        }

        Off = "";
        // The map's checksum is its navigation's when one is bound to the bsp; else the bsp's own bytes, hashed on the worker.
        _checksum = nav.Current is { BspSha256.Length: >= 16 } bound ? bound.BspSha256[..16] : null;
        _bsp = _checksum is null ? files.ReadGameFileBytes(NavLoader.BspPath(mapName)) : null;
        _worker.Start();
        log.Msg($"[SourceSharp/Ai] experience: {_path}{(learn.Value ? "" : " (read only: ai_experience_learn 0)")}\n");
    }

    /// <summary>A hot reload under a running level: as a level start.</summary>
    public void OnLevelCarried(string mapName) => OnLevelInitPostEntity(mapName);

    public void OnLevelShutdownPreEntity() => Leave();

    public void OnShutdown() => Leave();

    /// <summary>The composition is going (a shutdown, a hot reload): the worker thread goes with it.</summary>
    public void Dispose()
    {
        Leave();
        _worker.Dispose();
    }

    /// <summary>The level is over: the last flush, the store closed, the worker joined, and the brains' slot back to empty.</summary>
    private void Leave()
    {
        if (_worker.Running)
        {
            // A hold under way when the level ends was held: it goes with the last flush.
            if (Gathering)
            {
                _holdTracker.EndAll(_held);
                HandHolds();
            }

            _worker.Close(halfLife.Value);
        }

        Say();
        Slot.Current = MapExperience.Empty;
        _routeSlot.Current = ExperienceRoutes.None;
        _pending = null;
        _routeWork = null;
        _routesTriedOn = null;
        _routesTriedLinks = null;
        _holdTracker.Clear();
        _held.Clear();
        _judge = null;
        _path = null;
        _map = _mode = "";
        _checksum = null;
        _bsp = null;
        _lastSample = -1;
        _sinceFlush = 0d;
        _roundOver = false;
        Off = "no level";
    }

    public void OnPostThink(float frameTime)
    {
        if (_path is null)
            return;
        double start = _clock?.Invoke() ?? 0d;
        Think();
        if (_clock is not null)
            FeedMicros += _clock() - start;
    }

    /// <summary>The thread CPU the feed's frames took on the game's thread while on, microseconds (<c>ai.experience.feed.us</c>, lane G6a).</summary>
    public double FeedMicros { get; private set; }

    private void Think()
    {
        if (_worker.TakeLoaded() is { } fresh)
            Take(fresh);
        PublishRoutes();

        Say();
        int tick = frame.TickCount();
        if (_lastSample >= 0 && tick >= _lastSample && tick - _lastSample < SampleTicks)
            return;
        double seconds = _lastSample < 0 || tick < _lastSample ? 0d : Math.Min(MaxSampleSeconds, (tick - _lastSample) * (double)ServerAiSession.TickSeconds);
        _lastSample = tick;
        // A store waiting on a busy file: the worker looks whether it attached, even when nothing is gathered.
        if (_worker.Status?.Waiting is not null)
            _worker.CheckAttach();

        // The mode names the key: the level's first sample opens it, and a mode switched under the level opens the next.
        string mode = game.Mode;
        if (mode != _mode)
        {
            _mode = mode;
            _roundOver = game.RoundOver;
            if (mode.Length > 0)
                _worker.Open(_path!, _map, mode, _checksum, _bsp, halfLife.Value);
        }

        if (_mode.Length == 0)
            return;
        bool over = game.RoundOver;
        if (over != _roundOver)
        {
            _roundOver = over;
            if (!over)
                _worker.RoundStart(halfLife.Value);
            else if (learn.Value)
            {
                // A hold under way when the round ends was held: it is handed over with the round.
                _holdTracker.EndAll(_held);
                HandHolds();
                _worker.RoundEnd(game.WinningRole, halfLife.Value);
            }
        }

        // Only a round being played is gathered: not the freeze before it, not the winners' time after.
        if (!learn.Value || over)
            return;
        Sample(seconds);
        _sinceFlush += seconds;
        if (_sinceFlush >= FlushSeconds)
        {
            _sinceFlush = 0d;
            _worker.Flush(halfLife.Value);
        }
    }

    /// <summary>
    /// A snapshot the worker loaded: with no graph it goes to the brains at once with no routes; else its
    /// routes are built on a task (never on the game's thread) and the two published together when it is done.
    /// </summary>
    private void Take(MapExperience fresh)
    {
        if (nav.Current?.Nav.Graph is not { } graph)
        {
            Publish(fresh);
            return;
        }

        _pending = fresh;
        _routesTriedOn = graph;
        ExperienceLinkKeys? keys = _routesTriedLinks = links?.Keys;
        _routeWork = Task.Run(() => ExperienceRoutes.Build(fresh, graph, ThreadCpuClock.Micros(), keys));
    }

    /// <summary>
    /// The routes' task done: its snapshot (if it waited for them) and the routes go to the brains, on the game's
    /// thread. A graph that arrives after the snapshot (a navigation built in the server) has the snapshot's
    /// routes built again over it, and so do the game's links when they arrive or change (lane G5e: a level's
    /// links are found on a worker after its snapshot is loaded).
    /// </summary>
    private void PublishRoutes()
    {
        if (_routeWork is { IsCompleted: true } work)
        {
            ExperienceRoutes routes = ExperienceRoutes.None;
            if (work.IsCompletedSuccessfully)
                routes = work.Result;
            else
            {
                RouteFaults++;
                log.Warning($"[SourceSharp/Ai] experience: the routes were not built ({work.Exception?.GetBaseException().Message}); the bots route as with none\n");
            }

            MapExperience? snapshot = _pending;
            (_pending, _routeWork) = (null, null);
            if (snapshot is not null)
                Publish(snapshot);
            _routeSlot.Current = routes;
            RouteBuilds++;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[SourceSharp/Ai] experience: routes for {routes.Roles.Count} side(s), {routes.Costed} cells costed, {routes.Bytes / 1024} KiB, "
                + $"{routes.LinksShut?.Count ?? 0} of {routes.LinkKeys?.Count ?? 0} links left out, built in {routes.BuildMicros / 1000d:F1} ms of a worker\n"));
            return;
        }

        ExperienceLinkKeys? keys = links?.Keys;
        if (_routeWork is null && nav.Current?.Nav.Graph is { } graph && (!ReferenceEquals(_routesTriedOn, graph) || !ReferenceEquals(_routesTriedLinks, keys))
            && !Slot.Current.IsEmpty)
        {
            MapExperience current = Slot.Current;
            (_routesTriedOn, _routesTriedLinks) = (graph, keys);
            _routeWork = Task.Run(() => ExperienceRoutes.Build(current, graph, ThreadCpuClock.Micros(), keys));
        }
    }

    private void Publish(MapExperience fresh)
    {
        Slot.Current = fresh;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] experience: {fresh.Key.Map} {fresh.Key.Mode}: {fresh.Rows} rows over {fresh.Rounds} rounds (hash {fresh.ContentHash:x16})\n"));
    }

    /// <summary>Every living player's place, view and context, handed to the worker.</summary>
    private void Sample(double seconds)
    {
        directory.Living(_living, _ => ActorId.None);
        int stage = game.Stage;
        var samples = new ActorSample[_living.Count];
        int n = 0;
        _holdTracker.BeginFrame();
        foreach (SensedActor actor in _living)
        {
            (int role, int cls) = game.Of(actor.Player);
            if (role == 0)
                continue;
            Vector at = entities.GetOrigin(actor.Player), view = entities.GetEyeAngles(actor.Player);
            samples[n] = new ActorSample(new AiVector(at.X, at.Y, at.Z), view.Y, view.X, new ExperienceContext(stage, role, cls), Alive: true, botPlayers.IsBot(actor.Player));
            if (holds is not null)
                _holdTracker.Sample(entities.GetIndex(actor.Player), samples[n].Place, samples[n].Yaw, samples[n].Context, samples[n].Bot, seconds, _held);
            n++;
        }

        if (n < samples.Length)
            Array.Resize(ref samples, n);
        _worker.Frame(samples, seconds);
        _holdTracker.EndFrame(_held);
        HandHolds();
    }

    /// <summary>
    /// The holds that ended (lane G4b, <see cref="HoldTracker"/>) near their side's own objective, as the game says
    /// (<see cref="IExperienceHolds"/>), handed to the worker as <c>spot</c> rows of the game's hold kind, each count at the
    /// feed's weight for who held it (a bot a quarter), so bots do not teach each other one corner. The game's thread.
    /// </summary>
    private void HandHolds()
    {
        if (holds is not null)
        {
            foreach (HeldSpot h in _held)
            {
                if (!holds.NearOwn(h.Context.Role, h.At))
                    continue;
                double w = ExperienceAccumulator.Weight(h.Bot);
                if (_worker.Spot(h.Context, holds.HoldKind, h.At, h.Yaw, new SpotStat(w, h.Seconds * w, h.Kills * w, h.Deaths * w)))
                    SpotsNoted++;
            }
        }

        _held.Clear();
    }

    /// <summary>Holds near a side's own objective handed to the worker since the server started (<c>ai.experience.spots.noted</c>).</summary>
    public int SpotsNoted { get; private set; }

    /// <summary>
    /// A try of a link a bot of the game made (lane G5e, plan G.5 step 7): its row, and what it added (a try, and a success
    /// with its seconds and health). Handed to the worker while hits and deaths are (<see cref="Gathering"/>); a bot's own
    /// tries count in full. Answers whether it was handed over. The game's thread.
    /// </summary>
    public bool NoteLink(LinkKey key, LinkStat stat)
    {
        if (!Gathering || !_worker.Link(key, stat))
            return false;
        LinksNoted++;
        return true;
    }

    /// <summary>Link tries handed to the worker since the server started (<c>ai.experience.links.noted</c>).</summary>
    public int LinksNoted { get; private set; }

    /// <summary>
    /// What came of a choice a bot of the game made (lane G4b, plan G.4's outcomes: a way into the enemy's base): one try
    /// with its score in [0, 1], under the bot's side and class and the stage now, of the game's <paramref name="kind"/> and
    /// <paramref name="choice"/>. In full, a bot's own result (plan G.3). Handed over while hits and deaths are
    /// (<see cref="Gathering"/>); answers whether it was. The game's thread.
    /// </summary>
    public bool NoteOutcome(int role, int cls, int kind, long choice, double score)
    {
        if (!Gathering || role == 0 || !_worker.Outcome(new OutcomeKey(new ExperienceContext(game.Stage, role, cls), kind, choice), new OutcomeStat(1d, Math.Clamp(score, 0d, 1d))))
            return false;
        OutcomesNoted++;
        return true;
    }

    /// <summary>Outcomes handed to the worker since the server started (<c>ai.experience.outcomes.noted</c>).</summary>
    public int OutcomesNoted { get; private set; }

    /// <summary>
    /// A jump a player showed (lane G6a, <see cref="ShownJumpFeed"/>): handed to the worker with the player's class and the
    /// level's judge, to be judged there against the graph and, with <paramref name="withLinks"/>, the game's links
    /// (<see cref="IExperienceLinks"/>), and written when the level has no way for it. Handed while hits and deaths are
    /// (<see cref="Gathering"/>) and only with a graph to judge it on; answers whether it was. The game's thread.
    /// </summary>
    public bool NoteShownJump(EntityHandle player, ShownJumpRecipe jump, bool withLinks)
    {
        if (!Gathering)
            return false;
        (int role, int cls) = game.Of(player);
        if (role == 0)
            return false;
        if (nav.Current?.Nav.Graph is not { } graph)
        {
            ShownJumpsUnjudged++;
            return false;
        }

        if (!ReferenceEquals(_judge?.Graph, graph))
            _judge = ShownJumpJudge.Of(graph);
        if (!_worker.ShownJump(jump, cls, _judge, withLinks ? links?.Keys : null, ServerAiSession.TickSeconds))
            return false;
        ShownJumpsNoted++;
        return true;
    }

    /// <summary>Jumps players showed handed to the worker since the server started (<c>ai.experience.jumps.noted</c>).</summary>
    public int ShownJumpsNoted { get; private set; }

    /// <summary>Jumps players showed on a level with no graph to judge them on (<c>ai.experience.jumps.unjudged</c>).</summary>
    public int ShownJumpsUnjudged { get; private set; }

    /// <summary>The jumps players showed that the worker judged <paramref name="verdict"/> since it was made.</summary>
    public long ShownJumpVerdicts(ShownJumpVerdict verdict) => _worker.ShownJumps(verdict);

    /// <summary>The last jumps the worker judged, oldest first.</summary>
    public IReadOnlyList<ShownJumpSeen> RecentShownJumps() => _worker.RecentShownJumps();

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (!Gathering || hurt.Taken <= 0 || Party(hurt.Player, entities.GetOrigin(hurt.Player)) is not { } victim)
            return;
        _worker.Damage(victim, hurt.Attacker != hurt.Player ? Party(hurt.Attacker) : null, hurt.Taken);
    }

    public void OnPlayerDied(PlayerDeath death)
    {
        if (!Gathering || Party(death.Player, death.Origin) is not { } victim)
            return;
        CombatSample? killer = death.Attacker != death.Player ? Party(death.Attacker) : null;
        _worker.Kill(victim, killer, killer is null ? 0 : game.WeaponClass(death.Attacker));
        if (holds is null)
            return;
        if (killer is not null)
            _holdTracker.Kill(entities.GetIndex(death.Attacker));
        _holdTracker.Died(entities.GetIndex(death.Player), _held);
        HandHolds();
    }

    /// <summary>Whether hits and deaths are handed over now: on, learning, a key open and a round being played.</summary>
    public bool Gathering => _path is not null && _mode.Length > 0 && learn.Value && !_roundOver;

    /// <summary>One side of a hit: a player on a side, where it stands; null for the world, a thing or a spectator.</summary>
    private CombatSample? Party(EntityHandle who) => IsPlayer(who) ? Party(who, entities.GetOrigin(who)) : null;

    private CombatSample? Party(EntityHandle who, Vector at)
    {
        if (!IsPlayer(who))
            return null;
        (int role, int cls) = game.Of(who);
        return role == 0 ? null : new CombatSample(new AiVector(at.X, at.Y, at.Z), new ExperienceContext(game.Stage, role, cls), botPlayers.IsBot(who));
    }

    private bool IsPlayer(EntityHandle who)
    {
        if (who == EntityHandle.None)
            return false;
        int index = entities.GetIndex(who);
        return index is >= 1 and < VisionPass.Slots && players.GetHandle(index) == who;
    }

    /// <summary>
    /// What the worker met is said once, on the game's thread: a store waiting on a busy file, then that it attached;
    /// and a refusal.
    /// </summary>
    private void Say()
    {
        ExperienceStatus? status = _worker.Status;
        if (status?.Waiting is { } waiting)
        {
            if (waiting != _saidWaiting)
            {
                _saidWaiting = waiting;
                log.Msg($"[SourceSharp/Ai] experience: {waiting}; the game goes on without it until it attaches\n");
            }
        }
        else if (_saidWaiting is not null && status is not null)
        {
            _saidWaiting = null;
            if (status.Refusal is null)
                log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] experience: {status.Path} attached ({status.Counts.Rows} rows for {status.Key.Map} {status.Key.Mode})\n"));
        }

        if (status?.Refusal is not { } refusal || refusal == _saidRefusal)
            return;
        _saidRefusal = refusal;
        Refusals++;
        log.Warning($"[SourceSharp/Ai] experience refused: {refusal}; this server keeps it in memory only\n");
    }

    /// <summary>Refusals said since the server started (<c>ai.experience.refusals</c>).</summary>
    public int Refusals { get; private set; }

    /// <summary>
    /// <c>/ai/experience</c>: whether the store is on and where, the key, the
    /// rows per table the store held at its last load or flush, the snapshot
    /// the brains read, the last flush and the refusal. Read from what the
    /// worker last said: the route never asks the store. <c>state</c> is off,
    /// memory (refused), waiting (a busy file retried in the background, said
    /// in <c>waiting</c>) or on.
    /// </summary>
    public string Json()
    {
        ExperienceStatus? status = _worker.Status;
        MapExperience snapshot = Slot.Current;
        ExperienceCounts rows = status?.Counts ?? default;
        return AiJson.Write(new
        {
            state = _path is null ? "off" : status is { MemoryOnly: true } ? "memory" : status is { Waiting: not null } ? "waiting" : "on",
            off = Off,
            path = _path ?? "",
            learn = learn.Value,
            half_life = halfLife.Value,
            map = status?.Key.Map ?? _map,
            checksum = status?.Key.Checksum ?? "",
            mode = status?.Key.Mode ?? _mode,
            rounds = rows.Rounds,
            rows = new { cell_stat = rows.Cells, sight_line = rows.Sights, look = rows.Looks, spot = rows.Spots, link = rows.Links, outcome = rows.Outcomes, round = rows.RoundRows },
            snapshot = new { rows = snapshot.Rows, rounds = snapshot.Rounds, hash = snapshot.ContentHash.ToString("x16", CultureInfo.InvariantCulture) },
            routes = RoutesJson(),
            flushes = _worker.Flushes,
            last_flush_seconds_ago = status?.SecondsSinceFlush ?? -1d,
            refusal = status?.Refusal ?? "",
            waiting = status?.Waiting ?? "",
            attaches = _worker.Attaches,
        });
    }

    /// <summary>The published routes: their snapshot's hash, each side's costed cells, the bytes and the build's CPU time.</summary>
    private object RoutesJson()
    {
        ExperienceRoutes r = _routeSlot.Current;
        return new
        {
            snapshot = r.Of.ContentHash.ToString("x16", CultureInfo.InvariantCulture),
            graph_cells = r.Graph?.CellCount ?? 0,
            sides = r.Roles.Select(role => new { role, cells = r.For(role)!.Costed }).ToArray(),
            cells = r.Costed,
            bytes = r.Bytes,
            build_us = Math.Round(r.BuildMicros, 1),
            links = r.LinkKeys?.Count ?? 0,
            links_shut = r.LinksShut?.Count ?? 0,
            shut = r.LinksShut is { } mask ? Enumerable.Range(0, r.LinkKeys!.Count).Where(mask.Shut).ToArray() : [],
            builds = RouteBuilds,
            pending = _routeWork is not null,
        };
    }

    /// <summary>The worker's jump verdicts (lane G6a), each with its comma.</summary>
    private string JumpVerdicts()
        => string.Concat(Enum.GetValues<ShownJumpVerdict>().Select(v => string.Create(CultureInfo.InvariantCulture,
            $"\"ai.experience.jumps.{v.ToString().ToLowerInvariant()}\":{_worker.ShownJumps(v)},")));

    /// <summary>The feed's part of <c>/ai/counters</c>, with its comma.</summary>
    public string Counters()
        => string.Create(CultureInfo.InvariantCulture,
            $"\"ai.experience.on\":{(_path is null ? 0 : 1)},\"ai.experience.memory_only\":{(_worker.Status is { MemoryOnly: true } ? 1 : 0)},\"ai.experience.waiting\":{(_worker.Status is { Waiting: not null } ? 1 : 0)},\"ai.experience.attaches\":{_worker.Attaches},\"ai.experience.frames\":{_worker.Frames},\"ai.experience.events\":{_worker.Events},"
            + $"\"ai.experience.flushes\":{_worker.Flushes},\"ai.experience.rows.merged\":{_worker.RowsMerged},\"ai.experience.loads\":{_worker.Loads},\"ai.experience.merge_failures\":{_worker.MergeFailures},"
            + $"\"ai.experience.refusals\":{Refusals},\"ai.experience.dropped\":{_worker.Dropped},\"ai.experience.faults\":{_worker.Faults},\"ai.experience.main_thread_work\":{_worker.RanOnOwner},"
            + $"\"ai.experience.snapshot.rows\":{Slot.Current.Rows},\"ai.experience.routes.sides\":{_routeSlot.Current.Roles.Count},\"ai.experience.routes.cells\":{_routeSlot.Current.Costed},"
            + $"\"ai.experience.routes.bytes\":{_routeSlot.Current.Bytes},\"ai.experience.routes.us\":{_routeSlot.Current.BuildMicros:F0},\"ai.experience.routes.builds\":{RouteBuilds},\"ai.experience.routes.faults\":{RouteFaults},"
            + $"\"ai.experience.feed.us\":{FeedMicros:F0},\"ai.experience.jumps.noted\":{ShownJumpsNoted},\"ai.experience.jumps.unjudged\":{ShownJumpsUnjudged},"
            + $"{JumpVerdicts()}"
            + $"\"ai.experience.links.noted\":{LinksNoted},\"ai.experience.spots.noted\":{SpotsNoted},\"ai.experience.outcomes.noted\":{OutcomesNoted},\"ai.experience.routes.links_shut\":{_routeSlot.Current.LinksShut?.Count ?? 0},");
}
