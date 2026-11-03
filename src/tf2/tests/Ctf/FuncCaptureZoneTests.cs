using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// func_capturezone (stage 2.5): <c>CCaptureZone::ShimTouch</c> and
/// <c>Capture</c> (server/tf/func_capture_zone.cpp) with the flag's
/// <c>Capture</c> and <c>CTFGameRules::CanFlagBeCaptured</c>: a carrier
/// scores in their own team's zone only, and under <c>tf_flag_return_on_touch</c>
/// only while their own flag is home.
/// </summary>
public sealed class FuncCaptureZoneTests
{
    private sealed record Map(CtfRig Rig, ItemTeamFlag RedFlag, ItemTeamFlag BlueFlag, FuncCaptureZone RedZone,
                              FuncCaptureZone BlueZone, EntityHandle RedCarrier);

    /// <summary>Both flags home, both zones, and a red player carrying blue's flag.</summary>
    private static Map RedCarriesBlue()
    {
        var rig = new CtfRig();
        var redFlag = rig.Flag(TFTeam.Red);
        var blueFlag = rig.Flag(TFTeam.Blue);
        var redZone = rig.Zone(TFTeam.Red);
        var blueZone = rig.Zone(TFTeam.Blue);
        var carrier = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(blueFlag.Handle, carrier);
        Assert.True(blueFlag.IsStolen);
        return new Map(rig, redFlag, blueFlag, redZone, blueZone, carrier);
    }

