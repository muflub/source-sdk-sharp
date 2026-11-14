using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Loot;

/// <summary>What one roll of a loot table gives a player: nothing, a potion or an item (Australium is the kill's, rolled once: <see cref="AustraliumSettings"/>).</summary>
public enum LootOutcome
{
    Nothing,
    Potion,
    Item,
}

/// <summary>A potion base and its weight among a table's potions.</summary>
public sealed record PotionWeight(string Base, double Weight);

/// <summary>
/// A rarity's weight in a table's tier roll at depth 1, and how much it grows each depth below
/// that, compounding: weight · (1 + perDepthPct / 100)^(depth − 1).
/// </summary>
public sealed record TierWeight(string Rarity, double Weight, double PerDepthPct)
{
    public double At(int depth) => Weight * Math.Pow(1 + PerDepthPct / 100, Math.Max(0, depth - 1));
}

/// <summary>
/// A loot table (plan_tf2_rpg 2c): the weights of a player's roll (nothing, potion, item), the
/// potions it gives, and the rarity tiers of its items by depth. levels.json names one per
/// depth (the act's); furniture.json names the containers'.
/// </summary>
public sealed record LootTableRow(
    string Id,
    double Nothing,
    double Potion,
    double Item,
    IReadOnlyList<PotionWeight> Potions,
    IReadOnlyList<TierWeight> Tiers) : IContentRow;

/// <summary>
/// The Australium a kill pays (R4: rolled once, split among the party in range): it pays with
/// <see cref="ChancePct"/>, an amount of base · (1 + perDepthPct / 100)^(depth − 1) · the
/// robot's quality · a spread of ± <see cref="SpreadPct"/>, raised <see cref="PerExtraMemberPct"/>
/// for each member past the first. A container pays each player with <see cref="ContainerChancePct"/>
/// at quality 1.
/// </summary>
public sealed record AustraliumSettings(double ChancePct, double Base, double PerDepthPct, double SpreadPct, double PerExtraMemberPct,
    double ContainerChancePct);

/// <summary>
/// What champion affixes do to a kill (2c): each affix raises the item weight and the
/// Australium by these percents and adds tier rolls (the best of them stands). A champion's own
/// table (<c>IGameRules.BossDrops</c>, tier −1) gives each player <see cref="Items"/> items a
/// affix, at least <see cref="MinRarity"/>.
/// </summary>
public sealed record ChampionLoot(double ItemPctPerAffix, double AustraliumPctPerAffix, int TierRollsPerAffix, int Items, string MinRarity);

/// <summary>
/// A boss's own table (D13), rolled for each player: <see cref="Items"/> items on its tier
/// weights, <see cref="Potions"/> of <see cref="Potion"/>, and the Australium it pays.
/// </summary>
public sealed record BossLoot(string Id, int Items, int Potions, string Potion, long Australium, IReadOnlyList<TierWeight> Tiers) : IContentRow;

/// <summary>
/// loot.json (plan_tf2_rpg 2c; R4, D11, D13; plan_descent_logic RU4). A robot's quality is the
/// XP its template pays at the middle of its scale range, at level 1 with no affixes, over
/// <see cref="ReferenceXp"/>, clamped: XP and loot follow life and temperament (2f), so a giant
/// pays more and a runt less. Quality raises the item weight by quality^<see cref="ItemQualityExponent"/>
/// and the Australium in proportion.
/// </summary>
public sealed record LootSettings(
    double ReferenceXp,
    double QualityMin,
    double QualityMax,
    double ItemQualityExponent,
    AustraliumSettings Australium,
    ChampionLoot Champion,
    IReadOnlyList<LootTableRow> Tables,
    IReadOnlyList<BossLoot> Bosses)
{
    public LootTableRow Table(string id) =>
        Tables.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"no loot table '{id}'");

    public BossLoot? Boss(string template) => Bosses.FirstOrDefault(b => b.Id == template);
}

public sealed class LootTable : SingleTable<LootSettings>
{
    public const string TableName = "loot";

    public override string Name => TableName;

    private static IReadOnlyList<TierWeight> Tiers(JsonFields f, bool growth)
    {
        var list = new List<TierWeight>();
        foreach (var e in f.Array("tiers"))
        {
            var t = new JsonFields(e, f.Table, f.Row, f.Errors);
            list.Add(new TierWeight(t.Str("rarity"), t.Num("weight"), growth ? t.Num("perDepthPct") : 0));
            t.End();
        }
        return list;
    }

