using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A Soldier on a jump course (lane G7a): control points and no flag; the next stage is the nearest point its team may
/// take now, and the goal; short of the health its route's next link costs, it goes to a cabinet on its floor and back,
/// or waits in one; a fall leaves the goal where it was; a route with no way for long enough is given up; the end ends
/// it; with a flag on the map the branch scores nothing. On the synthetic field, the follower's reports put in the book
/// as the main thread would.
/// </summary>
public sealed class TFCourseTests
{
    private const int Red = TFField.Red, Blue = TFField.Blue;
    private const float Floor = TFDecider.RocketJumpHealthFloor;

    // Three stages along the field: the first red's, the second the next, the third needing the second.
    private static readonly AiVector Stage1 = new(800f, 256f, 0f), Stage2 = new(1600f, 256f, 0f), Stage3 = new(2400f, 256f, 0f);
    private static readonly AiVector At = new(1000f, 256f, 0f);

    /// <summary>A cabinet's zone for every team at x 1100..1228 on the bot's floor.</summary>
    private static readonly TeamZone Cabinet = new(0, ZoneKind.Resupply, new AiVector(1100f, 0f, 0f), new AiVector(1228f, 128f, 128f));

    private static readonly AiVector CabinetMid = new(1164f, 64f, 0f);

    private static ObjectiveKnowledge Points(bool secondOurs = false, bool thirdOurs = false, bool flag = false)
    {
        Objective P(int id, AiVector at, bool ours, bool next)
            => new(id, ObjectiveKind.Capture, ours ? Red : 0, next ? (1 << Red) | (1 << Blue) : 0, ours ? ObjectiveState.Won : ObjectiveState.Open, at);
        Objective[] points =
        [
            P(20, Stage1, true, false),
            P(21, Stage2, secondOurs, !secondOurs),
            P(22, Stage3, thirdOurs, secondOurs && !thirdOurs),
        ];
        return new ObjectiveKnowledge(flag ? [.. points, .. TFField.Ctf().All] : [.. points]);
    }

