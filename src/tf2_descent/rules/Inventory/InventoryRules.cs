using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Inventory;

/// <summary>An item someone holds, under the id the ledger knows it by.</summary>
public sealed record Held(long Id, ItemInstance Item);

/// <summary>
/// What one hero carries: the backpack by slot (one item a slot), the equipment by equipment
/// slot id, the potion belt by index, Australium, and what vendors keep for buy-back (oldest
/// first). Capacity is not stored: it is derived from the sheet each time
/// (<see cref="InventoryRules.BackpackSlots(HeroSheet)"/>).
/// </summary>
public sealed record HeroInventory
{
    public ImmutableSortedDictionary<int, Held> Backpack { get; init; } = ImmutableSortedDictionary<int, Held>.Empty;
    public ImmutableSortedDictionary<string, Held> Equipped { get; init; } = ImmutableSortedDictionary<string, Held>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<int, Held> Belt { get; init; } = ImmutableSortedDictionary<int, Held>.Empty;
    public ImmutableList<Held> BuyBack { get; init; } = ImmutableList<Held>.Empty;
    public long Australium { get; init; }

    /// <summary>Every item the hero holds: backpack, equipment and belt (not buy-back, which the vendor holds).</summary>
    public IEnumerable<Held> Items => Backpack.Values.Concat(Equipped.Values).Concat(Belt.Values);
}

/// <summary>The account's stash by slot (D4: shared by the account's characters; hardcore has its own).</summary>
public sealed record StashState
{
    public ImmutableSortedDictionary<int, Held> Slots { get; init; } = ImmutableSortedDictionary<int, Held>.Empty;
}

/// <summary>Why an inventory operation was refused. A refused operation changes nothing.</summary>
public static class InventoryRefusals
{
    public const string BackpackFull = "backpack_full";
    public const string StashFull = "stash_full";
    public const string WithdrawOnly = "withdraw_only";
    public const string NoItem = "no_item";
    public const string NoSuchSlot = "no_such_slot";
    public const string WrongSlot = "wrong_slot";
    public const string WrongClass = "wrong_class";
    public const string Level = "level";
    public const string Requirement = "requirement";
    public const string Unidentified = "unidentified";
    public const string Unusable = "unusable";
    public const string NotAPotion = "not_a_potion";
    public const string Occupied = "occupied";
    public const string NotStackable = "not_stackable";
    public const string NotEnoughAustralium = "not_enough_australium";
    public const string Refused = "refused";
}

/// <summary>
/// An operation's result: the hero's inventory and the stash after it, the items that left
/// both (dropped, consumed, gone from buy-back, or a stack merged whole into another, whose
/// units stay), or why it was refused.
/// </summary>
public sealed record InventoryResult(HeroInventory Hero, StashState Stash, IReadOnlyList<Held> Left, string? Refusal)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// The inventory's rules (plan_descent_logic RU5; plan_tf2_rpg 2d; R4, R8, R13, D4), pure
/// functions over an immutable state, so the host can replay them.
/// <list type="bullet">
/// <item><b>Capacity is derived, never stored.</b> The backpack's from the hero's
/// <c>deep_pockets</c> rank, the stash's from the highest <c>storage_plan</c> rank among the
/// account's living characters. Capacity can shrink (a respec); shrinking never destroys an
/// item: a slot at or beyond capacity is <b>withdraw-only</b>, its item can be taken out
/// (moved, equipped, dropped, sold, deposited) and nothing can be put in, until the overflow is
/// cleared.</item>
/// <item>Picking up, buying, buying back, unequipping, taking from the belt or the stash and
/// receiving a craft's output all need a free slot below capacity, and are refused when full.</item>
/// <item>Equip: the base's slot fits the equipment slot, the class may use it, the hero's level
/// is at least the base's, it is identified, it is not unusable, and its attribute requirements
/// are met by Now with what else is equipped. The item it replaces goes where it came from.</item>
/// <item>The belt holds potions only. Stacks merge up to the base's stack size.</item>
/// </list>
/// </summary>
public sealed class InventoryRules
{
    private readonly DescentContent _content;
    private readonly ItemRules _items;

