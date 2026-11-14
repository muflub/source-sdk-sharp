using System.Text.Json;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Items;

/// <summary>The item tables' names, the vocabularies their rows draw from, and shared readers.</summary>
public static class ItemTables
{
    public const string TFAttributes = "tf_attributes";
    public const string Rarities = "rarities";
    public const string Bases = "item_bases";
    public const string Affixes = "affixes";
    public const string UnusualEffects = "unusual_effects";
    public const string Uniques = "uniques";
    public const string Items = "items";

    /// <summary>What a base is: gear (weapon, armour) is rolled with affixes and wears; the rest are not.</summary>
    public static IReadOnlyList<string> Kinds => [ItemKinds.Weapon, ItemKinds.Armour, ItemKinds.Potion, ItemKinds.Metal, ItemKinds.Consumable, ItemKinds.Crate];

    /// <summary>The slots armour is worn in (D4, R29): hat, body, feet, a trinket (two equipment slots take one) and the amulet.</summary>
    public static IReadOnlyList<string> ArmourSlots => ["hat", "body", "feet", "trinket", "amulet"];

    /// <summary>Every slot a gear affix may name: TF's three weapon slots and the armour slots.</summary>
    public static IReadOnlyList<string> GearSlots => [.. FamilyTable.SlotKinds, .. ArmourSlots];

    /// <summary>The slot of a potion (the belt) and of anything that is only carried.</summary>
    public const string BeltSlot = "belt";
    public const string NoSlot = "none";

    /// <summary>A list of strings; a non-string element is malformed.</summary>
    internal static IReadOnlyList<string> Strings(JsonFields f, string field, bool required = true)
    {
        var list = new List<string>();
        foreach (var e in f.Array(field, required))
        {
            if (e.ValueKind == JsonValueKind.String)
                list.Add(e.GetString()!);
            else
                f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}' must hold only strings");
        }
        return list;
    }

    /// <summary><paramref name="value"/> must be one of <paramref name="allowed"/>; else <see cref="ContentRule.OutOfRange"/>.</summary>
    internal static bool OneOf(ContentErrors errors, string table, string? row, string field, string value, IReadOnlyList<string> allowed)
    {
        if (allowed.Contains(value, StringComparer.Ordinal)) return true;
        errors.Add(table, row, ContentRule.OutOfRange, $"'{field}' is '{value}', not one of {string.Join(", ", allowed)}");
        return false;
    }
}

/// <summary>The base kinds (<see cref="ItemBaseRow.Kind"/>).</summary>
public static class ItemKinds
{
    public const string Weapon = "weapon";
    public const string Armour = "armour";
    public const string Potion = "potion";
    public const string Metal = "metal";
    public const string Consumable = "consumable";
    public const string Crate = "crate";
}

//-----------------------------------------------------------------------------
// tf_attributes.json
//-----------------------------------------------------------------------------

/// <summary>
/// tf_attributes.json: the TF attributes an affix may grant, by their attribute_class in TF's
/// item schema (items_game.txt), with the bounds a rolled value must stay inside and the
/// value that changes nothing. An affix of kind "tf" names one; the server hands the
/// attribute and its value to TF's attribute system unchanged.
/// </summary>
public sealed record TFAttributeRow(string Id, double Min, double Max, double Neutral) : IContentRow;

public sealed class TFAttributeTable : RowTable<TFAttributeRow>
{
    public override string Name => ItemTables.TFAttributes;

    protected override TFAttributeRow ReadRow(JsonFields f) => new(f.Str("id"), f.Num("min"), f.Num("max"), f.Num("neutral"));

    protected override void ValidateRow(DescentContent content, TFAttributeRow row, ContentErrors errors)
    {
        if (ContentChecks.Roll(errors, Name, row.Id, "min..max", row.Min, row.Max))
            ContentChecks.Range(errors, Name, row.Id, "neutral", row.Neutral, row.Min, row.Max);
    }
}

//-----------------------------------------------------------------------------
// rarities.json
//-----------------------------------------------------------------------------

