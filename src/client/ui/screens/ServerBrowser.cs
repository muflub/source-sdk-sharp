using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The server browser (stock: GameUI's CServerBrowserDialog): tabs (Internet,
/// Favourites, LAN, History), a filter bar, a sortable table of every server
/// as a virtual list (2,000 rows cost what a screenful does), the selected
/// server's details, and Add favourite (Remove on Favourites) / Refresh /
/// Connect; on Favourites, Add server takes an address. A passworded server's
/// join asks for its password first (stock's password dialog). A refresh shows
/// its progress. Narrow: the details move under the table and the table drops
/// its Game column.
/// </summary>
public sealed class ServerBrowser : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IServerQuerySource _query;
    private readonly IServerDetailsSource _selection;
    private readonly IServerBrowserState _state;
    private readonly IServerBrowserActions _actions;
    private readonly ServerFilterBar _filters;
    private readonly ServerTable _table;
    private readonly ServerDetailsPanel _details;
    private readonly string[] _tabs = ["#SS_UI_Browser_Internet", "#SS_UI_Browser_Favourites", "#SS_UI_Browser_Lan", "#SS_UI_Browser_History"];

    // The add-server row and the password field: what the player is typing.
    private bool _adding;
    private bool _badAddress;
    private string _address = string.Empty;
    private string _password = string.Empty;

    public ServerBrowser(UiWindows windows, IServerQuerySource query, IServerDetailsSource selection, IServerBrowserState state,
                         IServerBrowserActions actions, ServerFilterBar filters, ServerTable table, ServerDetailsPanel details)
    {
        _windows = windows;
        _query = query;
        _selection = selection;
        _state = state;
        _actions = actions;
        _filters = filters;
        _table = table;
        _details = details;
    }

    /// <summary>Whether the favourites tab's add-server row is open.</summary>
    public bool AddingServer
    {
        get => _adding;
        set => _adding = value;
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
        _actions.Poll();
        using (ui.Window(_windows, WindowId, "#SS_UI_Browser_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            bool narrow = ui.Narrow;   // the window's width, not the screen's
            int tab = ui.TabBar(_tabs, (int)_query.Tab);
            if (tab != (int)_query.Tab)
            {
                _adding = false;
                _actions.SelectTab((ServerTab)tab);
            }
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

            if (_adding)
                AddRow(ref ui);

            bool selected = _selection.Selected >= 0;
            bool favourites = _query.Tab == ServerTab.Favourites;
            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("favourite", favourites ? "#SS_UI_Browser_RemoveFavourite" : "#SS_UI_Browser_AddFavourite",
                              enabled: selected && (favourites || !_state.SelectedIsFavourite)).Clicked)
                    _actions.ToggleFavourite();
                if (favourites && ui.Button("addserver", "#SS_UI_Browser_AddServer").Clicked)
                {
                    _adding = !_adding;
                    _badAddress = false;
                }
                ui.Flex();
                if (ui.Button("refresh", "#SS_UI_Browser_Refresh").Clicked)
                    _actions.Refresh();
                if (ui.Button("connect", "#SS_UI_Browser_Connect", primary: true, enabled: selected).Clicked)
                    _actions.Connect();
            }

            if (_state.AskingPassword)
                PasswordDialog(ref ui);
        }
    }

    // On Favourites: an address field and Add, its problem said under it.
    private void AddRow(ref UiFrame ui)
    {
        using (ui.Row(Space.M, Align.Centre).Width(Size.Fill).Key("addrow"))
        {
            ui.Text("#SS_UI_Browser_AddServer_Label", Text.Body, Tone.Text);
            using (ui.Column(Space.XS, Align.Start).Width(Size.Du(320)))
            {
                string typed = ui.EditText("address", _address, "#SS_UI_Browser_AddServer_Placeholder");
                if (!ReferenceEquals(typed, _address))
                {
                    _address = typed;
                    _badAddress = false;
                }
                if (_badAddress)
                    ui.Text("#SS_UI_Browser_AddServer_Bad", Text.Caption, Tone.Danger);
            }
            if (ui.Button("add", "#SS_UI_Browser_AddServer_Add", primary: true).Clicked)
            {
                if (_actions.AddServer(_address))
                {
                    _address = string.Empty;
                    _adding = false;
                }
                else
                    _badAddress = true;
            }
        }
    }

    // Stock's password dialog: over the browser, the server named, Join or Cancel.
    private void PasswordDialog(ref UiFrame ui)
    {
        using (ui.Overlay("password", modal: true))
        using (ui.Column(Space.M, Align.Stretch).At(UiAnchor.Centre).Width(Size.Du(420)).Fill(Tone.Surface).Round(Radius.M).Border(Tone.Line, 1)
                 .Raise(Elevation.Overlay).Pad(Space.L))
        {
            ui.Text("#SS_UI_Browser_Password_Title", Text.Heading, Tone.Text);
            ui.Data(_state.PasswordServer, Text.Body, Tone.TextMuted).Elide();
            string typed = ui.EditText("passwordfield", _password, "#SS_UI_Browser_Password_Placeholder");
            if (!ReferenceEquals(typed, _password))
                _password = typed;
            using (ui.Row(Space.M).Width(Size.Fill))
            {
                if (ui.Button("passwordcancel", "#SS_UI_Cancel").Clicked)
                {
                    _password = string.Empty;
                    _actions.CancelPassword();
                }
                ui.Flex();
                if (ui.Button("passwordjoin", "#SS_UI_Browser_Connect", primary: true).Clicked)
                {
                    _actions.SubmitPassword(_password);
                    _password = string.Empty;
                }
            }
        }
    }
}

