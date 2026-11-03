using System.Globalization;

using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The round machine's transitions (stage2 2.1), one per fact, driven by a
/// hand-moved clock over fakes of every seam: each asserts the state AND the
/// side effect the transition owes (a respawn, a map reset, an event, a
/// score, the intermission).
/// </summary>
public sealed class TeamRoundFlowTests
{
    [Fact]
    public void A_new_machine_waits_in_PREGAME()
    {
        var rig = new RoundRig();

        rig.Step();
        rig.Step();

        Assert.Equal(RoundState.Pregame, rig.Flow.State);
        Assert.Equal(0, rig.World.Count("reset"));
    }

    [Fact]
    public void A_ready_player_takes_PREGAME_through_STARTGAME_to_PREROUND()
    {
        var rig = new RoundRig();
        rig.Step();
        Assert.Equal(RoundState.Pregame, rig.Flow.State);

        rig.Roster.Add(TFTeam.Red);
        rig.Step();
        Assert.Equal(RoundState.StartGame, rig.Flow.State);

        rig.Step();
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(new[] { "Pregame->StartGame", "StartGame->Preround" }, rig.Listener.Heard);
    }

    [Fact]
    public void A_player_on_no_playing_team_is_not_enough_to_start()
    {
        var rig = new RoundRig();
        rig.Roster.Add(TFTeam.Spectator);
        var red = rig.Roster.Add(TFTeam.Red);
        rig.Roster.SetReady(red, false);   // on a team, no class yet

        rig.Step();
        rig.Step();

        Assert.Equal(RoundState.Pregame, rig.Flow.State);
    }

