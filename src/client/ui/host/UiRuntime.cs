using System.Diagnostics;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>The menu layer's sources the host refreshes and the panels it maps.</summary>
public sealed record UiMenus(ScoreboardUi Scores, TeamMenuUi Teams, MotdUi Motd, ConsoleUi Console, ChatInputUi? Chat = null,
                             MainMenuUi? Main = null, GameUiLoading? Loading = null, IRadialMenuSource? RadialSource = null, IRadialMenuActions? RadialActions = null);

/// <summary>
/// THE UI IN GAME (plan_client_ui §8 U2, U3): what IManagedUiClient forwards to.
/// Two layers: the HUD (the HUD panel's paint) and the menus (the menu root's
/// paint and input). Each paint refreshes what its screens read, lays the layer
/// out against the screen as it is now (size is an input, never a cache) and
/// draws it through <see cref="VguiBackend"/>. The menu layer decides whether
/// the menu root takes the mouse and keyboard, and its cursor.
/// </summary>
public sealed class UiHost : IUiRuntime, IGameUiNavigation
{
    /// <summary>Stock viewport panel names and the UI screens that answer them.
    /// A screen on the menu layer may claim panels with
    /// <see cref="Toolkit.UiScreen.Panel"/> and <see cref="Toolkit.UiScreen.Panels"/>,
    /// and the claim answers those panels for that host instead: this map is
    /// the host's starting point, and a claim never edits it
    /// (<see cref="PanelsFor(string)"/> is the routing).</summary>
    public static readonly IReadOnlyDictionary<string, string> Panels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["scores"] = "scoreboard",
        ["team"] = "teammenu",
        ["info"] = "motd",
    };

    /// <summary>
    /// Stock panels the UI takes and has no window for: the spectator bar draws
    /// by the observer mode, not by being shown; the spectator menu, the map
    /// overview and the nav-mesh progress dialog are not built.
    /// </summary>
    public static readonly IReadOnlySet<string> Absorbed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "specgui", "specmenu", "overview", "nav_progress",
    };

    /// <summary>Stock's PANEL_ALL: the viewport's every panel at once.</summary>
    public const string AllPanels = "all";

    /// <summary>How often an open menu's sources are re-read: they read the network's state, not the frame's.</summary>
    public const double MenuRefreshSeconds = 0.25;

    private readonly ILog _log;
    private readonly IGameClock _clock;
    private readonly UiLayer _hud, _menu;
    private readonly VguiBackend _backend;
    private readonly IUiSurface _surface;
    private readonly LocalPlayerUi _player;
    private readonly HudMessages _messages;
    private readonly HudBuildStamp _build;
    private readonly UiScaleConVar? _scale;
    private readonly UiMenus _menus;
    /// <summary>Panel claims of the menu layer's screens: panel name to screen
    /// name, first claimer in layer order. These override <see cref="Panels"/>.</summary>
    private readonly Dictionary<string, string> _claims;

    // The other names screens open under (UiScreen.Aliases), by name.
    private readonly Dictionary<string, string> _aliases;

    /// <summary>Stock TF2's viewport name for its main menu (PANEL_MAINMENUOVERRIDE,
    /// tf_shareddefs.h:176, the panel the client hands the GameUI): a mod screen
    /// claiming it is the main menu the GameUI shows, in place of the core's.</summary>
    public const string MainMenuPanel = "MainMenuOverride";

    /// <summary>The screen the GameUI's activation, Escape and Resume drive: the
    /// claimer of <see cref="MainMenuPanel"/>, else the core's.</summary>
    private string MainMenuScreen => _claims.TryGetValue(MainMenuPanel, out var claimed) ? claimed : "mainmenu";

    private bool _loggedFirstPaint;
    private bool _menuInput;
    private UiCursor _cursor;
    private double _menusReadAt = double.NegativeInfinity;
    private double _hudReadAt = double.NegativeInfinity;

    public UiHost(ILog log, IGameClock clock, UiLayer hud, UiLayer menu, VguiBackend backend, IUiSurface surface,
                  LocalPlayerUi player, HudMessages messages, HudBuildStamp build, UiScaleConVar? scale, UiMenus menus)
    {
        (_log, _clock, _hud, _menu, _backend, _surface) = (log, clock, hud, menu, backend, surface);
        (_player, _messages, _build, _scale, _menus) = (player, messages, build, scale, menus);
        _claims = new(StringComparer.OrdinalIgnoreCase);
        _aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in menu.Screens)
        {
            if (screen.Panel is { } claimed)
                _claims.TryAdd(claimed, screen.Name);   // the first claimer in layer order answers
            foreach (var name in screen.Panels)         // a screen may speak for several panels
                _claims.TryAdd(name, screen.Name);
            foreach (var name in screen.Aliases)
                _aliases.TryAdd(name, screen.Name);
        }
        // The taskbar is up exactly while the main or pause menu is (the claimed one or the
        // core's), never in play, whatever windows are open.
        menu.ChromeWith = MainMenuScreen;
        menus.Teams.Close = () => Close("teammenu");
        menus.Motd.Closed = () => Close("motd");
        if (menus.Chat is { } chat)
            chat.Closed = () => Close("chatinput");
        if (menus.Main is { } main)
        {
            main.Closed = () => Close(MainMenuScreen);
            main.OptionsShown = () => Open("options");
        }

        // A closed screen is told, whichever way the close came — the Esc on
        // the top window, a named close, the window's own button: the screen
        // that answers the name learns its hide as the close lands.
        menu.Closed += name =>
        {
            foreach (var screen in menu.Screens)
                if (screen.Name == name)
                    screen.Closed();
        };
    }

    /// <summary>The HUD layer.</summary>
    public UiLayer Layer => _hud;

    /// <summary>The menu layer.</summary>
    public UiLayer Menu => _menu;

    /// <summary>The menu layer's game sources.</summary>
    public UiMenus Menus => _menus;

    public VguiBackend Backend => _backend;

    /// <summary>The layers' windows, which a reload carries (<see cref="UiWindowsReload"/>).</summary>
    public UiWindows Windows { get; init; } = new(new MemoryWindowStore());

    /// <inheritdoc/>
    public IUiTextureLibrary? Textures { get; init; }

    /// <summary>What the HUD layer's last frame drew.</summary>
    public UiDrawLedger HudLedger { get; } = new();

    /// <summary>What the menu layer's last frame drew.</summary>
    public UiDrawLedger MenuLedger { get; } = new();

    /// <summary>The menu layer's last screen.</summary>
    public ScreenInfo MenuScreen { get; private set; }

    /// <summary>The HUD layer's frames.</summary>
    public UiFrameCost Cost { get; } = new();

    /// <summary>The menu layer's frames.</summary>
    public UiFrameCost MenuCost { get; } = new();

    /// <summary>Input events received, and how many the console used.</summary>
    public long Inputs { get; private set; }

    /// <summary>The screen the last frame was laid out for.</summary>
    public ScreenInfo Screen { get; private set; }

    /// <summary>Whether the menu root has the mouse and keyboard now.</summary>
    public bool MenuInput => _menuInput;

    public void Paint(int layerKind, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return;

        if (layerKind == (int)UiLayerKind.Hud)
            PaintHud(screenWidth, screenHeight);
        else if (layerKind == (int)UiLayerKind.Menu)
            PaintMenu(screenWidth, screenHeight);
    }

    private ScreenInfo ScreenFor(int w, int h)
    {
        float uiScale = _scale is null ? 1f : Math.Clamp(_scale.Value <= 0f ? 1f : _scale.Value, 0.5f, 3f);
        return UiScale.Screen(w, h, uiScale);
    }

    private void PaintHud(int screenWidth, int screenHeight)
    {
        long start = Stopwatch.GetTimestamp();
        _player.Refresh();
        float now = _clock.Now;

        if (!_loggedFirstPaint)
        {
            _loggedFirstPaint = true;
            _log.Msg($"[SourceSharp/Hud] first paint: {screenWidth}x{screenHeight}, local health {_player.RawHealth}\n");
        }

        _messages.Refresh(now);
        _build.Refresh(now);

        // The scoreboard lives on this layer: its roster reads on the same
        // cadence the menu screens refresh at.
        if (now < _hudReadAt || now - _hudReadAt >= MenuRefreshSeconds)
        {
            _hudReadAt = now;
            if (_hud.IsOpen("scoreboard"))
                _menus.Scores.Refresh();
        }

        Screen = ScreenFor(screenWidth, screenHeight);
        _backend.Ledger = HudLedger;
        _hud.Frame(UiLayerKind.Hud, Screen, now);
        Cost.Add(Stopwatch.GetTimestamp() - start);
    }

    private bool _windowsCovered, _coverFailed;

    /// <summary>
    /// No window shows over the loading screen. A load that worked shuts them, or they
    /// would come up over play; one that failed gives them back, so the server browser
    /// is there to try another.
    /// </summary>
    private void CoverWindows()
    {
        bool covered = _menus.Loading is { CoversConsole: true };
        if (covered == _windowsCovered && !covered)
            return;
        _coverFailed |= _menus.Loading is { Body.Length: > 0 };
        _menu.HoldWindows(covered, ConsoleScreen);
        if (_windowsCovered && !covered)
        {
            if (!_coverFailed)
                _menu.CloseWindows(ConsoleScreen);
            _coverFailed = false;
        }
        _windowsCovered = covered;
    }

    private void PaintMenu(int screenWidth, int screenHeight)
    {
        long start = Stopwatch.GetTimestamp();
        double now = _clock.Now;
        _build.Refresh(now);        // the pause menu draws the stamp the HUD's refresh keeps
        if (now < _menusReadAt || now - _menusReadAt >= MenuRefreshSeconds)
        {
            _menusReadAt = now;
            if (_menu.IsOpen("teammenu"))
                _menus.Teams.Refresh();          // reads the scores too
            else if (_menu.IsOpen("scoreboard"))
                _menus.Scores.Refresh();         // the core scoreboard: no mod screen took its slot
        }

        _menu.Hold(ConsoleScreen, _consoleHidden || _menus.Loading is { CoversConsole: true });
        CoverWindows();

        MenuScreen = ScreenFor(screenWidth, screenHeight);
        _backend.Ledger = MenuLedger;
        _menu.Frame(UiLayerKind.Menu, MenuScreen, now);

        // What the layer wants of the menu root: carried out only on a change.
        bool wants = _menu.WantsMouse;
        if (wants != _menuInput)
        {
            _menuInput = wants;
            _surface.SetMenuInput(wants, wants);
        }

        var cursor = _menu.Context.List.Cursor;
        if (cursor != _cursor)
        {
            _cursor = cursor;
            _backend.SetCursor(cursor);
        }
        MenuCost.Add(Stopwatch.GetTimestamp() - start);
    }

    public void Input(int kind, int x, int y, int code, int character, float delta)
    {
        Inputs++;
        var e = new UiInputEvent((UiInputKind)kind, x, y, code, (char)character, delta);
        // The console's Tab completes: it does not walk the window's focus to a button.
        if (!(e.Kind == UiInputKind.Key && (UiKey)code == UiKey.Tab && Windows.Top == ConsoleScreen
              && _menu.IsOpen(ConsoleScreen) && !_menu.IsHeld(ConsoleScreen)))
            _menu.Input(e);

        // The chat's input line has the keyboard while it is open (stock messagemode).
        if (_menus.Chat is { } chat && _menu.IsOpen("chatinput"))
        {
            if (e.Kind == UiInputKind.Key)
                chat.Key((UiKey)code);
            else if (e.Kind == UiInputKind.Char)
                chat.Char(e.Char);
            return;
        }

        bool console = _menu.IsOpen(ConsoleScreen) && !_menu.IsHeld(ConsoleScreen) && Windows.Top == ConsoleScreen;
        if (e.Kind == UiInputKind.Key)
        {
            if ((UiKey)code == UiKey.Escape)
            {
                ToggleMenu();
                return;
            }
            if (console)
                _menus.Console.Key((UiKey)code);
        }
        else if (e.Kind == UiInputKind.Char && console)
        {
            _menus.Console.Char(e.Char);
        }
    }

    private double _escapeHandledAt = double.NegativeInfinity;

    /// <summary>One Escape decision per frame: the press can reach the host twice
    /// (the menu root's panel arm and the engine's gameui events), so two calls in
    /// the same frame count once.</summary>
    private bool ClaimEscape()
    {
        double now = _clock.Now;
        if (now == _escapeHandledAt)
            return false;
        _escapeHandledAt = now;
        return true;
    }

    /// <summary>
    /// The Escape decision while the menu holds the keyboard (stock ClientModeSingle::
    /// OnKeyDown): close the top thing that wants it — the team panel's screen, claimed
    /// or core's, then the info panel's the same way (TF2's own text window), then the
    /// top window — and with nothing open, toggle the pause menu. While the menu has no
    /// keyboard the engine's own arm answers Escape instead, and its gameui_activated/
    /// gameui_hidden events come to <see cref="ShowPauseMenu"/> and <see cref="HidePauseMenu"/>.
    /// </summary>
    public void ToggleMenu()
    {
        if (!ClaimEscape() || BackOutOfRadial())
            return;

        var teamScreen = _claims.TryGetValue("team", out var team) ? team : "teammenu";
        var infoScreen = _claims.TryGetValue("info", out var info) ? info : "motd";
        if (_menu.IsOpen(teamScreen)) Close(teamScreen);
        else if (_menu.IsOpen(infoScreen)) Close(infoScreen);
        else if (Windows.Top is { } top && _menu.IsOpen(top)) Close(top);
        else if (_menu.IsOpen(MainMenuScreen)) Close(MainMenuScreen);
        else if (_menu.Has(MainMenuScreen)) Open(MainMenuScreen);   // like Restore's skip: no screen, no decision
    }

    private bool BackOutOfRadial()
    {
        if (_menus.RadialSource is not { Showing: true })
            return false;
        _menus.RadialActions?.Back();
        return true;
    }

    /// <summary>The engine activated its GameUI (gameui_activated): the pause menu shows
    /// with it, so what the engine considers visible and what the menu draws agree.</summary>
    public void ShowPauseMenu()
    {
        // Escape over a radial menu is for the menu; the engine asks for its GameUI before
        // any panel hears the key, so that goes back with it, and no pause menu shows.
        if (BackOutOfRadial())
        {
            _menus.Main?.Resume();
            return;
        }
        HoldViewport(true);
        if (!ClaimEscape())
            return;
        if (_menu.Has(MainMenuScreen)) Open(MainMenuScreen);        // like Restore's skip: no screen, no decision
    }

    /// <summary>The engine hid its GameUI again (gameui_hidden, whatever hid it — its own
    /// Escape arm or the menu's Resume running gameui_hide): the pause menu goes with it.</summary>
    public void HidePauseMenu()
    {
        HoldViewport(false);
        if (!ClaimEscape())
            return;
        Close(MainMenuScreen);
    }

    private const string ConsoleScreen = "console";

    private const string VotePanelScreen = "votepanel";

    /// <summary>Whether the engine's GameUI is up, by its two edges.</summary>
    private bool _gameUiUp;

    /// <summary>The GameUI hid and took the console with it.</summary>
    private bool _consoleHidden;

    /// <summary>
    /// While the GameUI is up the engine hides the client viewport, and with it
    /// every stock panel shown there (CEngineVGui::ActivateGameUI hides the
    /// client root), without closing one: the screens that answer a viewport
    /// panel are held hidden in the menu layer, which the GameUI's override
    /// panel paints then, and let go when it hides. The main menu is the
    /// GameUI's own and is never held.
    /// </summary>
    private void HoldViewport(bool up)
    {
        _gameUiUp = up;
        foreach (var screen in _claims.Values)
            if (screen != MainMenuScreen)
                _menu.Hold(screen, up);
        foreach (var screen in Panels.Values)
            _menu.Hold(screen, up);
        _menu.Hold(VotePanelScreen, up);
        // The other way about: stock's console is a child of the GameUI's panel, so
        // it goes when the GameUI hides, stays open, and is back with it.
        _consoleHidden = !up;
        _menu.Hold(ConsoleScreen, _consoleHidden);
    }

    private bool IsViewportScreen(string screen)
        => screen != MainMenuScreen && (_claims.ContainsValue(screen) || Panels.Values.Contains(screen));

    /// <summary>A stock viewport panel: the UI takes the ones it has a screen for.</summary>
    public bool ViewportPanel(string name, bool show)
    {
        // The viewport hides "all" when it is rebuilt (a video-mode change,
        // a reconnect): the UI's screens are laid out against the screen as it
        // is, so they stay as they are, and the UI takes the sweep.
        if (string.Equals(name, AllPanels, StringComparison.OrdinalIgnoreCase))
            return !show;
        if (Absorbed.Contains(name))
            return true;
        if (PanelsFor(name) is not { } screen)
            return false;
        if (show)
        {
            // Which name the show came in under, for a screen that speaks
            // for several (TF2's class menu, opened as class_red or blue).
            foreach (var claimer in _menu.Screens)
                if (claimer.Name == screen)
                {
                    claimer.PanelOpened(name);
                    break;
                }
            Open(screen);
        }
        else Close(screen);
        return true;
    }

    /// <summary>The screen that answers a panel name for this host: its
    /// claiming menu screen, or the stock map's. Null answers no one.</summary>
    private string? PanelsFor(string panel)
        => _claims.TryGetValue(panel, out var claimed) ? claimed
         : Panels.TryGetValue(panel, out var screen) ? screen : null;

    /// <summary>The server's VGUIMenu, before the native viewport: a panel the UI answers takes its data here.</summary>
    public bool TakePanel(string name, bool show, IReadOnlyList<KeyValuePair<string, string>> data)
    {
        if (!Answers(name))
            return false;
        if (string.Equals(name, "info", StringComparison.OrdinalIgnoreCase))
            _menus.Motd.SetData(data);
        return ViewportPanel(name, show);
    }

    public void Open(string screen)
    {
        // A name a screen answers to (an options page) or a panel it claims
        // opens that screen, told which name it came in under.
        if ((_aliases.TryGetValue(screen, out var claimer) || _claims.TryGetValue(screen, out claimer)) && claimer != screen)
        {
            foreach (var s in _menu.Screens)
                if (s.Name == claimer)
                {
                    s.PanelOpened(screen);
                    break;
                }
            screen = claimer;
        }
        if (screen == "teammenu") _menus.Teams.Refresh();
        if (screen == "scoreboard") _menus.Scores.Refresh();
        _menusReadAt = _clock.Now;
        if (_hud.Has(screen))
            _hud.Open(screen);              // the hud-drawn screens (the scoreboard)
        else
            _menu.Open(screen);
        if (_gameUiUp && !_hud.Has(screen) && IsViewportScreen(screen))
            _menu.Hold(screen, true);       // shown under the hidden viewport, as stock's
        _log.Msg($"[SourceSharp/Ui] open {screen}\n");
    }

    public void Close(string screen)
    {
        if (_hud.Has(screen))
        {
            if (!_hud.IsOpen(screen))
                return;
            _hud.Close(screen);
        }
        else
        {
            if (!_menu.IsOpen(screen))
                return;
            _menu.Close(screen);
        }
        _log.Msg($"[SourceSharp/Ui] close {screen}\n");
    }

    public void ScreenSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        _log.Msg($"[SourceSharp/Ui] screen {oldWidth}x{oldHeight} -> {newWidth}x{newHeight}\n");
        _backend.ScreenSizeChanged();
    }

    public string? FindText(string token) => _menu.Context.FindText(token);

    public void MaterialsRestored()
    {
        _log.Msg("[SourceSharp/Ui] materials restored: making the fonts again\n");
        _backend.MaterialsRestored();
    }

    /// <summary>
    /// A new map (stock CBaseViewport::FireGameEvent on game_newmap, which hid
    /// every viewport panel): the screens that answer the stock panels close,
    /// and the chat's input line (stock's chat stopped its message mode at the
    /// level's end). GameUI's windows and the HUD are not the viewport's. The
    /// first-paint stamp is per level like the level-init stamps in
    /// ClientModule.LevelInit, so the arm re-arms: the runtime is carried
    /// across levels (UiCarryReload), and the tf2-gate's changelevel step
    /// measures the second map's load by the line appearing again.
    /// </summary>
    public void NewMap()
    {
        foreach (var (panel, screen) in Panels)
            if (!_claims.ContainsKey(panel))
                Close(screen);          // the stock screen, while it still answers
        foreach (var screen in _claims.Values)
            Close(screen);              // and every claiming screen, panel-first-winner
        if (Has("chatinput"))
            Close("chatinput");
        _loggedFirstPaint = false;
    }

    public bool Answers(string panel) => PanelsFor(panel) is not null || Absorbed.Contains(panel);

    /// <summary>Whether a screen, or a panel a screen claims, answers the name.</summary>
    public bool Has(string screen) => _menu.Has(screen) || _hud.Has(screen) || _aliases.ContainsKey(screen) || _claims.ContainsKey(screen);

    public IEnumerable<string> ScreenNames
        => _menu.Screens.Concat(_hud.Screens).Select(s => s.Name).Concat(_aliases.Keys).Concat(_claims.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> PanelNames
        => Panels.Keys.Concat(_claims.Keys).Concat(Absorbed)
                 .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string screen) => _menu.IsOpen(screen) || _hud.IsOpen(screen);

    /// <summary>The GameUI's IsMainMenuVisible: the main menu, the claimed one or the core's.</summary>
    public bool MainMenuOpen => _menu.IsOpen(MainMenuScreen);

    public long MenuFrames => MenuCost.Frames;

    private ClientDebugUi? _debug;

    public IDebugClientUi Debug => _debug ??= new ClientDebugUi(this);

    // ---- a UI-only reload (plan_client_ui U5) ----

    private const int CarryVersion = 1;

    /// <summary>The menu layer (open screens, focus, per-id state), the HUD layer's state, and the windows.</summary>
    public byte[] Carry()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(CarryVersion);
            foreach (byte[] part in new[] { _menu.Carry(), _hud.Carry(), Windows.Carry() })
            {
                w.Write(part.Length);
                w.Write(part);
            }
        }
        return stream.ToArray();
    }

    /// <summary><see cref="Carry"/>'s bytes from the build before; a screen reopened refreshes its sources first.</summary>
    public int Restore(byte[] carried)
    {
        using var r = new BinaryReader(new MemoryStream(carried));
        if (r.ReadInt32() != CarryVersion)
            return 0;
        byte[] menu = r.ReadBytes(r.ReadInt32()), hud = r.ReadBytes(r.ReadInt32()), windows = r.ReadBytes(r.ReadInt32());
        Windows.Restore(windows);
        _hud.Restore(hud, UiStateTypes.Resolve);
        int opened = _menu.Restore(menu, UiStateTypes.Resolve);
        _menus.Teams.Refresh();
        _menus.Scores.Refresh();
        _menusReadAt = _clock.Now;
        return opened;
    }

    /// <summary>Gives the mouse back if this UI had it: the next build takes it again on its first paint if it wants it.</summary>
    public void Dispose()
    {
        if (_menuInput)
        {
            _menuInput = false;
            _surface.SetMenuInput(false, false);
        }

        // The console window's tap on the side's lines goes with this runtime:
        // the next build installs its own window's tap (the chain rule in
        // ConsoleUi.Dispose), and an abandoned one would feed a dead list.
        _menus.Console.Dispose();
    }
}
