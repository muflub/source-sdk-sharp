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
/// The ammo row: what stock's <c>CTFHudWeaponAmmo::OnThink</c> writes into
/// the panel's two dialog labels, <c>Ammo</c> and <c>AmmoInReserve</c>
/// (tf_hud_ammostatus.cpp:248-319). The clip arm (<c>Clip1() &gt;= 0</c>,
/// :276; <c>UpdateAmmoLabels(true, true, false)</c>, :298-304) draws the
/// rounds in hand and the pool behind them (:284-287); a weapon with no
/// clip (<c>nAmmo1 &lt; 0</c>, :279-282) draws the pool total as the one
/// number and hides the reserve label
/// (<c>UpdateAmmoLabels(false, false, true)</c>, :305-309). Which weapons
/// get a readout at all is the <see cref="ITFLocalPlayer.ShowsAmmo"/>
/// decision — stock's <c>ShouldDraw</c> (:120-163), whose medigun, no-ammo
/// and metal arms the adapter makes.
///
/// The panel draws at its .res coordinates (HudWeaponAmmo, hudlayout.res:
/// xpos r95 ypos r55 94x45 — the right edge 1 unit from the screen's, the
/// bottom 10 units up; du right 2.25, bottom 22.5, 211.5x101.25) with the
/// labels of hudammoweapons.res through the stock converter: the bg image
/// (HudWeaponAmmoBG, teambg_2/3), the clip in HudFontGiantBold TanLight
/// south-east of its box, the reserve in the small face south-west of its
/// own, and the no-clip total across the panel. The pool plus the clip
/// under 0.40 of the weapon's full stock (<c>hud_lowammowarning_threshold</c>,
/// :42) draws stock's low-ammo warning: <c>HudWeaponLowAmmoImage</c> — a
/// plain ImagePanel, so the res's teambg keys on it are inert (the team
/// swap is CTFImagePanel's, tf_imagepanel.cpp:45-59) and its
/// <c>../hud/ammo_red_bg</c> is the one art — grown about its centre by
/// the missing fraction of the 5-unit <c>hud_lowammowarning_maxposadjust</c>
/// (:43, SizeLowAmmoIndicator :215-230), tinted the FgColor red of
/// <c>ShowLowAmmoIndicator</c> (:207), and alpha-pulsed by HudLowAmmoPulse
/// (hudanimations_tf.txt:252-264) on its zpos 0 layer, beneath the panel's
/// own bg (:30-42 against :4-21). The bg layer keeps
/// a Tone.Surface plate at the same coordinates: the contrast rule's
/// backdrop when the texture library has the material, the fallback when
/// it misses it.
/// </summary>
public sealed class AmmoPanel(ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // Stock's art boxes, as the .res writes them, against HudWeaponAmmo's
    // own 94x45 panel (hudlayout.res:14-27); resolved to du per Describe.
    internal static class Stock
    {
        public static readonly StockRect Panel = new StockBox("r95", "r55", 94, 45).Rect(StockRect.Screen);

        // HudWeaponAmmoBG: xpos 4, ypos 0, 90x45 (hudammoweapons.res:3-22).
        public static readonly StockBox Bg = new("4", "0", 90, 45);

        // AmmoInClip: xpos 4, ypos 0, 55x40 (hudammoweapons.res:43-63);
        // AmmoInReserve: xpos 59, ypos 8, 40x27 (:88-108); AmmoNoClip:
        // xpos 0, ypos 2, 84x40 (:130-149).
        public static readonly StockBox Clip = new("4", "0", 55, 40);
        public static readonly StockBox Reserve = new("59", "8", 40, 27);
        public static readonly StockBox NoClip = new("0", "2", 84, 40);

        // hud_lowammowarning_threshold / _maxposadjust (tf_hud_ammostatus.cpp:42-43).
        public const float WarnThreshold = 0.40f;
        public const float WarnGrow = 5f;
    }

    // ShowLowAmmoIndicator's FgColor (tf_hud_ammostatus.cpp:207) over the
    // art: a game-supplied colour, so the describe body names no literal.
    private static readonly UiPaint WarnTint = UiPaint.FromGame(new UiColor(255, 0, 0, 255));

    public override void Describe(ref UiFrame ui)
    {
        if (!player.ShowsAmmo)
            return;                                   // ShouldDraw's false arms: nothing to count (:120-163)

        var bg = Stock.Bg.In(Stock.Panel);
        var clip = Stock.Clip.In(Stock.Panel);
        var reserve = Stock.Reserve.In(Stock.Panel);
        // Stock's 40-wide reserve box at xpos 59 runs 5 units past the
        // 94-wide panel's right edge (hudammoweapons.res:88-108 against
        // hudlayout.res:14-27); the panel clips it, so the label takes the
        // on-panel width — the Align.Start glyphs stay stock's.
        float reserveWide = MathF.Min(reserve.Wide, Stock.Panel.Wide * StockRes.Scale - reserve.Left);
        var noClip = Stock.NoClip.In(Stock.Panel);

        // The low-ammo warning (tf_hud_ammostatus.cpp:312-325): the pool
        // plus the clip against 0.40 of the full stock, the strict < of
        // :321. The warning image grows about the bg's centre by the
        // missing fraction of the 5-unit adjust (:219-228) and pulses
        // (HudLowAmmoPulse) on its zpos 0 layer — beneath this panel's
        // plate and bg, so stock shows only the ring outside the panel art.
        int total = player.Clip >= 0 ? player.Clip + player.Reserve : player.Reserve;
        int full = player.MaxTotalAmmo;
        float threshold = full * Stock.WarnThreshold;
        if (full > 0 && total < MathF.Round(threshold))
        {
            float adj = MathF.Round((threshold - total) / threshold * Stock.WarnGrow) * StockRes.Scale;
            StockArt.Draw(ref ui, textures, "low ammo", "hud/ammo_red_bg", bg.Left - adj, bg.Bottom - adj,
                bg.Wide + 2f * adj, bg.Tall + 2f * adj, WarnTint, default, StockArt.Pulse025(ui.Now));
        }

        if (StockArt.Missing(textures, Bg(player.Team)))
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Key("ammo plate");
        StockArt.Draw(ref ui, textures, "ammo bg", Bg(player.Team), bg.Left, bg.Bottom, bg.Wide, bg.Tall);

        if (player.Clip >= 0)
        {
            // The clip arm: the pool label shows only here (:298-304).
            ui.Number(player.Clip, Text.Display, Tone.Text).HudSize()
                .Width(Size.Du(clip.Wide)).Height(Size.Du(clip.Tall)).TextAlign(Align.End)
                .AtRes(UiAnchor.BottomLeft, clip.Left, clip.Bottom).Layered().Key("ammo");
            ui.Number(player.Reserve, Text.Body, Tone.Text).HudSize()
                .Width(Size.Du(reserveWide)).Height(Size.Du(reserve.Tall)).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, reserve.Left, reserve.Bottom).Layered().Key("reserve");
        }
        else
        {
            // No clip: nAmmo1 is the pool total, and the reserve label
            // stays hidden (:279-282, :305-309).
            ui.Number(player.Reserve, Text.Display, Tone.Text).HudSize()
                .Width(Size.Du(noClip.Wide)).Height(Size.Du(noClip.Tall)).TextAlign(Align.End)
                .AtRes(UiAnchor.BottomLeft, noClip.Left, noClip.Bottom).Layered().Key("ammo");
        }
    }

    /// <summary>The team bg (hudammoweapons.res teambg_2/3: the raw TF
    /// team numbers, 2 RED, 3 BLU).</summary>
    private static string Bg(int team) => team == 3 ? "hud/ammo_blue_bg" : "hud/ammo_red_bg";
}
