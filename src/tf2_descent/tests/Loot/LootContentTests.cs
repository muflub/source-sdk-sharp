using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// loot.json and experience.json load, and each of their validation rules turns a planted bad
/// value into a load error naming it (plan_descent_logic RU1's rule for every table), planted
/// in a copy of the shipped tables.
/// </summary>
public sealed class LootContentTests
{
    private static MemoryContentSource ShippedCopy() =>
        new(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());

    private static IReadOnlyList<ContentError> Errors(string table, Action<JsonNode> mutate)
    {
        var source = ShippedCopy();
        Assert.True(source.TryRead(table, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return new ContentLoader().Load(source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()))).Errors;
    }

    private static JsonObject Row(JsonNode array, string id) =>
        array.AsArray().Select(n => n!.AsObject()).Single(o => (string?)o["id"] == id);

    [Fact]
    public void The_shipped_loot_and_experience_tables_load()
    {
        var content = new ContentLoader().Load(ShippedCopy());

        Assert.Empty(content.Errors);
        Assert.Equal(["act1", "act2", "act3", "container"], content.Content.Loot.Tables.Select(t => t.Id));
        Assert.Equal(3, content.Content.Loot.Bosses.Count);
        Assert.Equal(5, content.Content.Experience.LevelDifference.Count);
    }

    [Fact]
    public void A_depth_naming_a_loot_table_that_does_not_exist_is_a_load_error()
    {
        var e = Assert.Single(Errors(ContentTables.Levels, n => Row(n, "depth7")["lootTable"] = "act9"));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal("loot", e.Table);
    }

    [Fact]
    public void A_potion_entry_naming_a_weapon_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["tables"]![0]!["potions"]![0]!["base"] = "shovel"));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }

    [Fact]
    public void A_tier_naming_a_rarity_that_does_not_exist_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["tables"]![1]!["tiers"]![2]!["rarity"] = "legendary"));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }

    [Fact]
    public void An_ammo_drop_of_a_pack_size_tf2_has_not_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["ammo"]![0]!["pack"] = "huge"));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Equal("ammo.runt", e.Row);
    }

    [Fact]
    public void An_ammo_drop_for_a_size_class_that_does_not_exist_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["ammo"]![1]!["size"] = "colossal"));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }

    [Fact]
    public void An_ammo_chance_over_a_hundred_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["ammo"]![2]!["chancePct"] = 150));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void A_boss_without_a_table_of_its_own_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["bosses"]!.AsArray().RemoveAt(2)));
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Equal("captain_punch", e.Row);
    }

    [Fact]
    public void A_boss_table_for_an_ordinary_robot_is_a_load_error()
    {
        var errors = Errors("loot", n => n["bosses"]![0]!["id"] = "soldier");

        Assert.Equal(ContentRule.OutOfRange, Assert.Single(errors, e => e.Row == "soldier").Rule);
        Assert.Equal(ContentRule.DanglingReference, Assert.Single(errors, e => e.Row == "sir_nukesalot").Rule);   // and the boss lost its table
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void An_australium_chance_over_100_is_a_load_error()
    {
        var e = Assert.Single(Errors("loot", n => n["australium"]!["chancePct"] = 101));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }

    [Fact]
    public void An_experience_curve_that_pays_less_for_a_deeper_monster_is_a_load_error()
    {
        var e = Assert.Single(Errors("experience", n => n["levelDifference"]![4]!["pct"] = 120));
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
    }
}
