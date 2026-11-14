using SourceSharp.Abi;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// One ledger item as its owner may see it, on the wire (plan_descent_logic
/// UI1, UI3). The view is the rules' owner view (<see cref="ItemViews.View"/>):
/// an unidentified item carries its base, rarity and item level and nothing
/// else, so its affixes, unusual effect and durability never leave the
/// server. Beside it, what the server decides about the item now: whether the
/// hero meets each of its requirements (read against Now, L2), and, equipped,
/// whether it counts (the gear's resolution).
/// </summary>
[SideService(Side.Server)]
public sealed class OwnerItemViews(TF2DescentRules rules)
{
    /// <summary>The wire's container of a ledger place; null for a place the owner's views leave out (buy-back is the vendor's).</summary>
    public static ItemContainer? ContainerOf(ItemPlace place) => place switch
    {
        ItemPlace.Backpack => ItemContainer.Backpack,
        ItemPlace.Equipped => ItemContainer.Equipped,
        ItemPlace.Belt => ItemContainer.Belt,
        ItemPlace.Stash => ItemContainer.Stash,
        _ => null,
    };

    /// <summary>
    /// The payload of <paramref name="item"/> for its owner, whose sheet and stats are
    /// <paramref name="sheet"/> and <paramref name="stats"/> (no stats: no requirement is
    /// known met), and <paramref name="counts"/> whether an equipped item counts now (null
    /// when not known or not equipped). Null for a place the views leave out.
    /// </summary>
    public OwnerItem? Of(LedgerItem item, HeroSheet? sheet, HeroStats? stats, bool? counts)
    {
        if (ContainerOf(item.Place) is not { } container)
            return null;

        var content = rules.Content;
        var equipment = rules.ItemRules.Equipment;
        var instance = item.Item;
        var row = content.ItemBases[instance.BaseId];
        var view = rules.ItemRules.Views.View(instance, ItemViewer.Owner);

        int unmet = Unmet(row, sheet, stats);
        var flags = ItemViewFlags.None;
        if (view.Identified)
            flags |= ItemViewFlags.Identified;
        if (equipment.IsBroken(instance))
            flags |= ItemViewFlags.Broken;
        if (equipment.IsUnusable(instance))
            flags |= ItemViewFlags.Unusable;
        if (counts == false)
            flags |= ItemViewFlags.Inactive;
        if (stats is not null && sheet is not null && unmet == 0)
            flags |= ItemViewFlags.MeetsRequirements;

        var affixes = view.Affixes.Take(OwnerItem.MaxAffixes).ToList();
        int slot = item.Place == ItemPlace.Equipped
            ? IndexOfSlot(item.EquipSlot)
            : item.Slot;

        return new OwnerItem
        {
            Id = item.Id,
            Container = (byte)container,
            Slot = (short)Math.Clamp(slot, short.MinValue, short.MaxValue),
            Base = (ushort)Math.Max(0, ContentKeys.IndexOf(content.ItemBases, instance.BaseId)),
            Rarity = (byte)Math.Clamp(view.Rarity, 0, byte.MaxValue),
            ItemLevel = (byte)Math.Clamp(view.ItemLevel, 0, byte.MaxValue),
            Flags = (byte)flags,
            Durability = (ushort)Math.Clamp(view.Durability, 0, ushort.MaxValue),
            MaxDurability = (ushort)Math.Clamp(view.MaxDurability, 0, ushort.MaxValue),
            Count = (ushort)Math.Clamp(view.Count, 0, ushort.MaxValue),
            Unusual = (byte)(ContentKeys.IndexOf(content.UnusualEffects, view.UnusualEffect) + 1),
            Unique = (byte)(ContentKeys.IndexOf(content.Uniques, view.Unique) + 1),
            Unmet = (byte)unmet,
            Affixes = [.. affixes.Select(a => (byte)Math.Max(0, ContentKeys.IndexOf(content.Affixes, a.Id)))],
            Values = [.. affixes.Select(a => (float)a.Value)],
        };
    }

    /// <summary>
    /// Which requirements of <paramref name="row"/> the hero fails now: bit 0 the base's
    /// level, bit k + 1 its k-th attribute requirement (against Now). 0 without a sheet or stats.
    /// </summary>
    public static int Unmet(ItemBaseRow row, HeroSheet? sheet, HeroStats? stats)
    {
        if (sheet is null || stats is null)
            return 0;
        int mask = sheet.Level < row.Level ? 1 : 0;
        for (int k = 0; k < row.Requires.Count && k < 7; k++)
            if (stats.Now(row.Requires[k].Attribute) < row.Requires[k].Min)
                mask |= 1 << (k + 1);
        return mask;
    }

    /// <summary>The index of an equipment slot in inventory.json; -1 for none.</summary>
    public int IndexOfSlot(string? equipSlot)
    {
        var slots = rules.Content.InventorySettings.Equipment;
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].Id == equipSlot)
                return i;
        return -1;
    }
}
