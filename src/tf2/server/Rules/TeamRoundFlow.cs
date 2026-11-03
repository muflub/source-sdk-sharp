//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Server.Modes;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// TF2's round machine: <c>CTeamplayRoundBasedRules</c>'s state machine
/// (<c>teamplayroundbased_gamerules.cpp</c>) with <c>CTFGameRules</c>'s arms,
/// as the <see cref="IRoundFlow"/> every TF2 mode answers with.
///
/// <para>
/// <b>One machine, the mode asked.</b> Every TF2 mode composes this one
/// service; where stock's <c>CTFGameRules</c> overrides a virtual per game
/// type (<c>TimerMayExpire</c>, <c>CanGoToStalemate</c>,
/// <c>ShouldScorePerRound</c>, ...), this asks the active mode's
/// <see cref="ITeamRoundMode"/>, and with no TF2 mode active it answers
/// stock's base-class default.
/// </para>
///
/// <para>
/// <b>The states.</b> PREGAME waits for a ready player; STARTGAME opens the
/// waiting-for-players period; PREROUND respawns everyone and resets the map
/// (<c>RoundRespawn</c>); RND_RUNNING plays, waves, balances and checks the
/// time limit; TEAM_WIN holds the bonus time, then checks the map's end and
/// goes back to PREROUND; STALEMATE is sudden death; RESTART resets the
/// scores and the map clock and is left at once for PREROUND. BONUS and
/// BETWEEN_RNDS are kept as values only: <c>CTFGameRules::ShouldGoToBonusRound</c>
/// returns false and the ready-status mode they serve is not ported.
/// GAME_OVER is the core's intermission, which ends the level.
/// </para>
///
/// <para>
/// <b>Not ported here</b> (rows in docs/plans/tf2/deferred.tsv): tournament,
/// arena, MvM, competitive, stopwatch, mini-rounds, the auto scramble, the win
/// panel, and the win and sudden-death songs.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITeamRoundState), typeof(ITeamRoundControl), typeof(IRoundTimerHost),
                                typeof(ITeamRespawnWaves), typeof(IObjectiveRoundRules), typeof(IPlayerChatHook)])]
