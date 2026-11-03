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
/// The target ID, stock's <c>CTargetID</c> (tf_hud_target_id.cpp) in the
/// stock layout: the <c>CMainTargetID</c> panel of hudlayout.res
/// (xpos c-126 ypos 285, 252x35) with the team's colour panel art for its
/// background (targetid.res TargetIDBG teambg_1/2/3 — brown, red, blu, the
/// 50-tall art hanging 15 units below the 35-tall element), the aimed
/// player's name (TargetNameLabel, HudFontSmall, xpos 8 — pushed to 40 by
/// PerformLayout's XRES(8) + health indent when the health cross draws)
/// and the data line under it (TargetDataLabel, ypos 22). The health cross
/// is the spectator bar's block (SpectatorGUIHealth 3,2 32x32 — health_bg
/// 4,4 24x24 under the health_color cross 6,6 20x20,
/// spectatorguihealth.res) and clips like <c>CTFHealthPanel::Paint</c>
/// (tf_hud_playerstatus.cpp:589-634): the texture's lower health-fraction,
/// the dead art when the health is out. The name and the cross show by the
/// adapter's content gate; the disguise and charge lines are the
/// <see cref="TFTargetIdLine"/> the data string built. Stock's
/// PerformLayout (:599-697) sizes the background to the text and centres
/// the whole; here the panel keeps the hudlayout box and the labels elide
/// into it — the fixed-width divergence the adapter records. The avatar
/// image and the kill-streak and moveable icons are not networked reads
/// (docs/plans/tf2/deferred.tsv).
/// </summary>
public sealed class TFTargetId(ITFTargetId target, IUiTextureLibrary textures, ITFRenderGroups? groups = null) : UiScreen
{
    public override string Name => "tftargetid";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // Stock's hud elements stand down for the scoreboard's locked "global"
    // group (ITFRenderGroups): the layer skips a screen whose Visible is
    // false, as CHudElement::ShouldDraw yields (hud.cpp:288-303).
    public override bool Visible => base.Visible && groups?.GlobalLocked != true;

    // The stock numbers, in the 640x480 .res space; resolved to du per Describe.
    private static class Stock
    {
        // The host: CMainTargetID's box widened to the art's 50 tall — the
        // element is 35 (hudlayout.res:255-320) and the TargetIDBG art hangs
        // 15 units below it (targetid.res:3-29), so the children hang
        // against the art's extent, its top at the element's.
        public static readonly StockRect Panel = new StockBox("c-126", "285", 252, 50).Rect(StockRect.Screen);

        // TargetIDBG: the full 252x50 art from the element's top (targetid.res:3-29).
        public static readonly StockBox Bg = new("0", "0", 252, 50);

        // SpectatorGUIHealth 3,2 32x32 (targetid.res:121-138) and its
        // children (spectatorguihealth.res): the bg 4,4, the cross 6,6 and
        // the value 6,11 20x10, all inside the block — the value widened to
        // the cross's full width, centred on it, for the toolkit's digits.
        public static readonly StockBox HealthBg = new("7", "6", 24, 24);
        public static readonly StockBox Cross = new("9", "8", 20, 20);
        public static readonly StockBox HealthValue = new("1", "11", 36, 10);

        // TargetNameLabel xpos 8 ypos 5 (targetid.res:75-96), the data label
        // xpos 8 ypos 22 (targetid.res:97-120) — the x pushed to 40 by the
        // health indent (PerformLayout, tf_hud_target_id.cpp:647-649).
        public static readonly StockBox Name = new("8", "5", 236, 24);
        public static readonly StockBox NameAfterHealth = new("40", "5", 204, 24);
        public static readonly StockBox Data = new("8", "22", 236, 15);
        public static readonly StockBox DataAfterHealth = new("40", "22", 204, 15);
    }

    // The res's own image names (its "image"/teambg values, the ".." dropped).
    private const string BrownBg = "hud/color_panel_brown";
    private const string RedBg = "hud/color_panel_red";
    private const string BlueBg = "hud/color_panel_blu";
    private const string HealthBg = "hud/health_bg";
    private const string HealthCross = "hud/health_color";
    private const string HealthDead = "hud/health_dead";

    // TF_TEAM_RED/BLUE (tf_shareddefs.h), the teambg_2/3 selectors.
    private const int Red = 2;
    private const int Blue = 3;

