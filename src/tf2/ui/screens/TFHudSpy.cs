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
/// The spy's cloak meter, stock's item-effect meter over the invis watch
/// (tf_hud_itemeffectmeter.cpp) at huditemeffectmeter_spy.res: the ammo-area
/// art in the local player's team (the res's teambg_2/3 pair) with the
/// charge as its ContinuousProgressBar — MeterFG White over MeterBG Gray
/// (clientscheme.res:47, :76) — and the #TF_CLOAK label under it (the res
/// carries the sandman's #TF_Ball; the meter's label is its item's). The
/// %progresscount% label stays empty on the cloak: stock's spy meter names
/// no count, and the shot shows none. Stock beeps when the meter refills
/// (:465-470); the bar here just fills.
/// </summary>
public sealed class CloakPanel(ITFSpy spy, ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // The res's own panel: the spy's override of hudlayout's HudItemEffectMeter
    // row — xpos r162 ypos r92 (the base's r52), 100x50 — the children's
    // coordinate space; TFHud places the panel there.
    internal static readonly StockBox Panel = new("r162", "r92", 100, 50);

    // The art boxes, as the .res writes them, against the panel.
    private static class Stock
    {
        // ItemEffectMeterBG: 12,0, 76x44 (:20-38).
        public static readonly StockBox Bg = new("12", "0", 76, 44);

        // ItemEffectMeter: 25,23, 40x6 (:61-81).
        public static readonly StockBox Bar = new("25", "23", 40, 6);

        // ItemEffectMeterLabel: 25,27, 41x15, TFFontSmall (:40-59).
        public static readonly StockBox Label = new("25", "27", 41, 15);
    }

    // The res's image names, the .. dropped; teambg_2 red, teambg_3 blue.
    private const string RedBg = "hud/misc_ammo_area_red";
    private const string BlueBg = "hud/misc_ammo_area_blue";

    /// <summary>TF_TEAM_RED: what the teambg pair keys on (the local player's).</summary>
    private const int Red = 2;

    // MeterBG Gray and MeterFG White (clientscheme.res:76, :47).
    private static readonly UiColor MeterBg = new(178, 178, 178, 255);
    private static readonly UiColor MeterFill = new(235, 235, 235, 255);

    public bool Present => spy.CloakPresent;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        var root = Panel.Rect(StockRes.Screen(ui.Screen));
        var bg = Stock.Bg.In(root);
        var bar = Stock.Bar.In(root);
        var label = Stock.Label.In(root);

        // The plate is the art's fallback (the contrast rule), then the art.
        if (StockArt.Missing(textures, player.Team == Red ? RedBg : BlueBg))
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("cloak plate");
        StockArt.Draw(ref ui, textures, "cloak bg", player.Team == Red ? RedBg : BlueBg,
            bg.Left, bg.Bottom, bg.Wide, bg.Tall);

        // The charge: ContinuousProgressBar, the fill cropped from the left
        // (CTFProgressBar::Paint) — the res's 0.25 s refill pulse is the
        // meter's own; here the fill just moves.
        float fraction = Math.Clamp(ui.Animate("cloak", spy.Cloak, Motion.Standard), 0f, 1f);
        ui.Rect(UiPaint.FromGame(MeterBg), Size.Du(bar.Wide), Size.Du(bar.Tall))
            .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("cloak meter");
        if (fraction > 0f)
            ui.Rect(UiPaint.FromGame(MeterFill), Size.Du(bar.Wide * fraction), Size.Du(bar.Tall))
                .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("cloak meter fill");

        ui.Text("#TF2_Hud_Cloak", Text.Caption, Tone.Text).HudSize()
            .Width(Size.Du(label.Wide)).Height(Size.Du(label.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, label.Left, label.Bottom).Layered().Key("cloak label");
    }
}

/// <summary>
/// The disguise status, stock's CDisguiseStatus
/// (tf_hud_disguise_status.cpp): "Disguised" with the class the disguise
/// shows, in the disguised team's tone (the BG panel's SetBGTeam :174-176,
/// through TFHudTeams). Stock also names the imitated player, their weapon
/// and a health readout (CheckName/CheckWeapon/CheckHealth :239-303); the
/// carrier networks only the class and the team (docs/divergences.md).
/// </summary>
public sealed class DisguisePanel(ITFSpy spy) : UiPanel
{
    public bool Present => spy.Disguised;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        using (ui.Row(Space.S, Align.Centre).Key("disguise"))
        {
            ui.Text("#TF2_Hud_Disguised", Text.Caption, Tone.Text).HudSize();
            ui.Data(spy.DisguiseClassName, Text.Caption, TFHudTeams.ToneOf(spy.DisguiseTeam)).HudSize();
        }
    }
}
