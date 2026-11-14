using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The ways into a goal's room (lane G4b, <see cref="RouteFamilies"/>) and the seeded Thompson sampling over what came of each
/// (<see cref="OutcomeSampler"/>). The graph: a room of 5 by 5 cells of 32 u round the goal, with a corridor 3 cells wide
/// and 10 long leaving it west and another east; the room reaches 4 steps (128 u) from the goal.
/// </summary>
public sealed class RouteFamiliesTests
{
    private const float Room = 4 * Grids.Size;

    private static (NavGraph Graph, int Goal) Level(bool east = true)
    {
        string wall = new string(' ', 10), corridor = new string('.', 10);
        string[] rows =
        [
            wall + "....." + wall,
            corridor + "....." + (east ? corridor : wall),
            corridor + "....." + (east ? corridor : wall),
            corridor + "....." + (east ? corridor : wall),
            wall + "....." + wall,
        ];
        (NavGraph graph, int[,] ids) = Grids.Parse(rows);
        return (graph, ids[12, 2]);
    }

    [Fact]
    public void TwoCorridorsIntoTheRoomAreTwoDoors()
    {
        (NavGraph graph, int goal) = Level();
        RouteFamilies f = RouteFamilies.Around(graph, goal, Room);
        Assert.Equal(2, f.Doors.Count);
        float goalX = graph.Cell(goal).Centre.X;
        Assert.Single(f.Doors, d => d.At.X < goalX - 3 * Grids.Size);
        Assert.Single(f.Doors, d => d.At.X > goalX + 3 * Grids.Size);
        Assert.All(f.Doors, d => Assert.Equal(d, f.Doors[f.IndexOf(d.Bin)]));
    }

    [Fact]
    public void ARoomWithOneWayInHasOneDoor()
    {
        (NavGraph graph, int goal) = Level(east: false);
        Assert.Single(RouteFamilies.Around(graph, goal, Room).Doors);
    }

    [Fact]
    public void ABodyAtADoorIsPassingItAndOneAtTheGoalIsNot()
    {
        (NavGraph graph, int goal) = Level();
        RouteFamilies f = RouteFamilies.Around(graph, goal, Room);
        foreach (RouteDoor d in f.Doors)
            Assert.Equal(d.Index, f.Passing(d.At + new AiVector(0f, 20f, 0f)));
        Assert.Equal(-1, f.Passing(graph.Cell(goal).Centre));
    }

    [Fact]
    public void OnlyTheRoomIsSearched()
    {
        (NavGraph graph, int goal) = Level();
        RouteFamilies f = RouteFamilies.Around(graph, goal, Room);
        Assert.InRange(f.RoomCells, 25, 40);
        Assert.True(f.RoomCells < graph.CellCount / 2);
    }

    [Fact]
    public void NoGoalCellHasNoDoor() => Assert.Empty(RouteFamilies.Around(Level().Graph, -1, Room).Doors);

    /// <summary>Of a choice tried five times for nothing and one tried five times that worked four, the first is seldom taken.</summary>
    [Fact]
    public void AChoiceThatFailedFiveTimesIsTakenLessThanOneThatWorked()
    {
        var random = new AiRandom(11);
        OutcomeStat[] stats = [new(5d, 0d), new(5d, 4d)];
        int failed = Enumerable.Range(0, 1000).Count(_ => OutcomeSampler.Choose(stats, random) == 0);
        Assert.InRange(failed, 1, 100);
        // A choice never tried keeps an even prior: it is taken over the one that failed most of the time.
        OutcomeStat[] untried = [new(5d, 0d), new(0d, 0d)];
        int fresh = Enumerable.Range(0, 1000).Count(_ => OutcomeSampler.Choose(untried, random) == 1);
        Assert.InRange(fresh, 750, 1000);
    }

    [Fact]
    public void TheSameStreamAndCountsTakeTheSameChoices()
    {
        OutcomeStat[] stats = [new(3d, 1d), new(4d, 2d), new(1d, 0.5d)];
        int[] Run(ulong seed)
        {
            var random = new AiRandom(seed);
            return [.. Enumerable.Range(0, 50).Select(_ => OutcomeSampler.Choose(stats, random))];
        }

        Assert.Equal(Run(4), Run(4));
        Assert.NotEqual(Run(4), Run(5));
    }

    /// <summary>The draws are Beta's: the mean of Beta(3, 7) is 0.3.</summary>
    [Fact]
    public void TheDrawsHaveBetasMean()
    {
        var random = new AiRandom(2);
        double mean = Enumerable.Range(0, 4000).Average(_ => OutcomeSampler.Beta(random, 3d, 7d));
        Assert.InRange(mean, 0.28d, 0.32d);
    }
}
