namespace SourceSharp.Ai;

/// <summary>How a session's steps went (§2.5: every drop is counted, never silent). Written on the main thread only.</summary>
public sealed class AiCounters
{
    public long EventsIn { get; internal set; }

    public long EventsProcessed { get; internal set; }

    /// <summary>Events for a slot that holds no bot.</summary>
    public long EventsDropped { get; internal set; }

    public long StepsRun { get; internal set; }

    /// <summary>
    /// Steps not run (§2.5): no view for the bot at their tick, or left
    /// behind by a late pool (a lane holding <see cref="AiSession.LaneDepth"/>
    /// unstarted steps drops its oldest), or handed over when a level ended.
    /// Their events go to the bot's next step.
    /// </summary>
    public long StepsSkipped { get; internal set; }

    /// <summary>Steps whose brain threw; the bot keeps its last intent.</summary>
    public long StepsFaulted { get; internal set; }

    /// <summary>The last fault's message, or null.</summary>
    public string? LastFault { get; internal set; }

    /// <summary>The wall time of every brain step run, in microseconds (§9 budgets: <c>ai.brain.us</c> per decide); a counter, never an input.</summary>
    public double BrainMicros { get; internal set; }

    /// <summary>The longest brain step so far, in microseconds.</summary>
    public double BrainMicrosMax { get; internal set; }

    /// <summary>The mean brain step, in microseconds.</summary>
    public double BrainMicrosMean => StepsRun == 0 ? 0 : BrainMicros / StepsRun;

    /// <summary>The CPU time of every brain step run on the thread that ran it, in microseconds, when the session has a <see cref="AiSession.CpuClock"/>.</summary>
    public double BrainCpuMicros { get; internal set; }

    /// <summary>The mean brain step's CPU time (<c>ai.brain.cpu.us</c>): the wall time less the time its thread was not running.</summary>
    public double BrainCpuMicrosMean => StepsRun == 0 ? 0 : BrainCpuMicros / StepsRun;

    /// <summary>Decides not run because the session was held for stepping (<c>ai_step</c>).</summary>
    public long StepsHeld { get; internal set; }

    /// <summary>Callouts re-published to a teammate's lane (plan_bots_teams_tf2 B.4), one per receiver.</summary>
    public long CalloutsDelivered { get; internal set; }

    /// <summary>Ticks on which the main thread waited for a step old enough that its callouts were due (B.4's determinism).</summary>
    public long CalloutWaits { get; internal set; }

    /// <summary>Steps handed over and not yet taken back (a pool's backlog).</summary>
    public int InFlight { get; internal set; }

    /// <summary>The most steps ever in flight at once.</summary>
    public int InFlightMax { get; internal set; }

    /// <summary>Forgets the brain-step times (a fresh window for a measurement).</summary>
    public void ResetBrainTimes()
    {
        BrainCpuMicros = 0;
        BrainMicros = 0;
        BrainMicrosMax = 0;
        StepsRun = 0;
    }
}

/// <summary>
/// The core's per-match AI: the bots and their brains, each bot's events
/// since its last step, the snapshot ring, and each bot's latest intent
/// (plan_hl2mp_ai §2.1). The binding calls it from the main thread: events as
/// they happen, then one <see cref="Publish"/> per tick, which hands the
/// transport a step for each bot whose decide tick it is.
///
/// <para>
/// THE HAND-OFF (§2.6, §2.7). A step's inputs are fixed when it is handed
/// over: its own copy of the bot's view at its tick and the events since the
/// bot's last step. Each bot has a lane, and its steps run in the lane one
/// after another (step N+1 never starts before N is done), on whichever
/// thread; brains share nothing mutable. Results come back to the main
/// thread in the order the steps were handed over, (tick, slot), never by
/// which finished first: <see cref="Drain"/> takes the finished ones from the
/// front and stops at the first that is not. So the intent stream is the same
/// at any thread count, inline or pooled; only when an intent arrives varies.
/// </para>
/// </summary>
public sealed class AiSession
{
    /// <summary>How many unstarted steps a lane holds; past it the oldest is skipped (§2.5: a step whose snapshot left the 8-tick ring).</summary>
    public const int LaneDepth = 8;

    private readonly IAiRecordSink _sink;
    private readonly Lane?[] _lanes;
    private readonly Queue<BrainStepJob> _handed = new();
    private readonly int _sightedCapacity;
    private readonly List<ScheduledCallout> _callouts = new();
    private readonly List<SpokenCallout> _spoken = new();
    private bool _teamed;

