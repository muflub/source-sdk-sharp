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
    }
}