/// <summary>
/// rarities.json: the rarity ladder (D5), in tier order: an item's rarity is its row's index,
/// and <c>IGameRules.Tiers</c> is the names in this order. Each says how many positive affixes
/// it rolls, whether it drops identified (D22), may carry cursed stats (R28), carries an
/// unusual effect, is hand-authored (Australium) or indestructible (D21), whether a recipe
/// may output it (D24), its base chance of becoming Unusual (2b), and its multipliers for
/// vendor value, repair price and salvage yield (scrap units), and the metal it costs to
/// raise it one step (0: it cannot be raised).
/// </summary>
public sealed record RarityRow(
    string Id,
    string Name,
    int MinAffixes,
    int MaxAffixes,
    bool Identified,
    bool Curses,
    bool Unusual,
    double UnusualBase,
    bool Unique,
    bool Indestructible,
    bool Craftable,
    double Value,
    double Repair,
    int Salvage,
    int UpgradeCost) : IContentRow;

public sealed class RarityTable : RowTable<RarityRow>
{
    public override string Name => ItemTables.Rarities;

    protected override RarityRow ReadRow(JsonFields f)
    {
        var n = f.Obj("affixes");
        var row = new RarityRow(f.Str("id"), f.Str("name"), n.Int("min"), n.Int("max"), f.Bool("identified"), f.Bool("curses"),
            f.Bool("unusual"), f.Num("unusualBase"), f.Bool("unique"), f.Bool("indestructible"), f.Bool("craftable"),
            f.Num("value"), f.Num("repair"), f.Int("salvage"), f.Int("upgradeCost"));
        n.End();
        return row;
    }

    protected override void ValidateRow(DescentContent content, RarityRow row, ContentErrors errors)
    {
        if (ContentChecks.Range(errors, Name, row.Id, "affixes.min", row.MinAffixes, 0, 12))
            ContentChecks.Range(errors, Name, row.Id, "affixes.max", row.MaxAffixes, row.MinAffixes, 12);
        ContentChecks.Range(errors, Name, row.Id, "unusualBase", row.UnusualBase, 0, 1);
        ContentChecks.Range(errors, Name, row.Id, "value", row.Value, 0, 1e6);
        ContentChecks.Range(errors, Name, row.Id, "repair", row.Repair, 0, 1e6);
        ContentChecks.Range(errors, Name, row.Id, "salvage", row.Salvage, 0, 1e6);
        ContentChecks.Range(errors, Name, row.Id, "upgradeCost", row.UpgradeCost, 0, 1e6);
    }

    protected override void ValidateTable(DescentContent content, Table<RarityRow> table, ContentErrors errors)
    {
        if (table.Count == 0)
            errors.Add(Name, null, ContentRule.Malformed, "no rarity");
        for (int i = 0; i < table.Count; i++)
        {
            var r = table.Rows[i];
            if (r.UpgradeCost <= 0) continue;
            if (i + 1 >= table.Count || !table.Rows[i + 1].Craftable)
                errors.Add(Name, r.Id, ContentRule.OutOfRange,
                    $"'upgradeCost' raises '{r.Id}' to a rarity no recipe may output");
        }
    }
}

//-----------------------------------------------------------------------------
// item_bases.json
//-----------------------------------------------------------------------------

/// <summary>"<paramref name="Attribute"/> of at least <paramref name="Min"/>, read against Now" (L2, C.3).</summary>
public sealed record Requirement(string Attribute, int Min);

