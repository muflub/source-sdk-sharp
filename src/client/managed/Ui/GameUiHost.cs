
namespace SourceSharp.Client.Vgui;

/// <summary>What the GameUI host asks of the UI: the pause menu's two edges and the screens by name.</summary>
public interface IGameUiNavigation
{
    /// <summary>The engine raised its GameUI: the main menu shows (the claimed one, else the core's).</summary>
    void ShowPauseMenu();

    /// <summary>The engine hid its GameUI: the main menu goes.</summary>
    void HidePauseMenu();

    /// <summary>Whether the main menu is open.</summary>
    bool MainMenuOpen { get; }

    void Open(string screen);

    void Close(string screen);

    bool IsOpen(string screen);
}

/// <summary>
/// Where every call the engine makes of its GameUI lands (plan_gameui_managed.md;
/// bound to GameUI011 and GameConsole004 from G4): each method of the two
/// interfaces, in their order, routed to the UI. The pause menu follows the
/// GameUI's activation and hide; the load dialog, the disconnect dialog and
/// Quit's question are the sources' (<see cref="GameUiLoading"/>,
/// <see cref="ConfirmQuitUi"/>); the console methods open, close and ask the
/// console window. The single-player, bonus-map, Xbox and session methods
/// answer inertly (docs/absence-client.md's "GameUI single-player surface"),
/// as do the calls the stock module made to itself through the engine
/// (<see cref="SetMainMenuOverride"/>, <see cref="SendMainMenuCommand"/>).
/// </summary>
public sealed class GameUiHost(IGameUiNavigation screens, GameUiLoading loading, ConfirmQuitUi quit)
{
    /// <summary>The console window's screen name.</summary>
    public const string ConsoleScreen = "console";

    // ---- IGameUI (GameUI011), slot order ----

    public void Initialize(nint appFactory) { }

    public void PostInit() { }

    public void Connect(nint gameFactory) { }

    public void Start() { }

    public void Shutdown() { }

    public void RunFrame() { }

    public void OnGameUIActivated() => screens.ShowPauseMenu();

    public void OnGameUIHidden() => screens.HidePauseMenu();

    public void OldOnConnectToServer(string? game, int ip, int port) { }

    public void OnDisconnectFromServerOld(int steamLoginFailure, string? username) => loading.Disconnected(steamLoginFailure);

    public void OnLevelLoadingStarted(bool showProgressDialog) => loading.Started(showProgressDialog);

    public void OnLevelLoadingFinished(bool error, string? failureReason, string? extendedReason)
        => loading.Finished(error, failureReason, extendedReason);

    public bool UpdateProgressBar(float progress, string? statusText) => loading.Update(progress, statusText);

    /// <summary>Stock's dialog switched to its text form; ours always shows its step.</summary>
    public bool SetShowProgressText(bool show) => false;

    public void ShowNewGameDialog(int chapter) { }

    public void SessionNotification(int notification, int param) { }

    public void SystemNotification(int notification) { }

    public void ShowMessageDialog(uint type, nint owner) { }

    public void UpdatePlayerInfo(ulong playerId, string? name, int team, byte voiceState, int playersNeeded, bool host) { }

    public void SessionSearchResult(int searchIndex, nint hostData, nint result, int ping) { }

    public void OnCreditsFinished() { }

    public void SetLoadingBackgroundDialog(nint panel) { }

    public void BonusMapUnlock(string? fileName, string? mapName) { }

    public void BonusMapComplete(string? fileName, string? mapName) { }

    public void BonusMapChallengeUpdate(string? fileName, string? mapName, string? challengeName, int best) { }

    public void BonusMapDatabaseSave() { }

    public int BonusMapNumAdvancedCompleted() => 0;

    public void OnConnectToServer2(string? game, int ip, int connectionPort, int queryPort) { }

    public bool ValidateStorageDevice() => false;

    public void SetProgressOnStart() { }

    public void OnDisconnectFromServer(int steamLoginFailure) => loading.Disconnected(steamLoginFailure);

    public void OnConfirmQuit() => quit.Ask();

    public bool IsMainMenuVisible() => screens.MainMenuOpen;

    public void SetMainMenuOverride(nint panel) { }

    public void SendMainMenuCommand(string? command) { }

    // ---- IGameConsole (GameConsole004), slot order ----

    public void ConsoleActivate() => screens.Open(ConsoleScreen);

    public void ConsoleInitialize() { }

    public void ConsoleHide() => screens.Close(ConsoleScreen);

    public void ConsoleClear() { }

    public bool IsConsoleVisible() => screens.IsOpen(ConsoleScreen);

    public void ConsoleSetParent(nint parent) { }
}
