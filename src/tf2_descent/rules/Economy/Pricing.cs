using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Economy;

/// <summary>
/// What items are worth and what the Spy and the Scout charge (plan_tf2_rpg 2d, 2i, 2j; D22,
/// D23), in Australium, from economy.json:
/// <list type="bullet">
/// <item>Gear: base value · (1 + perItemLevel · item level) · rarity value · (1 + perPower ·
/// max(0, net power)). An unidentified item is valued as Stock with no power (D22: it sells at
/// the Stock price), so a sale reveals nothing.</item>
/// <item>Anything else: its base value per unit of its stack. A crate: the Scout's price for its level.</item>
/// <item>Buy = value · buy, rounded up; sell = value · sell, rounded down; vendor value = value.</item>
/// <item>The buyer's own price stats (plan_tf2_rpg Appendix A.5, Haggler: "−4 % vendor, repair,
/// identify and crate prices; +4 % sell prices") move a price last (<see cref="ForBuyer"/>):
/// what he pays by <c>price_buy_pct</c>, rounded up, what he is paid by <c>price_sell_pct</c>,
/// rounded down, each percent held inside its stat's range.</item>
/// </list>
/// </summary>
public sealed class Pricing
{
    private readonly DescentContent _content;
    private readonly ItemRoller _roller;

    public Pricing(DescentContent content, ItemRoller roller)
    {
        _content = content;
        _roller = roller;
    }

    private EconomySettings E => _content.EconomySettings;

    /// <summary>The vendor value (see the class summary).</summary>
    public long Value(ItemInstance item)
    {
        var b = _content.ItemBases[item.BaseId];
        if (b.Kind == ItemKinds.Crate) return CratePrice(item.ItemLevel);
        if (!b.IsGear) return b.Value * Math.Max(1, item.Count);
        int rarity = item.Identified ? item.Rarity : 0;
        double power = item.Identified ? Math.Max(0, _roller.NetPower(item)) : 0;
        double v = b.Value * (1 + E.ValuePerItemLevel * item.ItemLevel) * _content.Rarities.Rows[rarity].Value * (1 + E.ValuePerPower * power);
        return (long)Math.Round(v, MidpointRounding.AwayFromZero);
    }

    /// <summary>A buy, a sell or the vendor value (<c>IGameRules.Price</c>).</summary>
    public long Price(ItemInstance item, PriceKind kind)
    {
        long value = Value(item);
        return kind switch
        {
            PriceKind.Buy => (long)Math.Ceiling(value * E.BuyFactor - 1e-9),
            PriceKind.Sell => (long)Math.Floor(value * E.SellFactor + 1e-9),
            _ => value,
        };
    }

    /// <summary>The Spy's fee to identify an item: base + perItemLevel · item level, rounded up.</summary>
    public long IdentifyFee(ItemInstance item) => (long)Math.Ceiling(E.IdentifyBase + E.IdentifyPerItemLevel * item.ItemLevel - 1e-9);

    /// <summary>A buyer's price stats, from his stats' totals (economy.json's <c>buyer</c> names the two stats).</summary>
    public BuyerPrices BuyerOf(HeroStats stats) => new(stats.Total(E.BuyerBuyStat), stats.Total(E.BuyerSellStat));

    /// <summary>
    /// A price as <paramref name="buyer"/> meets it: a sell price (<see cref="PriceKind.Sell"/>)
    /// raised by his sell percent and rounded down; anything he pays (a buy, a repair, an
    /// identify, a crate) moved by his buy percent and rounded up. Each percent is held inside
    /// its stat's range; a price of 0 stays 0.
    /// </summary>
    public long ForBuyer(long price, PriceKind kind, BuyerPrices buyer)
    {
        if (price <= 0) return 0;
        bool sell = kind == PriceKind.Sell;
        string stat = sell ? E.BuyerSellStat : E.BuyerBuyStat;
        double pct = sell ? buyer.SellPct : buyer.BuyPct;
        if (_content.Stats.TryGet(stat, out var row)) pct = Math.Clamp(pct, row.Min, row.Max);
        double moved = price * (1 + pct / 100);
        return sell ? (long)Math.Floor(moved + 1e-9) : (long)Math.Ceiling(moved - 1e-9);
    }

    /// <summary>The Scout's price for a sealed crate at a buyer's level: price + perLevel · level (D23).</summary>
    public long CratePrice(int level) => E.GamblePrice + E.GamblePerLevel * Math.Max(1, level);
}

/// <summary>
/// A buyer's price stats (economy.json's <c>buyer</c>): the percent that moves what he pays and
/// the percent that moves what he is paid. <see cref="None"/> moves nothing.
/// </summary>
public readonly record struct BuyerPrices(double BuyPct, double SellPct)
{
    public static BuyerPrices None => default;
}
