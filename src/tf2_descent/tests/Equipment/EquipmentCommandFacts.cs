using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Gear;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>What the equipment's dev commands print (<see cref="GearReport"/>, <c>tf2_descent_gear</c>).</summary>
public sealed class EquipmentCommandFacts
{
    [Fact]
    public void The_gear_print_shows_an_inactive_item_and_the_attributes_handed_to_TF2()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Equip(rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0))), "primary").Ok);
        rig.Plant("hat", EquipmentRig.Item("helmet"));

        string text = new GearReport(rig.Equipment).Gear(rig.Equipment.Of(EquipmentRig.Index)!);

        Assert.Contains("  primary: #", text);
        Assert.Contains("rocket_launcher strange ilvl 1 dur 60/60 Active\n", text);
        Assert.Contains("(mult_dmg) 1.2\n", text);
        Assert.Contains("helmet stock ilvl 1 dur 60/60 Inactive (Level)\n", text);
        Assert.Contains("    tf: none (no TF item)\n", text);
        Assert.Contains("  amulet: empty\n", text);
    }
}
