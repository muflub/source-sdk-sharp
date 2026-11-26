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

    /// <summary>A count to use up outside 1..the stack (the host's word for it).</summary>
    public const string BadCount = "bad_count";

    /// <summary>Only a potion or a consumable is used up.</summary>
    public const string NotConsumable = "not_consumable";
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
/// An item on a corpse and where the hero carried it when he died: the equipment slot it was
/// worn in, the belt slot it hung on, or neither (the backpack).
/// </summary>
public sealed record CorpseItem(Held Item, string? Equipped = null, int? Belt = null);

/// <summary>
/// A corpse recovered (<see cref="InventoryRules.Recover"/>): the hero's inventory after it, the
/// items that did not fit and stay on the corpse (their places kept), and how many went back on
/// to their old equipment slot, their old belt slot and the backpack.
/// </summary>
public sealed record CorpseRecovery(HeroInventory Hero, IReadOnlyList<CorpseItem> Left, int Equipped, int Belted, int Packed)
{
    public int Recovered => Equipped + Belted + Packed;
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

    /// <summary>
    /// The hero's own corpse back onto him (WO5's walk back; the plans say "one use-key takes
    /// all that fits" and are silent on where: this lane's rule). What he wore goes back on to
    /// its old equipment slot when that slot is free and the equip rules still take it (the
    /// level, the requirements with what is already back on, in the inventory's slot order);
    /// what hung on the belt goes back to its old belt slot when that is free; everything else,
    /// and whatever could not go back where it was, is picked up into the backpack. What does
    /// not fit stays on the corpse, its place kept. Last, since a death gives the starting kit
    /// (plan L26), an item picked up because a starting kit item holds its old equipment or belt
    /// slot takes that slot back when the equip rules take it, and the kit item goes into the
    /// backpack slot the corpse's item had (a swap: the backpack never fills by it).
    /// </summary>
    public CorpseRecovery Recover(HeroInventory hero, HeroSheet sheet, IReadOnlyList<CorpseItem> corpse)
    {
        int equipped = 0, belted = 0, packed = 0;
        var placed = new HashSet<CorpseItem>();

        // Worn items first, slot by slot in the inventory's order, so a requirement reads what is already back on.
        foreach (var slot in S.Equipment)
        {
            foreach (var c in corpse)
            {
                if (c.Equipped != slot.Id || placed.Contains(c) || hero.Equipped.ContainsKey(slot.Id))
                    continue;
                var others = hero.Equipped.Select(e => new EquippedItem(e.Key, e.Value.Item)).ToList();
                if (EquipRefusal(sheet, slot.Id, c.Item.Item, others) is not null)
                    continue;
                hero = hero with { Equipped = hero.Equipped.Add(slot.Id, c.Item) };
                placed.Add(c);
                equipped++;
            }
        }

        foreach (var c in corpse)
        {
            if (c.Belt is not { } index || placed.Contains(c) || index < 0 || index >= S.BeltSlots || hero.Belt.ContainsKey(index)
                || _content.ItemBases[c.Item.Item.BaseId].Slot != S.BeltAccepts)
                continue;
            hero = hero with { Belt = hero.Belt.Add(index, c.Item) };
            placed.Add(c);
            belted++;
        }

        var left = new List<CorpseItem>();
        var packedAt = new List<(CorpseItem Item, int Slot)>();
        foreach (var c in corpse)
        {
            if (placed.Contains(c))
                continue;
            int slot = FreeSlot(hero, sheet);
            if (slot < 0)
            {
                left.Add(c);
                continue;
            }
            hero = hero with { Backpack = hero.Backpack.Add(slot, c.Item) };
            packedAt.Add((c, slot));
            packed++;
        }

        // The kit a death gave gives its places back (plan L26).
        foreach (var (c, at) in packedAt)
        {
            if (c.Equipped is { } worn && hero.Equipped.TryGetValue(worn, out var kitWorn) && StartingKitRow.IsKit(kitWorn.Item))
            {
                var others = hero.Equipped.Where(e => e.Key != worn).Select(e => new EquippedItem(e.Key, e.Value.Item)).ToList();
                if (EquipRefusal(sheet, worn, c.Item.Item, others) is not null)
                    continue;
                hero = hero with { Equipped = hero.Equipped.SetItem(worn, c.Item), Backpack = hero.Backpack.SetItem(at, kitWorn) };
                equipped++;
                packed--;
            }
            else if (c.Belt is { } index && hero.Belt.TryGetValue(index, out var kitPotion) && StartingKitRow.IsKit(kitPotion.Item)
                     && _content.ItemBases[c.Item.Item.BaseId].Slot == S.BeltAccepts)
            {
                hero = hero with { Belt = hero.Belt.SetItem(index, c.Item), Backpack = hero.Backpack.SetItem(at, kitPotion) };
                belted++;
                packed--;
            }
        }
        return new CorpseRecovery(hero, left, equipped, belted, packed);
    }

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

