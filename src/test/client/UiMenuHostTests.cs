using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Tests.Ui;

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
        info.Run("0 \"Server rules\" \"Be nice.\"");
        Assert.True(host.Menu.IsOpen("motd"));
        // Stock's showinfo keyed the text "message" and the panel reads "msg".
        Assert.Equal(("Server rules", ""), (menus.Motd.Title, menus.Motd.Text));
    }

    [Fact]
    public void Showinfo_with_fewer_than_three_arguments_opens_nothing()
    {
        var (host, _, _) = Host();
        new Client.Vgui.ShowInfo(() => host, new RecordingLog()).Run("0 title");
        Assert.False(host.Menu.IsOpen("motd"));
    }

    [Fact]
    public void Showinfo_with_no_ui_says_it_found_no_info_panel()
    {
        var log = new RecordingLog();
        new Client.Vgui.ShowInfo(() => null, log).Run("0 a b");
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
}