    protected override LootSettings Read(JsonFields f)
    {
        var a = f.Obj("australium");
        var australium = new AustraliumSettings(a.Num("chancePct"), a.Num("base"), a.Num("perDepthPct"), a.Num("spreadPct"),
            a.Num("perExtraMemberPct"), a.Num("containerChancePct"));
        a.End();
        var c = f.Obj("champion");
        var champion = new ChampionLoot(c.Num("itemPctPerAffix"), c.Num("australiumPctPerAffix"), c.Int("tierRollsPerAffix"),
            c.Int("items"), c.Str("minRarity"));
        c.End();
        var tables = new List<LootTableRow>();
        foreach (var e in f.Array("tables"))
        {
            string? id = e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty("id", out var idv) ? idv.GetString() : null;
            var t = new JsonFields(e, f.Table, id ?? "tables", f.Errors);
            var o = t.Obj("outcomes");
            double nothing = o.Num("nothing"), potion = o.Num("potion"), item = o.Num("item");
            o.End();
            var potions = new List<PotionWeight>();
            foreach (var pe in t.Array("potions"))
            {
                var p = new JsonFields(pe, f.Table, id, f.Errors);
                potions.Add(new PotionWeight(p.Str("base"), p.Num("weight")));
                p.End();
            }
            tables.Add(new LootTableRow(t.Str("id"), nothing, potion, item, potions, Tiers(t, growth: true)));
            t.End();
        }
        var bosses = new List<BossLoot>();
        foreach (var e in f.Array("bosses"))
        {
            string? id = e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty("id", out var idv) ? idv.GetString() : null;
            var b = new JsonFields(e, f.Table, id ?? "bosses", f.Errors);
            bosses.Add(new BossLoot(b.Str("id"), b.Int("items"), b.Int("potions"), b.Str("potion"), b.Long("australium"), Tiers(b, growth: false)));
            b.End();
        }
        return new LootSettings(f.Num("referenceXp"), f.Num("qualityMin"), f.Num("qualityMax"), f.Num("itemQualityExponent"),
            australium, champion, tables, bosses);
    }

    protected override void Validate(DescentContent content, LootSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "referenceXp", s.ReferenceXp, 1e-6, 1e9);
        if (ContentChecks.Range(errors, t, null, "qualityMin", s.QualityMin, 1e-6, 1e6))
            ContentChecks.Range(errors, t, null, "qualityMax", s.QualityMax, s.QualityMin, 1e6);
        ContentChecks.Range(errors, t, null, "itemQualityExponent", s.ItemQualityExponent, 0, 4);
        var au = s.Australium;
        ContentChecks.Range(errors, t, "australium", "chancePct", au.ChancePct, 0, 100);
        ContentChecks.Range(errors, t, "australium", "containerChancePct", au.ContainerChancePct, 0, 100);
        ContentChecks.Range(errors, t, "australium", "base", au.Base, 0, 1e9);
        ContentChecks.Range(errors, t, "australium", "perDepthPct", au.PerDepthPct, 0, 1000);
        ContentChecks.Range(errors, t, "australium", "spreadPct", au.SpreadPct, 0, 100);
        ContentChecks.Range(errors, t, "australium", "perExtraMemberPct", au.PerExtraMemberPct, 0, 1000);
        var ch = s.Champion;
        ContentChecks.Range(errors, t, "champion", "itemPctPerAffix", ch.ItemPctPerAffix, 0, 10000);
        ContentChecks.Range(errors, t, "champion", "australiumPctPerAffix", ch.AustraliumPctPerAffix, 0, 10000);
        ContentChecks.Range(errors, t, "champion", "tierRollsPerAffix", ch.TierRollsPerAffix, 0, 16);
        ContentChecks.Range(errors, t, "champion", "items", ch.Items, 0, 16);
        ContentChecks.Reference<RarityRow>(content, errors, t, "champion", "minRarity", ch.MinRarity, ItemTables.Rarities);

