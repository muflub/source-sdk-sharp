using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The ledger over the hosting service (docs/sdk-integration.md §7). The rules decide first,
/// over the hero as the host last told it, so a refusal is the rules' own and nothing is sent;
/// a transition they allow goes to the host under the hero's lease, and only what the host did
/// is committed: a host refusal or failure changes nothing here and says why
/// (<see cref="LedgerRefusals.HostPrefix"/>). The host names items by string; this ledger names
/// each by a long (the rules' <see cref="Held.Id"/>) and keeps the map.
///
/// <para>A kill's drop is the hot path: the hero's reserve answers with no I/O, the item is on
/// the floor at once, and the reveal goes out after it. An empty tier falls back to
/// <c>MintDrops</c>. Operations the host has no call for are refused with
/// <see cref="LedgerRefusals.HostUnsupported"/>, after the rules have had their say.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class HostedItemLedger(IGameRules rules, IHooks<IItemLedgerHook> hooks, IHostItems host, ILog log)
    : ItemLedgerBase(rules as TF2DescentRules ?? throw new ArgumentException("the hosted ledger needs TF2.Descent's own rules", nameof(rules)), hooks)
{
    private readonly Dictionary<string, long> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _hostIds = new();
    private readonly Dictionary<long, string> _soldTo = new();

    public override LedgerKind Kind => LedgerKind.Hosted;

    /// <summary>The ledger's id for a host item, made the first time it is seen.</summary>
    public long IdOf(string hostId)
    {
        if (_ids.TryGetValue(hostId, out long id))
            return id;
        id = NextId();
        _ids[hostId] = id;
        _hostIds[id] = hostId;
        return id;
    }

    /// <summary>The host's id for a ledger id; null for one the host never named.</summary>
    public string? HostIdOf(long id) => _hostIds.GetValueOrDefault(id);

    /// <summary>The host rolls every item itself, with seeds of its own.</summary>
    protected override ulong RollSeed(string what) => 0;

    private ItemInstance? Decode(HostItem item)
    {
        try
        {
            var rolled = Rules.Items.Decode(item.Instance.Data, (int)item.Instance.SchemaVersion);
            return rolled.SchemaVersion == ItemCodec.Schema ? Rules.ItemRules.Codec.Instance(rolled) : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>A host refusal as the ledger says it: a full backpack is the rules' word for it.</summary>
    internal static string Refusal(HostRefusal r, string full = InventoryRefusals.BackpackFull) => r.Reason switch
    {
        "no_capacity" => full,
        "wrong_owner" => LedgerRefusals.WrongOwner,
        _ => $"{LedgerRefusals.HostPrefix}{r.Error}:{r.Reason}",
    };

    /// <summary>Whether this instance is the hub, where the account's stash is loaded with a hero (the facade asks <see cref="HostService.Boot"/>).</summary>
    public Func<bool> InHub { get; set; } = () => false;

    //-------------------------------------------------------------------------
    // Loading
    //-------------------------------------------------------------------------

    protected override async Task<LedgerHero?> LoadHero(string hero)
    {
        var answer = await host.Inventory(hero, InHub());
        if (!answer.Ok)
        {
            log.Warning($"[TF2Descent] the item ledger could not load {hero}: {Refusal(answer.Refusal!)}\n");
            return null;
        }
        var character = answer.Value.Character;
        var sheet = SheetOf(character);
        var inventory = Carried(new HeroInventory { Australium = character.Australium }, answer.Value.Backpack, out int skipped);
        var stash = Slots(answer.Value.Stash, out int skippedStash);
        if (skipped + skippedStash > 0)
            log.Warning($"[TF2Descent] {hero}: {skipped + skippedStash} items of an older schema, or in no slot of ours, are not shown\n");
        return new LedgerHero(hero, sheet, inventory, new StashState { Slots = stash });
    }

    /// <summary>
    /// <paramref name="inventory"/> with what the hero carries on the host in its three containers
    /// (SDK 2.7): backpack cells, the equipment slots (the host's slot is the index in the rules'
    /// <c>EquipSlots</c>) and the belt. An item of an older schema, or in a slot the rules do not
    /// have, is skipped and counted.
    /// </summary>
    private HeroInventory Carried(HeroInventory inventory, IEnumerable<HostItem> carried, out int skipped)
    {
        var all = carried.ToList();
        var backpack = Slots(all.Where(i => i.Container == HostContainer.Backpack), out skipped);
        var belt = Slots(all.Where(i => i.Container == HostContainer.Belt && i.Slot < Rules.Inventory.BeltSlots), out int skippedBelt);
        skipped += skippedBelt + all.Count(i => i.Container == HostContainer.Belt && i.Slot >= Rules.Inventory.BeltSlots);
        var equipped = System.Collections.Immutable.ImmutableSortedDictionary<string, Held>.Empty.WithComparers(StringComparer.Ordinal);
        var names = Rules.EquipSlots;
        foreach (var item in all.Where(i => i.Container == HostContainer.Equipped))
        {
            if (item.Slot < 0 || item.Slot >= names.Count || equipped.ContainsKey(names[item.Slot]) || Decode(item) is not { } instance)
            {
                skipped++;
                continue;
            }
            equipped = equipped.Add(names[item.Slot], new Held(IdOf(item.Id), instance));
        }
        return inventory with { Backpack = backpack, Equipped = equipped, Belt = belt };
    }

    private System.Collections.Immutable.ImmutableSortedDictionary<int, Held> Slots(IEnumerable<HostItem> items, out int skipped)
    {
        var slots = System.Collections.Immutable.ImmutableSortedDictionary<int, Held>.Empty;
        skipped = 0;
        foreach (var item in items)
        {
            if (Decode(item) is not { } instance || slots.ContainsKey(item.Slot))
            {
                skipped++;
                continue;
            }
            slots = slots.Add(item.Slot, new Held(IdOf(item.Id), instance));
        }
        return slots;
    }

    private HeroSheet SheetOf(HostCharacter c)
    {
        try
        {
            if (c.Sheet.Data.Length > 0)
                return Rules.SheetCodec.Read(new CharacterSheet(c.Sheet.Data, (int)c.Sheet.SchemaVersion));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            log.Warning($"[TF2Descent] {c.Id}'s sheet does not decode ({e.Message}); a new {c.ClassName} sheet stands in\n");
        }
        return Rules.Heroes.New(c.ClassName, c.Name);
    }

    /// <summary>What the hero carries again, as the host holds it, over <paramref name="basis"/>: after an operation whose outputs the host does not all answer.</summary>
    private async Task<Confirmed> Reloaded(LedgerHero basis, IReadOnlyList<long>? gone)
    {
        var answer = await host.Inventory(basis.Hero, false);
        if (!answer.Ok)
            return Confirmed.No(Refusal(answer.Refusal!));
        return new Confirmed(basis with { Inventory = Carried(basis.Inventory, answer.Value.Backpack, out _) }, null, gone);
    }

    /// <summary>
    /// A later step of an operation the host carried out in parts was refused (<paramref name="refusal"/>):
    /// what the hero carries is read back from the host and committed, so the ledger holds what the
    /// host does, and the operation is refused with the host's reason.
    /// </summary>
    private async Task<Confirmed> Resynced(LedgerHero before, string refusal)
    {
        if (await Reloaded(before, []) is { After: { } after })
            Commit("resync", before, after, []);
        return Confirmed.No(refusal);
    }

    private string HostId(long id) => HostIdOf(id) ?? "";

    /// <summary>Moves one item into a slot of a container on the host; the refusal as the ledger says it, or null.</summary>
    private async Task<string?> MoveOnHost(string hero, long id, HostContainer container, int slot)
    {
        var r = await host.Move(hero, HostId(id), container, slot);
        return r.Ok ? null : Refusal(r.Refusal!);
    }

    /// <summary>The backpack slot <paramref name="id"/> sits in after <paramref name="predicted"/>.</summary>
    private static int BackpackSlotOf(LedgerHero predicted, long id) => predicted.Inventory.Backpack.First(p => p.Value.Id == id).Key;

    /// <summary><paramref name="predicted"/> with the item <paramref name="id"/> as the host answered it, where it is (worn, carried, belted).</summary>
    private LedgerHero WithAnswered(LedgerHero predicted, long id, HostItem answered)
    {
        if (Decode(answered) is not { } item)
            return predicted;
        var inv = predicted.Inventory;
        foreach (var p in inv.Equipped.Where(p => p.Value.Id == id))
            return predicted with { Inventory = inv with { Equipped = inv.Equipped.SetItem(p.Key, p.Value with { Item = item }) } };
        foreach (var p in inv.Backpack.Where(p => p.Value.Id == id))
            return predicted with { Inventory = inv with { Backpack = inv.Backpack.SetItem(p.Key, p.Value with { Item = item }) } };
        foreach (var p in inv.Belt.Where(p => p.Value.Id == id))
            return predicted with { Inventory = inv with { Belt = inv.Belt.SetItem(p.Key, p.Value with { Item = item }) } };
        return predicted;
    }

    //-------------------------------------------------------------------------
    // Placing what the host answered
    //-------------------------------------------------------------------------

    /// <summary><paramref name="predicted"/> with the item <paramref name="id"/> in the backpack slot the host put it in.</summary>
    private static LedgerHero InBackpack(LedgerHero predicted, long id, int slot, ItemInstance? changed = null)
    {
        var inventory = predicted.Inventory;
        var at = inventory.Backpack.FirstOrDefault(p => p.Value.Id == id);
        if (at.Value is null)
            return predicted;
        var held = changed is null ? at.Value : at.Value with { Item = changed };
        var backpack = inventory.Backpack.Remove(at.Key);
        if (backpack.ContainsKey(slot))
            return predicted;   // the host's slot is one the rules would not have: keep the rules' (a reload sets it right)
        return predicted with { Inventory = inventory with { Backpack = backpack.Add(slot, held) } };
    }

    private static LedgerHero InStash(LedgerHero predicted, long id, int slot)
    {
        var at = predicted.Stash.Slots.FirstOrDefault(p => p.Value.Id == id);
        if (at.Value is null || (at.Key != slot && predicted.Stash.Slots.ContainsKey(slot)))
            return predicted;
        return predicted with { Stash = new StashState { Slots = predicted.Stash.Slots.Remove(at.Key).Add(slot, at.Value) } };
    }

    private static Confirmed Unsupported => Confirmed.No(LedgerRefusals.HostUnsupported);

    protected override async Task<Confirmed> Confirm(Op op, LedgerHero before, LedgerHero predicted)
    {
        string hero = before.Hero;
        switch (op.Name)
        {
            case "claim":
            {
                var r = await host.Claim(hero, HostIdOf(op.Id) ?? "");
                return r.Ok ? new Confirmed(InBackpack(predicted, op.Id, r.Value.Slot), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "drop":
            {
                var r = await host.Drop(hero, HostIdOf(op.Id) ?? "", op.Position);
                return r.Ok ? new Confirmed(predicted, null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "move":
            {
                // The host refuses an occupied slot (slot_taken); the rules swap. A swap is not asked.
                if (before.Inventory.Backpack.ContainsKey(op.To))
                    return Unsupported;
                var r = await host.Move(hero, HostIdOf(before.Inventory.Backpack[op.Slot].Id) ?? "", op.To);
                return r.Ok ? new Confirmed(predicted, null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "deposit":
            {
                long id = before.Inventory.Backpack[op.Slot].Id;
                var r = await host.StashMove(hero, HostIdOf(id) ?? "", true, -1);
                return r.Ok ? new Confirmed(InStash(predicted, id, r.Value.Slot), null) : Confirmed.No(Refusal(r.Refusal!, InventoryRefusals.StashFull));
            }
            case "withdraw":
            {
                long id = before.Stash.Slots[op.Slot].Id;
                var r = await host.StashMove(hero, HostIdOf(id) ?? "", false, -1);
                return r.Ok ? new Confirmed(InBackpack(predicted, id, r.Value.Slot), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "buy":
            {
                var r = await host.Buy(hero, HostIdOf(op.Id) ?? "", op.Text!);
                return r.Ok ? new Confirmed(InBackpack(predicted, op.Id, r.Value.Slot), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "sell":
            {
                long id = before.Inventory.Backpack[op.Slot].Id;
                var r = await host.Sell(hero, HostIdOf(id) ?? "", op.Text!);
                if (!r.Ok)
                    return Confirmed.No(Refusal(r.Refusal!));
                _soldTo[id] = op.Text!;
                return new Confirmed(predicted with { Inventory = predicted.Inventory with { Australium = r.Value } }, null);
            }
            case "buy_back":
            {
                long id = before.Inventory.BuyBack[op.Slot].Id;
                // The host keeps buy-back per vendor: ask the one the item was sold to.
                var r = await host.BuyBack(hero, HostIdOf(id) ?? "", _soldTo.GetValueOrDefault(id, op.Text!));
                if (!r.Ok)
                    return Confirmed.No(Refusal(r.Refusal!));
                _soldTo.Remove(id);
                return new Confirmed(InBackpack(predicted, id, r.Value.Slot), null);
            }
            case "identify" or "repair":
            {
                var r = op.Name == "identify" ? await host.Identify(hero, HostIdOf(op.Id) ?? "") : await host.Repair(hero, HostIdOf(op.Id) ?? "");
                if (!r.Ok)
                    return Confirmed.No(Refusal(r.Refusal!));
                return new Confirmed(InBackpack(predicted, op.Id, r.Value.Slot, Decode(r.Value)), null);
            }
            case "craft" or "salvage" or "crate":
            {
                if (op.Name == "craft")
                {
                    var r = await host.Craft(hero, op.Text!, [.. op.Slots!.Select(s => HostIdOf(before.Inventory.Backpack[s].Id) ?? "")]);
                    if (!r.Ok)
                        return Confirmed.No(Refusal(r.Refusal!));
                }
                else
                {
                    string id = HostIdOf(before.Inventory.Backpack[op.Slot].Id) ?? "";
                    var refusal = op.Name == "salvage"
                        ? (await host.Salvage(hero, id)).Refusal
                        : (await host.OpenCrate(hero, id)).Refusal;
                    if (refusal is not null)
                        return Confirmed.No(Refusal(refusal));
                }
                // The host answers one output of a recipe or a crate; the backpack as it holds it says all.
                return await Reloaded(predicted, null);
            }
            case "equip":
                return await Equip(op, before, predicted);
            case "unequip":
            {
                long id = before.Inventory.Equipped[op.Text!].Id;
                return await MoveOnHost(hero, id, HostContainer.Backpack, BackpackSlotOf(predicted, id)) is { } why
                    ? Confirmed.No(why) : new Confirmed(predicted, null);
            }
            case "to_belt":
            {
                // The host keeps a stack as one item and has no merge: only an empty belt slot is asked.
                if (before.Inventory.Belt.ContainsKey(op.To))
                    return Unsupported;
                return await MoveOnHost(hero, before.Inventory.Backpack[op.Slot].Id, HostContainer.Belt, op.To) is { } why
                    ? Confirmed.No(why) : new Confirmed(predicted, null);
            }
            case "from_belt":
            {
                long id = before.Inventory.Belt[op.Slot].Id;
                return await MoveOnHost(hero, id, HostContainer.Backpack, BackpackSlotOf(predicted, id)) is { } why
                    ? Confirmed.No(why) : new Confirmed(predicted, null);
            }
            case "drink":
                return await Drink(op, before, predicted);
            case "consume":
            {
                long id = before.Inventory.Backpack[op.Slot].Id;
                var r = await host.Consume(hero, HostId(id), 1);
                return r.Ok ? new Confirmed(Used(predicted, id, r.Value), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "wear":
                return await Wear(op, before, predicted);
            case "repair_worn":
            {
                // The Engineer's repair is the host's town repair (hub only, priced by the host); a waived price has no call.
                if (op.To != (int)WornRepair.Town)
                    return Unsupported;
                var r = await host.Repair(hero, HostId(op.Id), HostRepairKind.Town, null);
                return r.Ok ? new Confirmed(WithAnswered(predicted, op.Id, r.Value), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            case "field_repair_worn":
            {
                // The kit the rules chose (op.Slot) is used up by the host in the same write.
                var r = await host.Repair(hero, HostId(op.Id), HostRepairKind.Field, HostId(before.Inventory.Backpack[op.Slot].Id));
                return r.Ok ? new Confirmed(WithAnswered(predicted, op.Id, r.Value), null) : Confirmed.No(Refusal(r.Refusal!));
            }
            default:
                // stack, give, australium and set_durability: no host call carries them.
                return Unsupported;
        }
    }

    /// <summary>
    /// An equip on the host (SDK 2.7 <c>Move</c> into the slot's index): the item from its backpack
    /// cell into the equipment slot. An item worn there first goes to a free backpack cell (the
    /// host refuses a taken slot), then, when the rules put it where the new one was, there. Each
    /// is a host call of its own: a refusal after the first resyncs from the host.
    /// </summary>
    private async Task<Confirmed> Equip(Op op, LedgerHero before, LedgerHero predicted)
    {
        string hero = before.Hero, slot = op.Text!;
        int index = Rules.EquipSlots.ToList().IndexOf(slot);
        long id = before.Inventory.Backpack[op.Slot].Id;
        if (!before.Inventory.Equipped.TryGetValue(slot, out var old))
            return await MoveOnHost(hero, id, HostContainer.Equipped, index) is { } why ? Confirmed.No(why) : new Confirmed(predicted, null);

        int to = BackpackSlotOf(predicted, old.Id);
        int spare = Rules.Inventory.FreeSlot(before.Inventory, before.Sheet);
        if (spare < 0)
            return Unsupported;   // a swap through a full backpack: the host has no swap, and no cell to pass it through
        if (await MoveOnHost(hero, old.Id, HostContainer.Backpack, spare) is { } off)
            return Confirmed.No(off);
        if (await MoveOnHost(hero, id, HostContainer.Equipped, index) is { } on)
            return await Resynced(before, on);
        if (to != spare && await MoveOnHost(hero, old.Id, HostContainer.Backpack, to) is { } back)
            return await Resynced(before, back);
        return new Confirmed(predicted, null);
    }

    /// <summary><paramref name="predicted"/> with the item <paramref name="id"/> as the host left it after a use: gone, or its count lowered.</summary>
    private LedgerHero Used(LedgerHero predicted, long id, HostItem after) =>
        after.State == HostItemState.Consumed ? predicted : WithAnswered(predicted, id, after);

    /// <summary>
    /// A potion drunk from the belt (SDK 2.7 <c>Consume</c>); when the rules refill the emptied belt
    /// slot from the backpack, the refill is moved onto the belt too.
    /// </summary>
    private async Task<Confirmed> Drink(Op op, LedgerHero before, LedgerHero predicted)
    {
        string hero = before.Hero;
        long id = before.Inventory.Belt[op.Slot].Id;
        var r = await host.Consume(hero, HostId(id), 1);
        if (!r.Ok)
            return Confirmed.No(Refusal(r.Refusal!));
        if (predicted.Inventory.Belt.TryGetValue(op.Slot, out var refill) && refill.Id != id
            && await MoveOnHost(hero, refill.Id, HostContainer.Belt, op.Slot) is { } why)
            return await Resynced(before, why);
        return new Confirmed(Used(predicted, id, r.Value), null);
    }

    /// <summary>The most items one wear write carries (the host's limit).</summary>
    public const int WearBatch = 64;

    /// <summary>
    /// Wear (SDK 2.7 <c>Wear</c>): each item the hero still wears or carries, its events summed into
    /// the one amount that wears it (a weapon's uses, armour's hits: <c>Durability.Amount</c>), in
    /// writes of at most <see cref="WearBatch"/>; an item no event wears is not sent. The items come
    /// back as the host left them.
    /// </summary>
    private async Task<Confirmed> Wear(Op op, LedgerHero before, LedgerHero predicted)
    {
        var carried = before.Inventory.Equipped.Values.Concat(before.Inventory.Backpack.Values).ToDictionary(h => h.Id);
        var amounts = new Dictionary<long, int>();
        foreach (var e in op.Wear ?? [])
            if (carried.TryGetValue(e.ItemId, out var held))
                amounts[e.ItemId] = amounts.GetValueOrDefault(e.ItemId) + Rules.ItemRules.Durability.Amount(held.Item, e.Uses, e.Hits);
        var entries = amounts.Where(p => p.Value > 0).Select(p => new HostWear(HostId(p.Key), p.Value)).ToList();
        var after = predicted;
        foreach (var batch in entries.Chunk(WearBatch))
        {
            var r = await host.Wear(before.Hero, batch);
            if (!r.Ok)
                return Confirmed.No(Refusal(r.Refusal!));
            foreach (var answered in r.Value)
                after = WithAnswered(after, IdOf(answered.Id), answered);
        }
        return new Confirmed(after, null);
    }

    /// <summary>The host prices with no stat of the buyer's: his price stats move nothing here.</summary>
    protected override bool BuyerPricing => false;

    /// <summary>The host's stock is asked each time (it restocks by its own rules).</summary>
    protected override bool KeepsStock => false;

    /// <summary>
    /// A potion gained stays in the backpack cell the host put it in: the host lands every pickup
    /// and buy in the backpack (SDK 2.7), a stow onto the belt is a move of its own
    /// (<see cref="IItemLedger.ToBelt"/>), and a merge into a belt stack has no host call.
    /// </summary>
    protected override bool StowsPotions => false;

    protected override async Task<VendorAnswer> RollStock(LedgerHero hero, string vendor, long restock)
    {
        var r = await host.VendorStock(vendor);
        if (!r.Ok)
            return new VendorAnswer(false, Refusal(r.Refusal!), []);
        var items = new List<VendorItem>();
        foreach (var item in r.Value)
            if (Decode(item) is { } instance)
                items.Add(new VendorItem(IdOf(item.Id), instance, Rules.Pricing.Price(instance, PriceKind.Buy)));
        return new VendorAnswer(true, null, items);
    }

    //-------------------------------------------------------------------------
    // Drops
    //-------------------------------------------------------------------------

    private FloorItem? OnFloor(HostItem item, string fallbackHero, Vector position) =>
        Decode(item) is { } instance
            ? Place(new FloorItem(IdOf(item.Id), instance, string.IsNullOrEmpty(item.ForCharacter) ? fallbackHero : item.ForCharacter, position))
            : null;

    public override async Task<DropResult> KillDrop(KillDrop kill)
    {
        if (kill.Tier < 0)
            return DropResult.Nothing;
        var party = kill.Party ?? [];
        if (await host.TakeReserved(kill.Hero, kill.Tier) is { } reserved)
        {
            if (OnFloor(reserved, kill.Hero, kill.Position) is not { } placed)
                return DropResult.Refused(LedgerRefusals.HostPrefix + "undecodable");
            _ = Reveal(kill, reserved, placed, party);
            return new DropResult(true, null, [placed]);
        }
        var minted = await host.MintDrops(kill.Hero, kill.KillSeq, kill.Robot, kill.Tier, party, kill.Position);
        if (!minted.Ok)
            return DropResult.Refused(Refusal(minted.Refusal!));
        return new DropResult(true, null, [.. minted.Value.Select(i => OnFloor(i, kill.Hero, kill.Position)).OfType<FloorItem>()]);
    }

    /// <summary>The reveal, after the item is on the floor; a reveal the host refuses (a forged one, destroyed there) takes it off the floor.</summary>
    private async Task Reveal(KillDrop kill, HostItem item, FloorItem placed, IReadOnlyList<string> party)
    {
        try
        {
            var r = await host.Reveal(kill.Hero, item, kill.KillSeq, kill.Robot, party, kill.Position);
            if (!r.Ok)
            {
                Unplace(placed.Id);
                log.Warning($"[TF2Descent] reveal of kill {kill.KillSeq} for {kill.Hero} refused: {Refusal(r.Refusal!)}\n");
                return;
            }
            // After any operation of the hero's in flight: one that commits later would put back the Australium it read.
            // SDK 2.7 books the whole roll to the killer and to every nearby member here, at the reveal. SDK 2.8 makes
            // a kill's Australium one pile on the floor, shared by the party on pickup: its Claim credits it then.
            long booked = r.Value.AustraliumBooked;
            if (booked > 0)
                await Serial(kill.Hero, () => Task.FromResult(Find(kill.Hero) is { } h
                    ? Commit("kill_australium", h, h with { Inventory = h.Inventory with { Australium = h.Inventory.Australium + booked } }, [])
                    : LedgerResult.Refused(LedgerRefusals.NoHero)));
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent] reveal of kill {kill.KillSeq} for {kill.Hero} failed: {e.Message}\n");
        }
    }

    /// <summary>Refused: a potion is no item to the host's <c>KillRoll</c>, and no host call mints one.</summary>
    public override Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position) =>
        Task.FromResult(DropResult.Refused(LedgerRefusals.HostUnsupported));

    /// <summary>
    /// A container opened by its hero (SDK 2.7 <c>OpenContainer</c>, numbered <see cref="ContainerDrop.Seq"/>):
    /// the host replays the rules' roll (<c>ContainerDrops</c>) and mints it to this floor for him,
    /// once per container and hero (a second open is refused <c>already_opened</c>); what it minted
    /// is placed here for him to claim. The game's own roll is not sent: the host rolls. A drop with
    /// no number (a breakable's, whose roll is keyed by its marker, which the host does not know) is
    /// refused, nothing asked.
    /// </summary>
    public override async Task<DropResult> ContainerDrop(ContainerDrop drop)
    {
        if (drop.Seq is not { } seq)
            return DropResult.Refused(LedgerRefusals.HostUnsupported);
        var opened = await host.OpenContainer(drop.Hero, seq);
        if (!opened.Ok)
            return DropResult.Refused(Refusal(opened.Refusal!));
        return new DropResult(true, null, [.. opened.Value.Select(i => OnFloor(i, drop.Hero, drop.Position)).OfType<FloorItem>()]);
    }

    public override async Task<DropResult> BossDrop(BossDrop boss)
    {
        if (boss.Heroes.Count == 0)
            return DropResult.Nothing;
        var minted = await host.MintDrops(boss.Heroes[0], boss.KillSeq, boss.Robot, -1, [.. boss.Heroes.Skip(1)], boss.Position);
        if (!minted.Ok)
            return DropResult.Refused(Refusal(minted.Refusal!));
        return new DropResult(true, null, [.. minted.Value.Select(i => OnFloor(i, boss.Heroes[0], boss.Position)).OfType<FloorItem>()]);
    }

    //-------------------------------------------------------------------------
    // Death
    //-------------------------------------------------------------------------

    /// <summary><c>IHostItems.RecordDeath</c> (the host link's); taken, everything the hero carried is the corpse's, here as there.</summary>
    public override Task<bool> RecordDeath(string hero, Vector position) => Serial(hero, async () =>
    {
        bool taken = await host.RecordDeath(hero, position);
        if (taken && Find(hero) is { } h)
        {
            Commit("death", h, h with
            {
                Inventory = h.Inventory with
                {
                    Backpack = h.Inventory.Backpack.Clear(),
                    Equipped = h.Inventory.Equipped.Clear(),
                    Belt = h.Inventory.Belt.Clear(),
                    Australium = 0,
                },
            }, []);
        }
        return taken;
    });

    /// <summary><c>IHostItems.LootCorpse</c> (the host link's); what came back is read from the host's backpack.</summary>
    public override Task<LootAnswer> LootCorpse(string hero) => Serial(hero, async () =>
    {
        var answer = await host.LootCorpse(hero);
        if (answer.Ok && Find(hero) is { } h && await Reloaded(h, []) is { After: { } after })
            Commit("loot_corpse", h, after, []);
        return answer;
    });
}
