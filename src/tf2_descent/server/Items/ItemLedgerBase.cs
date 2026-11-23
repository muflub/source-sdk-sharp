using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
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
    private readonly Dictionary<(string Hero, string Vendor), (long Restock, List<VendorItem> Items)> _stock = new();
    private readonly Dictionary<long, long> _soldFor = new();
    private readonly Dictionary<string, Task> _queues = new(StringComparer.Ordinal);
    private readonly Dictionary<long, AustraliumPile> _piles = new();
    private readonly HashSet<(uint Seq, string Hero)> _broken = [];
    private long _nextId;

    protected ItemLedgerBase(TF2DescentRules rules, IHooks<IItemLedgerHook> hooks)
    {
        Rules = rules;
        _hooks = hooks;
    }

    /// <summary>The rules every transition is made of.</summary>
    protected TF2DescentRules Rules { get; }

    /// <summary>The same rules as the host calls them (Host.Contracts' members, the defaults included).</summary>
    protected IGameRules Contract => Rules;

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

    public void SetPrices(string hero, BuyerPrices prices)
    {
        if (_heroes.TryGetValue(hero, out var h))
            _heroes[hero] = h with { Prices = prices };
    }

    public virtual void Forget(string hero) => _heroes.Remove(hero);

    /// <summary>A new level: the floor, its piles and its breakables, and the vendors' offers were the last level's.</summary>
    public void NewLevel()
    {
        _floor.Clear();
        _piles.Clear();
        _broken.Clear();
        _offered.Clear();
        _stock.Clear();
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
                              IReadOnlyList<int>? Slots = null, IReadOnlyList<WearEvents>? Wear = null);

    /// <summary>What a confirmation committed (the hero after it), or why it was refused.</summary>
    protected sealed record Confirmed(LedgerHero? After, string? Refusal, IReadOnlyList<long>? Gone = null)
    {
        public static Confirmed No(string why) => new(null, why);
    }

    /// <summary>Loads a hero who is not in memory yet; null when they cannot be.</summary>
    protected abstract Task<LedgerHero?> LoadHero(string hero);

    /// <summary>Carries out a transition the rules allowed: <paramref name="predicted"/> is the rules' result over <paramref name="before"/>.</summary>
    protected abstract Task<Confirmed> Confirm(Op op, LedgerHero before, LedgerHero predicted);

    /// <summary>A vendor's stock for the hero in restock <paramref name="restock"/>, each item under a ledger id (its price is set by <see cref="Stock"/>).</summary>
    protected abstract Task<VendorAnswer> RollStock(LedgerHero hero, string vendor, long restock);

    /// <summary>Whether the hero's price stats move his prices: the in-memory ledger's own; hosted the host prices with none of his.</summary>
    protected virtual bool BuyerPricing => true;

    /// <summary>Whether one restock's stock is kept and shrinks as it is bought (the in-memory ledger); hosted the host's stock is asked each time.</summary>
    protected virtual bool KeepsStock => true;

    /// <summary>
    /// Whether a potion gained (picked up, bought, bought back, reclaimed, looted from the corpse)
    /// goes onto the belt (<see cref="InventoryRules.StowGained"/>, the whole run's F20): the
    /// in-memory ledger's; hosted the host carries no belt (X6), so it stays where the host put it.
    /// </summary>
    protected virtual bool StowsPotions => true;

    /// <summary><paramref name="result"/> with the potions it gained put on the belt, where this ledger does (<see cref="StowsPotions"/>).</summary>
    protected InventoryResult Stowing(LedgerHero before, InventoryResult result) =>
        StowsPotions ? Inv.StowGained(before.Inventory, result) : result;

    public abstract Task<DropResult> KillDrop(KillDrop kill);

    public abstract Task<DropResult> BossDrop(BossDrop boss);

    public abstract Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position);

    public abstract Task<DropResult> ContainerDrop(ContainerDrop drop);

    public abstract Task<bool> RecordDeath(string hero, Vector position);

    public abstract Task<LootAnswer> LootCorpse(string hero);

    /// <summary>What the hero's corpse holds; null when the ledger cannot say (the hosted ledger: the SDK lists no corpse).</summary>
    public virtual LedgerCorpse? Corpse(string hero) => null;

    /// <summary>Hosted, a corpse goes to the Lost &amp; Found when the host reaps its instance: nothing here.</summary>
    public virtual IReadOnlyList<string> CorpsesLost() => [];

    public virtual LedgerCorpse? LostAndFound(string hero) => null;

    public virtual long? ReclaimFee(string hero) => null;

    public virtual Task<LedgerResult> Reclaim(string hero) => Task.FromResult(LedgerResult.Refused(LedgerRefusals.NothingLost));

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

    public IReadOnlyCollection<AustraliumPile> Piles => _piles.Values;

    /// <summary>Puts a pile on the floor.</summary>
    protected AustraliumPile PlacePile(AustraliumPile pile) => _piles[pile.Id] = pile;

    /// <summary>The pile on the floor under <paramref name="id"/>; null when none is (taken, or never there).</summary>
    protected AustraliumPile? PileOf(long id) => _piles.GetValueOrDefault(id);

    /// <summary>Takes a pile off the floor.</summary>
    protected void UnplacePile(long id) => _piles.Remove(id);

    /// <summary>Marks the breakable broken by the hero; false when he broke it already (this level).</summary>
    protected bool MarkBroken(uint seq, string hero) => _broken.Add((seq, hero));

    /// <summary>
    /// <paramref name="amount"/> Australium into the hero's purse, as an operation of his after any in
    /// flight (one that commits later would put back the Australium it read). With
    /// <paramref name="load"/> false a hero not in memory is skipped: hosted, his next load reads what
    /// the host credited.
    /// </summary>
    protected Task<LedgerResult> Credit(string hero, long amount, string op, bool load) => Serial(hero, async () =>
        (load ? await Ensure(hero) : Find(hero)) is { } h
            ? Commit(op, h, h with { Inventory = h.Inventory with { Australium = h.Inventory.Australium + amount } }, [])
            : LedgerResult.Refused(LedgerRefusals.NoHero));

    public abstract Task<PileAnswer> KillAustralium(KillPile kill);

    public abstract Task<PileTake> ClaimAustralium(string hero, long pileId, IReadOnlyList<string> members);

    public abstract Task<BreakAnswer> Break(BreakDrop drop);

    public Task<LedgerResult> Claim(string hero, long floorId) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!_floor.TryGetValue(floorId, out var item))
            return LedgerResult.Refused(LedgerRefusals.NotOnFloor, h);
        if (item.ForHero is { } owner && owner != hero)
            return LedgerResult.Refused(LedgerRefusals.WrongOwner, h);
        var result = Stowing(h, Inv.Pickup(h.Inventory, h.Stash, h.Sheet, new Held(item.Id, item.Item)));
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

    /// <summary>A price as the hero meets it: moved by his price stats where they count (<see cref="BuyerPricing"/>).</summary>
    protected long Charge(LedgerHero h, long price, PriceKind kind) =>
        BuyerPricing ? Rules.Pricing.ForBuyer(price, kind, h.Prices) : price;

    private long BuyPrice(LedgerHero h, ItemInstance item) => Charge(h, Rules.Pricing.Price(item, PriceKind.Buy), PriceKind.Buy);

    private long SellPrice(LedgerHero h, ItemInstance item) => Charge(h, Rules.Price(Codec.ToRolled(item), PriceKind.Sell), PriceKind.Sell);

    public long? Price(string hero, LedgerPrice what, ItemInstance item)
    {
        if (Find(hero) is not { } h)
            return null;
        switch (what)
        {
            case LedgerPrice.Buy:
                return BuyPrice(h, item);
            case LedgerPrice.Sell:
                return SellPrice(h, item);
            case LedgerPrice.Identify:
                return item.Identified ? null : Charge(h, Rules.Pricing.IdentifyFee(item), PriceKind.Buy);
            default:
                return Rules.Repairs.Refusal(item) is null ? Charge(h, Rules.Repairs.Price(item), PriceKind.Buy) : null;
        }
    }

    public long? BuyBackPrice(string hero, int index) =>
        Find(hero) is { } h && index >= 0 && index < h.Inventory.BuyBack.Count ? SoldFor(h, h.Inventory.BuyBack[index]) : null;

    /// <summary>What a sold item is bought back for: what it sold for here, else its sell price now.</summary>
    private long SoldFor(LedgerHero h, Held sold) => _soldFor.TryGetValue(sold.Id, out long price) ? price : SellPrice(h, sold.Item);

    public Task<VendorAnswer> Stock(string hero, string vendor, long restock = 0) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return new VendorAnswer(false, LedgerRefusals.NoHero, []);
        if (!KeepsStock || !_stock.TryGetValue((hero, vendor), out var kept) || kept.Restock != restock)
        {
            var answer = await RollStock(h, vendor, restock);
            if (!answer.Ok)
                return answer;
            if (_stock.TryGetValue((hero, vendor), out var old))
                foreach (var item in old.Items)
                    _offered.Remove(item.Id);
            kept = (restock, [.. answer.Items]);
            _stock[(hero, vendor)] = kept;
            foreach (var item in kept.Items)
                _offered[item.Id] = (vendor, item);
        }
        return new VendorAnswer(true, null, [.. kept.Items.Select(i => i with { Price = BuyPrice(h, i.Item) })]);
    });

    public Task<LedgerResult> Buy(string hero, string vendor, long stockId) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!_offered.TryGetValue(stockId, out var offer) || offer.Vendor != vendor)
            return LedgerResult.Refused(LedgerRefusals.NotInStock, h);
        var result = Stowing(h, Inv.Buy(h.Inventory, h.Stash, h.Sheet, new Held(stockId, offer.Item.Item), BuyPrice(h, offer.Item.Item)));
        return await Settle(new Op("buy", Text: vendor, Id: stockId), h, result, () =>
        {
            _offered.Remove(stockId);
            if (_stock.TryGetValue((hero, vendor), out var kept))
                kept.Items.RemoveAll(i => i.Id == stockId);
        });
    });

    public Task<LedgerResult> Sell(string hero, string vendor, int slot)
    {
        long price = 0, id = 0;
        return Rule(hero, new Op("sell", slot, Text: vendor), h =>
        {
            if (!h.Inventory.Backpack.TryGetValue(slot, out var held))
                return new InventoryResult(h.Inventory, h.Stash, [], InventoryRefusals.NoItem);
            (price, id) = (SellPrice(h, held.Item), held.Id);
            return Inv.Sell(h.Inventory, h.Stash, slot, price);
        }, () => _soldFor[id] = price);
    }

    public Task<LedgerResult> BuyBack(string hero, string vendor, int index)
    {
        long id = 0;
        return Rule(hero, new Op("buy_back", index, Text: vendor), h =>
        {
            if (index < 0 || index >= h.Inventory.BuyBack.Count)
                return new InventoryResult(h.Inventory, h.Stash, [], InventoryRefusals.NoItem);
            id = h.Inventory.BuyBack[index].Id;
            return Stowing(h, Inv.BuyBack(h.Inventory, h.Stash, h.Sheet, index, SoldFor(h, h.Inventory.BuyBack[index])));
        }, () => _soldFor.Remove(id));
    }

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
    private Task<LedgerResult> InPlace(string hero, Op op, Func<LedgerHero, RolledItem, ItemChange> operate) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!h.Inventory.Backpack.TryGetValue(op.Slot, out var held))
            return LedgerResult.Refused(InventoryRefusals.NoItem, h);
        var change = operate(h, Codec.ToRolled(held.Item));
        if (!change.Ok)
            return LedgerResult.Refused(change.Refusal!, h);
        long cost = Charge(h, change.Cost, PriceKind.Buy);
        if (h.Inventory.Australium < cost)
            return LedgerResult.Refused(InventoryRefusals.NotEnoughAustralium, h);
        var result = Inv.Replace(h.Inventory, h.Stash, op.Slot, Codec.Instance(change.Produced[0]));
        return await Settle(op with { Id = held.Id }, h, result, cost: cost);
    });

    public Task<LedgerResult> Identify(string hero, int slot) =>
        InPlace(hero, new Op("identify", slot), (_, item) => Rules.Identify(item, RollSeed("identify")));

    /// <summary>The Engineer's repair by the hero (contract 1.2, the host's call): priced, or free when his perks waive it.</summary>
    public Task<LedgerResult> Repair(string hero, int slot) =>
        InPlace(hero, new Op("repair", slot), (h, item) => Contract.Repair(item, RepairKind.Town, null, Rules.SheetCodec.Write(h.Sheet), 0));

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
        Transform(hero, new Op("crate", slot), [slot], (h, inputs) => Rules.OpenCrate(inputs[0], TownContext(h, "crate"), CrateSeedOf(Codec.Instance(inputs[0]))));

    /// <summary>The seed a crate opens with: an in-memory roll's (the hosted ledger's crates are the host's).</summary>
    protected virtual ulong CrateSeedOf(ItemInstance crate) => RollSeed("crate");

    //-------------------------------------------------------------------------
    // HE8 and HE9: consumables, wear, repairs of what is worn
    //-------------------------------------------------------------------------

    public Task<LedgerResult> Consume(string hero, int slot) =>
        Rule(hero, new Op("consume", slot), h => Inv.Consume(h.Inventory, h.Stash, slot));

    public Task<LedgerResult> Wear(string hero, IReadOnlyList<WearEvents> events) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        var inventory = h.Inventory;
        var durability = Rules.ItemRules.Durability;
        foreach (var e in events)
        {
            if (e.Uses < 0 || e.Hits < 0)
                return LedgerResult.Refused(InventoryRefusals.Refused, h);
            if (inventory.Equipped.FirstOrDefault(p => p.Value.Id == e.ItemId) is { Value: { } worn } at)
            {
                var item = durability.Worn(worn.Item, e.Uses, e.Hits);
                inventory = inventory with { Equipped = inventory.Equipped.SetItem(at.Key, worn with { Item = item }) };
            }
            else if (inventory.Backpack.FirstOrDefault(p => p.Value.Id == e.ItemId) is { Value: { } carried } bp)
            {
                // Taken off before the write: its wear still counts where it is now.
                var item = durability.Worn(carried.Item, e.Uses, e.Hits);
                inventory = inventory with { Backpack = inventory.Backpack.SetItem(bp.Key, carried with { Item = item }) };
            }
        }
        return await Settle(new Op("wear", Wear: events), h, new InventoryResult(inventory, h.Stash, [], null));
    });

    public Task<LedgerResult> RepairWorn(string hero, string equipSlot, WornRepair how) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!h.Inventory.Equipped.TryGetValue(equipSlot, out var held))
            return LedgerResult.Refused(InventoryRefusals.NoItem, h);
        ItemInstance repaired;
        long cost = 0;
        int kitSlot = -1;
        if (how == WornRepair.Field)
        {
            // A field repair spends one Repair Kit from the backpack, the lowest slot first, at the hero's
            // level: the rules' Repair(item, Field, kit, sheet, seed), the call the host makes (contract 1.2),
            // so both ledgers agree.
            var kit = h.Inventory.Backpack.FirstOrDefault(p => p.Value.Item.BaseId == Rules.Repairs.Kit);
            if (kit.Value is null)
                return LedgerResult.Refused(RepairRefusals.NoKit, h);
            var field = Contract.Repair(Codec.ToRolled(held.Item), RepairKind.Field, Codec.ToRolled(kit.Value.Item), Rules.SheetCodec.Write(h.Sheet),
                RollSeed("field_repair"));
            if (!field.Ok)
                return LedgerResult.Refused(field.Refusal!, h);
            // Only the mutable half changes (ReplaceEquipped checks it): the worn item with the repair's numbers.
            var answer = Codec.Instance(field.Produced[0]);
            repaired = held.Item with { Durability = answer.Durability, MaxDurability = answer.MaxDurability, WearUses = answer.WearUses };
            kitSlot = kit.Key;
        }
        else
        {
            if (Rules.Repairs.Refusal(held.Item) is { } refusal)
                return LedgerResult.Refused(refusal, h);
            // The Engineer's price, waived by the dev command (TownUncharged) or by the hero's own perks (the
            // rules' town_free, the one waiver the host knows: contract 1.2's Repair reads it from his sheet).
            cost = how == WornRepair.Town && !Rules.TownFree(h.Sheet) ? Charge(h, Rules.Repairs.Price(held.Item), PriceKind.Buy) : 0;
            if (h.Inventory.Australium < cost)
                return LedgerResult.Refused(InventoryRefusals.NotEnoughAustralium, h);
            repaired = TF2.Descent.Rules.Economy.Repairs.Restore(held.Item);
        }
        var result = Inv.ReplaceEquipped(h.Inventory, h.Stash, equipSlot, repaired);
        if (kitSlot >= 0 && result.Ok)
            result = result with { Hero = result.Hero with { Backpack = result.Hero.Backpack.Remove(kitSlot) }, Left = [h.Inventory.Backpack[kitSlot]] };
        return await Settle(new Op(how == WornRepair.Field ? "field_repair_worn" : "repair_worn", kitSlot, (int)how, equipSlot, held.Id), h, result, cost: cost);
    });

    public Task<LedgerResult> SetDurability(string hero, string equipSlot, int durability, int maxDurability) =>
        Rule(hero, new Op("set_durability", Text: equipSlot), h =>
        {
            if (!h.Inventory.Equipped.TryGetValue(equipSlot, out var held))
                return new InventoryResult(h.Inventory, h.Stash, [], InventoryRefusals.NoItem);
            int max = Math.Max(0, maxDurability);
            return Inv.ReplaceEquipped(h.Inventory, h.Stash, equipSlot,
                held.Item with { MaxDurability = max, Durability = Math.Clamp(durability, 0, max), WearUses = 0 });
        });

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
