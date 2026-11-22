using SourceSharp.Abi;

using TF2.Client.Ui;

namespace TF2.Descent.Client.Ui;

/// <summary>
/// TF2 Descent on TF2's main menu (<see cref="ITFMainMenuBrand"/>): its title where TF2's logo stood,
/// and its own backdrop, a still of the game shipped in the mod as a material
/// (game/mod_tf2_descent/materials/descent/ui/menu_background.vmt), in place of TF2's holiday art
/// and class portrait.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMainMenuBrand)])]
public sealed class DescentMainMenuBrand : ITFMainMenuBrand
{
    /// <summary>The title's token, in the mod's language file.</summary>
    public const string Title = "#Descent_MainMenu_Title";

    /// <summary>The backdrop's material, under the mod's materials/.</summary>
    public const string BackgroundMaterial = "descent/ui/menu_background";

    // TF2's menu draws no title text now (the seam names a mark): the mark lands in the next commit.
    public string? Mark => null;

    public TFMainMenuButtons Hidden => TFMainMenuButtons.None;

    public string? Background => BackgroundMaterial;
}
