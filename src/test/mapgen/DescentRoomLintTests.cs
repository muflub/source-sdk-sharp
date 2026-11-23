using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Shapes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The room lint (task B1): one fact per rule, each with a refusing
/// fixture and the clean room it was made from as the control.
/// </summary>
public sealed class DescentRoomLintTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;
    private static readonly float Floor = Kit.Sill;

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
        // Clear of the way between the doors: a giant walks round the south of its stairs.
        "chasm" => NorthSouth().ChasmLeap(160, 320, Compass.East, 400, JumpTier.S2).Build(),
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
        // The bare fixture has no lamps or markers; those rules have fixtures of their own.
        Assert.DoesNotContain(RoomLint.Check(Feature(feature)), f => f.Rule is not (RoomLint.Lit or RoomLint.MarkerCount));
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
    public void Marker_floor_refuses_a_marker_whose_hull_stands_over_a_rail()
    {
        // A rail strip 8 wide and 8 high, a step, under the hull's east side: the map tools refuse the marker; 24 further east it is clear.
        Room clear = New(512).WithBox(new(560, 300, Floor), new(568, 700, Floor + 8)).WithMarker(MarkerKind.Barrel, new(512, 512, Floor)).Build();
        Room railed = New(512).WithBox(new(528, 300, Floor), new(536, 700, Floor + 8)).WithMarker(MarkerKind.Barrel, new(512, 512, Floor)).Build();

        Assert.DoesNotContain(RoomLint.MarkerFloor, Rules(clear));
        Assert.Contains(RoomLint.MarkerFloor, Rules(railed));
    }

    [Fact]
    public void Marker_floor_refuses_a_marker_off_the_kits_navigation_voxel()
    {
        RoomKit big = RoomKit.For("voxel32", 3072, FrameModel.FarmDoorframe, navVoxel: 32);
        float f = big.Sill;
        Room onGrid = new RoomBuilder("r", big, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West)
            .WithBox(new(1400, 1400, f), new(1600, 1600, f + 32)).WithMarker(MarkerKind.Shrine, new(1500, 1500, f + 32)).Build();
        Room offGrid = new RoomBuilder("r", big, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West)
            .WithBox(new(1400, 1400, f), new(1600, 1600, f + 16)).WithMarker(MarkerKind.Shrine, new(1500, 1500, f + 16)).Build();

        Assert.DoesNotContain(RoomLint.MarkerFloor, Rules(onGrid));
        Assert.Contains(RoomLint.MarkerFloor, Rules(offGrid));
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
    public void Frame_clear_refuses_a_kit_whose_frame_leaves_less_than_a_giants_92_by_156()
    {
        RoomKit narrow = Kit with { Frame = Kit.Frame with { Post = 60 } };
        Room room = New(512).Build();

        Assert.DoesNotContain(RoomLint.FrameClear, Rules(room));
        Assert.Contains(RoomLint.FrameClear, Rules(room with { Kit = narrow }));
    }

    [Fact]
    public void Frame_clear_refuses_a_solid_frame()
    {
        Room room = New(512).Build();
        var solid = room with
        {
            Entities = [.. room.Entities.Select(e => e.Key("model") == Kit.Frame.Model && e.Key("room_needs") == "joined_east"
                ? e with { Keys = [.. e.Keys.Where(k => k.Key != "solid"), new("solid", "6")] } : e)],
        };

        Assert.DoesNotContain(RoomLint.FrameClear, Rules(room));
        Assert.Contains(RoomLint.FrameClear, Rules(solid));
    }

    [Fact]
    public void Frame_clear_refuses_a_door_without_a_frame()
    {
        Assert.Contains(RoomLint.FrameClear, Rules(New(512).WithoutDoorFrames().Build()));
    }

    /// <summary>A 512 chamber west to east with two packs away from its doors, a barrel and a lamp over every part of its floor: clean under the marker and light rules.</summary>
    private static RoomBuilder Furnished()
    {
        var b = New(512).Pack(512, 256).Pack(512, 768).Mark(MarkerKind.Barrel, 256, 256);
        foreach (float x in (float[])[256, 512, 768])
            foreach (float y in (float[])[256, 768])
                b.LampAt(x, y, 384, Parts.ChamberLamp);
        return b;
    }

    [Fact]
    public void The_furnished_chamber_is_clean_under_the_door_path_marker_and_light_rules()
    {
        var rules = Rules(Furnished().Build()).ToArray();

        Assert.DoesNotContain(RoomLint.DoorPath, rules);
        Assert.DoesNotContain(RoomLint.MarkerCount, rules);
        Assert.DoesNotContain(RoomLint.Lit, rules);
    }

    [Fact]
    public void Door_path_refuses_a_wall_across_the_room()
    {
        Room room = Furnished().Block(new Rect(496, Kit.WallDepth, 528, Kit.Cell - Kit.WallDepth), 400, RoomTheme.Grey.Wall).Build();

        Assert.Contains(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Door_path_refuses_a_step_a_heavy_must_jump()
    {
        // A kerb the room's whole width, a crouch-jump high (T1 is not walking).
        Room room = Furnished().Block(new Rect(480, Kit.WallDepth, 544, Kit.Cell - Kit.WallDepth), JumpTable.CriticalPathReach).Build();

        Assert.Contains(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Door_path_refuses_a_gap_narrower_than_a_giant()
    {
        // A wall across with an 80-wide gap: a Heavy's 48 passes, a giant's 92 does not.
        Room room = Furnished().Block(new Rect(496, Kit.WallDepth, 528, 472), 400, RoomTheme.Grey.Wall)
            .Block(new Rect(496, 552, 528, Kit.Cell - Kit.WallDepth), 400, RoomTheme.Grey.Wall).Build();

        Assert.Contains(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Door_path_walks_up_stairs_of_16()
    {
        // The same kerb with stairs of 16 up to it from both sides.
        Room room = Furnished().Block(new Rect(480, Kit.WallDepth, 544, Kit.Cell - Kit.WallDepth), 64)
            .StairsUp(new Rect(352, Kit.WallDepth, 480, Kit.Cell - Kit.WallDepth), Compass.East, 64)
            .StairsUp(new Rect(544, Kit.WallDepth, 672, Kit.Cell - Kit.WallDepth), Compass.West, 64).Build();

        Assert.DoesNotContain(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Marker_count_refuses_a_combat_room_with_one_pack()
    {
        Room room = New(512).Pack(512, 256).Mark(MarkerKind.Barrel, 256, 256).LampAt(512, 512, 384, Parts.ChamberLamp).Build();

        Assert.Contains(RoomLint.MarkerCount, Rules(room));
    }

    [Fact]
    public void Marker_count_refuses_a_pack_beside_a_door()
    {
        Room room = Furnished().Pack(Kit.WallDepth + 96, Kit.Cell / 2f).Build();

        Assert.Contains(RoomLint.MarkerCount, Rules(room));
    }

    [Fact]
    public void Marker_count_refuses_a_combat_room_without_furniture()
    {
        Room clean = Furnished().Build();
        Room bare = clean with { Entities = [.. clean.Entities.Where(e => e.Key("poi_type") != Markers.PoiType(MarkerKind.Barrel))] };

        Assert.Contains(RoomLint.MarkerCount, Rules(bare));
    }

    [Fact]
    public void Marker_count_refuses_a_corridor_with_two_packs()
    {
        Room corridor = new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(256).WithWidth(256).WithDoors(CellFace.West, CellFace.East)
            .WithEntry(new RoomEntry(["corridor"], Low: true)).Pack(384, 512).Pack(640, 512).LampAt(512, 512, 160, Parts.ConnectorLamp).Build();

        Assert.Contains(RoomLint.MarkerCount, Rules(corridor));
        Assert.DoesNotContain(RoomLint.MarkerCount, Rules(corridor with { Entities = [.. corridor.Entities.Where(e => e.Origin != new Point(640, 512, Floor))] }));
    }

    [Fact]
    public void Lit_refuses_a_room_with_no_lamp()
    {
        Room clean = Furnished().Build();

        Assert.Contains(RoomLint.Lit, Rules(clean with { Entities = [.. clean.Entities.Where(e => e.ClassName != "light")] }));
    }

    [Fact]
    public void Lit_refuses_a_floor_beyond_its_lamps_reach()
    {
        // One chamber lamp in the west of a full cell leaves the east dark.
        Room room = New(512).Pack(512, 256).Pack(512, 768).Mark(MarkerKind.Barrel, 256, 256).LampAt(200, 512, 384, Parts.ChamberLamp).Build();

        Assert.Contains(RoomLint.Lit, Rules(room));
    }

    /// <summary>A spot at (512, 512, 384) reaching 900, pointing <paramref name="pitch"/> below level along +x, with a 60-degree cone.</summary>
    private static RoomEntity Spot(int pitch) => new("light_spot", new Point(512, 512, 384),
        [new("_fifty_percent_distance", "600"), new("_zero_percent_distance", "1200"), new("_cone", "60"),
         new("pitch", (-pitch).ToString(System.Globalization.CultureInfo.InvariantCulture)), new("angles", (-pitch).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 0")], [], []);

    [Fact]
    public void A_spot_lights_the_floor_inside_its_cone()
    {
        // Straight down: the floor under it is 368 below, on its axis.
        Assert.True(RoomLint.Lights(Spot(90), new Point(512, 512, 16)));
    }

    [Fact]
    public void A_spot_does_not_light_the_floor_behind_it()
    {
        // Pointing level along +x: the floor 300 behind it (-x) is 120 degrees off its axis, outside its 60.
        Assert.False(RoomLint.Lights(Spot(0), new Point(212, 512, 16)));
    }

    [Fact]
    public void Door_path_refuses_a_pack_no_giant_walks_to()
    {
        // One door; a wall across the room with a gap a Heavy passes and a giant does not.
        Room room = new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East)
            .Block(new Rect(496, Kit.WallDepth, 528, 472), 400, RoomTheme.Grey.Wall).Block(new Rect(496, 552, 528, Kit.Cell - Kit.WallDepth), 400, RoomTheme.Grey.Wall)
            .Pack(256, 512).Pack(768, 256).Mark(MarkerKind.Barrel, 768, 768).LampAt(512, 512, 384, Parts.ChamberLamp).Build();

        Assert.Contains(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Door_path_lets_a_pack_stand_by_a_wall_a_giant_walks_beside()
    {
        Room room = new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East)
            .Pack(Kit.WallDepth + 32, 512).Pack(768, 256).Mark(MarkerKind.Barrel, 768, 768).LampAt(512, 512, 384, Parts.ChamberLamp).Build();

        Assert.DoesNotContain(RoomLint.DoorPath, Rules(room));
    }

    [Fact]
    public void Hazard_path_refuses_a_hazard_across_the_way_between_two_doors()
    {
        // Wall to wall across the room between the west and east doors: no way round it.
        Room room = New(512).WithHazard("lava", new Point(480, Kit.WallDepth, Floor), new Point(544, Kit.Cell - Kit.WallDepth, Floor + 18)).Build();

        Assert.Contains(RoomLint.HazardPath, Rules(room));
    }

    [Fact]
    public void Hazard_path_lets_a_hazard_lie_beside_the_way_between_two_doors()
    {
        // The same strip short of the north half: a giant walks round its end.
        Room room = New(512).WithHazard("lava", new Point(480, Kit.WallDepth, Floor), new Point(544, 448, Floor + 18)).Build();

        Assert.DoesNotContain(RoomLint.HazardPath, Rules(room));
    }

    [Fact]
    public void Hazard_path_ignores_a_bosss_hazard_that_starts_off()
    {
        Room room = New(512).WithStagedHazard(2, "slime", new Point(480, Kit.WallDepth, Floor), new Point(544, Kit.Cell - Kit.WallDepth, Floor + 18)).Build();

        Assert.DoesNotContain(RoomLint.HazardPath, Rules(room));
    }
}
