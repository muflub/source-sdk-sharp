using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A route that takes two links in a row (lane G5e): the follower leads the bot to the first link's start and, once it
/// has come down on its landing, to the second's, and so on to the goal; a bot that can no longer pay for the second
/// when it comes down is routed round at once, never standing at its takeoff.
/// </summary>
public sealed class NavFollowerChainTests
{
    /// <summary>
    /// Three floors on the bottom row, gaps of three cells between them, joined the long way over the top (the middle
    /// one by its own way up at x 12): link 0 from (7, 2) to (11, 2) and link 1 from (14, 2) to (18, 2), each cheaper
    /// than the walk round.
    /// </summary>
    private static readonly string[] Steps =
    [
        ".......................",
        ".           .         .",
        "........   ....   .....",
    ];

    private sealed record Rig(NavGraph Graph, int[,] Ids, NavLinks Links, NavObstacleState State, NavFollower Follower)
    {
        public AiVector At(int x, int y) => Graph.Cell(new CellId(Ids[x, y])).Centre;

        /// <summary>Flies link <paramref name="link"/> from its start (begun) to its landing, and comes down still there.</summary>
        public void Fly(int link)
        {
            AiVector start = Graph.Cell(new CellId(Links[link].From)).Centre, landing = Graph.Cell(new CellId(Links[link].To)).Centre;
            Follower.BeginLink(link);
            Follower.Next(((start + landing) * 0.5f) with { Z = 120f }, out _);
            Follower.Next(landing with { Z = 40f }, out _);
            for (int i = 0; i < NavFollower.LinkStillMoves + 1; i++)
                Follower.Next(landing, out _);
        }
    }

    private static Rig Make(float health = 100f)
    {
        var (g, ids) = Grids.Parse(Steps);
        NavLinks set = NavLinks.Build(g, [new NavLink(ids[7, 2], ids[11, 2], NavLinkKind.Blast, 0.2f, 10f, 1f, 0), new NavLink(ids[14, 2], ids[18, 2], NavLinkKind.Blast, 0.2f, 10f, 1f, 1)],
                                      new NavLinkCosts { RunSpeed = 240f });
        var state = new NavObstacleState(0) { Links = set, Reach = JumpReach.Blast, LinkHealth = health };
        CellId objective = Grids.At(ids, 21, 2);
        return new Rig(g, ids, set, state, new NavFollower(g, objective, g.Cell(objective).Centre, ClearanceClass.Crouch, state));
    }

    [Fact]
    public void ARouteOfTwoLinksIsFollowedLinkAfterLink()
    {
        Rig r = Make();
        r.Follower.Next(r.At(4, 2), out _);
        Assert.Equal((0, false), (r.Follower.LinkStep.Link, r.Follower.LinkStep.AtStart));
        r.Follower.Next(r.At(7, 2), out _);
        Assert.True(r.Follower.LinkStep.AtStart);
        r.Fly(0);
        // Down on the first's landing: the next step is the second link, and the bot is led to its start.
        MoveIntent move = r.Follower.Next(r.At(11, 2), out _);
        Assert.Equal((1, false, MoveKind.Follow), (r.Follower.LinkStep.Link, r.Follower.LinkStep.AtStart, move.Kind));
        r.Follower.Next(r.At(14, 2), out _);
        Assert.Equal((1, true), (r.Follower.LinkStep.Link, r.Follower.LinkStep.AtStart));
        r.Fly(1);
        Assert.Equal((2, 2, 0), (r.Follower.LinksBegun, r.Follower.LinksLanded, r.Follower.LinksFailed));
        r.Follower.Next(r.At(18, 2), out _);
        Assert.False(r.Follower.LinkStep.IsLink);
        Assert.Equal(r.Ids[19, 2], r.Follower.NextCell.Index);
    }

    /// <summary>The first link's blast took the health the second needs: down on its landing, the bot is routed round at once.</summary>
    [Fact]
    public void ABotThatCanNoLongerPayForTheSecondLinkIsRoutedRoundAtOnce()
    {
        Rig r = Make();
        r.Follower.Next(r.At(7, 2), out _);
        r.Fly(0);
        r.State.LinkHealth = 9f;
        r.Follower.Next(r.At(11, 2), out _);
        Assert.Contains(1, r.Follower.AvoidedLinks);
        r.Follower.Next(r.At(11, 2), out _);
        Assert.False(r.Follower.LinkStep.IsLink);
        Assert.False(r.Follower.InLink);
        Assert.Equal(r.Ids[12, 2], r.Follower.NextCell.Index);
    }
}
