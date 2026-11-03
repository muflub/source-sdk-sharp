//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The map-goal hint panel, stock's <c>CHudTeamGoal</c>
/// (tf_hud_teamgoal.cpp) at hudteamgoal.res on hudlayout's HudTeamGoal row
/// (c-160, y65, 320x100): the death-panel background in the local team's
/// colour (the res's teambg_2/3 pair), the role icon at 27,25 and the goal
/// line wrapped beside it. The switch label is stock's hidden arm
/// (<c>SetupGoalPanel</c> :194), so the goal label sits at its y — the
/// move :241-243 makes — over the 320x150 background, which stock lets
/// hang past the 100-tall host.
/// </summary>
public sealed class TeamGoalPanel(ITFTeamGoal goal, IUiTextureLibrary textures) : UiPanel
{
    // The hudlayout row: the panel's own spot; TFHud places it there.
    internal static readonly StockBox Panel = new("c-160", "65", 320, 100);

    // The art boxes, as the .res writes them, against the panel.
    private static class Stock
    {
        // HudTeamGoalBG: 0,0, 320x150, zpos -1 (:4-20).
        public static readonly StockBox Bg = new("0", "0", 320, 150);

        // GoalImage: 27,25, 45x45 (:62-76), the res default hud_icon_attack.
        public static readonly StockBox Image = new("27", "25", 45, 45);

        // GoalLabel at the switch label's y (the :241-243 move), 220x55
        // (:41-60; the wide the PC res names), wrap, north.
        public static readonly StockBox Label = new("80", "25", 220, 55);
    }

    // The res's image names, the .. dropped; teambg_2 red, teambg_3 blue.
    private const string RedBg = "hud/death_panel_red_bg";
    private const string BlueBg = "hud/death_panel_blue_bg";

    // The res default the empty role keeps (:74).
    private const string DefaultIcon = "hud/hud_icon_attack";

    /// <summary>TF_TEAM_RED: what the teambg pair keys on.</summary>
    private const int Red = 2;

    public bool Present => goal.Visible;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        var root = Panel.Rect(StockRes.Screen(ui.Screen));
        var bg = Stock.Bg.In(root);
        var icon = Stock.Image.In(root);
        var label = Stock.Label.In(root);
        bool red = goal.Team == Red;

        // The plate is the art's fallback (the contrast rule), then the art.
        if (StockArt.Missing(textures, red ? RedBg : BlueBg))
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("goal plate");
        StockArt.Draw(ref ui, textures, "goal bg", red ? RedBg : BlueBg,
            bg.Left, bg.Bottom, bg.Wide, bg.Tall);

        string image = goal.Image.Length > 0 ? goal.Image : DefaultIcon;
        if (StockArt.Missing(textures, image))
            ui.Rect(Tone.Surface, Size.Du(icon.Wide), Size.Du(icon.Tall))
                .AtRes(UiAnchor.BottomLeft, icon.Left, icon.Bottom).Layered().Key("goal icon plate");
        StockArt.Draw(ref ui, textures, "goal icon", image,
            icon.Left, icon.Bottom, icon.Wide, icon.Tall);

        ui.Data(goal.Text, Text.Caption, Tone.Text).HudSize()
            .TextSize(TFHudType.ProgressText)   // the 12-tall face: the sentence wraps as stock's 3 lines
            .Width(Size.Du(label.Wide)).Height(Size.Du(label.Tall)).Wrap(label.Wide)
            .AtRes(UiAnchor.BottomLeft, label.Left, label.Bottom).Layered().Key("goal label");
    }
}
