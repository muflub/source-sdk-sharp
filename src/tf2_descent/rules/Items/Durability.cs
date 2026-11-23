using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Items;

/// <summary>A weapon family's wear: one point of durability per <see cref="UsesPerPoint"/> uses.</summary>
public sealed record FamilyWear(string Family, int UsesPerPoint);

/// <summary>
/// durability.json (plan_tf2_rpg 2h, D21; ruling L7 as amended): how fast each weapon family
/// wears per use (a minigun is not a shovel) and armour per hit taken, the stats that act on
/// an item's wear (a curse that makes it wear faster, the Indestructible affix), the
/// Engineer's repair price (base + per point missing, raised per item level, times the
/// rarity's repair multiplier), and the Repair Kit's recipe: Diablo 1's field repair, each
/// step losing max(maxDurability / (level + <see cref="LevelOffset"/>), <see cref="MinLoss"/>).
/// </summary>
public sealed record DurabilitySettings(
    IReadOnlyList<FamilyWear> Families,
    int ArmourHitsPerPoint,
    string RateStat,
    string IndestructibleStat,
    double RepairBase,
    double RepairPerPoint,
    double RepairPerItemLevel,
    string FieldRepairRecipe,
    string Kit,
    int LevelOffset,
    int MinLoss)
{
    /// <summary>The gear warning's line (the HUD's, UI2): an item at or below this percent of its maximum is low.</summary>
    public int WarnPercent { get; init; } = 25;

    /// <summary>Uses per point of a family; 0 for a family the table does not name (a load error).</summary>
    public int UsesPerPoint(string family) => Families.FirstOrDefault(f => f.Family == family)?.UsesPerPoint ?? 0;
}

public sealed class DurabilityTable : SingleTable<DurabilitySettings>
{
    public const string TableName = "durability";

    public override string Name => TableName;

    protected override DurabilitySettings Read(JsonFields f)
    {
        var wear = f.Obj("wear");
        var repair = f.Obj("repair");
        var field = f.Obj("fieldRepair");
        var warning = f.Obj("warning");
        var families = new List<FamilyWear>();
        foreach (var e in wear.Array("families"))
        {
            var w = new JsonFields(e, f.Table, f.Row, f.Errors);
            families.Add(new FamilyWear(w.Str("family"), w.Int("usesPerPoint")));
            w.End();
        }
        var s = new DurabilitySettings(families, wear.Int("armourHitsPerPoint"), wear.Str("rateStat"), wear.Str("indestructibleStat"),
            repair.Num("base"), repair.Num("perPoint"), repair.Num("perItemLevel"),
            field.Str("recipe"), field.Str("kit"), field.Int("levelOffset"), field.Int("minLoss"))
        { WarnPercent = warning.Int("pct") };
        warning.End();
        wear.End();
        repair.End();
        field.End();
        return s;
    }

    protected override void Validate(DescentContent content, DurabilitySettings s, ContentErrors errors)
    {
        string t = Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in s.Families)
        {
            if (!seen.Add(w.Family))
                errors.Add(t, w.Family, ContentRule.DuplicateId, $"family '{w.Family}' wears twice");
            ContentChecks.Reference<FamilyRow>(content, errors, t, w.Family, "wear.families", w.Family, ContentTables.Families);
            ContentChecks.Range(errors, t, w.Family, "usesPerPoint", w.UsesPerPoint, 1, 1_000_000);
        }
        if (content.TryGet<Table<FamilyRow>>(ContentTables.Families, out var families))
            foreach (var f in families.Rows.Where(f => !seen.Contains(f.Id)))
                errors.Add(t, f.Id, ContentRule.DanglingReference, $"family '{f.Id}' has no wear rate");
        ContentChecks.Range(errors, t, null, "wear.armourHitsPerPoint", s.ArmourHitsPerPoint, 1, 1_000_000);
        ContentChecks.Stat(content, errors, t, null, s.RateStat);
        ContentChecks.Stat(content, errors, t, null, s.IndestructibleStat);
        ContentChecks.Range(errors, t, null, "repair.base", s.RepairBase, 0, 1e9);
        ContentChecks.Range(errors, t, null, "repair.perPoint", s.RepairPerPoint, 1e-9, 1e9);
        ContentChecks.Range(errors, t, null, "repair.perItemLevel", s.RepairPerItemLevel, 0, 1e3);
        ContentChecks.Range(errors, t, null, "fieldRepair.levelOffset", s.LevelOffset, 0, 1000);
        ContentChecks.Range(errors, t, null, "fieldRepair.minLoss", s.MinLoss, 1, 1000);
        ContentChecks.Range(errors, t, null, "warning.pct", s.WarnPercent, 1, 99);
        if (ContentChecks.Reference<ItemBaseRow>(content, errors, t, null, "fieldRepair.kit", s.Kit, ItemTables.Bases)
            && content.ItemBases[s.Kit].Kind != ItemKinds.Consumable)
            errors.Add(t, null, ContentRule.DanglingReference, $"'fieldRepair.kit' names '{s.Kit}', which is not a consumable");
    }
}

/// <summary>How worn an item is, as the gear warning shows it (HE9, UI2).</summary>
public enum WearLevel
{
    /// <summary>Not gear: it does not wear.</summary>
    None,
    /// <summary>Above the warning line (or indestructible).</summary>
    Ok,
    /// <summary>At or below the warning line, not yet broken.</summary>
    Low,
    /// <summary>Durability 0: it grants nothing, and a weapon fires as its Stock base.</summary>
    Broken,
    /// <summary>Maximum durability 0 (L7 as amended): it cannot be equipped and grants nothing.</summary>
    Unusable,
}

