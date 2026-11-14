using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A Descent room library as <see cref="RoomLibrary"/> writes it for
/// map-tools' rooms pipeline: cells with gaps, each room's <c>info_room</c>,
/// the singletons in the gaps, and the level file of a hand-given grid.
/// </summary>
public sealed class DescentRoomLibraryTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static RoomBuilder New(string name) => new(name, Kit, RoomTheme.Mine);

    private static RoomLibrary Library() => new RoomLibrary(Kit)
        .Add(New("drift").WithHeight(256).WithWidth(256).WithDoors(CellFace.East, CellFace.West).Build())
        .Add(New("hall").WithDoors(CellFace.East).WithLight(new Point(512, 512, 900), 600).Build())
        .Add(New("up").WithHeight(512).WithDoors(CellFace.South).WithRole(RoomRole.Up, new Point(256, 512, 32), 0).Build());

    private static Dictionary<string, string> Keys(VmfEntity e) => e.KeyValues.ToDictionary(kv => kv.Key, kv => kv.Value);

    private static VmfEntity InfoRoom(VmfMap map, string name)
        => Assert.Single(map.Entities, e => e.ClassName == "info_room" && Keys(e)["name"] == name);

    private static Point Origin(VmfEntity e)
    {
        float[] v = [.. Keys(e)["origin"].Split(' ').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture))];
        return new Point(v[0], v[1], v[2]);
    }

    /// <summary>Which room's cell (index) a point lies in, or -1 in a gap.</summary>
    private static int CellOf(RoomLibrary lib, Point p)
    {
        for (int i = 0; i < lib.Rooms.Count; i++)
        {
            Point o = lib.Corner(i);
            if (p.X >= o.X && p.X <= o.X + Kit.Cell && p.Y >= o.Y && p.Y <= o.Y + Kit.Cell && p.Z >= 0 && p.Z <= lib.Rooms[i].Height)
                return i;
        }

        return -1;
    }

    [Fact]
    public void A_cube_rooms_info_room_names_it_and_states_the_grid_and_kit_at_its_cells_corner()
    {
        RoomLibrary lib = Library();
        var keys = Keys(InfoRoom(lib.Build(), "hall"));

        Assert.Equal(lib.Corner(1).ToString(), keys["origin"]);
        Assert.Equal(("1024", "208", "192", "16"), (keys["cell_size"], keys["door_width"], keys["door_height"], keys["wall_depth"]));
        Assert.False(keys.ContainsKey("room_height"));
        Assert.False(keys.ContainsKey("room_role"));
    }

    [Fact]
    public void A_low_room_writes_its_room_height()
    {
        Assert.Equal("256", Keys(InfoRoom(Library().Build(), "drift"))["room_height"]);
    }

    [Fact]
    public void A_role_room_writes_its_room_role()
    {
        Assert.Equal("up", Keys(InfoRoom(Library().Build(), "up"))["room_role"]);
    }

    [Fact]
    public void Cells_stand_a_gap_apart_along_x()
    {
        RoomLibrary lib = Library();

        Assert.Equal(Kit.Cell + RoomLibrary.DefaultGap, lib.Corner(2).X - lib.Corner(1).X);
        Assert.Equal((0f, 0f), (lib.Corner(2).Y, lib.Corner(2).Z));
    }

    [Fact]
    public void Every_brush_lies_inside_one_rooms_cell()
    {
        RoomLibrary lib = Library();
        VmfMap map = lib.Build();
        var solids = map.WorldSolids.Concat(map.Entities.SelectMany(e => e.Solids)).ToArray();

        Assert.NotEmpty(solids);
        foreach (VmfSolid solid in solids)
        {
            var points = solid.Sides.SelectMany(s => (Point[])[s.P0, s.P1, s.P2]).ToArray();
            var mins = new Point(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
            var maxs = new Point(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
            int cell = CellOf(lib, mins);
            Assert.True(cell >= 0, $"a brush at ({mins}) is in a gap");
            Assert.Equal(cell, CellOf(lib, maxs));
        }
    }

    [Theory]
    [InlineData("env_fog_controller")]
    [InlineData("env_tonemap_controller")]
    public void A_library_controller_stands_once_in_a_gap(string className)
    {
        RoomLibrary lib = Library();

        VmfEntity controller = Assert.Single(lib.Build().Entities, e => e.ClassName == className);
        Assert.Equal(-1, CellOf(lib, Origin(controller)));
    }

    [Fact]
    public void A_library_has_no_sun_unless_asked()
    {
        Assert.DoesNotContain(Library().Build().Entities, e => e.ClassName == "light_environment");
    }

    [Fact]
    public void A_library_asked_for_a_sun_has_one_in_a_gap()
    {
        RoomLibrary lib = Library().WithSun(new Sun("0 30 0", "-50", "255 240 220 300", "60 60 70 40"));

        VmfEntity sun = Assert.Single(lib.Build().Entities, e => e.ClassName == "light_environment");
        Assert.Equal(-1, CellOf(lib, Origin(sun)));
        Assert.Equal("-50", Keys(sun)["pitch"]);
    }

    [Fact]
    public void A_room_on_another_kit_is_refused()
    {
        Room arena = new RoomBuilder("arena", RoomKit.Boss3072, RoomTheme.Mine).Build();

        Assert.Throws<ArgumentException>(() => new RoomLibrary(Kit).Add(arena));
    }

    [Fact]
    public void Two_rooms_of_one_name_are_refused()
    {
        Room drift = New("drift").Build();

        Assert.Throws<ArgumentException>(() => new RoomLibrary(Kit).Add(drift).Add(drift));
    }

    [Fact]
    public void A_boss_library_sets_the_navigation_voxel_its_cell_needs()
    {
        var lib = new RoomLibrary(RoomKit.Boss4096).Add(new RoomBuilder("arena", RoomKit.Boss4096, RoomTheme.Mine).WithHeight(1280).Build());

        Assert.Contains(new KeyValuePair<string, string>("nav_voxel_size", "32"), lib.Build().World);
    }

    [Fact]
    public void A_normal_library_on_the_tools_default_voxel_leaves_it_unwritten()
    {
        Assert.DoesNotContain(Library().Build().World, kv => kv.Key == "nav_voxel_size");
    }

    [Fact]
    public void A_library_on_the_tools_default_voxel_leaves_it_unwritten()
    {
        Assert.DoesNotContain(new RoomLibrary(RoomKit.Boss2048).Build().World, kv => kv.Key == "nav_voxel_size");
    }

    [Fact]
    public void A_room_entity_is_moved_to_its_cell()
    {
        RoomLibrary lib = Library();

        VmfEntity light = Assert.Single(lib.Build().Entities, e => e.ClassName == "light");
        Assert.Equal(lib.Corner(1) + new Point(512, 512, 900), Origin(light));
    }

    [Fact]
    public void Two_builds_write_the_same_bytes()
    {
        Assert.Equal(Library().Build().Write(), Library().Build().Write());
    }

    [Fact]
    public void A_level_file_lists_the_north_row_first_with_empty_cells_as_tildes()
    {
        string yaml = new LevelGrid("rooms.vmf", [["up", null, "down@90"], ["bend", "drift", "tee@180"]])
        { UpMap = "tf2_descent_town", DownMap = "tf2_descent-2" }.Write();

        Assert.Contains("library: rooms.vmf\nrows: 2\ncolumns: 3\n", yaml);
        Assert.Contains("grid:\n  - [up, ~, down@90]\n  - [bend, drift, tee@180]\n", yaml);
        Assert.Contains("up_map: tf2_descent_town\n", yaml);
        Assert.Contains("down_map: tf2_descent-2\n", yaml);
    }

    [Fact]
    public void A_level_file_without_maps_says_none_for_up_and_down()
    {
        string yaml = new LevelGrid("rooms.vmf", [["arena"]]).Write();

        Assert.Contains("up: none\ndown: none\n", yaml);
    }

    [Fact]
    public void A_level_with_ragged_rows_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => new LevelGrid("rooms.vmf", [["a", "b"], ["c"]]).Write());
    }
}