    /// <summary>
    /// The contract's <c>Merge</c> (Host.Contracts 1.2): <paramref name="source"/>'s count into
    /// <paramref name="target"/>'s, up to the base's stack size, as <see cref="Stack"/> and
    /// <see cref="ToBelt"/> merge. Inputs are [source, target]: <c>Produced[0]</c> is the target
    /// with its new count; used up, the source is <c>Consumed = [0]</c>, else <c>Produced[1]</c>
    /// holds what did not fit. Both re-encoded, in place; nothing costs. Refused
    /// (<see cref="InventoryRefusals.NotStackable"/>) for two bases, a base that does not stack, or
    /// a target with no room.
    /// </summary>
    public ItemChange Merge(ItemInstance source, ItemInstance target)
    {
        int limit = _content.ItemBases.TryGet(target.BaseId, out var b) ? b.Stack : 1;
        if (source.BaseId != target.BaseId || limit <= 1)
            return new([], [], 0, InventoryRefusals.NotStackable);
        int moved = Math.Min(source.Count, limit - target.Count);
        if (moved <= 0)
            return new([], [], 0, InventoryRefusals.NotStackable);
        var into = _items.Codec.ToRolled(target with { Count = target.Count + moved });
        return moved == source.Count
            ? new([0], [into], 0)
            : new([], [into, _items.Codec.ToRolled(source with { Count = source.Count - moved })], 0);
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
        if (SlotRefusal(sheet, slot, item) is { } why) return why;
        var resolved = _items.Equipment.Resolve(sheet, [.. others, new EquippedItem(slot, item)]);
        return resolved.State(slot).Reason == InactiveReason.Requirement ? InventoryRefusals.Requirement : null;
    }

    /// <summary>
    /// What <see cref="EquipRefusal"/> asks of the item and the slot alone, with no other item
    /// worn: the slot, its base, identified, usable, the class and the level. The attribute
    /// requirements read what else is worn, so they are not asked here.
    /// </summary>
    private string? SlotRefusal(HeroSheet sheet, string slot, ItemInstance item)
    {
        if (S.Slot(slot) is not { } equipment) return InventoryRefusals.NoSuchSlot;
        var b = _content.ItemBases[item.BaseId];
        if (!b.IsGear || b.Slot != equipment.Accepts) return InventoryRefusals.WrongSlot;
        if (!item.Identified) return InventoryRefusals.Unidentified;
        if (_items.Equipment.IsUnusable(item)) return InventoryRefusals.Unusable;
        if (!b.AllowsClass(sheet.ClassId)) return InventoryRefusals.WrongClass;
        if (sheet.Level < b.Level) return InventoryRefusals.Level;
        return null;
    }

    //-------------------------------------------------------------------------
    // The host's three containers (Host.Contracts 1.1)
    //-------------------------------------------------------------------------

    /// <summary>The equipment slots in inventory.json's order: an equipped item's slot number is its index here (the contract's <c>EquipSlots</c>).</summary>
    public IReadOnlyList<string> EquipSlotIds => [.. S.Equipment.Select(e => e.Id)];

    /// <summary>How many potions the belt holds.</summary>
    public int BeltSlots => S.BeltSlots;

    /// <summary>
    /// Why the item may not sit in <paramref name="slot"/> of <paramref name="container"/> (the
    /// contract's <c>PlaceRefusal</c>; the host has checked the slot is in range and free), or
    /// null when it may. The backpack takes anything; the belt takes potions only
    /// (<see cref="InventoryRefusals.NotAPotion"/>); an equipment slot, by its index in
    /// <see cref="EquipSlotIds"/>, asks <see cref="EquipRefusal"/> of the item alone: the host
    /// sees one item, so the attribute requirements, which read what else is worn, are the
    /// game's to ask before it moves anything.
    /// </summary>
    public string? PlaceRefusal(HeroSheet sheet, ItemInstance item, ItemContainer container, int slot)
    {
        switch (container)
        {
            case ItemContainer.Backpack:
                return null;
            case ItemContainer.Belt:
                return _content.ItemBases[item.BaseId].Slot == S.BeltAccepts ? null : InventoryRefusals.NotAPotion;
            case ItemContainer.Equipped:
                return slot >= 0 && slot < S.Equipment.Count ? SlotRefusal(sheet, S.Equipment[slot].Id, item) : InventoryRefusals.NoSuchSlot;
            default:
                return InventoryRefusals.NoSuchSlot;
        }
    }

