using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The server browser (stock: GameUI's CServerBrowserDialog): tabs (Internet,
/// Favourites, LAN, History), a filter bar, a sortable table of every server
/// as a virtual list (2,000 rows cost what a screenful does), the selected
/// server's details, and Add favourite / Refresh / Connect. A refresh shows its
/// progress. Narrow: the details move under the table and the table drops its
/// Game column.
/// </summary>
public sealed class ServerBrowser : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IServerQuerySource _query;
    private readonly ServerFilterBar _filters;
    private readonly ServerTable _table;
    private readonly ServerDetailsPanel _details;

    public ServerBrowser(UiWindows windows, IServerQuerySource query, ServerFilterBar filters, ServerTable table, ServerDetailsPanel details)
    {
        _windows = windows;
        _query = query;
        _filters = filters;
        _table = table;
        _details = details;
    }

    public override string Name => "serverbrowser";

    public string WindowId => "serverbrowser";

    /// <summary>A narrow window shorter than this (du) drops the details pane, so the table keeps its rows.</summary>
    public const float ShortBelowDu = 640;

    /// <summary>
    /// Large by default (most of the screen, at most 1480 x 900 du); at least
    /// 720 x 540 du: at 720 x 420 the tabs, the two-line filter bar and the
    /// buttons left the table one row (seen in the minimum sample); 540 keeps five. Narrow
    /// below 1240 du: the 380 du details pane beside the table leaves the table
    /// under the ~780 du its seven columns need.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(1480, MathF.Round(screenW * 0.86f)), MathF.Min(900, MathF.Round(screenH * 0.86f)), 720, 540,
               screenW, screenH, 1240);

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(_windows, WindowId, "#SS_UI_Browser_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            bool narrow = ui.Narrow;   // the window's width, not the screen's
            ui.Tabs(["#SS_UI_Browser_Internet", "#SS_UI_Browser_Favourites", "#SS_UI_Browser_Lan", "#SS_UI_Browser_History"],
                    (int)_query.Tab);
            ui.Sub("filters", _filters).Width(Size.Fill);
            if (_query.Refreshing)
                ui.Progress(_query.RefreshProgress, "#SS_UI_Browser_Refreshing");

            if (narrow)
            {
                // Narrow: the details under the table, and only when the window
                // is tall enough to keep a useful table above them.
                ui.Sub("table", _table).Size(Size.Fill, Size.Fill);
                if (ui.DesignHeight >= ShortBelowDu)
                    ui.Sub("details", _details).Width(Size.Fill);
            }
            else
            {
                using (ui.Row(Space.L, Align.Stretch).Size(Size.Fill, Size.Fill))
                {
                    ui.Sub("table", _table).Size(Size.Fill, Size.Fill);
                    ui.Sub("details", _details).Size(Size.Du(380), Size.Fill);
                }
            }

            using (ui.DialogButtons().Width(Size.Fill))
            {
                ui.Button("favourite", "#SS_UI_Browser_AddFavourite");
                ui.Flex();
                ui.Button("refresh", "#SS_UI_Browser_Refresh");
                ui.Button("connect", "#SS_UI_Browser_Connect", primary: true);
            }
        }
    }
}

/// <summary>The filter bar: map text, not full, not empty, no password, max ping.</summary>
public sealed class ServerFilterBar : UiPanel
{
    private readonly IServerFilterSource _filter;

    public ServerFilterBar(IServerFilterSource filter) => _filter = filter;

    public override void Describe(ref UiFrame ui)
    {
        var f = _filter;
        bool narrow = ui.Narrow;
        if (narrow)
        {
            // Two lines: the map field and the ping limit, then the checkboxes.
            using (ui.Column(Space.S, Align.Stretch).Width(Size.Fill))
            {
                using (ui.Row(Space.M, Align.End).Width(Size.Fill))
                {
                    ui.TextField("map", f.MapContains, "#SS_UI_Browser_Map_Placeholder", width: Size.Fill);
                    ui.Dropdown("maxping", "#SS_UI_Browser_MaxPing", PingLabel(f.MaxPing), Size.Du(140));
                }
                Checks(ref ui, f);
            }
            return;
        }

        using (ui.Row(Space.L, Align.End).Width(Size.Fill))
        {
            ui.TextField("map", f.MapContains, "#SS_UI_Browser_Map_Placeholder", width: Size.Du(280));
            Checks(ref ui, f);
            ui.Dropdown("maxping", "#SS_UI_Browser_MaxPing", PingLabel(f.MaxPing), Size.Du(140));
        }
    }

