using SourceSharp.Abi;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>Which ledger answers: the hosting service's, or the process's own (unhosted, E8).</summary>
public enum LedgerKind
{
    InMemory,
    Hosted,
}

/// <summary>Where an item sits, as a hero's view of it says.</summary>
public enum ItemPlace
{
    Backpack,
    Equipped,
    Belt,
    Stash,
    BuyBack,
}

/// <summary>
/// One item a hero can see, under the ledger's id for it: where it is (a backpack, belt, stash
/// or buy-back index in <paramref name="Slot"/>; an equipment slot id in
/// <paramref name="EquipSlot"/>, with <paramref name="Slot"/> -1).
/// </summary>
public sealed record LedgerItem(long Id, ItemInstance Item, ItemPlace Place, int Slot, string? EquipSlot = null);

/// <summary>
/// A hero as the ledger holds them: the sheet the capacity and equip rules read, the rules'
/// immutable inventory (backpack, equipment, belt, buy-back, Australium) and the account's
/// stash. Every field is the rules' own type, so a later package hands it to the rules as is.
/// </summary>
public sealed record LedgerHero(string Hero, HeroSheet Sheet, HeroInventory Inventory, StashState Stash)
{
    /// <summary>The hero's price stats (Haggler's, <see cref="IItemLedger.SetPrices"/>): what moves the prices he meets at a vendor.</summary>
    public BuyerPrices Prices { get; init; }

    /// <summary>Every item the hero can see, with where it is.</summary>
    public IEnumerable<LedgerItem> Items =>
        Inventory.Backpack.Select(p => new LedgerItem(p.Value.Id, p.Value.Item, ItemPlace.Backpack, p.Key))
            .Concat(Inventory.Equipped.Select(p => new LedgerItem(p.Value.Id, p.Value.Item, ItemPlace.Equipped, -1, p.Key)))
            .Concat(Inventory.Belt.Select(p => new LedgerItem(p.Value.Id, p.Value.Item, ItemPlace.Belt, p.Key)))
            .Concat(Stash.Slots.Select(p => new LedgerItem(p.Value.Id, p.Value.Item, ItemPlace.Stash, p.Key)))
            .Concat(Inventory.BuyBack.Select((h, i) => new LedgerItem(h.Id, h.Item, ItemPlace.BuyBack, i)));
}

/// <summary>
/// An item on the floor. <paramref name="ForHero"/> is the only hero who may pick it up
/// (R4: a kill's drop is instanced per player); null when anyone may (an item a hero dropped,
/// as the host clears its owner on a drop).
/// </summary>
public sealed record FloorItem(long Id, ItemInstance Item, string? ForHero, Vector Position);

/// <summary>A hero's corpse as the ledger holds it (WO5): the items' ids and the Australium on it.</summary>
public sealed record LedgerCorpse(IReadOnlyList<long> Items, long Australium)
{
    /// <summary>Nothing left on it.</summary>
    public bool Empty => Items.Count == 0 && Australium == 0;
}

/// <summary>An item a vendor offers, at its buy price.</summary>
public sealed record VendorItem(long Id, ItemInstance Item, long Price);

/// <summary>What a price is for, as <see cref="IItemLedger.Price"/> asks it.</summary>
public enum LedgerPrice
{
    /// <summary>A vendor's buy price of the item.</summary>
    Buy,
    /// <summary>What a vendor pays for it.</summary>
    Sell,
    /// <summary>The Spy's fee to identify it.</summary>
    Identify,
    /// <summary>The Engineer's price to repair it (lossless, L7).</summary>
    Repair,
}

/// <summary>
/// What one change did to a hero's view, for the network and UI to follow without polling:
/// the items that came into it, left it, or changed in it (the item, or where it is), the ids
/// of the items that left the world for good (consumed, drunk, kept by a vendor past
/// buy-back), and the Australium before and after.
/// </summary>
public sealed record ItemsChanged(
    string Hero,
    string Operation,
    IReadOnlyList<LedgerItem> Added,
    IReadOnlyList<LedgerItem> Removed,
    IReadOnlyList<LedgerItem> Changed,
    IReadOnlyList<long> Gone,
    long AustraliumBefore,
    long AustraliumAfter);

/// <summary>An operation's answer: the hero after it, the change it made, or why it was refused (a refusal changes nothing).</summary>
public sealed record LedgerResult(bool Ok, string? Refusal, LedgerHero? Hero, ItemsChanged? Change)
{
    public static LedgerResult Refused(string why, LedgerHero? hero = null) => new(false, why, hero, null);
}

