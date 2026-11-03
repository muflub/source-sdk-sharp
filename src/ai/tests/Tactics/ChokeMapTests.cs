using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// The map's choke points (plan_hl2mp_ai §5.5): route betweenness over the graph. Two
/// rooms joined by one bridge have that bridge at the top; a fixture with no bridge
/// shows the score is the shape's, not something every graph answers.
/// </summary>
public sealed class ChokeMapTests
{
    private static readonly string[] Bridged =
    [
        "........   ........",
        "........   ........",
        "........   ........",
        "...................",
        "........   ........",
        "........   ........",
        "........   ........",
    ];

    private const int BridgeX = 9, BridgeY = 3;

    [Fact]
    public void TheTopCellIsTheBridge()
    {
        var (g, ids) = Grids.Parse(Bridged);
        ChokeMap map = ChokeMap.Compute(g);
        AiVector bridge = g.Cell(Grids.At(ids, BridgeX, BridgeY)).Centre;
        Assert.True(map.Top[0].Position.DistanceTo(bridge) <= Grids.Size * 1.5f, $"top {map.Top[0].Position}, bridge {bridge}");
        Assert.Equal(1f, map.Top[0].Score);
    }

    /// <summary>The bridge takes more of the routes than any cell of either room.</summary>
    [Fact]
    public void ABridgeOutscoresTheRooms()
    {
        var (g, ids) = Grids.Parse(Bridged);
        ChokeMap map = ChokeMap.Compute(g);
        float bridge = map.ScoreOf(Grids.At(ids, BridgeX, BridgeY).Index);
        for (int y = 0; y < Bridged.Length; y++)
        {
            for (int x = 0; x < 6; x++)
                Assert.True(map.ScoreOf(Grids.At(ids, x, y).Index) < bridge, $"({x},{y}) {map.ScoreOf(Grids.At(ids, x, y).Index)} against {bridge}");
        }
    }

    /// <summary>The control: the same floor with no walls has no cell near as far above the rest as the bridge is.</summary>
    [Fact]
    public void AnOpenFloorHasNoSuchPeak()
    {
        var (bridged, ids) = Grids.Parse(Bridged);
        var (open, openIds) = Grids.Parse([.. Enumerable.Repeat(new string('.', 19), 7)]);
        static float RoomMean(ChokeMap m, int[,] ids)
        {
            float sum = 0f;
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 3; x++)
                    sum += m.ScoreOf(Grids.At(ids, x, y).Index);
            }