    /// <summary>
    /// The shortest delay a callout may have (plan_bots_teams_tf2 B.4: 400 ms at the
    /// 15 ms tick). The session holds this as the bound that keeps re-publishing the
    /// same at any thread count: before a tick's deliveries, every step at least this
    /// many ticks old has been taken back, so every callout due by then is known.
    /// </summary>
    public const int CalloutMinDelayTicks = 26;

    public AiSession(IAiTransport transport, IAiRecordSink sink, int slots, int sightedCapacity)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sink);
        Transport = transport;
        _sink = sink;
        _lanes = new Lane?[slots];
        _sightedCapacity = sightedCapacity;
        Snapshots = new SnapshotRing(8, slots, sightedCapacity);
    }

    /// <summary>The running thread's CPU time in microseconds, or null: each step's CPU time is then not measured. A counter, never an input.</summary>
    public Func<double>? CpuClock { get; set; }

    /// <summary>Where steps run. Changed only while no step is in flight (after <see cref="Drain"/> with wait, or <see cref="Abandon"/>).</summary>
    public IAiTransport Transport { get; set; }

    public SnapshotRing Snapshots { get; }

    public AiCounters Counters { get; } = new();

    /// <summary>How many bots the session holds.</summary>
    public int BotCount { get; private set; }

    /// <summary>
    /// Held for stepping (<c>ai_step</c>, §7.3's live scrubbing): a tick hands
    /// over no step unless <see cref="StepTicks"/> lets it, and a held tick's
    /// snapshot is not recorded, so a replay steps exactly what ran.
    /// </summary>
    public bool Stepping { get; set; }

    /// <summary>While <see cref="Stepping"/>, how many more ticks may hand their decides over.</summary>
    public int StepTicks { get; set; }

    /// <summary>The map the bots' memories work over (§5.0); set by the binding when a level starts, before its bots.</summary>
    public MemoryWorld? World { get; set; }

    /// <summary>The game's skill curves (§10), read by the binding; null gives a bot no decision parameters.</summary>
    public SkillCurves? Curves { get; set; }

    /// <summary>The game's decision layer per bot (its trees), or null: brains hold.</summary>
    public Func<BrainSetup, IDecider?>? Deciders { get; set; }

    /// <summary>
    /// Adds a bot with its own seed and skill levels; with a <see cref="World"/>,
    /// its brain remembers, its opponents' habits in <paramref name="habits"/>
    /// (kept by the caller across the bot's lives and the maps, §5.2).
    /// </summary>
    public void AddBot(ActorId id, ulong seed, ReadOnlySpan<byte> skillLevels, HabitStore? habits = null)
    {
        if (!id.IsValid)
            throw new ArgumentException("a bot needs a valid actor", nameof(id));
        if ((uint)id.Slot >= (uint)_lanes.Length)
            throw new ArgumentOutOfRangeException(nameof(id), id.Slot, $"slots run 0..{_lanes.Length - 1}");
        if (_lanes[id.Slot] is { } held)
            throw new InvalidOperationException($"slot {id.Slot} already holds {held.Brain.Bot}");

        BotMemory? memory = null;
        // Ten levels, or eleven with Teamwork (plan_bots_teams_tf2 B.7): a recording made before teams has ten.
        bool skilled = skillLevels.Length == StandardSkills.Ten.Length || skillLevels.Length == StandardSkills.Eleven.Length;
        SkillProfile? profile = skilled
            ? new SkillProfile("session", skillLevels.Length == StandardSkills.Ten.Length ? StandardSkills.Ten : StandardSkills.Eleven,
                               [.. skillLevels.ToArray().Select(b => (int)b)])
            : null;
        if (World is { } world)
        {
            MemoryParams p = profile is not null
                ? MemoryParams.For(profile)
                : new MemoryParams(5f, MemoryParams.HabitHalfLife);
            memory = new BotMemory(id, _lanes.Length, p, world, habits ?? new HabitStore());
        }

        DecisionParams? decisions = Curves is { } curves && profile is not null ? curves.For(profile) : null;
        IDecider? decider = memory is not null && decisions is { } dp && Deciders is { } make
            ? make(new BrainSetup(id, seed, memory, dp, World!.TickSeconds))
            : null;
        _lanes[id.Slot] = new Lane(new Brain(id, seed, memory, decisions, decider), _sightedCapacity) { Levels = skillLevels.ToArray() };
        BotCount++;
        _sink.BotAdded(id, seed, skillLevels);
        if (decider is not null)
            _sink.DecisionTree(decider.NodeNames, decider.NodeBranches);
    }

    /// <summary>
    /// Removes the bot; its pending events and intent go with it. Steps it
    /// already handed over still run (as they had, inline, before the
    /// removal) and are recorded, but set no intent.
    /// </summary>
    public void RemoveBot(ActorId id)
    {
        if ((uint)id.Slot >= (uint)_lanes.Length || _lanes[id.Slot]?.Brain.Bot != id)
            throw new InvalidOperationException($"{id} is not in the session");
        _lanes[id.Slot] = null;
        BotCount--;
        _sink.BotRemoved(id);
    }

    /// <summary>
    /// Tells the sink every bot already in the session, in slot order, as
    /// though added now: a recording started mid-game (a human reference
    /// session, §3.4.4) names its bots and their seeds and skills. Their
    /// brains are not new, so such a recording measures; it does not replay.
    /// </summary>
    public void AnnounceBots()
    {
        foreach (Lane? lane in _lanes)
        {
            if (lane is null)
                continue;
            _sink.BotAdded(lane.Brain.Bot, lane.Brain.Seed, lane.Levels);
            if (lane.Brain.Decider is { } decider)
                _sink.DecisionTree(decider.NodeNames, decider.NodeBranches);
        }
    }

    /// <summary>Queues an event for its bot's next step.</summary>
    public void Push(in AiEvent e)
    {
        Counters.EventsIn++;
        _sink.Event(e);
        if ((uint)e.Bot.Slot < (uint)_lanes.Length && _lanes[e.Bot.Slot] is { } lane && lane.Brain.Bot == e.Bot)
            lane.Pending.Add(e);
        else
            Counters.EventsDropped++;
    }

    /// <summary>
    /// Publishes the tick's snapshot (written through <see cref="Snapshots"/>)
    /// and hands the transport a step for each bot whose decide tick it is,
    /// in slot order; then takes back what has finished. Never waits for a step.
    /// </summary>
    public void Publish(AiSnapshot snapshot)
    {
        Snapshots.Publish(snapshot);
        bool held = Stepping && StepTicks <= 0;
        if (!held)
            _sink.Snapshot(snapshot);

        Tick tick = snapshot.Tick;
        Callouts(snapshot);
        for (int slot = 0; slot < _lanes.Length; slot++)
        {
            if (_lanes[slot] is not { } lane || !DecideSchedule.IsDecideTick(tick, slot))
                continue;

            if (held)
            {
                // Its events wait for the bot's next step that runs.
                Counters.StepsHeld++;
                continue;
            }

            if (snapshot.ViewFor(slot) is not { } view || view.Bot != lane.Brain.Bot)
            {
                // Its events are not lost: they wait for the bot's next step.
                Counters.StepsSkipped++;
                continue;
            }

            BrainStepJob job = lane.Take();
            job.Set(lane, tick, view, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(lane.Pending), CpuClock);
            lane.Pending.Clear();
            Hand(lane, job);
        }

        if (Stepping && !held && StepTicks > 0)
            StepTicks--;
        Drain();
    }

    /// <summary>
    /// The team layer (plan_bots_teams_tf2 B.4): each lane's team as its newest view
    /// shows it; once any bot has been on a team, the steps old enough to have said
    /// something due now are taken back first (waiting if a pool is that far behind),
    /// then every callout due by this tick goes to each lane on the speaker's team but
    /// the speaker's own, in (due tick, spoken tick, slot) order. Its inputs are the
    /// snapshots and the steps' intents, so the stream is the same at any thread count.
    /// </summary>
    private void Callouts(AiSnapshot snapshot)
    {
        Tick tick = snapshot.Tick;
        for (int slot = 0; slot < _lanes.Length; slot++)
        {
            if (_lanes[slot] is { } lane && snapshot.ViewFor(slot) is { } view && view.Bot == lane.Brain.Bot)
            {
                lane.Team = view.Self.Team;
                _teamed |= TeamRule.IsGameTeam(lane.Team);
            }
        }

        if (!_teamed)
            return;
        DrainThrough(new Tick(tick.Value - CalloutMinDelayTicks));
        if (_callouts.Count == 0)
            return;
        _callouts.Sort(static (a, b) => a.Due != b.Due ? a.Due.Value.CompareTo(b.Due.Value)
                                     : a.Spoken != b.Spoken ? a.Spoken.Value.CompareTo(b.Spoken.Value)
                                     : a.Speaker.Slot.CompareTo(b.Speaker.Slot));
        int kept = 0;
        for (int i = 0; i < _callouts.Count; i++)
        {
            ScheduledCallout c = _callouts[i];
            if (c.Due > tick)
            {
                _callouts[kept++] = c;
                continue;
            }

            foreach (Lane? lane in _lanes)
            {
                if (lane is null || lane.Brain.Bot == c.Speaker || !TeamRule.Friends(lane.Team, c.Say.Team))
                    continue;
                AiEvent e = AiEvent.Callout(lane.Brain.Bot, c.Due, c.Speaker, c.Say) with { Velocity = new AiVector(c.Spoken.Value, 0f, 0f) };
                Counters.EventsIn++;
                Counters.CalloutsDelivered++;
                _sink.Event(e);
                lane.Pending.Add(e);
            }
        }

        _callouts.RemoveRange(kept, _callouts.Count - kept);
    }

    /// <summary>Takes back every step handed over at or before <paramref name="through"/>, waiting for one that has not finished.</summary>
    private void DrainThrough(Tick through)
    {
        Drain();
        bool waited = false;
        while (_handed.TryPeek(out BrainStepJob? job) && job.Tick <= through)
        {
            if (job.State is JobState.Queued or JobState.Running)
            {
                if (job.Lane.Start() && !Transport.Dispatch(job.Lane))
                {
                    job.Lane.Idle();
                    throw new InvalidOperationException("a step waits on a transport that runs nothing");
                }

                SpinWait.SpinUntil(() => job.State is not (JobState.Queued or JobState.Running));
                waited = true;
            }

            Drain();
        }

        if (waited)
            Counters.CalloutWaits++;
    }

    /// <summary>
    /// The callouts spoken since the last call that asked for a <c>say_team</c> line
    /// (<see cref="SayIntent.Text"/>), for the binding's chat: main thread, in the
    /// order their steps were taken back.
    /// </summary>
    public IReadOnlyList<SpokenCallout> TakeSpoken()
    {
        if (_spoken.Count == 0)
            return [];
        SpokenCallout[] taken = [.. _spoken];
        _spoken.Clear();
        return taken;
    }

    /// <summary>Callouts said and not yet due.</summary>
    public int CalloutsInFlight => _callouts.Count;

    private readonly record struct ScheduledCallout(Tick Due, Tick Spoken, ActorId Speaker, SayIntent Say);

    private void Hand(Lane lane, BrainStepJob job)
    {
        _handed.Enqueue(job);
        Counters.InFlight++;
        Counters.InFlightMax = Math.Max(Counters.InFlightMax, Counters.InFlight);
        bool start = lane.Queue(job, out BrainStepJob? dropped);
        if (dropped is not null)
            Counters.StepsSkipped++;
        if (start && !Transport.Dispatch(lane))
            lane.Idle();
    }

    /// <summary>
    /// Takes back the finished steps from the front of the hand-over order, in
    /// that order: each one's intent becomes its bot's latest (if the bot is
    /// still the slot's) and goes to the recording, its time to the counters.
    /// Stops at the first unfinished step. With <paramref name="wait"/> it
    /// waits for every step handed over (the facts and the replay tool; never
    /// the game).
    /// </summary>
    public void Drain(bool wait = false)
    {
        while (_handed.TryPeek(out BrainStepJob? job))
        {
            JobState state = job.State;
            if (state is JobState.Queued or JobState.Running)
            {
                if (!wait)
                    return;
                if (job.Lane.Start() && !Transport.Dispatch(job.Lane))
                {
                    job.Lane.Idle();
                    throw new InvalidOperationException("a step waits on a transport that runs nothing");
                }

                SpinWait.SpinUntil(() => job.State is not (JobState.Queued or JobState.Running));
                continue;
            }

            _handed.Dequeue();
            Counters.InFlight--;
            Lane lane = job.Lane;
            if (state == JobState.Done)
            {
                double us = job.Micros;
                Counters.BrainMicros += us;
                Counters.BrainCpuMicros += job.CpuMicros;
                if (us > Counters.BrainMicrosMax)
                    Counters.BrainMicrosMax = us;
                Counters.EventsProcessed += job.EventCount;
                Counters.StepsRun++;
                if (_lanes[lane.Brain.Bot.Slot] == lane)
                {
                    lane.Latest = job.Intent;
                    lane.HasIntent = true;
                    lane.LastDoing = job.Doing;
                }

                _sink.Intent(job.Intent);
                // Scheduled whether or not the speaker is still in its slot: a removal's
                // timing against the take-back varies with the threads, the words said do not.
                if (job.Intent.Say is { Says: true } say)
                {
                    _callouts.Add(new ScheduledCallout(job.Tick + Math.Max(CalloutMinDelayTicks, say.DelayTicks), job.Tick, job.Intent.Bot, say));
                    if (say.Text)
                        _spoken.Add(new SpokenCallout(job.Intent.Bot, say, job.Tick));
                }
            }
            else if (state == JobState.Faulted)
            {
                Counters.StepsFaulted++;
                Counters.LastFault = job.Fault;
            }

            lane.Return(job);
        }
    }

    /// <summary>
    /// The level is over and the transport has stopped (its pool joined):
    /// every step handed over and not run is dropped and counted, and the
    /// lanes are idle again.
    /// </summary>
    public void Abandon()
    {
        Drain();
        while (_handed.TryDequeue(out BrainStepJob? job))
        {
            Counters.InFlight--;
            if (job.State is JobState.Queued or JobState.Running)
                Counters.StepsSkipped++;
        }

        foreach (Lane? lane in _lanes)
            lane?.Reset();
        _callouts.Clear();
        _spoken.Clear();
        _teamed = false;
    }

    /// <summary>
    /// The brain in <paramref name="slot"/>, or null. Read it only while no
    /// step of it can run: inline, after <see cref="Drain"/> with wait, or
    /// through <see cref="TryReadBrain"/>.
    /// </summary>
    public Brain? BrainIn(int slot) => (uint)slot < (uint)_lanes.Length ? _lanes[slot]?.Brain : null;

    /// <summary>
    /// <paramref name="read"/> over the brain in <paramref name="slot"/> while
    /// none of its steps runs, without waiting: false when one runs now (ask
    /// again) or there is no bot.
    /// </summary>
    public bool TryReadBrain<T>(int slot, Func<Brain, T> read, out T value)
    {
        ArgumentNullException.ThrowIfNull(read);
        value = default!;
        if ((uint)slot >= (uint)_lanes.Length || _lanes[slot] is not { } lane)
            return false;
        if (!Monitor.TryEnter(lane.BrainLock))
            return false;
        try
        {
            value = read(lane.Brain);
            return true;
        }
        finally
        {
            Monitor.Exit(lane.BrainLock);
        }
    }

    /// <summary>The bot's newest intent, if it has one.</summary>
    public bool TryLatestIntent(int slot, out IntentCommand intent)
    {
        if ((uint)slot < (uint)_lanes.Length && _lanes[slot] is { HasIntent: true } lane)
        {
            intent = lane.Latest;
            return true;
        }

        intent = default;
        return false;
    }

    /// <summary>What the bot's decider said it was doing at its newest step taken back, or none.</summary>
    public DoingRecord? LastDoing(int slot) => (uint)slot < (uint)_lanes.Length ? _lanes[slot]?.LastDoing : null;

    internal enum JobState
    {
        Free,
        Queued,
        Running,
        Done,
        Skipped,
        Faulted,
    }

    /// <summary>
    /// One bot's steps (§2.7): the brain, the events since its last step, the
    /// steps handed over and not yet started, and a store of step records to
    /// reuse. The steps run one after another on whichever thread runs the lane.
    /// </summary>
    internal sealed class Lane(Brain brain, int sightedCapacity) : IAiWork
    {
        private readonly Queue<BrainStepJob> _queued = new();
        private readonly Stack<BrainStepJob> _free = new();
        private readonly object _gate = new();
        private bool _busy;

        public Brain Brain { get; } = brain;

        /// <summary>The skill levels the bot was added with (what a recording started later announces).</summary>
        public byte[] Levels { get; init; } = [];

        /// <summary>Held while a step of this brain runs; a reader takes it without waiting.</summary>
        public object BrainLock { get; } = new();

        public List<AiEvent> Pending { get; } = new();

        public IntentCommand Latest { get; set; }

        public bool HasIntent { get; set; }

        public DoingRecord? LastDoing { get; set; }

        /// <summary>The bot's team as its newest view showed it (plan_bots_teams_tf2 B.4).</summary>
        public int Team { get; set; }

        public BrainStepJob Take() => _free.Count > 0 ? _free.Pop() : new BrainStepJob(sightedCapacity);

        public void Return(BrainStepJob job)
        {
            job.State = JobState.Free;
            _free.Push(job);
        }

        /// <summary>
        /// Queues <paramref name="job"/>; past <see cref="LaneDepth"/> unstarted
        /// steps the oldest is skipped and its events carried into the next.
        /// True when the lane was idle and must be handed to the transport.
        /// </summary>
        public bool Queue(BrainStepJob job, out BrainStepJob? dropped)
        {
            dropped = null;
            lock (_gate)
            {
                job.State = JobState.Queued;
                _queued.Enqueue(job);
                if (_queued.Count > LaneDepth)
                {
                    dropped = _queued.Dequeue();
                    _queued.Peek().Carry(dropped);
                    dropped.State = JobState.Skipped;
                }

                if (_busy)
                    return false;
                _busy = true;
                return true;
            }
        }

        /// <summary>Marks the lane busy if it was idle: true when the caller must hand it to the transport.</summary>
        public bool Start()
        {
            lock (_gate)
            {
                if (_busy)
                    return false;
                _busy = true;
                return true;
            }
        }

        /// <summary>The transport would not take the lane: it waits for the next hand-over.</summary>
        public void Idle()
        {
            lock (_gate)
                _busy = false;
        }

        public void Reset()
        {
            lock (_gate)
            {
                _queued.Clear();
                _busy = false;
            }
        }

        public void Run()
        {
            while (true)
            {
                BrainStepJob job;
                lock (_gate)
                {
                    if (_queued.Count == 0)
                    {
                        _busy = false;
                        return;
                    }

                    job = _queued.Dequeue();
                    job.State = JobState.Running;
                }

                lock (BrainLock)
                    job.Run(Brain);
            }
        }
    }
}

