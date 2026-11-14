using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A door plug is the theme's own material: the wall's base texture with
/// <c>%compileTrigger</c> (the flag the link finds a plug by) and no
/// <c>%compileNonsolid</c> (a room's compile stays sealed), so a capped socket
/// reads as the wall at no entity's cost. The mod ships the material.
/// </summary>
public sealed class DescentPlugMaterialTests
{
    public static TheoryData<string> Themes => new() { "grey", "mine" };

    private static RoomTheme Theme(string name) => name == "grey" ? RoomTheme.Grey : RoomTheme.Mine;

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
        var (path, text) = Assert.Single(t.OwnMaterials);

        Assert.Equal(t.Plug.Material + ".vmt", path);
        Assert.Contains($"\"$basetexture\" \"{t.Wall.Material}\"", text, StringComparison.Ordinal);
        Assert.Contains("\"%compileTrigger\" \"1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nonsolid", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_mods_copy_of_a_plug_material_is_what_the_generator_writes(string theme)
    {
        var (path, text) = Assert.Single(Theme(theme).OwnMaterials);
        string committed = RepoLayout.Repo([.. DescentRooms.ModContent.Split('/'), "materials", .. path.Split('/')]);

        Assert.True(File.Exists(committed), $"{committed} is missing: run MapGen descent-materials {DescentRooms.ModContent}");
        Assert.Equal(text, File.ReadAllText(committed));
    }

    [Fact]
    public void The_material_writer_writes_every_themes_plug()
    {
        string dir = Path.Combine(Path.GetTempPath(), "descent-materials-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = DescentRooms.WriteMaterials(dir).Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).Order();

            Assert.Equal(["materials/descent/grey_plug.vmt", "materials/descent/mine_plug.vmt"], written);
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
