using SourceSharp.Abi;
using TF2.Server.Entities;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// trigger_capture_area's capture (stage 2.4): CaptureThink's arithmetic,
/// trigger_area_capture.cpp L304-664, driven on a fake clock in half-second
/// thinks (every value below is exact in binary).
///
/// <para>
/// The stock formula. A capture's total time is <c>area_time_to_cap</c> T
/// with mp_capstyle 0, and <c>T * 2 * team_numcap</c> with mp_capstyle 1.
/// Each think takes <c>reduction</c> off the time remaining, where for
/// mp_capstyle 1 <c>reduction = dt * (1 + 1/2 + ... + 1/n)</c> for n cappers
/// ("diminishing returns for successive players", L473-477) and
/// <c>reduction = dt</c> otherwise. So with mp_capstyle 1 and T = 6: one
/// capper takes 12 s, two take 12 / 1.5 = 8 s, three 12 / (11/6) = 6.545 s.
/// An abandoned capture deteriorates by <c>total / mp_capdeteriorate_time * dt</c>
/// (mp_capstyle 1) or <c>dt</c> (mp_capstyle 0) per think, six times faster in
/// overtime, and breaks once it is back to the full time (L577-596, L626-633).
/// </para>
/// </summary>
public sealed class AreaCaptureTests
{
    private static (ObjectiveRig Rig, TeamControlPoint Point, TriggerAreaCapture Area) Neutral(float capTime, int capStyle,
                                                                                                TFTeam owner = TFTeam.Unassigned,
                                                                                                float partialRate = 0f)
    {
        var rig = new ObjectiveRig();
        rig.Settings.CapStyle = capStyle;
        var point = rig.Point("cp_a", 0, owner);
        var area = rig.Area("cp_a", capTime);
        rig.Master(("partial_cap_points_rate",
                    partialRate.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        rig.StartRound();
        return (rig, point, area);
    }

    /// <summary>Thinks every 0.5 s from <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    private static void Run(ObjectiveRig rig, TriggerAreaCapture area, float from, float to)
    {
        for (float t = from; t <= to; t += 0.5f)
            rig.ThinkAt(area, t);
    }

    [Fact]
    public void One_capper_reaches_the_point_at_the_cap_time_with_mp_capstyle_0()
    {
        var (rig, point, area) = Neutral(capTime: 10f, capStyle: 0);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red); // StartTouch thinks at once: the capture starts at t = 0
        Assert.True(area.IsCapturing);

        Run(rig, area, 0.5f, 9.5f);
        Assert.Equal((int)TFTeam.Unassigned, point.Owner);
        Assert.Equal(0.5f, area.TimeRemaining);

        rig.ThinkAt(area, 10f);
        Assert.Equal((int)TFTeam.Red, point.Owner);
        Assert.False(area.IsCapturing);
    }

    [Fact]
    public void One_capper_takes_twice_the_cap_time_per_required_capper_with_mp_capstyle_1()
    {
        var (rig, point, area) = Neutral(capTime: 6f, capStyle: 1);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);
        Assert.Equal(12f, area.TimeRemaining);

        Run(rig, area, 0.5f, 11.5f);
        Assert.Equal((int)TFTeam.Unassigned, point.Owner);

        rig.ThinkAt(area, 12f);
        Assert.Equal((int)TFTeam.Red, point.Owner);
    }

    [Fact]
    public void Two_cappers_capture_faster_by_the_diminishing_return()
    {
        var (rig, point, area) = Neutral(capTime: 6f, capStyle: 1);
        var red1 = rig.Players.Add(1, TFTeam.Red);
        var red2 = rig.Players.Add(2, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red1);
        rig.Registry.Touch(area.Handle, red2);

        // 0.5 + 0.5/2 = 0.75 a think: 12 s of capture in 16 thinks, 8 s.
        rig.ThinkAt(area, 0.5f);
        Assert.Equal(11.25f, area.TimeRemaining);

        Run(rig, area, 1f, 7.5f);
        Assert.Equal((int)TFTeam.Unassigned, point.Owner);

        rig.ThinkAt(area, 8f);
        Assert.Equal((int)TFTeam.Red, point.Owner);
    }

    [Fact]
    public void Three_cappers_add_a_third()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        for (int i = 1; i <= 3; i++)
            rig.Registry.Touch(area.Handle, rig.Players.Add(i, TFTeam.Red));

