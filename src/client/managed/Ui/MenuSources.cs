using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The scoreboard's data in game (plan_client_ui U3): the player resource's
/// rows (frags, deaths, ping, team, alive) with each connected slot's name from
/// the engine (GetPlayerInfo), sorted by frags. Refreshed by the host once a
/// frame while the scoreboard is open; names are asked once per user id.
/// </summary>
public sealed class ScoreboardUi(IClientStandalones standalones, IEngineHud screen, Func<IVtableEngineClient?> engine,
                                 IPlayerNames names, IEngineConsole console, IEngineEnvironment environment) : IScoreboardSource
{
    private const int Slots = PlayerResourceNetState.Slots;

    private readonly ScoreboardPlayer[] _players = new ScoreboardPlayer[Slots];
    private readonly ScoreboardTeam[] _teams = new ScoreboardTeam[2];
    private readonly string[] _names = new string[Slots];
    private readonly int[] _nameUserIds = new int[Slots];
    private readonly bool[] _fake = new bool[Slots];
    private int _count, _teamCount;
    private IVtableEngineClient? _engine;
    private Standalone.ClientPlayerResource? _resource;

    public string ServerName { get; private set; } = string.Empty;

    public string MapName { get; private set; } = string.Empty;

    /// <summary>
    /// The header's "N / M": stock's figure is the engine's own
    /// <c>gpGlobals-&gt;maxClients</c> (<c>ClientScoreBoardDialog.cpp:320</c>),
    /// read live through the same load (docs/todo.md client follow-ups: it read
    /// 33, the net-state array bound, on a listen server). Before the globals
    /// are bound — a test process, or a frame before the engine answers — the
    /// array ceiling stands rather than 0: "1 / 0" is the worse lie.
    /// </summary>
    public int MaxPlayers
    {
        get
        {
            var globals = environment.Globals;
            return globals.IsAvailable && globals.MaxClients > 0 ? globals.MaxClients : Slots - 1;
        }
    }

    public int TeamCount => _teamCount;

    public ScoreboardTeam Team(int i) => _teams[i];

    public int PlayerCount => _count;

    public ScoreboardPlayer Player(int i) => _players[i];

    /// <summary>Team 2 and 3's names, as HL2MP's teams are called.</summary>
    public static string TeamName(int team) => team switch
    {
        2 => "Combine",
        3 => "Rebels",
        _ => "Deathmatch",
    };

    public void Refresh()
    {
        _engine ??= engine();
        if (_resource is null || !standalones.Live().Contains(_resource))
            _resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();

        ServerName = console.FindConVarValue("hostname") ?? string.Empty;
        string level = _engine?.LevelName() ?? string.Empty;
        MapName = Path.GetFileNameWithoutExtension(level);

        _count = 0;
        if (_resource is not { } r)
        {
            _teamCount = 0;
            return;
        }

        int local = screen.LocalPlayerIndex;
        bool teams = false;
        var state = r.State;
        for (int slot = 1; slot < Slots; slot++)
        {
            if (!state.HasFlag(slot, PlayerResourceNetState.FlagConnected))
                continue;
            int userId = state.UserId[slot];
            if (_names[slot] is null || _nameUserIds[slot] != userId)
            {
                _nameUserIds[slot] = userId;
                ClientPlayerInfo info = default;
                bool known = names.TryGet(slot, out info) && info.Name.Length > 0;
                _names[slot] = known ? info.Name : $"#{slot}";
                _fake[slot] = known && info.Fake;
            }
            bool fake = _fake[slot];
            int team = state.Team[slot];
            teams |= team >= 2;
            _players[_count++] = new ScoreboardPlayer(slot, _names[slot], team, state.Score[slot], state.Deaths[slot],
                state.Ping[slot],
                state.HasFlag(slot, PlayerResourceNetState.FlagAlive), fake, slot == local);
        }

        // Most frags first, fewer deaths breaking a tie (stock's order). Insertion: no allocation.
        for (int a = 1; a < _count; a++)
        {
            var v = _players[a];
            int b = a - 1;
            while (b >= 0 && (_players[b].Frags < v.Frags || _players[b].Frags == v.Frags && _players[b].Deaths > v.Deaths))
            {
                _players[b + 1] = _players[b];
                b--;
            }
            _players[b + 1] = v;
        }

        if (teams)
        {
            _teamCount = 2;
            _teams[0] = new ScoreboardTeam(2, TeamName(2), Sum(2));
            _teams[1] = new ScoreboardTeam(3, TeamName(3), Sum(3));
        }
        else
        {
            _teamCount = 1;
            _teams[0] = new ScoreboardTeam(_count > 0 ? _players[0].Team : 0, TeamName(0), Sum(_count > 0 ? _players[0].Team : 0));
        }
    }

    private int Sum(int team)
    {
        int s = 0;
        for (int i = 0; i < _count; i++)
            if (_players[i].Team == team)
                s += _players[i].Frags;
        return s;
    }
}

