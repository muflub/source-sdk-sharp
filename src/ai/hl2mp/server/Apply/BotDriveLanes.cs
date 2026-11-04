using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>What the main thread's trace answered about the breakable a bot's last step said is on its route (§6.2).</summary>
public readonly record struct BreakableSight(int Cell, bool Clear, AiVector Point)
{
    public static BreakableSight None => new(-1, false, default);
}

/// <summary>
/// One drive step's inputs, all read on the main thread when it is handed over
/// (§2.6): the brain's newest intent, the bot's own facts, and the level's
/// immutable or copied state. Where and when the step runs changes nothing.
/// </summary>
public readonly record struct DriveInput(
    IntentCommand Brain,
    AiVector Origin,
    Tick Tick,
    NavGraph? Graph,
    NavObstacleState? Obstacles,
    bool OnLadder,
    AiAngles View,
    EntityHandle Shooter,
    IReadOnlyList<AiVector> Ladders,
    IReadOnlyList<NearbyPlayer> Players,
    bool Breakables,
    BreakableSight Sight);

/// <summary>What a bot's drive has done so far, by the lane's own count: read from the step's result, never from the driver.</summary>
public readonly record struct DriveCounters(long Breaks, long BreaksGivenUp, long Mounts, long Climbs, long Stucks, long Yields, long Builds, long BuildsOnMain)
{
    public static DriveCounters operator +(DriveCounters a, DriveCounters b)
        => new(a.Breaks + b.Breaks, a.BreaksGivenUp + b.BreaksGivenUp, a.Mounts + b.Mounts, a.Climbs + b.Climbs, a.Stucks + b.Stucks, a.Yields + b.Yields,
               a.Builds + b.Builds, a.BuildsOnMain + b.BuildsOnMain);
}

/// <summary>
/// One drive step's result, handed back as a value as an intent is: the
/// command, and everything the main thread would otherwise have asked the
/// driver. <see cref="BreakableCell"/> is the breakable the bot wants a line
/// to, -1 for none: the trace is the main thread's, before the next step.
/// </summary>
public readonly record struct DriveOutput(IntentCommand Command, AiVector? Unreachable, string? StuckLine, int BreakableCell, DriveCounters Counters);

/// <summary>What <see cref="BotDriveLanes.Take"/> found for a bot.</summary>
public enum DriveTaken : byte
{
    /// <summary>No step was handed over: nothing to carry out.</summary>
    None = 0,

    /// <summary>A finished step: its command is the bot's newest.</summary>
    Fresh,

    /// <summary>The step has not finished: the bot's last command goes on, with no key pressed anew.</summary>
    Late,

    /// <summary>The step threw: the same as late, and said once by the main thread.</summary>
    Faulted,
}

/// <summary>
/// The bots' drive steps on the AI's workers (plan_hl2mp_ai §2.7, user ruling
/// 2026-11-03: "Bot logic should be on the ai thread pool not main thread"):
/// a lane per bot, each with a <see cref="BrainDriver"/> only that lane touches.
///
/// <para>
/// SINGLE FLIGHT. A lane holds at most one step. The main thread writes its
/// inputs only while it is idle, the step runs on whichever thread the
/// transport names, and the main thread reads its result only once it says it
/// is done, so nothing about a driver is ever shared. A step that has not
/// finished when the next tick wants one is not queued behind: that bot's last
/// command goes on, counted (<see cref="Late"/>).
/// </para>
///
/// <para>
/// Inline (<c>ai_sync 1</c>) the transport runs the step on the caller: the
/// same steps, in the same order, each done before its bot's next command.
/// Pooled with every step done in time the commands are the same, whatever the
/// thread count: a step's inputs are fixed when it is handed over.
/// </para>
/// </summary>
public sealed class BotDriveLanes(Func<IAiTransport> transport, int slots)
{
    private readonly Lane?[] _lanes = new Lane?[slots];
    private DriveCounters _gone;

    /// <summary>Ticks a bot's step had not finished when its command was due: <c>ai.drive.late</c>.</summary>
    public long Late { get; private set; }

    /// <summary>Steps handed over: <c>ai.drive.steps</c>.</summary>
    public long Steps { get; private set; }

    /// <summary>Steps that ran on the thread that handed them over: <c>ai.drive.main</c>; none while pooled.</summary>
    public long StepsOnMain { get; private set; }

    /// <summary>Steps the transport refused (its pool had stopped), run on the caller rather than left waiting.</summary>
    public long Refused { get; private set; }

    /// <summary>Steps that threw; the bot's last command goes on.</summary>
    public long Faults { get; private set; }

    /// <summary>The last fault's message, or null.</summary>
    public string? LastFault { get; private set; }

    /// <summary>Wall time of the steps taken back, in microseconds: <c>ai.drive.us</c>'s numerator.</summary>
    public double Micros { get; private set; }

    /// <summary>The longest step taken back, in microseconds.</summary>
    public double MicrosMax { get; private set; }

