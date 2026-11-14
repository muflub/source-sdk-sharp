using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The frame in every socket (task A15): the link drops a joined socket's
/// plug and the faces that touched it, so each room lines its half of the
/// doorway with a frame the link keeps only where the socket is joined.
/// </summary>
public sealed class DescentDoorFrameTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Chamber(params CellFace[] doors) =>
        new RoomBuilder("c", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(doors).Build();

    private static RoomEntity FrameOf(Room room, CellFace face) =>
        Assert.Single(room.Entities, e => e.ClassName == "func_brush" && e.Key("room_needs") == "joined_" + RoomBuilder.WallName(face));

    [Fact]
    public void Every_door_gets_one_frame_kept_only_where_its_socket_is_joined()
    {
        Room room = Chamber(CellFace.East, CellFace.West, CellFace.North);

        Assert.Equal(3, room.Entities.Count(e => e.ClassName == "func_brush" && e.Key("room_needs")!.StartsWith("joined_", StringComparison.Ordinal)));
        foreach (CellFace face in room.Doors)
            Assert.NotEmpty(FrameOf(room, face).Brushes);
    }

    [Fact]
    public void A_frame_lines_its_own_half_of_the_doorway_from_the_cell_face_to_proud_of_the_wall()
    {
        var frame = FrameOf(Chamber(CellFace.East), CellFace.East);
        float c = Kit.Cell, inner = c - Kit.WallDepth - Kit.Frame.Reveal;

        Assert.All(frame.Brushes, b => Assert.Equal((inner, c), (b.Mins.X, b.Maxs.X)));
    }

    [Fact]
    public void A_frame_covers_the_openings_sides_sill_and_head()
    {
        var b = FrameOf(Chamber(CellFace.North), CellFace.North).Brushes;

        Assert.Contains(b, x => x.Mins.X == Kit.DoorLow && x.Mins.Z == Kit.Sill && x.Maxs.Z == Kit.Head);
        Assert.Contains(b, x => x.Maxs.X == Kit.DoorHigh && x.Mins.Z == Kit.Sill && x.Maxs.Z == Kit.Head);
        Assert.Contains(b, x => x.Maxs.Z == Kit.Head && x.Maxs.X - x.Mins.X == Kit.DoorWidth - 2 * Kit.Frame.Post);
        Assert.Contains(b, x => x.Mins.Z == Kit.Sill && x.Maxs.Z == Kit.Sill + Kit.Frame.Sill);
    }

    [Fact]
    public void A_frame_leaves_a_giant_robots_92_by_156_clear()
    {
        var frame = FrameOf(Chamber(CellFace.West), CellFace.West);
        float m = Kit.Cell / 2f, sillTop = Kit.Sill + Kit.Frame.Sill;
        Point mins = new(0, m - RoomKit.GiantWidth / 2f, sillTop), maxs = new(Kit.WallDepth, m + RoomKit.GiantWidth / 2f, sillTop + RoomKit.GiantHeight);

        Assert.DoesNotContain(frame.Brushes, b => b.Overlaps(mins, maxs));
        // The positive arm: a box as wide as the whole opening does touch the posts.
        Assert.Contains(frame.Brushes, b => b.Overlaps(mins with { Y = Kit.DoorLow }, maxs with { Y = Kit.DoorHigh }));
    }

    [Fact]
    public void A_kit_whose_frame_leaves_less_than_a_giant_is_refused()
    {
        RoomKit tight = Kit with { Frame = Kit.Frame with { Post = 90 } };

        Assert.Throws<ArgumentException>(() => new RoomBuilder("c", tight, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East).Build());
    }

    [Fact]
    public void The_frames_inner_faces_are_lifted_off_black_by_minlight()
    {
        Assert.Equal(Kit.Frame.MinLight.ToString(System.Globalization.CultureInfo.InvariantCulture), FrameOf(Chamber(CellFace.East), CellFace.East).Key("_minlight"));
        Assert.True(Kit.Frame.MinLight > 0);
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

        Assert.Equal((wall - Kit.Frame.Cap, wall), (panel.Mins.X, panel.Maxs.X));
        Assert.Equal((Kit.DoorLow, Kit.DoorHigh, Kit.Sill, Kit.Head), (panel.Mins.Y, panel.Maxs.Y, panel.Mins.Z, panel.Maxs.Z));
    }

    [Fact]
    public void A_room_built_without_frames_has_none()
    {
        Room room = new RoomBuilder("c", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East).WithoutDoorFrames().Build();

        Assert.DoesNotContain(room.Entities, e => e.ClassName == "func_brush");
    }
}
