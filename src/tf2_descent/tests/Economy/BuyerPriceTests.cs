using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Skills;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// The buyer's own price stats (plan_tf2_rpg Appendix A.5, Haggler: "−4 % vendor, repair,
/// identify and crate prices; +4 % sell prices"), the first reader of <c>price_buy_pct</c> and
/// <c>price_sell_pct</c> (plan_descent_logic WO8).
/// </summary>
public sealed class BuyerPriceTests
{
    private static readonly TF2DescentRules R = NewRules();

    private static BuyerPrices Haggler(int rank)
    {
        var sheet = R.Heroes.New("soldier", "h");
        sheet = sheet with { SkillRanks = sheet.SkillRanks.SetItem("haggler", rank) };
        return R.Pricing.BuyerOf(R.Skills.Stats(sheet, SkillBonuses.None));
    }

    [Fact]
    public void A_buyer_with_no_price_stats_pays_the_rules_price()
    {
        Assert.Equal(1000, R.Pricing.ForBuyer(1000, PriceKind.Buy, BuyerPrices.None));
        Assert.Equal(250, R.Pricing.ForBuyer(250, PriceKind.Sell, BuyerPrices.None));
    }

    [Fact]
    public void Haggler_at_rank_five_lowers_a_buy_price_by_twenty_percent()
    {
        var buyer = Haggler(5);

        Assert.Equal(-20, buyer.BuyPct);
        Assert.Equal(800, R.Pricing.ForBuyer(1000, PriceKind.Buy, buyer));
    }

    [Fact]
    public void Haggler_at_rank_five_raises_a_sell_price_by_twenty_percent()
    {
        var buyer = Haggler(5);

        Assert.Equal(20, buyer.SellPct);
        Assert.Equal(300, R.Pricing.ForBuyer(250, PriceKind.Sell, buyer));
    }

    [Fact]
    public void A_price_paid_rounds_up_and_a_price_paid_out_rounds_down()
    {
        var buyer = new BuyerPrices(-4, 4);

        Assert.Equal(97, R.Pricing.ForBuyer(101, PriceKind.Buy, buyer));     // 96.96
        Assert.Equal(105, R.Pricing.ForBuyer(101, PriceKind.Sell, buyer));   // 105.04
    }

    [Fact]
    public void A_percent_is_held_inside_its_stats_range()
    {
        var floor = R.Content.Stats[R.Content.EconomySettings.BuyerBuyStat].Min;

        Assert.Equal((long)Math.Ceiling(1000 * (1 + floor / 100) - 1e-9),
                     R.Pricing.ForBuyer(1000, PriceKind.Buy, new BuyerPrices(-500, 0)));
    }

    [Fact]
    public void A_free_price_stays_free()
    {
        Assert.Equal(0, R.Pricing.ForBuyer(0, PriceKind.Buy, new BuyerPrices(50, 50)));
    }

    [Fact]
    public void Economy_naming_a_buyer_stat_that_is_no_stat_is_a_load_error()
    {
        var e = Assert.Single(EconomyPlant.Errors(node => node["buyer"]!["buy"] = "price_buy_percent"));

        Assert.Equal(ContentRule.UnknownStat, e.Rule);
        Assert.Equal(EconomyTable.TableName, e.Table);
    }
}
