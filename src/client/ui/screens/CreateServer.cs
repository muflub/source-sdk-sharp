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
/// has only the Server page's four. Bots join this window from the AI plan
/// (plan_hl2mp_ai.md §3.5).
/// </summary>
public sealed class CreateServer(UiWindows windows, IMapListSource maps, IServerSettingsSource settings, ICreateServerActions actions)
    : UiScreen, IUiWindowed
{
    private const float LabelColumnDu = UiForm<MouseOptions.Model>.LabelColumnDu;
    private const string HostNameVar = "hostname", MaxPlayersVar = "maxplayers", PasswordVar = "sv_password";

    // Stock's GetValue defaults, for a mod whose script names none.
    private const int DefaultMaxPlayers = 32, MaxPlayersCeiling = 32;

    private readonly ScriptRows _game = new(HostNameVar, MaxPlayersVar, PasswordVar);
    private readonly string[] _tabs = ["#SS_UI_Create_Server", "#SS_UI_Create_Game"];
    private readonly List<string> _mapNames = [];
    private long _lastFrame = long.MinValue;

    public override string Name => "createserver";

    public string WindowId => "createserver";

    public int Tab { get; set; }

    /// <summary>The map choice: 0 a random map, else the map at that place less one.</summary>
    public int Map { get; set; }

    public string HostName { get; set; } = "";

    public float MaxPlayers { get; set; } = DefaultMaxPlayers;

    public string Password { get; set; } = "";

    /// <summary>The maps listed, by name.</summary>
    public IReadOnlyList<string> Maps { get; private set; } = [];

    public ScriptRows Game => _game;

    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(760, screenW), MathF.Min(600, screenH), 480, 380, screenW, screenH, 600);

    public void Load()
    {
        Maps = maps.Maps();
        _mapNames.Clear();
        _mapNames.Add("#SS_UI_Create_RandomMap");
        _mapNames.AddRange(Maps);
        Map = Maps.Count > 0 ? Math.Min(Math.Max(Map, 0), Maps.Count) : 0;
        _game.Load(settings.Load());
        HostName = _game.Value(HostNameVar) is { Length: > 0 } name ? name : maps.GameName;
        MaxPlayers = _game.Value(MaxPlayersVar) is { } max ? ScriptRows.Number(max) : DefaultMaxPlayers;
        Password = _game.Value(PasswordVar) ?? "";
    }

    /// <summary>The map Start starts: the chosen one, or one of the maps at random.</summary>
    public string? ChosenMap(Random random)
        => Maps.Count == 0 ? null : Map <= 0 ? Maps[random.Next(Maps.Count)] : Maps[Math.Min(Map, Maps.Count) - 1];

    public void Start(Random random)
    {
        if (ChosenMap(random) is not { } map)
            return;
        int max = (int)MathF.Round(MaxPlayers);
        _game.Set(HostNameVar, HostName);
        _game.Set(MaxPlayersVar, max.ToString(CultureInfo.InvariantCulture));
        _game.Set(PasswordVar, Password);
        if (_game.Entries.Count > 0)
            settings.Save(_game.Entries, _game.Values);
        _game.TakeSaved();
        actions.Start(map, max, HostName, Password);
        windows.RequestClose(WindowId);
    }

    public override void Describe(ref UiFrame ui)
    {
        if (ui.FrameNumber != _lastFrame + 1)
            Load();
        _lastFrame = ui.FrameNumber;

        using (ui.Window(windows, WindowId, "#SS_UI_Create_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            Tab = ui.TabBar(_tabs, Tab);
            using (ui.Scroll("fields", Space.M).Size(Size.Fill, Size.Fill))
            using (ui.FormGrid(LabelColumnDu).Width(Size.Fill))
            {
                if (Tab == 0)
                    ServerPage(ref ui);
                else if (_game.Describe(ref ui, GameKeyBase) is var pressed and >= 0)
                    settings.Press(_game.Entries[pressed]);
            }
            if (Tab == 1 && !_game.AnyShown)
                ui.EmptyState(UiShape.Dot, "#SS_UI_Create_NoSettings", "#SS_UI_Create_NoSettingsBody").Size(Size.Fill, Size.Fill);

            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    windows.RequestClose(WindowId);
                ui.Flex();
                if (ui.Button("start", "#SS_UI_Create_Start", primary: true, enabled: Maps.Count > 0).Clicked)
                    Start(Random.Shared);
            }
        }
    }

    // Grid keys: the server page's rows, and the game page's from GameKeyBase.
    private const int ControlKey = 1000, GameKeyBase = 100;

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
