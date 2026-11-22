using SourceSharp.Tests;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Gear;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>What the equipment's dev commands print (<see cref="GearReport"/>, <c>tf2_descent_gear</c>).</summary>
public sealed class EquipmentCommandFacts
{
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_gear_print_shows_an_inactive_item_and_the_attributes_handed_to_TF2()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Equip(rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0))), "primary").Ok);
        rig.Plant("hat", EquipmentRig.Item("helmet"));

        string text = new GearReport(rig.Equipment, new HeroWear(rig.Ledger, rig.Keys, rig.Rules, rig.Schema, rig.Log)).Gear(rig.Equipment.Of(EquipmentRig.Index)!);

        Assert.Contains("  primary: #", text);
        Assert.Contains("rocket_launcher strange ilvl 1 dur 60/60 Active\n", text);
        Assert.Contains("(mult_dmg) 1.2\n", text);
        Assert.Contains("helmet stock ilvl 1 dur 60/60 Inactive (Level)\n", text);
        Assert.Contains("    tf: none (no TF item)\n", text);
        Assert.Contains("  amulet: empty\n", text);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_gear_print_shows_the_wear_written_and_the_wear_pending()
    {
        var rig = new EquipmentRig();
        var wear = new HeroWear(rig.Ledger, rig.Keys, rig.Rules, rig.Schema, rig.Log);
        var launcher = rig.Equipment.TFItemOf(rig.Equipment.Of(EquipmentRig.Index)!, "primary")!.Definition;
        for (int i = 0; i < 6; i++)
            wear.Fired(EquipmentRig.Index, launcher);
        wear.FlushDue();
        wear.Fired(EquipmentRig.Index, launcher);

        string text = new GearReport(rig.Equipment, wear).Gear(rig.Equipment.Of(EquipmentRig.Index)!);

        Assert.Contains("rocket_launcher stock ilvl 1 dur 59/60 Active\n    wear: 2 toward the next point, pending 1 uses 0 hits\n", text);
    }
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_repair_command_repairs_in_the_town_for_its_price_and_in_the_field_at_a_cost_to_the_maximum()
    {
        var rig = new EquipmentRig();
        var wear = new HeroWear(rig.Ledger, rig.Keys, rig.Rules, rig.Schema, rig.Log);
        var h = rig.Held;
        rig.Ledger.Replace(h with { Inventory = h.Inventory with { Australium = 1000 } });
        rig.Plant("primary", h.Inventory.Equipped["primary"].Item with { Durability = 30 }, h.Inventory.Equipped["primary"].Id);
        rig.Plant("secondary", h.Inventory.Equipped["secondary"].Item with { Durability = 0 }, h.Inventory.Equipped["secondary"].Id);
        long price = rig.Rules.Repairs.Price(rig.Held.Inventory.Equipped["primary"].Item);
        rig.Give(EquipmentRig.Item("repair_kit") with { Durability = 0, MaxDurability = 0 });   // a field repair spends a Repair Kit

        new RepairCommand(rig.Equipment, wear, rig.Log).Execute(EquipmentRig.Index, "primary", "town");
        new RepairCommand(rig.Equipment, wear, rig.Log).Execute(EquipmentRig.Index, "secondary", "field");

        var after = rig.Held.Inventory;
        Assert.Equal((60, 60), (after.Equipped["primary"].Item.Durability, after.Equipped["primary"].Item.MaxDurability));
        Assert.Equal(1000 - price, after.Australium);
        Assert.True(after.Equipped["secondary"].Item.MaxDurability < 50);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("town repair of primary (rocket_launcher): dur 30/60 -> 60/60, paid " + price, StringComparison.Ordinal));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_wear_command_sets_a_worn_items_durability()
    {
        var rig = new EquipmentRig();

        new WearCommand(rig.Equipment, new HeroWear(rig.Ledger, rig.Keys, rig.Rules, rig.Schema, rig.Log), rig.Log).Execute(EquipmentRig.Index, "primary", 0);

        Assert.Equal((0, 60), (rig.Held.Inventory.Equipped["primary"].Item.Durability, rig.Held.Inventory.Equipped["primary"].Item.MaxDurability));
        Assert.True(rig.Equipment.Of(EquipmentRig.Index)!.Result.State("primary").FiresAsStock);
    }
}
