using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The HL2MP binding's AI session (plan_hl2mp_ai §2.1): the core's
/// <see cref="AiSession"/> over the inline transport, sized for the engine's
/// player slots. The senses publish into it; the taps push events into it.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpAiSession(Nav.IHl2mpNavigation nav, WeaponCatalog weapons, ModelDescriptors models, IEnginePlayers players,
                                   ILog log, ConVarRegistry? convars = null) : IGameLifecycle
{
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

    public AiSession Session => _session ??= new AiSession(new InlineTransport(), Recording, VisionPass.Slots, SightedCapacity);

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
        Session.World = new MemoryWorld(map, nav.Current?.Nav.Graph, TickSeconds, IdentityOf);
        log.Msg($"[SourceSharp/Ai] {mapName}: the bots know {map.Items.Length} item spawns, {map.Spawns.Length} spawns, "
                + $"{facts.All.Count(f => f.HasClip)} clip weapons ({string.Join(", ", facts.All.Where(f => f.HasClip).Select(f => $"{f.Name} {f.ClipSize}/{f.ReloadSeconds:F2}s"))})\n");
    }

    /// <summary>An opponent's identity across its lives and the maps (§5.2): its user id, which a client keeps while connected.</summary>
    private string IdentityOf(ActorId id)
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
                                IEngineFrame frame, ILog log) : IFrameLifecycle
{
    /// <summary>The window the sense cost is averaged over (§9: a 300-tick mean).</summary>
    public const int Window = 300;

    private readonly List<SensedActor> _actors = new();
    private readonly List<AiEvent> _events = new();
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
        if (now <= _lastTick)
            return;
        _lastTick = now;
        Sense(new Tick(now), frameTime > 0f ? frameTime : 0.015f);
        _us[_samples % Window] = _senseUs;
        _publishUs[_samples % Window] = _stepUs;
        _samples++;
    }

    /// <summary>One tick of the senses (the facts call it with their own clock).</summary>
    public void Sense(Tick tick, float tickSeconds)
    {
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
        _events.Clear();
        long start = Stopwatch.GetTimestamp();
        vision.Run(tick, tickSeconds, sensed, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_actors), _events);
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

    private ActorId BotIn(int slot)
    {
        foreach (SensedBot b in _sensed.AsSpan(0, _sensedCount))
        {
            if (b.Id.Slot == slot)
                return b.Id;
        }

        return ActorId.None;
    }

    /// <summary>The session and the vision serve exactly the binding's bots.</summary>
    private void Follow()
    {
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
            ai.Session.AddBot(b.Id, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ Hl2mpAiSession.BrainSalt, b.Id.Serial), levels, ai.HabitsFor(b.Name));
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
            + $"{bots.Bots.Count} bots; ai.sense.traces {vision.TracesLastTick} last tick, {vision.Traces} in all\n"));
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
