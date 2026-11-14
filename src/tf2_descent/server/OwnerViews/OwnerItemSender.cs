using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// THE OWNER'S ITEM VIEWS (plan_descent_logic UI1's last sentence, E4): every
/// change to a hero's items, as the item ledger tells it
/// (<see cref="IItemLedgerHook"/>), goes to that hero's own client and to no
/// other, one item a message (<see cref="OwnerItem"/>), and an item that left
/// his view as <see cref="OwnerItemGone"/>.
///
/// <list type="bullet">
/// <item><b>Resync.</b> At each spawn (the first on a level, after a level
/// change, after a death) the whole inventory goes again, bracketed by
/// <see cref="OwnerItems"/> Begin and End with its count: the client forgets
/// what it held at Begin and holds exactly what follows.</item>
/// <item><b>The decisions</b> a view carries are made here: whether the hero
/// meets each requirement now and whether an equipped item counts. They change
/// when the hero's stats do (<see cref="IHeroStatsWatcher"/>), so each item is
/// recomputed then, and sent again only when its bytes changed.</item>
/// <item><b>The purse and the backpack's capacity</b> go as an
/// <see cref="OwnerItemsPhase.State"/> when either changes.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class OwnerItemSender(
    IUserMessageSender sender,
    IItemLedger ledger,
    HeroKeys keys,
    Heroes heroes,
    HeroEquipment equipment,
    OwnerItemViews views,
    TF2DescentRules rules)
    : IItemLedgerHook, IHeroStatsWatcher, IPlayerSpawnHook, IClientLifecycle, IOrderedHook
{
    /// <summary>In TF2 Descent's owner-views block (UI3).</summary>
    public const int Order = ModStages.First + 1100;

    public int HookOrder => Order;

    public int ClientLifecycleOrder => Order;

    /// <summary>What was last sent each owner: each item's bytes by id, and the purse and capacity.</summary>
    private sealed class Sent
    {
        public readonly Dictionary<long, byte[]> Items = new();
        public long Australium = -1;
        public int Capacity = -1;
    }

    private readonly Dictionary<int, Sent> _sent = new();

    private uint Fingerprint => _fingerprint ??= ContentKeys.Fingerprint(rules.Content);
    private uint? _fingerprint;

    //-------------------------------------------------------------------------
    // The ledger's changes
    //-------------------------------------------------------------------------

    public void OnItemsChanged(ItemsChanged change)
    {
        if (OwnerOf(change.Hero) is not { } hero || ledger.Find(change.Hero) is not { } held)
            return;

        // The change names the items it touched; where each is NOW is the ledger's hero, read
        // after it: a hook told of this change may already have made another (the kit's settle
        // equips what a load put in the backpack), and that later change was told first.
        var sent = SentTo(hero.PlayerIndex);
        var gear = equipment.Of(hero.PlayerIndex);
        var now = held.Items.Where(i => i.Place != ItemPlace.BuyBack).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (long id in change.Added.Concat(change.Changed).Concat(change.Removed).Select(i => i.Id).Distinct())
        {
            if (now.TryGetValue(id, out var item))
                SendItem(hero, sent, item, gear, always: false);
            else
                SendGone(hero.PlayerIndex, sent, id);
        }

        SendState(hero, held, sent, always: false);
    }

    //-------------------------------------------------------------------------
    // The hero's stats: requirements and whether gear counts may have changed
    //-------------------------------------------------------------------------

    public void StatsChanged(HeroComponent hero)
    {
        if (hero.PlayerIndex <= 0 || keys.Of(hero.PlayerIndex) is not { } key || ledger.Find(key) is not { } held
            || !_sent.TryGetValue(hero.PlayerIndex, out var sent))
            return;

        var gear = equipment.Of(hero.PlayerIndex);
        foreach (var item in held.Items)
            SendItem(hero, sent, item, gear, always: false);
        SendState(hero, held, sent, always: false);
    }

    public void HeroLeft(int playerIndex) => _sent.Remove(playerIndex);

    public void OnClientDisconnected(int entIndex) => _sent.Remove(entIndex);

    //-------------------------------------------------------------------------
    // Resync
    //-------------------------------------------------------------------------

    public void OnSpawning(EntityHandle player, float now) { }

    public void OnSpawned(EntityHandle player, float now)
    {
        if (heroes.All.FirstOrDefault(h => h.Player == player) is { } hero)
            Resync(hero.PlayerIndex);
    }

    /// <summary>
    /// The hero in <paramref name="playerIndex"/>'s whole inventory to his client: Begin,
    /// every item, End with the count, then the purse and capacity. Nothing for a player
    /// with no hero or no loaded items (the load's own change will tell them).
    /// </summary>
    public void Resync(int playerIndex)
    {
        if (playerIndex <= 0 || heroes.Of(playerIndex) is not { } hero || keys.Of(playerIndex) is not { } key
            || ledger.Find(key) is not { } held)
            return;

        var sent = new Sent();
        _sent[playerIndex] = sent;
        var items = held.Items.Where(i => OwnerItemViews.ContainerOf(i.Place) is not null).ToList();
        var gear = equipment.Of(playerIndex);

        sender.Send(new OwnerItems { Phase = (byte)OwnerItemsPhase.Begin, Fingerprint = Fingerprint, Count = (ushort)items.Count }, playerIndex);
        foreach (var item in items)
            SendItem(hero, sent, item, gear, always: true);
        sender.Send(new OwnerItems { Phase = (byte)OwnerItemsPhase.End, Fingerprint = Fingerprint, Count = (ushort)items.Count }, playerIndex);
        SendState(hero, held, sent, always: true);
    }

    //-------------------------------------------------------------------------

    private HeroComponent? OwnerOf(string key) =>
        heroes.All.FirstOrDefault(h => h.PlayerIndex > 0 && keys.Of(h.PlayerIndex) == key);

    private Sent SentTo(int playerIndex)
    {
        if (!_sent.TryGetValue(playerIndex, out var sent))
            _sent[playerIndex] = sent = new Sent();
        return sent;
    }

    private void SendItem(HeroComponent hero, Sent sent, LedgerItem item, HeroGear? gear, bool always)
    {
        bool? counts = item.Place == ItemPlace.Equipped && gear is not null && item.EquipSlot is { } slot
                       && gear.Result.States.Any(s => s.Slot == slot)
            ? gear.Result.State(slot).State == SourceState.Active
            : null;
        if (views.Of(item, hero.Sheet, hero.Stats, counts) is not { } payload)
        {
            SendGone(hero.PlayerIndex, sent, item.Id);
            return;
        }

        byte[] bytes = Bytes(payload);
        if (!always && sent.Items.TryGetValue(item.Id, out var last) && last.AsSpan().SequenceEqual(bytes))
            return;
        sent.Items[item.Id] = bytes;
        sender.Send(payload, hero.PlayerIndex);
    }

    private void SendGone(int playerIndex, Sent sent, long id)
    {
        if (!sent.Items.Remove(id))
            return;
        sender.Send(new OwnerItemGone { Id = id }, playerIndex);
    }

    private void SendState(HeroComponent hero, LedgerHero held, Sent sent, bool always)
    {
        long australium = held.Inventory.Australium;
        int capacity = hero.Sheet is { } sheet ? rules.Inventory.BackpackSlots(sheet) : rules.Inventory.BackpackSlots(held.Sheet);
        if (!always && australium == sent.Australium && capacity == sent.Capacity)
            return;
        sent.Australium = australium;
        sent.Capacity = capacity;
        sender.Send(new OwnerItems
        {
            Phase = (byte)OwnerItemsPhase.State,
            Fingerprint = Fingerprint,
            BackpackCapacity = (ushort)Math.Clamp(capacity, 0, ushort.MaxValue),
            Australium = australium,
        }, hero.PlayerIndex);
    }

    private static byte[] Bytes(OwnerItem payload)
    {
        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = payload.Write(buffer);
        return n < 0 ? [] : buffer[..n].ToArray();
    }
}