/// <summary>
/// The team menu in game: Combine and Rebels with their player counts and
/// frags from the scoreboard's rows; a choice is sent to the server as
/// <c>jointeam N</c> (0 auto-assign, 1 spectate) and closes the menu.
/// </summary>
public sealed class TeamMenuUi(ScoreboardUi scores, IEngineHud screen, Func<IVtableEngineClient?> engine, ILog log)
    : ITeamMenuSource, ITeamMenuActions
{
    private readonly TeamChoice[] _choices = new TeamChoice[2];

    /// <summary>Set by the host: what closes the menu.</summary>
    public Action? Close { get; set; }

    public string MapName => scores.MapName;

    public int ChoiceCount => 2;

    public TeamChoice Choice(int i) => _choices[i];

    public int CurrentTeam { get; private set; }

    public void Refresh()
    {
        scores.Refresh();
        int local = screen.LocalPlayerIndex;
        Span<int> players = stackalloc int[2], frags = stackalloc int[2];
        CurrentTeam = 0;
        for (int i = 0; i < scores.PlayerCount; i++)
        {
            var p = scores.Player(i);
            if (p.Index == local)
                CurrentTeam = p.Team;
            if (p.Team is 2 or 3)
            {
                players[p.Team - 2]++;
                frags[p.Team - 2] += p.Frags;
            }
        }
        _choices[0] = new TeamChoice(2, ScoreboardUi.TeamName(2), players[0], frags[0], false);
        _choices[1] = new TeamChoice(3, ScoreboardUi.TeamName(3), players[1], frags[1], false);
    }

    public void Choose(int team)
    {
        log.Msg($"[SourceSharp/Ui] team menu: jointeam {team}\n");
        engine()?.JoinTeam(team);
        Close?.Invoke();
    }

    public void Cancel() => Close?.Invoke();
}

/// <summary>
/// The message of the day in game: what the server's VGUIMenu "info" carried,
/// as stock's CTextWindow reads it (game_controls/vguitextwindow.cpp): type 0
/// is the text itself, 1 the user data of the InfoPanel string table's entry
/// named by msg (ShowIndex; HL2MP sends "motd"), 2 a URL, 3 a file. The UI has
/// no web view, so it is stock with HTML disallowed (ShowURL's
/// cl_disablehtmlmotd / IsHTMLInfoPanelAllowed arm): an index entry that is a
/// URL or an HTML page shows the InfoPanel's "motd_text" instead, as text.
/// </summary>
public sealed class MotdUi(Func<string, string?>? infoPanel = null) : IMotdSource, IMotdActions
{
    /// <summary>Stock's string table for the panel's index content.</summary>
    public const string InfoPanelTable = "InfoPanel";