/// <summary>A drop's answer: the items now on the floor (none when the roll dropped nothing), or why there are none.</summary>
public sealed record DropResult(bool Ok, string? Refusal, IReadOnlyList<FloorItem> Items)
{
    public static DropResult Nothing { get; } = new(true, null, []);

    public static DropResult Refused(string why) => new(false, why, []);
}

/// <summary>A vendor's stock for one hero, or why there is none.</summary>
public sealed record VendorAnswer(bool Ok, string? Refusal, IReadOnlyList<VendorItem> Items);

/// <summary>
/// A kill's drop for one hero (R4: rolled per eligible player). <paramref name="Tier"/> is the
/// rules' <c>KillRoll</c> tier, -1 for no drop; <paramref name="KillSeq"/> counts kills on this
/// instance; <paramref name="Party"/> the other heroes near (the host books the kill's
/// Australium for each).
/// </summary>
public sealed record KillDrop(string Hero, int Tier, uint KillSeq, string Robot, int Depth, Vector Position,
                              IReadOnlyList<string>? Party = null);

/// <summary>A boss's or champion's own table, rolled for each hero in <paramref name="Heroes"/> (the killer first).</summary>
public sealed record BossDrop(IReadOnlyList<string> Heroes, uint KillSeq, string Robot, int Depth, Vector Position);

/// <summary>
/// The wear one item took since the last write (HE9): <paramref name="Uses"/> times fired or
/// swung (a weapon) and <paramref name="Hits"/> hits taken (armour), by the ledger's id.
/// </summary>
public sealed record WearEvents(long ItemId, int Uses, int Hits);

/// <summary>A repair of a worn item (HE9): the Engineer's (priced, or waived by a dev command) or the field's (Diablo 1's steps).</summary>
public enum WornRepair
{
    /// <summary>The Engineer's: to the maximum, the maximum untouched, for its price in Australium.</summary>
    Town,
    /// <summary>The Engineer's, the price waived (the dev command's <c>town_free</c>).</summary>
    TownUncharged,
    /// <summary>The field's: Diablo 1's steps at the hero's level, each lowering the maximum (L7 as amended).</summary>
    Field,
}

/// <summary>
/// A container's roll for one hero (plan_tf2_rpg 2l, R4: each player in the room gets a roll of
/// their own), as the rules decided it (<c>LootRules.Container</c>): an item of
/// <paramref name="Tier"/> (-1 for none) or a potion of base <paramref name="Potion"/>, rolled
/// from <paramref name="Seed"/> (the rules' per-hero seed of the container,
/// <c>LootRequest.Seed</c>) at the depth's item level, on the floor at
/// <paramref name="Position"/> for that hero alone.
/// </summary>
public sealed record ContainerDrop(string Hero, string Container, ulong Seed, int Tier, string? Potion, int Depth, Vector Position);

/// <summary>
/// The ledger's refusals that are not the rules' own (the rules' are
/// <see cref="InventoryRefusals"/>, <c>CraftRefusals</c>, <c>RepairRefusals</c>).
/// </summary>
public static class LedgerRefusals
{
    /// <summary>The hero is not loaded, and could not be.</summary>
    public const string NoHero = "no_hero";

    /// <summary>No such item on the floor (taken, or never there).</summary>
    public const string NotOnFloor = "not_on_floor";

    /// <summary>The floor item dropped for another hero (R4).</summary>
    public const string WrongOwner = "wrong_owner";

    /// <summary>No item base of that id.</summary>
    public const string UnknownBase = "unknown_base";

    /// <summary>An amount of Australium that is not more than nothing.</summary>
    public const string BadAmount = "bad_amount";

    /// <summary>No such item in the vendor's stock.</summary>
    public const string NotInStock = "not_in_stock";

    /// <summary>The hosting service offers no call for this operation yet; nothing was asked.</summary>
    public const string HostUnsupported = "not_supported_by_the_host_yet";

    /// <summary>The prefix of a refusal or failure of the hosting service: <c>host:Error:reason</c>.</summary>
    public const string HostPrefix = "host:";
}

