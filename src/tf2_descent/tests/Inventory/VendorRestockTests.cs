using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>
/// When a vendor's stock is rolled again (plan_tf2_rpg Phase 9: "stock rolled per hub per hour";
/// plan_descent_logic WO8): one restock every economy.json <c>restock.seconds</c> of a town's
/// life, and a stock seed per town, vendor, buyer and restock.
/// </summary>
public sealed class VendorRestockTests
{
    private static readonly TF2DescentRules R = NewRules();

    private static double Period => R.Content.EconomySettings.RestockSeconds;

    [Fact]
    public void The_restock_period_is_an_hour_as_shipped()
    {
        Assert.Equal(3600, Period);
    }

    [Fact]
    public void A_town_shows_its_first_stock_until_one_period_has_passed()
    {
        Assert.Equal(0, R.Vendors.Restock(0));
        Assert.Equal(0, R.Vendors.Restock(Period - 1));
        Assert.Equal(1, R.Vendors.Restock(Period));
        Assert.Equal(2, R.Vendors.Restock(2.5 * Period));
    }

    [Fact]
    public void A_stock_seed_is_the_same_for_the_same_town_vendor_buyer_and_restock()
    {
        Assert.Equal(Vendors.StockSeed(7, "saxton", "a", 0), Vendors.StockSeed(7, "saxton", "a", 0));
    }

    [Fact]
    public void Each_of_town_vendor_buyer_and_restock_gives_another_stock_seed()
    {
        ulong seed = Vendors.StockSeed(7, "saxton", "a", 0);

        Assert.NotEqual(seed, Vendors.StockSeed(8, "saxton", "a", 0));
        Assert.NotEqual(seed, Vendors.StockSeed(7, "engineer", "a", 0));
        Assert.NotEqual(seed, Vendors.StockSeed(7, "saxton", "b", 0));
        Assert.NotEqual(seed, Vendors.StockSeed(7, "saxton", "a", 1));
    }

    [Fact]
    public void A_new_restock_rolls_another_stock()
    {
        var first = R.Vendors.StockOf("saxton@12@soldier", Vendors.StockSeed(7, "saxton", "a", 0));
        var next = R.Vendors.StockOf("saxton@12@soldier", Vendors.StockSeed(7, "saxton", "a", 1));

        Assert.NotEqual(first.Select(i => i.Seed), next.Select(i => i.Seed));
    }

    [Fact]
    public void A_restock_period_under_a_minute_is_a_load_error()
    {
        var errors = EconomyPlant.Errors(node => node["restock"]!["seconds"] = 5);

        var e = Assert.Single(errors);
        Assert.Equal(TF2.Descent.Rules.Content.ContentRule.OutOfRange, e.Rule);
        Assert.Equal(EconomyTable.TableName, e.Table);
    }
}
