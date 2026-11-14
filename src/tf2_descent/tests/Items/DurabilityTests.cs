using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// Wear (plan_descent_logic RU8; plan_tf2_rpg 2h, D21): per use by weapon family, per hit for
/// armour, the 1 to 0 transition, broken items grant nothing through the one Contribution path,
/// wear never goes below 0 or removes the item, and Indestructible never wears.
/// </summary>
public sealed class DurabilityTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static Durability Wear => R.ItemRules.Durability;

    private static ItemInstance Fresh(string baseId, params RolledAffix[] affixes)
    {
        int max = R.Content.ItemBases[baseId].Durability;
        return Item(baseId, 1, 20, affixes) with { Durability = max, MaxDurability = max };
    }

    [Fact]
    public void Every_weapon_family_wears_at_its_own_rate_and_a_minigun_is_not_a_shovel()
    {
        var s = R.Content.DurabilitySettings;

        Assert.All(R.Content.Families.Rows, f => Assert.True(s.UsesPerPoint(f.Id) > 0, f.Id));
        Assert.True(s.UsesPerPoint("minigun") > s.UsesPerPoint("melee"));
        Assert.Equal(s.UsesPerPoint("minigun"), Wear.PerPoint(Fresh("minigun")));
        Assert.Equal(s.UsesPerPoint("melee"), Wear.PerPoint(Fresh("shovel")));
    }

    [Fact]
    public void A_family_with_no_wear_rate_is_a_load_error()
    {
        Assert.Empty(Errors(Planted(DurabilityTable.TableName, _ => { })));

        var errors = Errors(Planted(DurabilityTable.TableName, t =>
        {
            var families = t["wear"]!["families"]!.AsArray();
            families.Remove(families.Single(f => (string?)f!["family"] == "minigun"));
        }));

        var e = Assert.Single(errors);
        Assert.Equal((DurabilityTable.TableName, "minigun", Rules.Content.ContentRule.DanglingReference), (e.Table, e.Row, e.Rule));
    }

    [Fact]
    public void A_weapon_loses_one_point_per_its_familys_uses()
    {
        foreach (var id in new[] { "rocket_launcher", "minigun", "shovel", "shotgun", "flamethrower" })
        {
            var item = Fresh(id);
            int per = Wear.PerPoint(item);

            var almost = Wear.Use(item, per - 1);
            var one = Wear.Use(almost.Item, 1);

            Assert.Equal(0, almost.PointsLost);
            Assert.Equal(1, one.PointsLost);
            Assert.Equal(item.MaxDurability - 1, one.Item.Durability);
            Assert.Equal(item.MaxDurability - 10, Wear.Use(item, per * 10).Item.Durability);
        }
    }

    [Fact]
    public void Armour_loses_one_point_per_its_hits_taken_and_does_not_wear_by_use()
    {
        var vest = Fresh("vest");
        int per = R.Content.DurabilitySettings.ArmourHitsPerPoint;

        Assert.Equal(vest.MaxDurability - 3, Wear.Hit(vest, per * 3).Item.Durability);
        Assert.Equal(vest, Wear.Use(vest, 1000).Item);
        Assert.Equal(Fresh("shovel"), Wear.Hit(Fresh("shovel"), 1000).Item);
    }

    [Fact]
    public void A_wear_curse_makes_an_item_wear_twice_as_fast()
    {
        var plain = Fresh("shovel");
        var fragile = Fresh("shovel", new RolledAffix("fragile", 0));
        int per = Wear.PerPoint(plain);

        Assert.Equal(1, Wear.Use(plain, per).PointsLost);
        Assert.Equal(2, Wear.Use(fragile, per).PointsLost);
    }

    [Fact]
    public void Durability_goes_from_1_to_0_and_the_item_is_broken()
    {
        var item = Fresh("rocket_launcher") with { Durability = 1 };

        var worn = Wear.Use(item, Wear.PerPoint(item));

        Assert.Equal(0, worn.Item.Durability);
        Assert.True(worn.BrokeNow);
        Assert.True(R.ItemRules.Equipment.IsBroken(worn.Item));
        Assert.False(R.ItemRules.Equipment.IsBroken(item));
    }

    [Fact]
    public void Wear_never_goes_below_0_or_removes_the_item()
    {
        var item = Fresh("minigun") with { Durability = 3 };

        var worn = Wear.Use(item, 1_000_000);
        var again = Wear.Use(worn.Item, 1_000_000);

        Assert.Equal((0, 3), (worn.Item.Durability, worn.PointsLost));
        Assert.Equal((0, 0, false), (again.Item.Durability, again.PointsLost, again.BrokeNow));
        Assert.Equal((item.BaseId, item.MaxDurability, item.Count), (again.Item.BaseId, again.Item.MaxDurability, again.Item.Count));
    }

    [Fact]
    public void An_indestructible_affix_never_wears()
    {
        var item = Fresh("shovel", new RolledAffix("of_permanence", 10000));

        Assert.True(Wear.IsIndestructible(item));
        Assert.Equal(item, Wear.Use(item, 1_000_000).Item);
    }

    [Fact]
    public void Australium_is_indestructible_without_the_affix()
    {
        var item = R.ItemRules.Roller.Roll(Ctx(4, 25), 1);

        Assert.Equal(4, item.Rarity);
        Assert.DoesNotContain(item.Affixes, a => a.Id == "of_permanence");
        Assert.True(Wear.IsIndestructible(item));
        Assert.Equal(item, Wear.Use(item, 1_000_000).Item);
    }

    [Fact]
    public void A_broken_item_contributes_exactly_zero_to_every_derived_stat()
    {
        var sheet = Soldier(R, brawn: 60, aim: 60);
        var hat = Fresh("war_helm", new RolledAffix("of_grit", 10000), new RolledAffix("robust", 10000), new RolledAffix("of_fire_warding", 10000));
        var broken = hat with { Durability = 0 };

        var none = R.Heroes.Stats(sheet);
        var whole = R.Heroes.Stats(sheet, R.ItemRules.Equipment.Resolve(sheet, [new EquippedItem("hat", hat)]).Contributions);
        var result = R.ItemRules.Equipment.Resolve(sheet, [new EquippedItem("hat", broken)]);
        var with = R.Heroes.Stats(sheet, result.Contributions);

        Assert.Equal((SourceState.Broken, InactiveReason.Broken), (result.State("hat").State, result.State("hat").Reason));
        Assert.True(whole.MaxLife > none.MaxLife);   // the stimulus: whole, it gives life
        Assert.All(R.Content.Stats.Rows, s => Assert.Equal(none.Total(s.Id), with.Total(s.Id)));
        Assert.Equal((none.MaxLife, none.MaxMana, none.Armour, none.Resist("fire")), (with.MaxLife, with.MaxMana, with.Armour, with.Resist("fire")));
    }

    [Fact]
    public void A_broken_weapon_fires_as_its_stock_base()
    {
        var launcher = Fresh("rocket_launcher", new RolledAffix("heavy", 10000)) with { Durability = 0 };
        var result = R.ItemRules.Equipment.Resolve(Soldier(R), [new EquippedItem("primary", launcher)]);

        Assert.True(result.State("primary").FiresAsStock);
        Assert.Empty(result.TFAttributes);
    }
}
