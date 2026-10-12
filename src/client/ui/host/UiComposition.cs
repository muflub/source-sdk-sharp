using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The UI host's entry (plan_client_ui U5): what the core's UiLoader
/// constructs in the UI's load context and asks for the runtime.
/// </summary>
public sealed class UiComposition : IUiRuntimeFactory
{
    public IUiRuntime Create(UiRuntimeServices s)
        => ClientUi.Build(s.Services, s.Log, s.Hud, s.Hud.Messages, s.Hud.Hints, s.Hud.Build, s.Arsenal, s.Scale, s.Engine, s.Input,
                          s.StringTables);
}

/// <summary>
/// The UI's composition in game (plan_client_ui §8 U2): the toolkit's
/// context and layer over <see cref="VguiBackend"/>, and the HUD's screen with
/// its panels over the game's sources. It lives in the UI host assembly, so
/// the core never names a toolkit or screen type (U5).
/// </summary>
public static class ClientUi
{
    /// <summary>
    /// The mod's language file and theme override, under its game directory.
    /// Read from disk: the engine's file reads are the server's slots only.
    /// </summary>
    public const string LanguageFile = "resource/mod_sharp_english.txt", ThemeFile = "resource/ui-theme.yaml";

    /// <summary>
    /// The UI's copy: the mod's language file first (it may override any core
    /// string and adds its own), then the core's #SS_UI_ strings, embedded in
    /// the toolkit, so a mod with no file (tf2_descent) still has them.
    /// </summary>
    public static UiLanguageFile Language(string gameDir, ILog log)
    {
        var core = UiLanguageFile.Core();
        string languagePath = Path.Combine(gameDir, LanguageFile);
        if (!File.Exists(languagePath))
        {
            log.Msg($"[SourceSharp/Ui] no {languagePath}: the core's UI strings only\n");
            return core;
        }
        return UiLanguageFile.Chain(UiLanguageFile.Load(languagePath), core);
    }

