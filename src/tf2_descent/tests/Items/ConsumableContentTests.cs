using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// consumables.json (HE8): the belt's shared cooldown, what each potion restores, the scrolls
/// and the tomes. Each validation rule is proven by a bad row planted in a copy of the shipped
/// tables, against the shipped tables loading clean.
/// </summary>
public sealed class ConsumableContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static ContentError One(IContentSource source) => Assert.Single(Errors(source));

    private static MemoryContentSource Consumables(Action<JsonObject> mutate) =>
        Planted(ConsumableTable.TableName, n => mutate(n.AsObject()));

    [Fact]
    public void The_shipped_consumables_load_with_no_error()
    {
        Assert.Empty(Errors(ShippedCopy()));
    }

    [Fact]
    public void The_belt_shares_one_three_second_cooldown_scaled_by_the_field_rations_stats_and_refills()
    {
        var s = Rules.Content.Consumables;

        Assert.Equal(3, s.BeltCooldown);
        Assert.Equal("potion_restore_pct", s.RestoreStat);
        Assert.Equal("potion_cooldown_pct", s.CooldownStat);
        Assert.True(s.RefuseAtFull);
        Assert.True(s.Refill);
    }

    [Fact]
    public void Sandviches_restore_life_and_bonk_restores_mana()
    {
        var s = Rules.Content.Consumables;

        Assert.All(["health_small", "health_medium", "health_large"], b => Assert.Equal(PotionResources.Life, s.Potion(b)!.Restores));
        Assert.All(["mana_small", "mana_medium", "mana_large"], b => Assert.Equal(PotionResources.Mana, s.Potion(b)!.Restores));
    }

    [Fact]
    public void A_potion_stacks_four_to_a_belt_slot()
    {
        Assert.All(Rules.Content.ItemBases.Rows.Where(b => b.Kind == ItemKinds.Potion), b => Assert.Equal(4, b.Stack));
    }

    [Fact]
    public void Every_spell_has_a_tome_and_the_dossier_and_blueprint_are_the_scrolls()
    {
        var s = Rules.Content.Consumables;

        Assert.Equal(Rules.Content.Spells.Rows.Select(r => r.Id).Order(), s.Tomes.Select(t => t.Spell).Order());
        Assert.Equal(ScrollUses.Identify, s.Scroll("dossier")!.Use);
        Assert.Equal(ScrollUses.TownPortal, s.Scroll("town_portal")!.Use);
        Assert.Equal(10, Rules.Content.ItemBases["town_portal"].Stack);
    }

    [Fact]
    public void A_potion_base_with_no_row_is_a_dangling_reference()
    {
        var error = One(Consumables(n => n["potions"]!.AsArray().RemoveAt(0)));

        Assert.Equal(ContentRule.DanglingReference, error.Rule);
        Assert.Equal("health_small", error.Row);
    }

    [Fact]
    public void A_potion_row_on_a_base_that_is_no_potion_is_out_of_range()
    {
        var error = One(Consumables(n => n["potions"]!.AsArray().Add(new JsonObject { ["base"] = "shovel", ["restores"] = "life" })));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Equal("shovel", error.Row);
    }

    [Fact]
    public void A_potion_that_restores_an_unknown_resource_is_out_of_range()
    {
        var error = One(Consumables(n => n["potions"]![0]!["restores"] = "rage"));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
    }

    [Fact]
    public void A_tome_of_no_spell_is_a_dangling_reference()
    {
        var error = One(Consumables(n => n["tomes"]![0]!["spell"] = "meteor"));

        Assert.Equal(ContentRule.DanglingReference, error.Rule);
        Assert.Equal("tome_fireball", error.Row);
    }

    [Fact]
    public void An_identify_scroll_that_is_not_the_dossier_is_out_of_range()
    {
        var error = One(Consumables(n => n["scrolls"]![1]!["use"] = "identify"));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
        Assert.Equal("town_portal", error.Row);
    }

    [Fact]
    public void A_base_named_twice_is_a_duplicate()
    {
        var error = One(Consumables(n => n["tomes"]!.AsArray().Add(new JsonObject { ["base"] = "dossier", ["spell"] = "fireball" })));

        Assert.Equal(ContentRule.DuplicateId, error.Rule);
    }

    [Fact]
    public void A_belt_cooldown_beyond_ten_minutes_is_out_of_range()
    {
        var error = One(Consumables(n => n["belt"]!["cooldown"] = 601));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
    }
}
