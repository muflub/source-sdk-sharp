using System.Diagnostics;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>The menu layer's sources the host refreshes and the panels it maps.</summary>
public sealed record UiMenus(ScoreboardUi Scores, TeamMenuUi Teams, MotdUi Motd, ConsoleUi Console, ChatInputUi? Chat = null);

/// <summary>
/// THE UI IN GAME (plan_client_ui §8 U2, U3): what IManagedUiClient forwards to.
/// Two layers: the HUD (the HUD panel's paint) and the menus (the menu root's
/// paint and input). Each paint refreshes what its screens read, lays the layer
/// out against the screen as it is now (size is an input, never a cache) and
/// draws it through <see cref="VguiBackend"/>. The menu layer decides whether
/// the menu root takes the mouse and keyboard, and its cursor.
/// </summary>
public sealed class UiHost : IUiRuntime
{
    /// <summary>Stock viewport panel names and the UI screens that answer them.</summary>
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
    private bool _loggedFirstPaint;
    private bool _menuInput;
    private UiCursor _cursor;
    private double _menusReadAt = double.NegativeInfinity;

    public UiHost(ILog log, IGameClock clock, UiLayer hud, UiLayer menu, VguiBackend backend, IUiSurface surface,
                  LocalPlayerUi player, HudMessages messages, HudBuildStamp build, UiScaleConVar? scale, UiMenus menus)
    {
        (_log, _clock, _hud, _menu, _backend, _surface) = (log, clock, hud, menu, backend, surface);
        (_player, _messages, _build, _scale, _menus) = (player, messages, build, scale, menus);
        menus.Teams.Close = () => Close("teammenu");
        menus.Motd.Closed = () => Close("motd");
        if (menus.Chat is { } chat)
            chat.Closed = () => Close("chatinput");
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

        Screen = ScreenFor(screenWidth, screenHeight);
        _backend.Ledger = HudLedger;
        _hud.Frame(UiLayerKind.Hud, Screen, now);
        Cost.Add(Stopwatch.GetTimestamp() - start);
    }

    private void PaintMenu(int screenWidth, int screenHeight)
    {
        long start = Stopwatch.GetTimestamp();
        double now = _clock.Now;
        if (now < _menusReadAt || now - _menusReadAt >= MenuRefreshSeconds)
        {
            _menusReadAt = now;
            if (_menu.IsOpen("teammenu"))
                _menus.Teams.Refresh();          // reads the scores too
            else if (_menu.IsOpen("scoreboard"))
                _menus.Scores.Refresh();
        }

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

        bool console = _menu.IsOpen("console") && Windows.Top == "console";
        if (e.Kind == UiInputKind.Key)
        {
            if ((UiKey)code == UiKey.Escape)
            {
                // Esc closes the top thing that wants the keyboard.
                if (_menu.IsOpen("teammenu")) Close("teammenu");
                else if (_menu.IsOpen("motd")) Close("motd");
                else if (Windows.Top is { } top && _menu.IsOpen(top)) Close(top);
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
        if (!Panels.TryGetValue(name, out var screen))
            return false;
        if (show) Open(screen); else Close(screen);
        return true;
    }

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
        if (screen == "teammenu") _menus.Teams.Refresh();
        if (screen == "scoreboard") _menus.Scores.Refresh();
        _menusReadAt = _clock.Now;
        _menu.Open(screen);
        _log.Msg($"[SourceSharp/Ui] open {screen}\n");
    }

    public void Close(string screen)
    {
        if (!_menu.IsOpen(screen))
            return;
        _menu.Close(screen);
        _log.Msg($"[SourceSharp/Ui] close {screen}\n");
    }

    public void ScreenSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        _log.Msg($"[SourceSharp/Ui] screen {oldWidth}x{oldHeight} -> {newWidth}x{newHeight}\n");
        _backend.ScreenSizeChanged();
    }

    /// <summary>
    /// A new map (stock CBaseViewport::FireGameEvent on game_newmap, which hid
    /// every viewport panel): the screens that answer the stock panels close,
    /// and the chat's input line (stock's chat stopped its message mode at the
    /// level's end). GameUI's windows and the HUD are not the viewport's.
    /// </summary>
    public void NewMap()
    {
        foreach (var screen in Panels.Values)
            Close(screen);
        if (Has("chatinput"))
            Close("chatinput");
    }

    public bool Answers(string panel) => Panels.ContainsKey(panel) || Absorbed.Contains(panel);

    public bool Has(string screen) => _menu.Has(screen);

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
