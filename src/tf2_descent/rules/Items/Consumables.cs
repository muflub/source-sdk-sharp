using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Spells;

namespace TF2.Descent.Rules.Items;

/// <summary>What a potion restores (consumables.json's <c>restores</c>).</summary>
public static class PotionResources
{
    public const string Life = "life";
    public const string Mana = "mana";

    public static IReadOnlyList<string> All => [Life, Mana];
}

/// <summary>What a scroll does when used (consumables.json's <c>use</c>).</summary>
public static class ScrollUses
{
    /// <summary>Identifies one unidentified item (the Dossier: the economy's <c>identify</c> recipe).</summary>
    public const string Identify = "identify";

    /// <summary>Opens a town portal (L10: the Engineer teleporter, WO4).</summary>
    public const string TownPortal = "town_portal";

    public static IReadOnlyList<string> All => [Identify, TownPortal];
}

/// <summary>A potion base and what it restores; its size (a percent of the maximum) is the base's <c>restore</c>.</summary>
public sealed record PotionRow(string Base, string Restores);

/// <summary>A scroll base and its use.</summary>
public sealed record ScrollRow(string Base, string Use);

/// <summary>A tome base and the spell reading it teaches or raises (R27).</summary>
public sealed record TomeRow(string Base, string Spell);

/// <summary>
/// consumables.json (plan_descent_logic HE8; plan_tf2_rpg 2e, D4, D16, R27; A.5 Field
/// Rations): the belt's one shared cooldown in seconds and the stats that scale a potion's
/// restore and that cooldown, whether a potion is refused when what it restores is full,
/// whether an emptied belt slot refills from the backpack with the same potion, what each
/// potion restores, what each scroll does, and the spell each tome teaches.
/// </summary>
public sealed record ConsumableSettings(
    double BeltCooldown,
    string RestoreStat,
    string CooldownStat,
    bool RefuseAtFull,
    bool Refill,
    IReadOnlyList<PotionRow> Potions,
    IReadOnlyList<ScrollRow> Scrolls,
    IReadOnlyList<TomeRow> Tomes)
{
    public PotionRow? Potion(string baseId) => Potions.FirstOrDefault(p => p.Base == baseId);

    public ScrollRow? Scroll(string baseId) => Scrolls.FirstOrDefault(s => s.Base == baseId);

    public TomeRow? Tome(string baseId) => Tomes.FirstOrDefault(t => t.Base == baseId);
}

/// <summary>
/// The consumables table. Load errors: a potion row on a base that is no potion, a potion base
/// with no row, a scroll or tome on a base that is no consumable, an unknown use or resource,
/// a tome of no spell, a base named twice, an identify scroll that is not the economy's
/// Dossier (identify is its recipe), a cooldown out of range, an unknown stat.
/// </summary>
public sealed class ConsumableTable : SingleTable<ConsumableSettings>
{
    public const string TableName = "consumables";

    public override string Name => TableName;

    protected override ConsumableSettings Read(JsonFields f)
    {
        var belt = f.Obj("belt");
        var potions = new List<PotionRow>();
        foreach (var e in f.Array("potions"))
        {
            var p = new JsonFields(e, f.Table, f.Row, f.Errors);
            potions.Add(new PotionRow(p.Str("base"), p.Str("restores")));
            p.End();
        }
        var scrolls = new List<ScrollRow>();
        foreach (var e in f.Array("scrolls"))
        {
            var s = new JsonFields(e, f.Table, f.Row, f.Errors);
            scrolls.Add(new ScrollRow(s.Str("base"), s.Str("use")));
            s.End();
        }
        var tomes = new List<TomeRow>();
        foreach (var e in f.Array("tomes"))
        {
            var t = new JsonFields(e, f.Table, f.Row, f.Errors);
            tomes.Add(new TomeRow(t.Str("base"), t.Str("spell")));
            t.End();
        }
        var settings = new ConsumableSettings(belt.Num("cooldown"), belt.Str("restoreStat"), belt.Str("cooldownStat"),
            belt.Bool("refuseAtFull"), belt.Bool("refill"), potions, scrolls, tomes);
        belt.End();
        return settings;
    }

    protected override void Validate(DescentContent content, ConsumableSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "belt.cooldown", s.BeltCooldown, 0, 600);
        ContentChecks.Stat(content, errors, t, null, s.RestoreStat);
        ContentChecks.Stat(content, errors, t, null, s.CooldownStat);

        content.TryGet<Table<ItemBaseRow>>(ItemTables.Bases, out var bases);
        var named = new HashSet<string>(StringComparer.Ordinal);

        bool Base(string field, string id, string kind)
        {
            if (!named.Add(id))
            {
                errors.Add(t, id, ContentRule.DuplicateId, $"base '{id}' is named twice");
                return false;
            }
            if (!ContentChecks.Reference<ItemBaseRow>(content, errors, t, id, field, id, ItemTables.Bases) || bases is null)
                return false;
            if (bases[id].Kind == kind)
                return true;
            errors.Add(t, id, ContentRule.OutOfRange, $"'{field}' names '{id}', which is no {kind}");
            return false;
        }

        foreach (var p in s.Potions)
        {
            Base("potions.base", p.Base, ItemKinds.Potion);
            ItemTables.OneOf(errors, t, p.Base, "potions.restores", p.Restores, PotionResources.All);
        }
        if (bases is not null)
            foreach (var b in bases.Rows.Where(b => b.Kind == ItemKinds.Potion && s.Potion(b.Id) is null))
                errors.Add(t, b.Id, ContentRule.DanglingReference, $"potion '{b.Id}' restores nothing: it has no row");

        foreach (var sc in s.Scrolls)
        {
            Base("scrolls.base", sc.Base, ItemKinds.Consumable);
            if (ItemTables.OneOf(errors, t, sc.Base, "scrolls.use", sc.Use, ScrollUses.All)
                && sc.Use == ScrollUses.Identify
                && content.TryGet<EconomySettings>(EconomyTable.TableName, out var economy) && economy.Dossier != sc.Base)
                errors.Add(t, sc.Base, ContentRule.OutOfRange, $"an identify scroll is the economy's Dossier ('{economy.Dossier}'), not '{sc.Base}'");
        }

        foreach (var tome in s.Tomes)
        {
            Base("tomes.base", tome.Base, ItemKinds.Consumable);
            ContentChecks.Reference<SpellDef>(content, errors, t, tome.Base, "tomes.spell", tome.Spell, ContentTables.Spells);
        }
    }
}
