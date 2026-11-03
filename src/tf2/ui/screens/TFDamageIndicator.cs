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
/// The damage indicator, stock's <c>CHudDamageIndicator</c>
/// (tf_hud_damageindicator.cpp) with hud_combattext's numbers beside it: each
/// hit still showing draws its quad on the circle around the screen centre —
/// the direction the hit came from, the radius travelling StartRadius 140 to
/// EndRadius 120 over the TravelTime 0.1 (:252), the size remapping off the
/// amount (MinimumWidth 10 to MaximumWidth 100, MinimumHeight 20 to
/// MaximumHeight 100, :245-246) and the alpha holding one until
/// FadeOutPercentage 0.7 of the life then fading to none (:259-268). The
/// quads are all the taken indicator draws in stock; the dealt numbers are
/// the account panel's (tf_hud_account.cpp:620-646): red, the large font for
/// a crit (:642), floating at the victim — the read's anchor (:630-639)
/// risen and projected per frame (:1120-1131). The glow
/// quad itself is stock's now — the <c>VGUI/damageindicator</c> pixels, white,
/// additive over the world, turned by the hit's bearing about the quad's
/// centre (DrawDamageIndicator :182-231) — with the theme's plate standing
/// where the texture loads nothing.
/// </summary>
public sealed class TFDamageIndicator(ITFDamageHistory hits, ITFCombatText dealt, IUiTextureLibrary textures) : UiScreen
{
    public override string Name => "tfdamageindicator";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // The core HUD's damage vignette and its dealt numbers: this screen replaces
    // both (Hud.cs's hud.damage and hud.dealt gates), the combat text being its own.
    private readonly string[] _takes = ["hud.damage", "hud.dealt"];

    public override IReadOnlyList<string> Takes => _takes;

    // The stock numbers, in the 640x480 .res space; resolved to du per Describe.
    private static class Stock
    {
        // The CPanelAnimationVars (tf_hud_damageindicator.cpp:57-69).
        public const float MinimumWidth = 10f, MaximumWidth = 100f;
        public const float MinimumHeight = 20f, MaximumHeight = 100f;
        public const float StartRadius = 140f, EndRadius = 120f;
        public const float MaximumDamage = 100f;
        public const float TravelTime = 0.1f;
        public const float FadeOutPercentage = 0.7f;
    }

    public override void Describe(ref UiFrame ui)
    {
        // The 640x480 canvas's du, as the other panels resolve it.
        float k = MathF.Min(1f, ui.DesignHeight / (480f * StockRes.Scale));
        float u = StockRes.Scale * k;
        float now = (float)ui.Now;

        int count = hits.Count;
        for (int i = 0; i < count; i++)
        {
            var hit = hits.Damage(i);
            float t = now - hit.At;
            if (t < 0f)
                continue;

            // The radius travels inward over the travel time (:252).
            float radius = (Stock.StartRadius
                            - (Stock.StartRadius - Stock.EndRadius) * MathF.Min(t, Stock.TravelTime) / Stock.TravelTime) * u;
            float yaw = hit.Yaw * MathF.PI / 180f;

            // GetDamagePosition's projection: right of centre for a hit from
            // the right, above for one from the front (the side/front dot
            // products through the atan2 + π dance, :150-177).
            float x = radius * MathF.Sin(yaw);
            float y = -radius * MathF.Cos(yaw);

            // The alpha: full until the fade-out percentage of the life, then
            // down to none (:259-268).
            float percent = t / hit.Life;
            float alpha = percent <= Stock.FadeOutPercentage
                ? 1f
                : Math.Clamp(1f - (percent - Stock.FadeOutPercentage) / (1f - Stock.FadeOutPercentage), 0f, 1f);

            // The size remaps off the amount, capped at the maximum (:243-246).
            float clamped = Math.Min(hit.Amount, Stock.MaximumDamage);
            float wide = (Stock.MinimumWidth + (Stock.MaximumWidth - Stock.MinimumWidth) * clamped / Stock.MaximumDamage) * u;
            float tall = (Stock.MinimumHeight + (Stock.MaximumHeight - Stock.MinimumHeight) * clamped / Stock.MaximumDamage) * u;

            // The glow: the VGUI/damageindicator pixels at the alpha, additive
            // over the world, the quad turned by the hit's bearing (the
            // projection's yaw is GetDamagePosition's -flRotation, the leaf's
            // turn the same radians about its centre, :167-197).
            int glow = textures?.Find("vgui/damageindicator") ?? 0;
            if (glow != 0)
            {
                _ = ui.Texture(glow, Size.Du(wide), Size.Du(tall))
                    .AtRes(UiAnchor.Centre, x, y).Rotate(yaw)
                    .Blend(UiBlend.Additive).Opacity(alpha)
                    .Layered().Name("indicator");
            }
            else
            {
                _ = ui.Rect(((UiPaint)Tone.Text).WithOpacity(alpha * 0.4f), Size.Du(wide), Size.Du(tall))
                        .Round(Radius.L).AtRes(UiAnchor.Centre, x, y).Layered().Name("indicator");
            }
        }

        // hud_combattext's numbers (tf_hud_account.cpp): the dealt amount,
        // red, the large font for a crit (:642), floating at the victim —
        // stock's m_bWorldSpace. The paint (:1120-1131) asks the read for
        // the anchor risen by the lifetime's share of the 32 units, pro-
        // jected to hud pixels, and skips when it refuses (GetVectorInHud-
        // Space's arm, :1126-1127); the pixels become du over the pixel
        // scale, the pill's corner on the spot. They ride an opaque window
        // pill and hold their life rather than fading, stock's alpha
        // (:1085-1087) putting a translucent pill over a bright world, the
        // contrast rule the kill feed's backing and the old riding number
        // already answered the same way.
        int dealtCount = dealt.Count;
        for (int i = 0; i < dealtCount; i++)
        {
            var number = dealt.Number(i);
            float t = now - number.At;
            if (t < 0f)
                continue;

            float percent = Math.Clamp(t / TFCombatNumber.DeltaLifetime, 0f, 1f);
            if (!dealt.Screen(i, percent, out var hud))
                continue;

            var x = new UiLength(hud.X / ui.PixelScale, false);
            var y = new UiLength(hud.Y / ui.PixelScale, false);

            using (ui.Column(Space.None).Fill(Tone.Window).Round(Radius.L).Pad(Space.S)
                       .At(UiAnchor.TopLeft).Inset(x, y).Layered().Name("combat text"))
                _ = ui.Number(number.Amount, number.Crit ? Text.Number : Text.Heading, Tone.Danger)
                        .TextAlign(Align.Centre);
        }
    }
}
