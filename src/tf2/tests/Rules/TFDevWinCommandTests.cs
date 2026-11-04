using SourceSharp.Abi;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>The live gate's stimulus, <c>tf2_dev_win</c>, reaches the round machine's ends.</summary>
public sealed class TFDevWinCommandTests
{
    private static (TFDevWinCommand Command, RoundRig Rig) Command()
    {
        var rig = new RoundRig();
        var services = new ServiceContainer();
        services.AddInstance<ITeamRoundControl>(rig.Flow);
        services.AddInstance<ITeamRoundState>(rig.Flow);
        return (new TFDevWinCommand(rig.Log, services), rig);
    }

    [Fact]
    public void A_team_number_wins_the_round_for_that_team()
    {
        var (command, rig) = Command();
        rig.Running();

        command.RunLine("3");

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Blue, rig.Flow.WinningTeam);
    }

    [Fact]
    public void Zero_is_a_stalemate()
    {
        var (command, rig) = Command();
        rig.Running();

        command.RunLine("0");

        Assert.Equal(RoundState.TeamWin, rig.Flow.State);
        Assert.Equal(TFTeam.Unassigned, rig.Flow.WinningTeam);
    }

    [Fact]
    public void Bare_it_changes_nothing_and_says_the_state()
    {
        var (command, rig) = Command();
        rig.Running();

        command.RunLine("");

        Assert.Equal(RoundState.RoundRunning, rig.Flow.State);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("state RoundRunning", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_the_machine_exists_it_warns_and_does_nothing()
    {
        var log = new SourceSharp.Abi.RecordingLog();

        new TFDevWinCommand(log, new ServiceContainer()).RunLine("2");

        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("not up yet", StringComparison.Ordinal));
    }
}
