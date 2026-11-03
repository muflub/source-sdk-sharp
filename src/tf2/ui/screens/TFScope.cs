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
/// The sniper scope, stock's <c>CHudScope::Paint</c> (tf_hud_scope.cpp:395-528):
/// the four quadrant materials (<c>HUD/scope_sniper_ul/ur/lr/ll</c>, :317-320)
/// tiled over the centred 4:3 region (:409-420), each sampled with the
/// half-pixel inset (:446), and the world outside it blacked with primitives
/// (:504-523). The quadrants are Refract shaders — a quadrant's pixels are the
/// refracted frame buffer, tinted by <c>$refracttinttexture</c>, not a picture
/// to upload — so they are bound whole
/// (<see cref="IUiTextureLibrary.Material"/>; stock's DrawSetTextureFile,
/// :317-320) and drawn after the frame's refract copy is refreshed (the draw
/// of a material-bound handle refreshes it first, as stock's Paint does at
/// :398). Where a material binds nothing (the preview) the quadrants stand as
/// <see cref="Tone.Black"/>, the silhouette without the curved cutout. Stock's
/// <c>scope_sniper_alt_*</c> pair and its <c>TF_WEAPON_SNIPERRIFLE_CLASSIC</c>
/// arm (:386) have no carrier — the one rifle ported draws the one pair.
///
/// <para>
/// The charge (<c>CHudScopeCharge</c>, :73-230) draws under the scope while
/// the hands hold the rifle: stock's <c>HUD/sniperscope_numbers</c> arc —
/// the value shifted in through a material proxy — becomes the meter and
/// percent the toolkit draws (the arc's pixels are a texture the toolkit
/// cannot shift a UV window into per frame).
/// </para>
/// </summary>
public sealed class TFScope(ITFScope scope, IUiTextureLibrary? textures = null) : UiScreen
{
    public override string Name => "tfscope";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's <c>SetZPos(-1)</c> (tf_hud_scope.cpp:366-367): the
    /// bars stand behind the other HUD elements, so the corner panels keep
    /// reading over them while zoomed. A negative order puts the screen
    /// first in the Hud layer, under every panel at the default 0.</summary>
    public override int Order => -1;

    public override void Describe(ref UiFrame ui)
    {
        if (!scope.Zoomed)
            return;

        float wide = ui.DesignWidth, tall = ui.DesignHeight;

        // The 4:3 region the quadrants tile, centred (:409-420): on a wide
        // screen the sides are primitives, on a tall one the top and bottom.
        // Stock lets the region overrun a 5:4 screen (1280x1024's 1365-wide
        // region clips 42 pixels a side, the quadrant corners lost to the
        // viewport); ours shrinks it to stay on the screen, still exactly
        // 4:3, so the cutout stays a circle (docs/divergences.md).
        float regionWide = MathF.Min(wide, tall * 4f / 3f);
        float regionTall = regionWide * 3f / 4f;

        float halfW = regionWide / 2f, halfH = regionTall / 2f;

        // The half-pixel inset every quadrant is sampled with (:446).
        float edge = 0.5f / 256f;
        var uv = new UiRect(edge, edge, 1f - 2f * edge, 1f - 2f * edge);

        // AtRes resolves y downward from the anchor (UiContext.Layout's
        // inward rule): the upper quads take the negative offset, as the
        // damage indicator's rising text does (TFDamageIndicator's -lift).
        Quad(ref ui, textures?.Material("HUD/scope_sniper_ul") ?? 0, halfW, halfH, -halfW / 2f, -halfH / 2f, uv, "upper left");
        Quad(ref ui, textures?.Material("HUD/scope_sniper_ur") ?? 0, halfW, halfH, halfW / 2f, -halfH / 2f, uv, "upper right");
        Quad(ref ui, textures?.Material("HUD/scope_sniper_lr") ?? 0, halfW, halfH, halfW / 2f, halfH / 2f, uv, "lower right");
        Quad(ref ui, textures?.Material("HUD/scope_sniper_ll") ?? 0, halfW, halfH, -halfW / 2f, halfH / 2f, uv, "lower left");

        // The primitives outside the region (:504-523), black as stock's
        // DrawSetColor(0,0,0,255).
        float side = (wide - regionWide) / 2f, above = (tall - regionTall) / 2f;
        if (side > 0f)
        {
            Bar(ref ui, side, tall, -(regionWide + side) / 2f, 0f, "left block");
            Bar(ref ui, side, tall, (regionWide + side) / 2f, 0f, "right block");
        }
        if (above > 0f)
        {
            Bar(ref ui, wide, above, 0f, (regionTall + above) / 2f, "top block");
            Bar(ref ui, wide, above, 0f, -(regionTall + above) / 2f, "bottom block");
        }

        // The charge under the scope (CHudScopeCharge::Paint :162-226), the
        // hands' rifle the gate its assert_cast is.
        if (scope.MeterPresent)
        {
            using (ui.Column(Space.XS, Align.Centre).At(UiAnchor.Bottom).Inset(Space.None, Space.XXL).Key("scope charge"))
            {
                // Stock draws only the digits and the line over the scope's
                // bottom (:221-223), no word before them: the percent and the
                // meter stand alone in the column.
                ui.Number((int)(scope.Charge * 100f), Text.Number, Tone.Accent).Key("percent");

                ui.Meter(ui.Animate("scope charge", scope.Charge, Motion.Standard), Tone.Accent, Size.Du(280f), 4)
                    .Key("charge meter");
            }
        }
    }

    /// <summary>One quadrant at its offset from the screen's centre — the
    /// bound material, or the black silhouette where it binds nothing.</summary>
    private static void Quad(ref UiFrame ui, int texture, float wide, float tall, float x, float y, in UiRect uv, string name)
    {
        if (texture != 0)
            _ = ui.Texture(texture, Size.Du(wide), Size.Du(tall), uv).AtRes(UiAnchor.Centre, x, y).Layered().Name(name);
        else
            _ = ui.Rect(Tone.Black, Size.Du(wide), Size.Du(tall)).AtRes(UiAnchor.Centre, x, y).Layered().Name(name);
    }

    private static void Bar(ref UiFrame ui, float wide, float tall, float x, float y, string name)
        => _ = ui.Rect(Tone.Black, Size.Du(wide), Size.Du(tall)).AtRes(UiAnchor.Centre, x, y).Layered().Name(name);
}
