using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Shapes;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Rooms that do not fill their cell (owner ruling 2026-11-10): an outlined
/// room's shell, its sky ceiling and water, the role overrides the stations
/// use, the brushes that bend an outline, and the ways in and down by depth.
/// </summary>
public sealed class DescentOutlineRoomTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private static readonly RoomKit Kit = RoomKit.Normal;
    private static readonly float T = Kit.WallDepth, C = Kit.Cell, M = Kit.Cell / 2f;

    private static RoomBuilder L() => new RoomBuilder("l", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West, CellFace.North)
        .WithOutline(new Rect(T, M - 128, M + 128, M + 128), new Rect(M - 128, M - 128, M + 128, C - T));

    private static bool Solid(Room room, float x, float y, float z) =>
        room.World.Any(b => b.Contains(new Point(x, y, z)) && b.Material != RoomTheme.Grey.Plug.Material);

    [Fact]
    public void An_outlined_rooms_cell_outside_its_outline_is_wall()
    {
        Room room = L().Build();

        Assert.True(Solid(room, 800, 200, 100));
        Assert.True(Solid(room, 800, 800, 100));
    }

    [Fact]
    public void An_outlined_rooms_outline_is_open()
    {
        Room room = L().Build();

        Assert.False(Solid(room, 200, M, 100));
        Assert.False(Solid(room, M, 900, 100));
    }

    [Fact]
    public void An_outline_that_leaves_a_doors_approach_closed_is_refused()
    {
        var b = new RoomBuilder("l", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East)
            .WithOutline(new Rect(T, M - 128, M, M + 128));

        Assert.Throws<ArgumentException>(() => b.Build());
    }

    [Fact]
    public void An_outline_reaching_into_the_cells_walls_is_refused()
    {
        var b = new RoomBuilder("l", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West).WithOutline(new Rect(0, M - 128, M, M + 128));

        Assert.Throws<ArgumentException>(() => b.Build());
    }

    [Fact]
    public void An_outlined_room_lints_sealed()
    {
        Assert.Empty(RoomLint.CheckSealed(L().Build()));
    }

    [Fact]
    public void A_sky_ceiling_is_sky_over_its_area_and_slab_elsewhere()
    {
        Room room = L().WithSkyCeiling(Rect.Around(M, M, 192, 192)).Build();

        Assert.True(room.HasSky);
        var sky = Assert.Single(room.World, b => b.Material == RoomBuilder.SkyMaterial);
        Assert.Equal((192f, 192f, (float)room.Height), (sky.Maxs.X - sky.Mins.X, sky.Maxs.Y - sky.Mins.Y, sky.Maxs.Z));
        Assert.DoesNotContain(room.World, b => b.Material == RoomTheme.Grey.Ceiling.Material && b.Contains(new Point(M, M, room.Height - 1)));
    }

    [Fact]
    public void A_library_with_a_sky_room_carries_the_towns_sun()
    {
        Room room = L().WithSkyCeiling(Rect.Around(M, M, 192, 192)).Build();
        var map = RoomCatalogue.Library(Kit, [room]).Build();

        var sun = Assert.Single(map.Entities, e => e.ClassName == "light_environment");
        Assert.Equal(MineShaft.Sun.Light, sun.KeyValues.Single(kv => kv.Key == "_light").Value);
    }

    [Fact]
    public void A_library_without_a_sky_room_has_no_sun()
    {
        var map = RoomCatalogue.Library(Kit, [L().Build()]).Build();

        Assert.DoesNotContain(map.Entities, e => e.ClassName == "light_environment");
    }

    [Fact]
    public void Water_is_kept_out_of_the_rooms_solids_and_written_to_its_library()
    {
        Room room = L().WithWater(new Rect(64, M - 64, 256, M + 64), Kit.Sill, Kit.Sill + 32, PoolShape.WaterMaterial).Build();

        Assert.Single(room.Water);
        Assert.DoesNotContain(room.World, b => b.Material == PoolShape.WaterMaterial);
        Assert.Contains(PoolShape.WaterMaterial, RoomCatalogue.Library(Kit, [room]).Build().Write(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_transition_box_and_a_button_stand_where_the_room_puts_them()
    {
        Room room = L().WithTransitionBox(new Point(100, 450, Kit.Sill), new Point(200, 550, Kit.Sill + 64))
            .WithButton(new Point(220, 500, Kit.Sill + 40), new Point(228, 524, Kit.Sill + 60))
            .WithRole(RoomRole.Up, new Point(300, M, Kit.Sill), 0).Build();

        var volume = Assert.Single(room.Entities, e => e.ClassName == "trigger_room_transition");
        var button = Assert.Single(room.Entities, e => e.ClassName == "func_button");
        Assert.Equal(new Point(100, 450, Kit.Sill), volume.Brushes[0].Mins);
        Assert.Equal(new Point(220, 500, Kit.Sill + 40), button.Brushes[0].Mins);
    }

    [Fact]
    public void A_down_room_with_a_walked_trigger_fires_from_it_and_has_no_button()
    {
        Room room = L().WithHallTrigger(new Point(100, 450, Kit.Sill), new Point(200, 550, Kit.Sill + 128))
            .WithRole(RoomRole.Down, new Point(300, M, Kit.Sill), 0).Build();

        Assert.DoesNotContain(room.Entities, e => e.ClassName == "func_button");
        Assert.Equal(new Point(100, 450, Kit.Sill), Assert.Single(room.Entities, e => e.ClassName == "trigger_multiple").Brushes[0].Mins);
    }

    [Fact]
    public void A_column_refuses_a_clockwise_footprint()
    {
        Assert.Throws<ArgumentException>(() => Brush.Column([(0, 0), (0, 10), (10, 0)], 0, 10, "x"));
        Assert.NotNull(Brush.Column([(0, 0), (10, 0), (0, 10)], 0, 10, "x"));
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    public void A_spike_is_a_closed_convex_brush_pointing_either_way(float tip, float bottom)
    {
        Brush spike = Brush.Spike(50, 50, 20, 6, tip, bottom, "x");

        Assert.True(spike.Contains(new Point(50, 50, (tip + bottom) / 2)));
        Assert.False(spike.Contains(new Point(50 + 19, 50, tip)));
    }

    [Fact]
    public void A_rounded_corner_leaves_the_corner_solid_and_the_middle_open()
    {
        var b = new RoomBuilder("r", Kit, RoomTheme.Grey).WithHeight(512);
        var r = new Rect(100, 100, 900, 900);
        var fan = b.RoundCorner(r, Corner.SouthWest, 200, 4).ToArray();

        Assert.Contains(fan, x => x.Contains(new Point(110, 110, 100)));
        Assert.DoesNotContain(fan, x => x.Contains(new Point(300, 300, 100)));
    }

    public static TheoryData<int> NormalDepths => [1, 2, 3, 4, 6, 7, 8, 9, 11, 12, 13, 14];

    [Theory]
    [MemberData(nameof(NormalDepths))]
    public void Every_normal_depth_has_a_way_in_and_a_way_down(int depth)
    {
        var rooms = libs.Rooms(depth, 0);

        Assert.Contains(rooms, r => r.Role == RoomRole.Up);
        Assert.Contains(rooms, r => r.Role == RoomRole.Down);
    }

    [Theory]
    [InlineData(1, "arrival_shaft")]
    [InlineData(3, "arrival_cage")]
    [InlineData(8, "arrival_rubble")]
    [InlineData(13, "arrival_chute")]
    public void A_depth_draws_its_way_in_from_its_acts_station(int depth, string family)
    {
        var ways = libs.Rooms(depth, 0).Where(r => r.Entry.Tags.Contains("start")).Select(r => r.Name).ToArray();

        Assert.NotEmpty(ways);
        Assert.All(ways, n => Assert.StartsWith(family, n, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2, "exit_cage")]
    [InlineData(7, "exit_")]
    [InlineData(12, "exit_")]
    public void A_depth_draws_its_way_down_from_its_acts_exits(int depth, string family)
    {
        var ways = libs.Rooms(depth, 0).Where(r => r.Role == RoomRole.Down).Select(r => r.Name).ToArray();

        Assert.NotEmpty(ways);
        Assert.All(ways, n => Assert.StartsWith(family, n, StringComparison.Ordinal));
        if (depth > 4)
            Assert.DoesNotContain(ways, n => n.StartsWith("exit_cage", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bottom_has_no_way_down()
    {
        Assert.DoesNotContain(libs.Rooms(15, 0), r => r.Role == RoomRole.Down);
    }

    [Fact]
    public void Level_ones_arrival_is_the_towns_shaft_open_to_the_sky_over_the_towns_cage()
    {
        var shafts = libs.Rooms(1, 0).Where(r => r.Name.StartsWith("arrival_shaft", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(shafts);
        Assert.All(shafts, r =>
        {
            var sky = Assert.Single(r.World, b => b.Material == RoomBuilder.SkyMaterial);
            Assert.Equal(((float)MineShaft.Size, (float)MineShaft.Size), (sky.Maxs.X - sky.Mins.X, sky.Maxs.Y - sky.Mins.Y));
            Assert.Contains(r.World, b => b.Material == MineShaft.Floor && b.Maxs.X - b.Mins.X == MineShaft.CageSize && b.Maxs.Y - b.Mins.Y == MineShaft.CageSize);
            Assert.Contains(r.World, b => b.Material == MineShaft.Lining);
        });
    }

    [Fact]
    public void Every_way_in_has_four_spawns_round_its_arrival()
    {
        var ways = libs.Rooms(3, 0).Where(r => r.Role == RoomRole.Up).ToArray();

        Assert.All(ways, r => Assert.Equal(4, r.Markers.Count(m => m.Kind == MarkerKind.Spawn)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void A_boss_arena_is_lit_over_all_its_floor(int depth)
    {
        Room arena = Assert.Single(libs.Rooms(depth, 0), r => r.Entry.Tags.Contains("boss_arena"));

        Assert.Empty(RoomLint.CheckLit(arena, new RoomColumns(arena)));
        Assert.True(arena.Markers.Count(m => m.Kind == MarkerKind.PackSpawn) >= 2);
    }

    [Fact]
    public void A_ways_in_and_downs_two_door_variants_open_both_ways()
    {
        foreach (int depth in (int[])[2, 7, 12])
        {
            var roles = libs.Rooms(depth, 0).Where(r => r.Role != RoomRole.None).ToArray();
            Assert.Contains(roles, r => r.Role == RoomRole.Up && LayoutNames.DoorsOf(r.Doors) == (Doors.West | Doors.East));
            Assert.Contains(roles, r => r.Role == RoomRole.Down && LayoutNames.DoorsOf(r.Doors) == (Doors.North | Doors.East));
        }
    }
}