/// <summary>
/// item_bases.json: what an item is before it is rolled (plan_tf2_rpg 2b; R12, R29; D4, D16,
/// D23, D24). A weapon names its TF item row (<see cref="TFItem"/>, items_working.tsv's name
/// column) and its family; armour has no TF row (the table covers weapon slots only). Gear
/// carries a class restriction (empty: every class), a level requirement, attribute
/// requirements read against Now, implicit stats, its maximum durability and a drop weight.
/// Potions restore a percent of life or mana and name the next size; metal is worth its
/// scrap units; a crate names the bases it may hold; anything may stack.
/// </summary>
public sealed record ItemBaseRow(
    string Id,
    string Name,
    string Kind,
    string? TFItem,
    string? Family,
    string Slot,
    IReadOnlyList<string> Classes,
    int Level,
    IReadOnlyList<Requirement> Requires,
    IReadOnlyList<StatAmount> Implicits,
    int Durability,
    int DropWeight,
    int Stack,
    long Value,
    double Restore,
    string? Next,
    int Metal,
    IReadOnlyList<string> Contains) : IItemBaseRow
{
    /// <summary>Weapons and armour: rolled with affixes, worn and repaired, equipped.</summary>
    public bool IsGear => Kind is ItemKinds.Weapon or ItemKinds.Armour;

    /// <summary>True when a hero of <paramref name="classId"/> may use it (no restriction, or the class is listed).</summary>
    public bool AllowsClass(string classId) =>
        Classes.Count == 0 || Classes.Contains(classId, StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemBaseTable : RowTable<ItemBaseRow>
{
    public override string Name => ItemTables.Bases;

    protected override ItemBaseRow ReadRow(JsonFields f)
    {
        var requires = new List<Requirement>();
        foreach (var e in f.Array("requires", required: false))
        {
            var r = new JsonFields(e, f.Table, f.Row, f.Errors);
            requires.Add(new Requirement(r.Str("attribute"), r.Int("min")));
            r.End();
        }
        var implicits = f.Array("implicits", required: false).Select(e => StatAmount.Read(e, f.Table, f.Row, f.Errors)).ToList();
        return new ItemBaseRow(
            f.Str("id"), f.Str("name"), f.Str("kind"), f.StrOr("tfItem", null), f.StrOr("family", null), f.Str("slot"),
            ItemTables.Strings(f, "classes", required: false), f.Int("level"), requires, implicits,
            f.IntOr("durability", 0), f.IntOr("dropWeight", 0), f.IntOr("stack", 1), f.Long("value"),
            f.NumOr("restore", 0), f.StrOr("next", null), f.IntOr("metal", 0), ItemTables.Strings(f, "contains", required: false));
    }

    protected override void ValidateRow(DescentContent content, ItemBaseRow row, ContentErrors errors)
    {
        string t = Name;
        if (!ItemTables.OneOf(errors, t, row.Id, "kind", row.Kind, ItemTables.Kinds)) return;
        int maxLevel = content.TryGet<ItemSettings>(ItemTables.Items, out var items) ? items.MaxItemLevel : 1000;
        ContentChecks.Range(errors, t, row.Id, "level", row.Level, 1, maxLevel);
        ContentChecks.Range(errors, t, row.Id, "dropWeight", row.DropWeight, 0, 1e6);
        ContentChecks.Range(errors, t, row.Id, "stack", row.Stack, 1, 1000);
        ContentChecks.Range(errors, t, row.Id, "value", row.Value, 0, 1e9);
        foreach (var c in row.Classes)
            ContentChecks.Reference<ClassRow>(content, errors, t, row.Id, "classes", c, ContentTables.Classes);
        foreach (var r in row.Requires)
        {
            ContentChecks.Reference<AttributeRow>(content, errors, t, row.Id, "requires.attribute", r.Attribute, ContentTables.Attributes);
            ContentChecks.Range(errors, t, row.Id, "requires.min", r.Min, 0, 1000);
        }
        foreach (var s in row.Implicits)
        {
            ContentChecks.Stat(content, errors, t, row.Id, s.Stat, s.Amount);
            // Requirements resolve to a fixed point by only ever adding attributes (Equipment).
            if (s.Amount < 0 && content.TryGet<Table<AttributeRow>>(ContentTables.Attributes, out var attrs) && attrs.Contains(s.Stat))
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"implicit '{s.Stat}' lowers an attribute");
        }

        if (row.IsGear)
        {
            ContentChecks.Range(errors, t, row.Id, "durability", row.Durability, 1, 10000);
            if (row.Stack != 1)
                errors.Add(t, row.Id, ContentRule.OutOfRange, "gear does not stack");
        }
        else if (row.Durability != 0)
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"'durability' on a {row.Kind}, which does not wear");

        switch (row.Kind)
        {
            case ItemKinds.Weapon:
                if (row.TFItem is null)
                    errors.Add(t, row.Id, ContentRule.Malformed, "a weapon names its TF item row in 'tfItem'");
                if (row.Family is null)
                    errors.Add(t, row.Id, ContentRule.Malformed, "a weapon names its 'family'");
                else if (ContentChecks.Reference<FamilyRow>(content, errors, t, row.Id, "family", row.Family, ContentTables.Families)
                         && content.Families[row.Family] is { Slots.Count: > 0 } family
                         && family.Slots.All(s => FamilyTable.SlotKinds.Contains(s))   // else the family's own error stands
                         && family.Slots.All(s => s != row.Slot))
                    errors.Add(t, row.Id, ContentRule.OutOfRange, $"'slot' is '{row.Slot}', which family '{row.Family}' is not held in");
                break;
            case ItemKinds.Armour:
                ItemTables.OneOf(errors, t, row.Id, "slot", row.Slot, ItemTables.ArmourSlots);
                break;
            case ItemKinds.Potion:
                ItemTables.OneOf(errors, t, row.Id, "slot", row.Slot, [ItemTables.BeltSlot]);
                ContentChecks.Range(errors, t, row.Id, "restore", row.Restore, 1, 100);
                if (row.Next is { } next && ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "next", next, ItemTables.Bases)
                    && content.ItemBases[next].Kind != ItemKinds.Potion)
                    errors.Add(t, row.Id, ContentRule.DanglingReference, $"'next' names '{next}', which is not a potion");
                break;
            case ItemKinds.Metal:
                ContentChecks.Range(errors, t, row.Id, "metal", row.Metal, 1, 1000);
                break;
            case ItemKinds.Crate:
                if (row.Contains.Count == 0)
                    errors.Add(t, row.Id, ContentRule.Malformed, "a crate 'contains' no base");
                foreach (var b in row.Contains)
                    if (ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "contains", b, ItemTables.Bases)
                        && !content.ItemBases[b].IsGear)
                        errors.Add(t, row.Id, ContentRule.DanglingReference, $"'contains' names '{b}', which is not gear");
                break;
        }
        if (row.Kind != ItemKinds.Weapon && row.Family is not null)
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"'family' on a {row.Kind}");
        if (row.Kind is not (ItemKinds.Weapon or ItemKinds.Armour) && row.Kind != ItemKinds.Potion
            && row.Slot != ItemTables.NoSlot)
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"a {row.Kind} is carried, so its 'slot' is '{ItemTables.NoSlot}'");
    }
}

