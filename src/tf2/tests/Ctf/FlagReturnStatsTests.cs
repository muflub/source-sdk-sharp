using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The flag return stat (CCaptureFlag::FlagTouch, entity_capture_flag.cpp:1131-1137):
/// a team mate who returns their dropped CTF flag by touch under
/// <c>tf_flag_return_on_touch</c> gets one TFSTAT_FLAGRETURNS
/// (CTFGameStats::Event_PlayerReturnedFlag).
/// </summary>
public sealed class FlagReturnStatsTests
{
    /// <summary>Blue player 3 drops red's CTF flag; red player 2 stands ready to touch it.</summary>
    private static (CtfRig Rig, ItemTeamFlag Flag, EntityHandle Defender) DroppedRedFlag(bool returnOnTouch)
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var defender = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);
        Assert.True(red.IsDropped);
        rig.Settings.ReturnOnTouch = returnOnTouch;
        return (rig, red, defender);
    }

    [Fact]
    public void A_team_mate_returning_a_dropped_flag_by_touch_is_credited_one_flag_return()
    {
        var (rig, flag, defender) = DroppedRedFlag(returnOnTouch: true);

        rig.Registry.Touch(flag.Handle, defender);

        Assert.True(flag.IsHome);
        Assert.Equal(1, rig.Stats.Accumulated(defender.EntryIndex, TFStat.Flagreturns));
        Assert.Equal(TFPlayerStats.ScoreFlagReturn, rig.Stats.Score(defender.EntryIndex));
    }

    [Fact]
    public void Without_return_on_touch_a_team_mates_touch_credits_no_flag_return()
    {
        var (rig, flag, defender) = DroppedRedFlag(returnOnTouch: false);

        rig.Registry.Touch(flag.Handle, defender);

        Assert.True(flag.IsDropped);
        Assert.Equal(0, rig.Stats.Accumulated(defender.EntryIndex, TFStat.Flagreturns));
    }
}
