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
    public const string ThemeFile = "resource/ui-theme.yaml";

    /// <summary>
    /// The disk name of the running mod's own language file: the name the
    /// engine's <c>CLocalizedStringTable::AddFile</c> asks for in the same
    /// launch: <c>resource/&lt;game directory&gt;_&lt;language&gt;.txt</c>, the
    /// game directory's name with the language <c>english</c>. So the game dir
    /// <c>mods/tf2</c> asks <c>resource/tf2_english.txt</c>, <c>mods/test</c>
    /// asks <c>resource/test_english.txt</c>. Both readers read that one file:
    /// the engine from its GAME paths, this chain from disk beside it.
    /// </summary>
    public static string LanguageFileName(string gameDir) =>
        Path.Combine("resource", Path.GetFileName(Path.TrimEndingDirectorySeparator(gameDir)) + "_english.txt");

    /// <summary>
    /// The engine's GameUI strings (#GameUI_*: disconnect reasons, options
    /// labels): the stock GameUI module loaded this file at its Initialize and
    /// nothing else does (docs/gameui-census.md §5), so the UI reads it off
    /// the GAME paths itself (a loose file in <c>hl2/resource</c>).
    /// </summary>
    public const string GameUiLanguageFile = "resource/gameui_english.txt";

    /// <summary>
    /// The UI's copy, first file wins: the mod's language file on disk (it may
    /// override any core string and adds its own), then the files mods.yaml
    /// lists (<c>localisation</c>, engine GAME paths, so a VPK-mounted
    /// <c>resource/tf_english.txt</c> counts), then the engine's GameUI strings
    /// (<see cref="GameUiLanguageFile"/>), then the core's #SS_UI_ strings,
    /// embedded in the toolkit, so a mod with no file (tf2_descent) still has
    /// them. GameUiLocaliser asks the engine for whatever this lacks.
    /// </summary>
    public static UiLanguageFile Language(string gameDir, ILog log, IEngineFiles? files, IReadOnlyList<string> gameFiles)
    {
        var chain = new List<UiLanguageFile>();
        string languagePath = Path.Combine(gameDir, LanguageFileName(gameDir));
        if (File.Exists(languagePath))
            chain.Add(UiLanguageFile.Load(languagePath));
        else
            log.Msg($"[SourceSharp/Ui] no {languagePath}: the mod's own UI strings are none\n");
        foreach (string path in gameFiles)
            if (GameLanguage(files, path, log) is { } file)
                chain.Add(file);
        if (files?.ReadGameFileBytes(GameUiLanguageFile) is { } gameUi)
        {
            try
            {
                chain.Add(UiLanguageFile.Parse(gameUi));
            }
            catch (InvalidDataException e)
            {
                log.Warning($"[SourceSharp/Ui] {GameUiLanguageFile} is no language file ({e.Message}): #GameUI_ tokens fall to the engine\n");
            }
        }
        else if (files is not null)
            log.Warning($"[SourceSharp/Ui] {GameUiLanguageFile} is on no GAME path: #GameUI_ tokens fall to the engine\n");
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
        // THE SIDE ADAPTERS' LOCALISER (IUiLocalisation in the interface
        // assembly, stable across a UI reload): a mod adapter that localises
        // its tokens asks this, not the engine's tables, which hold none of
        // the mod's. AddInstance so a UI reload replaces it, as the textures'.
        // The chat and TextMsg read it too (stock's client had ONE
        // g_pVGuiLocalize): they are composed before this context loads and
        // ask the container when a message arrives; a UI reload replaces it.
        services.AddInstance<IUiLocalisation>(localiser);

        // THE BINDS (the HUD's slot, stock Key_LookupBinding): the mod's
        // screens ask the container for the bound keys, so the store is an
        // instance before they are built — the same one the binds form reads.
        services.AddInstance<IUiKeyBindingStore>(new EngineKeyBindingStore(screen, engine, input));

        // The weapons' print names, from their scripts on the engine's GAME
        // paths (the IEngineFiles slot the fonts, textures and language files
        // already read): the old disk reader could not reach a VPK, and
        // shipped TF2 scripts ride only in one. Stock's ReadEncryptedKVFile
        // (weapon_parse.cpp:202-267) reads <name>.txt first and then the
        // ICE-decrypted <name>.ctx with the key stock's callers hand as
        // g_pGameRules->GetEncryptionKey(); here that key is the mod's
        // mods.yaml encryption_key row, and both spellings come over the
        // same bytes reader (the client's ReadGameFile slot is the server's).
        var files = services.Resolve<IEngineFiles>();
        var scripts = services.Resolve<WeaponScriptDatabase>() ?? new WeaponScriptDatabase(log,
            path => files?.ReadGameFileBytes(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null,
            path => files?.ReadGameFileBytes(path),
            mod.EncryptionKey);
        var names = new WeaponNames(c => scripts.ReadScript(c)?.PrintName, localiser.Find);
        var standalones = services.Require<IClientStandalones>();
        var player = new LocalPlayerUi(screen, arsenal, hud, standalones, names, services.Resolve<ViewReadout>());

        // THE SCOREBOARD'S DATA, before the mod screens: a mod screen that
        // replaces the core's takes it over by constructor, and the mod's UI
        // assemblies cannot name ScoreboardUi (they hold no core screen), so it
        // reaches them as the IScoreboardSource the container now answers for.
        var scores = new ScoreboardUi(standalones, screen, engine, services.Require<IPlayerNames>(),
                                      services.Require<IEngineConsole>(),
                                      services.Require<IEngineEnvironment>(), services.Resolve<TeamNames>());
        services.AddInstance<IScoreboardSource>(scores);

        // THE TEXT WINDOW'S DATA, before the mod screens, as the scoreboard's:
        // a mod window that replaces the core's motd (TF2's text window, stage
        // 3.2) reads it through the constructor, so the server's VGUIMenu
        // "info" reaches the claiming window as the IMotdSource the container
        // now answers for.
        IClientStringTables? tables = null;
        var motd = new MotdUi(entry => (tables ??= stringTables?.Invoke())?.UserDataText(MotdUi.InfoPanelTable, entry));
        services.AddInstance<IMotdSource>(motd);

        // The pause menu's buttons: the host wires them to its layer's opens
        // and closes (UiHost's ctor), and Resume runs the engine's gameui_hide
        // so engine and menu agree (the hide comes back as gameui_hidden).
        // Play's map is the mod's own (plan_client_novalve §3.2a's create-server
        // row): maplist.txt on the game's paths, the launch menu's start.
        var mainMenu = new MainMenuUi(engine, MenuMapList.First(services.Resolve<IEngineFiles>(), log));
        // Before the mod screens, as the text window's data: a mod's main menu
        // (TF2's, claiming UiHost.MainMenuPanel) reads the same level answer
        // and Resume the core's menu does.
        services.AddInstance<IMainMenuSource>(mainMenu);
        // The stock GameUI's backdrop, drawn by the menu layer's lowest screen
        // while no level is up (plan_gameui_managed.md G1).
        var menuBackground = new MenuBackgroundUi(services.Require<IMenuBackgroundName>(), mainMenu);
        services.AddInstance<IMenuBackgroundSource>(menuBackground);
        // The GameUI's load dialog, its disconnect form and Quit's question
        // (plan_gameui_managed.md G1), driven by the GameUI host from G4.
        var loading = new GameUiLoading(localiser, engine);
        services.AddInstance(loading).AddInstance<ILoadingProgressSource>(loading).AddInstance<IDisconnectMessageSource>(loading);
        var confirmQuit = new ConfirmQuitUi(engine);
        services.AddInstance(confirmQuit).AddInstance<IConfirmQuitSource>(confirmQuit);

        // THE MOD'S SCREENS (stage 0.9): every UiScreen in its UI assemblies,
        // built through the container, after the core's on the layer each names.
        var windows = new UiWindows(new MemoryWindowStore());
        var modScreens = UiModScreens.Build(services, UiModScreens.Assemblies(mod.UiAssemblies), windows);

        var selectionHud = services.Resolve<WeaponSelectionHud>();

        // THE HUD LAYER: its own context, so the menu's hit-testing never reads the HUD's frame.
        var hudScreen = new Ui.Screens.Hud(player, new VitalsPanel(player, player, new AuxPowerPanel(player)), new AmmoPanel(player, player),
                                           new CrosshairPanel(), new DamagePanel(player), new MapTextPanel(messages),
                                           new HintsPanel(hints), new BuildStampPanel(build),
                                           new KillFeedPanel(services.Require<IKillFeedSource>()),
                                           new VoicePanel(services.Require<IVoiceSource>()),
                                           new WeaponSelectionPanel(selectionHud is { } selection
                                               ? new WeaponSelectionUi(selection, services.Require<WeaponCatalog>(), names)
                                               : new NoWeaponSelection()),
                                           new WeaponPlusPanel(selectionHud is { } plus
                                               ? new WeaponPlusUi(plus, services.Require<WeaponCatalog>(), names)
                                               : new NoWeaponPlus()),
                                           new WeaponCarouselPanel(selectionHud is { } carousel
                                               ? new WeaponCarouselUi(carousel, services.Require<WeaponCatalog>(), names)
                                               : new NoWeaponCarousel()),
                                           new PickupHistoryPanel(services.Resolve<PickupHistory>() is { } history
                                               && services.Resolve<LocalPickupHistory>() is { } feed
                                               ? new PickupHistoryUi(history, feed)
                                               : new NoPickupHistory()));
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
        var teams = new TeamMenuUi(scores, screen, engine, log);
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
            new KeyBindings(windows, services.Require<IUiKeyBindingStore>()),
            new ChatInput(services.Require<IChatInputSource>()),
            new MainMenu(mainMenu),
            new MenuBackground(menuBackground, textures),
            new LoadingProgress(loading),
            new DisconnectMessage(loading),
            new ConfirmQuit(confirmQuit),
            new Ui.Screens.LoadingDisc(services.Require<ILoadingDiscSource>()),
        ];
        var (hudPlaced, menuPlaced) = UiModScreens.Place(hudScreens, menuScreens, modScreens);
        var hudLayer = new UiLayer(hudPlaced, new UiContext(theme, backend, localiser, []), backend);
        var menuLayer = new UiLayer(menuPlaced, new UiContext(theme, backend, localiser, []), backend, windows);

        var host = new UiHost(log, services.Require<IGameClock>(), hudLayer, menuLayer, backend, services.Require<IUiSurface>(),
                              player, messages, build, scale,
                              new UiMenus(scores, teams, motd, console, services.Resolve<ChatInputUi>(), mainMenu))
        {
            Windows = windows,
            Textures = textures,
        };
        // Where the engine's GameUI calls land (bound from G4), over this UI;
        // AddInstance so a UI reload hands the binding the new one.
        services.AddInstance(new GameUiHost(host, loading, confirmQuit));
        return host;
    }
}
