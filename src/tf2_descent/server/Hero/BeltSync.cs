using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>What carries the belt to a hero's own client (one owner-only message a send).</summary>
public interface IBeltChannel
{
    void Send(int playerIndex, Belt belt);
}

/// <summary>The channel over TF2 Descent's belt message.</summary>
[SideService(Side.Server, As = [typeof(IBeltChannel)])]
public sealed class BeltChannel(IUserMessageSender sender) : IBeltChannel
{
    public void Send(int playerIndex, Belt belt) => sender.Send(belt, playerIndex);
}

/// <summary>
/// THE BELT ON THE WIRE (plan_descent_logic HE8, for the HUD's belt strip, UI2): each hero's
/// four slots (base, name, count) and the shared cooldown's end and length in ticks, as one
/// owner-only message (<see cref="Belt"/>), sent at the end of a frame in which it changed (an
/// item change of the hero's in the ledger, a drink's cooldown) and only when it differs from
/// what that client was last sent. A client that arrives (a join, a level change) or a hero
/// who spawns is sent it again whatever was sent before; a level change forgets every
/// cooldown and every send. Other players are never sent it.
/// </summary>
[SideService(Side.Server)]
public sealed class BeltSync(TF2DescentRules rules, HeroBelt belt, Heroes heroes, HeroKeys keys, IItemLedger ledger,
                             IBeltChannel channel, IEngineEntities entities)
    : IItemLedgerHook, IFrameLifecycle, IClientLifecycle, IPlayerSpawnHook, IGameLifecycle, IOrderedHook
{
    /// <summary>TF2 Descent's belt block, after the equipment's (First + 400..402).</summary>
    public const int Order = ModStages.First + 813;

    private readonly HashSet<int> _due = [];
    private readonly Dictionary<int, string> _sent = new();

    public int HookOrder => Order;

    public int FrameOrder => Order;

    public int ClientLifecycleOrder => Order;

    public int LifecycleOrder => Order;

    /// <summary>The heroes due a send at the end of this frame.</summary>
    public IReadOnlyCollection<int> Due => _due;

    public void OnItemsChanged(ItemsChanged change)
    {
        foreach (var h in heroes.All)
            if (h.PlayerIndex > 0 && keys.Of(h.PlayerIndex) == change.Hero)
                _due.Add(h.PlayerIndex);
    }

    public void OnPostThink(float frameTime)
    {
        foreach (int index in belt.TakeChanged())
            _due.Add(index);
        if (_due.Count == 0)
            return;
        foreach (int index in _due.ToArray())
        {
            // A client in before its hero (the component attaches after the activation) stays due.
            if (BeltOf(index) is not { } payload)
                continue;
            _due.Remove(index);
            Send(index, payload, force: false);
        }
    }

    /// <summary>The client is in: its belt goes again, whatever it was sent before (a level change, a reconnect).</summary>
    public void OnClientActive(int entIndex)
    {
        _sent.Remove(entIndex);
        _due.Add(entIndex);
    }

    public void OnClientDisconnected(int entIndex)
    {
        _sent.Remove(entIndex);
        _due.Remove(entIndex);
        belt.Forget(entIndex);
    }

    public void OnSpawning(EntityHandle player, float now)
    {
    }

    /// <summary>A hero spawned: his belt goes again.</summary>
    public void OnSpawned(EntityHandle player, float now)
    {
        int index = entities.GetIndex(player);
        _sent.Remove(index);
        _due.Add(index);
    }

    /// <summary>A level change: every cooldown and every send is forgotten.</summary>
    public void OnLevelShutdown()
    {
        _sent.Clear();
        _due.Clear();
        belt.ForgetAll();
    }

    /// <summary>The hero's belt as the wire carries it now; null for a player with no hero or no items.</summary>
    public Belt? BeltOf(int playerIndex)
    {
        if (heroes.Of(playerIndex) is null || keys.Of(playerIndex) is not { } key || ledger.Find(key) is not { } held)
            return null;
        int slots = Belt.Slots;
        var bases = new string[slots];
        var names = new string[slots];
        var counts = new byte[slots];
        for (int i = 0; i < slots; i++)
        {
            if (held.Inventory.Belt.TryGetValue(i, out var item))
            {
                bases[i] = Belt.Cut(item.Item.BaseId, Belt.MaxId);
                names[i] = Belt.Cut(rules.Content.ItemBases[item.Item.BaseId].Name, Belt.MaxName);
                counts[i] = (byte)Math.Clamp(item.Item.Count, 0, byte.MaxValue);
            }
            else
            {
                bases[i] = names[i] = "";
            }
        }
        var (ready, length) = belt.CooldownOf(playerIndex);
        return new Belt
        {
            Bases = string.Join(',', bases),
            Names = string.Join('|', names),
            Counts = counts,
            ReadyTick = ready,
            CooldownTicks = length,
        };
    }

    /// <summary>Sends the hero's belt to his own client when it differs from the last sent (or <paramref name="force"/>); answers whether it went.</summary>
    public bool Send(int playerIndex, bool force) => BeltOf(playerIndex) is { } payload && Send(playerIndex, payload, force);

    private bool Send(int playerIndex, Belt payload, bool force)
    {
        string key = $"{payload.Bases}|{payload.Names}|{string.Join(',', payload.Counts)}|{payload.ReadyTick}|{payload.CooldownTicks}";
        if (!force && _sent.TryGetValue(playerIndex, out var last) && last == key)
            return false;
        _sent[playerIndex] = key;
        channel.Send(playerIndex, payload);
        return true;
    }
}
