using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// What the party window asks of the server (docs/plans/descent_ui.md §4 names
/// <c>IDescentPartyActions</c>; the shared sources file does not define it, so
/// this lane owns the narrow form — flagged in the lane report).
/// <see cref="PartyCode"/> is the code the field shows (what the player typed,
/// once keyboard editing lands with input in U3); <see cref="Join"/> hands it
/// to the server.
/// </summary>
public interface IDescentPartyActions
{
    string PartyCode { get; }
    void Invite();
    void Leave();
    void Join(string code);
}

/// <summary>
/// The party window (docs/plans/descent_ui.md §2.11, R8/R26): up to four
/// member cards — class, level, and the <see cref="PartyPill"/> in its
/// window (Focus) variant, which carries life, the downed caption with the
/// distance and the hardcore flag — over the invite/leave row and the party
/// code field with its caption under the field (the form rule: copy below,
/// Apply sends what the field holds). Opened from the HUD's pill row.
/// </summary>
public sealed class Party : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IDescentParty _party;
    private readonly IDescentPartyActions _actions;
    private readonly PartyPill _pill;

    public Party(UiWindows windows, IDescentParty party, IDescentPartyActions actions, PartyPill pill)
    {
        _windows = windows;
        _party = party;
        _actions = actions;
        _pill = pill;
    }

    public override string Name => "party";

    public string WindowId => "party";

    /// <summary>The class caption's width, du: enough for the longest class name.</summary>
    public const float ClassDu = 104;

    /// <summary>
    /// Small (560 x 560 du, clamped to the screen): four cards, a rule, the
    /// code row and the two action rows. The minimum keeps one card, the code
    /// field and the buttons legible (the focus pill with its 160 du name and
    /// 96 du meter beside the class column), and everything inside the client
    /// area at the smallest preview sizes.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(560, MathF.Round(screenW * 0.92f)), MathF.Min(560, MathF.Round(screenH * 0.92f)),
               440, 360, screenW, screenH, 440);

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(_windows, WindowId, "#Descent_Party_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            using (ui.Column(Space.XS, Align.Stretch))
            {
                // The member cards: the class and level columns, then the pill
                // (its Focus variant: the wide name, the long life meter).
                for (int i = 0; i < _party.MemberCount; i++)
                {
                    var m = _party.Member(i);
                    using (ui.Row(Space.M).Round(Radius.M).Fill(Tone.Surface).Border(Tone.Line, 1).Pad(Space.XS, Space.S))
                    {
                        ui.Text(m.ClassToken, Text.Caption, Tone.TextMuted).Width(Size.Du(ClassDu)).Elide();
                        ui.Number(m.Level, Text.Number, Tone.TextMuted);
                        ui.Sub(i, _pill, new PartyPillProps(m, Focus: true, Reviving: false)).Width(Size.Fill);
                    }
                }
                ui.Divider();
                // The code, form-style: the label, the field, and the caption
                // under the field; Apply hands the field's contents to Join.
                using (ui.Column(Space.None, Align.Stretch))
                {
                    ui.Text("#Descent_Party_CodeLabel", Text.Caption, Tone.TextMuted);
                    ui.TextField("code", _actions.PartyCode, "#Descent_Party_CodePlaceholder");
                    ui.Text("#Descent_Party_CodeHint", Text.Caption, Tone.TextMuted);
                }
                // The actions in two fill rows: three buttons in one line do
                // not fit the window's minimum width even with the copy
                // resolved (the narrow fact renders raw tokens), so invite and
                // leave share a line and Join the code its own.
                using (ui.Row(Space.M, Align.Stretch))
                {
                    var invite = ui.Button("invite", "#Descent_Party_Invite", width: Size.Fill);
                    if (invite.Clicked)
                        _actions.Invite();
                    var leave = ui.Button("leave", "#Descent_Party_Leave", width: Size.Fill);
                    if (leave.Clicked)
                        _actions.Leave();
                }
                var apply = ui.Button("apply", "#Descent_Party_Apply", enabled: _actions.PartyCode.Length > 0,
                                      width: Size.Fill);
                if (apply.Clicked)
                    _actions.Join(_actions.PartyCode);
            }
        }
    }
}
