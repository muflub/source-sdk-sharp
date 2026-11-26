using SourceSharp.Abi;

using TF2.Server.Entities;
using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// The capture stat of a point capture: CTFGameRules' teamplay_point_captured
/// handler (tf_gamerules.cpp:18197-18209) gives each player the event's
/// cappers string names one TFSTAT_CAPTURES
/// (CTFGameStats::Event_PlayerCapturedPoint), and the event fires only for a
/// point with a print name (CTeamControlPoint::SendCapString).
/// </summary>
public sealed class PointCaptureStatsTests
{
    /// <summary>The objective rig, with the entry indices a capper list names answered as players.</summary>
    private static ObjectiveRig Rig(params int[] playerIndices)
    {
        var rig = new ObjectiveRig();
        var known = new HashSet<int>(playerIndices);
        rig.Engine.ReturnsEachCall("Players.GetHandle", () =>
            rig.Engine.Calls[^1].Args[0] is int index && known.Contains(index)
                ? new EntityHandle((uint)index | (1u << 16))
                : EntityHandle.None);
        return rig;
    }

    [Fact]
    public void A_point_captured_by_two_players_credits_each_one_capture()
    {
        var rig = Rig(5, 6);
        var point = rig.Point("cp_a", 0);
        rig.StartRound();

        point.SetOwner((int)TFTeam.Blue, makeSound: true, [5, 6]);

        Assert.Equal((int)TFTeam.Blue, point.Owner);
        Assert.Equal(1, rig.Stats.Accumulated(5, TFStat.Captures));
        Assert.Equal(1, rig.Stats.Accumulated(6, TFStat.Captures));
        Assert.Equal(1, rig.Stats.Round(5, TFStat.Captures));
    }

    [Fact]
    public void A_point_capture_moves_each_cappers_score_by_the_capture_score()
    {
        var rig = Rig(5);
        var point = rig.Point("cp_a", 0);
        rig.StartRound();
        Assert.Equal(0, rig.Stats.Score(5));

        point.SetOwner((int)TFTeam.Red, makeSound: true, [5]);

        Assert.Equal(TFPlayerStats.ScoreCapture, rig.Stats.Score(5));
        Assert.Equal(TFPlayerStats.ScoreCapture, rig.Stats.RoundScore(5));
    }

    [Fact]
    public void A_capper_index_that_names_no_player_is_credited_nothing()
    {
        var rig = Rig(5);
        var point = rig.Point("cp_a", 0);
        rig.StartRound();

        point.SetOwner((int)TFTeam.Red, makeSound: true, [5, 9]);

        Assert.Equal(1, rig.Stats.Accumulated(5, TFStat.Captures));
        Assert.Equal(0, rig.Stats.Accumulated(9, TFStat.Captures));
    }

    [Fact]
    public void A_point_without_a_print_name_fires_no_capture_event_and_credits_no_one()
    {
        var rig = Rig(5);
        var point = rig.Spawn<TeamControlPoint>(TeamControlPoint.MapClass, ("targetname", "cp_quiet"), ("point_index", "0"),
                                                ("point_default_owner", "0"),
                                                ("team_icon_2", "sprites/obj_icons/icon_obj_red"),
                                                ("team_icon_3", "sprites/obj_icons/icon_obj_blu"),
                                                ("team_icon_0", "sprites/obj_icons/icon_obj_neutral"));
        rig.StartRound();

        point.SetOwner((int)TFTeam.Red, makeSound: true, [5]);

        Assert.Equal((int)TFTeam.Red, point.Owner);   // the capture itself happened
        Assert.DoesNotContain(rig.Events.Fired, e => e.StartsWith("captured ", StringComparison.Ordinal));
        Assert.Equal(0, rig.Stats.Accumulated(5, TFStat.Captures));
    }

    [Fact]
    public void An_area_capture_credits_its_cappers_one_capture_beside_their_escort_points()
    {
        // The escort ticks (Event_PlayerScoresEscortPoints) and the capture
        // event's one point are separate stock counts: the area's end hands its
        // cappers to the point's SetOwner (trigger_area_capture.cpp:902), whose
        // event credits each once.
        var rig = Rig(1);
        rig.Settings.CapStyle = 0;
        var point = rig.Point("cp_a", 0);
        var area = rig.Area("cp_a", 10f);
        rig.Master(("partial_cap_points_rate", "10"));
        rig.StartRound();
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);

        int ticks = 0;
        while (area.IsCapturing && ticks < 200)
        {
            ticks++;
            rig.ThinkAt(area, ticks * 0.1f);
        }

        Assert.False(area.IsCapturing);
        Assert.Equal((int)TFTeam.Red, point.Owner);
        int escorted = rig.GameEvents.Fired.Count(e => e == "event=player_escort_score player=1 points=1");
        Assert.Equal(ticks, escorted);                                              // one escort point a think, as before
        Assert.Equal(escorted + 1, rig.Stats.Accumulated(1, TFStat.Captures));      // and the capture's one
    }
}
