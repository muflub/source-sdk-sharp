using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// A starting kit item sells for nothing (the user, 2026-11-22, plan L26): a respawn gives the
/// class's kit again, so a kit that sold would be farmed by dying. A kit item is known by its
/// source (<see cref="StartingKitRow.Source"/>), whatever its base.
/// </summary>
public sealed class StartingKitPriceTests
{
    private static readonly TF2DescentRules R = NewRules();

    private static ItemInstance Kit(string baseId) =>
        R.ItemRules.Roller.Roll(Ctx(0, 1, depth: 0, table: "base:" + baseId, source: StartingKitRow.Source), 3);

    private static ItemInstance Bought(string baseId) =>
        R.ItemRules.Roller.Roll(Ctx(0, 1, depth: 0, table: "base:" + baseId, source: "vendor:saxton"), 3);

    [Fact]
    public void A_starting_kit_weapon_sells_for_nothing()
    {
        Assert.True(R.Pricing.Price(Bought("rocket_launcher"), PriceKind.Sell) > 0, "the control: the same base bought sells");

        Assert.Equal(0, R.Pricing.Price(Kit("rocket_launcher"), PriceKind.Sell));
    }

    [Fact]
    public void A_starting_kit_potion_sells_for_nothing()
    {
        Assert.True(R.Pricing.Price(Bought("health_small"), PriceKind.Sell) > 0, "the control: the same potion bought sells");

        Assert.Equal(0, R.Pricing.Price(Kit("health_small"), PriceKind.Sell));
    }

    [Fact]
    public void The_rules_answer_the_host_a_sell_price_of_nothing_for_a_kit_item()
    {
        var rolled = R.ItemRules.Codec.ToRolled(Kit("shovel"));

        Assert.Equal(0, R.Price(rolled, PriceKind.Sell));
    }

    [Fact]
    public void A_starting_kit_item_keeps_its_buy_price()
    {
        Assert.Equal(R.Pricing.Price(Bought("shotgun"), PriceKind.Buy), R.Pricing.Price(Kit("shotgun"), PriceKind.Buy));
    }
}