//-----------------------------------------------------------------------------
// affixes.json
//-----------------------------------------------------------------------------

/// <summary>The affix pools: prefixes and suffixes are positive; the cursed pool sits beside them (R28).</summary>
public static class AffixPools
{
    public const string Prefix = "prefix";
    public const string Suffix = "suffix";
    public const string Cursed = "cursed";

    public static IReadOnlyList<string> All => [Prefix, Suffix, Cursed];
}

/// <summary>What an affix's stat is: a TF attribute (tf_attributes.json) or a Descent stat (stats.json).</summary>
public static class AffixKinds
{
    public const string TF = "tf";
    public const string Descent = "descent";

    public static IReadOnlyList<string> All => [TF, Descent];
}

/// <summary>
/// affixes.json: the affix pools (plan_tf2_rpg 2b; R28). An affix grants one stat, either a TF
/// attribute TF already implements (kind "tf": the server hands it to TF's attribute system)
/// or a Descent stat the hero sheet reads (kind "descent"). Its value rolls from
/// <see cref="From"/> (the weakest) to <see cref="To"/> (the strongest), rounded to
/// <see cref="Step"/>. It rolls only at item level <see cref="MinLevel"/> and above, on the
/// slots and families it names, with drop weight <see cref="Weight"/>. A positive affix adds
/// <see cref="Power"/> × its roll to the item's power; a cursed one has no power of its own
/// and instead buys <see cref="Budget"/>: it raises the roll ceiling of the item's positive
/// affixes by that fraction of their range.
/// </summary>
public sealed record AffixRow(
    string Id,
    string Name,
    string Pool,
    string Kind,
    string Stat,
    double From,
    double To,
    double Step,
    int MinLevel,
    int Weight,
    double Power,
    double Budget,
    IReadOnlyList<string> Slots,
    IReadOnlyList<string> Families) : IContentRow
{
    public bool IsCursed => Pool == AffixPools.Cursed;

    public bool IsTF => Kind == AffixKinds.TF;

    /// <summary>True when it may roll on <paramref name="itemBase"/> at <paramref name="itemLevel"/>.</summary>
    public bool Fits(ItemBaseRow itemBase, int itemLevel) =>
        MinLevel <= itemLevel && Slots.Contains(itemBase.Slot, StringComparer.Ordinal)
        && (Families.Count == 0 || (itemBase.Family is { } f && Families.Contains(f, StringComparer.Ordinal)));
}

