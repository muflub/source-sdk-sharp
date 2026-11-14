namespace TF2.Descent.Rules.Content;

/// <summary>
/// damage_types.json: the four damage types (plan_tf2_rpg 2f, R25): fire, blast, bullet
/// (all kinetic damage: bullets and melee) and shock. Each names the hero stat that resists
/// it, which must be one of hero.json's resistances.
/// </summary>
public sealed record DamageTypeRow(string Id, string Name, string ResistStat) : IContentRow;

public sealed class DamageTypeTable : RowTable<DamageTypeRow>
{
    public override string Name => ContentTables.DamageTypes;

    protected override DamageTypeRow ReadRow(JsonFields f) => new(f.Str("id"), f.Str("name"), f.Str("resistStat"));

    protected override void ValidateRow(DescentContent content, DamageTypeRow row, ContentErrors errors)
    {
        if (!ContentChecks.Stat(content, errors, Name, row.Id, row.ResistStat)) return;
        if (content.TryGet<HeroSettings>(ContentTables.Hero, out var hero)
            && hero.Resistances.All(r => r.Stat != row.ResistStat))
            errors.Add(Name, row.Id, ContentRule.DanglingReference,
                $"'resistStat' names '{row.ResistStat}', which is not one of the hero's resistances");
    }
}

/// <summary>
/// families.json: the weapon families (plan_tf2_rpg 2b, 2e, 2f; R12, R17, R25). Every item
/// base carries one, an upgrade may name one, and each declares exactly one damage type.
/// <see cref="ItemClasses"/> are the TF item classes (items_working.tsv's item_class) that
/// belong to the family; no item class is under two families. <see cref="Slots"/> are the
/// loadout slots its weapons are held in: <see cref="FamilyTable.SlotKinds"/>.
/// </summary>
public sealed record FamilyRow(
    string Id,
    string Name,
    IReadOnlyList<string> Slots,
    string DamageType,
    IReadOnlyList<string> ItemClasses) : IContentRow;

public sealed class FamilyTable : RowTable<FamilyRow>
{
    /// <summary>The slot kinds a weapon is held in: TF's three weapon loadout slots.</summary>
    public static IReadOnlyList<string> SlotKinds => ["primary", "secondary", "melee"];

    public override string Name => ContentTables.Families;

    protected override FamilyRow ReadRow(JsonFields f) =>
        new(f.Str("id"), f.Str("name"), Strings(f, "slots"), f.Str("damageType"), Strings(f, "itemClasses"));

    private static IReadOnlyList<string> Strings(JsonFields f, string field)
    {
        var list = new List<string>();
        foreach (var e in f.Array(field))
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.String)
                list.Add(e.GetString()!);
            else
                f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, $"'{field}' must hold only strings");
        }
        return list;
    }

    protected override void ValidateRow(DescentContent content, FamilyRow row, ContentErrors errors)
    {
        ContentChecks.Reference<DamageTypeRow>(content, errors, Name, row.Id, "damageType", row.DamageType, ContentTables.DamageTypes);
        if (row.Slots.Count == 0)
            errors.Add(Name, row.Id, ContentRule.Malformed, "'slots' names no slot");
        foreach (var slot in row.Slots.Where(s => !SlotKinds.Contains(s)))
            errors.Add(Name, row.Id, ContentRule.OutOfRange, $"'slots' names '{slot}', not one of {string.Join(", ", SlotKinds)}");
        if (row.ItemClasses.Count == 0)
            errors.Add(Name, row.Id, ContentRule.Malformed, "'itemClasses' names no item class");
    }

    protected override void ValidateTable(DescentContent content, Table<FamilyRow> table, ContentErrors errors)
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
            foreach (var itemClass in row.ItemClasses)
                if (!owner.TryAdd(itemClass, row.Id))
                    errors.Add(Name, row.Id, ContentRule.DuplicateId,
                        $"item class '{itemClass}' is already under family '{owner[itemClass]}'");
    }
}
