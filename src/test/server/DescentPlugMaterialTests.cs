using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A door plug is the theme's own material: the wall's base texture with
/// <c>%compileTrigger</c> (the flag the link finds a plug by) and no
/// <c>%compileNonsolid</c> (a room's compile stays sealed), drawn by nothing
/// (<c>$no_draw</c>): the link leaves a joined socket's plug faces in the map,
/// and the engine draws them. The mod ships the material.
/// </summary>
public sealed class DescentPlugMaterialTests
{
    public static TheoryData<string> Themes => new(RoomTheme.All.Select(t => t.Name));

    private static RoomTheme Theme(string name) => RoomTheme.All.Single(t => t.Name == name);

    [Theory]
    [MemberData(nameof(Themes))]
    public void A_themes_plug_is_its_own_material_in_the_descent_folder(string theme)
    {
        Assert.Equal($"descent/{theme}_plug", Theme(theme).Plug.Material);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void A_plug_material_draws_the_walls_texture_as_a_solid_trigger(string theme)
    {
        RoomTheme t = Theme(theme);
        var (path, text) = Assert.Single(t.OwnMaterials, m => m.Path.EndsWith("_plug.vmt", StringComparison.Ordinal));

        Assert.Equal(t.Plug.Material + ".vmt", path);
        Assert.Contains($"\"$basetexture\" \"{t.Wall.Material}\"", text, StringComparison.Ordinal);
        Assert.Contains("\"%compileTrigger\" \"1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nonsolid", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void A_plug_material_is_drawn_by_nothing_so_a_joined_doorway_is_open_in_game(string theme)
    {
        RoomTheme t = Theme(theme);
        var (_, text) = Assert.Single(t.OwnMaterials, m => m.Path == t.Plug.Material + ".vmt");

        Assert.Contains("\"$no_draw\" \"1\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_mods_copy_of_a_themes_own_materials_is_what_the_generator_writes(string theme)
    {
        Assert.NotEmpty(Theme(theme).OwnMaterials);
        foreach (var (path, text) in Theme(theme).OwnMaterials)
        {
            string committed = RepoLayout.Repo([.. DescentRooms.ModContent.Split('/'), "materials", .. path.Split('/')]);

            Assert.True(File.Exists(committed), $"{committed} is missing: run MapGen descent-materials {DescentRooms.ModContent}");
            Assert.Equal(text, File.ReadAllText(committed));
        }
    }

    [Fact]
    public void The_material_writer_writes_every_themes_plug_and_own_materials()
    {
        string dir = Path.Combine(Path.GetTempPath(), "descent-materials-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = DescentRooms.WriteMaterials(dir).Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).Order();

            Assert.Equal(
                ["materials/descent/breakthrough_plug.vmt", "materials/descent/chapel_plug.vmt", "materials/descent/collar_plug.vmt",
                 "materials/descent/dripstone_plug.vmt", "materials/descent/dripstone_water.vmt", "materials/descent/grey_plug.vmt",
                 "materials/descent/haulage_plug.vmt", "materials/descent/mill_plug.vmt", "materials/descent/mine_plug.vmt",
                 "materials/descent/stopes_plug.vmt", "materials/descent/sunken_bed.vmt", "materials/descent/sunken_plug.vmt",
                 "materials/descent/sunken_water.vmt"],
                written);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_rooms_plugs_are_drawn_in_its_themes_plug_material()
    {
        Room room = new RoomBuilder("c_a", RoomKit.Normal, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East, CellFace.North).Build();

        Assert.Equal(2, room.World.Count(b => b.Material == "descent/grey_plug"));
    }
}
