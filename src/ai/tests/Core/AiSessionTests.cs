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

    /// <summary>ai_step's hold: a held session runs no step and keeps the bot's events for the step it lets run.</summary>
    [Fact]
    public void AHeldSessionStepsOnlyWhenLetAndKeepsTheEvents()
    {
        AiSession s = Session();
        s.AddBot(Bot, 1, []);
        s.Stepping = true;
        s.Push(AiEvent.Spawned(Bot, new Tick(1), default));
        Publish(s, 4);
        Publish(s, 10);
        Assert.Equal((0L, 2L), (s.Counters.StepsRun, s.Counters.StepsHeld));

        s.StepTicks = 6;   // every slot's decide once
        for (int t = 11; t <= 16; t++)
            Publish(s, t);
        Assert.Equal((1L, 1L, 0), (s.Counters.StepsRun, s.Counters.EventsProcessed, s.StepTicks));

        Publish(s, 22);
        Assert.Equal((1L, 3L), (s.Counters.StepsRun, s.Counters.StepsHeld));
    }

    /// <summary>A held tick is not recorded, so a replay of a stepped session steps exactly what ran.</summary>
    [Fact]
    public void ASteppedSessionReplaysToTheSameIntents()
    {
        byte[] rec = SourceSharp.Ai.Tests.Record.Recordings.Make(s =>
        {
            s.AddBot(Bot, 1, []);
            s.Stepping = true;
            for (int t = 1; t <= 40; t++)
            {
                if (t == 20)
                    s.StepTicks = 6;
                Publish(s, t);
            }
        });
        AiReplayResult r = SourceSharp.Ai.Tests.Record.Recordings.Replay(rec);
        Assert.Equal(1, r.RecordedIntentCount);
        Assert.True(r.Identical, "the replay ran a step the stepped session held");
    }

    /// <summary>The inline transport, with the thread each step ran on noted.</summary>
    private sealed class RunnerSpy : IAiTransport
    {
        public int Thread { get; private set; } = -1;

        public bool Inline => true;

        public bool Dispatch(IAiWork work)
        {
            Thread = Environment.CurrentManagedThreadId;
            work.Run();
            return true;
        }
    }
}