/// <summary>A callout that asked for a <c>say_team</c> line: who said it, what, and the tick of the step that said it.</summary>
public readonly record struct SpokenCallout(ActorId Speaker, SayIntent Say, Tick Tick);

/// <summary>
/// One brain step handed over: the bot's lane, its decide tick, its own copy
/// of the bot's view at that tick and the events it has not seen (§2.6), and
/// what it produced. The inputs are fixed when it is made, so where and when
/// it runs changes nothing.
/// </summary>
public sealed class BrainStepJob
{
    private AiEvent[] _events = new AiEvent[16];
    private int _state;

    internal BrainStepJob(int sightedCapacity) => View = new PerceivedView(sightedCapacity);

    public Tick Tick { get; private set; }

    public PerceivedView View { get; }

    public int EventCount { get; private set; }

    public ReadOnlySpan<AiEvent> Events => _events.AsSpan(0, EventCount);

    internal AiSession.Lane Lane { get; private set; } = null!;

    internal AiSession.JobState State
    {
        get => (AiSession.JobState)Volatile.Read(ref _state);
        set => Volatile.Write(ref _state, (int)value);
    }

    internal IntentCommand Intent { get; private set; }

    internal DoingRecord? Doing { get; private set; }

    internal double Micros { get; private set; }

    internal double CpuMicros { get; private set; }

