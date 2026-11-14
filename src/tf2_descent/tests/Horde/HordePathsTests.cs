using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Nav;
using Xunit;
using Xunit.Abstractions;
using static TF2.Descent.Tests.Horde.HordeNavDataTests;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's paths over the depth 1 seed 1 graph (<see cref="HordePaths"/>): around a pillar,
/// through a door square on, a goal a class cannot reach, the expansion budget, and the same
/// corners on any thread.
/// </summary>
public sealed class HordePathsTests(ITestOutputHelper output)
{
    private static HordePaths Paths()
    {
        var paths = new HordePaths();
        paths.Ready(Nav, FixtureLevel);
        return paths;
    }

    private static (PathAnswer Answer, AiVector[] Corners, NavEdgeType[] Arrive, bool Truncated) Find(HordePaths paths, AiVector from, AiVector to,
                                                                                                      ClearanceClass cls = ClearanceClass.Stand,
                                                                                                      int budget = HordeRoute.SearchBudget)
    {
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        var answer = paths.Find(from, to, cls, budget, corners, arrive, out int n, out bool truncated);
        return (answer, corners[..n], arrive[..n], truncated);
    }

    /// <summary>Two points of the pillar room (room 17, pillar_a-e-c) with its south-west pillar between them.</summary>
    private static (AiVector A, AiVector B) AcrossThePillar() =>
        (Floor(new AiVector(2320f, 8544f, 64f)), Floor(new AiVector(2560f, 8544f, 64f)));

    /// <summary>The middle of the pillar room's south half, which every class but the two largest giants' reaches.</summary>
    private static AiVector PillarRoom => Floor(new AiVector(2560f, 8300f, 64f));

    /// <summary>The alcove off the drift south of the arrival (room 12), where pack 34 to 37 stands.</summary>
    private static AiVector Alcove => Floor(new AiVector(2400f, 3600f, 64f));

    /// <summary>The far stub (room 5), the other end of the level from the pillar room.</summary>
    private static AiVector FarStub => Floor(new AiVector(7680f, 512f, 64f));

    private static AiVector Floor(AiVector p)
    {
        var c = Nav.CellAt(p with { Z = 200f }, ClearanceClass.Stand);
        Assert.True(c.IsValid, $"no cell at {p}");
        return p with { Z = Nav.Graph.Cell(c).FloorZ };
    }

    private static bool Walkable(AiVector a, AiVector b, ClearanceClass cls = ClearanceClass.Stand) =>
        Nav.Graph.WalkableLine(Nav.CellAt(a, cls).Index, a, b, cls, Nav.Open);

    /// <summary>Whether every point of the leg, each 4 units, stands over a cell of <paramref name="cls"/> (a hole in the graph, a pillar, is none).</summary>
    private static bool OverCells(AiVector a, AiVector b, ClearanceClass cls = ClearanceClass.Stand)
    {
        int n = Math.Max(1, (int)((b - a).Length2D / 4f));
        for (int i = 1; i < n; i++)
        {
            var p = a + (b - a) * ((float)i / n);
            var c = Nav.Graph.FindCell(p + new AiVector(0f, 0f, 30f));
            if (!c.IsValid || !Nav.Graph.Cell(c).Admits(cls))
                return false;
        }

        return true;
    }

    [Fact]
    public void A_path_across_the_pillar_room_goes_round_the_pillar_on_legs_the_graph_walks()
    {
        using var paths = Paths();
        var (a, b) = AcrossThePillar();
        Assert.False(OverCells(a, b), "the control: the straight line across the room stays on the graph, so there is nothing to go round");

        var (answer, corners, arrive, _) = Find(paths, a, b);

        Assert.Equal(PathAnswer.Found, answer);
        Assert.True(corners.Length >= 2, $"one leg: {corners.Length} corners");
        Assert.Equal(b, corners[^1]);
        var from = a;
        foreach (var c in corners)
        {
            Assert.True(OverCells(from, c), $"the leg {from} to {c} leaves the graph");
            from = c;
        }
    }

