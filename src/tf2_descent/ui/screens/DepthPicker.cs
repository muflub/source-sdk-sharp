using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The church's descent picker (docs/plans/descent_ui.md §2.11, R6/D8/D14):
/// the depths 1..maxReached as rows — the depth number big, the tileset's
/// name beside it (D14's mapping arrives on the row as a token), the boss
/// flagged where one lives — and under each reachable row the party in the
/// pill's HUD (mini) variant, since members follow where the leader reached
/// (D8). Rows the party never reached read disabled with the Lock.
/// <see cref="IDescentDescentActions.Descend"/> takes the selected row's
/// depth; the leader decides, so a row past the leader's reach cannot be
/// chosen. A window like the other town screens: Esc closes it before the main
/// menu opens (L8), which a dialog never did.
/// </summary>
public sealed class DepthPicker : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IDescentDescent _descent;
    private readonly IDescentDescentActions _actions;
    private readonly IDescentParty _party;
    private readonly PartyPill _pill;
    private readonly IDescentTownStatus? _status;
    private readonly TownStatusLine _statusLine = new();

    /// <summary>Pills per party row: two compact pills always fit the dialog body.</summary>
    public const int PillsPerRow = 2;

    public DepthPicker(UiWindows windows, IDescentDescent descent, IDescentDescentActions actions, IDescentParty party, PartyPill pill,
                       IDescentTownStatus? status = null)
    {
        _windows = windows;
        _status = status;
        _descent = descent;
        _actions = actions;
        _party = party;
        _pill = pill;
    }

    public override string Name => "depthpicker";

    /// <summary>The viewport panel the server opens this screen with (VGUIMenu): <c>descent_&lt;screen&gt;</c> (plan_descent_logic UI1).</summary>

    public override string? Panel => "descent_depthpicker";

    public string WindowId => "depthpicker";

    /// <summary>The window's width, du: two window-variant pills (288 du each) plus
    /// the body and row padding fit side by side without touching the clip.</summary>
    public const float WidthDu = 680;

    /// <summary>
    /// <see cref="WidthDu"/> wide and up to 720 du tall, clamped to the screen; the
    /// rows scroll past the list's cap, so the floor only keeps a row, the status
    /// line and Descend legible.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(WidthDu, MathF.Round(screenW * 0.92f)), MathF.Min(720, MathF.Round(screenH * 0.92f)),
               560, 420, screenW, screenH, 560);

    /// <summary>The depth column's width, du.</summary>
    public const float DepthDu = 56;

    public override void Describe(ref UiFrame ui)
    {
        // The rows fill what the window leaves between the leader line and the
        // status line and buttons (UI4), and scroll past it.
        using (ui.Window(_windows, WindowId, "#Descent_Depth_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            // D8: the party follows the leader, so the picker names whose reach the rows show.
            using (ui.Row(Space.S, Align.Centre))
            {
                ui.Text("#Descent_Depth_Leader", Text.Body, Tone.TextMuted);
                ui.Data(_descent.PartyLeaderToken, Text.Body, Tone.TextMuted).Elide();
            }
            // The target row: the clicked one when it still answers, else the
            // deepest reachable row; Descend may go there only when the leader
            // reached too (D8).
            ref int sel = ref ui.State<int>("sel");
            int fallback = 0, fallbackOpen = 0;
            for (int i = 0; i < _descent.RowCount; i++)
            {
                var r = _descent.Row(i);
                if (!r.Locked)
                {
                    fallback = r.Depth;
                    if (r.LeaderReached)
                        fallbackOpen = r.Depth;
                }
            }
            bool known = false, open = false;
            for (int i = 0; i < _descent.RowCount; i++)
            {
                var r = _descent.Row(i);
                if (r.Depth != sel)
                    continue;
                known = true;
                open = !r.Locked && r.LeaderReached;
            }
            int target = known ? sel : fallback;

            using (ui.Scroll("rows", Space.S, ScrollAxes.Vertical).Width(Size.Fill).Height(Size.Fill))
            {
                for (int i = 0; i < _descent.RowCount; i++)
                {
                    var r = _descent.Row(i);
                    bool selected = r.Depth == target;
                    var row = ui.Box().Key(i).Width(Size.Fill)
                                  .Fill(selected ? ((UiPaint)Tone.Accent).WithOpacity(0.14f) : (UiPaint)Tone.None)
                                  .Border(selected ? (UiPaint)Tone.Accent : Tone.Line, selected ? 2 : 1)
                                  .Round(Radius.M).Pad(Space.S, Space.M);
                    var hit = ui.Interact(row);
                    if (hit.Clicked && !r.Locked)
                        sel = r.Depth;
                    using (row)
                    {
                        // The box places its children freely; the depth line and
                        // the party row under it need the column between them.
                        using (ui.Column(Space.S, Align.Stretch))
                        {
                            using (ui.Row(Space.M, Align.Centre))
                            {
                                ui.Number(r.Depth, Text.Number, r.Locked ? Tone.TextMuted : Tone.Text)
                                    .Width(Size.Du(DepthDu)).TextAlign(Align.Start);
                                ui.Text(r.TilesetToken, Text.Body, r.Locked ? Tone.TextMuted : Tone.Text)
                                    .Width(Size.Fill).Elide();
                                if (r.Boss)
                                    ui.Text("#Descent_Depth_Boss", Text.Body, Tone.Warning);
                                if (r.Locked)
                                {
                                    ui.Text("#Descent_Depth_Locked", Text.Body, Tone.TextMuted);
                                    ui.Icon(UiShape.Lock, Tone.TextMuted, 16);
                                }
                            }
                            // The members under a row the party can take: they follow
                            // the leader, so they ride along with its reach (D8). Two
                            // window-variant pills fit the body side by side, so all
                            // four members stay readable; the negative sub keys keep
                            // their ids off the rows' own.
                            if (!r.Locked)
                                for (int base0 = 0; base0 < _party.MemberCount; base0 += PillsPerRow)
                                {
                                    using (ui.Row(Space.M, Align.Start))
                                    {
                                        for (int m = base0; m < Math.Min(base0 + PillsPerRow, _party.MemberCount); m++)
                                            ui.Sub(-1 - (i * 16 + m), _pill,
                                                   new PartyPillProps(_party.Member(m), Focus: true, Reviving: false));
                                        ui.Flex();
                                    }
                                }
                        }
                    }
                }
            }
            // UI4: the server's word on the last pick (a closed depth, no level installed).
            if (_status is { } st)
                ui.Sub("status", _statusLine, new TownStatusProps(st.StatusToken, st.StatusData, st.StatusError));
            using (ui.DialogButtons())
            {
                var descend = ui.Button("descend", "#Descent_Depth_Descend", primary: true, enabled: (known ? open : fallbackOpen > 0));
                if (descend.Clicked)
                    _actions.Descend(target);
            }
        }
    }
}
