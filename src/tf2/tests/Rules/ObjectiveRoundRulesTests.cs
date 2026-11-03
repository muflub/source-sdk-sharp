using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The round machine's answers to the control points (<see cref="IObjectiveRoundRules"/>):
/// the per-capture score, the escort blocking rule, overtime, and the
/// mini-round history the master picks the next round by.
/// </summary>
public sealed class ObjectiveRoundRulesTests
{
    [Fact]
    public void A_per_capture_score_lands_on_the_networked_team_table()
    {
        var table = new TeamResource();
        var rig = new RoundRig(table);
        IObjectiveRoundRules rules = rig.Flow;

        rules.AddTeamScore(TFTeam.Red, 1);
        rules.AddTeamScore(TFTeam.Red, 1);

        Assert.Equal(2, table.Score((int)TFTeam.Red));
        Assert.Equal(0, table.Score((int)TFTeam.Blue));
    }

    [Fact]
    public void A_round_win_scores_on_the_networked_team_table_and_a_restart_clears_it()
    {
        var table = new TeamResource();
        var rig = new RoundRig(table);
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.AllPointsCaptured);
        Assert.Equal(1, table.Score((int)TFTeam.Blue));

        rig.Flow.ResetRound();
        Assert.Equal(0, table.Score((int)TFTeam.Blue));
    }

    [Fact]
    public void Points_may_always_be_blocked_only_where_the_mode_says_so()
    {
        var rig = new RoundRig();
        IObjectiveRoundRules rules = rig.Flow;
        Assert.False(rules.PointsMayAlwaysBeBlocked);

        rig.Mode.PointsMayAlwaysBeBlocked = true;
        Assert.True(rules.PointsMayAlwaysBeBlocked);

        rig.Slot.Current = null;
        Assert.False(rules.PointsMayAlwaysBeBlocked);
    }

    [Fact]
    public void Overtime_is_the_machine_s()
    {
        var rig = new RoundRig();
        IObjectiveRoundRules rules = rig.Flow;

        rig.Flow.SetOvertime(true);

        Assert.True(rules.InOvertime);
    }

    [Fact]
    public void A_capture_area_s_wave_change_reaches_the_waves()
    {
        var rig = new RoundRig();
        rig.Running();
        for (int i = 0; i < 7; i++)
            rig.Roster.Add(TFTeam.Red);   // eight: unscaled

        ((IObjectiveRoundRules)rig.Flow).AddTeamRespawnWaveTime(TFTeam.Red, -4f);

        Assert.Equal(6f, rig.Flow.RespawnWaveMaxLength(TFTeam.Red));
    }

    [Fact]
    public void Only_the_last_two_played_rounds_are_remembered_newest_first()
    {
        var history = new RoundHistory();

        history.AddPlayedRound("round_a");
        history.AddPlayedRound("round_b");
        history.AddPlayedRound("round_c");
        history.AddPlayedRound("");   // NULL_STRING is not a round

        Assert.Equal("round_c", history.LastPlayedRound);
        Assert.True(history.IsPreviouslyPlayedRound("round_b"));
        Assert.False(history.IsPreviouslyPlayedRound("round_a"));
    }

    [Fact]
    public void A_full_map_reset_forgets_the_played_rounds_but_not_the_round_to_play_next()
    {
        var rig = new RoundRig();
        rig.Running();
        IObjectiveRoundRules rules = rig.Flow;
        rules.AddPlayedRound("round_a");
        rules.RoundToPlayNext = "round_b";

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);   // forceMapReset
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 20f));

        Assert.False(rules.IsPreviouslyPlayedRound("round_a"));
        Assert.Equal("", rules.LastPlayedRound);
        Assert.Equal("round_b", rules.RoundToPlayNext);
    }
}
