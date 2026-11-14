using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A follower whose only way is a link the bot's health does not cover (lane G7a): it says there is no way and what the
/// link needs, and once the health covers it (a cabinet) it takes the link again at once, where a link refused or
/// failed for anything else stays out for <see cref="NavFollower.LinkAvoidMoves"/>.
/// </summary>
public sealed class NavFollowerHealthTests
{
    /// <summary>Two islands of a row, x 0..7 and 11..16; only the link from (7, 0) to (11, 0), 30 health, joins them.</summary>
    private static readonly string[] Islands = ["........   ......"];

    private sealed record Rig(NavGraph Graph, int[,] Ids, NavLinks Links, NavObstacleState State, NavFollower Follower)
    {
        public AiVector At(int x) => Graph.Cell(new CellId(Ids[x, 0])).Centre;

        public AiVector Start => At(7);
    }

    private static Rig Make(float health)
    {
        var (g, ids) = Grids.Parse(Islands);
        var hop = new NavLink(ids[7, 0], ids[11, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0) { Start = g.Cell(new CellId(ids[7, 0])).Centre };
        NavLinks set = NavLinks.Build(g, [hop], new NavLinkCosts { RunSpeed = 240f });
        var state = new NavObstacleState(0) { Links = set, Reach = JumpReach.Blast, LinkHealth = health };
        CellId objective = Grids.At(ids, 14, 0);
        return new Rig(g, ids, set, state, new NavFollower(g, objective, g.Cell(objective).Centre, ClearanceClass.Crouch, state));
    }

    [Fact]
    public void ABotWhoseHealthFallsUnderItsOnlyLinkHasNoWayAndIsToldWhatTheLinkNeeds()
    {
        Rig r = Make(100f);
        r.Follower.Next(r.At(3), out _);
        Assert.True(r.Follower.LinkStep.IsLink);
        r.State.LinkHealth = 20f;
        r.Follower.Next(r.At(3), out _);
        r.Follower.Next(r.At(3), out FollowState state);
        Assert.Equal(FollowState.NoRoute, state);
        Assert.Equal((true, 30f, -1), (r.Follower.LinkStep.NoWay, r.Follower.LinkStep.Need, r.Follower.LinkStep.Link));
        Assert.Equal(r.Follower.Target, r.Follower.LinkStep.Objective);
    }

    [Fact]
    public void WhenItsHealthCoversTheLinkAgainItIsTakenAtOnceNotAfterTheWait()
    {
        Rig r = Make(100f);
        r.Follower.Next(r.At(3), out _);
        r.State.LinkHealth = 20f;
        r.Follower.Next(r.At(3), out _);
        r.Follower.Next(r.At(3), out _);
        Assert.Contains(0, r.Follower.AvoidedLinks);
        r.State.LinkHealth = 110f;
        r.Follower.Next(r.At(3), out FollowState state);
        Assert.Equal(FollowState.Moving, state);
        Assert.Empty(r.Follower.AvoidedLinks);
        Assert.True(r.Follower.LinkStep.IsLink);
        Assert.False(r.Follower.LinkStep.NoWay);
    }

    [Fact]
    public void AFollowerMadeWhenTheBotIsAlreadyShortSaysTheNeedAndTakesTheLinkWhenTheHealthIsBack()
    {
        Rig r = Make(20f);
        r.Follower.Next(r.At(3), out FollowState state);
        Assert.Equal(FollowState.NoRoute, state);
        Assert.Equal((true, 30f), (r.Follower.LinkStep.NoWay, r.Follower.LinkStep.Need));
        r.State.LinkHealth = 30f;
        r.Follower.Next(r.At(3), out state);
        Assert.Equal(FollowState.Moving, state);
        Assert.True(r.Follower.LinkStep.IsLink);
    }

    [Fact]
    public void ALinkThatFailedStaysOutWhateverTheHealthAndItsNoWayNeedsNothing()
    {
        Rig r = Make(100f);
        r.Follower.Next(r.Start, out _);
        r.Follower.BeginLink(0);
        for (int i = 0; i <= NavFollower.LinkLaunchMoves; i++)
            r.Follower.Next(r.Start, out _);
        Assert.Equal(1, r.Follower.LinksFailed);
        r.State.LinkHealth = 200f;
        r.Follower.Next(r.Start, out FollowState state);
        Assert.Contains(0, r.Follower.AvoidedLinks);
        Assert.Equal(FollowState.NoRoute, state);
        Assert.Equal((true, 0f), (r.Follower.LinkStep.NoWay, r.Follower.LinkStep.Need));
    }

    [Fact]
    public void ANewGoalsFollowerKeepsTheLinkOutUntilTheHealthCoversIt()
    {
        Rig r = Make(100f);
        r.Follower.Next(r.At(3), out _);
        r.State.LinkHealth = 20f;
        r.Follower.Next(r.At(3), out _);
        var next = new NavFollower(r.Graph, Grids.At(r.Ids, 15, 0), r.At(15), ClearanceClass.Crouch, r.State);
        next.GoOnFrom(r.Follower);
        next.Next(r.At(3), out FollowState state);
        Assert.Equal((FollowState.NoRoute, 30f), (state, next.LinkStep.Need));
        r.State.LinkHealth = 40f;
        next.Next(r.At(3), out state);
        Assert.Equal(FollowState.Moving, state);
        Assert.Empty(next.AvoidedLinks);
    }

    [Fact]
    public void ARouteWithAWaySaysNoNoWay()
    {
        Rig r = Make(100f);
        r.Follower.Next(r.At(3), out _);
        Assert.False(r.Follower.LinkStep.NoWay);
        Assert.Equal(0f, r.Follower.LinkStep.Need);
    }
}
