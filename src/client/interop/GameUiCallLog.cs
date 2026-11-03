namespace SourceSharp.Client.Interop;

/// <summary>The engine's GameUI011 and GameConsole004 methods, in slot order (<see cref="IGameUiCalls"/>).</summary>
public enum GameUiMethod : byte
{
    Initialize,
    PostInit,
    Connect,
    Start,
    Shutdown,
    RunFrame,
    OnGameUIActivated,
    OnGameUIHidden,
    OldOnConnectToServer,
    OnDisconnectFromServerOld,
    OnLevelLoadingStarted,
    OnLevelLoadingFinished,
    UpdateProgressBar,
    SetShowProgressText,
    ShowNewGameDialog,
    SessionNotification,
    SystemNotification,
    ShowMessageDialog,
    UpdatePlayerInfo,
    SessionSearchResult,
    OnCreditsFinished,
    SetLoadingBackgroundDialog,
    BonusMapUnlock,
    BonusMapComplete,
    BonusMapChallengeUpdate,
    BonusMapDatabaseSave,
    BonusMapNumAdvancedCompleted,
    OnConnectToServer2,
    ValidateStorageDevice,
    SetProgressOnStart,
    OnDisconnectFromServer,
    OnConfirmQuit,
    IsMainMenuVisible,
    SetMainMenuOverride,
    SendMainMenuCommand,
    ConsoleActivate,
    ConsoleInitialize,
    ConsoleHide,
    ConsoleClear,
    IsConsoleVisible,
    ConsoleSetParent,
}

/// <summary>
/// One kept call: its place in the session's calls (1-based), the method, its
/// arguments as text (null for none), whether a GameUI host was there to take
/// it, and how many times in a row it came the same.
/// </summary>
public readonly record struct GameUiCall(long Sequence, GameUiMethod Method, string? Arguments, bool Hosted, int Repeats);

/// <summary>
/// The engine's GameUI calls as they reach managed (plan_gameui_managed.md
/// §4): every call counted by method, the last <see cref="Capacity"/> kept
/// with their arguments, and the calls no host took. <see cref="GameUiMethod.RunFrame"/>
/// and <see cref="GameUiMethod.IsConsoleVisible"/> come every frame, so they
/// are counted and never kept; a call that repeats the last one kept (same
/// method, arguments and host) adds to its repeats, and a run of
/// <see cref="GameUiMethod.UpdateProgressBar"/> (some 250 a map load) is one
/// entry holding the latest progress.
/// Game thread only, as the GameUI's calls and the dev API's answers are.
/// </summary>
public sealed class GameUiCallLog
{
    public const int Capacity = 64;

    private readonly GameUiCall[] _ring = new GameUiCall[Capacity];
    private readonly long[] _counts = new long[(int)GameUiMethod.ConsoleSetParent + 1];
    private int _next;
    private int _kept;
    private long _calls;
    private long _unhosted;

    /// <summary>Every call so far.</summary>
    public long Calls => _calls;

    /// <summary>The calls that found no GameUI host: none of them was answered.</summary>
    public long Unhosted => _unhosted;

    public long Count(GameUiMethod method) => _counts[(int)method];

    public void Record(GameUiMethod method, bool hosted, string? arguments = null)
    {
        _counts[(int)method]++;
        _calls++;
        if (!hosted)
            _unhosted++;
        if (method is GameUiMethod.RunFrame or GameUiMethod.IsConsoleVisible)
            return;
        if (_kept > 0)
        {
            ref var last = ref _ring[(_next + Capacity - 1) % Capacity];
            if (last.Method == method && last.Hosted == hosted
                && (last.Arguments == arguments || method == GameUiMethod.UpdateProgressBar))
            {
                last = last with { Arguments = arguments, Repeats = last.Repeats + 1 };
                return;
            }
        }
        _ring[_next] = new GameUiCall(_calls, method, arguments, hosted, 1);
        _next = (_next + 1) % Capacity;
        if (_kept < Capacity)
            _kept++;
    }

    /// <summary>The kept calls, oldest first.</summary>
    public GameUiCall[] Recent()
    {
        var calls = new GameUiCall[_kept];
        int first = (_next + Capacity - _kept) % Capacity;
        for (int i = 0; i < _kept; i++)
            calls[i] = _ring[(first + i) % Capacity];
        return calls;
    }
}
