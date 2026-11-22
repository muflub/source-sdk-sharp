extern alias client;

using Xunit;

using DescentMainMenuBrand = client::TF2.Descent.Client.Ui.DescentMainMenuBrand;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>The main menu names the game from the mod's language file: TF2 Descent.</summary>
    [Fact]
    public void The_main_menu_title_is_in_the_language_file_and_reads_tf2_descent()
    {
        var tokens = Tokens();

        Assert.Equal("TF2 Descent", tokens.GetValueOrDefault(new DescentMainMenuBrand().TitleToken));
    }

    /// <summary>
    /// The main menu's backdrop ships in the mod: its material under materials/, naming its own
    /// texture, and that texture a VTF the engine reads (the "VTF\0" mark, a 2:1 picture, the
    /// menu's crop of a wide still).
    /// </summary>
    [Fact]
    public void The_main_menu_backdrop_ships_in_the_mod_as_a_material_and_its_texture()
    {
        string material = new DescentMainMenuBrand().Background!;
        string dir = Path.Combine(RepoRoot(), "game", "mod_tf2_descent", "materials");
        string vmt = File.ReadAllText(Path.Combine(dir, material + ".vmt"));
        byte[] vtf = File.ReadAllBytes(Path.Combine(dir, material + ".vtf"));

        Assert.Contains($"\"$basetexture\" \"{material}\"", vmt, StringComparison.Ordinal);
        Assert.True(vtf is [(byte)'V', (byte)'T', (byte)'F', 0, ..], "the texture starts with the VTF mark");
        int wide = BitConverter.ToUInt16(vtf, 16), tall = BitConverter.ToUInt16(vtf, 18);
        Assert.True(tall > 0 && wide == 2 * tall, $"the picture is {wide}x{tall}, not 2:1");
    }
}
