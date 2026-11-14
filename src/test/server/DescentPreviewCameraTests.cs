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

    private static float Dist(Point p, float x, float y) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
}
