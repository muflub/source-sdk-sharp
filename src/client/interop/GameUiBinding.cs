using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What the GameUI's managed-built area forwards to (plan_gameui_managed.md
/// G4): the managed GameUI host, one method per engine slot of GameUI011 and
/// GameConsole004, in their order.
/// </summary>
public interface IGameUiCalls
{
    void Initialize(nint appFactory);
    void PostInit();
    void Connect(nint gameFactory);
    void Start();
    void Shutdown();
    void RunFrame();
    void OnGameUIActivated();
    void OnGameUIHidden();
    void OldOnConnectToServer(string? game, int ip, int port);
    void OnDisconnectFromServerOld(int steamLoginFailure, string? username);
    void OnLevelLoadingStarted(bool showProgressDialog);
    void OnLevelLoadingFinished(bool error, string? failureReason, string? extendedReason);
    bool UpdateProgressBar(float progress, string? statusText);
    bool SetShowProgressText(bool show);
    void ShowNewGameDialog(int chapter);
    void SessionNotification(int notification, int param);
    void SystemNotification(int notification);
    void ShowMessageDialog(uint type, nint owner);
    void UpdatePlayerInfo(ulong playerId, string? name, int team, byte voiceState, int playersNeeded, bool host);
    void SessionSearchResult(int searchIndex, nint hostData, nint result, int ping);
    void OnCreditsFinished();
    void SetLoadingBackgroundDialog(nint panel);
    void BonusMapUnlock(string? fileName, string? mapName);
    void BonusMapComplete(string? fileName, string? mapName);
    void BonusMapChallengeUpdate(string? fileName, string? mapName, string? challengeName, int best);
    void BonusMapDatabaseSave();
    int BonusMapNumAdvancedCompleted();
    void OnConnectToServer2(string? game, int ip, int connectionPort, int queryPort);
    bool ValidateStorageDevice();
    void SetProgressOnStart();
    void OnDisconnectFromServer(int steamLoginFailure);
    void OnConfirmQuit();
    bool IsMainMenuVisible();
    void SetMainMenuOverride(nint panel);
    void SendMainMenuCommand(string? command);
    void ConsoleActivate();
    void ConsoleInitialize();
    void ConsoleHide();
    void ConsoleClear();
    bool IsConsoleVisible();
    void ConsoleSetParent(nint parent);
}

/// <summary>
/// The GameUI's managed-built area: each engine call native's SSGameUI and
/// SSGameConsole forward, its strings read, to the current GameUI host (a UI
/// reload replaces it, so it is asked for on every call), and into the call
/// log (<see cref="GameUiCallLog"/>, the dev API's <c>gameui</c> route). With
/// no host yet, a call does nothing and a question answers no.
/// </summary>
public sealed unsafe class GameUiBinding(Crossings crossings, GameUiCallLog log, Func<IGameUiCalls?> host) : IManagedGameUiClient
{
    // Long enough for any path or reason the engine passes; longer is cut.
    private const int MaxText = 1024;

    private static string? Text(byte* text) => Vtable.TextOf(text, MaxText);

    private static string Pointer(void* p) => $"0x{(nint)p:x}";

    private IGameUiCalls? Host(GameUiMethod method, string? arguments = null)
    {
        crossings.Count(CrossingOp.ClientGameUi);
        var h = host();
        log.Record(method, h is not null, arguments);
        return h;
    }

    public void Initialize(void* a0) => Host(GameUiMethod.Initialize, Pointer(a0))?.Initialize((nint)a0);
    public void PostInit() => Host(GameUiMethod.PostInit)?.PostInit();
    public void Connect(void* a0) => Host(GameUiMethod.Connect, Pointer(a0))?.Connect((nint)a0);
    public void Start() => Host(GameUiMethod.Start)?.Start();
    public void Shutdown() => Host(GameUiMethod.Shutdown)?.Shutdown();
    public void RunFrame() => Host(GameUiMethod.RunFrame)?.RunFrame();
    public void OnGameUIActivated() => Host(GameUiMethod.OnGameUIActivated)?.OnGameUIActivated();
    public void OnGameUIHidden() => Host(GameUiMethod.OnGameUIHidden)?.OnGameUIHidden();

    public void OldOnConnectToServer(byte* a0, int a1, int a2)
    {
        string? game = Text(a0);
        Host(GameUiMethod.OldOnConnectToServer, $"{game}, {a1}, {a2}")?.OldOnConnectToServer(game, a1, a2);
    }

    public void OnDisconnectFromServerOld(int a0, byte* a1)
    {
        string? user = Text(a1);
        Host(GameUiMethod.OnDisconnectFromServerOld, $"{a0}, {user}")?.OnDisconnectFromServerOld(a0, user);
    }

    public void OnLevelLoadingStarted(int a0) => Host(GameUiMethod.OnLevelLoadingStarted, a0 != 0 ? "true" : "false")?.OnLevelLoadingStarted(a0 != 0);

    public void OnLevelLoadingFinished(int a0, byte* a1, byte* a2)
    {
        string? reason = Text(a1), extended = Text(a2);
        Host(GameUiMethod.OnLevelLoadingFinished, $"{a0 != 0}, {reason}, {extended}")?.OnLevelLoadingFinished(a0 != 0, reason, extended);
    }

    public int UpdateProgressBar(float a0, byte* a1)
    {
        string? status = Text(a1);
        return Host(GameUiMethod.UpdateProgressBar, $"{a0:0.###}, {status}")?.UpdateProgressBar(a0, status) == true ? 1 : 0;
    }