    private Func<double>? _cpu;

    internal string? Fault { get; private set; }

    internal void Set(AiSession.Lane lane, Tick tick, PerceivedView view, ReadOnlySpan<AiEvent> events, Func<double>? cpu)
    {
        _cpu = cpu;
        CpuMicros = 0;
        Lane = lane;
        Tick = tick;
        Fault = null;
        View.CopyFrom(view);
        EventCount = 0;
        Grow(events.Length);
        events.CopyTo(_events);
        EventCount = events.Length;
    }

    /// <summary>A skipped step's events, ahead of this step's own.</summary>
    internal void Carry(BrainStepJob skipped)
    {
        int own = EventCount, carried = skipped.EventCount;
        Grow(carried + own);
        Array.Copy(_events, 0, _events, carried, own);
        skipped.Events.CopyTo(_events);
        EventCount = carried + own;
    }

    private void Grow(int count)
    {
        if (count > _events.Length)
            Array.Resize(ref _events, Math.Max(count, _events.Length * 2));
    }

    internal void Run(Brain brain)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        double cpu = _cpu?.Invoke() ?? 0;
        try
        {
            Intent = brain.Step(Tick, View, Events);
            Micros = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            CpuMicros = _cpu is { } clock ? clock() - cpu : 0;
            Doing = brain.Decider?.Doing;
            State = AiSession.JobState.Done;
        }
        catch (Exception e)
        {
            Fault = $"{brain.Bot} at {Tick}: {e.GetType().Name}: {e.Message}";
            State = AiSession.JobState.Faulted;
        }
    }
}
