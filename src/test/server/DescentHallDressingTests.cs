using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dressing of the great halls (the review of 0c79c6fc2: a hall is the level's set piece): track
/// through level 2's yard kept only where its sockets are joined, an overhead crane at levels 2 and 4,
/// a machine or a heap on the plinth, level 3's headframe, a run of pipe at height, and no dressing
/// brush on an internal cell face.
/// </summary>
public sealed class DescentHallDressingTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private Room Hall(int depth, string variant) => libs.Rooms(depth, 0).Single(r => r.Name.StartsWith(variant + "-", StringComparison.Ordinal));

    private static IEnumerable<RoomEntity> Role(Room room, RoomDresser.Role role) =>
        room.Entities.Where(e => e.Key(RoomDresser.RoleKey) == role.ToString());

    [Fact]
    public void A_yard_halls_track_is_kept_only_where_its_socket_is_joined()
    {
        var rails = Role(Hall(2, "hall_b"), RoomDresser.Role.Rail).ToArray();

        Assert.NotEmpty(rails);
        Assert.All(rails, e => Assert.Contains("joined_", e.Key("room_needs") ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public void A_yard_hall_lays_track_in_from_every_east_and_west_socket()
    {
        var needs = Role(Hall(2, "hall_b"), RoomDresser.Role.Rail).Select(e => e.Key("room_needs")).ToHashSet();

        Assert.All((string[])["west0", "west1", "east0", "east1"], s => Assert.Contains("joined_" + s, needs));
    }

    [Fact]
    public void Two_half_runs_meeting_in_a_yard_hall_end_in_a_stop_only_when_the_other_half_is_not_there()
    {
        var stops = Role(Hall(2, "hall_b"), RoomDresser.Role.Rail).Where(e => e.Key("model") == ActOne.TrackStop.Model).Select(e => e.Key("room_needs")).ToHashSet();

        Assert.Contains("joined_west0,!joined_east0", stops);
        Assert.Contains("joined_east0,!joined_west0", stops);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_tall_hall_has_an_overhead_crane_with_its_hook(int depth)
    {
        Room hall = Hall(depth, "hall_a");
        float z0 = hall.Kit.Sill + DescentRooms.ThemeFor(depth).Gantry!.Height;
        var girders = hall.World.Where(b => b.Mins.Z == z0 && b.Maxs.X - b.Mins.X > 1800).ToArray();

        Assert.Equal(2, girders.Length);
        Assert.Contains(hall.Entities, e => e.Key("model") == ActOne.CraneHook.Model);
    }

    [Fact]
    public void The_mills_yard_hall_stands_a_machine_on_its_plinth()
    {
        Room hall = Hall(4, "hall_b");
        var machine = Role(hall, RoomDresser.Role.Centre).Single();

        Assert.Equal((hall.Focus.X, hall.Focus.Y), (machine.Origin!.Value.X, machine.Origin.Value.Y));
        Assert.Contains(machine.Key("model"), (string[])[ActOne.Generator.Model, ActOne.GeneratorMachine.Model]);
    }

    [Fact]
    public void The_haulage_yard_hall_heaps_ore_on_its_plinth()
    {
        Room hall = Hall(2, "hall_b");

        Assert.Equal(ActOne.OreHeap.Model, Role(hall, RoomDresser.Role.Centre).Single().Key("model"));
    }

    [Fact]
    public void The_timber_levels_yard_hall_raises_a_headframe_over_its_plinth()
    {
        Room hall = Hall(3, "hall_b");
        Point f = hall.Focus;
        // Four legs of the trim, a headframe's reach from the focus on each diagonal, up from the plinth.
        var legs = hall.World.Where(b => b.Material == ActOne.TimberStopes.TrimOrBlock.Material && b.Maxs.Z - b.Mins.Z > 256
                                         && Math.Abs(Math.Abs((b.Mins.X + b.Maxs.X) / 2 - f.X) - 232) < 1 && Math.Abs(Math.Abs((b.Mins.Y + b.Maxs.Y) / 2 - f.Y) - 232) < 1);

        Assert.Equal(4, legs.Count());
    }

    [Fact]
    public void A_tall_room_runs_a_pipe_along_a_wall_at_height()
    {
        int height = ActOne.Haulage.Props.WallRunHeight;
        float sill = RoomKit.Normal.Sill;

        Assert.Contains(libs.Rooms(2, 0).Where(r => r.Height >= 768).SelectMany(r => Role(r, RoomDresser.Role.CeilingRun)),
            e => Math.Abs(e.Origin!.Value.Z + ActOne.TwinPipe.Mins.Z - (sill + height)) < 0.5f);
    }

    [Fact]
    public void A_dressing_brush_on_a_larger_rooms_internal_cell_face_is_refused()
    {
        var (built, dressed, theme) = libs.Dressed(2, 0).First(r => r.Built.Cells > 1);
        Room bad = dressed with { World = [.. dressed.World, Brush.Box(new Point(900, 900, 16), new Point(1024, 1000, 40), theme.TrimOrBlock)] };

        Assert.Contains(DressLint.Check(built, bad, theme), f => f.Rule == DressLint.CellFace);
    }
}
