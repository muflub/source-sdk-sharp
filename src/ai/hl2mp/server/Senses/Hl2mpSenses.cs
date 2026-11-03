using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The HL2MP binding's AI session (plan_hl2mp_ai §2.1): the core's
/// <see cref="AiSession"/>, sized for the engine's player slots. The senses
/// publish into it; the taps push events into it. Its brains run on the AI's
/// worker pool (§2.7, <c>ai_threads</c>), started when a level is up and
/// stopped and joined before the level's entities go, or inline on the main
/// thread under <c>ai_sync 1</c> (a debugging switch).
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpAiSession(Nav.IHl2mpNavigation nav, WeaponCatalog weapons, ModelDescriptors models, IEnginePlayers players,
                                   ILog log, ConVarRegistry? convars = null, Skills.ISkillCurveSource? curves = null) : IGameLifecycle
{
    private readonly AiWorkerPool _pool = new();
    private readonly InlineTransport _inline = new();
    private PooledTransport? _pooled;

    // Each actor's identity for its habits (§5.2), written on the main thread
    // when the actor is first sensed and only read by the brains after.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ActorId, string> _identities = new();

    /// <summary>How many actors one view can hold sighted: every other player.</summary>
    public const int SightedCapacity = VisionPass.Slots - 1;

    /// <summary>Mixed into the match seed for a bot's brain, a stream apart from its name and its motion.</summary>
    public const ulong BrainSalt = 0x4252_4149UL;

    /// <summary>The server's tick (0.015 s, HL2MP's 66 ticks a second).</summary>
    public const float TickSeconds = 0.015f;

    private readonly Dictionary<string, HabitStore> _habits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The session's sink: nothing until <c>ai_record start</c>.</summary>
    public RecordingSwitch Recording { get; } = new();

    private AiSession? _session;

    public AiSession Session => _session ??= new AiSession(_inline, Recording, VisionPass.Slots, SightedCapacity) { CpuClock = ThreadCpuClock.Micros() };

    /// <summary>The AI's worker threads (§2.7).</summary>
    public AiWorkerPool Pool => _pool;

    /// <summary>Whether the brains run inline on the main thread (<c>ai_sync 1</c>).</summary>
    public bool Sync => Session.Transport.Inline;

    /// <summary>The worker count a level starts with: <c>ai_threads</c>, or 0 for <see cref="AiWorkerPool.DefaultThreads"/>.</summary>
    public int ThreadsWanted
    {
        get
        {
            int n = convars?.Get<AiThreadsConVar>()?.Value ?? 0;
            return n > 0 ? n : AiWorkerPool.DefaultThreads;
        }
    }

    /// <summary>
    /// Runs the brains inline (<paramref name="sync"/>) or on the pool: what
    /// is in flight finishes first (the one place the main thread waits for a
    /// step, at a debugging command), then the transport changes.
    /// </summary>
    public void SetSync(bool sync)
    {
        Session.Drain(wait: true);
        if (!sync && !_pool.Running)
            _pool.Start(ThreadsWanted);
        Session.Transport = sync ? _inline : _pooled ??= new PooledTransport(_pool);
    }

    /// <summary>Follows <c>ai_sync</c>: a change takes effect at the next tick's senses.</summary>
    public void FollowSync()
    {
        if (convars?.Get<AiSyncConVar>() is { } sync && sync.Value != Sync)
        {
            SetSync(sync.Value);
            log.Msg(sync.Value ? "[SourceSharp/Ai] the brains run inline (ai_sync 1)\n" : $"[SourceSharp/Ai] the brains run on {_pool.Threads} worker thread(s)\n");
        }
    }

    /// <summary>The habits' name for <paramref name="id"/>, known from the tick it is first sensed (main thread).</summary>
    public void KnowActor(ActorId id)
    {
        if (id.IsValid && !_identities.ContainsKey(id))
            _identities[id] = IdentityFor(id);
    }

    /// <summary>The level's pool stops and is joined before its entities go (§2.5); steps not run are dropped and counted.</summary>
    public void OnLevelShutdownPreEntity()
    {
        int discarded = _pool.Stop();
        Session.Abandon();
        _identities.Clear();
        if (discarded > 0)
            log.Msg($"[SourceSharp/Ai] the pool stopped with {discarded} lane(s) unrun\n");
    }

    public void OnShutdown()
    {
        _pool.Stop();
        Session.Abandon();
    }

    /// <summary>After the navigation (+400), before the bots are re-added.</summary>
    public int LifecycleOrder => ModStages.First + 450;

    /// <summary>
    /// The habits the bot called <paramref name="botName"/> keeps of its
    /// opponents, for the server process only (§5.2, ruling 2026-11-01):
    /// never written to disk, lost when the server exits.
    /// </summary>
    public HabitStore HabitsFor(string botName)
    {
        if (!_habits.TryGetValue(botName, out HabitStore? h))
            _habits[botName] = h = new HabitStore();
        return h;
    }

    /// <summary>The bots' knowledge of this map (§5.0): built once when the level is up.</summary>
    public void OnLevelInitPostEntity(string mapName)
    {
        WeaponKnowledge facts = Knowledge.WeaponKnowledgeBuilder.Build(weapons, w => Knowledge.WeaponKnowledgeBuilder.ReloadSeconds(models, w));
        MapKnowledge map = nav.Current is { } loaded
            ? Knowledge.MapKnowledgeBuilder.Build(mapName, loaded.Entities, weapons, facts,
                                                  convars?.Get<WeaponRespawnTimeConVar>()?.Value ?? 20f,
                                                  convars?.Get<ItemRespawnTimeConVar>()?.Value ?? 30f)
            : new MapKnowledge(mapName, [], [], facts);
        Session.World = new MemoryWorld(map, nav.Current?.Nav.Graph, TickSeconds, IdentityOf, nav.Sight);
        // The level counts its ticks from low again: the last level's snapshots go.
        Session.Snapshots.Reset();
        bool sync = convars?.Get<AiSyncConVar>()?.Value == true;
        if (!sync)
            _pool.Start(ThreadsWanted);
        Session.Transport = sync ? _inline : _pooled ??= new PooledTransport(_pool);
        log.Msg(sync ? "[SourceSharp/Ai] the brains run inline (ai_sync 1)\n" : $"[SourceSharp/Ai] the brains run on {_pool.Threads} worker thread(s)\n");
        Session.Curves = curves?.Curves;
        Session.Deciders = setup => new Trees.Hl2mpDecider(setup);
        if (curves is not null && Session.Curves is null)
            log.Warning($"[SourceSharp/Ai] {curves.Refusal}: the bots decide nothing\n");
        log.Msg($"[SourceSharp/Ai] {mapName}: the bots know {map.Items.Length} item spawns, {map.Spawns.Length} spawns, "
                + $"{facts.All.Count(f => f.HasClip)} clip weapons ({string.Join(", ", facts.All.Where(f => f.HasClip).Select(f => $"{f.Name} {f.ClipSize}/{f.ReloadSeconds:F2}s"))})\n");
    }

    /// <summary>
    /// An opponent's identity for its habits, as the main thread knew it
    /// (<see cref="KnowActor"/>): a brain asks from a worker, which never asks
    /// the engine. One not yet known is named by its actor.
    /// </summary>
    private string IdentityOf(ActorId id) => _identities.TryGetValue(id, out string? known) ? known : id.ToString();

    /// <summary>An opponent's identity across its lives and the maps (§5.2): its user id, which a client keeps while connected.</summary>
    private string IdentityFor(ActorId id)
    {
        EntityHandle p = players.GetHandle(id.Slot);
        return p == EntityHandle.None ? id.ToString() : $"user {players.GetUserId(p)}";
    }
}

