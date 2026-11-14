using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// A host for the hosted ledger's facts: the item ledger the hosting service keeps, cut to what
/// the game sees (the host's DescentLedger is the model: an item has one owner and a slot; a
/// claim needs the item on the floor, dropped for the claimer or for nobody, and a free slot
/// below capacity; a drop clears the owner; buy-back is per vendor and keeps 12; identify and
/// repair change the item in place). Every call is recorded as "op steamId"; an op named in
/// <see cref="Failing"/> answers unreachable and changes nothing.
/// </summary>
internal sealed class FakeHostItems(TF2DescentRules r) : IHostItems
{
    public const int BuyBackKept = 12;

    private readonly Dictionary<(string Hero, int Tier), Queue<string>> _reserve = new();
    private readonly Dictionary<string, long> _corpseAustralium = new(StringComparer.Ordinal);
    private int _next;
    private ulong _seed;

    public List<string> Calls { get; } = [];
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>Reveals that never answer: the kill path must not wait on one.</summary>
    public bool HoldReveals { get; set; }

    public Dictionary<string, HostItem> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HeroSheet> Sheets { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Australium { get; } = new(StringComparer.Ordinal);

    public void AddCharacter(string hero, HeroSheet sheet, long australium = 0)
    {
        Sheets[hero] = sheet;
        Australium[hero] = australium;
    }

    private HostItem Mint(ItemInstance i, HostOwnerKind owner, string ownerId, int slot, string forCharacter = "") =>
        new($"h{++_next}", i.Seed, i.BaseId, i.Rarity, i.ItemLevel, i.Count, i.Identified,
            new OpaquePayload(ItemCodec.Encode(i), ItemCodec.Schema), owner, ownerId, slot, 1, forCharacter, "instance", i.Tier);

    private HostItem Store(HostItem item) => Items[item.Id] = item;

    private ItemInstance Instance(HostItem h) => ItemCodec.DecodeInstance(h.Instance.Data);

    private RolledItem Rolled(HostItem h) => r.ItemRules.Codec.ToRolled(Instance(h));

    private HostItem With(HostItem h, ItemInstance changed) => Store(h with
    {
        Instance = new OpaquePayload(ItemCodec.Encode(changed), ItemCodec.Schema),
        Identified = changed.Identified,
        Count = changed.Count,
        Version = h.Version + 1,
    });

    /// <summary>Queues <paramref name="item"/> in the hero's reserve for <paramref name="tier"/>.</summary>
    public HostItem Reserve(string hero, int tier, ItemInstance item)
    {
        var minted = Store(Mint(item, HostOwnerKind.Reserve, hero, -1));
        if (!_reserve.TryGetValue((hero, tier), out var q))
            _reserve[(hero, tier)] = q = new Queue<string>();
        q.Enqueue(minted.Id);
        return minted;
    }

    public IEnumerable<HostItem> Owned(HostOwnerKind kind, string ownerId) =>
        Items.Values.Where(i => i.Owner == kind && i.OwnerId == ownerId);

    private int Capacity(string hero) => r.Inventory.BackpackSlots(Sheets[hero]);

    private int FreeSlot(string hero)
    {
        var used = Owned(HostOwnerKind.Character, hero).Select(i => i.Slot).ToHashSet();
        for (int s = 0; s < Capacity(hero); s++)
            if (!used.Contains(s)) return s;
        return -1;
    }

    private static HostResult<T> No<T>(string reason, HostError error = HostError.Rejected) => HostResult<T>.Refused(new HostRefusal(error, reason, ""));

    private bool Fails(string op, string hero)
    {
        Calls.Add($"{op} {hero}");
        return Failing.Contains(op);
    }

    private static HostResult<T> Unreachable<T>() => No<T>("unreachable", HostError.Unreachable);

    private HostItem? OwnedBy(string hero, string id) =>
        Items.TryGetValue(id, out var i) && i.Owner == HostOwnerKind.Character && i.OwnerId == hero ? i : null;

    private ItemRollContext Ctx(string hero, string source, int tier = 0, int depth = 0)
    {
        var row = r.LevelTable(Math.Max(depth, 1));
        return new ItemRollContext(Sheets[hero].ClassId, Sheets[hero].Level, depth, row.ItemLevel, tier, row.LootTable, source);
    }

    //-------------------------------------------------------------------------

    public Task<HostResult<HostInventory>> Inventory(string steamId, bool stash)
    {
        if (Fails("inventory", steamId))
            return Task.FromResult(Unreachable<HostInventory>());
        if (!Sheets.TryGetValue(steamId, out var sheet))
            return Task.FromResult(No<HostInventory>("no_lease", HostError.NoCharacter));
        var written = r.SheetCodec.Write(sheet);
        var character = new HostCharacter(steamId, "acct-" + steamId, sheet.ClassId, sheet.Name, false, false, sheet.Level, 0, 0, 0,
            new OpaquePayload(written.Data, (uint)written.SchemaVersion), Australium[steamId], 1);
        IReadOnlyList<HostItem> stashed = stash ? [.. Owned(HostOwnerKind.Stash, "stash:" + steamId)] : [];
        return Task.FromResult(HostResult<HostInventory>.Success(new HostInventory(character, [.. Owned(HostOwnerKind.Character, steamId)], stashed)));
    }

    public Task<HostItem?> TakeReserved(string steamId, int tier)
    {
        Calls.Add($"next {steamId}");
        return Task.FromResult(_reserve.TryGetValue((steamId, tier), out var q) && q.TryDequeue(out var id) ? Items[id] : null);
    }

    /// <summary>When set, a reveal answers only once it completes.</summary>
    public TaskCompletionSource? RevealGate { get; set; }

    /// <summary>When set, an identify or a repair answers only once it completes.</summary>
    public TaskCompletionSource? InPlaceGate { get; set; }

    /// <summary>The Australium a reveal books for the kill (the host's KillRoll.Australium).</summary>
    public long RevealBooks { get; set; }

    public async Task<HostResult<RevealOutcome>> Reveal(string steamId, HostItem item, uint killSeq, string robot, IReadOnlyList<string> party, Vector position)
    {
        if (Fails("reveal", steamId))
            return Unreachable<RevealOutcome>();
        if (HoldReveals)
            return await new TaskCompletionSource<HostResult<RevealOutcome>>().Task;
        if (RevealGate is { } gate)
            await gate.Task;
        var moved = Store(Items[item.Id] with { Owner = HostOwnerKind.World, OwnerId = "instance", ForCharacter = steamId });
        Australium[steamId] += RevealBooks;
        return HostResult<RevealOutcome>.Success(new RevealOutcome(moved, RevealBooks));
    }

    public Task<HostResult<IReadOnlyList<HostItem>>> MintDrops(string steamId, uint killSeq, string robot, int tier, IReadOnlyList<string> party, Vector position)
    {
        if (Fails("mint", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostItem>>());
        var minted = new List<HostItem>();
        if (tier >= 0)
        {
            minted.Add(Store(Mint(r.ItemRules.Roller.Roll(Ctx(steamId, "fallback", tier, 1), ++_seed), HostOwnerKind.World, "instance", -1, steamId)));
        }
        else
        {
            foreach (var member in party.Prepend(steamId).Distinct().Where(Sheets.ContainsKey))
                foreach (var drop in r.BossDrops(Ctx(member, "boss:" + robot, -1, 1), robot, ++_seed))
                    minted.Add(Store(Mint(r.ItemRules.Codec.Instance(drop), HostOwnerKind.World, "instance", -1, member)));
        }
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success(minted));
    }

    public Task<HostResult<HostItem>> Claim(string steamId, string itemId)
    {
        if (Fails("claim", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (!Items.TryGetValue(itemId, out var item) || item.Owner != HostOwnerKind.World)
            return Task.FromResult(No<HostItem>("no_item", HostError.NoItem));
        if (item.ForCharacter.Length > 0 && item.ForCharacter != steamId)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        int slot = FreeSlot(steamId);
        if (slot < 0)
            return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.Character, OwnerId = steamId, Slot = slot, ForCharacter = "" })));
    }

    public Task<HostResult<HostItem>> Drop(string steamId, string itemId, Vector position)
    {
        if (Fails("drop", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.World, OwnerId = "instance", Slot = -1, ForCharacter = "" })));
    }

    public Task<HostResult<HostItem>> Move(string steamId, string itemId, int slot)
    {
        if (Fails("move", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        if (slot < 0 || slot >= Capacity(steamId))
            return Task.FromResult(No<HostItem>("bad_slot"));
        if (Owned(HostOwnerKind.Character, steamId).Any(i => i.Slot == slot && i.Id != itemId))
            return Task.FromResult(No<HostItem>("slot_taken"));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Slot = slot })));
    }

    public Task<HostResult<HostItem>> StashMove(string steamId, string itemId, bool toStash, int slot)
    {
        if (Fails("stash", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        string stash = "stash:" + steamId;
        if (toStash)
        {
            if (OwnedBy(steamId, itemId) is not { } item)
                return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
            int cap = r.Inventory.StashSlots([Sheets[steamId]]);
            var used = Owned(HostOwnerKind.Stash, stash).Select(i => i.Slot).ToHashSet();
            int free = Enumerable.Range(0, cap).FirstOrDefault(s => !used.Contains(s), -1);
            if (free < 0)
                return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
            return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.Stash, OwnerId = stash, Slot = free })));
        }
        if (!Items.TryGetValue(itemId, out var stashed) || stashed.Owner != HostOwnerKind.Stash || stashed.OwnerId != stash)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        int to = FreeSlot(steamId);
        if (to < 0)
            return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
        return Task.FromResult(HostResult<HostItem>.Success(Store(stashed with { Owner = HostOwnerKind.Character, OwnerId = steamId, Slot = to })));
    }

    public Task<HostResult<IReadOnlyList<HostItem>>> VendorStock(string vendor)
    {
        Calls.Add($"stock {vendor}");
        string owner = "vendor:" + vendor;
        if (!Owned(HostOwnerKind.Vendor, owner).Any())
        {
            var stock = r.VendorStock(vendor, 99);
            for (int i = 0; i < stock.Count; i++)
                Store(Mint(r.ItemRules.Codec.Instance(stock[i]), HostOwnerKind.Vendor, owner, i));
        }
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success([.. Owned(HostOwnerKind.Vendor, owner)]));
    }

    public Task<HostResult<HostItem>> Buy(string steamId, string itemId, string vendor)
    {
        if (Fails("buy", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (!Items.TryGetValue(itemId, out var item) || item.Owner != HostOwnerKind.Vendor || item.OwnerId != "vendor:" + vendor)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        int slot = FreeSlot(steamId);
        if (slot < 0)
            return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
        long price = r.Price(Rolled(item), PriceKind.Buy);
        if (Australium[steamId] < price)
            return Task.FromResult(No<HostItem>("insufficient_funds", HostError.InsufficientFunds));
        Australium[steamId] -= price;
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.Character, OwnerId = steamId, Slot = slot })));
    }

    public Task<HostResult<long>> Sell(string steamId, string itemId, string vendor)
    {
        if (Fails("sell", steamId))
            return Task.FromResult(Unreachable<long>());
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<long>("wrong_owner", HostError.WrongOwner));
        string buyBack = $"buyback:{steamId}:{vendor}";
        var kept = Owned(HostOwnerKind.Vendor, buyBack).OrderBy(i => i.Slot).ToList();
        Store(item with { Owner = HostOwnerKind.Vendor, OwnerId = buyBack, Slot = kept.Count == 0 ? 0 : kept.Max(i => i.Slot) + 1 });
        foreach (var old in kept.Take(Math.Max(0, kept.Count + 1 - BuyBackKept)))
            Items.Remove(old.Id);
        Australium[steamId] += r.Price(Rolled(item), PriceKind.Sell);
        return Task.FromResult(HostResult<long>.Success(Australium[steamId]));
    }

    public Task<HostResult<HostItem>> BuyBack(string steamId, string itemId, string vendor)
    {
        if (Fails("buyback", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (!Items.TryGetValue(itemId, out var item) || item.Owner != HostOwnerKind.Vendor || item.OwnerId != $"buyback:{steamId}:{vendor}")
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        int slot = FreeSlot(steamId);
        if (slot < 0)
            return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
        long price = r.Price(Rolled(item), PriceKind.Sell);
        if (Australium[steamId] < price)
            return Task.FromResult(No<HostItem>("insufficient_funds", HostError.InsufficientFunds));
        Australium[steamId] -= price;
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.Character, OwnerId = steamId, Slot = slot })));
    }

    private async Task<HostResult<HostItem>> InPlace(string op, string steamId, string itemId, Func<RolledItem, ItemChange> operate)
    {
        if (Fails(op, steamId))
            return Unreachable<HostItem>();
        if (InPlaceGate is { } gate)
            await gate.Task;
        if (OwnedBy(steamId, itemId) is not { } item)
            return No<HostItem>("wrong_owner", HostError.WrongOwner);
        var change = operate(Rolled(item));
        if (!change.Ok)
            return No<HostItem>(change.Refusal!);
        if (Australium[steamId] < change.Cost)
            return No<HostItem>("insufficient_funds", HostError.InsufficientFunds);
        Australium[steamId] -= change.Cost;
        return HostResult<HostItem>.Success(With(item, r.ItemRules.Codec.Instance(change.Produced[0])));
    }

    public Task<HostResult<HostItem>> Identify(string steamId, string itemId) => InPlace("identify", steamId, itemId, i => r.Identify(i, ++_seed));

    public Task<HostResult<HostItem>> Repair(string steamId, string itemId) => InPlace("repair", steamId, itemId, r.Repair);

    private HostResult<IReadOnlyList<HostItem>> Transform(string op, string steamId, IReadOnlyList<string> inputs, Func<IReadOnlyList<RolledItem>, ItemChange> operate)
    {
        if (Fails(op, steamId))
            return Unreachable<IReadOnlyList<HostItem>>();
        var items = inputs.Select(id => OwnedBy(steamId, id)).ToList();
        if (items.Count == 0 || items.Any(i => i is null) || inputs.Distinct().Count() != inputs.Count)
            return No<IReadOnlyList<HostItem>>("bad_inputs");
        var change = operate([.. items.Select(i => Rolled(i!))]);
        if (!change.Ok)
            return No<IReadOnlyList<HostItem>>(change.Refusal!);
        if (Australium[steamId] < change.Cost)
            return No<IReadOnlyList<HostItem>>("insufficient_funds", HostError.InsufficientFunds);
        // All or nothing: the slots for the outputs are found before anything moves.
        var used = Owned(HostOwnerKind.Character, steamId).Where(i => !change.Consumed.Select(k => items[k]!.Id).Contains(i.Id)).Select(i => i.Slot).ToHashSet();
        var slots = new List<int>();
        for (int s = 0; s < Capacity(steamId) && slots.Count < change.Produced.Count; s++)
            if (!used.Contains(s)) slots.Add(s);
        if (slots.Count < change.Produced.Count)
            return No<IReadOnlyList<HostItem>>("no_capacity", HostError.NoCapacity);
        Australium[steamId] -= change.Cost;
        foreach (int k in change.Consumed)
            Items.Remove(items[k]!.Id);
        var made = change.Produced.Select((p, k) => Store(Mint(r.ItemRules.Codec.Instance(p), HostOwnerKind.Character, steamId, slots[k]))).ToList();
        return HostResult<IReadOnlyList<HostItem>>.Success(made);
    }

    private static HostResult<HostItem> First(HostResult<IReadOnlyList<HostItem>> r) =>
        r.Ok ? HostResult<HostItem>.Success(r.Value[0]) : HostResult<HostItem>.Refused(r.Refusal!);

    public Task<HostResult<HostItem>> OpenCrate(string steamId, string itemId) =>
        Task.FromResult(First(Transform("crate", steamId, [itemId], i => r.OpenCrate(i[0], Ctx(steamId, "crate"), ++_seed))));

    public Task<HostResult<HostItem>> Craft(string steamId, string recipe, IReadOnlyList<string> inputs) =>
        Task.FromResult(First(Transform("craft", steamId, inputs, i => r.Craft(recipe, i, Ctx(steamId, "craft"), ++_seed))));

    public Task<HostResult<IReadOnlyList<HostItem>>> Salvage(string steamId, string itemId) =>
        Task.FromResult(Transform("salvage", steamId, [itemId], i => r.Salvage(i[0], Ctx(steamId, "salvage"), ++_seed)));

    public Task<bool> RecordDeath(string steamId, Vector position)
    {
        if (Fails("death", steamId))
            return Task.FromResult(false);
        foreach (var item in Owned(HostOwnerKind.Character, steamId).ToList())
            Store(item with { Owner = HostOwnerKind.Corpse, OwnerId = steamId });
        _corpseAustralium[steamId] = _corpseAustralium.GetValueOrDefault(steamId) + Australium[steamId];
        Australium[steamId] = 0;
        return Task.FromResult(true);
    }

    public Task<LootAnswer> LootCorpse(string steamId)
    {
        if (Fails("loot", steamId))
            return Task.FromResult(new LootAnswer(false, 0, "unreachable"));
        int n = 0;
        foreach (var item in Owned(HostOwnerKind.Corpse, steamId).ToList())
        {
            int slot = FreeSlot(steamId);
            if (slot < 0) break;
            Store(item with { Owner = HostOwnerKind.Character, Slot = slot });
            n++;
        }
        Australium[steamId] += _corpseAustralium.GetValueOrDefault(steamId);
        _corpseAustralium.Remove(steamId);
        return Task.FromResult(new LootAnswer(true, n, ""));
    }
}
