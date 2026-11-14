using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The book the brains read (plan_bots_teams_tf2_part_g G.5 step 6): which bot a plan serves, what
/// is asked of the planner and how often, and what stands of an answer of none. No simulator: the
/// plans here are written by hand.
/// </summary>
public class JumpBookTests
{
    private static readonly AiVector Goal = new(3000f, 100f, 0f), East = new(1f, 0f, 0f);

    /// <summary>A plan east along y = 100 from a takeoff at x = 1,000 to a landing at 2,200, with 160 of slide.</summary>
    private static JumpPlan Plan(float takeoff = 1000f, float y = 100f, AiVector? goal = null)
        => new(new JumpRecipe(new Vector(takeoff, y, 0f), new Vector(takeoff + 1200f, y, 0f), 160f, 35f, 180f, -3, true), goal ?? Goal, Slide: 160f, Seconds: 2.4f,
               AirSeconds: 1.8f, WalkSeconds: 5.6f, Health: 29f, Robustness: 0.95f, Touch: new Vector(takeoff + 1190f, y, 0f), Apex: 200f);

    private static JumpStretchAsk Ask(float x, float y = 100f, AiVector? heading = null, AiVector? goal = null, int tick = 100)
        => new(new AiVector(x, y, 0f), heading ?? East, goal ?? Goal, new Tick(tick));

    /// <summary>A book that holds <paramref name="plan"/>, taken and answered as a worker would.</summary>
    private static JumpBook Holding(JumpPlan plan, out JumpPlan kept)
    {
        var book = new JumpBook(new JumpBookSettings());
        Assert.Null(book.Advise(Ask(plan.Recipe.Takeoff.X - 200f, plan.Recipe.Takeoff.Y, goal: plan.Goal)));
        Assert.True(book.TryTake(out JumpStretchAsk ask, out int generation));
        kept = book.Answer(ask, generation, new JumpPlanned(plan, JumpRefusal.None, 100))!;
        return book;
    }

    [Fact]
    public void APlanServesABotOnItsLineBeforeItsTakeoffGoingToItsGoal()
    {
        JumpBook book = Holding(Plan(), out JumpPlan kept);
        Assert.True(kept.Id > 0);
        Assert.Same(kept, book.Advise(Ask(800f)));
        Assert.Same(kept, book.Plan(kept.Id));
        Assert.Equal(1, book.Counts.Advised);
    }

    [Fact]
    public void APlanServesNoBotGoingSomewhereElse()
        => Assert.Null(Holding(Plan(), out _).Advise(Ask(800f, goal: new AiVector(3000f, 900f, 0f))));

    [Fact]
    public void APlanServesNoBotOffItsLine()
    {
        JumpBook book = Holding(Plan(), out JumpPlan kept);
        float aside = book.Settings.AsideMax;
        Assert.Same(kept, book.Advise(Ask(800f, 100f + aside - 2f)));
        Assert.Null(book.Advise(Ask(800f, 100f + aside + 2f)));
    }

    [Fact]
    public void APlanServesNoBotHeadingAnotherWay()
    {
        JumpBook book = Holding(Plan(), out _);
        Assert.Null(book.Advise(Ask(800f, heading: new AiVector(0f, 1f, 0f))));
        Assert.Null(book.Advise(Ask(800f, heading: new AiVector(-1f, 0f, 0f))));
    }

    [Fact]
    public void APlanIsBegunNoFurtherBeforeItsTakeoffThanTheApproachGoes()
    {
        JumpBook book = Holding(Plan(), out JumpPlan kept);
        float reach = book.Settings.ApproachMax;
        Assert.Null(book.Advise(Ask(1000f - reach - 8f)));
        Assert.Same(kept, book.Advise(Ask(1000f - reach + 8f)));
    }

    [Fact]
    public void APlanIsBegunNoLaterThanAShareOfItsSlidePastItsTakeoff()
    {
        JumpBook book = Holding(Plan(), out JumpPlan kept);
        float late = kept.Slide * book.Settings.LateShare;
        Assert.Same(kept, book.Advise(Ask(1000f + late - 4f)));
        Assert.Null(book.Advise(Ask(1000f + late + 4f)));
    }