/// <summary>
/// The senses on the main thread, once a tick after the entities think
/// (plan_hl2mp_ai §2.1, §4): keeps the session's bots those the binding plays,
/// runs the <see cref="VisionPass"/>, pushes its sight events, builds each
/// bot's view with the <see cref="SnapshotBuilder"/> and publishes the tick.
/// Times itself: <see cref="SenseMeanUs"/> over the last 300 ticks is the
/// counter <c>ai.sense.us</c> (§9 budgets: the vision pass, the item pass and
/// the views), <see cref="PublishMeanUs"/> the brains' steps and recording.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpSenses(Hl2mpBots bots, Hl2mpAiSession ai, VisionPass vision, SnapshotBuilder builder, ActorDirectory directory,
                                IEngineFrame frame, ILog log, ReferenceRecorder reference, IBotInventory? inventory = null, WeaponCatalog? weapons = null)
    : IFrameLifecycle
{
    private readonly int[] _held = new int[VisionPass.Slots];

    /// <summary>The window the sense cost is averaged over (§9: a 300-tick mean).</summary>
    public const int Window = 300;

    private readonly List<NearbyPlayer> _nearby = new();

    private readonly List<SensedActor> _actors = new();
    private readonly List<AiEvent> _events = new();
    private readonly List<AiEvent> _observed = new();
    private readonly SensedBot[] _sensed = new SensedBot[VisionPass.Slots];
    private readonly Dictionary<int, ActorId> _inSession = new();
    private readonly double[] _us = new double[Window];
    private readonly double[] _publishUs = new double[Window];
    private double _senseUs, _stepUs;
    private readonly ActorActivity[] _activity = new ActorActivity[VisionPass.Slots];
    private int _samples;
    private int _sensedCount;
    private Func<int, ActorId>? _botIn;
    private int _lastTick = -1;

    /// <summary>After the bots' commands and the entities' think.</summary>
    public int FrameOrder => ModStages.First + 600;

    public AiSession Session => ai.Session;

    /// <summary>Whether sight changes are written to the console (<c>ai_sense_log</c>).</summary>
    public bool LogSight { get; set; }

    /// <summary>The mean main-thread cost of a tick's senses over the last <see cref="Window"/> ticks, in microseconds (<c>ai.sense.us</c>).</summary>
    public double SenseMeanUs => _samples == 0 ? 0 : _us.AsSpan(0, Math.Min(_samples, Window)).ToArray().Average();

    /// <summary>
    /// The mean cost of publishing the tick (the inline transport's brain
    /// steps and, while <c>ai_record</c> runs, the recording) over the window:
    /// <c>ai.publish.us</c>, kept apart from the senses' <c>ai.sense.us</c>.
    /// </summary>
    public double PublishMeanUs => _samples == 0 ? 0 : _publishUs.AsSpan(0, Math.Min(_samples, Window)).ToArray().Average();

    /// <summary>The worst tick in the window, in microseconds.</summary>
    public double SenseMaxUs => _samples == 0 ? 0 : _us.AsSpan(0, Math.Min(_samples, Window)).ToArray().Max();

    /// <summary>Ticks timed so far.</summary>
    public int SenseTicks => _samples;

    /// <summary>What a client sees the actor in <paramref name="slot"/>'s animation doing this tick (the reload tap fills it, §4.5).</summary>
    public void SetActivity(int slot, ActorActivity activity)
    {
        if ((uint)slot < (uint)_activity.Length)
            _activity[slot] = activity;
    }

    /// <summary>The actor living in <paramref name="slot"/> at the last pass, or none.</summary>
    public ActorId ActorAt(int slot) => vision.ActorIn(slot);

    public ActorActivity ActivityOf(int slot) => (uint)slot < (uint)_activity.Length ? _activity[slot] : ActorActivity.None;

    public void OnPostThink(float frameTime)
    {
        int now = frame.TickCount();
        if (!IsNewTick(now, _lastTick))
            return;
        _lastTick = now;
        Sense(new Tick(now), frameTime > 0f ? frameTime : 0.015f);
        _us[_samples % Window] = _senseUs;
        _publishUs[_samples % Window] = _stepUs;
        _poolBusy[_samples % Window] = ai.Pool.BusyMicros;
        _samples++;
    }

    private readonly double[] _poolBusy = new double[Window];

    /// <summary>
    /// The workers' busy time over the last <see cref="Window"/> ticks, as
    /// microseconds a second (<c>ai.pool.us</c>; §9's ceiling is a quarter of
    /// one core, 250,000).
    /// </summary>
    public double PoolUsPerSecond
    {
        get
        {
            int n = Math.Min(_samples, Window);
            if (n < 2)
                return 0;
            double newest = _poolBusy[(_samples - 1) % Window], oldest = _poolBusy[(_samples - n) % Window];
            return (newest - oldest) / ((n - 1) * Hl2mpAiSession.TickSeconds);
        }
    }

    /// <summary>
    /// Whether the frame's tick is one the senses have not run: any other than
    /// the last. A new level starts its tick count again, lower than the last
    /// level's; read as "not newer", the senses stood still on every level
    /// after the first until its tick passed the old one (the live gate's
    /// second pass: bots added after two map changes never decided).
    /// </summary>
    public static bool IsNewTick(int now, int last) => now != last;

    /// <summary>One tick of the senses (the facts call it with their own clock).</summary>
    public void Sense(Tick tick, float tickSeconds)
    {
        ai.FollowSync();
        Follow();
        int n = 0;
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p != EntityHandle.None)
                _sensed[n++] = new SensedBot(b.Id, p, VisionCurves.For(b.Skills));
        }

        _sensedCount = n;
        ReadOnlySpan<SensedBot> sensed = _sensed.AsSpan(0, n);
        directory.Living(_actors, _botIn ??= BotIn);
        foreach (SensedActor a in _actors)
            ai.KnowActor(a.Id);
        _events.Clear();
        _observed.Clear();
        ReadOnlySpan<SensedActor> living = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_actors);
        ReadOnlySpan<SensedBot> observers = reference.Observers(living, _botIn);
        long start = Stopwatch.GetTimestamp();
        vision.Run(tick, tickSeconds, sensed, living, _events, observers, _observed);
        _nearby.Clear();
        foreach (SensedActor a in _actors)
        {
            if (vision.ReadOf(a.Id.Slot) is { } read)
                _nearby.Add(new NearbyPlayer(a.Id.Slot, read.Origin));
        }

        bots.Driver.Players = _nearby;
        for (int i = 0; i < _activity.Length; i++)
        {
            if (!vision.ActorIn(i).IsValid)
                _activity[i] = ActorActivity.None;   // gone or dead: nothing to draw
        }
        if (ai.Session.World is { } world)
            vision.RunItems(tick, sensed, world.Map.Items, _events);
        foreach (AiEvent e in _events)
        {
            ai.Session.Push(e);
            if (LogSight && e.Kind is AiEventKind.SightGained or AiEventKind.SightLost)
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] {e.Bot} {(e.Kind == AiEventKind.SightGained ? "sees" : "lost sight of")} {e.Other} at ({e.Position.X:F0} {e.Position.Y:F0} {e.Position.Z:F0}), {e.Tick}\n"));
        }

        Holdings(tick, sensed);
        reference.Write(tick, living, _observed);
        AiSnapshot snapshot = ai.Session.Snapshots.BeginWrite(tick);
        builder.Build(snapshot, sensed, vision, ActivityOf);
        _senseUs = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        long publish = Stopwatch.GetTimestamp();
        ai.Session.Publish(snapshot);
        _stepUs = Stopwatch.GetElapsedTime(publish).TotalMicroseconds;

        // A shot's flash lasts the tick it was seen in.
        for (int i = 0; i < _activity.Length; i++)
            _activity[i] &= ~ActorActivity.Firing;
    }

    /// <summary>
    /// Each bot's weapons as its HUD's weapon selection shows them, told when
    /// they change (an <see cref="AiEventKind.Holdings"/> mask of catalog kinds).
    /// </summary>
    private void Holdings(Tick tick, ReadOnlySpan<SensedBot> sensed)
    {
        if (inventory is null || weapons is null)
            return;
        foreach (SensedBot b in sensed)
        {
            int mask = 0;
            foreach (string held in inventory.Held(b.Player))
            {
                int kind = weapons.IndexOf(held);
                if (kind is >= 0 and < 31)
                    mask |= 1 << kind;
            }

            if ((uint)b.Id.Slot < (uint)_held.Length && _held[b.Id.Slot] != mask)
            {
                _held[b.Id.Slot] = mask;
                ai.Session.Push(AiEvent.Holdings(b.Id, tick, mask));
            }
        }
    }

    private ActorId BotIn(int slot)
    {
        foreach (SensedBot b in _sensed.AsSpan(0, _sensedCount))
        {
            if (b.Id.Slot == slot)
                return b.Id;
        }

        return ActorId.None;
    }

    /// <summary>
    /// An event for the session, after the session has caught up with the
    /// binding's bots: a bot hears and is hit from the tick it joins, which is
    /// before the next senses pass would have added it (its spawn's sounds were
    /// dropped that way).
    /// </summary>
    public void Push(in AiEvent e)
    {
        Follow();
        ai.Session.Push(e);
    }

    private int _followed = -1;

    /// <summary>The session and the vision serve exactly the binding's bots.</summary>
    private void Follow()
    {
        if (_followed == bots.Version)
            return;
        _followed = bots.Version;

        foreach ((int slot, ActorId id) in _inSession.ToArray())
        {
            if (bots.Bots.Any(b => b.Id == id))
                continue;
            ai.Session.RemoveBot(id);
            vision.Remove(slot);
            _inSession.Remove(slot);
        }

        foreach (Hl2mpBot b in bots.Bots)
        {
            if (_inSession.ContainsKey(b.Id.Slot))
                continue;
            byte[] levels = [.. b.Skills.Levels.Select(l => (byte)l)];
            ai.Session.AddBot(b.Id, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ Hl2mpAiSession.BrainSalt, b.SeedIndex), levels, ai.HabitsFor(b.Name));
            vision.Add(b.Id);
            _inSession[b.Id.Slot] = b.Id;
        }
    }
}

