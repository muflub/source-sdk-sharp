using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Gear;

/// <summary>What carries a gear wear crossing to a hero's own client.</summary>
public interface IGearWearChannel
{
    void Send(int playerIndex, GearWear wear);
}

/// <summary>The channel over TF2 Descent's gear wear message.</summary>
[SideService(Side.Server, As = [typeof(IGearWearChannel)])]
public sealed class GearWearChannel(IUserMessageSender sender) : IGearWearChannel
{
    public void Send(int playerIndex, GearWear wear) => sender.Send(wear, playerIndex);
}

/// <summary>
/// THE GEAR WARNING'S DELTAS (plan_descent_logic HE9, for the HUD's gear warning, UI2): an
/// owner-only <see cref="GearWear"/> message for an equipment slot whose wear level changed
/// (the rules' <see cref="Durability.Level"/>: ok, low at the warning line, broken, unusable;
/// or emptied), at the end of a frame in which the hero's items changed in the ledger: a
/// crossing, never a shot (wear is written a point at a time, <see cref="HeroWear"/>). A client
/// that arrives or a hero who spawns is sent every worn slot again; a level change forgets
/// what was sent. Other players are never sent it.
/// </summary>
[SideService(Side.Server)]
public sealed class GearWearSync(TF2DescentRules rules, Heroes heroes, HeroKeys keys, IItemLedger ledger, IGearWearChannel channel,
                                 IEngineEntities entities)
    : IItemLedgerHook, IFrameLifecycle, IClientLifecycle, IPlayerSpawnHook, IGameLifecycle, IOrderedHook
{
    /// <summary>TF2 Descent's wear block.</summary>
    public const int Order = ModStages.First + 814;

    private readonly HashSet<int> _due = [];
    private readonly Dictionary<int, Dictionary<string, GearWearLevel>> _sent = new();

    public int HookOrder => Order;

    public int FrameOrder => Order;

    public int ClientLifecycleOrder => Order;

    public int LifecycleOrder => Order;

    /// <summary>The level a slot was last sent at; <see cref="GearWearLevel.Empty"/> when nothing was.</summary>
    public GearWearLevel SentLevel(int playerIndex, string slot) =>
        _sent.TryGetValue(playerIndex, out var slots) && slots.TryGetValue(slot, out var level) ? level : GearWearLevel.Empty;

    public void OnItemsChanged(ItemsChanged change)
    {
        foreach (var h in heroes.All)
            if (h.PlayerIndex > 0 && keys.Of(h.PlayerIndex) == change.Hero)
                _due.Add(h.PlayerIndex);
    }

    public void OnPostThink(float frameTime)
    {
        if (_due.Count == 0)
            return;
        foreach (int index in _due.ToArray())
        {
            // A client in before its hero (or before the ledger loads him) stays due.
            if (heroes.Of(index) is null || keys.Of(index) is not { } key || ledger.Find(key) is not { } held)
                continue;
            _due.Remove(index);
            Send(index, held);
        }
    }

    public void OnClientActive(int entIndex)
    {
        _sent.Remove(entIndex);
        _due.Add(entIndex);
    }

    public void OnClientDisconnected(int entIndex)
    {
        _sent.Remove(entIndex);
        _due.Remove(entIndex);
    }

    public void OnSpawning(EntityHandle player, float now)
    {
    }

    public void OnSpawned(EntityHandle player, float now)
    {
        int index = entities.GetIndex(player);
        _sent.Remove(index);
        _due.Add(index);
    }

    public void OnLevelShutdown()
    {
        _sent.Clear();
        _due.Clear();
    }

    /// <summary>The wear level of an item as the warning shows it (anything that does not wear is ok).</summary>
    public GearWearLevel LevelOf(ItemInstance? item) => item is null ? GearWearLevel.Empty : rules.ItemRules.Durability.Level(item) switch
    {
        WearLevel.Low => GearWearLevel.Low,
        WearLevel.Broken => GearWearLevel.Broken,
        WearLevel.Unusable => GearWearLevel.Unusable,
        _ => GearWearLevel.Ok,
    };

    /// <summary>Each equipment slot whose level differs from what was sent, sent.</summary>
    private void Send(int playerIndex, LedgerHero held)
    {
        if (!_sent.TryGetValue(playerIndex, out var sent))
            _sent[playerIndex] = sent = new Dictionary<string, GearWearLevel>(StringComparer.Ordinal);
        foreach (var slot in rules.Content.InventorySettings.Equipment)
        {
            var item = held.Inventory.Equipped.GetValueOrDefault(slot.Id)?.Item;
            var level = LevelOf(item);
            if (level == sent.GetValueOrDefault(slot.Id, GearWearLevel.Empty))
                continue;
            sent[slot.Id] = level;
            channel.Send(playerIndex, new GearWear
            {
                Slot = slot.Id,
                Name = item is null ? "" : Belt.Cut(rules.Content.ItemBases[item.BaseId].Name, GearWear.MaxName),
                Level = (byte)level,
                Durability = (short)Math.Clamp(item?.Durability ?? 0, 0, short.MaxValue),
                MaxDurability = (short)Math.Clamp(item?.MaxDurability ?? 0, 0, short.MaxValue),
            });
        }
    }
}
