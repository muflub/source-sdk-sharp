using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The GameUI's managed-built area (plan_gameui_managed.md G4): native's
/// calls reach the current host with their strings read and their answers
/// as native's ints; with no host, a call does nothing and a question
/// answers no.
/// </summary>
public unsafe class GameUiBindingTests
{
    private sealed class Host : IGameUiCalls
    {
        public List<string> Calls { get; } = [];
        public bool Progress { get; set; }

        public void Initialize(nint appFactory) => Calls.Add($"Initialize {appFactory}");
        public void PostInit() => Calls.Add("PostInit");
        public void Connect(nint gameFactory) => Calls.Add("Connect");
        public void Start() => Calls.Add("Start");
        public void Shutdown() => Calls.Add("Shutdown");
        public void RunFrame() => Calls.Add("RunFrame");
        public void OnGameUIActivated() => Calls.Add("OnGameUIActivated");
        public void OnGameUIHidden() => Calls.Add("OnGameUIHidden");
        public void OldOnConnectToServer(string? game, int ip, int port) { }
        public void OnDisconnectFromServerOld(int steamLoginFailure, string? username) { }
        public void OnLevelLoadingStarted(bool showProgressDialog) => Calls.Add($"OnLevelLoadingStarted {showProgressDialog}");
        public void OnLevelLoadingFinished(bool error, string? failureReason, string? extendedReason)
            => Calls.Add($"OnLevelLoadingFinished {error} {failureReason} {extendedReason}");
        public bool UpdateProgressBar(float progress, string? statusText) => Progress;
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
        public void OnConnectToServer2(string? game, int ip, int connectionPort, int queryPort)
            => Calls.Add($"OnConnectToServer2 {game} {ip} {connectionPort} {queryPort}");
        public bool ValidateStorageDevice() => false;
        public void SetProgressOnStart() { }
        public void OnDisconnectFromServer(int steamLoginFailure) => Calls.Add($"OnDisconnectFromServer {steamLoginFailure}");
        public void OnConfirmQuit() => Calls.Add("OnConfirmQuit");
        public bool IsMainMenuVisible() => true;
        public void SetMainMenuOverride(nint panel) { }
        public void SendMainMenuCommand(string? command) => Calls.Add($"SendMainMenuCommand {command}");
        public void ConsoleActivate() => Calls.Add("ConsoleActivate");
        public void ConsoleInitialize() { }
        public void ConsoleHide() { }
        public void ConsoleClear() { }
        public bool IsConsoleVisible() => true;
        public void ConsoleSetParent(nint parent) { }
    }

    private static byte[] C(string text) => Encoding.UTF8.GetBytes(text + "\0");

    [Fact]
    public void Calls_reach_the_host_with_their_strings_and_flags()
    {
        var host = new Host();
        var binding = new GameUiBinding(new Crossings(), new GameUiCallLog(), () => host);
        fixed (byte* game = C("hl2mp"))
        fixed (byte* reason = C("#Fail"))
        fixed (byte* command = C("OpenServerBrowser"))
        {
            binding.OnLevelLoadingStarted(1);
            binding.OnConnectToServer2(game, 0x0100007f, 27015, 27016);
            binding.OnLevelLoadingFinished(1, reason, null);
            binding.SendMainMenuCommand(command);
        }
        binding.OnDisconnectFromServer(4);
        binding.ConsoleActivate();
        Assert.Equal(["OnLevelLoadingStarted True", "OnConnectToServer2 hl2mp 16777343 27015 27016", "OnLevelLoadingFinished True #Fail ",
                      "SendMainMenuCommand OpenServerBrowser", "OnDisconnectFromServer 4", "ConsoleActivate"], host.Calls);
    }

    [Fact]
    public void Answers_are_natives_ints()
    {
        var host = new Host { Progress = true };
        var binding = new GameUiBinding(new Crossings(), new GameUiCallLog(), () => host);
        Assert.Equal(1, binding.UpdateProgressBar(0.5f, null));
        Assert.Equal(1, binding.IsMainMenuVisible());
        Assert.Equal(1, binding.ConsoleIsVisible());
        Assert.Equal(0, binding.SetShowProgressText(1));
    }

