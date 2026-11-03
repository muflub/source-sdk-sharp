using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The GameUI host (plan_gameui_managed.md G1): where every call the engine
/// makes of GameUI011 and GameConsole004 lands, routed to the UI. Driven here
/// through C0's recorded sequence of one session (plan_client_novalve.md §8,
/// "C0 probe results" item 3) over fakes of the UI.
/// </summary>
public class GameUiHostTests
{
    private sealed class Screens : IGameUiNavigation
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> Open { get; } = [];
        public bool MainMenu { get; set; }

        public void ShowPauseMenu() => Calls.Add("show-pause");
        public void HidePauseMenu() => Calls.Add("hide-pause");
        public bool MainMenuOpen => MainMenu;
        public long Frames { get; set; }
        public long MenuFrames => Frames;
        void IGameUiNavigation.Open(string screen)
        {
            Calls.Add("open " + screen);
            Open.Add(screen);
        }
        public void Close(string screen)
        {
            Calls.Add("close " + screen);
            Open.Remove(screen);
        }
        public bool IsOpen(string screen) => Open.Contains(screen);
    }

    private sealed class History : IServerHistory
    {
        public List<(uint Ip, ushort Connection, ushort Query)> Recorded { get; } = [];

        public void Record(uint ip, ushort connectionPort, ushort queryPort) => Recorded.Add((ip, connectionPort, queryPort));
    }

    private sealed class Strings : IUiLocalisation
    {
        public string? Find(string token) => token == "#LoadingProgress_Connecting" ? "Connecting to server..." : null;
    }

    private static (GameUiHost Host, Screens Screens, GameUiLoading Loading, ConfirmQuitUi Quit, List<string> Sent) Rig()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var screens = new Screens();
        var loading = new GameUiLoading(new Strings(), engine);
        var quit = new ConfirmQuitUi(engine);
        return (new GameUiHost(screens, loading, quit, new GameMenuCommands(screens, quit, engine), new History(), new GameUiLoadMeter()), screens, loading, quit, sent);
    }

    [Fact]
    public void The_gameuis_activation_and_hide_drive_the_pause_menu()
    {
        var (host, screens, _, _, _) = Rig();
        host.OnGameUIActivated();
        host.OnGameUIHidden();
        Assert.Equal(["show-pause", "hide-pause"], screens.Calls);
    }

    /// <summary>C0's map load: OnDisconnectFromServer(0), OnLevelLoadingStarted,
    /// the progress updates, OnConnectToServer2, OnLevelLoadingFinished,
    /// OnGameUIHidden; with the dialog asked for, it shows, steps, and ends in
    /// the engine's gameui_hide.</summary>
    [Fact]
    public void A_map_load_runs_the_load_dialog_through_to_the_hide()
    {
        var (host, screens, loading, _, sent) = Rig();
        host.OnDisconnectFromServer(0);
        host.OnLevelLoadingStarted(true);
        Assert.True(loading.Showing);
        Assert.True(host.UpdateProgressBar(0.2f, "#LoadingProgress_Connecting"));
        Assert.Equal("Connecting to server...", loading.Status);
        Assert.False(host.UpdateProgressBar(0.2f, "#LoadingProgress_Connecting"));
        host.OnConnectToServer2("game", 0x0100007f, 27015, 27015);
        host.OnLevelLoadingFinished(false, "", "");
        host.OnGameUIHidden();
        Assert.False(loading.Showing);
        Assert.Equal(["gameui_hide"], sent);
        Assert.Equal(["hide-pause"], screens.Calls);
    }

    [Fact]
    public void A_steam_login_failure_mid_load_leaves_the_disconnect_dialog()
    {
        var (host, _, loading, _, _) = Rig();
        host.OnLevelLoadingStarted(true);
        host.OnDisconnectFromServer(4);
        Assert.True(((IDisconnectMessageSource)loading).Showing);
    }

    [Fact]
    public void The_engines_confirm_quit_asks_the_question()
    {
        var (host, _, _, quit, sent) = Rig();
        host.OnConfirmQuit();
        Assert.True(quit.Asking);
        Assert.Empty(sent);
    }

    [Fact]
    public void Is_main_menu_visible_answers_the_main_menu()
    {
        var (host, screens, _, _, _) = Rig();
        Assert.False(host.IsMainMenuVisible());
        screens.MainMenu = true;
        Assert.True(host.IsMainMenuVisible());
    }

    [Fact]
    public void The_console_methods_open_close_and_ask_the_console_window()
    {
        var (host, screens, _, _, _) = Rig();
        Assert.False(host.IsConsoleVisible());
        host.ConsoleActivate();
        Assert.True(host.IsConsoleVisible());
        host.ConsoleHide();
        Assert.False(host.IsConsoleVisible());
        Assert.Equal(["open console", "close console"], screens.Calls);
    }

    /// <summary>The single-player, bonus-map, Xbox and session methods, and
    /// the stock module's calls to itself, touch neither the UI nor the engine.</summary>
    [Fact]
    public void The_inert_methods_touch_nothing()
    {
        var (host, screens, loading, quit, sent) = Rig();
        host.Initialize(1);
        host.PostInit();
        host.Connect(1);
        host.Start();
        host.RunFrame();
        host.OldOnConnectToServer("g", 1, 2);
        host.ShowNewGameDialog(1);
        host.SessionNotification(1, 2);
        host.SystemNotification(1);
        host.ShowMessageDialog(1, 0);
        host.UpdatePlayerInfo(1, "p", 2, 0, 0, false);
        host.SessionSearchResult(0, 0, 0, 0);
        host.OnCreditsFinished();
        host.SetLoadingBackgroundDialog(1);
        host.BonusMapUnlock("f", "m");
        host.BonusMapComplete("f", "m");
        host.BonusMapChallengeUpdate("f", "m", "c", 1);
        host.BonusMapDatabaseSave();
        Assert.Equal(0, host.BonusMapNumAdvancedCompleted());
        Assert.False(host.ValidateStorageDevice());
        Assert.False(host.SetShowProgressText(true));
        host.SetProgressOnStart();
        host.SetMainMenuOverride(1);
        host.SendMainMenuCommand("OpenNewGameDialog");
        host.ConsoleInitialize();
        host.ConsoleClear();
        host.ConsoleSetParent(1);
        host.Shutdown();
        Assert.Empty(screens.Calls);
        Assert.Empty(sent);
        Assert.False(loading.Showing);
        Assert.False(quit.Asking);
    }

    /// <summary>A server joined goes into the browser's history: the engine's
    /// network-order address read in host order, its ports as given.</summary>
    [Fact]
    public void A_server_joined_goes_into_the_browsers_history()
    {
        var screens = new Screens();
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var quit = new ConfirmQuitUi(engine);
        var history = new History();
        var host = new GameUiHost(screens, new GameUiLoading(new Strings(), engine), quit, new GameMenuCommands(screens, quit, engine), history, new GameUiLoadMeter());
        // 192.168.1.20 in network order, as GetIPNetworkByteOrder hands it on a little-endian machine.
        host.OnConnectToServer2("hl2mp", BitConverter.ToInt32([192, 168, 1, 20]), 27015, 27016);
        Assert.Equal([(0xC0A80114u, (ushort)27015, (ushort)27016)], history.Recorded);
    }

    /// <summary>A menu command the engine forwards (IGameUI::SendMainMenuCommand)
    /// runs through the game menu's table, as gamemenucommand does.</summary>
    [Fact]
    public void A_main_menu_command_runs_through_the_game_menus_table()
    {
        var (host, screens, _, _, _) = Rig();
        host.SendMainMenuCommand("OpenServerBrowser");
        Assert.Equal(["open serverbrowser"], screens.Calls);
    }

    /// <summary>The game's composition registers the host over its own UI:
    /// the activation opens the main menu that UI holds.</summary>
    [Fact]
    public void The_composed_ui_registers_the_host_over_itself()
    {
        string dir = Directory.CreateTempSubdirectory("ss-gameuihost-").FullName;
        try
        {
            var engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
                .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
                .Returns("Environment.Globals", default(SourceSharp.Abi.Globals));
            var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), dir);
            var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                              new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
            var ui = ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                                    null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
            var host = services.Require<GameUiHost>();
            Assert.False(ui.Menu.IsOpen("mainmenu"));
            host.OnGameUIActivated();
            Assert.True(ui.Menu.IsOpen("mainmenu"));
            Assert.True(host.IsMainMenuVisible());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Stock's console is a child of the GameUI's panel: Escape to play takes
    /// it away, and it is back, as it was, with the GameUI. Over the game it took the
    /// window and the keys.</summary>
    [Fact]
    public void The_console_hides_with_the_GameUI_and_comes_back_with_it()
    {
        string dir = Directory.CreateTempSubdirectory("ss-gameuihost-").FullName;
        try
        {
            var engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
                .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
                .Returns("Environment.Globals", default(SourceSharp.Abi.Globals));
            var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), dir);
            var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                              new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
            var ui = ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                                    null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
            var host = services.Require<GameUiHost>();
            var line = (ConsoleUi)services.Require<IConsoleLogSource>();
            host.OnGameUIActivated();
            host.ConsoleActivate();
            Assert.False(ui.Menu.IsHeld("console"));

            host.OnGameUIHidden();
            Assert.True(ui.Menu.IsHeld("console"));
            Assert.True(ui.Menu.IsOpen("console"));
            ui.Input((int)UiInputKind.Char, 0, 0, 0, 'x', 0f);
            Assert.Equal("", line.Input);

            host.OnGameUIActivated();
            Assert.False(ui.Menu.IsHeld("console"));
            Assert.True(ui.Menu.IsOpen("console"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // The composed UI over a radial menu a fact controls, and what the engine was told.
    private static (UiHost Ui, GameUiHost Host, SourceSharp.Client.Ui.Screens.FakeRadialMenu Radial, ServiceContainer Services, string Dir) Composed()
    {
        string dir = Directory.CreateTempSubdirectory("ss-gameuihost-").FullName;
        var engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
            .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
            .Returns("Environment.Globals", default(SourceSharp.Abi.Globals)).Returns("Hud.LookupBinding", "");
        var radial = new SourceSharp.Client.Ui.Screens.FakeRadialMenu("Title") { Showing = false };
        var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), dir, radial: radial);
        var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
            var ui = ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                                    null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
        return (ui, services.Require<GameUiHost>(), radial, services, dir);
    }

    private static void Done(string dir) => Directory.Delete(dir, recursive: true);

    private static void Paint(UiHost ui) => ui.Paint((int)UiLayerKind.Menu, 1920, 1080);

    /// <summary>The engine asks for its GameUI on Escape before any panel hears the key.</summary>
    [Fact]
    public void Escape_over_a_radial_menu_closes_it_and_shows_no_pause_menu()
    {
        var (ui, host, radial, _, dir) = Composed();
        try
        {
            radial.Showing = true;
            host.OnGameUIActivated();
            Assert.Equal(["back"], radial.Done);
            Assert.False(ui.Menu.IsOpen("mainmenu"));
        }
        finally { Done(dir); }
    }

    [Fact]
    public void Escape_with_no_radial_menu_up_shows_the_pause_menu()
    {
        var (ui, host, radial, _, dir) = Composed();
        try
        {
            host.OnGameUIActivated();
            Assert.Empty(radial.Done);
            Assert.True(ui.Menu.IsOpen("mainmenu"));
        }
        finally { Done(dir); }
    }

    [Fact]
    public void The_vote_panel_hides_while_the_pause_screen_is_up()
    {
        var (ui, host, _, _, dir) = Composed();
        try
        {
            Assert.False(ui.Menu.IsHeld("votepanel"));
            host.OnGameUIActivated();
            Assert.True(ui.Menu.IsHeld("votepanel"));
            host.OnGameUIHidden();
            Assert.False(ui.Menu.IsHeld("votepanel"));
            Assert.True(ui.Menu.IsOpen("votepanel"));
        }
        finally { Done(dir); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_window_shows_during_a_load_and_one_that_worked_shuts_them(bool dialog)
    {
        var (ui, host, _, _, dir) = Composed();
        try
        {
            ui.Open("options");
            Paint(ui);
            Assert.False(ui.Menu.IsHeld("options"));

            host.OnLevelLoadingStarted(dialog);
            Paint(ui);
            Assert.True(ui.Menu.IsHeld("options"));

            host.OnLevelLoadingFinished(false, null, null);
            Paint(ui);
            Assert.False(ui.Menu.IsOpen("options"));
            Assert.False(ui.Menu.IsHeld("options"));
        }
        finally { Done(dir); }
    }

    [Fact]
    public void A_window_opened_during_a_load_stays_unseen_too()
    {
        var (ui, host, _, _, dir) = Composed();
        try
        {
            host.OnLevelLoadingStarted(true);
            Paint(ui);
            ui.Open("options");
            Paint(ui);
            Assert.True(ui.Menu.IsHeld("options"));
        }
        finally { Done(dir); }
    }

    /// <summary>The server browser is there to try another server.</summary>
    [Fact]
    public void A_load_that_failed_gives_the_windows_back_when_its_message_is_closed()
    {
        var (ui, host, _, services, dir) = Composed();
        try
        {
            ui.Open("options");
            Paint(ui);
            host.OnLevelLoadingStarted(true);
            Paint(ui);
            host.OnLevelLoadingFinished(true, "no such server", null);
            Paint(ui);
            Assert.True(ui.Menu.IsHeld("options"));

            services.Require<GameUiLoading>().Close();
            Paint(ui);
            Assert.False(ui.Menu.IsHeld("options"));
            Assert.True(ui.Menu.IsOpen("options"));
        }
        finally { Done(dir); }
    }

    [Fact]
    public void The_console_keeps_its_own_rule_through_a_load_that_worked()
    {
        var (ui, host, _, _, dir) = Composed();
        try
        {
            host.OnGameUIActivated();
            host.ConsoleActivate();
            Paint(ui);
            host.OnLevelLoadingStarted(true);
            Paint(ui);
            host.OnLevelLoadingFinished(false, null, null);
            Paint(ui);
            Assert.True(ui.Menu.IsOpen("console"));
        }
        finally { Done(dir); }
    }

    /// <summary>Stock's loading dialog hides the GameUI's other dialogs: an open console is
    /// unseen and takes no keys from a load's start until the load ends, with or without the
    /// progress dialog, and is still open after; its failure's form covers it until closed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_load_covers_an_open_console_until_it_ends(bool dialog)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ss-console-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
                .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
                .Returns("Environment.Globals", default(SourceSharp.Abi.Globals));
            var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), dir);
            var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                              new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
            var ui = ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                                    null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
            var host = services.Require<GameUiHost>();
            var line = (ConsoleUi)services.Require<IConsoleLogSource>();
            host.OnGameUIActivated();
            host.ConsoleActivate();
            ui.Paint((int)UiLayerKind.Menu, 1920, 1080);
            Assert.False(ui.Menu.IsHeld("console"));

            host.OnLevelLoadingStarted(dialog);
            ui.Paint((int)UiLayerKind.Menu, 1920, 1080);
            Assert.True(ui.Menu.IsHeld("console"));
            Assert.True(ui.Menu.IsOpen("console"));
            ui.Input((int)UiInputKind.Char, 0, 0, 0, 'x', 0f);
            Assert.Equal("", line.Input);

            host.OnLevelLoadingFinished(true, "Lost", null);
            ui.Paint((int)UiLayerKind.Menu, 1920, 1080);
            Assert.True(ui.Menu.IsHeld("console"));

            services.Require<GameUiLoading>().Close();
            ui.Paint((int)UiLayerKind.Menu, 1920, 1080);
            Assert.False(ui.Menu.IsHeld("console"));
            Assert.True(ui.Menu.IsOpen("console"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A load's paint count (plan_gameui_managed.md §4): the dialog
    /// asked for, the updates, the ones that asked the engine to repaint, and
    /// the menu frames drawn between the start and the end.</summary>
    [Fact]
    public void A_load_s_updates_repaints_and_frames_are_metered()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var screens = new Screens { Frames = 100 };
        var quit = new ConfirmQuitUi(engine);
        var meter = new GameUiLoadMeter();
        var host = new GameUiHost(screens, new GameUiLoading(new Strings(), engine), quit, new GameMenuCommands(screens, quit, engine),
                                  new History(), meter);
        host.OnLevelLoadingStarted(true);
        Assert.True(host.UpdateProgressBar(0.5f, null));
        Assert.False(host.UpdateProgressBar(0.51f, null));
        Assert.True(host.UpdateProgressBar(0.6f, "#LoadingProgress_Connecting"));
        screens.Frames = 107;
        host.OnLevelLoadingFinished(false, null, null);
        Assert.Equal(1, meter.Loads);
        Assert.Equal(new GameUiLoad(true, 3, 2, 7), meter.Last);
    }
}
