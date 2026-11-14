using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Senses;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>
/// The horde's AI session on the game thread (plan_descent_logic 3.6, HO4, HO5):
/// one <see cref="HordeBrains"/> a level, made when the controller begins and
/// stopped and joined when it ends, BEFORE the controller lets the level's
/// data go. Each tick (<see cref="Tick"/>, from the controller, before its own
/// decide): the heroes, the brains' intents to <see cref="IRobotMotion"/> and
/// <see cref="IRobotActionSink"/>, a brain per lent body, sight (the engine's
/// PVS and lines through <see cref="ISightWorld"/>), the sounds the taps queued
/// since the last tick (to the brains, and to the far simulation as
/// <see cref="SoundEvent"/>s), then the snapshot. It never waits on the workers.
///
/// <para>
/// <b>Transport</b>: the process's one worker pool (<see cref="HordeWorkers"/>,
/// <c>tf2_descent_ai_threads</c>), started at begin and joined at end; or
/// inline under <c>tf2_descent_ai_sync 1</c>. <b>Recording</b>: under
/// <c>tf2_descent_ai_record 1</c>, the core's <see cref="AiRecorder"/> to
/// <c>&lt;game&gt;/ai_recordings</c>. <b>Seats</b>: a hero's seat is its player's
/// entity index; the session's hero slots 50 to 53 go to the first four seats
/// seen (<see cref="HeroSlots"/>). <b>Nav</b>: the level graph's paths
/// (<see cref="Nav.HordeNav.Paths"/>) once the level's graph is ready, the room route until then.
/// <b>Arrivals</b>: each body's arrival at its order's last corner (the motion's report) is an
/// <c>Arrived</c> for its brain the next tick.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeBrainHost), typeof(IHordeActors), typeof(IRobotTaunts)])]
public sealed class HordeSession(IRobotMotion motion, IRobotActionSink actions, ISightWorld sightWorld, IHordeHeroes heroes,
                                 IHordeRules rules, IHordeClock clock, IHeroLandings landings, HordeWorkers workers,
                                 HordeAiSyncConVar sync, HordeAiThreadsConVar threads, HordeAiRecordConVar record, IModInfo mod, ILog log,
                                 Attacks.IRobotAttackWorld fireLines, Nav.HordeNav nav, IRobotMotionReports reports)
    : IHordeBrainHost, IHordeActors, IRobotTaunts
{
    /// <summary>Sounds the taps may queue between two ticks; one more is counted and the gate fails (B.3: never dropped silently).</summary>
    public const int SoundCapacity = 256;

    /// <summary>How far a hero's landing from a blast jump carries, in units of walk.</summary>
    public const float LandingReach = 1500f;

    private const int MaxPlayers = 33;

    private readonly HeroPlace[] _places = new HeroPlace[MaxPlayers];
    private readonly HeroEye[] _eyes = new HeroEye[MaxPlayers];
    private readonly HeroPose[] _poses = new HeroPose[MaxPlayers];
    private readonly EntityHandle[] _heroPlayer = new EntityHandle[HordeSlots.Heroes];
    private readonly Queued[] _queue = new Queued[SoundCapacity];
    private readonly HordeSound[] _heard = new HordeSound[SoundCapacity];
    private readonly SoundEvent[] _far = new SoundEvent[SoundCapacity];
    private readonly HordeTickTimer _timer = new();
    private readonly (HordeSound Sound, long Tick)[] _recent = new (HordeSound, long)[64];
    private readonly HordeSound[] _recentScratch = new HordeSound[64];
    private int _recentNext;
    private long[] _arrivedSeen = [];
    private int _queued, _farCount, _gathered;
    private long _lastTick = long.MinValue;
    private HordeController? _horde;
    private HordeBrains? _brains;
    private Stream? _recording;
    private Adapters? _adapters;

    public HordeBrains? Brains => _brains;

    /// <summary>The level's controller this session serves, or none between levels.</summary>
    public HordeController? Horde => _horde;

    public bool Inline { get; private set; }

    public int Threads { get; private set; }

    public string? RecordingPath { get; private set; }

    /// <summary>The session's main-thread tick, timed while switched on (<c>tf2_descent_horde_ai_time</c>).</summary>
    public HordeTickTimer Timer => _timer;

    /// <summary>Levels this session began.</summary>
    public int Levels { get; private set; }

    /// <summary>Sounds the taps queued, all levels.</summary>
    public long SoundsQueued { get; private set; }

    /// <summary>Sounds refused for a full queue: the gate fails on any.</summary>
    public long SoundsOverflowed { get; private set; }

    /// <summary>Sounds handed to the far simulation.</summary>
    public long FarSounds { get; private set; }

    /// <summary>Sounds the far simulation's buffer had no room for between two of its ticks: the gate fails on any.</summary>
    public long FarOverflowed { get; private set; }

    public long Landings { get; private set; }

    public long HeroDeaths { get; private set; }

    public long RobotHits { get; private set; }

    /// <summary>Taunts told to brains (<see cref="Taunted"/>).</summary>
    public long Taunts { get; private set; }

    /// <summary>Ticks called again at a tick already stepped (nothing done).</summary>
    public long RepeatedTicks { get; private set; }

    public void Begin(HordeController horde, long now)
    {
        ArgumentNullException.ThrowIfNull(horde);
        if (_brains is not null)
            End();

        _horde = horde;
        Inline = sync.Value;
        IAiTransport transport;
        if (Inline)
        {
            transport = new InlineTransport();
            Threads = 0;
        }
        else
        {
            workers.Start(threads.Value);
            Threads = workers.Pool.Threads;
            transport = new PooledTransport(workers.Pool);
        }

        IAiRecordSink sink = new NoRecording();
        RecordingPath = null;
        float tickSeconds = (float)(1.0 / clock.TicksPerSecond);
        if (record.Value)
        {
            string dir = Path.Combine(mod.GameDir, "ai_recordings");
            Directory.CreateDirectory(dir);
            RecordingPath = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"horde-d{horde.Level!.Depth}-s{horde.Level.Seed}-{Levels + 1}.airec"));
            _recording = File.Create(RecordingPath);
            var recorder = new AiRecorder(_recording, HordeBrains.Header(horde.Level));
            recorder.World(HordeBrains.RecordedWorld(tickSeconds));
            sink = recorder;
        }

        _adapters = new Adapters(horde.Bodies!, motion, actions, sightWorld, _heroPlayer, fireLines);
        _brains = HordeBrains.For(horde.Roster!, horde.Pool!, horde.Level!, rules.Rules, _adapters, transport, sink,
                                  HordeFamilies.Deciders(horde.Level!, rules.Rules, nav.Paths), clock.TicksPerSecond, aim: _adapters, paths: nav.Paths);
        _arrivedSeen = new long[horde.Pool!.LastBody + 1];
        actions.Begin(horde, now);
        _brains.Session.CpuClock = HordeThreadClock.Micros;
        _queued = 0;
        _farCount = 0;
        _lastTick = long.MinValue;
        Levels++;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] AI session {Levels}: {HordeSlots.Slots} slots (bodies 1-{horde.Pool!.LastBody}, heroes {HordeSlots.FirstHero}-{HordeSlots.FirstHero + HordeSlots.Heroes - 1}), transport {(Inline ? "inline" : $"pooled, {Threads} threads")}, "
            + $"nav: the level graph's paths once ready (the room route until then), sight budget {_brains.Vision.Budget} lines a tick, team layer off, recording {RecordingPath ?? "off"}\n"));
    }

    public void Tick(long now)
    {
        if (_brains is not { } brains || _horde is not { Level: { } level } || _adapters is not { } a)
            return;

        // One step a server tick: a second frame at the same tick (or a clock that went back) adds nothing.
        if (now <= _lastTick)
        {
            RepeatedTicks++;
            return;
        }

        _lastTick = now;
        _timer.Enter();
        int n = heroes.Gather(level, _places, _eyes);
        _gathered = n;
        for (int k = 0; k < n; k++)
            _poses[k] = new HeroPose(_places[k].Seat, _places[k].Position, HordeVectors.ToAi(_eyes[k].Eye), _places[k].Room);
        brains.Heroes(now, _poses.AsSpan(0, n));
        for (int k = 0; k < n; k++)
        {
            ActorId actor = brains.HeroSlots.ActorOf(_places[k].Seat);
            if (actor.IsValid)
                _heroPlayer[actor.Slot - HordeSlots.FirstHero] = _eyes[k].Player;
            if (landings.Landed(_eyes[k].Player))
            {
                Landings++;
                Queue(SoundKind.Footstep, _places[k].Position, LandingReach, _places[k].Seat);
            }
        }

        // The motion's arrivals since the last tick, to the brains (the core's applier feedback).
        for (int b = 1; b < _arrivedSeen.Length; b++)
        {
            var arrived = reports.Arrived(b);
            if (arrived.Tick > _arrivedSeen[b])
            {
                _arrivedSeen[b] = arrived.Tick;
                brains.Arrived(now, b, arrived.At);
            }
        }

        actions.Tick(now);
        brains.Apply(now, a);
        brains.Sync(now, a);
        if (!brains.Added.IsEmpty)
            brains.HearAdded(now, Recent(now));
        brains.See(now, a);

        int heard = 0;
        for (int i = 0; i < _queued; i++)
        {
            ref readonly Queued q = ref _queue[i];
            int seat = SeatOf(q.Owner);
            if (seat == RobotOwner)
                continue;
            int room = level.RoomAt(q.At);
            if (room < 0)
                continue;
            _heard[heard++] = new HordeSound(q.Kind, q.At, q.Reach, seat, room);
            _recent[_recentNext] = (_heard[heard - 1], now);
            _recentNext = (_recentNext + 1) % _recent.Length;
            if (_farCount < _far.Length)
            {
                _far[_farCount++] = new SoundEvent(room, q.At, q.Reach, seat);
                FarSounds++;
            }
            else
            {
                FarOverflowed++;
            }
        }

        _queued = 0;
        brains.Hear(now, _heard.AsSpan(0, heard));
        brains.Publish(now);
        _timer.Leave();
    }

    public ReadOnlySpan<SoundEvent> TakeFarSounds()
    {
        int n = _farCount;
        _farCount = 0;
        return _far.AsSpan(0, n);
    }

    public void End()
    {
        if (_brains is not { } brains)
            return;

        int discarded = Inline ? 0 : workers.Stop();
        brains.End();
        actions.End();
        var c = brains.Session.Counters;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] AI session {Levels} ended: workers joined ({discarded} queued steps discarded), {brains.BrainsAdded} brains added, {brains.BrainsRemoved} removed, "
            + $"events in {c.EventsIn} processed {c.EventsProcessed} discarded {brains.EventsDiscarded}, steps run {c.StepsRun} skipped {c.StepsSkipped}\n"));
        _recording?.Dispose();
        _recording = null;
        _brains = null;
        _adapters = null;
        _horde = null;
        _queued = 0;
        _farCount = 0;
        Array.Clear(_heroPlayer);
        Array.Clear(_recent);
    }

    /// <summary>A sound the taps heard: queued for the next tick; refused and counted when the queue is full.</summary>
    public void Heard(SoundKind kind, Vector at, float reach, int owner)
    {
        if (_brains is null || !HordeSoundKinds.Heard(kind))
            return;
        Queue(kind, HordeVectors.ToAi(at), reach, owner);
    }

    /// <summary>A hit on a robot (the hit chain): damage for its brain, pain for its pack in earshot.</summary>
    public void Hit(in RobotHit hit)
    {
        if (_brains is not { } brains || _horde?.Bodies is not { } bodies)
            return;
        RobotHits++;
        int seat = SeatOf(hit.Attacker.EntryIndex);
        brains.Hit(clock.Tick, hit.Robot, seat > 0 ? seat : -1, (float)hit.Taken, hit.Killed, bodies.Body(hit.Body).Position);
    }

    /// <summary>
    /// A taunt (a hero's threat skill, HO8): the robot's brain is told hero <paramref name="seat"/>
    /// attacked it, a Damaged of nothing with the hero as its source, so it turns on him and
    /// carries him as its target. The core has no taunt event yet (B.3's
    /// <c>Taunted { robot, by, until }</c>); this is the event the brain already answers.
    /// </summary>
    public void Taunted(RobotId robot, int body, int seat)
    {
        if (_brains is not { } brains || _horde?.Bodies is not { } bodies || seat <= 0)
            return;
        Taunts++;
        brains.Hit(clock.Tick, robot, seat, 0f, false, bodies.Body(body).Position);
    }

    /// <summary>A hero died (the death hook): told to the robots that held sight of it.</summary>
    public void HeroDied(int seat, int killerIndex)
    {
        if (_brains is not { } brains || _horde?.Bodies is not { } bodies)
            return;
        HeroDeaths++;
        ActorId killer = ActorId.None;
        for (int b = 1; b <= bodies.LastBody; b++)
            if (!bodies.Body(b).Parked && bodies.Body(b).Handle.EntryIndex == killerIndex)
                killer = brains.BrainAt(b);
        brains.HeroDied(clock.Tick, seat, killer);
    }

    /// <summary>Heroes told down, and up, to the brains (WO6), since the module started.</summary>
    public long HeroDowns { get; private set; }

    public long HeroUps { get; private set; }

    /// <summary>
    /// A hero went down or got up (WO6, B.3's <c>HeroDowned</c>): every brain
    /// with a body is told, so its memory holds him as no threat until he is up,
    /// dies or respawns (the core's blackboard). A seat that holds no hero slot
    /// is told to nobody.
    /// </summary>
    public void HeroDowned(int seat, bool down, Vector at)
    {
        if (_brains is not { } brains || _horde?.Bodies is not { } bodies || seat <= 0)
            return;
        ActorId hero = brains.HeroSlots.ActorOf(seat);
        if (!hero.IsValid)
            return;
        if (down)
            HeroDowns++;
        else
            HeroUps++;
        var tick = new Tick((int)clock.Tick);
        AiVector where = HordeVectors.ToAi(at);
        for (int b = 1; b <= bodies.LastBody; b++)
            if (brains.BrainAt(b) is { IsValid: true } brain)
                brains.Session.Push(AiEvent.HeroDowned(brain, tick, hero, where, down));
    }

    public bool TryEye(ActorId actor, out AiVector eye)
    {
        eye = default;
        if (_brains is not { } brains)
            return false;
        return HordeSlots.IsHero(actor.Slot) ? brains.TryHeroEye(actor, out eye) : brains.TryRobotEye(actor, out eye);
    }

    /// <summary>The session as one line for <c>tf2_descent_horde</c>.</summary>
    public string Line()
    {
        if (_brains is not { } b || _horde?.Bodies is not { } bodies)
            return "AI: no session";
        var c = b.Session.Counters;
        var v = b.Vision;
        return string.Create(CultureInfo.InvariantCulture,
            $"AI session {Levels}: {(Inline ? "inline" : $"pooled {Threads} threads")}, brains {b.Session.BotCount} (lent bodies {bodies.Lent}), added {b.BrainsAdded}, removed {b.BrainsRemoved}; "
            + $"intents applied {b.IntentsApplied}, dropped stale {b.IntentsStale}, dead {b.IntentsDead}, expired {b.IntentsExpired}, actions {b.Actions}; "
            + $"events in {c.EventsIn} processed {c.EventsProcessed} waiting {b.EventsWaiting} discarded {b.EventsDiscarded} dropped {c.EventsDropped}; "
            + $"steps run {c.StepsRun} skipped {c.StepsSkipped} faulted {c.StepsFaulted} in flight {c.InFlight} (most {c.InFlightMax}), brain cpu {c.BrainCpuMicrosMean:0.0} us a decide; "
            + $"sight traces {v.Traces} (last tick {v.TracesThisTick}, most {v.MostTraces} of {v.Budget}, candidates {v.CandidatesThisTick}, unserved {v.UnservedThisTick}, longest wait {v.LongestWait} ticks), gained {v.Gains} lost {v.Losses}; "
            + $"sounds queued {SoundsQueued} overflowed {SoundsOverflowed} heard {b.SoundsHeard} to far {FarSounds}, landings {Landings}; hits {RobotHits}, taunts {Taunts}, hero deaths {HeroDeaths}, stuck {b.Stuck}, arrived told {b.ArrivalsTold}, allies told {b.AlliesTold}; "
            + $"hero slots {HeroSlotsLine(b)}{(SoundsOverflowed > 0 || FarOverflowed > 0 || c.EventsDropped > 0 ? "; EVENTS LOST" : "")}");
    }

    private static string HeroSlotsLine(HordeBrains b)
    {
        var parts = new string[HordeSlots.Heroes];
        for (int h = 0; h < HordeSlots.Heroes; h++)
            parts[h] = string.Create(CultureInfo.InvariantCulture, $"{HordeSlots.FirstHero + h}:seat {b.HeroSlots.SeatAt(h)}");
        return string.Join(' ', parts) + string.Create(CultureInfo.InvariantCulture, $" (unseated {b.HeroSlots.Unseated})");
    }

    /// <summary>
    /// The sounds heard in the last second, oldest first: what a robot lent a body
    /// since may have missed (the far simulation hears only robots without one, at its
    /// own 2 Hz, and a lend can fall between the sound and that tick).
    /// </summary>
    private ReadOnlySpan<HordeSound> Recent(long now)
    {
        long since = now - (long)Math.Ceiling(clock.TicksPerSecond);
        int n = 0;
        for (int k = 0; k < _recent.Length; k++)
        {
            var (sound, tick) = _recent[(_recentNext + k) % _recent.Length];
            if (tick > since && tick < now && sound.Room >= 0)
                _recentScratch[n++] = sound;
        }

        return _recentScratch.AsSpan(0, n);
    }

    private void Queue(SoundKind kind, AiVector at, float reach, int owner)
    {
        if (_queued >= _queue.Length)
        {
            SoundsOverflowed++;
            if (SoundsOverflowed == 1)
                log.Warning($"[TF2Descent/Horde] AI: the sound queue is full ({SoundCapacity}): a sound was refused; the horde's hearing gate fails\n");
            return;
        }

        _queue[_queued++] = new Queued(kind, at, reach, owner);
        SoundsQueued++;
    }

    private const int RobotOwner = -2;

    /// <summary>The hero seat of entity index <paramref name="owner"/>: itself when a hero gathered this tick; <see cref="RobotOwner"/> for a robot body; else -1.</summary>
    private int SeatOf(int owner)
    {
        if (owner <= 0)
            return -1;
        for (int k = 0; k < _gathered; k++)
            if (_places[k].Seat == owner)
                return owner;
        if (_horde?.Bodies is { } bodies)
            for (int b = 1; b <= bodies.LastBody; b++)
                if (bodies.Body(b).Handle.EntryIndex == owner)
                    return RobotOwner;
        return -1;
    }

    private readonly record struct Queued(SoundKind Kind, AiVector At, float Reach, int Owner);

    /// <summary>The brains' seams over this level's bodies, the motion, the action sink, the engine's sight and the guns' lines.</summary>
    private sealed class Adapters(RobotBodies bodies, IRobotMotion motion, IRobotActionSink actions, ISightWorld world, EntityHandle[] heroPlayer,
                                  Attacks.IRobotAttackWorld fireLines)
        : IHordeBodyPoses, IHordeOrders, IHordeLines, IHordeBodyAim
    {
        public AiAngles Aim(int body) => Attacks.RobotAim.Angles(bodies.Body(body));

        public float LineOfFire(int body, float reach)
        {
            var b = bodies.Body(body);
            Vector eye = Attacks.RobotAim.Eye(b);
            var line = fireLines.Line(eye, eye + Attacks.RobotAim.Forward(b) * reach, b.Handle);
            return line.Hit ? line.Fraction * reach : reach;
        }

        public AiVector Position(int body) => bodies.Body(body).Position;

        public float Yaw(int body) => bodies.Body(body).Yaw;

        public void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick) => motion.Order(body, move, look, expiresTick);

        public void Hold(int body) => motion.Hold(body);

        public void Act(int body, in ActionIntent action) => actions.Act(body, action);

        public bool InPvs(int hero, AiVector mins, AiVector maxs) =>
            heroPlayer[hero] is var p && p != EntityHandle.None && world.InPvs(p, HordeVectors.ToEngine(mins), HordeVectors.ToEngine(maxs));

        public bool LineClear(AiVector from, AiVector to) => world.LineClear(HordeVectors.ToEngine(from), HordeVectors.ToEngine(to));
    }
}
