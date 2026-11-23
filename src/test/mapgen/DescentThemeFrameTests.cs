using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One door frame per theme (the review of 0c79c6fc2): the Collar keeps the farm frame model, the
/// three levels below it frame their doors with brushes of their own (a steel channel, a timber set,
/// a heavy steel frame with the hazard stripe on its head), each one non-solid <c>func_brush</c> per
/// socket kept only where the socket is joined, leaving a giant's way through.
/// </summary>
public sealed class DescentThemeFrameTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Chamber(RoomTheme theme, params CellFace[] doors) =>
        new RoomBuilder("c", Kit, theme).WithHeight(512).WithDoors(doors).Build();

    private static RoomEntity[] FramesOf(Room room) =>
        [.. room.Entities.Where(e => e.ClassName == "func_brush" && (e.Key("room_needs") ?? "").StartsWith("joined_", StringComparison.Ordinal))];

    [Fact]
    public void The_collar_keeps_the_farm_frame_model()
    {
        Assert.Null(ActOne.Collar.FrameBrushes);
    }

    [Fact]
    public void The_three_levels_below_the_collar_each_have_a_frame_of_their_own()
    {
        BrushFrame?[] frames = [ActOne.Haulage.FrameBrushes, ActOne.TimberStopes.FrameBrushes, ActOne.Mill.FrameBrushes];

        Assert.All(frames, f => Assert.NotNull(f));
        Assert.Equal(3, frames.Distinct().Count());
    }

    [Fact]
    public void A_brush_frame_is_one_func_brush_per_socket_kept_only_where_it_is_joined()
    {
        Room room = Chamber(ActOne.TimberStopes, CellFace.West, CellFace.North);

        Assert.Equal(["joined_north", "joined_west"], FramesOf(room).Select(e => e.Key("room_needs")!).Order());
    }

    [Fact]
    public void A_brush_framed_door_places_no_frame_model()
    {
        Room room = Chamber(ActOne.Mill, CellFace.West, CellFace.East);

        Assert.DoesNotContain(room.Entities, e => e.Key("model") == Kit.Frame.Model);
    }

    [Fact]
    public void A_brush_frame_is_never_solid()
    {
        Assert.All(FramesOf(Chamber(ActOne.Haulage, CellFace.West)), e => Assert.Equal("1", e.Key("Solidity")));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_brush_framed_room_lints_clean(int depth)
    {
        Assert.DoesNotContain(RoomLint.Check(Chamber(DescentRooms.ThemeFor(depth), CellFace.West, CellFace.East)), f => f.Rule == RoomLint.FrameClear);
    }

    [Fact]
    public void A_frame_narrowing_the_opening_under_a_giant_is_refused()
    {
        // Posts 70 wide leave 68 of the 208 opening, under a giant's 92.
        RoomTheme narrow = ActOne.Mill with { FrameBrushes = ActOne.Mill.FrameBrushes! with { PostWidth = 70 } };

        Assert.Contains(RoomLint.CheckFrameClear(Chamber(narrow, CellFace.West)), f => f.Message.Contains("giant", StringComparison.Ordinal));
    }

    [Fact]
    public void Haulages_frame_lays_planks_across_the_threshold()
    {
        Room room = Chamber(ActOne.Haulage, CellFace.West);
        var planks = FramesOf(room).Single().Brushes.Where(b => b.Material == ActOne.Haulage.FrameBrushes!.Crossing!.Value.Material).ToArray();

        Assert.Equal(3, planks.Length);
        Assert.All(planks, b => Assert.True(b.Mins.Z == Kit.Sill && b.Maxs.Z <= Kit.Sill + 2 && b.Mins.X >= Kit.WallDepth - 1, $"{b.Mins}-{b.Maxs}"));
    }

    [Fact]
    public void The_mills_frame_carries_the_hazard_stripe_on_its_head()
    {
        Room room = Chamber(ActOne.Mill, CellFace.West);

        Assert.Contains(FramesOf(room).Single().Brushes, b => b.Material == ActOne.Mill.Hazard!.Value.Material && b.Mins.Z >= Kit.Head);
    }

    [Fact]
    public void The_timber_set_caps_its_posts_past_them_along_the_wall()
    {
        Room room = Chamber(ActOne.TimberStopes, CellFace.West);
        int over = ActOne.TimberStopes.FrameBrushes!.CapOverhang;

        Assert.Contains(FramesOf(room).Single().Brushes, b => b.Mins.Z == Kit.Head && b.Maxs.Y - b.Mins.Y == Kit.DoorWidth + 2 * over);
    }
}