/// <summary>What wear did: the item after, the points it lost, and whether it broke now (1 to 0).</summary>
public sealed record WearResult(ItemInstance Item, int PointsLost, bool BrokeNow);

/// <summary>
/// Wear (plan_tf2_rpg 2h, D21). A weapon loses a point per its family's uses-per-point, armour
/// a point per hits-per-point; a curse on the item's wear rate (+100 % wears twice as fast)
/// scales both. Progress toward the next point is kept on the item in hundredths of a use
/// (<see cref="ItemInstance.WearUses"/>), so no use is lost to rounding. Durability stops at 0:
/// the item is broken, stays where it is and grants nothing (<see cref="Equipment"/>); wear
/// never removes it. An Indestructible item (the affix, or a rarity that is indestructible:
/// Australium) never wears, nor does anything that is not gear.
/// </summary>
public sealed class Durability
{
    private const int Hundredths = 100;

    private readonly DescentContent _content;
    private readonly ItemRoller _roller;

    public Durability(DescentContent content, ItemRoller roller)
    {
        _content = content;
        _roller = roller;
    }

    private DurabilitySettings S => _content.DurabilitySettings;

    private double Stat(ItemInstance item, string stat) =>
        item.Affixes.Where(a => _content.Affixes[a.Id].Stat == stat && !_content.Affixes[a.Id].IsTF).Sum(a => _roller.Value(item, a))
        + _content.ItemBases[item.BaseId].Implicits.Where(s => s.Stat == stat).Sum(s => s.Amount);

    /// <summary>True when the item never wears: its rarity is indestructible, or it carries the Indestructible stat.</summary>
    public bool IsIndestructible(ItemInstance item) =>
        _content.Rarities.Rows[item.Rarity].Indestructible || Stat(item, S.IndestructibleStat) >= 1;

    /// <summary>The wear multiplier in percent: 100 + the item's wear-rate stat, never below 0.</summary>
    public int RatePercent(ItemInstance item) => Math.Max(0, (int)Math.Round(100 + Stat(item, S.RateStat), MidpointRounding.AwayFromZero));

    /// <summary>Uses (a weapon, by its family) or hits (armour) per point of durability; 0 for anything that does not wear.</summary>
    public int PerPoint(ItemInstance item)
    {
        var b = _content.ItemBases[item.BaseId];
        return b.Kind switch
        {
            ItemKinds.Weapon => S.UsesPerPoint(b.Family!),
            ItemKinds.Armour => S.ArmourHitsPerPoint,
            _ => 0,
        };
    }

    /// <summary>
    /// The item's wear level: unusable at maximum 0, broken at 0, low at or below the
    /// warning line's percent of its maximum, else ok; an indestructible item is never low.
    /// </summary>
    public WearLevel Level(ItemInstance item)
    {
        if (!_content.ItemBases[item.BaseId].IsGear) return WearLevel.None;
        if (item.MaxDurability <= 0) return WearLevel.Unusable;
        if (item.Durability <= 0) return WearLevel.Broken;
        if (IsIndestructible(item)) return WearLevel.Ok;
        return (long)item.Durability * 100 <= (long)item.MaxDurability * S.WarnPercent ? WearLevel.Low : WearLevel.Ok;
    }

    /// <summary>A weapon fired, swung or used <paramref name="uses"/> times. Armour does not wear by use.</summary>
    public WearResult Use(ItemInstance item, int uses) =>
        _content.ItemBases[item.BaseId].Kind == ItemKinds.Weapon ? Wear(item, uses) : new WearResult(item, 0, false);

    /// <summary>Armour struck <paramref name="hits"/> times. A weapon does not wear by hits taken.</summary>
    public WearResult Hit(ItemInstance item, int hits) =>
        _content.ItemBases[item.BaseId].Kind == ItemKinds.Armour ? Wear(item, hits) : new WearResult(item, 0, false);

    /// <summary>
    /// The events of one item's wear that count for it, as one number (the contract's
    /// <c>Wear</c> amount): a weapon's uses, armour's hits taken; 0 for anything else.
    /// </summary>
    public int Amount(ItemInstance item, int uses, int hits) => _content.ItemBases[item.BaseId].Kind switch
    {
        ItemKinds.Weapon => uses,
        ItemKinds.Armour => hits,
        _ => 0,
    };

    /// <summary>
    /// The item after <paramref name="uses"/> and <paramref name="hits"/> (each counts only where it
    /// wears the item: <see cref="Use"/>, then <see cref="Hit"/>), so a weapon's wear is
    /// <c>Worn(item, n, n)</c> as much as <c>Worn(item, n, 0)</c>.
    /// </summary>
    public ItemInstance Worn(ItemInstance item, int uses, int hits) => Hit(Use(item, uses).Item, hits).Item;

    private WearResult Wear(ItemInstance item, int events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(events);        int perPoint = PerPoint(item);
        if (events == 0 || perPoint <= 0 || item.Durability <= 0 || item.MaxDurability <= 0 || IsIndestructible(item))
            return new WearResult(item, 0, false);
        long progress = item.WearUses + (long)events * RatePercent(item);
        long per = (long)perPoint * Hundredths;
        int points = (int)Math.Min(progress / per, item.Durability);
        int durability = item.Durability - points;
        long left = durability == 0 ? 0 : progress - points * per;
        return new WearResult(item with { Durability = durability, WearUses = (int)left }, points, durability == 0);
    }
}
