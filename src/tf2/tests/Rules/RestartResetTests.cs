using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The restart's two halves as
/// <c>CTeamplayRoundBasedRules</c> runs them: the countdown's prints
/// (<c>CheckRestartRound</c>, teamplayroundbased_gamerules.cpp:1106-1152 — the
/// switch's <c>#game_switch_in_*</c>, the round restart's formats, the
/// tournament gate, the scramble's <c>teamplay_alert</c> standing in place of
/// its print) and the reset the restart enters
/// (<c>ResetScores</c>, <c>:3235-3282</c> — three arms, each spent by one
/// reset and re-armed, and the ready restart re-arming them at its ask
/// <c>:1759-1760</c>).
/// </summary>
public sealed class RestartResetTests
{
    [Fact]
    public void The_switch_restart_counts_down_with_the_switch_format()
    {
        // Stock's first print arm (teamplayroundbased_gamerules.cpp:1118-1123):
        // a game restart with the switch armed says game_switch_in_secs.
        var rig = new RoundRig();
        rig.Running();
        rig.Flow.SetSwitchTeams(true);
        rig.Settings.RestartGame = 5;

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));

        Assert.Contains("restart_seconds 5 #game_switch_in_secs", rig.Announcer.Said);
    }

    [Fact]
    public void A_one_second_switch_restart_prints_the_singular()
    {
        // The delay > 1 split at :1122, on its singular side.
        var rig = new RoundRig();
        rig.Running();
        rig.Flow.SetSwitchTeams(true);
        rig.Settings.RestartGame = 1;

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));

        Assert.Contains("restart_seconds 1 #game_switch_in_sec", rig.Announcer.Said);
    }

    [Fact]
    public void The_scramble_restart_alerts_instead_of_printing()
    {
        // TF's arm (:1128-1137): where the base game would print
        // #game_scramble_in_secs, TF fires teamplay_alert
        // (alert_type = HUD_ALERT_SCRAMBLE_TEAMS) and nulls the format. The
        // seconds event precedes the alert, as in stock (:1106 then :1129).
        var rig = new RoundRig();
        rig.Running();
        rig.Flow.SetScrambleTeams(true);
        rig.Settings.RestartGame = 5;

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));

        Assert.Contains("restart_seconds 5 ", rig.Announcer.Said);
        Assert.Equal(1, rig.Announcer.Count("scramble_teams_alert"));
        Assert.DoesNotContain(rig.Announcer.Said, s => s.Contains("game_scramble", StringComparison.Ordinal));
        Assert.True(rig.Announcer.Said.IndexOf("restart_seconds 5 ")
                    < rig.Announcer.Said.IndexOf("scramble_teams_alert"));
    }

    [Fact]
    public void Tournament_mode_withholds_every_countdown_print()
    {
        // The gate at :1113 wraps all four formats; the seconds event itself
        // (:1106-1111) stands outside it, so the round restart's event fires
        // with no format.
        var rig = new RoundRig();
        rig.Running();
        rig.Settings.Tournament = true;
        rig.Settings.RestartRound = 5;

        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));

        Assert.Contains("restart_seconds 5 ", rig.Announcer.Said);
        Assert.DoesNotContain(rig.Announcer.Said, s => s.Contains("#", StringComparison.Ordinal));
    }

    [Fact]
    public void The_plain_restart_resets_the_three_arms_and_announces_the_player_reset()
    {
        // A game restart with nothing disarmed: the team scores, the players'
        // accumulated stats (the player arm, :3245-3268) and the rounds played
        // all go, and the TF event of the player arm fires with them.
        var stats = new TFPlayerStats();
        var rig = new RoundRig(stats: stats);
        var (red, _) = rig.Running();
        stats.Increment(red, TFStat.Kills, 5);
        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning, limit: 120f));

        Assert.True(rig.Scores.Score(TFTeam.Red) > 0);
        Assert.Equal(5, stats.Accumulated(rig.Roster.EntIndex(red), TFStat.Kills));
        Assert.Equal(1, rig.Flow.RoundsPlayed);       // the one round won (EnterTeamWin counts it)
        int resets = rig.Announcer.Count("scorestats_accumulated_reset");

        rig.Settings.RestartGame = 5;
        // RESTART runs through in one think (its enter ends in the pre-round),
        // so the first non-running state is the pre-round it came from — and
        // RoundsPlayed only climbs again when that round starts.
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(0, stats.Accumulated(rig.Roster.EntIndex(red), TFStat.Kills));
        Assert.Equal(0, rig.Flow.RoundsPlayed);
        Assert.Equal(resets + 1, rig.Announcer.Count("scorestats_accumulated_reset"));
    }

    [Fact]
    public void The_switch_restart_keeps_the_scores_the_swap_trades_and_the_rounds_played()
    {
        // The shape mp_switchteams leaves the flags in (cc_SwitchTeams,
        // :286-287): the restart arrives but resets nothing — the team scores
        // HandleSwitchTeams swaps and the rounds played mp_maxrounds counts
        // stand, and the player arm never runs (no event, no stat clearing).
        var stats = new TFPlayerStats();
        var rig = new RoundRig(stats: stats);
        var (red, _) = rig.Running();
        stats.Increment(red, TFStat.Kills, 5);
        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning, limit: 120f));
        int redScore = rig.Scores.Score(TFTeam.Red);
        int blueScore = rig.Scores.Score(TFTeam.Blue);
        int played = rig.Flow.RoundsPlayed;           // the round won, counted by EnterTeamWin
        int resets = rig.Announcer.Count("scorestats_accumulated_reset");
        Assert.True(redScore > 0 && played > 0);

        rig.Flow.SetSwitchTeams(true);
        rig.Flow.ShouldResetScores(false, false);
        rig.Flow.ShouldResetRoundsPlayed(false);
        rig.Settings.RestartGame = 5;
        // Stopped at the pre-round the restart fell through to: its enter
        // already ran RoundRespawn, so the switch itself has happened — onto
        // the scores the disarmed flags kept.
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(redScore, rig.Scores.Score(TFTeam.Blue));
        Assert.Equal(blueScore, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(played, rig.Flow.RoundsPlayed);
        Assert.Equal(5, stats.Accumulated(rig.Roster.EntIndex(red), TFStat.Kills));
        Assert.Equal(resets, rig.Announcer.Count("scorestats_accumulated_reset"));
    }

    [Fact]
    public void The_scramble_restart_keeps_the_player_scores_the_sort_reads()
    {
        // cc_ScrambleTeams' ShouldResetScores(true, false) (:307): the team
        // scores and the rounds played reset — the redraw makes them new
        // anyway — but the accumulated stats stand, because HandleScrambleTeams
        // sorts and balances by them.
        var stats = new TFPlayerStats();
        var rig = new RoundRig(stats: stats);
        var (red, _) = rig.Running();
        stats.Increment(red, TFStat.Kills, 5);
        rig.Scores.AddScore(TFTeam.Red, 2);
        Assert.True(rig.Scores.Score(TFTeam.Red) > 0);
        int resets = rig.Announcer.Count("scorestats_accumulated_reset");

        rig.Flow.SetScrambleTeams(true);
        rig.Flow.ShouldResetScores(true, false);
        rig.Settings.RestartGame = 5;
        // The pre-round the restart fell through to: the redraw has already
        // run by now, and it read the stats this assert finds untouched.
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(0, rig.Flow.RoundsPlayed);       // its default was never disarmed
        Assert.Equal(5, stats.Accumulated(rig.Roster.EntIndex(red), TFStat.Kills));
        Assert.Equal(resets, rig.Announcer.Count("scorestats_accumulated_reset"));
    }

    [Fact]
    public void The_ready_restart_rearms_the_disarmed_resets()
    {
        // :1759-1760: the all-ready ask sets ShouldResetScores(true, true) and
        // ShouldResetRoundsPlayed(true), so the five-second restart it arms is
        // the full reset even after a command disarmed the flags.
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning, limit: 120f));
        Assert.True(rig.Scores.Score(TFTeam.Red) > 0);
        Assert.Equal(1, rig.Flow.RoundsPlayed);       // the round won, counted by EnterTeamWin

        rig.Flow.ShouldResetScores(false, false);
        rig.Flow.ShouldResetRoundsPlayed(false);

        rig.Settings.ReadyRestart = true;
        Assert.True(rig.RunUntil(() => rig.Flow.AwaitingReadyRestart, limit: 2f));
        rig.Flow.OnPlayerChat(new PlayerChat(red, "ready", TeamOnly: false));
        rig.Flow.OnPlayerChat(new PlayerChat(blue, "ready", TeamOnly: false));

        rig.Step();                                      // the check's think makes the ask
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(0, rig.Flow.RoundsPlayed);
    }

    [Fact]
    public void A_reset_rearms_the_arms_so_the_next_restart_resets_in_full()
    {
        // :3276-3280: every ResetScores re-arms the three flags, so a reset
        // someone disarmed leaves the next one resetting in full. The switch
        // restart keeps and trades the scores; the restart after it, with
        // nothing disarmed any more, zeroes them.
        var rig = new RoundRig();
        rig.Running();
        rig.Scores.AddScore(TFTeam.Red, 2);

        rig.Flow.SetSwitchTeams(true);
        rig.Flow.ShouldResetScores(false, false);
        rig.Settings.RestartGame = 5;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 8f));

        Assert.Equal(2, rig.Scores.Score(TFTeam.Blue));   // traded, not reset
        Assert.Equal(0, rig.Scores.Score(TFTeam.Red));
        int respawns = rig.Roster.Respawns.Count;         // the first restart's RoundRespawn

        rig.Settings.RestartGame = 5;
        // The countdown's event, then the restart's own enter: RoundRespawn
        // (Respawns) is the second half of that same think, run after the
        // re-armed ResetScores.
        Assert.True(rig.RunUntil(() => rig.Roster.Respawns.Count > respawns, limit: 8f));

        Assert.Equal(0, rig.Scores.Score(TFTeam.Blue));   // the re-armed team arm
    }

    [Fact]
    public void A_restart_begun_cancels_the_waiting_ready_restart()
    {
        // :1158-1159: mp_restartgame overrules a ready restart still waiting
        // on the teams.
        var rig = new RoundRig();
        rig.Running();

        rig.Settings.ReadyRestart = true;
        Assert.True(rig.RunUntil(() => rig.Flow.AwaitingReadyRestart, limit: 2f));

        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Announcer.Count("restart_seconds") > 0, limit: 2f));

        Assert.False(rig.Flow.AwaitingReadyRestart);
    }
}
