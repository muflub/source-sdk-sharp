extern alias client;

using Xunit;

using DescentMainMenuBrand = client::TF2.Descent.Client.Ui.DescentMainMenuBrand;

namespace TF2.Descent.Tests;

// The main menu's art and the window's icon ship in the mod (game/mod_tf2_descent), where the
// engine finds them first on the mod's GAME paths, ahead of TF2's content.
public sealed partial class LanguageTokensTests
{
    /// <summary>A material the brand names and its texture: the .vmt naming it, the VTF mark, and its size.</summary>
    private static (int Wide, int Tall) ShippedMaterial(string material)
    {
        string dir = Path.Combine(RepoRoot(), "game", "mod_tf2_descent", "materials");
        string vmt = File.ReadAllText(Path.Combine(dir, material + ".vmt"));
        byte[] vtf = File.ReadAllBytes(Path.Combine(dir, material + ".vtf"));

        Assert.Contains($"\"$basetexture\" \"{material}\"", vmt, StringComparison.Ordinal);
        Assert.True(vtf is [(byte)'V', (byte)'T', (byte)'F', 0, ..], "the texture starts with the VTF mark");
        return (BitConverter.ToUInt16(vtf, 16), BitConverter.ToUInt16(vtf, 18));
    }

    /// <summary>The backdrop: a 2:1 picture, the menu's crop of a wide still.</summary>
    [Fact]
    public void The_main_menu_backdrop_ships_in_the_mod_as_a_material_and_its_texture()
    {
        var (wide, tall) = ShippedMaterial(new DescentMainMenuBrand().Background!);

        Assert.True(tall > 0 && wide == 2 * tall, $"the picture is {wide}x{tall}, not 2:1");
    }

    /// <summary>The mark under TF2's logo: a translucent 4:1 strip, the logo's own shape.</summary>
    [Fact]
    public void The_main_menu_mark_ships_in_the_mod_as_a_translucent_material_and_its_texture()
    {
        string material = new DescentMainMenuBrand().Mark!;
        var (wide, tall) = ShippedMaterial(material);

        Assert.True(tall > 0 && wide == 4 * tall, $"the mark is {wide}x{tall}, not 4:1");
        Assert.Contains("\"$translucent\" \"1\"",
                        File.ReadAllText(Path.Combine(RepoRoot(), "game", "mod_tf2_descent", "materials", material + ".vmt")),
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// The window's icon: the engine sets it from resource/game-icon.bmp, the first on the GAME paths
    /// (CGame::CreateGameWindow, sys_mainwind.cpp), so the mod's own stands ahead of TF2's. A BMP the
    /// SDL loader reads: "BM", 32 bits a pixel with an alpha mask (BI_BITFIELDS, as TF2's own), square,
    /// 48 to 128 pixels. At 256 the window carried no _NET_WM_ICON at all (SDL_SetWindowIcon through the
    /// system's sdl2-compat on X11, measured 2026-11-15 with TF2's 48 and our 128 set and our 256 not).
    /// </summary>
    [Fact]
    public void The_window_icon_ships_in_the_mod_as_a_square_32_bit_bmp_with_alpha()
    {
        byte[] bmp = File.ReadAllBytes(Path.Combine(RepoRoot(), "game", "mod_tf2_descent", "resource", "game-icon.bmp"));

        Assert.True(bmp is [(byte)'B', (byte)'M', ..], "the icon starts with the BMP mark");
        int wide = BitConverter.ToInt32(bmp, 18), tall = BitConverter.ToInt32(bmp, 22);
        Assert.Equal(32, BitConverter.ToUInt16(bmp, 28));
        Assert.Equal(3u, BitConverter.ToUInt32(bmp, 30));                     // BI_BITFIELDS
        Assert.Equal(0xFF000000u, BitConverter.ToUInt32(bmp, 14 + 52));         // the alpha mask
        Assert.True(wide is >= 48 and <= 128 && wide == Math.Abs(tall), $"the icon is {wide}x{tall}");
    }
}
