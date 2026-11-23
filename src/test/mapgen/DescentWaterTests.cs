using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Act II's water reads as water in game (brief p9): level 8's murky water over its mud read as a mud
/// floor and level 7's pools as flat dark sheets. Each is the theme's own water, reflecting the room
/// (no cubemap needed in an LDR map with none); level 8's floor under it is wet rock the lamp lights.
/// </summary>
public sealed class DescentWaterTests
{
    private static RoomTheme Level(int depth) => DescentRooms.ThemeFor(depth);

    private static Room Built(int depth, ShapeVariants family, string name) =>
        RoomCatalogue.Named(RoomCatalogue.Rooms([new RoomShape(name.Split('_')[0], family)], DescentRooms.KitFor(depth), Level(depth), 0), name);

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void An_act_two_waters_are_its_own_reflecting_water_made_from_a_tf2_one(int depth)
    {
        RoomTheme theme = Level(depth);
        OwnMaterial water = Assert.Single(theme.Extra, m => m.Name == theme.Water);

        Assert.StartsWith("descent/", water.Name, StringComparison.Ordinal);
        Assert.StartsWith("\"Water\"", water.Vmt, StringComparison.Ordinal);
        Assert.Contains("\"$reflecttexture\" \"_rt_WaterReflection\"", water.Vmt, StringComparison.Ordinal);
        Assert.Contains("\"$reflectentities\" 0", water.Vmt, StringComparison.Ordinal);
        Assert.Contains("\"$fogenable\" 1", water.Vmt, StringComparison.Ordinal);
        Assert.Contains(ThemeAssets.Of(theme), a => a.Kind == "water" && a.Asset == water.BasedOn);
    }

    [Fact]
    public void Level_eights_floor_is_wet_rock_made_from_a_tf2_floor_with_no_envmap()
    {
        OwnMaterial floor = Assert.Single(Level(8).Extra, m => m.Name == Level(8).Floor.Material);

        Assert.DoesNotContain("envmap", floor.Vmt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"$basetexture\" \"Nature/rockfloor003a\"", floor.Vmt, StringComparison.Ordinal);
        Assert.Contains(ThemeAssets.Of(Level(8)), a => a.Role == "floor" && a.Asset == "nature/rockfloor003a");
    }

    [Fact]
    public void A_flooded_rooms_water_stands_on_its_floor_and_its_floor_is_the_wet_rock()
    {
        Room built = Built(8, StoreShape.Variants, "store_a");
        Room dressed = RoomDresser.Dress(built, Level(8), 0);

        Assert.NotEmpty(dressed.Water);
        Assert.All(dressed.Water, w => Assert.Equal(built.Kit.Sill, w.Mins.Z));
        Assert.Contains(dressed.World, b => b.Material == "descent/sunken_bed" && b.Maxs.Z == built.Kit.Sill);
    }
}
