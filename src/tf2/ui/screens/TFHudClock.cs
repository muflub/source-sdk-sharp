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
/// The round clock as stock's <c>CTFHudTimeStatus</c> draws it under the
/// match HUD (tf_time_panel.cpp): the digits in the res's if_match
/// TimePanelValue — centred, HudFontSmall (hudmatchstatus.res:342-353) —
/// over the backdrop and centre art the match status layers hang
/// (<see cref="TFHud"/>; the plain TimePanelBG box and the progress ring
/// are the hudobjectivetimepanel arms the if_match blocks hide,
/// :24-27, :54-57), and the state bands as the labels alone — the res's
/// band images wide 0 under if_match, the text full-width at y21 in
/// HudFontSmallestBold (hudobjectivetimepanel.res if_match blocks): setup
/// and overtime (SetupBG/OvertimeBG), sudden death for a stalemate
/// (SetExtraTimePanels :489-508, InStalemate; no arena mode to exclude),
/// waiting for players (:573-589, which hides the setup band — "can't be
/// waiting *AND* in setup"), and the server-time pair (:591-601,
/// :741-808) when tf_hud_show_servertimelimit is set, the round timer
/// states a max length and mp_timelimit is on — its label is GetTimeLeft
/// as m:ss or h:mm:ss, or #TF_HUD_ServerChangeOnRoundEnd once the limit
/// is spent. Overtime hides sudden death (:547-554); the four round
/// states win over the server band, which stock draws over them at the
/// same ypos — one band reads instead. The overtime and sudden-death
/// labels pulse red — OvertimeLabelPulseRed/SuddenDeathLabelPulseRed
/// (hudanimations_tf.txt:551-565), the same 0.6 s see-saw. The numbers
/// are the <c>m:ss</c> of the time on show (<c>"%d:%02d"</c>, OnThink
/// :720-724): remaining, or — for a timer that does not show the
/// remaining (:696-699) — the elapsed seconds of the round, never below
/// zero (:708-717). The match HUD is stock's default
/// (<c>tf_use_match_hud</c> 1); no read carries the convar yet.
/// </summary>
public sealed class RoundTimerPanel(ITFRoundTimer timer) : UiPanel
{
    // The .res's own panel: ObjectiveStatusTimePanel (hudmatchstatus.res:
    // 276-292) — the children's coordinate space; TFHud places the panel
    // there (hudlayout.res's HudMatchStatus is full-screen), resolved at
    // the frame's screen.
    internal static readonly StockBox Panel = new("c-55", "0", 110, 150);

    // The if_match value: centred, 45x10 at y12 (hudmatchstatus.res:342-353).
    private const float ValueWide = 45f;
    private const float ValueTall = 10f;
    private const float ValueTop = 12f;

    // The bands' label row: full-width at y21 (hudobjectivetimepanel.res
    // if_match); the box is the label's line, the text centred in it.
    private const float BandTop = 21f;
    private const float BandTall = 10f;

    /// <summary>The band box's slack each side of the panel: the longest
    /// label (WAITING FOR PLAYERS, HudFontSmallestBold) runs a few du past
    /// the 110-wide panel at the preview sizes.</summary>
    private const float BandSlack = 6f;

    // The pulse's two scheme colours (clientscheme.res:100 TanLight, :49 RedSolid).
    private static readonly UiColor TanLight = new(235, 226, 202, 255);
    private static readonly UiColor RedSolid = new(192, 28, 0, 255);

    /// <summary>OvertimeLabelPulseRed (hudanimations_tf.txt:551-557): the
    /// label see-saws RedSolid up to TanLight and back, 0.3 s each way on
    /// the 0.6 s loop, the ramps linear.</summary>
    internal static UiColor OvertimePulse(double now)
    {
        float p = (float)(now % 0.6);
        float k = p < 0.3f ? p / 0.3f : 1f - (p - 0.3f) / 0.3f;   // 0 RedSolid .. 1 TanLight
        return new UiColor((byte)MathF.Round(RedSolid.R + (TanLight.R - RedSolid.R) * k),
                           (byte)MathF.Round(RedSolid.G + (TanLight.G - RedSolid.G) * k),
                           (byte)MathF.Round(RedSolid.B + (TanLight.B - RedSolid.B) * k),
                           255);
    }

