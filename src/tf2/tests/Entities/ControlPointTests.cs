using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// team_control_point, team_control_point_master and team_control_point_round
/// (stage 2.4): ownership, the win the master declares through
/// ITeamRoundControl, the round reset, the linear-capture rule and the
/// mini-round choice.
/// </summary>
public sealed class ControlPointTests
{
    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;

    private static (ObjectiveRig Rig, TeamControlPoint A, TeamControlPoint B, TeamControlPointMaster Master) TwoPoints(
        params (string Key, string Value)[] masterKeys)
    {
        var rig = new ObjectiveRig();
        var a = rig.Point("cp_a", 0, TFTeam.Red);
        var b = rig.Point("cp_b", 1, TFTeam.Blue);
        var master = rig.Master(masterKeys);
        rig.StartRound();
        return (rig, a, b, master);
    }

    [Fact]
    public void The_master_declares_a_win_when_one_team_owns_every_point()
    {
        var (rig, _, b, master) = TwoPoints();
        Assert.Empty(rig.Control.Wins);

        b.SetOwner(Red, makeSound: true, [1]);

        Assert.Equal([(TFTeam.Red, WinReason.AllPointsCaptured, true, false)], rig.Control.Wins);
        Assert.Equal(1, master.OnWonByTeam1.FireCount);
    }

    [Fact]
    public void The_rounds_no_reset_win_reaches_the_master_through_the_world()
    {
        var (rig, a, _, master) = TwoPoints();
        var world = new CoreRoundWorld(new NoCleanup(), rig.Registry.Directory, rig.Registry, TestServices.Fake(),
                                       new RoundTimers(rig.Registry), rig.Services, new RecordingLog());

        Assert.True(world.SetWinnerAndForceCaps(TFTeam.Blue));

        // The caps forced and the win the master's: stock's handoff arm
        // (teamplayroundbased_gamerules.cpp:2235-2248) as the input it is.
        Assert.Equal(Blue, a.Owner);
        Assert.Contains((TFTeam.Blue, WinReason.AllPointsCaptured, true, false), rig.Control.Wins);
    }

    [Fact]
    public void The_win_carries_switch_teams()
    {
        var (rig, _, b, _) = TwoPoints(("switch_teams", "1"));

        b.SetOwner(Red, makeSound: true, [1]);

        Assert.True(rig.Control.Wins.Single().SwitchTeams);
    }

    [Fact]
    public void A_team_the_master_restricts_does_not_win_by_capping()
    {
        var (rig, _, b, _) = TwoPoints(("cpm_restrict_team_cap_win", "2"));

        b.SetOwner(Red, makeSound: true, [1]);

        Assert.Equal(Red, b.Owner);
        Assert.Empty(rig.Control.Wins);
    }

    [Fact]
    public void The_cpm_think_checks_for_a_win_while_points_may_be_captured()
    {
        var (rig, a, b, master) = TwoPoints();
        rig.Round.State = RoundState.Preround;
        // The owner change asks for the win itself, whatever the state (stock's
        // CheckWinConditions does not ask whether points may be captured):
        // clear it, to see the think's own check.
        b.ForceOwner(Red);
        rig.Control.Wins.Clear();

        master.CpmThink();
        Assert.Empty(rig.Control.Wins);

        rig.Round.State = RoundState.RoundRunning;
        master.CpmThink();
        Assert.Single(rig.Control.Wins);
        _ = a;
    }