/// <summary><c>ai_sense_log 0|1</c>: write each bot's sight changes to the console.</summary>
[ConCommand("ai_sense_log", Help = "ai_sense_log 0|1: print each bot's sight gained and lost", Flags = ConVarFlags.Cheat)]
public sealed class AiSenseLogCommand(Hl2mpSenses senses, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        senses.LogSight = args.Trim() != "0";
        log.Msg($"[SourceSharp/Ai] ai_sense_log {(senses.LogSight ? 1 : 0)}\n");
    }
}

/// <summary>
/// <c>ai_sense_cost</c>: the senses' main-thread cost, <c>ai.sense.us</c>, as a
/// 300-tick mean and worst tick, with the traces the last tick spent.
/// </summary>
[ConCommand("ai_sense_cost", Help = "ai_sense_cost: the vision pass's main-thread cost (300-tick mean)")]
public sealed class AiSenseCostCommand(Hl2mpSenses senses, VisionPass vision, Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
        => log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] ai.sense.us mean {senses.SenseMeanUs:F1} max {senses.SenseMaxUs:F1} over {Math.Min(senses.SenseTicks, Hl2mpSenses.Window)} ticks; ai.publish.us mean {senses.PublishMeanUs:F1}; "
            + $"{bots.Bots.Count} bots; ai.sense.traces {vision.TracesLastTick} last tick, {vision.Traces} in all; "
            + $"ai.brain.us {senses.Session.Counters.BrainMicrosMean:F1} per decide, worst {senses.Session.Counters.BrainMicrosMax:F1}, over {senses.Session.Counters.StepsRun} decides\n"));
}

/// <summary><c>ai_vision_traces_per_tick N</c>: the vision pass's trace budget (§4.1; default 32).</summary>
[ConCommand("ai_vision_traces_per_tick", Help = "ai_vision_traces_per_tick N: the vision pass's line traces a tick (default 32)", Flags = ConVarFlags.Cheat)]
public sealed class AiVisionTracesCommand(VisionPass vision, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        if (int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0)
            vision.TracesPerTick = n;
        log.Msg($"[SourceSharp/Ai] ai_vision_traces_per_tick {vision.TracesPerTick}\n");
    }
}
