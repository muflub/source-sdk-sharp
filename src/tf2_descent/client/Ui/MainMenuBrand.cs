using SourceSharp.Abi;

using TF2.Client.Ui;

namespace TF2.Descent.Client.Ui;

/// <summary>
/// TF2 Descent on TF2's main menu (<see cref="ITFMainMenuBrand"/>): TF2's logo kept, with Descent's
/// mark under it (user 2026-11-15: "can keep the tf2 logo but add something more to it"), DESCENT in
/// TF2's logo face with the logo's slab, the letters Australium gold
/// (game/mod_tf2_descent/materials/descent/ui/menu_mark.vmt); its own backdrop, a still of the town
/// (menu_background.vmt), in place of TF2's holiday art and class portrait; and the buttons that do
/// nothing for a hero left off: FIND A GAME (TF2's server browser), ITEMS (TF2's loadout, which Descent
/// keeps closed, HE1: the inventory is its own window), REPORT PLAYER and REQUEST COACH (Steam and
/// matchmaking services Descent has none of).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMainMenuBrand)])]
public sealed class DescentMainMenuBrand : ITFMainMenuBrand
{
    /// <summary>The mark's material, under the mod's materials/.</summary>
    public const string MarkMaterial = "descent/ui/menu_mark";

    /// <summary>The backdrop's material, under the mod's materials/.</summary>
    public const string BackgroundMaterial = "descent/ui/menu_background";

    public string? Mark => MarkMaterial;

    public string? Background => BackgroundMaterial;

    public TFMainMenuButtons Hidden =>
        TFMainMenuButtons.FindAGame | TFMainMenuButtons.Items | TFMainMenuButtons.ReportPlayer | TFMainMenuButtons.RequestCoach;
}
