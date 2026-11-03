using Xunit;

namespace SourceSharp.Ai.Tests.Nav;

/// <summary>The flow-field facts of plan_hl2mp_ai §9 Phase 2 step 1, on hand-built graphs.</summary>
public class FlowFieldTests
{
    // A crawlspace (c) is the short way from the left to the right; the long
    // way round goes over the top.
    private static readonly string[] Crawl =
    [
        ".......",
        ".     .",
        "..ccc..",
    ];

    private static FlowField Field(NavGraph g, CellId goal, ClearanceClass cls, NavObstacleState? obstacles = null)
    {
        var f = new FlowField();
        f.Build(g, goal, cls, obstacles ?? new NavObstacleState(g.ObstacleCount));
        return f;
    }

    [Fact]
    public void AStandFieldNeverRoutesThroughACrouchOnlyCell()
    {
        var (g, ids) = Grids.Parse(Crawl);
        FlowField f = Field(g, Grids.At(ids, 6, 2), ClearanceClass.Stand);
        Assert.DoesNotContain(Grids.Walk(f, ids[0, 2]), c => g.Cell(c).CrouchOnly);
    }

    [Fact]
    public void ACrouchFieldTakesTheCrawlspace()
    {
        var (g, ids) = Grids.Parse(Crawl);
        FlowField f = Field(g, Grids.At(ids, 6, 2), ClearanceClass.Crouch);
        Assert.Contains(Grids.Walk(f, ids[0, 2]), c => g.Cell(c).CrouchOnly);
    }

    [Fact]
    public void CostsStrictlyDecreaseTowardsTheObjective()
    {
        var (g, ids) = Grids.Parse(".....", ". . .", ".....", "w.w..");
        FlowField f = Field(g, Grids.At(ids, 4, 0), ClearanceClass.Stand);
        for (int c = 0; c < g.CellCount; c++)
        {
            CellId next = f.NextFrom(c);
            if (next.IsValid)
                Assert.True(f.CostAt(next.Index) < f.CostAt(c), $"cell {c} -> {next.Index} goes down");
        }
    }

    [Fact]
    public void TheObjectiveCostsNothing()
    {
        var (g, ids) = Grids.Parse("...");
        Assert.Equal(0f, Field(g, Grids.At(ids, 2, 0), ClearanceClass.Stand).CostAt(ids[2, 0]));
    }

    [Fact]
    public void AnUnconnectedCellIsUnreachable()
    {
        var (g, ids) = Grids.Parse(".. .");
        FlowField f = Field(g, Grids.At(ids, 0, 0), ClearanceClass.Stand);
        Assert.Equal((float.PositiveInfinity, CellId.None, 2), (f.CostAt(ids[3, 0]), f.NextFrom(ids[3, 0]), f.Reachable));
    }

    // Two ways from the left to the right: through the door (short) or round (long).
    private static readonly string[] Doors =
    [
        ".......",
        ".     .",
        "...D...",
    ];

    [Fact]
    public void ADoorCosts()
    {
        var (open, ids) = Grids.Parse(0f, Doors);
        var (waiting, _) = Grids.Parse(50f, Doors);
        float free = Field(open, Grids.At(ids, 6, 2), ClearanceClass.Stand).CostAt(ids[0, 2]);
        float paid = Field(waiting, Grids.At(ids, 6, 2), ClearanceClass.Stand).CostAt(ids[0, 2]);
        Assert.Equal(free + 100f, paid);
    }

    [Fact]
    public void AnOpenDoorIsTheShortWay()
    {
        var (g, ids) = Grids.Parse(10f, Doors);
        Assert.Contains(ids[3, 2], Grids.Walk(Field(g, Grids.At(ids, 6, 2), ClearanceClass.Stand), ids[0, 2]));
    }

    [Fact]
    public void AShutDoorIsNotPassed()
    {
        var (g, ids) = Grids.Parse(10f, Doors);
        var shut = new NavObstacleState(g.ObstacleCount);
        shut.Set(0, true);
        Assert.DoesNotContain(ids[3, 2], Grids.Walk(Field(g, Grids.At(ids, 6, 2), ClearanceClass.Stand, shut), ids[0, 2]));
    }

    [Fact]
    public void AnExpensiveDoorIsWalkedRound()
    {
        var (g, ids) = Grids.Parse(1000f, Doors);
        Assert.DoesNotContain(ids[3, 2], Grids.Walk(Field(g, Grids.At(ids, 6, 2), ClearanceClass.Stand), ids[0, 2]));
    }

    [Fact]
    public void RebuildingForAnotherObjectiveForgetsTheFirst()
    {
        var (g, ids) = Grids.Parse(".....");
        var f = new FlowField();
        f.Build(g, Grids.At(ids, 0, 0), ClearanceClass.Stand, new NavObstacleState(0));
        f.Build(g, Grids.At(ids, 4, 0), ClearanceClass.Stand, new NavObstacleState(0));
        Assert.Equal((0f, Grids.Size * 4), (f.CostAt(ids[4, 0]), f.CostAt(ids[0, 0])));
    }
}
