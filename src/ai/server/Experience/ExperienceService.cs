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
                                      IExperienceStores? stores = null)
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

    /// <summary>After the session built the level's world (First + 450), which holds <see cref="Slot"/>.</summary>
    public int LifecycleOrder => ModStages.First + 460;

    /// <summary>After the senses and the taps (First + 600 to 655): the last of the AI's frame members.</summary>
    public int FrameOrder => ModStages.First + 660;

    /// <summary>After the combat tap (First + 900).</summary>
    public int HookOrder => ModStages.First + 910;

    /// <summary>Where the brains read the map's experience: the session puts it in the level's world. Empty while off and until the first load lands.</summary>
    public ExperienceSlot Slot { get; } = new();

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
            _worker.Close(halfLife.Value);
        Say();
        Slot.Current = MapExperience.Empty;
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
        if (_worker.TakeLoaded() is { } fresh)
        {
            Slot.Current = fresh;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[SourceSharp/Ai] experience: {fresh.Key.Map} {fresh.Key.Mode}: {fresh.Rows} rows over {fresh.Rounds} rounds (hash {fresh.ContentHash:x16})\n"));
        }

        Say();
        int tick = frame.TickCount();
        if (_lastSample >= 0 && tick >= _lastSample && tick - _lastSample < SampleTicks)
            return;
        double seconds = _lastSample < 0 || tick < _lastSample ? 0d : Math.Min(MaxSampleSeconds, (tick - _lastSample) * (double)ServerAiSession.TickSeconds);
        _lastSample = tick;

        // The mode names the key: the level's first sample opens it, and a mode switched under the level opens the next.
        string mode = game.Mode;
        if (mode != _mode)
        {
            _mode = mode;
            _roundOver = game.RoundOver;
            if (mode.Length > 0)
                _worker.Open(_path, _map, mode, _checksum, _bsp, halfLife.Value);
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
                _worker.RoundEnd(game.WinningRole, halfLife.Value);
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

    /// <summary>Every living player's place, view and context, handed to the worker.</summary>
    private void Sample(double seconds)
    {
        directory.Living(_living, _ => ActorId.None);
        int stage = game.Stage;
        var samples = new ActorSample[_living.Count];
        int n = 0;
        foreach (SensedActor actor in _living)
        {
            (int role, int cls) = game.Of(actor.Player);
            if (role == 0)
                continue;
            Vector at = entities.GetOrigin(actor.Player), view = entities.GetEyeAngles(actor.Player);
            samples[n++] = new ActorSample(new AiVector(at.X, at.Y, at.Z), view.Y, view.X, new ExperienceContext(stage, role, cls), Alive: true, botPlayers.IsBot(actor.Player));
        }

        if (n < samples.Length)
            Array.Resize(ref samples, n);
        _worker.Frame(samples, seconds);
    }

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
    }

    /// <summary>Whether hits and deaths are handed over now: on, learning, a key open and a round being played.</summary>
    private bool Gathering => _path is not null && _mode.Length > 0 && learn.Value && !_roundOver;

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

    /// <summary>A refusal the worker met is said once, on the game's thread.</summary>
    private void Say()
    {
        if (_worker.Status?.Refusal is not { } refusal || refusal == _saidRefusal)
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
    /// worker last said: the route never asks the store.
    /// </summary>
    public string Json()
    {
        ExperienceStatus? status = _worker.Status;
        MapExperience snapshot = Slot.Current;
        ExperienceCounts rows = status?.Counts ?? default;
        return AiJson.Write(new
        {
            state = _path is null ? "off" : status is { MemoryOnly: true } ? "memory" : "on",
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
            flushes = _worker.Flushes,
            last_flush_seconds_ago = status?.SecondsSinceFlush ?? -1d,
            refusal = status?.Refusal ?? "",
        });
    }

    /// <summary>The feed's part of <c>/ai/counters</c>, with its comma.</summary>
    public string Counters()
        => string.Create(CultureInfo.InvariantCulture,
            $"\"ai.experience.on\":{(_path is null ? 0 : 1)},\"ai.experience.memory_only\":{(_worker.Status is { MemoryOnly: true } ? 1 : 0)},\"ai.experience.frames\":{_worker.Frames},\"ai.experience.events\":{_worker.Events},"
            + $"\"ai.experience.flushes\":{_worker.Flushes},\"ai.experience.rows.merged\":{_worker.RowsMerged},\"ai.experience.loads\":{_worker.Loads},\"ai.experience.merge_failures\":{_worker.MergeFailures},"
            + $"\"ai.experience.refusals\":{Refusals},\"ai.experience.dropped\":{_worker.Dropped},\"ai.experience.faults\":{_worker.Faults},\"ai.experience.main_thread_work\":{_worker.RanOnOwner},"
            + $"\"ai.experience.snapshot.rows\":{Slot.Current.Rows},");
}