    /// <summary>Every lane's counters, as of its last step taken back, and those of the bots gone.</summary>
    public DriveCounters Totals
    {
        get
        {
            DriveCounters all = _gone;
            foreach (Lane? lane in _lanes)
            {
                if (lane is not null)
                    all += lane.Counters;
            }

            return all;
        }
    }

    /// <summary>Whether a step of <paramref name="slot"/>'s bot is on its way.</summary>
    public bool Busy(int slot) => (uint)slot < (uint)_lanes.Length && _lanes[slot] is { InFlight: true };

    /// <summary>The bot's lane, for the facts: read its driver only while it is not <see cref="Busy"/>.</summary>
    internal BrainDriver? DriverOf(int slot) => (uint)slot < (uint)_lanes.Length ? _lanes[slot]?.Driver : null;

    /// <summary>
    /// Hands <paramref name="input"/> to the lane of its bot; false, handing
    /// nothing, while the lane's last step is on its way. A result nobody took
    /// is overwritten: it was for a bot a script or <c>ai_think 0</c> holds.
    /// </summary>
    public bool Hand(in DriveInput input, in BotSelfFacts self)
    {
        ActorId bot = input.Brain.Bot;
        if ((uint)bot.Slot >= (uint)_lanes.Length)
            throw new ArgumentOutOfRangeException(nameof(input), bot.Slot, $"slots run 0..{_lanes.Length - 1}");
        Lane? lane = _lanes[bot.Slot];
        if (lane is null || lane.Bot != bot)
        {
            Forget(bot.Slot);
            _lanes[bot.Slot] = lane = new Lane(bot);
        }

        if (lane.InFlight)
            return false;
        lane.Set(in input, in self, Environment.CurrentManagedThreadId);
        Steps++;
        if (transport().Dispatch(lane))
            return true;
        // No worker to take it: a stopped pool, between levels. Run here, not left waiting.
        Refused++;
        lane.Run();
        return true;
    }

    /// <summary>Takes <paramref name="bot"/>'s newest finished step back, with the facts it was handed.</summary>
    public DriveTaken Take(ActorId bot, out DriveOutput output, out BotSelfFacts self)
    {
        output = default;
        self = default;
        if ((uint)bot.Slot >= (uint)_lanes.Length || _lanes[bot.Slot] is not { } lane || lane.Bot != bot || lane.Idle)
            return DriveTaken.None;
        if (lane.InFlight)
        {
            Late++;
            return DriveTaken.Late;
        }

        self = lane.HandedSelf;
        StepsOnMain += lane.RanOnHandingThread ? 1 : 0;
        Micros += lane.Micros;
        MicrosMax = Math.Max(MicrosMax, lane.Micros);
        if (!lane.TakeResult(out output, out string? fault))
        {
            Faults++;
            LastFault = fault;
            return DriveTaken.Faulted;
        }

        return DriveTaken.Fresh;
    }

    /// <summary>
    /// The way a bot's brain's follower is going, as of a moment no worker was
    /// moving it: read straight off the follower when no step is on its way,
    /// otherwise the copy its last step published, asked for next time when
    /// there is none. Null when it has no follower, or no copy yet. Main thread.
    /// </summary>
    public (AiVector Target, AiVector[] Way)? RouteOf(int slot)
        => (uint)slot < (uint)_lanes.Length && _lanes[slot] is { } lane ? lane.Route() : null;

    /// <summary>
    /// Waits for every step on its way (<c>ai_sync</c> changing, the facts; the
    /// one other place than <see cref="AiSession.Drain"/> the main thread waits).
    /// </summary>
    public void Settle()
    {
        foreach (Lane? lane in _lanes)
        {
            if (lane is not null)
                SpinWait.SpinUntil(() => !lane.InFlight);
        }
    }

    /// <summary>
    /// The level is over and the pool has stopped and joined its workers: no
    /// step runs now or will. Every lane is idle, a discarded step's result
    /// never comes, and no lane holds the level's graph or obstacles. Answers
    /// how many steps were dropped.
    /// </summary>
    public int Abandon()
    {
        int dropped = 0;
        foreach (Lane? lane in _lanes)
        {
            if (lane is not null && lane.Reset())
                dropped++;
        }

        return dropped;
    }

    /// <summary>
    /// The bot in <paramref name="slot"/> has gone. A step of it on a worker
    /// finishes with its own lane, which nothing reads again.
    /// </summary>
    public void Forget(int slot)
    {
        if ((uint)slot >= (uint)_lanes.Length || _lanes[slot] is not { } lane)
            return;
        _gone += lane.Counters;
        _lanes[slot] = null;
    }

    private sealed class Lane(ActorId bot) : IAiWork
    {
        private const int StateIdle = 0, StateHanded = 1, StateDone = 2;

