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
/// The timed item's charge bar, stock's plain <c>CHudItemEffectMeter</c>
/// (tf_hud_itemeffectmeter.cpp) at huditemeffectmeter.res: the ammo-area
/// team plate with the charge as its ContinuousProgressBar and the label
/// over it (SetLabelText :607-628 shows the item's own words, the
/// <c>meter_label</c> attribute). The row sits one plate above the cloak's
/// base row — stock gives every item-effect meter element its own
/// hudlayout.res row and stacks them up from the bottom-right. The fill is
/// the gold tone of the timed-item meters; the track is MeterBG Gray and
/// the label ink plain white, as the cloak panel's reads from
/// clientscheme.res:76, :47 and tf_hud_itemeffectmeter.cpp:93.
/// </summary>
public sealed class ItemMeterPanel(ITFItemMeter meter, ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // The res's own panel: the item-effect meter row as huditemeffectmeter.res
    // writes it (xpos r174 ypos r62, 100x50), stacked one plate up for the
    // second meter of the column (GetResFile :86; the cloak comment).
    internal static readonly StockBox Panel = new("r174", "r112", 100, 50);

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

    // MeterBG Gray, the timed-item meter's gold fill, and the label's plain
    // white (clientscheme.res:76; tf_hud_itemeffectmeter.cpp:93).
    private static readonly UiColor MeterBg = new(178, 178, 178, 255);
    private static readonly UiColor MeterFill = new(255, 204, 0, 255);
    private static readonly UiColor LabelInk = new(255, 255, 255, 255);

    public bool Present => meter.Present;

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
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("item meter plate");
        StockArt.Draw(ref ui, textures, "item meter bg", player.Team == Red ? RedBg : BlueBg,
            bg.Left, bg.Bottom, bg.Wide, bg.Tall);

        // The charge: ContinuousProgressBar, the fill cropped from the left
        // as the cloak's is (CTFProgressBar::Paint).
        float fraction = Math.Clamp(ui.Animate("item meter", meter.Charge, Motion.Standard), 0f, 1f);
        ui.Rect(UiPaint.FromGame(MeterBg), Size.Du(bar.Wide), Size.Du(bar.Tall))
            .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("item meter");
        if (fraction > 0f)
            ui.Rect(UiPaint.FromGame(MeterFill), Size.Du(bar.Wide * fraction), Size.Du(bar.Tall))
                .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("item meter fill");

        // The item's own words beside the bar (meter_label), white as the
        // cloak label's; an item without one draws none.
        if (meter.Label.Length > 0)
            ui.Data(meter.Label, Text.Caption, Tone.Text).Color(UiPaint.FromGame(LabelInk))
                .TextSize(TFHudType.ItemEffectMeterLabel)
                .Width(Size.Du(label.Wide)).Height(Size.Du(label.Tall)).TextAlign(Align.Centre)
                .Elide()
                .AtRes(UiAnchor.BottomLeft, label.Left, label.Bottom).Layered().Key("item meter label");
    }
}
