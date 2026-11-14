using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// Links beside a graph (lane G5c-2a): a route search takes them only for an agent with their
/// reach and the health they cost, costs them by <see cref="NavLinkCosts"/>, names the steps
/// that are links, and stays a cheapest-path search when a link beats the straight line.
/// </summary>
public class NavLinksTests
{
    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>Two islands on one row, five cells each, a cell of nothing between: (0..4, 0) and (6..10, 0).</summary>
    private static (NavGraph Graph, int[,] Ids) Islands() => Grids.Parse("..... .....");

    private static NavLink Hop(int[,] ids, float seconds = 1f, float health = 30f, float robustness = 1f, int id = 77)
        => new(ids[4, 0], ids[6, 0], NavLinkKind.Blast, seconds, health, robustness, id);

    private static NavObstacleState Agent(JumpReach reach = JumpReach.Blast, float health = 100f) => new(0) { Reach = reach, LinkHealth = health };

    private static PathStatus Search(NavGraph g, NavLinks links, CellId from, CellId to, NavObstacleState agent, List<CellId> path, List<int> steps, out float cost)
        => new NavPathfinder().Find(g, links, from, to, ClearanceClass.Stand, agent, path, steps, 100_000, out cost);

    [Fact]
    public void WithoutTheBlastReachASearchTakesNoLink()
    {
        var (g, ids) = Islands();
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Unreachable, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(JumpReach.None), path, steps, out _));
        Assert.Equal(PathStatus.Unreachable, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(JumpReach.Double), path, steps, out _));
        // The control: with the reach, the same search crosses.
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(), path, steps, out _));
    }

    [Fact]
    public void UnderTheHealthALinkCostsTheSearchLeavesItOut()
    {
        var (g, ids) = Islands();
        NavLinks links = NavLinks.Build(g, [Hop(ids, health: 30f)], Costs);
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Unreachable, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(health: 29.9f), path, steps, out _));
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(health: 30f), path, steps, out _));
    }

    [Fact]
    public void APathNamesTheStepThatIsALinkAndWhichLink()
    {
        var (g, ids) = Islands();
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[0, 0], ids[10, 0], NavLinkKind.Blast, 99f, 0f, 1f, 5), Hop(ids, id: 77)], Costs);
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(), path, steps, out _));
        Assert.Equal(path.Count - 1, steps.Count);
        int at = path.FindIndex(c => c.Index == ids[4, 0]);
        Assert.Equal(ids[6, 0], path[at + 1].Index);
        Assert.Equal(1, steps[at]);
        Assert.Equal(77, links[steps[at]].Id);
        Assert.All(steps.Where((_, i) => i != at), s => Assert.Equal(-1, s));
    }

    [Fact]
    public void ALinksCostIsItsSecondsItsHealthAndItsMissesAtTheRunSpeed()
    {
        var costs = new NavLinkCosts { RunSpeed = 240f };
        Assert.True(costs.HealthSeconds > 0f && costs.MissSeconds > 0f, "the control: health and misses weigh something");
        var plain = new NavLink(0, 1, NavLinkKind.Blast, 1.5f, 0f, 1f, 0);
        Assert.Equal(1.5f * 240f, costs.Cost(plain), 3);
        Assert.Equal((1.5f + 30f * costs.HealthSeconds) * 240f, costs.Cost(plain with { Health = 30f }), 3);
        Assert.Equal((1.5f + 0.4f * costs.MissSeconds) * 240f, costs.Cost(plain with { Robustness = 0.6f }), 3);
        Assert.Equal(costs.Seconds(plain with { Health = 30f, Robustness = 0.6f }) * 240f, costs.Cost(plain with { Health = 30f, Robustness = 0.6f }), 3);
    }

    [Fact]
    public void ASearchCostsAPathWithALinkAtTheLinksCostAndItsWalks()
    {
        var (g, ids) = Islands();
        NavLink hop = Hop(ids, seconds: 1.25f, health: 30f, robustness: 0.95f);
        NavLinks links = NavLinks.Build(g, [hop], Costs);
        Assert.Equal(Costs.Cost(hop), links.CostOf(0));
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(), path, steps, out float cost));
        // Four walks of a cell either side of the link.
        Assert.Equal(8 * Grids.Size + Costs.Cost(hop), cost, 2);
    }

    [Fact]
    public void TheSearchTakesTheCheaperOfALinkAndTheWalk()
    {
        var (g, ids) = Grids.Parse("..........");
        CellId from = Grids.At(ids, 0, 0), to = Grids.At(ids, 9, 0);
        float walk = 9 * Grids.Size;
        var path = new List<CellId>();
        var steps = new List<int>();
        NavLink Link(float seconds) => new(ids[0, 0], ids[9, 0], NavLinkKind.Blast, seconds, 0f, 1f, 0);

        NavLinks cheaper = NavLinks.Build(g, [Link(0.5f * walk / Costs.RunSpeed)], Costs);
        Search(g, cheaper, from, to, Agent(), path, steps, out float cost);
        Assert.Equal([0], steps);
        Assert.Equal(0.5f * walk, cost, 2);

        NavLinks dearer = NavLinks.Build(g, [Link(1.5f * walk / Costs.RunSpeed)], Costs);
        Search(g, dearer, from, to, Agent(), path, steps, out cost);
        Assert.All(steps, s => Assert.Equal(-1, s));
        Assert.Equal(walk, cost, 2);
    }

    [Fact]
    public void ALinkCheaperThanItsStraightLineStillGivesTheCheapestPath()
    {
        // A row of 40 cells. From cell 10 to cell 39: on foot 29 cells. Or six cells back west to cell 4 and a link from
        // there to the goal that costs far less than the 35 cells' straight line it spans: 6 cells and the link.
        // An estimate of the straight line would put cell 4 behind the walk and answer the walk.
        var (g, ids) = Grids.Parse(new string('.', 40));
        float linkCost = 3f * Grids.Size;
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[39, 0], NavLinkKind.Blast, linkCost / Costs.RunSpeed, 0f, 1f, 0)], Costs);
        Assert.True(links.EstimateScale < 1f, $"the control: the link is cheaper than its straight line ({links.EstimateScale})");
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 10, 0), Grids.At(ids, 39, 0), Agent(), path, steps, out float cost));
        Assert.Equal((6 * Grids.Size) + linkCost, cost, 2);
        Assert.Contains(0, steps);
    }

    [Fact]
    public void AnAgentWithNoReachForTheLinksSearchesAsWithoutThem()
    {
        string[] maze =
        [
            "..........",
            ".  .    ..",
            ". .. ... .",
            ".  .   . .",
            "...... ...",
        ];
        var (g, ids) = Grids.Parse(maze);
        // Links that would cut every corner, the estimate scaled far down by them.
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[0, 4], ids[9, 4], NavLinkKind.Blast, 0.01f, 0f, 1f, 0),
                                            new NavLink(ids[0, 0], ids[9, 0], NavLinkKind.Blast, 0.01f, 0f, 1f, 1)], Costs);
        Assert.True(links.EstimateScale < 0.1f, "the control: the links would change the estimate");
        var state = new NavObstacleState(0) { LinkHealth = 100f };
        var finder = new NavPathfinder();
        var alone = new List<CellId>();
        var beside = new List<CellId>();
        var steps = new List<int>();
        foreach ((int fx, int fy, int tx, int ty) in new[] { (0, 4, 9, 4), (0, 0, 9, 3), (5, 4, 0, 0), (9, 0, 0, 4) })
        {
            PathStatus a = finder.Find(g, Grids.At(ids, fx, fy), Grids.At(ids, tx, ty), ClearanceClass.Stand, state, alone, 10_000, out float ca);
            PathStatus b = finder.Find(g, links, Grids.At(ids, fx, fy), Grids.At(ids, tx, ty), ClearanceClass.Stand, state, beside, steps, 10_000, out float cb);
            Assert.Equal((a, ca), (b, cb));
            Assert.Equal(alone, beside);
            Assert.All(steps, s => Assert.Equal(-1, s));

            // And it looks at no more cells: the least budget the search alone finds in is enough beside the links.
            int least = 1;
            while (finder.Find(g, Grids.At(ids, fx, fy), Grids.At(ids, tx, ty), ClearanceClass.Stand, state, alone, least, out _) == PathStatus.OutOfBudget)
                least++;
            Assert.Equal(PathStatus.Found, finder.Find(g, links, Grids.At(ids, fx, fy), Grids.At(ids, tx, ty), ClearanceClass.Stand, state, beside, steps, least, out _));
        }
    }

    [Fact]
    public void ALinkIntoABlockedCellIsNoWay()
    {
        var (g, ids) = Islands();
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        var blocks = new byte[g.CellCount];
        blocks[ids[6, 0]] = NavCellBlocks.Blocked;
        NavObstacleState agent = Agent();
        agent.Cells = new NavCellBlocks(blocks);
        var path = new List<CellId>();
        var steps = new List<int>();
        Assert.Equal(PathStatus.Unreachable, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), agent, path, steps, out _));
        Assert.Equal(PathStatus.Found, Search(g, links, Grids.At(ids, 0, 0), Grids.At(ids, 10, 0), Agent(), path, steps, out _));
    }

    [Fact]
    public void ALinkIsAdmittedOnlyWithItsReach()
    {
        var (g, ids) = Islands();
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        Assert.Equal(JumpReach.Blast, links.Reaches);
        Assert.False(links.Admits(g, 0, ClearanceClass.Stand, Agent(JumpReach.None)));
        Assert.False(links.Admits(g, 0, ClearanceClass.Stand, Agent(JumpReach.Double)));
        Assert.True(links.Admits(g, 0, ClearanceClass.Stand, Agent(JumpReach.Blast | JumpReach.Double)));
    }

    [Fact]
    public void ALinkIsAdmittedOnlyForABodyBothItsCellsHold()
    {
        var (g, ids) = Grids.Parse("..c");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[0, 0], ids[2, 0], NavLinkKind.Blast, 1f, 0f, 1f, 0)], Costs);
        Assert.False(links.Admits(g, 0, ClearanceClass.Stand, Agent()));
        Assert.True(links.Admits(g, 0, ClearanceClass.Crouch, Agent()));
    }

    [Fact]
    public void ASetOverAnotherGraphIsRefused()
    {
        var (g, ids) = Islands();
        var (other, _) = Grids.Parse("..... ....");
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        Assert.True(links.IsOver(g) && !links.IsOver(other));
        Assert.Throws<ArgumentException>(() => new NavPathfinder().Find(other, links, new CellId(0), new CellId(1), ClearanceClass.Stand, Agent(), [], null, 100, out _));
    }

    [Fact]
    public void ALinkToACellTheGraphDoesNotHaveIsRefused()
    {
        var (g, ids) = Islands();
        Assert.Throws<ArgumentException>(() => NavLinks.Build(g, [Hop(ids) with { To = g.CellCount }], Costs));
    }

    [Fact]
    public void TheStatesCopiesCarryTheHealthALinkMaySpend()
    {
        var state = new NavObstacleState(2) { Reach = JumpReach.Blast, LinkHealth = 42.5f };
        Assert.Equal(42.5f, state.Clone().LinkHealth);
        var copy = new NavObstacleState(2);
        copy.CopyFrom(state);
        Assert.Equal((JumpReach.Blast, 42.5f), (copy.Reach, copy.LinkHealth));
        Assert.Equal(42.5f, state.Shoved().LinkHealth);
    }

    [Fact]
    public void ASetsIndexByCellHoldsEachLinkOnceEachWay()
    {
        var (g, ids) = Islands();
        NavLink a = Hop(ids, id: 1), b = Hop(ids, id: 2) with { From = ids[6, 0], To = ids[4, 0] }, c = Hop(ids, id: 3);
        NavLinks links = NavLinks.Build(g, [a, b, c], Costs);
        Assert.Equal([0, 2], links.From(ids[4, 0]).ToArray());
        Assert.Equal([1], links.From(ids[6, 0]).ToArray());
        Assert.Equal([0, 2], links.To(ids[6, 0]).ToArray());
        Assert.Equal([1], links.To(ids[4, 0]).ToArray());
        Assert.Empty(links.From(ids[0, 0]).ToArray());
        // The hash follows the links: another order, another set.
        Assert.NotEqual(links.Hash, NavLinks.Build(g, [c, b, a], Costs).Hash);
        Assert.Equal(links.Hash, NavLinks.Build(g, [a, b, c], Costs).Hash);
    }
}
