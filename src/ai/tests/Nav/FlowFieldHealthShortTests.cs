using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>
/// What a field says of the links it left out for the agent's health (lane G7a): the least such link's health, so a
/// follower with no route can say what the bot needs, and build again once a cabinet gives it that. Saying it changes
/// no step: the field is the one it was, cell for cell.
/// </summary>
public class FlowFieldHealthShortTests
{
    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>Two islands of a row; only links join them: (4, 0) to (6, 0), 30 health, and a dearer one (3, 0) to (7, 0), 40.</summary>
    private static readonly string[] Islands = ["..... ....."];

    private static NavLink[] Jumps(int[,] ids) =>
    [
        new(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0),
        new(ids[3, 0], ids[7, 0], NavLinkKind.Blast, 1f, 40f, 1f, 1),
    ];

    private static NavObstacleState Agent(NavLinks? links, float health, JumpReach reach = JumpReach.Blast) => new(0) { Links = links, Reach = reach, LinkHealth = health };

    private static FlowField Field(NavGraph g, CellId goal, NavObstacleState agent, IReadOnlySet<int>? avoidedLinks = null)
    {
        var f = new FlowField();
        f.Build(g, goal, ClearanceClass.Stand, agent, avoidedLinks: avoidedLinks);
        return f;
    }

    [Fact]
    public void AFieldShortOfEveryLinksHealthHasNoRouteAndSaysTheLeastItLeftOut()
    {
        var (g, ids) = Grids.Parse(Islands);
        NavLinks links = NavLinks.Build(g, Jumps(ids), Costs);
        FlowField f = Field(g, Grids.At(ids, 10, 0), Agent(links, health: 20f));
        Assert.False(float.IsFinite(f.CostAt(ids[0, 0])));
        Assert.Equal(30f, f.HealthShort);
    }

    [Fact]
    public void WithTheHealthForTheCheaperLinkTheFieldTakesItAndSaysOnlyTheDearerOneIsShort()
    {
        var (g, ids) = Grids.Parse(Islands);
        NavLinks links = NavLinks.Build(g, Jumps(ids), Costs);
        FlowField f = Field(g, Grids.At(ids, 10, 0), Agent(links, health: 35f));
        Assert.True(float.IsFinite(f.CostAt(ids[0, 0])));
        Assert.Equal(0, f.LinkFrom(ids[4, 0]));
        Assert.Equal(40f, f.HealthShort);
        Assert.Equal(float.PositiveInfinity, Field(g, Grids.At(ids, 10, 0), Agent(links, health: 100f)).HealthShort);
    }

    [Fact]
    public void ALinkLeftOutForAnythingButTheHealthIsNotSaid()
    {
        var (g, ids) = Grids.Parse(Islands);
        NavLinks links = NavLinks.Build(g, Jumps(ids), Costs);
        // No reach; both avoided; both shut: none is short of health, whatever the health.
        Assert.Equal(float.PositiveInfinity, Field(g, Grids.At(ids, 10, 0), Agent(links, 0f, JumpReach.None)).HealthShort);
        Assert.Equal(float.PositiveInfinity, Field(g, Grids.At(ids, 10, 0), Agent(links, 0f), new HashSet<int> { 0, 1 }).HealthShort);
        var shut = NavLinkMask.Of(links, [0, 1]);
        NavObstacleState shutOut = Agent(links, 0f);
        shutOut.LinksShut = shut;
        Assert.Equal(float.PositiveInfinity, Field(g, Grids.At(ids, 10, 0), shutOut).HealthShort);
        // The control: the same agent with nothing else in the way says the cheaper one.
        Assert.Equal(30f, Field(g, Grids.At(ids, 10, 0), Agent(links, 0f)).HealthShort);
    }

    [Fact]
    public void ALinkFromCellsTheObjectiveIsNotReachedFromIsNotSaid()
    {
        // The links lead off the objective's island: no way to the objective goes over them.
        var (g, ids) = Grids.Parse(Islands);
        NavLinks links = NavLinks.Build(g, Jumps(ids), Costs);
        Assert.Equal(float.PositiveInfinity, Field(g, Grids.At(ids, 0, 0), Agent(links, health: 0f)).HealthShort);
    }

    [Fact]
    public void SayingItChangesNoStepTheFieldIsTheOneWithNoLinksCellForCell()
    {
        var (g, ids) = Grids.Parse(Islands);
        NavLinks links = NavLinks.Build(g, Jumps(ids), Costs);
        FlowField with = Field(g, Grids.At(ids, 10, 0), Agent(links, health: 20f));
        FlowField without = Field(g, Grids.At(ids, 10, 0), Agent(null, health: 20f));
        Assert.Equal(30f, with.HealthShort);
        for (int c = 0; c < g.CellCount; c++)
        {
            Assert.Equal(without.CostAt(c), with.CostAt(c));
            Assert.Equal(without.NextFrom(c), with.NextFrom(c));
            Assert.Equal(without.ViaFrom(c), with.ViaFrom(c));
            Assert.Equal(-1, with.LinkFrom(c));
        }
    }
}