            return sum / 21f;
        }

        Assert.True(RoomMean(ChokeMap.Compute(open), openIds) > 2f * RoomMean(ChokeMap.Compute(bridged), ids));
    }

    /// <summary>Two bridges share what one carried, and the wider way gets no more per cell than the narrow one would alone.</summary>
    [Fact]
    public void TwoSidesOfACorridorAreOneChoice()
    {
        var (g, _) = Grids.Parse(Bridged);
        ChokeMap map = ChokeMap.Compute(g);
        foreach (ChokePoint a in map.Top)
        {
            foreach (ChokePoint b in map.Top)
                Assert.True(a.Cell == b.Cell || a.Position.DistanceTo(b.Position) >= ChokeMap.TopSeparation);
        }
    }

    [Fact]
    public void TheAnswerIsTheSameOnAnyThreads()
    {
        var (g, _) = Grids.Parse(Bridged);
        ChokeMap one = ChokeMap.Compute(g, parallel: 1), four = ChokeMap.Compute(g, parallel: 4);
        Assert.Equal(one.Scores.ToArray(), four.Scores.ToArray());
        Assert.Equal(one.Top.ToArray(), four.Top.ToArray());
    }

    /// <summary>
    /// A wide room's routes converge before its door, and by the count alone that floor
    /// was the top of dm_lockdown. As many routes through a doorway as across the floor:
    /// the doorway is the choke.
    /// </summary>
    [Fact]
    public void WithEqualRoutesTheNarrowPlaceIsTheChoke()
    {
        var (g, ids) = Grids.Parse(Bridged);
        var flow = new double[g.CellCount];
        Array.Fill(flow, 1d);
        ChokeMap map = ChokeMap.FromFlow(g, flow);
        float door = map.ScoreOf(Grids.At(ids, BridgeX, BridgeY).Index), floor = map.ScoreOf(Grids.At(ids, 3, 3).Index);
        Assert.True(door > floor * 1.2f, $"the doorway {door}, the room's middle {floor}");
    }

    /// <summary>Clearance is the walk to a wall: nothing in a corridor, a room's middle more, and every cell has one.</summary>
    [Fact]
    public void ClearanceIsTheWalkToAWall()
    {
        var (g, ids) = Grids.Parse(Bridged);
        float[] c = ChokeMap.Clearance(g);
        Assert.Equal(0f, c[Grids.At(ids, BridgeX, BridgeY).Index]);
        Assert.True(c[Grids.At(ids, 3, 3).Index] >= 2f * Grids.Size, $"{c[Grids.At(ids, 3, 3).Index]}");
        Assert.All(c, v => Assert.True(float.IsFinite(v)));
    }

    /// <summary>A ladder's top: the only way up is a Ladder edge, and all the routes up go through its ends.</summary>
    [Fact]
    public void ALadderIsAChoke()
    {
        var b = new NavGraphBuilder();
        var low = new CellId[10];
        var high = new CellId[10];
        for (int i = 0; i < 10; i++)
        {
            low[i] = b.AddCell(new NavCell(i * 32f, 0f, (i + 1) * 32f, 32f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
            high[i] = b.AddCell(new NavCell(i * 32f, 0f, (i + 1) * 32f, 32f, 256f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
            if (i > 0)
            {
                b.AddBoth(low[i - 1], low[i], NavEdgeType.Walk);
                b.AddBoth(high[i - 1], high[i], NavEdgeType.Walk);
            }
        }

        b.AddBoth(low[9], high[9], NavEdgeType.Ladder);
        NavGraph g = b.Build();
        ChokeMap map = ChokeMap.Compute(g);
        Assert.True(map.ScoreOf(high[9].Index) > map.ScoreOf(high[0].Index));
        Assert.True(map.Top[0].Position.X > 8 * 32f, $"top at {map.Top[0].Position}");
    }

    /// <summary>The routes are between the places given: a room nobody goes to counts for nothing, however big, and with none given the map's cells stand in.</summary>
    [Fact]
    public void RoutesRunBetweenThePlacesGiven()
    {
        var (g, ids) = Grids.Parse(Bridged);
        AiVector a = g.Cell(Grids.At(ids, 15, 1)).Centre, b = g.Cell(Grids.At(ids, 15, 5)).Centre;
        // Two places in one room: nothing crosses the bridge.
        ChokeMap oneRoom = ChokeMap.Compute(g, [a, b]);
        Assert.Equal(0f, oneRoom.ScoreOf(Grids.At(ids, BridgeX, BridgeY).Index));
        // One in each: everything does.
        ChokeMap across = ChokeMap.Compute(g, [a, g.Cell(Grids.At(ids, 2, 1)).Centre]);
        Assert.True(across.ScoreOf(Grids.At(ids, BridgeX, BridgeY).Index) > 0.5f);
    }

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "ss-chokes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>The cache gives back what was written, top list included; its name carries the graph's name and the map's revision.</summary>
    [Fact]
    public void TheCacheRoundTrips()
    {
        var (g, _) = Grids.Parse(Bridged);
        ChokeMap map = ChokeMap.Compute(g);
        string dir = TempDir();
        try
        {
            var cache = new NavGraphCache(dir, NavEdgeCosts.Default);
            Assert.Null(cache.TryReadChokes("m", new string('a', 64), new string('b', 64), g));
            cache.WriteChokes("m", new string('a', 64), new string('b', 64), g, map);
            ChokeMap back = cache.TryReadChokes("m", new string('a', 64), new string('b', 64), g)!;
            Assert.Equal(map.Scores.ToArray(), back.Scores.ToArray());
            Assert.Equal(map.Top.ToArray(), back.Top.ToArray());
            Assert.EndsWith($".chokes{ChokeMap.Revision}", cache.ChokesPathFor("m", new string('a', 64), new string('b', 64)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A cached map of another graph is no answer: the graph it was made of is in its header.</summary>
    [Fact]
    public void AnotherGraphsCacheIsNotRead()
    {
        var (g, _) = Grids.Parse(Bridged);
        // As many cells, in other places: the name and the size agree, only the hash can tell.
        string[] other = [.. Bridged];
        other[0] = ".......   .........";
        var (h, _) = Grids.Parse(other);
        string dir = TempDir();
        try
        {
            var cache = new NavGraphCache(dir, NavEdgeCosts.Default);
            cache.WriteChokes("m", new string('a', 64), new string('b', 64), g, ChokeMap.Compute(g));
            Assert.Null(cache.TryReadChokes("m", new string('a', 64), new string('b', 64), h));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