public sealed class AffixTable : RowTable<AffixRow>
{
    public override string Name => ItemTables.Affixes;

    protected override AffixRow ReadRow(JsonFields f) =>
        new(f.Str("id"), f.Str("name"), f.Str("pool"), f.Str("kind"), f.Str("stat"), f.Num("from"), f.Num("to"), f.Num("step"),
            f.Int("minLevel"), f.Int("weight"), f.NumOr("power", 0), f.NumOr("budget", 0),
            ItemTables.Strings(f, "slots"), ItemTables.Strings(f, "families", required: false));

    /// <summary>The bounds an affix's value must stay inside: its stat's, or its TF attribute's.</summary>
    internal static bool Bounds(DescentContent content, AffixRow row, out double min, out double max)
    {
        min = max = 0;
        if (row.IsTF)
        {
            if (!content.TryGet<Table<TFAttributeRow>>(ItemTables.TFAttributes, out var tf) || !tf.TryGet(row.Stat, out var a)) return false;
            (min, max) = (a.Min, a.Max);
            return min <= max;   // a reversed range is the vocabulary row's own error: no cascade
        }
        if (!content.TryGet<Table<StatRow>>(ContentTables.Stats, out var stats) || !stats.TryGet(row.Stat, out var s)) return false;
        (min, max) = (s.Min, s.Max);
        return min <= max;
    }

    /// <summary>An attribute is a stat by its own row (attributes.json checks that it is one): naming it is never an unknown stat here.</summary>
    private static bool IsAttribute(DescentContent content, string stat) =>
        content.TryGet<Table<AttributeRow>>(ContentTables.Attributes, out var attrs) && attrs.Contains(stat);