    private static void Checks(ref UiFrame ui, IServerFilterSource f)
    {
        using (ui.Row(Space.L, Align.Centre))
        {
            ui.Checkbox("notfull", "#SS_UI_Browser_NotFull", f.NotFull);
            ui.Checkbox("notempty", "#SS_UI_Browser_NotEmpty", f.NotEmpty);
            ui.Checkbox("nopassword", "#SS_UI_Browser_NoPassword", f.NoPassword);
        }
    }

    /// <summary>A ping limit as the dropdown shows it; the strings are made once.</summary>
    private static string PingLabel(int ms) => ms switch
    {
        <= 0 => "Any",
        50 => "< 50 ms",
        100 => "< 100 ms",
        150 => "< 150 ms",
        250 => "< 250 ms",
        _ => "< 400 ms",
    };
}

/// <summary>
/// The table: a header (sorted by ping, marked), then the filtered and sorted
/// servers as a virtual list, the selected one raised. Sorting and filtering
/// happen when the list, the filters or the order change, never per frame.
/// An empty result says why.
/// </summary>
public sealed class ServerTable : UiPanel
{
    private readonly IServerListSource _list;
    private readonly IServerFilterSource _filter;
    private readonly IServerDetailsSource _details;
    private readonly IServerQuerySource _query;
    private readonly Row _row;
    private readonly ByPing _byPing;

    private int[] _order = new int[256];
    private int _shown;
    private int _version = -1;
    private (string Map, bool NotFull, bool NotEmpty, bool NoPassword, int MaxPing) _filters;

    public ServerTable(IServerListSource list, IServerFilterSource filter, IServerDetailsSource details, IServerQuerySource query)
    {
        _list = list;
        _filter = filter;
        _details = details;
        _query = query;
        _row = new Row(this);
        _byPing = new ByPing(list);
    }

    /// <summary>A row's height, du.</summary>
    public const float RowDu = 32;

    private void Refresh()
    {
        var f = _filter;
        var now = (f.MapContains, f.NotFull, f.NotEmpty, f.NoPassword, f.MaxPing);
        if (_version == _list.Version && ReferenceEquals(now.MapContains, _filters.Map) && now.NotFull == _filters.NotFull
            && now.NotEmpty == _filters.NotEmpty && now.NoPassword == _filters.NoPassword && now.MaxPing == _filters.MaxPing)
            return;

        // Allocates only when something changed (a refresh, a filter): not per frame.
        _version = _list.Version;
        _filters = now;
        if (_order.Length < _list.ServerCount)
            _order = new int[Math.Max(_list.ServerCount, _order.Length * 2)];
        _shown = 0;
        for (int i = 0; i < _list.ServerCount; i++)
        {
            var s = _list.Server(i);
            if (now.MapContains.Length > 0 && !s.Map.Contains(now.MapContains, StringComparison.OrdinalIgnoreCase)) continue;
            if (now.NotFull && s.Players >= s.MaxPlayers) continue;
            if (now.NotEmpty && s.Players == 0) continue;
            if (now.NoPassword && s.Password) continue;
            if (now.MaxPing > 0 && s.Ping > now.MaxPing) continue;
            _order[_shown++] = i;
        }
        Array.Sort(_order, 0, _shown, _byPing);
    }

    private sealed class ByPing(IServerListSource list) : IComparer<int>
    {
        public int Compare(int a, int b)
        {
            int d = list.Server(a).Ping.CompareTo(list.Server(b).Ping);
            return d != 0 ? d : a.CompareTo(b);
        }
    }

    /// <summary>Columns: lock, flags, name, game, map, players, ping (game dropped when narrow).</summary>
    private static void Columns(Span<UiSize> c, bool narrow)
    {
        c[0] = Size.Du(20);
        c[1] = Size.Du(48);
        c[2] = Size.Fr();
        c[3] = Size.Du(narrow ? 0 : 200);
        c[4] = Size.Du(narrow ? 140 : 180);
        c[5] = Size.Du(88);
        c[6] = Size.Du(80);
    }

