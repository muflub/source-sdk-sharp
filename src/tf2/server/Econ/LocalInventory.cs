using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// The connected players' local inventories (stage1 1.6, D2): loaded from
/// <see cref="InventoryStore"/> when a client becomes active, saved on every
/// change and when it leaves. Keyed by the SteamID the engine authenticated;
/// a player with none (0, an unauthenticated LAN client) keeps an inventory
/// for the session only, said once, because a file named by anything else
/// would hand the next occupant of the seat someone else's items.
/// </summary>
[SideService(Side.Server)]
public sealed class LocalInventory(InventoryStore store, ItemSchema schema, IConnectedClients clients, ILog log)
    : IClientLifecycle
{
    private readonly Dictionary<int, PlayerInventory> _byEntity = [];

    /// <summary>TF2's stage range (999,000-999,999: below the mods' ModStages.First, as TFClassTable.LoadOrder).</summary>
    public const int Order = 1_000_000 - 1_000 + 50;

    public int ClientLifecycleOrder => Order;

    /// <summary>The inventory of the client at <paramref name="entIndex"/>, or null when none is active there.</summary>
    public PlayerInventory? Of(int entIndex) => _byEntity.GetValueOrDefault(entIndex);

    public void OnClientActive(int entIndex)
    {
        ulong steamId = clients.SteamId64(entIndex);
        var inventory = steamId == 0 ? new PlayerInventory(0) : store.Load(steamId);
        _byEntity[entIndex] = inventory;

        if (steamId == 0)
            log.Msg($"[TF2/Inventory] client {entIndex} has no SteamID: its inventory lasts this session and is not saved\n");
        else
            log.Msg($"[TF2/Inventory] client {entIndex} ({steamId}): {inventory.Items.Count} items, {inventory.Equipped.Count} equipped\n");
    }

    public void OnClientDisconnected(int entIndex)
    {
        if (_byEntity.Remove(entIndex, out var inventory))
            Save(inventory);
    }

    /// <summary>
    /// Gives the client at <paramref name="entIndex"/> a new item of
    /// <paramref name="defindex"/>, at its definition's quality, and saves.
    /// Null, with a log line, for an unknown defindex or no active client.
    /// </summary>
    public ItemInstance? Grant(int entIndex, int defindex)
    {
        if (Of(entIndex) is not { } inventory)
        {
            log.Warning($"[TF2/Inventory] no active client at {entIndex}: nothing granted\n");
            return null;
        }
        if (schema.Item(defindex) is not { } def)
        {
            log.Warning($"[TF2/Inventory] defindex {defindex} is not in the item schema: nothing granted\n");
            return null;
        }

        var item = new ItemInstance(inventory.NextId, def, def.Quality, []);
        inventory.Add(item);
        Save(inventory);
        return item;
    }

    /// <summary><see cref="PlayerInventory.Equip"/> for the client at <paramref name="entIndex"/>, saved when it took.</summary>
    public bool Equip(int entIndex, TFClass cls, TFLoadoutSlot slot, ulong itemId)
    {
        if (Of(entIndex) is not { } inventory || !inventory.Equip(cls, slot, itemId))
            return false;
        Save(inventory);
        return true;
    }

    private void Save(PlayerInventory inventory)
    {
        if (inventory.SteamId == 0)
            return;
        try
        {
            store.Save(inventory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning($"[TF2/Inventory] could not save {store.PathOf(inventory.SteamId)}: {e.Message}\n");
        }
    }
}
