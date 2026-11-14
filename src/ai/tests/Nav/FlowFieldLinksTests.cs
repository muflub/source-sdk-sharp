using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// A flow field and the links beside its graph (lane G5c-2b): it steps over a link where that is
/// cheaper than the walk, for an agent with the link's reach and the health it costs, names the
/// link it steps over, leaves out the links it is told to, and for an agent without the reach is
/// the field it was before links, cell for cell.
/// </summary>
public class FlowFieldLinksTests
{
    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>
    /// Two halves of the bottom row, a gap between them, joined the long way over the top: from
    /// (4, 2) to the objective at (8, 2) the walk is 20 cells (640 u), the link (1 s and 30 health at
    /// 240 u/s, 456 u) and two cells 520 u.
    /// </summary>
    private static readonly string[] Horseshoe =
    [
        "...........",
        ".         .",
        "..... .....",
    ];

    private static NavLink Hop(int[,] ids, float seconds = 1f, float health = 30f) => new(ids[4, 2], ids[6, 2], NavLinkKind.Blast, seconds, health, 1f, 0);

    private static NavObstacleState Agent(NavLinks? links, JumpReach reach = JumpReach.Blast, float health = 100f)
        => new(0) { Links = links, Reach = reach, LinkHealth = health };

    private static FlowField Field(NavGraph g, CellId goal, NavObstacleState agent, IReadOnlySet<int>? avoidedLinks = null)
    {
        var f = new FlowField();
        f.Build(g, goal, ClearanceClass.Stand, agent, avoidedLinks: avoidedLinks);
        return f;
    }

    [Fact]
    public void WithTheReachAFieldStepsOverALinkWhereItIsCheaperAndNamesIt()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        FlowField f = Field(g, Grids.At(ids, 8, 2), Agent(links));
        Assert.Equal(ids[6, 2], f.NextFrom(ids[4, 2]).Index);
        Assert.Equal(NavEdgeType.Link, f.ViaFrom(ids[4, 2]));
        Assert.Equal(0, f.LinkFrom(ids[4, 2]));
        Assert.Equal(links.CostOf(0) + f.CostAt(ids[6, 2]), f.CostAt(ids[4, 2]), 2);
        Assert.Contains(ids[6, 2], Grids.Walk(f, ids[3, 2]));
        // Every other step is an edge.
        Assert.All(Enumerable.Range(0, g.CellCount).Where(c => c != ids[4, 2]), c => Assert.Equal(-1, f.LinkFrom(c)));
    }

    [Fact]
    public void WhereTheWalkIsCheaperTheFieldWalks()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        // 4 s of link (960 u and its health) against the 640 u walk.
        NavLinks links = NavLinks.Build(g, [Hop(ids, seconds: 4f)], Costs);
        FlowField f = Field(g, Grids.At(ids, 8, 2), Agent(links));
        Assert.Equal(-1, f.LinkFrom(ids[4, 2]));
        Assert.NotEqual(NavEdgeType.Link, f.ViaFrom(ids[4, 2]));
    }

    [Fact]
    public void WithoutTheBlastReachTheFieldTakesNoLink()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        foreach (JumpReach reach in new[] { JumpReach.None, JumpReach.Double })
        {
            FlowField f = Field(g, Grids.At(ids, 8, 2), Agent(links, reach));
            Assert.Equal(-1, f.LinkFrom(ids[4, 2]));
            Assert.NotEqual(ids[6, 2], f.NextFrom(ids[4, 2]).Index);
        }

        // The control: with the reach, the same field steps over it.
        Assert.Equal(ids[6, 2], Field(g, Grids.At(ids, 8, 2), Agent(links)).NextFrom(ids[4, 2]).Index);
    }

    [Fact]
    public void UnderTheHealthALinkCostsTheFieldLeavesItOut()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [Hop(ids, health: 30f)], Costs);
        Assert.Equal(-1, Field(g, Grids.At(ids, 8, 2), Agent(links, health: 29.9f)).LinkFrom(ids[4, 2]));
        Assert.Equal(0, Field(g, Grids.At(ids, 8, 2), Agent(links, health: 30f)).LinkFrom(ids[4, 2]));
    }

    [Fact]
    public void AnAvoidedLinkIsRoutedRound()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        FlowField f = Field(g, Grids.At(ids, 8, 2), Agent(links), new HashSet<int> { 0 });
        Assert.Equal(-1, f.LinkFrom(ids[4, 2]));
        Assert.True(float.IsFinite(f.CostAt(ids[0, 2])), "the walk round still reaches it");
        Assert.Contains(ids[0, 0], Grids.Walk(f, ids[0, 2]));
    }

    /// <summary>
    /// Without the reach, a field built with the links is the field built with none, cell for cell
    /// (its cost, its next cell, how it steps, and that no step is a link): every HL2MP agent and
    /// every TF2 class but the Soldier routes exactly as before links.
    /// </summary>
    [Fact]
    public void WithoutTheReachTheFieldIsTheOneWithNoLinksCellForCell()
    {
        var (g, ids) = Grids.Parse(
            "............",
            ". .. . .. ..",
            "....  ......",
            ".  .  .  . .",
            "..... ......");
        NavLink[] all = [new(ids[4, 4], ids[6, 4], NavLinkKind.Blast, 0.5f, 10f, 1f, 0), new(ids[3, 2], ids[6, 2], NavLinkKind.Blast, 0.2f, 10f, 1f, 1)];
        NavLinks links = NavLinks.Build(g, all, Costs);
        Assert.Equal(0, Field(g, Grids.At(ids, 11, 4), Agent(links)).LinkFrom(ids[4, 4]));
        foreach (JumpReach reach in new[] { JumpReach.None, JumpReach.Double })
        {
            FlowField with = Field(g, Grids.At(ids, 11, 4), Agent(links, reach));
            FlowField without = Field(g, Grids.At(ids, 11, 4), Agent(null, reach));
            for (int c = 0; c < g.CellCount; c++)
            {
                Assert.Equal(without.CostAt(c), with.CostAt(c));
                Assert.Equal(without.NextFrom(c), with.NextFrom(c));
                Assert.Equal(without.ViaFrom(c), with.ViaFrom(c));
                Assert.Equal(-1, with.LinkFrom(c));
            }
        }
    }

    [Fact]
    public void AFieldBuiltAgainWithoutTheLinksNamesNoLink()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [Hop(ids)], Costs);
        var f = new FlowField();
        f.Build(g, Grids.At(ids, 8, 2), ClearanceClass.Stand, Agent(links));
        Assert.Equal(0, f.LinkFrom(ids[4, 2]));
        f.Build(g, Grids.At(ids, 8, 2), ClearanceClass.Stand, Agent(null));
        Assert.All(Enumerable.Range(0, g.CellCount), c => Assert.Equal(-1, f.LinkFrom(c)));
    }

    [Fact]
    public void LinksOfAnotherGraphAreNoLinks()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        var (other, otherIds) = Grids.Parse(Horseshoe[0], Horseshoe[1], Horseshoe[2], "...........");
        NavLinks links = NavLinks.Build(other, [new NavLink(otherIds[4, 2], otherIds[6, 2], NavLinkKind.Blast, 1f, 30f, 1f, 0)], Costs);
        Assert.Equal(-1, Field(g, Grids.At(ids, 8, 2), Agent(links)).LinkFrom(ids[4, 2]));
    }
}
