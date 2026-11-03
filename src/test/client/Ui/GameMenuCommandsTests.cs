using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The game menu's commands (plan_gameui_managed.md G3; stock's
/// RunMenuCommand): every name stock answered is routed to a screen or the
/// engine, or ruled inert; an unknown name answers false.
/// </summary>
public class GameMenuCommandsTests
{
    private sealed class Screens : IGameUiNavigation
    {
        public List<string> Calls { get; } = [];
        public void ShowPauseMenu() => Calls.Add("show-pause");
        public void HidePauseMenu() => Calls.Add("hide-pause");
        public bool MainMenuOpen => false;
        public long MenuFrames => 0;
        public void Open(string screen) => Calls.Add("open " + screen);
        public void Close(string screen) => Calls.Add("close " + screen);
        public bool IsOpen(string screen) => false;
    }

    private static (GameMenuCommands Menu, Screens Screens, ConfirmQuitUi Quit, List<string> Sent) Rig()
    {
        var sent = new List<string>();
        var engine = new ChatFeedTests.RecordingEngineClient(sent);
        var screens = new Screens();
        var quit = new ConfirmQuitUi(engine);
        return (new GameMenuCommands(screens, quit, engine), screens, quit, sent);
    }

    /// <summary>Every name stock's RunMenuCommand compared, and the mute dialog's.</summary>
    public static TheoryData<string> StockNames() => new()
    {
        "OpenGameMenu", "OpenPlayerListDialog", "OpenNewGameDialog", "OpenLoadGameDialog", "OpenSaveGameDialog",
        "OpenBonusMapsDialog", "OpenOptionsDialog", "OpenControllerDialog", "OpenBenchmarkDialog", "OpenServerBrowser",
        "OpenFriendsDialog", "OpenLoadDemoDialog", "OpenCreateMultiplayerGameDialog", "OpenChangeGameDialog",
        "OpenLoadCommentaryDialog", "OpenLoadSingleplayerCommentaryDialog", "OpenMatchmakingBasePanel",
        "OpenAchievementsDialog", "OpenCSAchievementsDialog", "AchievementsDialogClosing", "Quit", "QuitNoConfirm",
        "QuitRestartNoConfirm", "ResumeGame", "Disconnect", "DisconnectNoConfirm", "OpenMutePlayerDialog",
    };

    [Theory]
    [MemberData(nameof(StockNames))]
    public void Every_stock_name_is_answered(string name)
    {
        var (menu, _, _, _) = Rig();
        Assert.True(menu.Run(name));
    }

    [Theory]
    [InlineData("OpenOptionsDialog", "options")]
    [InlineData("OpenServerBrowser", "serverbrowser")]
    [InlineData("OpenCreateMultiplayerGameDialog", "createserver")]
    [InlineData("OpenPlayerListDialog", "playerlist")]
    [InlineData("OpenMutePlayerDialog", "playerlist")]
    [InlineData("openoptionsdialog", "options")]
    public void The_dialog_names_open_their_screens(string name, string screen)
    {
        var (menu, screens, _, sent) = Rig();
        menu.Run(name);
        Assert.Equal(["open " + screen], screens.Calls);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("QuitNoConfirm", "quit")]
    [InlineData("ResumeGame", "gameui_hide")]
    [InlineData("Disconnect", "disconnect")]
    [InlineData("DisconnectNoConfirm", "disconnect")]
    public void The_engine_names_run_their_console_lines(string name, string line)
    {
        var (menu, _, _, sent) = Rig();
        menu.Run(name);
        Assert.Equal([line], sent);
    }

    [Fact]
    public void Quit_asks_and_OpenGameMenu_shows_the_menu()
    {
        var (menu, screens, quit, sent) = Rig();
        menu.Run("Quit");
        Assert.True(quit.Asking);
        Assert.Empty(sent);
        menu.Run("OpenGameMenu");
        Assert.Equal(["show-pause"], screens.Calls);
    }

    [Fact]
    public void The_inert_names_touch_nothing_and_an_unknown_name_answers_false()
    {
        var (menu, screens, quit, sent) = Rig();
        Assert.True(menu.Run("OpenNewGameDialog"));
        Assert.False(menu.Run("OpenNoSuchDialog"));
        Assert.False(menu.Run(null));
        Assert.Empty(screens.Calls);
        Assert.Empty(sent);
        Assert.False(quit.Asking);
    }
}