        RobotJson.Unique(errors, t, "tables", s.Tables.Select(x => x.Id));
        foreach (var row in s.Tables)
        {
            ContentChecks.Range(errors, t, row.Id, "outcomes.nothing", row.Nothing, 0, 1e9);
            ContentChecks.Range(errors, t, row.Id, "outcomes.potion", row.Potion, 0, 1e9);
            ContentChecks.Range(errors, t, row.Id, "outcomes.item", row.Item, 0, 1e9);
            if (!(row.Nothing + row.Potion + row.Item > 0))
                errors.Add(t, row.Id, ContentRule.OutOfRange, "no outcome has weight");
            if (row.Potion > 0 && !(row.Potions.Sum(p => p.Weight) > 0))
                errors.Add(t, row.Id, ContentRule.OutOfRange, "a table that drops potions names a potion with weight");
            foreach (var p in row.Potions)
                Potion(content, errors, row.Id, "potions", p.Base, p.Weight);
            TierChecks(content, errors, row.Id, row.Tiers);
        }
        RobotJson.Unique(errors, t, "bosses", s.Bosses.Select(x => x.Id));
        foreach (var b in s.Bosses)
        {
            if (ContentChecks.Reference<RobotTemplate>(content, errors, t, b.Id, "id", b.Id, ContentTables.Robots)
                && !content.Robots[b.Id].Boss)
                errors.Add(t, b.Id, ContentRule.OutOfRange, $"'{b.Id}' is not a boss: only a boss has a table of its own");
            ContentChecks.Range(errors, t, b.Id, "items", b.Items, 0, 64);
            ContentChecks.Range(errors, t, b.Id, "potions", b.Potions, 0, 64);
            ContentChecks.Range(errors, t, b.Id, "australium", b.Australium, 0, 1e12);
            Potion(content, errors, b.Id, "potion", b.Potion, 1);
            TierChecks(content, errors, b.Id, b.Tiers);
        }

        // Every table the levels and the containers name is here, and every boss has its own (D13).
        var ids = s.Tables.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (content.TryGet<Table<DepthRow>>(ContentTables.Levels, out var levels))
            foreach (var d in levels.Rows.Where(d => !ids.Contains(d.LootTable)))
                errors.Add(t, d.Id, ContentRule.DanglingReference, $"depth {d.Depth} names loot table '{d.LootTable}', which is not a table of '{t}'");
        if (content.TryGet<Furniture.FurnitureSettings>(ContentTables.Furniture, out var furniture) && !ids.Contains(furniture.Containers.LootTable))
            errors.Add(t, null, ContentRule.DanglingReference, $"the containers name loot table '{furniture.Containers.LootTable}', which is not a table of '{t}'");
        if (content.TryGet<Table<RobotTemplate>>(ContentTables.Robots, out var robots))
            foreach (var r in robots.Rows.Where(r => r.Boss && s.Boss(r.Id) is null))
                errors.Add(t, r.Id, ContentRule.DanglingReference, $"boss '{r.Id}' has no table of its own");
    }

    private void Potion(DescentContent content, ContentErrors errors, string row, string field, string id, double weight)
    {
        ContentChecks.Range(errors, Name, row, field + "." + id, weight, 0, 1e9);
        if (ContentChecks.Reference<ItemBaseRow>(content, errors, Name, row, field, id, ItemTables.Bases)
            && content.ItemBases[id].Kind != ItemKinds.Potion)
            errors.Add(Name, row, ContentRule.DanglingReference, $"'{field}' names '{id}', which is not a potion");
    }

    private void TierChecks(DescentContent content, ContentErrors errors, string row, IReadOnlyList<TierWeight> tiers)
    {
        RobotJson.Unique(errors, Name, row + ".tiers", tiers.Select(x => x.Rarity));
        foreach (var tw in tiers)
        {
            ContentChecks.Reference<RarityRow>(content, errors, Name, row, "tiers", tw.Rarity, ItemTables.Rarities);
            ContentChecks.Range(errors, Name, row, "tiers." + tw.Rarity + ".weight", tw.Weight, 0, 1e9);
            ContentChecks.Range(errors, Name, row, "tiers." + tw.Rarity + ".perDepthPct", tw.PerDepthPct, -100, 1000);
        }
        if (!(tiers.Sum(x => x.Weight) > 0))
            errors.Add(Name, row, ContentRule.OutOfRange, "no tier has weight");
    }
}
