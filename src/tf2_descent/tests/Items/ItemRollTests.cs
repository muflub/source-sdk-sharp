using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The item roll (plan_descent_logic RU3; plan_tf2_rpg 2b; D5, R28): seed-stable, the rarity's
/// affix counts, the item-level gates, uniques, the unusual chance by power, and cursed stats
/// that buy budget. Distribution facts state their seed counts and tolerances.
/// </summary>
public sealed class ItemRollTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static ItemRoller Roller => R.ItemRules.Roller;

    private const int Stock = 0, Vintage = 1, Strange = 2, Unusual = 3, Australium = 4;

    private static IEnumerable<ItemInstance> Rolls(int count, int tier, int itemLevel = 20, int depth = 5, string source = "kill", ulong from = 0)
    {
        for (ulong s = from; s < from + (ulong)count; s++)
            yield return Roller.Roll(Ctx(tier, itemLevel, depth, source: source), s);
    }

    private static int Positives(ItemInstance i) => i.Affixes.Count(a => !R.Content.Affixes[a.Id].IsCursed);
    private static int Curses(ItemInstance i) => i.Affixes.Count(a => R.Content.Affixes[a.Id].IsCursed);

    //-------------------------------------------------------------------------
    // Seed-stable
    //-------------------------------------------------------------------------

    [Fact]
    public void The_same_context_and_seed_roll_byte_identical_items_in_two_rules_instances()
    {
        var other = NewRules();
        for (ulong seed = 0; seed < 500; seed++)
            for (int tier = 0; tier < 5; tier++)
                Assert.Equal(R.RollItem(Ctx(tier), seed).Instance, other.RollItem(Ctx(tier), seed).Instance);
    }

    [Fact]
    public void Two_seeds_roll_different_items()
    {
        var bytes = Enumerable.Range(0, 200).Select(s => Convert.ToHexString(R.RollItem(Ctx(Strange), (ulong)s).Instance)).ToHashSet();

        Assert.Equal(200, bytes.Count);
    }

    [Fact]
    public void Every_rolled_item_replays()
    {
        for (ulong seed = 0; seed < 300; seed++)
            for (int tier = 0; tier < 5; tier++)
                Assert.True(R.Replays(R.RollItem(Ctx(tier, depth: 12), seed)), $"tier {tier} seed {seed}");
    }

    [Fact]
    public void An_item_whose_affix_roll_was_changed_does_not_replay()
    {
        var item = R.RollItem(Ctx(Strange), 7);
        var instance = R.ItemRules.Codec.Instance(item);
        Assert.NotEmpty(instance.Affixes);
        var forged = instance with { Affixes = instance.Affixes.SetItem(0, instance.Affixes[0] with { Roll = (instance.Affixes[0].Roll + 1) % 10001 }) };

        Assert.True(R.Replays(item));
        Assert.False(R.Replays(R.ItemRules.Codec.ToRolled(forged)));
    }

    [Fact]
    public void An_item_whose_column_disagrees_with_its_bytes_does_not_replay()
    {
        var item = R.RollItem(Ctx(Vintage), 11);

        Assert.True(R.Replays(item));
        Assert.False(R.Replays(item with { ItemLevel = item.ItemLevel + 1 }));
        Assert.False(R.Replays(item with { BaseType = "shovel" == item.BaseType ? "bat" : "shovel" }));
    }

    //-------------------------------------------------------------------------
    // What a roll holds
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_rarity_rolls_its_count_of_positive_affixes()
    {
        foreach (int tier in new[] { Stock, Vintage, Strange, Unusual })
            foreach (var item in Rolls(500, tier))
            {
                var rr = R.Content.Rarities.Rows[item.Rarity];
                Assert.InRange(Positives(item), rr.MinAffixes, rr.MaxAffixes);
            }
    }

    [Fact]
    public void Vintage_and_above_drop_unidentified_and_stock_drops_identified()
    {
        Assert.All(Rolls(200, Stock), i => Assert.True(i.Identified));
        foreach (int tier in new[] { Vintage, Strange, Unusual, Australium })
            Assert.All(Rolls(200, tier), i => Assert.False(i.Identified));
    }

    [Fact]
    public void A_vendor_roll_arrives_identified()
    {
        Assert.All(Rolls(200, Strange, source: "vendor:saxton"), i => Assert.True(i.Identified));
    }

    [Fact]
    public void No_affix_rolls_above_its_item_level_or_on_a_slot_or_family_it_does_not_name()
    {
        foreach (int level in new[] { 1, 5, 12, 30 })
            foreach (var item in Rolls(400, Unusual, level))
            {
                var b = R.Content.ItemBases[item.BaseId];
                Assert.All(item.Affixes, a => Assert.True(R.Content.Affixes[a.Id].Fits(b, item.ItemLevel), $"{a.Id} on {b.Id} at {item.ItemLevel}"));
            }
    }

    [Fact]
    public void No_item_carries_two_affixes_of_one_stat()
    {
        foreach (var item in Rolls(2000, Unusual, 30, depth: 15))
        {
            var stats = item.Affixes.Select(a => R.Content.Affixes[a.Id]).Select(a => a.Kind + a.Stat).ToList();
            Assert.Equal(stats.Count, stats.Distinct().Count());
        }
    }

    [Fact]
    public void A_drop_is_a_base_its_class_may_use_at_or_below_its_item_level()
    {
        foreach (var item in Rolls(1000, Vintage, 8))
        {
            var b = R.Content.ItemBases[item.BaseId];
            Assert.True(b.AllowsClass("soldier"), b.Id);
            Assert.True(b.Level <= 8, b.Id);
            Assert.True(b.IsGear, b.Id);
        }
    }

    [Fact]
    public void A_loot_table_naming_a_base_rolls_that_base()
    {
        Assert.All(Enumerable.Range(0, 100), s =>
            Assert.Equal("black_box", Roller.Roll(Ctx(Strange, table: "base:black_box"), (ulong)s).BaseId));
    }

    [Fact]
    public void An_australium_roll_is_a_hand_authored_unique_with_its_fixed_affixes()
    {
        foreach (var item in Rolls(100, Australium, 25))
        {
            Assert.NotNull(item.Unique);
            var u = R.Content.Uniques[item.Unique!];
            Assert.Equal(u.Base, item.BaseId);
            Assert.Equal(u.Affixes.Select(a => (a.Affix, (int)Math.Round(a.Roll * 10000))), item.Affixes.Select(a => (a.Id, a.Roll)));
            Assert.Equal(u.Effect, item.UnusualEffect);
        }
    }

    [Fact]
    public void An_unusual_item_carries_an_effect_and_no_rolled_item_of_another_rarity_does()
    {
        foreach (int tier in new[] { Stock, Vintage, Strange, Unusual })
            foreach (var item in Rolls(500, tier, 30))
                Assert.Equal(item.Rarity == Unusual, item.UnusualEffect is not null);
    }

    //-------------------------------------------------------------------------
    // The unusual chance (2b): P = rarityBase x power^2
    //-------------------------------------------------------------------------

    [Fact]
    public void The_unusual_chance_rises_with_power()
    {
        for (int rarity = Vintage; rarity <= Strange; rarity++)
            for (double p = 0; p < 1; p += 0.05)
                Assert.True(Roller.UnusualChance(rarity, p + 0.05) > Roller.UnusualChance(rarity, p), $"rarity {rarity} at {p}");
    }

    [Fact]
    public void The_unusual_chance_rises_with_rarity_below_unusual()
    {
        for (double p = 0.05; p <= 1; p += 0.05)
        {
            Assert.True(Roller.UnusualChance(Vintage, p) > Roller.UnusualChance(Stock, p));
            Assert.True(Roller.UnusualChance(Strange, p) > Roller.UnusualChance(Vintage, p));
        }
    }

    [Fact]
    public void A_max_roll_item_becomes_unusual_more_often_than_a_min_roll_one_by_the_stated_ratio_over_100k_rolls()
    {
        // At the top item level, power is affixShare x (rolls) + (1 - affixShare): a max-roll item
        // has power 1, a min-roll one 1 - affixShare, so P differs by 1 / (1 - affixShare)^2.
        double share = R.Content.ItemSettings.AffixShare;
        double stated = 1 / ((1 - share) * (1 - share));
        int top = R.Content.ItemSettings.MaxItemLevel;
        var max = Item("rocket_launcher", Strange, top, new RolledAffix("heavy", 10000), new RolledAffix("rapid", 10000), new RolledAffix("of_reloading", 10000));
        var min = max with { Affixes = [.. max.Affixes.Select(a => a with { Roll = 0 })] };
        Assert.Equal(1, Roller.UnusualPower(max), 9);
        Assert.Equal(1 - share, Roller.UnusualPower(min), 9);

        int hitsMax = 0, hitsMin = 0;
        for (ulong s = 0; s < 100_000; s++)
        {
            if (Roller.PromotesToUnusual(Strange, Roller.UnusualPower(max), SeededRandom.For(s, "max"))) hitsMax++;
            if (Roller.PromotesToUnusual(Strange, Roller.UnusualPower(min), SeededRandom.For(s, "min"))) hitsMin++;
        }

        Assert.True(hitsMin > 500, $"{hitsMin} min-roll hits: too few to measure");
        Assert.InRange((double)hitsMax / hitsMin, stated * 0.9, stated * 1.1);   // ±10 %: about 3.5 standard errors
    }

    [Fact]
    public void Strange_drops_become_unusual_sometimes_and_vendor_and_crate_rolls_never_do()
    {
        int promoted = Rolls(20_000, Strange, 30).Count(i => i.Rarity == Unusual);

        Assert.InRange(promoted, 1, 20_000 * 0.08);   // at most rarityBase: power never exceeds 1
        Assert.DoesNotContain(Rolls(5000, Strange, 30, source: "vendor:saxton"), i => i.Rarity == Unusual);
        Assert.DoesNotContain(Rolls(5000, Strange, 30, source: "crate"), i => i.Rarity == Unusual);
    }

    //-------------------------------------------------------------------------
    // Cursed stats (R28)
    //-------------------------------------------------------------------------

    [Fact]
    public void An_item_can_carry_positive_and_cursed_stats_together()
    {
        var both = Rolls(5000, Strange, 20, depth: 15).Where(i => Curses(i) > 0 && Positives(i) > 0).ToList();

        Assert.NotEmpty(both);
        var view = R.ItemRules.Views.View(both[0] with { Identified = true }, ItemViewer.Owner);
        Assert.Contains(view.Affixes, a => a.Cursed);
        Assert.Contains(view.Affixes, a => !a.Cursed);
    }

    [Fact]
    public void Each_cursed_stat_raises_the_positive_ceiling_by_its_stated_budget()
    {
        var heavy = R.Content.Affixes["heavy"];
        var uncursed = Item("rocket_launcher", Strange, 20, new RolledAffix("heavy", 10000));
        var one = uncursed with { Affixes = uncursed.Affixes.Add(new RolledAffix("frail", 5000)) };
        var two = one with { Affixes = one.Affixes.Add(new RolledAffix("loud", 0)) };
        double b1 = R.Content.Affixes["frail"].Budget, b2 = R.Content.Affixes["loud"].Budget;

        Assert.Equal(1, Roller.Ceiling(uncursed), 9);
        Assert.Equal(1 + b1, Roller.Ceiling(one), 9);
        Assert.Equal(1 + b1 + b2, Roller.Ceiling(two), 9);
        Assert.Equal(1 + b1, Roller.Position(one, one.Affixes[0]), 9);          // a max roll sits at the ceiling
        Assert.Equal(1 + b1 + b2, Roller.Position(two, two.Affixes[0]), 9);
        Assert.Equal(heavy.To, Roller.Value(uncursed, uncursed.Affixes[0]), 6);
        Assert.Equal(1.13, Roller.Value(one, one.Affixes[0]), 6);               // 1.05 + 1.15 x 0.07, past the uncursed top 1.12
    }

    [Fact]
    public void An_uncursed_item_never_rolls_a_positive_past_the_top_of_its_range()
    {
        int seen = 0;
        foreach (var item in Rolls(20_000, Strange, 30, depth: 15).Where(i => Curses(i) == 0))
            foreach (var a in item.Affixes)
            {
                Assert.True(Roller.Position(item, a) <= 1, $"{a.Id} at {Roller.Position(item, a)}");
                seen++;
            }
        Assert.True(seen > 10_000, $"{seen} affixes seen");
    }

    [Fact]
    public void A_cursed_item_rolls_positives_past_the_uncursed_top_up_to_its_ceiling()
    {
        double highest = 0;
        foreach (var item in Rolls(20_000, Strange, 30, depth: 15).Where(i => Curses(i) > 0))
            foreach (var a in item.Affixes.Where(a => !R.Content.Affixes[a.Id].IsCursed))
            {
                double position = Roller.Position(item, a);
                Assert.True(position <= Roller.Ceiling(item) + 1e-9);
                highest = Math.Max(highest, position);
            }
        Assert.True(highest > 1.1, $"highest position {highest}");
    }

    [Fact]
    public void Net_power_stays_inside_the_band_for_its_rarity_and_level()
    {
        int seen = 0;
        foreach (int level in new[] { 5, 20, 30 })
            foreach (int tier in new[] { Vintage, Strange, Unusual })
                foreach (var item in Rolls(3000, tier, level, depth: 15))
                {
                    var (min, max) = Roller.NetPowerBand(item.Rarity, item.ItemLevel);
                    Assert.InRange(Roller.NetPower(item), min - 1e-9, max + 1e-9);
                    seen++;
                }
        Assert.Equal(27_000, seen);
    }

    [Fact]
    public void Cursed_and_uncursed_items_have_the_same_mean_net_power_so_a_curse_is_a_trade()
    {
        var items = Rolls(30_000, Strange, 25, depth: 15).Where(i => i.Rarity == Strange).ToList();
        var cursed = items.Where(i => Curses(i) > 0).Select(Roller.NetPower).ToList();
        var clean = items.Where(i => Curses(i) == 0).Select(Roller.NetPower).ToList();

        Assert.True(cursed.Count > 3000, $"{cursed.Count} cursed");
        Assert.InRange(cursed.Average() / clean.Average(), 0.9, 1.1);   // equal in expectation; ±10 % over these counts
        Assert.True(cursed.Max() > clean.Max());                         // best-in-slot lives on cursed items
        Assert.True(cursed.Min() < clean.Min());                         // and so does the worst
    }

    [Fact]
    public void The_chance_of_a_cursed_stat_rises_with_depth()
    {
        int Cursed(int depth) => Rolls(5000, Strange, 20, depth).Count(i => Curses(i) > 0);

        int shallow = Cursed(1), deep = Cursed(15);
        Assert.True(shallow > 0, "no curse at depth 1");
        Assert.True(deep > shallow * 2, $"depth 1: {shallow}, depth 15: {deep}");
    }

    [Fact]
    public void Stock_vendor_and_crafted_rolls_carry_no_curse()
    {
        Assert.DoesNotContain(Rolls(3000, Stock, 30, 15), i => Curses(i) > 0);
        Assert.DoesNotContain(Rolls(3000, Strange, 30, 15, source: "vendor:saxton"), i => Curses(i) > 0);
        Assert.DoesNotContain(Rolls(3000, Strange, 30, 15, source: "craft"), i => Curses(i) > 0);
    }

    //-------------------------------------------------------------------------
    // The history the host replays
    //-------------------------------------------------------------------------

    [Fact]
    public void A_rerolled_item_replays_through_its_history()
    {
        var item = Roller.Roll(Ctx(Strange), 21);
        var rerolled = Roller.Reroll(Roller.Reroll(item, 0, 900), 1, 901);

        Assert.True(Roller.Replays(rerolled));
        Assert.False(Roller.Replays(rerolled with { History = rerolled.History.RemoveAt(1) }));
    }

    [Fact]
    public void An_upgraded_item_replays_through_its_history()
    {
        var item = Roller.Roll(Ctx(Vintage, source: "craft"), 5);
        var upgraded = Roller.Upgrade(item, 77);

        Assert.Equal(Strange, upgraded.Rarity);
        Assert.True(Roller.Replays(upgraded));
        Assert.False(Roller.Replays(upgraded with { History = [] }));
    }
}