    [Fact]
    public void Entering_PREROUND_resets_the_map_then_sends_the_round_inputs_then_respawns_everyone()
    {
        var rig = new RoundRig();
        var red = rig.Roster.Add(TFTeam.Red);
        var blue = rig.Roster.Add(TFTeam.Blue);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround));

        Assert.Equal(new[] { "reset", "spawn", "activate", "cp_spawns" }, rig.World.Calls);
        Assert.Equal(1, rig.Mode.RoundStarts);
        // RespawnPlayers( true ): the living too, at the round's start.
        Assert.Equal(new[] { red, blue }, rig.Roster.Respawns.Select(r => r.Player));
        Assert.Equal(1, rig.Announcer.Count("round_start full_reset=True"));
    }

    [Fact]
    public void PREROUND_becomes_RND_RUNNING_after_five_times_mp_enableroundwaittime()
    {
        var rig = new RoundRig();
        rig.Roster.Add(TFTeam.Red);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround));
        float entered = rig.Clock.Now;

        rig.Clock.Now = entered + 5f;      // not past it yet
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.Announcer.Count("round_active"));

        rig.Clock.Now = entered + 5.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("round_active"));
        Assert.Equal(1, rig.Mode.RoundRunnings);
    }

    [Fact]
    public void With_mp_enableroundwaittime_0_PREROUND_lasts_one_think()
    {
        var rig = new RoundRig();
        rig.Settings.EnableRoundWaitTime = 0f;
        rig.Roster.Add(TFTeam.Red);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround));

        rig.Step(0.01f);

        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);
    }

    [Fact]
    public void A_win_enters_TEAM_WIN_scores_the_winner_and_announces_it()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Red, rig.Flow.WinningTeam);
        Assert.Equal(1, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(0, rig.Scores.Score(TFTeam.Blue));
        Assert.Equal(1, rig.Announcer.Count("round_win Red AllPointsCaptured full=True sudden=False"));
        Assert.True(rig.Flow.RoundOver);
    }

    [Fact]
    public void TEAM_WIN_holds_the_stock_bonus_time_then_starts_the_next_round_with_a_map_reset()
    {
        var rig = new RoundRig();
        rig.Running();
        int resets = rig.World.Count("reset");

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.FlagCaptureLimit);
        float won = rig.Clock.Now;

        rig.Clock.Now = won + 15f;         // mp_bonusroundtime 15, not past it
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);

        rig.Clock.Now = won + 15.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(resets + 1, rig.World.Count("reset"));
        Assert.Equal(1, rig.Flow.RoundsPlayed);
        // Stock's State_Transition_TEAM_WIN ends in GR_STATE_RESTART, which
        // enters GR_STATE_PREROUND: the win yields to RESTART, not straight
        // to the pre-round.
        Assert.Equal(new[] { "TeamWin->Restart", "Restart->Preround" }, rig.Listener.Heard.TakeLast(2));
        // A round restart is not mp_restartgame: the winner's round-win
        // score (added by SetWinningTeam) must survive into the next round.
        Assert.Equal(1, rig.Scores.Score(TFTeam.Blue));
    }

    [Fact]
    public void The_bonus_time_is_never_under_five_seconds()
    {
        var rig = new RoundRig();
        rig.Settings.BonusRoundTime = 1;
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.OpponentsDead);
        float won = rig.Clock.Now;

        rig.Clock.Now = won + 4.9f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);

        rig.Clock.Now = won + 5.05f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Preround, rig.Flow.State);
    }

    [Fact]
    public void A_second_win_in_TEAM_WIN_changes_nothing()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);

        Assert.Equal(TFTeam.Red, rig.Flow.WinningTeam);
        Assert.Equal(0, rig.Scores.Score(TFTeam.Blue));
        Assert.Equal(1, rig.Announcer.Count("round_win"));
    }

    [Fact]
    public void A_mode_that_does_not_score_per_round_adds_no_team_point()
    {
        var rig = new RoundRig();
        rig.Mode.ScoresPerRound = false;
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
    }

    [Fact]
    public void A_win_the_mode_refuses_leaves_the_round_running()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Mode.AllowsWins = false;

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);

        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);
        Assert.Equal(0, rig.Announcer.Count("round_win"));
    }

    [Fact]
    public void The_listeners_hear_a_win_with_the_crit_boost_length()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.OpponentsDead);

        Assert.Equal("won Blue OpponentsDead 16", rig.Listener.Heard[^1]);
    }

    [Fact]
    public void A_win_reaching_mp_winlimit_ends_the_game_after_the_bonus_time()
    {
        var rig = new RoundRig();
        rig.Settings.WinLimit = 1;
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.Equal(0, rig.End.Intermissions);

        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.TeamWin, limit: 20f));
        Assert.Equal(RoundState.GameOver, rig.Flow.State);
        Assert.Equal(1, rig.End.Intermissions);
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Win Limit"));
    }

    [Fact]
    public void A_lead_of_mp_windifference_ends_the_game()
    {
        var rig = new RoundRig();
        rig.Settings.WinDifference = 3;
        rig.Running();

        // The standings plus the round win's point: Blue 3, Red 0.
        rig.Scores.AddScore(TFTeam.Blue, 2);
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.GameOver, limit: 20f));
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Win Difference Limit"));
    }

    [Fact]
    public void The_mirror_lead_ends_the_game_for_red()
    {
        var rig = new RoundRig();
        rig.Settings.WinDifference = 3;
        rig.Running();

        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.GameOver, limit: 20f));
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Win Difference Limit"));
    }

    [Fact]
    public void mp_windifference_min_holds_the_end_until_the_leader_is_there()
    {
        var rig = new RoundRig();
        rig.Settings.WinDifference = 2;
        rig.Settings.WinDifferenceMin = 4;
        rig.Running();

        // Blue 2, Red 0 once the win's point lands: the lead qualifies, the
        // floor does not, so the map plays on.
        rig.Scores.AddScore(TFTeam.Blue, 1);
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        Assert.Equal(0, rig.Announcer.Count("game_over"));

        // Blue 4, Red 0 after the next Blue win: both gates open.
        rig.Scores.AddScore(TFTeam.Blue, 1);
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.GameOver, limit: 20f));
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Win Difference Limit"));
    }

    [Fact]
    public void A_lead_below_mp_windifference_ends_nothing()
    {
        var rig = new RoundRig();
        rig.Settings.WinDifference = 5;
        rig.Running();

        rig.Scores.AddScore(TFTeam.Blue, 3);
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        Assert.Equal(0, rig.Announcer.Count("game_over"));
    }

    [Fact]
    public void mp_maxrounds_ends_the_game_after_that_many_full_rounds()
    {
        var rig = new RoundRig();
        rig.Settings.MaxRounds = 1;
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.TeamWin, limit: 20f));

        Assert.Equal(RoundState.GameOver, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Round Limit"));
    }

    [Fact]
    public void A_running_round_with_no_ready_player_goes_back_to_PREGAME()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Roster.SetReady(red, false);
        rig.Roster.SetReady(blue, false);

        rig.Step();

        Assert.Equal(RoundState.Pregame, rig.Flow.State);
    }

    [Fact]
    public void mp_restartgame_counts_down_then_RESTART_resets_the_scores_and_goes_to_PREROUND()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Settings.RestartGame = 3;

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));
        Assert.Equal(0, rig.Settings.RestartGame);   // read and cleared
        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);

        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 5f));

        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(1, rig.Announcer.Count("restart_round"));
        Assert.Equal(new[] { "RoundRunning->Restart", "Restart->Preround" }, rig.Listener.Heard.TakeLast(2));
    }

    [Fact]
    public void mp_clan_readyrestart_arms_the_wait_tells_the_rules_and_spends_itself()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.RestartRound = 30;          // a countdown the arm cancels
        Assert.True(rig.RunUntil(() => rig.Flow.RestartRoundTime > 0, limit: 2f));

        rig.Settings.ReadyRestart = true;
        Assert.True(rig.RunUntil(() => rig.Flow.AwaitingReadyRestart, limit: 2f));

        Assert.False(rig.Settings.ReadyRestart); // read and cleared
        Assert.Equal(-1f, rig.Flow.RestartRoundTime);   // the running countdown cancelled
        Assert.Contains("clan_ready_rules ready", rig.Announcer.Said);
        Assert.Contains("ready_restart", rig.Announcer.Said);
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Red));
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Blue));
    }

    [Fact]
    public void The_ready_signal_from_one_team_marks_it_and_fires_team_ready()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        ArmTheWait(rig);

        rig.Flow.OnPlayerChat(new PlayerChat(red, "not yet", TeamOnly: false));
        Assert.Equal(0, rig.Announcer.Count("team_ready"));

        rig.Flow.OnPlayerChat(new PlayerChat(red, "ready", TeamOnly: false));

        Assert.True(rig.Flow.TeamIsReady(TFTeam.Red));
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Blue));
        Assert.Equal(1, rig.Announcer.Count("team_ready Red"));
    }

    [Fact]
    public void A_signal_from_outside_the_game_teams_marks_nothing()
    {
        var rig = new RoundRig();
        rig.Running();
        var spectator = rig.Roster.Add(TFTeam.Spectator);
        ArmTheWait(rig);

        rig.Flow.OnPlayerChat(new PlayerChat(spectator, "ready", TeamOnly: false));

        Assert.Equal(0, rig.Announcer.Count("team_ready"));
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Red));
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Blue));
    }

    [Fact]
    public void Both_teams_ready_restarts_the_game_in_five_seconds_with_a_full_reset()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Scores.AddScore(TFTeam.Red, 2);
        ArmTheWait(rig);

        rig.Flow.OnPlayerChat(new PlayerChat(red, "ready", TeamOnly: false));
        rig.Flow.OnPlayerChat(new PlayerChat(blue, "ready", TeamOnly: false));

        rig.Step();                                      // the check's think makes the ask
        Assert.Equal(TeamRoundFlow.RestartOnReady, rig.Settings.RestartGame);

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));
        Assert.False(rig.Flow.AwaitingReadyRestart);     // the check disarmed itself at its ask
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(RoundState.Preround, rig.Flow.State);
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));   // the full reset the restart carries
        Assert.Equal(1, rig.Announcer.Count("restart_round"));
    }

    [Fact]
    public void Without_the_arm_no_word_reaches_the_ready_restart()
    {
        var rig = new RoundRig();
        var (red, _) = rig.Running();

        rig.Flow.OnPlayerChat(new PlayerChat(red, "ready", TeamOnly: false));

        Assert.Equal(0, rig.Announcer.Count("team_ready"));
        Assert.Equal(0, rig.Settings.RestartGame);
        Assert.False(rig.Flow.TeamIsReady(TFTeam.Red));
    }

    [Fact]
    public void Tournament_refuses_to_arm_the_ready_restart()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.Tournament = true;
        rig.Settings.ReadyRestart = true;

        for (int i = 0; i < 12; i++)                     // past a periodic think
            rig.Step();

        Assert.False(rig.Flow.AwaitingReadyRestart);
        Assert.True(rig.Settings.ReadyRestart);          // stock's gate leaves the convar standing
        Assert.Equal(0, rig.Announcer.Count("ready_restart"));
    }

    /// <summary>Arms the ready restart and waits for the periodic think that reads it.</summary>
    private static void ArmTheWait(RoundRig rig)
    {
        rig.Settings.ReadyRestart = true;
        Assert.True(rig.RunUntil(() => rig.Flow.AwaitingReadyRestart, limit: 2f));
    }

    [Fact]
    public void The_waiting_for_players_period_ends_in_a_restart_and_a_map_reset()
    {
        var rig = new RoundRig();
        rig.Settings.WaitingForPlayersTime = 30f;
        rig.Running();
        Assert.True(rig.Flow.InWaitingForPlayers);
        int resets = rig.World.Count("reset");

        Assert.True(rig.RunUntil(() => !rig.Flow.InWaitingForPlayers, limit: 40f));

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 1f));
        Assert.Equal(resets + 1, rig.World.Count("reset"));
        Assert.Equal(1, rig.Announcer.Count("restart_round"));
    }

    [Fact]
    public void Without_a_wait_there_is_no_waiting_for_players_period()
    {
        var rig = new RoundRig();
        rig.Running();

        Assert.False(rig.Flow.InWaitingForPlayers);
    }

    [Fact]
    public void mp_waitingforplayers_cancel_ends_the_wait_at_once()
    {
        var rig = new RoundRig();
        rig.Settings.WaitingForPlayersTime = 30f;
        rig.Running();
        Assert.True(rig.Flow.InWaitingForPlayers);

        rig.Settings.WaitingForPlayersCancel = true;
        Assert.True(rig.RunUntil(() => !rig.Flow.InWaitingForPlayers, limit: 1.5f));
        Assert.False(rig.Settings.WaitingForPlayersCancel);
    }

    [Fact]
    public void The_wait_shows_its_HUD_timer_running_for_the_seconds_left()
    {
        var rig = new RoundRig();
        rig.Settings.WaitingForPlayersTime = 30f;
        rig.World.CreatedWaiting = true;
        rig.Running();

        // Stock's else arm (teamplayroundbased_gamerules.cpp:1005-1023): a
        // team_round_timer asked of the world, for the wait's seconds left.
        // The creation hides the previous HUD timers (RoundTimerTests).
        string call = rig.World.Calls.First(c => c.StartsWith("waiting_timer ", StringComparison.Ordinal));
        Assert.True(float.Parse(call["waiting_timer ".Length..], CultureInfo.InvariantCulture) > 0f);
    }

    [Fact]
    public void A_restarted_wait_retunes_its_HUD_timer()
    {
        var rig = new RoundRig();
        rig.Settings.WaitingForPlayersTime = 30f;
        rig.World.CreatedWaiting = true;
        rig.Running();
        rig.World.Calls.Clear();

        rig.Settings.WaitingForPlayersRestart = true;
        rig.Step(1.1f);   // the periodic think the check rides

        // The restart arm (teamplayroundbased_gamerules.cpp:931-938): the
        // timer it already has, the new end — under a minute of the wait.
        string call = rig.World.Calls.First(c => c.StartsWith("retune_waiting_timer ", StringComparison.Ordinal));
        float seconds = float.Parse(call["retune_waiting_timer ".Length..], CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 28.9f, 30f);
    }

    [Fact]
    public void The_wait_end_removes_its_HUD_timer()
    {
        var rig = new RoundRig();
        rig.Settings.WaitingForPlayersTime = 2f;
        rig.World.CreatedWaiting = true;
        rig.Running();

        Assert.True(rig.RunUntil(() => !rig.Flow.InWaitingForPlayers, limit: 5f));

        // SetInWaitingForPlayers(false)'s UTIL_Remove (teamplayroundbased_gamerules.cpp:866-869).
        Assert.Contains("remove_waiting_timer", rig.World.Calls);
    }

    [Fact]
    public void A_stalemate_with_mp_stalemate_enable_is_sudden_death()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        Assert.Equal(RoundState.Stalemate, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("round_stalemate Timer"));
        Assert.Equal(1, rig.Timers.Hides);
    }

    [Fact]
    public void Entering_the_stalemate_respawns_every_player()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, blue) = rig.Running();
        int before = rig.Roster.Respawns.Count;

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // SetupOnStalemateStart's non-arena arm (tf_gamerules.cpp:5500): the
        // re-kit under mp_stalemate_meleeonly rides this respawn.
        Assert.Equal([red, blue], rig.Roster.Respawns.Skip(before).Select(r => r.Player).ToArray());
    }

    [Fact]
    public void Entering_the_stalemate_wipes_the_projectiles_and_buildings_before_anyone_respawns()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();
        int before = rig.Roster.Respawns.Count;
        rig.World.RespawnCount = () => rig.Roster.Respawns.Count - before;

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // RemoveAllProjectilesAndBuildings before RespawnPlayers(true)
        // (tf_gamerules.cpp:5495-5500): nobody falls out of the air onto a
        // sentry that is still standing.
        Assert.Contains("remove_projectiles_buildings", rig.World.Calls);
        Assert.Equal(0, rig.World.RespawnsAtFirstWipe);
    }

    [Fact]
    public void Entering_the_stalemate_disables_the_worlds_health_kits()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // SetupOnStalemateStart's kit half (tf_gamerules.cpp:5502-5513).
        Assert.Contains("disable_kits", rig.World.Calls);
    }

    [Fact]
    public void Leaving_the_stalemate_restores_the_health_kits()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);
        Assert.Contains("disable_kits", rig.World.Calls);

        rig.Roster.Kill(red);
        rig.Step();

        // SetupOnStalemateEnd, from State_Leave_STALEMATE
        // (teamplayroundbased_gamerules.cpp:2057) — even the leave into a win.
        Assert.Contains("restore_kits", rig.World.Calls);
    }

    [Fact]
    public void The_stalemate_names_its_start_for_the_class_change_window()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();
        float started = rig.Clock.Now;

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // m_flStalemateStartTime (tf_gamerules.cpp:5561), what
        // CanChangeClassInStalemate counts the window from.
        Assert.Equal(started, rig.Flow.StalemateStartTime);
    }

    [Fact]
    public void A_stalemate_without_mp_stalemate_enable_is_a_win_for_nobody()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Unassigned, rig.Flow.WinningTeam);
        Assert.Equal(1, rig.Announcer.Count("round_win Unassigned Stalemate"));
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red) + rig.Scores.Score(TFTeam.Blue));
    }

    [Fact]
    public void In_sudden_death_the_team_left_alive_wins()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);

        rig.Roster.Kill(red);
        rig.Step();

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Blue, rig.Flow.WinningTeam);
        Assert.Equal(1, rig.Announcer.Count("round_win Blue OpponentsDead full=True sudden=True"));
    }

    [Fact]
    public void A_no_reset_sudden_death_hands_its_win_to_the_control_point_master()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer, forceMapReset: false);
        rig.World.HasMaster = true;

        rig.Roster.Kill(red);
        rig.Step();

        // The SetWinnerAndForceCaps arm (teamplayroundbased_gamerules.cpp:2235-2248):
        // the master forces the caps and owns the win; the round does not win again.
        Assert.Equal(["master_win Blue"], rig.World.Calls.Where(c => c.StartsWith("master_win", StringComparison.Ordinal)));
        Assert.Equal(0, rig.Announcer.Count("round_win Blue OpponentsDead"));
    }

    [Fact]
    public void A_sudden_death_without_a_master_or_on_a_forced_reset_wins_outright()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer, forceMapReset: false);
        rig.World.HasMaster = false;

        rig.Roster.Kill(red);
        rig.Step();

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Blue, rig.Flow.WinningTeam);
        Assert.Equal(0, rig.World.Count("master_win Blue"));

        var forced = new RoundRig();
        forced.Settings.StalemateEnable = true;
        var (redTwo, _) = forced.Running();
        forced.Flow.SetStalemate(StalemateReason.Timer, forceMapReset: true);
        forced.World.HasMaster = true;

        forced.Roster.Kill(redTwo);
        forced.Step();

        // On a forced map reset the master is never asked (stock :2235).
        Assert.Equal(RoundState.TeamWin, forced.Flow.State);
        Assert.Equal(0, forced.World.Count("master_win Blue"));
    }

    [Fact]
    public void The_game_restart_tells_the_time_remaining_to_the_map_change()
    {
        var rig = new RoundRig();
        rig.Settings.TimeLimitMinutes = 1;
        rig.Running();
        rig.Settings.RestartGame = 1;
        for (int i = 0; i < 10; i++)
            rig.Step(0.5f);                // the countdown, then the restart lands

        // ResetMapTime fires teamplay_map_time_remaining with GetTimeLeft()
        // (teamplayroundbased_gamerules.cpp:3289-3297), after the reset time.
        Assert.Equal(1, rig.Announcer.Count("map_time_remaining 60"));
    }

    [Fact]
    public void Sudden_death_running_out_is_a_win_for_nobody_and_a_veto_holds_it()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Settings.StalemateTimeLimit = 10;
        rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);
        float started = rig.Clock.Now;

        rig.Veto.Allows = false;
        rig.Clock.Now = started + 11f;
        rig.Flow.Think(rig.Clock.Now);
        Assert.Equal(RoundState.Stalemate, rig.Flow.State);

        rig.Veto.Allows = true;
        rig.Step();
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Unassigned, rig.Flow.WinningTeam);
    }

    [Fact]
    public void Leaving_the_stalemate_restores_the_hidden_HUD_timer()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);
        int before = rig.Timers.Restores;

        rig.Roster.Kill(red);
        rig.Step();

        Assert.Equal(before + 1, rig.Timers.Restores);
    }

    [Fact]
    public void The_sudden_death_shows_its_HUD_timer_and_tells_the_HUD()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // mp_stalemate_timelimit on the timer, then teamplay_update_timer
        // (teamplayroundbased_gamerules.cpp:2032-2048).
        Assert.Contains("stalemate_timer 240", rig.World.Calls);
        Assert.Equal(1, rig.Announcer.Count("update_timer"));
    }

    [Fact]
    public void A_sudden_death_without_a_limit_spawns_no_HUD_timer()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        rig.Settings.StalemateTimeLimit = 0;
        rig.Running();

        rig.Flow.SetStalemate(StalemateReason.Timer);

        // The `if ( iTimeLimit > 0 )` arm (:2029-2049): no timer, no event.
        Assert.DoesNotContain(rig.World.Calls, c => c.StartsWith("stalemate_timer", StringComparison.Ordinal));
        Assert.Equal(0, rig.Announcer.Count("update_timer"));
    }

    [Fact]
    public void The_sudden_death_end_removes_its_HUD_timer_and_tells_the_HUD()
    {
        var rig = new RoundRig();
        rig.Settings.StalemateEnable = true;
        var (red, _) = rig.Running();
        rig.Flow.SetStalemate(StalemateReason.Timer);
        int told = rig.Announcer.Count("update_timer");

        rig.Roster.Kill(red);
        rig.Step();

        // State_Leave_STALEMATE's UTIL_Remove and its event (:2059-2072).
        Assert.Contains("remove_stalemate_timer", rig.World.Calls);
        Assert.Equal(told + 1, rig.Announcer.Count("update_timer"));
    }

    [Fact]
    public void A_map_reset_spawns_the_time_limit_timer_when_the_map_has_none()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.RestartGame = 1;

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        // RoundRespawn -> CreateTimeLimitTimer (teamplayroundbased_gamerules.cpp:2812-2819),
        // with mp_timelimit on it and the map's seconds left.
        Assert.Equal(1, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void A_map_with_its_own_HUD_timer_gets_no_time_limit_timer()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.RestartGame = 1;
        rig.World.HasActiveTimer = true;

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        // "check that we have an active timer in the HUD and use mp_timelimit
        // if we don't" (:2814-2818).
        Assert.Equal(0, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void Without_the_end_at_timelimit_guard_no_time_limit_timer_is_spawned()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.RestartGame = 1;   // mp_match_end_at_timelimit 0, AllowStalemateAtTimelimit false

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        // CreateTimeLimitTimer's bAllowStalemate (teamplayroundbased_gamerules.cpp:2777-2780).
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
    }

    [Fact]
    public void SetStalemateOnTimelimit_true_spawns_the_time_limit_timer_without_the_convar()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.RestartGame = 1;   // mp_match_end_at_timelimit 0
        rig.Flow.SetStalemateOnTimelimit(true);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        // The stalemate half of CreateTimeLimitTimer's guard (:2778).
        Assert.Equal(1, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void A_live_mp_timelimit_change_spawns_the_time_limit_timer_at_once()
    {
        var rig = new RoundRig();          // clock 100, the map clock's start
        rig.Settings.MatchEndAtTimeLimit = true;

        rig.Settings.ChangeTimeLimit(0, 5);

        // HandleTimeLimitChange's create arm (teamplayroundbased_gamerules.cpp:2711-2714):
        // the timer the new limit's seconds left, not the next reset's.
        Assert.Equal(1, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void A_live_mp_timelimit_change_retunes_the_timer_it_already_has()
    {
        var rig = new RoundRig();
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.ChangeTimeLimit(0, 5);

        rig.Settings.ChangeTimeLimit(5, 2);

        // The reuse path re-shows with the new limit's seconds left (stock's
        // CreateTimeLimitTimer re-running SetTime/Resume/Enable, :2783-2794).
        Assert.Equal(1, rig.World.Count("timelimit_timer 120"));
    }

    [Fact]
    public void A_live_change_to_zero_removes_the_time_limit_timer()
    {
        var rig = new RoundRig();
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.ChangeTimeLimit(0, 5);

        rig.Settings.ChangeTimeLimit(5, 0);

        // The else arm (:2715-2722): the limit is gone, so is the rules' timer
        // the earlier change had spawned.
        Assert.Equal(1, rig.World.Count("remove_timelimit_timer"));
        Assert.Equal(1, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void A_live_change_whose_limit_has_already_passed_removes_the_time_limit_timer()
    {
        var rig = new RoundRig();
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Clock.Now = 400.5f;            // the map clock ran out under a 5-minute limit

        rig.Settings.ChangeTimeLimit(2, 5);

        // GetTimeLeft() <= 0 is the else arm too (:2711): no timer for a limit already passed.
        Assert.Equal(1, rig.World.Count("remove_timelimit_timer"));
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_live_change_with_the_maps_own_timer_removes_the_rules_timer()
    {
        var rig = new RoundRig();
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.World.HasActiveTimer = true;

        rig.Settings.ChangeTimeLimit(0, 5);

        // MapHasActiveTimer() true takes the else arm: the map's own timer
        // shows, the rules' is not kept beside it (:2711, :2717-2721).
        Assert.Equal(1, rig.World.Count("remove_timelimit_timer"));
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_negative_live_mp_timelimit_is_clamped_to_zero()
    {
        var rig = new RoundRig();

        rig.Settings.ChangeTimeLimit(5, -1);

        // MPTimeLimitCallback's clamp (multiplay_gamerules.cpp:74-77): the
        // rewrite re-enters with the 0 and its arm removes the timer.
        Assert.Equal(0, rig.Settings.TimeLimitMinutes);
        Assert.Equal(1, rig.World.Count("remove_timelimit_timer"));
    }

    [Fact]
    public void A_live_mp_winlimit_change_fires_winlimit_changed()
    {
        var rig = new RoundRig();

        rig.Settings.ChangeWinLimit(0, 3);

        // WinlimitChanged (teamplayroundbased_gamerules.cpp:188-195): the
        // change callback's one act, for the HUD.
        Assert.Equal(1, rig.Announcer.Count("winlimit_changed"));
    }

    [Fact]
    public void A_live_change_without_the_end_at_timelimit_guard_touches_no_timer()
    {
        var rig = new RoundRig();          // mp_match_end_at_timelimit 0, no stalemate allowance

        rig.Settings.ChangeTimeLimit(0, 5);

        // CreateTimeLimitTimer's bAllowStalemate returns before either arm's
        // work (:2778-2780): the rules' timer, if one stood, stays as it was.
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
        Assert.Equal(0, rig.World.Count("remove_timelimit_timer"));
    }

    [Fact]
    public void The_server_time_limit_with_mp_match_end_at_timelimit_gives_the_round_to_the_leader_unscored()
    {
        var rig = new RoundRig();
        rig.Settings.TimeLimitMinutes = 1;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Running();
        rig.Scores.AddScore(TFTeam.Blue, 1);

        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 70f));

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Blue, rig.Flow.WinningTeam);
        Assert.Equal(WinReason.TimeLimit, rig.Flow.WinReason);
        Assert.Equal(1, rig.Scores.Score(TFTeam.Blue));   // bDontAddScore
    }

    [Fact]
    public void Without_mp_match_end_at_timelimit_the_time_limit_waits_for_the_round_to_end()
    {
        var rig = new RoundRig();
        rig.Settings.TimeLimitMinutes = 1;
        rig.Running();

        rig.RunUntil(() => false, limit: 70f);
        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.TeamWin, limit: 20f));
        Assert.Equal(RoundState.GameOver, rig.Flow.State);
        Assert.Equal(1, rig.Announcer.Count("game_over Reached Time Limit"));
    }

    [Fact]
    public void ResetRound_is_a_fresh_machine()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);

        rig.Flow.ResetRound();

        Assert.Equal(RoundState.Pregame, rig.Flow.State);
        Assert.Equal(TFTeam.Unassigned, rig.Flow.WinningTeam);
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(0, rig.Flow.RoundsPlayed);
        Assert.False(rig.Flow.RoundOver);
    }

    [Fact]
    public void The_constructor_tells_no_listener()
    {
        var rig = new RoundRig();

        Assert.Empty(rig.Listener.Heard);
        Assert.Equal(RoundState.Pregame, rig.Flow.State);
    }
}