    protected override void ValidateRow(DescentContent content, AffixRow row, ContentErrors errors)
    {
        string t = Name;
        bool pool = ItemTables.OneOf(errors, t, row.Id, "pool", row.Pool, AffixPools.All);
        if (ItemTables.OneOf(errors, t, row.Id, "kind", row.Kind, AffixKinds.All))
        {
            if (row.IsTF)
                ContentChecks.Reference<TFAttributeRow>(content, errors, t, row.Id, "stat", row.Stat, ItemTables.TFAttributes);
            else if (!IsAttribute(content, row.Stat))
                ContentChecks.Stat(content, errors, t, row.Id, row.Stat);
        }
        if (Bounds(content, row, out var min, out var max))
        {
            ContentChecks.Range(errors, t, row.Id, "from", row.From, min, max);
            ContentChecks.Range(errors, t, row.Id, "to", row.To, min, max);
        }
        ContentChecks.Range(errors, t, row.Id, "step", row.Step, 1e-6, 1e6);
        int maxLevel = content.TryGet<ItemSettings>(ItemTables.Items, out var items) ? items.MaxItemLevel : 1000;
        ContentChecks.Range(errors, t, row.Id, "minLevel", row.MinLevel, 1, maxLevel);
        ContentChecks.Range(errors, t, row.Id, "weight", row.Weight, 1, 1e6);
        if (row.Slots.Count == 0)
            errors.Add(t, row.Id, ContentRule.Malformed, "'slots' names no slot");
        foreach (var s in row.Slots)
            ItemTables.OneOf(errors, t, row.Id, "slots", s, ItemTables.GearSlots);
        foreach (var fam in row.Families)
            ContentChecks.Reference<FamilyRow>(content, errors, t, row.Id, "families", fam, ContentTables.Families);

        if (!pool) return;
        bool attribute = !row.IsTF && IsAttribute(content, row.Stat);
        if (row.IsCursed)
        {
            ContentChecks.Range(errors, t, row.Id, "budget", row.Budget, 1e-6, 1);
            ContentChecks.Range(errors, t, row.Id, "power", row.Power, 0, 0);
            // Requirements resolve to a fixed point by only ever adding attributes (Equipment).
            if (attribute)
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"a cursed affix lowers the attribute '{row.Stat}'");
        }
        else
        {
            ContentChecks.Range(errors, t, row.Id, "budget", row.Budget, 0, 0);
            ContentChecks.Range(errors, t, row.Id, "power", row.Power, 1e-6, 1e3);
            if (attribute && (row.From < 0 || row.To < 0))
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"a positive affix lowers the attribute '{row.Stat}'");
        }
    }

    /// <summary>
    /// A positive affix's value may roll past <see cref="AffixRow.To"/> on a cursed item: up to
    /// From + (1 + B)·(To − From), where B is the most budget the cursed pool can buy. That
    /// value must stay inside the stat's bounds too.
    /// </summary>
    protected override void ValidateTable(DescentContent content, Table<AffixRow> table, ContentErrors errors)
    {
        if (!content.TryGet<ItemSettings>(ItemTables.Items, out var items)) return;
        double most = 1 + MostBudget(table.Rows, items.Curses.MaxCount);
        foreach (var row in table.Rows.Where(r => !r.IsCursed))
            if (Bounds(content, row, out var min, out var max)
                && row.From >= min && row.From <= max && row.To >= min && row.To <= max)   // else the row's own error stands
                ContentChecks.Range(errors, Name, row.Id, "to on a cursed item", row.From + most * (row.To - row.From), min, max);
    }

    /// <summary>The largest budget <paramref name="count"/> cursed affixes can buy together.</summary>
    public static double MostBudget(IEnumerable<AffixRow> rows, int count) =>
        rows.Where(r => r.IsCursed).Select(r => r.Budget).OrderDescending().Take(count).Sum();
}

//-----------------------------------------------------------------------------
// unusual_effects.json
//-----------------------------------------------------------------------------

/// <summary>
/// unusual_effects.json: the effects an Unusual item carries (plan_tf2_rpg 2b). Each is a
/// gameplay proc (an id the server binds, its chance per trigger and its amount) whose visual
/// is a TF unusual particle system, by its name in TF's item schema.
/// </summary>
public sealed record UnusualEffectRow(string Id, string Name, string Particle, int Weight, string Proc, double ProcChance, double ProcAmount) : IContentRow;

public sealed class UnusualEffectTable : RowTable<UnusualEffectRow>
{
    public override string Name => ItemTables.UnusualEffects;

    protected override UnusualEffectRow ReadRow(JsonFields f)
    {
        var p = f.Obj("proc");
        var row = new UnusualEffectRow(f.Str("id"), f.Str("name"), f.Str("particle"), f.Int("weight"), p.Str("id"), p.Num("chance"), p.Num("amount"));
        p.End();
        return row;
    }

    protected override void ValidateRow(DescentContent content, UnusualEffectRow row, ContentErrors errors)
    {
        ContentChecks.Range(errors, Name, row.Id, "weight", row.Weight, 1, 1e6);
        ContentChecks.Range(errors, Name, row.Id, "proc.chance", row.ProcChance, 1e-6, 1);
        ContentChecks.Range(errors, Name, row.Id, "proc.amount", row.ProcAmount, 0, 1e6);
        if (row.Particle.Length == 0 || row.Proc.Length == 0)
            errors.Add(Name, row.Id, ContentRule.Malformed, "an effect names its particle and its proc");
    }