        rig.ThinkAt(area, 0.5f);

        // 12 - 0.5 * (1 + 1/2 + 1/3)
        Assert.Equal(12f - (0.5f + 0.5f / 2f + 0.5f / 3f), area.TimeRemaining, 5);
    }

    [Fact]
    public void Cappers_count_only_without_diminishing_returns_with_mp_capstyle_0()
    {
        var (rig, _, area) = Neutral(capTime: 10f, capStyle: 0);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Red));

        rig.ThinkAt(area, 0.5f);

        Assert.Equal(9.5f, area.TimeRemaining);
    }

    [Fact]
    public void An_enemy_on_the_point_blocks_the_capture_with_mp_blockstyle_1()
    {
        var (rig, point, area) = Neutral(capTime: 6f, capStyle: 1);
        var red = rig.Players.Add(1, TFTeam.Red);
        var blue = rig.Players.Add(2, TFTeam.Blue);

        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 2f);
        Assert.Equal(10f, area.TimeRemaining);

        rig.Clock.Now = 2.5f;
        rig.Registry.Touch(area.Handle, blue);

        Run(rig, area, 3f, 6f);

        Assert.True(area.IsCapturing);
        Assert.True(area.IsBlocked);
        Assert.True(rig.Objectives.IsCapBlocked(0));
        Assert.Equal(10f, area.TimeRemaining);
        Assert.Equal((int)TFTeam.Unassigned, point.Owner);
    }

    [Fact]
    public void An_enemy_breaks_the_capture_with_mp_blockstyle_0()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        rig.Settings.BlockStyle = 0;
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        rig.ThinkAt(area, 0.5f);

        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Blue));

        Assert.False(area.IsCapturing);
        Assert.Contains("broken 0", rig.Events.Fired);
    }

    [Fact]
    public void A_blocker_past_half_way_is_credited_once_per_attempt()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        var red = rig.Players.Add(1, TFTeam.Red);
        var blue = rig.Players.Add(2, TFTeam.Blue);
        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 6.5f); // 5.5 s of 12 left: under half to go

        rig.Clock.Now = 7f;
        rig.Registry.Touch(area.Handle, blue);
        Run(rig, area, 7.5f, 8f);

        Assert.Single(rig.Events.Fired, e => e.StartsWith("blocked 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaving_the_point_lets_the_capture_deteriorate_and_break()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        rig.Settings.CapDeteriorateTime = 24f;
        var red = rig.Players.Add(1, TFTeam.Red);
        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 4f);
        Assert.Equal(8f, area.TimeRemaining);

        rig.Registry.EndTouch(area.Handle, red);

        // total / mp_capdeteriorate_time * dt = 12 / 24 * 0.5 = 0.25 a think.
        rig.ThinkAt(area, 4.5f);
        Assert.Equal(8.25f, area.TimeRemaining);
        Assert.True(area.IsCapturing);

        // Back to 12 after 16 thinks: the capture breaks.
        Run(rig, area, 5f, 12f);
        Assert.False(area.IsCapturing);
        Assert.Contains("broken 0", rig.Events.Fired);
    }

    [Fact]
    public void Overtime_deteriorates_six_times_faster()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        rig.Settings.CapDeteriorateTime = 24f;
        var red = rig.Players.Add(1, TFTeam.Red);
        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 4f);
        rig.Registry.EndTouch(area.Handle, red);
        rig.RoundRules.InOvertime = true;

        // 0.25 a think, times six.
        rig.ThinkAt(area, 4.5f);
        Assert.Equal(9.5f, area.TimeRemaining);
    }

    [Fact]
    public void A_capture_deteriorates_by_the_time_itself_with_mp_capstyle_0()
    {
        var (rig, _, area) = Neutral(capTime: 10f, capStyle: 0);
        var red = rig.Players.Add(1, TFTeam.Red);
        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 2f);
        rig.Registry.EndTouch(area.Handle, red);

        // total / total * dt.
        rig.ThinkAt(area, 2.5f);
        Assert.Equal(8.5f, area.TimeRemaining);
    }

    [Fact]
    public void The_other_team_reverses_a_capture_of_a_neutral_point()
    {
        var (rig, point, area) = Neutral(capTime: 6f, capStyle: 1);
        var red = rig.Players.Add(1, TFTeam.Red);
        var blue = rig.Players.Add(2, TFTeam.Blue);
        rig.Registry.Touch(area.Handle, red);
        Run(rig, area, 0.5f, 4f);
        Assert.Equal(8f, area.TimeRemaining);

        rig.Registry.EndTouch(area.Handle, red);
        rig.Clock.Now = 4f;
        rig.Registry.Touch(area.Handle, blue);

        // Owner unassigned and only blue in the zone: red's capture goes back at blue's rate.
        rig.ThinkAt(area, 4.5f);
        Assert.Equal(8.5f, area.TimeRemaining);
        Assert.Equal((int)TFTeam.Red, area.CapturingTeam);
        Assert.Equal((int)TFTeam.Unassigned, point.Owner);
    }

    [Fact]
    public void No_capture_starts_while_points_may_not_be_captured()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        rig.Round.InSetup = true;

        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));

        Assert.False(area.IsCapturing);
    }

    [Fact]
    public void The_owner_does_not_capture_its_own_point()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1, owner: TFTeam.Red);

        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));

        Assert.False(area.IsCapturing);
    }

    [Fact]
    public void A_dead_player_is_no_capper()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);

        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red, alive: false));

        Assert.False(area.IsCapturing);
    }

    [Fact]
    public void A_capture_tells_the_point_and_the_table()
    {
        var (rig, point, area) = Neutral(capTime: 6f, capStyle: 1);
        var red = rig.Players.Add(3, TFTeam.Red);

        int ownerBefore = rig.Objectives.GetOwningTeam(0);
        rig.Registry.Touch(area.Handle, red);
        Assert.Equal((int)TFTeam.Red, rig.Objectives.GetCappingTeam(0));
        Assert.Equal(1, rig.Objectives.GetNumPlayers(0, (int)TFTeam.Red));

        rig.ThinkAt(area, 3f);
        Assert.Equal(9f / 12f, rig.Objectives.GetCPCapPercentage(0));

        Run(rig, area, 3.5f, 12f);

        Assert.NotEqual(ownerBefore, rig.Objectives.GetOwningTeam(0));
        Assert.Equal((int)TFTeam.Red, rig.Objectives.GetOwningTeam(0));
        Assert.Equal(1, point.OnCapTeam1.FireCount);
        Assert.Equal(1, point.OnOwnerChangedToTeam1.FireCount);
        Assert.Equal(1, area.OnCapTeam1.FireCount);
        Assert.Contains("captured 0 2 3", rig.Events.Fired);
    }

    [Fact]
    public void The_num_cappers_outputs_carry_the_count_and_minus_one_when_blocked()
    {
        var (rig, _, area) = Neutral(capTime: 6f, capStyle: 1);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        Assert.Equal("1", area.OnNumCappersChanged2.LastValue);

        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Blue));

        Assert.Equal("-1", area.OnNumCappersChanged2.LastValue);
        Assert.Equal("0", area.OnNumCappersChanged.LastValue);
    }

    // ---- SetLastObjectiveTime per capper (trigger_area_capture.cpp:369 and :382) ----

    [Fact]
    public void A_cappers_objective_time_refreshes_every_think()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0);
        var red = rig.Players.Add(1, TFTeam.Red);

        Assert.Equal(999f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 0f));   // never touched

        rig.Registry.Touch(area.Handle, red);   // StartTouch thinks at once: written at t = 0
        Assert.Equal(0f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 0f), 3);

        rig.ThinkAt(area, 2f);                  // and again each think
        Assert.Equal(1f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 3f), 3);
    }

    [Fact]
    public void A_blocking_player_records_the_objective_time()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 1);
        var red = rig.Players.Add(1, TFTeam.Red);
        var blue = rig.Players.Add(2, TFTeam.Blue);
        rig.PlayerRules.Blockers.Add(blue);   // may not cap, may block: the ubered arm

        rig.Registry.Touch(area.Handle, red);
        rig.Clock.Now = 2f;
        rig.Registry.Touch(area.Handle, blue);   // its StartTouch think counts it as a blocker

        Assert.Equal(0f, rig.ObjectiveTimes.GetTimeSinceLastObjective(2, 2f), 3);   // L369
        Assert.Equal(0f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 2f), 3);   // the capper too, L382
    }

    [Fact]
    public void A_dead_player_in_the_area_records_no_objective_time()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0);
        var alive = rig.Players.Add(1, TFTeam.Red);
        var dead = rig.Players.Add(2, TFTeam.Red, alive: false);

        rig.Registry.Touch(area.Handle, alive);
        rig.Registry.Touch(area.Handle, dead);
        rig.ThinkAt(area, 4f);

        Assert.Equal(0f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 4f), 3);   // the living capper is recorded
        Assert.Equal(999f, rig.ObjectiveTimes.GetTimeSinceLastObjective(2, 4f));    // the dead one is skipped before the count
    }

    [Fact]
    public void A_player_on_a_team_that_may_not_cap_records_no_objective_time()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0);
        rig.Objectives.SetCPLocked(0, true);   // TeamMayCapturePoint answers no, so the count skips the player

        var red = rig.Players.Add(1, TFTeam.Red);
        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 4f);

        Assert.Equal(999f, rig.ObjectiveTimes.GetTimeSinceLastObjective(1, 4f));
    }

    // ---- partial-cap escort points (partial_cap_points_rate; EscortScoringThink,
    //      basemultiplayerplayer.cpp:305-352, armed from trigger_area_capture.cpp
    //      :210 and :810, the stock stop loops at :917 and :984 read the capturing
    //      team after its reset) ----

    [Fact]
    public void Escorting_the_point_scores_partial_cap_points_every_tenth_second()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);   // the capture starts at t = 0; the escort is armed

        rig.ThinkAt(area, 0.1f);   // rate 10: one point a tenth-second think
        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));
        Assert.Contains("event=player_escort_score player=1 points=1", rig.GameEvents.Fired);

        rig.ThinkAt(area, 0.2f);
        Assert.Equal(2, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    [Fact]
    public void A_player_entering_mid_capture_joins_the_escort_scoring()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        rig.ThinkAt(area, 0.1f);

        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Red));   // StartTouch's escort arm

        rig.ThinkAt(area, 0.2f);
        Assert.Equal(2, rig.Stats.Accumulated(1, TFStat.Captures));   // on its own tenth-seconds
        Assert.Equal(1, rig.Stats.Accumulated(2, TFStat.Captures));   // the newcomer, from its first tick
    }

    [Fact]
    public void Escort_scoring_arms_when_the_capture_starts_without_a_new_touch()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Objectives.SetCPLocked(0, true);   // not cappable: the touch starts no capture
        rig.Registry.Touch(area.Handle, red);
        Assert.False(area.IsCapturing);

        rig.Objectives.SetCPLocked(0, false);  // the next think starts the capture, with no touch to arm on
        rig.ThinkAt(area, 0.1f);
        Assert.True(area.IsCapturing);
        rig.ThinkAt(area, 0.2f);

        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));   // armed by StartCapture's walk of the area
    }

    [Fact]
    public void A_player_of_the_other_team_does_not_escort_the_capture()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));

        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Blue));   // blocks, but the red capture is still capturing

        rig.ThinkAt(area, 0.1f);
        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));
        Assert.Equal(0, rig.Stats.Accumulated(2, TFStat.Captures));   // never on the capturing team
    }

    [Fact]
    public void Escort_scoring_needs_a_partial_cap_rate()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0);   // partial_cap_points_rate 0
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);
        Assert.True(area.IsCapturing);   // the capture runs, only nothing is scored

        rig.ThinkAt(area, 0.5f);
        Assert.Equal(0, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    [Fact]
    public void Without_a_master_nothing_is_earned()
    {
        var rig = new ObjectiveRig();
        rig.Settings.CapStyle = 0;
        rig.Point("cp_a", 0);
        var area = rig.Area("cp_a", 20f);
        rig.StartRound();   // no team_control_point_master: no rate to score at

        var red = rig.Players.Add(1, TFTeam.Red);
        rig.Registry.Touch(area.Handle, red);
        Assert.True(area.IsCapturing);
        rig.ThinkAt(area, 0.5f);

        Assert.Equal(0, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    [Fact]
    public void Leaving_the_point_stops_the_escort_scoring()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 0.1f);
        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));

        rig.Registry.EndTouch(area.Handle, red);   // the area keeps capturing; the player does not escort

        rig.ThinkAt(area, 0.2f);
        rig.ThinkAt(area, 0.3f);
        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    [Fact]
    public void A_player_still_on_the_point_keeps_earning_after_the_capture_until_they_leave()
    {
        var (rig, point, area) = Neutral(capTime: 10f, capStyle: 0, partialRate: 10f);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);

        int ticks = 0;
        while (area.IsCapturing && ticks < 200)
        {
            ticks++;
            rig.ThinkAt(area, ticks * 0.1f);
        }

        Assert.False(area.IsCapturing);
        Assert.Equal((int)TFTeam.Red, point.Owner);   // the capture is done, and the player is still inside
        Assert.Equal(ticks, rig.Stats.Accumulated(1, TFStat.Captures));   // one point a tenth-second think

        // The stock stop loops of EndCapture and BreakCapture (L908-920, L975-987)
        // read the capturing team after EndCapture and BreakCapture reset it, so
        // they walk the unassigned team and stop nobody: the escort keeps
        // racking up points on the area's think until the end touch.
        rig.ThinkAt(area, (ticks + 1) * 0.1f);
        Assert.Equal(ticks + 1, rig.Stats.Accumulated(1, TFStat.Captures));

        rig.Registry.EndTouch(area.Handle, red);
        rig.ThinkAt(area, (ticks + 2) * 0.1f);
        Assert.Equal(ticks + 1, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    // ---- the spawn-time stop (CBaseMultiplayerPlayer::Spawn's
    //      StopScoringEscortPoints, basemultiplayerplayer.cpp:230-237; the stop
    //      itself :356-359 drops the think context and nothing else) ----

    [Fact]
    public void A_respawn_stops_the_escort_scoring()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        var red = rig.Players.Add(1, TFTeam.Red);
        var spawns = new TFEscortSpawnStop(rig.Entities);

        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 0.1f);
        rig.ThinkAt(area, 0.2f);
        Assert.Equal(2, rig.Stats.Accumulated(1, TFStat.Captures));

        spawns.OnSpawning(red, 0.25f);   // the respawn, while still touching: the schedule dies with the life

        rig.ThinkAt(area, 0.3f);
        rig.ThinkAt(area, 0.4f);
        Assert.Equal(2, rig.Stats.Accumulated(1, TFStat.Captures));
    }

    [Fact]
    public void A_player_who_does_not_respawn_keeps_earning_when_their_peer_respawns()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 10f);
        var stay = rig.Players.Add(1, TFTeam.Red);
        var goes = rig.Players.Add(2, TFTeam.Red);
        var spawns = new TFEscortSpawnStop(rig.Entities);

        rig.Registry.Touch(area.Handle, stay);
        rig.Registry.Touch(area.Handle, goes);
        rig.ThinkAt(area, 0.1f);

        spawns.OnSpawning(goes, 0.15f);

        rig.ThinkAt(area, 0.2f);
        Assert.Equal(2, rig.Stats.Accumulated(1, TFStat.Captures));   // never respawned: the schedule stands
        Assert.Equal(1, rig.Stats.Accumulated(2, TFStat.Captures));
    }

    [Fact]
    public void The_respawn_leaves_the_accumulator_standing_as_stocks_stop_does()
    {
        var (rig, _, area) = Neutral(capTime: 20f, capStyle: 0, partialRate: 2f);   // a point half-second
        var red = rig.Players.Add(1, TFTeam.Red);
        var spawns = new TFEscortSpawnStop(rig.Entities);

        rig.Registry.Touch(area.Handle, red);
        rig.ThinkAt(area, 0.1f);
        rig.ThinkAt(area, 0.2f);
        rig.ThinkAt(area, 0.3f);
        rig.ThinkAt(area, 0.4f);
        Assert.Equal(0, rig.Stats.Accumulated(1, TFStat.Captures));   // 0.4 accumulated, no point yet

        spawns.OnSpawning(red, 0.45f);
        rig.Registry.EndTouch(area.Handle, red);
        rig.Registry.Touch(area.Handle, red);   // back on the point after the respawn: re-armed

        rig.ThinkAt(area, 0.5f);
        // The stock stop cleared only the think (:356-359): the 0.4 stands, so
        // the re-armed schedule owes a point a tenth second later. A stop that
        // cleared the accumulator too would answer 0 here.
        Assert.Equal(1, rig.Stats.Accumulated(1, TFStat.Captures));
    }
}
