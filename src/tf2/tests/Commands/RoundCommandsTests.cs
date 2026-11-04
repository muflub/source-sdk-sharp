using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Server.Rules;
using TF2.Server.Entities;
using TF2.Tests.Entities;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>
/// tf_listrounds and tf_playround (team_control_point_master.cpp:1267-1341)
/// against a master whose rounds have activated.
/// </summary>
public sealed class RoundCommandsTests
{
    private (ObjectiveRig Rig, RecordingLog Log, TF2.Tests.Rules.FakeSettings Settings, TeamControlPointMaster Master) Rig()
    {
        var rig = new ObjectiveRig();
        var log = new RecordingLog();
        var settings = new TF2.Tests.Rules.FakeSettings();
        rig.Services.AddInstance<ITeamRoundSettings>(settings);
        var master = rig.Master();
        return (rig, log, settings, master);
    }

    private static void Execute(object command, string args = "") => command.RunLine(args);

    [Fact]
    public void List_rounds_names_every_round_of_the_map()
    {
        var (rig, log, _, master) = Rig();
        rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_1"));
        rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_2"));
        rig.StartRound();
        Assert.True(master.PlayingMiniRounds, "the master did not pick its rounds up on activate");

        Execute(new ListRoundsCommand(log, rig.Services));

        // the names in the master's own round order (its priority sort, not
        // the command's business)
        Assert.Equal("Rounds in this map:\n\n" + string.Concat(master.Rounds.Select(r => r.Name + "\n")), log.Text);
        Assert.Contains("round_1", log.Text);
        Assert.Contains("round_2", log.Text);
    }

    [Fact]
    public void A_map_without_rounds_is_said()
    {
        var (rig, log, _, master) = Rig();
        rig.StartRound();
        Assert.False(master.PlayingMiniRounds);

        Execute(new ListRoundsCommand(log, rig.Services));

        Assert.Equal("* No rounds in this map *\n", log.Text);
    }

    [Fact]
    public void Play_round_names_the_round_and_restarts_the_game()
    {
        var (rig, log, settings, master) = Rig();
        var round = rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_2"));
        rig.StartRound();

        Execute(new PlayRoundCommand(log, rig.Services), "round_2");

        Assert.Equal("round_2", rig.RoundRules.RoundToPlayNext);
        Assert.Equal(5, settings.RestartGame);
        Assert.Equal("round_2", round.Name);
    }

    [Fact]
    public void An_unknown_round_is_said()
    {
        var (rig, log, settings, _) = Rig();
        rig.Spawn<TeamControlPointRound>(TeamControlPointRound.MapClass, ("targetname", "round_1"));
        rig.StartRound();

        Execute(new PlayRoundCommand(log, rig.Services), "round_nine");

        Assert.Equal("* Round \"round_nine\" not found in this map *\n", log.Text);
        Assert.Equal(string.Empty, rig.RoundRules.RoundToPlayNext);
        Assert.Equal(0, settings.RestartGame);
    }

    [Fact]
    public void A_bare_play_round_prints_its_usage()
    {
        var (rig, log, settings, _) = Rig();

        new PlayRoundCommand(log, rig.Services).RunLine("   ", log.Msg);

        Assert.Contains("usage: tf_playround <name>", log.Text);
        Assert.Equal(0, settings.RestartGame);
    }
}
