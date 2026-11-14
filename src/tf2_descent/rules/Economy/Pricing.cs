using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
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

    /// <summary>The Scout's price for a sealed crate at a buyer's level: price + perLevel · level (D23).</summary>
    public long CratePrice(int level) => E.GamblePrice + E.GamblePerLevel * Math.Max(1, level);
}
