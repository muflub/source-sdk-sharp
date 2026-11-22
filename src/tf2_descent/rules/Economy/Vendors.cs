using System.Globalization;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Economy;

/// <summary>
/// vendors.json: the town's vendors (plan_tf2_rpg 2d, D15). Each stocks <see cref="Stock"/>
/// items a roll, either from the bases it names or from every base of the kinds it names, in
/// a rarity from its odds (Stock alone when it names none). The bases it names under
/// <see cref="Always"/> it has at every restock, one of each after the rolled stock (the
/// Medic's Teleporter Blueprint: Diablo's healer always sells the town portal scroll). Vendor
/// stock arrives identified, with no curse (items.json's "vendor" source rule).
/// </summary>
public sealed record VendorRow(string Id, string Name, int Stock, IReadOnlyList<string> Kinds, IReadOnlyList<string> Bases, IReadOnlyList<RarityOdds> Odds,
                               IReadOnlyList<string> Always) : IContentRow;

public sealed class VendorTable : RowTable<VendorRow>
{
    public const string TableName = "vendors";

    public override string Name => TableName;

    protected override VendorRow ReadRow(JsonFields f)
    {
        var odds = new List<RarityOdds>();
        foreach (var e in f.Array("odds", required: false))
        {
            var o = new JsonFields(e, f.Table, f.Row, f.Errors);
            odds.Add(new RarityOdds(o.Str("rarity"), o.Int("weight")));
            o.End();
        }
        return new VendorRow(f.Str("id"), f.Str("name"), f.Int("stock"),
            ItemTables.Strings(f, "kinds", required: false), ItemTables.Strings(f, "bases", required: false), odds,
            ItemTables.Strings(f, "always", required: false));
    }

    protected override void ValidateRow(DescentContent content, VendorRow row, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, row.Id, "stock", row.Stock, 1, 100);
        if (row.Kinds.Count == 0 == (row.Bases.Count == 0))
            errors.Add(t, row.Id, ContentRule.Malformed, "a vendor names 'kinds' or 'bases', one of the two");
        foreach (var k in row.Kinds)
            ItemTables.OneOf(errors, t, row.Id, "kinds", k, ItemTables.Kinds);
        foreach (var b in row.Bases)
            ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "bases", b, ItemTables.Bases);
        foreach (var b in row.Always)
            ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "always", b, ItemTables.Bases);
        foreach (var o in row.Odds)
        {
            ContentChecks.Range(errors, t, row.Id, "odds." + o.Rarity, o.Weight, 1, 1e9);
            if (ContentChecks.Reference<RarityRow>(content, errors, t, row.Id, "odds", o.Rarity, ItemTables.Rarities)
                && !content.Rarities[o.Rarity].Craftable)
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'odds' names '{o.Rarity}', which a vendor may not sell");
        }
    }
}

/// <summary>
/// Vendor stock (<c>IGameRules.VendorStock</c>), rolled per vendor per seed at the buyer's
/// level. The contract's <c>VendorStock(vendor, seed)</c> carries no buyer, so a vendor name
/// may carry one: <c>"saxton@12"</c> is Saxton's stock for a level 12 buyer, and
/// <c>"saxton@12@soldier"</c> for a level 12 Soldier; a bare name is level 1, any class.
/// An unknown vendor stocks nothing.
/// </summary>
public sealed class Vendors
{
    private readonly DescentContent _content;
    private readonly ItemRoller _roller;
    private readonly ItemCodec _codec;

    public Vendors(DescentContent content, ItemRoller roller, ItemCodec codec)
    {
        _content = content;
        _roller = roller;
        _codec = codec;
    }

    /// <summary>A vendor name split into the vendor, the buyer's level and class (see the class summary).</summary>
    public static (string Vendor, int Level, string Class) Parse(string vendor)
    {
        var parts = vendor.Split('@');
        int level = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? Math.Max(1, l) : 1;
        return (parts[0], level, parts.Length > 2 ? parts[2] : "any");
    }

    /// <summary>
    /// Which stock a town shows <paramref name="seconds"/> after it opened (plan_tf2_rpg Phase 9:
    /// "stock rolled per hub per hour"): one more every economy.json <c>restock.seconds</c>, from 0.
    /// </summary>
    public long Restock(double seconds) =>
        seconds <= 0 ? 0 : (long)Math.Floor(seconds / _content.EconomySettings.RestockSeconds + 1e-9);

    /// <summary>
    /// The seed of one buyer's stock at one vendor in one restock of one town: a town (its own
    /// seed), the vendor, the buyer and the restock each give another roll, the same four the same.
    /// </summary>
    public static ulong StockSeed(ulong townSeed, string vendor, string buyer, long restock) =>
        SeededRandom.Mix(townSeed, "vendor_stock", vendor, buyer, restock.ToString(CultureInfo.InvariantCulture));

    /// <summary>The stock of <paramref name="vendor"/> for one seed (see the class summary).</summary>
    public IReadOnlyList<RolledItem> Stock(string vendor, ulong seed) =>
        [.. StockOf(vendor, seed).Select(_codec.ToRolled)];

    /// <summary>The stock as instances.</summary>
    public IReadOnlyList<ItemInstance> StockOf(string vendor, ulong seed)
    {
        var (id, level, cls) = Parse(vendor);
        if (!_content.Vendors.TryGet(id, out var row)) return [];
        int itemLevel = Math.Clamp(level, 1, _content.ItemSettings.MaxItemLevel);
        var pool = row.Bases.Count > 0
            ? row.Bases.Select(b => _content.ItemBases[b]).ToList()
            : _content.ItemBases.Rows.Where(b => row.Kinds.Contains(b.Kind) && b.Level <= itemLevel).ToList();
        var forClass = pool.Where(b => b.AllowsClass(cls)).ToList();
        if (forClass.Count > 0) pool = forClass;
        var stock = new List<ItemInstance>();
        if (pool.Count == 0) return Always(row, level, cls, itemLevel, seed, stock);
        for (int i = 0; i < row.Stock; i++)
        {
            var r = SeededRandom.For(seed, "vendor", row.Id, i.ToString(CultureInfo.InvariantCulture));
            var b = r.Weighted(pool, x => Math.Max(1, x.DropWeight));
            int rarity = row.Odds.Count == 0 ? 0 : Index(r.Weighted(row.Odds, o => o.Weight).Rarity);
            var ctx = new ItemRollContext(cls, level, 0, itemLevel, rarity, _content.ItemSettings.ForcedBasePrefix + b.Id, "vendor:" + row.Id);
            stock.Add(_roller.Roll(ctx, SeededRandom.Mix(seed, "vendor_item", row.Id, i.ToString(CultureInfo.InvariantCulture))));
        }
        return Always(row, level, cls, itemLevel, seed, stock);
    }

    /// <summary>The stock with one of each base the vendor always has after it, rolled Stock from the same seed.</summary>
    private List<ItemInstance> Always(VendorRow row, int level, string cls, int itemLevel, ulong seed, List<ItemInstance> stock)
    {
        foreach (var b in row.Always)
        {
            var ctx = new ItemRollContext(cls, level, 0, itemLevel, 0, _content.ItemSettings.ForcedBasePrefix + b, "vendor:" + row.Id);
            stock.Add(_roller.Roll(ctx, SeededRandom.Mix(seed, "vendor_always", row.Id, b)));
        }
        return stock;
    }

    private int Index(string rarity)
    {
        var rows = _content.Rarities.Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Id == rarity) return i;
        throw new KeyNotFoundException($"no rarity '{rarity}'");
    }
}
