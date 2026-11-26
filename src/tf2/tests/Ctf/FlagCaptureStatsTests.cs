using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The capture stat of a flag capture (CCaptureFlag::Capture,
/// entity_capture_flag.cpp:1493-1675): the carrier gets one TFSTAT_CAPTURES
/// (CTFGameStats::Event_PlayerCapturedPoint); in CTF and resource control the
/// player who first took the flag from home gets one too when someone else
/// carried it in; attack/defend and invade credit the carrier only.
/// </summary>
public sealed class FlagCaptureStatsTests
{
    /// <summary>Blue's CTF flag stolen by red player 2, red's zone to score it in.</summary>
    private static (CtfRig Rig, ItemTeamFlag Flag, FuncCaptureZone Zone, EntityHandle Stealer) Stolen(string gameType = "0")
    {
        var rig = new CtfRig();
        rig.Flag(TFTeam.Red, ("GameType", gameType));
        var blue = rig.Flag(TFTeam.Blue, ("GameType", gameType));
        var zone = rig.Zone(TFTeam.Red);
        var stealer = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(blue.Handle, stealer);
        Assert.True(blue.IsStolen);
        return (rig, blue, zone, stealer);
    }

    /// <summary>The stealer drops the flag and red player 3 picks it up.</summary>
    private static EntityHandle HandOff(CtfRig rig, ItemTeamFlag flag, EntityHandle stealer)
    {
        flag.RemoveFromCarrier(stealer);
        Assert.True(flag.IsDropped);
        var mate = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(flag.Handle, mate);
        Assert.True(flag.IsStolen);
        return mate;
    }

    [Fact]
    public void A_flag_capture_credits_the_carrier_one_capture()
    {
        var (rig, _, zone, stealer) = Stolen();

        rig.Registry.Touch(zone.Handle, stealer);

        Assert.Equal(1, rig.Stats.Accumulated(stealer.EntryIndex, TFStat.Captures));   // stealer and carrier: once
        Assert.Equal(1, rig.Stats.Round(stealer.EntryIndex, TFStat.Captures));
    }

    [Fact]
    public void A_flag_capture_moves_the_carriers_score_by_the_capture_score()
    {
        var (rig, _, zone, stealer) = Stolen();
        Assert.Equal(0, rig.Stats.Score(stealer.EntryIndex));

        rig.Registry.Touch(zone.Handle, stealer);

        Assert.Equal(TFPlayerStats.ScoreCapture, rig.Stats.Score(stealer.EntryIndex));
    }

    [Fact]
    public void A_ctf_capture_carried_in_by_another_player_credits_the_stealer_too()
    {
        var (rig, flag, zone, stealer) = Stolen();
        var mate = HandOff(rig, flag, stealer);

        rig.Registry.Touch(zone.Handle, mate);

        Assert.Equal(1, rig.Stats.Accumulated(mate.EntryIndex, TFStat.Captures));
        Assert.Equal(1, rig.Stats.Accumulated(stealer.EntryIndex, TFStat.Captures));
    }

    [Fact]
    public void A_stealer_who_left_before_the_capture_is_credited_nothing()
    {
        var (rig, flag, zone, stealer) = Stolen();
        var mate = HandOff(rig, flag, stealer);
        rig.Players.All.Remove(stealer);   // m_hInitialPlayer.Get() is NULL

        rig.Registry.Touch(zone.Handle, mate);

        Assert.Equal(1, rig.Stats.Accumulated(mate.EntryIndex, TFStat.Captures));
        Assert.Equal(0, rig.Stats.Accumulated(stealer.EntryIndex, TFStat.Captures));
    }

    [Fact]
    public void The_stealer_is_credited_once_and_forgotten_by_the_capture()
    {
        var (rig, flag, zone, stealer) = Stolen();
        var mate = HandOff(rig, flag, stealer);
        rig.Registry.Touch(zone.Handle, mate);

        // The flag is home again; the mate takes it alone and scores it again.
        rig.ThinkAt(flag, rig.Clock.Now + 1f);
        rig.Registry.Touch(flag.Handle, mate);
        Assert.True(flag.IsStolen);
        flag.Capture(mate, capturePoint: 1);

        Assert.Equal(2, rig.Stats.Accumulated(mate.EntryIndex, TFStat.Captures));
        Assert.Equal(1, rig.Stats.Accumulated(stealer.EntryIndex, TFStat.Captures));
    }

    [Fact]
    public void An_attack_defend_capture_credits_the_carrier_but_not_the_stealer()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red, ("GameType", "1"));
        var stealer = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(red.Handle, stealer);
        Assert.True(red.IsStolen);
        var mate = HandOff(rig, red, stealer);

        red.Capture(mate, capturePoint: 1);

        Assert.Equal(1, rig.Stats.Accumulated(mate.EntryIndex, TFStat.Captures));
        Assert.Equal(0, rig.Stats.Accumulated(stealer.EntryIndex, TFStat.Captures));
    }
}
