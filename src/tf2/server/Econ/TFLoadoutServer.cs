using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// The loadout data path's server half (D2): claims the client's
/// <see cref="LoadoutAsk"/> and answers it — the class's visible slots with
/// their equipped defindexes (<see cref="PlayerInventory.ItemInLoadout"/>,
/// the base item when nothing is equipped) and the owned items the class can
/// use. What stock's <c>CClassLoadoutPanel</c> asks the GC for
/// (class_loadout_panel.cpp), carried by our own <c>TF2:loadout</c> reply.
/// </summary>
[SideService(Side.Server)]
public sealed class TFLoadoutServer(LocalInventory inventory, ItemSchema schema, IUserMessageSender messages, ILog log)
    : IClientLifecycle
{
    /// <summary>A stage of its own in TF2's thousand, after the join-class command (+70).</summary>
    public const int Order = 1_000_000 - 1_000 + 80;

    public int ClientLifecycleOrder => Order;

    public bool OnClientMessage(int entIndex, string key, ReadOnlySpan<byte> payload)
    {
        if (key != LoadoutAsk.Key || !LoadoutAsk.TryRead(payload, out byte cls))
            return false;

        if (inventory.Of(entIndex) is null)
        {
            log.Warning($"[TF2/Loadout] client {entIndex} has no inventory yet; nothing to answer its ask with\n");
            return true;
        }

        Send(entIndex, (TFClass)cls);
        return true;
    }

    /// <summary>
    /// Sends the class's reply to <paramref name="entIndex"/>. False — and
    /// nothing sent — for an inventory-less client or a class with no
    /// loadout screen (stock stops at <c>TF_LAST_NORMAL_CLASS</c>).
    /// </summary>
    public bool Send(int entIndex, TFClass cls)
    {
        if (inventory.Of(entIndex) is not { } player || TFLoadoutSlots.Visible(cls).Length == 0)
            return false;

        return messages.Send(Reply(player, cls), player: entIndex, reliable: true);
    }

    /// <summary>
    /// The class's reply: one entry per visible slot in panel order, then
    /// the owned items that fit the class (stock's per-class usability),
    /// cut to <see cref="LoadoutReply.MaxOwned"/>.
    /// </summary>
    public LoadoutReply Reply(PlayerInventory player, TFClass cls)
    {
        var slots = TFLoadoutSlots.Visible(cls);
        var equipped = new uint[slots.Length];
        var slotBytes = new byte[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            slotBytes[i] = (byte)slots[i];
            equipped[i] = player.ItemInLoadout(cls, slots[i], schema) is { } item
                ? (uint)item.Defindex
                : LoadoutReply.NoItem;
        }

        var ownedDefindexes = new List<uint>(LoadoutReply.MaxOwned);
        var ownedSlots = new List<byte>(LoadoutReply.MaxOwned);
        bool cut = false;
        foreach (var item in player.Items)
        {
            if (!item.Definition.UsedBy(cls))
                continue;

            if (ownedDefindexes.Count == LoadoutReply.MaxOwned)
            {
                cut = true;
                break;
            }

            ownedDefindexes.Add((uint)item.Defindex);
            ownedSlots.Add((byte)item.Definition.Slot(cls));
        }

        if (cut)
            log.Warning($"[TF2/Loadout] {player.SteamId} owns more {TFClassNames.Raw(cls)} items than a reply carries; the rest are off the screen\n");

        return new LoadoutReply
        {
            Class = (byte)cls,
            EquippedSlots = slotBytes,
            EquippedDefindexes = equipped,
            OwnedDefindexes = [.. ownedDefindexes],
            OwnedSlots = [.. ownedSlots],
        };
    }
}
