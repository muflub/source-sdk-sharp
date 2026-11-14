using TF2.Descent.Server.Skills;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The skill bar lives on the hero's sheet (sheet schema 3): the assign verb changes the
/// sheet through its audited apply, the bar outlives a map change with the sheet, and a
/// respec empties it.
/// </summary>
public sealed class SkillSlotStoreTests
{
    private static SkillClientCommands Commands(SkillRig rig) => new(rig.Runtime, rig.Heroes.Heroes, rig.Heroes.Engine);

    /// <summary>A level 3 hero who has learned Patriot Missile and put it in slot 2 with the client's verb.</summary>
    private static SkillRig Slotted()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 3);
        rig.Learn("patriot_missile");
        Assert.True(Commands(rig).Handle(hero.Player, SkillClientCommands.AssignVerb, ["2", "patriot_missile"]));
        return rig;
    }

    [Fact]
    public void The_assign_verb_puts_the_skill_on_the_heros_sheet()
    {
        var rig = Slotted();

        Assert.Equal(["", "patriot_missile", "", "", "", ""], rig.Hero().Sheet!.SkillSlots.ToArray());
        Assert.Equal("patriot_missile", rig.Slots.In(SkillRig.Index, 2));
    }

    [Fact]
    public void Assigning_none_empties_the_slot_on_the_sheet()
    {
        var rig = Slotted();

        Assert.True(Commands(rig).Handle(rig.Hero().Player, SkillClientCommands.AssignVerb, ["2", "none"]));

        Assert.Equal(["", "", "", "", "", ""], rig.Hero().Sheet!.SkillSlots.ToArray());
        Assert.Null(rig.Slots.In(SkillRig.Index, 2));
    }

    [Fact]
    public void The_bar_outlives_a_map_change()
    {
        var rig = Slotted();

        rig.Heroes.LevelShutdown();
        rig.Join(level: 3);

        Assert.Equal("patriot_missile", rig.Slots.In(SkillRig.Index, 2));
        Assert.Equal("patriot_missile", rig.Hero().Sheet!.SkillSlots[1]);
    }

    [Fact]
    public void A_respec_empties_the_bar()
    {
        var rig = Slotted();

        Assert.True(rig.Hero().Apply(sheet => rig.Heroes.Rules.Heroes.Respec(sheet).Sheet).Ok);

        Assert.Equal([null, null, null, null, null, null], rig.Slots.Of(SkillRig.Index));
    }

    [Fact]
    public void A_skill_the_hero_has_not_learned_is_refused_and_the_sheet_is_unchanged()
    {
        var rig = Slotted();

        string? refused = rig.Runtime.Assign(SkillRig.Index, 3, "salvo");

        Assert.NotNull(refused);
        Assert.Equal(["", "patriot_missile", "", "", "", ""], rig.Hero().Sheet!.SkillSlots.ToArray());
    }
}
