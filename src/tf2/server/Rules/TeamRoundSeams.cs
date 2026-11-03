using SourceSharp.Abi;

using TF2.Server.Modes;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

// The seams the team-round machine (TeamRoundFlow) is built over. Each is what
// the machine ASKS of one neighbour, so the machine's facts hold a fake of
// each and the adapters (TeamRoundAdapters.cs) are the only code that names
// the core's concrete services. Other lanes register into the ones marked
// "for other lanes".

/// <summary>
/// The round convars the machine reads, live (a server.cfg change between
/// rounds is the next round's rule). The settable ones are stock's
/// "set it to 1 to do X" convars, which the machine reads and clears.
/// </summary>
public interface ITeamRoundSettings
{
    /// <summary><c>mp_respawnwavetime</c>.</summary>
    float RespawnWaveTime { get; }

    /// <summary><c>mp_disable_respawn_times</c>.</summary>
    bool DisableRespawnTimes { get; }

    /// <summary><c>mp_bonusroundtime</c>: seconds of TEAM_WIN before the next round.</summary>
    int BonusRoundTime { get; }

    /// <summary><c>mp_enableroundwaittime</c>: PREROUND lasts five times this.</summary>
    float EnableRoundWaitTime { get; }

    /// <summary><c>mp_stalemate_enable</c>.</summary>
    bool StalemateEnable { get; }

    /// <summary><c>mp_stalemate_timelimit</c>, seconds.</summary>
    int StalemateTimeLimit { get; }

    /// <summary><c>mp_waitingforplayers_time</c>, seconds.</summary>
    float WaitingForPlayersTime { get; }

    /// <summary><c>mp_waitingforplayers_cancel</c>, cleared once read.</summary>
    bool WaitingForPlayersCancel { get; set; }

    /// <summary><c>mp_waitingforplayers_restart</c>, cleared once read.</summary>
    bool WaitingForPlayersRestart { get; set; }

    /// <summary>
    /// <c>mp_timelimit</c> as stock reads it (<c>GetInt</c>), minutes. Writable
    /// for <c>MPTimeLimitCallback</c>'s clamp, which rewrites a negative to 0.
    /// </summary>
    int TimeLimitMinutes { get; set; }

    /// <summary><c>mp_winlimit</c>.</summary>
    int WinLimit { get; }

    /// <summary><c>mp_windifference</c>: the score lead that ends the map on its own.</summary>
    int WinDifference { get; }

    /// <summary><c>mp_windifference_min</c>: the score the leading team needs first (0 off).</summary>
    int WinDifferenceMin { get; }

    /// <summary><c>mp_maxrounds</c>.</summary>
    int MaxRounds { get; }

    /// <summary><c>mp_scrambleteams_auto</c>.</summary>
    bool ScrambleAuto { get; }

    /// <summary><c>mp_scrambleteams_auto_windifference</c>.</summary>
    int ScrambleAutoWinDifference { get; }

    /// <summary><c>nextlevel</c>.</summary>
    string NextLevel { get; }

    /// <summary><c>mp_restartround</c>, cleared once read.</summary>
    int RestartRound { get; set; }

    /// <summary><c>mp_restartgame</c>, cleared once read.</summary>
    int RestartGame { get; set; }

    /// <summary><c>mp_restartgame_immediate</c>, cleared once read.</summary>
    bool RestartGameImmediate { get; set; }

    /// <summary><c>mp_match_end_at_timelimit</c>.</summary>
    bool MatchEndAtTimeLimit { get; }

    /// <summary><c>mp_teams_unbalance_limit</c>.</summary>
    int TeamsUnbalanceLimit { get; }

    /// <summary><c>mp_spectators_restricted</c>.</summary>
    bool SpectatorsRestricted { get; }

    /// <summary><c>mp_autoteambalance</c> (0 off, 1 force, 2 ask volunteers).</summary>
    int AutoTeamBalance { get; }

    /// <summary><c>mp_forceautoteam</c>.</summary>
    bool ForceAutoTeam { get; }

    /// <summary><c>mp_tournament</c>.</summary>
    bool Tournament { get; }

    /// <summary><c>mp_clan_readyrestart</c>, cleared once read.</summary>
    bool ReadyRestart { get; set; }

    /// <summary><c>mp_clan_ready_signal</c>.</summary>
    string ReadySignal { get; }

