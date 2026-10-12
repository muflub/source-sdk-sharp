using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The scoreboard (stock: CClientScoreBoardDialog, on +showscores): the server
/// and map in a header, then one <see cref="TeamTable"/> per team, side by side
/// on a wide screen and stacked on a narrow one; an empty server says so.
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
        bool scrolls = !wide && StackedHeightDu() > ui.DesignHeight - 2 * (float)Space.Huge;

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
                var teams = wide ? ui.Row(Space.XL, Align.Start)
                          : scrolls ? ui.Scroll("teams", Space.L).Height(Size.Fill)
                          : ui.Column(Space.L);
                using (teams)
                {
                    for (int t = 0; t < _source.TeamCount && t < 2; t++)
                    {
                        var tone = t == 0 ? Tone.TeamA : Tone.TeamB;
                        ui.Sub(t, _table, new TeamTableProps(t, tone)).Width(wide ? Size.Fr() : Size.Fill);
                    }
                }
            }

            ui.Slot("scoreboard.footer", Space.S);
        }
    }

    /// <summary>The stacked panel's height, du: header, then per team its band, column header and rows.</summary>
    private float StackedHeightDu()
    {
        float h = 140;
        for (int t = 0; t < _source.TeamCount && t < 2; t++)
            h += 110;
        return h + 36 * _source.PlayerCount;
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
