using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The team menu (stock: CTeamMenu, on the server's VGUIMenu "team"): a modal
/// dialog with one card per team (colour, name, head count, score, Join), then
/// Spectate, Cancel and the primary Auto assign. A full team's card says so and
/// cannot be joined; the team you are on is marked. Slot: <c>teammenu.footer</c>.
///
/// <para>
/// Display and hit-testing are here; what a click does (the jointeam command)
/// is U3's: <see cref="Chosen"/> records the choice for it.
/// </para>
/// </summary>
public sealed class TeamMenu : UiScreen
{
    private readonly ITeamMenuSource _source;
    private readonly TeamCard _card;
    private readonly ITeamMenuActions? _actions;

    /// <param name="actions">What a choice does (the core: jointeam); none in a sample.</param>
    public TeamMenu(ITeamMenuSource source, TeamCard card, ITeamMenuActions? actions = null)
    {
        _source = source;
        _card = card;
        _actions = actions;
    }

    public override string Name => "teammenu";

    /// <summary>The team number last clicked (0 auto assign, 1 spectate, -1 none yet).</summary>
    public int Chosen { get; private set; } = -1;

    private void Choose(int team)
    {
        Chosen = team;
        _actions?.Choose(team);
    }

    public override void Describe(ref UiFrame ui)
    {
        // Narrow: the cards stack, in a dialog no wider than the screen allows.
        bool wide = !ui.Narrow;

        using (ui.Dialog("teammenu", "#SS_UI_TeamMenu_Title", wide ? 800 : MathF.Min(560, ui.DesignWidth - 64)))
        {
            using (ui.Row(Space.S))
            {
                ui.Text("#SS_UI_TeamMenu_Subtitle", Text.Body, Tone.TextMuted);
                ui.Flex();
                ui.Data(_source.MapName, Text.Label, Tone.TextMuted);
            }

            var cards = wide ? ui.Row(Space.L, Align.Stretch) : ui.Column(Space.M);
            using (cards.Width(Size.Fill))
            {
                for (int i = 0; i < _source.ChoiceCount; i++)
                {
                    var choice = _source.Choice(i);
                    var props = new TeamCardProps(choice, i == 0 ? Tone.TeamA : Tone.TeamB, choice.Number == _source.CurrentTeam);
                    ui.Sub(choice.Number, _card, props).Width(wide ? Size.Fr() : Size.Fill);
                    if (_card.Clicked)
                        Choose(choice.Number);
                }
            }

            ui.Slot("teammenu.footer", Space.S);

            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("spectate", "#SS_UI_TeamMenu_Spectate").Clicked)
                    Choose(1);
                ui.Flex();
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                {
                    Chosen = -1;
                    _actions?.Cancel();
                }
                if (ui.Button("auto", "#SS_UI_TeamMenu_Auto", primary: true).Clicked)
                    Choose(0);
            }
        }
    }
}

/// <summary>What one team card shows.</summary>
public readonly record struct TeamCardProps(TeamChoice Choice, Tone Tone, bool Current);

/// <summary>
/// One team on the team menu: a band in the team's colour, the name, players
/// and score, and a Join button (or why it cannot be joined). The whole card is
/// the click target; it lifts on hover.
/// </summary>
public sealed class TeamCard : UiPanel<TeamCardProps>
{
    /// <summary>Whether the last described card was clicked.</summary>
    public bool Clicked { get; private set; }

    public override void Describe(ref UiFrame ui, in TeamCardProps p)
    {
        var c = p.Choice;
        var card = ui.Column(Space.None).Width(Size.Fill).Fill(Tone.SurfaceRaised).Round(Radius.L).Clip().Key("card");
        var hit = c.Full ? default : ui.Interact(card);
        Clicked = hit.Clicked;
        float lift = ui.Animate(card, hit.Hovered ? 1f : 0f, Motion.Quick);
        card.Border(((UiPaint)p.Tone).WithOpacity(0.35f + 0.65f * lift), 2);

        using (card)
        {
            ui.Rect(p.Tone, Size.Fill, Size.Du(8));
            using (ui.Column(Space.L).Pad(Space.XL, Space.XL).Width(Size.Fill))
            {
                using (ui.Row(Space.S).Width(Size.Fill))
                {
                    ui.Data(c.Name, Text.Title, p.Tone);
                    ui.Flex();
                    if (p.Current)
                        ui.Text("#SS_UI_TeamMenu_Current", Text.Label, Tone.Text);
                }

                using (ui.Row(Space.Huge))
                {
                    Stat(ref ui, c.Players, "#SS_UI_Players");
                    Stat(ref ui, c.Score, "#SS_UI_Score");
                }

                using (ui.Box().Width(Size.Fill).Height(Size.Du(40)).Round(Radius.M)
                         .Fill(c.Full ? (UiPaint)Tone.Surface : ((UiPaint)p.Tone).WithOpacity(0.18f + 0.22f * lift)))
                {
                    ui.Text(c.Full ? "#SS_UI_TeamMenu_Full" : "#SS_UI_TeamMenu_Join", Text.Heading, c.Full ? Tone.TextMuted : Tone.Text)
                      .At(UiAnchor.Centre);
                }
            }
        }
    }

    private static void Stat(ref UiFrame ui, int value, string label)
    {
        using (ui.Column(Space.None, Align.Start))
        {
            ui.Number(value, Text.Heading, Tone.Text);
            ui.Label(label);
        }
    }
}