    /// <summary>
    /// Stock's <c>MPTimeLimitCallback</c> (multiplay_gamerules.cpp:72-84): a
    /// handler told of every live <c>mp_timelimit</c> change with the (old,
    /// new) minutes, after the value holds. The disposable unsubscribes.
    /// </summary>
    IDisposable OnTimeLimitChanged(Action<int, int> handler);

    /// <summary>
    /// Stock's <c>WinlimitChanged</c> (teamplayroundbased_gamerules.cpp:188-195):
    /// a handler told of every live <c>mp_winlimit</c> change with the (old,
    /// new) limits, after the value holds. The disposable unsubscribes.
    /// </summary>
    IDisposable OnWinLimitChanged(Action<int, int> handler);
}

/// <summary>
/// The players as the machine sees them: who is on which team, alive or
/// dead since when, and the two things it does to them (respawn, move).
/// </summary>
public interface ITeamRoundRoster
{
    /// <summary>Every player on <paramref name="team"/>, in a stable order.</summary>
    IReadOnlyList<EntityHandle> Players(TFTeam team);

    /// <summary>The player's team.</summary>
    TFTeam TeamOf(EntityHandle player);

    bool IsAlive(EntityHandle player);

    /// <summary>When the player last died; negative while alive or never seen dead.</summary>
    float DeathTime(EntityHandle player);

    /// <summary>
    /// Stock's <c>IsReadyToPlay</c>: on a playing team, and (TF) a class
    /// chosen, which <see cref="ITeamRoundReadiness"/> answers when registered.
    /// </summary>
    bool IsReadyToPlay(EntityHandle player);

    /// <summary>Stock's <c>CBasePlayer::ForceRespawn</c>: alive again at a spawn point, now.</summary>
    void ForceRespawn(EntityHandle player);

    /// <summary>Stock's forced <c>ChangeTeam</c> (autobalance); false when nothing moved.</summary>
    bool ChangeTeam(EntityHandle player, TFTeam team);

    /// <summary>The player's name, for the balance announcement.</summary>
    string Name(EntityHandle player);

    /// <summary>The player's entity index, for the balance event.</summary>
    int EntIndex(EntityHandle player);
}

/// <summary>
/// FOR OTHER LANES (2.3): whether a player on a playing team has chosen a
/// class, the rest of stock's <c>CTFPlayer::IsReadyToPlay</c>. Optional and
/// single-owner: with none registered a player on a playing team is ready.
/// </summary>
public interface ITeamRoundReadiness
{
    bool HasChosenClass(EntityHandle player);
}

/// <summary>
/// What the machine tells everyone: the stock <c>teamplay_*</c> game events
/// and the <c>UTIL_ClientPrintAll</c> lines beside them.
/// </summary>
public interface IRoundAnnouncer
{
    /// <summary><c>teamplay_round_start</c> (<c>full_reset</c>).</summary>
    void RoundStart(bool fullReset);

    /// <summary><c>teamplay_round_active</c>.</summary>
    void RoundActive();

    /// <summary><c>teamplay_round_win</c>.</summary>
    void RoundWin(TFTeam team, WinReason reason, bool fullRound, float roundTime, bool wasSuddenDeath);

    /// <summary><c>teamplay_round_stalemate</c>.</summary>
    void RoundStalemate(StalemateReason reason);

    /// <summary><c>teamplay_restart_round</c>.</summary>
    void RestartRound();

    /// <summary><c>teamplay_round_restart_seconds</c>, and the centre/console print stock adds (null for none).</summary>
    void RestartSeconds(int seconds, string? format);

    /// <summary><c>teamplay_game_over</c> with its reason.</summary>
    void GameOver(string reason);

    /// <summary><c>#game_auto_team_balance_in</c> to everyone's chat.</summary>
    void BalanceWarning(int seconds);

    /// <summary><c>teamplay_teambalanced_player</c> and <c>#game_player_was_team_balanced</c>.</summary>
    void TeamBalanced(int entIndex, TFTeam team, string name);

    /// <summary>
    /// Stock's <c>PlayWinSong</c> (<c>teamplayroundbased_gamerules.cpp:3314-3340</c>):
    /// the win and lose songs, or the stalemate's when no team won.
    /// </summary>
    void WinSong(TFTeam team);

