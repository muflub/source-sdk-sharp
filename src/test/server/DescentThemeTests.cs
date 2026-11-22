using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The depths' themes (plan_descent_mine_levels.md task A9, §6, §7): Act
/// I's four levels each look different from the one before, level 1 is the
/// town's continuation, and the depths outside Acts I and II keep the grey through the same
/// path.
/// </summary>
public sealed class DescentThemeTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    public static TheoryData<int> ActOneSteps => [2, 3, 4];

    public static TheoryData<int> GreyDepths => [5, 10, 11, 13, 14, 15];

    private static RoomTheme Level(int depth) => DescentRooms.ThemeFor(depth);

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_has_another_wall_than_the_level_above(int depth)
    {
        Assert.NotEqual(Level(depth - 1).Wall.Material, Level(depth).Wall.Material);
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_has_another_floor_than_the_level_above(int depth)
    {
        Assert.NotEqual(Level(depth - 1).Floor.Material, Level(depth).Floor.Material);
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_has_another_lamp_than_the_level_above(int depth)
    {
        Assert.NotEqual(Level(depth - 1).Lamps!.Fixture.Model, Level(depth).Lamps!.Fixture.Model);
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_shares_no_wall_clutter_with_the_level_above(int depth)
    {
        var above = Level(depth - 1).Props.Wall.Concat(Level(depth - 1).Props.Hung.Select(h => h.Model)).Select(m => m.Model).ToHashSet();

        Assert.DoesNotContain(Level(depth).Props.Wall.Concat(Level(depth).Props.Hung.Select(h => h.Model)), m => above.Contains(m.Model));
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_hangs_its_lamps_no_closer_than_the_level_above(int depth)
    {
        Assert.True(Level(depth).Lamps!.Spacing >= Level(depth - 1).Lamps!.Spacing);
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void A_level_is_wetter_than_the_level_above(int depth)
    {
        Assert.True(Level(depth).Wetness > Level(depth - 1).Wetness);
    }

    [Fact]
    public void Level_one_has_no_fog()
    {
        Assert.False(Level(1).Fog.Enabled);
    }

    [Theory]
    [MemberData(nameof(ActOneSteps))]
    public void Below_level_one_the_fog_closes_in_with_depth(int depth)
    {
        Assert.True(Level(depth).Fog.Enabled);
        if (depth > 2)
            Assert.True(Level(depth).Fog.End < Level(depth - 1).Fog.End);
    }

    [Theory]
    [MemberData(nameof(GreyDepths))]
    public void A_depth_outside_acts_one_and_two_is_still_grey(int depth)
    {
        Assert.Same(RoomTheme.Grey, Level(depth));
    }

    [Fact]
    public void Level_twelve_is_the_grey_with_slime_in_its_pools()
    {
        Assert.Equal(RoomTheme.Grey with { PoolHazard = "slime" }, Level(12));
    }

    [Fact]
    public void Level_ones_way_in_is_drawn_in_the_collars_concrete()
    {
        RoomTheme arrival = Level(1).Arrival!;

        // The review of 2026-11-10: the shaft's foot is level 1's pale concrete, not the town's rock.
        Assert.Equal((ActOne.Collar.Floor, ActOne.Collar.Wall), (arrival.Floor, arrival.Wall));
    }

    [Fact]
    public void Level_ones_way_in_is_lit_in_daylight_white()
    {
        RoomTheme arrival = Level(1).Arrival!;

        Assert.Equal((ActOne.Daylight, ActOne.Daylight), (arrival.Lamps!.Light.Colour, arrival.WorkLamps!.Kind.Light.Colour));
    }

    [Fact]
    public void Level_ones_library_carries_the_towns_sun_and_sky()
    {
        Assert.Equal((MineShaft.Sun, MineShaft.Sky), (Level(1).Sun, Level(1).Sky));
    }

    [Fact]
    public void A_depths_library_writes_its_themes_fog()
    {
        string vmf = DescentRooms.Library(3, []).Build().Write();

        Assert.Contains($"\"fogend\" \"{Level(3).Fog.End}\"", vmf, StringComparison.Ordinal);
    }

    [Fact]
    public void Level_ones_library_switches_its_fog_off()
    {
        string vmf = DescentRooms.Library(1, []).Build().Write();

        Assert.Contains("\"fogenable\" \"0\"", vmf, StringComparison.Ordinal);
    }

    [Fact]
    public void Level_ones_library_writes_the_towns_sun()
    {
        string vmf = DescentRooms.Library(1, []).Build().Write();

        Assert.Contains($"\"_light\" \"{MineShaft.Sun.Light}\"", vmf, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grey_depths_library_has_no_sun()
    {
        string vmf = DescentRooms.Library(2, []).Build().Write();

        Assert.DoesNotContain("light_environment", vmf, StringComparison.Ordinal);
    }

    [Fact]
    public void A_redrawn_room_has_none_of_the_old_themes_surfaces()
    {
        Room room = new RoomBuilder("c_a", RoomKit.Normal, ActOne.Collar).WithHeight(512).WithDoors(CellFace.East).Build();
        RoomTheme from = ActOne.Collar;

        Room redrawn = from.Redraw(room, RoomTheme.Mine);

        var old = new[] { from.Floor, from.Wall, from.Ceiling, from.Plug }.Select(s => s.Material).ToHashSet();
        Assert.DoesNotContain(redrawn.World.SelectMany(b => b.Faces), f => old.Contains(f.Surface.Material));
    }

    [Fact]
    public void A_redrawn_rooms_plugs_are_the_new_themes()
    {
        Room room = new RoomBuilder("c_a", RoomKit.Normal, ActOne.Collar).WithHeight(512).WithDoors(CellFace.East).Build();

        Room redrawn = ActOne.Collar.Redraw(room, RoomTheme.Mine);

        Assert.Single(redrawn.World, b => b.Material == RoomTheme.Mine.Plug.Material);
    }

    [Fact]
    public void Level_ones_way_in_keeps_the_towns_rock_as_one_band_at_its_foot()
    {
        Room start = libs.Rooms(1, 0).First(r => r.Role == RoomRole.Up);
        var rock = start.World.Where(b => b.Material == RoomTheme.Mine.Wall.Material).ToArray();

        Assert.NotEmpty(rock);
        Assert.All(rock, b => Assert.True(b.Maxs.Z <= start.Kit.Sill + Level(1).Arrival!.WallBand, $"rock up to {b.Maxs.Z}"));
    }

    [Fact]
    public void Level_ones_other_rooms_are_built_in_its_own_wall()
    {
        Room drift = libs.Rooms(1, 0).First(r => r.Role == RoomRole.None);

        Assert.Contains(drift.World, b => b.Material == ActOne.Collar.Wall.Material);
    }
}
