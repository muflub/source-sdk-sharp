using Xunit;

namespace SourceSharp.Ai.Tests.Bt;

/// <summary>
/// The behaviour-tree runtime (plan_hl2mp_ai §9 Phase 5 step 1, §8.2): a leaf
/// runs once per decide, a failed child aborts a sequence, a utility selector
/// runs the best, ties are seed-stable, the three decorators, and the trace.
/// </summary>
public sealed class BtTests
{
    private sealed class State
    {
        public readonly List<string> Ran = new();
        public float A = 1f, B = 1f;
        public BtStatus Answer = BtStatus.Success;
    }

    private static BtNode<State> Leaf(string name, BtStatus status = BtStatus.Success)
        => new BtAction<State>(name, (s, _) => { s.Ran.Add(name); return status; });

    private static BtRun Run(ulong seed = 1) => new(new AiRandom(seed), new BtTrace(), 0.015f);

    [Fact]
    public void ALeafSharedByTwoBranchesRunsOncePerDecide()
    {
        BtNode<State> shared = Leaf("shared", BtStatus.Failure);
        var tree = new BehaviorTree<State>(new BtSelector<State>("root",
            new BtSequence<State>("a", shared, Leaf("after a")),
            new BtSequence<State>("b", shared, Leaf("after b")),
            Leaf("last")));
        var s = new State();

        tree.Decide(s, Run(), new Tick(1));

        Assert.Equal(["shared", "last"], s.Ran);
        Assert.Equal(1, shared.Runs);
    }

    [Fact]
    public void AFailedChildAbortsASequence()
    {
        var tree = new BehaviorTree<State>(new BtSequence<State>("root", Leaf("one"), Leaf("two", BtStatus.Failure), Leaf("three")));
        var s = new State();

        Assert.Equal(BtStatus.Failure, tree.Decide(s, Run(), new Tick(1)));
        Assert.Equal(["one", "two"], s.Ran);
    }

    [Fact]
    public void AUtilitySelectorRunsTheBestAndFallsToTheNextOnAFailure()
    {
        var s = new State { A = 0.9f, B = 0.4f };
        var sel = new BtUtilitySelector<State>("root",
            new BtScored<State>("A", x => x.A, Leaf("a", BtStatus.Failure)),
            new BtScored<State>("B", x => x.B, Leaf("b")),
            new BtScored<State>("zero", _ => 0f, Leaf("never")));
        var tree = new BehaviorTree<State>(sel);

        Assert.Equal(BtStatus.Success, tree.Decide(s, Run(), new Tick(1)));
        Assert.Equal(["a", "b"], s.Ran);
        Assert.Equal("B", sel.Chosen?.Name);

        s.Ran.Clear();
        s.A = 0.3f;
        tree.Decide(s, Run(), new Tick(2));
        Assert.Equal(["b"], s.Ran);
    }

    private static string TieWinner(ulong seed, int decides = 5)
    {
        var sel = new BtUtilitySelector<State>("root",
            new BtScored<State>("A", x => x.A, Leaf("a")),
            new BtScored<State>("B", x => x.B, Leaf("b")),
            new BtScored<State>("C", x => x.B, Leaf("c")));
        var tree = new BehaviorTree<State>(sel);
        var s = new State();
        BtRun run = Run(seed);
        string? first = null;
        for (int t = 1; t <= decides; t++)
        {
            tree.Decide(s, run, new Tick(t));
            first ??= sel.Chosen!.Name;
            Assert.Equal(first, sel.Chosen!.Name);
        }

        return first!;
    }

    [Fact]
    public void TiesAreStableForOneSeedAndDifferAcrossSeeds()
    {
        Assert.Equal(TieWinner(7), TieWinner(7));
        var winners = Enumerable.Range(1, 32).Select(i => TieWinner((ulong)i)).ToHashSet();
        Assert.True(winners.Count >= 2, $"over 32 seeds the tie went to {string.Join(",", winners)}");
    }

    [Fact]
    public void ACooldownFailsForItsSecondsAfterASuccess()
    {
        var s = new State();
        var tree = new BehaviorTree<State>(new BtCooldown<State>("cd", 0.15f, Leaf("x")));
        BtRun run = Run();
        var answers = Enumerable.Range(1, 14).Select(t => tree.Decide(s, run, new Tick(t))).ToArray();

        Assert.Equal(BtStatus.Success, answers[0]);
        Assert.All(answers[1..10], a => Assert.Equal(BtStatus.Failure, a));   // 0.15 s = 10 ticks
        Assert.Equal(BtStatus.Success, answers[10]);
    }

