using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// WEAR IN PLAY (plan_descent_logic HE9; plan_tf2_rpg 2h, D21). Weapons wear on fire,
/// armour on hits taken, by the rules' <see cref="Durability"/>; the item is the ledger's,
/// so the wear is written through it.
///
/// <list type="bullet">
/// <item><b>What wears.</b> A shot or swing of a weapon a hero wears in a weapon slot (the
/// TF item fired is that slot's item's TF row) is one use of that item; a hit a hero takes
/// from anyone else is one hit on each armour piece he wears. An item that cannot wear (not
/// gear, indestructible, broken, unusable) gathers nothing.</item>
/// <item><b>The batching rule.</b> Uses and hits are kept here per hero and item, never a
/// ledger call a shot. At the end of each frame a hero is written only when his pending wear
/// would take at least one point off one of his items (the rules' own arithmetic, asked
/// with the pending counts): ONE ledger write for all his pending items, so a launcher costs
/// one write every 4 shots and a minigun one every 40. What is left below a point is written
/// at a level's end, at a disconnect and when an item with pending wear is taken off: the
/// ledger keeps progress toward the next point on the item (<see cref="ItemInstance.WearUses"/>),
/// so no use is lost across a map change. A write is keyed by the ledger's id, so an item
/// taken off first takes its wear in the backpack.</item>
/// <item>Hosted, the host has no durability call: the write is refused and the pending wear
/// is dropped (said once).</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroWear(IItemLedger ledger, HeroKeys keys, TF2DescentRules rules, ItemSchema schema, ILog log)
{
    /// <summary>The equipment slots a TF weapon can come from.</summary>
    private readonly string[] _weaponSlots = ["primary", "secondary", "melee"];

    private readonly Dictionary<string, Dictionary<long, (int Uses, int Hits)>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _players = new(StringComparer.Ordinal);
    private bool _refusalSaid;

    /// <summary>The ledger writes made (the batching's measure: points lost and flushes, never shots).</summary>
    public int Writes { get; private set; }

    /// <summary>A weapon of the hero in <paramref name="playerIndex"/> fired, swung or was used once (the TF item that did it: <paramref name="weapon"/>).</summary>
    public void Fired(int playerIndex, ItemDefinition? weapon)
    {
        if (weapon is null || Hero(playerIndex) is not { } h)
            return;
        foreach (var slot in _weaponSlots)
        {
            if (!h.Held.Inventory.Equipped.TryGetValue(slot, out var held) || !Wears(held.Item))
                continue;
            if (rules.Content.ItemBases[held.Item.BaseId].TFItem is not { Length: > 0 } row || schema.Item(row) is not { } definition
                || definition.Defindex != weapon.Defindex)
                continue;
            Add(h.Key, playerIndex, held.Id, uses: 1, hits: 0);
            return;
        }
    }

    /// <summary>The hero in <paramref name="playerIndex"/> took a hit: each armour piece he wears takes it.</summary>
    public void Hit(int playerIndex)
    {
        if (Hero(playerIndex) is not { } h)
            return;
        foreach (var (_, held) in h.Held.Inventory.Equipped)
            if (rules.Content.ItemBases[held.Item.BaseId].Kind == ItemKinds.Armour && Wears(held.Item))
                Add(h.Key, playerIndex, held.Id, uses: 0, hits: 1);
    }

    /// <summary>The wear an item has pending, not yet written.</summary>
    public (int Uses, int Hits) Pending(string hero, long itemId) =>
        _pending.TryGetValue(hero, out var items) && items.TryGetValue(itemId, out var p) ? p : (0, 0);

    public bool HasPending(string hero, long itemId) => Pending(hero, itemId) != (0, 0);

    /// <summary>The end of a frame: each hero whose pending wear would take a point is written (the batching rule).</summary>
    public void FlushDue()
    {
        if (_pending.Count == 0)
            return;
        foreach (var hero in _pending.Keys.ToArray())
            if (Due(hero))
                FlushHero(hero, "a point");
    }

    /// <summary>Writes the pending wear of the hero last seen in <paramref name="playerIndex"/> (a disconnect).</summary>
    public void Flush(int playerIndex, string why)
    {
        foreach (var hero in _players.Where(p => p.Value == playerIndex).Select(p => p.Key).ToArray())
            FlushHero(hero, why);
    }

    /// <summary>Writes the hero's pending wear, all of it, in one ledger write.</summary>
    public void FlushHero(string hero, string why)
    {
        if (!_pending.Remove(hero, out var items) || items.Count == 0)
            return;
        var events = items.Select(p => new WearEvents(p.Key, p.Value.Uses, p.Value.Hits)).ToArray();
        Writes++;
        _ = Write(hero, why, events);
    }

    /// <summary>Writes every hero's pending wear (a level's end).</summary>
    public void FlushAll(string why)
    {
        foreach (var hero in _pending.Keys.ToArray())
            FlushHero(hero, why);
    }

    private async Task Write(string hero, string why, IReadOnlyList<WearEvents> events)
    {
        var r = await ledger.Wear(hero, events);
        if (!r.Ok)
        {
            if (!_refusalSaid)
                log.Warning($"[TF2Descent/Wear] {hero}'s wear was not written ({why}): {r.Refusal}; pending wear is dropped while it is refused\n");
            _refusalSaid = true;
            return;
        }
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Wear] {hero} wear written ({why}): {string.Join(", ", events.Select(e => $"#{e.ItemId} {e.Uses} uses {e.Hits} hits"))}\n"));
    }

    private (string Key, LedgerHero Held)? Hero(int playerIndex) =>
        keys.Of(playerIndex) is { } key && ledger.Find(key) is { } held ? (key, held) : null;

    private bool Wears(ItemInstance item) =>
        item.Durability > 0 && item.MaxDurability > 0 && rules.ItemRules.Durability.PerPoint(item) > 0
        && !rules.ItemRules.Durability.IsIndestructible(item);

    private void Add(string hero, int playerIndex, long itemId, int uses, int hits)
    {
        if (!_pending.TryGetValue(hero, out var items))
            _pending[hero] = items = new Dictionary<long, (int Uses, int Hits)>();
        var p = items.GetValueOrDefault(itemId);
        items[itemId] = (p.Uses + uses, p.Hits + hits);
        _players[hero] = playerIndex;
    }

    /// <summary>True when the hero's pending wear would take a point off any of his items (asked of the rules with the pending counts).</summary>
    private bool Due(string hero)
    {
        if (ledger.Find(hero) is not { } held)
            return true;   // gone from the ledger: the write is refused and the pending dropped
        var d = rules.ItemRules.Durability;
        foreach (var (id, (uses, hits)) in _pending[hero])
        {
            var item = held.Inventory.Equipped.Values.Concat(held.Inventory.Backpack.Values).FirstOrDefault(h => h.Id == id)?.Item;
            if (item is null || d.Hit(d.Use(item, uses).Item, hits).Item.Durability != item.Durability)
                return true;
        }
        return false;
    }
}