    public override void Describe(ref UiFrame ui)
    {
        Refresh();
        bool narrow = ui.Narrow;
        Span<UiSize> columns = stackalloc UiSize[7];
        Columns(columns, narrow);

        using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill).Fill(Tone.Surface).Round(Radius.M).Clip())
        {
            using (ui.Grid(columns, Space.S).Width(Size.Fill).Height(Size.Du(36)).Fill(Tone.SurfaceRaised).Pad(Space.M, Space.None))
            {
                ui.Shape(UiShape.Lock, Tone.TextMuted, 12);
                ui.Spacer(Size.Du(1), Size.Du(1));
                ui.Text("#SS_UI_Browser_Name", Text.Label, Tone.TextMuted);
                if (narrow) ui.Spacer(Size.Du(1), Size.Du(1)); else ui.Text("#SS_UI_Browser_Game", Text.Label, Tone.TextMuted);
                ui.Text("#SS_UI_Browser_Map", Text.Label, Tone.TextMuted);
                ui.Text("#SS_UI_Browser_Players", Text.Label, Tone.TextMuted).TextAlign(Align.End);
                using (ui.Row(Space.XS).Align(Align.End, Align.Centre))
                {
                    ui.Text("#SS_UI_Browser_Ping", Text.Label, Tone.Accent);
                    ui.Shape(UiShape.ChevronUp, Tone.Accent, 12);
                }
            }

            if (_shown == 0)
            {
                using (ui.Box().Size(Size.Fill, Size.Fill))
                    ui.EmptyState(UiShape.Person, "#SS_UI_Browser_Empty_Title",
                                  _query.Tab == ServerTab.Lan && _list.ServerCount == 0 ? "#SS_UI_Browser_Empty_Lan" : "#SS_UI_Browser_Empty_Filtered")
                      .At(UiAnchor.Centre);
            }
            else
            {
                ui.VirtualList("rows", _shown, RowDu, _row).Size(Size.Fill, Size.Fill);
            }

            using (ui.Row(Space.XS).Width(Size.Fill).Pad(Space.M, Space.S).Fill(Tone.SurfaceRaised))
            {
                ui.Text($"{_shown}", Text.Label, Tone.Text);
                ui.Text("#SS_UI_Browser_Shown", Text.Label, Tone.TextMuted);
                ui.Text("#SS_UI_Browser_Of", Text.Label, Tone.TextMuted);
                ui.Text($"{_list.ServerCount}", Text.Label, Tone.Text);
            }
        }
    }

    /// <summary>One server: lock, secure and bot marks, name, game, map, players / max, ping with bars.</summary>
    private sealed class Row(ServerTable table) : UiPanel<int>
    {
        public override void Describe(ref UiFrame ui, in int r)
        {
            int index = table._order[r];
            var s = table._list.Server(index);
            bool selected = index == table._details.Selected;
            bool narrow = ui.Narrow;
            Span<UiSize> columns = stackalloc UiSize[7];
            Columns(columns, narrow);
            var fill = selected ? ((UiPaint)Tone.Accent).WithOpacity(0.18f)
                     : r % 2 == 1 ? ((UiPaint)Tone.SurfaceRaised).WithOpacity(0.35f) : Tone.None;
            var ink = s.Players >= s.MaxPlayers ? Tone.TextMuted : Tone.Text;

            var row = ui.Grid(columns, Space.S).Size(Size.Fill, Size.Fill).Fill(fill).Pad(Space.M, Space.None);
            ui.Interact(row);
            using (row)
            {
                if (s.Password) ui.Shape(UiShape.Lock, Tone.TextMuted, 12); else ui.Spacer(Size.Du(1), Size.Du(1));
                using (ui.Row(Space.XS))
                {
                    if (s.Secure) ui.Shape(UiShape.Shield, Tone.Positive, 12);
                    if (s.Bots > 0) ui.Text("#SS_UI_Browser_Bots", Text.Label, Tone.TextMuted);
                }
                ui.Data(s.Name, Text.Body, selected ? Tone.Accent : ink).Elide();
                if (narrow) ui.Spacer(Size.Du(1), Size.Du(1)); else ui.Data(s.Game, Text.Body, Tone.TextMuted).Elide();
                ui.Data(s.Map, Text.Body, ink).Elide();
                ui.Text($"{s.Players} / {s.MaxPlayers}", Text.Body, ink).TextAlign(Align.End);
                using (ui.Row(Space.XS).Align(Align.End, Align.Centre))
                {
                    ui.Number(s.Ping, Text.Body, Tone.TextMuted);
                    ui.Shape(UiShape.Signal, ink, 16, TeamTable.Bars(s.Ping));
                }
            }
        }
    }
}

