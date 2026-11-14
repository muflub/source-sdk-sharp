using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Skills;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>The skill verbs a hero's client sends (HE6), and the skill bar's message.</summary>
public sealed class SkillCommandTests
{
    private static SkillClientCommands Commands(SkillRig rig) => new(rig.Runtime, rig.Heroes.Heroes, rig.Heroes.Engine);

    [Fact]
    public void Learn_assign_and_cast_press_and_release_reach_the_runtime()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        var commands = Commands(rig);

        Assert.True(commands.Handle(hero.Player, "tf2_descent_learn", ["patriot_missile"]));
        Assert.True(commands.Handle(hero.Player, "tf2_descent_learn", ["emp_charge"]));
        Assert.True(commands.Handle(hero.Player, "tf2_descent_assign", ["2", "emp_charge"]));
        Assert.True(commands.Handle(hero.Player, "tf2_descent_cast", ["2", "1"]));
        rig.RunSeconds(2);
        Assert.Empty(rig.World.Spawned);
        Assert.True(commands.Handle(hero.Player, "tf2_descent_cast", ["2", "0"]));

        Assert.Equal(1, SkillRules.RealRank(hero.Sheet!, "emp_charge"));
        Assert.Equal("emp_charge", rig.Slots.In(SkillRig.Index, 2));
        Assert.Single(rig.World.Spawned);
    }

    [Fact]
    public void Other_verbs_and_players_who_are_no_heroes_pass_on()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        var commands = Commands(rig);

        Assert.False(commands.Handle(hero.Player, "joinclass", ["soldier"]));
        Assert.False(commands.Handle(SkillRig.PlayerOf(9), "tf2_descent_cast", ["1", "1"]));
    }

    [Fact]
    public void The_skill_bar_takes_its_keys_place_among_the_mods_messages()
    {
        var server = typeof(SourceSharp.Server.ServerModule).Assembly;
        var serverSide = LoadedAssemblies.Types(server, new RecordingLog())
            .Where(t => t.Assembly.GetName().Name != "TF2.Descent.Client");
        var registry = new UserMessageRegistry(new RecordingLog(), receives: false);
        registry.Register(serverSide, server);
        var names = registry.Messages.Select(m => m.Name).ToList();

        Assert.Contains("TF2.Descent:skill_bar", names);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public void A_full_bar_fits_one_message_and_reads_back()
    {
        var bar = new SkillBar
        {
            Skills = string.Join(',', Enumerable.Repeat(new string('x', SkillBar.MaxId), SkillBar.Slots)),
            ReadyTicks = [int.MaxValue, 1, 2, 3, 4, 5],
            ChargeSlot = 6,
            ChargeStartTick = int.MaxValue,
            ToggledMask = 0x3F,
        };

        Span<byte> wire = stackalloc byte[255];
        int length = bar.Write(wire);
        var reader = new UserMessageReader(wire[..length]);
        var view = SkillBar.Read(ref reader).View();

        Assert.InRange(length, 1, 255);
        Assert.False(reader.Overran);
        Assert.Equal(SkillBar.Slots, view.Skills.Count);
        Assert.All(view.Skills, s => Assert.Equal(SkillBar.MaxId, s.Length));
        Assert.Equal([int.MaxValue, 1, 2, 3, 4, 5], view.ReadyTicks);
        Assert.Equal((6, int.MaxValue, 0x3F), (view.ChargeSlot, view.ChargeStartTick, view.ToggledMask));
    }
}
