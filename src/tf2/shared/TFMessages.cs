namespace TF2.Shared;

/// <summary>
/// The client prints and game events TF2's rules send, in one place
/// (stage2 2.1). TF2 sends no message of its own yet: every line rides the
/// stock <c>TextMsg</c> user message (<c>UTIL_ClientPrintAll</c>'s), whose
/// text is a localisation token the client resolves from TF2's
/// <c>tf_english.txt</c>, and every announcement is a stock game event from
/// TF2's <c>modevents.res</c>. A TF2-keyed user message (<c>TF2:&lt;id&gt;</c>)
/// joins this file when one is declared.
/// </summary>
public static class TFMessages
{
    // ---- TextMsg destinations (shareddefs.h HUD_PRINT*) ---------------------

    public const byte HudPrintNotify = 1;
    public const byte HudPrintConsole = 2;
    public const byte HudPrintTalk = 3;
    public const byte HudPrintCenter = 4;

    // ---- TextMsg tokens ----------------------------------------------------

    /// <summary>"Teams will be auto-balanced in %s1 seconds" (BalanceTeams' warning).</summary>
    public const string AutoTeamBalanceIn = "#game_auto_team_balance_in";

    /// <summary>"%s1 has been changed to the other team for team balance." (BalanceTeams).</summary>
    public const string PlayerWasTeamBalanced = "#game_player_was_team_balanced";

    /// <summary>The <c>#TF_TeamsSwitched</c> chat line (CTFGameRules::HandleSwitchTeams, tf_gamerules.cpp:16539).</summary>
    public const string TeamsSwitched = "#TF_TeamsSwitched";

    /// <summary>"You will be auto-balanced to %s1 in %s2 seconds." (CTFAutobalance's ask, tf_autobalance.cpp:490).</summary>
    public const string AutoBalanceTeamChangePending = "#TF_Autobalance_TeamChangePending";

    /// <summary>"You have been auto-balanced to %s1 for %s2 XP." (CTFAutobalance's done line, tf_autobalance.cpp:439).</summary>
    public const string AutoBalanceTeamChangeDone = "#TF_Autobalance_TeamChangeDone";

    /// <summary>The spectator join's unbalance refusal (HandleCommand_JoinTeam, tf_player.cpp:6377).</summary>
    public const string CannotBeSpectatorUnbalance = "#Cannot_Be_Spectator_Unbalance";

    /// <summary>The team's name as the autobalance prints carry it (tf_autobalance.cpp:438, :482).</summary>
    public static string BalanceTeamName(TFTeam team) => team == TFTeam.Red ? "#TF_RedTeam_Name" : "#TF_BlueTeam_Name";

    /// <summary>mp_restartround's countdown, plural and singular (CheckRestartRound).</summary>
    public const string RoundRestartInSecs = "#round_restart_in_secs";

    public const string RoundRestartInSec = "#round_restart_in_sec";

    /// <summary>
    /// The switch's countdown (stock's <c>#game_switch_in_secs/_sec</c>,
    /// teamplayroundbased_gamerules.cpp:1122). The scramble pair
    /// <c>#game_scramble_in_secs/_sec</c> (<c>:1126</c>) has no twin here: TF
    /// replaces that print with the <c>teamplay_alert</c> event below
    /// (<c>:1128-1137</c>, <c>pFormat = NULL</c>).
    /// </summary>
    public const string GameSwitchInSecs = "#game_switch_in_secs";

    public const string GameSwitchInSec = "#game_switch_in_sec";

    /// <summary>"You may not change class until %s1 seconds have passed since the sudden death round began." (JoinClass).</summary>
    public const string StalemateCantChangeClass = "#game_stalemate_cant_change_class";

    /// <summary>The clan ready restart's rules line, the signal its argument (CheckRestartRound's arm).</summary>
    public const string ClanReadyRules = "#clan_ready_rules";

    // ---- game events (modevents.res) ---------------------------------------

    public const string EventRoundStart = "teamplay_round_start";
    public const string EventRoundActive = "teamplay_round_active";
    public const string EventWinPanel = "teamplay_win_panel";
    public const string EventBroadcastAudio = "teamplay_broadcast_audio";
    public const string EventRoundWin = "teamplay_round_win";
    public const string EventRoundStalemate = "teamplay_round_stalemate";
    public const string EventRestartRound = "teamplay_restart_round";
    public const string EventRoundRestartSeconds = "teamplay_round_restart_seconds";
    public const string EventGameOver = "teamplay_game_over";
    public const string EventTeamBalancedPlayer = "teamplay_teambalanced_player";
    public const string EventSetupFinished = "teamplay_setup_finished";
    public const string EventTimerTimeAdded = "teamplay_timer_time_added";
    public const string EventPreRoundTimeLeft = "teamplay_pre_round_time_left";
    public const string EventMapTimeRemaining = "teamplay_map_time_remaining";

    /// <summary>The overtime nag (teamplay_round_timer.cpp:1014): the coin's heads, broadcast to all.</summary>
    public const string EventOvertimeNag = "overtime_nag";

    public const string EventUpdateTimer = "teamplay_update_timer";

    public const string EventWinlimitChanged = "winlimit_changed";

    public const string EventTeamReady = "teamplay_team_ready";

    public const string EventReadyRestart = "teamplay_ready_restart";

    /// <summary>
    /// <c>teamplay_alert</c> with an <c>alert_type</c> (the HUD scramble-teams
    /// alert stands beside the restart countdown,
    /// teamplayroundbased_gamerules.cpp:1129-1134).
    /// </summary>
    public const string EventTeamplayAlert = "teamplay_alert";

    /// <summary>
    /// <c>scorestats_accumulated_reset</c>, no fields — the accumulated player
    /// scores went with a score reset
    /// (<c>CTeamplayRoundBasedRules::ResetScores</c>, <c>:3262-3268</c>).
    /// </summary>
    public const string EventScorestatsAccumulatedReset = "scorestats_accumulated_reset";
}
