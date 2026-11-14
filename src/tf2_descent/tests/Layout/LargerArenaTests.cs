using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// The boss arenas as rooms larger than one cell (plan section 3.2): the codec spells the sockets
/// a larger room chose, and the boss row lays the arena's square with the arrival and antechamber
/// on its west socket's row and the exit on its east socket's.
/// </summary>
public sealed class LargerArenaTests
{
    private static RoomInfo R(string name, int depth, params string[] tags) => new(name, tags, 1, depth, depth);

    private static List<RoomInfo> Pool(int depth, string arena) =>
    [
        R("arrival_cage-e-c", depth, "start"),
        R("antechamber_a-ew-c", depth),
        R(arena, depth, "boss_arena"),
        R("exit_collapse-e-c", depth, "stairs"),
    ];

    [Fact]
    public void A_larger_rooms_chosen_sockets_ride_in_its_doors()
    {
        RoomShape shape = RoomName.Parse("arena_b-e1w1-s-3x3-j.b2");

        Assert.True(shape.HasSocket(Doors.East, 1) && shape.HasSocket(Doors.West, 1));
        Assert.False(shape.HasSocket(Doors.East, 0) || shape.HasSocket(Doors.West, 2) || shape.HasSocket(Doors.North, 1));
    }

    [Theory]
    [InlineData("arena_b-e1w1-s-3x3-j.b2")]
    [InlineData("arena_a-e1w0-h-2x2")]
    [InlineData("arena_c-w1-h-4x4")]
    [InlineData("hall_x-ne0s-h-2x2")]
    public void A_name_with_chosen_sockets_spells_back_the_same(string name)
    {
        Assert.Equal(name, RoomName.Parse(name).Name);
    }

    [Theory]
    [InlineData("drift_a-e0w-l")]
    [InlineData("arena_a-e01w-h-2x2")]
    [InlineData("arena_a-e2w-h-2x2")]
    [InlineData("arena_a-e10w-h-2x2")]
    [InlineData("arena_a-0ew-h-2x2")]
    public void Socket_numbers_the_codec_cannot_hold_are_refused(string name)
    {
        // A room of one cell, a side numbering every edge (spelled bare), an edge past the side, out of order, a number before a side.
        Assert.False(RoomName.TryParse(name, out _, out _));
    }

    [Fact]
    public void A_larger_arena_stands_unturned_with_the_arrival_on_its_west_sockets_row()
    {
        LevelPlan plan = new LevelLayout().Generate(10, 1, Pool(10, "arena_b-e1w1-s-3x3-j.b2")).Plan!;
        Placement arena = plan.Placements.Single(p => p.Role == "arena"), up = plan.Placements.Single(p => p.Role == "up");

        Assert.Equal((0, 3, 3), (arena.Rotation, plan.Rows, arena.Column - up.Column - 0 + 1));
        Assert.Equal(arena.Row + 1, up.Row);
    }

    [Fact]
    public void A_larger_arenas_exit_stands_off_its_east_socket()
    {
        LevelPlan plan = new LevelLayout().Generate(5, 2, Pool(5, "arena_a-e1w0-h-2x2-j.s2.b1")).Plan!;
        Placement arena = plan.Placements.Single(p => p.Role == "arena"), down = plan.Placements.Single(p => p.Role == "down");

        Assert.Equal((arena.Row + 1, arena.Column + 2), (down.Row, down.Column));
    }

    [Fact]
    public void A_larger_arena_row_joins_every_room_and_caps_only_the_arenas_other_edges()
    {
        LayoutResult result = new LevelLayout().Generate(10, 3, Pool(10, "arena_b-e1w1-s-3x3-j.b2"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal((4, 3, 0, 0, 0), (result.Report!.Rooms, result.Report.Joints, result.Report.MultiCellCaps, result.Report.CappedDoors, result.Report.WastedDoors));
    }

    [Fact]
    public void The_bottom_larger_arena_has_no_way_down()
    {
        var pool = Pool(15, "arena_c-w1-h-4x4").Where(r => !r.Tags.Contains("stairs")).ToList();
        LayoutResult result = new LevelLayout().Generate(15, 1, pool);

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain(result.Plan!.Placements, p => p.Role == "down");
        Assert.Equal(4, result.Plan.Rows);
    }

    [Fact]
    public void A_larger_arena_without_a_west_socket_is_refused()
    {
        LayoutResult result = new LevelLayout().Generate(10, 1, Pool(10, "arena_b-ne1-s-3x3"));

        Assert.False(result.Ok);
        Assert.Contains("west socket", result.Error, StringComparison.Ordinal);
    }
}