    [Fact]
    public void Without_a_host_a_call_does_nothing_and_a_question_answers_no()
    {
        var binding = new GameUiBinding(new Crossings(), new GameUiCallLog(), () => null);
        binding.OnGameUIActivated();
        Assert.Equal(0, binding.UpdateProgressBar(0.5f, null));
        Assert.Equal(0, binding.IsMainMenuVisible());
    }

    [Fact]
    public void Each_call_counts_a_crossing()
    {
        var crossings = new Crossings();
        var binding = new GameUiBinding(crossings, new GameUiCallLog(), () => new Host());
        binding.RunFrame();
        binding.RunFrame();
        Assert.Equal(2, crossings.Read(CrossingOp.ClientGameUi));
    }

    // ---- the call log (the dev API's gameui route) ----

    [Fact]
    public void Each_call_is_logged_with_its_arguments_and_whether_a_host_took_it()
    {
        var log = new GameUiCallLog();
        IGameUiCalls? host = null;
        var binding = new GameUiBinding(new Crossings(), log, () => host);
        binding.OnGameUIActivated();
        host = new Host();
        fixed (byte* game = C("hl2mp"))
            binding.OnConnectToServer2(game, 0x0100007f, 27015, 27016);
        binding.ConsoleActivate();
        Assert.Equal(3, log.Calls);
        Assert.Equal(1, log.Unhosted);
        Assert.Equal([
            new GameUiCall(1, GameUiMethod.OnGameUIActivated, null, false, 1),
            new GameUiCall(2, GameUiMethod.OnConnectToServer2, "hl2mp, 16777343, 27015, 27016", true, 1),
            new GameUiCall(3, GameUiMethod.ConsoleActivate, null, true, 1),
        ], log.Recent());
    }

    /// <summary>RunFrame comes every frame: counted, never kept, so the kept
    /// calls stay the ones worth reading; a repeat of the last kept call adds
    /// to its repeats.</summary>
    [Fact]
    public void Run_frame_is_counted_not_kept_and_a_repeat_adds_to_the_last_call()
    {
        var log = new GameUiCallLog();
        var binding = new GameUiBinding(new Crossings(), log, () => new Host());
        binding.IsMainMenuVisible();
        binding.RunFrame();
        binding.IsMainMenuVisible();
        binding.RunFrame();
        binding.IsMainMenuVisible();
        Assert.Equal(2, log.Count(GameUiMethod.RunFrame));
        Assert.Equal(3, log.Count(GameUiMethod.IsMainMenuVisible));
        Assert.Equal([new GameUiCall(1, GameUiMethod.IsMainMenuVisible, null, true, 3)], log.Recent());
    }

    /// <summary>Live, one map load sent 247 progress updates and the console
    /// was asked 1905 times, which filled the ring: the question is counted,
    /// and a run of progress is one entry at the latest progress.</summary>
    [Fact]
    public void A_run_of_progress_is_one_entry_and_the_console_question_is_counted_not_kept()
    {
        var log = new GameUiCallLog();
        log.Record(GameUiMethod.UpdateProgressBar, true, "0.1, ");
        log.Record(GameUiMethod.IsConsoleVisible, true);
        log.Record(GameUiMethod.UpdateProgressBar, true, "0.2, ");
        log.Record(GameUiMethod.UpdateProgressBar, true, "0.3, #LoadingProgress_SignonLocal");
        Assert.Equal(1, log.Count(GameUiMethod.IsConsoleVisible));
        Assert.Equal([new GameUiCall(1, GameUiMethod.UpdateProgressBar, "0.3, #LoadingProgress_SignonLocal", true, 3)], log.Recent());
    }

    [Fact]
    public void The_log_keeps_the_last_sixty_four_calls_oldest_first()
    {
        var log = new GameUiCallLog();
        for (int i = 0; i < GameUiCallLog.Capacity + 6; i++)
            log.Record(GameUiMethod.ShowNewGameDialog, true, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var recent = log.Recent();
        Assert.Equal(GameUiCallLog.Capacity, recent.Length);
        Assert.Equal("6", recent[0].Arguments);
        Assert.Equal(7, recent[0].Sequence);
        Assert.Equal("69", recent[^1].Arguments);
    }
}