    [Fact]
    public void APlanThatAllowsAWaitIsBegunNoLaterThanWhereABotWouldStopForItsView()
    {
        // Two hold runs before the slide's end: here 32 u before the takeoff.
        JumpBook book = Holding(Plan() with { Slide = 96f, HoldRun = 64f, HoldSeconds = 1f }, out JumpPlan kept);
        Assert.Equal(96f - 2f * 64f, book.Latest(kept));
        Assert.Same(kept, book.Advise(Ask(1000f - 32f - 4f)));
        Assert.Null(book.Advise(Ask(1000f - 32f + 4f)));
        // With room to spare past the takeoff it is that far past it: further than the share of the slide a plan with no wait allows.
        JumpBook roomy = Holding(Plan() with { Slide = 320f, HoldRun = 64f, HoldSeconds = 1f }, out JumpPlan long_);
        Assert.True(roomy.Latest(long_) > long_.Slide * roomy.Settings.LateShare);
        Assert.Same(long_, roomy.Advise(Ask(1000f + 192f - 4f)));
        Assert.Null(roomy.Advise(Ask(1000f + 192f + 4f)));
    }

    [Fact]
    public void ABotPastWhereAPlanMayBeBegunAsksAboutTheStretchAheadOfIt()
    {
        JumpBook book = Holding(Plan() with { Slide = 96f, HoldRun = 64f, HoldSeconds = 1f }, out _);
        long asked = book.Counts.Asked;
        Assert.Null(book.Advise(Ask(1000f - 8f)));
        Assert.Equal(asked + 1, book.Counts.Asked);
    }

    [Fact]
    public void AStretchNothingIsKnownOfIsQueuedOnceHoweverOftenItIsAskedAbout()
    {
        var book = new JumpBook(new JumpBookSettings());
        int cues = 0;
        book.Queued = () => cues++;
        for (int i = 0; i < 10; i++)
            Assert.Null(book.Advise(Ask(500f + i * 8f, tick: 100 + i * 6)));
        Assert.Equal((1, 1L, 1), (cues, book.Counts.Asked, book.Counts.Waiting));

        // While a worker has it, too.
        Assert.True(book.TryTake(out _, out _));
        Assert.Null(book.Advise(Ask(600f)));
        Assert.Equal((1, 1L, 1), (cues, book.Counts.Asked, book.Counts.Waiting));
    }

    [Fact]
    public void AStretchFurtherOnIsAnotherStretch()
    {
        var book = new JumpBook(new JumpBookSettings());
        book.Advise(Ask(500f));
        book.Advise(Ask(500f + book.Settings.RefusalReach + 8f));
        book.Advise(Ask(500f, goal: new AiVector(0f, 2000f, 0f)));
        Assert.Equal(3, book.Counts.Asked);
    }

    [Fact]
    public void AnAnswerOfNoneStandsAndIsNotAskedForAgainUntilItsTimeIsOut()
    {
        var book = new JumpBook(new JumpBookSettings());
        book.Advise(Ask(500f, tick: 100));
        Assert.True(book.TryTake(out JumpStretchAsk ask, out int generation));
        Assert.Null(book.Answer(ask, generation, new JumpPlanned(null, JumpRefusal.Ceiling, 140)));
        Assert.Equal((1L, 1L), (book.Counts.Refused, book.Refused(JumpRefusal.Ceiling)));

        book.Advise(Ask(520f, tick: 100 + book.Settings.RefusalTicks - 1));
        Assert.Equal(1, book.Counts.Asked);
        book.Advise(Ask(520f, tick: 100 + book.Settings.RefusalTicks));
        Assert.Equal(2, book.Counts.Asked);
    }

    [Fact]
    public void ABotThatWasTurningIsAskedAboutAgainSoon()
    {
        var book = new JumpBook(new JumpBookSettings());
        book.Advise(Ask(500f, tick: 100));
        Assert.True(book.TryTake(out JumpStretchAsk ask, out int generation));
        book.Answer(ask, generation, new JumpPlanned(null, JumpRefusal.Turning, 0));
        book.Advise(Ask(520f, tick: 100 + book.Settings.PassingTicks - 1));
        Assert.Equal(1, book.Counts.Asked);
        book.Advise(Ask(520f, tick: 100 + book.Settings.PassingTicks));
        Assert.Equal(2, book.Counts.Asked);
    }

    [Fact]
    public void ABotComingUpOnAPlansLineWaitsForItAndAsksForNoOther()
    {
        JumpBook book = Holding(Plan(), out _);
        long asked = book.Counts.Asked;
        // Past the approach's reach but coming up on the plan: nothing to begin, and nothing asked.
        Assert.Null(book.Advise(Ask(1000f - book.Settings.WaitFor + 8f)));
        Assert.Equal(asked, book.Counts.Asked);
        // Further back than it waits for, it is a stretch of its own.
        Assert.Null(book.Advise(Ask(1000f - book.Settings.WaitFor - 200f)));
        Assert.Equal(asked + 1, book.Counts.Asked);
    }

