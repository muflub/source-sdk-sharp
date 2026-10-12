using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// THE KILL FEED (stock CHudDeathNotice, hl2mp/hud_deathnotice.cpp), top
/// right, oldest line first: the killer's name in their team's colour, the
/// weapon's kill icon, the victim's name in theirs; a suicide or a world death
/// has no killer and the skull. Each line sits on the window tone (opaque: the theme's
/// contrast rule; stock drew bare text) and a line the local player is in is
/// outlined in the accent (stock HL2MP did not mark it; TF's feed does).
///
/// <para>
/// THE ICONS are the UI's own icon set (src/client/ui/icons: original art,
/// user ruling 2026-10-05), drawn in stock's icon colour (orange, 255 80 0:
/// Tone.Warning). Stock HL2MP draws no headshot mark (m_iconD_headshot is
/// never loaded or drawn), so neither does this.
/// </para>
/// </summary>
public sealed class KillFeedPanel(IKillFeedSource feed) : UiPanel
{
    public override void Describe(ref UiFrame ui)
    {
        var f = feed;
        if (f.LineCount == 0)
            return;

        // Stock: right-justified 16 px (at 480) from the right edge, from
        // YRES(16) down; here 32 du right and 48 du down, below the debug line.
        using (ui.Column(Space.XS, Align.End).At(UiAnchor.TopRight).Inset(Space.XXL, Space.Huge))
        {
            for (int i = 0; i < f.LineCount; i++)
            {
                var l = f.Line(i);
                using (ui.Row(Space.S, Align.Centre).Key(i).Fill(Tone.Window).Round(Radius.M).Pad(Space.M, Space.S)
                         .Border(l.Local ? Tone.Accent : Tone.None, l.Local ? 2 : 0))
                {
                    if (!l.Suicide)
                        ui.Data(l.Killer, Text.Body, UiTeams.ToneOf(l.KillerTeam, Tone.Text)).Elide().MaxWidth(280).Name("killer");
                    ui.Icon(l.Icon, Tone.Warning, 24).Name("icon");
                    ui.Data(l.Victim, Text.Body, UiTeams.ToneOf(l.VictimTeam, Tone.Text)).Elide().MaxWidth(280).Name("victim");
                }
            }
        }
    }
}