/// <summary>The filter bar: map text, not full, not empty, no password, max ping; each edit goes to the browser.</summary>
public sealed class ServerFilterBar : UiPanel
{
    private readonly IServerFilterSource _filter;
    private readonly IServerBrowserActions _actions;
    private readonly string[] _pingNames =
        ["#SS_UI_Browser_PingAny", "< 50 ms", "< 100 ms", "< 150 ms", "< 250 ms", "< 400 ms"];
    private readonly int[] _pings = [0, 50, 100, 150, 250, 400];

    public ServerFilterBar(IServerFilterSource filter, IServerBrowserActions actions)
    {
        _filter = filter;
        _actions = actions;
    }

    public override void Describe(ref UiFrame ui)
    {
        var f = _filter;
        if (ui.Narrow)
        {
            // Two lines: the map field and the ping limit, then the checkboxes.
            using (ui.Column(Space.S, Align.Stretch).Width(Size.Fill))
            {
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    using (ui.Column(Space.None).Width(Size.Fill))
                        Map(ref ui, f);
                    Ping(ref ui, f);
                }
                Checks(ref ui, f);
            }
            return;
        }

        using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
        {
            using (ui.Column(Space.None).Width(Size.Du(280)))
                Map(ref ui, f);
            Checks(ref ui, f);
            Ping(ref ui, f);
        }
    }

    private void Map(ref UiFrame ui, IServerFilterSource f)
    {
        string typed = ui.EditText("map", f.MapContains, "#SS_UI_Browser_Map_Placeholder");
        if (!ReferenceEquals(typed, f.MapContains))
            _actions.SetMapContains(typed);
    }

    private void Ping(ref UiFrame ui, IServerFilterSource f)
    {
        int chosen = Math.Max(0, Array.IndexOf(_pings, f.MaxPing));
        using (ui.Column(Space.None).Width(Size.Du(160)))
        {
            int picked = ui.Choice("maxping", chosen, _pings.Length, new UiChoiceList(_pingNames), tokens: true);
            if (picked != chosen)
                _actions.SetMaxPing(_pings[picked]);
        }
    }

    private void Checks(ref UiFrame ui, IServerFilterSource f)
    {
        using (ui.Row(Space.L, Align.Centre))
        {
            if (ui.Checkbox("notfull", "#SS_UI_Browser_NotFull", f.NotFull).Clicked)
                _actions.SetNotFull(!f.NotFull);
            if (ui.Checkbox("notempty", "#SS_UI_Browser_NotEmpty", f.NotEmpty).Clicked)
                _actions.SetNotEmpty(!f.NotEmpty);
            if (ui.Checkbox("nopassword", "#SS_UI_Browser_NoPassword", f.NoPassword).Clicked)
                _actions.SetNoPassword(!f.NoPassword);
        }
    }
}

/// <summary>
/// The table: a header (each column sorts it, the sorted one marked), then
/// the filtered and sorted servers as a virtual list, the selected one raised;
/// a row clicked is selected. Sorting and filtering happen when the list, the
/// filters or the order change, never per frame. An empty result says why.
/// </summary>
public sealed class ServerTable : UiPanel
{
    private readonly IServerListSource _list;
    private readonly IServerFilterSource _filter;
    private readonly IServerDetailsSource _details;
    private readonly IServerQuerySource _query;
    private readonly IServerSortSource _sort;
    private readonly IServerBrowserState _state;
    private readonly IServerBrowserActions _actions;
    private readonly Row _row;
    private readonly ByColumn _byColumn;

    private int[] _order = new int[256];
    private int _shown;
    private int _version = -1;
    private (string Map, bool NotFull, bool NotEmpty, bool NoPassword, int MaxPing, ServerSort Sort, bool Descending) _filters;

