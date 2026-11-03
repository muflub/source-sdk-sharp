using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// A bot holding Follow along a flow field on the course (plan_hl2mp_ai
/// Phase 3 step 5): corners toward the target, a jump at a ledge's take-off,
/// ducked into the crawlspace, arrived on the target.
/// </summary>
public sealed class NavFollowerTests
{
    private static readonly Lazy<DerivedNav> Nav = new(() => NavGraphFromNav3d.Derive(
        new Nav3dNavSource(NavFixtures.Course().Open()), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default, []));

    private static NavFollower To(AiVector target)
    {
        NavGraph g = Nav.Value.Graph;
        var follower = new NavFollower(g, NavPoints.CellNear(g, target, ClearanceClass.Crouch), target, ClearanceClass.Crouch,
                                       new NavObstacleState(g.ObstacleCount));
        follower.Rebuild();
        return follower;
    }

    [Fact]
    public void FarFromTheTargetItFollowsCornersTowardIt()
    {
        MoveIntent move = To(new AiVector(700, 256, 16)).Next(new AiVector(40, 40, 16), out FollowState state);
        Assert.Equal((FollowState.Moving, MoveKind.Follow), (state, move.Kind));
        Assert.True(move.CornerCount >= 1);
        Assert.True(move.Corner(0).X > 40f, $"the first corner {move.Corner(0)} leads away from the target");
    }

    /// <summary>At a ledge's take-off it jumps, ducked (a plain jump's ledge too: live, the plain jump fell short of ss_ai_nav's 32 u ledge).</summary>
    [Fact]
    public void AtALedgesTakeOffItJumps()
    {
        MoveIntent move = To(new AiVector(200, 200, 46)).Next(new AiVector(200, 136, 16), out FollowState state);
        Assert.Equal((FollowState.Moving, MoveKind.CrouchJump), (state, move.Kind));
    }

    [Fact]
    public void IntoTheCrawlspaceItDucks()
    {
        MoveIntent move = To(new AiVector(440, 200, 16)).Next(new AiVector(440, 136, 16), out _);
        Assert.True(move.CrouchOn);
    }

    /// <summary>The route is the follower's own way: through the door's gap in the wall at x 608, where a straight line would cross solid wall.</summary>
    [Fact]
    public void TheRouteGoesThroughTheGapNotStraightAtTheGoal()
    {
        var target = new AiVector(700, 40, 16);
        NavFollower follower = To(target);
        follower.Next(new AiVector(560, 40, 16), out FollowState state);
        var route = new List<AiVector>();
        follower.Route(route);
        Assert.Equal(FollowState.Moving, state);
        Assert.Equal((560f, 40f), (route[0].X, route[0].Y));
        Assert.Equal((target.X, target.Y), (route[^1].X, route[^1].Y));

        // Where each leg crosses the wall's plane: inside the gap (y 224-288).
        bool crossed = false;
        for (int i = 1; i < route.Count; i++)
        {
            if (route[i - 1].X < 616f && route[i].X >= 616f)
            {
                float y = route[i - 1].Y + (route[i].Y - route[i - 1].Y) * (616f - route[i - 1].X) / (route[i].X - route[i - 1].X);
                Assert.InRange(y, 224f, 288f);
                crossed = true;
            }
        }

        Assert.True(crossed, "the route crosses the wall's plane");
    }

    /// <summary>The steering corners stop at the corridor's end; the route goes on along the field to the goal.</summary>
    [Fact]
    public void TheRouteRunsPastTheCorridorToTheGoal()
    {
        NavGraph g = Nav.Value.Graph;
        var target = new AiVector(700, 480, 16);
        NavFollower follower = To(target);
        var from = new AiVector(40, 40, 16);
        MoveIntent move = follower.Next(from, out _);
        var route = new List<AiVector>();
        follower.Route(route);
        Assert.True(g.Cell(NavPoints.CellNear(g, target, ClearanceClass.Crouch)).Centre.DistanceTo(from) > NavFollower.CorridorCells * 8f, "the control: the goal is past a corridor's reach");
        Assert.Equal(move.Corner(0), route[1]);
        Assert.Equal((target.X, target.Y), (route[^1].X, route[^1].Y));
    }

