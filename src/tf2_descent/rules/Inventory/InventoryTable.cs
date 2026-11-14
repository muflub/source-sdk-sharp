using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Inventory;

/// <summary>A capacity bought with a skill (R13, D4): base + perRank · rank, the rank capped at <see cref="MaxRank"/>.</summary>
public sealed record SkillCapacity(int Base, int PerRank, string Skill, int MaxRank)
{
    public int At(int rank) => Base + PerRank * Math.Clamp(rank, 0, MaxRank);
}

/// <summary>An equipment slot and the base slot it accepts (a trinket base fits either trinket slot).</summary>
public sealed record EquipmentSlot(string Id, string Accepts);

/// <summary>
/// inventory.json (plan_tf2_rpg 2d; R13, R29, D4): the backpack's and the stash's capacity by
/// skill rank (the skills are the skills lane's, named here by id), the fixed equipment slots
/// and potion belt, and how many sold items a vendor keeps for buy-back.
/// </summary>
public sealed record InventorySettings(
    SkillCapacity Backpack,
    SkillCapacity Stash,
    IReadOnlyList<EquipmentSlot> Equipment,
    int BeltSlots,
    string BeltAccepts,
    int BuyBack)
{
    public EquipmentSlot? Slot(string id) => Equipment.FirstOrDefault(e => e.Id == id);
}

public sealed class InventoryTable : SingleTable<InventorySettings>
{
    public const string TableName = "inventory";

    public override string Name => TableName;

    private static SkillCapacity Capacity(JsonFields f)
    {
        var c = new SkillCapacity(f.Int("base"), f.Int("perRank"), f.Str("skill"), f.Int("maxRank"));
        f.End();
        return c;
    }

    protected override InventorySettings Read(JsonFields f)
    {
        var slots = new List<EquipmentSlot>();
        foreach (var e in f.Array("equipment"))
        {
            var s = new JsonFields(e, f.Table, f.Row, f.Errors);
            slots.Add(new EquipmentSlot(s.Str("id"), s.Str("accepts")));
            s.End();
        }
        var belt = f.Obj("belt");
        var settings = new InventorySettings(Capacity(f.Obj("backpack")), Capacity(f.Obj("stash")), slots,
            belt.Int("slots"), belt.Str("accepts"), f.Int("buyBack"));
        belt.End();
        return settings;
    }

    protected override void Validate(DescentContent content, InventorySettings s, ContentErrors errors)
    {
        string t = Name;
        foreach (var (field, c) in new[] { ("backpack", s.Backpack), ("stash", s.Stash) })
        {
            ContentChecks.Range(errors, t, null, field + ".base", c.Base, 1, 10_000);
            ContentChecks.Range(errors, t, null, field + ".perRank", c.PerRank, 0, 10_000);
            ContentChecks.Range(errors, t, null, field + ".maxRank", c.MaxRank, 0, 100);
            if (c.Skill.Length == 0)
                errors.Add(t, null, ContentRule.Malformed, $"'{field}.skill' is empty");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in s.Equipment)
        {
            if (!ids.Add(e.Id))
                errors.Add(t, e.Id, ContentRule.DuplicateId, $"equipment slot '{e.Id}' appears twice");
            ItemTables.OneOf(errors, t, e.Id, "accepts", e.Accepts, ItemTables.GearSlots);
        }
        foreach (var gear in ItemTables.GearSlots.Where(g => s.Equipment.All(e => e.Accepts != g)))
            errors.Add(t, null, ContentRule.DanglingReference, $"no equipment slot accepts '{gear}'");
        ContentChecks.Range(errors, t, null, "belt.slots", s.BeltSlots, 1, 100);
        ItemTables.OneOf(errors, t, null, "belt.accepts", s.BeltAccepts, [ItemTables.BeltSlot]);
        ContentChecks.Range(errors, t, null, "buyBack", s.BuyBack, 0, 1000);
    }
}
