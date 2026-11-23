using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The frame in every socket (task A15): the link drops a joined socket's plug
/// and the faces that touched it, so each room lines its half of the doorway
/// with the kit's frame model and covers its floor with a sill plate, static
/// props kept only where the socket is joined, at no entity's cost. The kit's
/// door is cut to the frame.
/// </summary>
public sealed class DescentDoorFrameTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    public static TheoryData<string> Kits => new() { "normal", "voxel32" };

    private static RoomKit KitNamed(string name) =>
        new[] { RoomKit.Normal, RoomKit.For("voxel32", 3072, FrameModel.FarmDoorframe, navVoxel: 32) }.Single(k => k.Name == name);

    private static Room Chamber(params CellFace[] doors) =>
        new RoomBuilder("c", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(doors).Build();

    private static RoomEntity FrameOf(Room room, CellFace face) =>
        Assert.Single(room.Entities, e => e.Key("model") == Kit.Frame.Model && e.Key("room_needs") == "joined_" + RoomBuilder.WallName(face));

    private static RoomEntity[] SillsOf(Room room, CellFace face) =>
        [.. room.Entities.Where(e => e.Key("model") == Kit.Frame.Sill!.Model && e.Key("room_needs") == "joined_" + RoomBuilder.WallName(face))];

    /// <summary>A prop's mesh extent along x and y (the model's depth along its facing, its width across), room-local, for a yaw of 0, 90, 180 or 270.</summary>
    private static (float X0, float X1, float Y0, float Y1) Extent(RoomEntity prop, float minX, float maxX, float halfWidth)
    {
        Point o = prop.Origin!.Value;
        int yaw = int.Parse(prop.Key("angles")!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        return yaw switch
        {
            0 => (o.X + minX, o.X + maxX, o.Y - halfWidth, o.Y + halfWidth),
            180 => (o.X - maxX, o.X - minX, o.Y - halfWidth, o.Y + halfWidth),
            90 => (o.X - halfWidth, o.X + halfWidth, o.Y + minX, o.Y + maxX),
            _ => (o.X - halfWidth, o.X + halfWidth, o.Y - maxX, o.Y - minX),
        };
    }

    [Fact]
    public void Every_door_gets_one_frame_and_its_sill_planks_kept_only_where_its_socket_is_joined()
    {
        Room room = Chamber(CellFace.East, CellFace.West, CellFace.North);

        foreach (CellFace face in room.Doors)
        {
            Assert.Equal("prop_static", FrameOf(room, face).ClassName);
            Assert.Equal(RoomBuilder.SillInsets(Kit).Count, SillsOf(room, face).Length);
            Assert.All(SillsOf(room, face), p => Assert.Equal("prop_static", p.ClassName));
        }
    }

    [Fact]
    public void A_wall_deeper_than_a_sill_plank_takes_more_than_one()
    {
        Assert.True(Kit.WallDepth > Kit.Frame.Sill!.Depth);
        Assert.Equal(2, RoomBuilder.SillInsets(Kit).Count);
    }

    [Theory]
    [InlineData(CellFace.East)]
    [InlineData(CellFace.South)]
    public void A_doors_floor_is_let_down_under_its_plug_and_stops_short_of_the_cell_face(CellFace face)
    {
        Room room = Chamber(face);
        var (mins, maxs) = Kit.PlugBox(face);
        float c = Kit.Cell, s = RoomBuilder.DoorwayDipShort;
        // The column at the plug's middle: the floor's top is the dip's.
        float x = (mins.X + maxs.X) / 2, y = (mins.Y + maxs.Y) / 2;
        var tops = room.World.Where(b => b.Material == RoomTheme.Grey.Floor.Material && b.Column(x, y) is not null).Select(b => b.Maxs.Z);
        Assert.Equal(Kit.Sill - RoomBuilder.DoorwayDip, tops.Max());
        // The floor at the cell face's own sliver is whole, so the slit under the plug is sealed.
        float fx = face == CellFace.East ? c - s / 2 : x, fy = face == CellFace.South ? s / 2 : y;
        Assert.Contains(room.World, b => b.Material == RoomTheme.Grey.Floor.Material && b.Maxs.Z == Kit.Sill && b.Column(fx, fy) is not null);
    }

    [Fact]
    public void A_door_adds_one_entity_the_link_keeps_where_it_is_joined_the_slab_over_its_dip()
    {
        Room bare = new RoomBuilder("c", Kit, RoomTheme.Grey).WithHeight(512).Build();
        Room doored = Chamber(CellFace.East, CellFace.West, CellFace.North, CellFace.South);
        var added = doored.Entities.Except(bare.Entities).ToArray();

        Assert.Contains(added, e => e.ClassName == "prop_static");
        // Static props cost no entity. The brush entities a door brings are its cap, kept where it is not joined,
        // and (brief p9) a lit slab over the doorway's dip, kept where it is: one entity a joined door.
        var brushes = added.Where(e => e.ClassName != "prop_static").ToArray();
        Assert.Equal(4, brushes.Count(e => e.Key("room_needs")!.StartsWith("!joined_", StringComparison.Ordinal)));
        Assert.Equal(4, brushes.Count(e => e.Key("room_needs")!.StartsWith("joined_", StringComparison.Ordinal) && e.Brushes.All(b => b.Maxs.Z <= Kit.Sill + 1)));
        Assert.Equal(8, brushes.Length);
    }

    [Fact]
    public void Frames_and_sills_are_not_solid_and_cast_no_shadow()
    {
        Room room = Chamber(CellFace.East);

        Assert.All([FrameOf(room, CellFace.East), .. SillsOf(room, CellFace.East)], p =>
        {
            Assert.Equal("0", p.Key("solid"));
            Assert.Equal("1", p.Key("disableshadows"));
        });
    }

    [Theory]
    [InlineData(CellFace.East)]
    [InlineData(CellFace.West)]
    [InlineData(CellFace.North)]
    [InlineData(CellFace.South)]
    public void A_frame_lines_its_rooms_half_of_the_doorway_up_to_the_cell_face(CellFace face)
    {
        FrameModel f = Kit.Frame;
        var (x0, x1, y0, y1) = Extent(FrameOf(Chamber(face), face), f.MinX, f.MaxX, f.Width / 2f);
        float c = Kit.Cell, t = Kit.WallDepth;
        (float near, float far) = face switch
        {
            CellFace.East => (c - x1, c - x0),
            CellFace.West => (x0, x1),
            CellFace.North => (c - y1, c - y0),
            _ => (y0, y1),
        };

        // It stops at its own cell face and reaches past the wall's face into the room.
        Assert.InRange(near, 0f, 0.05f);
        Assert.True(far > t, $"the frame reaches {far} in from the face; the wall is {t}");
    }

    [Theory]
    [InlineData(CellFace.East)]
    [InlineData(CellFace.North)]
    public void A_doors_sill_planks_cover_its_rooms_half_of_the_doorways_floor_up_to_the_cell_face(CellFace face)
    {
        SillModel s = Kit.Frame.Sill!;
        float c = Kit.Cell;
        var spans = SillsOf(Chamber(face), face).Select(p =>
        {
            var (x0, x1, y0, y1) = Extent(p, s.MinX, s.MaxX, s.Width / 2f);
            return face == CellFace.East ? (Near: c - x1, Far: c - x0, Lo: y0, Hi: y1) : (Near: c - y1, Far: c - y0, Lo: x0, Hi: x1);
        }).OrderBy(p => p.Near).ToArray();

        Assert.InRange(spans[0].Near, 0f, 0.05f);
        Assert.True(spans[^1].Far >= Kit.WallDepth);
        for (int i = 1; i < spans.Length; i++)
            Assert.True(spans[i].Near <= spans[i - 1].Far, $"a gap between planks at {spans[i - 1].Far}..{spans[i].Near}");
        (float lo, float hi) = (spans.Min(p => p.Lo), spans.Max(p => p.Hi));
        // Across, it reaches the posts, which stand on the rest of the doorway's floor.
        Assert.True(lo <= Kit.DoorLow + Kit.Frame.Post && hi >= Kit.DoorHigh - Kit.Frame.Post, $"{lo}..{hi}");
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void The_kits_opening_is_the_frames_outer_width_and_its_head_covers_the_top(string name)
    {
        RoomKit kit = KitNamed(name);
        FrameModel f = kit.Frame;

        Assert.Equal(f.Width, kit.DoorWidth);
        Assert.InRange(kit.DoorHeight, f.InnerHeight, f.Height);
    }

    [Theory]
    [MemberData(nameof(DescentRoomsVerbTests.AllDepths), MemberType = typeof(DescentRoomsVerbTests))]
    public void A_depths_kit_is_cut_to_its_themes_frame_whose_posts_and_head_cover_the_opening(int depth)
    {
        RoomKit kit = DescentRooms.KitFor(depth);
        FrameModel f = DescentRooms.ThemeFor(depth).DoorFrame;

        Assert.Equal(f, kit.Frame);
        // The posts' outer faces are the opening's sides; the head's top is at or above the opening's top, its underside below.
        Assert.Equal(f.Width, kit.DoorWidth);
        Assert.True(kit.DoorHeight <= f.Height && kit.DoorHeight >= f.InnerHeight, $"{kit.DoorHeight} against {f.InnerHeight}..{f.Height}");
    }

    [Fact]
    public void The_normal_kits_frame_fills_its_rooms_half_of_the_doorway()
    {
        Assert.Equal(Kit.Frame.Depth, Kit.WallDepth);
        Assert.Equal(Kit.WallDepth, Kit.FrameLining);
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void A_kits_framed_doorway_leaves_a_giant_its_92_by_156(string name)
    {
        RoomKit kit = KitNamed(name);

        Assert.True(kit.ClearWidth >= RoomKit.GiantWidth && kit.ClearHeight >= RoomKit.GiantHeight, $"{kit.ClearWidth} by {kit.ClearHeight}");
    }

    [Fact]
    public void A_frame_too_narrow_for_a_giant_is_refused()
    {
        RoomKit tight = Kit with { Frame = Kit.Frame with { Post = 70 } };

        Assert.Throws<ArgumentException>(() => new RoomBuilder("c", tight, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East).Build());
    }

    [Fact]
    public void A_frame_whose_head_no_voxel_height_falls_under_makes_no_kit()
    {
        FrameModel squat = FrameModel.FarmDoorframe with { Top = 180, Head = 2 };

        Assert.Throws<ArgumentException>(() => RoomKit.For("x", 1024, squat, navVoxel: 32));
    }

    [Fact]
    public void Every_door_gets_a_wall_panel_kept_only_where_its_socket_is_capped()
    {
        Room room = Chamber(CellFace.East, CellFace.South);

        foreach (CellFace face in room.Doors)
        {
            var cap = Assert.Single(room.Entities, e => e.Key("room_needs") == "!joined_" + RoomBuilder.WallName(face));
            Assert.Equal(RoomTheme.Grey.Wall.Material, Assert.Single(cap.Brushes).Material);
        }
    }

    [Fact]
    public void A_cap_panel_covers_the_opening_just_in_front_of_the_plug()
    {
        Brush panel = Assert.Single(Assert.Single(Chamber(CellFace.East).Entities, e => e.Key("room_needs") == "!joined_east").Brushes);
        float wall = Kit.Cell - Kit.WallDepth;

        Assert.Equal((wall - RoomBuilder.CapThickness, wall), (panel.Mins.X, panel.Maxs.X));
        Assert.Equal((Kit.DoorLow, Kit.DoorHigh, Kit.Sill, Kit.Head), (panel.Mins.Y, panel.Maxs.Y, panel.Mins.Z, panel.Maxs.Z));
    }

    [Fact]
    public void A_room_built_without_frames_has_none()
    {
        Room room = new RoomBuilder("c", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East).WithoutDoorFrames().Build();

        Assert.DoesNotContain(room.Entities, e => e.ClassName is "func_brush" or "prop_static");
    }
}
