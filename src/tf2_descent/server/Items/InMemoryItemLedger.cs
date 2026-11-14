using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The ledger with no host (E8): every item minted by the rules' roll with a seed derived from
/// the session seed and what is rolled (a kill's sequence and its hero, a vendor, a recipe's
/// count), every operation the rules' own transition, every hero kept in memory, keyed by hero,
/// for the process's life: through respawns and map changes. A new hero starts with the
/// class's <see cref="StartingKit"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class InMemoryItemLedger(IGameRules rules, IHooks<IItemLedgerHook> hooks, StartingKit kit)
    : ItemLedgerBase(rules as TF2DescentRules ?? throw new ArgumentException("the in-memory ledger needs TF2.Descent's own rules", nameof(rules)), hooks)
{
    private readonly Dictionary<string, (List<Held> Items, long Australium)> _corpses = new(StringComparer.Ordinal);
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

    /// <summary>A Stock item of <paramref name="baseId"/> at item level 1, rolled through the rules.</summary>
    private Held KitItem(HeroSheet sheet, string baseId)
    {
        var ctx = new ItemRollContext(sheet.ClassId, sheet.Level, 0, 1, 0,
            Rules.Content.ItemSettings.ForcedBasePrefix + baseId, StartingKit.Source);
        return new Held(NextId(), Rules.ItemRules.Roller.Roll(ctx, RollSeed("kit:" + baseId)));
    }

    protected override Task<Confirmed> Confirm(Op op, LedgerHero before, LedgerHero predicted) =>
        Task.FromResult(new Confirmed(predicted, null));

    protected override Task<VendorAnswer> RollStock(LedgerHero hero, string vendor)
    {
        // The rules' stock for this buyer (the vendor name carries the level and class, Vendors.Parse).
        var stock = Rules.Vendors.StockOf($"{vendor}@{hero.Sheet.Level}@{hero.Sheet.ClassId}", RollSeed("vendor:" + vendor));
        return Task.FromResult(new VendorAnswer(true, null,
            [.. stock.Select(i => new VendorItem(NextId(), i, Rules.Pricing.Price(i, PriceKind.Buy)))]));
    }

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
        var row = Rules.LevelTable(drop.Depth);
        var ctx = drop.Tier >= 0
            ? new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, drop.Depth, row.ItemLevel, drop.Tier, row.LootTable, "container")
            : new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, drop.Depth, row.ItemLevel, 0,
                Rules.Content.ItemSettings.ForcedBasePrefix + drop.Potion, "container");
        var item = Rules.ItemRules.Roller.Roll(ctx, drop.Seed);
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

    /// <summary>Everything carried, and the Australium, onto the hero's corpse (as the host's RecordDeath).</summary>
    public override Task<bool> RecordDeath(string hero, Vector position) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return false;
        var carried = h.Inventory.Items.ToList();
        (List<Held> Items, long Australium) corpse = _corpses.TryGetValue(hero, out var c) ? c : (new List<Held>(), 0L);
        corpse.Items.AddRange(carried);
        _corpses[hero] = (corpse.Items, corpse.Australium + h.Inventory.Australium);
        var stripped = h with
        {
            Inventory = h.Inventory with
            {
                Backpack = h.Inventory.Backpack.Clear(),
                Equipped = h.Inventory.Equipped.Clear(),
                Belt = h.Inventory.Belt.Clear(),
                Australium = 0,
            },
        };
        Commit("death", h, stripped, []);
        return true;
    });

    /// <summary>The hero's own corpse back onto them: as many items as fit, the rest stay; the Australium comes back.</summary>
    public override Task<LootAnswer> LootCorpse(string hero) => Serial(hero, async () =>
    {
        if (await Ensure(hero) is not { } h)
            return new LootAnswer(false, 0, LedgerRefusals.NoHero);
        if (!_corpses.TryGetValue(hero, out var corpse))
            return new LootAnswer(false, 0, "no corpse");
        var inventory = h.Inventory with { Australium = h.Inventory.Australium + corpse.Australium };
        var stash = h.Stash;
        int recovered = 0;
        while (recovered < corpse.Items.Count)
        {
            var r = Rules.Inventory.Pickup(inventory, stash, h.Sheet, corpse.Items[recovered]);
            if (!r.Ok)
                break;
            (inventory, stash) = (r.Hero, r.Stash);
            recovered++;
        }
        var left = corpse.Items.Skip(recovered).ToList();
        if (left.Count == 0)
            _corpses.Remove(hero);
        else
            _corpses[hero] = (left, 0);
        Commit("loot_corpse", h, h with { Inventory = inventory, Stash = stash }, []);
        return new LootAnswer(true, recovered, "");
    });

    /// <summary>The hero's corpse: its items in the order a loot takes them, and its Australium; null with none.</summary>
    public override LedgerCorpse? Corpse(string hero) =>
        _corpses.TryGetValue(hero, out var corpse) ? new LedgerCorpse([.. corpse.Items.Select(i => i.Id)], corpse.Australium) : null;

    /// <summary>The process keeps every hero for its life: a leave forgets nothing.</summary>
    public override void Forget(string hero)
    {
    }
}
