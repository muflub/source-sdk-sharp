using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Tests.Ui;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The menu layer in game (plan_client_ui U3): panels the viewport hands over,
/// input from the menu root, the mouse and keyboard taken only while a screen
/// wants them, Esc, the console's line, and the window cursor.
/// </summary>
public class UiMenuHostTests
{
    private static (UiHost Host, UiFidelityTests.RecordingSurface Surface, UiMenus Menus) Host()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);                // for its menus' sources
        var m = host0.Menus;
        var windows = new UiWindows(new MemoryWindowStore());
        UiScreen[] menus =
        [
            new Client.Ui.Screens.Scoreboard(m.Scores, new TeamTable(m.Scores)),
            new TeamMenu(m.Teams, new TeamCard(), m.Teams),
            new Motd(m.Motd, m.Motd),
            new ConsoleScreen(windows, new ConsoleLogPanel(m.Console), new ConsoleInputPanel(m.Console, m.Console),
                              new ConsoleCompletionsPanel(m.Console)),
        ];
        var host = UiHostKit.Build(surface, [], menus, m);
        return (host, surface, m);
    }

    [Fact]
    public void The_viewport_s_scores_team_and_info_are_the_ui_s_and_nothing_else_is()
    {
        var (host, _, _) = Host();
        Assert.True(host.ViewportPanel("scores", true));
        Assert.True(host.Menu.IsOpen("scoreboard"));
        Assert.True(host.ViewportPanel("scores", false));
        Assert.False(host.Menu.IsOpen("scoreboard"));
        Assert.True(host.ViewportPanel("team", true));
        Assert.True(host.Menu.IsOpen("teammenu"));
        Assert.False(host.ViewportPanel("class", true));
    }

    [Fact]
    public void A_new_map_closes_the_viewport_screens_as_stock_hid_its_panels_and_leaves_the_console()
    {
        var (host, _, _) = Host();
        host.Open("scoreboard");
        host.Open("teammenu");
        host.Open("motd");
        host.Open("console");
        Assert.True(host.Menu.IsOpen("scoreboard") && host.Menu.IsOpen("console"));
        host.NewMap();
        Assert.False(host.Menu.IsOpen("scoreboard"));
        Assert.False(host.Menu.IsOpen("teammenu"));
        Assert.False(host.Menu.IsOpen("motd"));
        Assert.True(host.Menu.IsOpen("console"));            // GameUI's, not the viewport's
    }

    [Fact]
    public void The_spectator_overview_and_nav_panels_are_taken_and_open_no_window()
    {
        var (host, _, _) = Host();
        string[] before = [.. host.Menu.OpenScreens()];
        foreach (var name in new[] { "specgui", "specmenu", "overview", "nav_progress" })
        {
            Assert.True(host.ViewportPanel(name, true), name);
            Assert.True(host.TakePanel(name, true, []), name);
            Assert.True(host.Answers(name), name);
        }
        Assert.Equal(before, host.Menu.OpenScreens());
    }

    [Fact]
    public void The_menu_root_takes_the_mouse_only_while_a_screen_wants_it()
    {
        var (host, surface, _) = Host();
        host.ViewportPanel("scores", true);
        host.Paint(1, 1920, 1080);
        Assert.Empty(surface.MenuInputs);                         // the scoreboard does not take the mouse

        host.ViewportPanel("team", true);
        host.Paint(1, 1920, 1080);
        host.Paint(1, 1920, 1080);
        Assert.Equal([(true, true)], surface.MenuInputs);         // asked once, on the change

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("teammenu"));
        host.Paint(1, 1920, 1080);
        Assert.Equal([(true, true), (false, false)], surface.MenuInputs);
    }

    [Fact]
    public void The_server_s_info_panel_brings_its_title_and_text_to_the_motd()
    {
        var (host, _, menus) = Host();
        Assert.True(host.TakePanel("info", true, [new("title", "Welcome"), new("type", "0"), new("msg", "Be nice.")]));
        Assert.True(host.Menu.IsOpen("motd"));
        Assert.Equal(("Welcome", "Be nice."), (menus.Motd.Title, menus.Motd.Text));
        Assert.False(host.TakePanel("class", true, []));
    }

    [Fact]
    public void Showinfo_opens_the_motd_with_its_title_and_stock_s_empty_body()
    {
        var (host, _, menus) = Host();
        var log = new RecordingLog();
        var info = new Client.Vgui.ShowInfo(() => host, log);
        info.Run(0, "Server rules", "Be nice.");
        Assert.True(host.Menu.IsOpen("motd"));
        // Stock's showinfo keyed the text "message" and the panel reads "msg".
        Assert.Equal(("Server rules", ""), (menus.Motd.Title, menus.Motd.Text));
    }

    [Fact]
    public void The_console_completes_what_the_host_has_and_what_it_answers()
    {
        var (host, _, _) = Host();

        Assert.Contains("motd", host.ScreenNames);
        Assert.Contains("scores", host.PanelNames);
    }

    [Fact]
    public void Showinfo_with_fewer_than_three_arguments_opens_nothing()
    {
        var (host, _, _) = Host();
        new Client.Vgui.ShowInfoCommand(new Client.Vgui.ShowInfo(() => host, new RecordingLog())).RunLine("0 title", _ => { });
        Assert.False(host.Menu.IsOpen("motd"));
    }

    [Fact]
    public void Showinfo_with_no_ui_says_it_found_no_info_panel()
    {
        var log = new RecordingLog();
        new Client.Vgui.ShowInfo(() => null, log).Run(0, "a", "b");
        Assert.Equal(["Couldn't find info panel.\n"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Typing_into_the_console_and_pressing_enter_runs_the_line()
    {
        var (host, _, menus) = Host();
        host.Open("console");
        host.Paint(1, 1920, 1080);
        foreach (char c in "status")
            host.Input((int)UiInputKind.Char, 0, 0, 0, c, 0f);
        Assert.Equal("status", menus.Console.Input);

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Enter, 0, 0f);
        Assert.Equal(string.Empty, menus.Console.Input);
        Assert.Equal("] status", menus.Console.Line(menus.Console.LineCount - 1).Text);

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Up, 0, 0f);
        Assert.Equal("status", menus.Console.Input);
    }

    [Fact]
    public void A_window_s_corner_under_the_mouse_sets_the_resize_cursor()
    {
        var (host, surface, _) = Host();
        host.Menu.Context.RecordNames = true;
        host.Open("console");
        host.Paint(1, 1920, 1080);
        var list = host.Menu.Context.List.AsDrawList();
        var elements = list.Elements.ToArray();
        string text = new(list.Text);
        var corner = elements.First(e => text.Substring(e.NameStart, e.NameLength).Contains("wnd-br"));

        host.Input((int)UiInputKind.MouseMove, (int)(corner.Rect.X + corner.Rect.W / 2), (int)(corner.Rect.Y + corner.Rect.H / 2), 0, 0, 0f);
        host.Paint(1, 1920, 1080);

        Assert.Contains((int)UiCursor.ResizeNWSE, surface.Cursors);
    }

    /// <summary>
    /// The server's VGUIMenu "team" opens the UI's team menu even though the
    /// stock viewport made no "team" panel: the live run's first VGUIMenu was
    /// dropped at the handler's "does the panel exist" question.
    /// </summary>
    [Fact]
    public void The_server_s_team_vguimenu_opens_the_team_menu_with_no_stock_panel()
    {
        var (host, _, _) = Host();
        var engine = TestServices.Fake().Returns("Hud.ViewportQuery", (ViewportFacts)0);
        var effects = new Client.ClientMessageEffects(engine, () => host);
        var message = new Shared.VguiMenuMessage(effects, null, null);

        message.OnReceived(new Shared.VguiMenu { Name = "team", Show = true, Keys = [], Values = [] });

        Assert.True(host.Menu.IsOpen("teammenu"));
        Assert.Equal(0, engine.CountOf("Hud.ViewportShow"));       // the stock viewport never saw it
    }

    /// <summary>A mod screen claiming a stock panel (TF2 stage 3.2's team menu
    /// claiming "team"): the host opens it wherever the map named the core's.</summary>
    private sealed class ClaimingTeamMenu : UiScreen
    {
        public override string Name => "tf2teammenu";

        public override string? Panel => "team";

        public override void Describe(ref UiFrame ui) { }
    }

    private static (UiHost Host, UiFidelityTests.RecordingSurface Surface) ClaimedHost()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        var m = host0.Menus;
        UiScreen[] menus = [new TeamMenu(m.Teams, new TeamCard(), m.Teams), new ClaimingTeamMenu()];
        return (UiHostKit.Build(surface, [], menus, m), surface);
    }

    /// <summary>
    /// A screen claiming the "team" panel answers it: the viewport's and the
    /// server's show opens the claimer and never the core's teammenu, which
    /// the same call opened in the fact above (before ≠ after).
    /// </summary>
    [Fact]
    public void A_claiming_screen_is_the_team_panel_s()
    {
        var (host, _) = ClaimedHost();
        Assert.True(host.ViewportPanel("team", true));
        Assert.True(host.Menu.IsOpen("tf2teammenu"));
        Assert.False(host.Menu.IsOpen("teammenu"));         // the core's stayed shut
        Assert.True(host.TakePanel("team", true, []));
        Assert.True(host.Menu.IsOpen("tf2teammenu"));
    }

    [Fact]
    public void A_claiming_screen_answers_its_panel_s_close_and_its_new_map()
    {
        var (host, _) = ClaimedHost();
        host.ViewportPanel("team", true);
        host.NewMap();                                      // before: the claimer is open
        Assert.False(host.Menu.IsOpen("tf2teammenu"));
        host.ViewportPanel("team", true);
        Assert.True(host.Menu.IsOpen("tf2teammenu"));       // the stimulus reaches it again
        host.ViewportPanel("team", false);
        Assert.False(host.Menu.IsOpen("tf2teammenu"));
    }

    [Fact]
    public void Escape_closes_the_claiming_team_screen_as_it_closes_the_core_s()
    {
        var (host, _) = ClaimedHost();
        host.ViewportPanel("team", true);
        Assert.True(host.Menu.IsOpen("tf2teammenu"));      // before: the claim is the open one
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("tf2teammenu"));
    }

    /// <summary>A screen claiming panel names (TF2 stage 3.2's class menu, which
    /// speaks for "class", "class_red" and "class_blue" at once), recording the
    /// name each show came in under.</summary>
    private sealed class ClaimingPanelMenu : UiScreen
    {
        public string[] ClaimNames { get; init; } = [];

        public readonly List<string> OpenedUnder = [];

        public override string Name => "panel-claimer";

        public override IReadOnlyList<string> Panels => ClaimNames;

        public override void PanelOpened(string panel) => OpenedUnder.Add(panel);

        public int Described { get; private set; }

        public override void Describe(ref UiFrame ui) => Described++;
    }

    private static (UiHost Host, ClaimingPanelMenu Screen, UiMenus Menus) ClaimingNamesHost(string[] names)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        var m = host0.Menus;
        var screen = new ClaimingPanelMenu { ClaimNames = names };
        UiScreen[] menus =
        [
            new TeamMenu(m.Teams, new TeamCard(), m.Teams),
            new Motd(m.Motd, m.Motd),
            screen,
        ];
        return (UiHostKit.Build(surface, [], menus, m), screen, m);
    }

    /// <summary>
    /// A claim can carry more than one name (TF2's class menu): the viewport's
    /// show of any of them opens the claimer, and a name nobody claimed answers
    /// no one.
    /// </summary>
    [Fact]
    public void A_screen_claiming_several_names_answers_every_one_of_them()
    {
        var (host, _, _) = ClaimingNamesHost(["class", "class_red", "class_blue"]);
        Assert.True(host.ViewportPanel("class_red", true));
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.True(host.Answers("class_blue"));
        Assert.False(host.Answers("mapinfo"));             // nobody claimed or maps it
    }

    /// <summary>The show tells the claimer which of its names opened it — how
    /// TF2's one class menu knows a class_red show from a class_blue one; the
    /// plain open path (a chain, a command) says nothing.</summary>
    [Fact]
    public void A_show_of_a_claimed_panel_names_the_screen_the_name_it_came_under()
    {
        var (host, screen, _) = ClaimingNamesHost(["class", "class_red"]);
        host.ViewportPanel("class_red", true);
        Assert.Equal(["class_red"], screen.OpenedUnder);   // before: the show named it
        host.Open("panel-claimer");                        // the direct path: no name
        Assert.Equal(["class_red"], screen.OpenedUnder);
    }

    /// <summary>An info claim (TF2's own text window): Esc closes the claimer
    /// as it closes the core's MOTD, and the claimed show's title and text
    /// still come through the MOTD's source — the data arm of the panel does
    /// not follow the claim.</summary>
    [Fact]
    public void Escape_closes_the_claiming_info_screen_and_its_data_still_reaches_the_motd()
    {
        var (host, _, menus) = ClaimingNamesHost(["info"]);
        Assert.True(host.TakePanel("info", true, [new("title", "Welcome"), new("type", "0"), new("msg", "Be nice.")]));
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.False(host.Menu.IsOpen("motd"));            // the core's stayed shut
        Assert.Equal(("Welcome", "Be nice."), (menus.Motd.Title, menus.Motd.Text));

        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("panel-claimer"));
    }

    /// <summary>A host whose menu layer carries the pause menu (plus any screen the
    /// fact adds) and whose clock moves a frame between reads (so two presses are
    /// two decisions).</summary>
    private static (UiHost Host, UiFidelityTests.RecordingSurface Surface) EscMenuHost(params UiScreen[] extra)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        var m = host0.Menus;
        var fake = new FakeMainMenu();
        UiScreen[] menus = [new MainMenu(fake, fake, new BuildStampPanel(new FakeBuildStamp())), .. extra];
        float seconds = 3;   // IGameClock.Now is a float: a boxed double answers default.
        var engine = TestServices.Fake().ReturnsEachCall("Clock.Now", () => seconds += 1);
        return (UiHostKit.Build(surface, [], menus, m, engine), surface);
    }

    private sealed class FakeMainMenu : IMainMenuSource, IMainMenuCommands
    {
        public void Run(string command) { }

        public bool InLevel => true;

        public bool CanPlay => false;

        public void Play() { }

        public void Resume() { }

        public void OpenOptions() { }

        public void Quit() { }
    }

    /// <summary>A screen with a name and the other names it opens under.</summary>
    private sealed class Named(string name, params string[] aliases) : UiScreen
    {
        public override string Name => name;

        public override IReadOnlyList<string> Aliases => aliases;

        public override void Describe(ref UiFrame ui) { }
    }

    /// <summary>
    /// A mod's create-server screen takes the core's place (a mod that
    /// replaces the whole window; one that only adds bots adds a page,
    /// plan_hl2mp_ai §3.5): one that answers the name <c>createserver</c>
    /// (<see cref="UiScreen.Aliases"/>) is what the main menu's Create server,
    /// stock's OpenCreateMultiplayerGameDialog, opens, and the core's own stays
    /// shut; a mod without one gets the core's.
    /// </summary>
    [Fact]
    public void A_mods_screen_answering_createserver_opens_in_place_of_the_cores()
    {
        string name = SourceSharp.Client.Vgui.GameMenuCommands.ScreenFor("OpenCreateMultiplayerGameDialog")!;
        var (core, _) = EscMenuHost(new Named("createserver"));
        core.Open(name);
        Assert.True(core.Menu.IsOpen("createserver"));

        var (modded, _) = EscMenuHost(new Named("createserver"), new Named("modcreateserver", "createserver"));
        modded.Open(name);
        Assert.Equal((true, false), (modded.Menu.IsOpen("modcreateserver"), modded.Menu.IsOpen("createserver")));
    }

    /// <summary>
    /// The Escape toggle (stock ClientModeSingle::OnKeyDown): idle, the press
    /// opens the pause menu; the next press hides it. Before the fix the idle
    /// press closed nothing and opened nothing, so the first assert is the red one.
    /// </summary>
    [Fact]
    public void Escape_opens_the_pause_menu_and_the_next_press_returns_to_the_game()
    {
        var (host, _) = EscMenuHost();
        Assert.False(host.Menu.IsOpen("mainmenu"));             // before: nothing is up
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.True(host.Menu.IsOpen("mainmenu"));              // the press opened it
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("mainmenu"));             // and the next hid it
    }

    /// <summary>With a screen up, Escape closes that first; the pause menu opens
    /// only once nothing else wants the keyboard (two presses, one behaviour each).</summary>
    [Fact]
    public void Escape_closes_the_open_screen_before_it_opens_the_pause_menu()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["info"] };
        var (host, _) = EscMenuHost(claimer);                    // the pause menu and the claimer together
        Assert.True(host.ViewportPanel("info", true));
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("panel-claimer"));         // the press closed the screen
        Assert.False(host.Menu.IsOpen("mainmenu"));              // and opened nothing yet
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.True(host.Menu.IsOpen("mainmenu"));               // the next press opened the menu
    }

    /// <summary>One press can reach the host twice (the menu root's panel arm and
    /// the client mode's decision): two calls in one frame are one toggle.</summary>
    [Fact]
    public void Two_calls_in_the_same_frame_toggle_the_pause_menu_once()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        var m = host0.Menus;
        var fake = new FakeMainMenu();
        UiScreen[] menus = [new MainMenu(fake, fake, new BuildStampPanel(new FakeBuildStamp()))];
        var engine = TestServices.Fake().Returns("Clock.Now", 5f);   // one frozen frame
        var host = UiHostKit.Build(surface, [], menus, m, engine);
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.True(host.Menu.IsOpen("mainmenu"));
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.True(host.Menu.IsOpen("mainmenu"));              // the second call counts once
    }

    /// <summary>
    /// The engine-side GameUI arm drives the pause menu through the client-side
    /// events: gameui_activated shows it, gameui_hidden hides it, so what the engine
    /// considers visible and what the menu draws agree without a managed key handler
    /// deciding first.
    /// </summary>
    [Fact]
    public void The_gameui_events_show_and_hide_the_pause_menu()
    {
        var (host, _) = EscMenuHost();
        Assert.False(host.Menu.IsOpen("mainmenu"));             // before: nothing is up
        host.ShowPauseMenu();
        Assert.True(host.Menu.IsOpen("mainmenu"));              // gameui_activated showed it
        host.HidePauseMenu();
        Assert.False(host.Menu.IsOpen("mainmenu"));             // gameui_hidden hid it
    }

    /// <summary>
    /// A mod's main menu claims stock TF2's viewport name for it
    /// (PANEL_MAINMENUOVERRIDE, "MainMenuOverride"): the GameUI's activation
    /// and hide open and close the claimer, and the core's menu, which the same
    /// calls drive in the fact above, stays shut.
    /// </summary>
    [Fact]
    public void A_screen_claiming_the_main_menu_override_is_what_the_gameui_shows()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["MainMenuOverride"] };
        var (host, _) = EscMenuHost(claimer);
        host.ShowPauseMenu();
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.False(host.Menu.IsOpen("mainmenu"));            // the core's stayed shut
        Assert.Equal("panel-claimer", host.Menu.ChromeWith);   // and the taskbar comes up with the claimer
        host.HidePauseMenu();
        Assert.False(host.Menu.IsOpen("panel-claimer"));
    }

    /// <summary>
    /// While the GameUI is up the engine hides the client viewport and every
    /// panel on it without closing one (CEngineVGui::ActivateGameUI): a screen
    /// answering a viewport panel stays open but is held, so the menu layer,
    /// which the GameUI's override paints then, draws it not; the hide lets it
    /// go. Live, TF2's text window drew over the in-level main menu and its
    /// modal took the menu's clicks.
    /// </summary>
    [Fact]
    public void A_viewport_screen_is_held_hidden_while_the_gameui_is_up()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["info"] };
        var (host, _) = EscMenuHost(claimer);
        host.Open("panel-claimer");
        host.Paint((int)UiLayerKind.Menu, 1920, 1080);
        int before = claimer.Described;
        Assert.True(before > 0);                                  // drawn while the GameUI is down
        host.ShowPauseMenu();
        host.Paint((int)UiLayerKind.Menu, 1920, 1080);
        Assert.Equal(before, claimer.Described);                  // held: open, not drawn
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.False(host.Menu.IsHeld("mainmenu"));               // the GameUI's own menu draws
        host.HidePauseMenu();
        host.Paint((int)UiLayerKind.Menu, 1920, 1080);
        Assert.True(claimer.Described > before);                  // let go: drawn again
    }

    /// <summary>A panel the server shows while the GameUI is up is shown under
    /// the hidden viewport: open and held until the GameUI hides.</summary>
    [Fact]
    public void A_viewport_panel_shown_while_the_gameui_is_up_waits_for_its_hide()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["info"] };
        var (host, _) = EscMenuHost(claimer);
        host.ShowPauseMenu();
        Assert.True(host.ViewportPanel("info", true));
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.True(host.Menu.IsHeld("panel-claimer"));
        host.HidePauseMenu();
        Assert.False(host.Menu.IsHeld("panel-claimer"));
    }

    [Fact]
    public void Escape_toggles_the_claimed_main_menu()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["MainMenuOverride"] };
        var (host, _) = EscMenuHost(claimer);
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        Assert.False(host.Menu.IsOpen("mainmenu"));
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        Assert.False(host.Menu.IsOpen("panel-claimer"));
    }

    /// <summary>The pause menu draws above the screens it covers (stock's GameUI main
    /// panel above every viewport panel): the claimed info screen (TF2's text window)
    /// stays below it in draw order, so its dialog never covers the menu.</summary>
    [Fact]
    public void The_pause_menu_draws_above_the_claimed_info_screen()
    {
        var claimer = new ClaimingPanelMenu { ClaimNames = ["info"] };
        var (host, _) = EscMenuHost(claimer);
        host.Open("panel-claimer");
        Assert.True(host.Menu.IsOpen("panel-claimer"));
        host.ShowPauseMenu();
        Assert.Equal(["panel-claimer", "mainmenu"], host.Menu.OpenScreens());   // the menu last drawn: its dialog on top
    }

    /// <summary>Resume runs the engine's own hide command (stock's GameUI "ResumeGame"),
    /// so engine and menu agree: the engine's OnGameUIHidden fires gameui_hidden and the
    /// screen closes with it.</summary>
    [Fact]
    public void Resume_runs_the_engine_s_own_hide_command()
    {
        var sent = new List<string>();
        new MainMenuUi(new ChatFeedTests.RecordingEngineClient(sent), new ConfirmQuitUi(new ChatFeedTests.RecordingEngineClient(sent))).Resume();
        Assert.Equal(["gameui_hide"], sent);
    }

    /// <summary>
    /// A viewport rebuild (video-mode change) hides "all": the UI takes the
    /// sweep and its screens stay. Live, three mat_setvideomodes shut the
    /// held scoreboard before this.
    /// </summary>
    [Fact]
    public void A_viewport_rebuild_s_hide_all_leaves_the_ui_s_screens_open()
    {
        var (host, _, _) = Host();
        host.ViewportPanel("scores", true);
        Assert.True(host.ViewportPanel("all", false));
        Assert.True(host.Menu.IsOpen("scoreboard"));
        Assert.False(host.ViewportPanel("all", true));
    }

    [Fact]
    public void A_hud_layer_screen_opens_and_closes_through_the_host()
    {
        // The TF2 scoreboard is a hud screen (Layer Hud): the host opens and
        // closes it on the hud layer — visible over the hud while the game
        // renders, not held under the menu root (TFScoreboardTests holds the
        // layering; this holds the routing).
        var scoreboard = Assert.Single(PreviewRun.AllSamples(),
                                       s => !s.Gallery && s.Group == "scoreboard" && s.Slug == "two-teams-and-the-clock").Create();
        var host = UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [scoreboard]);

        Assert.True(host.ViewportPanel("scores", true));
        Assert.True(host.IsOpen("scoreboard"));
        Assert.True(host.Layer.IsOpen("scoreboard"));
        Assert.False(host.Menu.IsOpen("scoreboard"));

        Assert.True(host.ViewportPanel("scores", false));
        Assert.False(host.IsOpen("scoreboard"));
    }

    [Fact]
    public void A_menu_layer_scoreboards_source_refreshes_again_after_the_cadence()
    {
        // When no mod screen takes the core scoreboard's slot it stays on the
        // menu layer, and the host re-reads its source on the menu cadence
        // while it shows — the hud layer's arm answers only the mod's screen.
        float now = 5f;
        var engine = new RecordingEngine()
            .Returns("Hud.LocalPlayerHealth", 80).Returns("Hud.LocalPlayerIndex", 1)
            .Returns("Console.FindConVarValue", "host a").Returns("Environment.Globals", default(Globals))
            .ReturnsEachCall("Clock.Now", () => now);
        var surface = new UiFidelityTests.RecordingSurface();
        var seed = UiHostKit.Build(surface, [], null, null, engine);
        var scores = seed.Menus.Scores;
        var host = UiHostKit.Build(surface, [],
            new UiScreen[] { new Client.Ui.Screens.Scoreboard(scores, new TeamTable(scores)) }, seed.Menus, engine);

        host.Open("scoreboard");
        host.Paint((int)UiLayerKind.Menu, 1280, 720);
        Assert.Equal("host a", host.Menus.Scores.ServerName);

        engine.Returns("Console.FindConVarValue", "host b");
        now += 0.3f;                                    // past MenuRefreshSeconds
        host.Paint((int)UiLayerKind.Menu, 1280, 720);
        Assert.Equal("host b", host.Menus.Scores.ServerName);
    }
}
