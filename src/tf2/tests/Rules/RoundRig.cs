using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Tests.Rules;

/// <summary>A clock a fact moves by hand.</summary>
internal sealed class FakeClock : IGameClock
{
    public float Now { get; set; }
}

/// <summary>The level seam, naming the map a fact is playing on.</summary>
internal sealed class FakeLevel : ILevelInfo
{
    public string? Map { get; set; }
    public bool IsReady { get; set; } = true;
}

/// <summary>The round convars at their stock defaults, settable.</summary>
internal sealed class FakeSettings : ITeamRoundSettings
{
    private readonly List<Action<int, int>> _timeLimitHandlers = new();
    private readonly List<Action<int, int>> _winLimitHandlers = new();

    public float RespawnWaveTime { get; set; } = 10f;
    public bool DisableRespawnTimes { get; set; }
    public int BonusRoundTime { get; set; } = 15;
    public float EnableRoundWaitTime { get; set; } = 1f;
    public bool StalemateEnable { get; set; }
    public int StalemateTimeLimit { get; set; } = 240;
    public float WaitingForPlayersTime { get; set; }
    public bool WaitingForPlayersCancel { get; set; }
    public bool WaitingForPlayersRestart { get; set; }
    public int TimeLimitMinutes { get; set; }
    public int WinLimit { get; set; }
    public int WinDifference { get; set; }
    public int WinDifferenceMin { get; set; }
    public int MaxRounds { get; set; }
    public bool ScrambleAuto { get; set; } = true;
    public int ScrambleAutoWinDifference { get; set; } = 2;
    public string NextLevel { get; set; } = string.Empty;
    public int RestartRound { get; set; }
    public int RestartGame { get; set; }
    public bool RestartGameImmediate { get; set; }
    public bool MatchEndAtTimeLimit { get; set; }
    public int TeamsUnbalanceLimit { get; set; } = 1;
    public bool SpectatorsRestricted { get; set; }
    public int AutoTeamBalance { get; set; } = 1;
    public bool ForceAutoTeam { get; set; }
    public bool Tournament { get; set; }
    public bool ReadyRestart { get; set; }
    public string ReadySignal { get; set; } = "ready";

    public IDisposable OnTimeLimitChanged(Action<int, int> handler)
        => Subscribe(_timeLimitHandlers, handler);

    public IDisposable OnWinLimitChanged(Action<int, int> handler)
        => Subscribe(_winLimitHandlers, handler);

    /// <summary>A live change to a round convar, as the engine's change callback delivers it.</summary>
    public void ChangeTimeLimit(int oldMinutes, int newMinutes)
    {
        TimeLimitMinutes = newMinutes;
        foreach (var handler in _timeLimitHandlers.ToArray())
            handler(oldMinutes, newMinutes);
    }

    /// <summary>A live change to mp_winlimit, as the engine's change callback delivers it.</summary>
    public void ChangeWinLimit(int oldLimit, int newLimit)
    {
        WinLimit = newLimit;
        foreach (var handler in _winLimitHandlers.ToArray())
            handler(oldLimit, newLimit);
    }

    private static IDisposable Subscribe(List<Action<int, int>> handlers, Action<int, int> handler)
    {
        handlers.Add(handler);
        return new Unsubscribe(handlers, handler);
    }

    private sealed class Unsubscribe(List<Action<int, int>> handlers, Action<int, int> handler) : IDisposable
    {
        public void Dispose() => handlers.Remove(handler);
    }
}

/// <summary>
/// Players as plain records: a team, alive or not, when they died. Every
/// respawn and team change is recorded in order.
/// </summary>
internal sealed class FakeRoster(FakeClock clock) : ITeamRoundRoster
{
    private sealed class Player
    {
        public TFTeam Team;
        public bool Alive = true;
        public float Died = -1f;
        public bool Ready = true;
        public string Name = string.Empty;
    }

    private readonly Dictionary<EntityHandle, Player> _players = new();
    private readonly List<EntityHandle> _order = new();
    private uint _next = 0x0001_0001;

    public readonly List<(EntityHandle Player, float At)> Respawns = new();
    public readonly List<(EntityHandle Player, TFTeam Team)> Moves = new();

    public EntityHandle Add(TFTeam team, bool alive = true, string? name = null)
    {
        var handle = new EntityHandle(_next++);
        _players[handle] = new Player { Team = team, Alive = alive, Name = name ?? $"player{_order.Count + 1}" };
        _order.Add(handle);
        return handle;
    }

