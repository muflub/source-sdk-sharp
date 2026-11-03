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
/// The chat feed at stock's spot (tf_hud_chat.cpp through CBaseHudChat):
/// hudlayout.res's HudChat panel is xpos 10 ypos 275, 320x120 (:980-992) —
/// the box's left edge 10 units in, its bottom 85 up
/// (480 − 275 − 120, written bottom-referenced as the disguise's r-ypos is,
/// so the feed hugs the bottom whatever the canvas's height) — over the
/// class art's shoulder as stock's is. The speaker in their team's colour,
/// the (TEAM) and *DEAD* tags, each line fading as the game says, all on
/// one translucent surface (PaintBackgroundType 2). The core hud's chat
/// screen hangs its block off the HL2 vitals' height, which the TF2 corners
/// stand taller than; this screen takes the feed and draws it here instead.
/// </summary>
public sealed class ChatPanel(IChatSource chat) : UiPanel
{
    /// <summary>The HudChat box itself (hudlayout.res:986-990): the .res's
    /// top-referenced ypos 275 read from the bottom, the panel's children's
    /// space, resolved at the frame's screen.</summary>
    internal static readonly StockBox Panel = new("10", "r205", 320, 120);

    /// <summary>The newest lines shown (the core feed's count).</summary>
    private const int Lines = 8;

    /// <summary>Anything said: the corner draws nothing over an empty feed.</summary>
    public bool Present => chat.Count > 0;

    public override void Describe(ref UiFrame ui)
    {
        if (chat.Count == 0)
            return;

        var at = Panel.In(StockRes.Screen(ui.Screen));
        // The plate is the box's width, never past the canvas's right edge
        // (the portrait canvas is narrower than the box's 320 units).
        float wide = MathF.Min(at.Wide, ui.DesignWidth - at.Left);
        int first = Math.Max(0, chat.Count - Lines);
        // The feed rides over the corner art by intent — the class portrait's
        // top reaches into the box's lower edge — as the corners themselves do.
        using (ui.Column(Space.S, Align.Start).AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key("chat"))
        using (ui.Panel(Space.M, Space.XS, Elevation.Flat).Width(Size.Du(wide)))
        {
            for (int i = first; i < chat.Count; i++)
            {
                var line = chat.Line(i);
                var tone = line.Team == 3 ? Tone.TeamA : line.Team == 2 ? Tone.TeamB : Tone.Text;
                using (ui.Row(Space.S, Align.Start).Opacity(line.Alpha).Key(i))
                {
                    if (line.Dead)
                        ui.Text("#SS_UI_Chat_Dead", Text.Label, Tone.TextMuted);
                    if (line.TeamOnly)
                        ui.Text("#SS_UI_Chat_Team", Text.Label, Tone.TextMuted);
                    if (line.Speaker.Length > 0)
                        ui.Data(line.Speaker, Text.Body, tone);
                    ui.Data(line.Text, Text.Body).Wrap(wide - 160);
                }
            }
        }
    }
}
