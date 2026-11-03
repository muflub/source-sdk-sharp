using SourceSharp.Abi;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The team switch at the round's start: <c>CTFGameRules::HandleSwitchTeams</c>
/// (tf_gamerules.cpp:16476-16559) behind the <c>ShouldSwitchTeams</c> flag, as
/// <c>RoundRespawn</c> does it (teamplayroundbased_gamerules.cpp:2844-2851):
/// the wipe, the players crossing, the scores crossing with them, the chat
/// line, and the flag spending itself once.
/// </summary>
public sealed class TeamSwitchTests
{
    [Fact]
    public void A_win_with_switch_teams_moves_everyone_across_at_the_next_round_start()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(blue));
        Assert.Equal(new (EntityHandle, TFTeam)[] { (red, TFTeam.Blue), (blue, TFTeam.Red) }, rig.Roster.Moves);
    }

    [Fact]
    public void The_switch_carries_the_team_scores_across_with_the_players()
    {
        var rig = new RoundRig();
        rig.Running();
        rig.Scores.SetScore(TFTeam.Red, 3);
        rig.Scores.SetScore(TFTeam.Blue, 1);

        // SetWinningTeam adds the round-win point to red (4) before the next
        // round's start swaps the two numbers, as stock orders them
        // (SetWinningTeam scores at the win; HandleSwitchTeams at RoundRespawn).
        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(1, rig.Scores.Score(TFTeam.Red));
        Assert.Equal(4, rig.Scores.Score(TFTeam.Blue));
    }

    [Fact]
    public void The_switch_tells_everyone_in_chat()
    {
        var rig = new RoundRig();
        rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(1, rig.Announcer.Count("teams_switched"));
    }

    [Fact]
    public void The_switch_empties_the_air_before_it_respawns()
    {
        var rig = new RoundRig();
        rig.Running();
        int respawnsBefore = rig.Roster.Respawns.Count;
        rig.World.RespawnCount = () => rig.Roster.Respawns.Count;

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        // The wipe (HandleSwitchTeams :16496) runs before RespawnPlayers(true)
        // (:2873): no respawn of the new round had happened when it came.
        Assert.Equal(1, rig.World.Count("remove_projectiles_buildings"));
        Assert.Equal(respawnsBefore, rig.World.RespawnsAtFirstWipe);
        Assert.True(rig.Roster.Respawns.Count > respawnsBefore);
    }

    [Fact]
    public void The_switch_spends_its_flag_and_comes_back_no_more()
    {
        var rig = new RoundRig();
        // The auto scramble is its own subject (TeamAutoScrambleTests); here
        // it would redraw the teams this flag's second round asserts.
        rig.Settings.ScrambleAuto = false;
        var (red, blue) = rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));
        Assert.False(rig.Flow.ShouldSwitchTeams());

        // A second round won without the flag: nobody crosses again.
        rig.Flow.SetWinningTeam(TFTeam.Blue, WinReason.OpponentsDead);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(blue));
        Assert.Equal(1, rig.Announcer.Count("teams_switched"));
        Assert.Equal(2, rig.Roster.Moves.Count);
    }

    [Fact]
    public void An_unswitched_win_switches_nothing()
    {
        var rig = new RoundRig();
        var (red, blue) = rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(blue));
        Assert.Empty(rig.Roster.Moves);
        Assert.Equal(0, rig.Announcer.Count("teams_switched"));
        Assert.Equal(0, rig.World.Count("remove_projectiles_buildings"));
    }

    [Fact]
    public void A_switch_armed_by_hand_switches_at_the_restart_it_arms()
    {
        // The mp_switchteams shape (commit C adds the command): the flag plus
        // mp_restartgame, no win. Stock's SetWinningTeam would clear the flag
        // (it writes it at teamplayroundbased_gamerules.cpp:2346); the restart
        // carries it to the round's start, where RoundRespawn switches.
        var rig = new RoundRig();
        var (red, blue) = rig.Running();
        rig.Flow.SetSwitchTeams(true);
        Assert.True(rig.Flow.ShouldSwitchTeams());

        rig.Settings.RestartGame = 1;
        Assert.True(rig.RunUntil(() => rig.Flow.State != RoundState.RoundRunning));   // the countdown fires
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));   // and brings the next round

        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(blue));
        Assert.Equal(1, rig.Announcer.Count("teams_switched"));
        Assert.False(rig.Flow.ShouldSwitchTeams());
    }

    [Fact]
    public void ARaceWinNeverSwitchesTeams()
    {
        // Stock gates the round-start switch behind !HasMultipleTrains
        // (tf_gamerules.cpp:2775, beside the ShouldSwitchTeams override at
        // :15106): two carts race their own tracks, so the teams stay put
        // even when the win arms the switch.
        var rig = new RoundRig();
        rig.Mode.HasMultipleTrains = true;
        // The auto scramble is its own subject; here it would redraw the
        // teams this fact says stay.
        rig.Settings.ScrambleAuto = false;
        var (red, blue) = rig.Running();

        rig.Flow.SetWinningTeam(TFTeam.Red, WinReason.AllPointsCaptured, switchTeams: true);
        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.RoundRunning));

        Assert.Equal(TFTeam.Red, rig.Roster.TeamOf(red));
        Assert.Equal(TFTeam.Blue, rig.Roster.TeamOf(blue));
        Assert.Empty(rig.Roster.Moves);
        Assert.Equal(0, rig.Announcer.Count("teams_switched"));
    }
}
