using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Items;
using TF2.Descent.Tests.Items.Ledger;
using TF2.Server.Econ;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// What the gear adds to the sheet (<see cref="HeroGearContributions"/>), what the gear
/// command prints (<see cref="GearReport"/>), and who the equipment claims.
/// </summary>
public sealed class HeroGearFacts
{
    [Fact]
    public void The_contributions_are_the_rules_resolution_of_the_equipped_items()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Equip(rig.Give(EquipmentRig.Item("vest")), "body").Ok);
        Assert.True(rig.Equip(rig.Give(EquipmentRig.Item("dog_tags", new RolledAffix("of_brawn", 0))), "trinket1").Ok);
        var sheet = rig.HeroComponent.Sheet!;
        var equipped = rig.Equipment.Of(EquipmentRig.Index)!.Equipped;

        var given = rig.Gear.ContributionsOf(EquipmentRig.Index, sheet).ToList();

        var expected = rig.Rules.ItemRules.Equipment.Resolve(sheet, equipped).Contributions;
        Assert.NotEmpty(expected);
        Assert.Equal(expected, given);
    }

    [Fact]
    public void Armour_counts_on_the_sheet_though_it_has_no_TF_item()
    {
        // The Padded Vest: armour 10, and no TF row.
        var rig = new EquipmentRig();
        double armour = rig.HeroComponent.Stats!.Armour;

        Assert.True(rig.Equip(rig.Give(EquipmentRig.Item("vest")), "body").Ok);

        Assert.Equal(armour + 10, rig.HeroComponent.Stats!.Armour, 3);
        Assert.Null(rig.Equipment.TFItemOf(rig.Equipment.Of(EquipmentRig.Index)!, "body"));
    }

    [Fact]
    public void An_inactive_items_contributions_count_for_nothing()
    {
        // A planted Steel Helmet (level 10, brawn 25) on a level 1 soldier.
        var rig = new EquipmentRig();
        double armour = rig.HeroComponent.Stats!.Armour;
        rig.Plant("hat", EquipmentRig.Item("helmet"));
        rig.HeroComponent.Recompute();

        var mine = rig.Gear.ContributionsOf(EquipmentRig.Index, rig.HeroComponent.Sheet!).Where(c => c.Source.Id == "hat").ToList();

        Assert.NotEmpty(mine);
        Assert.All(mine, c => Assert.False(c.Source.Counts));
        Assert.Equal(armour, rig.HeroComponent.Stats!.Armour, 3);
    }

    [Fact]
    public void A_player_who_is_no_hero_contributes_nothing()
    {
        var rig = new EquipmentRig();

        Assert.Empty(rig.Gear.ContributionsOf(EquipmentRig.Index + 1, rig.HeroComponent.Sheet!));
    }

    [Fact]
    public void A_hosted_ledger_claims_no_hero()
    {
        // The host's SDK has no equip yet: a hosted hero keeps TF2's own kit.
        var rig = new EquipmentRig(join: false);
        var hosted = new HostedItemLedger(rig.Rules, new TF2.Descent.Tests.Hero.HeroRig.ListHooks<IItemLedgerHook>([]),
                                          new FakeHostItems(rig.Rules), new RecordingLog());
        var equipment = new HeroEquipment(hosted, rig.Keys, rig.Hero.Sheets, rig.Rules, rig.Schema, new On(), rig.BotList, rig.Hero.Engine, rig.Log);
        Assert.Equal(LedgerKind.Hosted, hosted.Kind);

        Assert.False(equipment.KitAnswer(EquipmentRig.Index, TFClass.Soldier, TFLoadoutSlot.Primary).HasOpinion);
    }

    private sealed class On : TF2.Descent.Server.Mode.IDescentModeState
    {
        public bool Active => true;
    }
}
