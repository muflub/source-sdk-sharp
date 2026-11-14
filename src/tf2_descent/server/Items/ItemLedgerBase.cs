using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>
/// What both ledgers share: each operation is the rules' transition, defined once here. The
/// rules decide first, over the hero's current state, so a refusal is the rules' own and
/// changes nothing. A transition the rules allow is then <see cref="Confirm"/>ed: the
/// in-memory ledger takes it as is; the hosted ledger asks the host and commits only what the
/// host did. A commit stores the hero and tells every <see cref="IItemLedgerHook"/> once.
/// Operations on one hero run one after another, so a hosted one in flight never sees
/// another's half.
/// </summary>
public abstract class ItemLedgerBase : IItemLedger
{
    private readonly IHooks<IItemLedgerHook> _hooks;
    private readonly Dictionary<string, LedgerHero> _heroes = new(StringComparer.Ordinal);
    private readonly Dictionary<long, FloorItem> _floor = new();
    private readonly Dictionary<long, (string Vendor, VendorItem Item)> _offered = new();
    private readonly Dictionary<string, Task> _queues = new(StringComparer.Ordinal);
    private long _nextId;

    protected ItemLedgerBase(TF2DescentRules rules, IHooks<IItemLedgerHook> hooks)
    {
        Rules = rules;
        _hooks = hooks;
    }

    /// <summary>The rules every transition is made of.</summary>
    protected TF2DescentRules Rules { get; }

    private InventoryRules Inv => Rules.Inventory;

    private ItemCodec Codec => Rules.ItemRules.Codec;

    public abstract LedgerKind Kind { get; }

    public IReadOnlyCollection<FloorItem> Floor => _floor.Values;

    public LedgerHero? Find(string hero) => _heroes.GetValueOrDefault(hero);

    public void SetSheet(string hero, HeroSheet sheet)
    {
        if (_heroes.TryGetValue(hero, out var h))
            _heroes[hero] = h with { Sheet = sheet };
    }

    public virtual void Forget(string hero) => _heroes.Remove(hero);

    /// <summary>A new level: the floor and the vendors' offers were the last level's.</summary>
    public void NewLevel()
    {
        _floor.Clear();
        _offered.Clear();
    }

    /// <summary>Sets a hero's state as it stands (facts only: a test's Australium, a planted sheet).</summary>
    internal void Replace(LedgerHero hero) => _heroes[hero.Hero] = hero;

    /// <summary>A fresh id for an item this ledger names.</summary>
    protected long NextId() => ++_nextId;

    //-------------------------------------------------------------------------
    // What each ledger supplies
    //-------------------------------------------------------------------------

    /// <summary>The operation a transition carries out, for <see cref="Confirm"/>: its name and its arguments.</summary>
    protected sealed record Op(string Name, int Slot = -1, int To = -1, string? Text = null, long Id = 0, Vector Position = default,
                              IReadOnlyList<int>? Slots = null);

    /// <summary>What a confirmation committed (the hero after it), or why it was refused.</summary>
    protected sealed record Confirmed(LedgerHero? After, string? Refusal, IReadOnlyList<long>? Gone = null)
    {
        public static Confirmed No(string why) => new(null, why);
    }

    /// <summary>Loads a hero who is not in memory yet; null when they cannot be.</summary>
    protected abstract Task<LedgerHero?> LoadHero(string hero);

    /// <summary>Carries out a transition the rules allowed: <paramref name="predicted"/> is the rules' result over <paramref name="before"/>.</summary>
    protected abstract Task<Confirmed> Confirm(Op op, LedgerHero before, LedgerHero predicted);

    /// <summary>A vendor's stock for the hero, each item under a ledger id.</summary>
    protected abstract Task<VendorAnswer> RollStock(LedgerHero hero, string vendor);

    public abstract Task<DropResult> KillDrop(KillDrop kill);

    public abstract Task<DropResult> BossDrop(BossDrop boss);