    public string Title { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    /// <summary>Set by the host: what closes the window.</summary>
    public Action? Closed { get; set; }

    /// <summary>The panel's data: stock's "title", "type" and "msg".</summary>
    public void SetData(IReadOnlyList<KeyValuePair<string, string>> data)
    {
        string Of(string key) => data.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
        Title = Of("title");
        string msg = Of("msg");
        // Stock: type is atoi'd; 1 is TYPE_INDEX. ShowIndex shows nothing for an
        // entry with no data, and the text it has otherwise.
        // Line ends as text, not as glyphs: the shipped motd files are CRLF.
        Text = (Of("type") == "1" ? Index(msg) : msg).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public void Close() => Closed?.Invoke();

    /// <summary>The fallback entry stock shows when HTML is not allowed.</summary>
    public const string TextFallback = "motd_text";

    private string Index(string entry)
    {
        string? data = infoPanel?.Invoke(entry);
        if (string.IsNullOrEmpty(data))
            return string.Empty;
        bool html = data.StartsWith("http://", StringComparison.Ordinal) || data.StartsWith("https://", StringComparison.Ordinal)
                    || data[0] == '<';
        return html ? infoPanel?.Invoke(TextFallback) ?? string.Empty : data;
    }
}

/// <summary>
/// The console in game: the engine's lines and the lines typed into it,
/// echoed, and the input line with caret and history, edited by the keys the
/// menu root forwards; Enter or Submit runs the line.
/// </summary>
/// <param name="lines">
/// The side's console lines, whose feed the window mirrors (docs/todo.md's
/// console-window follow-up): every print — the engine's through the display
/// func, this side's through <see cref="ILog"/> — arrives here while the
/// window lives. Null leaves the window mirroring typed lines only, which is
/// what a UI host without the side's container gets.
/// </param>
public sealed class ConsoleUi : IConsoleLogSource, IConsoleInputSource, IConsoleCompletionSource, IConsoleActions, IDisposable
{
    private readonly Func<IVtableEngineClient?> _engine;
    private readonly ConsoleLines? _engineLines;
    private readonly object _gate = new();
    private readonly Action<string, bool>? _previousTap;
    private readonly Action<string, bool>? _tap;
    private readonly List<ConsoleLine> _lines = [new("SourceSharp console. Lines typed here run as the engine console's do.", ConsoleSeverity.Developer)];
    private readonly List<string> _history = [];
    private string _input = string.Empty;

    // The engine echoes a submitted line back through the feed; Submit
    // already drew it, so the copy matching this arrives-consumed. Thread and
    // text are checked together, like ConsoleLines.SuppressEcho, so another
    // thread's coincidental line is never eaten.
    private string? _echoPending;
    private int _echoThread;

    /// <summary>Scrollback cap: the newest lines, plus the banner. The engine
    /// prints freely; an unbounded list would grow all session.</summary>
    public const int MaxLines = 1024;

    public ConsoleUi(Func<IVtableEngineClient?> engine, ConsoleLines? lines = null)
    {
        _engine = engine;
        _engineLines = lines;
        if (lines is { } src)
        {
            // Chain, as DebugLog does: this installs in front of whatever tap
            // was there, and Dispose puts it back — a UI reload must not
            // leave the old window's tap feeding a dead list.
            _tap = OnEngineLine;
            _previousTap = src.Tap;
            src.Tap = _tap;
        }
    }

    public int LineCount => _lines.Count;

    public ConsoleLine Line(int i) => _lines[i];

    public string Input => _input;

    public int Caret { get; private set; }

    public int SelectionStart => 0;

    public int SelectionLength => 0;

    public int HistoryCount => _history.Count;

    public int HistoryIndex { get; private set; } = -1;

    public int CompletionCount => 0;

    public ConsoleCompletion Completion(int i) => throw new ArgumentOutOfRangeException(nameof(i));

    public int SelectedCompletion => -1;

    /// <summary>A key the console's window received; true when the console used it.</summary>
    public bool Key(UiKey key)
    {
        switch (key)
        {
            case UiKey.Enter: Submit(); return true;
            case UiKey.Backspace:
                if (Caret > 0)
                {
                    _input = _input.Remove(Caret - 1, 1);
                    Caret--;
                }
                return true;
            case UiKey.Left: Caret = Math.Max(0, Caret - 1); return true;
            case UiKey.Right: Caret = Math.Min(_input.Length, Caret + 1); return true;
            case UiKey.Home: Caret = 0; return true;
            case UiKey.End: Caret = _input.Length; return true;
            case UiKey.Up: Recall(HistoryIndex < 0 ? _history.Count - 1 : Math.Max(0, HistoryIndex - 1)); return true;
            case UiKey.Down: Recall(HistoryIndex < 0 ? -1 : HistoryIndex + 1); return true;
            default: return false;
        }
    }

    /// <summary>A character typed into the line.</summary>
    public void Char(char c)
    {
        if (char.IsControl(c) || c == '`' || c == '~')
            return;
        _input = _input.Insert(Caret, c.ToString());
        Caret++;
        HistoryIndex = -1;
    }

    public void Submit()
    {
        string line = _input.Trim();
        _input = string.Empty;
        Caret = 0;
        HistoryIndex = -1;
        if (line.Length == 0)
            return;
        lock (_gate)
        {
            _lines.Add(new ConsoleLine("] " + line, ConsoleSeverity.Echo));
            _echoPending = "] " + line;
            _echoThread = Environment.CurrentManagedThreadId;
        }
        if (_history.Count == 0 || _history[^1] != line)
            _history.Add(line);
        _engine()?.SubmitConsoleLine(line);
    }

    /// <summary>
    /// One line the side's console printed (<see cref="ConsoleLines.Tap"/>):
    /// the engine's through the display func, this side's through its log.
    /// The echo of a just-submitted line is consumed, not drawn again.
    /// </summary>
    private void OnEngineLine(string text, bool isWarning)
    {
        string line = text.TrimEnd('\n', '\r');
        lock (_gate)
        {
            if (_echoPending is not null &&
                _echoThread == Environment.CurrentManagedThreadId &&
                string.Equals(_echoPending, line, StringComparison.Ordinal))
            {
                _echoPending = null;
                return;
            }

            _lines.Add(new ConsoleLine(line, isWarning ? ConsoleSeverity.Warning : ConsoleSeverity.Message));
            if (_lines.Count > MaxLines)
                _lines.RemoveRange(1, _lines.Count - MaxLines);
        }
    }

    private void Recall(int index)
    {
        if (index < 0 || index >= _history.Count)
        {
            HistoryIndex = -1;
            _input = string.Empty;
        }
        else
        {
            HistoryIndex = index;
            _input = _history[index];
        }
        Caret = _input.Length;
    }

    /// <summary>
    /// Takes this window's tap down and puts back the one it chained, as long
    /// as nothing else replaced it meanwhile: a UI reload must not leave the
    /// old window's tap feeding a list nobody reads.
    /// </summary>
    public void Dispose()
    {
        if (_engineLines is not { } src || _tap is null)
            return;
        if (ReferenceEquals(src.Tap, _tap))
            src.Tap = _previousTap;
    }
}

/// <summary>
/// The server browser in game: no master-server query exists in this build,
/// so its list is empty and it says it is not refreshing. Openable, so the
/// window, its tabs and filters are there to use when the query lands.
/// </summary>
public sealed class ServerBrowserUi : IServerListSource, IServerQuerySource, IServerFilterSource, IServerDetailsSource
{
    public int ServerCount => 0;

    public ServerEntry Server(int i) => throw new ArgumentOutOfRangeException(nameof(i));

    public int Version => 0;

    public ServerTab Tab => ServerTab.Internet;

    public bool Refreshing => false;

    public float RefreshProgress => 0f;

    public string MapContains => string.Empty;

    public bool NotFull => false;

    public bool NotEmpty => false;

    public bool NoPassword => false;

    public int MaxPing => 0;

    public int Selected => -1;

    public int PlayerCount => 0;

    public ServerPlayer Player(int i) => throw new ArgumentOutOfRangeException(nameof(i));
}

/// <summary>The engine's convars, as a form's bindings read and write them (plan_client_ui U4).</summary>
public sealed class EngineConVarStore(IEngineConsole console) : IUiConVarStore
{
    public string? Get(string name) => console.FindConVarValue(name);

    public void Set(string name, string value) => console.SetConVarValueByName(name, value);
}

/// <summary>
/// The engine's binds, as the key bindings form reads and writes them: the key
/// a command has through Key_LookupBinding (the HUD's slot), binds and unbinds
/// as console lines, and a captured button's name from the input system.
/// Names are upper case, as stock's keyboard options show them.
/// </summary>
public sealed class EngineKeyBindingStore(IEngineHud screen, Func<IVtableEngineClient?> engine, Func<IVtableInputSystem?> input)
    : IUiKeyBindingStore
{
    private IVtableEngineClient? _engine;
    private IVtableInputSystem? _input;

    public string? KeyFor(string command)
        => screen.LookupBinding(command) is { Length: > 0 } key ? key.ToUpperInvariant() : null;

    public void Bind(string key, string command)
        => (_engine ??= engine())?.SubmitConsoleLine($"bind \"{key}\" \"{command}\"");

    public void Unbind(string key)
        => (_engine ??= engine())?.SubmitConsoleLine($"unbind \"{key}\"");

    public string? KeyName(int buttonCode)
        => (_input ??= input())?.ButtonName(buttonCode)?.ToUpperInvariant();
}