public sealed class TeamRoundFlow : IRoundFlow, ITeamRoundState, ITeamRoundControl, IRoundTimerHost, ITeamRespawnWaves,
                                    IObjectiveRoundRules, IPlayerChatHook
{
    /// <summary><c>TEAMPLAY_ROUND_WIN_SCORE</c> (shareddefs.h).</summary>
    public const int RoundWinScore = 1;

    /// <summary>
    /// The "death anim + freeze panel" part of the minimum respawn time
    /// (<c>GetMinTimeWhenPlayerMaySpawn</c>): 2 s plus <c>spec_freeze_traveltime</c>
    /// and <c>spec_freeze_time</c>, whose non-CSTRIKE defaults (player.cpp:92-93)
    /// are 0.4 and 4.0. Those two convars are the core player's and are not
    /// declared in this build, so their defaults stand as the constant.
    /// </summary>
    public const float DeathAnimLength = 2.0f + 0.4f + 4.0f;

    /// <summary>The longest <c>mp_restartround</c>/<c>mp_restartgame</c> delay outside MvM/competitive.</summary>
    public const int MaxRestartDelay = 60;

    /// <summary>The ready restart's <c>mp_restartgame</c> (stock's all-teams arm).</summary>
    public const int RestartOnReady = 5;

    private readonly ITeamRoundSettings _settings;
    private readonly ITeamRoundRoster _roster;
    private readonly IRoundAnnouncer _announcer;
    private readonly IRoundWorld _world;
    private readonly IGameEnd _end;
    private readonly IActiveTeamRoundMode _mode;
    private readonly ITFTeamScores _scores;
    private readonly IRoundTimers _timers;
    private readonly TeamBalance _balance;
    private readonly IGameClock _clock;
    private readonly ILog _log;
    private readonly IEngineHost _host;
    private readonly IHooks<ITeamRoundListener>? _listeners;
    private readonly IHooks<ITimerExpiryVeto>? _vetoes;
    private readonly ITFWinPanel? _winPanel;
    private readonly ILevelInfo? _level;
    private readonly TF2.Server.Entities.IObjectiveEntities? _objectives;

    // The convar hooks the machine answers (stock's MPTimeLimitCallback and
    // WinlimitChanged): the machine is a side-wide singleton, so the
    // subscriptions live as long as it.
    private readonly IDisposable? _timeLimitHook;
    private readonly IDisposable? _winLimitHook;

    // The dead players' respawn overrides (the volumes' pair, read through
    // their carrier parts): the wave's hold and the override's own release.
    private readonly TF2.Server.Player.TFPlayerCarriers? _overrides;

    // The scramble's player scores (CTFPlayerResource::GetTotalScore reads
    // them; without them stock's sort keeps its order and the swap pass is
    // skipped), and the auto-assign coin's stream (the same arm TFTeams
    // takes, seeded once).
    private readonly TF2.Server.Player.ITFPlayerStats? _stats;
    private readonly Random _random;

    private const int Teams = (int)TFTeam.Count;
    private readonly float[] _nextRespawnWave = new float[Teams];
    private readonly float[] _teamRespawnWaveTimes = new float[Teams];
    private readonly float[] _originalTeamRespawnWaveTime = new float[Teams];
    private readonly bool[] _teamReady = new bool[Teams];

    private RoundState _state;
    private float _stateTransitionTime;
    private float _nextPeriodicThink;
    private bool _inWaitingForPlayers;
    private float _waitingForPlayersEnds;
    private float _restartRoundTime;
    private bool _awaitingReadyRestart;
    private bool _forceMapReset;
    private bool _switchTeams;
    private bool _scrambleTeams;
    // Stock's m_GameTeams (teamplayroundbased_gamerules.h:554): the win
    // counters the auto scramble's delta reads, one per game team in stock's
    // FIRST_GAME_TEAM iteration order — Red, then Blue. Stock fills the map
    // from the global team list on the first winning win and returns unless
    // it holds exactly two "real" teams (:2421-2437); the two teams are the
    // ones this game always fields, so the entries stand from the start.
    private readonly int[] _gameTeamWins = new int[2];
    // Stock's m_bResetTeamScores / m_bResetPlayerScores / m_bResetRoundsPlayed
    // (teamplayroundbased_gamerules.h:515-517): the three arms of ResetScores,
    // each live unless someone (the switch and scramble commands, :286-287 and
    // :307-314) disarms it before the next reset consumes and re-arms them.
    private bool _resetTeamScores = true;
    private bool _resetPlayerScores = true;
    private bool _resetRoundsPlayed = true;
    private bool _prevRoundWasWaitingForPlayers;
    // Set when a win yields to RESTART (stock's State_Transition_TEAM_WIN
    // ends in GR_STATE_RESTART): that restart ends a round, it is not a game
    // restart, so the scores and the map time survive it.
    private bool _restartAfterWin;
    private TFTeam _winningTeam;
    private WinReason _winReason;
    private bool _useAddScoreAnim;
    private int _roundsPlayed;
    private float _mapResetTime;
    private float _roundStartTime;
    private float _nextBalanceTeamsTime;
    private float _stalemateStartTime;
    private float _stalemateEnds;
    private bool _inOvertime;
    private bool _inSetup;
    private bool _changelevelAfterStalemate;
    private float _lastTeamWin;
    private int _lastCapPointChanged;
    // The HUD's round overlay state (stock's m_iPrevRoundState,
    // m_iCurrentRoundState and m_iCurrentMiniRoundMask, tf_gamerules.cpp:3316-3318).
    private int _prevRoundState = -1;
    private int _currentRoundState = -1;
    private int _currentMiniRoundMask;
    private readonly RoundHistory _history = new();

    public TeamRoundFlow(ITeamRoundSettings settings, ITeamRoundRoster roster, IRoundAnnouncer announcer,
                         IRoundWorld world, IGameEnd end, IActiveTeamRoundMode mode, ITFTeamScores scores,
                         IRoundTimers timers, TeamBalance balance, IGameClock clock, ILog log, IRandomSeeds seeds,
                         IEngineHost host,
                         IHooks<ITeamRoundListener>? listeners = null, IHooks<ITimerExpiryVeto>? vetoes = null,
                         TF2.Server.Player.TFPlayerCarriers? carriers = null, ITFWinPanel? winPanel = null,
                         ILevelInfo? level = null,
                         TF2.Server.Entities.IObjectiveEntities? objectives = null,
                         TF2.Server.Player.ITFPlayerStats? stats = null)
    {
        _settings = settings;
        _roster = roster;
        _announcer = announcer;
        _world = world;
        _end = end;
        _mode = mode;
        _scores = scores;
        _timers = timers;
        _balance = balance;
        _clock = clock;
        _log = log;
        _host = host;
        _listeners = listeners;
        _vetoes = vetoes;
        _overrides = carriers;
        _winPanel = winPanel;
        _level = level;
        _objectives = objectives;
        _stats = stats;
        _random = new Random(seeds.Next());

        // The constructor tells no listener: the listeners are resolved from
        // the container, and one that takes this machine would be asked for
        // while it is being built. The first ResetRound (the level's init)
        // tells them.
        _notify = false;
        Initialise();
        _notify = true;

        // Stock installs MPTimeLimitCallback on mp_timelimit's declaration and
        // the rules object answers it (multiplay_gamerules.cpp:72-90), so a
        // live change reaches HandleTimeLimitChange at once. mp_winlimit's
        // WinlimitChanged (:188-195) answers the same way.
        _timeLimitHook = _settings.OnTimeLimitChanged(HandleTimeLimitChange);
        _winLimitHook = _settings.OnWinLimitChanged(WinlimitChanged);
    }

    private bool _notify;

    private float Now => _clock.Now;

    private ITeamRoundMode? Mode => _mode.Current;

    /// <summary>
    /// Stock's <c>CTFGameRules::IsValveMap</c> (<c>tf_gamerules.cpp:16641-16652</c>):
    /// the current map against <c>s_ValveMaps</c>. The base class's fallback
    /// (<c>teamplayroundbased_gamerules.h:340</c>) answers false, and a
    /// machine built without the level seam answers it.
    /// </summary>
    private bool IsValveMap => _level?.Map is { } map && TFValveMaps.IsValveMap(map);

    //-------------------------------------------------------------------------
    // The reads (ITeamRoundState, IRoundTimerHost).
    //-------------------------------------------------------------------------

    public RoundState State => _state;

    public bool InWaitingForPlayers => _inWaitingForPlayers;

    public bool InSetup => _inSetup;

    public TFTeam WinningTeam => _winningTeam;

    public bool InOvertime => _inOvertime;

    /// <summary>Why the last round was won.</summary>
    public WinReason WinReason => _winReason;

    /// <summary>Full rounds played this map (stock <c>m_nRoundsPlayed</c>).</summary>
    public int RoundsPlayed => _roundsPlayed;

    /// <summary>When the waiting-for-players period ends; negative outside it.</summary>
    public float WaitingForPlayersEnds => _waitingForPlayersEnds;

    /// <summary>When a stalemate's time limit runs out; negative with none.</summary>
    public float StalemateEnds => _stalemateEnds;

    /// <summary>When the sudden death began (stock <c>m_flStalemateStartTime</c>); negative outside one.</summary>
    public float StalemateStartTime => _stalemateStartTime;

    /// <summary>When a restart countdown (<c>mp_restartgame</c>, the end of the wait) fires; negative with none.</summary>
    public float RestartRoundTime => _restartRoundTime;

    /// <summary>Whether the next round resets the map (stock <c>m_bForceMapReset</c>).</summary>
    public bool ForceMapReset => _forceMapReset;

    /// <summary>Stock's <c>IsWaitingForTeams</c>: the ready restart is armed.</summary>
    public bool AwaitingReadyRestart => _awaitingReadyRestart;

    /// <summary>Stock's <c>IsTeamReady</c>: the team has said the signal since the arm.</summary>
    public bool TeamIsReady(TFTeam team) => _teamReady[(int)team];

    /// <summary>The map clock's start (stock <c>m_flMapResetTime</c>).</summary>
    public float MapResetTime => _mapResetTime;

    /// <summary>When this round started (stock <c>m_flRoundStartTime</c>).</summary>
    public float RoundStartTime => _roundStartTime;

    /// <summary>When the last round was won (stock <c>m_flLastTeamWin</c>); negative before one.</summary>
    public float LastTeamWin => _lastTeamWin;

    /// <summary>
    /// Stock's <c>m_bAllowStalemateAtTimelimit</c> (the rules proxy's
    /// <c>SetStalemateOnTimelimit</c> input): the server time limit ends a
    /// running round. Per machine reset, off.
    /// </summary>
    public bool AllowStalemateAtTimelimit { get; set; }

    /// <summary>The proxy input's route in (stock's setter, teamplayroundbased_gamerules.cpp:374).</summary>
    public void SetStalemateOnTimelimit(bool stalemate) => AllowStalemateAtTimelimit = stalemate;

    //-------------------------------------------------------------------------
    // IRoundFlow: what the core's rules ask.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A won round scores nothing more: kills traded over the bonus time and
    /// the intermission must not reach the core's scoreboard.
    /// </summary>
    public bool RoundOver => _state is RoundState.TeamWin or RoundState.GameOver;

    /// <summary>
    /// A fresh machine (the level's init, and a mode switch's flush): what
    /// stock's constructor did, since stock built a new rules object per map.
    /// </summary>
    public void ResetRound()
    {
        Initialise();
        _balance.Reset();
    }

    public void OnPlayerSpawned(EntityHandle player, float now) => _balance.NoteSeen(player, now);

    public void OnPlayerKilled(PlayerDeathArgs death) { }

    /// <summary>
    /// Stock's <c>Think</c>: the state's own think, then once a second the
    /// win limit and round limit (outside a win, a stalemate and the game's
    /// end), the restart convars and the waiting-for-players period.
    /// </summary>
    public void Think(float now)
    {
        // g_fGameOver: the intermission is the core's, and it changes the level.
        if (_state == RoundState.GameOver)
            return;

        // CTFGameRules::Think's periodic CheckCapsPerRound, before the base
        // think: a team's flag captures win the round (the mode's answer).
        if (Now > _nextPeriodicThink
            && _state is not (RoundState.Bonus or RoundState.TeamWin or RoundState.GameOver)
            && !_inWaitingForPlayers
            && (Mode?.CheckCapsPerRound() ?? false))
        {
            return;
        }

        StateThink();

        if (Now > _nextPeriodicThink)
        {
            if (_state is not (RoundState.TeamWin or RoundState.Stalemate or RoundState.GameOver))
            {
                if (CheckWinLimit())
                    return;

                if (CheckMaxRounds())
                    return;
            }

            CheckRestartRound();
            CheckWaitingForPlayers();

            _nextPeriodicThink = Now + 1.0f;
        }
    }

    private void Initialise()
    {
        for (int i = 0; i < Teams; i++)
        {
            _nextRespawnWave[i] = 0f;
            _teamRespawnWaveTimes[i] = -1f;
            _originalTeamRespawnWaveTime[i] = -1f;
            _teamReady[i] = false;
        }

        _inOvertime = false;
        _inSetup = false;
        _awaitingReadyRestart = false;
        _winningTeam = TFTeam.Unassigned;
        _winReason = WinReason.None;
        _inWaitingForPlayers = false;
        _restartRoundTime = -1f;
        _roundsPlayed = 0;
        _forceMapReset = true;
        _switchTeams = false;
        _prevRoundWasWaitingForPlayers = false;
        _restartAfterWin = false;
        _changelevelAfterStalemate = false;
        _roundStartTime = 0f;
        _waitingForPlayersEnds = -1f;
        _stalemateEnds = -1f;
        _stalemateStartTime = -1f;
        _lastTeamWin = -1f;
        _lastCapPointChanged = 0;
        _prevRoundState = -1;
        _currentRoundState = -1;
        _currentMiniRoundMask = 0;
        _gameTeamWins[0] = _gameTeamWins[1] = 0;
        _history.Reset();
        _stateTransitionTime = 0f;
        _nextBalanceTeamsTime = 0f;
        AllowStalemateAtTimelimit = false;

        // The constructor's State_Transition( GR_STATE_PREGAME ), then stock's
        // three reset flags to true (:463-465), ResetMapTime and ResetScores
        // (:467-468).
        _state = RoundState.Init;
        Transition(RoundState.Pregame);
        _resetTeamScores = _resetPlayerScores = _resetRoundsPlayed = true;
        _mapResetTime = Now;
        ResetScores();
    }

    //-------------------------------------------------------------------------
    // The machine.
    //-------------------------------------------------------------------------

    private void Transition(RoundState next)
    {
        RoundState from = _state;
        Leave(from);
        Enter(next, from);
    }

    /// <summary>State_Enter without a Leave (stock's GoToIntermission arm).</summary>
    private void Enter(RoundState next, RoundState from)
    {
        _state = next;
        _log.Msg($"[TF2/Rounds] {from} -> {next}\n");

        // The listeners hear the move before the state's entry runs, so a
        // state left at once (RESTART -> PREROUND) is heard in order.
        if (_notify)
            _listeners?.Each(nameof(ITeamRoundListener.OnStateChanged), l => l.OnStateChanged(from, next));

        switch (next)
        {
            case RoundState.Init: EnterInit(); break;
            case RoundState.Pregame: _nextPeriodicThink = Now + 0.1f; break;
            case RoundState.StartGame: _stateTransitionTime = Now; break;
            case RoundState.Preround: EnterPreround(); break;
            case RoundState.RoundRunning: EnterRoundRunning(); break;
            case RoundState.TeamWin: EnterTeamWin(); break;
            case RoundState.Stalemate: EnterStalemate(); break;
            case RoundState.Restart: EnterRestart(); break;
        }
    }

    private void Leave(RoundState state)
    {
        if (state == RoundState.Stalemate)
        {
            // State_Leave_STALEMATE (teamplayroundbased_gamerules.cpp:2055-2074):
            // the kits the stalemate disabled come back (SetupOnStalemateEnd),
            // the stalemate timer goes, the hidden HUD timer returns, and the
            // HUD hears the timer change (:2068-2072).
            _world.RestoreWorldHealthKits();
            _world.RemoveStalemateTimer();
            _stalemateEnds = -1f;
            _timers.RestoreActive();
            _announcer.UpdateTimer();
        }
    }

    private void StateThink()
    {
        switch (_state)
        {
            case RoundState.Init: Transition(RoundState.Pregame); break;
            case RoundState.Pregame: ThinkPregame(); break;
            case RoundState.StartGame: ThinkStartGame(); break;
            case RoundState.Preround: ThinkPreround(); break;
            case RoundState.RoundRunning: ThinkRoundRunning(); break;
            case RoundState.TeamWin: ThinkTeamWin(); break;
            case RoundState.Stalemate: ThinkStalemate(); break;
        }
    }

    private void EnterInit() => ResetMapTime();

    private void ThinkPregame()
    {
        CheckRespawnWaves();

        if (HavePlayers())
            Transition(RoundState.StartGame);
    }

    private void ThinkStartGame()
    {
        if (Now > _stateTransitionTime)
        {
            // Go into waiting for players, reset at the end of it.
            if (_settings.WaitingForPlayersTime > 0f)
                SetInWaitingForPlayers(true);

            Transition(RoundState.Preround);
        }
    }

    private void EnterPreround()
    {
        _balance.BalanceTeams(requireDead: false);
        _balance.StartBalancingAt = Now + 60.0f;

        RoundRespawn();

        _announcer.RoundStart(_forceMapReset);

        _stateTransitionTime = Now + 5f * _settings.EnableRoundWaitTime;
    }

    private void ThinkPreround()
    {
        if (Now > _stateTransitionTime)
            Transition(RoundState.RoundRunning);

        CheckRespawnWaves();
    }

    private void EnterRoundRunning()
    {
        // SetupOnRoundRunning: the mode's part (CTF: nothing; the CP masters'
        // RoundStart input is the objectives').
        Mode?.OnRoundRunning();

        _announcer.RoundActive();

        // State_Enter_RND_RUNNING's voice (teamplayroundbased_gamerules.cpp:1681-1684).
        if (!_inWaitingForPlayers)
            _announcer.StartRoundVoice();

        _prevRoundWasWaitingForPlayers = false;
        _nextBalanceTeamsTime = Now + 1.0f;
    }

    private void ThinkRoundRunning()
    {
        // If we don't find any active players, return to PREGAME.
        if (!HavePlayers())
        {
            Transition(RoundState.Pregame);
            return;
        }

        if (_nextBalanceTeamsTime < Now)
        {
            _balance.BalanceTeams(requireDead: true);
            _nextBalanceTeamsTime = Now + 1.0f;
        }

        CheckRespawnWaves();

        CheckReadyRestart();
        if (_state != RoundState.RoundRunning)
            return;

        // Coming up to the server time limit: force a stalemate (or a win)
        // now. Stock's convar arm spares a Valve map (the official map list,
        // tf_gamerules.cpp:264): "m_bAllowStalemateAtTimelimit ||
        // (mp_match_end_at_timelimit.GetBool() && !IsValveMap())"
        // (teamplayroundbased_gamerules.cpp:1799).
        if (_settings.TimeLimitMinutes > 0 && TimeLeft() <= 0
            && (AllowStalemateAtTimelimit || (_settings.MatchEndAtTimeLimit && !IsValveMap)))
        {
            int drawScore = -1;
            TFTeam winning = TFTeam.Unassigned;
            bool drawn = true;
            for (var team = TFTeam.Red; team <= TFTeam.Blue && drawn; team++)
            {
                int score = _scores.Score(team);
                if (score > drawScore)
                    winning = team;

                if (score != drawScore)
                {
                    if (drawScore == -1)
                        drawScore = score;
                    else
                        drawn = false;
                }
            }

            if (drawn)
            {
                if (Mode?.CanGoToStalemate() ?? true)
                {
                    _changelevelAfterStalemate = true;
                    SetStalemate(StalemateReason.ServerTimeLimit, _forceMapReset);
                }
                else
                {
                    SetOvertime(true);
                }
            }
            else
            {
                SetWinningTeam(winning, WinReason.TimeLimit, true, false, true);
            }
        }
    }

    private void EnterTeamWin()
    {
        // Forcing the map to reset means the end of a full round, not a mini-round.
        if (_forceMapReset)
            _roundsPlayed++;

        // State_Enter_TEAM_WIN's SendWinPanelInfo( IsGameOver() )
        // (tf_gamerules.cpp:1879, 13599).
        _winPanel?.Send(new TFWinPanelData(
            GameOver: IsGameOver,
            WinningTeam: _winningTeam,
            Reason: _winReason,
            RoundComplete: _forceMapReset || (_settings.TimeLimitMinutes > 0 && TimeLeft() <= 0),
            ScoringPerCapture: !(Mode?.ShouldScorePerRound() ?? true),
            UseAddScoreAnim: _useAddScoreAnim));

        _stateTransitionTime = Now + BonusRoundTime();
    }

    private void ThinkTeamWin()
    {
        if (Now <= _stateTransitionTime)
            return;

        // The win limit, max rounds, time limit and nextlevel, before the next round.
        bool done = CheckTimeLimit() || CheckWinLimit() || CheckMaxRounds() || CheckNextLevelCvar();
        if (!done)
        {
            // Stock's State_Transition_TEAM_WIN ends in GR_STATE_RESTART, not
            // straight into the pre-round: the win yields through RESTART,
            // which announces the round's end and then enters the pre-round.
            _restartAfterWin = true;
            Transition(RoundState.Restart);
        }
    }

    private void EnterStalemate()
    {
        // SetupOnStalemateStart (tf_gamerules.cpp:5492-5562), its non-arena
        // arm: the projectiles and buildings go first, then everyone respawns
        // (which re-kits each from the loadout -- under
        // mp_stalemate_meleeonly, that kit is the melee weapon alone) and the
        // world's health kits are disabled for the sudden death. The arena
        // arm waits on the mode (docs/plans/tf2/deferred.tsv).
        _world.RemoveProjectilesAndBuildings();
        _stalemateStartTime = Now;
        RespawnPlayers(force: true);
        _world.DisableWorldHealthKits();

        // Stop any timers, and bring up a new one (here: the end time alone).
        _timers.HideActive();

        // The previous stalemate's timer entity goes (the CHandle check, :2016-2020).
        _world.RemoveStalemateTimer();

        int limit = _settings.StalemateTimeLimit;
        _stalemateEnds = limit > 0 ? Now + limit : -1f;

        // mp_stalemate_timelimit > 0: a HUD timer for the sudden death, and
        // teamplay_update_timer (:2032-2048).
        if (limit > 0)
        {
            _world.SpawnStalemateTimer(limit);
            _announcer.UpdateTimer();
        }
    }

    private void ThinkStalemate()
    {
        if (!HavePlayers())
        {
            Transition(RoundState.Pregame);
            return;
        }

        TFTeam deadTeam = TFTeam.Unassigned;
        TFTeam aliveTeam = TFTeam.Unassigned;

        // If a team is fully killed, the other team has won.
        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            var players = _roster.Players(team);
            if (players.Count > 0 && players.Any(_roster.IsAlive))
                aliveTeam = team;
            else
                deadTeam = team;
        }

        if (deadTeam != TFTeam.Unassigned && aliveTeam != TFTeam.Unassigned)
        {
            // The live team has won. A no-reset stalemate hands the win to the
            // control-point master, which forces the caps and owns the win
            // (teamplayroundbased_gamerules.cpp:2233-2252); without one, or on
            // a forced map reset, the round wins outright.
            bool masterHandled = !_forceMapReset && _world.SetWinnerAndForceCaps(aliveTeam);
            if (!masterHandled)
                SetWinningTeam(aliveTeam, WinReason.OpponentsDead, _forceMapReset);
        }
        else if ((deadTeam != TFTeam.Unassigned && aliveTeam == TFTeam.Unassigned)
                 || (_stalemateEnds >= 0f && TimerMayExpire() && Now >= _stalemateEnds))
        {
            // Both teams are dead (or sudden death ran out). Pure stalemate.
            SetWinningTeam(TFTeam.Unassigned, WinReason.Stalemate, true, false);
        }
    }

    private void EnterRestart()
    {
        _announcer.RestartRound();

        _prevRoundWasWaitingForPlayers = _inWaitingForPlayers;
        SetInWaitingForPlayers(false);

        // Only a game restart (mp_restartgame, the waiting-for-players end)
        // clears the scores and the map time. The RESTART a win yields to
        // keeps both: CheckWinLimit must still see the winner's point, and
        // mp_maxrounds still counts the round just played.
        if (!_restartAfterWin)
        {
            ResetScores();
            ResetMapTime();
        }
        _restartAfterWin = false;

        Transition(RoundState.Preround);
    }

    /// <summary>Stock <c>GoToIntermission</c>: the core's end of the level, and GAME_OVER entered without a leave.</summary>
    private void GoToIntermission()
    {
        _end.GoToIntermission();
        Enter(RoundState.GameOver, _state);
    }

    //-------------------------------------------------------------------------
    // The round's start (RoundRespawn, CleanUpMap, SetupOnRoundStart).
    //-------------------------------------------------------------------------

    private void RoundRespawn()
    {
        _roundStartTime = Now;

        if (_forceMapReset || _prevRoundWasWaitingForPlayers)
        {
            int created = _world.ResetMap();
            _log.Msg($"[TF2/Rounds] map reset: {created} map entities created again\n");

            // Clear out the previously played rounds.
            _history.ClearPlayed();
            _lastCapPointChanged = 0;

            // mp_timelimit with seconds left and no timer of the map's own in
            // the HUD: the rules spawn their time-limit timer (RoundRespawn ->
            // CreateTimeLimitTimer, teamplayroundbased_gamerules.cpp:2812-2819),
            // whose bAllowStalemate guard — stalemate allowed, or the convar
            // on a map that is not a Valve map — is inside it (:2778-2780).
            if (_settings.TimeLimitMinutes > 0 && TimeLeft() > 0 && !_world.MapHasActiveTimer())
                CreateTimeLimitTimer();
        }

        // Reset our spawn times to the original values.
        for (int i = 0; i < Teams; i++)
        {
            if (_originalTeamRespawnWaveTime[i] >= 0f)
                _teamRespawnWaveTimes[i] = _originalTeamRespawnWaveTime[i];
        }

        // Setup before respawning players, so we can mess with spawnpoints.
        SetupOnRoundStart();

        // Do we need to switch the teams? (stock's RoundRespawn arm,
        // teamplayroundbased_gamerules.cpp:2844-2851. The round's
        // m_bSwitchedTeamsThisRound (:2845, :2848) is read only by the client's
        // team-goal HUD item (tf_hud_teamgoal.cpp:221), which is not ported.)
        if (ShouldSwitchTeams())
        {
            HandleSwitchTeams();
            SetSwitchTeams(false);
        }

        // Do we need to scramble the teams? (stock's RoundRespawn arm,
        // teamplayroundbased_gamerules.cpp:2853-2858, right after the switch.)
        if (ShouldScrambleTeams())
        {
            HandleScrambleTeams();
            SetScrambleTeams(false);
        }

        RespawnPlayers(force: true);
    }

    /// <summary>
    /// <c>CTFGameRules::SetupOnRoundStart</c>: overtime off, every entity's
    /// RoundSpawn then every entity's RoundActivate, then the mode's part.
    /// </summary>
    private void SetupOnRoundStart()
    {
        SetOvertime(false);

        _world.RoundSpawn();
        _world.RoundActivate();

        // The control points' team spawns (not playing mini-rounds; the
        // objectives decide that), then the mode's part.
        _world.SetupControlPointSpawns();

        Mode?.OnRoundStart();
    }

    /// <summary>
    /// <c>CTFGameRules::HandleSwitchTeams</c> (tf_gamerules.cpp:16476-16559):
    /// the projectiles and buildings go, every player crosses to the other
    /// team, the team scores swap with them, and everyone is told. Stock's PVE
    /// gate (:16478) has no PVE here; the <c>mp_humans_must_join_team</c> swap
    /// (:16484-16491) and the tournament team-name swap (:16526-16536) wait on
    /// convars this build does not declare; the <c>m_bTeamsSwitched</c> netvar
    /// (:16481) is read only by the competitive GC getters
    /// (tf_gamerules.cpp:3741, :3768, :16549), and the GC match-info arm
    /// (:16541-16558) only by the GC: none is ported.
    /// </summary>
    private void HandleSwitchTeams()
    {
        // Remove everyone's projectiles and objects (:16496).
        _world.RemoveProjectilesAndBuildings();

        // Everyone on a game team crosses (:16499-16513); both teams are read
        // before either moves, as stock's one index pass does.
        var reds = _roster.Players(TFTeam.Red).ToArray();
        var blues = _roster.Players(TFTeam.Blue).ToArray();
        foreach (var player in reds)
            _roster.ChangeTeam(player, TFTeam.Blue);
        foreach (var player in blues)
            _roster.ChangeTeam(player, TFTeam.Red);

        // Switch the team scores (:16516-16524).
        int red = _scores.Score(TFTeam.Red);
        int blue = _scores.Score(TFTeam.Blue);
        _scores.SetScore(TFTeam.Red, blue);
        _scores.SetScore(TFTeam.Blue, red);

        // UTIL_ClientPrintAll( HUD_PRINTTALK, "#TF_TeamsSwitched" ) (:16539).
        _announcer.TeamsSwitched();
    }

    /// <summary>
    /// <c>CTFGameRules::HandleScrambleTeams</c> (tf_gamerules.cpp:16122-16211):
    /// red and blue are collected and sorted by score, everyone is dropped to
    /// Spectator to clear the teams, then reassigned by auto-assign, and the
    /// two teams are brought closer by score with up to eight swap passes.
    ///
    /// <para>
    /// The duel exclusion (:16141-16145) and the player-resource score sums
    /// need the player resource: with no stats service the sort keeps stock's
    /// unsorted order (its <c>pPR</c>-null arm, :16165-16166 skips the swap
    /// pass too). The <c>mp_teams_unbalance_limit</c> drop around the swap
    /// (:16185, :16204) is stock's: our roster's <c>ChangeTeam</c> is a raw
    /// join that never consults it, so the temporary zero changes nothing.
    /// </para>
    /// </summary>
    private void HandleScrambleTeams()
    {
        var players = _roster.Players(TFTeam.Red).Concat(_roster.Players(TFTeam.Blue)).ToList();

        // Sort by player score, highest first (ScramblePlayersSort,
        // tf_gamerules.cpp:14133-14150, over CTFPlayerResource::GetTotalScore).
        if (_stats is { } stats)
            players = players.OrderByDescending(p => stats.Score(_roster.EntIndex(p))).ToList();

        // Everyone to Spectator to clear the teams, back to front so a
        // removal would not skip (there is none here; FOR_EACH_VEC_BACK, :16134).
        for (var i = players.Count - 1; i >= 0; i--)
            _roster.ChangeTeam(players[i], TFTeam.Spectator);

        // Assign by the quick method: whoever is not already on a game team
        // takes the auto-assign team (ForceChangeTeam( TF_TEAM_AUTOASSIGN ), :16159).
        foreach (var player in players)
        {
            if (_roster.TeamOf(player) >= TFTeam.Red)
                continue;
            _roster.ChangeTeam(player, AutoAssignTeam());
        }

        // The swap pass, only with more than two players and a score table
        // (the CTFPlayerResource-null arm skips it, :16165-16166).
        if (_stats is { } table && players.Count > 2)
        {
            var (red, blue) = TeamScoreSums(players, table);
            var delta = Math.Abs(red - blue);
            if (delta > 1)
            {
                for (var pass = 0; pass < 8 && SwapToBalanceTeams(players, ref delta, table); pass++)
                {
                }
            }
        }

        // scrambleteams_auto tracking (tf_gamerules.cpp:16209-16210).
        ResetTeamsRoundWinTracking();
    }

    /// <summary>
    /// <c>FindScoreDifferenceBetweenTeams</c> (tf_gamerules.cpp:16024-16048):
    /// the sums are read afresh, so this hands back the two teams' totals.
    /// </summary>
    private (int Red, int Blue) TeamScoreSums(List<EntityHandle> players, TF2.Server.Player.ITFPlayerStats stats)
    {
        int red = 0, blue = 0;
        foreach (var player in players)
        {
            if (_roster.TeamOf(player) == TFTeam.Red)
                red += stats.Score(_roster.EntIndex(player));
            else
                blue += stats.Score(_roster.EntIndex(player));
        }
        return (red, blue);
    }

    /// <summary>
    /// <c>FindAndSwapPlayersToBalanceTeams</c> (tf_gamerules.cpp:16053-16117):
    /// the first red/blue pair whose swap strictly reduces the score delta
    /// swaps and reports the new delta; the first such pair, as stock returns
    /// on the first find.
    /// </summary>
    private bool SwapToBalanceTeams(List<EntityHandle> players, ref int delta,
                                    TF2.Server.Player.ITFPlayerStats stats)
    {
        var (redScore, blueScore) = TeamScoreSums(players, stats);

        foreach (var red in players)
        {
            if (_roster.TeamOf(red) != TFTeam.Red)
                continue;

            foreach (var blue in players)
            {
                if (_roster.TeamOf(blue) != TFTeam.Blue || red == blue)
                    continue;

                int redPlayerScore = stats.Score(_roster.EntIndex(red));
                int bluePlayerScore = stats.Score(_roster.EntIndex(blue));

                int playerDiff = Math.Abs(redPlayerScore - bluePlayerScore);
                if (playerDiff == 0)
                    continue;

                int newRed = redScore, newBlue = blueScore;
                if (redPlayerScore > bluePlayerScore)
                {
                    newRed -= playerDiff;
                    newBlue += playerDiff;
                }
                else
                {
                    newRed += playerDiff;
                    newBlue -= playerDiff;
                }

                if (Math.Abs(newRed - newBlue) < delta)
                {
                    _roster.ChangeTeam(red, TFTeam.Blue);
                    _roster.ChangeTeam(blue, TFTeam.Red);
                    var (newRedSum, newBlueSum) = TeamScoreSums(players, stats);
                    delta = Math.Abs(newRedSum - newBlueSum);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The auto-assign team (stock's <c>TF_TEAM_AUTOASSIGN</c> resolving
    /// through <c>CTFPlayer::GetAutoTeam</c>): the team with fewer players,
    /// else the attackers on an attack/defend map, else a coin — the same arms
    /// <see cref="TFTeams"/> answers, taken directly because that service
    /// answers the round machine this one is. Its <c>preferred</c> arm has no
    /// twin here: the scramble calls the auto-assign with no preference.
    /// </summary>
    private TFTeam AutoAssignTeam()
    {
        int blue = _roster.Players(TFTeam.Blue).Count;
        int red = _roster.Players(TFTeam.Red).Count;

        if (blue < red)
            return TFTeam.Blue;
        if (red < blue)
            return TFTeam.Red;
        if (Mode is { EvenTeamsJoinAttackers: true })
            return TFTeam.Blue;

        return _random.Next(0, 2) == 1 ? TFTeam.Red : TFTeam.Blue;
    }

    //-------------------------------------------------------------------------
    // The ends of a round (ITeamRoundControl).
    //-------------------------------------------------------------------------

    public void SetWinningTeam(TFTeam team, WinReason reason, bool forceMapReset = true, bool switchTeams = false,
                               bool dontAddScore = false, bool final = false)
    {
        if (team != TFTeam.Unassigned && team is not (TFTeam.Red or TFTeam.Blue))
        {
            _log.Warning($"[TF2/Rounds] SetWinningTeam() called with invalid team {team}\n");
            return;
        }

        // Are we already in this state?
        if (_state == RoundState.TeamWin)
            return;

        if (Mode is { } mode && !mode.AllowWin(team, reason))
        {
            _log.Msg($"[TF2/Rounds] the mode refused the win ({team}, {reason})\n");
            return;
        }

        _forceMapReset = forceMapReset;
        SetSwitchTeams(switchTeams); // teamplayroundbased_gamerules.cpp:2346

        // A forced reset ends a full round: the overlay state goes back to
        // none with it (stock InternalHandleTeamWin, tf_gamerules.cpp:15932-15937).
        if (forceMapReset)
        {
            _prevRoundState = -1;
            _currentRoundState = -1;
            _currentMiniRoundMask = 0;
        }

        _winningTeam = team;
        _winReason = reason;

        // Only reward the team if they have won the map and we're going to do
        // a full reset, or the time has run out and we're changing maps.
        // m_bUseAddScoreAnim (tf_gamerules.cpp:2359-2360): set exactly when
        // this win added the score, which the win panel's prev-score arm reads.
        bool reward = (forceMapReset || (_settings.TimeLimitMinutes > 0 && TimeLeft() <= 0)) && !dontAddScore;
        _useAddScoreAnim = reward && team != TFTeam.Unassigned && (Mode?.ShouldScorePerRound() ?? true);
        if (_useAddScoreAnim)
            _scores.AddScore(team, RoundWinScore);

        // A sudden-death win: we were in stalemate, then a team won it.
        bool wasSuddenDeath = _state == RoundState.Stalemate && team >= TFTeam.Red;

        Transition(RoundState.TeamWin);

        // this needs to be AFTER we add score above (for TF)
        // (teamplayroundbased_gamerules.cpp:2371-2372).
        _announcer.WinSong(team);

        _lastTeamWin = Now;

        _announcer.RoundWin(team, reason, forceMapReset, Now - _roundStartTime, wasSuddenDeath);
        _log.Msg($"[TF2/Rounds] round won by {team} ({reason}); scores red {_scores.Score(TFTeam.Red)} " +
                 $"blue {_scores.Score(TFTeam.Blue)}\n");

        // CTFGameRules::SetWinningTeam's crit boost lasts GetBonusRoundTime( bFinal ) + 1.
        float bonus = BonusRoundTime() + 1;
        _listeners?.Each(nameof(ITeamRoundListener.OnRoundWon), l => l.OnRoundWon(team, reason, bonus));

        // Auto scramble teams? The base win's tail, which the TF override
        // reaches after its own body (tf_gamerules.cpp:9687).
        CheckAutoScramble();
    }

    public void SetStalemate(StalemateReason reason, bool forceMapReset = true, bool switchTeams = false)
    {
        if (!_settings.StalemateEnable)
        {
            SetWinningTeam(TFTeam.Unassigned, WinReason.Stalemate, forceMapReset, switchTeams);
            return;
        }

        if (_state == RoundState.Stalemate)
            return;

        _forceMapReset = forceMapReset;
        _winningTeam = TFTeam.Unassigned;

        // PlaySuddenDeathSong before the transition (teamplayroundbased_gamerules.cpp:2535-2537).
        _announcer.SuddenDeathSong();

        Transition(RoundState.Stalemate);

        _announcer.RoundStalemate(reason);
    }

    /// <summary>
    /// Stock's <c>SetSwitchTeams</c> (teamplay_gamerules.h:94). A win arms it
    /// (<c>SetWinningTeam</c>'s <c>switchTeams</c>,
    /// teamplayroundbased_gamerules.cpp:2346); <c>mp_switchteams</c> will too.
    /// </summary>
    public void SetSwitchTeams(bool switchTeams) => _switchTeams = switchTeams;

    /// <summary>
    /// Stock's <c>ShouldSwitchTeams</c> (teamplay_gamerules.h:95, the TF
    /// override at tf_gamerules.cpp:15106-15111). Stock's race shape at
    /// tf_gamerules.cpp:2775 gates the switch on <c>!HasMultipleTrains</c>:
    /// on a multiple-train map each team races its own cart, so the armed
    /// switch never crosses anyone. A machine with no mode answers the base.
    /// </summary>
    public bool ShouldSwitchTeams() => !(Mode?.HasMultipleTrains ?? false) && _switchTeams;

    /// <summary>
    /// Stock's <c>SetScrambleTeams</c> (teamplay_gamerules.h:99). The
    /// <c>mp_scrambleteams</c> command arms it
    /// (teamplayroundbased_gamerules.cpp:305); the win-time auto block
    /// (:2497-2499) will too, behind its dedicated-server gate (:2410-2414).
    /// </summary>
    public void SetScrambleTeams(bool scrambleTeams) => _scrambleTeams = scrambleTeams;

    /// <summary>
    /// Stock's <c>ShouldScrambleTeams</c> (teamplay_gamerules.h:100, and the
    /// <c>CTFGameRules</c> override at tf_gamerules.cpp:15118-15127, whose
    /// PVE and competitive gates have neither mode here).
    /// </summary>
    public bool ShouldScrambleTeams() => _scrambleTeams;

    /// <summary>
    /// The auto scramble's arm, the tail of stock's <c>SetWinningTeam</c>
    /// (teamplayroundbased_gamerules.cpp:2404-2509): on a full-reset win under
    /// <c>mp_scrambleteams_auto</c>, the per-team win counters decide whether
    /// the next round restart scrambles — unless a nearer map change, the
    /// round limits or a listen server make it pointless.
    /// </summary>
    private void CheckAutoScramble()
    {
        if (!_forceMapReset || !_settings.ScrambleAuto)
            return;

        // Arena and the PVE modes skip it (:2407): the game types of stock's
        // IsInArenaMode (tf_gamerules.cpp:19210-19213) and, through the
        // ShouldSkipAutoScramble override (tf_gamerules.h:473-476), of
        // IsPVEModeActive (tf_gamerules.cpp:2106-2117); tournament is the
        // convar's own arm.
        if (Mode is { } mode && mode.GameType is TFGameType.Arena or TFGameType.Mvm)
            return;

        if (_settings.Tournament)
            return;

        // Don't bother on a listen server - usually not desirable (:2410-2414).
        if (!_host.IsDedicatedServer)
            return;

        // Skip if we have a nextlevel set (:2416-2418).
        if (_settings.NextLevel.Length != 0)
            return;

        // Look for impending level change (:2440-2442).
        if (((_settings.TimeLimitMinutes > 0 && CanChangelevelBecauseOfTimeLimit()) || _changelevelAfterStalemate)
            && TimeLeft() <= 300)
            return;

        if (_settings.WinLimit > 0 || _settings.MaxRounds > 0)
        {
            // One round from the limit (:2444-2451).
            if (_settings.MaxRounds - RoundsPlayed == 1)
                return;

            // One win from the limit, per game team (:2452-2461): the teams'
            // scores against mp_winlimit, which stands at 0 when only
            // mp_maxrounds is set and no score is -1, so the arm passes.
            if (_settings.WinLimit - _scores.Score(TFTeam.Red) == 1
                || _settings.WinLimit - _scores.Score(TFTeam.Blue) == 1)
                return;
        }

        // Increment win counters (:2463-2473): stock's Find-and-increment,
        // whose invalid-team arm asserts and returns.
        if (_winningTeam is not (TFTeam.Red or TFTeam.Blue))
            return;

        _gameTeamWins[_winningTeam == TFTeam.Red ? 0 : 1]++;

        // Did we hit our win delta? (:2475-2500) The alert is the TF arm: the
        // teamplay_alert event whose #game_scramble_onrestart print the other
        // builds have is taken away here (:2480-2494).
        if (Math.Abs(_gameTeamWins[1] - _gameTeamWins[0]) >= _settings.ScrambleAutoWinDifference)
        {
            _announcer.ScrambleTeamsAlert();
            _log.Msg("[TF2/Rounds] World triggered \"ScrambleTeams_Auto\"\n");

            SetScrambleTeams(true);
            ShouldResetScores(true, false);
            ShouldResetRoundsPlayed(false);
        }

        // If we switch teams after this win, swap scores (:2502-2508).
        if (ShouldSwitchTeams())
            (_gameTeamWins[0], _gameTeamWins[1]) = (_gameTeamWins[1], _gameTeamWins[0]);
    }

    /// <summary>
    /// <c>CTFGameRules::CanChangelevelBecauseOfTimeLimit</c>
    /// (tf_gamerules.cpp:4109-4126): mp_timelimit may not change the map under
    /// a mini-round plan this win keeps — the base answer is yes, and only a
    /// master playing its rounds until all are done says no.
    /// </summary>
    private bool CanChangelevelBecauseOfTimeLimit()
    {
        if (_forceMapReset || _objectives is not { } objectives)
            return true;

        var master = objectives.Master;
        if (master is not { } known || !known.PlayingMiniRounds || !known.ShouldPlayAllControlPointRounds)
            return true;

        return known.NumPlayableControlPointRounds() == 0;
    }

    /// <summary>
    /// Stock's <c>ResetTeamsRoundWinTracking</c>
    /// (teamplayroundbased_gamerules.cpp:3773-3780), which
    /// <c>HandleScrambleTeams</c> calls after every redraw
    /// (tf_gamerules.cpp:16209-16210): the counters the delta reads start
    /// over once the scramble they armed has run.
    /// </summary>
    private void ResetTeamsRoundWinTracking() => _gameTeamWins[0] = _gameTeamWins[1] = 0;

    /// <summary>
    /// Stock's <c>ShouldResetScores</c> (teamplayroundbased_gamerules.h:364),
    /// the two commands' lever on the next <see cref="ResetScores"/> arms.
    /// </summary>
    public void ShouldResetScores(bool teamScores, bool playerScores)
    {
        _resetTeamScores = teamScores;
        _resetPlayerScores = playerScores;
    }

    /// <summary>Stock's <c>ShouldResetRoundsPlayed</c> (<c>:365</c>).</summary>
    public void ShouldResetRoundsPlayed(bool roundsPlayed) => _resetRoundsPlayed = roundsPlayed;

    //-------------------------------------------------------------------------
    // The timer host.
    //-------------------------------------------------------------------------

    public void SetSetup(bool setup) => _inSetup = setup;

    public void SetOvertime(bool overtime)
    {
        if (_inOvertime == overtime)
            return;

        if (overtime)
            _log.Msg("[TF2/Rounds] World triggered \"Round_Overtime\"\n");

        _inOvertime = overtime;
    }

    /// <summary>
    /// Stock's chain: the train watchers and the control points (the
    /// <see cref="ITimerExpiryVeto"/> services), then the mode
    /// (<c>CTFGameRules::TimerMayExpire</c>'s flag arm).
    /// </summary>
    public bool TimerMayExpire()
    {
        if (_vetoes is not null)
        {
            foreach (var veto in _vetoes.All)
            {
                try
                {
                    if (!veto.TimerMayExpire())
                        return false;
                }
                catch (Exception e)
                {
                    _log.Warning($"[TF2/Rounds] timer veto {veto.GetType().FullName} threw: {e.Message}\n");
                }
            }
        }

        return Mode?.TimerMayExpire() ?? true;
    }

    //-------------------------------------------------------------------------
    // Waiting for players, restarts, the map's end.
    //-------------------------------------------------------------------------

    private void SetInWaitingForPlayers(bool waiting)
    {
        if (_inWaitingForPlayers == waiting)
            return;

        _inWaitingForPlayers = waiting;

        if (waiting)
        {
            _waitingForPlayersEnds = Now + _settings.WaitingForPlayersTime;
            _log.Msg($"[TF2/Rounds] waiting for players: {_settings.WaitingForPlayersTime:F0}s\n");
        }
        else
        {
            _waitingForPlayersEnds = -1f;

            // Stock's `if ( m_hWaitingForPlayersTimer ) UTIL_Remove( ... )` (:866-869).
            _world.RemoveWaitingTimer();
            _timers.RestoreActive();
        }
    }

    private void CheckWaitingForPlayers()
    {
        if (_settings.WaitingForPlayersRestart)
        {
            if (_inWaitingForPlayers)
            {
                _waitingForPlayersEnds = Now + _settings.WaitingForPlayersTime;

                // The wait restarts: the timer it already has is retuned (:933-938).
                _world.RetuneWaitingTimer(_waitingForPlayersEnds - Now);
            }
            else
                SetInWaitingForPlayers(true);

            _settings.WaitingForPlayersRestart = false;
        }

        if (_settings.WaitingForPlayersCancel)
        {
            // Stock (:955-973): resume the hidden HUD timer BEFORE the clear
            // drops its handle -- the restore inside SetInWaitingForPlayers
            // forgets it (:957-960).
            _timers.ResumeHiddenIfRunning();

            SetInWaitingForPlayers(false);
            _settings.WaitingForPlayersCancel = false;
        }

        if (!_inWaitingForPlayers)
            return;

        // Only exit waiting for players if the time is up and we are not in a
        // round restart countdown already; until then the HUD timer runs
        // (stock's else arm, :1005-1023, created once and kept -- the creation
        // hides the previous HUD timers before its own shows).
        if (Now > _waitingForPlayersEnds && _restartRoundTime < 0f)
            _restartRoundTime = Now;   // reset asap
        else
            _world.EnsureWaitingTimer(_waitingForPlayersEnds - Now);
    }

    private void CheckRestartRound()
    {
        // The clan ready restart's arm (stock's head, teamplayroundbased_gamerules.cpp:1032-1062):
        // mp_clan_readyrestart arms the wait, clears every game team's ready,
        // tells everyone the rules and fires teamplay_ready_restart. The
        // convar is spent at once, and any restart countdown already running
        // is cancelled. Stock's length guard rewrites a local nothing reads
        // again -- the chat arm re-reads the convar -- so it is not carried.
        if (_settings.ReadyRestart && !_settings.Tournament)
        {
            _awaitingReadyRestart = true;

            for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
                _teamReady[(int)team] = false;

            _announcer.ClanReadyRules(_settings.ReadySignal);
            _announcer.ReadyRestart();

            _settings.ReadyRestart = false;

            // Cancel any restart round in progress (:1060-1061).
            _restartRoundTime = -1f;
        }

        // Restart the game if specified by the server.
        int delay = _settings.RestartRound;
        bool now = _settings.RestartGameImmediate;
        if (delay == 0 && !now)
            delay = _settings.RestartGame;

        if (delay <= 0 && !now)
            return;

        delay = Math.Min(delay, MaxRestartDelay);

        _forceMapReset = _settings.RestartGame > 0 || now;

        if (now)
            delay = 0;

        _restartRoundTime = Now + delay;

        // Let the players know (stock's countdown, teamplayroundbased_gamerules.cpp:1106-1152):
        // the teamplay_round_restart_seconds event fires wherever tournament
        // mode stands, and only the prints wait outside it. A game restart
        // counts down as the switch when one is armed; the scramble takes the
        // alert instead of the print (stock's TF arm nulls the format,
        // :1128-1137); a round restart takes the round formats.
        string? format = null;
        bool alertScramble = false;
        if (!_settings.Tournament)
        {
            if (_settings.RestartGame > 0)
            {
                if (ShouldSwitchTeams())
                    format = delay > 1 ? TFMessages.GameSwitchInSecs : TFMessages.GameSwitchInSec;
                else if (ShouldScrambleTeams())
                    alertScramble = true;
            }
            else if (_settings.RestartRound > 0)
            {
                format = delay > 1 ? TFMessages.RoundRestartInSecs : TFMessages.RoundRestartInSec;
            }
        }

        // Stock's order: the seconds event (:1106-1111), then the alert
        // (:1129-1134), then the print (:1145-1151) — and the scramble's
        // print is null, so its alert stands last.
        _announcer.RestartSeconds(delay, format);
        if (alertScramble)
            _announcer.ScrambleTeamsAlert();
        _log.Msg($"[TF2/Rounds] restart in {delay}s{(_forceMapReset ? " (full reset)" : "")}\n");

        _settings.RestartRound = 0;
        _settings.RestartGame = 0;
        _settings.RestartGameImmediate = false;

        // A restart begun cancels any ready restart still waiting (:1158-1159).
        _awaitingReadyRestart = false;
    }

    private void CheckReadyRestart()
    {
        if (_restartRoundTime > 0f && _restartRoundTime <= Now)
        {
            _restartRoundTime = -1f;
            Transition(RoundState.Restart);
        }

        // The ready restart's check (stock's tail, :1735-1762): every game
        // team ready restarts the game, five seconds out, and disarms. Stock
        // withholds this from TF's player-ready mode (MvM, competitive,
        // tournament readymode); none of those is ported, so the flag alone
        // gates. The ask also re-arms the three reset flags — the ready
        // restart's game restart is a full reset (:1759-1760).
        if (!_awaitingReadyRestart)
            return;

        bool teamNotReady = false;
        for (var team = TFTeam.Red; team <= TFTeam.Blue && !teamNotReady; team++)
            teamNotReady = !_teamReady[(int)team];

        if (!teamNotReady)
        {
            _settings.RestartGame = RestartOnReady;
            _awaitingReadyRestart = false;

            ShouldResetScores(true, true);
            ShouldResetRoundsPlayed(true);
        }
    }

    /// <summary>
    /// Stock's <c>CheckChatForReadySignal</c>
    /// (teamplayroundbased_gamerules.cpp:786-806), the rules' half of the say
    /// path's hook (<see cref="IPlayerChatHook"/>): while the ready restart is
    /// armed, a game team's player saying the signal marks the team and fires
    /// <c>teamplay_team_ready</c>. Stock's own tournament gate stands, and the
    /// signal is the convar as it is.
    /// </summary>
    public void OnPlayerChat(PlayerChat chat)
    {
        if (!_awaitingReadyRestart || _settings.Tournament)
            return;

        if (!string.Equals(chat.Text, _settings.ReadySignal, StringComparison.Ordinal))
            return;

        var team = _roster.TeamOf(chat.Speaker);
        if (team is not (TFTeam.Red or TFTeam.Blue))
            return;

        _teamReady[(int)team] = true;
        _announcer.TeamReady(team);
    }

    private int TimeLeft()
    {
        float mapChange = _mapResetTime + _settings.TimeLimitMinutes * 60f;
        return Math.Max(0, (int)(mapChange - Now));
    }

    private bool CheckTimeLimit(bool allowEnd = true)
    {
        if (_settings.TimeLimitMinutes > 0 || _changelevelAfterStalemate)
        {
            // Under five minutes to go: switch now, so sudden death does not
            // start just after a new round.
            const int minTime = 5;
            bool switchDueToTime = _settings.TimeLimitMinutes > minTime && TimeLeft() < minTime * 60;

            if ((_settings.TimeLimitMinutes > 0 && TimeLeft() <= 0) || _changelevelAfterStalemate || switchDueToTime)
            {
                if (allowEnd)
                    EndGame("Reached Time Limit");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stock's <c>HandleTimeLimitChange</c>
    /// (teamplayroundbased_gamerules.cpp:2708-2723), through
    /// <c>MPTimeLimitCallback</c>'s clamp (multiplay_gamerules.cpp:74-77): a
    /// live <c>mp_timelimit</c> change retunes the map clock — <see
    /// cref="TimeLeft"/> reads the convar — and re-decides the HUD
    /// time-limit timer at once, instead of waiting for the next reset.
    /// </summary>
    private void HandleTimeLimitChange(int oldValue, int newValue)
    {
        // A negative limit is rewritten to 0; the write re-enters this with
        // the 0, as stock's mp_timelimit.SetValue(0) does.
        if (newValue < 0)
            _settings.TimeLimitMinutes = 0;

        // check that we have an active timer in the HUD and use mp_timelimit if we don't
        if (!_world.MapHasActiveTimer() && _settings.TimeLimitMinutes > 0 && TimeLeft() > 0)
            CreateTimeLimitTimer();
        else
            _world.RemoveTimeLimitTimer();
    }

    /// <summary>
    /// Stock's <c>CreateTimeLimitTimer</c> (<c>:2771-2796</c>): the arena and
    /// koth arms wait on the modes, and the stalemate guard — don't show the
    /// timelimit timer if we're not going to end the map when it runs out,
    /// "m_bAllowStalemateAtTimelimit || (mp_match_end_at_timelimit.GetBool()
    /// && !IsValveMap())" (<c>:2778</c>) — is inside it, so every asker gets
    /// the same guard.
    /// </summary>
    private void CreateTimeLimitTimer()
    {
        if (!(AllowStalemateAtTimelimit || (_settings.MatchEndAtTimeLimit && !IsValveMap)))
            return;

        _world.SpawnTimeLimitTimer(TimeLeft());
    }

    /// <summary>
    /// Stock's <c>WinlimitChanged</c> (teamplayroundbased_gamerules.cpp:188-195):
    /// the <c>winlimit_changed</c> event, no fields. The machine reads
    /// <c>mp_winlimit</c> live each second; the event is the HUD's.
    /// </summary>
    private void WinlimitChanged(int oldValue, int newValue)
        => _announcer.WinlimitChanged();

    private bool CheckWinLimit(bool allowEnd = true)
    {
        int limit = _settings.WinLimit;

        if (limit > 0)
        {
            for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
            {
                if (_scores.Score(team) >= limit)
                {
                    if (allowEnd)
                        EndGame("Reached Win Limit");
                    return true;
                }
            }
        }

        // "has one team gone far enough ahead of the other team to trigger
        // the win difference?" (tf_gamerules.cpp:9410-9440): asked whenever no
        // win limit matched, the lead's own floor aside.
        int difference = _settings.WinDifference;
        if (difference <= 0)
            return false;

        int blue = _scores.Score(TFTeam.Blue);
        int red = _scores.Score(TFTeam.Red);
        int minimum = _settings.WinDifferenceMin;

        if (blue - red >= difference && (minimum == 0 || blue >= minimum))
        {
            if (allowEnd)
                EndGame("Reached Win Difference Limit");
            return true;
        }

        if (red - blue >= difference && (minimum == 0 || red >= minimum))
        {
            if (allowEnd)
                EndGame("Reached Win Difference Limit");
            return true;
        }

        return false;
    }

    private bool CheckMaxRounds(bool allowEnd = true)
    {
        if (_settings.MaxRounds > 0 && _roundsPlayed >= _settings.MaxRounds)
        {
            if (allowEnd)
                EndGame("Reached Round Limit");
            return true;
        }

        return false;
    }

    private bool CheckNextLevelCvar(bool allowEnd = true)
    {
        if (_forceMapReset && _settings.NextLevel.Length > 0)
        {
            if (allowEnd)
                EndGame("NextLevel CVAR");
            return true;
        }

        return false;
    }

    /// <summary>Stock <c>IsGameOver</c>: any of the four ends, asked without ending.</summary>
    public bool IsGameOver => CheckTimeLimit(false) || CheckWinLimit(false) || CheckMaxRounds(false) || CheckNextLevelCvar(false);

    private void EndGame(string reason)
    {
        _announcer.GameOver(reason);
        _log.Msg($"[TF2/Rounds] game over: {reason}\n");
        GoToIntermission();
    }

    // Stock fires the event after m_flMapResetTime is named
    // (teamplayroundbased_gamerules.cpp:3289-3297), so TimeLeft reads it.
    private void ResetMapTime()
    {
        _mapResetTime = Now;
        _announcer.MapTimeRemaining(TimeLeft());
    }

    /// <summary>
    /// <c>CTeamplayRoundBasedRules::ResetScores</c>
    /// (teamplayroundbased_gamerules.cpp:3235-3282): the three arms stand
    /// unless <see cref="ShouldResetScores"/> or
    /// <see cref="ShouldResetRoundsPlayed"/> disarmed them before this reset,
    /// and every one of them comes back armed when the reset runs.
    /// </summary>
    private void ResetScores()
    {
        if (_resetTeamScores)
            _scores.ResetScores();

        if (_resetPlayerScores)
        {
            // Stock walks every connected player, whatever the team
            // (:3249-3260); the roster's four held teams are all a player can
            // stand in. CTFPlayer::ResetScores (tf_player.cpp:3284-3292)
            // clears the accumulated stats through CTF_GameStats — our
            // ITFPlayerStats' rows — and the TF build fires the game event
            // for it.
            if (_stats is { } stats)
            {
                foreach (TFTeam held in new[] { TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue })
                    foreach (var player in _roster.Players(held))
                        stats.ResetPlayer(player);
            }

            _announcer.ScorestatsAccumulatedReset();
        }

        if (_resetRoundsPlayed)
            _roundsPlayed = 0;

        // "assume we always want to reset the scores unless someone tells us
        // not to for the next reset" (:3276-3280).
        _resetTeamScores = _resetPlayerScores = _resetRoundsPlayed = true;
    }

    private int BonusRoundTime() => Math.Max(5, _settings.BonusRoundTime);

    private bool HavePlayers()
    {
        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            foreach (var player in _roster.Players(team))
            {
                if (_roster.IsReadyToPlay(player))
                    return true;
            }
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // What the control points ask beyond the contract (IObjectiveRoundRules).
    //-------------------------------------------------------------------------

    /// <summary><c>CTFGameRules::PointsMayAlwaysBeBlocked</c>: escort (Payload) answers true.</summary>
    public bool PointsMayAlwaysBeBlocked => Mode?.PointsMayAlwaysBeBlocked ?? false;

    /// <summary><c>CTeam::AddScore</c> for a point that scores per capture.</summary>
    public void AddTeamScore(TFTeam team, int points) => _scores.AddScore(team, points);

    /// <summary>Stock <c>SetLastCapPointChanged</c>: the point's index + 1, 0 for none.</summary>
    public void SetLastCapPointChanged(int index) => _lastCapPointChanged = index;

    /// <summary>Stock <c>GetLastCapPointChanged</c>.</summary>
    public int LastCapPointChanged => _lastCapPointChanged;

    public string RoundToPlayNext
    {
        get => _history.RoundToPlayNext;
        set => _history.RoundToPlayNext = value;
    }

    public string LastPlayedRound => _history.LastPlayedRound;

    public string FirstRoundPlayed
    {
        get => _history.FirstRoundPlayed;
        set => _history.FirstRoundPlayed = value;
    }

    public void AddPlayedRound(string round) => _history.AddPlayedRound(round);

    public bool IsPreviouslyPlayedRound(string round) => _history.IsPreviouslyPlayedRound(round);

    /// <summary>Stock <c>m_iPrevRoundState</c>: the round state before this one; -1 before any.</summary>
    public int PrevRoundState => _prevRoundState;

    /// <summary>Stock <c>m_iCurrentRoundState</c>: the blue-owned bit per master index; -1 before any.</summary>
    public int CurrentRoundState => _currentRoundState;

    /// <summary>Stock <c>m_iCurrentMiniRoundMask</c>: the two opposing points of the mini-round.</summary>
    public int CurrentMiniRoundMask => _currentMiniRoundMask;

    /// <summary>
    /// Stock <c>CTFGameRules::SetRoundOverlayDetails</c>
    /// (<c>tf_gamerules.cpp:16591-16620</c>): with a master playing mini-round
    /// rounds, the mask names the round's two opposing points, and the round
    /// state records which points blue owns over the master's table
    /// (<c>SetCurrentRoundStateBitString</c>, <c>:13896-13923</c> — the
    /// previous state moves back first). The base class's arm is a no-op
    /// (<c>teamplayroundbased_gamerules.h:362</c>).
    /// </summary>
    public void SetRoundOverlayDetails()
    {
        if (_objectives?.Master is not { } master || !master.PlayingMiniRounds
            || master.CurrentRound is not { } round)
            return;

        TeamControlPoint? red = round.GetPointOwnedBy((int)TFTeam.Red);
        TeamControlPoint? blue = round.GetPointOwnedBy((int)TFTeam.Blue);

        // do we have opposing points in this round?
        _currentMiniRoundMask = red is not null && blue is not null
            ? (1 << blue.PointIndex) | (1 << red.PointIndex)
            : 0;

        _prevRoundState = _currentRoundState;
        _currentRoundState = 0;
        for (int i = 0; i < master.NumPoints; i++)
        {
            if (master.ControlPoints[i].Owner == (int)TFTeam.Blue)
                _currentRoundState |= 1 << i;
        }
    }

    /// <summary>
    /// Stock <c>CTFGameRules::PlaySpecialCapSounds</c> (tf_gamerules.cpp:18794-18836):
    /// only the CP game type names capture sounds. A KOTH map plays
    /// <c>Hud.PointCaptured</c> to both teams beside the result; a per-capture
    /// score round plays the pair when the capture would not end the game —
    /// the capping team <c>Announcer.Success</c>, the other
    /// <c>Announcer.Failure</c>. The Halloween Lakeside arm has no scenario
    /// state in this port; the CTF and payload captures name no sound here.
    /// </summary>
    public void PlaySpecialCapSounds(TFTeam cappingTeam, TeamControlPoint point)
    {
        if (Mode is not { } mode || mode.GameType != TFGameType.Cp)
            return;

        bool play = mode.InKothMode;
        if (!play && _objectives?.Master is { } master && !master.ShouldScorePerCapture
            && !master.WouldNewCPOwnerWinGame(point, (int)cappingTeam))
            play = true;

        if (!play)
            return;

        // FIRST_GAME_TEAM .. GetNumberOfTeams - 1: the two playable teams.
        foreach (var team in new[] { TFTeam.Red, TFTeam.Blue })
        {
            if (mode.InKothMode)
                _announcer.Broadcast(team, "Hud.PointCaptured");

            _announcer.Broadcast(team, team == cappingTeam ? "Announcer.Success" : "Announcer.Failure");
        }
    }

    //-------------------------------------------------------------------------
    // Respawn waves (ITeamRespawnWaves).
    //-------------------------------------------------------------------------

    public float NextRespawnWave(TFTeam team) => Valid(team) ? _nextRespawnWave[(int)team] : 0f;

    public float NextRespawnWave(TFTeam team, EntityHandle player)
    {
        if (_state == RoundState.Stalemate)
            return 0f;

        // The soonest this player may spawn.
        float minSpawnTime = MinTimeWhenPlayerMaySpawn(player);

        // The next scheduled wave, and the length of one.
        float next = NextRespawnWave(team);
        float length = RespawnWaveMaxLength(team);
        if (length <= 0f)
            return next;

        // Keep adding one wave until we find one this player is eligible for.
        while (next < minSpawnTime)
            next += length;

        return next;
    }

    public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true)
    {
        if (!Valid(team))
            return 0f;

        if (_state != RoundState.RoundRunning)
            return 0f;

        if (_settings.DisableRespawnTimes)
            return 0f;

        float time = _teamRespawnWaveTimes[(int)team] >= 0f ? _teamRespawnWaveTimes[(int)team] : _settings.RespawnWaveTime;

        // For long respawn times, scale the time as the number of players drops.
        if (scaleWithNumPlayers && time > 5f)
            time = Math.Max(5f, time * RespawnTimeScalar(team));

        return Mode?.RespawnWaveTime(team, time) ?? time;
    }

    public void SetTeamRespawnWaveTime(TFTeam team, float seconds)
    {
        if (!Valid(team))
            return;

        seconds = Math.Max(0f, seconds);

        // Initialised to -1 so the first spawn time received for this team is the original.
        if (_originalTeamRespawnWaveTime[(int)team] < 0f)
            _originalTeamRespawnWaveTime[(int)team] = seconds;

        _teamRespawnWaveTimes[(int)team] = seconds;
    }

    public void AddTeamRespawnWaveTime(TFTeam team, float seconds)
    {
        if (!Valid(team))
            return;

        float current = _teamRespawnWaveTimes[(int)team];
        if (current < 0f)
            current = _settings.RespawnWaveTime;

        if (_originalTeamRespawnWaveTime[(int)team] < 0f)
            _originalTeamRespawnWaveTime[(int)team] = current;

        _teamRespawnWaveTimes[(int)team] = Math.Max(0f, current + seconds);
    }

    /// <summary>
    /// Stock <c>GetRespawnTimeScalar</c>: 16 players total, 8 per team is the
    /// optimum, and the wave shrinks to a quarter for a lone player.
    /// </summary>
    private float RespawnTimeScalar(TFTeam team)
    {
        const int optimalPlayers = 8;
        int players = _roster.Players(team).Count;
        return RemapValClamped(players, 1, optimalPlayers, 0.25f, 1.0f);
    }

    private float MinTimeWhenPlayerMaySpawn(EntityHandle player)
    {
        // a) one full UNSCALED wave for their team, b) death anim + freeze panel.
        float delay = DeathAnimLength + RespawnWaveMaxLength(_roster.TeamOf(player), scaleWithNumPlayers: false);
        return _roster.DeathTime(player) + delay;
    }

    private bool HasPassedMinRespawnTime(EntityHandle player) => Now > MinTimeWhenPlayerMaySpawn(player);

    private void CheckRespawnWaves()
    {
        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            float next = NextRespawnWave(team);
            if (next != 0f && next > Now)
                continue;

            RespawnPlayers(force: false, team);

            // Zero when there is no respawn time, to reduce networking.
            float length = RespawnWaveMaxLength(team);
            _nextRespawnWave[(int)team] = length != 0f ? Now + length : 0f;
        }

        // The arm CTFGameRules::CheckRespawnWaves adds over the base walk
        // (tf_gamerules.cpp:9485-9503): out of PREROUND, a dead player whose
        // respawn override has run out is forced back in at once — the wave
        // gate above never lets them out early, and the hold beside the
        // minimum respawn time keeps them out until its release moment.
        if (_state != RoundState.Preround && _overrides is not null)
        {
            for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
            {
                foreach (var player in _roster.Players(team))
                {
                    if (_roster.IsAlive(player)) // :9493
                        continue;

                    if (_overrides.Part<TF2.Server.Player.TFRespawnOverride>(player.EntryIndex) is { HasOverride: true } held
                        && Now > held.ReleaseAt) // :9498: curtime > GetDeathTime() + GetRespawnTimeOverride()
                        _roster.ForceRespawn(player); // :9500
                }
            }
        }
    }

    /// <summary>
    /// Stock <c>RespawnPlayers</c>: everyone ready to play (forced), or the
    /// dead of one team who have waited their minimum time.
    /// </summary>
    private void RespawnPlayers(bool force, TFTeam? only = null)
    {
        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            if (only is { } t && t != team)
                continue;

            foreach (var player in _roster.Players(team).ToArray())
            {
                // Players that haven't chosen a team/class can never spawn.
                if (!_roster.IsReadyToPlay(player))
                    continue;

                if (!force)
                {
                    if (_roster.IsAlive(player))
                        continue;

                    // Out of PREROUND, a player who hasn't been dead the
                    // minimum respawn time waits for the next wave.
                    if (_state != RoundState.Preround && only is not null && !HasPassedMinRespawnTime(player))
                        continue;

                    // IsReadyToSpawn's override arm (tf_player.cpp:3266) as the
                    // base RespawnPlayers' gate consults it (:2660): out of
                    // PREROUND, a player whose override has not run out is held
                    // back from the wave even past their minimum respawn time.
                    if (_state != RoundState.Preround
                        && _overrides?.Part<TF2.Server.Player.TFRespawnOverride>(player.EntryIndex) is { HasOverride: true } held
                        && Now < held.ReleaseAt)
                        continue;

                    // IsReadyToSpawn's menu arm, the last one the gate asks
                    // (tf_player.cpp:3243): a player whose class menu is open
                    // waits for the next wave — and, ShouldGainInstantSpawn's
                    // menu arm standing too (tf_player.cpp:3278), is granted
                    // the instant spawn their next class choice spends
                    // (:2661-2666, spent by tf_player.cpp:7032).
                    if (_state != RoundState.Preround
                        && _overrides?.Part<TF2.Server.Player.TFPlayerClass>(player.EntryIndex) is { MenuOpen: true } menu)
                    {
                        menu.AllowInstantSpawn = true;
                        continue;
                    }
                }

                _roster.ForceRespawn(player);
            }
        }
    }

    private static bool Valid(TFTeam team) => team is >= TFTeam.Unassigned and < TFTeam.Count;

    /// <summary>mathlib's <c>RemapValClamped</c>.</summary>
    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float t = Math.Clamp((val - a) / (b - a), 0f, 1f);
        return c + (d - c) * t;
    }
}
