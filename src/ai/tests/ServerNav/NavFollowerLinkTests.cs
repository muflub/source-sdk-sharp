using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A follower whose route takes a link (lane G5c-2b): it says the next step is the link and which,
/// leads the bot to the link's start and holds there for the brain, briefly; a link begun is the
/// program's until the bot comes down, and one refused, failed or no longer the bot's to pay for is
/// left out for a while and routed round. A bot never stands at a takeoff waiting.
/// </summary>
public sealed class NavFollowerLinkTests
{
    /// <summary>
    /// The bottom row's west half (x 0..7) and east half (x 11..16), a gap of three cells, joined the
    /// long way over the top; the link from (7, 2) to (11, 2) is cheaper than that walk.
    /// </summary>
    private static readonly string[] Gap =
    [
        ".................",
        ".               .",
        "........   ......",
    ];

    private sealed record Rig(NavGraph Graph, int[,] Ids, NavLinks Links, NavObstacleState State, NavFollower Follower)
    {
        public AiVector At(int x, int y) => Graph.Cell(new CellId(Ids[x, y])).Centre;

        /// <summary>Where the link's first command is: inside its takeoff cell, off the centre.</summary>
        public AiVector Start => At(7, 2) + new AiVector(-10f, 5f, 0f);

        public AiVector Landing => At(11, 2);
    }

    private static Rig Make(bool links = true, JumpReach reach = JumpReach.Blast, float health = 100f)
    {
        var (g, ids) = Grids.Parse(Gap);
        var hop = new NavLink(ids[7, 2], ids[11, 2], NavLinkKind.Blast, 1f, 30f, 1f, 0) { Start = g.Cell(new CellId(ids[7, 2])).Centre + new AiVector(-10f, 5f, 0f) };
        NavLinks set = NavLinks.Build(g, [hop], new NavLinkCosts { RunSpeed = 240f });
        var state = new NavObstacleState(0) { Links = links ? set : null, Reach = reach, LinkHealth = health };
        CellId objective = Grids.At(ids, 14, 2);
        var follower = new NavFollower(g, objective, g.Cell(objective).Centre, ClearanceClass.Crouch, state);
        return new Rig(g, ids, set, state, follower);
    }

    private static MoveIntent Hold(Rig r, int moves)
    {
        MoveIntent move = default;
        for (int i = 0; i < moves; i++)
            move = r.Follower.Next(r.Start, out _);
        return move;
    }

    [Fact]
    public void TheFollowerSaysTheNextStepIsALinkAndLeadsTheBotToItsStart()
    {
        Rig r = Make();
        MoveIntent move = r.Follower.Next(r.At(4, 2), out FollowState state);
        Assert.Equal((FollowState.Moving, MoveKind.Follow), (state, move.Kind));
        Assert.True(r.Follower.LinkStep.IsLink);
        Assert.Equal((0, r.Links.Hash, false), (r.Follower.LinkStep.Link, r.Follower.LinkStep.Links, r.Follower.LinkStep.AtStart));
        Assert.Equal(r.Start, move.Corner(move.CornerCount - 1));
        Assert.False(r.Follower.InLink);
    }

    [Fact]
    public void AtTheStartTheBotHoldsAndSaysItIsThere()
    {
        Rig r = Make();
        MoveIntent move = r.Follower.Next(r.Start, out FollowState state);
        Assert.Equal((FollowState.Moving, MoveKind.Hold), (state, move.Kind));
        Assert.True(r.Follower.LinkStep.AtStart);
        Assert.True(r.Follower.InLink);
    }

    [Fact]
    public void AWaitNobodyEndsRunsOutAndTheLinkIsRoutedRound()
    {
        Rig r = Make();
        Hold(r, NavFollower.LinkHoldMoves);
        Assert.True(r.Follower.LinkStep.AtStart, "still waiting on the last move of the wait");
        Hold(r, 1);
        Assert.Equal((1, true), (r.Follower.LinkWaitsOut, r.Follower.AvoidedLinks.Contains(0)));
        MoveIntent move = Hold(r, 1);
        Assert.False(r.Follower.LinkStep.IsLink);
        Assert.False(r.Follower.InLink);
        Assert.Equal(MoveKind.Follow, move.Kind);
        Assert.NotEqual(r.Ids[11, 2], r.Follower.NextCell.Index);
    }

    [Fact]
    public void AnAvoidedLinkComesBackWhenItsTimeIsUp()
    {
        Rig r = Make();
        Hold(r, NavFollower.LinkHoldMoves + 1);
        Assert.Contains(0, r.Follower.AvoidedLinks);
        for (int i = 0; i < NavFollower.LinkAvoidMoves; i++)
            r.Follower.Next(r.At(4, 2), out _);
        Assert.Empty(r.Follower.AvoidedLinks);
        Assert.True(r.Follower.LinkStep.IsLink);
    }

