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
/// The crosshair, stock's <c>CHudTFCrosshair::Paint</c>
/// (tf_hud_crosshair.cpp:143-217) off the reads the adapter carries
/// (<see cref="ITFCrosshair"/>), which answer the player's own
/// <c>cl_crosshair_*</c> convars (:23-29). The custom-file arm binds the
/// whole material of Paint's <c>vgui/crosshairs/&lt;file&gt;</c> path
/// (:150-158, through <see cref="IUiTextureLibrary.Material"/>) and draws
/// it as the quad at <c>iX±iWidth</c> (:215) — full size <c>2*iWidth</c>,
/// <c>iWidth</c> being the scale times the 32-pixel texture rounded the
/// stock way (:205-206). The empty file takes the base arm's centred
/// sprite (:175-178 and hud_crosshair.cpp:275-280) as the toolkit's
/// crosshair shape. Both arms
/// modulate by <c>(r, g, b, 255)</c> — the three <c>cl_crosshair_*</c>
/// ints (:200, hud_crosshair.cpp:262-264), the colour the theme used to
/// carry. Whether either draws is the weapon's script's
/// <c>DrawCrosshair</c> word, or the custom file's override
/// (tf_weaponbase.cpp:3638-3648). Where the material binds nothing — the
/// preview, or a file the game lacks — the shape stands in: stock's error
/// texture is pixels the toolkit cannot show (docs/divergences.md).
/// </summary>
public sealed class TFCrosshair(ITFCrosshair crosshair, IUiTextureLibrary? textures = null,
                                ITFRenderGroups? groups = null) : UiScreen
{
    public override string Name => "tfcrosshair";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // Stock's hud elements stand down for the scoreboard's locked "global"
    // group (ITFRenderGroups): the layer skips a screen whose Visible is
    // false, as CHudElement::ShouldDraw yields (hud.cpp:288-303).
    public override bool Visible => base.Visible && groups?.GlobalLocked != true;

    // The core HUD's crosshair: this screen replaces it (Hud.cs's
    // hud.crosshair gate).
    private readonly string[] _takes = ["hud.crosshair"];

    public override IReadOnlyList<string> Takes => _takes;

    public override void Describe(ref UiFrame ui)
    {
        if (!crosshair.Draw)
            return;

        // Paint's iWidth (:205-206): flWidth = flWeaponScale * flPlayerScale
        // * iTextureW, iWidth = (int)(flWidth + 0.5f). The adapter folds the
        // weapon's scale and the player's (cl_crosshair_scale / 32) into one
        // number, so flWidth is the 32-pixel texture at that scale.
        int iWidth = (int)(32f * crosshair.Scale + 0.5f);

        // The custom file's arm (:150-158, :215): the material, drawn
        // iX-iWidth .. iX+iWidth — the full size, centred. The empty file
        // asks nothing: stock takes the BaseClass::Paint arm there.
        int texture = crosshair.Material.Length > 0 ? textures?.Material(crosshair.Material) ?? 0 : 0;
        if (texture != 0)
        {
            _ = ui.Texture(texture, Size.Du(2f * iWidth), Size.Du(2f * iWidth))
                .At(UiAnchor.Centre).Name("crosshair").Tint(ConvarColour());
            return;
        }

        // The default crosshair: the base arm's centred sprite
        // (hud_crosshair.cpp:275-280) as the toolkit's shape — and the
        // stand-in where the material binds nothing, the documented
        // divergence from stock's error texture.
        _ = ui.Shape(UiShape.Crosshair, ConvarColour(), iWidth, 5f).At(UiAnchor.Centre).Name("crosshair");
    }

    // Paint's modulate colour, both arms (tf_hud_crosshair.cpp:200,
    // hud_crosshair.cpp:262-264): stock's Color(r, g, b, 255) of the three
    // cl_crosshair_* ints. Built outside the describe body, as a
    // game-supplied colour.
    private UiPaint ConvarColour() =>
        UiPaint.FromGame(new UiColor((byte)crosshair.Red, (byte)crosshair.Green, (byte)crosshair.Blue, 255));
}