    protected override void ValidateTable(DescentContent content, Table<UnusualEffectRow> table, ContentErrors errors)
    {
        if (table.Count == 0)
            errors.Add(Name, null, ContentRule.Malformed, "no unusual effect");
    }
}

//-----------------------------------------------------------------------------
// uniques.json
//-----------------------------------------------------------------------------

/// <summary>A fixed affix of a hand-authored unique: the affix and where in its range it sits (0..1).</summary>
public sealed record UniqueAffix(string Affix, double Roll);

/// <summary>
/// uniques.json: the hand-authored Australium items (D5). Each names its base, the item level
/// it drops from, a drop weight, its fixed affixes and, optionally, an unusual effect.
/// </summary>
public sealed record UniqueRow(string Id, string Name, string Base, int Level, int Weight, IReadOnlyList<UniqueAffix> Affixes, string? Effect) : IContentRow;

public sealed class UniqueTable : RowTable<UniqueRow>
{
    public override string Name => ItemTables.Uniques;

    protected override UniqueRow ReadRow(JsonFields f)
    {
        var affixes = new List<UniqueAffix>();
        foreach (var e in f.Array("affixes"))
        {
            var a = new JsonFields(e, f.Table, f.Row, f.Errors);
            affixes.Add(new UniqueAffix(a.Str("affix"), a.Num("roll")));
            a.End();
        }
        return new UniqueRow(f.Str("id"), f.Str("name"), f.Str("base"), f.Int("level"), f.Int("weight"), affixes, f.StrOr("effect", null));
    }

    protected override void ValidateRow(DescentContent content, UniqueRow row, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, row.Id, "weight", row.Weight, 1, 1e6);
        if (row.Effect is { } effect)
            ContentChecks.Reference<UnusualEffectRow>(content, errors, t, row.Id, "effect", effect, ItemTables.UnusualEffects);
        if (!ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "base", row.Base, ItemTables.Bases)) return;
        var b = content.ItemBases[row.Base];
        if (!b.IsGear)
            errors.Add(t, row.Id, ContentRule.DanglingReference, $"'base' names '{row.Base}', which is not gear");
        ContentChecks.Range(errors, t, row.Id, "level", row.Level, b.Level, 1000);
        if (row.Affixes.Count == 0)
            errors.Add(t, row.Id, ContentRule.Malformed, "a unique has no affix");
        var stats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ua in row.Affixes)
        {
            ContentChecks.Range(errors, t, row.Id, ua.Affix + ".roll", ua.Roll, 0, 1);
            if (!ContentChecks.Reference<AffixRow>(content, errors, t, row.Id, "affixes", ua.Affix, ItemTables.Affixes)) continue;
            var a = content.Affixes[ua.Affix];
            if (a.IsCursed)
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'{ua.Affix}' is cursed");
            if (!a.Fits(b, row.Level))
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'{ua.Affix}' does not roll on '{row.Base}' at level {row.Level}");
            if (!stats.Add(a.Kind + ":" + a.Stat))
                errors.Add(t, row.Id, ContentRule.DuplicateId, $"two affixes grant '{a.Stat}'");
        }
    }
}

//-----------------------------------------------------------------------------
// items.json
//-----------------------------------------------------------------------------

/// <summary>How many cursed stats an item rolls (R28): the chance of the first rises with depth to a cap; each further one is that chance times <see cref="SecondChance"/>.</summary>
public sealed record CurseSettings(double Chance, double ChancePerDepth, double ChanceMax, int MaxCount, double SecondChance)
{
    /// <summary>The chance an item rolled at <paramref name="depth"/> carries its first cursed stat.</summary>
    public double ChanceAt(int depth) => Math.Min(ChanceMax, Chance + ChancePerDepth * Math.Max(0, depth));
}