    [Fact]
    public void A_carrier_in_their_own_zone_captures_and_the_flag_goes_home()
    {
        var m = RedCarriesBlue();

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsHome);
        Assert.True(m.BlueFlag.IsCaptured);
        Assert.Equal(CtfRig.BlueHome, m.Rig.OriginOf(m.BlueFlag));
        Assert.Null(m.Rig.Rules.CarriedBy(m.RedCarrier));
        Assert.Equal(1, m.Rig.Scores.FlagCaptures(TFTeam.Red));
        Assert.Equal(1, m.RedZone.OnCapture.FireCount);
        Assert.Equal(1, m.RedZone.OnCapTeam1.FireCount);
        Assert.Equal(1, m.BlueFlag.OnCapture.FireCount);
        Assert.Equal(1, m.BlueFlag.OnCapTeam1.FireCount);
        Assert.Contains("flag Capture player=2 team=Blue priority=9", m.Rig.Events.Fired);
        Assert.Contains("captured team=Red score=1 capper=102", m.Rig.Events.Fired);
    }

    [Fact]
    public void A_carrier_in_the_enemy_zone_does_not_capture()
    {
        var m = RedCarriesBlue();

        m.Rig.Registry.Touch(m.BlueZone.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsStolen);
        Assert.Equal(0, m.Rig.Scores.FlagCaptures(TFTeam.Red));
        Assert.Equal(0, m.BlueZone.OnCapture.FireCount);
    }

    [Fact]
    public void A_player_with_no_flag_in_a_zone_captures_nothing()
    {
        var m = RedCarriesBlue();
        var redPlayer = m.Rig.Players.Add(4, TFTeam.Red);

        m.Rig.Registry.Touch(m.RedZone.Handle, redPlayer);

        Assert.True(m.BlueFlag.IsStolen);
        Assert.Equal(0, m.RedZone.OnCapture.FireCount);
    }

    [Fact]
    public void A_zone_of_no_team_takes_any_teams_capture()
    {
        var m = RedCarriesBlue();
        var neutral = m.Rig.Zone(TFTeam.Unassigned);

        m.Rig.Registry.Touch(neutral.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsHome);
        Assert.Equal(1, m.Rig.Scores.FlagCaptures(TFTeam.Red));
    }

    [Fact]
    public void A_disabled_zone_captures_nothing_until_enabled()
    {
        var m = RedCarriesBlue();
        m.Rig.Registry.Input(m.RedZone.Handle, "Disable", string.Empty, 0f, EntityHandle.None);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsStolen);

        m.Rig.Registry.Input(m.RedZone.Handle, "Enable", string.Empty, 0f, EntityHandle.None);
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsHome);
    }

    [Fact]
    public void Standing_in_the_zone_captures_on_a_later_frame_once_capture_is_allowed()
    {
        // Stock's Touch runs every frame of the overlap, so a carrier already inside scores when the bar lifts.
        var m = RedCarriesBlue();
        m.Rig.Settings.ReturnOnTouch = true;
        var blueThief = m.Rig.Players.Add(3, TFTeam.Blue);
        m.Rig.Registry.Touch(m.RedFlag.Handle, blueThief);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsStolen);

        m.RedFlag.RemoveFromCarrier(blueThief);
        m.RedFlag.ResetFlag();
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsHome);
        Assert.True(m.RedZone.TouchEveryTick);
    }

    // ---- own flag home ------------------------------------------------------------

    [Fact]
    public void Under_return_on_touch_a_carrier_whose_own_flag_is_away_may_not_capture()
    {
        var m = RedCarriesBlue();
        m.Rig.Settings.ReturnOnTouch = true;
        var blueThief = m.Rig.Players.Add(3, TFTeam.Blue);
        m.Rig.Registry.Touch(m.RedFlag.Handle, blueThief);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsStolen);
        Assert.False(m.Rig.Rules.CanFlagBeCaptured(m.RedCarrier));
        Assert.Equal(0, m.Rig.Scores.FlagCaptures(TFTeam.Red));
    }

    [Fact]
    public void Without_return_on_touch_a_carrier_whose_own_flag_is_away_still_captures()
    {
        var m = RedCarriesBlue();
        var blueThief = m.Rig.Players.Add(3, TFTeam.Blue);
        m.Rig.Registry.Touch(m.RedFlag.Handle, blueThief);
        Assert.True(m.RedFlag.IsStolen);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsHome);
        Assert.Equal(1, m.Rig.Scores.FlagCaptures(TFTeam.Red));
    }

    // ---- scoring ------------------------------------------------------------------

    [Fact]
    public void With_tf_flag_caps_per_round_0_a_capture_scores_the_team_instead()
    {
        var m = RedCarriesBlue();
        m.Rig.Settings.CapsPerRound = 0;

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.Equal(0, m.Rig.Scores.FlagCaptures(TFTeam.Red));
        Assert.Equal(1, m.Rig.Scores.Score(TFTeam.Red));
    }

    [Fact]
    public void Captures_add_up_per_team_and_the_team_table_is_written()
    {
        var m = RedCarriesBlue();
        int before = m.Rig.TeamTable.FlagCaptures((int)TFTeam.Red);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        m.Rig.ThinkAt(m.BlueFlag, m.Rig.Clock.Now + ItemTeamFlag.ThinkTime);
        m.Rig.Registry.Touch(m.BlueFlag.Handle, m.RedCarrier);
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.Equal(0, before);
        Assert.Equal(2, m.Rig.TeamTable.FlagCaptures((int)TFTeam.Red));
        Assert.Equal(0, m.Rig.TeamTable.FlagCaptures((int)TFTeam.Blue));
    }

    [Fact]
    public void A_captured_flag_cannot_be_touched_until_its_next_think()
    {
        var m = RedCarriesBlue();
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.False(m.BlueFlag.TouchEnabled);

        m.Rig.Registry.Touch(m.BlueFlag.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsHome);

        m.Rig.ThinkAt(m.BlueFlag, m.Rig.Clock.Now + ItemTeamFlag.ThinkTime);
        Assert.False(m.BlueFlag.IsCaptured);
        m.Rig.Registry.Touch(m.BlueFlag.Handle, m.RedCarrier);
        Assert.True(m.BlueFlag.IsStolen);
    }

    [Fact]
    public void A_capture_that_will_win_skips_the_capture_bonus_and_others_give_it()
    {
        var m = RedCarriesBlue();
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);
        Assert.Equal(new[] { "bonus Red" }, m.Rig.Effects.Done);

        m.Rig.TeamTable.SetFlagCaptures((int)TFTeam.Red, 2);
        m.Rig.ThinkAt(m.BlueFlag, m.Rig.Clock.Now + ItemTeamFlag.ThinkTime);
        m.Rig.Registry.Touch(m.BlueFlag.Handle, m.RedCarrier);
        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.Equal(new[] { "bonus Red" }, m.Rig.Effects.Done);
        Assert.Equal(3, m.Rig.Scores.FlagCaptures(TFTeam.Red));
    }

    [Fact]
    public void A_capture_in_a_stalemate_gives_the_capping_team_enough_captures_to_win()
    {
        var m = RedCarriesBlue();
        m.Rig.Round.State = RoundState.Stalemate;

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.Equal(3, m.Rig.Scores.FlagCaptures(TFTeam.Red));
    }

    [Theory]
    [InlineData(RoundState.TeamWin)]
    [InlineData(RoundState.Preround)]
    public void No_capture_while_flags_may_not_be_capped(RoundState state)
    {
        var m = RedCarriesBlue();
        m.Rig.Round.State = state;

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.True(m.BlueFlag.IsStolen);
    }

    // ---- the announcer (stage 2.5) -------------------------------------------------

    [Fact]
    public void A_capture_tells_the_announcer_who_capped_which_flag()
    {
        var m = RedCarriesBlue();

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.Contains($"captured flag={m.BlueFlag.Handle.EntryIndex} capperTeam=Red capper=2", m.Rig.Announcer.Lines);
    }

    [Fact]
    public void A_capture_that_would_win_tells_the_announcer_nothing()
    {
        var m = RedCarriesBlue();
        m.Rig.TeamTable.SetFlagCaptures((int)TFTeam.Red, 2);
        m.Rig.ThinkAt(m.BlueFlag, m.Rig.Clock.Now + ItemTeamFlag.ThinkTime);
        m.Rig.Registry.Touch(m.BlueFlag.Handle, m.RedCarrier);

        m.Rig.Registry.Touch(m.RedZone.Handle, m.RedCarrier);

        Assert.DoesNotContain(m.Rig.Announcer.Lines, l => l.StartsWith("captured", StringComparison.Ordinal));
    }

    [Fact]
    public void A_carrier_touching_the_enemy_zone_hears_the_touch_cap_notice_once_every_five_seconds()
    {
        var m = RedCarriesBlue();

        m.Rig.Registry.Touch(m.BlueZone.Handle, m.RedCarrier);
        Assert.Equal(["touchcap player=2"], m.Rig.Announcer.Lines.Where(l => l.StartsWith("touchcap", StringComparison.Ordinal)).ToList());

        m.Rig.Registry.Touch(m.BlueZone.Handle, m.RedCarrier);
        Assert.Equal(["touchcap player=2"], m.Rig.Announcer.Lines.Where(l => l.StartsWith("touchcap", StringComparison.Ordinal)).ToList());

        m.Rig.Clock.Now += 5.1f;
        m.Rig.Registry.Touch(m.BlueZone.Handle, m.RedCarrier);
        Assert.Equal(["touchcap player=2", "touchcap player=2"], m.Rig.Announcer.Lines.Where(l => l.StartsWith("touchcap", StringComparison.Ordinal)).ToList());
    }
}
