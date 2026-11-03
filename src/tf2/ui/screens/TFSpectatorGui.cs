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
/// The spectator's bars (stock's <c>CTFSpectatorGUI</c>,
/// game/client/tf/vgui/tf_spectatorgui.cpp) at stock's own look and
/// layout: spectator.res's topbar at 0 0 f0x84 (the black-bar colour
/// CSpectatorGUI::ApplySchemeSettings paints it,
/// SpectatorGUI.cpp:486-487) carrying the map label (r260 16 240x20,
/// "map: <c>%s1</c>" — UpdateKeyLabels :678-703), the three key-hint rows
/// (SwitchCamMode at 5/80 10, CycleTargetFwd at 5/80 20, CycleTargetRev
/// at 5/80 30, the key boxes east and their texts west — :556-676) and
/// the ClassOrTeamLabel at c-85 68 170x15 ("Press [ key ] to Change
/// Team" from the spectator team, "Change Class" from RED or BLU,
/// :492-554); and the bottombarblank at 0 r70 f0x70 (:50-68). The bars
/// ride while the local player is observing, and nothing while playing.
///
/// <para>
/// Stock's plain GUI shows no observed-player line
/// (<c>ShouldShowPlayerLabel</c> returns false, tf_spectatorgui.h:64) —
/// the name-and-health rows are the tournament GUI's player panels
/// (deferred) and the coach's student health. The bottom bar's tip
/// (<c>g_TFTips.GetNextClassTip</c>, :467-490) needs a tips system this
/// build does not carry: the bar draws bare.
/// </para>
/// </summary>
public sealed class TFSpectatorGui(IObserverSource observer, ITFLocalPlayer local,
                                   IUiLocalisation localise, IUiKeyBindingStore keys,
                                   ITFMapInfoSource maps) : UiScreen
{
    // Stock's own name (spectator.res:14-16 answers the core specgui with an
    // empty block, suppressing the base panel): the core SpecGui is stock's
    // "Map CTF_2FORT / death cam" chrome, which TF2 does not draw. Taking
    // its name drops it in the composition, the core's slot on this layer.
    public override string Name => "specgui";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>The bars are backdrop the HUD reads over: stock paints the
    /// panel's own background transparent and only the two bars black
    /// (SpectatorGUI.cpp:485-487), and the panel sets no z-pos of its own
    /// (its sole SetZPos is the CSP scope guarantee, :494), so the hud
    /// elements — the clock, the kill feed, the respawn hint — draw over
    /// it. Order -1 joins this screen ahead of the Order-0 hud screens, the
    /// scope's place.</summary>
    public override int Order => -1;

    /// <summary>Stock's panel rides while the viewport has it shown — the
    /// player is observing (the death cam included; the reinforcements
    /// label lives on it, UpdateReinforcements :341-356).</summary>
    public override bool Visible => observer.Mode != SpectatorMode.None;

    public override void Describe(ref UiFrame ui)
    {
        // The top bar (0 0 f0x84): the black plate and its labels.
        var top = Stock.TopBar.Rect(StockRect.Screen);
        var topAt = StockRes.Spot(top, StockRect.Screen);
        using (ui.Box().AtRes(UiAnchor.TopLeft, 0, 0)
                       .Size(Size.Fill, Size.Du(topAt.Tall)).Layered().Key("top bar"))
        {
            ui.Rect(BlackBar, Size.Fill, Size.Du(topAt.Tall)).Layered().Key("top plate");

            // "map: <name>" (UpdateKeyLabels :678-703) — the token's %s1
            // takes the map's display name, stock's GetMapDisplayName
            // (tf_gamerules.cpp:18974) as the map-info source serves it.
            var map = StockRes.Spot(Stock.Map.Rect(top), top);
            ui.Data((localise.Find("#TF2_Spec_Map") ?? "").Replace("%s1", maps.MapName),
                    Text.Body, Tone.Text)
                .Width(Size.Du(map.Wide)).TextAlign(Align.End)
                .AtRes(UiAnchor.TopRight, map.Right, map.Top).Layered().Key("map");

            // The key-hint rows: the key token's %<command>% takes the
            // bound key, its text stands alone (:556-676).
            KeyHint(ref ui, top, Stock.SwitchKey, "#TF2_Spectator_SwitchCamModeKey", "%jump%", "jump", "switch key");
            Hint(ref ui, top, Stock.SwitchText, "#TF2_Spectator_SwitchCamMode", "switch cam");
            KeyHint(ref ui, top, Stock.FwdKey, "#TF2_Spectator_CycleTargetFwdKey", "%attack%", "attack", "cycle fwd key");
            Hint(ref ui, top, Stock.FwdText, "#TF2_Spectator_CycleTargetFwd", "cycle fwd");
            KeyHint(ref ui, top, Stock.RevKey, "#TF2_Spectator_CycleTargetRevKey", "%attack2%", "attack2", "cycle rev key");
            Hint(ref ui, top, Stock.RevText, "#TF2_Spectator_CycleTargetRev", "cycle rev");

            // "Press [ key ] to Change Team" from the spectator team,
            // "to Change Class" from RED or BLU (:492-554).
            bool onTeam = local.Team is Red or Blue;
            string ask = onTeam ? "#TF2_Spectator_ChangeClass" : "#TF2_Spectator_ChangeTeam";
            string command = onTeam ? "%changeclass%" : "%changeteam%";
            var classOrTeam = StockRes.Spot(Stock.ClassOrTeam.Rect(top), top);
            ui.Data((localise.Find(ask) ?? "").Replace(command, keys.KeyFor(onTeam ? "changeclass" : "changeteam") ?? ""),
                    Text.Caption, Tone.Text)
                .Width(Size.Du(classOrTeam.Wide)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.Top, classOrTeam.CentreX, classOrTeam.Top).Layered().Key("class or team");
        }

        // The bottom bar (0 r70 f0x70): the black band stock parks the
        // tip on; no tips system in this build, the band draws bare.
        var bottom = Stock.BottomBar.Rect(StockRect.Screen);
        var bottomAt = StockRes.Spot(bottom, StockRect.Screen);
        using (ui.Box().AtRes(UiAnchor.BottomLeft, 0, 0)
                       .Size(Size.Fill, Size.Du(bottomAt.Tall)).Layered().Key("bottom bar"))
        {
            ui.Rect(BlackBar, Size.Fill, Size.Du(bottomAt.Tall)).Layered().Key("bottom plate");
        }
    }

    /// <summary>One key-hint row's key box: the token's placeholder in
    /// place of the bound key, east-aligned at its .res box.</summary>
    private void KeyHint(ref UiFrame ui, StockRect top, StockBox box, string token, string placeholder,
                         string command, string key)
    {
        var at = StockRes.Spot(box.Rect(top), top);
        ui.Data((localise.Find(token) ?? "").Replace(placeholder, keys.KeyFor(command) ?? ""),
                Text.Caption, Tone.Text)
            .Width(Size.Du(at.Wide)).TextAlign(Align.End)
            .AtRes(UiAnchor.TopLeft, at.Left, at.Top).Layered().Key(key);
    }

    /// <summary>One key-hint row's text box: west-aligned at its own .res
    /// box (the rows' texts at 80, their keys at 5).</summary>
    private void Hint(ref UiFrame ui, StockRect top, StockBox box, string token, string key)
    {
        var at = StockRes.Spot(box.Rect(top), top);
        ui.Text(token, Text.Caption, Tone.Text)
            .Width(Size.Du(at.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.TopLeft, at.Left, at.Top).Layered().Key(key);
    }

    /// <summary>The .res boxes on the 640x480 canvas spectator.res is
    /// written in ($WIN32): the topbar 0 0 f0x84 (:17-32), the
    /// bottombarblank 0 r70 f0x70 (:50-68) and the labels (:69-323). "f0"
    /// is the canvas's own width; the map label reaches its right edge 260
    /// units from the screen's, the key rows their keys at 5 and their
    /// texts at 80, the class-or-team label centred 68 units down.</summary>
    internal static class Stock
    {
        public static readonly StockBox TopBar = new("0", "0", 640, 84);
        public static readonly StockBox BottomBar = new("0", "r70", 640, 70);

        public static readonly StockBox Map = new("r260", "16", 240, 20);
        public static readonly StockBox SwitchKey = new("5", "10", 60, 20);
        public static readonly StockBox SwitchText = new("80", "10", 125, 20);
        public static readonly StockBox FwdKey = new("5", "20", 60, 20);
        public static readonly StockBox FwdText = new("80", "20", 125, 20);
        public static readonly StockBox RevKey = new("5", "30", 60, 20);
        public static readonly StockBox RevText = new("80", "30", 125, 20);
        public static readonly StockBox ClassOrTeam = new("c-85", "68", 170, 15);
    }

    // The raw TF team numbers (tf_shareddefs.h): 2 RED, 3 BLU.
    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>The bars' fill: GetBlackBarColor's TF override, the opaque
    /// 52 48 45 (tf_spectatorgui.h:68) ApplySchemeSettings paints the bars
    /// with (SpectatorGUI.cpp:486-487) — the world shows through nothing.</summary>
    public static readonly UiPaint BlackBar = UiPaint.FromGame(new UiColor(52, 48, 45, 255));
}