        private readonly NavBuildTally _tally = new();
        private readonly List<AiVector> _route = [];
        private readonly HandedAim _aim = new();
        private DriveInput _input;
        private DriveOutput _output;
        private NavObstacleState? _obstacles;
        private NavGraph? _graph;
        private string? _fault;
        private int _handingThread;
        private int _state;
        private volatile bool _routeAsked;
        private (AiVector Target, AiVector[] Way)? _publishedRoute;
        private readonly object _routeGate = new();

        public ActorId Bot { get; } = bot;

        public BrainDriver Driver { get; } = new();

        public BotSelfFacts HandedSelf { get; private set; }

        public DriveCounters Counters { get; private set; }

        public double Micros { get; private set; }

        public bool RanOnHandingThread { get; private set; }

        public bool Idle => Volatile.Read(ref _state) == StateIdle;

        public bool InFlight => Volatile.Read(ref _state) == StateHanded;

        /// <summary>The inputs, written on the main thread while no step runs; the obstacles a private copy, so what the main thread learns later is the next step's.</summary>
        public void Set(in DriveInput input, in BotSelfFacts self, int handingThread)
        {
            _input = input;
            HandedSelf = self;
            _handingThread = handingThread;
            _tally.MainThread = handingThread;
            if (input.Obstacles is { } live)
            {
                if (_obstacles is null || _obstacles.Count != live.Count)
                    _obstacles = live.Clone();
                else
                    _obstacles.CopyFrom(live);
            }
            else
                _obstacles = null;

            Volatile.Write(ref _state, StateHanded);
        }

        public void Run()
        {
            long started = Stopwatch.GetTimestamp();
            int slot = Bot.Slot;
            try
            {
                DriveInput input = _input;
                RanOnHandingThread = Environment.CurrentManagedThreadId == _handingThread;
                Driver.Tally = _tally;
                // Another level's graph: a follower of the last one has no way here.
                if (!ReferenceEquals(input.Graph, _graph))
                {
                    Driver.Forget(slot);
                    _graph = input.Graph;
                }

                Driver.Ladders = input.Ladders;
                Driver.Players = input.Players;
                _aim.Set(input.Sight);
                Driver.Breakables = input.Breakables ? _aim : null;
                IntentCommand brain = input.Brain;
                IntentCommand command = Driver.Drive(in brain, input.Origin, input.Tick, 0, input.Graph, _obstacles, input.OnLadder, input.View, input.Shooter);
                _output = new DriveOutput(command, Driver.TakeUnreachable(slot), Driver.TakeStuckLine(slot), _aim.Wanted, Driver.CountersOf(_tally));
                _fault = null;
                if (_routeAsked)
                {
                    _routeAsked = false;
                    Publish(Driver.FollowerOf(slot));
                }
            }
#pragma warning disable CA1031 // A step that throws is counted, and the bot keeps its last command: it must not take a worker's run or the main thread's frame with it.
            catch (Exception e)
#pragma warning restore CA1031
            {
                _fault = $"{Bot} at {_input.Tick}: {e.GetType().Name}: {e.Message}";
            }

            Micros = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            Volatile.Write(ref _state, StateDone);
        }

        public bool TakeResult(out DriveOutput output, out string? fault)
        {
            output = _output;
            fault = _fault;
            Volatile.Write(ref _state, StateIdle);
            if (fault is not null)
                return false;
            Counters = output.Counters;
            return true;
        }

        public (AiVector Target, AiVector[] Way)? Route()
        {
            if (!InFlight)
                Publish(Driver.FollowerOf(Bot.Slot));
            else
                _routeAsked = true;
            lock (_routeGate)
                return _publishedRoute;
        }

        private void Publish(NavFollower? follower)
        {
            (AiVector, AiVector[])? copy = null;
            if (follower is not null)
            {
                follower.Route(_route);
                if (_route.Count > 0)
                    copy = (follower.Target, [.. _route]);
            }

            lock (_routeGate)
                _publishedRoute = copy;
        }

        /// <summary>Back to idle with nothing held of the level; true when a handed step was dropped.</summary>
        public bool Reset()
        {
            bool dropped = InFlight;
            Volatile.Write(ref _state, StateIdle);
            _input = default;
            _output = default;
            _obstacles = null;
            _graph = null;
            _fault = null;
            Driver.Forget(Bot.Slot);
            lock (_routeGate)
                _publishedRoute = null;
            return dropped;
        }
    }

    /// <summary>The main thread's answer for the breakable the last step named: a question about another cell is put to it before the next step.</summary>
    private sealed class HandedAim : IBreakableAim
    {
        private BreakableSight _sight = BreakableSight.None;

        public int Wanted { get; private set; } = -1;

        public void Set(BreakableSight sight)
        {
            _sight = sight;
            Wanted = -1;
        }

        public bool TryAim(int cell, AiVector eye, EntityHandle shooter, out AiVector point)
        {
            Wanted = cell;
            point = _sight.Point;
            return cell == _sight.Cell && _sight.Clear;
        }
    }
}