    public InventoryRules(DescentContent content, ItemRules items)
    {
        _content = content;
        _items = items;
    }

    private InventorySettings S => _content.InventorySettings;

    private static InventoryResult Refuse(HeroInventory hero, StashState stash, string why) => new(hero, stash, [], why);

    private static InventoryResult Done(HeroInventory hero, StashState stash, params Held[] left) => new(hero, stash, left, null);

    //-------------------------------------------------------------------------
    // Capacity (R13, D4)
    //-------------------------------------------------------------------------

    /// <summary>Backpack capacity: base + perRank · the sheet's <c>deep_pockets</c> rank (12 to 40 as shipped).</summary>
    public int BackpackSlots(HeroSheet sheet) =>
        S.Backpack.At(sheet.SkillRanks.TryGetValue(S.Backpack.Skill, out var rank) ? rank : 0);

    /// <summary>Stash capacity: base + perRank · the highest <c>storage_plan</c> rank among the living (not fallen) characters given (20 to 100 as shipped).</summary>
    public int StashSlots(IEnumerable<HeroSheet> living) =>
        S.Stash.At(living.Where(s => !s.Fallen).Select(s => s.SkillRanks.TryGetValue(S.Stash.Skill, out var r) ? r : 0).DefaultIfEmpty(0).Max());

    /// <summary>The backpack's items in withdraw-only slots: at or beyond capacity.</summary>
    public IReadOnlyList<int> Overflow(HeroInventory hero, HeroSheet sheet)
    {
        int cap = BackpackSlots(sheet);
        return [.. hero.Backpack.Keys.Where(k => k >= cap)];
    }

    /// <summary>The stash's items in withdraw-only slots.</summary>
    public static IReadOnlyList<int> StashOverflow(StashState stash, int capacity) => [.. stash.Slots.Keys.Where(k => k >= capacity)];

    private static int Free(ImmutableSortedDictionary<int, Held> slots, int capacity)
    {
        for (int i = 0; i < capacity; i++)
            if (!slots.ContainsKey(i)) return i;
        return -1;
    }

    /// <summary>The first free backpack slot below capacity; -1 when full.</summary>
    public int FreeSlot(HeroInventory hero, HeroSheet sheet) => Free(hero.Backpack, BackpackSlots(sheet));

    /// <summary>How many backpack slots below capacity are free.</summary>
    public int FreeSlots(HeroInventory hero, HeroSheet sheet)
    {
        int cap = BackpackSlots(sheet);
        return cap - hero.Backpack.Keys.Count(k => k < cap);
    }

    //-------------------------------------------------------------------------
    // The backpack
    //-------------------------------------------------------------------------

    /// <summary>Picks an item up into the first free slot; refused when full (R4: loot is the picker's alone).</summary>
    public InventoryResult Pickup(HeroInventory hero, StashState stash, HeroSheet sheet, Held item)
    {
        int slot = FreeSlot(hero, sheet);
        return slot < 0 ? Refuse(hero, stash, InventoryRefusals.BackpackFull)
            : Done(hero with { Backpack = hero.Backpack.Add(slot, item) }, stash);
    }

    /// <summary>Drops the item in a backpack slot (withdraw-only slots included); it leaves.</summary>
    public InventoryResult Drop(HeroInventory hero, StashState stash, int slot) =>
        hero.Backpack.TryGetValue(slot, out var item)
            ? Done(hero with { Backpack = hero.Backpack.Remove(slot) }, stash, item)
            : Refuse(hero, stash, InventoryRefusals.NoItem);

