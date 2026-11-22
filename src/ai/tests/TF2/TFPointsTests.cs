using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A control-point level (lane Gc): with a teammate on its scoreboard a bot holds the points stance or the team stance
/// for a stretch and chooses again after it; its chance of the points is half, leaned by its Aggression over its
/// Teamwork and by what its teammates are doing; a point in play that matters pulls a bot in the team stance to the
/// points; the points stance goes to an own point being taken, an own point with an enemy on it, the point the team may
/// capture, an own point the enemy may capture, in that order, and stands on it; the team stance goes to a call for
/// help, the teammate furthest forward, or back to the nearest; a bot alone runs the course as before.
/// </summary>
public sealed class TFPointsTests
{
    private const int Red = TFField.Red, Blue = TFField.Blue, Mate = 3;
    private const float DecideSeconds = DecideSchedule.Period * 0.015f;

    private static readonly AiVector PointA = new(1600f, 256f, 0f), PointB = new(2400f, 256f, 0f);
    private static readonly AiVector At = new(1000f, 256f, 0f);
    private static readonly ActorId MateId = new(Mate, 1), Enemy = new(5, 1);

    /// <summary>Two points: A (id 20) and B (id 21), each as asked: its owner, the teams that may capture it now, its state.</summary>
    private static ObjectiveKnowledge Level(int aOwner = Blue, int aMay = 1 << Red, ObjectiveState aState = ObjectiveState.Open,
                                            int bOwner = Blue, int bMay = 0, ObjectiveState bState = ObjectiveState.Locked, AiVector? aAt = null)
        => new([
            new Objective(20, ObjectiveKind.Capture, aOwner, aMay, aState, aAt ?? PointA),
            new Objective(21, ObjectiveKind.Capture, bOwner, bMay, bState, PointB),
        ]);

    /// <summary>Red's defence: both points red's, A open to blue, B locked behind it.</summary>
    private static ObjectiveKnowledge Defence(ObjectiveState aState = ObjectiveState.Open)
        => Level(aOwner: Red, aMay: 1 << Blue, aState: aState, bOwner: Red, bMay: 0, bState: ObjectiveState.Locked);

    private static TFField Field(int[]? mates = null, ulong seed = 7, Func<DecisionParams, DecisionParams>? tune = null, IJumpAdvisor? jumps = null)
        => new(seed: seed, mates: mates ?? [Mate], jumps: jumps, tune: p => (tune ?? (q => q))(p with { GlanceRate = 0f, ScoreNoise = 0f }));

    private static SelfState Bot(AiVector at, TFClass playerClass = TFClass.Soldier) => TFField.Self(at, playerClass: playerClass);

    private static (ActorId, AiVector, AiVector, int) Seen(ActorId id, AiVector at, int team = Red) => (id, at, default, team);

    private static int Decides(float seconds) => (int)MathF.Ceiling(seconds / DecideSeconds);

    // ------------------------------------------------------------------ the chance

    [Fact]
    public void ThePointsChanceIsHalfLeanedByAggressionOverTeamwork()
    {
        Assert.Equal(0.5f, TFDecider.PointsChance(1f, 1f, 0, 0), 4);
        Assert.Equal(0.5f * 1.2f / 0.8f, TFDecider.PointsChance(1.2f, 0.8f, 0, 0), 4);
        Assert.Equal(0.5f * 0.8f / 1.2f, TFDecider.PointsChance(0.8f, 1.2f, 0, 0), 4);
    }

    [Fact]
    public void HalfTheTeammatesOnAPointCapTheChanceAndNoneOnOneLeavesTheDrives()
    {
        Assert.Equal(TFDecider.EnoughOnPoint, TFDecider.PointsChance(1f, 1f, 2, 1), 4);
        Assert.Equal(TFDecider.EnoughOnPoint, TFDecider.PointsChance(1.3f, 0.7f, 3, 3), 4);
        // None of them on a point, or one of three: neither rule, the lean alone.
        Assert.Equal(0.5f, TFDecider.PointsChance(1f, 1f, 2, 0), 4);
        Assert.Equal(0.5f, TFDecider.PointsChance(1f, 1f, 3, 1), 4);
    }

    [Fact]
    public void NoLeaningDropsAStance()
    {
        Assert.Equal(1f - TFDecider.StanceLeast, TFDecider.PointsChance(3f, 0.1f, 0, 0), 4);
        Assert.Equal(TFDecider.StanceLeast, TFDecider.PointsChance(0.1f, 3f, 0, 0), 4);
    }

    [Fact]
    public void TheBotsChanceIsMadeFromItsDrivesAndTheTeammatesItSeesOnAPoint()
    {
        // The teammate seen on A, the point the team may capture: one of one on a point caps the chance.
        TFField on = Field();
        on.Step(Bot(At), Level(), Seen(MateId, PointA));
        Assert.Equal(TFDecider.EnoughOnPoint, on.Decider.LastPointsChance, 4);
        // Seen away from every point: nobody on one leaves the drives alone (the first pass's lift to 0.65 went).
        TFField off = Field();
        off.Step(Bot(At), Level(), Seen(MateId, new AiVector(400f, 256f, 0f)));
        Assert.Equal(TFDecider.PointsChance(off.Params.FightDrive, off.Params.EscortDrive, 0, 0), off.Decider.LastPointsChance, 4);
        Assert.NotEqual(TFDecider.EnoughOnPoint, off.Decider.LastPointsChance);
        // Seen nowhere: the drives alone.
        TFField leaned = Field(tune: p => p with { FightDrive = 1.2f, EscortDrive = 0.8f });
        leaned.Step(Bot(At), Level());
        Assert.Equal(0.75f, leaned.Decider.LastPointsChance, 4);
    }

    // ------------------------------------------------------------------ the level

    [Fact]
    public void ABotWithNoTeammateOnItsScoreboardRunsTheCourseAsBefore()
    {
        var book = new JumpBook(new JumpBookSettings());
        TFField alone = Field(mates: [], jumps: book);
        alone.Step(Bot(At), Level());
        Assert.Equal((false, TFStance.None, TFDecider.Course, PointA), (alone.Decider.OnPoints, alone.Decider.Stance, alone.Branch, alone.Decider.Doing.Goal));
        // And one that does not rocket jump runs none, as before.
        TFField scout = Field(mates: [], jumps: book);
        scout.Step(Bot(At, TFClass.Scout), Level());
        Assert.NotEqual(TFDecider.Course, scout.Branch);
        Assert.NotEqual(TFDecider.PointsBranch, scout.Branch);
    }

