using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Shapes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A room's preview camera (the review of 2026-11-10): at a player's eye in
/// a corner of the room's body, on floor a player stands on, looking across
/// the body at its focus.
/// </summary>
public sealed class DescentPreviewCameraTests
{
    private static Room Named(string stem) => RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0), stem);

    [Fact]
    public void The_camera_stands_at_a_players_eye_over_walked_floor()
    {
        Room room = Named("pillar_b");
        var (at, _) = PreviewCamera.Of(room);
        var columns = new RoomColumns(room);
        var floor = RoomLint.WalkedFloor(room, columns);

        Assert.Contains(floor, s => s.I == (int)(at.X / RoomLint.Grid) && s.J == (int)(at.Y / RoomLint.Grid) && Math.Abs(s.Z + PreviewCamera.Eye - at.Z) < 0.5f);
    }

    [Fact]
    public void The_camera_stands_in_a_corner_of_the_body()
    {
        // A store's corner sees across its crates to its focus (a pillar room's piers block every corner of pillar_b's).
        Room room = Named("store_b");
        var (at, _) = PreviewCamera.Of(room);
        Rect b = room.Body;
        float toCorner = MathF.Min(MathF.Min(Dist(at, b.X0, b.Y0), Dist(at, b.X1, b.Y0)), MathF.Min(Dist(at, b.X1, b.Y1), Dist(at, b.X0, b.Y1)));

        Assert.True(toCorner < Dist(at, b.CentreX, b.CentreY), $"the camera at ({at}) is nearer the body's middle than a corner");
    }

    [Fact]
    public void The_camera_looks_at_the_rooms_focus()
    {
        Room room = Named("pillar_d");
        var (at, yaw) = PreviewCamera.Of(room);
        float toFocus = MathF.Atan2(room.Focus.Y - at.Y, room.Focus.X - at.X) * 180 / MathF.PI;

        Assert.InRange(Math.Abs(((yaw - toFocus) % 360 + 540) % 360 - 180), 0, 1);
    }

    [Fact]
    public void A_larger_rooms_camera_walks_out_of_a_solid_in_its_corner_toward_the_focus()
    {
        // Brief p15: two of the arenas' views stood inside a bench and a stepping pillar.
        Room hall = RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0).First(r => r.Cells > 1);
        var (corner, _) = PreviewCamera.Of(hall);
        Room blocked = hall with { World = [.. hall.World, Brush.Box(new Point(corner.X - 64, corner.Y - 64, hall.Kit.Sill), new Point(corner.X + 64, corner.Y + 64, hall.Kit.Sill + 160), RoomTheme.Grey.Block)] };

        var (at, _) = PreviewCamera.Of(blocked);

        Assert.True(PreviewCamera.InOpenAir(new MapGen.Descent.Dressing.RoomSolids(hall), corner), "the control: the corner was open");
        Assert.False(PreviewCamera.InOpenAir(new MapGen.Descent.Dressing.RoomSolids(blocked), corner), "the control: the block takes the corner");
        Assert.True(PreviewCamera.InOpenAir(new MapGen.Descent.Dressing.RoomSolids(blocked), at), $"the camera at ({at}) stands in the block");
        Assert.True(Dist(at, hall.Focus.X, hall.Focus.Y) < Dist(corner, hall.Focus.X, hall.Focus.Y), "the camera walked toward the focus");
    }

    private static float Dist(Point p, float x, float y) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
}
