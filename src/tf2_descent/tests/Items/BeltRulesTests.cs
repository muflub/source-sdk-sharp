using System.Collections.Immutable;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The belt's rules (HE8): a drink's restore and cooldown by the potion's row and the Field
/// Rations stats, its refusals, and what leaves the belt (one from the stack, the backpack's
/// refill); a scroll or tome consumed one at a time; potions merged onto the belt.
/// </summary>
public sealed class BeltRulesTests
{
    private static readonly TF2DescentRules Rules = new();

    private static BeltRules Belt => Rules.ItemRules.Belt;

    private static HeroStats Stats(double restorePct = 0, double cooldownPct = 0)
    {
        var source = new ContributionSource(SourceKind.Skill, "field_rations");
        return Rules.Heroes.Stats(Rules.Heroes.New("soldier", "t"),
            [new Contribution("potion_restore_pct", restorePct, source), new Contribution("potion_cooldown_pct", cooldownPct, source)]);
    }

    private static ItemInstance Potion(string baseId, int count = 1) => Item(baseId, 0, 1) with { Count = count, Durability = 0, MaxDurability = 0 };

    private static Held H(long id, string baseId, int count = 1) => new(id, Potion(baseId, count));

    //-------------------------------------------------------------------------
    // The drink's numbers
    //-------------------------------------------------------------------------

    [Fact]
    public void A_small_sandvich_restores_a_quarter_of_max_life_and_starts_the_three_second_cooldown()
    {
        var r = Belt.Drink(Potion("health_small"), Stats(), life: 100, lifeMax: 200, mana: 0, manaMax: 100, cooldownLeft: 0);

        Assert.True(r.Ok);
        Assert.Equal(PotionResources.Life, r.Restores);
        Assert.Equal(50, r.Amount, 9);
        Assert.Equal(3, r.CooldownSeconds, 9);
    }

    [Fact]
    public void A_bonk_can_restores_half_of_max_mana()
    {
        var r = Belt.Drink(Potion("mana_medium"), Stats(), life: 200, lifeMax: 200, mana: 10, manaMax: 80, cooldownLeft: 0);

        Assert.Equal(PotionResources.Mana, r.Restores);
        Assert.Equal(40, r.Amount, 9);
    }

    [Fact]
    public void Field_rations_at_five_ranks_restores_forty_percent_more_and_shortens_the_cooldown_by_thirty()
    {
        var r = Belt.Drink(Potion("health_small"), Stats(restorePct: 40, cooldownPct: -30), 100, 200, 0, 100, 0);

        Assert.Equal(70, r.Amount, 9);
        Assert.Equal(2.1, r.CooldownSeconds, 9);
    }

    [Fact]
    public void A_drink_while_the_belts_cooldown_runs_is_refused()
    {
        var r = Belt.Drink(Potion("health_small"), Stats(), 100, 200, 0, 100, cooldownLeft: 0.5);

        Assert.Equal(DrinkRefusal.OnCooldown, r.Refusal);
        Assert.Equal(0, r.Amount);
    }

    [Fact]
    public void A_drink_at_full_life_is_refused()
    {
        var r = Belt.Drink(Potion("health_small"), Stats(), life: 200, lifeMax: 200, mana: 0, manaMax: 100, cooldownLeft: 0);

        Assert.Equal(DrinkRefusal.Full, r.Refusal);
    }

    [Fact]
    public void A_mana_potion_at_full_life_but_short_of_mana_is_drunk()
    {
        Assert.True(Belt.Drink(Potion("mana_small"), Stats(), 200, 200, 50, 100, 0).Ok);
    }

    [Fact]
    public void Anything_that_is_no_potion_is_not_drunk()
    {
        Assert.Equal(DrinkRefusal.NotAPotion, Belt.Drink(Item("shovel", 0, 1), Stats(), 1, 200, 0, 100, 0).Refusal);
    }

    //-------------------------------------------------------------------------
    // What leaves the belt
    //-------------------------------------------------------------------------

    [Fact]
    public void A_drink_takes_one_from_the_slots_stack_and_keeps_its_id()
    {
        var hero = new HeroInventory { Belt = new Dictionary<int, Held> { [0] = H(7, "health_small", 3) }.ToImmutableSortedDictionary() };

        var r = Rules.Inventory.Drink(hero, new StashState(), 0);

        Assert.True(r.Ok);
        Assert.Empty(r.Left);
        Assert.Equal((7L, 2), (r.Hero.Belt[0].Id, r.Hero.Belt[0].Item.Count));
    }

    [Fact]
    public void The_last_drink_of_a_slot_refills_it_with_the_same_potion_from_the_backpack()
    {
        var hero = new HeroInventory
        {
            Belt = new Dictionary<int, Held> { [1] = H(7, "health_small") }.ToImmutableSortedDictionary(),
            Backpack = new Dictionary<int, Held> { [2] = H(8, "mana_small"), [5] = H(9, "health_small", 2) }.ToImmutableSortedDictionary(),
        };

        var r = Rules.Inventory.Drink(hero, new StashState(), 1);

        Assert.Equal(7, Assert.Single(r.Left).Id);
        Assert.Equal(9, r.Hero.Belt[1].Id);
        Assert.Equal(2, r.Hero.Belt[1].Item.Count);
        Assert.Equal([2], r.Hero.Backpack.Keys);
    }

    [Fact]
    public void The_last_drink_with_no_same_potion_in_the_backpack_empties_the_slot()
    {
        var hero = new HeroInventory
        {
            Belt = new Dictionary<int, Held> { [1] = H(7, "health_small") }.ToImmutableSortedDictionary(),
            Backpack = new Dictionary<int, Held> { [2] = H(8, "health_medium") }.ToImmutableSortedDictionary(),
        };

        var r = Rules.Inventory.Drink(hero, new StashState(), 1);

        Assert.False(r.Hero.Belt.ContainsKey(1));
        Assert.Equal([2], r.Hero.Backpack.Keys);
    }

