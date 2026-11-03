extern alias client;

using SourceSharp.Abi;
using Xunit;

using Limits = client::TF2.Shared.Resources.ObjectiveLimits;
using State = client::TF2.Shared.Resources.ObjectiveResourceNetState;
using Team = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The client half of <c>tf_objective_resource</c>: the decoded table stock's
/// <c>C_BaseTeamObjectiveResource</c> getters read, and the cap-time model its
/// receive proxies and <c>ClientThink</c> kept client-side.
/// </summary>
public sealed class ClientObjectiveResourceTests
{
    private const int Blue = (int)Team.Blue;
    private const int Red = (int)Team.Red;
    private const int Unassigned = (int)Team.Unassigned;

    [Fact]
    public void A_packet_decodes_the_table_the_client_reads()
    {
        var rig = new ObjectiveRig();

        Assert.Equal(0, rig.Source.NumControlPoints);
        Assert.Equal(Unassigned, rig.Source.OwningTeam(1));
        Assert.Equal(string.Empty, rig.Source.CapLayoutInHud);
        Assert.False(rig.Source.IsVisible(1));
        Assert.Equal(-1, rig.Source.Group(1));
        Assert.Equal(-1, rig.Source.PreviousPoint(1, Blue, 0));
        Assert.Equal(-1, rig.Source.BaseControlPointForTeam(Blue));
        Assert.Equal(Vector.Zero, rig.Source.Position(1));

        rig.Arrive(s =>
        {
            s.NumControlPoints = 3;
            s.TimerToShowInHud = 42;
            s.StopWatchTimer = 43;
            s.PlayingMiniRounds = true;
            s.ControlPointsReset = true;
            s.UpdateCapHudParity = 7;
            s.CapLayoutInHud = "abc";
            s.CustomPositionX = 11f;
            s.CustomPositionY = 12f;

            s.Owner[1] = (byte)Team.Blue;
            s.CappingTeam[1] = (byte)Team.Red;
            s.TeamInZone[1] = (byte)Team.Blue;
            s.Positions[3] = 10f;
            s.Positions[4] = 20f;
            s.Positions[5] = 30f;
            s.Visible[1] = true;
            s.Blocked[1] = true;
            s.Locked[1] = true;
            s.InMiniRound[1] = true;
            s.WarnOnCap[1] = 2;
            s.Group[1] = 3;

            int blue = Limits.TeamArray(1, Blue);
            s.TeamIcons[blue] = 9;
            s.TeamOverlays[blue] = 10;
            s.TeamReqCappers[blue] = 2;
            s.TeamCapTime[blue] = 22f;
            s.TeamCanCap[blue] = true;
            s.NumTeamMembers[blue] = 1;
            s.PreviousPoints[Limits.PreviousArray(1, Blue, 0)] = 2;

            s.TeamBaseIcons[Blue] = 5;
            s.BaseControlPoints[Blue] = 1;
        });

        Assert.Equal(3, rig.Source.NumControlPoints);
        Assert.Equal(42, rig.Source.TimerToShowInHud);
        Assert.Equal(43, rig.Source.StopWatchTimer);
        Assert.True(rig.Source.PlayingMiniRounds);
        Assert.True(rig.Source.ControlPointsReset);
        Assert.Equal(7, rig.Source.UpdateCapHudParity);
        Assert.Equal("abc", rig.Source.CapLayoutInHud);
        Assert.Equal(11f, rig.Source.CustomPositionX, 3);
        Assert.Equal(12f, rig.Source.CustomPositionY, 3);

        Assert.Equal(Blue, rig.Source.OwningTeam(1));
        Assert.Equal(Red, rig.Source.CappingTeam(1));
        Assert.Equal(Blue, rig.Source.TeamInZone(1));
        Vector position = rig.Source.Position(1);
        Assert.Equal(10f, position.X, 3);
        Assert.Equal(20f, position.Y, 3);
        Assert.Equal(30f, position.Z, 3);
        Assert.True(rig.Source.IsVisible(1));
        Assert.True(rig.Source.IsBlocked(1));
        Assert.True(rig.Source.IsLocked(1));
        Assert.True(rig.Source.InMiniRound(1));
        Assert.Equal(2, rig.Source.WarningLevel(1));
        Assert.Equal(3, rig.Source.Group(1));

        Assert.Equal(9, rig.Source.IconForTeam(1, Blue));
        Assert.Equal(10, rig.Source.OverlayForTeam(1, Blue));
        Assert.Equal(2, rig.Source.RequiredCappers(1, Blue));
        Assert.Equal(22f, rig.Source.TeamCapTime(1, Blue), 3);
        Assert.True(rig.Source.TeamCanCap(1, Blue));
        Assert.Equal(1, rig.Source.NumPlayersInArea(1, Blue));
        Assert.Equal(2, rig.Source.PreviousPoint(1, Blue, 0));
        Assert.Equal(5, rig.Source.BaseIconForTeam(Blue));
        Assert.Equal(1, rig.Source.BaseControlPointForTeam(Blue));

        Assert.Equal(1, rig.Entity.Decodes);
        Assert.Same(rig.Entity, rig.Source.Live);
    }

