using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

public class NavGraphTests
{
    [Fact]
    public void EdgesAreListedFromTheirCell()
    {
        var (g, ids) = Grids.Parse("...");
        Assert.Equal([ids[0, 0], ids[2, 0]], g.EdgesFrom(ids[1, 0]).ToArray().Select(e => e.To).Order());
    }

    [Fact]
    public void EdgesAreListedIntoTheirCell()
    {
        var (g, ids) = Grids.Parse("...");
        Assert.Equal([ids[0, 0], ids[2, 0]], g.EdgesTo(ids[1, 0]).ToArray().Select(e => e.From).Order());
    }

    [Fact]
    public void AnEdgeCostsAtLeastItsLength()
    {
        var (g, ids) = Grids.Parse("..");
        g.TryEdge(ids[0, 0], ids[1, 0], out NavEdge e);
        Assert.Equal(Grids.Size, e.Cost);
    }

    [Fact]
    public void WaterCostsDouble()
    {
        var (g, ids) = Grids.Parse(".ww");
        g.TryEdge(ids[1, 0], ids[2, 0], out NavEdge e);
        Assert.Equal(Grids.Size * 2f, e.Cost);
    }

    [Fact]
    public void ACellIsFoundByAPointAboveItsFloor()
    {
        var (g, ids) = Grids.Parse("...");
        Assert.Equal(new CellId(ids[1, 0]), g.FindCell(new AiVector(40, 10, 5)));
    }

    [Fact]
    public void APointOutsideEveryCellFindsNone()
        => Assert.Equal(CellId.None, Grids.Parse("...").Graph.FindCell(new AiVector(500, 10, 5)));

    [Fact]
    public void TheHashIsStable()
        => Assert.Equal(Grids.Parse("..", "..").Graph.Hash, Grids.Parse("..", "..").Graph.Hash);

    [Fact]
    public void TheHashSeesAMissingCell()
        => Assert.NotEqual(Grids.Parse("..", "..").Graph.Hash, Grids.Parse("..", ". ").Graph.Hash);

    [Fact]
    public void ACellWithNoFootprintIsRefused()
        => Assert.Throws<ArgumentException>(() => new NavGraphBuilder().AddCell(new NavCell(0, 0, 0, 10, 0, ClearanceClasses.Stand, 0, 1f)));

    [Fact]
    public void ACostMultiplierBelowOneIsRefused()
        => Assert.Throws<ArgumentException>(() => new NavGraphBuilder().AddCell(new NavCell(0, 0, 10, 10, 0, ClearanceClasses.Stand, 0, 0.5f)));

    /// <summary>Round 2's S14: a cell beside the start with no way to it is not within the walk, however near it is.</summary>
    [Fact]
    public void ACellWithNoWayToItIsNotWithinTheWalk()
    {
        var (g, ids) = Grids.Parse("... .");
        var into = new List<int>();
        g.CellsWithin(Grids.At(ids, 2, 0), 1000f, ClearanceClass.Stand, new NavObstacleState(g.ObstacleCount), into);
        Assert.Equal([ids[0, 0], ids[1, 0], ids[2, 0]], into.Order());
    }

    /// <summary>Round 2's S14: the walk's reach is the way's cost, not the distance: a cell 64 u off past a wall is a walk of 160 u round it.</summary>
    [Fact]
    public void TheWalksReachIsTheWayRoundNotTheDistance()
    {
        var (g, ids) = Grids.Parse("...", ". .", ". .");
        var into = new List<int>();
        var open = new NavObstacleState(g.ObstacleCount);
        g.CellsWithin(Grids.At(ids, 0, 2), 128f, ClearanceClass.Stand, open, into);
        Assert.DoesNotContain(ids[2, 2], into);
        Assert.Contains(ids[2, 0], into);
        g.CellsWithin(Grids.At(ids, 0, 2), 192f, ClearanceClass.Stand, open, into);
        Assert.Contains(ids[2, 2], into);
    }
}