/// <summary>
/// The one way game code touches items (plan_descent_logic HE0, E8). Two implementations behind
/// it: the hosted ledger over the hosting service, and the in-memory ledger over the rules alone
/// (unhosted), so the whole game plays and proves itself with no host.
///
/// <para>Heroes are named by their key (<see cref="HeroKeys"/>: the SteamID the host knows,
/// hosted). Every item operation is atomic: it applies the rules' transition and its result, or
/// it changes nothing and says why, with the rules' refusal when the rules refuse. Every task
/// completes on the game thread; the in-memory ledger's are complete when returned. Each change
/// is told once to every <see cref="IItemLedgerHook"/>.</para>
/// </summary>
public interface IItemLedger
{
    LedgerKind Kind { get; }

    /// <summary>Loads the hero (hosted: the backpack, the stash in the hub, the sheet and Australium; unhosted: a new hero gets the starting kit).</summary>
    Task<LedgerResult> Load(string hero);

    /// <summary>The hero as last known, or null before a load.</summary>
    LedgerHero? Find(string hero);

    /// <summary>Gives the ledger the hero's current sheet (a level-up, a respec): capacity and requirements follow it.</summary>
    void SetSheet(string hero, HeroSheet sheet);

    /// <summary>
    /// Gives the ledger the hero's price stats (Haggler's, the rules' <c>Pricing.BuyerOf</c>): every
    /// price he meets after it is moved by them (<c>Pricing.ForBuyer</c>). Hosted they move nothing:
    /// the host prices a buy, a sale, an identify and a repair itself, with no stat of the buyer's.
    /// </summary>
    void SetPrices(string hero, BuyerPrices prices);

    /// <summary>
    /// What the hero would pay for <paramref name="what"/> on <paramref name="item"/> now, or be
    /// paid for it (a sale), by the rules and his price stats; null when the rules would refuse it
    /// (an identified item to the Spy; one the Engineer will not repair: not gear, at its
    /// maximum, unusable). Nothing changes.
    /// </summary>
    long? Price(string hero, LedgerPrice what, ItemInstance item);

    /// <summary>What buying back the item sold as <paramref name="index"/> costs: the price it sold for; null for none there.</summary>
    long? BuyBackPrice(string hero, int index);

    /// <summary>Every item on this level's floor.</summary>
    IReadOnlyCollection<FloorItem> Floor { get; }

    /// <summary>A kill's drop for its hero; hosted, the reserve answers with no I/O and the reveal goes out after.</summary>
    Task<DropResult> KillDrop(KillDrop kill);

    /// <summary>A boss's or champion's drops, one table a hero.</summary>
    Task<DropResult> BossDrop(BossDrop boss);

