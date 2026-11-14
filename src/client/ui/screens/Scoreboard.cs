using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The scoreboard (stock: CClientScoreBoardDialog, on +showscores): the server
/// and map in a header, then one <see cref="TeamTable"/> per team, side by side
/// on a wide screen (two to a row when team deathmatch runs three or four) and
/// stacked on a narrow one; an empty server says so.
/// Slot: <c>scoreboard.footer</c>.
/// </summary>
public sealed class Scoreboard : UiScreen
{
    private readonly IScoreboardSource _source;
    private readonly TeamTable _table;

    /// <summary>One <see cref="TeamTable"/> serves both teams: each is its own Sub scope.</summary>
    public Scoreboard(IScoreboardSource source, TeamTable table)
    {
        _source = source;
        _table = table;
    }

    public override string Name => "scoreboard";

    /// <summary>Shown while TAB is held, over play: no cursor until asked for.</summary>
    public override bool WantsMouse => false;

    public override void Describe(ref UiFrame ui)
    {
        bool wide = !ui.Narrow;

        // Narrow: the teams stack in one column; when they are taller than the
        // screen, the panel takes the screen's height and the column scrolls.
        bool scrolls = StackedHeightDu(wide) > ui.DesignHeight - 2 * (float)Space.Huge;

        // A margin all round, so a narrow screen's panel does not meet its edges.
        using (ui.Box().Size(Size.Fill, Size.Fill).Pad(Space.XL, Space.Huge))
        using (ui.Panel(wide ? Space.XL : Space.L, Space.L, Elevation.Overlay).At(UiAnchor.Top)
                 .Size(Size.Fill, scrolls ? Size.Fill : Size.Auto).MaxWidth(1240).Name("scoreboard panel"))
        {
            Header(ref ui);
            ui.Divider();

            if (_source.PlayerCount == 0)
            {
                ui.EmptyState(UiShape.Person, "#SS_UI_Empty_Title", "#SS_UI_Empty_Body").Width(Size.Fill);
            }
            else
            {
                int count = Math.Min(_source.TeamCount, MaxTeams);
                var teams = scrolls ? ui.Scroll("teams", Space.L).Height(Size.Fill) : ui.Column(Space.L);
                using (teams.Width(Size.Fill))
                {
                    if (!wide)
                    {
                        for (int t = 0; t < count; t++)
                            ui.Sub(t, _table, Props(t)).Width(Size.Fill);
                    }
                    else
                    {
                        // Two teams side by side, three or four two to a row: a
                        // third team's table keeps a column's width, not the row's.
                        int across = count <= 2 ? Math.Max(count, 1) : 2;
                        for (int row = 0; row < count; row += across)
                        {
                            // Keyed apart from the tables' Sub keys (the team indices).
                            using (ui.Row(Space.XL, Align.Start).Width(Size.Fill).Key(-1 - row))
                            {
                                for (int t = row; t < row + across; t++)
                                {
                                    if (t < count)
                                        ui.Sub(t, _table, Props(t)).Width(Size.Fr());
                                    else
                                        ui.Spacer(Size.Fr(), Size.Du(1));
                                }
                            }
                        }
                    }
                }
            }

            ui.Slot("scoreboard.footer", Space.S);
        }
    }

    /// <summary>The most tables the board draws: team deathmatch's four teams.</summary>
    public const int MaxTeams = 4;

    /// <summary>A team's table: its tone by its number, deathmatch's one table the first team tone.</summary>
    private TeamTableProps Props(int t)
        => new(t, UiTeams.ToneOf(_source.Team(t).Number, t == 0 ? Tone.TeamA : Tone.TeamB));

    /// <summary>
    /// The panel's height, du: the header, then per row of tables its band and
    /// column header and its longest table's rows (one table a row when stacked).
    /// </summary>
    private float StackedHeightDu(bool wide)
    {
        int count = Math.Min(_source.TeamCount, MaxTeams);
        int across = wide ? 2 : 1;
        float h = 140;
        for (int row = 0; row < count; row += across)
        {
            int rows = 0;
            for (int t = row; t < row + across && t < count; t++)
                rows = Math.Max(rows, Members(_source.Team(t).Number));
            h += 110 + 36 * rows;
        }
        return h;
    }

    private int Members(int team)
    {
        int n = 0;
        for (int i = 0; i < _source.PlayerCount; i++)
            if (_source.Player(i).Team == team)
                n++;
        return n;
    }

    private void Header(ref UiFrame ui)
    {
        using (ui.Row(Space.L).Width(Size.Fill))
        {
            using (ui.Column(Space.XS, Align.Start).Width(Size.Fill))
            {
                ui.Data(_source.ServerName, Text.Title).Elide();
                using (ui.Row(Space.S))
                {
                    ui.Label("#SS_UI_Map");
                    ui.Data(_source.MapName, Text.Label, Tone.Text);
                }
            }
            using (ui.Column(Space.XS, Align.End))
            {
                ui.Text($"{_source.PlayerCount} / {_source.MaxPlayers}", Text.Heading).TextAlign(Align.End);
                ui.Label("#SS_UI_Players").TextAlign(Align.End);
            }
        }
    }
}
