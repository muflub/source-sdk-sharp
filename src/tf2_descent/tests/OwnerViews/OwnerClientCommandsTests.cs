using SourceSharp.Tests;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Server.Skills;
using TF2.Descent.Tests.Gear;
using TF2.Descent.Tests.Skills;

using Xunit;

namespace TF2.Descent.Tests.OwnerViews;

/// <summary>
/// The sheet's and inventory's buttons as client verbs (plan_descent_logic UI3), through the
/// real server services: a verb acts for the player who sent it, the rules decide, a refusal
/// changes nothing and is told to the hero with its reason.
/// </summary>
public sealed class OwnerClientCommandsTests
{
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Spending_a_point_on_the_sheet_changes_the_servers_sheet()
    {
        var rig = new OwnerRig();
        rig.Level(3);
        int points = rig.Hero.Sheet!.AttributePoints;

        Assert.True(rig.Command(OwnerClientCommands.Spend, "aim", "2"));

        Assert.Equal(22, rig.Hero.Sheet!.Base("aim"));
        Assert.Equal(points - 2, rig.Hero.Sheet!.AttributePoints);
        Assert.True(rig.Rules.Heroes.Audit(rig.Hero.Sheet!).Ok);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_spend_with_no_points_changes_nothing_and_tells_why()
    {
        var rig = new OwnerRig();
        var before = rig.Hero.Sheet!;

        Assert.True(rig.Command(OwnerClientCommands.Spend, "aim", "1"));

        Assert.Same(before, rig.Hero.Sheet);
        Assert.Equal(("#Descent_Refused_NoPoints", "Aim"), rig.Told.Single());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_spend_on_no_attribute_changes_nothing_and_tells_why()
    {
        var rig = new OwnerRig();
        rig.Level(3);
        var before = rig.Hero.Sheet!;

        rig.Command(OwnerClientCommands.Spend, "luck", "1");

        Assert.Same(before, rig.Hero.Sheet);
        Assert.Equal("#Descent_Refused_UnknownAttribute", rig.Told.Single().Text);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_verb_from_a_player_with_no_hero_passes_on()
    {
        var rig = new OwnerRig();

        Assert.False(rig.Commands.Handle(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(OwnerRig.Index + 1), OwnerClientCommands.Spend, ["aim", "1"]));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Another_command_passes_on()
    {
        var rig = new OwnerRig();

        Assert.False(rig.Command("say", "hello"));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Unequipping_and_equipping_through_the_verbs_moves_the_shovel()
    {
        var rig = new OwnerRig();
        long shovel = rig.Gear.Held.Inventory.Equipped["melee"].Id;

        Assert.True(rig.Command(OwnerClientCommands.Unequip, "melee"));
        Assert.False(rig.Gear.Held.Inventory.Equipped.ContainsKey("melee"));
        int cell = rig.Gear.Held.Inventory.Backpack.Single(p => p.Value.Id == shovel).Key;

        Assert.True(rig.Command(OwnerClientCommands.Equip, cell.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(shovel, rig.Gear.Held.Inventory.Equipped["melee"].Id);
        Assert.Empty(rig.Told);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Equipping_an_item_the_hero_fails_is_refused_with_the_reason_and_changes_nothing()
    {
        var rig = new OwnerRig();
        rig.Level(6);   // the Direct Hit's level, not its aim 30
        int cell = rig.Gear.Give(EquipmentRig.Item("direct_hit"));
        long launcher = rig.Gear.Held.Inventory.Equipped["primary"].Id;

        rig.Command(OwnerClientCommands.Equip, cell.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(launcher, rig.Gear.Held.Inventory.Equipped["primary"].Id);
        Assert.Equal(("#Descent_Refused_requirement", "Direct Hit"), rig.Told.Single());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Moving_a_backpack_item_through_the_verb_moves_it()
    {
        var rig = new OwnerRig();
        int cell = rig.Gear.Give(EquipmentRig.Item("shotgun"));
        long id = rig.Gear.Held.Inventory.Backpack[cell].Id;

        rig.Command(OwnerClientCommands.Move, cell.ToString(System.Globalization.CultureInfo.InvariantCulture), "7");

        Assert.Equal(id, rig.Gear.Held.Inventory.Backpack[7].Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Moving_a_backpack_potion_to_a_belt_slot_through_the_verb_belts_it()
    {
        var rig = new OwnerRig();
        int cell = rig.Gear.Carry(TF2.Descent.Tests.Hero.BeltRig.Carried("health_medium"));
        long id = rig.Gear.Held.Inventory.Backpack[cell].Id;

        rig.Command(OwnerClientCommands.Move, cell.ToString(System.Globalization.CultureInfo.InvariantCulture), "belt4");

        Assert.Equal(id, rig.Gear.Held.Inventory.Belt[3].Id);
        Assert.False(rig.Gear.Held.Inventory.Backpack.ContainsKey(cell));
        Assert.Empty(rig.Told);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Moving_a_belt_slot_to_a_backpack_cell_through_the_verb_takes_it_off_the_belt_into_that_cell()
    {
        var rig = new OwnerRig();
        long id = rig.Gear.Held.Inventory.Belt[0].Id;

        rig.Command(OwnerClientCommands.Move, "belt1", "9");

        Assert.False(rig.Gear.Held.Inventory.Belt.ContainsKey(0));
        Assert.Equal(id, rig.Gear.Held.Inventory.Backpack[9].Id);
        Assert.Empty(rig.Told);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Moving_gear_to_the_belt_is_refused_with_the_reason_and_changes_nothing()
    {
        var rig = new OwnerRig();
        int cell = rig.Gear.Give(EquipmentRig.Item("shotgun"));

        rig.Command(OwnerClientCommands.Move, cell.ToString(System.Globalization.CultureInfo.InvariantCulture), "belt4");

        Assert.True(rig.Gear.Held.Inventory.Backpack.ContainsKey(cell));
        Assert.False(rig.Gear.Held.Inventory.Belt.ContainsKey(3));
        Assert.Equal(("#Descent_Refused_not_a_potion", "Shotgun"), rig.Told.Single());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Dropping_a_backpack_item_through_the_verb_puts_it_on_the_floor()
    {
        var rig = new OwnerRig();
        int cell = rig.Gear.Give(EquipmentRig.Item("shotgun"));
        long id = rig.Gear.Held.Inventory.Backpack[cell].Id;

        rig.Command(OwnerClientCommands.Drop, cell.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.DoesNotContain(rig.Gear.Held.Inventory.Backpack.Values, h => h.Id == id);
        Assert.Contains(rig.Gear.Ledger.Floor, f => f.Id == id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Equipping_an_empty_cell_is_refused_as_no_item()
    {
        var rig = new OwnerRig();

        rig.Command(OwnerClientCommands.Equip, "11");

        Assert.Equal("#Descent_Refused_no_item", rig.Told.Single().Text);
    }

    [Fact]
    public void A_refusal_the_language_file_does_not_word_is_told_by_its_code()
    {
        Assert.Equal("#Descent_Refused", OwnerClientCommands.TokenOf("host:Unavailable:down"));
        Assert.Equal("#Descent_Refused_requirement", OwnerClientCommands.TokenOf("requirement"));
    }

    [Fact]
    public void Assigning_an_unlearned_skill_through_the_verb_is_refused_with_the_reason_and_changes_nothing()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 3);
        var commands = new SkillClientCommands(rig.Runtime, rig.Heroes.Heroes, rig.Heroes.Engine, rig.Told);
        var before = hero.Sheet!;

        Assert.True(commands.Handle(hero.Player, SkillClientCommands.AssignVerb, ["1", "patriot_missile"]));

        Assert.Same(before, rig.Hero().Sheet);
        Assert.Contains("is not learned", rig.Told.Notices.Single().Text);
    }

    [Fact]
    public void Buying_a_rank_through_the_verb_changes_the_servers_sheet()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 3);
        var commands = new SkillClientCommands(rig.Runtime, rig.Heroes.Heroes, rig.Heroes.Engine, rig.Told);

        Assert.True(commands.Handle(hero.Player, SkillClientCommands.Learn, ["patriot_missile"]));

        Assert.Equal(1, rig.Hero().Sheet!.SkillRanks["patriot_missile"]);
    }

    [Fact]
    public void Buying_a_skill_above_the_heros_level_through_the_verb_changes_nothing_and_tells_why()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 3);
        var commands = new SkillClientCommands(rig.Runtime, rig.Heroes.Heroes, rig.Heroes.Engine, rig.Told);
        var before = hero.Sheet!;

        commands.Handle(hero.Player, SkillClientCommands.Learn, ["salvo"]);

        Assert.Same(before, rig.Hero().Sheet);
        Assert.Contains("needs hero level 6", rig.Told.Notices.Single().Text);
    }
}
