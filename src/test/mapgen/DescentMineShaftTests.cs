using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The town holds the shared shaft values (plan_descent_mine_levels.md §8,
/// tasks A1 and B3): the opening, the cage, the lantern and the sun are
/// <see cref="MineShaft"/>'s, so level 1 can be built to the same numbers.
/// </summary>
public sealed class DescentMineShaftTests
{
    private static VmfMap Town() => new TF2DescentTown().Build();

    private static (Point Mins, Point Maxs) Bounds(VmfSolid solid)
    {
        var points = solid.Sides.SelectMany(s => new[] { s.P0, s.P1, s.P2 }).ToArray();
        return (new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    private static string Key(VmfEntity e, string key) => e.KeyValues.Single(kv => kv.Key == key).Value;

    [Fact]
    public void The_towns_shaft_opening_is_the_shared_size()
    {
        var trigger = Assert.Single(Town().Entities, e => e.Name == "mine_shaft_trigger");
        var (mins, maxs) = Bounds(Assert.Single(trigger.Solids));

        Assert.Equal((float)MineShaft.Size, maxs.X - mins.X);
        Assert.Equal((float)MineShaft.Size, maxs.Y - mins.Y);
    }

    [Fact]
    public void The_towns_cage_floor_is_the_shared_square_and_its_roof_the_shared_height()
    {
        var boxes = Town().WorldSolids.Select(Bounds).ToArray();

        Assert.Contains(boxes, b => b.Mins.Z == 0 && b.Maxs.Z == 8
                                    && b.Maxs.X - b.Mins.X == MineShaft.CageSize && b.Maxs.Y - b.Mins.Y == MineShaft.CageSize);
        Assert.Contains(boxes, b => b.Maxs.Z == MineShaft.CageHeight && b.Maxs.X - b.Mins.X == MineShaft.CageSize);
    }

    [Fact]
    public void The_towns_sun_has_the_shared_colours_and_its_own_height_and_brightness()
    {
        // The colours are the shared sun's. The town, open to the sky with
        // its street in the buildings' shade and a 924-tall headframe, has
        // its own: a higher sun (TF2DescentTown.SunAngles) and a brighter one
        // (SunBrightness, AmbientBrightness). Level 1 keeps the shared sun.
        var sun = Assert.Single(Town().Entities, e => e.ClassName == "light_environment");
        static string Colour(string light) => light[..light.LastIndexOf(' ')];

        Assert.Equal(
            (Colour(MineShaft.Sun.Light), Colour(MineShaft.Sun.Ambient)),
            (Colour(Key(sun, "_light")), Colour(Key(sun, "_ambient"))));
        Assert.Equal(TF2DescentTown.WithBrightness(MineShaft.Sun.Light, TF2DescentTown.SunBrightness), Key(sun, "_light"));
        Assert.Equal(TF2DescentTown.WithBrightness(MineShaft.Sun.Ambient, TF2DescentTown.AmbientBrightness), Key(sun, "_ambient"));
        Assert.Equal((TF2DescentTown.SunAngles, TF2DescentTown.SunPitch), (Key(sun, "angles"), Key(sun, "pitch")));
    }

    [Fact]
    public void With_brightness_replaces_only_the_last_number()
    {
        Assert.Equal("255 232 196 400", TF2DescentTown.WithBrightness("255 232 196 450", 400));
    }

    [Fact]
    public void Every_town_lantern_gives_the_shared_lanterns_light()
    {
        var lights = Town().Entities.Where(e => e.ClassName == "light").ToArray();

        Assert.NotEmpty(lights);
        Assert.All(lights, l => Assert.Equal(
            (MineShaft.Lantern.Light, "192", "512"),
            (Key(l, "_light"), Key(l, "_fifty_percent_distance"), Key(l, "_zero_percent_distance"))));
    }

    [Fact]
    public void The_towns_lining_lantern_and_sky_are_the_shared_ones()
    {
        Assert.Equal(
            (MineShaft.Lining, MineShaft.LanternModel, MineShaft.Sky),
            (TownPalette.TF2Mining.Wall, TownPalette.TF2Mining.Lantern, TownPalette.TF2Mining.Sky));
    }
}