    /// <summary>Moves an item to another slot below capacity; swaps when both slots are below capacity and occupied.</summary>
    public InventoryResult Move(HeroInventory hero, StashState stash, HeroSheet sheet, int from, int to)
    {
        int cap = BackpackSlots(sheet);
        if (!hero.Backpack.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.NoSuchSlot);
        if (to >= cap) return Refuse(hero, stash, InventoryRefusals.WithdrawOnly);
        if (from == to) return Done(hero, stash);
        if (!hero.Backpack.TryGetValue(to, out var other))
            return Done(hero with { Backpack = hero.Backpack.Remove(from).Add(to, item) }, stash);
        if (from >= cap) return Refuse(hero, stash, InventoryRefusals.WithdrawOnly);   // a swap would put an item into a withdraw-only slot
        return Done(hero with { Backpack = hero.Backpack.SetItem(from, other).SetItem(to, item) }, stash);
    }

    /// <summary>Merges the stack in <paramref name="from"/> into the stack in <paramref name="to"/> (below capacity), up to the base's stack size; what does not fit stays.</summary>
    public InventoryResult Stack(HeroInventory hero, StashState stash, HeroSheet sheet, int from, int to)
    {
        if (!hero.Backpack.TryGetValue(from, out var a) || !hero.Backpack.TryGetValue(to, out var b) || from == to)
            return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (to >= BackpackSlots(sheet)) return Refuse(hero, stash, InventoryRefusals.WithdrawOnly);
        int limit = _content.ItemBases[b.Item.BaseId].Stack;
        if (a.Item.BaseId != b.Item.BaseId || limit <= 1) return Refuse(hero, stash, InventoryRefusals.NotStackable);
        int moved = Math.Min(a.Item.Count, limit - b.Item.Count);
        if (moved <= 0) return Refuse(hero, stash, InventoryRefusals.NotStackable);
        var backpack = hero.Backpack.SetItem(to, b with { Item = b.Item with { Count = b.Item.Count + moved } });
        if (moved < a.Item.Count)
            return Done(hero with { Backpack = backpack.SetItem(from, a with { Item = a.Item with { Count = a.Item.Count - moved } }) }, stash);
        return Done(hero with { Backpack = backpack.Remove(from) }, stash, a);   // merged whole: its id leaves, its units stay in 'to'
    }

    /// <summary>Replaces the item in a slot with its changed self, same id (the Spy's identify, the Engineer's repair: the host changes those in place).</summary>
    public InventoryResult Replace(HeroInventory hero, StashState stash, int slot, ItemInstance changed) =>
        hero.Backpack.TryGetValue(slot, out var held)
            ? Done(hero with { Backpack = hero.Backpack.SetItem(slot, held with { Item = changed }) }, stash)
            : Refuse(hero, stash, InventoryRefusals.NoItem);

    //-------------------------------------------------------------------------
    // Equipment and belt
    //-------------------------------------------------------------------------

    /// <summary>Why the item cannot be equipped in <paramref name="slot"/> with <paramref name="others"/> equipped; null when it can.</summary>
    public string? EquipRefusal(HeroSheet sheet, string slot, ItemInstance item, IReadOnlyList<EquippedItem> others)
    {
        if (S.Slot(slot) is not { } equipment) return InventoryRefusals.NoSuchSlot;
        var b = _content.ItemBases[item.BaseId];
        if (!b.IsGear || b.Slot != equipment.Accepts) return InventoryRefusals.WrongSlot;
        if (!item.Identified) return InventoryRefusals.Unidentified;
        if (_items.Equipment.IsUnusable(item)) return InventoryRefusals.Unusable;
        if (!b.AllowsClass(sheet.ClassId)) return InventoryRefusals.WrongClass;
        if (sheet.Level < b.Level) return InventoryRefusals.Level;
        var resolved = _items.Equipment.Resolve(sheet, [.. others, new EquippedItem(slot, item)]);
        return resolved.State(slot).Reason == InactiveReason.Requirement ? InventoryRefusals.Requirement : null;
    }

