using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Economy;

/// <summary>A rarity and its weight in a published odds table.</summary>
public sealed record RarityOdds(string Rarity, int Weight);

/// <summary>
/// economy.json (plan_tf2_rpg 2d, 2i, 2j, 2k; D22, D23, D24): what items are worth and what
/// the town's services cost. Vendor value of gear is the base's value, raised per item level,
/// times the rarity's value multiplier, raised by its net power (an unidentified item is
/// valued as Stock, D22); a buy and a sell are fractions of it. The Spy's identify fee rises
/// with item level; a Dossier identifies in the field. Metal is TF2's ladder (each rung worth
/// <see cref="MetalRatio"/> of the one below, both ways); salvage yields the rarity's metal
/// raised per item level. Recipes cost metal: a random item of a chosen base (on the
/// published craft odds), a re-roll (rising each time on an item, cursed stats at a
/// multiple), a rarity upgrade (the rarity's upgradeCost), a Repair Kit; three potions make
/// the next size. Crates cost Australium by the buyer's level and open on the published
/// gambling odds.
/// </summary>
public sealed record EconomySettings(
    double ValuePerItemLevel,
    double ValuePerPower,
    double BuyFactor,
    double SellFactor,
    double IdentifyBase,
    double IdentifyPerItemLevel,
    string IdentifyRecipe,
    string Dossier,
    IReadOnlyList<string> MetalLadder,
    int MetalRatio,
    string MetalUpRecipe,
    string MetalDownRecipe,
    double SalvagePerItemLevel,
    string CraftItemRecipe,
    int CraftItemCost,
    IReadOnlyList<RarityOdds> CraftOdds,
    string RerollRecipe,
    int RerollCost,
    double RerollGrowth,
    double RerollCursedMultiplier,
    string UpgradeRecipe,
    string RepairKitRecipe,
    int RepairKitCost,
    string PotionRecipe,
    int PotionCombine,
    long GamblePrice,
    long GamblePerLevel,
    int GambleLevelSpread,
    IReadOnlyList<RarityOdds> GambleOdds);

public sealed class EconomyTable : SingleTable<EconomySettings>
{
    public const string TableName = "economy";

    public override string Name => TableName;

    private static IReadOnlyList<RarityOdds> Odds(JsonFields f, string field)
    {
        var list = new List<RarityOdds>();
        foreach (var e in f.Array(field))
        {
            var o = new JsonFields(e, f.Table, f.Row, f.Errors);
            list.Add(new RarityOdds(o.Str("rarity"), o.Int("weight")));
            o.End();
        }
        return list;
    }

    protected override EconomySettings Read(JsonFields f)
    {
        var value = f.Obj("value");
        var prices = f.Obj("prices");
        var identify = f.Obj("identify");
        var metal = f.Obj("metal");
        var salvage = f.Obj("salvage");
        var craft = f.Obj("craftItem");
        var reroll = f.Obj("reroll");
        var upgrade = f.Obj("upgrade");
        var kit = f.Obj("repairKit");
        var potions = f.Obj("potions");
        var gamble = f.Obj("gambling");
        var s = new EconomySettings(
            value.Num("perItemLevel"), value.Num("perPower"),
            prices.Num("buy"), prices.Num("sell"),
            identify.Num("base"), identify.Num("perItemLevel"), identify.Str("recipe"), identify.Str("dossier"),
            ItemTables.Strings(metal, "ladder"), metal.Int("ratio"), metal.Str("up"), metal.Str("down"),
            salvage.Num("perItemLevel"),
            craft.Str("recipe"), craft.Int("cost"), Odds(craft, "odds"),
            reroll.Str("recipe"), reroll.Int("cost"), reroll.Num("growth"), reroll.Num("cursedMultiplier"),
            upgrade.Str("recipe"),
            kit.Str("recipe"), kit.Int("cost"),
            potions.Str("recipe"), potions.Int("combine"),
            gamble.Long("price"), gamble.Long("perLevel"), gamble.Int("levelSpread"), Odds(gamble, "odds"));
        foreach (var o in new[] { value, prices, identify, metal, salvage, craft, reroll, upgrade, kit, potions, gamble })
            o.End();
        return s;
    }