    public int SetShowProgressText(int a0)
        => Host(GameUiMethod.SetShowProgressText, a0 != 0 ? "true" : "false")?.SetShowProgressText(a0 != 0) == true ? 1 : 0;
    public void ShowNewGameDialog(int a0) => Host(GameUiMethod.ShowNewGameDialog, $"{a0}")?.ShowNewGameDialog(a0);
    public void SessionNotification(int a0, int a1) => Host(GameUiMethod.SessionNotification, $"{a0}, {a1}")?.SessionNotification(a0, a1);
    public void SystemNotification(int a0) => Host(GameUiMethod.SystemNotification, $"{a0}")?.SystemNotification(a0);
    public void ShowMessageDialog(uint a0, void* a1) => Host(GameUiMethod.ShowMessageDialog, $"{a0}, {Pointer(a1)}")?.ShowMessageDialog(a0, (nint)a1);

    public void UpdatePlayerInfo(ulong a0, byte* a1, int a2, byte a3, int a4, int a5)
    {
        string? name = Text(a1);
        Host(GameUiMethod.UpdatePlayerInfo, $"{a0}, {name}, {a2}, {a3}, {a4}, {a5 != 0}")?.UpdatePlayerInfo(a0, name, a2, a3, a4, a5 != 0);
    }

    public void SessionSearchResult(int a0, void* a1, void* a2, int a3)
        => Host(GameUiMethod.SessionSearchResult, $"{a0}, {Pointer(a1)}, {Pointer(a2)}, {a3}")?.SessionSearchResult(a0, (nint)a1, (nint)a2, a3);
    public void OnCreditsFinished() => Host(GameUiMethod.OnCreditsFinished)?.OnCreditsFinished();
    public void SetLoadingBackgroundDialog(void* a0)
        => Host(GameUiMethod.SetLoadingBackgroundDialog, Pointer(a0))?.SetLoadingBackgroundDialog((nint)a0);

    public void BonusMapUnlock(byte* a0, byte* a1)
    {
        string? file = Text(a0), map = Text(a1);
        Host(GameUiMethod.BonusMapUnlock, $"{file}, {map}")?.BonusMapUnlock(file, map);
    }

    public void BonusMapComplete(byte* a0, byte* a1)
    {
        string? file = Text(a0), map = Text(a1);
        Host(GameUiMethod.BonusMapComplete, $"{file}, {map}")?.BonusMapComplete(file, map);
    }

    public void BonusMapChallengeUpdate(byte* a0, byte* a1, byte* a2, int a3)
    {
        string? file = Text(a0), map = Text(a1), challenge = Text(a2);
        Host(GameUiMethod.BonusMapChallengeUpdate, $"{file}, {map}, {challenge}, {a3}")?.BonusMapChallengeUpdate(file, map, challenge, a3);
    }

    public void BonusMapDatabaseSave() => Host(GameUiMethod.BonusMapDatabaseSave)?.BonusMapDatabaseSave();
    public int BonusMapNumAdvancedCompleted() => Host(GameUiMethod.BonusMapNumAdvancedCompleted)?.BonusMapNumAdvancedCompleted() ?? 0;

    public void OnConnectToServer2(byte* a0, int a1, int a2, int a3)
    {
        string? game = Text(a0);
        Host(GameUiMethod.OnConnectToServer2, $"{game}, {a1}, {a2}, {a3}")?.OnConnectToServer2(game, a1, a2, a3);
    }

    public int ValidateStorageDevice() => Host(GameUiMethod.ValidateStorageDevice)?.ValidateStorageDevice() == true ? 1 : 0;
    public void SetProgressOnStart() => Host(GameUiMethod.SetProgressOnStart)?.SetProgressOnStart();
    public void OnDisconnectFromServer(int a0) => Host(GameUiMethod.OnDisconnectFromServer, $"{a0}")?.OnDisconnectFromServer(a0);
    public void OnConfirmQuit() => Host(GameUiMethod.OnConfirmQuit)?.OnConfirmQuit();
    public int IsMainMenuVisible() => Host(GameUiMethod.IsMainMenuVisible)?.IsMainMenuVisible() == true ? 1 : 0;
    public void SetMainMenuOverride(void* a0) => Host(GameUiMethod.SetMainMenuOverride, Pointer(a0))?.SetMainMenuOverride((nint)a0);

    public void SendMainMenuCommand(byte* a0)
    {
        string? command = Text(a0);
        Host(GameUiMethod.SendMainMenuCommand, command)?.SendMainMenuCommand(command);
    }

    public void ConsoleActivate() => Host(GameUiMethod.ConsoleActivate)?.ConsoleActivate();
    public void ConsoleInitialize() => Host(GameUiMethod.ConsoleInitialize)?.ConsoleInitialize();
    public void ConsoleHide() => Host(GameUiMethod.ConsoleHide)?.ConsoleHide();
    public void ConsoleClear() => Host(GameUiMethod.ConsoleClear)?.ConsoleClear();
    public int ConsoleIsVisible() => Host(GameUiMethod.IsConsoleVisible)?.IsConsoleVisible() == true ? 1 : 0;
    public void ConsoleSetParent(void* a0) => Host(GameUiMethod.ConsoleSetParent, Pointer(a0))?.ConsoleSetParent((nint)a0);
}
