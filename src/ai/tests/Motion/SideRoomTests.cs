using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// <see cref="SideRoom"/>: what lies a step along a way, on the graph. A corridor of four cells along
/// x (y 0 to 64), a stair cell 16 up north of the second, a pit 64 down north of the third joined by a
/// drop, and a cell north of the fourth behind a door.
/// </summary>
public sealed class SideRoomTests
{
    private static readonly AiVector North = new(0f, 1f, 0f), South = new(0f, -1f, 0f), East = new(1f, 0f, 0f);

    private static (NavGraph Graph, int Door) Corridor()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[4];
        for (int x = 0; x < 4; x++)
            ids[x] = b.AddCell(new NavCell(x * 64f, 0f, (x + 1) * 64f, 64f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        for (int x = 0; x + 1 < 4; x++)
            b.AddBoth(ids[x], ids[x + 1], NavEdgeType.Walk);
        CellId stair = b.AddCell(new NavCell(64f, 64f, 128f, 128f, 16f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        b.AddBoth(ids[1], stair, NavEdgeType.Walk);
        CellId pit = b.AddCell(new NavCell(128f, 64f, 192f, 128f, -64f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        b.AddEdge(ids[2], pit, NavEdgeType.Drop);
        CellId room = b.AddCell(new NavCell(192f, 64f, 256f, 128f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        int door = b.AddObstacle();
        b.AddBoth(ids[3], room, NavEdgeType.Door, 0f, door);
        return (b.Build(), door);
    }

    private static SideFloor At(float x, AiVector way, NavObstacleState? obstacles = null)
    {
        (NavGraph graph, int _) = Corridor();
        return SideRoom.Ahead(graph, obstacles ?? new NavObstacleState(graph.ObstacleCount), new AiVector(x, 40f, 0f), way, ClearanceClass.Crouch);
    }

    [Fact]
    public void AlongTheCorridorIsOpen() => Assert.Equal(SideFloor.Open, At(40f, East));

    [Fact]
    public void OutOfTheCorridorWithNoCellIsAWall() => Assert.Equal(SideFloor.Wall, At(32f, North));

    [Fact]
    public void AStairAStepUpIsOpen() => Assert.Equal(SideFloor.Open, At(96f, North));

    [Fact]
    public void APitBelowAStepIsADrop() => Assert.Equal(SideFloor.Drop, At(160f, North));

    [Fact]
    public void ADoorOpenIsOpenAndShutIsAWall()
    {
        (NavGraph graph, int door) = Corridor();
        var shut = new NavObstacleState(graph.ObstacleCount);
        shut.Set(door, true);
        Assert.Equal(SideFloor.Open, At(224f, North));
        Assert.Equal(SideFloor.Wall, At(224f, North, shut));
    }

    [Fact]
    public void ABodyOnNoCellIsOffTheGraph()
    {
        (NavGraph graph, int _) = Corridor();
        Assert.Equal(SideFloor.OffGraph, SideRoom.Ahead(graph, new NavObstacleState(graph.ObstacleCount), new AiVector(32f, 400f, 0f), South, ClearanceClass.Crouch));
    }

    /// <summary>A step is all it looks: a wall two steps off is not in its way.</summary>
    [Fact]
    public void AWallBeyondTheStepIsNotInTheWay()
    {
        (NavGraph graph, int _) = Corridor();
        var open = new NavObstacleState(graph.ObstacleCount);
        Assert.Equal(SideFloor.Open, SideRoom.Ahead(graph, open, new AiVector(32f, 8f, 0f), North, ClearanceClass.Crouch));
        Assert.Equal(SideFloor.Wall, SideRoom.Ahead(graph, open, new AiVector(32f, 8f, 0f), North, ClearanceClass.Crouch, 64f));
    }
}