    public void Kill(EntityHandle player)
    {
        _players[player].Alive = false;
        _players[player].Died = clock.Now;
    }

    public void SetReady(EntityHandle player, bool ready) => _players[player].Ready = ready;

    public IReadOnlyList<EntityHandle> Players(TFTeam team) => _order.Where(p => _players[p].Team == team).ToArray();

    public TFTeam TeamOf(EntityHandle player) => _players[player].Team;

    public bool IsAlive(EntityHandle player) => _players[player].Alive;

    public float DeathTime(EntityHandle player) => _players[player].Alive ? -1f : _players[player].Died;

    public bool IsReadyToPlay(EntityHandle player)
        => _players[player].Team is TFTeam.Red or TFTeam.Blue && _players[player].Ready;

    public void ForceRespawn(EntityHandle player)
    {
        _players[player].Alive = true;
        _players[player].Died = -1f;
        Respawns.Add((player, clock.Now));
    }

    public bool ChangeTeam(EntityHandle player, TFTeam team)
    {
        _players[player].Team = team;
        Moves.Add((player, team));
        return true;
    }

    public string Name(EntityHandle player) => _players[player].Name;

    public int EntIndex(EntityHandle player) => _order.IndexOf(player) + 1;
}

/// <summary>Every announcement, as a line.</summary>
internal sealed class FakeAnnouncer : IRoundAnnouncer
{
    public readonly List<string> Said = new();

    public void RoundStart(bool fullReset) => Said.Add($"round_start full_reset={fullReset}");
    public void RoundActive() => Said.Add("round_active");
    public void RoundWin(TFTeam team, WinReason reason, bool fullRound, float roundTime, bool wasSuddenDeath)
        => Said.Add($"round_win {team} {reason} full={fullRound} sudden={wasSuddenDeath}");
    public void RoundStalemate(StalemateReason reason) => Said.Add($"round_stalemate {reason}");
    public void RestartRound() => Said.Add("restart_round");
    public void RestartSeconds(int seconds, string? format) => Said.Add($"restart_seconds {seconds} {format}");
    public void GameOver(string reason) => Said.Add($"game_over {reason}");
    public void BalanceWarning(int seconds) => Said.Add($"balance_warning {seconds}");
    public void TeamBalanced(int entIndex, TFTeam team, string name) => Said.Add($"team_balanced {name} {team}");
    public void MapTimeRemaining(int seconds) => Said.Add($"map_time_remaining {seconds}");

    public void UpdateTimer() => Said.Add("update_timer");

    public void WinlimitChanged() => Said.Add("winlimit_changed");

    public void WinSong(TFTeam team) => Said.Add($"win_song {team}");

    public void SuddenDeathSong() => Said.Add("sudden_death_song");

    public void Broadcast(TFTeam team, string sound) => Said.Add($"broadcast {team} {sound}");

    public void StartRoundVoice() => Said.Add("start_round_voice");

    public void ClanReadyRules(string signal) => Said.Add($"clan_ready_rules {signal}");

    public void ReadyRestart() => Said.Add("ready_restart");

    public void TeamReady(TFTeam team) => Said.Add($"team_ready {team}");

    public void TeamsSwitched() => Said.Add("teams_switched");

    public void ScrambleTeamsAlert() => Said.Add("scramble_teams_alert");

    public void ScorestatsAccumulatedReset() => Said.Add("scorestats_accumulated_reset");