    [Fact]
    public void A_path_through_a_door_crosses_it_square_on()
    {
        using var paths = Paths();
        var door = FixtureLevel.Doors.First(d => (d.RoomA == 13 && d.RoomB == 12) || (d.RoomA == 12 && d.RoomB == 13));
        var a = Floor(Arrival);
        var b = Alcove;

        var (answer, corners, _, _) = Find(paths, a, b);

        Assert.Equal(PathAnswer.Found, answer);
        int i = Array.FindIndex(corners, c => (c - door.Position).Length2D is > 40f and < 56f);
        Assert.True(i >= 0 && i + 1 < corners.Length, $"no corner {HordePaths.DoorApproach} before the door at {door.Position}: {string.Join(" ", corners)}");
        var through = corners[i + 1] - corners[i];
        float square = MathF.Abs(through.X * door.Normal.X + through.Y * door.Normal.Y) / through.Length2D;
        Assert.True(square > 0.99f, $"the crossing is {MathF.Acos(square) * 180f / MathF.PI:0} degrees off the door's normal");
        Assert.True((corners[i] + through * 0.5f - door.Position).Length2D < 2f, "the crossing is not through the door's centre");
    }

    [Fact]
    public void A_giant_asked_for_a_room_its_class_cannot_reach_is_answered_unreachable()
    {
        using var paths = Paths();
        // Three doors from the pillar room: the search stops past the door into the dogleg (16), which no giant reaches.
        var from = Floor(new AiVector(2560f, 5300f, 64f));
        var goal = PillarRoom;

        Assert.Equal(PathAnswer.Found, Find(paths, from, goal, ClearanceClass.Stand).Answer);
        Assert.Equal(PathAnswer.Unreachable, Find(paths, from, goal, ClearanceClass.Hull5).Answer);
        Assert.Equal(1, paths.Unreachable);
    }

    [Fact]
    public void A_search_that_needs_more_cells_than_its_budget_is_answered_out_of_budget()
    {
        using var paths = Paths();
        var goal = FarStub;

        var full = Find(paths, Floor(Arrival), goal);
        Assert.Equal(PathAnswer.Found, full.Answer);
        output.WriteLine($"across the level: {full.Corners.Length} corners (truncated {full.Truncated}), {paths.CellsOnPaths} cells on the path, {paths.Micros:0} us");

        Assert.Equal(PathAnswer.OutOfBudget, Find(paths, Floor(Arrival), goal, budget: 200).Answer);
        Assert.Equal(1, paths.OutOfBudget);
    }

    [Fact]
    public void The_longest_trip_on_the_level_fits_the_walkers_budget()
    {
        using var paths = Paths();
        Assert.Equal(PathAnswer.Found, Find(paths, PillarRoom, FarStub).Answer);
        output.WriteLine($"pillar room to the far stub: {paths.CellsOnPaths} cells on the path, {paths.Micros:0} us");
        for (int k = 0; k < 10; k++)
            Find(paths, PillarRoom, FarStub);
        output.WriteLine($"warm: {paths.Searches} searches, {paths.Micros:0} us in all, longest {paths.MostMicros:0} us");
    }

    [Fact]
    public void A_path_is_the_same_corners_on_four_threads_at_once()
    {
        using var paths = Paths();
        var goal = FarStub;
        var expected = Find(paths, Floor(Arrival), goal).Corners;

        var results = new AiVector[4][];
        var threads = Enumerable.Range(0, 4).Select(t => new Thread(() =>
        {
            for (int k = 0; k < 5; k++)
                results[t] = Find(paths, Floor(Arrival), goal).Corners;
        })).ToArray();
        foreach (var t in threads)
            t.Start();
        foreach (var t in threads)
            t.Join();

        foreach (var r in results)
            Assert.Equal(expected, r);
    }

    [Fact]
    public void Before_the_graph_is_ready_and_after_the_level_a_search_answers_not_ready_at_epoch_0()
    {
        using var paths = new HordePaths();
        Assert.Equal(0, paths.Epoch);
        Assert.Equal(PathAnswer.NotReady, Find(paths, Floor(Arrival), Floor(Arrival) + new AiVector(100f, 0f, 0f)).Answer);

        paths.Ready(Nav, FixtureLevel);
        Assert.Equal(HordePaths.EpochOf(Nav.Identity), paths.Epoch);
        Assert.NotEqual(0, paths.Epoch);

        paths.Clear();
        Assert.Equal(0, paths.Epoch);
        Assert.Equal(PathAnswer.NotReady, Find(paths, Floor(Arrival), Floor(Arrival) + new AiVector(100f, 0f, 0f)).Answer);
    }
}