    /// <summary>Stock's <c>PlaySuddenDeathSong</c> (<c>:3345-3353</c>).</summary>
    void SuddenDeathSong();

    /// <summary>
    /// Stock's <c>BroadcastSound</c> (<c>teamplayroundbased_gamerules.cpp:3383-3394</c>):
    /// one <c>teamplay_broadcast_audio</c> event, the named team only.
    /// </summary>
    void Broadcast(TFTeam team, string sound);

    /// <summary>Stock's <c>PlayStartRoundVoice</c> (<c>:3303-3309</c>), each game team its own voice.</summary>
    void StartRoundVoice();

    /// <summary>
    /// <c>teamplay_map_time_remaining</c> with the seconds to the map change
    /// (<c>CTeamplayRoundBasedRules::ResetMapTime</c>,
    /// teamplayroundbased_gamerules.cpp:3292-3297).
    /// </summary>
    void MapTimeRemaining(int seconds);

    /// <summary><c>teamplay_update_timer</c>, no fields (a HUD timer changed under the HUD).</summary>
    void UpdateTimer();

    /// <summary><c>winlimit_changed</c>, no fields (stock's <c>WinlimitChanged</c>, <c>:188-195</c>).</summary>
    void WinlimitChanged();

    /// <summary>The <c>#clan_ready_rules</c> console and chat print, the ready restart's rules line.</summary>
    void ClanReadyRules(string signal);

    /// <summary><c>teamplay_ready_restart</c>, no fields (the ready restart armed).</summary>
    void ReadyRestart();

    /// <summary><c>teamplay_team_ready</c> with the team (<c>CheckChatForReadySignal</c>).</summary>
    void TeamReady(TFTeam team);

    /// <summary><c>#TF_TeamsSwitched</c> to everyone's chat
    /// (<c>CTFGameRules::HandleSwitchTeams</c>, tf_gamerules.cpp:16539).</summary>
    void TeamsSwitched();

    /// <summary>
    /// <c>teamplay_alert</c> with <c>alert_type = HUD_ALERT_SCRAMBLE_TEAMS</c>
    /// (0, tf_shareddefs.h:1783), fired where the base game's
    /// <c>#game_scramble_in_*</c> countdown print would have gone
    /// (teamplayroundbased_gamerules.cpp:1124-1138) — TF takes the print away
    /// and leaves only the alert.
    /// </summary>
    void ScrambleTeamsAlert();

    /// <summary>
    /// <c>scorestats_accumulated_reset</c>, no fields: the player arm of
    /// <c>CTeamplayRoundBasedRules::ResetScores</c> cleared the accumulated
    /// stats (<c>:3262-3268</c>).
    /// </summary>
    void ScorestatsAccumulatedReset();
}

/// <summary>The map as a round sees it: the map cleanup, the round inputs, the points' spawns.</summary>
public interface IRoundWorld
{
    /// <summary>
    /// Stock's <c>CleanUpMap</c> (<c>CTFGameRules</c>' override): what TF's
    /// rules do not keep is removed and the map's entities are created again.
    /// Returns how many map entities were created.
    /// </summary>
    int ResetMap();

    /// <summary>
    /// <c>CTFGameRules::SetupOnRoundStart</c>'s first walk: the <c>RoundSpawn</c>
    /// input to every live entity. Returns how many took it.
    /// </summary>
    int RoundSpawn();

    /// <summary>Its second walk, after every RoundSpawn: the <c>RoundActivate</c> input to every live entity.</summary>
    int RoundActivate();

    /// <summary>
    /// Stock's no-reset win arm (<c>CTeamplayRoundBasedRules::SetWinnerAndForceCaps</c>,
    /// teamplayroundbased_gamerules.cpp:2235-2248): the <c>SetWinnerAndForceCaps</c>
    /// input to every control-point master. True when a master took it.
    /// </summary>
    bool SetWinnerAndForceCaps(TFTeam team);

    /// <summary>
    /// <c>SetupOnRoundStart</c>'s spawn half: which points have team spawns,
    /// those spawns off, and the farthest owned points' on (the objectives'
    /// <c>IControlPointRules.SetupControlPointSpawns</c>).
    /// </summary>
    void SetupControlPointSpawns();

