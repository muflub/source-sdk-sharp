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
/// The engineer's metal account, stock's <c>CHudAccountPanel</c>
/// (game/client/tf/tf_hud_account.cpp): the count the metal dialog
/// variable carries — the local player's <c>TF_AMMO_METAL</c> pool, the
/// account the changed-metal event keeps (c_tf_player.cpp:4799-4813) —
/// drawn only for a living engineer (<c>CHudAccountPanel::ShouldDraw</c>'s
/// <c>IsPlayerClass(TF_CLASS_ENGINEER)</c> and alive arms, :274-294). The
/// layout is hudlayout.res:199-211's element, xpos r162 ypos r152
/// 116x180 — its top 152 units up, its 180-tall canvas hanging 28 past
/// the screen's bottom — with hudaccountpanel.res's visible children on
/// it: the team area art (AccountBG 5,103 84x42, teambg_2 red / teambg_3
/// blue), the metal icon (MetalIcon 19,116 10x10, <c>ico_metal</c> in
/// ProgressOffWhite) and the account (AccountValue 20,112 55x26, the
/// centred <c>%metal%</c> in HudFontMediumSmall).
/// </summary>
public sealed class MetalPanel(ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // Stock's boxes, as the .res writes them; resolved to du per Describe.
    internal static class Stock
    {
        // CHudAccountPanel (hudlayout.res:199-211).
        public static readonly StockBox Panel = new("r162", "r152", 116, 180);

        // AccountBG, MetalIcon and AccountValue (hudaccountpanel.res:14-74).
        public static readonly StockBox Bg = new("5", "103", 84, 42);
        public static readonly StockBox Icon = new("19", "116", 10, 10);
        public static readonly StockBox Value = new("20", "112", 55, 26);
    }

    /// <summary>Stock's show arm: a living engineer (ShouldDraw,
    /// tf_hud_account.cpp:274-294).</summary>
    public bool Present => player.ClassName == "Engineer" && player.Health > 0;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        // The canvas hangs 28 units past the screen's bottom; the screen
        // clips it (vgui clips what hangs out), so the children resolve
        // against the visible band — the same spots, the host's bottom up
        // at the edge.
        var panel = StockRes.ClippedToScreen(Stock.Panel.Rect(StockRes.Screen(ui.Screen)));
        var bg = Stock.Bg.In(panel);
        var icon = Stock.Icon.In(panel);
        var value = Stock.Value.In(panel);

        // The team area art (AccountBG teambg_2/3); the plate is the
        // contrast backdrop when the texture library misses the material
        // (the TFHud.cs pattern).
        if (StockArt.Missing(textures, Area(player.Team)))
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Key("metal plate");
        StockArt.Draw(ref ui, textures, "metal bg", Area(player.Team), bg.Left, bg.Bottom, bg.Wide, bg.Tall);

        // The metal icon and the account (hudaccountpanel.res MetalIcon's
        // icon key and AccountValue's %metal%).
        StockArt.DrawIcon(ref ui, textures, "metal icon", "ico_metal", icon.Left, icon.Bottom, icon.Wide, icon.Tall, OffWhite);
        ui.Number(player.Metal, Text.Body, Tone.Text).HudSize()
            .Width(Size.Du(value.Wide)).Height(Size.Du(value.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, value.Left, value.Bottom).Layered().Key("metal account");
    }

    /// <summary>Stock's <c>ico_metal</c> tint (hudaccountpanel.res
    /// iconColor ProgressOffWhite, clientscheme.res).</summary>
    private static readonly UiPaint OffWhite = UiPaint.FromGame(new UiColor(251, 235, 202, 255));

    /// <summary>The team area art (hudaccountpanel.res teambg_2/3: the raw
    /// TF team numbers, 2 RED, 3 BLU; the base image is the blue arm).</summary>
    private static string Area(int team) => team == 3 ? "hud/misc_ammo_area_blue" : "hud/misc_ammo_area_red";
}