    [Fact]
    public void OnceRunsUntilItsChildSucceedsThenFailsUntilReset()
    {
        var s = new State { Answer = BtStatus.Running };
        var tree = new BehaviorTree<State>(new BtOnce<State>("once", new BtAction<State>("x", (x, _) => x.Answer)));
        BtRun run = Run();

        Assert.Equal(BtStatus.Running, tree.Decide(s, run, new Tick(1)));
        s.Answer = BtStatus.Success;
        Assert.Equal(BtStatus.Success, tree.Decide(s, run, new Tick(2)));
        Assert.Equal(BtStatus.Failure, tree.Decide(s, run, new Tick(3)));
        tree.Reset();
        Assert.Equal(BtStatus.Success, tree.Decide(s, run, new Tick(4)));
    }

    [Fact]
    public void ATimeoutFailsAChildRunningPastItsSeconds()
    {
        var s = new State { Answer = BtStatus.Running };
        var tree = new BehaviorTree<State>(new BtTimeout<State>("t", 0.1f, new BtAction<State>("x", (x, _) => x.Answer)));
        BtRun run = Run();
        var answers = Enumerable.Range(1, 9).Select(t => tree.Decide(s, run, new Tick(t))).ToArray();

        Assert.All(answers[..7], a => Assert.Equal(BtStatus.Running, a));
        Assert.Equal(BtStatus.Failure, answers[7]);   // 7 ticks of 0.015 s is past 0.1 s
    }

    [Fact]
    public void TheTraceKeepsEachVisitedNodeInOrderWithItsAnswerAndValue()
    {
        var s = new State { A = 0.7f };
        var tree = new BehaviorTree<State>(new BtUtilitySelector<State>("root",
            new BtScored<State>("A", x => x.A, new BtSequence<State>("seq", new BtCondition<State>("ok", _ => true), Leaf("act")))));
        BtRun run = Run();

        tree.Decide(s, run, new Tick(9));
        run.Trace.End(new DecisionReason(4, 0.7f, 0f, 0.25f));

        BtTraceEntry[] e = run.Trace.EntriesAt(0).ToArray();
        Assert.Equal([0, 1, 2, 3, 4], e.Select(x => (int)x.NodeId));
        Assert.All(e, x => Assert.Equal(BtStatus.Success, x.Status));
        Assert.Equal((0.7f, 0.7f, 1f), (e[0].Value, e[1].Value, e[3].Value));
        Assert.Equal((new Tick(9), 0.25f), (run.Trace.TickAt(0), run.Trace.ReasonAt(0).AmmoTerm));
    }

    [Fact]
    public void TheTraceRingKeepsTheLastSixtyFourDecides()
    {
        var tree = new BehaviorTree<State>(Leaf("x"));
        BtRun run = Run();
        for (int t = 1; t <= 100; t++)
            tree.Decide(new State(), run, new Tick(t));

        Assert.Equal(BtTrace.Decides, run.Trace.Count);
        Assert.Equal((new Tick(100), new Tick(37)), (run.Trace.TickAt(0), run.Trace.TickAt(63)));
    }

    [Fact]
    public void ASteadyDecideAllocatesNothing()
    {
        var s = new State { A = 0.5f, B = 0.6f };
        var tree = new BehaviorTree<State>(new BtUtilitySelector<State>("root",
            new BtScored<State>("A", x => x.A, new BtCooldown<State>("cd", 1f, new BtAction<State>("a", (_, _) => BtStatus.Success))),
            new BtScored<State>("B", x => x.B, new BtSequence<State>("s", new BtCondition<State>("c", _ => true),
                                                                       new BtAction<State>("b", (_, _) => BtStatus.Running)))));
        BtRun run = Run();
        for (int t = 1; t <= 10; t++)
            tree.Decide(s, run, new Tick(t));

        int now = 10;
        Assert.Equal(0L, SourceSharp.Ai.Tests.Core.SteadyAllocation.Measure(() => tree.Decide(s, run, new Tick(++now)), 1000));
    }
}