    public override void Describe(ref UiFrame ui)
    {
        if (!timer.Present)
            return;                                   // no round timer up to show

        // The bands: waiting for players hides the setup band ("can't be
        // waiting *AND* in setup", tf_time_panel.cpp:578-588); overtime
        // turns the sudden-death one off (:547-554).
        bool wfp = timer.WaitingForPlayers;
        bool overtime = timer.Overtime && !wfp;
        bool sudden = timer.Stalemate && !wfp && !overtime;
        bool setup = timer.InSetup && !wfp;

        if (wfp)
            Band(ref ui, "#TF2_Hud_WaitingForPlayers", pulse: false, "waiting");
        else if (setup)
            Band(ref ui, "#TF2_Hud_Setup", pulse: false, "setup");
        else if (overtime)
            Band(ref ui, "#TF2_Hud_Overtime", pulse: true, "overtime");
        else if (sudden)
            Band(ref ui, "#TF2_Hud_SuddenDeath", pulse: true, "sudden death");
        else if (timer.ShowServerLimit && timer.ServerTimeLimit > 0 && timer.TotalSeconds > 0)
        {
            // GetTimeLeft (teamplayroundbased_gamerules.cpp:1226-1239, the
            // map reset time plus mp_timelimit minus now, floored at zero):
            // the change-on-round-end line once spent (tf_time_panel.cpp
            // :784-789), then m:ss or h:mm:ss (:791-804).
            int left = timer.ServerTimeLeft;
            if (left == 0)
                Band(ref ui, "#TF2_Hud_ServerChangeOnRoundEnd", pulse: false, "server limit");
            else
                BandTime(ref ui, left / 3600, left % 3600 / 60, left % 60);
        }

        // The digits, in the if_match value: centred, y12 (hudmatchstatus
        // :342-353). The m:ss of the seconds on show (:692-724), never
        // below zero (:708-717).
        var panel = Panel.Rect(StockRes.Screen(ui.Screen));
        var value = StockRes.Spot(new StockRect(
            panel.Left + (panel.Wide - ValueWide) / 2f,
            panel.Bottom + panel.Tall - ValueTop - ValueTall, ValueWide, ValueTall), panel);
        int shown = System.Math.Max(0, timer.ShowTimeRemaining
            ? timer.SecondsLeft
            : timer.TotalSeconds - timer.SecondsLeft);
        // HudFontSmall (hudmatchstatus.res:352): stock's 14 tall scaled with
        // the screen — ~13 tall at 720 lines, the ClockDigits step.
        ui.Text($"{shown / 60}:{shown % 60:00}", Text.Label, Tone.Text).TextSize(TFHudType.ClockDigits)
            .Width(Size.Du(value.Wide)).Height(Size.Du(value.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, value.Left, value.Bottom).Layered().Key("clock");
    }

    /// <summary>The server-time band's counted-down label, the interpolation
    /// the lint reads as data (the hours arm names h:mm:ss, tf_time_panel.cpp
    /// :795-806).</summary>
    private void BandTime(ref UiFrame ui, int hours, int minutes, int seconds)
    {
        var text = hours == 0
            ? ui.Text($"{minutes}:{seconds:00}", Text.Heading, Tone.Text).BandSize()
            : ui.Text($"{hours}:{minutes:00}:{seconds:00}", Text.Heading, Tone.Text).BandSize();
        Place(ref ui, text, "server limit");
    }

    /// <summary>A band's label over the backdrop, full-width at y21 in the
    /// res's HudFontSmallestBold (hudobjectivetimepanel.res if_match) —
    /// TF2 Build 11, 24.75 du, so the band takes the hud's bold display face
    /// (<see cref="TFHudType"/>'s BandSize), not the body face; the Title
    /// role's drop shadow is what keeps it legible where it hangs below the
    /// BGFrame's edge, over the world (clientscheme.res's hud faces carry
    /// the same shadow their labels read by). The pulsing labels (Overtime
    /// and SuddenDeath, hudanimations_tf.txt :551-565) see-saw red over the
    /// dark backdrop.</summary>
    private void Band(ref UiFrame ui, string label, bool pulse, string key)
    {
        var text = ui.Text(label, Text.Title, Tone.Text).BandSize();
        if (pulse)
            text = text.Color(UiPaint.FromGame(OvertimePulse(ui.Now)));
        Place(ref ui, text, key);
    }

    /// <summary>A band label's box: the panel's full width at y21.</summary>
    private void Place(ref UiFrame ui, UiBox text, string key)
    {
        var panel = Panel.Rect(StockRes.Screen(ui.Screen));
        var band = StockRes.Spot(new StockRect(
            panel.Left - BandSlack / 2f, panel.Bottom + panel.Tall - BandTop - BandTall, panel.Wide + BandSlack, BandTall), panel);
        text.Width(Size.Du(band.Wide)).Height(Size.Du(band.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, band.Left, band.Bottom).Layered().Key(key);
    }
}