    /// <summary>Equips the item in a backpack slot; the item it replaces takes its place (or the first free slot, if it came from a withdraw-only one).</summary>
    public InventoryResult Equip(HeroInventory hero, StashState stash, HeroSheet sheet, int from, string slot)
    {
        if (!hero.Backpack.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        var others = hero.Equipped.Where(e => e.Key != slot).Select(e => new EquippedItem(e.Key, e.Value.Item)).ToList();
        if (EquipRefusal(sheet, slot, item.Item, others) is { } why) return Refuse(hero, stash, why);
        var backpack = hero.Backpack.Remove(from);
        if (hero.Equipped.TryGetValue(slot, out var old))
        {
            int to = from < BackpackSlots(sheet) ? from : Free(backpack, BackpackSlots(sheet));
            if (to < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
            backpack = backpack.Add(to, old);
        }
        return Done(hero with { Backpack = backpack, Equipped = hero.Equipped.SetItem(slot, item) }, stash);
    }

    /// <summary>Takes off what is in an equipment slot, into the first free backpack slot.</summary>
    public InventoryResult Unequip(HeroInventory hero, StashState stash, HeroSheet sheet, string slot)
    {
        if (!hero.Equipped.TryGetValue(slot, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        int to = FreeSlot(hero, sheet);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with { Backpack = hero.Backpack.Add(to, item), Equipped = hero.Equipped.Remove(slot) }, stash);
    }

    /// <summary>Puts a potion from the backpack on an empty belt slot.</summary>
    public InventoryResult ToBelt(HeroInventory hero, StashState stash, int from, int index)
    {
        if (!hero.Backpack.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (index < 0 || index >= S.BeltSlots) return Refuse(hero, stash, InventoryRefusals.NoSuchSlot);
        if (_content.ItemBases[item.Item.BaseId].Slot != S.BeltAccepts) return Refuse(hero, stash, InventoryRefusals.NotAPotion);
        if (hero.Belt.ContainsKey(index)) return Refuse(hero, stash, InventoryRefusals.Occupied);
        return Done(hero with { Backpack = hero.Backpack.Remove(from), Belt = hero.Belt.Add(index, item) }, stash);
    }

    /// <summary>Takes a potion off the belt into the first free backpack slot.</summary>
    public InventoryResult FromBelt(HeroInventory hero, StashState stash, HeroSheet sheet, int index)
    {
        if (!hero.Belt.TryGetValue(index, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        int to = FreeSlot(hero, sheet);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with { Backpack = hero.Backpack.Add(to, item), Belt = hero.Belt.Remove(index) }, stash);
    }

    /// <summary>Drinks the potion on a belt slot: it leaves (HE8 applies what it restores).</summary>
    public InventoryResult Drink(HeroInventory hero, StashState stash, int index) =>
        hero.Belt.TryGetValue(index, out var item)
            ? Done(hero with { Belt = hero.Belt.Remove(index) }, stash, item)
            : Refuse(hero, stash, InventoryRefusals.NoItem);

    //-------------------------------------------------------------------------
    // The stash
    //-------------------------------------------------------------------------

    /// <summary>Moves an item from the backpack (any slot) into the first free stash slot below the stash's capacity.</summary>
    public InventoryResult Deposit(HeroInventory hero, StashState stash, int stashCapacity, int from)
    {
        if (!hero.Backpack.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        int to = Free(stash.Slots, stashCapacity);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.StashFull);
        return Done(hero with { Backpack = hero.Backpack.Remove(from) }, stash with { Slots = stash.Slots.Add(to, item) });
    }

    /// <summary>Moves an item from the stash (any slot, withdraw-only ones included) into the first free backpack slot.</summary>
    public InventoryResult Withdraw(HeroInventory hero, StashState stash, HeroSheet sheet, int from)
    {
        if (!stash.Slots.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        int to = FreeSlot(hero, sheet);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with { Backpack = hero.Backpack.Add(to, item) }, stash with { Slots = stash.Slots.Remove(from) });
    }

    //-------------------------------------------------------------------------
    // Vendors (buy, sell, buy-back)
    //-------------------------------------------------------------------------

    /// <summary>Buys a vendor's item at its buy price (<see cref="PriceKind.Buy"/>): needs the Australium and a free slot.</summary>
    public InventoryResult Buy(HeroInventory hero, StashState stash, HeroSheet sheet, Held item, long price)
    {
        if (hero.Australium < price) return Refuse(hero, stash, InventoryRefusals.NotEnoughAustralium);
        int slot = FreeSlot(hero, sheet);
        if (slot < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with { Backpack = hero.Backpack.Add(slot, item), Australium = hero.Australium - price }, stash);
    }

    /// <summary>
    /// Sells the item in a backpack slot (withdraw-only included) at its sell price; the vendor
    /// keeps it for buy-back, and the oldest beyond the buy-back count leaves for good.
    /// </summary>
    public InventoryResult Sell(HeroInventory hero, StashState stash, int slot, long price)
    {
        if (!hero.Backpack.TryGetValue(slot, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        var buyBack = hero.BuyBack.Add(item);
        var gone = buyBack.Take(Math.Max(0, buyBack.Count - S.BuyBack)).ToArray();
        return Done(hero with
        {
            Backpack = hero.Backpack.Remove(slot),
            Australium = hero.Australium + price,
            BuyBack = buyBack.RemoveRange(0, gone.Length),
        }, stash, gone);
    }

    /// <summary>Buys back a sold item at the price it sold for: needs the Australium and a free slot.</summary>
    public InventoryResult BuyBack(HeroInventory hero, StashState stash, HeroSheet sheet, int index, long price)
    {
        if (index < 0 || index >= hero.BuyBack.Count) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (hero.Australium < price) return Refuse(hero, stash, InventoryRefusals.NotEnoughAustralium);
        int slot = FreeSlot(hero, sheet);
        if (slot < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with
        {
            Backpack = hero.Backpack.Add(slot, hero.BuyBack[index]),
            BuyBack = hero.BuyBack.RemoveAt(index),
            Australium = hero.Australium - price,
        }, stash);
    }

    //-------------------------------------------------------------------------
    // Crafting, salvage, crates (RU9) applied to the backpack
    //-------------------------------------------------------------------------

    /// <summary>
    /// Applies an item operation's result to the backpack, all at once or not at all: the
    /// inputs (backpack slots, in the order the operation was given them) it consumed leave, its
    /// outputs (under <paramref name="ids"/>, one per output) fill the lowest free slots below
    /// capacity, and its cost is paid. Refused when the outputs do not fit or the Australium
    /// does not cover the cost.
    /// </summary>
    public InventoryResult Apply(HeroInventory hero, StashState stash, HeroSheet sheet, IReadOnlyList<int> inputs, ItemChange change,
        IReadOnlyList<long> ids)
    {
        if (!change.Ok) return Refuse(hero, stash, change.Refusal!);
        if (ids.Count != change.Produced.Count) throw new ArgumentException("one id an output", nameof(ids));
        if (inputs.Any(s => !hero.Backpack.ContainsKey(s)) || inputs.Distinct().Count() != inputs.Count)
            return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (hero.Australium < change.Cost) return Refuse(hero, stash, InventoryRefusals.NotEnoughAustralium);
        var left = change.Consumed.Select(i => hero.Backpack[inputs[i]]).ToArray();
        var backpack = hero.Backpack.RemoveRange(change.Consumed.Select(i => inputs[i]));
        int cap = BackpackSlots(sheet);
        for (int k = 0; k < change.Produced.Count; k++)
        {
            int slot = Free(backpack, cap);
            if (slot < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
            backpack = backpack.Add(slot, new Held(ids[k], _items.Codec.Instance(change.Produced[k])));
        }
        return Done(hero with { Backpack = backpack, Australium = hero.Australium - change.Cost }, stash, left);
    }
}
