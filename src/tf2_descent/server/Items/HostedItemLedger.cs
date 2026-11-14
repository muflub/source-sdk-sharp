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
        var inventory = new HeroInventory { Australium = character.Australium };
        var backpack = Slots(answer.Value.Backpack, out int skipped);
        var stash = Slots(answer.Value.Stash, out int skippedStash);
        if (skipped + skippedStash > 0)
            log.Warning($"[TF2Descent] {hero}: {skipped + skippedStash} items of an older schema are not shown\n");
        return new LedgerHero(hero, sheet, inventory with { Backpack = backpack }, new StashState { Slots = stash });
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

    /// <summary>The backpack again, as the host holds it, over <paramref name="basis"/>: after an operation whose outputs the host does not all answer.</summary>
    private async Task<Confirmed> Reloaded(LedgerHero basis, IReadOnlyList<long>? gone)
    {
        var answer = await host.Inventory(basis.Hero, false);
        if (!answer.Ok)
            return Confirmed.No(Refusal(answer.Refusal!));
        var backpack = Slots(answer.Value.Backpack, out _);
        return new Confirmed(basis with { Inventory = basis.Inventory with { Backpack = backpack } }, null, gone);
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
            default:
                // stack, equip, unequip, to_belt, from_belt, drink, give: no host call carries them.
                return Unsupported;
        }
    }

    protected override async Task<VendorAnswer> RollStock(LedgerHero hero, string vendor)
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