    public static UiHost Build(ServiceContainer services, ILog log, Hud hud, HudMessages messages, HudHints hints,
                               HudBuildStamp build, ILocalArsenal arsenal, UiScaleConVar? scale,
                               Func<IVtableEngineClient?> engine, Func<IVtableInputSystem?> input,
                               Func<IClientStringTables?>? stringTables = null)
    {
        var screen = services.Require<IEngineHud>();
        var mod = services.Require<IModInfo>();

        var fonts = new UiFontLibrary();
        string fontDir = Path.Combine(mod.ManagedDir, "fonts");
        var fontFiles = new UiFontFiles(Directory.Exists(fontDir)
            ? Directory.GetFiles(fontDir, "*.ttf").Order(StringComparer.Ordinal).ToArray()
            : []);
        if (fontFiles.Paths.Count == 0)
            log.Warning($"[SourceSharp/Ui] no fonts in {fontDir}: text falls back to the system's\n");
        var backend = new VguiBackend(services.Require<IUiSurface>(), fonts, fontFiles) { Icons = UiIconSet.Embedded() };

        var theme = SourceSharpTheme.Create();
        string themePath = Path.Combine(mod.GameDir, ThemeFile);
        if (File.Exists(themePath))
            UiThemeFile.Apply(theme, File.ReadAllText(themePath));

        var language = Language(mod.GameDir, log);

        var localiser = new GameUiLocaliser(language, screen);

        // The weapons' print names, from their scripts on disk (the client has
        // no engine file read: those slots are the server's).
        var scripts = new WeaponScriptDatabase(log, path =>
        {
            string full = Path.Combine(mod.GameDir, path);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        });
        var names = new WeaponNames(c => scripts.ReadScript(c)?.PrintName, localiser.Find);
        var standalones = services.Require<IClientStandalones>();
        var player = new LocalPlayerUi(screen, arsenal, hud, standalones, names, services.Resolve<ViewReadout>());

        // THE HUD LAYER: its own context, so the menu's hit-testing never reads the HUD's frame.
        var hudScreen = new Ui.Screens.Hud(player, new VitalsPanel(player, player), new AmmoPanel(player, player),
                                           new CrosshairPanel(), new DamagePanel(player), new MapTextPanel(messages),
                                           new HintsPanel(hints), new BuildStampPanel(build),
                                           new KillFeedPanel(services.Require<IKillFeedSource>()),
                                           new VoicePanel(services.Require<IVoiceSource>()));
        var centre = new Ui.Screens.CentrePrint(services.Require<ICentrePrintSource>());
        var chat = new Chat(services.Require<IChatSource>());
        var overlayText = new Ui.Screens.DebugOverlayText(services.Require<IDebugOverlaySource>());
        // THE SPECTATOR (stock CSpectatorGUI): the top bar, and who is watched.
        var spec = new SpecGui(services.Require<IObserverSource>());
        var specTarget = new SpectatorTarget(services.Require<ISpectatorTargetSource>());
        // The debug camera's panel (lane camproto), when the core has the camera.
        UiScreen[] hudScreens = services.Resolve<IViewCameraSource>() is { } camera
            ? [hudScreen, centre, chat, overlayText, spec, specTarget, new ViewCameraHud(camera)]
            : [hudScreen, centre, chat, overlayText, spec, specTarget];
        var hudLayer = new UiLayer(hudScreens, new UiContext(theme, backend, localiser, []), backend);

        // THE MENU LAYER (plan_client_ui U3): the screens the server, the binds
        // and cs_ui_open show, and the windows they live in, which a reload carries.
        var scores = new ScoreboardUi(standalones, screen, engine, services.Require<IPlayerNames>(),
                                      services.Require<IEngineConsole>(),
                                      services.Require<IEngineEnvironment>());
        var teams = new TeamMenuUi(scores, screen, engine, log);
        IClientStringTables? tables = null;
        var motd = new MotdUi(entry => (tables ??= stringTables?.Invoke())?.UserDataText(MotdUi.InfoPanelTable, entry));
        // The engine's prints join the typed lines the window shows: the side's
        // ConsoleLines, whose feed the display func lands on (docs/todo.md's
        // console-window follow-up). Optional: the UI loader's test host
        // composes without the side's container, and a window with no feed
        // just mirrors the typed lines, the pre-capture behaviour.
        var console = new ConsoleUi(engine, services.Resolve<ConsoleLines>());
        var browser = new ServerBrowserUi();
        var windows = new UiWindows(new MemoryWindowStore());
        UiScreen[] menus =
        [
            new Ui.Screens.Scoreboard(scores, new TeamTable(scores)),
            new TeamMenu(teams, new TeamCard(), teams),
            new Motd(motd, motd),
            new ConsoleScreen(windows, new ConsoleLogPanel(console), new ConsoleInputPanel(console, console),
                              new ConsoleCompletionsPanel(console)),
            new ServerBrowser(windows, browser, new ServerFilterBar(browser), new ServerTable(browser, browser, browser, browser),
                              new ServerDetailsPanel(browser, browser)),
            new MouseOptions(windows, new EngineConVarStore(services.Require<IEngineConsole>())),
            new KeyBindings(windows, new EngineKeyBindingStore(screen, engine, input)),
            new ChatInput(services.Require<IChatInputSource>()),
            new Ui.Screens.LoadingDisc(services.Require<ILoadingDiscSource>()),
        ];
        var menuLayer = new UiLayer(menus, new UiContext(theme, backend, localiser, []), backend, windows);

        return new UiHost(log, services.Require<IGameClock>(), hudLayer, menuLayer, backend, services.Require<IUiSurface>(),
                          player, messages, build, scale, new UiMenus(scores, teams, motd, console, services.Resolve<ChatInputUi>()))
        {
            Windows = windows,
        };
    }
}
