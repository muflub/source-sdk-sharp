using System.Globalization;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The create-server window (plan_gameui_managed.md G3; stock: GameUI's
/// create multiplayer game dialog). The Server page: the map (a random one
/// first, then the maps by name), the server's name, its size and password.
/// The Game page: the mod's server script (cfg/settings_default.scr, then
/// cfg/settings.scr), its hostname, maxplayers and sv_password drawn on the
/// Server page. Start writes the script's values and file, then starts the
/// server through <see cref="ICreateServerActions"/>; a mod with no script
/// has only the Server page's four. A mod adds pages of its own after these
/// two (<see cref="CreateServerPage"/>, found in its UI assemblies; the HL2MP
/// AI's bots, plan_hl2mp_ai.md §3.5): each may refuse Start with a reason,
/// and its console lines run after the server's size and before the map.
/// </summary>
public sealed class CreateServer(UiWindows windows, IMapListSource maps, IServerSettingsSource settings, ICreateServerActions actions,
                                 IReadOnlyList<CreateServerPage> pages, ICatalogSource catalog)
    : UiScreen, IUiWindowed
{
    private const float LabelColumnDu = UiForm<MouseOptions.Model>.LabelColumnDu;
    private const string HostNameVar = "hostname", MaxPlayersVar = "maxplayers", PasswordVar = "sv_password";

    /// <summary>The convar the chosen mode starts the server in (the server's <c>sv_mode_next</c>); empty: the map's own.</summary>
    public const string ModeVar = "sv_mode_next";

    /// <summary>Stock's team-play knob: the mode row replaces it.</summary>
    private const string TeamplayVar = "mp_teamplay";

    // Stock's GetValue defaults, for a mod whose script names none.
    private const int DefaultMaxPlayers = 32, MaxPlayersCeiling = 32;

    private readonly ScriptRows _game = new(HostNameVar, MaxPlayersVar, PasswordVar);
    private readonly ScriptRows _modeRows = new();
    private readonly List<string> _modeNames = [];
    private readonly List<string> _modeIds = [];
    private IReadOnlyList<UiScriptEntry> _script = [];
    private int _modeShown = -1;
    private readonly string[] _tabs = CreateServerWindow.Tabs(pages);
    private readonly List<string> _mapNames = [];
    private readonly List<string> _beforeMap = [];
    private readonly (float W, float H) _minimum = CreateServerWindow.Minimum(pages);
    private long _lastFrame = long.MinValue;

    public override string Name => CreateServerWindow.Id;

    public string WindowId => CreateServerWindow.Id;

    /// <summary>The mod's pages, after the core's two tabs.</summary>
    public IReadOnlyList<CreateServerPage> Pages => pages;

    public int Tab { get; set; }

    /// <summary>The map choice: 0 a random map, else the map at that place less one.</summary>
    public int Map { get; set; }

    public string HostName { get; set; } = "";

    public float MaxPlayers { get; set; } = DefaultMaxPlayers;

    public string Password { get; set; } = "";

    /// <summary>The maps listed, by name.</summary>
    public IReadOnlyList<string> Maps { get; private set; } = [];

    public ScriptRows Game => _game;

    /// <summary>The chosen mode, an index into the catalog's modes after the first choice, "the map's own" (0).</summary>
    public int Mode { get; set; }

    /// <summary>The chosen mode's id; empty for the map's own.</summary>
    public string ModeId => Mode > 0 && Mode < _modeIds.Count ? _modeIds[Mode] : "";

    /// <summary>The chosen mode's settings, as rows.</summary>
    public ScriptRows ModeSettings => _modeRows;

    /// <summary>The window's size: the core's, grown to what the mod's pages need.</summary>
    public UiWindowSpec Spec(float screenW, float screenH) => CreateServerWindow.Spec(screenW, screenH, _minimum);

    /// <summary>What the window has chosen, as the mod's pages see it: the map (null while random) and the size.</summary>
    public CreateServerChoices Choices => new(Map > 0 && Map <= Maps.Count ? Maps[Map - 1] : null, (int)MathF.Round(MaxPlayers));

    /// <summary>The first page's reason to refuse Start, or "".</summary>
    public string StartRefusalToken
    {
        get
        {
            CreateServerChoices choices = Choices;
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i].StartRefusalToken(choices) is { Length: > 0 } refusal)
                    return refusal;
            }

            return "";
        }
    }

    public void Load()
    {
        Maps = maps.Maps();
        _mapNames.Clear();
        _mapNames.Add("#SS_UI_Create_RandomMap");
        _mapNames.AddRange(Maps);
        Map = Maps.Count > 0 ? Math.Min(Math.Max(Map, 0), Maps.Count) : 0;
        _script = settings.Load();
        LoadModes();
        _game.Load([.. _script.Where(e => !Covered(e.Name))]);
        HostName = _game.Value(HostNameVar) is { Length: > 0 } name ? name : maps.GameName;
        MaxPlayers = _game.Value(MaxPlayersVar) is { } max ? ScriptRows.Number(max) : DefaultMaxPlayers;
        Password = _game.Value(PasswordVar) ?? "";
    }

    /// <summary>The map Start starts: the chosen one, or one of the maps at random.</summary>
    // The catalog's modes: "the map's own" first, then each mode by its title; the script's sv_mode_next chosen.
    private void LoadModes()
    {
        _modeNames.Clear();
        _modeIds.Clear();
        _modeNames.Add("#SS_UI_Create_ModeFromMap");
        _modeIds.Add("");
        foreach (var mode in catalog.Modes)
        {
            _modeNames.Add(mode.Spec.Title);
            _modeIds.Add(mode.Spec.Id);
        }
        string saved = ScriptValue(ModeVar) ?? "";
        Mode = Math.Max(0, _modeIds.FindIndex(id => string.Equals(id, saved, StringComparison.OrdinalIgnoreCase)));
        _modeShown = -1;
        ShowMode();
    }

    /// <summary>
    /// The chosen mode's settings as rows, each holding the value it held, else the
    /// script's, else the mode's default: a mode's rows are its own (deathmatch has
    /// no team count), and a value carries over when two modes share a setting.
    /// </summary>
    public void ShowMode()
    {
        if (_modeShown == Mode)
            return;
        _modeShown = Mode;
        var spec = Mode > 0 && Mode - 1 < catalog.Modes.Count ? catalog.Modes[Mode - 1].Spec : null;
        var rows = new List<UiScriptEntry>();
        foreach (var setting in spec?.Settings ?? [])
        {
            if (setting.AdminOnly)
                continue;
            string value = _modeRows.Value(setting.Key) ?? ScriptValue(setting.Key) ?? setting.Default;
            rows.Add(Row(setting, value));
        }
        _modeRows.Load(rows);
    }

    private static UiScriptEntry Row(SourceSharp.Abi.SettingSpec setting, string value)
    {
        string prompt = "#SS_UI_Vote_Setting_" + setting.Key;
        if (setting.Choices.Count == 0)
            return new UiScriptEntry(setting.Key, prompt, null, UiScriptKind.Number, setting.Min, setting.Max, [], value, false);
        if (setting.Kind == SourceSharp.Abi.SettingKind.Bool)
            return new UiScriptEntry(setting.Key, prompt, null, UiScriptKind.Bool, 0, 1, [], value, false);
        return new UiScriptEntry(setting.Key, prompt, null, UiScriptKind.List, 0, 0,
                                 [.. setting.Choices.Select(c => new UiScriptItem(c.Label, c.Value))], value, false);
    }

    private string? ScriptValue(string name)
    {
        foreach (var e in _script)
            if (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return e.Value;
        return null;
    }

    // A script row the mode row or a mode's settings draw: the knob the mode replaces, and every catalog setting.
    private bool Covered(string name)
    {
        if (name.Equals(ModeVar, StringComparison.OrdinalIgnoreCase) || name.Equals(TeamplayVar, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var mode in catalog.Modes)
            foreach (var setting in mode.Spec.Settings)
                if (setting.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
        return false;
    }

    public string? ChosenMap(Random random)
        => Maps.Count == 0 ? null : Map <= 0 ? Maps[random.Next(Maps.Count)] : Maps[Math.Min(Map, Maps.Count) - 1];

    public void Start(Random random)
    {
        if (StartRefusalToken.Length > 0 || ChosenMap(random) is not { } map)
            return;
        int max = (int)MathF.Round(MaxPlayers);
        var choices = new CreateServerChoices(map, max);
        _beforeMap.Clear();
        for (int i = 0; i < pages.Count; i++)
            pages[i].BeforeMap(choices, _beforeMap);
        _game.Set(HostNameVar, HostName);
        _game.Set(MaxPlayersVar, max.ToString(CultureInfo.InvariantCulture));
        _game.Set(PasswordVar, Password);
        // The script keeps the server's fields, the mode and its settings; saving sets each convar before the map.
        ShowMode();
        var entries = new List<UiScriptEntry>(_game.Entries);
        var values = new List<string>(_game.Values);
        entries.Add(new UiScriptEntry(ModeVar, "#SS_UI_Create_Mode", null, UiScriptKind.String, 0, 0, [], "", false));
        values.Add(ModeId);
        entries.AddRange(_modeRows.Entries);
        values.AddRange(_modeRows.Values);
        settings.Save(entries, values);
        _game.TakeSaved();
        _modeRows.TakeSaved();
        actions.Start(map, max, HostName, Password, _beforeMap);
        windows.RequestClose(WindowId);
    }

    public override void Describe(ref UiFrame ui)
    {
        if (ui.FrameNumber != _lastFrame + 1)
            Load();
        _lastFrame = ui.FrameNumber;

        using (ui.Window(windows, WindowId, CreateServerWindow.TitleToken, Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            Tab = ui.TabBar(_tabs, Tab);
            if (Tab >= CreateServerWindow.CorePages && Tab - CreateServerWindow.CorePages < pages.Count)
            {
                using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, Size.Fill))
                    pages[Tab - CreateServerWindow.CorePages].Describe(ref ui, Choices);
            }
            else
            {
                using (ui.Scroll("fields", Space.M).Size(Size.Fill, Size.Fill))
                using (ui.FormGrid(LabelColumnDu).Width(Size.Fill))
                {
                    if (Tab == 0)
                    {
                        ServerPage(ref ui);
                    }
                    else
                    {
                        GameModeRow(ref ui);
                        _modeRows.Describe(ref ui, ModeKeyBase);
                        if (_game.Describe(ref ui, GameKeyBase) is var pressed and >= 0)
                            settings.Press(_game.Entries[pressed]);
                    }
                }
                if (Tab == 1 && !_game.AnyShown && _modeNames.Count <= 1)
                    ui.EmptyState(UiShape.Dot, "#SS_UI_Create_NoSettings", "#SS_UI_Create_NoSettingsBody").Size(Size.Fill, Size.Fill);
            }

            if (CreateServerWindow.Buttons(ref ui, windows, StartRefusalToken, Maps.Count > 0))
                Start(Random.Shared);
        }
    }

    // Grid keys: the server page's rows, the mode's settings from ModeKeyBase and the script's from GameKeyBase.
    private const int ControlKey = 1000, GameKeyBase = 100, ModeKeyBase = 400, ModeRow = 10;

    // The mode row heads the game page; a change shows the chosen mode's settings.
    private void GameModeRow(ref UiFrame ui)
    {
        if (_modeNames.Count <= 1)
            return;
        Label(ref ui, ModeRow, "#SS_UI_Create_Mode");
        using (Control(ref ui, ModeRow))
            Mode = ui.Choice("mode", Mode, _modeNames.Count, new UiChoiceList(_modeNames), tokens: true);
        ShowMode();
    }

    private void ServerPage(ref UiFrame ui)
    {
        Label(ref ui, 0, "#SS_UI_Create_Map");
        using (Control(ref ui, 0))
        {
            if (Maps.Count == 0)
                ui.Text("#SS_UI_Create_NoMaps", Text.Body, Tone.TextMuted);
            else
                Map = ui.Choice("map", Map, _mapNames.Count, new UiChoiceList(_mapNames), tokens: true);
        }
        Label(ref ui, 1, "#SS_UI_Create_HostName");
        using (Control(ref ui, 1))
        {
            string name = ui.EditText("hostname", HostName, "#SS_UI_Form_TextPlaceholder");
            if (!ReferenceEquals(name, HostName))
                HostName = name;
        }
        Label(ref ui, 2, "#SS_UI_Create_MaxPlayers");
        using (Control(ref ui, 2))
        using (ui.Row(Space.M).Width(Size.Fill))
        {
            float min = 2f, max = MaxPlayersCeiling;
            if (_game.Entry(MaxPlayersVar) is { } entry)
            {
                min = MathF.Max(1f, entry.Min);
                max = entry.Max > min ? entry.Max : MaxPlayersCeiling;
            }
            MaxPlayers = ui.Slider("maxplayers", MaxPlayers, min, max, 1f);
            ui.Number((int)MathF.Round(MaxPlayers), Text.Body, Tone.Text).MinWidth(56);
        }
        Label(ref ui, 3, "#SS_UI_Create_Password");
        using (Control(ref ui, 3))
        {
            string password = ui.EditText("password", Password, "#SS_UI_Create_NoPassword");
            if (!ReferenceEquals(password, Password))
                Password = password;
        }
    }

    private static void Label(ref UiFrame ui, int key, string token) => ui.Text(token, Text.Body, Tone.Text).Wrap(LabelColumnDu).Key(key);

    private static UiBox Control(ref UiFrame ui, int key) => ui.Column(Space.XS, Align.Start).Key(ControlKey + key).Width(Size.Fill);
}
