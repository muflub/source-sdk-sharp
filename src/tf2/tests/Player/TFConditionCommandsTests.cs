using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFConditionCommands"/>: stock's condition cheat verbs
/// (tf_player.cpp:7195-7292) over the one condition set.
/// </summary>
public sealed class TFConditionCommandsTests
{
    private static readonly EntityHandle Teammate = new(0x0001_0000u | 4);

    /// <summary>Two named players on RED, as the round roster holds them.</summary>
    private sealed class NamedRoster : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) =>
            team == TFTeam.Red ? [PlayerRig.Player, Teammate] : [];

        public TFTeam TeamOf(EntityHandle player) => TFTeam.Red;

        public bool IsAlive(EntityHandle player) => true;

        public float DeathTime(EntityHandle player) => -1f;

        public bool IsReadyToPlay(EntityHandle player) => true;

        public void ForceRespawn(EntityHandle player) { }

        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;

        public string Name(EntityHandle player) => player == PlayerRig.Player ? "Burning Man" : "Other Guy";

        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    private static (PlayerRig Rig, TFConditionCommands Commands, TFPlayerConditions Conditions, RecordingLog Log) Rig(string cheats = "1")
    {
        var rig = new PlayerRig().Join();
        rig.Engine.Returns("Console.FindConVarValue", cheats);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var commands = new TFConditionCommands(conditions, new NamedRoster(), rig.Engine, rig.Log);
        return (rig, commands, conditions, rig.Log);
    }

    [Fact]
    public void Addcond_with_cheats_adds_the_condition_for_the_duration()
    {
        var (rig, commands, conditions, _) = Rig();

        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["22", "4"]));

        Assert.True(rig.State.InCond(TFCond.Burning));
        Assert.Equal(4f, conditions.Of(PlayerRig.Index)!.Duration(TFCond.Burning));
    }

    [Fact]
    public void Addcond_without_a_duration_is_permanent()
    {
        var (rig, commands, conditions, _) = Rig();

        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, [((int)TFCond.Invulnerable).ToString()]);

        Assert.True(rig.State.InCond(TFCond.Invulnerable));
        Assert.Equal(TFConditions.Permanent, conditions.Of(PlayerRig.Index)!.Duration(TFCond.Invulnerable));
    }

    [Fact]
    public void Addcond_without_cheats_does_nothing()
    {
        var (rig, commands, _, _) = Rig("0");

        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["22", "4"]));

        Assert.False(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void Addcond_takes_a_condition_name()
    {
        var (rig, commands, _, _) = Rig();

        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["TF_COND_BURNING", "4"]);

        Assert.True(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void A_leading_zero_stays_condition_zero()
    {
        var (rig, commands, _, _) = Rig();

        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["0"]);

        Assert.True(rig.State.InCond(TFCond.Aiming));
    }

    [Fact]
    public void A_condition_out_of_range_is_refused_with_stocks_warning()
    {
        var (rig, commands, _, log) = Rig();

        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["131"]));

        Assert.True(rig.State.Conditions().IsEmpty);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("Failed to addcond 131 to player"));
    }

    [Fact]
    public void A_name_no_condition_matches_is_refused_with_stocks_warning()
    {
        var (rig, commands, _, log) = Rig();

        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["not_a_thing"]);

        Assert.True(rig.State.Conditions().IsEmpty);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("Failed to addcond not_a_thing to player"));
    }

    [Fact]
    public void The_target_argument_adds_to_the_player_whose_name_matches()
    {
        var (rig, commands, _, _) = Rig();

        // The speaker is the teammate slot; the substring names the rig's player.
        commands.Handle(Teammate, TFConditionCommands.Add, ["22", "4", "Burning"]);

        Assert.True(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void Removecond_removes_the_condition()
    {
        var (rig, commands, conditions, _) = Rig();
        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["22", "4"]);

        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Remove, ["TF_COND_BURNING"]));

        Assert.False(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void Removecond_all_clears_every_condition()
    {
        var (rig, commands, _, _) = Rig();
        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["22"]);
        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["5"]);

        commands.Handle(PlayerRig.Player, TFConditionCommands.Remove, ["all"]);

        Assert.False(rig.State.InCond(TFCond.Burning));
        Assert.False(rig.State.InCond(TFCond.Invulnerable));
    }

    [Fact]
    public void Removecond_of_an_unknown_condition_warns_and_leaves_the_rest()
    {
        var (rig, commands, _, log) = Rig();
        commands.Handle(PlayerRig.Player, TFConditionCommands.Add, ["22"]);

        commands.Handle(PlayerRig.Player, TFConditionCommands.Remove, ["999"]);

        Assert.True(rig.State.InCond(TFCond.Burning));
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("Failed to removecond 999 from player"));
    }

    [Fact]
    public void Other_commands_pass_through()
    {
        var (_, commands, _, _) = Rig();

        Assert.False(commands.Handle(PlayerRig.Player, "joinclass", ["soldier"]));
    }

    [Fact]
    public void With_no_condition_argument_nothing_happens()
    {
        var (rig, commands, _, _) = Rig();

        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Add, []));
        Assert.True(commands.Handle(PlayerRig.Player, TFConditionCommands.Remove, []));

        Assert.False(rig.State.InCond(TFCond.Aiming));
    }
}
