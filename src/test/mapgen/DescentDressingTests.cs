using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dressing pass (task A10) on the libraries it dresses: Act I's rooms
/// come out dressed by every role their theme names, keep every dressing
/// rule, and the same seed gives the same bytes; the grey is left alone.
/// </summary>
public sealed class DescentDressingTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    public static TheoryData<int> ActOneDepths => [1, 2, 3, 4];

    private IEnumerable<RoomEntity> Pieces(int depth, string role) =>
        libs.Rooms(depth, 0).SelectMany(r => r.Entities).Where(e => e.Key(RoomDresser.RoleKey) == role);

    [Theory]
    [MemberData(nameof(ActOneDepths))]
    public void Every_dressed_room_of_a_depth_keeps_the_dressing_rules(int depth)
    {
        var findings = libs.Dressed(depth, 0).SelectMany(r => DressLint.Check(r.Built, r.Dressed, r.Theme)).Select(f => f.ToString()).ToArray();

        Assert.True(findings.Length == 0, $"{findings.Length} findings:\n{string.Join("\n", findings.Take(12))}");
    }

    [Theory]
    [MemberData(nameof(ActOneDepths))]
    public void Every_dressed_room_of_a_depth_still_lints_clean(int depth)
    {
        Assert.Empty(libs.Rooms(depth, 0).SelectMany(RoomLint.Check).Select(f => f.ToString()));
    }

    [Theory]
    [MemberData(nameof(ActOneDepths))]
    public void A_depth_hangs_its_themes_lamps_with_their_lights(int depth)
    {
        string fixture = DescentRooms.ThemeFor(depth).Lamps!.Fixture.Model;
        var lamps = Pieces(depth, nameof(RoomDresser.Role.Lamp)).ToArray();

        Assert.Contains(lamps, e => e.Key("model") == fixture);
        Assert.Contains(lamps, e => e.ClassName is "light" or "light_spot");
    }

    [Theory]
    [MemberData(nameof(ActOneDepths))]
    public void Every_room_of_a_themed_depth_has_a_lamp(int depth)
    {
        // The four-door hall's catwalk ring once stood over the only row of wall lamps it was given.
        var dark = libs.Rooms(depth, 0).Where(r => !r.Entities.Any(e => e.ClassName is "light" or "light_spot")).Select(r => r.Name);

        Assert.Empty(dark);
    }

    [Fact]
    public void No_corridor_of_level_one_has_clutter_on_its_floor()
    {
        var corridors = libs.Rooms(1, 0).Where(RoomDresser.IsHallway).ToArray();

        Assert.NotEmpty(corridors);
        Assert.DoesNotContain(corridors.SelectMany(r => r.Entities), e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Floor));
    }

    [Fact]
    public void Haulages_floods_are_spot_lamps()
    {
        Assert.Contains(Pieces(2, nameof(RoomDresser.Role.Lamp)), e => e.ClassName == "light_spot");
    }

    [Theory]
    [InlineData(1, nameof(RoomDresser.Role.Wall))]
    [InlineData(1, nameof(RoomDresser.Role.Corner))]
    [InlineData(1, nameof(RoomDresser.Role.Floor))]
    [InlineData(1, nameof(RoomDresser.Role.CeilingRun))]
    [InlineData(2, nameof(RoomDresser.Role.Rail))]
    [InlineData(2, nameof(RoomDresser.Role.Floor))]
    [InlineData(3, nameof(RoomDresser.Role.Corner))]
    [InlineData(4, nameof(RoomDresser.Role.Wall))]
    [InlineData(4, nameof(RoomDresser.Role.CeilingRun))]
    public void A_depth_places_props_of_each_role_its_theme_names(int depth, string role)
    {
        Assert.NotEmpty(Pieces(depth, role));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_depth_with_sets_stands_them_across_its_drifts(int depth)
    {
        var dressed = libs.Dressed(depth, 0);

        // Two posts of the theme's trim standing on a hallway's floor: a set across it.
        string trim = DescentRooms.ThemeFor(depth).TrimOrBlock.Material;
        Assert.Contains(dressed, r => RoomDresser.IsHallway(r.Built)
                                      && r.Dressed.World.Skip(r.Built.World.Count).Count(b => b.Material == trim && b.Mins.Z == r.Built.Kit.Sill && b.Maxs.Z > r.Built.Kit.Sill + 128) >= 2);
    }

    [Fact]
    public void A_themed_room_keeps_none_of_its_shapes_lights()
    {
        var lights = libs.Rooms(2, 0).SelectMany(r => r.Entities).Where(e => e.ClassName is "light" or "light_spot");

        Assert.All(lights, l => Assert.NotNull(l.Key(RoomDresser.RoleKey)));
    }

    [Fact]
    public void Level_threes_lanterns_are_dynamic_within_its_budget()
    {
        var perRoom = libs.Rooms(3, 0).Select(r => r.Entities.Count(e => e.ClassName == "prop_dynamic")).ToArray();

        Assert.Contains(perRoom, n => n > 0);
        Assert.All(perRoom, n => Assert.True(n <= ActOne.TimberStopes.DynamicBudget));
    }

    [Fact]
    public void The_same_seed_dresses_a_library_byte_for_byte()
    {
        string a = DescentRooms.Library(1, DescentRooms.Rooms(1, 7)).Build().Write();
        string b = DescentRooms.Library(1, DescentRooms.Rooms(1, 7)).Build().Write();

        Assert.Equal(a, b);
    }

    [Fact]
    public void Another_seed_dresses_the_same_room_differently()
    {
        var built = libs.Dressed(1, 0).Select(r => r.Built).Where(r => r.Role == RoomRole.None).ToArray();
        static string Props(Room r) => string.Join(";", r.Entities.Where(e => e.Key(RoomDresser.RoleKey) is not null).Select(e => $"{e.Key("model")}@{e.Origin}"));

        Assert.Contains(built, r => Props(RoomDresser.Dress(r, ActOne.Collar, 7)) != Props(RoomDresser.Dress(r, ActOne.Collar, 8)));
    }

    [Fact]
    public void The_grey_theme_leaves_a_room_as_the_shapes_built_it()
    {
        Room room = libs.Rooms(6, 0)[0];

        Assert.Same(room, RoomDresser.Dress(room, RoomTheme.Grey, 0));
    }
}
