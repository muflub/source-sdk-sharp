using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The item tables (plan_descent_logic RU3; plan_tf2_rpg 2b; D4, D5, R12, R29): what ships,
/// how it ties to TF's own tables, and one fact per validation rule, each planted in a copy
/// of the shipped tables.
/// </summary>
public sealed class ItemContentTests
{
    private static DescentContent Shipped() => NewRules().Content;

    //-------------------------------------------------------------------------
    // The shipped tables
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_item_tables_load_with_no_errors()
    {
        var result = new ContentLoader().Load(ShippedCopy());

        Assert.Empty(result.Errors);
        Assert.True(result.Content.ItemBases.Count > 40, $"{result.Content.ItemBases.Count} bases");   // the stimulus: the table was read
        Assert.True(result.Content.Affixes.Count > 30, $"{result.Content.Affixes.Count} affixes");
    }

    [Fact]
    public void Tiers_are_the_five_rarities_in_order()
    {
        Assert.Equal(["Stock", "Vintage", "Strange", "Unusual", "Australium"], NewRules().Tiers);
    }

    [Fact]
    public void The_rarity_affix_counts_are_D5s()
    {
        var r = Shipped().Rarities.Rows;

        Assert.Equal([(0, 0), (1, 2), (3, 4), (5, 6)], r.Take(4).Select(x => (x.MinAffixes, x.MaxAffixes)));
        Assert.True(r[3].Unusual);
        Assert.True(r[4].Unique && r[4].Indestructible);
        Assert.True(Shipped().Uniques.Count >= 2);
    }

    [Fact]
    public void Every_base_names_a_working_items_working_row_whose_item_class_is_in_the_bases_family()
    {
        var content = Shipped();
        var working = ItemsWorking();
        var weapons = content.ItemBases.Rows.Where(b => b.Kind == ItemKinds.Weapon).ToList();

        Assert.True(weapons.Count >= 30, $"{weapons.Count} weapons");
        Assert.All(content.ItemBases.Rows.Where(b => b.TFItem is not null), b =>
        {
            Assert.True(working.TryGetValue(b.TFItem!, out var row), $"{b.Id}: '{b.TFItem}' is not a row of items_working.tsv");
            Assert.True(row!.Status == "works", $"{b.Id}: '{b.TFItem}' is {row.Status}");
            if (b.Kind != ItemKinds.Weapon) return;
            Assert.True(content.TryGetFamilyOfItemClass(row.ItemClass, out var family), $"{b.Id}: {row.ItemClass} is in no family");
            Assert.Equal(b.Family, family.Id);
            Assert.Equal(row.Slot, b.Slot);
            Assert.All(b.Classes, c => Assert.Contains(c, row.Classes));
        });
    }

    [Fact]
    public void Every_weapon_names_a_tf_row_and_armour_names_none()
    {
        var bases = Shipped().ItemBases.Rows;

        Assert.All(bases.Where(b => b.Kind == ItemKinds.Weapon), b => Assert.NotNull(b.TFItem));
        Assert.All(bases.Where(b => b.Kind == ItemKinds.Armour), b => Assert.Null(b.TFItem));
    }

    [Fact]
    public void Every_family_has_a_base()
    {
        var content = Shipped();
        var used = content.ItemBases.Rows.Select(b => b.Family).ToHashSet();

        Assert.All(content.Families.Rows, f => Assert.True(used.Contains(f.Id), $"no base of family {f.Id}"));
    }

    [Fact]
    public void The_soldier_has_the_stock_and_three_unlock_launchers_shotguns_and_melee()
    {
        var soldier = Shipped().ItemBases.Rows.Where(b => b.AllowsClass("soldier") && b.Kind == ItemKinds.Weapon).ToList();

        Assert.True(soldier.Count(b => b.Family == "rocket_launcher") >= 4);
        Assert.Contains(soldier, b => b.TFItem == "TF_WEAPON_ROCKETLAUNCHER");
        Assert.True(soldier.Count(b => b.Family == "shotgun") >= 2);
        Assert.True(soldier.Count(b => b.Family == "melee") >= 3);
    }

    [Fact]
    public void Every_armour_slot_has_a_base_any_class_can_wear_at_level_1()
    {
        var armour = Shipped().ItemBases.Rows.Where(b => b.Kind == ItemKinds.Armour).ToList();

        Assert.All(ItemTables.ArmourSlots, slot =>
            Assert.Contains(armour, b => b.Slot == slot && b.Classes.Count == 0 && b.Level == 1 && b.Requires.Count == 0));
    }

