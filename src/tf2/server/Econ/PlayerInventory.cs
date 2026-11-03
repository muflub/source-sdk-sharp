//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// One owned item: <c>CEconItemView</c>'s identity and the values that are its
/// own (stock's item id, definition, quality and dynamic attribute list).
/// A base item is the definition alone, with <see cref="InvalidItemId"/>.
/// </summary>
public sealed class ItemInstance(ulong id, ItemDefinition definition, int quality, IReadOnlyList<AttributeValue> attributes)
{
    /// <summary><c>INVALID_ITEM_ID</c> (econ_item_constants.h): <c>(itemid_t)-1</c>.</summary>
    public const ulong InvalidItemId = ulong.MaxValue;

    public ulong Id { get; } = id;

    public ItemDefinition Definition { get; } = definition;

    public int Defindex => Definition.Defindex;

    public int Quality { get; } = quality;

    /// <summary>The item's own attributes, over its definition's statics.</summary>
    public IReadOnlyList<AttributeValue> Attributes { get; } = attributes;

    /// <summary>A stock item every player has without owning it (<c>GetBaseItemForClass</c>).</summary>
    public bool IsBaseItem => Id == InvalidItemId;

    /// <summary>The base item for <paramref name="definition"/>: no id, the script quality, no own attributes.</summary>
    public static ItemInstance Base(ItemDefinition definition) => new(InvalidItemId, definition, definition.Quality, []);

    public override string ToString() => IsBaseItem ? $"base {Definition}" : $"#{Id} {Definition}";
}

/// <summary>
/// A player's local inventory and per-class loadout: the shape of
/// <c>CTFPlayerInventory</c> (tf_item_inventory.cpp) without its GC
/// (D2): the items the player owns and, per class and slot, the item id
/// equipped there (absent = <c>LOADOUT_SLOT_USE_BASE_ITEM</c>).
/// </summary>
public sealed class PlayerInventory(ulong steamId)
{
    private readonly List<ItemInstance> _items = [];
    private readonly Dictionary<(TFClass, TFLoadoutSlot), ulong> _loadout = [];

    /// <summary>The SteamID64 the inventory belongs to; 0 for a player the engine has not authenticated.</summary>
    public ulong SteamId { get; } = steamId;

    public IReadOnlyList<ItemInstance> Items => _items;

    /// <summary>The (class, slot) → item id pairs equipped, in no order.</summary>
    public IReadOnlyDictionary<(TFClass Class, TFLoadoutSlot Slot), ulong> Equipped => _loadout;

    /// <summary>The next id <see cref="Add"/> hands out: one past the largest owned.</summary>
    public ulong NextId => _items.Count == 0 ? 1 : _items.Max(i => i.Id) + 1;

    /// <summary><c>GetInventoryItemByItemID</c>; null for an id the player does not own.</summary>
    public ItemInstance? Item(ulong id) => _items.FirstOrDefault(i => i.Id == id);

    /// <summary>Takes an item the player now owns, as loaded or granted.</summary>
    public void Add(ItemInstance item)
    {
        if (item.IsBaseItem || Item(item.Id) is not null)
            throw new ArgumentException($"item id {item.Id} is a base item or already owned", nameof(item));
        _items.Add(item);
    }

    /// <summary>
    /// <c>CTFInventoryManager::EquipItemInLoadout</c>'s checks (tf_item_inventory.cpp:255-290):
    /// the item is owned, its slot for <paramref name="cls"/> is considered the
    /// same as <paramref name="slot"/> (<c>AreSlotsConsideredIdentical</c>), and
    /// the class can use it. <see cref="ItemInstance.InvalidItemId"/> clears the
    /// slot back to the base item. False, and nothing changed, otherwise.
    /// </summary>
    public bool Equip(TFClass cls, TFLoadoutSlot slot, ulong itemId)
    {
        if (itemId == ItemInstance.InvalidItemId)
        {
            _loadout.Remove((cls, slot));
            return true;
        }

        if (Item(itemId) is not { } item)
            return false;
        if (!SlotsConsideredIdentical(item.Definition, item.Definition.Slot(cls), slot))
            return false;
        if (!item.Definition.UsedBy(cls))
            return false;

        _loadout[(cls, slot)] = itemId;
        return true;
    }

    /// <summary>
    /// <c>CTFPlayerInventory::GetItemInLoadout</c> (tf_item_inventory.cpp:1443-1470):
    /// the equipped item when it still fits the slot, else the base item from
    /// <paramref name="schema"/>. Null for a class outside Scout..Engineer (stock
    /// stops at <c>TF_LAST_NORMAL_CLASS</c>), a slot out of range, or a slot
    /// with no base item.
    /// </summary>
    public ItemInstance? ItemInLoadout(TFClass cls, TFLoadoutSlot slot, ItemSchema schema)
    {
        if (slot < TFLoadoutSlot.Primary || slot >= TFLoadoutSlot.Count)
            return null;
        if (cls < TFClass.Scout || cls >= TFClass.Civilian)
            return null;

        if (_loadout.TryGetValue((cls, slot), out ulong id) && Item(id) is { } item
            && SlotsConsideredIdentical(item.Definition, item.Definition.Slot(cls), slot))
        {
            return item;
        }

        return schema.BaseItem(cls, slot) is { } def ? ItemInstance.Base(def) : null;
    }

    /// <summary><c>AreSlotsConsideredIdentical</c> (tf_item_inventory.cpp:168-195) for a class item.</summary>
    public static bool SlotsConsideredIdentical(ItemDefinition item, TFLoadoutSlot baseSlot, TFLoadoutSlot testSlot)
    {
        if (item.EquipType != EquipType.Class)
            return baseSlot == testSlot;

        return baseSlot switch
        {
            TFLoadoutSlot.Misc => testSlot is TFLoadoutSlot.Misc or TFLoadoutSlot.Misc2 or TFLoadoutSlot.Head,
            TFLoadoutSlot.Building => testSlot == TFLoadoutSlot.Building,
            TFLoadoutSlot.Taunt => testSlot is >= TFLoadoutSlot.Taunt and <= TFLoadoutSlot.Taunt8,
            _ => baseSlot == testSlot,
        };
    }
}
