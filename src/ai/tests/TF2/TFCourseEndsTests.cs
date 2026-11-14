using System.Collections.Immutable;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A course of ends (lane Ge): a capture area whose point the level does not place is a course's end, as
/// jump_academy2_rc9a's are; the Course branch goes to the easiest end its title names, then the nearest, of those it
/// has not stood in this life; standing in one reaches it; every one reached finishes the course; a death or the
/// game's respawn forgets them; no way to one sets it aside for the next; the level's points are no stages; a flag
/// turns it off. On the synthetic field, whose floor is at z 0.
/// </summary>
public sealed class TFCourseEndsTests
{
    private const int Red = TFField.Red, Blue = TFField.Blue;

    private static readonly AiVector At = new(1000f, 256f, 0f);

    private static TFCourseEnd End(int index, string title, float west) => new(index, title, TFCourseEnds.RankOf(title), new AiVector(west, 0f, 0f), new AiVector(west + 128f, 128f, 128f));

    /// <summary>Three ends: a medium one near the bot, an easy one further, a medium one far.</summary>
    private static readonly ImmutableArray<TFCourseEnd> Ends =
    [
        End(0, "Medium Ledge by lane Ge", 1200f),
        End(1, "Easy Ledge by lane Ge", 2000f),
        End(2, "Medium Gap by lane Ge", 3000f),
    ];

    private static AiVector Floor(TFCourseEnd end) => new(end.Middle.X, end.Middle.Y, 0f);

    /// <summary>The HUD on such a level: one point, locked, at the hub (jump_academy2's).</summary>
    private static ObjectiveKnowledge Hub(bool open = false, bool flag = false)
    {
        var point = new Objective(30, ObjectiveKind.Capture, 0, open ? (1 << Red) | (1 << Blue) : 0, open ? ObjectiveState.Open : ObjectiveState.Locked, new AiVector(500f, 256f, 0f));
        return new ObjectiveKnowledge(flag ? [point, .. TFField.Ctf().All] : [point]);
    }

