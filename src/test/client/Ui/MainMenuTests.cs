using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;           // the host's source: MainMenuUi, MenuMapList

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The main menu's buttons: the launch menu plays the maplist's map, quits,
/// and resumes only in game — the launch arm of the pause menu (the client's
/// GameUI watch fires the activation the menu opens on).
/// </summary>
public sealed class MainMenuTests
{
    private sealed class RecordingSource : IMainMenuSource, IMainMenuCommands
    {
        public void Run(string command) => Clicked.Add("cmd " + command);

        public List<string> Clicked { get; } = [];

        public bool InLevel { get; set; } = true;

        public bool CanPlay { get; set; } = true;

        public void Play() => Clicked.Add("play");

        public void Resume() => Clicked.Add("resume");

        public void OpenOptions() => Clicked.Add("options");

        public void Quit() => Clicked.Add("quit");
    }

    private static UiContext Drawn(RecordingSource source)
    {
        var screen = new MainMenu(source, source);
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static bool Draws(UiContext ctx, string token)
    {
        var els = Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                return true;
        return false;
    }

    /// <summary>Every text the frame drew, for the missing-button messages.</summary>
    private static string DrawnText(UiContext ctx)
    {
        var els = Elements(ctx);
        var names = new string[els.Length];
        for (int i = 0; i < els.Length; i++)
            names[i] = RunText(ctx, i);
        return string.Join("|", names);
    }

    private static void Click(UiContext ctx, UiScreen screen, string token)
    {
        var els = Elements(ctx);
        int index = -1;
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                index = i;
        Assert.True(index >= 0, $"no element drew {token}");
        var r = els[index].Rect;
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    [Fact]
    public void Play_loads_the_map_the_source_was_given()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        new MainMenuUi(engine, new ConfirmQuitUi(engine), "ctf_2fort").Play();
        Assert.Equal(["map ctf_2fort"], sent);          // the map command, the listen server's start
    }

    [Fact]
    public void Play_without_a_map_loads_nothing()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        new MainMenuUi(engine, new ConfirmQuitUi(engine)).Play();
        Assert.Empty(sent);                             // no maplist, no command
    }

    /// <summary>Stock's Quit asked first (its quit confirmation): the question, no quit yet.</summary>
    [Fact]
    public void Quit_asks_before_it_ends_the_game()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var quit = new ConfirmQuitUi(engine);
        new MainMenuUi(engine, quit, "ctf_2fort").Quit();
        Assert.True(quit.Asking);
        Assert.Empty(sent);
    }

    [Fact]
    public void The_menus_commands_go_to_the_game_menus_table()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var asked = new List<string>();
        var menu = new MainMenuUi(engine, new ConfirmQuitUi(engine)) { MenuCommand = c => { asked.Add(c); return true; } };
        menu.Run("OpenServerBrowser");
        Assert.Equal(["OpenServerBrowser"], asked);
    }

    /// <summary>Stock HL2MP's game menu: Disconnect and the player list in game;
    /// Find servers and Create server always.</summary>
    [Fact]
    public void The_menu_draws_stocks_multiplayer_items()
    {
        var menu = new RecordingSource { InLevel = false };
        var ctx = Drawn(menu);
        Assert.True(Draws(ctx, "#SS_UI_Main_FindServers"), DrawnText(ctx));
        Assert.True(Draws(ctx, "#SS_UI_Main_CreateServer"), DrawnText(ctx));
        Assert.False(Draws(ctx, "#SS_UI_Main_Disconnect"), DrawnText(ctx));
        Assert.False(Draws(ctx, "#SS_UI_Main_Players"), DrawnText(ctx));
        menu.InLevel = true;
        ctx = Drawn(menu);
        Assert.True(Draws(ctx, "#SS_UI_Main_Disconnect"), DrawnText(ctx));
        Assert.True(Draws(ctx, "#SS_UI_Main_Players"), DrawnText(ctx));
    }

    [Fact]
    public void Clicking_find_servers_sends_stocks_command()
    {
        var menu = new RecordingSource { InLevel = false };
        var screen = new MainMenu(menu, menu);
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Click(ctx, screen, "#SS_UI_Main_FindServers");
        Assert.Equal(["cmd OpenServerBrowser"], menu.Clicked);
    }

    [Fact]
    public void InLevel_is_the_engine_s_level_answer()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent) { Level = "ctf_2fort" };
        Assert.True(new MainMenuUi(engine, new ConfirmQuitUi(engine)).InLevel);    // a map is up
        engine.Level = string.Empty;
        Assert.False(new MainMenuUi(engine, new ConfirmQuitUi(engine)).InLevel);   // the launch menu: none is
    }

    [Fact]
    public void The_maplist_s_first_entry_is_the_start_map()
    {
        const string list = "// the start\r\n# a note\r\n\r\nctf_2fort\r\nkoth_badlands\n";
        Assert.Equal("ctf_2fort", MenuMapList.First(list));
        Assert.Null(MenuMapList.First("  \n// only comments\n"));
    }

    [Fact]
    public void The_menu_draws_play_and_quit_and_reserves_resume_for_the_game()
    {
        var menu = new RecordingSource { InLevel = false };
        var ctx = Drawn(menu);
        Assert.True(Draws(ctx, "#SS_UI_Main_Play"), DrawnText(ctx));
        Assert.True(Draws(ctx, "#SS_UI_Main_Options"), DrawnText(ctx));
        Assert.True(Draws(ctx, "#SS_UI_Main_Quit"), DrawnText(ctx));
        Assert.False(Draws(ctx, "#SS_UI_Main_Resume"), DrawnText(ctx));  // no level, no resume

        menu.InLevel = true;
        Assert.True(Draws(Drawn(menu), "#SS_UI_Main_Resume"));
    }

    [Fact]
    public void A_menu_without_a_maplist_draws_no_play()
    {
        var ctx = Drawn(new RecordingSource { CanPlay = false, InLevel = true });
        Assert.False(Draws(ctx, "#SS_UI_Main_Play"));
        Assert.True(Draws(ctx, "#SS_UI_Main_Resume"));
    }

    [Fact]
    public void Clicking_play_loads_the_map()
    {
        var menu = new RecordingSource { InLevel = false };
        var screen = new MainMenu(menu, menu);
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Assert.Empty(menu.Clicked);

        Click(ctx, screen, "#SS_UI_Main_Play");
        Assert.Equal(["play"], menu.Clicked);
    }
}