    /// <summary>
    /// Stock's <c>RemoveAllProjectilesAndBuildings</c> (<c>tf_gamerules.cpp:4098-4103</c>):
    /// every projectile in the air and every standing building gone, the
    /// buildings the way stock removes them when it does not explode them
    /// (<c>RemoveAllBuildings</c>, <c>:4045-4077</c>).
    /// </summary>
    void RemoveProjectilesAndBuildings();

    /// <summary>
    /// <c>SetupOnStalemateStart</c>'s kit half (<c>tf_gamerules.cpp:5503-5513</c>):
    /// every health kit in the world that is not disabled already is disabled,
    /// and remembered for <see cref="RestoreWorldHealthKits"/>.
    /// </summary>
    void DisableWorldHealthKits();

    /// <summary>
    /// <c>SetupOnStalemateEnd</c> (<c>tf_gamerules.cpp:5567-5579</c>): every kit
    /// the stalemate disabled, back on — a kit removed since is left alone.
    /// </summary>
    void RestoreWorldHealthKits();

    /// <summary>
    /// Stock's waiting-for-players timer
    /// (<c>teamplayroundbased_gamerules.cpp:1005-1023</c>): a
    /// <c>team_round_timer</c> created once, named
    /// <c>zz_teamplay_waiting_timer</c>, shown in the HUD and running for the
    /// seconds the wait has left. A live one is kept; true when this ask
    /// created one (stock's <c>if ( !m_hWaitingForPlayersTimer )</c> arm —
    /// the creation hides the HUD's previous timers before its own shows).
    /// </summary>
    bool EnsureWaitingTimer(float seconds);

    /// <summary>The restart arm's <c>SetTime</c> (<c>:933-938</c>), the timer it already has.</summary>
    void RetuneWaitingTimer(float seconds);

    /// <summary>The <c>SetInWaitingForPlayers(false)</c> removal (<c>:866-869</c>).</summary>
    void RemoveWaitingTimer();

    /// <summary>
    /// Stock's stalemate timer (<c>:2033-2042</c>): a <c>team_round_timer</c>
    /// shown in the HUD and running for <c>mp_stalemate_timelimit</c>.
    /// </summary>
    void SpawnStalemateTimer(int seconds);

    /// <summary>The <c>State_Leave_STALEMATE</c> removal (<c>:2059-2062</c>).</summary>
    void RemoveStalemateTimer();

    /// <summary>
    /// Stock's <c>CreateTimeLimitTimer</c> (<c>:2771-2796</c>): a
    /// <c>team_round_timer</c> named <c>zz_teamplay_timelimit_timer</c>,
    /// created once, shown in the HUD and running for the map's seconds left.
    /// </summary>
    void SpawnTimeLimitTimer(int seconds);

    /// <summary>
    /// Stock's <c>HandleTimeLimitChange</c> removal (<c>:2717-2721</c>): the
    /// rules' own time-limit timer gone, the map's own left alone.
    /// </summary>
    void RemoveTimeLimitTimer();

    /// <summary>
    /// Stock's <c>MapHasActiveTimer</c> (<c>:2752-2766</c>): a
    /// <c>team_round_timer</c> shows in the HUD that is not the time-limit one.
    /// </summary>
    bool MapHasActiveTimer();
}

/// <summary>The end of the map: the core's intermission (stock <c>GoToIntermission</c>).</summary>
public interface IGameEnd
{
    void GoToIntermission();
}

/// <summary>The active game mode, when it is a team-round mode.</summary>
public interface IActiveTeamRoundMode
{
    /// <summary>The active mode's team-round half, or null (no mode, or not a TF2 one).</summary>
    ITeamRoundMode? Current { get; }
}

/// <summary>
/// What the machine asks of the active mode: stock's <c>CTFGameRules</c>
/// overrides of <c>CTeamplayRoundBasedRules</c>'s virtuals, per mode. Every
/// member's stock default is what <c>TFModeBase</c> answers.
/// </summary>
public interface ITeamRoundMode
{
    /// <summary>Stock's <c>SetupOnRoundStart</c>, the mode's part (after the round inputs).</summary>
    void OnRoundStart();

    /// <summary>Stock's <c>SetupOnRoundRunning</c>, the mode's part.</summary>
    void OnRoundRunning();

    /// <summary>Stock's <c>TimerMayExpire</c>: false holds a round timer at zero (overtime).</summary>
    bool TimerMayExpire();

