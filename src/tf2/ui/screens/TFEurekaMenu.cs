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
/// The Eureka Effect's teleport menu (stock's CHudEurekaEffectTeleportMenu,
/// game/client/tf/tf_hud_menu_eureka_teleport.cpp): the want the wrench's
/// +reload press laid down, drawn while it stands and the wrench is still the
/// held weapon (CanTeleport, :159-181). Two choices, stock's
/// eEurekaTeleportTargets — the spawn, always; the builder's own exit, when
/// one stands placed (OnTick's availability read, :397-405, the row's state
/// as the build menu's gate is) — and the build menu's cancel sentence, whose
/// binding stock resolves into the label (:128-134).
///
/// <para>
/// A choice is the adapter's Teleport — stock's SendTeleportMessage
/// (:186-209), which spends the want whatever the choice answers, the denied
/// exit included. The keys are the hud input layer's
/// (<see cref="SourceSharp.Client.Vgui.IHudMenuKeys"/>, the ask stock's client
/// mode makes of the element, clientmode_tf.cpp:1548): slot1 and slot2 choose,
/// the rest of the slots eaten while the menu stands (:296-317). The frame is
/// the hudmenu family's plate — this tree carries no eureka .res — with the
/// menu's own two rows over it.
/// </para>
/// </summary>
public sealed class TFEurekaMenu(ITFEurekaMenuSource source, ITFEurekaMenuActions actions,
                                 ITFRenderGroups groups)
    : UiScreen
{
    private readonly ITFEurekaMenuSource _source = source;
    private readonly ITFEurekaMenuActions _actions = actions;

    public override string Name => "tf2eurekamenu";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's ShouldDraw: the want, still under the wrench
    /// (:159-181), and not while the scoreboard locks the hud's group
    /// (the hud menus' ITFRenderGroups arm).</summary>
    public override bool Visible => _source.WantsTeleport && _source.WrenchActive
                                    && groups.GlobalLocked != true;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Column(Space.S, Align.Stretch)
                  .Width(Size.Du(304))
                  .Fill(Tone.Surface).Round(Radius.L).Pad(Space.L, Space.M)
                  .At(UiAnchor.Centre).Raise(Elevation.Overlay).Key("eureka menu"))
        {
            ui.Text("#TF2_EurekaMenu_Title", Text.Heading, Tone.Text).Key("title");

            // The spawn, EUREKA_TELEPORT_HOME: stock's always-available row
            // (OnTick's :394-395).
            Row(ref ui, "home", 1, "#TF2_EurekaMenu_Home", available: true);

            // The exit, EUREKA_TELEPORT_TELEPORTER_EXIT's row: available
            // while the builder's own stands (OnTick's :397-405); unavailable
            // still shows and chooses, stock's denied send (:200-204).
            Row(ref ui, "exit", 2, "#TF2_EurekaMenu_Exit", _source.ExitStanding);

            ui.Data(_source.CancelLabel, Text.Body, Tone.TextMuted).Key("cancel");
        }
    }

    /// <summary>One choice: the key chip's number, the target's name, the
    /// whole row the click target — the row stock's available/unavailable
    /// panel pair is, the press the frame's.</summary>
    private void Row(ref UiFrame ui, string key, int number, string label, bool available)
    {
        using (var row = ui.Row(Space.S, Align.Centre)
                  .Width(Size.Fill).Height(Size.Du(48))
                  .Fill(available ? Tone.SurfaceRaised : Tone.Surface)
                  .Round(Radius.M).Pad(Space.S, Space.M)
                  .Key(key))
        {
            var hit = ui.Interact(row);
            if (hit.Clicked)
                _actions.Teleport(number - 1);   // the chip's 1 is EUREKA_TELEPORT_HOME's 0

            using (row)
            {
                if (key == "home")
                {
                    ui.Number(number, Text.Number, Tone.Accent).Key("home number");
                    ui.Text(label, Text.Body, Tone.Text).Key("home label");
                }
                else
                {
                    ui.Number(number, Text.Number, available ? Tone.Accent : Tone.TextMuted).Key("exit number");
                    ui.Text(label, Text.Body, available ? Tone.Text : Tone.TextMuted).Key("exit label");
                    if (!available)
                        ui.Text("#TF2_BuildMenu_Unavailable", Text.Caption, Tone.TextMuted).Key("exit why");
                }
            }
        }
    }
}
