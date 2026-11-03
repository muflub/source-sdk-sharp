using TF2.Server.Entities;
using TF2.Server.Modes;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Tests.Entities;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// Stock <c>CTFGameRules::PlaySpecialCapSounds</c> (tf_gamerules.cpp:18794-18836)
/// through the round machine, over a live objective grid: the CP gate, KOTH's
/// point pair, and the per-capture round's result pair.
/// </summary>
public sealed class TeamRoundCapSoundsTests
{
    private readonly record struct CapPoint(string Name, int Index, TFTeam Owner);

    /// <summary>A CP map's points and master under the round machine.</summary>
    private static (RoundRig Rig, ObjectiveRig Map) Rig(TFGameType gameType, bool koth, CapPoint[] points,
                                                        bool scorePerCapture = false)
    {
        var map = new ObjectiveRig();
        foreach (var point in points)
            _ = map.Point(point.Name, point.Index, point.Owner);

        _ = map.Master(scorePerCapture ? [("score_style", "1")] : []);
        map.StartRound();

        var rig = new RoundRig(objectives: map.Entities)
        {
            Mode = { GameType = gameType, InKothMode = koth },
        };
        return (rig, map);
    }

    private static RoundRig Capped(TFGameType gameType, CapPoint[] points, string capped, TFTeam cappingTeam,
                                   bool koth = false, bool scorePerCapture = false)
    {
        var (rig, map) = Rig(gameType, koth, points, scorePerCapture);
        rig.Flow.PlaySpecialCapSounds(cappingTeam, map.Entities.Points.First(p => p.Name == capped));
        return rig;
    }

    /// <summary>
    /// The sounds the capture played. The recorder holds every announcer
    /// seam call, including the round machine's bookkeeping — the Initialise
    /// <c>scorestats_accumulated_reset</c> (stock arms the resets and calls
    /// ResetScores at teamplayroundbased_gamerules.cpp:463-468, whose player
    /// arm fires the event at :3245-3269). This class measures the cap
    /// sounds, so it reads the broadcast lines alone.
    /// </summary>
    private static string[] Sounds(RoundRig rig)
        => rig.Announcer.Said.Where(s => s.StartsWith("broadcast ", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_koth_capture_plays_the_point_and_the_result_to_each_team()
    {
        var rig = Capped(TFGameType.Cp, [new CapPoint("cap_a", 0, TFTeam.Red)], "cap_a", TFTeam.Blue, koth: true);

        string[] expected =
        [
            "broadcast Red Hud.PointCaptured",
            "broadcast Red Announcer.Failure",
            "broadcast Blue Hud.PointCaptured",
            "broadcast Blue Announcer.Success",
        ];
        Assert.Equal(expected, Sounds(rig));
    }

    [Fact]
    public void A_capture_that_leaves_a_point_unowned_plays_the_result_pair_only()
    {
        // cap_a held, cap_b contested after the cap: the round goes on.
        var rig = Capped(TFGameType.Cp,
                         [new CapPoint("cap_a", 0, TFTeam.Red), new CapPoint("cap_b", 1, TFTeam.Unassigned)],
                         "cap_a", TFTeam.Blue);

        string[] expected = ["broadcast Red Announcer.Failure", "broadcast Blue Announcer.Success"];
        Assert.Equal(expected, Sounds(rig));
    }

    [Fact]
    public void A_capture_that_wins_the_map_plays_nothing_here()
    {
        // cap_a is the only point: taking it wins, and the win songs follow.
        var rig = Capped(TFGameType.Cp, [new CapPoint("cap_a", 0, TFTeam.Unassigned)], "cap_a", TFTeam.Blue);

        Assert.Empty(Sounds(rig));
    }

    [Fact]
    public void A_per_capture_score_master_is_silent()
    {
        var (rig, map) = Rig(TFGameType.Cp, koth: false, [new CapPoint("cap_a", 0, TFTeam.Red)], scorePerCapture: true);
        Assert.True(map.Entities.Master!.ShouldScorePerCapture);

        rig.Flow.PlaySpecialCapSounds(TFTeam.Blue, map.Entities.Points.First());

        Assert.Empty(Sounds(rig));
    }

    [Fact]
    public void Only_the_cp_game_type_is_answered()
    {
        var rig = Capped(TFGameType.Ctf, [new CapPoint("cap_a", 0, TFTeam.Red)], "cap_a", TFTeam.Blue, koth: true);

        Assert.Empty(Sounds(rig));
    }
}