    [Fact]
    public void ABegunLinkThatComesDownOnItsLandingIsDoneAndTheRouteGoesOn()
    {
        Rig r = Make();
        Hold(r, 1);
        r.Follower.BeginLink(0);
        Hold(r, 3);
        Assert.True(r.Follower.InLink);
        Assert.False(r.Follower.LinkStep.IsLink, "on the link the brain is told nothing to begin");
        AiVector mid = (r.Start + r.Landing) * 0.5f;
        r.Follower.Next(mid with { Z = 120f }, out _);
        r.Follower.Next(r.Landing with { Z = 40f }, out _);
        for (int i = 0; i < NavFollower.LinkStillMoves + 1; i++)
            r.Follower.Next(r.Landing, out _);
        Assert.Equal((1, 1, 0), (r.Follower.LinksBegun, r.Follower.LinksLanded, r.Follower.LinksFailed));
        Assert.False(r.Follower.InLink);
        Assert.Empty(r.Follower.AvoidedLinks);
        MoveIntent move = r.Follower.Next(r.Landing, out _);
        Assert.Equal(MoveKind.Follow, move.Kind);
    }

    [Fact]
    public void ABegunLinkThatComesDownElsewhereFailedAndIsLeftOut()
    {
        Rig r = Make();
        Hold(r, 1);
        r.Follower.BeginLink(0);
        r.Follower.Next(r.Start with { Z = 72f }, out _);
        for (int i = 0; i < NavFollower.LinkStillMoves + 2; i++)
            r.Follower.Next(r.Start, out _);
        Assert.Equal((0, 1), (r.Follower.LinksLanded, r.Follower.LinksFailed));
        Assert.Contains(0, r.Follower.AvoidedLinks);
        Assert.False(r.Follower.InLink);
    }

    [Fact]
    public void ABegunLinkThatNeverLeavesTheGroundFailed()
    {
        Rig r = Make();
        Hold(r, 1);
        r.Follower.BeginLink(0);
        Hold(r, NavFollower.LinkLaunchMoves);
        Assert.True(r.Follower.InLink, "still the program's on the last move it has");
        Hold(r, 1);
        Assert.Equal(1, r.Follower.LinksFailed);
        Assert.Contains(0, r.Follower.AvoidedLinks);
    }

    [Fact]
    public void ABeginOfAnotherLinkOrAwayFromTheStartIsNoBegin()
    {
        Rig r = Make();
        r.Follower.Next(r.At(4, 2), out _);
        r.Follower.BeginLink(0);
        Assert.Equal(0, r.Follower.LinksBegun);
        Hold(r, 1);
        r.Follower.BeginLink(1);
        Assert.Equal(0, r.Follower.LinksBegun);
        // The control: the link it waits at.
        r.Follower.BeginLink(0);
        Assert.Equal(1, r.Follower.LinksBegun);
    }

    /// <summary>
    /// The field was built when the bot could pay for the link; its health has fallen since (its lane's copy of the
    /// obstacles says so this step): the link is left out at once and the route goes round, before the bot is even
    /// at the takeoff.
    /// </summary>
    [Fact]
    public void AHurtBotsFollowerLeavesOutALinkItCanNoLongerPayFor()
    {
        Rig r = Make();
        r.Follower.Next(r.At(4, 2), out _);
        Assert.True(r.Follower.LinkStep.IsLink);
        r.State.LinkHealth = 29f;
        r.Follower.Next(r.At(4, 2), out _);
        Assert.Contains(0, r.Follower.AvoidedLinks);
        r.Follower.Next(r.At(4, 2), out _);
        Assert.False(r.Follower.LinkStep.IsLink);
        Assert.NotEqual(r.Ids[5, 2], r.Follower.NextCell.Index);
    }

    [Fact]
    public void LinksThatArriveAfterTheFollowerWasMadeAreTaken()
    {
        Rig r = Make(links: false);
        r.Follower.Next(r.At(4, 2), out _);
        Assert.False(r.Follower.LinkStep.IsLink);
        int builds = r.Follower.Builds;
        r.State.Links = r.Links;
        r.Follower.Next(r.At(4, 2), out _);
        Assert.True(r.Follower.LinkStep.IsLink);
        Assert.Equal(builds + 1, r.Follower.Builds);
    }

    [Fact]
    public void WithoutTheReachArrivingLinksBuildNothing()
    {
        Rig r = Make(links: false, reach: JumpReach.Double);
        r.Follower.Next(r.At(4, 2), out _);
        int builds = r.Follower.Builds;
        r.State.Links = r.Links;
        r.Follower.Next(r.At(4, 2), out _);
        Assert.Equal(builds, r.Follower.Builds);
        Assert.False(r.Follower.LinkStep.IsLink);
    }

    /// <summary>A hunt re-goals every half second: a new goal's follower goes on with the wait, or it would never run out.</summary>
    [Fact]
    public void ANewGoalsFollowerGoesOnWithTheWait()
    {
        Rig r = Make();
        Hold(r, NavFollower.LinkHoldMoves / 2);
        var next = new NavFollower(r.Graph, Grids.At(r.Ids, 15, 2), r.At(15, 2), ClearanceClass.Crouch, r.State);
        next.GoOnFrom(r.Follower);
        for (int i = 0; i < NavFollower.LinkHoldMoves - (NavFollower.LinkHoldMoves / 2) + 1; i++)
            next.Next(r.Start, out _);
        Assert.Equal(1, next.LinkWaitsOut);
        Assert.Contains(0, next.AvoidedLinks);
    }
}