/// <summary>The selected server: name, map, players, ping and flags, and who is on it.</summary>
public sealed class ServerDetailsPanel : UiPanel
{
    private readonly IServerListSource _list;
    private readonly IServerDetailsSource _details;

    public ServerDetailsPanel(IServerListSource list, IServerDetailsSource details)
    {
        _list = list;
        _details = details;
    }

    /// <summary>The most players listed.</summary>
    public const int Listed = 12;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Column(Space.M, Align.Stretch).Width(Size.Fill).Height(ui.Narrow ? Size.Auto : Size.Fill)
                 .Fill(Tone.Surface).Round(Radius.M).Pad(Space.L).Clip())
        {
            int sel = _details.Selected;
            if (sel < 0 || sel >= _list.ServerCount)
            {
                ui.Text("#SS_UI_Browser_Details_None", Text.Body, Tone.TextMuted).Wrap(320);
                return;
            }

            var s = _list.Server(sel);
            bool narrow = ui.Narrow;
            ui.Data(s.Name, Text.Heading).Elide();
            // Narrow: the stats in one line, and no player list (the table needs the height).
            using (narrow ? ui.Grid([Size.Fr(), Size.Fr(), Size.Fr(), Size.Fr()], Space.M, Space.S).Width(Size.Fill)
                          : ui.Grid([Size.Fr(), Size.Fr()], Space.M, Space.S).Width(Size.Fill))
            {
                Stat(ref ui, "#SS_UI_Browser_Map", s.Map);
                using (ui.Column(Space.None, Align.Start))
                {
                    ui.Label("#SS_UI_Browser_Players");
                    ui.Text($"{s.Players} / {s.MaxPlayers}", Text.Body);
                }
                using (ui.Column(Space.None, Align.Start))
                {
                    ui.Label("#SS_UI_Browser_Ping");
                    ui.Text($"{s.Ping} ms", Text.Body);
                }
                using (ui.Row(Space.S))
                {
                    if (s.Secure)
                    {
                        ui.Shape(UiShape.Shield, Tone.Positive, 12);
                        ui.Text("#SS_UI_Browser_Secure", Text.Label, Tone.Text);
                    }
                    if (s.Password)
                    {
                        ui.Shape(UiShape.Lock, Tone.TextMuted, 12);
                        ui.Text("#SS_UI_Browser_Password", Text.Label, Tone.Text);
                    }
                }
            }
            if (narrow)
                return;
            ui.Divider();
            ui.Label("#SS_UI_Browser_Details_Players");
            // The players scroll when the window is too short for them all.
            using var players = ui.Scroll("players", Space.XS).Size(Size.Fill, Size.Fill);
            for (int i = 0; i < _details.PlayerCount && i < Listed; i++)
            {
                var p = _details.Player(i);
                using (ui.Row(Space.S).Key(i).Width(Size.Fill))
                {
                    ui.Data(p.Name, Text.Body).Width(Size.Fill).Elide();
                    ui.Number(p.Score, Text.Body, Tone.Text);
                    ui.Text($"{p.Minutes} min", Text.Body, Tone.TextMuted).Width(Size.Du(64)).TextAlign(Align.End);
                }
            }
        }
    }

    private static void Stat(ref UiFrame ui, string label, string value)
    {
        using (ui.Column(Space.None, Align.Start))
        {
            ui.Label(label);
            ui.Data(value, Text.Body).Elide();
        }
    }
}