/// <summary>
/// How a roll's source changes it: whether the item arrives identified (else its rarity
/// decides), whether it may become Unusual by its power, and whether it may roll curses.
/// A context whose <c>Source</c> starts with <see cref="Prefix"/> uses this rule; any other
/// source is a drop.
/// </summary>
public sealed record SourceRule(string Prefix, bool? Identified, bool Promote, bool Curses);

/// <summary>
/// items.json: the numbers of the roll (plan_tf2_rpg 2b, 2i; R28). The item level cap, the
/// roll's resolution (a rolled affix holds an integer 0..<see cref="RollScale"/>), the loot
/// table prefix that names a base outright, the kinds a drop picks from, the Descent stats
/// that act on the item itself (its wear) rather than on the hero, the share of an
/// item's power that its affixes make (the rest is its level), the curse chances, and the
/// source rules.
/// </summary>
public sealed record ItemSettings(
    int MaxItemLevel,
    int RollScale,
    string ForcedBasePrefix,
    IReadOnlyList<string> DropKinds,
    IReadOnlyList<string> ItemLocalStats,
    double AffixShare,
    CurseSettings Curses,
    IReadOnlyList<SourceRule> Sources)
{
    /// <summary>The rule for a roll's source: the first whose prefix it starts with, else a drop's.</summary>
    public SourceRule SourceOf(string source) =>
        Sources.FirstOrDefault(s => source.StartsWith(s.Prefix, StringComparison.Ordinal)) ?? new SourceRule("", null, true, true);
}

public sealed class ItemSettingsTable : SingleTable<ItemSettings>
{
    public override string Name => ItemTables.Items;

    protected override ItemSettings Read(JsonFields f)
    {
        var power = f.Obj("power");
        var curses = f.Obj("curses");
        var sources = new List<SourceRule>();
        foreach (var e in f.Array("sources"))
        {
            var s = new JsonFields(e, f.Table, f.Row, f.Errors);
            sources.Add(new SourceRule(s.Str("prefix"), s.Bool("identified"), s.Bool("promote"), s.Bool("curses")));
            s.End();
        }
        var settings = new ItemSettings(f.Int("maxItemLevel"), f.Int("rollScale"), f.Str("forcedBasePrefix"),
            ItemTables.Strings(f, "dropKinds"), ItemTables.Strings(f, "itemLocalStats"), power.Num("affixShare"),
            new CurseSettings(curses.Num("chance"), curses.Num("chancePerDepth"), curses.Num("chanceMax"),
                curses.Int("maxCount"), curses.Num("secondChance")),
            sources);
        power.End();
        curses.End();
        return settings;
    }

    protected override void Validate(DescentContent content, ItemSettings s, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Range(errors, t, null, "maxItemLevel", s.MaxItemLevel, 1, 1000);
        ContentChecks.Range(errors, t, null, "rollScale", s.RollScale, 1, 1_000_000);
        ContentChecks.Range(errors, t, null, "power.affixShare", s.AffixShare, 0, 1);
        ContentChecks.Range(errors, t, null, "curses.chance", s.Curses.Chance, 0, 1);
        ContentChecks.Range(errors, t, null, "curses.chancePerDepth", s.Curses.ChancePerDepth, 0, 1);
        ContentChecks.Range(errors, t, null, "curses.chanceMax", s.Curses.ChanceMax, 0, 1);
        ContentChecks.Range(errors, t, null, "curses.maxCount", s.Curses.MaxCount, 0, 6);
        ContentChecks.Range(errors, t, null, "curses.secondChance", s.Curses.SecondChance, 0, 1);
        if (s.ForcedBasePrefix.Length == 0)
            errors.Add(t, null, ContentRule.Malformed, "'forcedBasePrefix' is empty");
        foreach (var k in s.DropKinds)
            ItemTables.OneOf(errors, t, null, "dropKinds", k, ItemTables.Kinds);
        foreach (var stat in s.ItemLocalStats)
            ContentChecks.Stat(content, errors, t, null, stat);
        if (s.Sources.Any(r => r.Prefix.Length == 0))
            errors.Add(t, null, ContentRule.Malformed, "a source rule has an empty prefix");
    }
}