    public int Count(string prefix) => Said.Count(s => s.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>The map: counts its resets and round inputs, in order.</summary>
internal sealed class FakeWorld : IRoundWorld
{
    public readonly List<string> Calls = new();

    /// <summary>How many respawns had happened when the first wipe came — to see the stalemate's order.</summary>
    public Func<int>? RespawnCount { get; set; }
    public int RespawnsAtFirstWipe { get; private set; } = -1;

    public int ResetMap()
    {
        Calls.Add("reset");
        return 1;
    }

    public int RoundSpawn()
    {
        Calls.Add("spawn");
        return 1;
    }

    public int RoundActivate()
    {
        Calls.Add("activate");
        return 1;
    }

    public void SetupControlPointSpawns() => Calls.Add("cp_spawns");

    public void RemoveProjectilesAndBuildings()
    {
        if (RespawnsAtFirstWipe < 0 && RespawnCount is { } count)
            RespawnsAtFirstWipe = count();
        Calls.Add("remove_projectiles_buildings");
    }

    public void DisableWorldHealthKits() => Calls.Add("disable_kits");

    public void RestoreWorldHealthKits() => Calls.Add("restore_kits");

    private bool _createdWaitingTimer;

    /// <summary>Whether the fake world answers the waiting-timer ask with a new timer (stock's create arm).</summary>
    public bool CreatedWaiting { get; set; }

    public bool EnsureWaitingTimer(float seconds)
    {
        Calls.Add($"waiting_timer {seconds}");
        if (!CreatedWaiting || _createdWaitingTimer)
            return false;

        _createdWaitingTimer = true;
        return true;
    }

    public void RetuneWaitingTimer(float seconds) => Calls.Add($"retune_waiting_timer {seconds}");

    public void RemoveWaitingTimer() => Calls.Add("remove_waiting_timer");

    public void SpawnStalemateTimer(int seconds) => Calls.Add($"stalemate_timer {seconds}");

    public void RemoveStalemateTimer() => Calls.Add("remove_stalemate_timer");

    public void SpawnTimeLimitTimer(int seconds) => Calls.Add($"timelimit_timer {seconds}");

    public void RemoveTimeLimitTimer() => Calls.Add("remove_timelimit_timer");

    /// <summary>Whether the map already has a timer of its own in the HUD (stock MapHasActiveTimer).</summary>
    public bool HasActiveTimer { get; set; }

    public bool MapHasActiveTimer() => HasActiveTimer;

    /// <summary>Whether a control-point master stands; when it does, it takes the win.</summary>
    public bool HasMaster { get; set; }

    public bool SetWinnerAndForceCaps(TFTeam team)
    {
        if (!HasMaster)
            return false;

        Calls.Add($"master_win {team}");
        return true;
    }

    public int Count(string call) => Calls.Count(c => c == call);
    public int Count(Func<string, bool> call) => Calls.Count(call);
}

internal sealed class FakeEnd : IGameEnd
{
    public int Intermissions { get; private set; }

    public void GoToIntermission() => Intermissions++;
}

/// <summary>A TF2 mode with every answer settable and every call counted.</summary>
internal sealed class FakeMode : ITeamRoundMode
{
    public int RoundStarts { get; private set; }
    public int RoundRunnings { get; private set; }
    public bool MayExpire { get; set; } = true;
    public bool MayStalemate { get; set; } = true;
    public bool ScoresPerRound { get; set; } = true;
    public bool AllowsWins { get; set; } = true;
    public float WaveScale { get; set; } = 1f;
    public bool EvenTeamsJoinAttackers { get; set; }
    public bool PointsMayAlwaysBeBlocked { get; set; }
    public TF2.Server.Modes.TFGameType GameType { get; set; }
    public bool InKothMode { get; set; }

    public void OnRoundStart() => RoundStarts++;
    public void OnRoundRunning() => RoundRunnings++;
    public bool TimerMayExpire() => MayExpire;
    public bool CanGoToStalemate() => MayStalemate;
    public int CapsChecks { get; private set; }
    public bool CapsWin { get; set; }
    public bool CheckCapsPerRound()
    {
        CapsChecks++;
        return CapsWin;
    }
    public bool ShouldScorePerRound() => ScoresPerRound;
    public bool AllowWin(TFTeam team, WinReason reason) => AllowsWins;
    public float RespawnWaveTime(TFTeam team, float seconds) => seconds * WaveScale;
}

internal sealed class ModeSlot : IActiveTeamRoundMode
{
    public ITeamRoundMode? Current { get; set; }
}

/// <summary>Every win panel ask, as its data.</summary>
internal sealed class FakeWinPanel : ITFWinPanel
{
    public readonly List<TFWinPanelData> Sent = new();

    public void Send(TFWinPanelData data) => Sent.Add(data);
}

/// <summary>A timer set with a settable HUD timer.</summary>
internal sealed class FakeTimers : IRoundTimers
{
    public ITeamRoundTimer? Active { get; set; }
    public int Hides { get; private set; }
    public int Restores { get; private set; }

    public void SetActive(ITeamRoundTimer? newlyActive) => Active = newlyActive;
    public void HideActive() => Hides++;
    public void RestoreActive() => Restores++;
}

internal sealed class FakeTimer : ITeamRoundTimer
{
    public EntityHandle Handle { get; } = new(0x0002_0001u);
    public float TimeRemaining { get; set; }
    public bool ShowInHud { get; set; } = true;
    public bool IsDisabled { get; set; }
    public void SetShowInHud(bool show) => ShowInHud = show;
    public void InputShowInHud(bool show) => ShowInHud = show;
}

internal sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
{
    public IReadOnlyList<T> All => hooks;

    public void Each(string what, Action<T> call)
    {
        foreach (var hook in hooks)
            call(hook);
    }
}

internal sealed class RecordingListener : ITeamRoundListener
{
    public readonly List<string> Heard = new();

    public void OnStateChanged(RoundState from, RoundState to) => Heard.Add($"{from}->{to}");

    public void OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds) => Heard.Add($"won {team} {reason} {bonusSeconds}");
}

internal sealed class FixedVeto : ITimerExpiryVeto
{
    public bool Allows { get; set; } = true;

