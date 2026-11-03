using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Server.Modes;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Modes;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The CTF mode (stage 2.5): <c>CTFGameRules</c>'s CTF arms
/// (tf_gamerules.cpp): the caps-per-round win (<c>SetCtfWinningTeam</c>,
/// L9338), the round machine asking it once a second (L8161-8167), the
/// flag arms of <c>TimerMayExpire</c> (L14022) and <c>CanGoToStalemate</c>
/// (L4129), and <c>ValidateCapturesPerRound</c> (L1256).
/// </summary>
public sealed class CtfModeTests
{
    private static (CtfRig Rig, CtfMode Mode, ItemTeamFlag Red, ItemTeamFlag Blue) Ctf()
    {
        var rig = new CtfRig();
        var modes = new TFModeRig();
        var mode = new CtfMode(modes.Services, rig.Rules, rig.Settings);
        rig.Slot.Current = mode;
        return (rig, mode, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));
    }

    [Fact]
    public void Reaching_tf_flag_caps_per_round_wins_the_round_for_that_team_by_the_flag_capture_limit()
    {
        var (rig, mode, _, _) = Ctf();
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 2);
        Assert.False(mode.CheckCapsPerRound());
        Assert.Empty(rig.Control.Wins);

        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 3);

        Assert.True(mode.CheckCapsPerRound());
        var win = Assert.Single(rig.Control.Wins);
        Assert.Equal((TFTeam.Blue, WinReason.FlagCaptureLimit), (win.Team, win.Reason));
    }

    [Fact]
    public void Two_teams_over_a_lowered_limit_the_one_with_more_wins()
    {
        var (rig, mode, _, _) = Ctf();
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Red, 3);
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 2);
        rig.Settings.CapsPerRound = 2;

        Assert.True(mode.CheckCapsPerRound());
        Assert.Equal(TFTeam.Red, Assert.Single(rig.Control.Wins).Team);
    }

    [Fact]
    public void Three_real_captures_win_through_the_mode()
    {
        var (rig, mode, _, blueFlag) = Ctf();
        var zone = rig.Zone(TFTeam.Red);
        var carrier = rig.Players.Add(2, TFTeam.Red);

        for (int i = 0; i < 3; i++)
        {
            Assert.False(mode.CheckCapsPerRound());
            rig.ThinkAt(blueFlag, rig.Clock.Now + ItemTeamFlag.ThinkTime);
            rig.Registry.Touch(blueFlag.Handle, carrier);
            rig.Registry.Touch(zone.Handle, carrier);
        }

        Assert.Equal(3, rig.Scores.FlagCaptures(TFTeam.Red));
        Assert.True(mode.CheckCapsPerRound());
        Assert.Equal((TFTeam.Red, WinReason.FlagCaptureLimit), (rig.Control.Wins[0].Team, rig.Control.Wins[0].Reason));
    }

    [Fact]
    public void Every_round_starts_at_no_captures_so_last_rounds_win_does_not_win_again()
    {
        var (rig, mode, _, _) = Ctf();
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Red, 3);
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 1);
        Assert.True(mode.CheckCapsPerRound());

        mode.OnRoundStart();

        Assert.Equal(0, rig.TeamTable.FlagCaptures((int)TFTeam.Red));
        Assert.Equal(0, rig.TeamTable.FlagCaptures((int)TFTeam.Blue));
        Assert.False(mode.CheckCapsPerRound());
    }

    [Fact]
    public void With_tf_flag_caps_per_round_0_nothing_wins_and_old_captures_are_zeroed()
    {
        var (rig, mode, _, _) = Ctf();
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Red, 5);
        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 1);
        rig.Settings.CapsPerRound = 0;

        Assert.False(mode.CheckCapsPerRound());

        Assert.Empty(rig.Control.Wins);
        Assert.Equal(0, rig.TeamTable.FlagCaptures((int)TFTeam.Red));
        Assert.Equal(0, rig.TeamTable.FlagCaptures((int)TFTeam.Blue));
    }

    [Fact]
    public void The_timer_may_not_expire_while_any_enabled_flag_is_away_from_home()
    {
        var (rig, mode, red, _) = Ctf();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        Assert.True(mode.TimerMayExpire());

        rig.Registry.Touch(red.Handle, blue);
        Assert.False(mode.TimerMayExpire());

        red.RemoveFromCarrier(blue);
        Assert.False(mode.TimerMayExpire());

        red.SetDisabled(true);
        Assert.True(mode.TimerMayExpire());
    }

    [Fact]
    public void No_stalemate_while_a_flag_is_stolen_or_dropped_or_once_captures_have_won()
    {
        var (rig, mode, red, _) = Ctf();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        Assert.True(mode.CanGoToStalemate());

        rig.Registry.Touch(red.Handle, blue);
        Assert.False(mode.CanGoToStalemate());

        red.RemoveFromCarrier(blue);
        Assert.False(mode.CanGoToStalemate());

        red.ResetFlag();
        Assert.True(mode.CanGoToStalemate());

        rig.TeamTable.SetFlagCaptures((int)TFTeam.Blue, 3);
        Assert.False(mode.CanGoToStalemate());
        Assert.Single(rig.Control.Wins);
    }

    [Fact]
    public void With_the_ctf_overtime_key_off_a_flag_away_from_home_holds_no_timer()
    {
        var (rig, mode, red, _) = Ctf();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        Assert.False(mode.TimerMayExpire());

        rig.Rules.SetOvertimeAllowedForCtf(false);

        Assert.True(mode.TimerMayExpire());
    }

    [Fact]
    public void The_ctf_overtime_default_is_on_and_a_level_init_restores_it()
    {
        var (rig, _, _, _) = Ctf();
        Assert.True(rig.Rules.OvertimeAllowedForCtf);

        rig.Rules.SetOvertimeAllowedForCtf(false);
        Assert.False(rig.Rules.OvertimeAllowedForCtf);

        // Stock re-runs the rules ctor at every level's CWorld::Spawn
        // (world.cpp:608-616); the lifecycle reset is that default.
        ((IGameLifecycle)rig.Rules).OnLevelInit("ctf_2fort");

        Assert.True(rig.Rules.OvertimeAllowedForCtf);
    }

    [Fact]
    public void A_flag_event_reaches_the_mode_hooks()
    {
        var rig = new CtfRig();
        var modes = new TFModeRig();
        var mode = new CtfMode(modes.Services, rig.Rules, rig.Settings);
        rig.Slot.Current = mode;
        var red = rig.Flag(TFTeam.Red);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(red.Handle, blue);

        Assert.Contains(modes.Hook.Heard, h => h.StartsWith("objective ctf flag_picked_up Red", StringComparison.Ordinal));
    }

    [Fact]
    public void Outside_CTF_no_mode_counts_captures()
        => Assert.False(new TFModeRig().Payload.CheckCapsPerRound());

    // ---- the round machine's periodic ask ----------------------------------------

    [Fact]
    public void The_round_machine_asks_the_mode_for_a_caps_win_once_a_second_while_the_round_runs()
    {
        var round = new RoundRig();
        round.Running();
        int before = round.Mode.CapsChecks;

        for (int i = 0; i < 30; i++)
            round.Step();

        int asked = round.Mode.CapsChecks - before;
        Assert.InRange(asked, 2, 4); // three seconds of 0.1 s frames, once a second
    }

    [Fact]
    public void A_caps_win_returns_before_the_periodic_think_moves_on_so_the_next_frame_asks_again()
    {
        var round = new RoundRig();
        round.Running();
        round.Mode.CapsWin = true;
        int before = round.Mode.CapsChecks;

        // One ask within the first second; from then on every frame asks, as
        // stock's return skips the base think that moves m_flNextPeriodicThink.
        for (int i = 0; i < 15; i++)
            round.Step();

        Assert.True(round.Mode.CapsChecks - before >= 5, $"asked {round.Mode.CapsChecks - before} times");
    }

    [Fact]
    public void The_round_machine_does_not_ask_after_a_win_or_while_waiting_for_players()
    {
        var round = new RoundRig();
        round.Running();
        round.Flow.SetWinningTeam(TFTeam.Red, WinReason.FlagCaptureLimit);
        int before = round.Mode.CapsChecks;

        for (int i = 0; i < 30; i++)
            round.Step();

        Assert.Equal(RoundState.TeamWin, round.Flow.State);
        Assert.Equal(before, round.Mode.CapsChecks);
    }
}
