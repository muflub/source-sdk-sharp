using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// A session without the team layer (<see cref="AiSession.TeamCallouts"/> false): its
/// <see cref="AiSession.Publish"/> never takes back a step by waiting for it, however
/// old the step and whatever team its bots are on, and re-publishes nothing a brain
/// says. Each fact's control is the same session with the layer on, which waits for
/// the step due by the tick exactly as B.4 has it (<see cref="AiSession.CalloutMinDelayTicks"/>).
/// </summary>
public sealed class TeamCalloutOptOutTests
{
    private const int Slots = 8, Sighted = 4;

    // Slot 1 decides where (tick + 1) % 6 == 0: 5, 11, 17, ...
    private static readonly ActorId A = new(1, 1), B = new(2, 2);

    private const int FirstDecide = 5, FirstDue = FirstDecide + AiSession.CalloutMinDelayTicks;

    /// <summary>Runs nothing while shut (a stopped pool: the caller keeps the work); open, runs work on the caller.</summary>
    private sealed class Gate : IAiTransport
    {
        public bool Open { get; set; }

        public int Refused { get; private set; }

        public bool Inline => true;

        public bool Dispatch(IAiWork work)
        {
            if (!Open)
            {
                Refused++;
                return false;
            }

            work.Run();
            return true;
        }
    }

    private static void Publish(AiSession s, int tick, int team, params ActorId[] bots)
    {
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
        foreach (ActorId bot in bots)
            snap.Add(bot, new SelfState(default, default, default, 100, 0, 3, 30, 90, SelfFlags.Alive, Team: team));
        s.Publish(snap);
    }

    private static AiSession Session(IAiTransport transport, bool callouts)
    {
        var s = new AiSession(transport, new NoRecording(), Slots, Sighted) { TeamCallouts = callouts };
        s.AddBot(A, 1, []);
        return s;
    }

    /// <summary>The first tick whose publish asked a shut transport to run a step (it throws: nothing will), or 0 for none through <paramref name="ticks"/>.</summary>
    private static int FirstWait(AiSession s, int team, int ticks)
    {
        for (int t = 1; t <= ticks; t++)
        {
            try
            {
                Publish(s, t, team, A);
            }
            catch (InvalidOperationException)
            {
                return t;
            }
        }

        return 0;
    }

    [Fact]
    public void ATeamedSessionWithoutCalloutsNeverAsksForAStepOnPublish()
    {
        var gate = new Gate();
        AiSession s = Session(gate, callouts: false);
        Assert.Equal(0, FirstWait(s, team: 2, ticks: 120));
        Assert.Equal((0L, 0L), (s.Counters.CalloutWaits, s.Counters.StepsRun));
        Assert.True(s.Counters.InFlight > 0, "the stimulus: steps old enough to be due were handed over and never run");
    }

    /// <summary>The control: with the layer on, the same session asks for its first step the tick that step's callouts could be due.</summary>
    [Fact]
    public void ATeamedSessionWithCalloutsAsksForTheStepDueByTheTick()
    {
        AiSession s = Session(new Gate(), callouts: true);
        Assert.Equal(FirstDue, FirstWait(s, team: 2, ticks: 120));
    }

    /// <summary>What turns the wait on is the team: a deathmatch session never asks, layer or no layer.</summary>
    [Fact]
    public void AnUnteamedSessionWithCalloutsNeverAsks()
    {
        AiSession s = Session(new Gate(), callouts: true);
        Assert.Equal(0, FirstWait(s, team: 0, ticks: 120));
    }

    /// <summary>
    /// A transport that would run the step if asked: through the first due tick, not
    /// a decide tick of the bot's, the session without the layer leaves it unrun.
    /// </summary>
    [Fact]
    public void WithoutCalloutsADueStepIsLeftToItsTransport()
    {
        var gate = new Gate();
        AiSession s = Session(gate, callouts: false);
        for (int t = 1; t < FirstDue; t++)
            Publish(s, t, 2, A);
        gate.Open = true;
        Publish(s, FirstDue, 2, A);
        Assert.Equal((0L, 0L), (s.Counters.StepsRun, s.Counters.CalloutWaits));
        s.Drain(wait: true);
        Assert.True(s.Counters.StepsRun > 0, "the stimulus: there were steps to run");
    }

    /// <summary>The control: with the layer on, that publish runs the step and counts the wait.</summary>
    [Fact]
    public void WithCalloutsADueStepIsTakenBackOnPublish()
    {
        var gate = new Gate();
        AiSession s = Session(gate, callouts: true);
        for (int t = 1; t < FirstDue; t++)
            Publish(s, t, 2, A);
        Assert.Equal(0L, s.Counters.StepsRun);
        gate.Open = true;
        Publish(s, FirstDue, 2, A);
        Assert.True(s.Counters.StepsRun > 0, "the due step was run by the publish");
        Assert.Equal(1L, s.Counters.CalloutWaits);
    }