    [Fact]
    public void The_point_getters_answer_unassigned_past_the_point_count()
    {
        var rig = new ObjectiveRig();

        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.Owner[5] = (byte)Team.Red;
            s.CappingTeam[5] = (byte)Team.Red;
            s.TeamInZone[5] = (byte)Team.Red;
        });

        Assert.Equal(Unassigned, rig.Source.OwningTeam(5));
        Assert.Equal(Unassigned, rig.Source.CappingTeam(5));
        Assert.Equal(Unassigned, rig.Source.TeamInZone(5));

        rig.Arrive(s => s.NumControlPoints = 6);

        Assert.Equal(Red, rig.Source.OwningTeam(5));
        Assert.Equal(Red, rig.Source.CappingTeam(5));
        Assert.Equal(Red, rig.Source.TeamInZone(5));
    }

    [Fact]
    public void A_cap_in_progress_seeds_from_the_lazy_percentage()
    {
        var rig = new ObjectiveRig();
        Assert.Equal(0f, rig.Source.CapProgress(0, 0f), 3);

        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 1), now: 0f);

        Assert.Equal(0.5f, rig.Source.CapProgress(0, 0f), 3);
    }

    [Fact]
    public void A_capping_team_counts_the_cap_down_by_the_elapsed_time()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 1), now: 0f);

        Assert.Equal(0.6f, rig.Source.CapProgress(0, 2f), 3);
    }

    [Fact]
    public void Extra_cappers_speed_a_scaled_cap()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 2, scalesWithPlayers: true), now: 0f);

        // Two cappers, mp_capstyle 1: elapsed plus a half share for the second.
        Assert.Equal(0.65f, rig.Source.CapProgress(0, 2f), 3);
    }

    [Fact]
    public void Cap_style_zero_ignores_extra_cappers()
    {
        var rig = new ObjectiveRig(new SettingsFake { CapStyle = 0 });
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 2, scalesWithPlayers: true), now: 0f);

        Assert.Equal(0.6f, rig.Source.CapProgress(0, 2f), 3);
    }

    [Fact]
    public void A_blocked_point_holds_its_progress()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 0), now: 0f);
        rig.Arrive(s => s.Blocked[0] = true, now: 5f);

        Assert.Equal(0.5f, rig.Source.CapProgress(0, 5f), 3);
    }

    [Fact]
    public void A_cap_with_no_cappers_deteriorates()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 0), now: 0f);

        // Not scaled with players: the whole cap falls back over one cap length.
        Assert.Equal(0.35f, rig.Source.CapProgress(0, 3f), 3);
    }

    [Fact]
    public void A_scaled_cap_deteriorates_over_the_deteriorate_time()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 0, scalesWithPlayers: true), now: 0f);

        // mp_capdeteriorate_time 90: three seconds of a twenty-second cap is 0.667 s of progress.
        Assert.Equal(0.467f, rig.Source.CapProgress(0, 3f), 3);
    }

    [Fact]
    public void A_point_that_may_not_be_captured_wipes_its_cap_time()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 0, locked: true), now: 0f);

        // Stock wipes m_flCapTimeLeft to 0; GetCPCapPercentage then reads the
        // full elapsed time (c_team_objectiveresource.cpp:264-279, :448-451).
        Assert.Equal(1f, rig.Source.CapProgress(0, 1f), 3);
        Assert.False(rig.Source.CapIsBlocked(0));
    }

    [Fact]
    public void A_capping_team_change_seeds_the_full_cap_time()
    {
        var rig = new ObjectiveRig();
        Assert.Equal(0f, rig.Source.CapProgress(0, 0f), 3);

        rig.Arrive(s => Seed(s, lazyPercent: 0f, cappers: 1), now: 0f);

        Assert.Equal(0f, rig.Source.CapProgress(0, 0f), 3);
        Assert.Equal(0.1f, rig.Source.CapProgress(0, 2f), 3);
    }

    [Fact]
    public void A_capture_by_the_capping_team_clears_the_cap()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 1), now: 0f);
        Assert.Equal(0.5f, rig.Source.CapProgress(0, 0f), 3);

        // The server's SetOwningTeam clears the capping team in the same packet.
        rig.Arrive(s =>
        {
            s.Owner[0] = (byte)Team.Blue;
            s.CappingTeam[0] = (byte)Team.Unassigned;
        }, now: 1f);

        Assert.Equal(0f, rig.Source.CapProgress(0, 1f), 3);
    }

    [Fact]
    public void A_cap_with_time_left_and_no_cappers_is_blocked()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => Seed(s, lazyPercent: 0.5f, cappers: 0), now: 0f);
        Assert.True(rig.Source.CapIsBlocked(0));

        rig.Arrive(s => s.NumTeamMembers[Limits.TeamArray(0, Blue)] = 1, now: 0f);
        Assert.False(rig.Source.CapIsBlocked(0));
    }

    [Fact]
    public void A_packet_decodes_the_train_fields()
    {
        var rig = new ObjectiveRig();
        Assert.False(rig.Source.HillData(Blue, 1, out _, out _));

        int hill = 1 * Limits.TrainFloatsPerHill + Blue * Limits.TrainMaxHills * Limits.TrainFloatsPerHill;
        rig.Arrive(s =>
        {
            s.NumControlPoints = 2;
            s.PathDistance[1] = 0.75f;
            s.NumNodeHillData[Blue] = 3;
            s.NodeHillData[hill] = 1.5f;
            s.NodeHillData[hill + 1] = 2.5f;
            s.HillIsDownhill[1 + Blue * Limits.TrainMaxHills] = true;
            s.TrackAlarm[Blue] = true;
            s.TrainProgress[Blue] = 0.4f;
            s.TrainCappers[Blue] = -1;
            s.TrainSpeedLevel[Blue] = 3;
            s.TrainRecedeTime[Blue] = 30.5f;
        });

        Assert.Equal(0.75f, rig.Source.PathDistance(1), 3);
        Assert.Equal(3, rig.Source.NumNodeHillData(Blue));
        Assert.True(rig.Source.HillData(Blue, 1, out float start, out float end));
        Assert.Equal(1.5f, start, 3);
        Assert.Equal(2.5f, end, 3);
        Assert.True(rig.Source.IsHillDownhill(Blue, 1));
        Assert.True(rig.Source.TrackAlarm(Blue));
        Assert.Equal(0.4f, rig.Source.TrainProgress(Blue), 3);
        Assert.Equal(-1, rig.Source.TrainCappers(Blue));
        Assert.Equal(3, rig.Source.TrainSpeedLevel(Blue));
        Assert.Equal(30.5f, rig.Source.TrainRecedeTime(Blue), 3);
    }

    [Fact]
    public void The_train_getters_guard_past_their_runs()
    {
        var rig = new ObjectiveRig();
        rig.Arrive(s => s.NumControlPoints = 1);

        Assert.False(rig.Source.HillData(Blue, Limits.TrainMaxHills, out _, out _));
        Assert.False(rig.Source.HillData(Limits.TrainMaxTeams, 0, out _, out _));
        Assert.True(rig.Source.IsHillDownhill(Blue, Limits.TrainMaxHills));
        Assert.Equal(0, rig.Source.NumNodeHillData(Limits.TrainMaxTeams));
        Assert.False(rig.Source.TrackAlarm(Limits.TrainMaxTeams));
        Assert.Equal(0f, rig.Source.TrainProgress(Limits.TrainMaxTeams), 3);
        Assert.Equal(0, rig.Source.TrainCappers(Limits.TrainMaxTeams));
        Assert.Equal(0, rig.Source.TrainSpeedLevel(Limits.TrainMaxTeams));
        Assert.Equal(0f, rig.Source.TrainRecedeTime(Limits.TrainMaxTeams), 3);
    }

    [Fact]
    public void A_packet_decodes_the_flag_fields()
    {
        var rig = new ObjectiveRig();
        Assert.Equal(0, rig.Source.FlagStatus(Blue));
        Assert.Equal(0, rig.Source.FlagCarrier(Blue));
        Assert.Equal(0f, rig.Source.FlagResetTime(Blue), 3);
        Assert.False(rig.Source.HasFlag(Blue));

        rig.Arrive(s =>
        {
            s.FlagStatus[Blue] = 1;
            s.FlagCarrier[Blue] = 17;
            s.FlagResetTime[Blue] = 12.5f;
            s.HasFlag[Blue] = true;
            s.FlagOrigin[Blue * 3] = 11f;
            s.FlagOrigin[Blue * 3 + 1] = 22f;
            s.FlagOrigin[Blue * 3 + 2] = 33f;
            s.ZoneOrigin[Red * 3] = -1f;
            s.ZoneOrigin[Red * 3 + 1] = -2f;
            s.ZoneOrigin[Red * 3 + 2] = -3f;
        });

        Assert.Equal(1, rig.Source.FlagStatus(Blue));
        Assert.Equal(17, rig.Source.FlagCarrier(Blue));
        Assert.Equal(12.5f, rig.Source.FlagResetTime(Blue), 3);
        Assert.True(rig.Source.HasFlag(Blue));
        Assert.Equal(0, rig.Source.FlagStatus(Red));
        Assert.False(rig.Source.HasFlag(Red));
        Assert.Equal(new Vector(11f, 22f, 33f), rig.Source.FlagOrigin(Blue));
        Assert.Equal(new Vector(-1f, -2f, -3f), rig.Source.ZoneOrigin(Red));
    }

    [Fact]
    public void The_table_counts_the_points_a_team_owns()
    {
        var rig = new ObjectiveRig();
        Assert.Equal(0, rig.Source.OwnedControlPoints(Blue));

        rig.Arrive(s =>
        {
            s.NumControlPoints = 3;
            s.Owner[0] = (byte)Team.Blue;
            s.Owner[1] = (byte)Team.Blue;
            s.Owner[2] = (byte)Team.Red;
        });

        Assert.Equal(2, rig.Source.OwnedControlPoints(Blue));
        Assert.Equal(1, rig.Source.OwnedControlPoints(Red));
    }

    [Fact]
    public void The_source_is_live_from_its_first_update_to_its_removal()
    {
        var rig = new ObjectiveRig();
        Assert.Null(rig.Source.Live);

        rig.Entity.OnSpawn();
        Assert.Same(rig.Entity, rig.Source.Live);

        rig.Arrive(s => s.NumControlPoints = 1);
        Assert.Same(rig.Entity, rig.Source.Live);
        Assert.Equal(1, rig.Entity.Decodes);

        rig.Entity.OnRemoved();
        Assert.Null(rig.Source.Live);
    }

    /// <summary>
    /// A point mid-capture with <paramref name="cappers"/> of Blue on it and
    /// <paramref name="lazyPercent"/> of the cap time left, which is what the
    /// server's <c>SetCPCapPercentage</c> networks (trigger_area_capture.cpp:665-685).
    /// </summary>
    private static void Seed(State state, float lazyPercent, int cappers, bool scalesWithPlayers = false, bool locked = false)
    {
        state.NumControlPoints = 1;
        state.CappingTeam[0] = (byte)Team.Blue;
        state.TeamInZone[0] = (byte)Team.Blue;
        state.NumTeamMembers[Limits.TeamArray(0, Blue)] = (byte)cappers;
        state.TeamCapTime[Limits.TeamArray(0, Blue)] = 20f;
        state.LazyCapPerc[0] = lazyPercent;
        state.CapRateScalesWithPlayers[0] = scalesWithPlayers;
        state.Locked[0] = locked;

        for (int previous = 0; previous < Limits.MaxPreviousPoints; previous++)
            state.PreviousPoints[Limits.PreviousArray(0, Blue, previous)] = -1;
    }
}