    private static (TFField Field, JumpBook Book, NavLinks Links) Course(TeamZone[]? zones = null, ItemSpawn[]? packs = null, TFClass? playerClass = null)
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 40f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        var f = new TFField(tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f }, jumps: book, zones: zones ?? [Cabinet], packs: packs ?? []);
        return (f, book, links);
    }

    private static SelfState Bot(AiVector at, int health = 200, TFClass playerClass = TFClass.Soldier) => TFField.Self(at, health: health, playerClass: playerClass);

    /// <summary>The follower's report, said before the next decide, that the route to <paramref name="goal"/> has no way and what it needs.</summary>
    private static void NoWay(TFField f, JumpBook book, NavLinks links, AiVector goal, float need)
        => book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, goal, false) { NoWay = true, Need = need });

    [Fact]
    public void TheNextStageIsTheNearestPointTheTeamMayTakeNowAndItIsTheGoal()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Points());
        Assert.Equal(TFDecider.Course, f.Branch);
        Assert.Equal((true, Stage2), (f.Decider.Doing.HasGoal, f.Decider.Doing.Goal));
        // Beside the third, which needs the second: still the second.
        f.Step(Bot(Stage3 - new AiVector(100f, 0f, 0f)), Points());
        Assert.Equal(Stage2, f.Decider.Doing.Goal);
        // The second taken, the third is next, though the bot is nearer the first.
        f.Step(Bot(At), Points(secondOurs: true));
        Assert.Equal(Stage3, f.Decider.Doing.Goal);
    }

    [Fact]
    public void WithAFlagOnTheMapTheCourseScoresNothingAndCtfIsAsItWas()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Points(flag: true));
        Assert.NotEqual(TFDecider.Course, f.Branch);
        Assert.Null(f.Decider.Stage);
        // The control: the same bot with the flags alone does what it does with the points beside them.
        var (ctf, _, _) = Course();
        ctf.Step(Bot(At), TFField.Ctf());
        Assert.Equal((ctf.Branch, ctf.Decider.Doing.Goal), (f.Branch, f.Decider.Doing.Goal));
    }

    [Fact]
    public void ABotThatDoesNotRocketJumpRunsNoCourse()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At, playerClass: TFClass.Scout), Points());
        Assert.NotEqual(TFDecider.Course, f.Branch);
    }

    [Fact]
    public void ShortOfTheHealthItsNextLinkCostsTheBotGoesToTheCabinetAndThenBackToTheStage()
    {
        var (f, book, links) = Course();
        f.Step(Bot(At), Points());
        NoWay(f, book, links, Stage2, 40f);
        int hurt = (int)(Floor + 40f) - 1;
        f.Step(Bot(At, health: hurt), Points());
        Assert.Equal((TFDecider.Course, CabinetMid), (f.Branch, f.Decider.Doing.Goal));
        Assert.Equal("to the cabinet for the jump", f.Decider.Doing.Verdict);
        // The control: with the health for it, the same report sends it on to the stage.
        f.Step(Bot(At, health: (int)(Floor + 40f)), Points());
        Assert.Equal(Stage2, f.Decider.Doing.Goal);
    }

    [Fact]
    public void InACabinetsZoneTheBotWaitsThereUntilItsHealthCoversTheLink()
    {
        var (f, book, links) = Course();
        f.Step(Bot(At), Points());
        NoWay(f, book, links, Stage2, 40f);
        IntentCommand waiting = f.Step(Bot(CabinetMid + new AiVector(0f, 0f, 1f), health: 100), Points());
        Assert.Equal((TFDecider.Course, false, MoveKind.Hold), (f.Branch, f.Decider.Doing.HasGoal, waiting.Move.Kind));
        f.Step(Bot(CabinetMid + new AiVector(0f, 0f, 1f), health: 200), Points());
        Assert.Equal((true, Stage2), (f.Decider.Doing.HasGoal, f.Decider.Doing.Goal));
    }

    [Fact]
    public void AHealthPackOnItsFloorServesAndACabinetOnAnotherFloorDoesNot()
    {
        var below = new TeamZone(0, ZoneKind.Resupply, new AiVector(1100f, 0f, -300f), new AiVector(1228f, 128f, -172f));
        var pack = new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(1300f, 300f, 0f), 10f);
        var (f, book, links) = Course(zones: [below], packs: [pack]);
        f.Step(Bot(At), Points());
        NoWay(f, book, links, Stage2, 40f);
        f.Step(Bot(At, health: 100), Points());
        Assert.Equal(pack.Position, f.Decider.Doing.Goal);

        // With the cabinet below alone: nowhere to heal on its floor, so it cannot go on.
        var (g, gBook, gLinks) = Course(zones: [below]);
        g.Step(Bot(At), Points());
        NoWay(g, gBook, gLinks, Stage2, 40f);
        g.Step(Bot(At, health: 100), Points());
        Assert.Equal(1, g.Decider.CourseQuits);
        Assert.NotEqual(TFDecider.Course, g.Branch);
    }

    [Fact]
    public void AFallLeavesTheGoalWhereItWas()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Points());
        Assert.Equal(Stage2, f.Decider.Doing.Goal);
        // Put back by a reset (or fallen and still going): the stage is not taken, so it is still the goal.
        f.Step(Bot(new AiVector(300f, 256f, 0f)), Points());
        Assert.Equal((TFDecider.Course, Stage2), (f.Branch, f.Decider.Doing.Goal));
    }

    [Fact]
    public void ARouteWithNoWayForLongerThanItBearsIsGivenUpAndTriedAgainLater()
    {
        var (f, book, links) = Course();
        f.Step(Bot(At), Points());
        NoWay(f, book, links, Stage2, 0f);
        int decides = (int)(TFDecider.CourseGiveUpSeconds / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < decides; i++)
            f.Step(Bot(At), Points());
        Assert.Equal((0, TFDecider.Course), (f.Decider.CourseQuits, f.Branch));
        f.Step(Bot(At), Points());
        f.Step(Bot(At), Points());
        Assert.Equal(1, f.Decider.CourseQuits);
        Assert.NotEqual(TFDecider.Course, f.Branch);
        // A link next for the stage after the quit's time: the course again.
        int quit = (int)(TFDecider.CourseQuitSeconds / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < quit; i++)
            f.Step(Bot(At), Points());
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, Stage2, false));
        f.Step(Bot(At), Points());
        Assert.Equal((TFDecider.Course, Stage2), (f.Branch, f.Decider.Doing.Goal));
    }

    [Fact]
    public void TheEndEndsItTheBotStaysWhereItIs()
    {
        var (f, _, _) = Course();
        IntentCommand done = f.Step(Bot(Stage3), Points(secondOurs: true, thirdOurs: true));
        Assert.Equal((TFDecider.Course, false, MoveKind.Hold), (f.Branch, f.Decider.Doing.HasGoal, done.Move.Kind));
        Assert.Equal("course finished", f.Decider.Doing.Verdict);
        Assert.Null(f.Decider.Stage);
    }
}