    // ---------------------------------------------------------------- on a pool

    /// <summary>Sleeps through its first decide, as a brain that stalls.</summary>
    private sealed class Stall(ActorId bot, int sleepMs) : IDecider
    {
        private bool _slept;

        public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
        {
            if (!_slept)
            {
                _slept = true;
                Thread.Sleep(sleepMs);
            }

            return IntentCommand.Hold(bot, tick, seq);
        }

        public BtTrace Trace { get; } = new();

        public DoingRecord Doing => default;

        public IReadOnlyList<string> NodeNames => [];

        public IReadOnlyList<int> NodeBranches => [];
    }

    /// <summary>
    /// A teamed bot on a pool of one thread whose first decide stalls for a second, published
    /// through the tick that step's callouts could be due: how often a publish waited, and how
    /// many steps were still out when the main thread got there.
    /// </summary>
    private static (long Waits, int InFlight) PublishedThroughAStall(bool callouts)
    {
        using var pool = new AiWorkerPool();
        pool.Start(1);
        var s = new AiSession(new PooledTransport(pool), new NoRecording(), Slots, Sighted)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup => new Stall(setup.Bot, 1000),
            TeamCallouts = callouts,
        };
        SkillPresets.TryGet("normal", out SkillProfile? p);
        s.AddBot(A, 1, [.. p!.Levels.Select(l => (byte)l)]);
        for (int t = 1; t <= FirstDue; t++)
            Publish(s, t, 2, A);
        (long, int) seen = (s.Counters.CalloutWaits, s.Counters.InFlight);
        s.Drain(wait: true);
        Assert.Equal(0, s.Counters.StepsFaulted);
        return seen;
    }

    [Fact]
    public void WithoutCalloutsAStalledTeamedBrainNeverHoldsUpThePublish()
    {
        (long waits, int inFlight) = PublishedThroughAStall(callouts: false);
        Assert.Equal(0L, waits);
        Assert.True(inFlight > 0, "the control: the stalled step was still out when the main thread passed its due tick");
    }

    /// <summary>The control: with the layer on, the publish at the due tick waits the stall out.</summary>
    [Fact]
    public void WithCalloutsThePublishWaitsForTheStalledStep() => Assert.Equal(1L, PublishedThroughAStall(callouts: true).Waits);

    // ---------------------------------------------------------------- what is said

    private static readonly SayIntent Spotted = new(CalloutKind.Enemy, new AiVector(256, 256, 0), 2, 5, 40, 2, true);

    /// <summary>Says <see cref="Spotted"/> at every decide.</summary>
    private sealed class Speaker(ActorId bot) : IDecider
    {
        public IntentCommand Decide(Tick tick, PerceivedView view, int seq) => IntentCommand.Hold(bot, tick, seq) with { Say = Spotted };

        public BtTrace Trace { get; } = new();

        public DoingRecord Doing => default;

        public IReadOnlyList<string> NodeNames => [];

        public IReadOnlyList<int> NodeBranches => [];
    }

    /// <summary>A and B on one team, each saying <see cref="Spotted"/> at every decide, for 120 ticks.</summary>
    private static AiSession Talk(bool callouts)
    {
        var s = new AiSession(new InlineTransport(), new NoRecording(), Slots, Sighted)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup => new Speaker(setup.Bot),
            TeamCallouts = callouts,
        };
        SkillPresets.TryGet("normal", out SkillProfile? p);
        byte[] levels = [.. p!.Levels.Select(l => (byte)l)];
        s.AddBot(A, 1, levels);
        s.AddBot(B, 2, levels);
        for (int t = 1; t <= 120; t++)
            Publish(s, t, 2, A, B);
        return s;
    }

    [Fact]
    public void WithoutCalloutsNothingSaidIsRePublishedScheduledOrKeptForChat()
    {
        AiSession s = Talk(callouts: false);
        Assert.True(s.Counters.StepsRun > 0, "the stimulus: the speakers decided");
        Assert.Equal((0L, 0, 0), (s.Counters.CalloutsDelivered, s.CalloutsInFlight, s.TakeSpoken().Count));
    }

    /// <summary>The control: with the layer on, the same speakers' words reach each other and the chat.</summary>
    [Fact]
    public void WithCalloutsTheSameWordsAreRePublished()
    {
        AiSession s = Talk(callouts: true);
        Assert.True(s.Counters.CalloutsDelivered > 0, "no callout was delivered");
        Assert.True(s.CalloutsInFlight > 0, "no callout was waiting for its tick");
        Assert.NotEmpty(s.TakeSpoken());
    }
}