    [Fact]
    public void One_group_held_wins_while_another_is_contested()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0, TFTeam.Red, group: 0);
        rig.Point("cp_b", 1, TFTeam.Blue, group: 0);
        rig.Point("cp_c", 2, TFTeam.Red, group: 1);
        var d = rig.Point("cp_d", 3, TFTeam.Blue, group: 1);
        rig.Master();
        rig.StartRound();

        d.SetOwner(Red, makeSound: true, [1]);

        Assert.Equal(TFTeam.Red, rig.Control.Wins.Single().Team);
    }

    [Fact]
    public void The_master_numbers_the_points_from_zero_by_point_index()
    {
        var rig = new ObjectiveRig();
        var high = rig.Point("cp_high", 5);
        var low = rig.Point("cp_low", 2);
        rig.Master();
        rig.StartRound();

        Assert.Equal(0, low.PointIndex);
        Assert.Equal(1, high.PointIndex);
        Assert.Equal(2, rig.Objectives.NumControlPoints);
    }

    [Fact]
    public void The_master_writes_each_point_into_the_table()
    {
        var (rig, a, b, _) = TwoPoints();

        Assert.Equal(Red, rig.Objectives.GetOwningTeam(a.PointIndex));
        Assert.Equal(Blue, rig.Objectives.GetOwningTeam(b.PointIndex));
        // red owns the low end, blue the high one (GetBaseControlPoint).
        Assert.Equal(0, rig.Objectives.GetBaseControlPointForTeam(Red));
        Assert.Equal(1, rig.Objectives.GetBaseControlPointForTeam(Blue));
    }

    [Fact]
    public void A_capture_changes_the_tables_owner()
    {
        var (rig, _, b, _) = TwoPoints(("cpm_restrict_team_cap_win", "1"));
        int before = rig.Objectives.GetOwningTeam(b.PointIndex);

        b.SetOwner(Red, makeSound: true, [4]);

        Assert.NotEqual(before, rig.Objectives.GetOwningTeam(b.PointIndex));
        Assert.Contains("captured 1 2 4", rig.Events.Fired);
    }

    [Fact]
    public void Linear_capture_needs_the_previous_point()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0, TFTeam.Red);
        rig.Point("cp_b", 1);
        rig.Point("cp_c", 2, TFTeam.Blue);
        rig.Master();
        rig.StartRound();

        Assert.True(rig.Rules.TeamMayCapturePoint(Red, 1));
        Assert.False(rig.Rules.TeamMayCapturePoint(Red, 2));

        rig.Settings.CapLinear = false;
        Assert.True(rig.Rules.TeamMayCapturePoint(Red, 2));
    }

    [Fact]
    public void A_locked_point_may_not_be_captured_until_it_unlocks()
    {
        var (rig, _, b, _) = TwoPoints();
        rig.Settings.CapLinear = true;
        // RoundActivate already fired OnUnlocked once: stock fires it for every unlocked point there.
        int unlockedBefore = b.OnUnlocked.FireCount;
        rig.Registry.Input(b.Handle, "SetLocked", "1", 1f, EntityHandle.None);
        Assert.True(b.IsLocked);
        Assert.False(rig.Rules.TeamMayCapturePoint(Red, b.PointIndex));

        rig.Clock.Now = 10f;
        rig.Registry.Input(b.Handle, "SetUnlockTime", "5", 5f, EntityHandle.None);
        rig.Clock.Now = 14f;
        b.UnlockThink();
        Assert.True(b.IsLocked);

        rig.Clock.Now = 15.5f;
        b.UnlockThink();
        Assert.False(b.IsLocked);
        Assert.Equal(unlockedBefore + 1, b.OnUnlocked.FireCount);
        Assert.Contains("unlocked 1", rig.Events.Fired);
    }

    [Fact]
    public void Locking_is_refused_while_waiting_for_players()
    {
        var (rig, _, b, _) = TwoPoints();
        rig.Round.InWaitingForPlayers = true;

        rig.Registry.Input(b.Handle, "SetLocked", "1", 1f, EntityHandle.None);

        Assert.False(b.IsLocked);
    }

    [Fact]
    public void A_master_scoring_per_capture_gives_the_team_a_point()
    {
        var (rig, _, b, _) = TwoPoints(("score_style", "1"), ("cpm_restrict_team_cap_win", "1"));

        b.SetOwner(Red, makeSound: true, [1]);

        Assert.Equal([(TFTeam.Red, 1)], rig.RoundRules.Scores);
    }

    [Fact]
    public void The_highest_priority_playable_round_is_played_and_its_win_keeps_the_map()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0, TFTeam.Red);
        var b = rig.Point("cp_b", 1, TFTeam.Blue);
        rig.Point("cp_c", 2, TFTeam.Blue);
        var first = rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_1"),
                                                     ("cpr_cp_names", "cp_a cp_b"), ("cpr_priority", "1"));
        var second = rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_2"),
                                                      ("cpr_cp_names", "cp_b cp_c"), ("cpr_priority", "0"));
        var master = rig.Master();
        rig.StartRound();

        Assert.Same(first, master.CurrentRound);
        Assert.Equal(1, first.OnStart.FireCount);
        Assert.True(rig.Objectives.IsInMiniRound(0));
        Assert.False(rig.Objectives.IsInMiniRound(2));
        Assert.Contains("round round_1", rig.Events.Fired);

        b.SetOwner(Red, makeSound: true, [1]);

        // round_2 is still playable (blue owns cp_c): no full reset.
        var win = rig.Control.Wins.Single();
        Assert.Equal(TFTeam.Red, win.Team);
        Assert.False(win.ForceMapReset);
        Assert.Equal(1, first.OnWonByTeam1.FireCount);
        _ = second;
    }

    [Fact]
    public void A_round_names_its_points_by_whole_names()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0);
        rig.Point("cp_ab", 1);
        var round = rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "r"), ("cpr_cp_names", "cp_AB"));
        rig.Master();
        rig.StartRound();

        Assert.Equal(["cp_ab"], round.ControlPoints.Select(p => p.Name));
    }

    [Fact]
    public void SetWinner_with_no_team_is_a_stalemate()
    {
        var (rig, _, _, master) = TwoPoints();

        rig.Registry.Input(master.Handle, "SetWinner", "0", 0f, EntityHandle.None);

        Assert.Equal([StalemateReason.Timer], rig.Control.Stalemates);
    }

    [Fact]
    public void The_final_checkpoint_wins_the_round_through_the_master()
    {
        var rig = new ObjectiveRig();
        var first = rig.Point("cp_first", 0, TFTeam.Blue);
        var final = rig.Point("cp_final", 1, TFTeam.Red);
        rig.Area("cp_first", 10f);
        rig.Area("cp_final", 10f);
        var master = rig.Master();
        rig.StartRound();

        // the attackers have pushed through the first; the final is still the
        // defenders', so no one owns the whole map yet
        first.ForceOwner((int)TFTeam.Blue);
        final.ForceOwner((int)TFTeam.Red);
        master.CheckWinConditions();
        Assert.Empty(rig.Control.Wins);

        // the final checkpoint falls: the owner change reaches the master through
        // the point, and the attackers hold the whole map — the win rides
        // ITeamRoundControl
        final.ForceOwner((int)TFTeam.Blue);
        Assert.Single(rig.Control.Wins);
        Assert.Equal(TFTeam.Blue, rig.Control.Wins[0].Team);
        Assert.Equal(WinReason.AllPointsCaptured, rig.Control.Wins[0].Reason);
    }

    [Fact]
    public void A_capture_area_adjusts_the_respawn_waves_on_a_capture()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0, TFTeam.Blue);
        var area = rig.Spawn<TriggerAreaCapture>(TriggerAreaCapture.MapClass, ("area_cap_point", "cp_a"), ("area_time_to_cap", "1"),
                                                 ("team_cancap_2", "1"), ("team_spawn_2", "-5"), ("team_spawn_3", "3"));
        rig.Master(("cpm_restrict_team_cap_win", "1"));
        rig.StartRound();
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 2.5f);

        Assert.Equal([(TFTeam.Blue, -3f), (TFTeam.Red, -5f)], rig.RoundRules.WaveTimes);
    }

    /// <summary>A map cleanup these facts never ask: they are about the master's dispatch.</summary>
    private sealed class NoCleanup : IMapCleanup
    {
        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter) => default;
    }
}