    protected override void Validate(DescentContent content, EconomySettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "value.perItemLevel", s.ValuePerItemLevel, 0, 100);
        ContentChecks.Range(errors, t, null, "value.perPower", s.ValuePerPower, 0, 100);
        ContentChecks.Range(errors, t, null, "prices.buy", s.BuyFactor, 1e-6, 100);
        ContentChecks.Range(errors, t, null, "prices.sell", s.SellFactor, 0, s.BuyFactor);   // never sell above the buy price
        ContentChecks.Range(errors, t, null, "identify.base", s.IdentifyBase, 0, 1e9);
        ContentChecks.Range(errors, t, null, "identify.perItemLevel", s.IdentifyPerItemLevel, 0, 1e9);
        ContentChecks.Range(errors, t, null, "metal.ratio", s.MetalRatio, 2, 100);
        ContentChecks.Range(errors, t, null, "salvage.perItemLevel", s.SalvagePerItemLevel, 0, 100);
        ContentChecks.Range(errors, t, null, "craftItem.cost", s.CraftItemCost, 1, 1e6);
        ContentChecks.Range(errors, t, null, "reroll.cost", s.RerollCost, 1, 1e6);
        ContentChecks.Range(errors, t, null, "reroll.growth", s.RerollGrowth, 1, 100);
        ContentChecks.Range(errors, t, null, "reroll.cursedMultiplier", s.RerollCursedMultiplier, 1, 100);
        ContentChecks.Range(errors, t, null, "repairKit.cost", s.RepairKitCost, 1, 1e6);
        ContentChecks.Range(errors, t, null, "potions.combine", s.PotionCombine, 2, 100);
        ContentChecks.Range(errors, t, null, "gambling.price", s.GamblePrice, 1, 1e12);
        ContentChecks.Range(errors, t, null, "gambling.perLevel", s.GamblePerLevel, 0, 1e12);
        ContentChecks.Range(errors, t, null, "gambling.levelSpread", s.GambleLevelSpread, 0, 100);

        if (ContentChecks.Reference<ItemBaseRow>(content, errors, t, null, "identify.dossier", s.Dossier, ItemTables.Bases)
            && content.ItemBases[s.Dossier].Kind != ItemKinds.Consumable)
            errors.Add(t, null, ContentRule.DanglingReference, $"'identify.dossier' names '{s.Dossier}', which is not a consumable");

        if (s.MetalLadder.Count < 2)
            errors.Add(t, null, ContentRule.Malformed, "'metal.ladder' needs two rungs");
        int? below = null;
        foreach (var m in s.MetalLadder)
        {
            if (!ContentChecks.Reference<ItemBaseRow>(content, errors, t, null, "metal.ladder", m, ItemTables.Bases)) { below = null; continue; }
            var b = content.ItemBases[m];
            if (b.Kind != ItemKinds.Metal)
                errors.Add(t, null, ContentRule.DanglingReference, $"'metal.ladder' names '{m}', which is not metal");
            else if (below is { } lower && b.Metal != lower * s.MetalRatio)
                errors.Add(t, null, ContentRule.OutOfRange, $"'{m}' is worth {b.Metal}, not {s.MetalRatio} x {lower}");
            below = b.Metal;
        }

        foreach (var (field, odds) in new[] { ("craftItem.odds", s.CraftOdds), ("gambling.odds", s.GambleOdds) })
        {
            if (odds.Count == 0)
                errors.Add(t, null, ContentRule.Malformed, $"'{field}' is empty");
            foreach (var o in odds)
            {
                ContentChecks.Range(errors, t, null, field + "." + o.Rarity, o.Weight, 1, 1e9);
                ContentChecks.Reference<RarityRow>(content, errors, t, null, field, o.Rarity, ItemTables.Rarities);
            }
        }
        if (content.TryGet<Table<RarityRow>>(ItemTables.Rarities, out var rarities))
            foreach (var o in s.CraftOdds.Where(o => rarities.TryGet(o.Rarity, out var r) && !r.Craftable))
                errors.Add(t, null, ContentRule.OutOfRange, $"'craftItem.odds' names '{o.Rarity}', which no recipe may output");
    }
}
