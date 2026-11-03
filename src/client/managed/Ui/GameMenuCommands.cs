using SourceSharp.Abi;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Stock's game menu commands (the GameUI's RunMenuCommand, reached by the
/// <c>gamemenucommand</c> console command and IGameUI::SendMainMenuCommand):
/// each name a mod's menu sends, routed to a managed screen or the engine
/// (plan_gameui_managed.md G3, docs/gameui-census.md §4). The single-player,
/// Xbox, matchmaking and achievement names answer inertly
/// (docs/absence-client.md's "GameUI single-player surface"); an unknown
/// name answers false.
/// </summary>
public sealed class GameMenuCommands(IGameUiNavigation screens, ConfirmQuitUi quit, IFwdEngineClient engine)
{
    /// <summary>The screen a name opens, or null.</summary>
    public static string? ScreenFor(string name) => name.ToLowerInvariant() switch
    {
        "openoptionsdialog" => "options",
        "openserverbrowser" => "serverbrowser",
        "opencreatemultiplayergamedialog" => "createserver",
        "openplayerlistdialog" or "openmuteplayerdialog" => "playerlist",
        _ => null,
    };

    /// <summary>Whether a name is one of stock's with nothing to do in a multiplayer-only game.</summary>
    public static bool IsInert(string name) => name.ToLowerInvariant() switch
    {
        "opennewgamedialog" or "openloadgamedialog" or "opensavegamedialog" or "openbonusmapsdialog"
            or "opencontrollerdialog" or "openbenchmarkdialog" or "openfriendsdialog" or "openloaddemodialog"
            or "openchangegamedialog" or "openloadcommentarydialog" or "openloadsingleplayercommentarydialog"
            or "openmatchmakingbasepanel" or "openachievementsdialog" or "opencsachievementsdialog"
            or "achievementsdialogclosing" or "quitrestartnoconfirm" => true,
        _ => false,
    };

    /// <summary>Runs a name; false for one stock never had.</summary>
    public bool Run(string? command)
    {
        string name = command?.Trim() ?? "";
        if (ScreenFor(name) is { } screen)
        {
            screens.Open(screen);
            return true;
        }
        switch (name.ToLowerInvariant())
        {
            case "opengamemenu":
                screens.ShowPauseMenu();
                return true;
            case "quit":
                quit.Ask();
                return true;
            case "quitnoconfirm":
                engine.SubmitConsoleLine("quit");
                return true;
            case "resumegame":
                engine.SubmitConsoleLine("gameui_hide");
                return true;
            case "disconnect":
            case "disconnectnoconfirm":
                engine.SubmitConsoleLine("disconnect");
                return true;
        }
        return IsInert(name);
    }
}