    public override void Describe(ref UiFrame ui)
    {
        if (!target.Present)
            return;

        // The element's own box: the children's .res spots are relative to
        // it (hudlayout.res against targetid.res), so the corner hosts it.
        // Top-anchored: the .res canvas's top is pinned to the design's
        // (the disguise corner's rule), so the panel keeps its 285-unit
        // drop on any canvas height. The du ride the design's height when
        // it is denser than the canvas's own (a ui-scaled design is the
        // same picture smaller); a larger design keeps the canvas's du.
        float k = MathF.Min(1f, ui.DesignHeight / (480f * StockRes.Scale));
        var at = Scaled(StockRes.Spot(Stock.Panel, StockRect.Screen), k);
        using (ui.Box().AtRes(UiAnchor.Top, at.CentreX, at.Top)
                       .Size(Size.Du(at.Wide), Size.Du(at.Tall)).Key("target id"))
        {
            var bg = Scaled(Stock.Bg.In(Stock.Panel), k);
            string art = target.Team switch { Red => RedBg, Blue => BlueBg, _ => BrownBg };
            if (!StockArt.Draw(ref ui, textures, "target id bg", art, bg.Left, bg.Bottom, bg.Wide, bg.Tall))
                using (ui.Box().Size(Size.Du(bg.Wide), Size.Du(bg.Tall)).Fill(Tone.Surface)
                               .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("target id bg fallback"))
                {
                }

            bool health = target.ShowsHealth;
            var name = Scaled((health ? Stock.NameAfterHealth : Stock.Name).In(Stock.Panel), k);
            ui.Data(target.Name, Text.Body, Tone.Text)
                .Width(Size.Du(name.Wide)).Height(Size.Du(name.Tall)).TextAlign(Align.Start)
                .Elide().MaxWidth(name.Wide)
                .AtRes(UiAnchor.BottomLeft, name.Left, name.Bottom).Layered().Key("target name");

            var data = Scaled((health ? Stock.DataAfterHealth : Stock.Data).In(Stock.Panel), k);
            var line = target.Line;
            using (ui.Row(Space.XS, Align.Centre)
                       .Width(Size.Du(data.Wide)).Height(Size.Du(data.Tall))
                       .AtRes(UiAnchor.BottomLeft, data.Left, data.Bottom).Layered().Key("target data"))
            {
                switch (line.Kind)
                {
                    // #TF_playerid_friendlyspy_disguise: "Disguised as %s1 %s2"
                    // (c_tf_player.cpp:9768-9779) — the alignment word by the
                    // disguise's team, then the class.
                    case TFTargetIdLineKind.Disguise:
                        ui.Text("#TF2_TargetId_DisguisedAs", Text.Caption, Tone.Text);
                        ui.Text(line.Enemy ? "#TF2_TargetId_Enemy" : "#TF2_TargetId_Friendly", Text.Caption, Tone.Text);
                        ui.Data(line.ClassName, Text.Caption, Tone.Text).Elide();
                        break;

                    // #TF_playerid_mediccharge: "ÜberCharge: %s1" (:9794-9804).
                    case TFTargetIdLineKind.MedicCharge:
                        ui.Text("#TF2_TargetId_UberCharge", Text.Caption, Tone.Text);
                        ui.Number(line.Charge, Text.Caption, Tone.Text).Key("charge");
                        break;
                }
            }

            if (health)
                Health(ref ui, k);
        }
    }

    /// <summary>A spot's du offsets and extents scaled onto the design.</summary>
    private static StockSpot Scaled(StockSpot spot, float k) =>
        new(spot.Left * k, spot.Right * k, spot.Bottom * k, spot.Top * k,
            spot.CentreX * k, spot.Wide * k, spot.Tall * k);

    /// <summary>The health block, clipping like CTFHealthPanel::Paint
    /// (tf_hud_playerstatus.cpp:589-634): the texture's lower
    /// health-fraction over the bg, the dead art when the health is out.</summary>
    private void Health(ref UiFrame ui, float k)
    {
        var block = Scaled(Stock.HealthBg.In(Stock.Panel), k);
        if (!StockArt.Draw(ref ui, textures, "health bg", HealthBg, block.Left, block.Bottom, block.Wide, block.Tall))
            using (ui.Box().Size(Size.Du(block.Wide), Size.Du(block.Tall)).Fill(Tone.SurfaceRaised)
                           .AtRes(UiAnchor.BottomLeft, block.Left, block.Bottom).Layered().Key("health bg fallback"))
            {
            }

        var cross = Scaled(Stock.Cross.In(Stock.Panel), k);
        int max = target.MaxHealth;
        float fraction = max > 0 ? Math.Clamp(target.Health / (float)max, 0f, 1f) : 0f;
        bool alive = target.Health > 0;

        // Alive: the cross's lower health-fraction, the uv window
        // CTFHealthPanel paints (uv2 - m_flHealth .. uv2); dead: the full
        // dead art (:601-612).
        float tall = alive ? cross.Tall * fraction : cross.Tall;
        var uv = alive ? new UiRect(0, 1f - fraction, 1f, fraction) : new UiRect(0, 0, 1f, 1f);
        if (!StockArt.Draw(ref ui, textures, "health cross", alive ? HealthCross : HealthDead,
                           cross.Left, cross.Bottom, cross.Wide, tall, uv: uv))
            using (ui.Box().Size(Size.Du(cross.Wide), Size.Du(cross.Tall)).Fill(Tone.Danger)
                           .AtRes(UiAnchor.BottomLeft, cross.Left, cross.Bottom).Layered().Key("health cross fallback"))
            {
            }

        // PlayerStatusHealthValue, centred over the cross
        // (spectatorguihealth.res:74-93). The res's fgcolor TanDark rides
        // the busy art at under 3:1, so the number takes the theme's ink on
        // a surface chip (the core cross plate's rule).
        var value = Scaled(Stock.HealthValue.In(Stock.Panel), k);
        _ = ui.Rect(Tone.Surface, Size.Du(value.Wide), Size.Du(value.Tall))
                .AtRes(UiAnchor.BottomLeft, value.Left, value.Bottom).Layered().Key("target health chip");
        ui.Number(target.Health, Text.Caption, Tone.Text)
            .Width(Size.Du(value.Wide)).Height(Size.Du(value.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, value.Left, value.Bottom).Layered().Key("target health");
    }
}
