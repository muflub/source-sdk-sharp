using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// The Engineer's bench, salvage and the Spy (plan_descent_logic RU9; plan_tf2_rpg 2i, 2k; D22,
/// D24): a fact per recipe, each with its refusals; metal both ways; re-roll leaves the locked
/// affixes byte-identical; nothing crafts Unusual or Australium; salvage, craft, salvage loses.
/// </summary>
public sealed class CraftingTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static Crafting Bench => R.Crafting;
    private static ItemCodec Codec => R.ItemRules.Codec;

    private static ItemRollContext Hero(int level = 20) => Ctx(0, level, depth: 5, source: "test");

    private static RolledItem Make(string baseId, int count = 1, int tier = 0, ulong seed = 1, int level = 20) =>
        Codec.ToRolled(R.ItemRules.Roller.Roll(Ctx(tier, level, table: "base:" + baseId, source: "craft"), seed) with { Count = count });

    private static RolledItem Scrap(int n) => Make("scrap", n);
    private static RolledItem Refined(int n) => Make("refined", n);

    private static int Units(IEnumerable<RolledItem> items) => items.Sum(i => Bench.MetalUnits(Codec.Instance(i)));

    /// <summary>An identified gear item with at least two positive affixes and a curse, from a drop.</summary>
    private static RolledItem Cursed()
    {
        for (ulong s = 0; s < 20_000; s++)
        {
            var i = R.ItemRules.Roller.Roll(Ctx(2, 25, depth: 15), s);
            if (i.Affixes.Count(a => !R.Content.Affixes[a.Id].IsCursed) >= 2 && i.Affixes.Any(a => R.Content.Affixes[a.Id].IsCursed))
                return Codec.ToRolled(i with { Identified = true });
        }
        throw new InvalidOperationException("no cursed Strange in 20,000 seeds");
    }

    //-------------------------------------------------------------------------
    // Identify (2i)
    //-------------------------------------------------------------------------

    [Fact]
    public void The_spys_fee_rises_with_item_level()
    {
        for (int level = 1; level < 30; level++)
            Assert.True(R.Pricing.IdentifyFee(Codec.Instance(Make("shovel", tier: 2, level: level + 1)))
                        > R.Pricing.IdentifyFee(Codec.Instance(Make("shovel", tier: 2, level: level))));
    }

    [Fact]
    public void The_spy_identifies_for_his_fee_and_refuses_an_identified_item()
    {
        var item = R.RollItem(Ctx(2, 12), 4);

        var change = R.Identify(item, 1);

        Assert.True(change.Ok);
        Assert.Equal(R.Pricing.IdentifyFee(Codec.Instance(item)), change.Cost);
        Assert.True(change.Produced[0].Identified);
        Assert.Equal(CraftRefusals.AlreadyIdentified, R.Identify(change.Produced[0], 1).Refusal);
    }

    [Fact]
    public void Identify_is_seed_stable_and_never_rolls_again()
    {
        var item = R.RollItem(Ctx(3, 25), 6);

        Assert.Equal(R.Identify(item, 1).Produced[0].Instance, R.Identify(item, 99).Produced[0].Instance);
        Assert.Equal(Codec.Instance(item).Affixes.ToArray(), Codec.Instance(R.Identify(item, 5).Produced[0]).Affixes.ToArray());
    }

    [Fact]
    public void A_dossier_identifies_in_the_field_and_is_consumed_with_the_rest_of_its_stack_returned()
    {
        var item = R.RollItem(Ctx(2, 12), 4);

        var change = R.Craft("identify", [Make("dossier", 3), item], Hero(), 1);

        Assert.True(change.Ok);
        Assert.Equal([0, 1], change.Consumed);
        Assert.True(change.Produced[0].Identified);
        Assert.Equal(0, change.Cost);
        Assert.Contains(change.Produced, p => p.BaseType == "dossier" && p.Count == 2);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("identify", [item], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.AlreadyIdentified, R.Craft("identify", [Make("dossier"), change.Produced[0]], Hero(), 1).Refusal);
    }

    //-------------------------------------------------------------------------
    // Metal (D24)
    //-------------------------------------------------------------------------

    [Fact]
    public void Three_of_a_rung_make_one_of_the_next()
    {
        var change = R.Craft("metal_up", [Scrap(5)], Hero(), 1);

        Assert.True(change.Ok);
        Assert.Contains(change.Produced, p => p.BaseType == "reclaimed" && p.Count == 1);
        Assert.Contains(change.Produced, p => p.BaseType == "scrap" && p.Count == 2);
        Assert.Equal(5, Units(change.Produced));
    }

    [Fact]
    public void One_of_a_rung_makes_three_of_the_one_below()
    {
        var change = R.Craft("metal_down", [Refined(2)], Hero(), 1);

        Assert.True(change.Ok);
        Assert.Contains(change.Produced, p => p.BaseType == "reclaimed" && p.Count == 3);
        Assert.Contains(change.Produced, p => p.BaseType == "refined" && p.Count == 1);
        Assert.Equal(18, Units(change.Produced));
    }

    [Fact]
    public void Metal_conversion_round_trips_losslessly()
    {
        var up = R.Craft("metal_up", [Scrap(3)], Hero(), 1).Produced.Single();
        var down = R.Craft("metal_down", [up], Hero(), 2).Produced.Single();

        Assert.Equal(("scrap", 3), (down.BaseType, down.Count));
        var refined = R.Craft("metal_up", [R.Craft("metal_up", [Make("reclaimed", 3)], Hero(), 3).Produced.Single()], Hero(), 4);
        Assert.Equal(CraftRefusals.WrongInputs, refined.Refusal);   // refined is the top rung
    }

    [Fact]
    public void Metal_conversion_refuses_too_little_or_the_wrong_thing()
    {
        Assert.Equal(CraftRefusals.NotEnoughMetal, R.Craft("metal_up", [Scrap(2)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("metal_down", [Scrap(5)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("metal_up", [Make("shovel")], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("metal_up", [Scrap(3), Scrap(3)], Hero(), 1).Refusal);
    }

    [Fact]
    public void Metal_stacks_to_100()
    {
        Assert.All(R.Content.EconomySettings.MetalLadder, m => Assert.Equal(100, R.Content.ItemBases[m].Stack));
        var stacks = Bench.Metal(250, Hero(), 1, "craft");
        Assert.Equal([("refined", 27), ("reclaimed", 2), ("scrap", 1)], stacks.Select(s => (s.BaseId, s.Count)));
        Assert.Equal([("refined", 100), ("refined", 50)], Bench.Metal(9 * 150, Hero(), 1, "craft").Select(s => (s.BaseId, s.Count)));
    }

    //-------------------------------------------------------------------------
    // Salvage
    //-------------------------------------------------------------------------

    [Fact]
    public void Salvage_yields_more_metal_for_a_rarer_or_deeper_item()
    {
        int Yield(int tier, int level) => Bench.SalvageUnits(Codec.Instance(Make("shovel", tier: tier, level: level)) with { Rarity = tier });

        for (int tier = 0; tier < 4; tier++)
            Assert.True(Yield(tier + 1, 20) > Yield(tier, 20), $"tier {tier}");
        Assert.True(Yield(2, 30) > Yield(2, 1));
    }

    [Fact]
    public void Salvage_consumes_the_item_and_returns_its_metal()
    {
        var item = Make("shovel", tier: 2, level: 20);

        var change = R.Salvage(item, Hero(), 3);

        Assert.True(change.Ok);
        Assert.Equal([0], change.Consumed);
        Assert.Equal(Bench.SalvageUnits(Codec.Instance(item)), Units(change.Produced));
        Assert.All(change.Produced, p => Assert.True(R.Replays(p)));
    }

    [Fact]
    public void Only_gear_salvages()
    {
        Assert.Equal(CraftRefusals.NotSalvageable, R.Salvage(Scrap(3), Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.NotSalvageable, R.Salvage(Make("health_small"), Hero(), 1).Refusal);
    }

    //-------------------------------------------------------------------------
    // Recipes
    //-------------------------------------------------------------------------

    [Fact]
    public void Craft_item_makes_a_random_item_of_the_chosen_base_at_the_heros_level_and_returns_change()
    {
        var change = R.Craft("craft_item:black_box", [Refined(4)], Hero(17), 1);

        Assert.True(change.Ok);
        Assert.Equal([0], change.Consumed);
        var made = change.Produced[0];
        Assert.Equal(("black_box", 17), (made.BaseType, made.ItemLevel));
        Assert.True(made.Identified);
        Assert.True(R.Replays(made));
        Assert.Equal(36 - R.Content.EconomySettings.CraftItemCost, Units(change.Produced.Skip(1)));
    }

    [Fact]
    public void Craft_item_refuses_too_little_metal_a_base_that_is_not_gear_and_another_class()
    {
        Assert.Equal(CraftRefusals.NotEnoughMetal, R.Craft("craft_item:black_box", [Scrap(5)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("craft_item:scrap", [Refined(4)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongClass, R.Craft("craft_item:minigun", [Refined(4)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("craft_item:black_box", [Refined(4), Make("shovel")], Hero(), 1).Refusal);
    }

    [Fact]
    public void No_recipe_outputs_unusual_or_australium()
    {
        int unusual = R.ItemRules.Roller.UnusualRarity();
        for (ulong seed = 0; seed < 3000; seed++)
        {
            var made = R.Craft("craft_item:rocket_launcher", [Refined(4)], Hero(30), seed).Produced[0];
            Assert.True(made.Rarity < unusual, $"seed {seed}: rarity {made.Rarity}");
        }
        var strange = Codec.ToRolled(Codec.Instance(Make("rocket_launcher", tier: 2)) with { Identified = true });
        Assert.Equal(CraftRefusals.CannotUpgrade, R.Craft("upgrade", [strange, Refined(10)], Hero(), 1).Refusal);
        Assert.All(R.Content.EconomySettings.CraftOdds, o => Assert.True(R.Content.Rarities[o.Rarity].Craftable));
    }

    [Fact]
    public void Upgrade_raises_stock_to_vintage_and_vintage_to_strange_filling_the_affix_count()
    {
        var stock = Make("shovel", tier: 0);

        var vintage = R.Craft("upgrade", [stock, Refined(1)], Hero(), 1);
        var strange = R.Craft("upgrade", [vintage.Produced[0], Refined(3)], Hero(), 2);

        Assert.True(vintage.Ok && strange.Ok);
        var v = Codec.Instance(vintage.Produced[0]);
        var s = Codec.Instance(strange.Produced[0]);
        Assert.Equal((1, 2), (v.Rarity, s.Rarity));
        Assert.InRange(v.Affixes.Length, 1, 2);
        Assert.InRange(s.Affixes.Length, 3, 4);
        Assert.Equal(v.Affixes.ToArray(), s.Affixes.Take(v.Affixes.Length).ToArray());   // what it had stays
        Assert.True(R.Replays(strange.Produced[0]));
    }

    [Fact]
    public void Upgrade_refuses_an_unidentified_item_and_too_little_metal()
    {
        var hidden = R.RollItem(Ctx(1, 20), 3);

        Assert.False(hidden.Identified);
        Assert.Equal(CraftRefusals.NotIdentified, R.Craft("upgrade", [hidden, Refined(10)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.NotEnoughMetal, R.Craft("upgrade", [Make("shovel"), Scrap(8)], Hero(), 1).Refusal);
    }

    [Fact]
    public void Reroll_changes_one_affix_and_leaves_every_other_byte_identical()
    {
        var item = Cursed();
        var before = Codec.Instance(item);

        for (int index = 0; index < before.Affixes.Length; index++)
        {
            var change = R.Craft("reroll:" + index, [item, Refined(10)], Hero(), (ulong)index + 40);
            var after = Codec.Instance(change.Produced[0]);

            Assert.True(change.Ok);
            Assert.Equal(before.Affixes.Length, after.Affixes.Length);
            for (int other = 0; other < before.Affixes.Length; other++)
                if (other != index)
                    Assert.Equal(before.Affixes[other], after.Affixes[other]);
            Assert.Equal(before.Affixes[index].Id, after.Affixes[index].Id);
            Assert.Equal(1, after.RerollCount);
            Assert.True(R.Replays(change.Produced[0]));
        }
    }

    [Fact]
    public void Reroll_costs_rise_each_time_on_an_item_and_a_cursed_stat_costs_triple()
    {
        var item = Codec.Instance(Cursed());
        int positive = item.Affixes.ToList().FindIndex(a => !R.Content.Affixes[a.Id].IsCursed);
        int cursed = item.Affixes.ToList().FindIndex(a => R.Content.Affixes[a.Id].IsCursed);

        Assert.Equal(3 * Bench.RerollCost(item, positive), Bench.RerollCost(item, cursed));
        var once = R.ItemRules.Roller.Reroll(item, positive, 1);
        Assert.True(Bench.RerollCost(once, positive) > Bench.RerollCost(item, positive));
        Assert.True(Bench.RerollCost(R.ItemRules.Roller.Reroll(once, positive, 2), positive) > Bench.RerollCost(once, positive));
    }

    [Fact]
    public void Reroll_refuses_an_unidentified_item_a_missing_affix_and_too_little_metal()
    {
        var item = Cursed();

        Assert.Equal(CraftRefusals.NotIdentified, R.Craft("reroll:0", [item with { Identified = false, Instance = Codec.ToRolled(Codec.Instance(item) with { Identified = false }).Instance }, Refined(10)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.NoSuchAffix, R.Craft("reroll:9", [item, Refined(10)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.NotEnoughMetal, R.Craft("reroll:0", [item], Hero(), 1).Refusal);
    }

    [Fact]
    public void Three_potions_make_the_next_size_and_the_largest_does_not_combine()
    {
        var small = new[] { Make("health_small", seed: 1), Make("health_small", seed: 2), Make("health_small", seed: 3) };

        var change = R.Craft("combine_potions", small, Hero(), 1);

        Assert.True(change.Ok);
        Assert.Equal([0, 1, 2], change.Consumed);
        Assert.Equal("health_medium", Assert.Single(change.Produced).BaseType);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("combine_potions", [.. small.Take(2)], Hero(), 1).Refusal);
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("combine_potions", [small[0], small[1], Make("mana_small")], Hero(), 1).Refusal);
        var large = Make("health_large");
        Assert.Equal(CraftRefusals.WrongInputs, R.Craft("combine_potions", [large, large, large], Hero(), 1).Refusal);
    }

    [Fact]
    public void A_repair_kit_is_crafted_from_metal()
    {
        var change = R.Craft("repair_kit", [Scrap(8)], Hero(), 1);

        Assert.True(change.Ok);
        Assert.Equal("repair_kit", change.Produced[0].BaseType);
        Assert.Equal(8 - R.Content.EconomySettings.RepairKitCost, Units(change.Produced.Skip(1)));
        Assert.Equal(CraftRefusals.NotEnoughMetal, R.Craft("repair_kit", [Scrap(2)], Hero(), 1).Refusal);
    }

    [Fact]
    public void An_unknown_recipe_is_refused_and_consumes_nothing()
    {
        var change = R.Craft("transmute", [Scrap(3)], Hero(), 1);

        Assert.Equal(CraftRefusals.UnknownRecipe, change.Refusal);
        Assert.Empty(change.Consumed);
    }

    [Fact]
    public void A_metal_rung_not_worth_three_of_the_one_below_is_a_load_error()
    {
        Assert.Empty(Errors(Planted(EconomyTable.TableName, _ => { })));

        var e = Assert.Single(Errors(Planted(ItemTables.Bases, t => Row(t, "refined")["metal"] = 10)));

        Assert.Equal((EconomyTable.TableName, Rules.Content.ContentRule.OutOfRange), (e.Table, e.Rule));
        Assert.Contains("refined", e.Message);
    }

    [Fact]
    public void Craft_odds_naming_a_rarity_no_recipe_may_output_are_a_load_error()
    {
        var e = Assert.Single(Errors(Planted(EconomyTable.TableName,
            t => t["craftItem"]!["odds"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["rarity"] = "unusual", ["weight"] = 1 }))));

        Assert.Equal((EconomyTable.TableName, Rules.Content.ContentRule.OutOfRange), (e.Table, e.Rule));
        Assert.Contains("unusual", e.Message);
    }

    [Fact]
    public void Salvage_then_craft_then_salvage_strictly_loses_metal()
    {
        int max = R.Content.ItemSettings.MaxItemLevel;
        int cost = R.Content.EconomySettings.CraftItemCost;
        var rarities = R.Content.Rarities.Rows;
        for (int rarity = 0; rarity < rarities.Count; rarity++)
            for (int level = 1; level <= max; level++)
            {
                var item = Codec.Instance(Make("shovel", level: level)) with { Rarity = rarity };
                int units = Bench.SalvageUnits(item);
                if (rarities[rarity].Craftable)
                    Assert.True(units < cost, $"a crafted {rarities[rarity].Id} at {level} salvages for {units}, the craft costs {cost}");
                if (rarities[rarity].UpgradeCost > 0)
                {
                    int up = Bench.SalvageUnits(item with { Rarity = rarity + 1 });
                    Assert.True(up - rarities[rarity].UpgradeCost < units, $"upgrading {rarities[rarity].Id} at {level} makes metal");
                }
            }
    }
}
