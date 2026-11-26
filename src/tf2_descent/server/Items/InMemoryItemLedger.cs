using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The ledger with no host (E8): every item minted by the rules' roll with a seed derived from
/// the session seed and what is rolled (a kill's sequence and its hero, a vendor, a recipe's
/// count), every operation the rules' own transition, every hero kept in memory, keyed by hero,
/// for the process's life: through respawns and map changes. A new hero starts with the
/// class's <see cref="StartingKit"/>, and a death gives it again (plan L26).
/// </summary>
[SideService(Side.Server)]
public sealed class InMemoryItemLedger(IGameRules rules, IHooks<IItemLedgerHook> hooks, StartingKit kit)
    : ItemLedgerBase(rules as TF2DescentRules ?? throw new ArgumentException("the in-memory ledger needs TF2.Descent's own rules", nameof(rules)), hooks)
{
    // Each corpse's items with where the hero carried them, so a loot puts them back (InventoryRules.Recover).
    // A corpse holds no Australium: it stays with the hero (the user, 2026-11-22, plan L26).
    private readonly Dictionary<string, List<CorpseItem>> _corpses = new(StringComparer.Ordinal);

    // What the Lost & Found holds of each hero's: his corpses whose levels are gone, merged, places kept.
    private readonly Dictionary<string, List<CorpseItem>> _lost = new(StringComparer.Ordinal);
    private ulong _rolls;

    /// <summary>The class a new hero starts as (HE1's mode spawns heroes as Soldiers).</summary>
    public string NewHeroClass { get; init; } = "soldier";

    /// <summary>What every in-memory roll is derived from: the same seed replays the same session.</summary>
    public ulong SessionSeed { get; init; } = 0x7F2D_E5CE_0001;

    public override LedgerKind Kind => LedgerKind.InMemory;

    protected override ulong RollSeed(string what) => TF2DescentRules.Hash("ledger", SessionSeed, what, ++_rolls);

    protected override Task<LedgerHero?> LoadHero(string hero)
    {
        var sheet = Rules.Heroes.New(NewHeroClass, hero);
        var inventory = new HeroInventory();
        foreach (var b in kit.WeaponsOf(sheet.ClassId))
            inventory = inventory with { Backpack = inventory.Backpack.Add(inventory.Backpack.Count, KitItem(sheet, b)) };
        var potions = kit.PotionsOf(sheet.ClassId);
        for (int i = 0; i < potions.Count; i++)
            inventory = inventory with { Belt = inventory.Belt.Add(i, KitItem(sheet, potions[i])) };
        return Task.FromResult<LedgerHero?>(new LedgerHero(hero, sheet, inventory, new StashState()));
    }

    /// <summary>
    /// The class's starting kit given to a hero who has just died (the user, 2026-11-22, plan L26:
    /// "respawn with default weapons"), as the host's <c>IGameRules.StartingKit</c> places it: each
    /// weapon in the first free equipment slot its base takes, each potion on the belt, slot by
    /// slot. Every item is new (the corpse keeps the old kit: both exist) and sells for nothing.
    /// </summary>
    private HeroInventory WithKit(HeroSheet sheet, HeroInventory inventory)
    {
        var slots = Rules.Content.InventorySettings.Equipment;
        foreach (var b in kit.WeaponsOf(sheet.ClassId))
        {
            string accepts = Rules.Content.ItemBases[b].Slot;
            if (slots.FirstOrDefault(s => s.Accepts == accepts && !inventory.Equipped.ContainsKey(s.Id)) is { } slot)
                inventory = inventory with { Equipped = inventory.Equipped.Add(slot.Id, KitItem(sheet, b)) };
        }
        var potions = kit.PotionsOf(sheet.ClassId);
        for (int i = 0; i < potions.Count; i++)
            inventory = inventory with { Belt = inventory.Belt.Add(i, KitItem(sheet, potions[i])) };
        return inventory;
    }

    /// <summary>A Stock item of <paramref name="baseId"/> at item level 1, rolled through the rules.</summary>
    private Held KitItem(HeroSheet sheet, string baseId)
    {
        var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, 0, 1, 0,
            Rules.Content.ItemSettings.ForcedBasePrefix + baseId, StartingKit.Source);
        return new Held(NextId(), Rules.ItemRules.Roller.Roll(ctx, RollSeed("kit:" + baseId)));
    }

    protected override Task<Confirmed> Confirm(Op op, LedgerHero before, LedgerHero predicted) =>
        Task.FromResult(new Confirmed(predicted, null));

    protected override Task<VendorAnswer> RollStock(LedgerHero hero, string vendor, long restock)
    {
        // The rules' stock for this buyer (the vendor name carries the level and class, Vendors.Parse),
        // from the rules' seed of this session, the vendor, the hero and the restock (Vendors.StockSeed).
        var stock = Rules.Vendors.StockOf($"{vendor}@{hero.Sheet.Level}@{hero.Sheet.ClassId}", StockSeed(vendor, hero.Hero, restock));
        return Task.FromResult(new VendorAnswer(true, null,
            [.. stock.Select(i => new VendorItem(NextId(), i, Rules.Pricing.Price(i, PriceKind.Buy)))]));
    }

    /// <summary>The seed of a hero's stock at a vendor in a restock: the rules' <see cref="Vendors.StockSeed"/> over the session seed.</summary>
    public ulong StockSeed(string vendor, string hero, long restock) => Vendors.StockSeed(SessionSeed, vendor, hero, restock);

    /// <summary>The seed a crate opens with: the session and the crate's own seed, so the rules' <c>OpenCrate</c> over the same two replays it.</summary>
    public ulong CrateSeed(ItemInstance crate) => TF2DescentRules.Hash("ledger_crate", SessionSeed, crate.Seed);

    protected override ulong CrateSeedOf(ItemInstance crate) => CrateSeed(crate);

    /// <summary>Puts an item the world made on the floor (a test's item; later a container's roll).</summary>
    internal FloorItem PlaceOnFloor(ItemInstance item, string? forHero, Vector position) =>
        Place(new FloorItem(NextId(), item, forHero, position));

    /// <summary>The seed of a kill's roll for one hero: the session, the kill and the hero (R4: one roll a player).</summary>
    public ulong KillSeed(uint killSeq, string hero) => TF2DescentRules.Hash("ledger_kill", SessionSeed, killSeq, hero);

    public override async Task<DropResult> KillDrop(KillDrop kill)
    {
        if (kill.Tier < 0)
            return DropResult.Nothing;
        if (await Ensure(kill.Hero) is not { } h)
            return DropResult.Refused(LedgerRefusals.NoHero);
        var row = Rules.LevelTable(kill.Depth);
        // The host's reserve rolls with this context (DescentLedger.TakeReserve): the hero's class and level, the depth's row.
        var ctx = new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, kill.Depth, row.ItemLevel, kill.Tier, row.LootTable, "reserve");
        var item = Rules.ItemRules.Roller.Roll(ctx, KillSeed(kill.KillSeq, kill.Hero));
        return new DropResult(true, null, [Place(new FloorItem(NextId(), item, kill.Hero, kill.Position))]);
    }

    /// <summary>The seed of a kill's potion for one hero: as <see cref="KillSeed"/>, under a name of its own.</summary>
    public ulong PotionSeed(uint killSeq, string hero) => TF2DescentRules.Hash("ledger_potion", SessionSeed, killSeq, hero);

    /// <summary>A potion of the base at the depth's item level, rolled as the starting kit's are, on the floor for the hero.</summary>
    public override async Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position)
    {
        if (!Rules.Content.ItemBases.Contains(potionBase))
            return DropResult.Refused(LedgerRefusals.UnknownBase);
        if (await Ensure(hero) is not { } h)
            return DropResult.Refused(LedgerRefusals.NoHero);
        var ctx = new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, depth, Rules.LevelTable(depth).ItemLevel, 0,
            Rules.Content.ItemSettings.ForcedBasePrefix + potionBase, "potion");
        var item = Rules.ItemRules.Roller.Roll(ctx, PotionSeed(killSeq, hero));
        return new DropResult(true, null, [Place(new FloorItem(NextId(), item, hero, position))]);
    }

    /// <summary>
    /// A container's roll for its hero: an item of the rules' tier from the depth's loot table, or a
    /// potion of the rules' base, at the depth's item level, from the rules' own seed for this hero
    /// and container, on the floor for him; nothing when the rules gave neither.
    /// </summary>
    public override async Task<DropResult> ContainerDrop(ContainerDrop drop)
    {
        if (drop.Tier < 0 && drop.Potion is null)
            return DropResult.Nothing;
        if (drop.Potion is { } potion && !Rules.Content.ItemBases.Contains(potion))
            return DropResult.Refused(LedgerRefusals.UnknownBase);
        if (await Ensure(drop.Hero) is not { } h)
            return DropResult.Refused(LedgerRefusals.NoHero);
        // The rules' item for the roll, the one the host mints for a hosted container (LootRules.ContainerItem).
        var item = Rules.Loot.ContainerItem(h.Sheet.ClassId, h.Sheet.Level, drop.Depth, drop.Tier, drop.Tier >= 0 ? null : drop.Potion, drop.Seed)!;
        return new DropResult(true, null, [Place(new FloorItem(NextId(), item, drop.Hero, drop.Position))]);
    }

    public override async Task<DropResult> BossDrop(BossDrop boss)
    {
        var placed = new List<FloorItem>();
        foreach (var hero in boss.Heroes.Distinct(StringComparer.Ordinal))
        {
            if (await Ensure(hero) is not { } h)
                continue;
            var row = Rules.LevelTable(boss.Depth);
            var ctx = new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, boss.Depth, row.ItemLevel, -1, row.LootTable, "boss:" + boss.Robot);
            foreach (var drop in Rules.BossDrops(ctx, boss.Robot, KillSeed(boss.KillSeq, hero)))
                placed.Add(Place(new FloorItem(NextId(), Rules.ItemRules.Codec.Instance(drop), hero, boss.Position)));
        }
        return new DropResult(true, null, placed);
    }

    /// <summary>The kill's pile as the game rolled it (L24: one pile a kill, shared on pickup); none for a roll of none.</summary>
    public override Task<PileAnswer> KillAustralium(KillPile kill) => Task.FromResult(kill.Australium <= 0
        ? new PileAnswer(false, LedgerRefusals.NoAustralium, null)
        : new PileAnswer(true, null, PlacePile(new AustraliumPile(NextId(), kill.Australium, kill.Killer))));

    /// <summary>
    /// The pile picked up, as the host does it: the claimer must be one of <paramref name="members"/>
    /// (the killer's party on the level); each member is credited the rules' share
    /// (<c>AustraliumShares</c> over their sheets, in that order) and the pile is gone.
    /// </summary>
    public override async Task<PileTake> ClaimAustralium(string hero, long pileId, IReadOnlyList<string> members)
    {
        if (PileOf(pileId) is not { } pile)
            return PileTake.Refused(LedgerRefusals.NotOnFloor);
        if (!members.Contains(hero, StringComparer.Ordinal))
            return PileTake.Refused(LedgerRefusals.NotInParty);
        var here = new List<LedgerHero>();
        foreach (var m in members.Distinct(StringComparer.Ordinal))
            if (await Ensure(m) is { } h)
                here.Add(h);
        if (PileOf(pileId) is null)
            return PileTake.Refused(LedgerRefusals.NotOnFloor);   // taken while the members loaded
        UnplacePile(pileId);
        var shares = Contract.AustraliumShares(pile.Australium, [.. here.Select(h => Rules.SheetCodec.Write(h.Sheet))]);
        var credits = new List<PileCredit>();
        for (int i = 0; i < here.Count && i < shares.Count; i++)
        {
            if (shares[i] <= 0)
                continue;
            await Credit(here[i].Hero, shares[i], "australium_pile", load: true);
            credits.Add(new PileCredit(here[i].Hero, shares[i]));
        }
        return new PileTake(true, null, credits);
    }

    /// <summary>A numbered breakable's roll for one hero (the rules' <c>BreakableDrops</c>, as the host replays it): his Australium credited, his items on the floor; once per breakable and hero.</summary>
    public override async Task<BreakAnswer> Break(BreakDrop drop)
    {
        if (await Ensure(drop.Hero) is not { } h)
            return BreakAnswer.Refused(LedgerRefusals.NoHero);
        if (!MarkBroken(drop.Seq, drop.Hero))
            return BreakAnswer.Refused(LedgerRefusals.AlreadyBroken);
        var roll = Contract.BreakableDrops(drop.InstanceSeed, drop.Seq, drop.Depth, Rules.SheetCodec.Write(h.Sheet));
        if (roll.Australium > 0)
            await Credit(drop.Hero, roll.Australium, "breakable_australium", load: true);
        var items = roll.Items.Select(i => Place(new FloorItem(NextId(), Rules.ItemRules.Codec.Instance(i), drop.Hero, drop.Position))).ToList();
        return new BreakAnswer(true, null, roll.Australium, items);
    }

    /// <summary>
    /// Everything carried onto the hero's corpse (as the host's RecordDeath), each item with where he
    /// carried it. His Australium stays in his purse, and, in the same write, after the corpse took
    /// what he carried, he is given the class's starting kit (<see cref="WithKit"/>), so he respawns
    /// with it; a hardcore hero gets none (he is fallen). The user, 2026-11-22, plan L26.
    /// </summary>
    public override Task<bool> RecordDeath(string hero, Vector position) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return false;
        var carried = h.Inventory.Equipped.Select(e => new CorpseItem(e.Value, Equipped: e.Key))
            .Concat(h.Inventory.Belt.Select(b => new CorpseItem(b.Value, Belt: b.Key)))
            .Concat(h.Inventory.Backpack.Values.Select(b => new CorpseItem(b)))
            .ToList();
        var corpse = _corpses.TryGetValue(hero, out var c) ? c : [];
        corpse.AddRange(carried);
        _corpses[hero] = corpse;
        var stripped = h.Inventory with
        {
            Backpack = h.Inventory.Backpack.Clear(),
            Equipped = h.Inventory.Equipped.Clear(),
            Belt = h.Inventory.Belt.Clear(),
        };
        var after = h.Sheet.Hardcore ? stripped : WithKit(h.Sheet, stripped);
        Commit("death", h, h with { Inventory = after }, []);
        return true;
    });

    /// <summary>
    /// The hero's own corpse back onto them (<see cref="InventoryRules.Recover"/>): what he wore back
    /// in its slot when it is free and still allowed, the belt's potions back on the belt, the rest
    /// into the backpack as far as it fits; what does not fit stays.
    /// </summary>
    public override Task<LootAnswer> LootCorpse(string hero) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return new LootAnswer(false, 0, LedgerRefusals.NoHero);
        if (!_corpses.TryGetValue(hero, out var corpse))
            return new LootAnswer(false, 0, "no corpse");
        var back = Rules.Inventory.Recover(h.Inventory, h.Sheet, corpse);
        var stowed = Stowing(h, new InventoryResult(back.Hero, h.Stash, [], null));
        if (back.Left.Count == 0)
            _corpses.Remove(hero);
        else
            _corpses[hero] = [.. back.Left];
        Commit("loot_corpse", h, h with { Inventory = stowed.Hero }, [.. stowed.Left.Select(l => l.Id)]);
        LastRecovery = back;
        return new LootAnswer(true, back.Recovered, "");
    });

    /// <summary>The last loot's recovery, where each item went (the log line and the facts).</summary>
    public CorpseRecovery? LastRecovery { get; private set; }

    /// <summary>The hero's corpse: its items in the order a loot takes them, and no Australium (it stays with him); null with none.</summary>
    public override LedgerCorpse? Corpse(string hero) =>
        _corpses.TryGetValue(hero, out var corpse) ? new LedgerCorpse([.. corpse.Select(i => i.Item.Id)], 0) : null;

    /// <summary>
    /// The level every corpse lies on is gone (unhosted, a level change: one process holds one map,
    /// so the level a corpse belongs to is never loaded again as itself; plan_descent_logic WO4).
    /// Each corpse goes to the Lost &amp; Found as the rules say (<see cref="global::TF2.Descent.Rules.Death.DeathRules.ToLostAndFound"/>):
    /// merged onto what it holds of the hero's already, or, for a hardcore hero's (D7), nowhere.
    /// Answers the heroes whose corpses reached the Lost &amp; Found, in name order.
    /// </summary>
    public override IReadOnlyList<string> CorpsesLost()
    {
        var moved = new List<string>();
        foreach (var hero in _corpses.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            var corpse = _corpses[hero];
            _corpses.Remove(hero);
            var record = new global::TF2.Descent.Rules.Death.Corpse(hero, Find(hero)?.Sheet.Hardcore == true,
                                                [.. corpse.Select(i => i.Item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            if (global::TF2.Descent.Rules.Death.DeathRules.ToLostAndFound(record) is null)
                continue;
            var held = _lost.TryGetValue(hero, out var l) ? l : [];
            held.AddRange(corpse);
            _lost[hero] = held;
            moved.Add(hero);
        }

        return moved;
    }

    /// <summary>What the Lost &amp; Found holds of the hero's: its items in the order a reclaim takes them, and no Australium; null with none.</summary>
    public override LedgerCorpse? LostAndFound(string hero) =>
        _lost.TryGetValue(hero, out var lost) ? new LedgerCorpse([.. lost.Select(i => i.Item.Id)], 0) : null;

    /// <summary>The fee for everything the Lost &amp; Found holds of the hero's (a reclaim that cannot take all charges for what it takes).</summary>
    public override long? ReclaimFee(string hero) => _lost.TryGetValue(hero, out var lost) ? Fee(lost) : null;

    /// <summary>D6's fee on items: the rules' share (death.json's 25 %) of what a vendor pays for them.</summary>
    private long Fee(IEnumerable<CorpseItem> items) =>
        Rules.Deaths.LostAndFoundFee(items.Sum(i => Rules.Price(Rules.ItemRules.Codec.ToRolled(i.Item.Item), PriceKind.Sell)));

    /// <summary>
    /// The hero's own from the Lost &amp; Found (<see cref="InventoryRules.Recover"/>, as a corpse is
    /// looted): the fee for the items that go back is paid from his purse, and what does not fit
    /// stays. Refused, nothing changed, when the purse cannot pay.
    /// </summary>
    public override Task<LedgerResult> Reclaim(string hero) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return LedgerResult.Refused(LedgerRefusals.NoHero);
        if (!_lost.TryGetValue(hero, out var lost))
            return LedgerResult.Refused(LedgerRefusals.NothingLost, h);
        var back = Rules.Inventory.Recover(h.Inventory, h.Sheet, lost);
        long fee = Fee(lost.Where(i => !back.Left.Contains(i)));
        long purse = h.Inventory.Australium;
        if (purse < fee)
            return LedgerResult.Refused(LedgerRefusals.LostAndFoundFee, h);
        if (back.Left.Count == 0)
            _lost.Remove(hero);
        else
            _lost[hero] = [.. back.Left];
        LastRecovery = back;
        LastReclaimFee = fee;
        var stowed = Stowing(h, new InventoryResult(back.Hero, h.Stash, [], null));
        return Commit("reclaim", h, h with { Inventory = stowed.Hero with { Australium = purse - fee } }, [.. stowed.Left.Select(l => l.Id)]);
    });

    /// <summary>The last reclaim's fee (the log line).</summary>
    public long LastReclaimFee { get; private set; }

    /// <summary>The process keeps every hero for its life: a leave forgets nothing.</summary>
    public override void Forget(string hero)
    {
    }
}
