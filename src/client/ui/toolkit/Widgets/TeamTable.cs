using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>Which team a <see cref="TeamTable"/> shows (an index into the source's teams), in which tone.</summary>
public readonly record struct TeamTableProps(int TeamIndex, Tone Tone);

/// <summary>
/// One team of a scoreboard (plan_client_ui §5.8.2): a header band in the team's
/// colour with its name, score and head count; a column header; then a row per
/// player, sorted by score then deaths, the local player highlighted, dead
/// players muted, bots tagged, ping as signal bars. An empty team says so.
/// Embed one per team with <c>ui.Sub(key, table, props)</c>: one instance
/// serves every team (its per-team state is keyed by the Sub scope).
/// </summary>
public sealed class TeamTable : UiPanel<TeamTableProps>
{
    private readonly IScoreboardSource _source;
    private int[] _order = new int[64];

    public TeamTable(IScoreboardSource source) => _source = source;

    /// <summary>The columns every row and the header share: stripe, name, tag, score, deaths, ping.</summary>
    private static ReadOnlySpan<byte> Fixed => [4, 0, 52, 64, 64, 76];   // bytes: a float span literal allocates in Debug

    private static void Columns(Span<UiSize> c)
    {
        var f = Fixed;
        for (int i = 0; i < c.Length; i++)
            c[i] = i == 1 ? Size.Fr() : Size.Du(f[i]);
    }

    private readonly struct ByScore(IScoreboardSource source) : IComparer<int>
    {
        public int Compare(int a, int b)
        {
            var pa = source.Player(a);
            var pb = source.Player(b);
            if (pa.Frags != pb.Frags) return pb.Frags.CompareTo(pa.Frags);
            if (pa.Deaths != pb.Deaths) return pa.Deaths.CompareTo(pb.Deaths);
            return string.CompareOrdinal(pa.Name, pb.Name);
        }
    }

    /// <summary>Signal bars for a ping: 4 under 50 ms, 1 over 150.</summary>
    public static int Bars(int ping) => ping < 50 ? 4 : ping < 90 ? 3 : ping < 150 ? 2 : 1;

    public override void Describe(ref UiFrame ui, in TeamTableProps props)
    {
        var src = _source;
        var team = src.Team(props.TeamIndex);

        int count = 0;
        for (int i = 0; i < src.PlayerCount; i++)
        {
            if (src.Player(i).Team != team.Number)
                continue;
            if (count == _order.Length)
                Array.Resize(ref _order, _order.Length * 2);
            _order[count++] = i;
        }
        // Insertion sort with a struct comparer: Span.Sort turns its comparer
        // into a delegate, an allocation a frame; a team is a handful of rows.
        var by = new ByScore(src);
        for (int i = 1; i < count; i++)
        {
            int v = _order[i], j = i - 1;
            while (j >= 0 && by.Compare(_order[j], v) > 0)
            {
                _order[j + 1] = _order[j];
                j--;
            }
            _order[j + 1] = v;
        }

        Span<UiSize> columns = stackalloc UiSize[6];
        Columns(columns);

        using (ui.Column(Space.None).Width(Size.Fill))
        {
            // The band: team colour stripe, name, head count, score.
            using (ui.Row(Space.M).Pad(Space.M, Space.S).Fill(((UiPaint)props.Tone).WithOpacity(0.14f)).Round(Radius.M))
            {
                ui.Rect(props.Tone, Size.Du(4), Size.Du(28)).Round(Radius.S);
                using (ui.Column(Space.None, Align.Start))
                {
                    ui.Data(team.Name, Text.Heading, props.Tone);
                    ui.Text($"{count} {Localised(ref ui, "#SS_UI_Players")}", Text.Label, Tone.TextMuted);
                }
                ui.Flex();
                ui.Number(team.Score, Text.Title, props.Tone);
            }

            // Column headers.
            using (ui.Grid(columns, Space.S).Pad(Space.None, Space.S, Space.S, Space.XS))
            {
                ui.Spacer(Size.Du(4), Size.Du(1));
                ui.Text("#SS_UI_Player", Text.Label, Tone.TextMuted);
                ui.Spacer(Size.Du(1), Size.Du(1));
                ui.Text("#SS_UI_Score", Text.Label, Tone.TextMuted).TextAlign(Align.End);
                ui.Text("#SS_UI_Deaths", Text.Label, Tone.TextMuted).TextAlign(Align.End);
                ui.Text("#SS_UI_Ping", Text.Label, Tone.TextMuted).TextAlign(Align.End);
            }
            ui.Divider();

            if (count == 0)
            {
                using (ui.Box().Width(Size.Fill).Height(Size.Du(44)))
                    ui.Text("#SS_UI_Team_Empty", Text.Body, Tone.TextMuted).At(UiAnchor.Left).Inset(Space.M, Space.None);
            }

            for (int r = 0; r < count; r++)
                PlayerRow(ref ui, src.Player(_order[r]), columns, r);
        }
    }

    /// <summary>A localised word for use inside interpolated text.</summary>
    private static string Localised(ref UiFrame ui, string token) => ui.Context.Localise(token, out _);

    /// <summary>One player: stripe (local), name, tag (bot / dead), score, deaths, ping.</summary>
    public static void PlayerRow(ref UiFrame ui, in ScoreboardPlayer p, scoped ReadOnlySpan<UiSize> columns, int row)
    {
        var fill = p.Local ? ((UiPaint)Tone.Accent).WithOpacity(0.16f) : (row % 2 == 1 ? ((UiPaint)Tone.SurfaceRaised).WithOpacity(0.35f) : Tone.None);
        var ink = p.Alive ? Tone.Text : Tone.TextMuted;

        using (ui.Grid(columns, Space.S).Key(p.Index).Height(Size.Du(36)).Fill(fill).Pad(Space.None, Space.None, Space.S, Space.None))
        {
            ui.Rect(p.Local ? Tone.Accent : Tone.None, Size.Du(4), Size.Fill);
            ui.Data(p.Name, Text.Body, ink).Elide();

            if (p.Bot)
                ui.Text("#SS_UI_Bot", Text.Label, Tone.TextMuted);
            else if (!p.Alive)
                ui.Text("#SS_UI_Dead", Text.Label, Tone.Danger);
            else
                ui.Spacer(Size.Du(1), Size.Du(1));

            ui.Number(p.Frags, Text.Body, ink).TextAlign(Align.End);
            ui.Number(p.Deaths, Text.Body, Tone.TextMuted).TextAlign(Align.End);

            using (ui.Row(Space.XS).Align(Align.End, Align.Centre))
            {
                // A bot has no connection to measure: its tag already says so.
                if (!p.Bot)
                {
                    ui.Number(p.Ping, Text.Body, Tone.TextMuted);
                    ui.Shape(UiShape.Signal, ink, 16, Bars(p.Ping));
                }
            }
        }
    }
}
