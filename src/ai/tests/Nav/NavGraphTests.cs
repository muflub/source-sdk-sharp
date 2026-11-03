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
}
