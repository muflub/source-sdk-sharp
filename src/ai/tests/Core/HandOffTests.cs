using System.Diagnostics;
using Xunit;

namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// §8.2's hand-off facts over the pooled transport (§2.5, §2.7): a brain that
/// stalls for a second never holds up the main thread; its lane drops steps it
/// can no longer run in time, counted, never running one on a newer view;
/// every event pushed is processed in the end; results come back in
/// hand-over order. (The applier's halves are BotCommandApplierTests':
/// AnExpiredIntentIsDroppedAndTheBotHolds, AWeaponIsSelectedBySlotExactlyOnce,
/// ADeadBotDropsItsIntentAndPressesFireToRespawn.)
/// </summary>
public sealed class HandOffTests
{
    // Slot 2 decides where (tick + 2) % 6 == 0: 4, 10, 16, ...
    private static readonly ActorId Bot = new(2, 1);
    private static readonly ActorId Other = new(3, 1);
    private static readonly byte[] Medium = [4, 4, 4, 3, 3, 3, 4, 3, 4, 3];

    /// <summary>A decider that sleeps through one decide, and notes every tick it ran at.</summary>
    private sealed class Stall(int at, int sleepMs) : IDecider
    {
        public List<(Tick Tick, Tick ViewTick)> Ran { get; } = new();

        public BtTrace Trace { get; } = new();

        public DoingRecord Doing => default;

        public IReadOnlyList<string> NodeNames => [];

        public IReadOnlyList<int> NodeBranches => [];

        public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
        {
            Ran.Add((tick, view.Tick));
            if (tick.Value == at)
                Thread.Sleep(sleepMs);
            return IntentCommand.Hold(view.Bot, tick, seq);
        }
    }

    private static AiSession Pooled(AiWorkerPool pool, Func<BrainSetup, IDecider?> deciders, int slots = 8)
        => new(new PooledTransport(pool), new NoRecording(), slots, 4)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = deciders,
        };

    private static double Publish(AiSession s, int tick, params ActorId[] bots)
    {
        long start = Stopwatch.GetTimestamp();
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
        foreach (ActorId bot in bots)
            snap.Add(bot, new SelfState(default, default, default, 100, 0, 0, 0, 0, SelfFlags.Alive));
        s.Publish(snap);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    [Fact]
    public void ASecondsStallNeverHoldsUpTheMainThreadAndLosesNoEvent()
    {
        using var pool = new AiWorkerPool();
        pool.Start(1);
        Stall? stall = null;
        AiSession s = Pooled(pool, setup => stall = new Stall(at: 10, sleepMs: 1000));
        s.AddBot(Bot, 1, Medium);

        double before = 0, during = 0;
        // Through tick 94, a decide tick of slot 2's, so no event waits at the end.
        for (int t = 1; t <= 94; t++)
        {
            s.Push(AiEvent.SoundHeard(Bot, new Tick(t), SoundKind.Footstep, new AiVector(t, 0, 0), 0.5f, ActorId.None, -1));
            double ms = Publish(s, t, Bot);
            if (t < 10)
                before = Math.Max(before, ms);
            else
                during = Math.Max(during, ms);
        }

        Assert.True(s.Counters.InFlight > 0, "the control: the stalled step was still running when the main thread moved on");
        s.Drain(wait: true);

        // Measured, not asserted zero: the main thread's publish stayed far
        // inside the stall (it never waited for the second).
        Assert.True(during < 250, $"a publish during the stall took {during:F1} ms (before it: {before:F1} ms)");
        Assert.True(s.Counters.StepsSkipped > 0, "the late lane dropped steps it could not run in time");
        Assert.Equal(0, s.Counters.StepsFaulted);
        Assert.All(stall!.Ran, r => Assert.Equal(r.Tick, r.ViewTick));
        Assert.Equal(s.Counters.EventsIn, s.Counters.EventsProcessed);
        Assert.Equal(94L, s.Counters.EventsProcessed);
    }

    [Fact]
    public void ResultsComeBackInHandOverOrderWhateverFinishesFirst()
    {
        using var pool = new AiWorkerPool();
        pool.Start(4);
        var order = new List<IntentCommand>();
        var sink = new IntentOrder(order);
        // The bot in slot 2 is slow at its first decide; slot 3's step is quick.
        var s = new AiSession(new PooledTransport(pool), sink, 8, 4)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup => new Stall(at: setup.Bot.Slot == 2 ? 4 : -1, sleepMs: 200),
        };
        s.AddBot(Bot, 1, Medium);
        s.AddBot(Other, 2, Medium);
        for (int t = 1; t <= 30; t++)
            Publish(s, t, Bot, Other);
        s.Drain(wait: true);

        Assert.True(order.Count > 4, "the control: both bots decided");
        for (int i = 1; i < order.Count; i++)
        {
            (int, int) a = (order[i - 1].FromTick.Value, order[i - 1].Bot.Slot), b = (order[i].FromTick.Value, order[i].Bot.Slot);
            Assert.True(a.CompareTo(b) < 0, $"intent {i} ({b}) came back after ({a})");
        }
    }

    /// <summary>
    /// A level's end (§2.5): the pool stops and is joined (a lane already
    /// running finishes its queue first), and every step handed over is
    /// accounted for, run or counted skipped; none is left in flight.
    /// </summary>
    [Fact]
    public void ALevelsEndAccountsForEveryStepHandedOver()
    {
        using var pool = new AiWorkerPool();
        pool.Start(1);
        AiSession s = Pooled(pool, _ => new Stall(at: 4, sleepMs: 300));
        s.AddBot(Bot, 1, Medium);
        int handed = 0;
        for (int t = 1; t <= 70; t++)
        {
            Publish(s, t, Bot);
            if (DecideSchedule.IsDecideTick(new Tick(t), Bot.Slot))
                handed++;
        }

        Assert.True(s.Counters.InFlight > 0, "the control: steps were in flight at the level's end");
        pool.Stop();
        s.Abandon();
        Assert.Equal(0, pool.Alive);
        Assert.Equal(0, s.Counters.InFlight);
        Assert.Equal(handed, s.Counters.StepsRun + s.Counters.StepsSkipped);
        Assert.True(s.Counters.StepsSkipped > 0, "the lane overflowed behind the stall and said so");
    }

    private sealed class IntentOrder(List<IntentCommand> into) : IAiRecordSink
    {
        public void Intent(in IntentCommand intent) => into.Add(intent);

        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) { }

        public void BotRemoved(ActorId bot) { }

        public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches) { }

        public void Event(in AiEvent e) { }

        public void Snapshot(AiSnapshot snapshot) { }

        public void UserCmd(in AiUserCmd cmd) { }

        public void Frame(in AiActorFrame frame) { }

        public void Observed(in AiEvent e) { }
    }
}