    [Fact]
    public void TheQueueIsShortAndWhatDoesNotFitIsDroppedAndCounted()
    {
        var book = new JumpBook(new JumpBookSettings());
        for (int i = 0; i < book.Settings.Asks + 3; i++)
            book.Advise(Ask(500f, goal: new AiVector(3000f, 500f * i, 0f)));
        Assert.Equal((book.Settings.Asks, 3L, book.Settings.Asks), ((int)book.Counts.Asked, book.Counts.Dropped, book.Counts.Waiting));
    }

    [Fact]
    public void AnotherLevelForgetsEverythingAndAnAnswerForTheOldOneIsDropped()
    {
        JumpBook book = Holding(Plan(), out JumpPlan kept);
        book.Advise(Ask(500f, goal: new AiVector(0f, 2000f, 0f)));
        Assert.True(book.TryTake(out JumpStretchAsk old, out int before));
        book.Clear();
        Assert.Null(book.Plan(kept.Id));
        Assert.Null(book.Answer(old, before, new JumpPlanned(Plan(), JumpRefusal.None, 100)));
        Assert.Equal((0, 0), (book.Counts.Plans, book.Counts.Waiting));
        Assert.Null(book.Advise(Ask(800f)));
    }

    /// <summary>
    /// What a decide pays to ask: with the book as full as it gets (96 plans, 128 answers of none, the queue full),
    /// a read that finds nothing for its stretch, in the thread's own CPU time. The bound is many times what it
    /// reads (a few microseconds in debug), so a loaded box does not turn it red; a search in the read would be
    /// tens of thousands of times over it (a search is some 200 ms in debug).
    /// </summary>
    [Fact]
    public void AReadOfAFullBookCostsADecideMicrosecondsNotASearch()
    {
        var settings = new JumpBookSettings();
        var book = new JumpBook(settings);
        for (int i = 0; i < settings.Plans + settings.Refusals; i++)
        {
            var goal = new AiVector(3000f, 500f * i, 0f);
            Assert.Null(book.Advise(Ask(500f, goal: goal)));
            Assert.True(book.TryTake(out JumpStretchAsk ask, out int generation));
            book.Answer(ask, generation, i < settings.Plans ? new JumpPlanned(Plan(goal: goal), JumpRefusal.None, 100) : new JumpPlanned(null, JumpRefusal.Ceiling, 100));
        }

        for (int i = 0; i < settings.Asks; i++)
            book.Advise(Ask(500f, goal: new AiVector(-3000f, 500f * i, 0f)));
        Assert.Equal((settings.Plans, settings.Asks), (book.Counts.Plans, book.Counts.Waiting));

        Func<double> clock = SourceSharp.Ai.Server.Senses.ThreadCpuClock.Micros() ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() * 1e6 / System.Diagnostics.Stopwatch.Frequency);
        const int Reads = 5000;
        double best = double.MaxValue;
        for (int trial = 0; trial < 5; trial++)
        {
            double start = clock();
            for (int i = 0; i < Reads; i++)
                book.Advise(Ask(700f + i % 7, y: 9000f, goal: new AiVector(9000f, 9000f, 0f), tick: 100 + i));
            best = Math.Min(best, (clock() - start) / Reads);
        }

        Assert.True(best <= 100d, $"a read of the full book cost {best:F1} microseconds");
        // Nothing was queued by those reads (the queue is full) and each was counted as dropped: the read went the whole way through the book.
        Assert.True(book.Counts.Dropped >= Reads);
    }

    [Fact]
    public void EachPlanKeptHasANumberOfItsOwnAndTheOldestGoesWhenTheBookIsFull()
    {
        var book = new JumpBook(new JumpBookSettings { Plans = 3 });
        var ids = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            book.Advise(Ask(500f, goal: new AiVector(3000f, 500f * i, 0f)));
            Assert.True(book.TryTake(out JumpStretchAsk ask, out int generation));
            ids.Add(book.Answer(ask, generation, new JumpPlanned(Plan(goal: ask.Goal), JumpRefusal.None, 100))!.Id);
        }

        Assert.Equal(5, ids.Distinct().Count());
        Assert.Equal(3, book.Counts.Plans);
        Assert.Null(book.Plan(ids[0]));
        Assert.NotNull(book.Plan(ids[4]));
    }
}
