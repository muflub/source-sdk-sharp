namespace SourceSharp.Ai;

/// <summary>How a session's steps went (§2.5: every drop is counted, never silent).</summary>
public sealed class AiCounters
{
    public long EventsIn { get; internal set; }

    public long EventsProcessed { get; internal set; }

    /// <summary>Events for a slot that holds no bot.</summary>
    public long EventsDropped { get; internal set; }

    public long StepsRun { get; internal set; }

    /// <summary>Steps whose snapshot had left the ring, or that had no view for their bot (§2.5).</summary>
    public long StepsSkipped { get; internal set; }
}

/// <summary>
/// The core's per-match AI: the bots and their brains, each bot's events
/// since its last step, the snapshot ring, and each bot's latest intent
/// (plan_hl2mp_ai §2.1). The binding calls it from the main thread: events as
/// they happen, then one <see cref="Publish"/> per tick; the transport runs
/// the steps that tick's decide rule names.
/// </summary>
public sealed class AiSession : IBrainStepRunner
{
    private readonly IAiTransport _transport;
    private readonly IAiRecordSink _sink;
    private readonly Bot?[] _bots;
    private readonly BrainStepJob _job;

    public AiSession(IAiTransport transport, IAiRecordSink sink, int slots, int sightedCapacity)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sink);
        _transport = transport;
        _sink = sink;
        _bots = new Bot?[slots];
        _job = new BrainStepJob(64);
        Snapshots = new SnapshotRing(8, slots, sightedCapacity);
    }

    public SnapshotRing Snapshots { get; }

    public AiCounters Counters { get; } = new();

    /// <summary>How many bots the session holds.</summary>
    public int BotCount { get; private set; }

    /// <summary>The map the bots' memories work over (§5.0); set by the binding when a level starts, before its bots.</summary>
    public MemoryWorld? World { get; set; }

    /// <summary>
    /// Adds a bot with its own seed and skill levels; with a <see cref="World"/>,
    /// its brain remembers, its opponents' habits in <paramref name="habits"/>
    /// (kept by the caller across the bot's lives and the maps, §5.2).
    /// </summary>
    public void AddBot(ActorId id, ulong seed, ReadOnlySpan<byte> skillLevels, HabitStore? habits = null)
    {
        if (!id.IsValid)
            throw new ArgumentException("a bot needs a valid actor", nameof(id));
        if ((uint)id.Slot >= (uint)_bots.Length)
            throw new ArgumentOutOfRangeException(nameof(id), id.Slot, $"slots run 0..{_bots.Length - 1}");
        if (_bots[id.Slot] is { } held)
            throw new InvalidOperationException($"slot {id.Slot} already holds {held.Brain.Bot}");

        BotMemory? memory = null;
        if (World is { } world)
        {
            MemoryParams p = skillLevels.Length == StandardSkills.Ten.Length
                ? MemoryParams.For(new SkillProfile("session", StandardSkills.Ten, [.. skillLevels.ToArray().Select(b => (int)b)]))
                : new MemoryParams(5f, MemoryParams.HabitHalfLife);
            memory = new BotMemory(id, _bots.Length, p, world, habits ?? new HabitStore());
        }

        _bots[id.Slot] = new Bot(new Brain(id, seed, memory));
        BotCount++;
        _sink.BotAdded(id, seed, skillLevels);
    }

    /// <summary>Removes the bot; its pending events and intent go with it.</summary>
    public void RemoveBot(ActorId id)
    {
        if ((uint)id.Slot >= (uint)_bots.Length || _bots[id.Slot]?.Brain.Bot != id)
            throw new InvalidOperationException($"{id} is not in the session");
        _bots[id.Slot] = null;
        BotCount--;
        _sink.BotRemoved(id);
    }

    /// <summary>Queues an event for its bot's next step.</summary>
    public void Push(in AiEvent e)
    {
        Counters.EventsIn++;
        _sink.Event(e);
        if ((uint)e.Bot.Slot < (uint)_bots.Length && _bots[e.Bot.Slot] is { } bot && bot.Brain.Bot == e.Bot)
            bot.Pending.Add(e);
        else
            Counters.EventsDropped++;
    }

    /// <summary>
    /// Publishes the tick's snapshot (written through <see cref="Snapshots"/>)
    /// and hands the transport a step for each bot whose decide tick it is,
    /// in slot order. Never waits for a step.
    /// </summary>
    public void Publish(AiSnapshot snapshot)
    {
        Snapshots.Publish(snapshot);
        _sink.Snapshot(snapshot);

        Tick tick = snapshot.Tick;
        for (int slot = 0; slot < _bots.Length; slot++)
        {
            if (_bots[slot] is not { } bot || !DecideSchedule.IsDecideTick(tick, slot))
                continue;

            _job.Set(slot, tick, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bot.Pending));
            bot.Pending.Clear();
            _transport.Dispatch(_job, this);
        }
    }

    /// <summary>The brain in <paramref name="slot"/>, or null.</summary>
    public Brain? BrainIn(int slot) => (uint)slot < (uint)_bots.Length ? _bots[slot]?.Brain : null;

    /// <summary>The bot's newest intent, if it has one.</summary>
    public bool TryLatestIntent(int slot, out IntentCommand intent)
    {
        if ((uint)slot < (uint)_bots.Length && _bots[slot] is { HasIntent: true } bot)
        {
            intent = bot.Latest;
            return true;
        }

        intent = default;
        return false;
    }

    void IBrainStepRunner.Run(BrainStepJob job)
    {
        if (_bots[job.Slot] is not { } bot)
            return;

        if (!Snapshots.TryGet(job.Tick, out AiSnapshot snapshot) || snapshot.ViewFor(job.Slot) is not { } view
            || view.Bot != bot.Brain.Bot)
        {
            // Its events are not lost: they wait for the bot's next step.
            Counters.StepsSkipped++;
            bot.Pending.InsertRange(0, job.Events);
            return;
        }

        IntentCommand intent = bot.Brain.Step(job.Tick, view, job.Events);
        Counters.EventsProcessed += job.EventCount;
        Counters.StepsRun++;
        bot.Latest = intent;
        bot.HasIntent = true;
        _sink.Intent(intent);
    }

    private sealed class Bot(Brain brain)
    {
        public Brain Brain { get; } = brain;

        public List<AiEvent> Pending { get; } = new();

        public IntentCommand Latest { get; set; }

        public bool HasIntent { get; set; }
    }
}
