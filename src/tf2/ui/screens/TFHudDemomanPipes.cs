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
/// The demoman's sticky-bomb counter, stock's <c>CHudDemomanPipes</c>
/// (tf_hud_demomanpipes.cpp): the count beside the sticky icon, drawn from
/// huddemomanpipes.res inside HudDemomanPipes (hudlayout.res:1045-1058,
/// r162 r52 100x50). OnTick's pair (:138-146) is the count's two arms —
/// bombs out shows it by the live icon, the empty tray keeps the corner with
/// the same label dull by the faded one; the charge-meter sub-mode
/// (ChargeLabel/ChargeMeter, :147-206) waits on the demo shields, which have
/// no port to equip.
/// </summary>
public sealed class DemomanPipesPanel(ITFDemomanPipes pipes, ITFLocalPlayer player,
                                      IUiTextureLibrary textures) : UiPanel
{
    // Stock's art boxes, as the .res writes them, against HudDemomanPipes's
    // own 100x50 panel (hudlayout.res:1045-1058); resolved to du per Describe.
    private static class Stock
    {
        // The panel: xpos r162, ypos r52, 100x50 (hudlayout.res:1045-1058).
        public static readonly StockRect Panel = new StockBox("r162", "r52", 100, 50).Rect(StockRect.Screen);

        // background: xpos 12, ypos 6, 76x38 (huddemomanpipes.res:3-22).
        public static readonly StockBox Bg = new("12", "6", 76, 38);

        // PipeIcon: xpos 26, ypos 16, 20x20 (:79-96; the empty arm's faded
        // art :162-179).
        public static readonly StockBox Icon = new("26", "16", 20, 20);

        // NumPipesLabel: xpos 50, ypos 18, 35x20 (:98-112), the count west of
        // its box — both arms carry the label, the value alone changing.
        public static readonly StockBox Count = new("50", "18", 35, 20);
    }

    /// <summary>Whether the corner stands at all: stock's ShouldDraw
    /// (tf_hud_demomanpipes.cpp:102-122), the read the screen gates the
    /// sub on.</summary>
    public bool Present => pipes.Present;

    public override void Describe(ref UiFrame ui)
    {
        int count = pipes.Count;
        bool any = count > 0;
        var bg = Stock.Bg.In(Stock.Panel);
        var icon = Stock.Icon.In(Stock.Panel);
        var countBox = Stock.Count.In(Stock.Panel);

        if (StockArt.Missing(textures, Bg(player.Team)))
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Key("pipes plate");
        StockArt.Draw(ref ui, textures, "pipes bg", Bg(player.Team), bg.Left, bg.Bottom, bg.Wide, bg.Tall);
        StockArt.Draw(ref ui, textures, "pipes icon", Icon(any, player.Team), icon.Left, icon.Bottom, icon.Wide, icon.Tall);

        // The label both arms share; dull when the tray is empty (the res's
        // dulltext, :200/:224).
        ui.Number(count, Text.Number, any ? Tone.Text : Tone.TextMuted).HudSize()
            .Width(Size.Du(countBox.Wide)).Height(Size.Du(countBox.Tall)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, countBox.Left, countBox.Bottom).Layered().Key("pipes count");
    }

    /// <summary>The team bg (huddemomanpipes.res teambg_2/3: the raw TF
    /// team numbers, 2 RED, 3 BLU).</summary>
    private static string Bg(int team) => team == 3 ? "hud/misc_ammo_area_blue" : "hud/misc_ammo_area_red";

    /// <summary>The icon's two arms: live while bombs out, faded else
    /// (the res's *_faded images, :92-95 and :175-178).</summary>
    private static string Icon(bool any, int team) => (any, team) switch
    {
        (true, 3) => "hud/ico_stickybomb_blue",
        (true, _) => "hud/ico_stickybomb_red",
        (_, 3) => "hud/ico_stickybomb_blue_faded",
        _ => "hud/ico_stickybomb_red_faded",
    };
}
