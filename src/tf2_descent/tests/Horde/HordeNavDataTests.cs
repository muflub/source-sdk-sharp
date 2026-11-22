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
///
/// <para>
/// The committed level is depth 1 as generated before 50cefc6c3 (2026-11-14), kept on purpose
/// (Level/Fixtures/README.md): its wide drift rooms have solid 8-unit floor rails. Since the
/// derivation asks a hull's fit on a step from the voxel over its feet (ruling 2026-11-15) the
/// giants stand on those rails, so every class reaches every room, as in a level built today
/// (whose rails are not solid). The cut floor the reach and path facts need is a level of their
/// own (<see cref="CutFloorLevel"/>).
/// </para>
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
    /// The two largest giants' classes reach all 18 rooms. Until 2026-11-15 they reached 15: the
    /// wide drift rooms' 8-unit floor rails top out inside a voxel, and the derivation answered a
    /// hull standing on such a step as blocked, which cut rooms 6, 16 and 17 off (first read as
    /// the rails being in the way, 2026-11-13). Asked from the voxel over the feet, the giants stand
    /// on the rails as the game's movement puts them.
    /// </summary>
    [Fact]
    public void The_two_largest_giant_classes_reach_every_room_standing_on_the_floor_rails()
    {
        foreach (var cls in new[] { ClearanceClass.Hull4, ClearanceClass.Hull5 })
        {
            var giant = RoomsReached(Nav, FixtureLevel, Nav.CellAt(Arrival, cls), cls);
            output.WriteLine($"{HordeHulls.NameOf(cls)}: {giant.Count} rooms");
            Assert.Equal(FixtureLevel.RoomCount, giant.Count);
        }
    }

    /// <summary>
    /// Every class reaches every room of a built level whose pool rooms stand their walkways 64
    /// over the floor up a flight from each door: the derivation stands a hull on the highest step
    /// under its box. While it raised them one step only, every extra hull reached 20 of depth 8
    /// seed 1's 37 rooms and 21 of depth 9's (measured 2026-11-15 over 16a29fcd6's derivation).
    /// Depth 9 is asked at seed 2: its seed 1 layout at 6b09663d7 draws no pool room.
    /// </summary>
    private void EveryClassReachesEveryRoomOf(int depth, ulong seed)
    {
        var files = LevelFixture.Built(depth, seed);
        LevelData data = files.Read();
        HordeLevel level = LevelAdapters.ToHorde(data);
        HordeNavData nav = HordeNavData.Derive(files.Nav3d);
        var arrival = new AiVector(data.Arrivals[0].Position.X, data.Arrivals[0].Position.Y, data.Arrivals[0].Position.Z);
        Assert.Contains(data.Rooms, r => r.Name.StartsWith("pool", StringComparison.Ordinal));
        foreach (var cls in new[] { ClearanceClass.Stand, ClearanceClass.Hull2, ClearanceClass.Hull3, ClearanceClass.Hull4, ClearanceClass.Hull5 })
        {
            var reached = RoomsReached(nav, level, nav.CellAt(arrival, cls), cls);
            output.WriteLine($"{HordeHulls.NameOf(cls)}: {reached.Count} of {level.RoomCount} rooms");
            Assert.Equal(level.RoomCount, reached.Count);
        }
    }

    [BuiltLevelFact(8, 1)]
    public void Every_class_reaches_every_room_of_depth_8_seed_1_pools_included() => EveryClassReachesEveryRoomOf(8, 1);

    [BuiltLevelFact(9, 2)]
    public void Every_class_reaches_every_room_of_depth_9_seed_2_pools_included() => EveryClassReachesEveryRoomOf(9, 2);

    /// <summary>
    /// The room table of depth 1 seed 1 (the output): for each room, the cells each class stands on
    /// and how many of them it reaches from the arrival. Every class reaches cells in every room;
    /// until 2026-11-15 the x1.75 and x1.90 classes reached none in rooms 6, 16 and 17 (see
    /// <see cref="The_two_largest_giant_classes_reach_every_room_standing_on_the_floor_rails"/>).
    /// </summary>
    [Fact]
    public void The_room_table_of_depth_1_seed_1_has_every_class_reaching_cells_in_every_room()
    {
        var nav = Nav;
        var level = FixtureLevel;
        var classes = new[] { ClearanceClass.Stand, ClearanceClass.Hull2, ClearanceClass.Hull3, ClearanceClass.Hull4, ClearanceClass.Hull5 };
        var reached = classes.Select(c => nav.Reachable(nav.CellAt(Arrival, c), c)).ToArray();
        var unreached = new List<string>();
        for (int r = 0; r < level.RoomCount; r++)
        {
            var row = new System.Text.StringBuilder(FormattableString.Invariant($"room {r,2} {Data.Rooms[r].Name,-16}"));
            for (int k = 0; k < classes.Length; k++)
            {
                int stands = 0, reach = 0;
                for (int i = 0; i < nav.Graph.CellCount; i++)
                {
                    if (level.RoomAt(nav.Graph.Cell(i).Centre + new AiVector(0f, 0f, 1f)) != r || !nav.Graph.Cell(i).Admits(classes[k]))
                        continue;
                    stands++;
                    if (reached[k][i])
                        reach++;
                }

                row.Append(FormattableString.Invariant($" | {HordeHulls.NameOf(classes[k])} {reach}/{stands}{(reach == 0 ? " UNREACHED" : "")}"));
                if (reach == 0)
                    unreached.Add($"{HordeHulls.NameOf(classes[k])} in room {r}");
            }

            output.WriteLine(row.ToString());
        }

        Assert.Empty(unreached);
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
