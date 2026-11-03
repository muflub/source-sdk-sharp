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
/// The medic's charge meter, stock's <c>CHudMedicChargeMeter</c>
/// (tf_hud_mediccharge.cpp) at hudmediccharge.res: the team plate
/// (<c>Background</c>, 130x65 at the panel's origin), the health-cluster
/// cross (<c>HealthClusterIcon</c>, 36x36 at 2,17) left of the
/// #TF_Ubercharge label — "ÜBERCHARGE: %charge%%%" (tf_english.txt:8396), the
/// level as its dialog variable — and the ContinuousProgressBar under it
/// (86x8 at 30,38),
/// ProgressOffWhite over ProgressBackground (clientscheme.res:108-109,
/// :326-327). The beam's patient under it (m_hHealTarget through g_PR). The
/// ready pulse is the label and meter FgColor stepping TanLight/TanDarker on
/// the 0.6 s <c>HudMedicCharged</c> loop (hudanimations_tf.txt:425-440); the
/// release stops it (:442-449, HudMedicChargedStop), so the pair holds
/// TanLight while draining.
/// </summary>
public sealed class MedicChargePanel(ITFMedicCharge medic, ITFLocalPlayer player, IUiTextureLibrary textures,
                                     IUiLocalisation localise) : UiPanel
{
    private readonly IUiLocalisation _localise = localise;

    public bool Present => medic.Present;

    // The icon, the label and the meter, as hudmediccharge.res writes them,
    // against the visible Background (whose origin is the panel's).
    private static class Stock
    {
        // HealthClusterIcon: 2,17, 36x36, scaleImage, visible in normal mode
        // (:174-187). The label starts at its right edge.
        public static readonly StockBox Icon = new("2", "17", 36, 36);

        // ChargeLabel: 30,24, 90x15, HudFontSmallest (:20-42).
        public static readonly StockBox Label = new("30", "24", 90, 15);

        // ChargeMeter: 30,38, 86x8 (:68-87).
        public static readonly StockBox Meter = new("30", "38", 86, 8);
    }

    // The res's image names, the .. dropped; teambg_2 red, teambg_3 blue;
    // the cluster icon is HealthClusterIcon's image.
    private const string RedBg = "hud/medic_charge_red_bg";
    private const string BlueBg = "hud/medic_charge_blue_bg";
    private const string ClusterArt = "hud/ico_health_cluster";

    /// <summary>TF_TEAM_RED: what the teambg pair keys on (the local player's).</summary>
    private const int Red = 2;

    // The meter's inks, the label's, and the pulse's dark step
    // (clientscheme.res:100-101, :108-109).
    private static readonly UiColor MeterBg = new(250, 234, 201, 51);
    private static readonly UiColor MeterFill = new(251, 235, 202, 255);
    private static readonly UiColor TanLight = new(235, 226, 202, 255);
    private static readonly UiColor TanDarker = new(46, 43, 42, 255);

    /// <summary>The shared ink, game colours through <see cref="UiPaint.FromGame"/>.</summary>
    private static UiPaint Light => UiPaint.FromGame(TanLight);

    private static UiPaint Dark => UiPaint.FromGame(TanDarker);

    /// <summary>The pulse's dark half, as the label fades to it.</summary>
    private const float PulseDim = 0.4f;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        var panel = HealthPanel.MedicPanel.Rect(StockRes.Screen(ui.Screen));
        var bg = HealthPanel.MedicBg.Rect(panel);
        var bgSpot = StockRes.Spot(HealthPanel.MedicBg, HealthPanel.MedicPanel, ui.Screen);
        var icon = Stock.Icon.In(bg);
        var label = Stock.Label.In(bg);
        var meter = Stock.Meter.In(bg);

        // The plate is the art's fallback (the contrast rule), then the art.
        if (StockArt.Missing(textures, player.Team == Red ? RedBg : BlueBg))
            ui.Rect(Tone.Surface, Size.Du(bgSpot.Wide), Size.Du(bgSpot.Tall))
                .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key("medic plate");
        StockArt.Draw(ref ui, textures, "medic bg", player.Team == Red ? RedBg : BlueBg,
            0f, 0f, bgSpot.Wide, bgSpot.Tall);

        // The health-cluster cross left of the label (HealthClusterIcon,
        // hud/ico_health_cluster in its box, visible outside min mode).
        StockArt.Draw(ref ui, textures, "medic cluster", ClusterArt,
            icon.Left, icon.Bottom, icon.Wide, icon.Tall);

        // The level, and the charged pulse: the fill steps TanLight/TanDarker
        // and the label fades with it, the 0.6 s HudMedicCharged loop
        // (hudanimations_tf.txt:425-434); the release stops it on TanLight
        // (:442-449), so the pair holds TanLight while draining — the resting
        // ink too. The label fades rather than darkens: the linted ink is the
        // full-strength TanLight either way.
        float charge = medic.Charge;
        bool ready = charge >= 1f && !medic.Releasing;
        bool dark = ready && (int)(ui.Now / 0.3) % 2 == 1;

        // The label, the charge as its percent: stock's single CExLabel, the
        // dialog variable inside the sentence (tf_english.txt:8396).
        ui.Text($"{Localised("#TF2_Hud_UberCharge")} {(int)(charge * 100)}%", Text.Caption).Color(Light)
            .Width(Size.Du(label.Wide)).Height(Size.Du(label.Tall)).TextAlign(Align.Start).Elide()
            .Opacity(dark ? PulseDim : 1f)
            .AtRes(UiAnchor.BottomLeft, label.Left, label.Bottom).Layered().Key("uber label");

        // The meter: the level as the medigun states it, eased like the
        // health cross (ContinuousProgressBar's SetProgress, :257).
        float level = Math.Clamp(ui.Animate("charge", charge, Motion.Standard), 0f, 1f);
        ui.Rect(UiPaint.FromGame(MeterBg), Size.Du(meter.Wide), Size.Du(meter.Tall))
            .AtRes(UiAnchor.BottomLeft, meter.Left, meter.Bottom).Layered().Key("charge");
        if (level > 0f)
        {
            UiPaint fill = dark ? Dark : UiPaint.FromGame(MeterFill);
            ui.Rect(fill, Size.Du(meter.Wide * level), Size.Du(meter.Tall))
                .AtRes(UiAnchor.BottomLeft, meter.Left, meter.Bottom).Layered().Key("charge fill");
        }

        // The beam's patient (m_hHealTarget), when there is one — a caption's
        // height under the meter, on the plate's lower band.
        string patient = medic.HealTarget;
        if (patient.Length > 0)
            ui.Text($"{Localised("#TF2_Hud_Healing")} {patient}", Text.Caption)
                .Width(Size.Du(meter.Wide)).TextAlign(Align.Start).Elide()
                .AtRes(UiAnchor.BottomLeft, meter.Left, meter.Bottom - (float)Space.XL)
                .Layered().Key("healing");
    }

    /// <summary>The token's words, or the token itself when no table holds it.</summary>
    private string Localised(string token) => _localise.Find(token) ?? token;
}
