using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// Repair (plan_descent_logic RU8; plan_tf2_rpg 2h; ruling L7 as amended): the Engineer's
/// lossless, priced repair, and the Repair Kit's Diablo 1 field repair through Craft, which
/// never destroys an item.
/// </summary>
public sealed class RepairTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static Repairs Repairs => R.Repairs;

    private static ItemInstance Worn(string baseId = "rocket_launcher", int durability = 10, int rarity = 1, int itemLevel = 20)
    {
        int max = R.Content.ItemBases[baseId].Durability;
        return Item(baseId, rarity, itemLevel) with { Durability = durability, MaxDurability = max };
    }

    private static RolledItem Kit(int count = 1) =>
        R.ItemRules.Codec.ToRolled(R.ItemRules.Roller.Roll(Ctx(0, 1, table: "base:repair_kit", source: "craft"), 5) with { Count = count });

    private static RolledItem Rolled(ItemInstance item) => R.ItemRules.Codec.ToRolled(item);

    //-------------------------------------------------------------------------
    // The Engineer
    //-------------------------------------------------------------------------

    [Fact]
    public void The_engineer_restores_durability_to_its_maximum_and_never_lowers_the_maximum()
    {
        for (int d = 0; d < 60; d++)
        {
            var item = Worn(durability: d);
            var change = R.Repair(Rolled(item));
            var after = R.ItemRules.Codec.Instance(change.Produced[0]);

            Assert.True(change.Ok);
            Assert.Equal((60, 60), (after.Durability, after.MaxDurability));
        }
    }

    [Fact]
    public void The_engineer_restores_but_never_raises_a_maximum_a_field_repair_lowered()
    {
        var item = Worn() with { MaxDurability = 41 };

        var after = R.ItemRules.Codec.Instance(R.Repair(Rolled(item)).Produced[0]);

        Assert.Equal((41, 41), (after.Durability, after.MaxDurability));
    }

    [Fact]
    public void The_engineers_price_rises_with_item_level()
    {
        for (int level = 1; level < 30; level++)
            Assert.True(Repairs.Price(Worn(itemLevel: level + 1)) > Repairs.Price(Worn(itemLevel: level)), $"level {level}");
    }

    [Fact]
    public void The_engineers_price_rises_with_rarity()
    {
        for (int rarity = 0; rarity < 4; rarity++)
            Assert.True(Repairs.Price(Worn(rarity: rarity + 1)) > Repairs.Price(Worn(rarity: rarity)), $"rarity {rarity}");
    }

    [Fact]
    public void The_engineers_price_rises_with_the_points_missing()
    {
        for (int d = 59; d > 0; d--)
            Assert.True(Repairs.Price(Worn(durability: d - 1)) > Repairs.Price(Worn(durability: d)), $"durability {d}");
        Assert.True(Repairs.Price(Worn(durability: 59)) > 0);
        Assert.Equal(0, Repairs.Price(Worn(durability: 60)));
    }

    [Fact]
    public void The_change_carries_the_price()
    {
        var item = Worn(durability: 7);

        Assert.Equal(Repairs.Price(item), R.Repair(Rolled(item)).Cost);
    }

    [Fact]
    public void Repair_all_is_the_sum_of_each_price()
    {
        var items = new[] { Worn(durability: 1), Worn("shovel", 30), Worn("vest", 50, rarity: 2), Worn(durability: 60) };

        Assert.Equal(items.Sum(Repairs.Price), Repairs.PriceAll(items));
        Assert.True(Repairs.PriceAll(items) > 0);
    }

    [Fact]
    public void The_engineer_refuses_an_item_at_its_maximum_and_an_unusable_one()
    {
        Assert.Equal(RepairRefusals.AtMaximum, R.Repair(Rolled(Worn(durability: 60))).Refusal);
        Assert.Equal(RepairRefusals.Unusable, R.Repair(Rolled(Worn(durability: 0) with { MaxDurability = 0 })).Refusal);
    }

    //-------------------------------------------------------------------------
    // The Repair Kit (Diablo 1's field repair)
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_field_repair_step_matches_diablo_1s_formula_at_levels_1_15_and_30()
    {
        foreach (int level in new[] { 1, 15, 30 })
            for (ulong seed = 0; seed < 200; seed++)
            {
                var item = Worn(durability: 3);
                var result = Repairs.Field(item, level, seed);
                int max = item.MaxDurability, restored = 0;

                Assert.True(result.Ok);
                Assert.NotEmpty(result.Steps);
                foreach (var step in result.Steps)
                {
                    Assert.InRange(step.Gained, level, 2 * level - 1);                  // level + rand(level)
                    Assert.Equal(Math.Max(max / (level + 9), 1), step.MaxLost);           // max(maxDur / (level + 9), 1)
                    max -= step.MaxLost;
                    restored += step.Gained;
                    Assert.Equal(max, step.MaxDurability);
                }
                Assert.True(restored + item.Durability >= max || max == 0);              // it stops when current reaches maximum
                Assert.Equal(Math.Min(item.Durability + restored, max), result.Item!.Durability);
                Assert.Equal(max, result.Item.MaxDurability);
            }
    }

    [Fact]
    public void A_field_repair_at_level_1_restores_exactly_one_point_a_step()
    {
        var result = Repairs.Field(Worn(durability: 50), 1, 3);

        // 50/60: +1 and 60 / 10 = 6 off the maximum (51 < 54), then +1 and 54 / 10 = 5 off (52 >= 49).
        Assert.Equal([(1, 6), (1, 5)], result.Steps.Select(s => (s.Gained, s.MaxLost)));
        Assert.Equal((49, 49), (result.Item!.Durability, result.Item.MaxDurability));
    }

    [Fact]
    public void Field_repair_gains_spread_over_level_plus_rand_level_with_the_seed()
    {
        var gains = Enumerable.Range(0, 2000).SelectMany(s => Repairs.Field(Worn(durability: 0), 15, (ulong)s).Steps.Select(x => x.Gained)).ToList();

        Assert.Equal(15, gains.Min());
        Assert.Equal(29, gains.Max());
        Assert.InRange(gains.Average(), 21.5, 22.5);   // 15 + 7 on average
    }

    [Fact]
    public void A_field_repair_is_seed_stable()
    {
        var steps = Repairs.Field(Worn(), 15, 9).Steps;

        Assert.NotEmpty(steps);
        Assert.Equal(steps, Repairs.Field(Worn(), 15, 9).Steps);
    }

    [Fact]
    public void A_kit_through_craft_consumes_the_kit_and_returns_the_repaired_item()
    {
        var change = R.Craft("field_repair", [Kit(), Rolled(Worn(durability: 5))], Ctx(0, 15), 4);

        Assert.True(change.Ok);
        Assert.Equal([0, 1], change.Consumed);
        var after = R.ItemRules.Codec.Instance(Assert.Single(change.Produced));
        Assert.Equal("rocket_launcher", after.BaseId);
        Assert.True(after.Durability > 5);
        Assert.True(after.MaxDurability < 60);
    }

    [Fact]
    public void A_kit_on_an_item_at_its_maximum_is_refused_and_not_consumed()
    {
        var change = R.Craft("field_repair", [Kit(), Rolled(Worn(durability: 60))], Ctx(0, 15), 4);

        Assert.Equal(RepairRefusals.AtMaximum, change.Refusal);
        Assert.Empty(change.Consumed);
        Assert.Empty(change.Produced);
    }

    [Fact]
    public void A_kit_from_a_stack_leaves_the_rest_of_the_stack()
    {
        var change = R.Craft("field_repair", [Rolled(Worn(durability: 5)), Kit(3)], Ctx(0, 15), 4);

        Assert.True(change.Ok);
        Assert.Contains(change.Produced, p => p.BaseType == "repair_kit" && p.Count == 2);
    }

    [Fact]
    public void Field_repair_needs_one_kit_and_one_item()
    {
        Assert.Equal(RepairRefusals.NeedsKitAndItem, R.Craft("field_repair", [Rolled(Worn())], Ctx(0, 15), 1).Refusal);
        Assert.Equal(RepairRefusals.NeedsKitAndItem, R.Craft("field_repair", [Rolled(Worn()), Rolled(Worn())], Ctx(0, 15), 1).Refusal);
        Assert.Equal(RepairRefusals.NeedsKitAndItem, R.Craft("field_repair", [Kit(), Kit()], Ctx(0, 15), 1).Refusal);
    }

    [Fact]
    public void A_field_repair_that_takes_the_maximum_to_0_leaves_an_unusable_item_that_is_still_there()
    {
        // At level 1 a step loses at least 1 of the maximum: a 0/1 item loses its last point.
        var item = R.ItemRules.Roller.Roll(Ctx(1, table: "base:shovel"), 8) with { Durability = 0, MaxDurability = 1 };
        Assert.True(R.Replays(Rolled(item)));

        var change = R.Craft("field_repair", [Kit(), Rolled(item)], Ctx(0, 1), 1);

        Assert.True(change.Ok);
        var after = R.ItemRules.Codec.Instance(change.Produced[0]);
        Assert.Equal(("shovel", 0, 0), (after.BaseId, after.Durability, after.MaxDurability));
        Assert.True(R.ItemRules.Equipment.IsUnusable(after));
        Assert.True(R.Replays(change.Produced[0]));
    }

    [Fact]
    public void An_unusable_item_grants_nothing_and_nothing_restores_its_maximum()
    {
        var item = Worn("vest", durability: 0) with { MaxDurability = 0, Affixes = [new RolledAffix("robust", 10000)] };
        var sheet = Soldier(R);
        var result = R.ItemRules.Equipment.Resolve(sheet, [new EquippedItem("body", item)]);

        Assert.Equal(InactiveReason.Unusable, result.State("body").Reason);
        Assert.Equal(R.Heroes.Stats(sheet).MaxLife, R.Heroes.Stats(sheet, result.Contributions).MaxLife);
        Assert.Equal(RepairRefusals.Unusable, R.Repair(Rolled(item)).Refusal);
        Assert.Equal(RepairRefusals.Unusable, R.Craft("field_repair", [Kit(), Rolled(item)], Ctx(0, 30), 1).Refusal);
    }

    [Fact]
    public void An_unusable_item_can_still_be_salvaged_and_sold()
    {
        var item = Rolled(Worn("vest", durability: 0) with { MaxDurability = 0 });

        Assert.True(R.Salvage(item, Ctx(0, 10), 3).Ok);
        Assert.True(R.Price(item, PriceKind.Sell) > 0);
    }
}