    [Fact]
    public void With_refill_off_the_last_drink_empties_the_slot()
    {
        var content = new ContentLoader().Load(Planted(ConsumableTable.TableName, n => n["belt"]!["refill"] = false)).OrThrow();
        var inventory = new InventoryRules(content, new ItemRules(content, new HeroRules(content)));
        var hero = new HeroInventory
        {
            Belt = new Dictionary<int, Held> { [1] = H(7, "health_small") }.ToImmutableSortedDictionary(),
            Backpack = new Dictionary<int, Held> { [2] = H(8, "health_small") }.ToImmutableSortedDictionary(),
        };

        var r = inventory.Drink(hero, new StashState(), 1);

        Assert.False(r.Hero.Belt.ContainsKey(1));
        Assert.Equal([2], r.Hero.Backpack.Keys);
    }

    [Fact]
    public void A_consumed_scroll_leaves_one_fewer_and_the_last_one_leaves()
    {
        var hero = new HeroInventory { Backpack = new Dictionary<int, Held> { [0] = new(5, Potion("town_portal", 2)) }.ToImmutableSortedDictionary() };

        var once = Rules.Inventory.Consume(hero, new StashState(), 0);
        var twice = Rules.Inventory.Consume(once.Hero, new StashState(), 0);

        Assert.Equal(1, once.Hero.Backpack[0].Item.Count);
        Assert.Empty(once.Left);
        Assert.Empty(twice.Hero.Backpack);
        Assert.Equal(5, Assert.Single(twice.Left).Id);
    }

    [Fact]
    public void A_potion_put_on_a_slot_of_the_same_potion_merges_up_to_four_and_the_rest_stays()
    {
        var hero = new HeroInventory
        {
            Belt = new Dictionary<int, Held> { [0] = H(7, "health_small", 3) }.ToImmutableSortedDictionary(),
            Backpack = new Dictionary<int, Held> { [4] = H(8, "health_small", 2) }.ToImmutableSortedDictionary(),
        };

        var r = Rules.Inventory.ToBelt(hero, new StashState(), 4, 0);

        Assert.True(r.Ok);
        Assert.Equal(4, r.Hero.Belt[0].Item.Count);
        Assert.Equal(1, r.Hero.Backpack[4].Item.Count);
    }

    [Fact]
    public void A_potion_put_on_a_slot_of_another_potion_is_refused()
    {
        var hero = new HeroInventory
        {
            Belt = new Dictionary<int, Held> { [0] = H(7, "health_small") }.ToImmutableSortedDictionary(),
            Backpack = new Dictionary<int, Held> { [4] = H(8, "mana_small") }.ToImmutableSortedDictionary(),
        };

        Assert.Equal(InventoryRefusals.Occupied, Rules.Inventory.ToBelt(hero, new StashState(), 4, 0).Refusal);
    }

    [Fact]
    public void A_worn_item_changes_in_place_only_in_its_mutable_half()
    {
        var launcher = Item("rocket_launcher", 0, 1);
        var hero = new HeroInventory { Equipped = HeroInventoryOf("primary", new Held(3, launcher)) };

        var worn = Rules.Inventory.ReplaceEquipped(hero, new StashState(), "primary", launcher with { Durability = 10 });
        var forged = Rules.Inventory.ReplaceEquipped(hero, new StashState(), "primary", launcher with { Durability = 10, ItemLevel = 30 });

        Assert.Equal(10, worn.Hero.Equipped["primary"].Item.Durability);
        Assert.Equal(InventoryRefusals.Refused, forged.Refusal);
    }

    private static System.Collections.Immutable.ImmutableSortedDictionary<string, Held> HeroInventoryOf(string slot, Held held) =>
        new HeroInventory().Equipped.Add(slot, held);

    //-------------------------------------------------------------------------
    // Wear levels and the spell audit
    //-------------------------------------------------------------------------

    [Fact]
    public void A_launcher_at_a_quarter_of_its_maximum_is_low_and_one_point_above_is_not()
    {
        var d = Rules.ItemRules.Durability;
        var launcher = Item("rocket_launcher", 0, 1);

        Assert.Equal(WearLevel.Ok, d.Level(launcher with { Durability = 16 }));
        Assert.Equal(WearLevel.Low, d.Level(launcher with { Durability = 15 }));
        Assert.Equal(WearLevel.Broken, d.Level(launcher with { Durability = 0 }));
        Assert.Equal(WearLevel.Unusable, d.Level(launcher with { Durability = 0, MaxDurability = 0 }));
        Assert.Equal(WearLevel.None, d.Level(Potion("health_small")));
    }

    [Fact]
    public void A_warning_line_of_zero_percent_is_out_of_range()
    {
        var error = Assert.Single(Errors(Planted("durability", n => n["warning"]!["pct"] = 0)));

        Assert.Equal(ContentRule.OutOfRange, error.Rule);
    }

    [Fact]
    public void A_spell_read_past_its_cap_fails_the_sheets_audit()
    {
        var sheet = Rules.Heroes.New("soldier", "t");

        Assert.True(Rules.Heroes.Audit(sheet with { SpellRanks = sheet.SpellRanks.SetItem("fireball", 5) }).Ok);
        Assert.Equal("spell_rank", Rules.Heroes.Audit(sheet with { SpellRanks = sheet.SpellRanks.SetItem("fireball", 6) }).Rule);
        Assert.Equal("spell_unknown", Rules.Heroes.Audit(sheet with { SpellRanks = sheet.SpellRanks.SetItem("meteor", 1) }).Rule);
    }
}