    [Fact]
    public void Every_tf_attribute_an_affix_names_is_an_attribute_class_of_tfs_item_schema()
    {
        var schema = ItemsGameText();
        var names = Regex.Matches(schema, "\"attribute_class\"\\s+\"([a-z_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var tf = Shipped().Affixes.Rows.Where(a => a.IsTF).Select(a => a.Stat).Distinct().ToList();

        Assert.True(names.Count > 300, $"{names.Count} attribute classes read");
        Assert.True(tf.Count >= 5, $"{tf.Count} TF attributes");
        Assert.All(tf, n => Assert.True(names.Contains(n), $"{n} is not an attribute_class"));
    }

    [Fact]
    public void Every_unusual_effect_names_a_particle_system_of_tfs_item_schema()
    {
        var schema = ItemsGameText();
        var systems = Regex.Matches(schema, "\"system\"\\s+\"([A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.NotEmpty(Shipped().UnusualEffects.Rows);
        Assert.All(Shipped().UnusualEffects.Rows, e => Assert.True(systems.Contains(e.Particle), $"{e.Id}: {e.Particle}"));
    }

    [Fact]
    public void Each_affix_kind_is_named_by_its_vocabulary()
    {
        var content = Shipped();

        Assert.All(content.Affixes.Rows.Where(a => a.IsTF), a => Assert.True(content.TFAttributes.Contains(a.Stat), a.Id));
        Assert.All(content.Affixes.Rows.Where(a => !a.IsTF), a => Assert.True(content.Stats.Contains(a.Stat), a.Id));
        Assert.Contains(content.Affixes.Rows, a => a.IsTF);
        Assert.Contains(content.Affixes.Rows, a => !a.IsTF);
    }

    //-------------------------------------------------------------------------
    // One fact per validation rule
    //-------------------------------------------------------------------------

    [Fact]
    public void A_copy_of_the_shipped_item_tables_loads_clean_so_a_planted_error_is_the_only_one()
    {
        Assert.Empty(Errors(ShippedCopy()));
        Assert.Empty(Errors(Planted(ItemTables.Bases, _ => { })));
        Assert.Empty(Errors(Planted(ItemTables.Affixes, _ => { })));
    }

    private static ContentError Single(IContentSource source, string table, string row, ContentRule rule)
    {
        var e = Assert.Single(Errors(source));
        Assert.Equal((table, row, rule), (e.Table, e.Row, e.Rule));
        return e;
    }

    [Fact]
    public void A_base_of_an_unknown_kind_is_out_of_range()
    {
        Single(Planted(ItemTables.Bases, t => Row(t, "bat")["kind"] = "jewel"), ItemTables.Bases, "bat", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_weapon_with_no_tf_row_is_malformed()
    {
        var e = Single(Planted(ItemTables.Bases, t => Row(t, "shovel").Remove("tfItem")), ItemTables.Bases, "shovel", ContentRule.Malformed);
        Assert.Contains("tfItem", e.Message);
    }

    [Fact]
    public void A_weapon_in_a_slot_its_family_is_not_held_in_is_out_of_range()
    {
        var e = Single(Planted(ItemTables.Bases, t => Row(t, "black_box")["slot"] = "secondary"), ItemTables.Bases, "black_box", ContentRule.OutOfRange);
        Assert.Contains("rocket_launcher", e.Message);
    }

    [Fact]
    public void A_base_of_an_unknown_family_is_a_dangling_reference()
    {
        Single(Planted(ItemTables.Bases, t => Row(t, "shotgun")["family"] = "blunderbuss"), ItemTables.Bases, "shotgun", ContentRule.DanglingReference);
    }

    [Fact]
    public void A_requirement_on_an_unknown_attribute_is_a_dangling_reference()
    {
        Single(Planted(ItemTables.Bases, t => Row(t, "black_box")["requires"]![0]!["attribute"] = "charm"),
            ItemTables.Bases, "black_box", ContentRule.DanglingReference);
    }

    [Fact]
    public void An_implicit_that_lowers_an_attribute_is_out_of_range()
    {
        var e = Single(Planted(ItemTables.Bases, t =>
        {
            Row(t, "vest")["implicits"]![0]!["stat"] = "brawn";
            Row(t, "vest")["implicits"]![0]!["amount"] = -2;
        }), ItemTables.Bases, "vest", ContentRule.OutOfRange);
        Assert.Contains("brawn", e.Message);
        // the control: a raising implicit of the same attribute loads
        Assert.Empty(Errors(Planted(ItemTables.Bases, t => Row(t, "vest")["implicits"]![0]!["stat"] = "brawn")));
    }

    [Fact]
    public void An_affix_of_an_unknown_stat_is_an_unknown_stat()
    {
        Single(Planted(ItemTables.Affixes, t => Row(t, "hale")["stat"] = "max_lief"), ItemTables.Affixes, "hale", ContentRule.UnknownStat);
    }

    [Fact]
    public void A_tf_affix_naming_no_tf_attribute_is_a_dangling_reference()
    {
        Single(Planted(ItemTables.Affixes, t => Row(t, "rapid")["stat"] = "mult_fire_rate"), ItemTables.Affixes, "rapid", ContentRule.DanglingReference);
    }

    [Fact]
    public void An_affix_rolling_outside_its_stats_bounds_is_out_of_range()
    {
        Single(Planted(ItemTables.Affixes, t => Row(t, "heavy")["to"] = 9.0), ItemTables.Affixes, "heavy", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_cursed_affix_with_no_budget_is_out_of_range()
    {
        var e = Single(Planted(ItemTables.Affixes, t => Row(t, "loud").Remove("budget")), ItemTables.Affixes, "loud", ContentRule.OutOfRange);
        Assert.Contains("budget", e.Message);
    }

    [Fact]
    public void A_positive_affix_with_a_budget_is_out_of_range()
    {
        Single(Planted(ItemTables.Affixes, t => Row(t, "fleet")["budget"] = 0.1), ItemTables.Affixes, "fleet", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_cursed_affix_that_lowers_an_attribute_is_out_of_range()
    {
        var e = Single(Planted(ItemTables.Affixes, t =>
        {
            var row = Row(t, "frail");
            row["stat"] = "grit";
            row["from"] = -1;
            row["to"] = -5;
        }), ItemTables.Affixes, "frail", ContentRule.OutOfRange);
        Assert.Contains("grit", e.Message);
    }

    [Fact]
    public void A_positive_affix_whose_cursed_ceiling_leaves_its_stats_bounds_is_out_of_range()
    {
        // move_speed_pct allows up to 200: 190 is inside, 150 + 1.3 x 40 = 202 is not.
        var e = Single(Planted(ItemTables.Affixes, t =>
        {
            Row(t, "fleet")["from"] = 150;
            Row(t, "fleet")["to"] = 190;
        }), ItemTables.Affixes, "fleet", ContentRule.OutOfRange);
        Assert.Contains("cursed", e.Message);
    }

    [Fact]
    public void An_affix_in_an_unknown_slot_is_out_of_range()
    {
        Single(Planted(ItemTables.Affixes, t => Row(t, "sturdy")["slots"]!.AsArray().Add("cape")), ItemTables.Affixes, "sturdy", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_unique_with_a_cursed_affix_is_out_of_range()
    {
        Single(Planted(ItemTables.Uniques, t => Row(t, "gray_manns_spade")["affixes"]!.AsArray().Add(new JsonObject { ["affix"] = "loud", ["roll"] = 1 })),
            ItemTables.Uniques, "gray_manns_spade", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_unique_with_an_affix_its_base_cannot_roll_is_out_of_range()
    {
        Single(Planted(ItemTables.Uniques, t => Row(t, "gray_manns_spade")["affixes"]!.AsArray().Add(new JsonObject { ["affix"] = "fleet", ["roll"] = 1 })),
            ItemTables.Uniques, "gray_manns_spade", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_rarity_that_upgrades_into_one_no_recipe_may_output_is_out_of_range()
    {
        Single(Planted(ItemTables.Rarities, t => Row(t, "strange")["upgradeCost"] = 81), ItemTables.Rarities, "strange", ContentRule.OutOfRange);
    }

    [Fact]
    public void A_crate_holding_something_that_is_not_gear_is_a_dangling_reference()
    {
        Single(Planted(ItemTables.Bases, t => Row(t, "crate_shotgun")["contains"]!.AsArray().Add("scrap")),
            ItemTables.Bases, "crate_shotgun", ContentRule.DanglingReference);
    }

    [Fact]
    public void Changing_an_affix_range_changes_the_content_hash()
    {
        var before = new ContentLoader().Load(ShippedCopy());
        var after = new ContentLoader().Load(Planted(ItemTables.Affixes, t => Row(t, "heavy")["to"] = 1.13));

        Assert.Empty(after.Errors);
        Assert.NotEqual(before.Content.Hash, after.Content.Hash);
    }
}
