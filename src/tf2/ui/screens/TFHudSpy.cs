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
/// The spy's cloak meter, stock's plain item-effect meter — the
/// <c>CHudItemEffectMeter</c> the spy case adds (tf_hud_itemeffectmeter.cpp:313)
/// — at huditemeffectmeter.res: the base row r174 r62 100x50 (GetResFile :86;
/// huditemeffectmeter_spy.res's r162 r92 is the revolver's meter, :322). The
/// ammo-area art in the local player's team (the res's teambg_2/3 pair, the
/// horiz1 cut) with the charge as its ContinuousProgressBar — MeterFG White
/// over MeterBG Gray (clientscheme.res:47, :76) — and the label over it:
/// SetLabelText (:607-628) replaces the res's #TF_Ball with #TF_Cloak
/// (tf_english.txt:390 "CLOAK"), in the ink GetLabelTextColor names, plain
/// white (:93), in the res's TFFontSmall (clientscheme.res:2012, Verdana 8 —
/// no Verdana file ships, so the face is the theme's at the stock 8x2.25).
/// Stock beeps when the meter refills (:527-532); the bar here just fills.
/// </summary>
public sealed class CloakPanel(ITFSpy spy, ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // The res's own panel: hudlayout's HudItemEffectMeter row as the base res
    // writes it — xpos r174 ypos r62, 100x50 — the children's coordinate
    // space; TFHud places the panel there.
    internal static readonly StockBox Panel = new("r174", "r62", 100, 50);

    // The art boxes, as the .res writes them, against the panel.
    private static class Stock
    {
        // ItemEffectMeterBG: 12,6, 100x50 (:20-38).
        public static readonly StockBox Bg = new("12", "6", 100, 50);

        // ItemEffectMeter: 47,28, 30x5 (:65-85).
        public static readonly StockBox Bar = new("47", "28", 30, 5);

        // ItemEffectMeterLabel: 42,30, 41x15, TFFontSmall (:40-59).
        public static readonly StockBox Label = new("42", "30", 41, 15);
    }

    // The res's image names, the .. dropped; teambg_2 red, teambg_3 blue.
    private const string RedBg = "hud/misc_ammo_area_horiz1_red";
    private const string BlueBg = "hud/misc_ammo_area_horiz1_blue";

    /// <summary>TF_TEAM_RED: what the teambg pair keys on (the local player's).</summary>
    private const int Red = 2;

    // MeterBG Gray and MeterFG White (clientscheme.res:76, :47); the label's
    // ink, GetLabelTextColor's plain white (tf_hud_itemeffectmeter.cpp:93).
    private static readonly UiColor MeterBg = new(178, 178, 178, 255);
    private static readonly UiColor MeterFill = new(235, 235, 235, 255);
    private static readonly UiColor LabelInk = new(255, 255, 255, 255);

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

        ui.Text("#TF2_Hud_Cloak", Text.Caption, Tone.Text).Color(UiPaint.FromGame(LabelInk))
            .TextSize(TFHudType.ItemEffectMeterLabel)
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
