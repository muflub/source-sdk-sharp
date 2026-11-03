using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The Valve-map arms of the time limit (<c>TeamRoundFlow</c> over
/// <see cref="TFValveMaps"/>): stock's "&& !IsValveMap()" on the
/// <c>mp_match_end_at_timelimit</c> convar's arm
/// (<c>teamplayroundbased_gamerules.cpp:1799</c> and <c>:2778</c>) — a Valve
/// map's time limit waits for the round, and shows no HUD time-limit timer.
/// </summary>
public sealed class TFValveMapRoundTests
{
    private static RoundRig ValveRig() =>
        new(level: new FakeLevel { Map = "ctf_2fort" });

    [Fact]
    public void On_a_Valve_map_mp_match_end_at_timelimit_does_not_end_the_round()
    {
        var rig = ValveRig();
        rig.Settings.TimeLimitMinutes = 1;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Running();

        rig.RunUntil(() => false, limit: 70f);

        // State_Think_RND_RUNNING's convar arm (:1799) is off on a Valve map:
        // the round runs on (the stalemate the arm would force never comes).
        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);
    }

    [Fact]
    public void On_a_Valve_map_the_map_reset_spawns_no_time_limit_timer()
    {
        var rig = ValveRig();
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.RestartGame = 1;

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        // CreateTimeLimitTimer's bAllowStalemate (:2778-2780) is false on a
        // Valve map: no HUD timer for a limit the round will not end at.
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
    }

    [Fact]
    public void On_a_Valve_map_a_live_mp_timelimit_change_touches_no_timer()
    {
        var rig = ValveRig();
        rig.Settings.MatchEndAtTimeLimit = true;

        rig.Settings.ChangeTimeLimit(0, 5);

        // HandleTimeLimitChange asks CreateTimeLimitTimer, whose guard answers
        // no on a Valve map (:2778); the else arm has no timer to remove.
        Assert.Equal(0, rig.World.Calls.Count(c => c.StartsWith("timelimit_timer", StringComparison.Ordinal)));
        Assert.Equal(0, rig.World.Count("remove_timelimit_timer"));
    }

    [Fact]
    public void On_a_map_that_is_not_Valves_the_time_limit_still_ends_the_round()
    {
        var rig = new RoundRig(level: new FakeLevel { Map = "ss_barrel" });
        rig.Settings.TimeLimitMinutes = 1;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Running();
        rig.Scores.AddScore(TFTeam.Red, 2);
        rig.Scores.AddScore(TFTeam.Blue, 1);

        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 70f));

        // The control arm: off the Valve lists the convar stands (:1799).
        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Red, rig.Flow.WinningTeam);
        Assert.Equal(WinReason.TimeLimit, rig.Flow.WinReason);
    }

    [Fact]
    public void On_a_map_that_is_not_Valves_the_time_limit_timer_still_spawns()
    {
        var rig = new RoundRig(level: new FakeLevel { Map = "ss_barrel" });
        rig.Running();
        rig.Settings.TimeLimitMinutes = 5;
        rig.Settings.MatchEndAtTimeLimit = true;
        rig.Settings.RestartGame = 1;

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround, limit: 5f));

        Assert.Equal(1, rig.World.Count("timelimit_timer 300"));
    }

    [Fact]
    public void A_stalemate_allowance_still_ends_a_Valve_maps_round()
    {
        var rig = ValveRig();
        rig.Settings.TimeLimitMinutes = 1;
        rig.Flow.AllowStalemateAtTimelimit = true;
        rig.Running();

        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning, limit: 70f));

        // m_bAllowStalemateAtTimelimit is the arm's first half (:1799): it is
        // not the convar's, so the Valve-map guard does not reach it.
        Assert.NotEqual(RoundState.RoundRunning, rig.Flow.State);
    }
}
