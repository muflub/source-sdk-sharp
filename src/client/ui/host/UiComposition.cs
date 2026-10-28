using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;
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
    /// The mod's own language file and theme override, under its game
    /// directory, read from disk. The mod's other language files
    /// (<see cref="GameLanguage"/>) come through the engine's filesystem.
    /// </summary>
    public const string LanguageFile = "resource/mod_sharp_english.txt", ThemeFile = "resource/ui-theme.yaml";

    /// <summary>
    /// The UI's copy, first file wins: the mod's language file on disk (it may
    /// override any core string and adds its own), then the files mods.yaml
    /// lists (<c>localisation</c>, engine GAME paths, so a VPK-mounted
    /// <c>resource/tf_english.txt</c> counts), then the core's #SS_UI_ strings,
    /// embedded in the toolkit, so a mod with no file (tf2_descent) still has
    /// them. GameUiLocaliser asks the engine for whatever this lacks.
    /// </summary>
    public static UiLanguageFile Language(string gameDir, ILog log, IEngineFiles? files, IReadOnlyList<string> gameFiles)
    {
        var chain = new List<UiLanguageFile>();
        string languagePath = Path.Combine(gameDir, LanguageFile);
        if (File.Exists(languagePath))
            chain.Add(UiLanguageFile.Load(languagePath));
        else
            log.Msg($"[SourceSharp/Ui] no {languagePath}: the mod's own UI strings are none\n");
        foreach (string path in gameFiles)
            if (GameLanguage(files, path, log) is { } file)
                chain.Add(file);
        chain.Add(UiLanguageFile.Core());
        return UiLanguageFile.Chain([.. chain]);
    }

    /// <summary>
    /// A language file on the engine's GAME search paths (a loose file or one
    /// in a mounted VPK), through <see cref="IEngineFiles.ReadGameFileBytes"/>
    /// (the FilesReadGameFile slot): bytes, since the file is UTF-16. Null,
    /// said in the log, when no path holds it or there is no engine to ask.
    /// </summary>
    public static UiLanguageFile? GameLanguage(IEngineFiles? files, string path, ILog log)
    {
        if (files?.ReadGameFileBytes(path) is { } bytes)
            return UiLanguageFile.Parse(bytes);
        log.Warning($"[SourceSharp/Ui] {path} (mods.yaml localisation) is on no GAME path: its tokens fall to the engine\n");
        return null;
    }

    public static UiHost Build(ServiceContainer services, ILog log, Hud hud, HudMessages messages, HudHints hints,
                               HudBuildStamp build, ILocalArsenal arsenal, UiScaleConVar? scale,
                               IFwdEngineClient engine, IFwdInputSystem input,
                               Func<IClientStringTables?>? stringTables = null)
    {
        var screen = services.Require<IEngineHud>();
        var mod = services.Require<IModInfo>();

        var theme = SourceSharpTheme.Create();
        string themePath = Path.Combine(mod.GameDir, ThemeFile);
        if (File.Exists(themePath))
            UiThemeFile.Apply(theme, File.ReadAllText(themePath));

        var fonts = new UiFontLibrary();
        string fontDir = Path.Combine(mod.ManagedDir, "fonts");
        var managed = new UiFontFiles(Directory.Exists(fontDir)
            ? Directory.GetFiles(fontDir, "*.ttf").Order(StringComparer.Ordinal).ToArray()
            : []);
        if (managed.Paths.Count == 0)
            log.Warning($"[SourceSharp/Ui] no fonts in {fontDir}: text falls back to the system's\n");
        // The theme's own fonts, from the game (the offline preview has no IEngineFiles and keeps the repo's).
        var game = GameFonts.Load(theme.FontFiles, fonts, services.Resolve<IEngineFiles>(), log);
        var fontFiles = new UiFontFiles([.. managed.Paths, .. game.Paths]);
        var surface = services.Require<IUiSurface>();
        var backend = new VguiBackend(surface, game.Metrics ?? fonts, fontFiles) { Icons = UiIconSet.Embedded() };

        // THE HUD ART (user ruling 2026-10-26): material names to uploaded
        // textures, over the engine's GAME paths. The mod's screens and the
        // debug commands take it; AddInstance so a UI reload replaces it.
        var textures = new UiTextureLibrary(services.Resolve<IEngineFiles>(), surface, log);
        services.AddInstance<IUiTextureLibrary>(textures);

        var language = Language(mod.GameDir, log, services.Resolve<IEngineFiles>(), mod.Localisation);

        var localiser = new GameUiLocaliser(language, screen);

        // The weapons' print names, from their scripts on disk (the client has
        // no engine file read: those slots are the server's).
        var scripts = services.Resolve<WeaponScriptDatabase>() ?? new WeaponScriptDatabase(log, path =>
        {
            string full = Path.Combine(mod.GameDir, path);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        });
        var names = new WeaponNames(c => scripts.ReadScript(c)?.PrintName, localiser.Find);
        var standalones = services.Require<IClientStandalones>();
        var player = new LocalPlayerUi(screen, arsenal, hud, standalones, names, services.Resolve<ViewReadout>());

        // THE MOD'S SCREENS (stage 0.9): every UiScreen in its UI assemblies,
        // built through the container, after the core's on the layer each names.
        var windows = new UiWindows(new MemoryWindowStore());
        var modScreens = UiModScreens.Build(services, UiModScreens.Assemblies(mod.UiAssemblies), windows);

        // THE HUD LAYER: its own context, so the menu's hit-testing never reads the HUD's frame.
        var hudScreen = new Ui.Screens.Hud(player, new VitalsPanel(player, player, new AuxPowerPanel(player)), new AmmoPanel(player, player),
                                           new CrosshairPanel(), new DamagePanel(player), new MapTextPanel(messages),
                                           new HintsPanel(hints), new BuildStampPanel(build),
                                           new KillFeedPanel(services.Require<IKillFeedSource>()),
                                           new VoicePanel(services.Require<IVoiceSource>()),
                                           new WeaponSelectionPanel(services.Resolve<WeaponSelectionHud>() is { } selection
                                               ? new WeaponSelectionUi(selection, services.Require<WeaponCatalog>(), names)
                                               : new NoWeaponSelection()));
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
        UiScreen[] menuScreens =
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
        var (hudPlaced, menuPlaced) = UiModScreens.Place(hudScreens, menuScreens, modScreens);
        var hudLayer = new UiLayer(hudPlaced, new UiContext(theme, backend, localiser, []), backend);
        var menuLayer = new UiLayer(menuPlaced, new UiContext(theme, backend, localiser, []), backend, windows);

        return new UiHost(log, services.Require<IGameClock>(), hudLayer, menuLayer, backend, services.Require<IUiSurface>(),
                          player, messages, build, scale, new UiMenus(scores, teams, motd, console, services.Resolve<ChatInputUi>()))
        {
            Windows = windows,
        };
    }
}
