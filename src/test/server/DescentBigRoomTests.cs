using SourceSharp.Host.Contracts;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Rooms larger than one cell (M24, map tools' multi-cell rooms and
/// <c>door_sill floor</c>): the footprint key, a socket per perimeter cell
/// edge with its frame and cap, nothing on an internal cell face, the
/// library's room for them, and the level file's '+' cells.
/// </summary>
public sealed class DescentBigRoomTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Hall() => BigShapes.Hall(Kit, RoomTheme.Grey, 0).First();

    [Fact]
    public void A_two_by_two_hall_has_a_socket_on_each_of_its_eight_perimeter_cell_edges()
    {
        Room hall = Hall();

        Assert.Equal(8, hall.Sockets.Count());
        Assert.Equal(8, hall.World.Count(b => b.Material == RoomTheme.Grey.Plug.Material));
    }

    [Fact]
    public void Each_socket_is_framed_and_capped_under_its_own_name()
    {
        Room hall = Hall();
        string[] names = ["east0", "east1", "west0", "west1", "north0", "north1", "south0", "south1"];

        foreach (string n in names)
        {
            Assert.Contains(hall.Entities, e => e.Key("room_needs") == "joined_" + n && e.Key("model") == Kit.Frame.Model);
            Assert.Contains(hall.Entities, e => e.ClassName == "func_brush" && e.Key("room_needs") == "!joined_" + n);
        }
    }

    [Fact]
    public void A_larger_rooms_info_room_states_its_footprint()
    {
        var map = new RoomLibrary(Kit).Add(Hall()).Build();
        var info = map.Entities.Single(e => e.ClassName == "info_room");

        var keys = info.KeyValues.ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(("2 2", "floor"), (keys["room_footprint"], keys["door_sill"]));
    }

    [Fact]
    public void A_brush_on_an_internal_cell_face_is_refused()
    {
        var b = new RoomBuilder("hall_x", Kit, RoomTheme.Grey).WithFootprint(2).WithHeight(512)
            .WithBox(new Point(900, 300, 16), new Point(1024, 400, 64));

        Assert.Throws<ArgumentException>(() => b.Build());
    }

    [Fact]
    public void A_brush_crossing_an_internal_cell_face_is_kept()
    {
        Room room = new RoomBuilder("hall_x", Kit, RoomTheme.Grey).WithFootprint(2).WithHeight(512)
            .WithBox(new Point(900, 300, 16), new Point(1100, 400, 64)).Build();

        Assert.Contains(room.World, b => b.Mins.X == 900 && b.Maxs.X == 1100);
    }

    [Fact]
    public void The_library_leaves_a_larger_room_its_whole_footprint()
    {
        var lib = new RoomLibrary(Kit).Add(Hall()).Add(Hall() with { Name = "hall_y" });

        Assert.Equal(2 * Kit.Cell + lib.Gap, lib.Corner(1).X);
    }

    [Fact]
    public void A_larger_room_is_named_with_its_footprint()
    {
        Room hall = RoomCatalogue.Rooms([new("hall", BigShapes.Hall)], Kit, RoomTheme.Grey, 0).First();

        Assert.Equal((2, 2, Doors.All), (RoomName.Parse(hall.Name).CellsX, RoomName.Parse(hall.Name).CellsY, RoomName.Parse(hall.Name).Doors));
    }

    [Fact]
    public void The_level_file_marks_a_larger_rooms_other_cells_with_a_plus()
    {
        var plan = new LevelPlan(2, 3, [new Placement("drift_a-ew-l", 0, 0, 0, "up"), new Placement("hall_a-nesw-h-2x2", 0, 1, 0, "branch")], "drift_a-ew-l", "");

        string text = DescentLevel.Write(plan, "descent_d02", null);

        Assert.Contains("[drift_a-ew-l, hall_a-nesw-h-2x2, +", text, StringComparison.Ordinal);
        Assert.Contains("[~", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Count(ch => ch == '+'));
    }

    [Fact]
    public void A_larger_room_covering_another_room_is_refused_by_the_level_file()
    {
        var plan = new LevelPlan(2, 3, [new Placement("drift_a-ew-l", 1, 2, 0, "up"), new Placement("hall_a-nesw-h-2x2", 0, 1, 0, "branch")], "drift_a-ew-l", "");

        Assert.Throws<ArgumentException>(() => DescentLevel.Write(plan, "descent_d02", null));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public void A_depths_larger_rooms_build(int depth)
    {
        var big = libs.Rooms(depth, 0).Where(r => r.Cells > 1).ToArray();

        Assert.NotEmpty(big);
    }

    [Fact]
    public void The_lint_reads_a_larger_room_over_its_whole_footprint()
    {
        // A pack spawn in the air over the hall's second cell: the lint read one cell and let it through.
        Room hall = BigShapes.Hall(Kit, RoomTheme.Grey, 0).First();
        Room bad = hall with { Entities = [.. hall.Entities, new RoomEntity("info_poi", new Point(1600, 1600, 400), [new("poi_type", "descent_spawn_pack")], [], [])] };

        Assert.Contains(RoomLint.Check(bad), f => f.Rule == RoomLint.MarkerFloor);
    }

    [Fact]
    public void A_larger_rooms_door_path_reads_its_sockets_beyond_the_first_cell()
    {
        // A wall across the hall's east half cuts east1 off from every other socket.
        Room hall = new RoomBuilder("hall_x", Kit, RoomTheme.Grey).WithFootprint(2).WithHeight(512)
            .WithBox(new Point(1200, 1120, 16), new Point(2032, 1180, 496)).WithBox(new Point(1120, 1180, 16), new Point(1180, 2032, 496)).Build();

        Assert.Contains(RoomLint.CheckDoorPath(hall, new RoomColumns(hall)), f => f.Message.Contains("east1", StringComparison.Ordinal));
    }

    [Fact]
    public void The_dress_lint_reads_a_larger_rooms_sockets_beyond_the_first_cell()
    {
        // A crate against the north1 socket's opening, in the hall's second cell.
        Room hall = Hall();
        PropModel crate = ActOne.BigCrate;
        Room dressed = hall with
        {
            Entities = [.. hall.Entities, new RoomEntity("prop_static", new Point(1536, 2048 - 16 - 60, 18), [new("model", crate.Model), new("angles", "0 0 0"), new("solid", "6"), new(RoomDresser.RoleKey, nameof(RoomDresser.Role.Floor))], [], [])],
        };

        Assert.Contains(DressLint.Check(hall, dressed, ActOne.Haulage), f => f.Rule == DressLint.DoorClear && f.Message.Contains("north1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_larger_room_with_chosen_sockets_has_a_plug_only_on_them()
    {
        Room arena = new RoomBuilder("arena_x", Kit, RoomTheme.Grey).WithFootprint(3).WithHeight(1024)
            .WithSockets((CellFace.West, 1), (CellFace.East, 1)).Build();
        var plugs = arena.World.Where(b => b.Material == RoomTheme.Grey.Plug.Material).Select(b => (b.Mins, b.Maxs)).ToHashSet();

        Assert.Equal([arena.PlugBox(CellFace.West, 1), arena.PlugBox(CellFace.East, 1)], plugs);
    }

    [Fact]
    public void A_larger_room_with_chosen_sockets_has_no_door_box_on_its_other_edges()
    {
        // The tools take any brush exactly filling a door box for a socket.
        Room arena = new RoomBuilder("arena_x", Kit, RoomTheme.Grey).WithFootprint(3).WithHeight(1024)
            .WithSockets((CellFace.West, 1), (CellFace.East, 1)).Build();
        var other = new[] { CellFace.East, CellFace.West, CellFace.North, CellFace.South }
            .SelectMany(f => Enumerable.Range(0, 3).Select(k => (f, k))).Where(s => s is not (CellFace.West, 1) and not (CellFace.East, 1))
            .Select(s => arena.PlugBox(s.f, s.k)).ToHashSet();

        Assert.DoesNotContain(arena.World, b => other.Contains((b.Mins, b.Maxs)));
    }

    [Fact]
    public void A_larger_room_with_chosen_sockets_frames_and_caps_only_them()
    {
        Room arena = new RoomBuilder("arena_x", Kit, RoomTheme.Grey).WithFootprint(3).WithHeight(1024)
            .WithSockets((CellFace.West, 1), (CellFace.East, 1)).Build();

        // A cap where each is capped, and (a model frame's doorway, brief p9) a lit slab over its dip where it is joined.
        Assert.Equal(["!joined_east1", "!joined_west1", "joined_east1", "joined_west1"],
            arena.Entities.Where(e => e.ClassName == "func_brush").Select(e => e.Key("room_needs")!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_larger_room_with_chosen_sockets_lints_clean()
    {
        var b = new RoomBuilder("arena_x", Kit, RoomTheme.Grey).WithFootprint(2).WithHeight(1024)
            .WithSockets((CellFace.West, 0), (CellFace.East, 1));
        foreach (float x in (float[])[256, 768, 1280, 1792])
            foreach (float y in (float[])[256, 768, 1280, 1792])
                b.LampAt(x, y, 448, Parts.HallLamp);
        b.Pack(1024, 700).Pack(1024, 1400).Mark(MarkerKind.Barrel, 1700, 300);

        Assert.Empty(RoomLint.Check(b.Build()));
    }

    [Fact]
    public void A_halls_middle_is_lit_by_a_lamp_hung_on_a_cord_to_its_roof()
    {
        // The middle of a 2x2 hall is out of reach of every wall and roof lamp (the lint's lit rule): a pendant lights it.
        Room hall = libs.Rooms(2, 0).First(r => r.Cells > 1 && r.Height >= 1024);
        var bells = hall.Entities.Where(e => e.Key("model") == ActOne.BellLamp.Model).Select(e => e.Origin!.Value).ToArray();
        var cords = hall.Entities.Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Cord)).ToArray();

        var solids = new RoomSolids(hall);

        Assert.NotEmpty(bells);
        // Each pendant's topmost cord piece ends on the brush over it (the roof, or a roof beam).
        Assert.All(bells, p => Assert.Contains(cords, c => c.Origin!.Value.X == p.X && c.Origin.Value.Y == p.Y
                                                         && Math.Abs(c.Origin.Value.Z + ModelLength(c) - solids.CeilingAbove(p.X, p.Y, p.Z)!.Value) < 0.6f));
    }

    [Fact]
    public void A_halls_pendants_hang_on_a_grid_over_its_body()
    {
        Room hall = libs.Rooms(2, 0).First(r => r.Cells > 1 && r.Height >= 1024);
        int grid = ActOne.Haulage.Pendants!.Grid;
        Rect body = hall.Body;
        int nx = (int)((body.X1 - body.X0) / grid), ny = (int)((body.Y1 - body.Y0) / grid);
        var points = Enumerable.Range(0, nx).SelectMany(i => Enumerable.Range(0, ny).Select(j =>
            (X: MathF.Round(body.X0 + (body.X1 - body.X0) * (i + 0.5f) / nx), Y: MathF.Round(body.Y0 + (body.Y1 - body.Y0) * (j + 0.5f) / ny)))).ToArray();
        var bells = hall.Entities.Where(e => e.Key("model") == ActOne.BellLamp.Model).Select(e => (e.Origin!.Value.X, e.Origin.Value.Y)).ToHashSet();

        Assert.True(points.Count(bells.Contains) * 2 >= points.Length, $"{points.Count(bells.Contains)} of {points.Length} grid points carry a pendant");
    }

    public static TheoryData<string> HallVariants => ["hall_a", "hall_b", "hall_c"];

    private static Room HallOf(string variant) => BigShapes.Hall(Kit, ActOne.Haulage, 0).Single(r => r.Name == variant);

    [Theory]
    [MemberData(nameof(HallVariants))]
    public void A_hall_lints_clean(string variant)
    {
        Assert.Empty(RoomLint.Check(HallOf(variant)));
    }

    [Fact]
    public void The_loading_hall_has_a_dock_a_crouch_jump_high_along_its_north_wall()
    {
        Room hall = HallOf("hall_a");
        var columns = new RoomColumns(hall);
        // Surfaces 48 up whose column is in the north quarter: the dock's deck.
        int deck = columns.Surfaces().Count(s => s.Z == hall.Kit.Sill + 48 && s.Y > 1696);

        Assert.True(deck * RoomLint.Grid * RoomLint.Grid > 400_000, $"{deck} columns of dock");
    }

    [Fact]
    public void The_yard_hall_stands_a_plinth_under_its_focus()
    {
        Room hall = HallOf("hall_b");

        Assert.Equal(hall.Focus.Z, new RoomColumns(hall).SurfaceUnder(hall.Focus.X, hall.Focus.Y, hall.Focus.Z + 1)!.Value.Z);
        Assert.True(hall.Focus.Z > hall.Kit.Sill);
    }

    [Fact]
    public void The_gallery_hall_has_a_gallery_over_its_doors_that_everyone_walks_up_to()
    {
        Room hall = HallOf("hall_c");
        var columns = new RoomColumns(hall);
        var reached = columns.ReachableAtT1(hall.Sockets.Select(s => hall.DoorInside(s.Face, s.Offset, 48)));
        float top = hall.Kit.Sill + 224;

        Assert.True(columns.Surfaces().Count(s => s.Z == top && reached.Contains(s.Key)) > 1000, "the gallery is not reached on foot");
        // Over the north doors: the deck stands over the doorway's head.
        Assert.NotNull(columns.SurfaceUnder(hall.Kit.Cell / 2f, 2 * hall.Kit.Cell - 64, top + 1));
    }

    private static float ModelLength(RoomEntity cord) =>
        new[] { ActOne.Cable128, ActOne.Cable64, ActOne.Cable32 }.Single(m => m.Model == cord.Key("model")).Height;
}