    [Fact]
    public void ALevelWithCourseEndsIsNoPointsLevel()
    {
        var end = new SourceSharp.Ai.TF2.Knowledge.TFCourseEnd(0, "easy", 1, new AiVector(2000f, 0f, 0f), new AiVector(2100f, 512f, 128f));
        var f = new TFField(mates: [Mate], jumps: new JumpBook(new JumpBookSettings()), ends: [end], tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        f.Step(Bot(At), Level());
        Assert.Equal((false, TFStance.None, TFDecider.Course), (f.Decider.OnPoints, f.Decider.Stance, f.Branch));
    }

    [Fact]
    public void WithATeammateOnItsScoreboardTheBotHoldsAStanceOnAPointsLevel()
    {
        TFField f = Field();
        f.Step(Bot(At), Level());
        Assert.True(f.Decider.OnPoints);
        Assert.NotEqual(TFStance.None, f.Decider.Stance);
        Assert.Contains(f.Branch, (string[])[TFDecider.PointsBranch, TFDecider.TeamBranch, TFDecider.Roam]);
        // A flag on the map: no points level, and CTF as it was.
        TFField ctf = Field();
        ctf.Step(Bot(At), new ObjectiveKnowledge([.. Level().All, .. TFField.Ctf().All]));
        Assert.Equal((false, TFStance.None), (ctf.Decider.OnPoints, ctf.Decider.Stance));
    }

    // ------------------------------------------------------------------ the stretch

    [Fact]
    public void AStanceIsHeldForItsStretchThenChosenAgainAndBothAreTaken()
    {
        // A lone wolf: a bot that would call a rally with nobody to gather takes the points at once (the third pass).
        TFField f = Follower();
        f.Step(Bot(At), Level());
        TFStance first = f.Decider.Stance;
        for (int i = 0; i < Decides(TFDecider.StanceHoldMin) - 2; i++)
            f.Step(Bot(At), Level());
        Assert.Equal((first, 0), (f.Decider.Stance, f.Decider.StanceChanges));

        var seen = new HashSet<TFStance>();
        for (int i = 0; i < Decides(20 * (TFDecider.StanceHoldMin + TFDecider.StanceHoldSpread)); i++)
        {
            f.Step(Bot(At), Level());
            seen.Add(f.Decider.Stance);
        }

        Assert.Equal([TFStance.Points, TFStance.Team], seen.Order());
        Assert.InRange(f.Decider.StanceChanges, 3, 40);
        Assert.True(f.Decider.PointsStanceDecides > 0 && f.Decider.TeamStanceDecides > 0);
    }

    // ------------------------------------------------------------------ the points stance

    [Fact]
    public void ThePointsStanceGoesToThePointTheTeamMayCaptureAndStandsOnIt()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At, TFClass.Scout), Level());
        Assert.Equal((TFDecider.PointsBranch, PointA, "to the point"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
        IntentCommand on = f.Step(Bot(PointA + new AiVector(20f, 0f, 0f), TFClass.Scout), Level());
        Assert.Equal((TFDecider.PointsBranch, "on the point", MoveKind.Hold), (f.Branch, f.Decider.Doing.Verdict, on.Move.Kind));
    }

    [Fact]
    public void ABotThatRocketJumpsTakesThePointsByTheCoursesWay()
    {
        TFField f = Field(jumps: new JumpBook(new JumpBookSettings()));
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level());
        Assert.Equal((TFDecider.PointsBranch, PointA, "to the next stage"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void AnOwnPointBeingTakenComesBeforeThePointTheTeamMayCapture()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level(aOwner: Blue, aMay: 1 << Red, bOwner: Red, bMay: 1 << Blue, bState: ObjectiveState.Capturing));
        Assert.Equal((PointB, "to our point"), (f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
        // The control: B open, not being taken, and A is the goal.
        TFField g = Field();
        g.Decider.HoldStance(TFStance.Points);
        g.Step(Bot(At), Level(aOwner: Blue, aMay: 1 << Red, bOwner: Red, bMay: 1 << Blue, bState: ObjectiveState.Open));
        Assert.Equal(PointA, g.Decider.Doing.Goal);
    }

    [Fact]
    public void AnEnemySeenOnAnOwnPointComesBeforeThePointTheTeamMayCapture()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level(aOwner: Blue, aMay: 1 << Red, bOwner: Red, bMay: 1 << Blue, bState: ObjectiveState.Open), Seen(Enemy, PointB, Blue));
        Assert.Equal(PointB, f.Decider.PointsGoal?.Position);
    }

    [Fact]
    public void AQuietOwnPointIsNoGoal()
    {
        // Red's defence with nothing happening on A: no point to go to, the points stance has nothing to do.
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Defence());
        Assert.Null(f.Decider.PointsGoal);
        Assert.NotEqual(TFDecider.PointsBranch, f.Branch);
        // The control: A being taken is still the goal.
        TFField g = Field();
        g.Decider.HoldStance(TFStance.Points);
        g.Step(Bot(At), Defence(ObjectiveState.Capturing));
        Assert.Equal(PointA, g.Decider.PointsGoal?.Position);
    }

    [Fact]
    public void AStageGivenUpSendsTheBotToTheTeam()
    {
        var (g, ids) = Grids.Parse("..... .....");
        var book = new JumpBook(new JumpBookSettings());
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 40f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        TFField f = Field(jumps: book);
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level(), Seen(MateId, new AiVector(400f, 256f, 0f)));
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, PointA, false) { NoWay = true, Need = 0f });
        for (int i = 0; i < Decides(TFDecider.CourseGiveUpSeconds) + 2; i++)
            f.Step(Bot(At), Level(), Seen(MateId, new AiVector(400f, 256f, 0f)));
        Assert.Equal(1, f.Decider.CourseQuits);
        f.Step(Bot(At), Level(), Seen(MateId, new AiVector(400f, 256f, 0f)));
        Assert.Equal((TFStance.Team, (AiVector?)new AiVector(400f, 256f, 0f)), (f.Decider.Stance, f.Decider.TeamPlace));
        // Once the commitment to what the quit fell to is past, the team.
        for (int i = 0; i < Decides(2f); i++)
            f.Step(Bot(At), Level(), Seen(MateId, new AiVector(400f, 256f, 0f)));
        Assert.Equal((TFStance.Team, TFDecider.TeamBranch), (f.Decider.Stance, f.Branch));
    }

    [Fact]
    public void APullDoesNotUndoAGivenUpStage()
    {
        var (g, ids) = Grids.Parse("..... .....");
        var book = new JumpBook(new JumpBookSettings());
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 40f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        TFField f = Field(jumps: book);
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level());
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, PointA, false) { NoWay = true, Need = 0f });
        for (int i = 0; i < Decides(TFDecider.CourseGiveUpSeconds) + 3; i++)
            f.Step(Bot(At), Level());
        Assert.Equal((1, TFStance.Team), (f.Decider.CourseQuits, f.Decider.Stance));
        int changes = f.Decider.StanceChanges;
        // The team's capture of A under way, within reach: while the stage is given up it pulls nobody back and forth.
        for (int i = 0; i < Decides(5f); i++)
            f.Step(Bot(At), Level(aState: ObjectiveState.Capturing));
        Assert.Equal((TFStance.Team, changes, 0), (f.Decider.Stance, f.Decider.StanceChanges, f.Decider.StancePulls));
    }

    // ------------------------------------------------------------------ pulls

    [Fact]
    public void AnOwnPointBeingTakenWithinReachPullsABotInTheTeamStance()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Defence(ObjectiveState.Capturing));
        Assert.Equal((TFStance.Points, 1, PointA), (f.Decider.Stance, f.Decider.StancePulls, f.Decider.Doing.Goal));
        // Out of reach: no pull (a lone wolf, which no rule but the pull moves to the points).
        TFField far = Follower();
        far.Decider.HoldStance(TFStance.Team);
        far.Step(Bot(PointA - new AiVector(TFDecider.PullReach + 64f, 0f, 0f)), Defence(ObjectiveState.Capturing));
        Assert.Equal((TFStance.Team, 0), (far.Decider.Stance, far.Decider.StancePulls));
    }

    [Fact]
    public void TheTeamsCaptureUnderWayPullsABotInTheTeamStance()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Level(aState: ObjectiveState.Capturing));
        Assert.Equal((TFStance.Points, 1), (f.Decider.Stance, f.Decider.StancePulls));
        // The control: A open and no one on it, no pull (a lone wolf, which no rule but the pull moves to the points).
        TFField g = Follower();
        g.Decider.HoldStance(TFStance.Team);
        g.Step(Bot(At), Level());
        Assert.Equal(TFStance.Team, g.Decider.Stance);
    }

    [Fact]
    public void AnEnemySeenOnAnOwnPointPullsABotInTheTeamStance()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Defence(), Seen(Enemy, PointA + new AiVector(100f, 0f, 0f), Blue));
        Assert.Equal((TFStance.Points, 1), (f.Decider.Stance, f.Decider.StancePulls));
        // The control: the enemy seen off the point, no pull.
        TFField g = Field();
        g.Decider.HoldStance(TFStance.Team);
        g.Step(Bot(At), Defence(), Seen(Enemy, PointA + new AiVector(TFDecider.OnPointUnits + 100f, 0f, 0f), Blue));
        Assert.Equal(TFStance.Team, g.Decider.Stance);
    }

    // ------------------------------------------------------------------ the team stance of a lone wolf (no rally heeded or called)

    /// <summary>A bot of Teamwork 2: it neither heeds nor calls a rally, and goes with the team as before.</summary>
    private static TFField Follower(int[]? mates = null) => Field(mates: mates, tune: p => p with { Teamwork = TFDecider.RallyIgnoreTeamwork });

    [Fact]
    public void TheTeamStanceGoesToTheTeammateFurthestForward()
    {
        // Two teammates: the nearer behind the bot's line to A, the further nearer A.
        TFField f = Follower(mates: [Mate, 4]);
        f.Decider.HoldStance(TFStance.Team);
        AiVector near = new(700f, 256f, 0f), ahead = new(1450f, 256f, 0f);
        f.Step(Bot(At), Level(), Seen(MateId, near), Seen(new ActorId(4, 1), ahead));
        Assert.Equal((TFDecider.TeamBranch, ahead, "to the team"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void ABotAheadOfItsTeamGoesBackToTheNearestTeammate()
    {
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        var behind = new AiVector(300f, 256f, 0f);
        f.Step(Bot(At), Level(), Seen(MateId, behind));
        Assert.Equal((behind, "back to the team"), (f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void WithTheTeamTheBotStaysThere()
    {
        // With the teammate 250 u off, apart enough: it holds where it is.
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand with = f.Step(Bot(At), Level(), Seen(MateId, At + new AiVector(250f, 0f, 0f)));
        Assert.Equal((TFDecider.TeamBranch, "with the team", MoveKind.Hold), (f.Branch, f.Decider.Doing.Verdict, with.Move.Kind));
    }

    [Fact]
    public void WithTheTeamTheBotStandsApartFromItsTeammate()
    {
        // Lane Gobj, the user: "and stand too close to each other". The teammate 100 u off: the bot steps to a spot at least
        // 200 u from it (over a rocket's splash), within the team's reach of it.
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        AiVector mate = At + new AiVector(100f, 0f, 0f);
        IntentCommand with = f.Step(Bot(At), Level(), Seen(MateId, mate));
        Assert.Equal((TFDecider.TeamBranch, "with the team", MoveKind.Follow), (f.Branch, f.Decider.Doing.Verdict, with.Move.Kind));
        AiVector spot = f.Decider.Doing.Goal;
        Assert.InRange(Flat(spot, mate), TFDecider.StandApartUnits, TFDecider.GroupStandUnits + TFDecider.StandArriveUnits);
        // There, it holds.
        Assert.Equal(MoveKind.Hold, f.Step(Bot(spot), Level(), Seen(MateId, mate)).Move.Kind);
    }

    [Fact]
    public void OnThePointTwoTeammatesStandApartInsideItsArea()
    {
        // A teammate already on A: the bot takes a spot on A at least 200 u from it, within the area's reach of A's place.
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        IntentCommand on = f.Step(Bot(PointA + new AiVector(-60f, 0f, 0f), TFClass.Scout), Level(), Seen(MateId, PointA));
        Assert.Equal((TFDecider.PointsBranch, "on the point", MoveKind.Follow), (f.Branch, f.Decider.Doing.Verdict, on.Move.Kind));
        Assert.InRange(Flat(f.Decider.Doing.Goal, PointA), TFDecider.StandApartUnits - 32f, TFDecider.PointStandUnits + TFDecider.StandArriveUnits);
        Assert.True(Flat(f.Decider.Doing.Goal, PointA) <= TFDecider.OnPointUnits);
    }

    private static float Flat(AiVector a, AiVector b) => new AiVector(a.X - b.X, a.Y - b.Y, 0f).Length;

    [Fact]
    public void ATeammatesCallForHelpIsWhereTheTeamIs()
    {
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        var call = new AiVector(2000f, 400f, 0f);
        f.Step(Bot(At), Level());
        f.Memory.Apply(AiEvent.Callout(f.Bot, new Tick(f.Tick + 1), MateId, new SayIntent(CalloutKind.Help, call, 0, 0, 0, 0, false)));
        f.Step(Bot(At), Level());
        Assert.Equal(call, f.Decider.TeamPlace);
        // Past the roaming's commitment (a second), the call; well within the bot's followSeconds (5 s at the least).
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal((call, "to the call"), (f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void ATeammateSeenWithinTheRoleDealsSecondsIsStillWhereItWasSeen()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        var seen = new AiVector(1450f, 256f, 0f);
        f.Step(Bot(At), Level(), Seen(MateId, seen));
        for (int i = 0; i < Decides(TFDecider.PersonSeenSeconds - 3f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal(seen, f.Decider.TeamPlace);
        for (int i = 0; i < Decides(6f); i++)
            f.Step(Bot(At), Level());
        Assert.Null(f.Decider.TeamPlace);
    }

    [Fact]
    public void ATeammateNotSeenWhereItWasSeenIsNoLongerThere()
    {
        // A 1,800 u past the teammate: within the front's reach, so the bot gathers (lane Gobj) and the team's place stands.
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        var seen = new AiVector(1600f, 256f, 0f);
        var a = new AiVector(3400f, 256f, 0f);
        f.Step(Bot(At), Level(aAt: a), Seen(MateId, seen));
        f.Step(Bot(At), Level(aAt: a));
        Assert.Equal(seen, f.Decider.TeamPlace);
        // Come to the place, and the teammate is not in sight there: it has gone, and the bot has no team to stay with.
        f.Step(Bot(seen + new AiVector(100f, 0f, 0f)), Level(aAt: a));
        Assert.Null(f.Decider.TeamPlace);
    }

    [Fact]
    public void WithNoTeammatePlacedATeammatesEnemyCalloutIsWhereTheTeamIsFighting()
    {
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Level());
        var fight = new AiVector(2200f, 300f, 0f);
        f.Memory.Apply(AiEvent.Callout(f.Bot, new Tick(f.Tick + 1), MateId, new SayIntent(CalloutKind.Enemy, fight, 1, 0, 0, 0, false)));
        f.Step(Bot(At), Level());
        Assert.Equal(fight, f.Decider.TeamPlace);
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal((TFDecider.TeamBranch, fight, "to the fight called"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void WithNoTeammatePlacedAPointWhoseCaptureIsUnderWayIsWhereTheTeamIsFighting()
    {
        // A out of the pull's reach, its capture by the team under way: the team stance goes there, the stance unchanged.
        var far = new AiVector(1000f + TFDecider.PullReach + 400f, 256f, 0f);
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Level(aState: ObjectiveState.Capturing, aAt: far));
        Assert.Equal((TFStance.Team, (AiVector?)far), (f.Decider.Stance, f.Decider.TeamPlace));
        // The control: A open, no fight there, no place.
        TFField g = Follower();
        g.Decider.HoldStance(TFStance.Team);
        g.Step(Bot(At), Level(aAt: far));
        Assert.Null(g.Decider.TeamPlace);
    }

    [Fact]
    public void WithNoTeammatesPlaceKnownTheBotRoams()
    {
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        f.Step(Bot(At), Level());
        Assert.Equal((TFStance.Team, null, TFDecider.Roam), (f.Decider.Stance, f.Decider.TeamPlace, f.Branch));
    }

    // ------------------------------------------------------------------ gather, then push (the second pass)

    private static readonly ActorId Mate4 = new(4, 1), Mate5 = new(5, 1);

    /// <summary>The teammate a calling bot sees in <see cref="Rallied"/>: 800 u behind it, so the rally (their middle) is beyond the rally's reach of it.</summary>
    private static readonly AiVector MateBehind = new(200f, 256f, 0f);

    /// <summary>Within a cell's width of <paramref name="expected"/> on the floor.</summary>
    private static void Near(AiVector expected, AiVector place)
    {
        Assert.InRange(place.X, expected.X - 64f, expected.X + 64f);
        Assert.InRange(place.Y, expected.Y - 64f, expected.Y + 64f);
    }

    private static AiEvent Heard(TFField f, ActorId from, CalloutKind kind, AiVector place, int ahead = 1)
        => AiEvent.Callout(f.Bot, new Tick(f.Tick + ahead), from, new SayIntent(kind, place, 0, 0, 0, Red, false));

    /// <summary>A bot that called its rally, a teammate seen behind it (<see cref="MateBehind"/>): the rally's place.</summary>
    private static AiVector Rallied(TFField f)
    {
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level(), Seen(MateId, MateBehind));
        Assert.Equal(CalloutKind.Rally, said.Say.Kind);
        return said.Say.Place;
    }

    [Fact]
    public void ABotInTheTeamStanceCallsARallyWhereItsTeamIsAndGoesThere()
    {
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level(), Seen(MateId, MateBehind));
        Assert.Equal((CalloutKind.Rally, true, Red), (said.Say.Kind, said.Say.Text, said.Say.Team));
        Assert.True(said.Say.DelayTicks >= AiSession.CalloutMinDelayTicks);
        // At the team's front, the bot itself (lane Gobj: not their middle), held back to 900 u short of A.
        Near(PointA - new AiVector(TFDecider.RallyShortUnits, 0f, 0f), said.Say.Place);
        Assert.Equal((TFTeamPlay.Gather, 1, (AiVector?)said.Say.Place), (f.Decider.TeamPlay, f.Decider.RalliesCalled, f.Decider.RallyPlace));
        // Its teammate now beside it, on the far side from the rally (so it waits for nobody on the way, and neither stands at it yet).
        for (int i = 0; i < Decides(1.5f); i++)
            Assert.False(f.Step(Bot(At), Level(), Seen(MateId, At + new AiVector(100f, 100f, 0f))).Say.Says);
        Assert.Equal((TFDecider.TeamBranch, said.Say.Place, "to the rally", 1), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict, f.Decider.RalliesCalled));
        IntentCommand at = f.Step(Bot(said.Say.Place), Level());
        Assert.Equal(("at the rally", MoveKind.Hold), (f.Decider.Doing.Verdict, at.Move.Kind));
    }

    [Fact]
    public void ALoneWolfCallsNoRallyAndABotOfTeamworkThreeDoes()
    {
        TFField f = Follower();
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level());
        Assert.Equal((false, 0, TFTeamPlay.None), (said.Say.Says, f.Decider.RalliesCalled, f.Decider.TeamPlay));
        TFField g = Field(tune: p => p with { Teamwork = 3f });
        g.Decider.HoldStance(TFStance.Team);
        Assert.Equal((CalloutKind.Rally, 1), (g.Step(Bot(At), Level(), Seen(MateId, MateBehind)).Say.Kind, g.Decider.RalliesCalled));
    }

    [Fact]
    public void ABotThatPlacesNoTeammateCallsNoRallyAndTakesThePoints()
    {
        // No teammate in sight nor seen lately: no team to gather.
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level());
        Assert.Equal((false, 0, TFStance.Points, 1), (said.Say.Says, f.Decider.RalliesCalled, f.Decider.Stance, f.Decider.AloneToPoints));
        // A teammate seen longer ago than the team stance places one is not placed either.
        TFField g = Field();
        g.Decider.HoldStance(TFStance.Points);
        g.Step(Bot(At), Level(), Seen(MateId, MateBehind));
        for (int i = 0; i < Decides(TFDecider.TeamSeenSeconds + 1f); i++)
            g.Step(Bot(At), Level());
        g.Decider.HoldStance(TFStance.Team);
        Assert.False(g.Step(Bot(At), Level()).Say.Says);
        Assert.Equal(TFStance.Points, g.Decider.Stance);
        // Nor one last seen where the bot now stands, not in sight there: it has gone.
        TFField h = Field();
        h.Decider.HoldStance(TFStance.Points);
        h.Step(Bot(At), Level(), Seen(MateId, At + new AiVector(0f, 100f, 0f)));
        h.Step(Bot(At), Level());
        h.Decider.HoldStance(TFStance.Team);
        Assert.False(h.Step(Bot(At), Level()).Say.Says);
        Assert.Equal(TFStance.Points, h.Decider.Stance);
    }

    [Fact]
    public void ATeammateSeenWithinTheTeamStancesSecondsIsGatheredWith()
    {
        // Seen 10 s ago 800 u behind: placed, so the bot calls a rally (at the front, itself, held back 900 u short of A) rather than taking the points.
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Points);
        f.Step(Bot(At), Level(), Seen(MateId, MateBehind));
        for (int i = 0; i < Decides(10f); i++)
            f.Step(Bot(At), Level());
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level());
        Assert.Equal((CalloutKind.Rally, TFStance.Team, 0), (said.Say.Kind, f.Decider.Stance, f.Decider.AloneToPoints));
        Near(PointA - new AiVector(TFDecider.RallyShortUnits, 0f, 0f), said.Say.Place);
    }

    [Fact]
    public void TheRallyMovesTowardThePointOnlyAsFarAsStaysOutOfTheEnemysSight()
    {
        // A at 3,000 u; the bot at 1,000 u and its teammate at 1,200 u: the team's front is the teammate, 1,800 u from A.
        var far = new AiVector(3000f, 256f, 0f);
        var mate = new AiVector(1200f, 256f, 0f);
        AiVector middle = mate;
        NavGraph floor = TFField.Floor();
        // No enemy known to see anything: it moves the most, 600 u.
        var open = new TFField(mates: [Mate], floor: floor, tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        open.Decider.HoldStance(TFStance.Team);
        Near(middle + new AiVector(TFDecider.RallyAdvanceUnits, 0f, 0f), open.Step(Bot(At), Level(aAt: far), Seen(MateId, mate)).Say.Place);
        // An enemy seen at B; the sight table has it see the cells from 1,480 u on: one step of 150 u is out of sight.
        var seen = new List<int>();
        for (int c = 0; c < floor.CellCount; c++)
        {
            if (floor.Cell(c).Centre.X >= 1480f)
                seen.Add(c);
        }

        var f = new TFField(mates: [Mate], floor: floor, sight: new Sees([.. seen]), tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level(aAt: far), Seen(MateId, mate), Seen(Enemy, PointB, Blue));
        Assert.Equal(CalloutKind.Rally, said.Say.Kind);
        Near(middle + new AiVector(TFDecider.RallyAdvanceStep, 0f, 0f), said.Say.Place);
        // Never within 900 u of the point: A at 1,900 u, 700 u past the front, holds the rally back to 1,000 u and leaves no step.
        var near = new TFField(mates: [Mate], floor: floor, tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        near.Decider.HoldStance(TFStance.Team);
        Near(new AiVector(1000f, 256f, 0f), near.Step(Bot(At), Level(aAt: new AiVector(1900f, 256f, 0f)), Seen(MateId, mate)).Say.Place);
    }

    [Fact]
    public void ARallyIsCalledAtTheTeamsFrontNotItsMiddle()
    {
        // Lane Gobj. The bot at 1,000 u, its teammate ahead at 1,400 u, A at 3,000 u: the front is the teammate, and the
        // rally goes on from it 600 u toward A (the middle, 1,200 u, would have it at 1,800 u; the bot itself at 1,600 u).
        var a = new AiVector(3000f, 256f, 0f);
        var ahead = new AiVector(1400f, 256f, 0f);
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(At), Level(aAt: a), Seen(MateId, ahead));
        Assert.Equal(CalloutKind.Rally, said.Say.Kind);
        Near(ahead + new AiVector(TFDecider.RallyAdvanceUnits, 0f, 0f), said.Say.Place);
    }

    [Fact]
    public void ATeamFarFromThePointGoesToTheFrontAndCallsNoRallyThere()
    {
        // Lane Gobj, the user's report: a team by its spawn after a round's start. The bot and its teammate at the west
        // end, A 3,700 u off: no rally by the spawn; the team stance goes to the front.
        var a = new AiVector(3800f, 256f, 0f);
        var start = new AiVector(100f, 256f, 0f);
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        for (int i = 0; i < Decides(1.5f); i++)
            Assert.False(f.Step(Bot(start), Level(aAt: a), Seen(MateId, start + new AiVector(0f, 100f, 0f))).Say.Says);
        Assert.Equal((TFStance.Team, TFTeamPlay.None, 0, (AiVector?)a), (f.Decider.Stance, f.Decider.TeamPlay, f.Decider.RalliesCalled, f.Decider.TeamPlace));
        Assert.Equal((TFDecider.TeamBranch, a, "to the front"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
        Assert.True(f.Decider.ToFrontDecides > 0);
        // Come within the front's reach of A (1,800 u), its teammate with it: the rally is called there.
        var near = new AiVector(2000f, 256f, 0f);
        IntentCommand said = f.Step(Bot(near), Level(aAt: a), Seen(MateId, near + new AiVector(0f, 100f, 0f)));
        Assert.Equal((CalloutKind.Rally, 1), (said.Say.Kind, f.Decider.RalliesCalled));
        Assert.True(Math.Abs(said.Say.Place.X - a.X) >= TFDecider.RallyShortUnits - 64f);
        Assert.True(said.Say.Place.X >= near.X - 64f);
    }

    [Fact]
    public void OnItsWayToTheFrontTheTeamStancesStretchHolds()
    {
        // Lane Gobj: the team far from A, the bot going to the front; past the longest stretch it is still in the team stance,
        // though its drives would take the points at nine choices in ten.
        var a = new AiVector(3800f, 256f, 0f);
        var start = new AiVector(100f, 256f, 0f);
        TFField f = Field(tune: p => p with { FightDrive = 3f, EscortDrive = 0.1f });
        f.Decider.HoldStance(TFStance.Team);
        for (int i = 0; i < Decides(TFDecider.StanceHoldMin + TFDecider.StanceHoldSpread + 2f); i++)
            f.Step(Bot(start), Level(aAt: a), Seen(MateId, start + new AiVector(0f, 100f, 0f)));
        Assert.Equal((TFStance.Team, 0, "to the front"), (f.Decider.Stance, f.Decider.StanceChanges, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void ABotFarFromThePointThatPlacesNoTeammateGoesToTheFrontInTheTeamStance()
    {
        // Lane Gobj: nobody placed and A 3,700 u off: the bot keeps the team stance and goes to the front; within the
        // front's reach of A, still alone, it takes the points.
        var a = new AiVector(3800f, 256f, 0f);
        var start = new AiVector(100f, 256f, 0f);
        TFField f = Field();
        f.Decider.HoldStance(TFStance.Team);
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(start), Level(aAt: a));
        Assert.Equal((TFStance.Team, 0, "to the front"), (f.Decider.Stance, f.Decider.AloneToPoints, f.Decider.Doing.Verdict));
        f.Step(Bot(new AiVector(2000f, 256f, 0f)), Level(aAt: a));
        Assert.Equal((TFStance.Points, 1), (f.Decider.Stance, f.Decider.AloneToPoints));
    }

    private sealed class Sees(params int[] cells) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => Array.IndexOf(cells, b) >= 0;

        public float Exposure(int cell) => 0f;
    }

    [Fact]
    public void ATeammatesRallyIsWhereTheBotGathersAndItCallsNone()
    {
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        var rally = new AiVector(500f, 256f, 0f);
        IntentCommand first = f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        Assert.Equal((false, (AiVector?)rally, TFTeamPlay.Gather), (first.Say.Says, f.Decider.RallyPlace, f.Decider.TeamPlay));
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal((rally, "to the rally", 0), (f.Decider.Doing.Goal, f.Decider.Doing.Verdict, f.Decider.RalliesCalled));
    }

    [Fact]
    public void OnItsWayToTheRallyABotKeepsWithItsGroupAWhile()
    {
        // A teammate's rally far ahead; a teammate seen 700 u behind the bot: it waits for them, then goes on.
        var far = new AiVector(3800f, 256f, 0f);
        var here = new AiVector(1500f, 256f, 0f);
        var behind = here - new AiVector(TFDecider.PushKeepUnits + 200f, 0f, 0f);
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        f.StepHearing(Bot(here), Level(aAt: far), [Heard(f, MateId, CalloutKind.Rally, new AiVector(3000f, 256f, 0f))],
                      new ActorFacts(MateId, behind, default, TFField.Launcher, ActorActivity.None, 1f, Red));
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(here), Level(aAt: far), Seen(MateId, behind));
        Assert.Equal((TFDecider.TeamBranch, "waiting on the way to the rally"), (f.Branch, f.Decider.Doing.Verdict));
        // A teammate near: on its way.
        f.Step(Bot(here), Level(aAt: far), Seen(MateId, here - new AiVector(200f, 0f, 0f)));
        Assert.Equal("to the rally", f.Decider.Doing.Verdict);
        // Waiting has its most.
        for (int i = 0; i < Decides(TFDecider.PushWaitMostSeconds + 1f); i++)
            f.Step(Bot(here), Level(aAt: far), Seen(MateId, behind));
        Assert.Equal("to the rally", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void ABotComesToARallyLateByItsTeamworkAndTheLeastIgnoresIt()
    {
        var rally = new AiVector(500f, 256f, 0f);
        TFField f = Field(tune: p => p with { Teamwork = 5f });
        f.Decider.HoldStance(TFStance.Team);
        f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        // Held, not yet come: five points under the cap, five seconds late.
        Assert.Equal(((AiVector?)rally, TFTeamPlay.None), (f.Decider.RallyPlace, f.Decider.TeamPlay));
        for (int i = 0; i < Decides(5f * TFDecider.RallyLatePerTeamwork - 1f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal(TFTeamPlay.None, f.Decider.TeamPlay);
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(At), Level());
        Assert.Equal(TFTeamPlay.Gather, f.Decider.TeamPlay);
        // Teamwork 2: it holds none, and calls none.
        TFField g = Field(tune: p => p with { Teamwork = TFDecider.RallyIgnoreTeamwork });
        g.Decider.HoldStance(TFStance.Team);
        IntentCommand said = g.StepHearing(Bot(At), Level(), [Heard(g, MateId, CalloutKind.Rally, rally)]);
        Assert.Equal((null, TFTeamPlay.None, false), (g.Decider.RallyPlace, g.Decider.TeamPlay, said.Say.Says));
    }

    /// <summary>
    /// Lane Groutes, fourth pass: a bot that comes to a teammate's rally late goes to the rally by its own way while late, not
    /// first to its teammates (live on cp_granary late bots walked "back to the team" 7 s from a rally called ahead of them).
    /// Its play stays none until its lateness is past, as ABotComesToARallyLateByItsTeamworkAndTheLeastIgnoresIt holds.
    /// </summary>
    [Fact]
    public void ABotLateToATeammatesRallyGoesToTheRallyNotBackToItsTeam()
    {
        var rally = new AiVector(1300f, 256f, 0f);
        TFField f = Field(tune: p => p with { Teamwork = 5f });
        f.Decider.HoldStance(TFStance.Team);
        f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        for (int i = 0; i < Decides(2f); i++)
        {
            // A teammate seen behind it: its team's place is back there.
            f.Step(Bot(At), Level(), Seen(MateId, MateBehind));
            Assert.Equal(TFTeamPlay.None, f.Decider.TeamPlay);
            Assert.Equal(("to the rally", rally), (f.Decider.Doing.Verdict, f.Decider.Doing.Goal));
        }

        // At the rally while still late: it waits there, looking about.
        IntentCommand at = f.Step(Bot(rally), Level());
        Assert.Equal(("at the rally", LookKind.Point), (f.Decider.Doing.Verdict, at.Look.Kind));
        Assert.Equal(TFTeamPlay.None, f.Decider.TeamPlay);
    }

    /// <summary>
    /// The user, 2026-11-20: "at the start of the round bots dont have to wait and assume all team members are going towards the
    /// objective directly". Held still by the round's start, then let go: a team-stance bot whose teammate stands behind it
    /// (mid-round, where it calls a rally: the control) calls none, holds none, waits for nobody, and goes for the point.
    /// </summary>
    [Fact]
    public void AtARoundsStartNoRallyIsCalledAndNoBotGoesToItsTeamOrWaits()
    {
        TFField f = Field(mates: [Mate, 4, 5]);
        f.Decider.HoldStance(TFStance.Team);
        for (int i = 0; i < Decides(1f); i++)
            Assert.False(f.Step(TFField.Self(At, extra: SelfFlags.Frozen), Level(), Seen(MateId, MateBehind)).Say.Says);
        for (int i = 0; i < Decides(4f); i++)
        {
            IntentCommand c = f.Step(Bot(At + new AiVector(i * 20f, 0f, 0f)), Level(), Seen(MateId, MateBehind));
            Assert.False(c.Say.Says);
            Assert.True(f.Decider.InOpening);
            Assert.Equal((TFTeamPlay.Push, (AiVector?)null), (f.Decider.TeamPlay, f.Decider.RallyPlace));
            Assert.DoesNotContain(f.Decider.Doing.Verdict, (string[])["to the team", "back to the team", "at the rally", "to the rally", "to the front", "waiting for the push"]);
            // Its push is for point A, the point in play (the way there is the Course branch's, a Soldier's).
            Assert.Equal(20, f.Decider.PushTarget);
        }

        Assert.Equal(1, f.Decider.OpeningPushes);
        Assert.Equal(0, f.Decider.RalliesCalled);
        // The control: the same bot and teammate mid-round, never held by a round's start: it calls a rally.
        TFField g = Field(mates: [Mate, 4, 5]);
        Assert.Equal(CalloutKind.Rally, Rallied(g) == default ? CalloutKind.None : CalloutKind.Rally);
        Assert.False(g.Decider.InOpening);
    }

    /// <summary>After its death the opening is over for the bot: back from the dead it gathers by the usual rules.</summary>
    [Fact]
    public void ARoundsOpeningEndsWithTheBotsDeath()
    {
        TFField f = Field(mates: [Mate, 4, 5]);
        f.Decider.HoldStance(TFStance.Team);
        f.Step(TFField.Self(At, extra: SelfFlags.Frozen), Level(), Seen(MateId, MateBehind));
        f.Step(Bot(At), Level(), Seen(MateId, MateBehind));
        Assert.True(f.Decider.InOpening);
        f.Step(TFField.Self(At) with { Flags = SelfFlags.None, Health = 0 }, Level());
        Assert.False(f.Decider.InOpening);
    }

    [Fact]
    public void TwoRalliesCalledAtOnceAreTheLowerSlots()
    {
        // Slot 2 called; slot 3's, called the same moment, is heard: slot 2 keeps its own.
        TFField low = Field();
        AiVector own = Rallied(low);
        low.StepHearing(Bot(At), Level(), [AiEvent.Callout(low.Bot, new Tick(low.Tick), MateId, new SayIntent(CalloutKind.Rally, new AiVector(400f, 256f, 0f), 0, 0, 0, Red, false))]);
        Assert.Equal(own, low.Decider.RallyPlace);
        // Slot 4 called; slot 3's heard: slot 4 holds slot 3's.
        var high = new TFField(slot: 4, mates: [Mate], tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        Rallied(high);
        var theirs = new AiVector(400f, 256f, 0f);
        high.StepHearing(Bot(At), Level(), [AiEvent.Callout(high.Bot, new Tick(high.Tick), MateId, new SayIntent(CalloutKind.Rally, theirs, 0, 0, 0, Red, false))]);
        Assert.Equal(theirs, high.Decider.RallyPlace);
        // Slot 4 again, slot 3's heard well after: not a clash, slot 4 keeps its own.
        var later = new TFField(slot: 4, mates: [Mate], tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f });
        AiVector kept = Rallied(later);
        for (int i = 0; i < Decides(TFDecider.RallyClashSeconds + 1f); i++)
            later.Step(Bot(At), Level());
        later.StepHearing(Bot(At), Level(), [Heard(later, MateId, CalloutKind.Rally, theirs, ahead: 0)]);
        Assert.Equal(kept, later.Decider.RallyPlace);
    }

    [Fact]
    public void TheCallerCallsThePushWhenHalfTheTeamIsAtTheRally()
    {
        // A team of four: two at the rally push.
        TFField f = Field(mates: [Mate, 4, 5]);
        AiVector rally = Rallied(f);
        IntentCommand push = f.Step(Bot(rally), Level(), Seen(MateId, rally + new AiVector(200f, 0f, 0f)));
        Assert.Equal((CalloutKind.Push, rally, TFTeamPlay.Push, 20, 1), (push.Say.Kind, push.Say.Place, f.Decider.TeamPlay, f.Decider.PushTarget, f.Decider.PushesCalled));
        // The control: the teammate seen beyond the rally's reach, no push.
        TFField g = Field(mates: [Mate, 4, 5]);
        AiVector r = Rallied(g);
        IntentCommand none = g.Step(Bot(r), Level(), Seen(MateId, r + new AiVector(TFDecider.RallyGatherUnits + 50f, 0f, 0f)));
        Assert.Equal((false, TFTeamPlay.Gather), (none.Say.Says, g.Decider.TeamPlay));
        // A team of six needs three.
        TFField six = Field(mates: [Mate, 4, 5, 6, 7]);
        AiVector r6 = Rallied(six);
        Assert.False(six.Step(Bot(r6), Level(), Seen(MateId, r6)).Say.Says);
        Assert.Equal(CalloutKind.Push, six.Step(Bot(r6), Level(), Seen(MateId, r6), Seen(Mate4, r6)).Say.Kind);
    }

    /// <summary>
    /// Lane Groutes: a bot waiting at its rally looks all round within the time a sighting counts, so teammates who came by
    /// another way, or up behind it, are seen there. Live on cp_granary a caller looking one way stood 23 s, counting nobody.
    /// </summary>
    [Fact]
    public void ABotWaitingAtItsRallyLooksAllRoundWithinTheTimeASightingCounts()
    {
        TFField f = Field(mates: [Mate, 4, 5]);
        AiVector rally = Rallied(f);
        var bearings = new List<float>();
        for (int i = 0; i < Decides(TFDecider.RallySeenSeconds); i++)
        {
            IntentCommand c = f.Step(Bot(rally), Level());
            Assert.Equal("at the rally", f.Decider.Doing.Verdict);
            Assert.Equal(LookKind.Point, c.Look.Kind);
            AiVector d = c.Look.Point - rally;
            bearings.Add((MathF.Atan2(d.Y, d.X) * (180f / MathF.PI) + 360f) % 360f);
        }

        // No gap between bearings wider than the eyes' 90°: every way round was in view at some decide.
        float[] sorted = [.. bearings.Order()];
        float widest = 360f - sorted[^1] + sorted[0];
        for (int i = 1; i < sorted.Length; i++)
            widest = MathF.Max(widest, sorted[i] - sorted[i - 1]);
        Assert.True(widest < 90f, $"widest gap {widest:F0}° over {sorted.Length} decides");
    }

    [Fact]
    public void ATeammateSeenAtTheRallyAMomentAgoCountsAsThere()
    {
        // Seen at the rally from afar, then out of sight (behind the bot): 2 s later, the caller at the rally, it still counts.
        var away = new AiVector(100f, 256f, 0f);
        TFField f = Field(mates: [Mate, 4, 5]);
        AiVector rally = Rallied(f);
        f.Step(Bot(away), Level(), Seen(MateId, rally));
        Assert.Equal(TFTeamPlay.Gather, f.Decider.TeamPlay);
        bool pushed = false;
        for (int i = 0; i < Decides(TFDecider.RallySeenSeconds - 1f) && !pushed; i++)
            pushed = f.Step(Bot(rally), Level()).Say.Kind == CalloutKind.Push;
        Assert.True(pushed);
        // Seen there longer ago than that: it does not count.
        TFField g = Field(mates: [Mate, 4, 5]);
        AiVector r = Rallied(g);
        g.Step(Bot(away), Level(), Seen(MateId, r));
        for (int i = 0; i < Decides(TFDecider.RallySeenSeconds + 0.5f); i++)
            g.Step(Bot(away), Level());
        Assert.False(g.Step(Bot(r), Level()).Say.Says);
    }

    [Fact]
    public void TheCallerCallsThePushWhenTheRallysTimeIsUpAfterItsWayThere()
    {
        // A at the far end, the bot near its own end, its teammate seen 1,800 u ahead: the rally (their middle, moved
        // toward A) is a long way off for the caller; it says how many seconds.
        var far = new AiVector(3800f, 256f, 0f);
        var start = new AiVector(100f, 256f, 0f);
        TFField f = Field(mates: [Mate, 4, 5]);
        f.Decider.HoldStance(TFStance.Team);
        IntentCommand said = f.Step(Bot(start), Level(aAt: far), Seen(MateId, new AiVector(1900f, 256f, 0f)));
        int travel = (int)MathF.Ceiling(start.DistanceTo(said.Say.Place) / TFDecider.RallyRunSpeed);
        Assert.Equal((CalloutKind.Rally, travel), (said.Say.Kind, said.Say.Count));
        Assert.True(travel >= 5);
        float pushedAt = -1f;
        float most = travel + TFDecider.RallyTimeoutSeconds + TFDecider.RallyJitterSeconds + 1f;
        for (int i = 0; i < Decides(most) && pushedAt < 0f; i++)
        {
            if (f.Step(Bot(start), Level(aAt: far)).Say.Kind == CalloutKind.Push)
                pushedAt = (i + 1) * DecideSeconds;
        }

        Assert.InRange(pushedAt, travel + TFDecider.RallyTimeoutSeconds - TFDecider.RallyJitterSeconds, most);
        Assert.Equal(TFTeamPlay.Push, f.Decider.TeamPlay);
    }

    [Fact]
    public void ABotHeedsTheSecondsACallerSaid()
    {
        // A teammate's rally with 20 s of way: no push of its own until past them, the time and the grace.
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        var rally = new AiVector(500f, 256f, 0f);
        f.StepHearing(Bot(At), Level(), [AiEvent.Callout(f.Bot, new Tick(f.Tick + 1), MateId, new SayIntent(CalloutKind.Rally, rally, 20, 0, 0, Red, false))]);
        for (int i = 0; i < Decides(20f + TFDecider.RallyTimeoutSeconds - TFDecider.RallyJitterSeconds + TFDecider.RallyGraceSeconds - 1f); i++)
            f.Step(Bot(rally), Level());
        Assert.Equal(TFTeamPlay.Gather, f.Decider.TeamPlay);
    }

    [Fact]
    public void ABotAtATeammatesRallyPushesOnItsPush()
    {
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        var rally = new AiVector(500f, 256f, 0f);
        f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        f.Step(Bot(rally), Level());
        Assert.Equal(TFTeamPlay.Gather, f.Decider.TeamPlay);
        f.StepHearing(Bot(rally), Level(), [Heard(f, MateId, CalloutKind.Push, rally)]);
        Assert.Equal((TFTeamPlay.Push, 20, 1, 0), (f.Decider.TeamPlay, f.Decider.PushTarget, f.Decider.PushesBegun, f.Decider.PushesCalled));
        for (int i = 0; i < Decides(1.5f); i++)
            f.Step(Bot(rally), Level());
        Assert.Equal((TFDecider.TeamBranch, PointA, "pushing"), (f.Branch, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void ABotWhoseCallerCallsNoPushPushesAfterTheGrace()
    {
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        var rally = new AiVector(500f, 256f, 0f);
        f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        for (int i = 0; i < Decides(TFDecider.RallyTimeoutSeconds - TFDecider.RallyJitterSeconds + TFDecider.RallyGraceSeconds - 1f); i++)
            f.Step(Bot(rally), Level());
        Assert.Equal(TFTeamPlay.Gather, f.Decider.TeamPlay);
        for (int i = 0; i < Decides(2f * TFDecider.RallyJitterSeconds + 2f) && f.Decider.TeamPlay != TFTeamPlay.Push; i++)
            f.Step(Bot(rally), Level());
        Assert.Equal((TFTeamPlay.Push, 0), (f.Decider.TeamPlay, f.Decider.PushesCalled));
    }

    /// <summary>A bot in a push for A, called by itself with a teammate at its rally.</summary>
    private static (TFField, AiVector) Pushing(int[]? mates = null, IJumpAdvisor? jumps = null, TFClass playerClass = TFClass.Soldier)
    {
        TFField f = Field(mates: mates ?? [Mate, 4, 5], jumps: jumps);
        f.Decider.HoldStance(TFStance.Team);
        AiVector rally = f.Step(Bot(At, playerClass), Level(), Seen(MateId, MateBehind)).Say.Place;
        f.Step(Bot(rally, playerClass), Level(), Seen(MateId, rally));
        Assert.Equal(TFTeamPlay.Push, f.Decider.TeamPlay);
        return (f, rally);
    }

    [Fact]
    public void APushHoldsUntilItsPointIsTakenOrLost()
    {
        (TFField f, AiVector rally) = Pushing();
        for (int i = 0; i < Decides(10f); i++)
            f.Step(Bot(rally), Level());
        Assert.Equal(TFTeamPlay.Push, f.Decider.TeamPlay);
        // A red's: taken, and the push is over.
        f.Step(Bot(PointA), Level(aOwner: Red, aMay: 1 << Blue));
        Assert.Equal((TFTeamPlay.None, 1, 0), (f.Decider.TeamPlay, f.Decider.PushesTaken, f.Decider.PushesLost));
        // And the points for its next stretch.
        Assert.Equal((TFStance.Points, 1), (f.Decider.Stance, f.Decider.PointsAfterPush));
        // A no longer red's to take (the round over): lost.
        (TFField g, AiVector r) = Pushing();
        g.Step(Bot(r), Level(aMay: 0, aState: ObjectiveState.Locked));
        Assert.Equal((TFTeamPlay.None, 0, 1), (g.Decider.TeamPlay, g.Decider.PushesTaken, g.Decider.PushesLost));
    }

    [Fact]
    public void APushEndsAtItsMostAndTheStretchHoldsUntilThen()
    {
        // The bot never comes near: its rally times out, it pushes, the push times out; its stance holds all the while,
        // though a choice would take the points nine times in ten (its Aggression far over its Teamwork).
        TFField f = Field(mates: [Mate, 4, 5], tune: p => p with { FightDrive = 3f, EscortDrive = 0.1f });
        Rallied(f);
        float seconds = TFDecider.RallyTimeoutSeconds + TFDecider.RallyJitterSeconds + TFDecider.PushMostSeconds;
        Assert.True(seconds > TFDecider.StanceHoldMin + TFDecider.StanceHoldSpread);
        for (int i = 0; i < Decides(seconds - 1f) && f.Decider.PushesTimedOut == 0; i++)
        {
            f.Step(Bot(new AiVector(100f, 256f, 0f)), Level());
            if (f.Decider.PushesTimedOut == 0)
                Assert.Equal((TFStance.Team, 0), (f.Decider.Stance, f.Decider.StanceChanges));
        }

        for (int i = 0; i < Decides(3f) && f.Decider.PushesTimedOut == 0; i++)
            f.Step(Bot(new AiVector(100f, 256f, 0f)), Level());
        Assert.Equal((1, 1, TFStance.Points), (f.Decider.PushesBegun, f.Decider.PushesTimedOut, f.Decider.Stance));
    }

    [Fact]
    public void APusherDoesNotTurnBackToDefendAndIsNotPulled()
    {
        (TFField f, AiVector rally) = Pushing();
        // B, red's, being taken within reach: the push goes on for A.
        ObjectiveKnowledge threat = Level(bOwner: Red, bMay: 1 << Blue, bState: ObjectiveState.Capturing);
        for (int i = 0; i < Decides(2f); i++)
            f.Step(Bot(rally), threat);
        Assert.Equal((TFStance.Team, TFTeamPlay.Push, 0, PointA, "pushing"),
                     (f.Decider.Stance, f.Decider.TeamPlay, f.Decider.StancePulls, f.Decider.Doing.Goal, f.Decider.Doing.Verdict));
    }

    [Fact]
    public void ABotGatheringIsNotPulled()
    {
        TFField f = Field(mates: [Mate, 4, 5]);
        AiVector rally = Rallied(f);
        // A's capture by the team under way within reach: it would pull a bot not gathering.
        f.Step(Bot(rally), Level(aState: ObjectiveState.Capturing));
        Assert.Equal((TFStance.Team, TFTeamPlay.Gather, 0), (f.Decider.Stance, f.Decider.TeamPlay, f.Decider.StancePulls));
    }

    [Fact]
    public void APusherWaitsForATeammateFarBehindItAWhile()
    {
        (TFField f, _) = Pushing();
        AiVector here = new(700f, 256f, 0f);
        IntentCommand wait = f.Step(Bot(here), Level(), Seen(MateId, here - new AiVector(TFDecider.PushKeepUnits + 100f, 0f, 0f)));
        Assert.Equal(("waiting for the push", MoveKind.Hold), (f.Decider.Doing.Verdict, wait.Move.Kind));
        // A teammate within reach: on.
        f.Step(Bot(here), Level(), Seen(MateId, here - new AiVector(300f, 0f, 0f)));
        Assert.Equal("pushing", f.Decider.Doing.Verdict);
        // Ahead of it, near the point: no wait.
        f.Step(Bot(PointA - new AiVector(TFDecider.PushCommitUnits - 100f, 0f, 0f)), Level(), Seen(MateId, here - new AiVector(TFDecider.PushKeepUnits + 300f, 0f, 0f)));
        Assert.Equal("pushing", f.Decider.Doing.Verdict);
        // Waiting has its most: past it, on.
        for (int i = 0; i < Decides(TFDecider.PushWaitMostSeconds + 1f); i++)
            f.Step(Bot(here), Level(), Seen(MateId, here - new AiVector(TFDecider.PushKeepUnits + 100f, 0f, 0f)));
        Assert.Equal("pushing", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void APusherKeepsWithItsGroupNotTheFarSideOfTheMap()
    {
        AiVector here = new(2000f, 256f, 0f);
        // A teammate seen 2 s ago 700 u behind: it waits.
        (TFField f, _) = Pushing();
        f.Step(Bot(here), Level(aAt: new AiVector(3800f, 256f, 0f)), Seen(MateId, here - new AiVector(TFDecider.PushKeepUnits + 200f, 0f, 0f)));
        f.Step(Bot(here), Level(aAt: new AiVector(3800f, 256f, 0f)));
        IntentCommand wait = f.Step(Bot(here), Level(aAt: new AiVector(3800f, 256f, 0f)));
        Assert.Equal(("waiting for the push", MoveKind.Hold), (f.Decider.Doing.Verdict, wait.Move.Kind));
        // The only teammate known beyond the group's reach behind: it goes on.
        (TFField g, _) = Pushing();
        g.Step(Bot(here), Level(aAt: new AiVector(3800f, 256f, 0f)), Seen(MateId, here - new AiVector(TFDecider.PushGroupUnits + 100f, 0f, 0f)));
        Assert.Equal("pushing", g.Decider.Doing.Verdict);
    }

    [Fact]
    public void APushHeardIsJoinedNearItsPlaceOrByTheBotsOfItsRally()
    {
        var place = new AiVector(500f, 256f, 0f);
        // Holding no rally, near the push's place: joined.
        TFField near = Field(tune: p => p with { Teamwork = 10f });
        near.Decider.HoldStance(TFStance.Team);
        near.StepHearing(Bot(place + new AiVector(TFDecider.PushJoinUnits - 100f, 0f, 0f)), Level(), [Heard(near, MateId, CalloutKind.Push, place)]);
        Assert.Equal(TFTeamPlay.Push, near.Decider.TeamPlay);
        // Holding no rally, beyond it: not joined (it calls its own rally instead).
        TFField far = Field(mates: [Mate, 4], tune: p => p with { Teamwork = 10f });
        far.Decider.HoldStance(TFStance.Team);
        far.StepHearing(Bot(new AiVector(3000f, 256f, 0f)), Level(aAt: new AiVector(3600f, 256f, 0f)), [Heard(far, Mate4, CalloutKind.Push, place)]);
        Assert.NotEqual(TFTeamPlay.Push, far.Decider.TeamPlay);
        // Holding its rally, as far: joined.
        TFField mine = Field(mates: [Mate, 4], tune: p => p with { Teamwork = 10f });
        mine.Decider.HoldStance(TFStance.Team);
        mine.StepHearing(Bot(new AiVector(3000f, 256f, 0f)), Level(aAt: new AiVector(3600f, 256f, 0f)), [Heard(mine, Mate4, CalloutKind.Rally, place)]);
        mine.StepHearing(Bot(new AiVector(3000f, 256f, 0f)), Level(aAt: new AiVector(3600f, 256f, 0f)), [Heard(mine, Mate4, CalloutKind.Push, place)]);
        Assert.Equal(TFTeamPlay.Push, mine.Decider.TeamPlay);
    }

    private static SelfState Dead(AiVector at) => Bot(at) with { Flags = SelfFlags.Grounded, Health = 0 };

    [Fact]
    public void ABotBackFromTheDeadJoinsItsTeamsPushFromItsSpawn()
    {
        // In a push, it dies; back at its spawn far from the push's place, it pushes again, its stance the team's still.
        (TFField f, AiVector rally) = Pushing();
        var spawn = new AiVector(3000f, 256f, 0f);
        Assert.True(spawn.DistanceTo(rally) > TFDecider.PushJoinUnits);
        f.Step(Dead(rally), Level());
        Assert.Equal(TFStance.Team, f.Decider.Stance);
        for (int i = 0; i < Decides(5f); i++)
            f.Step(Dead(rally), Level());
        f.Step(Bot(spawn), Level());
        Assert.Equal((TFTeamPlay.Push, 2, 1, TFStance.Team), (f.Decider.TeamPlay, f.Decider.PushesBegun, f.Decider.PushRejoins, f.Decider.Stance));
        // Dead longer than a push lasts: the push it knew of is over, it does not push from its spawn.
        (TFField g, AiVector r) = Pushing();
        for (int i = 0; i < Decides(TFDecider.PushMostSeconds + 2f); i++)
            g.Step(Dead(r), Level());
        g.Step(Bot(spawn), Level());
        Assert.Equal((0, 1), (g.Decider.PushRejoins, g.Decider.PushesBegun));
        Assert.NotEqual(TFTeamPlay.Push, g.Decider.TeamPlay);
    }

    [Fact]
    public void ABotBackFromTheDeadDropsARallyWhoseTimeHasPassed()
    {
        var rally = new AiVector(500f, 256f, 0f);
        // The rally's time (its draw is the team's and the place's): a bot alive at it pushes on its own a grace after it.
        TFField clock = Field(tune: p => p with { Teamwork = 10f });
        clock.Decider.HoldStance(TFStance.Team);
        clock.StepHearing(Bot(At), Level(), [Heard(clock, MateId, CalloutKind.Rally, rally)]);
        int decides = 0;
        while (clock.Decider.PushesBegun == 0 && decides < Decides(60f))
        {
            clock.Step(Bot(rally), Level());
            decides++;
        }

        float time = decides * DecideSeconds - TFDecider.RallyGraceSeconds;
        // Dead from the call until 2 s past that time: within the rally's life, short of its own push.
        float past = time + 2f;
        TFField f = Field(tune: p => p with { Teamwork = 10f });
        f.Decider.HoldStance(TFStance.Team);
        f.StepHearing(Bot(At), Level(), [Heard(f, MateId, CalloutKind.Rally, rally)]);
        for (int i = 0; i < Decides(past); i++)
            f.Step(Dead(At), Level());
        f.Step(Bot(new AiVector(3000f, 256f, 0f)), Level());
        // Not walked to, and not pushed from on its own either.
        Assert.Equal(((AiVector?)null, 0), (f.Decider.RallyPlace, f.Decider.PushesBegun));
        // Back within the rally's time: it keeps it, and goes there.
        TFField g = Field(tune: p => p with { Teamwork = 10f });
        g.Decider.HoldStance(TFStance.Team);
        g.StepHearing(Bot(At), Level(), [Heard(g, MateId, CalloutKind.Rally, rally)]);
        for (int i = 0; i < Decides(5f); i++)
            g.Step(Dead(At), Level());
        g.Step(Bot(new AiVector(3000f, 256f, 0f)), Level());
        Assert.Equal(((AiVector?)rally, TFTeamPlay.Gather), (g.Decider.RallyPlace, g.Decider.TeamPlay));
    }

    [Fact]
    public void ARocketJumperPushesByTheCoursesWay()
    {
        // The route says the way to A needs 150 health it lacks: a pusher that rocket jumps goes for health first, as the
        // Course branch does; one that does not (a Scout) walks the push.
        var book = new JumpBook(new JumpBookSettings());
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 40f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        (TFField f, AiVector rally) = Pushing(jumps: book);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, PointA, false) { NoWay = true, Need = 150f });
        for (int i = 0; i < Decides(1f); i++)
            f.Step(Bot(rally), Level());
        Assert.Equal((TFTeamPlay.Push, "to the cabinet for the jump"), (f.Decider.TeamPlay, f.Decider.Doing.Verdict));
        (TFField s, AiVector r) = Pushing(jumps: book, playerClass: TFClass.Scout);
        book.NoteRoute(s.Bot.Slot, new Tick(s.Tick), new NavLinkStep(-1, links.Hash, PointA, false) { NoWay = true, Need = 150f });
        for (int i = 0; i < Decides(1f); i++)
            s.Step(Bot(r, TFClass.Scout), Level());
        Assert.Equal((TFTeamPlay.Push, "pushing"), (s.Decider.TeamPlay, s.Decider.Doing.Verdict));
    }

    /// <summary>Whether a bot in the points stance at <paramref name="at"/> joins the rally at <paramref name="rally"/> it hears.</summary>
    private static bool Joins(AiVector at, AiVector rally, ObjectiveKnowledge? level = null, float teamwork = 10f, int floorLength = TFField.Length)
    {
        var d = new TFField(mates: [Mate], floorLength: floorLength, tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f, Teamwork = teamwork });
        d.Decider.HoldStance(TFStance.Points);
        d.StepHearing(Bot(at), level ?? Level(), [Heard(d, MateId, CalloutKind.Rally, rally)]);
        if (d.Decider.RallyJoins > 0)
            Assert.Equal((TFStance.Team, TFTeamPlay.Gather), (d.Decider.Stance, d.Decider.TeamPlay));
        else
            Assert.Equal(TFStance.Points, d.Decider.Stance);
        return d.Decider.RallyJoins > 0;
    }

    [Fact]
    public void ABotInThePointsStanceJoinsATeammatesRallyNearerThanItsPoint()
    {
        // At 1,000 u, A at 1,600: a rally at 700 is nearer, joined.
        Assert.True(Joins(At, new AiVector(700f, 256f, 0f)));
        // A rally past A, within reach but further than A: the bot keeps on to A.
        Assert.False(Joins(At, new AiVector(1900f, 256f, 0f)));
        // On a point in play: it stays. Standing on B, red's and open to blue, A its goal 800 u off, a rally 500 u off.
        ObjectiveKnowledge onB = Level(bOwner: Red, bMay: 1 << Blue, bState: ObjectiveState.Open);
        Assert.False(Joins(PointB, PointB + new AiVector(500f, 0f, 0f), onB));
        // The control: just off B, the same rally is joined.
        Assert.True(Joins(PointB + new AiVector(TFDecider.OnPointUnits + 20f, 0f, 0f), PointB + new AiVector(500f, 0f, 0f), onB));
        // A lone wolf heeds none.
        Assert.False(Joins(At, new AiVector(700f, 256f, 0f), teamwork: TFDecider.RallyIgnoreTeamwork));
        // Going to its own point being taken: it stays.
        Assert.False(Joins(At, new AiVector(700f, 256f, 0f), Defence(ObjectiveState.Capturing)));
    }

    [Fact]
    public void ABotInThePointsStanceJoinsOnlyARallyWithinItsReach()
    {
        // A at the far end; the bot at its own end; a rally on its way just within reach is joined, one just beyond is not.
        var far = new AiVector(3800f, 256f, 0f);
        var start = new AiVector(100f, 256f, 0f);
        Assert.True(Joins(start, start + new AiVector(TFDecider.RallyJoinReach - 100f, 0f, 0f), Level(aAt: far)));
        Assert.False(Joins(start, start + new AiVector(TFDecider.RallyJoinReach + 100f, 0f, 0f), Level(aAt: far)));
    }
}
