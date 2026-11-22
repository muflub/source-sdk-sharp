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

    /// <summary>Mints <paramref name="item"/> as the hero's, in a slot of a container (the host's starting kit does this at creation).</summary>
    public HostItem Carry(string hero, ItemInstance item, HostContainer container, int slot) =>
        Store(Mint(item, HostOwnerKind.Character, hero, slot) with { Container = container });

    public IEnumerable<HostItem> Owned(HostOwnerKind kind, string ownerId) =>
        Items.Values.Where(i => i.Owner == kind && i.OwnerId == ownerId);

    private int Capacity(string hero) => r.Inventory.BackpackSlots(Sheets[hero]);

    private int FreeSlot(string hero)
    {
        // Only backpack items take cells (SDK 2.7): what is worn or belted takes none.
        var used = Owned(HostOwnerKind.Character, hero).Where(i => i.Container == HostContainer.Backpack).Select(i => i.Slot).ToHashSet();
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

    /// <summary>A 2.8 reveal (the SDK sets australium_pile): it books no Australium; the kill's is its pile's.</summary>
    public async Task<HostResult<RevealOutcome>> Reveal(string steamId, HostItem item, uint killSeq, string robot, IReadOnlyList<string> party, Vector position)
    {
        if (Fails("reveal", steamId))
            return Unreachable<RevealOutcome>();
        if (HoldReveals)
            return await new TaskCompletionSource<HostResult<RevealOutcome>>().Task;
        if (RevealGate is { } gate)
            await gate.Task;
        var moved = Store(Items[item.Id] with { Owner = HostOwnerKind.World, OwnerId = "instance", ForCharacter = steamId });
        return HostResult<RevealOutcome>.Success(new RevealOutcome(moved, 0));
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
        if (item.IsAustraliumPile)
            return Task.FromResult(No<HostItem>("australium_pile"));
        int slot = FreeSlot(steamId);
        if (slot < 0)
            return Task.FromResult(No<HostItem>("no_capacity", HostError.NoCapacity));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with
        {
            Owner = HostOwnerKind.Character, OwnerId = steamId, Slot = slot, ForCharacter = "", Container = HostContainer.Backpack,
        })));
    }

    public Task<HostResult<HostItem>> Drop(string steamId, string itemId, Vector position)
    {
        if (Fails("drop", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Owner = HostOwnerKind.World, OwnerId = "instance", Slot = -1, ForCharacter = "" })));
    }

    public Task<HostResult<HostItem>> Move(string steamId, string itemId, int slot) => Move(steamId, itemId, HostContainer.Backpack, slot);

    /// <summary>The host's move into a container (DescentLedger.Move, SDK 2.7): in range, free, and allowed by the rules.</summary>
    public Task<HostResult<HostItem>> Move(string steamId, string itemId, HostContainer container, int slot)
    {
        if (Fails("move", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        var sheet = r.SheetCodec.Write(Sheets[steamId]);
        if (slot < 0 || slot >= r.ContainerSlots(sheet, (ItemContainer)container))
            return Task.FromResult(No<HostItem>("bad_slot"));
        if (Owned(HostOwnerKind.Character, steamId).Any(i => i.Container == container && i.Slot == slot && i.Id != itemId))
            return Task.FromResult(No<HostItem>("slot_taken"));
        if (r.PlaceRefusal(sheet, Rolled(item), (ItemContainer)container, slot) is { } why)
            return Task.FromResult(No<HostItem>(why));
        return Task.FromResult(HostResult<HostItem>.Success(Store(item with { Container = container, Slot = slot })));
    }

    /// <summary>The host's use (DescentLedger.Consume): the rules' answer, the item ended (state Consumed) or its count lowered in place.</summary>
    public Task<HostResult<HostItem>> Consume(string steamId, string itemId, int count)
    {
        if (Fails("consume", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (count < 1)
            return Task.FromResult(No<HostItem>("bad_count"));
        if (OwnedBy(steamId, itemId) is not { } item)
            return Task.FromResult(No<HostItem>("wrong_owner", HostError.WrongOwner));
        var change = r.Consume(r.SheetCodec.Write(Sheets[steamId]), Rolled(item), count);
        if (!change.Ok)
            return Task.FromResult(No<HostItem>(change.Refusal!));
        if (change.Consumed is [0] && change.Produced.Count == 0)
        {
            Items.Remove(itemId);
            return Task.FromResult(HostResult<HostItem>.Success(item with { State = HostItemState.Consumed, Version = item.Version + 1 }));
        }
        if (change.Consumed.Count != 0 || change.Produced is not [{ } left] || left.Count != item.Count - count)
            return Task.FromResult(No<HostItem>("rules_bad_consume"));
        return Task.FromResult(HostResult<HostItem>.Success(With(item, r.ItemRules.Codec.Instance(left))));
    }

    /// <summary>The host's wear (DescentLedger.Wear): 1..64 items, each once, each amount above 0, the rules' answer; all or nothing.</summary>
    public Task<HostResult<IReadOnlyList<HostItem>>> Wear(string steamId, IReadOnlyList<HostWear> entries)
    {
        if (Fails("wear", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostItem>>());
        WearBatches.Add([.. entries]);
        if (entries.Count is 0 or > 64 || entries.Select(e => e.ItemId).Distinct().Count() != entries.Count)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("bad_entries"));
        var worn = new List<(HostItem Item, ItemInstance After)>();
        foreach (var e in entries)
        {
            if (e.Amount <= 0)
                return Task.FromResult(No<IReadOnlyList<HostItem>>("bad_amount"));
            if (OwnedBy(steamId, e.ItemId) is not { } item)
                return Task.FromResult(No<IReadOnlyList<HostItem>>("wrong_owner", HostError.WrongOwner));
            var change = r.Wear(Rolled(item), e.Amount);
            if (!change.Ok)
                return Task.FromResult(No<IReadOnlyList<HostItem>>(change.Refusal!));
            worn.Add((item, r.ItemRules.Codec.Instance(change.Produced[0])));
        }
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success([.. worn.Select(w => With(w.Item, w.After))]));
    }

    /// <summary>Every wear write the host was sent.</summary>
    public List<List<HostWear>> WearBatches { get; } = [];

    /// <summary>Whether this is the hub (a town repair is refused <c>not_a_hub</c> elsewhere).</summary>
    public bool InHub { get; set; } = true;

    /// <summary>The instance's seed and depth a container is replayed with.</summary>
    public ulong InstanceSeed { get; set; } = 42;

    public int Depth { get; set; } = 2;

    private readonly HashSet<(uint Seq, string Hero)> _opened = [];

    /// <summary>
    /// The host's container (DescentLedger.OpenContainer): the rules' <c>ContainerDrops</c> for this
    /// instance and the opener's sheet, minted to the floor for him; once per container and hero.
    /// </summary>
    public Task<HostResult<IReadOnlyList<HostItem>>> OpenContainer(string steamId, uint containerSeq)
    {
        if (Fails("open", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostItem>>());
        if (!_opened.Add((containerSeq, steamId)))
            return Task.FromResult(No<IReadOnlyList<HostItem>>("already_opened"));
        var drops = r.ContainerDrops(InstanceSeed, containerSeq, Depth, r.SheetCodec.Write(Sheets[steamId]));
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success(
            [.. drops.Select(d => Store(Mint(r.ItemRules.Codec.Instance(d), HostOwnerKind.World, "instance", -1, steamId)))]));
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

    public Task<HostResult<HostItem>> Repair(string steamId, string itemId) => Repair(steamId, itemId, HostRepairKind.Town, null);

    /// <summary>
    /// The host's repair of a kind (DescentLedger.Repair): the town's in the hub only; the field's
    /// names a kit of the hero's other than the item, which the rules may use up (index 1) in the
    /// same write. The item changes in place.
    /// </summary>
    public async Task<HostResult<HostItem>> Repair(string steamId, string itemId, HostRepairKind kind, string? kitId)
    {
        if (kind == HostRepairKind.Town && !InHub)
        {
            Calls.Add($"repair {steamId}");
            return No<HostItem>("not_a_hub", HostError.NotAHub);
        }
        HostItem? kit = null;
        if (kind == HostRepairKind.Field && (string.IsNullOrEmpty(kitId) || kitId == itemId || (kit = OwnedBy(steamId, kitId)) is null))
        {
            Calls.Add($"repair {steamId}");
            return No<HostItem>("bad_kit");
        }
        KitsNamed.Add(kitId);
        // The host's 2.8 ledger asks the 1.2 member, with the repairer's sheet.
        var answer = await InPlace("repair", steamId, itemId,
            i => ((IGameRules)r).Repair(i, (RepairKind)(int)kind, kit is null ? null : Rolled(kit), r.SheetCodec.Write(Sheets[steamId]), ++_seed));
        if (answer.Ok && kit is not null)
            Items.Remove(kit.Id);
        return answer;
    }

    /// <summary>The kit each repair named (null for a town repair).</summary>
    public List<string?> KitsNamed { get; } = [];

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

    //-------------------------------------------------------------------------
    // SDK 2.8 (the host's DescentLedger: KillAustralium, ClaimAustralium, Break, Swap, Merge)
    //-------------------------------------------------------------------------

    private readonly Dictionary<uint, string> _piles = new();
    private readonly Dictionary<string, string> _pileKiller = new(StringComparer.Ordinal);
    private readonly HashSet<string> _paid = new(StringComparer.Ordinal);
    private readonly HashSet<(uint Seq, string Hero)> _broken = [];

    /// <summary>The host's parties: each member's party, killer included (none: a party of one).</summary>
    public Dictionary<string, List<string>> Parties { get; } = new(StringComparer.Ordinal);

    /// <summary>The characters leased on another instance (left the level): a pile's pickup pays them nothing.</summary>
    public HashSet<string> Elsewhere { get; } = new(StringComparer.Ordinal);

    /// <summary>Puts <paramref name="members"/> in one party.</summary>
    public void Party(params string[] members)
    {
        var party = members.ToList();
        foreach (var m in members)
            Parties[m] = party;
    }

    /// <summary>The host's pile: the kill's roll replayed, one pile per kill.</summary>
    public Task<HostResult<HostItem>> KillAustralium(string steamId, uint killSeq, string robot, int partySize, Vector position)
    {
        if (Fails("pile", steamId))
            return Task.FromResult(Unreachable<HostItem>());
        if (_piles.ContainsKey(killSeq))
            return Task.FromResult(No<HostItem>("already_minted"));
        var roll = r.KillRoll(InstanceSeed, killSeq, robot, Depth, Math.Max(1, partySize));
        if (roll.Australium <= 0)
            return Task.FromResult(No<HostItem>("no_australium"));
        var pile = Store(new HostItem($"h{++_next}", 0, HostItem.AustraliumPileType, 0, 0, (int)roll.Australium, true, OpaquePayload.Empty,
            HostOwnerKind.World, "instance", -1, 1, "", "instance", 0));
        _piles[killSeq] = pile.Id;
        _pileKiller[pile.Id] = steamId;
        return Task.FromResult(HostResult<HostItem>.Success(pile));
    }

    /// <summary>The host's pickup: the claimer in the killer's party, every member on the level credited his share by the rules, once.</summary>
    public Task<HostResult<IReadOnlyList<HostAustraliumCredit>>> ClaimAustralium(string steamId, string pileId)
    {
        if (Fails("pile_claim", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostAustraliumCredit>>());
        if (!Items.TryGetValue(pileId, out var pile) || !pile.IsAustraliumPile || pile.Owner != HostOwnerKind.World)
            return Task.FromResult(No<IReadOnlyList<HostAustraliumCredit>>("no_item", HostError.NoItem));
        string killer = _pileKiller[pileId];
        var members = (Parties.GetValueOrDefault(killer) ?? [killer]).Prepend(killer).Distinct().ToList();
        if (!members.Contains(steamId))
            return Task.FromResult(No<IReadOnlyList<HostAustraliumCredit>>("not_in_party", HostError.WrongOwner));
        var here = members.Where(m => Sheets.ContainsKey(m) && !Elsewhere.Contains(m)).ToList();
        var shares = r.AustraliumShares(pile.Count, [.. here.Select(m => r.SheetCodec.Write(Sheets[m]))]);
        if (shares.Count != here.Count || shares.Any(s => s < 0 || s > pile.Count))
            return Task.FromResult(No<IReadOnlyList<HostAustraliumCredit>>("rules_bad_shares"));
        var credits = new List<HostAustraliumCredit>();
        for (int i = 0; i < here.Count; i++)
        {
            if (shares[i] == 0 || !_paid.Add($"pile:{pileId}:{here[i]}"))
                continue;
            Australium[here[i]] += shares[i];
            credits.Add(new HostAustraliumCredit(here[i], shares[i]));
        }
        Items.Remove(pileId);
        return Task.FromResult(HostResult<IReadOnlyList<HostAustraliumCredit>>.Success(credits));
    }

    /// <summary>The host's break: the rules' roll for this hero, its Australium credited, its items to the floor for him; once per breakable and hero.</summary>
    public Task<HostResult<HostBreak>> Break(string steamId, uint breakableSeq)
    {
        if (Fails("break", steamId))
            return Task.FromResult(Unreachable<HostBreak>());
        if (!_broken.Add((breakableSeq, steamId)))
            return Task.FromResult(No<HostBreak>("already_broken"));
        var roll = r.BreakableDrops(InstanceSeed, breakableSeq, Depth, r.SheetCodec.Write(Sheets[steamId]));
        Australium[steamId] += roll.Australium;
        var items = roll.Items.Select(d => Store(Mint(r.ItemRules.Codec.Instance(d), HostOwnerKind.World, "instance", -1, steamId))).ToList();
        return Task.FromResult(HostResult<HostBreak>.Success(new HostBreak(roll.Australium, items)));
    }

    /// <summary>The host's swap: both the caller's, each destination asked of the rules; one refusal refuses both.</summary>
    public Task<HostResult<IReadOnlyList<HostItem>>> Swap(string steamId, string itemA, string itemB)
    {
        if (Fails("swap", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostItem>>());
        if (itemA == itemB)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("bad_items"));
        if (OwnedBy(steamId, itemA) is not { } a || OwnedBy(steamId, itemB) is not { } b)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("wrong_owner", HostError.WrongOwner));
        var sheet = r.SheetCodec.Write(Sheets[steamId]);
        if ((r.PlaceRefusal(sheet, Rolled(a), (ItemContainer)b.Container, b.Slot) ?? r.PlaceRefusal(sheet, Rolled(b), (ItemContainer)a.Container, a.Slot)) is { } why)
            return Task.FromResult(No<IReadOnlyList<HostItem>>(why));
        var movedA = Store(a with { Container = b.Container, Slot = b.Slot, Version = a.Version + 1 });
        var movedB = Store(b with { Container = a.Container, Slot = a.Slot, Version = b.Version + 1 });
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success([movedA, movedB]));
    }

    /// <summary>The host's merge: the rules' answer, the count conserved, into raised and from ended or lowered; answers [into, from].</summary>
    public Task<HostResult<IReadOnlyList<HostItem>>> Merge(string steamId, string fromItemId, string intoItemId)
    {
        if (Fails("merge", steamId))
            return Task.FromResult(Unreachable<IReadOnlyList<HostItem>>());
        if (fromItemId == intoItemId)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("bad_items"));
        if (OwnedBy(steamId, fromItemId) is not { } source || OwnedBy(steamId, intoItemId) is not { } target)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("wrong_owner", HostError.WrongOwner));
        var change = r.Merge(Rolled(source), Rolled(target));
        if (!change.Ok)
            return Task.FromResult(No<IReadOnlyList<HostItem>>(change.Refusal!));
        bool whole = change.Produced.Count == 1 && change.Consumed is [0];
        bool partial = change.Produced.Count == 2 && change.Consumed.Count == 0;
        if (change.Cost != 0 || !(whole || partial) || change.Produced.Sum(p => p.Count) != source.Count + target.Count)
            return Task.FromResult(No<IReadOnlyList<HostItem>>("rules_bad_merge"));
        var into = With(target, r.ItemRules.Codec.Instance(change.Produced[0]));
        HostItem from;
        if (whole)
        {
            Items.Remove(source.Id);
            from = source with { State = HostItemState.Consumed, Version = source.Version + 1 };
        }
        else
        {
            from = With(source, r.ItemRules.Codec.Instance(change.Produced[1]));
        }
        return Task.FromResult(HostResult<IReadOnlyList<HostItem>>.Success([into, from]));
    }

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
            Store(item with { Owner = HostOwnerKind.Character, Slot = slot, Container = HostContainer.Backpack });
            n++;
        }
        Australium[steamId] += _corpseAustralium.GetValueOrDefault(steamId);
        _corpseAustralium.Remove(steamId);
        return Task.FromResult(new LootAnswer(true, n, ""));
    }
}