/// <summary>
/// The wear's taps (HE9): TF2's weapon chain tells of each shot or swing after it fired
/// (<see cref="ITFWeaponHook.AfterFire"/>, the server's live run only), and the core tells
/// of each hit on a player (<see cref="IPlayerHurtHook"/>): a hero's shot is a use of the
/// weapon he wears, a hit on a hero from anyone else (not his own blast) a hit on his armour.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroWearTaps(HeroWear wear, IDescentModeState mode, IEngineEntities entities) : ITFWeaponHook, IPlayerHurtHook
{
    /// <summary>TF2 Descent's wear block (the weapon chain runs TF2's own hooks first whatever the number).</summary>
    public const int Order = ModStages.First + 810;

    public int HookOrder => Order;

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (!mode.Active || !attack.EffectsLive || weapon.OwnerIndex <= 0)
            return;
        wear.Fired(weapon.OwnerIndex, (weapon.Item as AttributeContainer)?.Item);
    }

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (!mode.Active || hurt.Taken <= 0 || hurt.Attacker == hurt.Player)
            return;
        wear.Hit(entities.GetIndex(hurt.Player));
    }
}

/// <summary>
/// When the wear is written (HE9's batching rule, <see cref="HeroWear"/>): at the end of each
/// frame for the heroes a point is due for, at a level's end for everyone, at a disconnect
/// for the hero leaving (before the ledger forgets a hosted hero, First + 7), and when an item
/// with pending wear leaves a hero's equipment.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroWearFlush(HeroWear wear) : IFrameLifecycle, IGameLifecycle, IClientLifecycle, IItemLedgerHook, IOrderedHook
{
    public const int Order = ModStages.First + 811;

    public int HookOrder => Order;

    public int FrameOrder => Order;

    public int LifecycleOrder => Order;

    /// <summary>Before the ledger forgets a leaving hosted hero (First + 7).</summary>
    public int ClientLifecycleOrder => ModStages.First + 5;

    public void OnPostThink(float frameTime) => wear.FlushDue();

    public void OnLevelShutdown() => wear.FlushAll("level end");

    public void OnClientDisconnected(int entIndex) => wear.Flush(entIndex, "disconnect");

    public void OnItemsChanged(ItemsChanged change)
    {
        if (change.Operation == "wear")
            return;
        foreach (var item in change.Changed.Concat(change.Removed))
        {
            if (item.Place != ItemPlace.Equipped && wear.HasPending(change.Hero, item.Id))
            {
                wear.FlushHero(change.Hero, "taken off");
                return;
            }
        }
    }
}
