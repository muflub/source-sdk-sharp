using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The party window (docs/plans/descent_ui.md §2.11, R8/R26; plan_descent_logic
/// WO9): up to four member cards (class, level, and the <see cref="PartyPill"/>
/// in its window (Focus) variant, which carries life, the downed caption with
/// the distance and the hardcore flag), the leader tagged and, for the leader,
/// a Kick on each other card; the invitation waiting, with Decline and Accept;
/// the heroes on this server the local hero may invite, each with Invite; and
/// Leave. Every action is a client verb the server decides. Opened with P or
/// from the HUD's pill row.
/// </summary>
public sealed class Party : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IDescentParty _party;
    private readonly IDescentPartyRoster _roster;
    private readonly IDescentPartyActions _actions;
    private readonly PartyPill _pill;

    /// <summary>The most heroes a server holds: one key each for the invite and kick buttons, made once.</summary>
    private const int MaxRows = 64;

    private readonly string[] _kickKeys = new string[MaxRows];
    private readonly string[] _inviteKeys = new string[MaxRows];

    public Party(UiWindows windows, IDescentParty party, IDescentPartyRoster roster, IDescentPartyActions actions, PartyPill pill)
    {
        _windows = windows;
        _party = party;
        _roster = roster;
        _actions = actions;
        _pill = pill;
        for (int i = 0; i < MaxRows; i++)
        {
            _kickKeys[i] = "kick" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _inviteKeys[i] = "invite" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public override string Name => "party";

    /// <summary>The viewport panel the server opens this screen with (VGUIMenu): <c>descent_&lt;screen&gt;</c> (plan_descent_logic UI1).</summary>

    public override string? Panel => "descent_party";

    public string WindowId => "party";

    /// <summary>The class caption's width, du: enough for the longest class name.</summary>
    public const float ClassDu = 104;

    /// <summary>
    /// Small (680 x 640 du, clamped to the screen): four cards, the invitation,
    /// the invite list (scrolling past a few heroes) and Leave. The minimum keeps
    /// one card with its Kick, the invitation's buttons and the list legible.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(680, MathF.Round(screenW * 0.92f)), MathF.Min(640, MathF.Round(screenH * 0.92f)),
               560, 400, screenW, screenH, 560);

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(_windows, WindowId, "#Descent_Party_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            using (ui.Column(Space.XS, Align.Stretch))
            {
                int members = _party.MemberCount;
                bool leads = _roster.LocalLeads;
                int leader = _roster.LeaderIndex;
                // The member cards: the class and level columns, the pill (its
                // Focus variant: the wide name, the long life meter), the
                // leader's tag, and the leader's Kick on each other member.
                for (int i = 0; i < members && i < MaxRows; i++)
                {
                    var m = _party.Member(i);
                    using (ui.Row(Space.M, Align.Centre).Round(Radius.M).Fill(Tone.Surface).Border(Tone.Line, 1).Pad(Space.XS, Space.S))
                    {
                        // The class, and under it the leader's tag and a member who is not here.
                        using (ui.Column(Space.None).Width(Size.Du(ClassDu)))
                        {
                            ui.Text(m.ClassToken, Text.Caption, Tone.TextMuted).Elide();
                            if (i == leader)
                                ui.Text("#Descent_Party_Leader", Text.Caption, Tone.Accent);
                            if (m.OffLevel)
                                ui.Text("#Descent_Party_OffLevel", Text.Caption, Tone.TextMuted);
                        }
                        ui.Number(m.Level, Text.Number, Tone.TextMuted);
                        ui.Sub(i, _pill, new PartyPillProps(m, Focus: true, Reviving: false)).Width(Size.Fill);
                        if (leads && !m.Local && ui.Button(_kickKeys[i], "#Descent_Party_Kick").Clicked)
                            _actions.Kick(i);
                    }
                }

                // The invitation waiting: who asks, and the answer (accept last, the primary).
                if (_roster.InviterName.Length > 0)
                {
                    using (ui.Row(Space.M, Align.Centre).Round(Radius.M).Fill(Tone.SurfaceRaised).Border(Tone.Accent, 1).Pad(Space.S, Space.M))
                    {
                        using (ui.Column(Space.None).Width(Size.Fill))
                        {
                            ui.Text("#Descent_Party_InvitedBy", Text.Caption, Tone.TextMuted);
                            ui.Data(_roster.InviterName, Text.Body).Elide();
                        }
                        if (ui.Button("decline", "#Descent_Party_Decline").Clicked)
                            _actions.Decline();
                        if (ui.Button("accept", "#Descent_Party_Accept", primary: true).Clicked)
                            _actions.Accept();
                    }
                }

                ui.Divider();
                // The heroes here the local hero may invite: a hero in no party, or the leader with room.
                int candidates = _roster.CandidateCount;
                if (!_roster.CanInvite)
                {
                    ui.Text("#Descent_Party_LeaderInvites", Text.Caption, Tone.TextMuted).Wrap(400);
                }
                else if (candidates == 0)
                {
                    ui.Text("#Descent_Party_InviteLabel", Text.Caption, Tone.TextMuted);
                    ui.Text("#Descent_Party_NoCandidates", Text.Caption, Tone.TextMuted).Wrap(400);
                }
                else
                {
                    ui.Text("#Descent_Party_InviteLabel", Text.Caption, Tone.TextMuted);
                    using (ui.Scroll("candidates", Space.XS, ScrollAxes.Vertical).Width(Size.Fill).Height(Size.Du(152)))
                    {
                        for (int i = 0; i < candidates && i < MaxRows; i++)
                        {
                            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                            {
                                ui.Data(_roster.CandidateName(i), Text.Body).Width(Size.Fill).Elide();
                                if (ui.Button(_inviteKeys[i], "#Descent_Party_Invite").Clicked)
                                    _actions.Invite(i);
                            }
                        }
                    }
                }

                ui.Flex();
                var leave = ui.Button("leave", "#Descent_Party_Leave", enabled: leader >= 0, width: Size.Fill);
                if (leave.Clicked)
                    _actions.Leave();
            }
        }
    }
}