    private static (TFField Field, JumpBook Book, NavLinks Links) Course(ImmutableArray<TFCourseEnd>? ends = null)
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 40f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        var f = new TFField(tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f }, jumps: book, zones: [], packs: [], ends: ends ?? Ends);
        return (f, book, links);
    }

    private static SelfState Bot(AiVector at, int health = 200) => TFField.Self(at, health: health);

    private static MapEntities Placed(params MapEntity[] all) => new(all, null);

    private static MapEntity Area(string point, float west)
        => new("trigger_capture_area", null, default, new AiVector(west, 0f, 0f), new AiVector(west + 128f, 128f, 128f), Keys: [new(TFCourseEnds.PointKey, point)]);

    private static MapEntity Point(string name) => new("team_control_point", name, default, null, null);

    [Fact]
    public void AnAreaNamingAPointTheLevelDoesNotPlaceIsAnEndAndOneNamingAPlacedPointIsNot()
    {
        ImmutableArray<TFCourseEnd> ends = TFCourseEnds.Of(Placed(Point("stage1"), Area("STAGE1", 0f), Area("Easy Pogo by RNC1839", 512f)));
        TFCourseEnd only = Assert.Single(ends);
        Assert.Equal((0, "Easy Pogo by RNC1839", 1, 512f), (only.Index, only.Title, only.Rank, only.Mins.X));
    }

    [Fact]
    public void AnAreaThatNamesNoPointAtAllIsNoEnd()
        => Assert.Empty(TFCourseEnds.Of(Placed(Area("", 0f))));

    [Fact]
    public void ATitlesWordSaysHowHardTheCourseIs()
    {
        Assert.Equal([0, 1, 2, 3, 4, 2],
                     new[] { "Basic by GentlePuppet", "Easy Walls by pardinensis", "Medium Sync by Plopy", "Hard Pogo by Syro", "insane-bounce", "Ctap 691" }
                         .Select(TFCourseEnds.RankOf));
    }

    [Fact]
    public void ABodyStandingOnTheVolumesFloorOrBesideItsEdgeTouchesItAndOneUnderItDoesNot()
    {
        TFCourseEnd end = Ends[0];
        Assert.True(end.Holds(Floor(end)));
        Assert.True(end.Holds(new AiVector(end.Mins.X - 20f, 64f, 0f)));
        Assert.False(end.Holds(new AiVector(end.Mins.X - 30f, 64f, 0f)));
        Assert.False(end.Holds(new AiVector(end.Middle.X, 64f, -90f)));
    }

    [Fact]
    public void TheNextEndIsTheEasiestThenTheNearestAndItsMiddleIsTheGoal()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Hub());
        Assert.Equal((TFDecider.Course, 1, Ends[1].Middle), (f.Branch, f.Decider.EndTarget, f.Decider.Doing.Goal));
        Assert.Equal("to the course's end", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void StandingInAnEndReachesItAndTheNextIsTaken()
    {
        var (f, _, _) = Course();
        f.Step(Bot(Floor(Ends[1])), Hub());
        Assert.True(f.Decider.EndReached(1));
        Assert.Equal(1, f.Decider.CourseEndsReached);
        // Of the two medium ends left, the nearer to where it stands (800 u against 1,000).
        Assert.Equal((0, Ends[0].Middle), (f.Decider.EndTarget, f.Decider.Doing.Goal));
        // Walked off it, it stays reached; from nearer the far one, that one is next.
        f.Step(Bot(new AiVector(2800f, 256f, 0f)), Hub());
        Assert.Equal((true, 2), (f.Decider.EndReached(1), f.Decider.EndTarget));
    }

    [Fact]
    public void EveryEndReachedFinishesTheCourseAndTheBotStays()
    {
        var (f, _, _) = Course();
        foreach (TFCourseEnd end in Ends)
            f.Step(Bot(Floor(end)), Hub());
        IntentCommand done = f.Step(Bot(Floor(Ends[2])), Hub());
        Assert.Equal((TFDecider.Course, false, MoveKind.Hold, -1), (f.Branch, f.Decider.Doing.HasGoal, done.Move.Kind, f.Decider.EndTarget));
        Assert.Equal("course finished", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void ADeathForgetsTheEnds()
    {
        var (f, _, _) = Course();
        f.Step(Bot(Floor(Ends[1])), Hub());
        Assert.True(f.Decider.EndReached(1));
        f.Step(Bot(Floor(Ends[1])) with { Flags = SelfFlags.None }, Hub());
        f.Step(Bot(Floor(Ends[1])) with { Flags = SelfFlags.None }, Hub());
        // Alive again off its spawn: the death alone forgets them.
        f.Step(Bot(At), Hub());
        Assert.Equal((false, 1, 1), (f.Decider.EndReached(1), f.Decider.EndTarget, f.Decider.CourseEndLives));
    }

    [Fact]
    public void PutOnItsTeamsSpawnByTheGameTheBotForgetsTheEndsAndWalkingThereItDoesNot()
    {
        var (f, _, _) = Course();
        f.Step(Bot(Floor(Ends[1])), Hub());
        // A round's restart: on its spawn from 1,900 u away a decide later.
        f.Step(Bot(TFField.RedSpawn), Hub());
        Assert.Equal((false, 1), (f.Decider.EndReached(1), f.Decider.CourseEndLives));

        // The controls: moved as fast to a place off the spawn, then walked onto it.
        var (g, _, _) = Course();
        g.Step(Bot(Floor(Ends[1])), Hub());
        g.Step(Bot(TFField.RedSpawn + new AiVector(200f, 0f, 0f)), Hub());
        g.Step(Bot(TFField.RedSpawn), Hub());
        Assert.Equal((true, 0), (g.Decider.EndReached(1), g.Decider.CourseEndLives));
    }

    [Fact]
    public void NoWayToAnEndForLongerThanItBearsSetsItAsideAndTheNextIsTaken()
    {
        var (f, book, links) = Course();
        f.Step(Bot(At), Hub());
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, Ends[1].Middle, false) { NoWay = true });
        int decides = (int)(TFDecider.CourseGiveUpSeconds / (DecideSchedule.Period * 0.015f));
        for (int i = 0; i < decides + 2; i++)
            f.Step(Bot(At), Hub());
        Assert.Equal((1, true), (f.Decider.CourseQuits, f.Decider.EndAside(1)));
        // Past the roam the quit's decide fell to (its commitment is a second).
        for (int i = 0; i < 12; i++)
            f.Step(Bot(At), Hub());
        Assert.Equal((TFDecider.Course, 0, Ends[0].Middle), (f.Branch, f.Decider.EndTarget, f.Decider.Doing.Goal));
    }

    [Fact]
    public void OnACourseOfEndsTheLevelsPointsAreNoStages()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Hub(open: true));
        Assert.Null(f.Decider.Stage);
        Assert.Equal((TFDecider.Course, Ends[1].Middle), (f.Branch, f.Decider.Doing.Goal));
    }

    [Fact]
    public void WithAFlagOnTheMapTheEndsScoreNothing()
    {
        var (f, _, _) = Course();
        f.Step(Bot(At), Hub(flag: true));
        Assert.NotEqual(TFDecider.Course, f.Branch);
        Assert.Equal(-1, f.Decider.EndTarget);
    }
}