    public bool TimerMayExpire() => Allows;
}

/// <summary>
/// The round machine over fakes of every seam, a TF2 mode active, the clock
/// at 100 s. <see cref="Step"/> moves the clock and runs one think.
/// </summary>
internal sealed class RoundRig
{
    public readonly FakeClock Clock = new() { Now = 100f };
    public readonly FakeSettings Settings = new();
    public readonly FakeRoster Roster;
    public readonly FakeAnnouncer Announcer = new();
    public readonly FakeWorld World = new();
    public readonly FakeEnd End = new();
    public readonly FakeMode Mode = new();
    public readonly ModeSlot Slot = new();
    public readonly TFTeamScores Scores;
    public readonly FakeTimers Timers = new();
    public readonly SourceSharp.Abi.RecordingLog Log = new();
    public readonly RecordingListener Listener = new();
    public readonly FixedVeto Veto = new();
    public readonly FakeWinPanel WinPanel = new();
    public readonly TeamBalance Balance;
    public readonly RecordingEngine Engine;
    public readonly TeamRoundFlow Flow;
    public readonly TF2.Server.Entities.IObjectiveEntities? Objectives;

    public RoundRig(TF2.Server.Entities.ITeamResource? table = null, TF2.Server.Player.TFPlayerCarriers? carriers = null,
                    TF2.Server.Econ.ITFPlayerClasses? classes = null, ILevelInfo? level = null,
                    TF2.Server.Entities.IObjectiveEntities? objectives = null,
                    TF2.Server.Player.ITFPlayerStats? stats = null, int seed = 1234)
    {
        Scores = new TFTeamScores(table ?? new TF2.Server.Entities.TeamResource());
        Roster = new FakeRoster(Clock);
        Slot.Current = Mode;
        Objectives = objectives;
        Balance = new TeamBalance(Settings, Roster, Announcer, Timers, Clock, Log, classes);
        // The auto scramble's dedicated-server gate: a dedicated box, as the
        // arm presumes; a listen-server fact flips it before the win.
        Engine = RecordingEngine.Default().Returns("Host.IsDedicatedServer", true);
        Flow = new TeamRoundFlow(Settings, Roster, Announcer, World, End, Slot, Scores, Timers, Balance, Clock, Log,
                                 new RandomSeeds(seed), Engine,
                                 new ListHooks<ITeamRoundListener>(Listener), new ListHooks<ITimerExpiryVeto>(Veto),
                                 carriers, WinPanel, level, objectives, stats);
    }

    /// <summary>Moves the clock by <paramref name="seconds"/> and thinks once.</summary>
    public void Step(float seconds = 0.1f)
    {
        Clock.Now += seconds;
        Flow.Think(Clock.Now);
    }

    /// <summary>Thinks every 0.1 s until <paramref name="until"/> holds; false after <paramref name="limit"/> seconds.</summary>
    public bool RunUntil(Func<bool> until, float limit = 120f)
    {
        float end = Clock.Now + limit;
        while (Clock.Now < end)
        {
            if (until())
                return true;
            Step();
        }

        return until();
    }

    /// <summary>One ready player per team, and the machine run into a playing round.</summary>
    public (EntityHandle Red, EntityHandle Blue) Running()
    {
        var red = Roster.Add(TFTeam.Red);
        var blue = Roster.Add(TFTeam.Blue);
        if (!RunUntil(() => Flow.State == RoundState.RoundRunning))
            throw new InvalidOperationException($"never reached RoundRunning: {Flow.State}");
        return (red, blue);
    }
}