    public ServerTable(IServerListSource list, IServerFilterSource filter, IServerDetailsSource details, IServerQuerySource query,
                       IServerSortSource sort, IServerBrowserState state, IServerBrowserActions actions)
    {
        _list = list;
        _filter = filter;
        _details = details;
        _query = query;
        _sort = sort;
        _state = state;
        _actions = actions;
        _row = new Row(this);
        _byColumn = new ByColumn(list);
    }

    /// <summary>How many servers pass the filters.</summary>
    public int Shown => _shown;

    /// <summary>The list index of shown row <paramref name="row"/>.</summary>
    public int ShownIndex(int row) => _order[row];

    /// <summary>A row's height, du.</summary>
    public const float RowDu = 32;

    private void Refresh()
    {
        var f = _filter;
        var now = (f.MapContains, f.NotFull, f.NotEmpty, f.NoPassword, f.MaxPing, _sort.Sort, _sort.Descending);
        if (_version == _list.Version && ReferenceEquals(now.MapContains, _filters.Map) && now.NotFull == _filters.NotFull
            && now.NotEmpty == _filters.NotEmpty && now.NoPassword == _filters.NoPassword && now.MaxPing == _filters.MaxPing
            && now.Sort == _filters.Sort && now.Descending == _filters.Descending)
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
        _byColumn.Sort = now.Sort;
        _byColumn.Descending = now.Descending;
        Array.Sort(_order, 0, _shown, _byColumn);
    }

    // One column, up or down; ties keep the list's order.
    private sealed class ByColumn(IServerListSource list) : IComparer<int>
    {
        public ServerSort Sort;
        public bool Descending;

        public int Compare(int a, int b)
        {
            var x = list.Server(a);
            var y = list.Server(b);
            int d = Sort switch
            {
                ServerSort.Name => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase),
                ServerSort.Game => string.Compare(x.Game, y.Game, StringComparison.OrdinalIgnoreCase),
                ServerSort.Map => string.Compare(x.Map, y.Map, StringComparison.OrdinalIgnoreCase),
                ServerSort.Players => x.Players.CompareTo(y.Players),
                _ => x.Ping.CompareTo(y.Ping),
            };
            if (Descending)
                d = -d;
            return d != 0 ? d : a.CompareTo(b);
        }
    }

    // A header cell: its label, the sort mark when it is the sorted column; clicked, sorts by it.
    private void Header(ref UiFrame ui, string key, string token, ServerSort column, bool end = false)
    {
        bool on = _sort.Sort == column;
        var cell = ui.Row(Space.XS).Key(key).Align(end ? Align.End : Align.Start, Align.Centre);
        if (ui.Interact(cell).Clicked)
            _actions.SortBy(column);
        using (cell)
        {
            ui.Text(token, Text.Label, on ? Tone.Accent : Tone.TextMuted);
            if (on)
                ui.Shape(_sort.Descending ? UiShape.ChevronDown : UiShape.ChevronUp, Tone.Accent, 12);
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
                Header(ref ui, "sortname", "#SS_UI_Browser_Name", ServerSort.Name);
                if (narrow) ui.Spacer(Size.Du(1), Size.Du(1)); else Header(ref ui, "sortgame", "#SS_UI_Browser_Game", ServerSort.Game);
                Header(ref ui, "sortmap", "#SS_UI_Browser_Map", ServerSort.Map);
                Header(ref ui, "sortplayers", "#SS_UI_Browser_Players", ServerSort.Players, end: true);
                Header(ref ui, "sortping", "#SS_UI_Browser_Ping", ServerSort.Ping, end: true);
            }

            if (_shown == 0)
            {
                using (ui.Box().Size(Size.Fill, Size.Fill))
                    ui.EmptyState(UiShape.Person, "#SS_UI_Browser_Empty_Title", EmptyReason()).At(UiAnchor.Centre);
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

    private string EmptyReason()
    {
        if (_list.ServerCount > 0)
            return "#SS_UI_Browser_Empty_Filtered";
        return _query.Tab switch
        {
            ServerTab.Internet when !_state.SteamAvailable => "#SS_UI_Browser_Empty_NoSteam",
            ServerTab.Lan => "#SS_UI_Browser_Empty_Lan",
            ServerTab.Favourites => "#SS_UI_Browser_Empty_Favourites",
            ServerTab.History => "#SS_UI_Browser_Empty_History",
            _ => _query.Refreshing ? "#SS_UI_Browser_Empty_Refreshing" : "#SS_UI_Browser_Empty_Internet",
        };
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
            if (ui.Interact(row).Clicked)
                table._actions.Select(index);
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
