using SourceSharp.Client.Interop;


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

    /// <summary>The menu layer's frames drawn so far (a load's paints are its difference).</summary>
    long MenuFrames { get; }

    void Open(string screen);

    void Close(string screen);

    bool IsOpen(string screen);
}

/// <summary>
/// Where every call the engine makes of its GameUI lands (plan_gameui_managed.md;
/// bound to GameUI011 and GameConsole004 from G4): each method of the two
/// interfaces, in their order, routed to the UI (the three Bonus-map calls
/// with out-parameters are answered natively and never reach here). The pause menu follows the
/// GameUI's activation and hide; the load dialog, the disconnect dialog and
/// Quit's question are the sources' (<see cref="GameUiLoading"/>,
/// <see cref="ConfirmQuitUi"/>); the console methods open, close and ask the
/// console window. The single-player, bonus-map, Xbox and session methods
/// answer inertly (docs/absence-client.md's "GameUI single-player surface"),
/// as does the override panel a stock TF2 client handed it
/// (<see cref="SetMainMenuOverride"/>); a menu command it was sent runs
/// through the game menu's table (<see cref="GameMenuCommands"/>), as
/// <c>gamemenucommand</c> does.
/// </summary>
public sealed class GameUiHost(IGameUiNavigation screens, GameUiLoading loading, ConfirmQuitUi quit, GameMenuCommands menu,
                               IServerHistory history, GameUiLoadMeter meter, ConnectedServer? connected = null)
    : SourceSharp.Client.Interop.IGameUiCalls
{
    /// <summary>The console window's screen name.</summary>
    public const string ConsoleScreen = "console";

    /// <summary>
    /// The methods this host answers inertly by ruling (the empty bodies and
    /// constant answers below): the dev API's <c>gameui</c> route calls their
    /// calls "inert" rather than "handled".
    /// </summary>
    public static bool AnswersInertly(GameUiMethod method) => method is
        GameUiMethod.Initialize or GameUiMethod.PostInit or GameUiMethod.Connect or GameUiMethod.Start
        or GameUiMethod.Shutdown or GameUiMethod.RunFrame or GameUiMethod.OldOnConnectToServer
        or GameUiMethod.SetShowProgressText or GameUiMethod.ShowNewGameDialog or GameUiMethod.SessionNotification
        or GameUiMethod.SystemNotification or GameUiMethod.ShowMessageDialog or GameUiMethod.UpdatePlayerInfo
        or GameUiMethod.SessionSearchResult or GameUiMethod.OnCreditsFinished or GameUiMethod.SetLoadingBackgroundDialog
        or GameUiMethod.BonusMapUnlock or GameUiMethod.BonusMapComplete or GameUiMethod.BonusMapChallengeUpdate
        or GameUiMethod.BonusMapDatabaseSave or GameUiMethod.BonusMapNumAdvancedCompleted
        or GameUiMethod.ValidateStorageDevice or GameUiMethod.SetProgressOnStart or GameUiMethod.SetMainMenuOverride
        or GameUiMethod.ConsoleInitialize or GameUiMethod.ConsoleClear or GameUiMethod.ConsoleSetParent;

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

    public void OnLevelLoadingStarted(bool showProgressDialog)
    {
        loading.Started(showProgressDialog);
        meter.Started(showProgressDialog, screens.MenuFrames);
    }

    public void OnLevelLoadingFinished(bool error, string? failureReason, string? extendedReason)
    {
        loading.Finished(error, failureReason, extendedReason);
        meter.Finished(screens.MenuFrames);
    }

    public bool UpdateProgressBar(float progress, string? statusText)
    {
        bool repaint = loading.Update(progress, statusText);
        meter.Updated(repaint);
        return repaint;
    }

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

    /// <summary>A server joined: into the browser's history, as stock's browser
    /// kept it. The engine passes the address in network byte order
    /// (GetIPNetworkByteOrder) and the ports in host order.</summary>
    public void OnConnectToServer2(string? game, int ip, int connectionPort, int queryPort)
    {
        history.Record(HostOrder(ip), (ushort)connectionPort, (ushort)queryPort);
        connected?.Joined(HostOrder(ip), (ushort)connectionPort);
    }

    private static uint HostOrder(int networkOrder)
        => BitConverter.IsLittleEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)networkOrder) : (uint)networkOrder;

    public bool ValidateStorageDevice() => false;

    public void SetProgressOnStart() { }

    public void OnDisconnectFromServer(int steamLoginFailure) => loading.Disconnected(steamLoginFailure);

    public void OnConfirmQuit() => quit.Ask();

    public bool IsMainMenuVisible() => screens.MainMenuOpen;

    public void SetMainMenuOverride(nint panel) { }

    public void SendMainMenuCommand(string? command) => menu.Run(command);

    // ---- IGameConsole (GameConsole004), slot order ----

    public void ConsoleActivate() => screens.Open(ConsoleScreen);

    public void ConsoleInitialize() { }

    public void ConsoleHide() => screens.Close(ConsoleScreen);

    public void ConsoleClear() { }

    public bool IsConsoleVisible() => screens.IsOpen(ConsoleScreen);

    public void ConsoleSetParent(nint parent) { }
}
