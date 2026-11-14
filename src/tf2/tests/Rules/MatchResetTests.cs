using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// TF2's game restart tells the core's new-match chain (plan_bots_teams_tf2
/// A.6), so the departed players' scores go where stock resets the players'
/// own: <c>CTeamplayRoundBasedRules::ResetScores</c> with
/// <c>m_bResetPlayerScores</c>. A round inside the match keeps them.
/// </summary>
public class MatchResetTests
{
    [Fact]
    public void A_game_restart_tells_the_core_a_new_match_began()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.MatchResets.Heard.Clear();

        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        Assert.Equal(["match restart"], rig.MatchResets.Heard);
    }

    [Fact]
    public void A_won_round_is_not_a_new_match()
    {
        var rig = new RoundRig();
        rig.Settings.ScrambleAuto = false;
        rig.Running();
        rig.MatchResets.Heard.Clear();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: false);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        // The win's RESTART keeps the scores (EnterRestart's _restartAfterWin arm).
        Assert.Empty(rig.MatchResets.Heard);
    }

    [Fact]
    public void A_restart_that_keeps_the_player_scores_is_not_a_new_match()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.MatchResets.Heard.Clear();

        // mp_scrambleteams' shape (teamplayroundbased_gamerules.cpp:307): the
        // team scores go, the players' stay, so the departed players' stay too.
        rig.Flow.ShouldResetScores(true, false);
        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        Assert.Empty(rig.MatchResets.Heard);
    }
}