    /// <summary>Stock's <c>CanGoToStalemate</c>.</summary>
    bool CanGoToStalemate();

    /// <summary>
    /// Stock's <c>CTFGameRules::CheckCapsPerRound</c>, asked once a second
    /// outside the win, the bonus, the game's end and the wait for players:
    /// true when the mode ended the round (a team reached its flag captures).
    /// </summary>
    bool CheckCapsPerRound();

    /// <summary>Stock's <c>ShouldScorePerRound</c>: a round win adds a team point.</summary>
    bool ShouldScorePerRound();

    /// <summary>Whether this win may stand (a D10 hook's veto; stock has none).</summary>
    bool AllowWin(TFTeam team, WinReason reason);

    /// <summary>The team's respawn wave length after the mode's modifiers (stock <c>GetRespawnWaveMaxLength</c>'s TF arm).</summary>
    float RespawnWaveTime(TFTeam team, float seconds);

    /// <summary>
    /// <c>GetAutoTeam</c>'s even-teams rule: "AutoTeam should give new players
    /// to the attackers on A/D maps" — true sends them to Blue.
    /// </summary>
    bool EvenTeamsJoinAttackers { get; }

    /// <summary>Stock <c>PointsMayAlwaysBeBlocked</c>: TF's escort answers true.</summary>
    bool PointsMayAlwaysBeBlocked { get; }

    /// <summary>Stock's <c>HasMultipleTrains</c> (<c>m_bMultipleTrains</c>): escort answers it from
    /// a <c>tf_logic_multiple_escort</c> on the map (tf_gamerules.cpp:4350-4357); the round switch
    /// and the escort HUD read it.</summary>
    bool HasMultipleTrains { get; }

    /// <summary>Stock's <c>GetGameType</c>: the <c>ETFGameType</c> the mode plays.</summary>
    TFGameType GameType { get; }

    /// <summary>Stock's <c>IsInKothMode</c> (<c>m_bPlayingKoth</c>).</summary>
    bool InKothMode { get; }
}

/// <summary>
/// FOR OTHER LANES (2.4: control points; 2.6: train watchers): a service that
/// can hold a round timer at zero while its objective is contested (stock's
/// <c>CTFGameRules::TimerMayExpire</c> capping-team walk and
/// <c>CTeamTrainWatcher::TimerMayExpire</c>). A chain (<c>IHooks</c>): any
/// false holds the timer; implement <c>IOrderedHook</c> with an order no other
/// veto uses.
/// </summary>
public interface ITimerExpiryVeto
{
    bool TimerMayExpire();
}

/// <summary>
/// FOR OTHER LANES (2.3 crit boost on win, the HUD, objectives): told of every
/// state change and every round win, after the machine has moved. A chain
/// (<c>IHooks</c>); implement <c>IOrderedHook</c> with an order of its own.
/// </summary>
public interface ITeamRoundListener
{
    void OnStateChanged(RoundState from, RoundState to);

    /// <summary>
    /// A round was won (<see cref="TFTeam.Unassigned"/> for a stalemate win);
    /// <paramref name="bonusSeconds"/> is stock's <c>GetBonusRoundTime + 1</c>,
    /// the length of TF2's winners' crit boost.
    /// </summary>
    void OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds);
}

/// <summary>
/// The respawn waves (stock's <c>m_TeamRespawnWaveTimes</c> family): what the
/// HUD reads and a map's <c>tf_gamerules</c> entity sets.
/// </summary>
public interface ITeamRespawnWaves
{
    /// <summary>When the team's next wave fires; 0 when there is none scheduled.</summary>
    float NextRespawnWave(TFTeam team);

    /// <summary>When this dead player will next be let out (stock <c>GetNextRespawnWave(team, player)</c>).</summary>
    float NextRespawnWave(TFTeam team, EntityHandle player);

    /// <summary>Stock <c>GetRespawnWaveMaxLength</c>.</summary>
    float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true);

    /// <summary>Stock <c>SetTeamRespawnWaveTime</c>.</summary>
    void SetTeamRespawnWaveTime(TFTeam team, float seconds);

    /// <summary>Stock <c>AddTeamRespawnWaveTime</c>.</summary>
    void AddTeamRespawnWaveTime(TFTeam team, float seconds);
}