    /// <summary>A move with no way has no route: nothing of the last move's is left to draw.</summary>
    [Fact]
    public void ArrivedItHasNoRoute()
    {
        NavFollower follower = To(new AiVector(300, 300, 16));
        var route = new List<AiVector>();
        follower.Next(new AiVector(200, 300, 16), out _);
        follower.Route(route);
        Assert.NotEmpty(route);
        follower.Next(new AiVector(300, 300, 16), out FollowState state);
        follower.Route(route);
        Assert.Equal((FollowState.Arrived, 0), (state, route.Count));
    }

    [Fact]
    public void OnTheTargetItHasArrived()
    {
        To(new AiVector(300, 300, 16)).Next(new AiVector(305, 300, 16), out FollowState state);
        Assert.Equal(FollowState.Arrived, state);
    }

    /// <summary>A bot on a table right over a kit under it has not arrived, and one on a shelf's floor under its item has.</summary>
    [Fact]
    public void OverItsTargetOnAFloorAboveItTheBotHasNotArrived()
    {
        To(new AiVector(300, 300, 16)).Next(new AiVector(305, 300, 16 + NavFollower.ArriveAbove + 40f), out FollowState over);
        Assert.NotEqual(FollowState.Arrived, over);
        To(new AiVector(300, 300, 60)).Next(new AiVector(305, 300, 16), out FollowState under);
        Assert.Equal(FollowState.Arrived, under);
    }

    // Live, dm_lockdown (2026-11-02): a prop_physics the file's box
    // under-states stood in a corridor, and a bot steering into it at full
    // forward made no progress for good. A stuck bot routes round.
    [Fact]
    public void AStuckBotRoutesRoundWhereItStuck()
    {
        NavFollower f = To(new AiVector(40, 470, 16));
        var at = new AiVector(40, 200, 16);
        MoveIntent before = f.Next(at, out _);
        Assert.InRange(before.Corner(0).X, 20f, 60f);
        Assert.True(f.ReportStuck(at), "a way round exists on the open floor");
        MoveIntent after = f.Next(at, out FollowState state);
        Assert.Equal(FollowState.Moving, state);
        Assert.NotEmpty(f.Avoided);
        Assert.True(MathF.Abs(after.Corner(0).X - 40f) > 24f || MathF.Abs(after.Corner(0).Y - 200f) < 8f,
                    $"the first corner {after.Corner(0)} still runs into the stuck spot (before: {before.Corner(0)}); next {f.NextStep()}; avoided {string.Join(",", f.Avoided.Select(c => Nav.Value.Graph.Cell(c).Centre))}; corners {after.CornerCount}: {after.Corner(1)} {after.Corner(2)}");
        var g = Nav.Value.Graph;
        foreach (int c in f.Avoided)
            Assert.True(g.Cell(c).Centre.DistanceTo(g.Cell(g.FindCell(at + new AiVector(0, 0, 1))).Centre) <= MathF.Sqrt(NavFollower.AvoidAhead * NavFollower.AvoidAhead + NavFollower.StripHalfWidth * NavFollower.StripHalfWidth) + 1f);
    }

