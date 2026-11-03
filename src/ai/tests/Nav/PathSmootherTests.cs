using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>Paths are taut and smooth, never a grid zigzag (plan_hl2mp_ai §3.4.3).</summary>
public class PathSmootherTests
{
    // A corridor three cells wide and twelve long.
    private static readonly string[] Corridor = ["............", "............", "............"];

    private static (NavGraph, List<CellId>) Corridor4Connected(int[,] ids, NavGraph g, CellId from, CellId to)
    {
        var path = new List<CellId>();
        new NavPathfinder().Find(g, from, to, ClearanceClass.Stand, new NavObstacleState(0), path, 100_000, out _);
        return (g, path);
    }

    private static List<PathCorner> Smoothed(NavGraph g, List<CellId> corridor, AiVector start, AiVector end)
    {
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, corridor, start, end, corners);
        PathSmoother.Shortcut(g, ClearanceClass.Stand, new NavObstacleState(0), corners);
        var samples = new List<PathCorner>();
        PathSmoother.CatmullRom(corners, Grids.Size * 0.5f, samples);
        return samples;
    }

    [Fact]
    public void AStraightCorridorTurnsNoMoreThanFiveDegrees()
    {
        var (g, ids) = Grids.Parse(Corridor);
        // From the bottom-left cell to the top-right: a 4-connected cell path is a staircase.
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 2), Grids.At(ids, 11, 0));
        List<PathCorner> path = Smoothed(g, cells, g.Cell(Grids.At(ids, 0, 2)).Centre, g.Cell(Grids.At(ids, 11, 0)).Centre);
        Assert.InRange(Grids.MaxTurnDegrees(path), 0f, 5f);
    }

    [Fact]
    public void TheFunnelAloneBendsInThatCorridor()
    {
        // The control for the shortcut: the cheapest cell path runs along one
        // side, so the funnel through it alone turns by more than five degrees.
        var (g, ids) = Grids.Parse(Corridor);
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 2), Grids.At(ids, 11, 0));
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, cells, g.Cell(Grids.At(ids, 0, 2)).Centre, g.Cell(Grids.At(ids, 11, 0)).Centre, corners);
        Assert.True(Grids.MaxTurnDegrees(corners) > 5f, "the funnel alone keeps the corridor's bend");
    }

    [Fact]
    public void AShortcutStopsAtAWall()
    {
        var (g, ids) = Grids.Parse(
            ".   ",
            ".   ",
            ".   ",
            "....");
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 0), Grids.At(ids, 3, 3));
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, cells, g.Cell(Grids.At(ids, 0, 0)).Centre, g.Cell(Grids.At(ids, 3, 3)).Centre, corners);
        PathSmoother.Shortcut(g, ClearanceClass.Stand, new NavObstacleState(0), corners);
        Assert.Equal(3, corners.Count);
    }

    [Fact]
    public void AShortcutKeepsAJump()
    {
        var b = new NavGraphBuilder();
        CellId a0 = b.AddCell(new NavCell(0, 0, 32, 32, 0, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        CellId a1 = b.AddCell(new NavCell(32, 0, 64, 32, 0, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        CellId hi = b.AddCell(new NavCell(64, 0, 96, 32, 30, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        b.AddBoth(a0, a1, NavEdgeType.Walk);
        b.AddEdge(a1, hi, NavEdgeType.Jump, 20f);
        NavGraph g = b.Build();
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, [a0, a1, hi], g.Cell(a0).Centre, g.Cell(hi).Centre, corners);
        PathSmoother.Shortcut(g, ClearanceClass.Stand, new NavObstacleState(0), corners);
        Assert.Contains(corners, c => c.Arrive == NavEdgeType.Jump);
    }

    [Fact]
    public void TheCellCentresOfThatCorridorZigzag()
    {
        // The control: without the funnel the same corridor turns 90 degrees,
        // so the fact above is about the smoothing, not the corridor.
        var (g, ids) = Grids.Parse(Corridor);
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 2), Grids.At(ids, 11, 0));
        var centres = cells.Select(c => new PathCorner(g.Cell(c).Centre, NavEdgeType.Walk)).ToList();
        Assert.True(Grids.MaxTurnDegrees(centres) >= 45f, "the cell path is a staircase");
    }

    [Fact]
    public void AnLCorridorBendsOnceAtItsInnerCorner()
    {
        var (g, ids) = Grids.Parse(
            ".   ",
            ".   ",
            ".   ",
            "....");
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 0), Grids.At(ids, 3, 3));
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, cells, g.Cell(Grids.At(ids, 0, 0)).Centre, g.Cell(Grids.At(ids, 3, 3)).Centre, corners);
        // Start, the inner corner of the L (x = 32, y = 32), end.
        Assert.Equal((3, new AiVector(32, 32, 0)), (corners.Count, corners[1].Point));
    }

    [Fact]
    public void AStraightRunHasOnlyItsEnds()
    {
        var (g, ids) = Grids.Parse(".....");
        var (_, cells) = Corridor4Connected(ids, g, Grids.At(ids, 0, 0), Grids.At(ids, 4, 0));
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, cells, g.Cell(Grids.At(ids, 0, 0)).Centre, g.Cell(Grids.At(ids, 4, 0)).Centre, corners);
        Assert.Equal(2, corners.Count);
    }

    [Fact]
    public void AJumpIsAForcedCornerArrivedByJumping()
    {
        var b = new NavGraphBuilder();
        CellId a0 = b.AddCell(new NavCell(0, 0, 32, 32, 0, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        CellId a1 = b.AddCell(new NavCell(32, 0, 64, 32, 0, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        CellId hi = b.AddCell(new NavCell(96, 0, 128, 32, 30, ClearanceClasses.Stand | ClearanceClasses.Crouch, 0, 1f));
        b.AddBoth(a0, a1, NavEdgeType.Walk);
        b.AddEdge(a1, hi, NavEdgeType.Jump, 20f);
        NavGraph g = b.Build();
        var corners = new List<PathCorner>();
        PathSmoother.Funnel(g, [a0, a1, hi], g.Cell(a0).Centre, g.Cell(hi).Centre, corners);
        Assert.Contains(corners, c => c.Arrive == NavEdgeType.Jump && c.Point == g.Cell(hi).Centre);
    }

    [Fact]
    public void SamplesAreAtMostHalfACellApart()
    {
        var corners = new List<PathCorner> { new(new AiVector(0, 0, 0), 0), new(new AiVector(100, 0, 0), 0), new(new AiVector(100, 100, 0), 0) };
        var samples = new List<PathCorner>();
        PathSmoother.CatmullRom(corners, 16f, samples);
        Assert.All(Enumerable.Range(1, samples.Count - 1),
                   i => Assert.True(samples[i].Point.DistanceTo(samples[i - 1].Point) <= 16f * 1.2f, $"sample {i}"));
    }

    [Fact]
    public void TheSplinePassesThroughTheCorners()
    {
        var corners = new List<PathCorner> { new(new AiVector(0, 0, 0), 0), new(new AiVector(100, 0, 0), 0), new(new AiVector(100, 100, 0), 0) };
        var samples = new List<PathCorner>();
        PathSmoother.CatmullRom(corners, 16f, samples);
        Assert.Contains(samples, s => s.Point == new AiVector(100, 0, 0));
    }
}
