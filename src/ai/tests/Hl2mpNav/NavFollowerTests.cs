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

    [Fact]
    public void AtALedgesTakeOffItJumps()
    {
        MoveIntent move = To(new AiVector(200, 200, 46)).Next(new AiVector(200, 136, 16), out FollowState state);
        Assert.Equal((FollowState.Moving, MoveKind.Jump), (state, move.Kind));
    }

    [Fact]
    public void IntoTheCrawlspaceItDucks()
    {
        MoveIntent move = To(new AiVector(440, 200, 16)).Next(new AiVector(440, 136, 16), out _);
        Assert.True(move.CrouchOn);
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
}
