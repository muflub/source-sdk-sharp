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
/// The TF2 scoreboard (stock's <c>CTFClientScoreBoardDialog</c>,
/// tf_clientscoreboard.cpp, restyled over the toolkit): the two team bands
/// with the server-time box between them (<c>TimerBG</c> + the
/// ServerTimeLeft labels, <c>UpdateServerTimeLeft</c>, :2031-2114), the blue
/// list left and the red right (scoreboard.res: BluePlayerList x5,
/// RedPlayerList x325, each 310x280 at a 640-wide panel — rows of the 18-px
/// linespacing) under each list's column-header section (<c>InitPlayerList</c>'s
/// "name"/"score" header entries, :909/:914, always visible :892), the
/// spectator line at the bottom (<c>UpdateSpectatorList</c>, :1646-1705) and
/// the shaded bar with the local player's name strip, details grid, the
/// server and the map (ShadedBar 633x72, PlayerNameBG, PlayerNameLabel,
/// LocalPlayerStatsPanel, ServerLabelNew, MapName — <c>UpdatePlayerDetails</c>,
/// :1799-2026). The panel is 1440 du wide (640 px) and starts 70 du down
/// (ypos 31).
///
/// <para>
/// Each row is stock's column layout (scoreboard.res:16-26, laid left to
/// right by <c>InitPlayerList</c>, tf_clientscoreboard.cpp:897-924): medal,
/// avatar, spacer, name (flexing — stock's <c>m_nExtraSpace</c> arm,
/// :907-909), killstreak, killstreak image, dominating, nemesis, score,
/// class and ping, at the .res widths times <see cref="StockRes.Scale"/>.
/// The medal, avatar and killstreak slots stand reserved (the badge,
/// Steam-avatar and streak data are not carried; docs/plans/tf2/deferred.tsv).
/// The dominating column draws the
/// row's count of players dominated (m_iActiveDominations through
/// g_TF_PR-&gt;GetActiveDominations, tf_clientscoreboard.cpp:1335-1371) as
/// the leaderboard_dom count images, the "_d" set on a dead row (:1367-1371),
/// clamped to the table's span (SCOREBOARD_DOMINATION_ICONS - 1,
/// tf_shareddefs.h:638). The nemesis column draws the row's pairwise mark
/// against the local player (:1507-1519): the row that dominates them the
/// leaderboard_nemesis art (:1512), the row they dominate the
/// leaderboard_dominated art (:1517), each the "_d" set when dead — the
/// local player's own carrier holds the pair (tf_player_shared.cpp:7739-7751).
/// The class column draws stock's
/// class image (g_pszClassIcons, tf_shareddefs.cpp:1856-1877, 13x13,
/// tf_clientscoreboard.cpp:350-355) — the local team's rows only, stock's
/// gate <c>!AreEnemyTeams(row, local) &amp;&amp; local != TEAM_UNASSIGNED</c>
/// (:1467-1468) — with the darker "_d" set on a dead row (:1498).
/// </para>
/// </summary>
public sealed class TFScoreboard(IScoreboardSource source, ITFScoreboardClasses classes, ITFScoreboardDominations dominations,
                                 ITFScoreboardDetails details, ITFRoundTimer timer, IUiTextureLibrary textures) : UiScreen
{
    private const int Red = 2;               // TF_TEAM_RED (tf_shareddefs.h)
    private const int Blue = 3;              // TF_TEAM_BLUE
    private const int Unassigned = 0;        // TEAM_UNASSIGNED

    private readonly IScoreboardSource _source = source;
    private readonly ITFScoreboardClasses _classes = classes;
    private readonly ITFScoreboardDominations _dominations = dominations;
    private readonly ITFScoreboardDetails _details = details;
    private readonly ITFRoundTimer _timer = timer;
    private readonly IUiTextureLibrary _textures = textures;

    /// <summary>The row's columns, as the .res writes them (scoreboard.res:
    /// 16-26) — the name flexes, the icons are 13x13
    /// (tf_clientscoreboard.cpp:350-355) and the list's first column carries
    /// stock's 6-px data indent (SectionedListPanel::COLUMN_DATA_INDENT,
    /// SectionedListPanel.h:241).</summary>
    internal static class Stock
    {
        public const float Medal = 18f;              // medal_column_width 18 (:17)
        public const float Avatar = 28f;             // avatar_width 28 (:18)
        public const float Spacer = 5f;              // spacer 5 (:19)
        public const float Name = 118f;              // name_width 118 (:20)
        public const float Killstreak = 18f;         // killstreak_width 18 (:25)
        public const float KillstreakImage = 15f;    // killstreak_image_width 15 (:26)
        public const float Nemesis = 15f;            // nemesis_width 15 (:21)
        public const float Score = 25f;              // score_width 25 (:23)
        public const float Class = 15f;              // class_width 15 (:22)
        public const float Ping = 15f;               // ping_width 15 (:24)
        public const float Icon = 13f;               // the images' 13x13 (:350-355)
        public const float LineSpacing = 18f;        // linespacing 18 (scoreboard.res:506)
        public const float FirstIndent = 6f;         // COLUMN_DATA_INDENT 6

        // The server-time box (scoreboard.res: TimerBG 80x43 at the panel's
        // centre, the label above the inset and the value inside it,
        // :384-466); the box takes the gap column's width, the heights are
        // the res's own.
        public const float TimerTall = 43f;          // TimerBG tall (:391)
        public const float TimerWide = 80f;          // TimerBG wide (:390)
        public const float TimerLabelTall = 10f;     // ServerTimeLeftLabel tall (:446)
        public const float TimerInsetTall = 23f;     // ServerTimeLeftInsetBG tall (:421)
        public const float TimerInsetPad = 6f;       // the inset's side pad (286 - 280)

        // The details grid's tracks (LocalPlayerStatsPanel's label/value
        // columns, scoreboard.res:1133-1723): the label widths are the res's
        // own, the values 35, a 10-px track between the groups where the
        // res's overlapping boxes leave one.
        public const float GridLabel = 95f;          // KillsLabel wide (:1160)
        public const float GridLabelWide = 100f;     // CapturesLabel wide (:1642)
        public const float GridValue = 35f;          // Kills wide (:1601)
        public const float GridGap = 10f;
        public const float GridRow = 24f;            // the 10-px rows at the panel's 27.5-du pitch, rounded to the lint's grid
    }

    private int[] _order = new int[64];

    /// <summary>The scoreboard's stock canvas: the 640x448 panel the .res
    /// writes its children against (scoreboard.res:9-10).</summary>
    private static readonly StockRect Canvas = new(0f, 0f, 640f, 448f);

    /// <summary>The spectator names joined, rebuilt when the roster moves.</summary>
    private string _spectators = "";

    private int _spectatorCount;

    private int _rosterVersion = -1;

    public override string Name => "scoreboard";

    // Stock's scoreboard is a hud-drawn dialog, not a menu: it covers the
    // hud — the team-goal hint included — while TAB is held, and the game
    // under it keeps drawing (tf_clientscoreboard.cpp:361-362 paints the
    // dialog's background transparent over the hud pass). Order 1 above
    // TFHud's 0.
    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 1;

    // Opened by +showscores; the hud screens otherwise always show.
    public override bool OpenByDefault => false;

    /// <summary>Shown while TAB is held, over play: no cursor until asked for.</summary>
    public override bool WantsMouse => false;

    /// <summary>The row's column count: the stock order of scoreboard.res:
    /// 16-26.</summary>
    private const int ColumnCount = 11;

    public override void Describe(ref UiFrame ui)
    {
        var src = _source;
        bool hasLocal = false;
        int localTeam = Unassigned, localIndex = 0, localDeaths = 0;
        string localName = "";
        for (int i = 0; i < src.PlayerCount; i++)
        {
            var p = src.Player(i);
            if (!p.Local)
                continue;
            hasLocal = true;
            localTeam = p.Team;
            localIndex = p.Index;
            localName = p.Name;
            localDeaths = p.Deaths;
        }

        // The spectator line's roster: a cheap fold of who is on, rebuilt
        // (allocating) only when it moves, never once a frame.
        int version = src.PlayerCount;
        for (int i = 0; i < src.PlayerCount; i++)
        {
            var p = src.Player(i);
            version = version * 31 + p.Index;
            version = version * 31 + p.Name.GetHashCode();
        }
        if (version != _rosterVersion)
        {
            _rosterVersion = version;
            RefreshSpectators(src);
        }

        // Stock lays the blue list left and the red right
        // (BluePlayerList x5, RedPlayerList x325); the source's order is its own.
        int left = -1, right = -1;
        for (int t = 0; t < src.TeamCount && t < 2; t++)
        {
            if (src.Team(t).Number == Blue)
                left = t;
            else
                right = t;
        }
        if (left < 0)
        {
            left = right;
            right = -1;
        }

        bool wide = !ui.Narrow;

        Span<UiSize> columns = stackalloc UiSize[ColumnCount];
        Columns(columns);

        // The scores panel: stock's 640x448 at ypos 31 (scoreboard.res) is
        // 1440x1008 du; the panel's pad is the res's own 5-px list inset, so
        // 1448 caps it at a 1440 interior. The side pads follow the res: the
        // details grid's 1419 du must fit a 4:3 screen's 1440. The panel is
        // the screen's height and the lists row (or, narrow, the stacked
        // scroll) takes what height is left, so a short screen squeezes the
        // lists (they scroll) and never pushes the bar off screen.
        using (ui.Box().Size(Size.Fill, Size.Fill).Pad(Space.XS, Space.L, Space.XS, Space.L))
        using (ui.Panel(Space.XS).At(UiAnchor.Top).Size(Size.Fill, Size.Fill)
                 .MaxWidth(1448).Name("tf scoreboard panel"))
        {
            if (left < 0)
            {
                ui.EmptyState(UiShape.Person, "#SS_UI_Empty_Title", "#SS_UI_Empty_Body").Width(Size.Fill);
            }
            else if (wide)
            {
                using (ui.Row(Space.None).Width(Size.Fill))
                {
                    TeamBand(ref ui, src, left, Size.Fr());
                    if (right >= 0)
                    {
                        // The server-time box between the bands (scoreboard.res:
                        // TimerBG 80x43 px astride the bands' seam, its label
                        // and inset value inside — UpdateServerTimeLeft,
                        // tf_clientscoreboard.cpp:2031-2114).
                        using (ui.Column(Space.None, Align.Centre).Width(Size.Du(Stock.TimerWide * StockRes.Scale)).Align(Align.Centre, Align.Centre))
                            ServerTimeLeft(ref ui);
                        TeamBand(ref ui, src, right, Size.Fr());
                    }
                }

                using (ui.Row(Space.None).Pad(Space.None, Space.M, Space.None, Space.None)
                         .Width(Size.Fill).Height(Size.Fill))
                {
                    TeamList(ref ui, src, left, columns, localTeam, Size.Fill, Size.Fr());
                    if (right >= 0)
                    {
                        ui.Spacer(Size.Du(Stock.TimerWide * StockRes.Scale), Size.Du(1));
                        TeamList(ref ui, src, right, columns, localTeam, Size.Fill, Size.Fr());
                    }
                }

                Spectators(ref ui);
                BottomBar(ref ui, hasLocal, localName, localIndex, localTeam, localDeaths);
            }
            else
            {
                // Narrow: the teams stack; the stack fills the panel and
                // scrolls once its rows are taller than it.
                using (ui.Scroll("stacked", Space.M).Width(Size.Fill).Height(Size.Fill))
                {
                    TeamBand(ref ui, src, left, Size.Fill);
                    TeamList(ref ui, src, left, columns, localTeam, Size.Auto, Size.Fill);
                    if (right >= 0)
                    {
                        TeamBand(ref ui, src, right, Size.Fill);
                        TeamList(ref ui, src, right, columns, localTeam, Size.Auto, Size.Fill);
                    }
                    Spectators(ref ui);
                    BottomBar(ref ui, hasLocal, localName, localIndex, localTeam, localDeaths);
                }
            }
        }
    }

    /// <summary>The row's columns: the .res widths times the scale, the name
    /// flexing — stock's <c>m_nExtraSpace</c> arm (tf_clientscoreboard.cpp:
    /// 907-909) — and no gaps between cells (the spacer column is the gap,
    /// scoreboard.res:19).</summary>
    private static void Columns(Span<UiSize> c)
    {
        float s = StockRes.Scale;
        c[0] = Size.Du(Stock.Medal * s);
        c[1] = Size.Du(Stock.Avatar * s);
        c[2] = Size.Du(Stock.Spacer * s);
        c[3] = Size.Fr();
        c[4] = Size.Du(Stock.Killstreak * s);
        c[5] = Size.Du(Stock.KillstreakImage * s);
        c[6] = Size.Du(Stock.Nemesis * s);
        c[7] = Size.Du(Stock.Nemesis * s);
        c[8] = Size.Du(Stock.Score * s);
        c[9] = Size.Du(Stock.Class * s);
        c[10] = Size.Du(Stock.Ping * s);
    }

    /// <summary>Stock's AreEnemyTeams (tf_clientscoreboard.cpp:1067-1076): only the 2/3 pair.</summary>
    private static bool Enemies(int a, int b) => a == Red && b == Blue || a == Blue && b == Red;

    /// <summary>Every row's <c>SetItemBgColor</c>: the same translucent black
    /// (tf_clientscoreboard.cpp:1569) — stock has no zebra and no local
    /// wash.</summary>
    internal static class RowPaints
    {
        public static readonly UiPaint RowBg = UiPaint.FromGame(new UiColor(0, 0, 0, 80));

        /// <summary>The details labels' green on a counted row (cGreen, tf_clientscoreboard.cpp:1880).</summary>
        public static readonly UiPaint Counted = UiPaint.FromGame(new UiColor(0, 255, 0, 255));

        /// <summary>The server-time value's ink (clientscheme.res:53 BrightYellow).</summary>
        public static readonly UiPaint Yellow = UiPaint.FromGame(new UiColor(251, 235, 0, 255));
    }

    /// <summary>
    /// Whether a row may show its class: stock's gate
    /// <c>!AreEnemyTeams(g_PR-&gt;GetTeam(playerIndex), localteam) &amp;&amp;
    /// !(localteam == TEAM_UNASSIGNED)</c> (:1467-1468) — the local team's
    /// rows, and every row to a spectator.
    /// </summary>
    private static bool ShowsClass(int rowTeam, int localTeam) => localTeam != Unassigned && !Enemies(rowTeam, localTeam);

    /// <summary>The names to rebuild the spectator line from — a describe
    /// method may not allocate, so the join lives here and runs only when the
    /// roster version moved (stock's UpdateSpectatorList,
    /// tf_clientscoreboard.cpp:1646-1705: team spectator or unassigned).</summary>
    private void RefreshSpectators(IScoreboardSource src)
    {
        var names = new List<string>();
        for (int i = 0; i < src.PlayerCount; i++)
        {
            var p = src.Player(i);
            if (p.Team < Red)
                names.Add(p.Name);
        }
        _spectatorCount = names.Count;
        _spectators = string.Join(", ", names);
    }

    /// <summary>A team band: the solid team-colour bar stock paints with the
    /// BlueScoreBG/RedScoreBG art (290x71 px, 160 du tall), the name and head
    /// count at its top left (ScoreboardTeamNameNew 20 px, ScoreboardTeam-
    /// CountNew 14 px — scoreboard.res BlueTeamLabel 80,21, BlueTeamPlayer-
    /// Count 80,42) and the score huge at the inner edge, vertically centred
    /// (ScoreboardTeamScoreNew 36/48 px, BlueTeamScore east-aligned at the
    /// band's right, RedTeamScore west-aligned at its left, y12 h55 of 71).</summary>
    private void TeamBand(ref UiFrame ui, IScoreboardSource src, int teamIndex, UiSize width)
    {
        var team = src.Team(teamIndex);
        var tone = TFHudTeams.ToneOf(team.Number);
        int count = 0;
        for (int i = 0; i < src.PlayerCount; i++)
            if (src.Player(i).Team == team.Number)
                count++;

        // Stock mirrors the whole band (BlueTeamImage at the outer edge,
        // RedTeamImage x571): the name and count at the outer top corner, the
        // score at the inner edge (BlueTeamScore east, RedTeamScore west,
        // scoreboard.res 212, 299).
        bool blue = team.Number == Blue;
        using (ui.Box().Width(width).Height(Size.Du(160)).Fill((UiPaint)tone).Name(blue ? "tf band blue" : "tf band red"))
        {
            // The team badge at the outer top corner (BlueTeamImage 60x60 at
            // 11,0 of the band, RedTeamImage 80x80 mirrored to the right —
            // scoreboard.res:48-70, 115-141); stock's red art hangs 14 px
            // above the band's top edge, the band's empty upper strip here.
            float s = StockRes.Scale;
            if (blue)
                StockArt.Draw(ref ui, _textures, "tf badge blue", "hud/team_blue",
                              11f * s, 11f * s, 60f * s, 60f * s);
            else
                StockArt.Draw(ref ui, _textures, "tf badge red", "hud/team_red",
                              7f * s, 5f * s, 80f * s, 80f * s, corner: UiAnchor.BottomRight);
            // The name clears the badge by stock's gap (BlueTeamLabel x80
            // against the 60-wide badge at 7; RedTeamLabel's right edge 561
            // against the 80-wide badge inset 7 — scoreboard.res:186-199,
            // 290-303): 11+60+13 here, 7+80+10 mirrored there.
            using (blue ? ui.Column(Space.M, Align.Start).AtRes(UiAnchor.TopLeft, 84f * s, (int)Space.Huge)
                        : ui.Column(Space.M, Align.End).AtRes(UiAnchor.TopRight, 97f * s, (int)Space.Huge))
            {
                ui.Data(team.Name, Text.Title, Tone.OnAccent).Name(blue ? "tf name blue" : "tf name red");
                // The count is a token pair in stock (TF_ScoreBoard_Player
                // "%s1 player" / TF_ScoreBoard_Players "%s1 players",
                // tf_english.txt:59-60), so one player is not "1 players";
                // the count rides beside the token as its own leaf. The ink
                // is OnAccent, the team menu card's (TFTeamMenu.cs:296): the
                // light Text token does not clear the lint's floor on the
                // pale core-theme team colours, and only the 24-du-and-up
                // roles clear 3:1 against the fill. Titles, not Labels, for
                // the same reason.
                using (blue ? ui.Row(Space.XS, Align.Start) : ui.Row(Space.XS, Align.End))
                {
                    ui.Number(count, Text.Title, Tone.OnAccent);
                    ui.Text(count == 1 ? "#TF2_ScoreBoard_Player" : "#TF2_ScoreBoard_Players",
                            Text.Title, Tone.OnAccent);
                }
            }
            ui.Number(team.Score, Text.Display, Tone.OnAccent)
              .Name(blue ? "tf score blue" : "tf score red")
              .At(blue ? UiAnchor.Right : UiAnchor.Left)
              .Inset(Space.XL, Space.None);
        }
    }

    /// <summary>A team's player rows, sorted by score then deaths, in a scroll
    /// of <paramref name="height"/> (stock's 280-px list, 15 rows of the
    /// 18-px linespacing).</summary>
    private void TeamList(ref UiFrame ui, IScoreboardSource src, int teamIndex, scoped ReadOnlySpan<UiSize> columns,
                          int localTeam, UiSize height, UiSize width)
    {
        var team = src.Team(teamIndex);
        var tone = TFHudTeams.ToneOf(team.Number);

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

        var scroll = ui.Scroll(team.Number == Blue ? "players-blu" : "players-red", Space.XS).Height(height).Width(width);
        using (scroll)
        {
            // The section header stock keeps always visible over every team's
            // rows (SetSectionAlwaysVisible, tf_clientscoreboard.cpp:892): the
            // name and score heads in the row grid's own columns (:909/:914),
            // the rule under it the section's separator.
            Span<UiSize> head = stackalloc UiSize[ColumnCount];
            Columns(head);
            using (ui.Grid(head, Space.None).Height(Size.Du(Stock.LineSpacing * StockRes.Scale))
                     .Key(team.Number == Blue ? "head-blue" : "head-red"))
            {
                ui.Spacer(Size.Du(1), Size.Du(1));           // medal
                ui.Spacer(Size.Du(1), Size.Du(1));           // avatar
                ui.Spacer(Size.Du(1), Size.Du(1));           // stock's spacer column
                using (ui.Row(Space.XS))
                {
                    ui.Spacer(Size.Du(Stock.FirstIndent * StockRes.Scale), Size.Du(1));
                    ui.Text("#TF2_Scoreboard_Name", Text.Label, Tone.Text);
                }
                ui.Spacer(Size.Du(1), Size.Du(1));           // killstreak
                ui.Spacer(Size.Du(1), Size.Du(1));           // killstreak image
                ui.Spacer(Size.Du(1), Size.Du(1));           // dominating
                ui.Spacer(Size.Du(1), Size.Du(1));           // nemesis
                ui.Text("#TF2_Scoreboard_Score", Text.Label, Tone.Text).TextAlign(Align.End);
                ui.Spacer(Size.Du(1), Size.Du(1));           // class
                ui.Spacer(Size.Du(1), Size.Du(1));           // ping
            }
            ui.Divider();

            for (int r = 0; r < count; r++)
            {
                var p = src.Player(_order[r]);
                bool showClass = ShowsClass(p.Team, localTeam);
                Row(ref ui, p, _textures, tone, showClass ? _classes.ClassNameOf(p.Index) : "",
                    _dominations.DominationsOf(p.Index), _dominations.DominatesLocal(p.Index),
                    _dominations.DominatedByLocal(p.Index), r);
            }
        }
    }

    /// <summary>
    /// One player at stock's columns (scoreboard.res:16-26): the reserved
    /// medal, avatar and killstreak slots stand empty — the badge, Steam
    /// avatar and streak data are not carried (docs/plans/tf2/deferred.tsv)
    /// — the name carries the first column's 6-px data indent
    /// (SectionedListPanel::COLUMN_DATA_INDENT), the dominating column draws
    /// the row's count image where the count is one or more
    /// (tf_clientscoreboard.cpp:1367-1371), the nemesis column the row's
    /// pairwise mark against the local player (:1507-1519), the score reads
    /// right (COLUMN_RIGHT), the class draws the image where the gate allows
    /// (COLUMN_IMAGE | COLUMN_RIGHT, tf_clientscoreboard.cpp:915) and the
    /// ping its threshold image (:1400-1448). The row's ink is the team
    /// colour alive (:1539) and muted when dead — stock's own dim tints
    /// (:1548-1563) sit below the contrast floor; the row's bg is stock's
    /// uniform translucent black (:1569).
    /// </summary>
    private static void Row(ref UiFrame ui, in ScoreboardPlayer p, IUiTextureLibrary textures, Tone tone,
                            string className, int dominations, bool nemesis, bool dominated, int row)
    {
        var fill = RowPaints.RowBg;                    // SetItemBgColor 0,0,0,80 (:1569)
        // Alive rows read the team colour (g_PR->GetTeamColor, :1539). Stock
        // dims dead rows to fixed tints (135,83,83 / 182,75,75 RED,
        // 81,97,129 / 123,153,187 BLU, :1548-1563) below the toolkit's 4.5:1
        // contrast floor; the muted token stands in at the same job.
        var ink = p.Alive ? (UiPaint)tone : (UiPaint)Tone.TextMuted;
        float s = StockRes.Scale;
        float icon = Stock.Icon * s;
        float rowH = Stock.LineSpacing * s;

        Span<UiSize> columns = stackalloc UiSize[ColumnCount];
        Columns(columns);
        using (ui.Grid(columns, Space.None).Key(p.Index).Height(Size.Du(rowH)).Fill(fill))
        {
            ui.Spacer(Size.Du(1), Size.Du(1));           // medal: reserved
            ui.Spacer(Size.Du(1), Size.Du(1));           // avatar: reserved
            ui.Spacer(Size.Du(1), Size.Du(1));           // stock's spacer column
            using (ui.Row(Space.XS))
            {
                ui.Spacer(Size.Du(Stock.FirstIndent * s), Size.Du(1));
                ui.Data(p.Name, Text.Body).Color(ink).Elide();
                if (p.Bot)
                    ui.Text("#TF2_Scoreboard_Bot", Text.Label, Tone.TextMuted);
            }
            ui.Spacer(Size.Du(1), Size.Du(1));           // killstreak: reserved
            ui.Spacer(Size.Du(1), Size.Du(1));           // killstreak image: reserved
            if (DomIcon(dominations, p.Alive) is { Length: > 0 } domName)
            {
                // The count image centred in its cell (stock's
                // COLUMN_IMAGE | COLUMN_CENTER, tf_clientscoreboard.cpp:912),
                // 13x13 in the row's 18-px line (:350-355).
                using (ui.Box())
                    StockArt.Draw(ref ui, textures, "", domName,
                                  (Stock.Nemesis * s - icon) / 2f, (rowH - icon) / 2f, icon, icon,
                                  keyNumber: 2_000_000 + p.Index);
            }
            else
                ui.Spacer(Size.Du(1), Size.Du(1));
            if (NemesisIcon(nemesis, dominated, p.Alive) is { Length: > 0 } nemName)
            {
                // The pairwise mark centred in its cell (stock's
                // COLUMN_IMAGE | COLUMN_CENTER, tf_clientscoreboard.cpp:913),
                // 13x13 in the row's 18-px line (:350-355). The nemesis arm
                // wins when both hold, as stock's else-if (:1509-1518) does.
                using (ui.Box())
                    StockArt.Draw(ref ui, textures, "", nemName,
                                  (Stock.Nemesis * s - icon) / 2f, (rowH - icon) / 2f, icon, icon,
                                  keyNumber: 4_000_000 + p.Index);
            }
            else
                ui.Spacer(Size.Du(1), Size.Du(1));       // nemesis: no pair with the local player
            ui.Number(p.Frags, Text.Body).Color(ink).TextAlign(Align.End);
            if (className.Length > 0 && ClassIcon(className, p.Alive) is { Length: > 0 } iconName)
            {
                // The image at the column's right edge (stock's COLUMN_RIGHT),
                // 13x13 in the row's 18-px line (:350-355); a miss leaves the
                // class name where the text stood.
                using (ui.Box())
                {
                    // The icon's own key: a million offset keeps it out of the
                    // row keys' space (the row is keyed p.Index in this scope).
                    if (!StockArt.Draw(ref ui, textures, "", iconName,
                                       0f, (rowH - icon) / 2f, icon, icon,
                                       corner: UiAnchor.BottomRight, keyNumber: 1_000_000 + p.Index))
                        ui.Data(className, Text.Label, Tone.TextMuted).At(UiAnchor.Centre);
                }
            }
            else
                ui.Spacer(Size.Du(1), Size.Du(1));
            if (PingIcon(p) is { Length: > 0 } pingName)
            {
                // The ping image at the column's right edge (stock's
                // COLUMN_IMAGE | COLUMN_RIGHT, tf_clientscoreboard.cpp:923),
                // 13x13 in the row's 18-px line (:350-355).
                using (ui.Box())
                    StockArt.Draw(ref ui, textures, "", pingName,
                                  (Stock.Ping * s - icon) / 2f, (rowH - icon) / 2f, icon, icon,
                                  keyNumber: 3_000_000 + p.Index);
            }
            else
                ui.Spacer(Size.Du(1), Size.Du(1));
        }
    }

    /// <summary>The ping column's image, spelled out from the table
    /// (pszPingIcons, tf_clientscoreboard.cpp:101-108, dead :109-116): a
    /// bot its own pair before any ping is read (:1397-1408), a ping under
    /// one no image (:1414-1423), then the thresholds (:1433-1446). Stock's
    /// <c>tf_scoreboard_ping_as_text</c> defaults to false — the images;
    /// the convar itself is not ported.</summary>
    private static string PingIcon(in ScoreboardPlayer p)
    {
        int index;
        if (p.Bot)
            index = p.Team == Red ? 4 : 5;
        else if (p.Ping < 1)
            return "";
        else if (p.Ping < 125)
            index = 0;
        else if (p.Ping < 200)
            index = 1;
        else if (p.Ping < 275)
            index = 2;
        else
            index = 3;
        return (index, p.Alive) switch
        {
            (0, true) => "hud/scoreboard_ping_low",
            (1, true) => "hud/scoreboard_ping_med",
            (2, true) => "hud/scoreboard_ping_high",
            (3, true) => "hud/scoreboard_ping_very_high",
            (4, true) => "hud/scoreboard_ping_bot_red",
            (5, true) => "hud/scoreboard_ping_bot_blue",
            (0, false) => "hud/scoreboard_ping_low_d",
            (1, false) => "hud/scoreboard_ping_med_d",
            (2, false) => "hud/scoreboard_ping_high_d",
            (3, false) => "hud/scoreboard_ping_very_high_d",
            (4, false) => "hud/scoreboard_ping_bot_red_d",
            (5, false) => "hud/scoreboard_ping_bot_blue_d",
            _ => "",
        };
    }

    /// <summary>The class image's material, spelled out from the table
    /// (g_pszClassIcons, tf_shareddefs.cpp:1856-1877), the dead row the
    /// darker "_d" set (tf_clientscoreboard.cpp:1498). No class, no image —
    /// stock's <c>SetInt("class", 0)</c> arm (:1503).</summary>
    private static string ClassIcon(string className, bool alive) => (className, alive) switch
    {
        ("Scout", true) => "hud/leaderboard_class_scout",
        ("Sniper", true) => "hud/leaderboard_class_sniper",
        ("Soldier", true) => "hud/leaderboard_class_soldier",
        ("Demoman", true) => "hud/leaderboard_class_demo",
        ("Medic", true) => "hud/leaderboard_class_medic",
        ("Heavy", true) => "hud/leaderboard_class_heavy",
        ("Pyro", true) => "hud/leaderboard_class_pyro",
        ("Spy", true) => "hud/leaderboard_class_spy",
        ("Engineer", true) => "hud/leaderboard_class_engineer",
        ("Scout", false) => "hud/leaderboard_class_scout_d",
        ("Sniper", false) => "hud/leaderboard_class_sniper_d",
        ("Soldier", false) => "hud/leaderboard_class_soldier_d",
        ("Demoman", false) => "hud/leaderboard_class_demo_d",
        ("Medic", false) => "hud/leaderboard_class_medic_d",
        ("Heavy", false) => "hud/leaderboard_class_heavy_d",
        ("Pyro", false) => "hud/leaderboard_class_pyro_d",
        ("Spy", false) => "hud/leaderboard_class_spy_d",
        ("Engineer", false) => "hud/leaderboard_class_engineer_d",
        _ => "",
    };

    /// <summary>The nemesis column's image, stock's four arms
    /// (tf_clientscoreboard.cpp:1507-1519): a row that dominates the local
    /// player the nemesis art (:1512), a row they dominate the dominated art
    /// (:1517), the "_d" set on a dead row — the nemesis arm wins when both
    /// hold, as stock's else-if does.</summary>
    private static string NemesisIcon(bool nemesis, bool dominated, bool alive) => (nemesis, dominated, alive) switch
    {
        (true, _, true) => "hud/leaderboard_nemesis",
        (true, _, false) => "hud/leaderboard_nemesis_d",
        (false, true, true) => "hud/leaderboard_dominated",
        (false, true, false) => "hud/leaderboard_dominated_d",
        _ => "",
    };

    /// <summary>The domination-count image, spelled out from the table
    /// (pszDominationIcons, tf_clientscoreboard.cpp:120-138, dead :140-158),
    /// the count clamped to its span as stock's :1350-1353 arm does —
    /// 16 images, SCOREBOARD_DOMINATION_ICONS - 1 (tf_shareddefs.h:638).
    /// No count, no image — stock's <c>: 0</c> arm (:1367).</summary>
    private static string DomIcon(int count, bool alive) => (count, alive) switch
    {
        (1, true) => "hud/leaderboard_dom1",
        (2, true) => "hud/leaderboard_dom2",
        (3, true) => "hud/leaderboard_dom3",
        (4, true) => "hud/leaderboard_dom4",
        (5, true) => "hud/leaderboard_dom5",
        (6, true) => "hud/leaderboard_dom6",
        (7, true) => "hud/leaderboard_dom7",
        (8, true) => "hud/leaderboard_dom8",
        (9, true) => "hud/leaderboard_dom9",
        (10, true) => "hud/leaderboard_dom10",
        (11, true) => "hud/leaderboard_dom11",
        (12, true) => "hud/leaderboard_dom12",
        (13, true) => "hud/leaderboard_dom13",
        (14, true) => "hud/leaderboard_dom14",
        (15, true) => "hud/leaderboard_dom15",
        (16, true) => "hud/leaderboard_dom16",
        (1, false) => "hud/leaderboard_dom1_d",
        (2, false) => "hud/leaderboard_dom2_d",
        (3, false) => "hud/leaderboard_dom3_d",
        (4, false) => "hud/leaderboard_dom4_d",
        (5, false) => "hud/leaderboard_dom5_d",
        (6, false) => "hud/leaderboard_dom6_d",
        (7, false) => "hud/leaderboard_dom7_d",
        (8, false) => "hud/leaderboard_dom8_d",
        (9, false) => "hud/leaderboard_dom9_d",
        (10, false) => "hud/leaderboard_dom10_d",
        (11, false) => "hud/leaderboard_dom11_d",
        (12, false) => "hud/leaderboard_dom12_d",
        (13, false) => "hud/leaderboard_dom13_d",
        (14, false) => "hud/leaderboard_dom14_d",
        (15, false) => "hud/leaderboard_dom15_d",
        (16, false) => "hud/leaderboard_dom16_d",
        (> 16, true) => "hud/leaderboard_dom16",
        (> 16, false) => "hud/leaderboard_dom16_d",
        _ => "",
    };

    /// <summary>The spectator line, hidden while there are none — stock's
    /// Spectators label at the lists' feet (scoreboard.res: y357), the token
    /// pair of :1693.</summary>
    private void Spectators(ref UiFrame ui)
    {
        if (_spectatorCount == 0)
            return;
        // Stock's token (ScoreBoard_Spectators "%s1 spectator(s): %s2",
        // tf_english.txt:61-62) carries the colon; ours do too, and the
        // count and the joined names ride beside the token as their own
        // leaves — a describe method may not allocate.
        using (ui.Row(Space.XS, Align.Centre).Width(Size.Fill).Pad(Space.None, Space.M, Space.None, Space.None))
        {
            ui.Number(_spectatorCount, Text.Label, Tone.TextMuted);
            ui.Text(_spectatorCount == 1 ? "#TF2_Scoreboard_Spectator" : "#TF2_Scoreboard_Spectators",
                    Text.Label, Tone.TextMuted);
            ui.Data(_spectators, Text.Label, Tone.TextMuted).Elide();
        }
    }

    /// <summary>
    /// The shaded bar (stock's ShadedBar 633x72 px, fillcolor 0 0 0 153,
    /// scoreboard.res:606-627): the local player's name in its bordered strip
    /// (PlayerNameBG 105,372 485x29 TFThinLineBorder, :789-812; PlayerNameLabel
    /// 115,377, :813-831 — its ink the row's team colour, :1969-1970), the
    /// details grid under it (LocalPlayerStatsPanel y395, :1132-1723, its
    /// labels green on a counted row, :1884-1963), the server and the map at
    /// the top right (ServerLabelNew, MapName 315,375/385) and the ClassImage
    /// (22,350 92x92, :628-647 — 18 from the bar's left, its bottom 2 above
    /// the bar's). The image is stock's gate (tf_clientscoreboard.cpp:
    /// 1981-1982) over the selected player — the local one: on a game team,
    /// a normal class — drawn from the HUD's own table.
    /// </summary>
    private void BottomBar(ref UiFrame ui, bool hasLocal, string localName, int localIndex, int localTeam, int localDeaths)
    {
        // The bar and the image, resolved against the scoreboard's 640x448
        // stock canvas (scoreboard.res:9-10).
        var bar = new StockBox("4", "372", 633, 72).Rect(Canvas);
        // ypos 350 is written against the canvas (the bar's parent in the
        // .res is the 640x448 panel, not the bar), so resolve it there and
        // take the bar-relative spot afterwards.
        var portrait = StockRes.Spot(new StockBox("22", "350", 92, 92).Rect(Canvas), bar);
        var strip = StockRes.Spot(new StockBox("105", "372", 485, 29).Rect(Canvas), bar);
        var grid = StockRes.Spot(new StockBox("80", "395", 500, 49).Rect(Canvas), bar);
        string own = hasLocal ? _classes.ClassNameOf(localIndex) : "";
        string image = hasLocal && localTeam >= Red ? HealthPanel.Portrait(own, localTeam) : "";
        var details = hasLocal ? _details.Of(localIndex) : default;

        using (ui.Box().Width(Size.Fill).Height(Size.Du(bar.Tall * StockRes.Scale)).Fill(Tone.Scrim).Round(Radius.M))
        {
            if (image.Length > 0)
                StockArt.Draw(ref ui, _textures, "class image", image, portrait.Left, portrait.Bottom,
                              portrait.Wide, portrait.Tall);

            // The name strip's rule (PlayerNameBG's TFThinLineBorder), under
            // the name and beside the grid's first row as the res overlaps them.
            using (ui.Box().AtRes(UiAnchor.BottomLeft, strip.Left, strip.Bottom)
                     .Size(Size.Du(strip.Wide), Size.Du(strip.Tall)).Border(Tone.Line, 1f).Layered()
                     .Name("tf name strip"))
            { }

            using (ui.Row(Space.L).Width(Size.Fill).Height(Size.Fill).Align(Align.Stretch, Align.Start)
                     .Pad(Space.L, Space.None))
            {
                // The name clears the image: PlayerNameLabel x115 against the
                // bar's x4 (scoreboard.res:816, :610), less the row's pad.
                ui.Spacer(Size.Du((115f - 4f) * StockRes.Scale - (float)Space.L), Size.Du(1));
                using (ui.Column(Space.XS, Align.Start))
                {
                    if (hasLocal)
                    {
                        var ink = localTeam >= Red ? (UiPaint)TFHudTeams.ToneOf(localTeam) : (UiPaint)Tone.Text;
                        ui.Data(localName, Text.Body).Color(ink).Elide();
                    }
                }
                ui.Flex();
                using (ui.Column(Space.XS, Align.End))
                {
                    using (ui.Row(Space.XS).Align(Align.End, Align.Centre))
                    {
                        ui.Text("#TF2_Scoreboard_Server", Text.Label, Tone.TextMuted);
                        ui.Data(_source.ServerName, Text.Label, Tone.TextMuted).Elide();
                    }
                    ui.Data(_source.MapName, Text.Label, Tone.TextMuted).TextAlign(Align.End).Elide();
                }
            }

            // The details grid (UpdatePlayerDetails's arms): four label/value
            // groups at the res's columns, a label green when its count is up
            // (:1884-1963). The deaths ride the row the lists already show.
            // Its 1420 du of columns are the res's own; the stacked narrow
            // screen's panel is half that, so there the row carries the record.
            if (hasLocal && !ui.Narrow)
            {
                float s = StockRes.Scale;
                float row = Stock.GridRow;
                Span<UiSize> columns = stackalloc UiSize[11];
                float label = Stock.GridLabel * s, wide = Stock.GridLabelWide * s,
                    value = Stock.GridValue * s, gap = Stock.GridGap * s;
                columns[0] = Size.Du(label);
                columns[1] = Size.Du(value);
                columns[2] = Size.Du(gap);
                columns[3] = Size.Du(wide);
                columns[4] = Size.Du(value);
                columns[5] = Size.Du(gap);
                columns[6] = Size.Du(label);
                columns[7] = Size.Du(value);
                columns[8] = Size.Du(gap);
                columns[9] = Size.Du(label);
                columns[10] = Size.Du(value);
                using (ui.Grid(columns, Space.None).AtRes(UiAnchor.BottomLeft, grid.Left, grid.Bottom)
                         .Width(Size.Du(grid.Wide)).Height(Size.Du(grid.Tall)).Layered().Name("tf details"))
                {
                    Cell(ref ui, "#TF2_Scoreboard_Kills", details.Kills, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Captures", details.Captures, row, wide, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Invulns", details.Invulns, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Backstabs", details.Backstabs, row, label, value);

                    Cell(ref ui, "#TF2_Scoreboard_Deaths", localDeaths, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Defenses", details.Defenses, row, wide, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Headshots", details.Headshots, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Bonus", details.Bonus, row, label, value);

                    Cell(ref ui, "#TF2_Scoreboard_Assists", details.Assists, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Dominations", details.Dominations, row, wide, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Teleports", details.Teleports, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Support", details.Support, row, label, value);

                    Cell(ref ui, "#TF2_Scoreboard_Destruction", details.Destruction, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Revenge", details.Revenge, row, wide, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Healing", details.Healing, row, label, value);
                    ui.Spacer(Size.Du(1), Size.Du(1));
                    Cell(ref ui, "#TF2_Scoreboard_Damage", details.Damage, row, label, value);
                }
            }
        }
    }

    /// <summary>One label/value pair of the details grid: the label right-
    /// aligned over its count, green when the count is up (the cGreen arms,
    /// tf_clientscoreboard.cpp:1884-1963).</summary>
    private static void Cell(ref UiFrame ui, string label, int value, float row, float labelWide, float valueWide)
    {
        var ink = value != 0 ? RowPaints.Counted : (UiPaint)Tone.Text;
        ui.Text(label, Text.Label).Color(ink).TextSize(TFHudType.ScoreboardVerySmall).TextAlign(Align.End)
            .Width(Size.Du(labelWide)).Height(Size.Du(row)).Pad(Space.None, Space.None, Space.M, Space.None);
        ui.Number(value, Text.Label).TextSize(TFHudType.ScoreboardVerySmall).Color(Tone.Text)
            .Width(Size.Du(valueWide)).Height(Size.Du(row));
    }

    /// <summary>
    /// The server-time box between the bands (TimerBG's TFThinLineBorder, the
    /// ServerTimeLeftInsetBG inset and the pair of labels inside,
    /// scoreboard.res:384-466). The value is UpdateServerTimeLeft's arms
    /// (tf_clientscoreboard.cpp:2031-2114): no limit the no-time-limit line,
    /// a spent one the change-on-round-end line, else the h:mm:ss / m:ss
    /// count. The value's ink is the res's BrightYellow (clientscheme.res:53).
    /// </summary>
    private void ServerTimeLeft(ref UiFrame ui)
    {
        float s = StockRes.Scale;
        using (ui.Column(Space.None, Align.Centre).Width(Size.Fill).Height(Size.Du(Stock.TimerTall * s))
                 .Border(Tone.Line, 1f).Name("tf server time"))
        {
            ui.Text("#TF2_Scoreboard_TimeLeftLabel", Text.Label, Tone.Text).TextSize(TFHudType.ScoreboardVerySmall)
                .TextAlign(Align.Centre).Height(Size.Du(Stock.TimerLabelTall * s))
                .Pad(new UiLength(0f, false), new UiLength(2f * s, false), new UiLength(0f, false), new UiLength(0f, false));

            // The inset the value sits in, its own rule inside the box's.
            using (ui.Column(Space.None, Align.Centre).Width(Size.Fill)
                     .Height(Size.Du(Stock.TimerInsetTall * s))
                     .Pad(new UiLength(Stock.TimerInsetPad * s, false), new UiLength(0f, false),
                          new UiLength(Stock.TimerInsetPad * s, false), new UiLength(0f, false))
                     .Border(Tone.Line, 1f).Name("tf server time inset"))
            {
                int limit = _timer.ServerTimeLeft;
                if (_timer.ServerTimeLimit == 0)
                    NoTime(ref ui, "#TF2_Scoreboard_NoTimeLimit");
                else if (limit <= 0)
                    NoTime(ref ui, "#TF2_Scoreboard_ChangeOnRoundEnd");
                else if (limit / 3600 == 0)
                    TimeLeft(ref ui, -1, limit / 60, limit % 60);
                else
                    TimeLeft(ref ui, limit / 3600, limit % 3600 / 60, limit % 60);
            }
        }
    }

    /// <summary>The box's word arms: the no-limit and spent-limit lines, and
    /// the counted seconds as m:ss or h:mm:ss (tf_clientscoreboard.cpp:
    /// :2098-2107) — the clock's own interpolation. A negative hour is the
    /// no-hours arm.</summary>
    private static void NoTime(ref UiFrame ui, string token)
        => ui.Text(token, Text.Label).Color(RowPaints.Yellow).TextSize(TFHudType.ScoreboardVerySmall).TextAlign(Align.Centre);

    private static void TimeLeft(ref UiFrame ui, int hours, int minutes, int seconds)
    {
        var text = hours < 0
            ? ui.Text($"{minutes}:{seconds:00}", Text.Label).Color(RowPaints.Yellow)
            : ui.Text($"{hours}:{minutes:00}:{seconds:00}", Text.Label).Color(RowPaints.Yellow);
        text.TextSize(TFHudType.ScoreboardMediumSmall).TextAlign(Align.Centre);
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
}
