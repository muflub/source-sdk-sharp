using SourceSharp.Tests;
using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// The kit follows the equipment (<see cref="TF2.Descent.Server.Gear.HeroKitSettle"/>):
/// an equip through the ledger rebuilds the kit where the hero stands, holds health and ammo
/// under a maximum that fell and never raises them, rebuilds nothing when the weapons are as
/// they were, follows a sheet change that wakes an item, and puts the first kit on once.
/// </summary>
public sealed class HeroKitSettleFacts
{
    private static string Primary => TFAmmoNames.Name(TFAmmo.Primary);

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_first_kit_puts_the_starting_weapons_on()
    {
        var rig = new EquipmentRig();

        Assert.Equal(["melee", "primary", "secondary"], rig.Held.Inventory.Equipped.Keys);
        Assert.Equal(["rocket_launcher", "shotgun", "shovel"],
                     rig.Held.Inventory.Equipped.Values.Select(h => h.Item.BaseId).Order());
        Assert.Contains("first kit equipped: primary rocket_launcher", rig.Log.Text);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_first_kit_is_put_on_once()
    {
        var rig = new EquipmentRig();
        foreach (var slot in new[] { "primary", "secondary", "melee" })
            Assert.True(rig.Unequip(slot).Ok);

        // The ledger loads the hero again (a later level's load, as the hook would hear it),
        // with nothing on him: the rule that put the kit on the first time must not again.
        rig.Settle.OnItemsChanged(new ItemsChanged(EquipmentRig.Key, "load", [], [], [], [], 0, 0));

        Assert.Empty(rig.Held.Inventory.Equipped);
        Assert.Contains(EquipmentRig.Key, rig.Settle.FirstKitDone); // the stimulus: it ran the first time
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Equipping_rebuilds_the_kit_once()
    {
        var rig = new EquipmentRig();
        int before = rig.Refresh.Rebuilt;
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(before + 1, rig.Refresh.Rebuilt);
        Assert.Single(rig.Refresh.Holding[TFLoadoutSlot.Primary]!.Attributes);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Equipping_neither_heals_nor_moves_the_hero()
    {
        var rig = new EquipmentRig();
        rig.Health = 120;
        rig.Hero.Engine.Clear();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(120, rig.Health);
        Assert.Equal(0, rig.Hero.Engine.CountOf("Entities.SetHealth"));
        Assert.Equal(0, rig.Hero.Engine.CountOf("Entities.SetOrigin"));
        Assert.Equal(0, rig.Hero.Engine.CountOf("Entities.Teleport"));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_with_no_class_yet_has_no_kit_to_rebuild()
    {
        // Seen live after a map change: a joining hero is "alive" before he has a class, and
        // the settle asked TF2 for a kit it had no class for.
        var rig = new EquipmentRig();
        rig.Class = TFClass.Undefined;
        int asked = rig.Refresh.Asked;
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);
        Spend(rig, "brawn", 1, level: 2);
        rig.Frame();

        Assert.Equal(asked, rig.Refresh.Asked);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_maximum_that_falls_holds_the_health_under_it()
    {
        var rig = new EquipmentRig();
        rig.Health = 190;
        rig.Refresh.Changes = () => rig.Resupply.MaxHealthNow = 175;
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(175, rig.Health);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_maximum_that_falls_holds_the_ammo_under_it()
    {
        var rig = new EquipmentRig();
        rig.Refresh.Changes = () => rig.Resupply.Max[TFAmmo.Primary] = 15;
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(15, rig.Ammo.Counts[Primary]);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_maximum_that_rises_raises_nothing()
    {
        var rig = new EquipmentRig();
        rig.Health = 120;
        rig.Ammo.Counts[Primary] = 7;
        rig.Refresh.Changes = () =>
        {
            rig.Resupply.MaxHealthNow = 300;
            rig.Resupply.Max[TFAmmo.Primary] = 40;
        };
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(120, rig.Health);
        Assert.Equal(7, rig.Ammo.Counts[Primary]);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_health_under_a_fallen_maximum_stays()
    {
        var rig = new EquipmentRig();
        rig.Health = 90;
        rig.Refresh.Changes = () => rig.Resupply.MaxHealthNow = 175;
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        Assert.Equal(90, rig.Health);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_change_that_leaves_the_weapons_as_they_were_rebuilds_nothing()
    {
        var rig = new EquipmentRig();
        int asked = rig.Refresh.Asked;
        int from = rig.Give(EquipmentRig.Item("dog_tags"));

        Assert.True(rig.Equip(from, "trinket1").Ok);

        Assert.Equal(asked, rig.Refresh.Asked);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_change_that_leaves_the_weapons_as_they_were_still_recomputes_the_sheet()
    {
        // Dog Tags: +5 maximum life.
        var rig = new EquipmentRig();
        double life = rig.HeroComponent.Stats!.MaxLife;
        int from = rig.Give(EquipmentRig.Item("dog_tags"));

        Assert.True(rig.Equip(from, "trinket1").Ok);

        Assert.Equal(life + 5, rig.HeroComponent.Stats!.MaxLife, 3);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_sheet_change_that_wakes_a_weapon_rebuilds_the_kit_at_the_frames_end()
    {
        var rig = InactivePainTrain();
        int rebuilt = rig.Refresh.Rebuilt;

        Spend(rig, "brawn", 5);
        rig.Frame();

        Assert.Equal(rebuilt + 1, rig.Refresh.Rebuilt);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Meeting_a_requirement_later_activates_the_item_with_nothing_moved()
    {
        var rig = InactivePainTrain();
        var equipped = rig.Held.Inventory.Equipped;
        Assert.Equal(SourceState.Inactive, rig.Equipment.Of(EquipmentRig.Index)!.Result.State("melee").State);

        Spend(rig, "brawn", 5);

        Assert.Equal(SourceState.Active, rig.Equipment.Of(EquipmentRig.Index)!.Result.State("melee").State);
        Assert.Same(equipped, rig.Held.Inventory.Equipped);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_sheet_change_that_wakes_nothing_rebuilds_nothing()
    {
        var rig = new EquipmentRig();
        int asked = rig.Refresh.Asked;

        Assert.True(rig.HeroComponent.Apply(sheet => rig.Hero.AtLevel(sheet, 3)).Ok);
        rig.Frame();

        Assert.Equal(asked, rig.Refresh.Asked);
        Assert.Empty(rig.Settle.Pending);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_unidentified_item_is_refused_with_the_rules_reason_and_changes_nothing()
    {
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)) with { Identified = false });
        var before = rig.Held;
        int rebuilt = rig.Refresh.Rebuilt;

        var r = rig.Equip(from, "primary");

        Assert.Equal(InventoryRefusals.Unidentified, r.Refusal);
        Assert.Same(before.Inventory, rig.Held.Inventory);
        Assert.Equal(rebuilt, rig.Refresh.Rebuilt);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_wrong_class_item_is_refused_with_the_rules_reason_and_changes_nothing()
    {
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("flamethrower"));
        var before = rig.Held;

        var r = rig.Equip(from, "primary");

        Assert.Equal(InventoryRefusals.WrongClass, r.Refusal);
        Assert.Same(before.Inventory, rig.Held.Inventory);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_equip_asks_the_rules_with_the_heros_live_sheet()
    {
        // The Pain Train asks level 6: the ledger's own copy of the sheet is a new level 1
        // soldier until the equipment tells it the live one.
        var rig = new EquipmentRig();
        Spend(rig, "brawn", 5, level: 6);
        int from = rig.Give(EquipmentRig.Item("pain_train"));

        var r = rig.Equip(from, "melee");

        Assert.True(r.Ok, r.Refusal);
    }

    /// <summary>A level 6 soldier with a rolled Pain Train (brawn 25; Heavy: mult_dmg) on, then respecced to brawn 20: it stays on, inactive, its affix asleep.</summary>
    private static EquipmentRig InactivePainTrain()
    {
        var rig = new EquipmentRig();
        Spend(rig, "brawn", 5, level: 6);
        int from = rig.Give(EquipmentRig.Item("pain_train", new RolledAffix("heavy", 0)));
        Assert.True(rig.Equip(from, "melee").Ok);
        Assert.True(rig.HeroComponent.Apply(sheet => rig.Rules.Heroes.Respec(sheet).Sheet).Ok);
        rig.Frame();
        Assert.Equal(SourceState.Inactive, rig.Equipment.Of(EquipmentRig.Index)!.Result.State("melee").State);
        return rig;
    }

    private static void Spend(EquipmentRig rig, string attribute, int points, int level = 0)
    {
        var result = rig.HeroComponent.Apply(sheet =>
        {
            var at = level > sheet.Level ? rig.Hero.AtLevel(sheet, level) : sheet;
            return rig.Rules.Heroes.Spend(at, attribute, points).Sheet;
        });
        Assert.True(result.Ok, result.Rule);
    }
}
