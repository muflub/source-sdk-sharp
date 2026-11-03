using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Entities;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// Stock <c>CTFGameRules::SetRoundOverlayDetails</c> (tf_gamerules.cpp:16591-16620)
/// and <c>SetCurrentRoundStateBitString</c> (<c>:13896-13923</c>): the
/// mini-round's mask of opposing points and the blue-owned state over the
/// master's table, reset by a full-round win (<c>:15932-15937</c>).
/// </summary>
public sealed class TeamRoundOverlayTests
{
    /// <summary>
    /// A three-point map whose round plays cp_a and cp_b. A same-owner pair
    /// needs the restrict key, or the round counts as already won and is
    /// never chosen.
    /// </summary>
    private static (RoundRig Rig, ObjectiveRig Map) MiniRound(TFTeam secondOwner = TFTeam.Blue)
    {
        var map = new ObjectiveRig();
        map.Point("cp_a", 0, TFTeam.Red);
        map.Point("cp_b", 1, secondOwner);
        map.Point("cp_c", 2, TFTeam.Red);
        _ = map.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_1"),
                                             ("cpr_cp_names", "cp_a cp_b"), ("cpr_priority", "1"),
                                             ("cpr_restrict_team_cap_win", secondOwner == TFTeam.Red ? "1" : "0"));
        map.Master();
        map.StartRound();
        return (new RoundRig(objectives: map.Entities), map);
    }

    [Fact]
    public void A_mini_round_records_its_mask_and_the_blue_state()
    {
        var (rig, _) = MiniRound();

        // Before any round: stock's constructor state (:3316-3318).
        Assert.Equal(-1, rig.Flow.PrevRoundState);
        Assert.Equal(-1, rig.Flow.CurrentRoundState);
        Assert.Equal(0, rig.Flow.CurrentMiniRoundMask);

        rig.Flow.SetRoundOverlayDetails();

        // cp_a (bit 0, red) against cp_b (bit 1, blue).
        Assert.Equal(0b11, rig.Flow.CurrentMiniRoundMask);
        Assert.Equal(1 << 1, rig.Flow.CurrentRoundState);
        Assert.Equal(-1, rig.Flow.PrevRoundState);
    }

    [Fact]
    public void A_second_round_moves_the_previous_state_back()
    {
        var (rig, map) = MiniRound();
        rig.Flow.SetRoundOverlayDetails();

        var a = map.Entities.Points.First(p => p.Name == "cp_a");
        a.SetOwner((int)TFTeam.Blue, makeSound: false, []);
        rig.Flow.SetRoundOverlayDetails();

        Assert.Equal(1 << 1, rig.Flow.PrevRoundState);
        Assert.Equal(0b11, rig.Flow.CurrentRoundState);
    }

    [Fact]
    public void A_round_without_opposing_points_masks_nothing()
    {
        var (rig, _) = MiniRound(TFTeam.Red);

        rig.Flow.SetRoundOverlayDetails();

        Assert.Equal(0, rig.Flow.CurrentMiniRoundMask);

        // The state is still taken: no point is blue's.
        Assert.Equal(0, rig.Flow.CurrentRoundState);
    }

    [Fact]
    public void A_master_without_mini_rounds_leaves_the_overlay_alone()
    {
        var map = new ObjectiveRig();
        map.Point("cp_a", 0, TFTeam.Red);
        map.Point("cp_b", 1, TFTeam.Blue);
        map.Master();
        map.StartRound();
        var rig = new RoundRig(objectives: map.Entities);

        rig.Flow.SetRoundOverlayDetails();

        Assert.Equal(-1, rig.Flow.PrevRoundState);
        Assert.Equal(-1, rig.Flow.CurrentRoundState);
        Assert.Equal(0, rig.Flow.CurrentMiniRoundMask);
    }

    [Fact]
    public void A_win_without_a_forced_reset_keeps_the_overlay()
    {
        var (rig, _) = MiniRound();
        rig.Flow.SetRoundOverlayDetails();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.TimeLimit, forceMapReset: false);

        Assert.Equal(1 << 1, rig.Flow.CurrentRoundState);
        Assert.Equal(0b11, rig.Flow.CurrentMiniRoundMask);
    }

    [Fact]
    public void A_full_round_win_resets_the_overlay()
    {
        var (rig, _) = MiniRound();
        rig.Flow.SetRoundOverlayDetails();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.TimeLimit, forceMapReset: true);

        Assert.Equal(-1, rig.Flow.PrevRoundState);
        Assert.Equal(-1, rig.Flow.CurrentRoundState);
        Assert.Equal(0, rig.Flow.CurrentMiniRoundMask);
    }
}
