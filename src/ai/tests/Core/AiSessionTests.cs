using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class AiSessionTests
{
    // Slot 2 decides at ticks where (tick + 2) % 6 == 0: 4, 10, 16, ...
    private static readonly ActorId Bot = new(2, 1);

    private static AiSession Session() => new(new InlineTransport(), new NoRecording(), 8, 4);

    private static void Publish(AiSession s, int tick, bool withView = true)
    {
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
        if (withView)
            snap.Add(Bot, new SelfState(default, default, default, 100, 0, 0, 0, 0, SelfFlags.Alive));
        s.Publish(snap);
    }

    [Fact]
    public void ADecideTickLeavesAHoldFromThatTick()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        Publish(s, 4);
        Assert.True(s.TryLatestIntent(2, out IntentCommand i) && i.FromTick == new Tick(4) && i.Move.Kind == MoveKind.Hold,
                    "the bot holds from tick 4");
    }

    [Fact]
    public void AnotherTickRunsNoStep()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        Publish(s, 5);
        Assert.Equal((0L, false), (s.Counters.StepsRun, s.TryLatestIntent(2, out _)));
    }

    [Fact]
    public void EventsReachTheBotsNextStep()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        s.Push(AiEvent.Spawned(Bot, new Tick(1), default));
        s.Push(AiEvent.Killed(Bot, new Tick(2), ActorId.None));
        Publish(s, 4);
        Assert.Equal(2L, s.Counters.EventsProcessed);
    }

    [Fact]
    public void AnEventForNoBotIsDroppedAndCounted()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        s.Push(AiEvent.Spawned(new ActorId(3, 1), new Tick(1), default));
        Assert.Equal(1L, s.Counters.EventsDropped);
    }

    [Fact]
    public void AnEventForAnEarlierLifeIsDropped()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        s.Push(AiEvent.Spawned(new ActorId(2, 99), new Tick(1), default));
        Assert.Equal(1L, s.Counters.EventsDropped);
    }

    [Fact]
    public void AStepWithNoViewIsSkippedAndCounted()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        Publish(s, 4, withView: false);
        Assert.Equal((1L, 0L), (s.Counters.StepsSkipped, s.Counters.StepsRun));
    }

    [Fact]
    public void ASkippedStepsEventsWaitForTheNextStep()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        s.Push(AiEvent.Spawned(Bot, new Tick(1), default));
        Publish(s, 4, withView: false);
        Publish(s, 10);
        Assert.Equal(1L, s.Counters.EventsProcessed);
    }

    [Fact]
    public void ARemovedBotHasNoIntent()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        Publish(s, 4);
        s.RemoveBot(Bot);
        Assert.False(s.TryLatestIntent(2, out _), "the removed bot's intent went with it");
    }

    [Fact]
    public void ASlotHoldsOneBot()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        Assert.Throws<InvalidOperationException>(() => s.AddBot(new ActorId(2, 2), 1, []));
    }

    [Fact]
    public void TheInlineTransportRunsTheStepOnTheCaller()
    {
        var spy = new RunnerSpy();
        var s = new AiSession(spy, new NoRecording(), 8, 4);
        s.AddBot(Bot, 1, []);
        Publish(s, 4);
        Assert.Equal((Environment.CurrentManagedThreadId, 1L), (spy.Thread, s.Counters.StepsRun));
    }

    /// <summary>The inline transport, with the thread each step ran on noted.</summary>
    private sealed class RunnerSpy : IAiTransport, IBrainStepRunner
    {
        private readonly InlineTransport _inline = new();
        private IBrainStepRunner? _inner;

        public int Thread { get; private set; } = -1;

        public void Dispatch(BrainStepJob job, IBrainStepRunner runner)
        {
            _inner = runner;
            _inline.Dispatch(job, this);
        }

        public void Run(BrainStepJob job)
        {
            Thread = Environment.CurrentManagedThreadId;
            _inner!.Run(job);
        }
    }
}
