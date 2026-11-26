using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Inventory;

namespace TF2.Descent.Rules.Items;

/// <summary>
/// starting_kits.json: what a new hero of each class starts with (HE0): the gear bases put in
/// the backpack (the class's stock weapons, each equipped into its own slot by the server's
/// first kit) and the potions put on the belt, belt slot by belt slot. Each is a Stock item
/// rolled from the base. Every class has a kit; every backpack base is gear the class may use,
/// no two in one slot; every belt base is a potion, and no more than the belt holds.
/// </summary>
public sealed record StartingKitRow(string Id, IReadOnlyList<string> Backpack, IReadOnlyList<string> Belt) : IContentRow
{
    /// <summary>The source a kit item is rolled under: a craft's (identified, never cursed or promoted).</summary>
    public const string Source = "craft:starting_kit";

    /// <summary>Whether <paramref name="item"/> is a starting kit's: rolled under <see cref="Source"/>.</summary>
    public static bool IsKit(ItemInstance item) => string.Equals(item.Context.Source, Source, StringComparison.Ordinal);
}

public sealed class StartingKitTable : RowTable<StartingKitRow>
{
    public const string TableName = "starting_kits";

    public override string Name => TableName;

    protected override StartingKitRow ReadRow(JsonFields f) =>
        new(f.Str("id"), ItemTables.Strings(f, "backpack"), ItemTables.Strings(f, "belt"));

    protected override void ValidateRow(DescentContent content, StartingKitRow row, ContentErrors errors)
    {
        string t = Name;
        ContentChecks.Reference<ClassRow>(content, errors, t, row.Id, "id", row.Id, ContentTables.Classes);
        if (row.Backpack.Count == 0)
            errors.Add(t, row.Id, ContentRule.Malformed, "'backpack' names no base");

        content.TryGet<Table<ItemBaseRow>>(ItemTables.Bases, out var bases);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in row.Backpack)
        {
            if (!ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "backpack", b, ItemTables.Bases)
                || bases is null || !bases.TryGet(b, out var item))
                continue;
            if (!ItemTables.Kinds.Contains(item.Kind))
                continue;   // the base's own error stands
            if (!item.IsGear)
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'backpack' names '{b}', which is no weapon or armour");
            else if (!item.AllowsClass(row.Id))
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'backpack' names '{b}', which class '{row.Id}' may not use");
            else if (!slots.TryAdd(item.Slot, b))
                errors.Add(t, row.Id, ContentRule.DuplicateId, $"'backpack' names '{b}' and '{slots[item.Slot]}' for slot '{item.Slot}'");
        }

        if (content.TryGet<InventorySettings>(InventoryTable.TableName, out var inventory) && row.Belt.Count > inventory.BeltSlots)
            errors.Add(t, row.Id, ContentRule.OutOfRange, $"'belt' names {row.Belt.Count} potions; the belt holds {inventory.BeltSlots}");
        foreach (var b in row.Belt)
            if (ContentChecks.Reference<ItemBaseRow>(content, errors, t, row.Id, "belt", b, ItemTables.Bases)
                && bases is not null && bases.TryGet(b, out var potion) && potion.Slot != ItemTables.BeltSlot)
                errors.Add(t, row.Id, ContentRule.OutOfRange, $"'belt' names '{b}', which does not go on the belt");
    }

    protected override void ValidateTable(DescentContent content, Table<StartingKitRow> table, ContentErrors errors)
    {
        if (content.TryGet<Table<ClassRow>>(ContentTables.Classes, out var classes))
            foreach (var c in classes.Rows.Where(c => !table.Contains(c.Id)))
                errors.Add(Name, c.Id, ContentRule.DanglingReference, $"class '{c.Id}' has no starting kit");
    }
}