    /// <summary>
    /// A walkway at z 128 whose edge column has only the floor below it (its
    /// body does not fit at the column's centre for the rail), a rail top past
    /// it, and that floor running on.
    /// </summary>
    private static (NavGraph G, CellId Walkway, CellId EdgeFloor, CellId Rail, CellId Beyond) RailCourse()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId w0 = b.AddCell(new NavCell(0f, 0f, 16f, 16f, 128f, Both, NavCellFlags.None, 1f));
        CellId walkway = b.AddCell(new NavCell(0f, 16f, 16f, 32f, 128f, Both, NavCellFlags.None, 1f));
        CellId edgeFloor = b.AddCell(new NavCell(0f, 32f, 16f, 48f, 0f, Both, NavCellFlags.None, 1f));
        CellId rail = b.AddCell(new NavCell(0f, 48f, 16f, 64f, 168f, Both, NavCellFlags.None, 1f));
        CellId beyond = b.AddCell(new NavCell(0f, 80f, 16f, 96f, 0f, Both, NavCellFlags.None, 1f));
        b.AddBoth(w0, walkway, NavEdgeType.Walk);
        b.AddEdge(walkway, rail, NavEdgeType.CrouchJump);
        b.AddEdge(rail, beyond, NavEdgeType.Drop);
        b.AddEdge(edgeFloor, beyond, NavEdgeType.Walk);
        return (b.Build(), walkway, edgeFloor, rail, beyond);
    }

    /// <summary>
    /// A bot's centre at the walkway's edge is over a column whose only cell
    /// is the floor 128 u below; it is on the walkway. Taken to be on that
    /// floor it walked the floor's way into the rail, for good (live,
    /// dm_lockdown: at z 128, "next Walk to" a cell at z 0).
    /// </summary>
    [Fact]
    public void ABotAtAWalkwaysEdgeIsOnTheWalkwayNotTheFloorBelowIt()
    {
        var (g, walkway, edgeFloor, _, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        Assert.Equal(edgeFloor, g.FindCell(new AiVector(8f, 36f, 129f)));   // what the column answers: the setup

        MoveIntent move = f.Next(new AiVector(8f, 36f, 128f), out FollowState state);

        Assert.Equal(FollowState.Moving, state);
        Assert.Equal(walkway, f.At);
        Assert.Equal(NavEdgeType.CrouchJump, f.NextVia);
        Assert.Equal(MoveKind.CrouchJump, move.Kind);
    }

    /// <summary>In the air, over a floor and with no cell near its feet, the bot is over that floor.</summary>
    [Fact]
    public void ABotHighOverAFloorWithNoCellNearIsOverThatFloor()
    {
        var (g, _, edgeFloor, _, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        f.Next(new AiVector(8f, 40f, 100f), out _);
        Assert.Equal(edgeFloor, f.At);
    }

    /// <summary>
    /// Falling past a rail, level with the walkway it jumped from, the bot is over
    /// the floor it is falling to: taken to be back on the walkway it turned back
    /// to jump the rail again (live, dm_lockdown).
    /// </summary>
    [Fact]
    public void ABotFallingPastAWalkwayIsOverTheFloorBelow()
    {
        var (g, walkway, edgeFloor, _, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        f.Next(new AiVector(8f, 36f, 130f), out _);
        Assert.Equal(walkway, f.At);
        MoveIntent falling = f.Next(new AiVector(8f, 36f, 126f), out _);
        Assert.Equal(edgeFloor, f.At);
        Assert.Equal(MoveKind.Hold, falling.Kind);   // not steered in the air, to catch the lip it came over
    }

    /// <summary>The end of a jump's arc is no fall: taken for one, the bot stopped steering over the rail and came down in front of it (live, ss_ai_nav).</summary>
    [Fact]
    public void ComingDownFromAJumpTheBotStillSteers()
    {
        NavFollower f = To(new AiVector(700, 256, 16));
        f.Next(new AiVector(200, 136, 16 + 50f), out _);
        MoveIntent move = f.Next(new AiVector(204, 136, 16 + 40f), out _);
        Assert.NotEqual(MoveKind.Hold, move.Kind);
    }

    /// <summary>A corner round a wall is walked to, however near: passed through the wall, the bot steered into it (live, dm_lockdown's balcony).</summary>
    [Fact]
    public void ACornerRoundAWallIsNotPassedForBeingNear()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        var cells = new List<CellId>();
        foreach ((float x, float y) in ((float, float)[])[(0f, 0f), (0f, 16f), (0f, 32f), (16f, 32f), (32f, 32f), (48f, 32f), (64f, 32f)])
            cells.Add(b.AddCell(new NavCell(x, y, x + 16f, y + 16f, 0f, Both, NavCellFlags.None, 1f)));
        for (int i = 0; i + 1 < cells.Count; i++)
            b.AddBoth(cells[i], cells[i + 1], NavEdgeType.Walk);
        NavGraph g = b.Build();
        var f = new NavFollower(g, cells[^1], g.Cell(cells[^1]).Centre, ClearanceClass.Crouch, new NavObstacleState(0));

        MoveIntent move = f.Next(new AiVector(12f, 22f, 0f), out _);

        Assert.True(move.Corner(0).X < 20f, $"steers round the wall's corner: {move.Corner(0)}");
    }

    /// <summary>A rail's take-off is a crouch-jump: a plain jump tops out under its 40 u.</summary>
    [Fact]
    public void AtARailsTakeOffItCrouchJumpsOverIt()
    {
        var (g, walkway, _, _, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        MoveIntent move = f.Next(g.Cell(walkway).Centre, out _);
        Assert.Equal(MoveKind.CrouchJump, move.Kind);
        Assert.True(move.CornerCount >= 2 && move.Corner(0).Y > g.Cell(walkway).Centre.Y, $"the jump leads toward the rail: {move.Corner(0)}, {move.CornerCount} corners");
        Assert.True(move.Corner(move.CornerCount - 1).Y >= g.Cell(beyond).Centre.Y, $"the jump goes on past the rail, not onto it: {move.Corner(move.CornerCount - 1)}");
    }

    /// <summary>
    /// A ladder's foot beside the bot and its top over it: the take-off is walked
    /// to, not passed because it is near across (live, dm_lockdown's ladder at
    /// (-2720 5060): "steering to none", 16 u under the top, for good).
    /// </summary>
    [Fact]
    public void ALaddersFootNearByIsWalkedToAndItsTopOverheadIsNotPassed()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId here = b.AddCell(new NavCell(0f, 0f, 16f, 16f, 0f, Both, NavCellFlags.None, 1f));
        CellId foot = b.AddCell(new NavCell(0f, 16f, 16f, 32f, 0f, Both, NavCellFlags.None, 1f));
        CellId top = b.AddCell(new NavCell(0f, 0f, 16f, 16f, 128f, Both, NavCellFlags.None, 1f));
        b.AddBoth(here, foot, NavEdgeType.Walk);
        b.AddBoth(foot, top, NavEdgeType.Ladder);
        NavGraph g = b.Build();
        var f = new NavFollower(g, top, g.Cell(top).Centre, ClearanceClass.Crouch, new NavObstacleState(0));

        MoveIntent move = f.Next(g.Cell(here).Centre, out _);

        Assert.Equal((1, g.Cell(foot).Centre), (move.CornerCount, move.Corner(0)));
    }

    /// <summary>A room with a block in it and the objective behind the block: two routes round it cost the same.</summary>
    private static (NavGraph G, int[,] Ids, AiVector Left, AiVector Right, CellId Objective) RoundABlock()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse(
            ".........",
            ".........",
            "..     ..",
            "..     ..",
            ".........",
            ".........");
        return (g, ids, g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 3, 4)).Centre, g.Cell(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 5, 4)).Centre,
                SourceSharp.Ai.Tests.Nav.Grids.At(ids, 4, 0));
    }

    private static NavFollower Round(NavGraph g, CellId objective)
        => new(g, objective, g.Cell(objective).Centre, ClearanceClass.Crouch, new NavObstacleState(0));

    /// <summary>
    /// Two equally good corners do not alternate: a bot on the ridge between two
    /// routes round a block, its cell's next step differing either side of it,
    /// holds the corner it was making for (live: -2984 and -3176 in turn, tick by
    /// tick, the strafe's jitter). The control shows the two are there to pick.
    /// </summary>
    [Fact]
    public void TwoEquallyGoodCornersDoNotAlternate()
    {
        var (g, _, left, right, objective) = RoundABlock();
        float fromLeft = Round(g, objective).Next(left, out _).Corner(0).X, fromRight = Round(g, objective).Next(right, out _).Corner(0).X;
        Assert.True(fromLeft < left.X && fromRight > right.X, $"the control: from either side its own corner, {fromLeft} and {fromRight}");

        NavFollower f = Round(g, objective);
        AiVector held = f.Next(left, out _).Corner(0);
        for (int t = 0; t < 40; t++)
            Assert.Equal(held, f.Next(t % 2 == 0 ? right : left, out _).Corner(0));
    }

    /// <summary>A held corner goes when it is passed: holding it for good, the bot circled it.</summary>
    [Fact]
    public void AHeldCornerGoesWhenItIsPassed()
    {
        var (g, _, left, _, objective) = RoundABlock();
        NavFollower f = Round(g, objective);
        AiVector held = f.Next(left, out _).Corner(0);
        AiVector next = f.Next(held + new AiVector(-2f, 2f, 0f), out _).Corner(0);
        Assert.NotEqual(held, next);
    }

    /// <summary>A new goal's follower goes on to the held corner while it leads the new way, and drops it when it does not: the route has really changed.</summary>
    [Fact]
    public void ANewGoalKeepsAHeldCornerOnlyWhileItLeadsThatWay()
    {
        var (g, ids, left, right, objective) = RoundABlock();
        NavFollower f = Round(g, objective);
        AiVector held = f.Next(left, out _).Corner(0);

        NavFollower sameWay = Round(g, SourceSharp.Ai.Tests.Nav.Grids.At(ids, 5, 0));
        sameWay.GoOnFrom(f);
        Assert.Equal(held, sameWay.Next(left, out _).Corner(0));

        NavFollower behind = Round(g, SourceSharp.Ai.Tests.Nav.Grids.At(ids, 8, 5));
        behind.GoOnFrom(f);
        Assert.NotEqual(held, behind.Next(right, out _).Corner(0));
    }

    /// <summary>
    /// Past the take-off the route is a walk again, and the crouch-jump stays
    /// ducked until it lands: letting go in the air dropped the feet 36 u, into
    /// the rail (live, ss_ai_nav). On the floor again it stands.
    /// </summary>
    [Fact]
    public void ACrouchJumpStaysDuckedUntilItLands()
    {
        var (g, walkway, _, rail, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        Assert.Equal(MoveKind.CrouchJump, f.Next(g.Cell(walkway).Centre, out _).Kind);

        // Over the rail's top, its route a drop: no more jump, still the duck.
        AiVector overRail = g.Cell(rail).Centre + new AiVector(0f, 0f, 10f);
        MoveIntent air = f.Next(overRail, out _);
        Assert.NotEqual(MoveKind.CrouchJump, air.Kind);
        Assert.True(air.CrouchOn, "let go of the duck in the air");

        for (int t = 0; t < NavFollower.TuckRise + 1; t++)
            f.Next(overRail, out _);
        Assert.False(f.Next(g.Cell(rail).Centre, out _).CrouchOn, "still ducked on the floor");
    }

    /// <summary>
    /// A bot off the graph with its goal elsewhere does not hold still: it heads
    /// for the nearest cell with a way on, and told it is stuck it jumps at
    /// whatever holds it (live, on a ladder's column mid-air it held for good,
    /// reporting stuck to no effect).
    /// </summary>
    [Fact]
    public void ABotOffTheGraphHeadsBackAndWhenStuckJumps()
    {
        var (g, _, _, _, beyond) = RailCourse();
        var f = new NavFollower(g, beyond, g.Cell(beyond).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        var off = new AiVector(-64f, 16f, 130f);
        MoveIntent move = f.Next(off, out FollowState state);
        Assert.Equal(FollowState.OffGraph, state);
        Assert.Equal(MoveKind.Follow, move.Kind);
        Assert.True(move.CornerCount == 1 && move.Corner(0).X >= 0f, $"heads to a cell: {move.Corner(0)}");
        Assert.False(f.ReportStuck(off));
        Assert.Equal(MoveKind.CrouchJump, f.Next(off, out _).Kind);
    }

    /// <summary>
    /// A prop that stands in the only way out: routed round while there is a way
    /// round, and shoved through when there is none, as a player does (live,
    /// dm_lockdown: "NoRoute" for good behind a sink). What it cannot shove stays shut.
    /// </summary>
    [Fact]
    public void WithNoOtherWayOutTheBotGoesThroughAPropItCanShove()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        NavGraph Line(out CellId first, out CellId last)
        {
            var b = new NavGraphBuilder();
            int prop = b.AddObstacle();
            first = b.AddCell(new NavCell(0f, 0f, 32f, 32f, 0f, Both, NavCellFlags.None, 1f));
            CellId mid = b.AddCell(new NavCell(32f, 0f, 64f, 32f, 0f, Both, NavCellFlags.None, 1f));
            last = b.AddCell(new NavCell(64f, 0f, 96f, 32f, 0f, Both, NavCellFlags.None, 1f));
            b.AddEdge(first, mid, NavEdgeType.Walk, 0f, prop);
            b.AddEdge(mid, first, NavEdgeType.Walk, 0f, prop);
            b.AddBoth(mid, last, NavEdgeType.Walk);
            return b.Build();
        }

        NavGraph g = Line(out CellId from, out CellId to);
        var shovable = new NavObstacleState(1);
        shovable.Set(0, true);
        shovable.SetShovable(0);
        var f = new NavFollower(g, to, g.Cell(to).Centre, ClearanceClass.Crouch, shovable);
        f.Next(g.Cell(from).Centre, out FollowState through);
        Assert.Equal(FollowState.Moving, through);
        Assert.True(f.Shoving);

        var shut = new NavObstacleState(1);
        shut.Set(0, true);
        var refused = new NavFollower(g, to, g.Cell(to).Centre, ClearanceClass.Crouch, shut);
        refused.Next(g.Cell(from).Centre, out FollowState none);
        Assert.Equal(FollowState.NoRoute, none);
    }

    /// <summary>
    /// A link that does not work (a drop through standing glass) is not taken again
    /// when the bot reports stuck, whatever floor it lands on: the strip ahead only
    /// avoids its own floor (live, dm_lockdown: 25 s on a window's sill).
    /// </summary>
    [Fact]
    public void AStuckBotDoesNotTakeTheSameLinkAgain()
    {
        const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        var b = new NavGraphBuilder();
        CellId sill = b.AddCell(new NavCell(0f, 0f, 32f, 32f, 128f, Both, NavCellFlags.None, 1f));
        CellId below = b.AddCell(new NavCell(32f, 0f, 64f, 32f, 0f, Both, NavCellFlags.None, 1f));
        CellId floor = b.AddCell(new NavCell(64f, 0f, 96f, 32f, 0f, Both, NavCellFlags.None, 1f));
        CellId back = b.AddCell(new NavCell(-32f, 0f, 0f, 32f, 0f, Both, NavCellFlags.None, 1f));
        var round = new List<CellId>();
        for (int i = 0; i < 6; i++)
            round.Add(b.AddCell(new NavCell(-32f + i * 32f, 128f, i * 32f, 160f, 0f, Both, NavCellFlags.None, 1f)));
        b.AddEdge(sill, below, NavEdgeType.Drop);
        b.AddBoth(below, floor, NavEdgeType.Walk);
        b.AddEdge(sill, back, NavEdgeType.Drop);
        b.AddBoth(back, round[0], NavEdgeType.Walk);
        for (int i = 0; i + 1 < round.Count; i++)
            b.AddBoth(round[i], round[i + 1], NavEdgeType.Walk);
        b.AddBoth(round[^1], floor, NavEdgeType.Walk);
        NavGraph g = b.Build();
        var f = new NavFollower(g, floor, g.Cell(floor).Centre, ClearanceClass.Crouch, new NavObstacleState(0));
        AiVector on = g.Cell(sill).Centre;
        f.Next(on, out _);
        Assert.Equal(below, f.NextCell);
        Assert.True(f.ReportStuck(on));
        f.Next(on, out _);
        Assert.Equal(back, f.NextCell);
    }

    /// <summary>A second report whose way round has not freed the bot jumps as well; a new goal's follower goes on with it.</summary>
    [Fact]
    public void AStuckBotsSecondReportJumpsAndANewGoalKeepsIt()
    {
        NavFollower f = To(new AiVector(40, 470, 16));
        var at = new AiVector(40, 200, 16);
        f.Next(at, out _);
        Assert.True(f.ReportStuck(at));
        Assert.Equal(MoveKind.Follow, f.Next(at, out _).Kind);
        Assert.True(f.ReportStuck(at));
        Assert.Equal(MoveKind.CrouchJump, f.Next(at, out _).Kind);

        NavFollower fresh = To(new AiVector(60, 470, 16));
        fresh.GoOnFrom(f);
        Assert.Equal(MoveKind.CrouchJump, fresh.Next(at, out _).Kind);
        Assert.NotEmpty(fresh.Avoided);
    }

    /// <summary>
    /// A follower asked for its first move before any field is built, by a
    /// bot standing where its class is not admitted, answers a move rather
    /// than reading a field it has not got (live: "Hl2mpBots.OnPreThink threw:
    /// Index was outside the bounds of the array").
    /// </summary>
    [Fact]
    public void AFollowersFirstMoveFromACellItsClassRefusesReadsNoUnbuiltField()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse(["..ccc.."]);
        var follower = new NavFollower(g, SourceSharp.Ai.Tests.Nav.Grids.At(ids, 0, 0), g.Cell(ids[0, 0]).Centre, ClearanceClass.Stand,
                                       new NavObstacleState(g.ObstacleCount));
        follower.Next(g.Cell(ids[3, 0]).Centre, out FollowState state);
        Assert.NotEqual(FollowState.Arrived, state);
    }

    /// <summary>
    /// At a drop's edge, the bot past its own cell's centre steers on, off
    /// the edge, not back to the centre behind it (live, ss_ai_nav's step
    /// top: "next Drop ... steering to (984 152 80)", 18 u behind, for ever).
    /// </summary>
    [Fact]
    public void AtADropsEdgeTheBotSteersOnNotBackToItsCellsCentre()
    {
        var b = new NavGraphBuilder();
        CellId p1 = b.AddCell(new NavCell(0f, 0f, 32f, 32f, 64f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        CellId p2 = b.AddCell(new NavCell(0f, 32f, 32f, 64f, 64f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        CellId floor = b.AddCell(new NavCell(0f, 96f, 32f, 128f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        CellId far = b.AddCell(new NavCell(0f, 128f, 32f, 160f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        b.AddBoth(p1, p2, NavEdgeType.Walk);
        b.AddEdge(p2, floor, NavEdgeType.Drop);
        b.AddBoth(floor, far, NavEdgeType.Walk);
        NavGraph g = b.Build();
        var follower = new NavFollower(g, far, g.Cell(far).Centre, ClearanceClass.Crouch, new NavObstacleState(0));

        MoveIntent move = follower.Next(new AiVector(16f, 62f, 64f), out FollowState state);

        Assert.Equal(FollowState.Moving, state);
        Assert.True(move.CornerCount >= 1 && move.Corner(0).Y > 62f, $"the first corner {move.Corner(0)} is ahead, off the edge");
        Assert.True(move.Corner(0).Y > g.Cell(floor).Centre.Y + 8f, $"the first corner {move.Corner(0)} is past the landing, so the body clears the lip");
    }
}