    public abstract Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position);

    public abstract Task<bool> RecordDeath(string hero, Vector position);

    public abstract Task<LootAnswer> LootCorpse(string hero);

    //-------------------------------------------------------------------------
    // State
    //-------------------------------------------------------------------------

    public Task<LedgerResult> Load(string hero) => Serial(hero, async () =>
        await Ensure(hero) is { } h ? new LedgerResult(true, null, h, null) : LedgerResult.Refused(LedgerRefusals.NoHero));

    /// <summary>The hero, loaded on first use.</summary>
    protected async Task<LedgerHero?> Ensure(string hero)
    {
        if (_heroes.TryGetValue(hero, out var h))
            return h;
        if (await LoadHero(hero) is not { } loaded)
            return null;
        // A load is a change too: what the hero holds comes into view.
        var empty = loaded with { Inventory = new HeroInventory(), Stash = new StashState() };
        return Commit("load", empty, loaded, []).Hero;
    }

    /// <summary>Stores <paramref name="after"/> and tells the hooks what changed from <paramref name="before"/>.</summary>
    protected LedgerResult Commit(string op, LedgerHero before, LedgerHero after, IReadOnlyList<long> gone)
    {
        _heroes[after.Hero] = after;
        var change = Diff(op, before, after, gone);
        if (change is not null)
            _hooks.Each(nameof(IItemLedgerHook.OnItemsChanged), h => h.OnItemsChanged(change));
        return new LedgerResult(true, null, after, change);
    }

    private static bool Same(LedgerItem a, LedgerItem b) =>
        a.Place == b.Place && a.Slot == b.Slot && a.EquipSlot == b.EquipSlot
        && (ReferenceEquals(a.Item, b.Item) || ItemCodec.Encode(a.Item).AsSpan().SequenceEqual(ItemCodec.Encode(b.Item)));

    /// <summary>The change from one view of a hero to the next; null when nothing changed.</summary>
    internal static ItemsChanged? Diff(string op, LedgerHero before, LedgerHero after, IReadOnlyList<long> gone)
    {
        var was = before.Items.ToDictionary(i => i.Id);
        var now = after.Items.ToDictionary(i => i.Id);
        var added = now.Values.Where(i => !was.ContainsKey(i.Id)).ToList();
        var removed = was.Values.Where(i => !now.ContainsKey(i.Id)).ToList();
        var changed = now.Values.Where(i => was.TryGetValue(i.Id, out var w) && !Same(w, i)).ToList();
        long a0 = before.Inventory.Australium, a1 = after.Inventory.Australium;
        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0 && gone.Count == 0 && a0 == a1)
            return null;
        return new ItemsChanged(after.Hero, op, added, removed, changed, gone, a0, a1);
    }

    /// <summary>Runs one hero's operations one after another, in the order asked.</summary>
    protected Task<T> Serial<T>(string hero, Func<Task<T>> work)
    {
        var previous = _queues.GetValueOrDefault(hero) ?? Task.CompletedTask;
        var next = After(previous, work);
        _queues[hero] = next;
        return next;
    }

    private static async Task<T> After<T>(Task previous, Func<Task<T>> work)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // The earlier operation's caller has its failure; this one still runs.
        }
        return await work();
    }

    /// <summary>The rules decide over the hero; a transition they allow is confirmed and committed.</summary>
    private Task<LedgerResult> Rule(string hero, Op op, Func<LedgerHero, InventoryResult> rule, Action? committed = null) =>
        Serial(hero, async () =>
        {
            if (await Ensure(hero) is not { } h)
                return LedgerResult.Refused(LedgerRefusals.NoHero);
            var result = rule(h);
            return await Settle(op, h, result, committed);
        });

    private async Task<LedgerResult> Settle(Op op, LedgerHero h, InventoryResult result, Action? committed = null, long cost = 0)
    {
        if (!result.Ok)
            return LedgerResult.Refused(result.Refusal!, h);
        var predicted = h with
        {
            Inventory = result.Hero with { Australium = result.Hero.Australium - cost },
            Stash = result.Stash,
        };
        var confirmed = await Confirm(op, h, predicted);
        if (confirmed.Refusal is not null)
            return LedgerResult.Refused(confirmed.Refusal, Find(h.Hero));
        committed?.Invoke();
        return Commit(op.Name, h, confirmed.After!, confirmed.Gone ?? [.. result.Left.Select(l => l.Id)]);
    }

    //-------------------------------------------------------------------------
    // The floor
    //-------------------------------------------------------------------------

    /// <summary>Puts an item on the floor.</summary>
    protected FloorItem Place(FloorItem item)
    {
        _floor[item.Id] = item;
        return item;
    }

    /// <summary>Takes an item off the floor (a forged reveal the host destroyed).</summary>
    protected void Unplace(long id) => _floor.Remove(id);

    public Task<LedgerResult> Claim(string hero, long floorId) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!_floor.TryGetValue(floorId, out var item))
            return LedgerResult.Refused(LedgerRefusals.NotOnFloor, h);
        if (item.ForHero is { } owner && owner != hero)
            return LedgerResult.Refused(LedgerRefusals.WrongOwner, h);
        var result = Inv.Pickup(h.Inventory, h.Stash, h.Sheet, new Held(item.Id, item.Item));
        return await Settle(new Op("claim", Id: item.Id), h, result, () => _floor.Remove(floorId));
    });

    /// <summary>Australium off the floor into the purse, as an operation of its own the ledger confirms (the host has none).</summary>
    public Task<LedgerResult> CollectAustralium(string hero, long amount) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (amount <= 0)
            return LedgerResult.Refused(LedgerRefusals.BadAmount, h);
        var predicted = h with { Inventory = h.Inventory with { Australium = h.Inventory.Australium + amount } };
        var confirmed = await Confirm(new Op("australium", Id: amount), h, predicted);
        if (confirmed.Refusal is not null)
            return LedgerResult.Refused(confirmed.Refusal, Find(h.Hero));
        return Commit("australium", h, confirmed.After!, []);
    });

    public Task<LedgerResult> Drop(string hero, int slot, Vector position) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        var result = Inv.Drop(h.Inventory, h.Stash, slot);
        if (!result.Ok)
            return LedgerResult.Refused(result.Refusal!, h);
        var dropped = result.Left[0];
        var answer = await Settle(new Op("drop", slot, Id: dropped.Id, Position: position), h, result with { Left = [] },
            () => Place(new FloorItem(dropped.Id, dropped.Item, null, position)));
        return answer;
    });

    //-------------------------------------------------------------------------
    // Moves, equipment, belt, stash
    //-------------------------------------------------------------------------

    public Task<LedgerResult> Move(string hero, int from, int to) =>
        Rule(hero, new Op("move", from, to), h => Inv.Move(h.Inventory, h.Stash, h.Sheet, from, to));

    public Task<LedgerResult> Stack(string hero, int from, int to) =>
        Rule(hero, new Op("stack", from, to), h => Inv.Stack(h.Inventory, h.Stash, h.Sheet, from, to));

    public Task<LedgerResult> Equip(string hero, int from, string slot) =>
        Rule(hero, new Op("equip", from, Text: slot), h => Inv.Equip(h.Inventory, h.Stash, h.Sheet, from, slot));

    public Task<LedgerResult> Unequip(string hero, string slot) =>
        Rule(hero, new Op("unequip", Text: slot), h => Inv.Unequip(h.Inventory, h.Stash, h.Sheet, slot));

    public Task<LedgerResult> ToBelt(string hero, int from, int index) =>
        Rule(hero, new Op("to_belt", from, index), h => Inv.ToBelt(h.Inventory, h.Stash, from, index));

    public Task<LedgerResult> FromBelt(string hero, int index) =>
        Rule(hero, new Op("from_belt", index), h => Inv.FromBelt(h.Inventory, h.Stash, h.Sheet, index));

    public Task<LedgerResult> Drink(string hero, int index) =>
        Rule(hero, new Op("drink", index), h => Inv.Drink(h.Inventory, h.Stash, index));

    /// <summary>The stash's capacity for this hero: the rules' over the account's living characters, which the ledger knows as this hero alone.</summary>
    protected int StashCapacity(LedgerHero h) => Inv.StashSlots([h.Sheet]);

    public Task<LedgerResult> Deposit(string hero, int from) =>
        Rule(hero, new Op("deposit", from), h => Inv.Deposit(h.Inventory, h.Stash, StashCapacity(h), from));

    public Task<LedgerResult> Withdraw(string hero, int stashSlot) =>
        Rule(hero, new Op("withdraw", stashSlot), h => Inv.Withdraw(h.Inventory, h.Stash, h.Sheet, stashSlot));

    //-------------------------------------------------------------------------
    // Vendors
    //-------------------------------------------------------------------------

    public Task<VendorAnswer> Stock(string hero, string vendor) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return new VendorAnswer(false, LedgerRefusals.NoHero, []);
        var answer = await RollStock(h, vendor);
        foreach (var item in answer.Items)
            _offered[item.Id] = (vendor, item);
        return answer;
    });

    public Task<LedgerResult> Buy(string hero, string vendor, long stockId) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!_offered.TryGetValue(stockId, out var offer) || offer.Vendor != vendor)
            return LedgerResult.Refused(LedgerRefusals.NotInStock, h);
        var result = Inv.Buy(h.Inventory, h.Stash, h.Sheet, new Held(stockId, offer.Item.Item), offer.Item.Price);
        return await Settle(new Op("buy", Text: vendor, Id: stockId), h, result, () => _offered.Remove(stockId));
    });

    private long SellPrice(ItemInstance item) => Rules.Price(Codec.ToRolled(item), PriceKind.Sell);

    public Task<LedgerResult> Sell(string hero, string vendor, int slot) =>
        Rule(hero, new Op("sell", slot, Text: vendor), h => h.Inventory.Backpack.TryGetValue(slot, out var held)
            ? Inv.Sell(h.Inventory, h.Stash, slot, SellPrice(held.Item))
            : new InventoryResult(h.Inventory, h.Stash, [], InventoryRefusals.NoItem));

    public Task<LedgerResult> BuyBack(string hero, string vendor, int index) =>
        Rule(hero, new Op("buy_back", index, Text: vendor), h => index >= 0 && index < h.Inventory.BuyBack.Count
            ? Inv.BuyBack(h.Inventory, h.Stash, h.Sheet, index, SellPrice(h.Inventory.BuyBack[index].Item))
            : new InventoryResult(h.Inventory, h.Stash, [], InventoryRefusals.NoItem));

    //-------------------------------------------------------------------------
    // Identify, repair, craft, salvage, crates
    //-------------------------------------------------------------------------

    /// <summary>The seed an in-memory roll of <paramref name="what"/> uses; the hosted ledger's rolls are the host's.</summary>
    protected abstract ulong RollSeed(string what);

    /// <summary>The roll context of a town operation for the hero (the host's: the hero's class and level, depth 1's row).</summary>
    protected ItemRollContext TownContext(LedgerHero h, string source)
    {
        var row = Rules.LevelTable(1);
        return new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, 0, row.ItemLevel, 0, row.LootTable, source);
    }

    /// <summary>Identify and repair change the item in place, as the host does: the same id, the rules' changed item, the cost paid.</summary>
    private Task<LedgerResult> InPlace(string hero, Op op, Func<RolledItem, ItemChange> operate) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!h.Inventory.Backpack.TryGetValue(op.Slot, out var held))
            return LedgerResult.Refused(InventoryRefusals.NoItem, h);
        var change = operate(Codec.ToRolled(held.Item));
        if (!change.Ok)
            return LedgerResult.Refused(change.Refusal!, h);
        if (h.Inventory.Australium < change.Cost)
            return LedgerResult.Refused(InventoryRefusals.NotEnoughAustralium, h);
        var result = Inv.Replace(h.Inventory, h.Stash, op.Slot, Codec.Instance(change.Produced[0]));
        return await Settle(op with { Id = held.Id }, h, result, cost: change.Cost);
    });

    public Task<LedgerResult> Identify(string hero, int slot) =>
        InPlace(hero, new Op("identify", slot), item => Rules.Identify(item, RollSeed("identify")));

    public Task<LedgerResult> Repair(string hero, int slot) =>
        InPlace(hero, new Op("repair", slot), item => Rules.Repair(item));

    /// <summary>An operation that consumes backpack inputs and produces outputs, all at once (the rules' <see cref="InventoryRules.Apply"/>).</summary>
    private Task<LedgerResult> Transform(string hero, Op op, IReadOnlyList<int> slots, Func<LedgerHero, IReadOnlyList<RolledItem>, ItemChange> operate) =>
        Serial(hero, async () =>
        {
            if (await Ensure(hero) is not { } h)
                return LedgerResult.Refused(LedgerRefusals.NoHero);
            if (slots.Count == 0 || slots.Any(s => !h.Inventory.Backpack.ContainsKey(s)))
                return LedgerResult.Refused(InventoryRefusals.NoItem, h);
            var change = operate(h, [.. slots.Select(s => Codec.ToRolled(h.Inventory.Backpack[s].Item))]);
            if (!change.Ok)
                return LedgerResult.Refused(change.Refusal!, h);
            var ids = change.Produced.Select(_ => NextId()).ToList();
            var result = Inv.Apply(h.Inventory, h.Stash, h.Sheet, slots, change, ids);
            return await Settle(op, h, result);
        });

    public Task<LedgerResult> Craft(string hero, string recipe, IReadOnlyList<int> slots) =>
        Transform(hero, new Op("craft", Text: recipe, Slots: slots), slots,
            (h, inputs) => Rules.Craft(recipe, inputs, TownContext(h, "craft"), RollSeed("craft")));

    public Task<LedgerResult> Salvage(string hero, int slot) =>
        Transform(hero, new Op("salvage", slot), [slot], (h, inputs) => Rules.Salvage(inputs[0], TownContext(h, "salvage"), RollSeed("salvage")));

    public Task<LedgerResult> OpenCrate(string hero, int slot) =>
        Transform(hero, new Op("crate", slot), [slot], (h, inputs) => Rules.OpenCrate(inputs[0], TownContext(h, "crate"), RollSeed("crate")));

    //-------------------------------------------------------------------------
    // The dev mint
    //-------------------------------------------------------------------------

    /// <summary>The source a dev mint rolls under: none of items.json's prefixes, so the rarity's own rules hold.</summary>
    public const string GiveSource = "dev_give";

    public Task<LedgerResult> Give(string hero, string baseId, int rarity, int itemLevel) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!Rules.Content.ItemBases.Contains(baseId))
            return LedgerResult.Refused(LedgerRefusals.UnknownBase, h);
        int tier = Math.Clamp(rarity, 0, Rules.Content.Rarities.Count - 1);
        int level = Math.Clamp(itemLevel, 1, Rules.Content.ItemSettings.MaxItemLevel);
        var ctx = new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, 0, level, tier,
            Rules.Content.ItemSettings.ForcedBasePrefix + baseId, GiveSource);
        var item = new Held(NextId(), Rules.ItemRules.Roller.Roll(ctx, RollSeed("give")));
        var result = Inv.Pickup(h.Inventory, h.Stash, h.Sheet, item);
        return await Settle(new Op("give", Text: baseId, Id: item.Id), h, result);
    });
}