    /// <summary>Whether an item of this base may be used up: a potion or a consumable (a scroll, a tome, a kit).</summary>
    public bool IsConsumable(ItemInstance item) =>
        _content.ItemBases[item.BaseId].Kind is ItemKinds.Potion or ItemKinds.Consumable;

    /// <summary>
    /// The contract's <c>Consume</c>: <paramref name="count"/> of the stack used up, as
    /// <see cref="Drink"/> and <see cref="Consume(HeroInventory, StashState, int)"/> use one: the
    /// last of a stack ends the item (<c>Consumed = [0]</c>), else the stack comes back with its
    /// count lowered, re-encoded (<c>Produced = [it]</c>). Refused for a count outside 1..the
    /// stack (<c>bad_count</c>) and for anything that is no potion or consumable.
    /// </summary>
    public ItemChange UseUp(ItemInstance item, int count)
    {
        if (count < 1 || count > item.Count) return new([], [], 0, InventoryRefusals.BadCount);
        if (!IsConsumable(item)) return new([], [], 0, InventoryRefusals.NotConsumable);
        return count == item.Count ? new([0], [], 0) : new([], [_items.Codec.ToRolled(item with { Count = item.Count - count })], 0);
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

    /// <summary>
    /// Puts a potion from the backpack on a belt slot: an empty one, or one holding the same
    /// potion with room, where it merges up to the base's stack size (what does not fit stays
    /// in the backpack; merged whole, its id leaves and its units stay on the belt).
    /// </summary>
    public InventoryResult ToBelt(HeroInventory hero, StashState stash, int from, int index)
    {
        if (!hero.Backpack.TryGetValue(from, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (index < 0 || index >= S.BeltSlots) return Refuse(hero, stash, InventoryRefusals.NoSuchSlot);
        if (_content.ItemBases[item.Item.BaseId].Slot != S.BeltAccepts) return Refuse(hero, stash, InventoryRefusals.NotAPotion);
        if (!hero.Belt.TryGetValue(index, out var there))
            return Done(hero with { Backpack = hero.Backpack.Remove(from), Belt = hero.Belt.Add(index, item) }, stash);
        int room = _content.ItemBases[there.Item.BaseId].Stack - there.Item.Count;
        if (there.Item.BaseId != item.Item.BaseId || room <= 0) return Refuse(hero, stash, InventoryRefusals.Occupied);
        int moved = Math.Min(room, item.Item.Count);
        var belt = hero.Belt.SetItem(index, there with { Item = there.Item with { Count = there.Item.Count + moved } });
        if (moved < item.Item.Count)
            return Done(hero with { Backpack = hero.Backpack.SetItem(from, item with { Item = item.Item with { Count = item.Item.Count - moved } }), Belt = belt }, stash);
        return Done(hero with { Backpack = hero.Backpack.Remove(from), Belt = belt }, stash, item);
    }

    /// <summary>
    /// A potion just gained, in backpack slot <paramref name="from"/>, goes onto the belt (Diablo's
    /// rule; the whole run's F20): onto a slot holding the same potion with room, then the first
    /// empty slot, as many as fit (<see cref="ToBelt"/> each time); what does not fit stays in the
    /// backpack. Anything but a potion, or a full belt, changes nothing; this never refuses.
    /// </summary>
    public InventoryResult Stow(HeroInventory hero, StashState stash, int from)
    {
        var left = new List<Held>();
        while (hero.Backpack.TryGetValue(from, out var item) && _content.ItemBases[item.Item.BaseId].Slot == S.BeltAccepts
               && BeltRoom(hero.Belt, item.Item) is var to && to >= 0)
        {
            var r = ToBelt(hero, stash, from, to);
            if (!r.Ok) break;
            hero = r.Hero;
            left.AddRange(r.Left);
        }
        return new InventoryResult(hero, stash, left, null);
    }

    /// <summary>
    /// <paramref name="result"/> with every potion it brought into the backpack (an id
    /// <paramref name="before"/> did not hold) put on the belt by <see cref="Stow"/>, lowest
    /// backpack slot first; a refusal is answered as it is.
    /// </summary>
    public InventoryResult StowGained(HeroInventory before, InventoryResult result)
    {
        if (!result.Ok) return result;
        var held = before.Items.Select(i => i.Id).ToHashSet();
        var hero = result.Hero;
        var left = new List<Held>(result.Left);
        foreach (int slot in result.Hero.Backpack.Where(p => !held.Contains(p.Value.Id)).Select(p => p.Key).ToList())
        {
            var r = Stow(hero, result.Stash, slot);
            hero = r.Hero;
            left.AddRange(r.Left);
        }
        return new InventoryResult(hero, result.Stash, left, null);
    }

    /// <summary>Where a potion goes on the belt: a slot of the same potion with room, else the first empty one; -1 for none.</summary>
    private int BeltRoom(ImmutableSortedDictionary<int, Held> belt, ItemInstance potion)
    {
        int stack = _content.ItemBases[potion.BaseId].Stack;
        for (int i = 0; i < S.BeltSlots; i++)
            if (belt.TryGetValue(i, out var there) && there.Item.BaseId == potion.BaseId && there.Item.Count < stack)
                return i;
        for (int i = 0; i < S.BeltSlots; i++)
            if (!belt.ContainsKey(i))
                return i;
        return -1;
    }

    /// <summary>Takes a potion off the belt into the first free backpack slot.</summary>
    public InventoryResult FromBelt(HeroInventory hero, StashState stash, HeroSheet sheet, int index)
    {
        if (!hero.Belt.TryGetValue(index, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        int to = FreeSlot(hero, sheet);
        if (to < 0) return Refuse(hero, stash, InventoryRefusals.BackpackFull);
        return Done(hero with { Backpack = hero.Backpack.Add(to, item), Belt = hero.Belt.Remove(index) }, stash);
    }

    /// <summary>
    /// Drinks one potion from a belt slot's stack (HE8 applies what it restores): the stack
    /// loses one, and the last one leaves. A slot the drink empties refills, with the
    /// consumables' <c>refill</c>, from the lowest backpack slot holding the same potion,
    /// below capacity or not.
    /// </summary>
    public InventoryResult Drink(HeroInventory hero, StashState stash, int index)
    {
        if (!hero.Belt.TryGetValue(index, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (item.Item.Count > 1)
            return Done(hero with { Belt = hero.Belt.SetItem(index, item with { Item = item.Item with { Count = item.Item.Count - 1 } }) }, stash);
        var after = hero with { Belt = hero.Belt.Remove(index) };
        if (_content.Consumables.Refill
            && hero.Backpack.FirstOrDefault(p => p.Value.Item.BaseId == item.Item.BaseId) is { Value: { } refill } at)
            after = after with { Backpack = after.Backpack.Remove(at.Key), Belt = after.Belt.Add(index, refill) };
        return Done(after, stash, item);
    }

    /// <summary>Uses one of the stack in a backpack slot (a scroll read, a tome learnt): the stack loses one, and the last one leaves.</summary>
    public InventoryResult Consume(HeroInventory hero, StashState stash, int slot)
    {
        if (!hero.Backpack.TryGetValue(slot, out var item)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (item.Item.Count > 1)
            return Done(hero with { Backpack = hero.Backpack.SetItem(slot, item with { Item = item.Item with { Count = item.Item.Count - 1 } }) }, stash);
        return Done(hero with { Backpack = hero.Backpack.Remove(slot) }, stash, item);
    }

    /// <summary>
    /// Replaces the item in an equipment slot with its changed self, same id (wear, a repair
    /// of what is worn): only the mutable half may change (durability, maximum, wear progress).
    /// </summary>
    public InventoryResult ReplaceEquipped(HeroInventory hero, StashState stash, string slot, ItemInstance changed)
    {
        if (!hero.Equipped.TryGetValue(slot, out var held)) return Refuse(hero, stash, InventoryRefusals.NoItem);
        if (changed with { Durability = held.Item.Durability, MaxDurability = held.Item.MaxDurability, WearUses = held.Item.WearUses } != held.Item)
            return Refuse(hero, stash, InventoryRefusals.Refused);
        return Done(hero with { Equipped = hero.Equipped.SetItem(slot, held with { Item = changed }) }, stash);
    }

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
