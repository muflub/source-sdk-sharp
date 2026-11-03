using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

public class PathfinderTests
{
    private static readonly string[] Maze =
    [
        "..........",
        ".  .    ..",
        ". .. ... .",
        ".  .   . .",
        "...... ...",
    ];

    [Fact]
    public void AStarFindsWhatTheFieldCosts()
    {
        var (g, ids) = Grids.Parse(Maze);
        var obstacles = new NavObstacleState(0);
        var field = new FlowField();
        field.Build(g, Grids.At(ids, 9, 4), ClearanceClass.Stand, obstacles);
        var path = new List<CellId>();
        new NavPathfinder().Find(g, Grids.At(ids, 0, 4), Grids.At(ids, 9, 4), ClearanceClass.Stand, obstacles, path, 10_000, out float cost);
        Assert.Equal(field.CostAt(ids[0, 4]), cost, 3);
    }

    [Fact]
    public void APathRunsFromStartToGoal()
    {
        var (g, ids) = Grids.Parse(Maze);
        var path = new List<CellId>();
        new NavPathfinder().Find(g, Grids.At(ids, 0, 4), Grids.At(ids, 9, 4), ClearanceClass.Stand, new NavObstacleState(0), path, 10_000, out _);
        Assert.Equal((ids[0, 4], ids[9, 4]), (path[0].Index, path[^1].Index));
    }

    [Fact]
    public void EachStepOfAPathIsAnEdge()
    {
        var (g, ids) = Grids.Parse(Maze);
        var path = new List<CellId>();
        new NavPathfinder().Find(g, Grids.At(ids, 0, 4), Grids.At(ids, 9, 4), ClearanceClass.Stand, new NavObstacleState(0), path, 10_000, out _);
        Assert.All(Enumerable.Range(0, path.Count - 1), i => Assert.True(g.TryEdge(path[i].Index, path[i + 1].Index, out _), $"step {i}"));
    }

    [Fact]
    public void AnUnconnectedGoalIsUnreachable()
    {
        var (g, ids) = Grids.Parse(".. .");
        Assert.Equal(PathStatus.Unreachable, new NavPathfinder().Find(g, Grids.At(ids, 0, 0), Grids.At(ids, 3, 0), ClearanceClass.Stand,
                                                                       new NavObstacleState(0), new List<CellId>(), 1000, out _));
    }

    [Fact]
    public void ASmallBudgetRunsOut()
    {
        var (g, ids) = Grids.Parse(Maze);
        Assert.Equal(PathStatus.OutOfBudget, new NavPathfinder().Find(g, Grids.At(ids, 0, 4), Grids.At(ids, 9, 4), ClearanceClass.Stand,
                                                                       new NavObstacleState(0), new List<CellId>(), 3, out _));
    }

    [Fact]
    public void AStandPathAvoidsACrouchOnlyCell()
    {
        var (g, ids) = Grids.Parse(".......", ".     .", "..ccc..");
        var path = new List<CellId>();
        new NavPathfinder().Find(g, Grids.At(ids, 0, 2), Grids.At(ids, 6, 2), ClearanceClass.Stand, new NavObstacleState(0), path, 10_000, out _);
        Assert.DoesNotContain(path, c => g.Cell(c).CrouchOnly);
    }
}
