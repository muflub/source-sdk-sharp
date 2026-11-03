using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The auto scramble's arm, the tail of stock's <c>SetWinningTeam</c>
/// (teamplayroundbased_gamerules.cpp:2404-2509): on a full-reset win under
/// <c>mp_scrambleteams_auto</c> the per-team win counters are counted against
/// <c>mp_scrambleteams_auto_windifference</c>, and a delta that reaches it
/// arms the scramble for the round restart — with the alert, the log line and
/// the reset arms — unless a nearer map change, the round limits or a listen
/// server make it pointless.
/// </summary>
public sealed class TeamAutoScrambleTests
{
    /// <summary>Wins the round for <paramref name="team"/> and plays the restart out.</summary>
    private static void Win(RoundRig rig, TFTeam team)
    {
        rig.Flow.SetWinningTeam(team, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
    }

    [Fact]
    public void A_win_delta_at_the_windifference_arms_the_scramble_with_its_alert()
    {
        var rig = new RoundRig();
        rig.Running();

        Win(rig, TFTeam.Red);   // the counters at 1-0, one below the difference
        Assert.False(rig.Flow.ShouldScrambleTeams());

        // 2-0: the delta reaches mp_scrambleteams_auto_windifference.
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(1, rig.Announcer.Count("scramble_teams_alert"));

        // The armed reset takes the team scores and keeps the rounds played
        // (ShouldResetScores( true, false ), ShouldResetRoundsPlayed( false ),
        // :2489-2490) at the next full reset — the win's own restart keeps
        // the scores for the limits' read, so a game restart asks for it.
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() =>
            rig.Flow.State == RoundState.RoundRunning && rig.Scores.Score(TFTeam.Red) == 0));
        Assert.Equal(2, rig.Flow.RoundsPlayed);
    }

    [Fact]
    public void A_win_delta_below_the_windifference_arms_nothing()
    {
        var rig = new RoundRig();
        rig.Running();

        Win(rig, TFTeam.Red);

        Assert.False(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(0, rig.Announcer.Count("scramble_teams_alert"));
    }

    [Fact]
    public void A_listen_server_never_arms_the_auto_scramble()
    {
        var rig = new RoundRig();
        rig.Engine.Returns("Host.IsDedicatedServer", false);
        rig.Running();

        Win(rig, TFTeam.Red);
        Win(rig, TFTeam.Red);   // a delta that would arm a dedicated server

        Assert.False(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(0, rig.Announcer.Count("scramble_teams_alert"));
    }

    [Fact]
    public void A_team_one_win_from_mp_winlimit_arms_nothing()
    {
        // Three wins to the map's end: after two red rounds the team is one
        // away, whatever its win counter says.
        var rig = new RoundRig();
        rig.Settings.WinLimit = 3;
        rig.Running();

        Win(rig, TFTeam.Red);
        Win(rig, TFTeam.Red);

        Assert.False(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(0, rig.Announcer.Count("scramble_teams_alert"));
    }

    [Fact]
    public void A_round_one_short_of_mp_maxrounds_arms_nothing()
    {
        // Three rounds to the map's end: the third is one away after two.
        var rig = new RoundRig();
        rig.Settings.MaxRounds = 3;
        rig.Running();

        Win(rig, TFTeam.Red);
        Win(rig, TFTeam.Red);

        Assert.False(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(0, rig.Announcer.Count("scramble_teams_alert"));
    }

    [Fact]
    public void A_round_won_with_the_switch_counts_for_the_team_it_found()
    {
        // The counters swap after a switching win (:2502-2508), so the second
        // red win lands on the same side of the ledger the first left: with
        // the switch the two red wins are 1-1, never the 2-0 that arms.
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        Win(rig, TFTeam.Red);

        Assert.False(rig.Flow.ShouldScrambleTeams());
        Assert.Equal(0, rig.Announcer.Count("scramble_teams_alert"));
    }
}
