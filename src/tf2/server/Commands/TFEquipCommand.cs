using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Commands;

/// <summary>
/// The <c>tf2_equip &lt;class&gt; &lt;slot&gt; &lt;item&gt;</c> client
/// command: the loadout screen's equip, through the inventory
/// (<c>CTFInventoryManager::EquipItemInLoadout</c>'s checks, already
/// <see cref="PlayerInventory.Equip"/>'s) and a fresh reply. The item is an
/// owned defindex, or <c>none</c> to clear the slot back to the base item;
/// <c>tf2_equip &lt;class&gt; all none</c> clears the class's every slot
/// (stock's reset is highlander-only — docs/divergences.md).
/// </summary>
[SideService(Side.Server)]
public sealed class TFEquipCommand(LocalInventory inventory, ItemSchema schema, TFLoadoutServer loadout, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Verb = "tf2_equip";

    /// <summary>A stage of its own in TF2's thousand, after the loadout server (+80).</summary>
    public const int Order = 1_000_000 - 1_000 + 90;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        if (args.Length >= 3)
            Equip(player, args[0], args[1], args[2]);
        else
            log.Warning("[TF2/Loadout] tf2_equip takes <class> <slot> <defindex|none|all>\n");
        return true;
    }

    private void Equip(EntityHandle player, string classWord, string slotWord, string itemWord)
    {
        TFClass cls = TFJoinClassCommand.Named(classWord);
        if (cls == TFClass.Undefined || TFLoadoutSlots.Visible(cls).Length == 0)
        {
            log.Warning($"[TF2/Loadout] tf2_equip: '{classWord}' is not a class with a loadout\n");
            return;
        }

        int index = player.EntryIndex;

        // "all" is the reset arm, and only ever clears: a screen asking to
        // equip one item in every slot is asking something no stock panel does.
        if (string.Equals(slotWord, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(itemWord, "none", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning("[TF2/Loadout] tf2_equip <class> all takes only 'none'\n");
                return;
            }

            for (var slot = TFLoadoutSlot.Primary; slot < TFLoadoutSlot.Count; slot++)
                inventory.Equip(index, cls, slot, ItemInstance.InvalidItemId);
            loadout.Send(index, cls);
            return;
        }

        int slotIndex = TFItemStrings.ClassSlotIndex(slotWord);
        if (slotIndex is < 0 or > (int)TFLoadoutSlot.Misc2)
        {
            log.Warning($"[TF2/Loadout] tf2_equip: '{slotWord}' is not a loadout slot\n");
            return;
        }

        if (string.Equals(itemWord, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (inventory.Equip(index, cls, (TFLoadoutSlot)slotIndex, ItemInstance.InvalidItemId))
                loadout.Send(index, cls);
            return;
        }

        if (!int.TryParse(itemWord, out int defindex) || schema.Item(defindex) is null)
        {
            log.Warning($"[TF2/Loadout] tf2_equip: '{itemWord}' is neither a defindex in the schema nor 'none'\n");
            return;
        }

        if (inventory.Of(index) is not { } owned || owned.Items.FirstOrDefault(i => i.Defindex == defindex) is not { } item)
        {
            log.Warning($"[TF2/Loadout] tf2_equip: {index} owns no item of defindex {defindex}\n");
            return;
        }

        // The schema's own fit check: the slot the item's definition names
        // for the class must be the one the command names.
        if (item.Definition.Slot(cls) != (TFLoadoutSlot)slotIndex)
        {
            log.Warning($"[TF2/Loadout] tf2_equip: {item.Definition} does not fit the {slotWord} slot of a {classWord}\n");
            return;
        }

        if (inventory.Equip(index, cls, (TFLoadoutSlot)slotIndex, item.Id))
            loadout.Send(index, cls);
    }
}
