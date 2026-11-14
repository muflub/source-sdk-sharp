using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// The Scout's sealed crates (plan_descent_logic RU9; plan_tf2_rpg 2j, D23): bought by base
/// type for Australium, opened at the buyer's level ± 2 into a rarity on the published odds
/// table, unidentified. The comparison with the cost of playing is RU14's, through
/// <see cref="Crafting.ExpectedCostPerUnusualFromGambling"/>.
/// </summary>
public sealed class GamblingTests
{
    private static readonly TF2DescentRules R = NewRules();

    private static RolledItem Crate(string id = "crate_rocket_launcher", int level = 12) =>
        R.ItemRules.Codec.ToRolled(R.ItemRules.Roller.Roll(Ctx(0, level, table: "base:" + id, source: "vendor:scout"), 1));

    private static ItemRollContext Buyer(int level) => Ctx(0, level, source: "test");

    [Fact]
    public void A_crate_opens_into_one_of_its_bases_unidentified_and_is_consumed()
    {
        var crate = Crate();
        var holds = R.Content.ItemBases["crate_rocket_launcher"].Contains;

        for (ulong seed = 0; seed < 500; seed++)
        {
            var change = R.OpenCrate(crate, Buyer(12), seed);
            var item = Assert.Single(change.Produced);
            Assert.Equal([0], change.Consumed);
            Assert.Contains(item.BaseType, holds);
            Assert.False(item.Identified);
            Assert.True(R.Replays(item));
        }
    }

    [Fact]
    public void A_crate_rolls_at_the_buyers_level_plus_or_minus_two()
    {
        var levels = Enumerable.Range(0, 2000).Select(s => R.OpenCrate(Crate(), Buyer(12), (ulong)s).Produced[0].ItemLevel).ToHashSet();

        Assert.Equal([10, 11, 12, 13, 14], levels.Order());
    }

    [Fact]
    public void Rarities_over_100k_opens_match_the_published_odds()
    {
        var odds = R.Crafting.GambleOdds();
        var counts = new Dictionary<int, int>();
        var crate = Crate();
        const int opens = 100_000;
        for (ulong s = 0; s < opens; s++)
        {
            int rarity = R.OpenCrate(crate, Buyer(12), s).Produced[0].Rarity;
            counts[rarity] = counts.GetValueOrDefault(rarity) + 1;
        }

        Assert.Equal(odds.Keys.Order(), counts.Keys.Order());
        foreach (var (rarity, p) in odds)
        {
            double expected = p * opens, sd = Math.Sqrt(opens * p * (1 - p));
            Assert.InRange(counts[rarity], expected - 4 * sd, expected + 4 * sd);   // 4 standard deviations
        }
    }

    [Fact]
    public void The_odds_put_top_rarities_well_below_common_ones()
    {
        var odds = R.Crafting.GambleOdds();
        int unusual = R.ItemRules.Roller.UnusualRarity();

        Assert.True(odds[unusual] < 0.05, $"Unusual at {odds[unusual]}");
        Assert.False(odds.ContainsKey(4));   // no Australium from a crate
        Assert.Equal(1, odds.Values.Sum(), 9);
    }

    [Fact]
    public void A_crate_costs_a_steep_price_rising_with_the_buyers_level()
    {
        var c1 = R.ItemRules.Codec.Instance(Crate(level: 1));
        var c20 = R.ItemRules.Codec.Instance(Crate(level: 20));

        Assert.True(R.Pricing.Price(c20, PriceKind.Buy) > R.Pricing.Price(c1, PriceKind.Buy));
        Assert.True(R.Pricing.Price(c1, PriceKind.Buy) > 10 * R.Pricing.Price(R.ItemRules.Roller.Roll(Ctx(1, 1, table: "base:rocket_launcher", source: "vendor:saxton"), 1), PriceKind.Buy));
    }

    [Fact]
    public void Opening_something_that_is_not_a_crate_is_refused_and_consumes_nothing()
    {
        var change = R.OpenCrate(R.RollItem(Ctx(1), 1), Buyer(10), 1);

        Assert.Equal(CraftRefusals.NotACrate, change.Refusal);
        Assert.Empty(change.Consumed);
    }

    [Fact]
    public void A_crate_opening_is_seed_stable()
    {
        Assert.Equal(R.OpenCrate(Crate(), Buyer(12), 7).Produced[0].Instance, R.OpenCrate(Crate(), Buyer(12), 7).Produced[0].Instance);
    }

    [Fact]
    public void The_expected_cost_of_an_unusual_from_gambling_is_published_for_the_economy_model()
    {
        double cost = R.Crafting.ExpectedCostPerUnusualFromGambling(10);
        int unusual = R.ItemRules.Roller.UnusualRarity();

        Assert.Equal(R.Pricing.CratePrice(10) / R.Crafting.GambleOdds()[unusual], cost, 6);
    }
}
