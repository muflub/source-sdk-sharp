using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The room lint (task B1): one fact per rule, each with a refusing
/// fixture and the clean room it was made from as the control.
/// </summary>
public sealed class DescentRoomLintTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;
    private const float Floor = 32;

    private static RoomBuilder New(int height = 1024) =>
        new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(height).WithDoors(CellFace.West, CellFace.East);

    private static RoomBuilder NorthSouth(int height = 1024) =>
        new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(height).WithDoors(CellFace.North, CellFace.South);

    private static IEnumerable<string> Rules(Room room) => RoomLint.Check(room).Select(f => f.Rule);

    public static TheoryData<string> Features => ["crates", "mezzanine", "pillars", "chasm", "shelf", "catwalk", "chimney", "gallery"];

    private static Room Feature(string name) => name switch
    {
        "crates" => New().CrateSteps(500, 700, Compass.East).Build(),
        "mezzanine" => New().Mezzanine(CellFace.North, 200, 500).Build(),
        "pillars" => New().SteppingPillars(300, 300, Compass.East, JumpTier.S3).Build(),
        "chasm" => NorthSouth().ChasmLeap(160, 200, Compass.East, 400, JumpTier.S2).Build(),
        "shelf" => New(512).RocketShelf(CellFace.South, 400, 624).Build(),
        "catwalk" => New().CatwalkRing().Build(),
        "chimney" => New(1536).ChimneyLedges(CellFace.North, CellFace.South).Build(),
        _ => New(1536).RoofGallery(CellFace.North, 300, 700).Build(),
    };

    [Fact]
    public void The_demonstration_rooms_lint_clean_at_every_seed_tried()
    {
        for (ulong seed = 0; seed < 4; seed++)
            foreach (Room room in RoomCatalogue.Rooms(RoomCatalogue.Demonstration, Kit, RoomTheme.Grey, seed))
                Assert.Empty(RoomLint.Check(room));
    }

    [Theory]
    [MemberData(nameof(Features))]
    public void Every_jump_feature_lints_clean_in_a_hall(string feature)
    {
        Assert.Empty(RoomLint.Check(Feature(feature)));
    }

    [Fact]
    public void Sealed_refuses_a_room_with_a_wall_taken_out()
    {
        Room room = New(512).WithDoors(CellFace.North).Build();
        // The south cap: a wall-material brush across the cell at the south face.
        bool SouthCap(Brush b) => b.Material == RoomTheme.Grey.Wall.Material && b.Mins.Y == 0 && b.Maxs.Y == Kit.WallDepth && b.Maxs.X - b.Mins.X >= Kit.Interior;
        var open = room with { World = [.. room.World.Where(b => !SouthCap(b))] };

        Assert.DoesNotContain(RoomLint.Sealed, Rules(room));
        Assert.True(open.World.Count < room.World.Count);
        Assert.Contains(RoomLint.Sealed, Rules(open));
    }

    [Fact]
    public void Doors_on_the_kit_refuses_a_plug_cut_smaller_than_the_opening()
    {
        Room room = New(512).Build();
        var (mins, maxs) = Kit.PlugBox(CellFace.West);
        var cut = room with
        {
            World = [.. room.World.Select(b => b.IsBox && b.Mins == mins && b.Maxs == maxs ? Brush.Box(mins, maxs with { Z = maxs.Z - 16 }, b.Surface) : b)],
        };

        Assert.DoesNotContain(RoomLint.DoorsOnKit, Rules(room));
        Assert.Contains(RoomLint.DoorsOnKit, Rules(cut));
    }

    [Fact]
    public void Marker_floor_refuses_a_marker_in_mid_air()
    {
        Assert.DoesNotContain(RoomLint.MarkerFloor, Rules(New(512).WithMarker(MarkerKind.Trap, new(512, 512, Floor)).Build()));
        Assert.Contains(RoomLint.MarkerFloor, Rules(New(512).WithMarker(MarkerKind.Trap, new(512, 512, Floor + 200)).Build()));
    }

    [Fact]
    public void Marker_floor_refuses_a_marker_whose_hull_would_stand_half_off_a_ledge()
    {
        Room room = New(512).WithBox(new(400, 400, Floor), new(432, 432, Floor + 64)).WithMarker(MarkerKind.Barrel, new(416, 416, Floor + 64)).Build();

        Assert.Contains(RoomLint.MarkerFloor, Rules(room));
    }

    [Fact]
    public void Jump_clear_air_is_measured_from_the_brushes_not_the_tag()
    {
        var jump = new RoomEntry([], Jump: true);
        Room hall = New(1024).WithEntry(jump).Build();
        Room chamber = New(512).WithEntry(jump).Build();
        Room cluttered = New(1024).WithEntry(jump).WithBox(new(160, 32, 400), new(864, 992, 432)).Build();

        Assert.DoesNotContain(RoomLint.JumpClearAir, Rules(hall));
        Assert.Contains(RoomLint.JumpClearAir, Rules(chamber));
        Assert.Contains(RoomLint.JumpClearAir, Rules(cluttered));
    }

    [Fact]
    public void Ledge_pays_refuses_a_ledge_only_a_jump_reaches_with_nothing_on_it()
    {
        RoomBuilder Block() => New(512).WithBox(new(448, 448, Floor), new(576, 576, Floor + 224));

        Assert.Contains(RoomLint.LedgePays, Rules(Block().Build()));
        Assert.DoesNotContain(RoomLint.LedgePays, Rules(Block().WithMarker(MarkerKind.Container, new(512, 512, Floor + 224)).Build()));
        Assert.DoesNotContain(RoomLint.LedgePays, Rules(Block().WithBrushes(Brush.Stairs(new(576, 448, Floor), new(800, 576, Floor + 224), Compass.West, 14, "t/s")).Build()));
    }

    [Fact]
    public void Inside_cell_refuses_a_brush_past_the_cells_edge()
    {
        Room room = New(512).Build();
        var past = room with { World = [.. room.World, Brush.Box(new(1000, 0, 32), new(1040, 32, 64), "t/x")] };

        Assert.DoesNotContain(RoomLint.InsideCell, Rules(room));
        Assert.Contains(RoomLint.InsideCell, Rules(past));
    }

    [Fact]
    public void Frame_clear_refuses_a_frame_that_leaves_less_than_a_giants_92_by_156()
    {
        Room room = New(512).Build();
        var (mins, maxs) = Kit.PlugBox(CellFace.East);
        var fat = room with
        {
            Entities = [.. room.Entities.Select(e => e.Key("room_needs") == "joined_east"
                ? e with { Brushes = [.. e.Brushes, Brush.Box(mins with { Y = 384 }, maxs with { Y = 470 }, "t/f")] } : e)],
        };

        Assert.DoesNotContain(RoomLint.FrameClear, Rules(room));
        Assert.Contains(RoomLint.FrameClear, Rules(fat));
    }

    [Fact]
    public void Frame_clear_refuses_a_door_without_a_frame()
    {
        Assert.Contains(RoomLint.FrameClear, Rules(New(512).WithoutDoorFrames().Build()));
    }
}