    /// <summary>
    /// A potion a kill's roll dropped (the rules' <c>LootOutcome.Potion</c>, of base
    /// <paramref name="potionBase"/>) put on the floor for its hero. Hosted it is refused
    /// (<see cref="LedgerRefusals.HostUnsupported"/>): a potion is no item to the host's
    /// <c>KillRoll</c>, and the host has no call that mints one.
    /// </summary>
    Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position);

    /// <summary>
    /// A container's item or potion for its hero (WO3), rolled as the rules said and put on the
    /// floor for him. Hosted it is refused (<see cref="LedgerRefusals.HostUnsupported"/>): the
    /// host's <c>IGameRules</c> has no container roll and no call mints a container's drop.
    /// </summary>
    Task<DropResult> ContainerDrop(ContainerDrop drop);

    /// <summary>Picks a floor item up into the first free backpack slot; refused when full or another hero's.</summary>
    Task<LedgerResult> Claim(string hero, long floorId);

    /// <summary>
    /// Australium picked up off the floor (WO2) into the hero's purse. Hosted it is refused
    /// (<see cref="LedgerRefusals.HostUnsupported"/>): the host books a kill's Australium itself
    /// at the reveal and has no call that credits more.
    /// </summary>
    Task<LedgerResult> CollectAustralium(string hero, long amount);

    /// <summary>Drops the item in a backpack slot onto the floor at <paramref name="position"/>.</summary>
    Task<LedgerResult> Drop(string hero, int slot, Vector position);

    /// <summary>Moves a backpack item to another slot (the rules swap with an occupied one).</summary>
    Task<LedgerResult> Move(string hero, int from, int to);

    /// <summary>Merges a backpack stack into another.</summary>
    Task<LedgerResult> Stack(string hero, int from, int to);

    /// <summary>Equips a backpack item in an equipment slot; the rules refuse an unidentified item, an unmet requirement, a wrong slot.</summary>
    Task<LedgerResult> Equip(string hero, int from, string slot);

    Task<LedgerResult> Unequip(string hero, string slot);

    /// <summary>Puts a backpack potion on a belt slot.</summary>
    Task<LedgerResult> ToBelt(string hero, int from, int index);

    Task<LedgerResult> FromBelt(string hero, int index);

    /// <summary>Uses (drinks) the potion on a belt slot: it leaves (HE8 applies what it restores).</summary>
    Task<LedgerResult> Drink(string hero, int index);

    /// <summary>Backpack to the first free stash slot.</summary>
    Task<LedgerResult> Deposit(string hero, int from);

    /// <summary>Stash slot to the first free backpack slot.</summary>
    Task<LedgerResult> Withdraw(string hero, int stashSlot);

    /// <summary>
    /// What a vendor offers this hero in restock <paramref name="restock"/> (the rules'
    /// <c>Vendors.Restock</c> of the town's age): the same stock, less what he bought, until the
    /// restock changes; unhosted, rolled from the rules' <c>Vendors.StockSeed</c> of the session,
    /// the vendor, the hero and the restock. Prices are his (<see cref="SetPrices"/>).
    /// </summary>
    Task<VendorAnswer> Stock(string hero, string vendor, long restock = 0);

    Task<LedgerResult> Buy(string hero, string vendor, long stockId);

    /// <summary>Sells a backpack item at its sell price, moved by the seller's price stats; the vendor keeps it for buy-back at that price.</summary>
    Task<LedgerResult> Sell(string hero, string vendor, int slot);

    /// <summary>Buys back a sold item at the price it sold for.</summary>
    Task<LedgerResult> BuyBack(string hero, string vendor, int index);

    /// <summary>The Spy's identify, for his fee; the item keeps its id.</summary>
    Task<LedgerResult> Identify(string hero, int slot);

    /// <summary>The Engineer's repair, priced; the item keeps its id.</summary>
    Task<LedgerResult> Repair(string hero, int slot);

    /// <summary>A recipe over backpack slots, in the order given.</summary>
    Task<LedgerResult> Craft(string hero, string recipe, IReadOnlyList<int> slots);

    Task<LedgerResult> Salvage(string hero, int slot);

    Task<LedgerResult> OpenCrate(string hero, int slot);

    /// <summary>A dev mint (tf2_descent_give): an item of a base, rolled through the rules, into the backpack.</summary>
    Task<LedgerResult> Give(string hero, string baseId, int rarity, int itemLevel);

    /// <summary>The hero died at <paramref name="position"/>: everything carried goes to the corpse (hosted, <c>IHostLink.RecordDeath</c>).</summary>
    Task<bool> RecordDeath(string hero, Vector position);

    /// <summary>The hero's own corpse back onto them, as many as fit (hosted, <c>IHostLink.LootCorpse</c>).</summary>
    Task<LootAnswer> LootCorpse(string hero);

    /// <summary>
    /// What the hero's corpse holds (WO5): the items' ids, in the order a loot takes them, and the
    /// Australium; null when the hero has no corpse, or when the ledger cannot say (hosted: the
    /// host's SDK lists no corpse).
    /// </summary>
    LedgerCorpse? Corpse(string hero);

    //-------------------------------------------------------------------------
    // HE8 and HE9: consumables used, gear worn and repaired where it is worn
    //-------------------------------------------------------------------------

    /// <summary>Uses one of the stack in a backpack slot (a scroll read, a tome learnt); the last one leaves. Hosted: no host call carries it.</summary>
    Task<LedgerResult> Consume(string hero, int slot);

    /// <summary>
    /// The wear gear took (HE9), applied by the rules' <c>Durability</c> to each item where it
    /// is now, one write for all of them; an item no longer held is skipped. Hosted: no host
    /// call carries it.
    /// </summary>
    Task<LedgerResult> Wear(string hero, IReadOnlyList<WearEvents> events);

    /// <summary>Repairs the item worn in an equipment slot (HE9, L7); the rules' refusal comes back as is. Hosted: no host call carries it.</summary>
    Task<LedgerResult> RepairWorn(string hero, string equipSlot, WornRepair how);

    /// <summary>A dev mutation (<c>tf2_descent_wear</c>): the worn item's durability and maximum set as given, its wear progress cleared.</summary>
    Task<LedgerResult> SetDurability(string hero, string equipSlot, int durability, int maxDurability);

    /// <summary>Drops a hero from the ledger's memory (hosted: the cache, when the player leaves; unhosted: nothing, the process keeps them).</summary>
    void Forget(string hero);
}

/// <summary>Told once per change to a hero's items, on the game thread.</summary>
public interface IItemLedgerHook
{
    void OnItemsChanged(ItemsChanged change);
}
