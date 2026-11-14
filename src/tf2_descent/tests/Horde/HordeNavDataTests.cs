using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Level;
using Xunit;
using Xunit.Abstractions;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The level's navigation as the horde derives it (plan_descent_logic 3.7): the committed depth 1
/// seed 1 <c>.nav3d</c> becomes a graph for the robots' numbers with a class per robot scale band,
/// and its voxel columns; which rooms each class reaches from the arrival.
/// </summary>
public sealed class HordeNavDataTests(ITestOutputHelper output)
{
    private static readonly Lazy<(HordeNavData Nav, HordeLevel Level, LevelData Data, byte[] Bytes)> Fixture = new(() =>
    {
        var files = LevelFixture.Committed();
        LevelData data = files.Read();
        return (HordeNavData.Derive(files.Nav3d), LevelAdapters.ToHorde(data), data, files.Nav3d);
    });

    internal static HordeNavData Nav => Fixture.Value.Nav;

    internal static HordeLevel FixtureLevel => Fixture.Value.Level;

    internal static LevelData Data => Fixture.Value.Data;

    internal static byte[] Bytes => Fixture.Value.Bytes;

    internal static AiVector Arrival => new(Data.Arrivals[0].Position.X, Data.Arrivals[0].Position.Y, Data.Arrivals[0].Position.Z);

    [Fact]
    public void The_standing_class_reaches_every_room_of_depth_1_seed_1_from_the_arrival()
    {
        var nav = Nav;
        var from = nav.CellAt(Arrival, ClearanceClass.Stand);
        Assert.True(from.IsValid, "the arrival point is on no cell");
        output.WriteLine($"cells {nav.Graph.CellCount}, edges {nav.Graph.EdgeCount}, obstacles {nav.Graph.ObstacleCount}, derive {nav.DeriveMs:0} ms, columns {nav.ColumnsMs:0} ms");

        Assert.Equal(FixtureLevel.RoomCount, RoomsReached(nav, FixtureLevel, from, ClearanceClass.Stand).Count);
    }

    [Fact]
    public void The_largest_ordinary_robot_and_the_smaller_giants_reach_every_room()
    {
        foreach (var cls in new[] { ClearanceClass.Hull2, ClearanceClass.Hull3 })
            Assert.Equal(FixtureLevel.RoomCount, RoomsReached(Nav, FixtureLevel, Nav.CellAt(Arrival, cls), cls).Count);
    }

    /// <summary>
    /// The stairs limit (docs/todo.md, "a hull wider than the game's cannot climb stairs"): a hull
    /// is raised one step only, so the two largest giants' classes stop at a stair two steps
    /// tall for them. On depth 1 seed 1 they reach 15 of the 18 rooms (HO0 measured the same).
    /// </summary>
    [Fact]
    public void The_two_largest_giant_classes_reach_15_of_the_18_rooms()
    {
        var stand = RoomsReached(Nav, FixtureLevel, Nav.CellAt(Arrival, ClearanceClass.Stand), ClearanceClass.Stand);
        foreach (var cls in new[] { ClearanceClass.Hull4, ClearanceClass.Hull5 })
        {
            var giant = RoomsReached(Nav, FixtureLevel, Nav.CellAt(Arrival, cls), cls);
            output.WriteLine($"{HordeHulls.NameOf(cls)}: {giant.Count} rooms; missing {string.Join(", ", stand.Except(giant).Select(r => $"{r} {Data.Rooms[r].Name}"))}");
            Assert.Equal(15, giant.Count);
            Assert.Subset(stand, giant);
        }
    }

    [Fact]
    public void Each_extra_hull_admits_no_more_cells_than_the_one_below_it()
    {
        int last = Nav.CellsOf(ClearanceClass.Stand);
        for (int c = 2; c < 2 + HordeHulls.Scales.Length; c++)
        {
            int cells = Nav.CellsOf((ClearanceClass)c);
            Assert.True(cells > 0 && cells <= last, $"class {c}: {cells} cells after {last}");
            last = cells;
        }
    }

    [Fact]
    public void The_voxel_columns_cover_the_graph()
    {
        var nav = Nav;
        for (int i = 0; i < nav.Graph.CellCount; i += 97)
        {
            var c = nav.Graph.Cell(i).Centre;
            var spans = nav.Columns.Spans(nav.Columns.ColumnX(c.X), nav.Columns.ColumnY(c.Y));
            Assert.Contains(spans.ToArray(), s => s.Bottom <= c.Z + 1f && s.Top > c.Z + 1f);
        }
    }

    [Fact]
    public void Bytes_that_are_no_nav3d_are_refused_naming_the_file_format()
    {
        var e = Assert.Throws<InvalidDataException>(() => HordeNavData.Derive([1, 2, 3, 4]));
        Assert.NotEmpty(e.Message);
    }

    /// <summary>The level's rooms holding a cell of <paramref name="cls"/> reached from <paramref name="from"/>.</summary>
    internal static HashSet<int> RoomsReached(HordeNavData nav, HordeLevel level, CellId from, ClearanceClass cls)
    {
        bool[] reached = nav.Reachable(from, cls);
        var rooms = new HashSet<int>();
        for (int i = 0; i < reached.Length; i++)
        {
            if (!reached[i])
                continue;
            int room = level.RoomAt(nav.Graph.Cell(i).Centre + new AiVector(0f, 0f, 1f));
            if (room >= 0)
                rooms.Add(room);
        }

        return rooms;
    }
}

/// <summary>A robot's planning class by its scale (<see cref="HordeHulls"/>).</summary>
public sealed class HordeHullsTests
{
    [Theory]
    [InlineData(0.6f, ClearanceClass.Stand)]
    [InlineData(1.0f, ClearanceClass.Stand)]
    [InlineData(1.05f, ClearanceClass.Hull2)]
    [InlineData(1.15f, ClearanceClass.Hull2)]
    [InlineData(1.5f, ClearanceClass.Hull3)]
    [InlineData(1.6f, ClearanceClass.Hull4)]
    [InlineData(1.75f, ClearanceClass.Hull4)]
    [InlineData(1.9f, ClearanceClass.Hull5)]
    [InlineData(2.5f, ClearanceClass.Hull5)]
    public void A_robot_plans_by_the_smallest_class_its_scale_fits(float scale, ClearanceClass expected) =>
        Assert.Equal(expected, HordeHulls.ClassOf(scale));

    [Fact]
    public void The_graph_is_classed_for_a_hull_of_each_band_scaled_from_the_players()
    {
        var n = HordeNavNumbers.For();
        Assert.Equal(HordeHulls.Scales.Length, n.ExtraHulls.Count);
        Assert.Equal(new SourceSharp.Ai.Server.Nav.NavHull(92f, 156f), n.ExtraHulls[3]);
        Assert.Equal(48f, n.HullWidth);
        Assert.Equal(0f, n.MaxDoubleJumpRise);
    }

    [Fact]
    public void The_descent_server_references_the_service_free_nav_and_never_the_tf2_bots()
    {
        var references = typeof(HordeNavData).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("SourceSharp.Ai.Nav", references);
        Assert.DoesNotContain("SourceSharp.Ai.TF2", references);
        Assert.DoesNotContain("SourceSharp.Ai.Server", references);
    }
}
