using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server.Items;

/// <summary>What a hero holds on the host, as one load: the character (sheet, Australium), the backpack and, in the hub, the account's stash.</summary>
public sealed record HostInventory(HostCharacter Character, IReadOnlyList<HostItem> Backpack, IReadOnlyList<HostItem> Stash);

/// <summary>
/// The host SDK's item calls as the hosted ledger makes them (docs/sdk-integration.md §7), every
/// task completing on the game thread. <see cref="HostLink"/> is the one implementation: it
/// holds each player's lease and reserve on the SDK thread and puts the lease on every call, so
/// a player is named here by SteamID only. A player with no lease held here is refused locally
/// (<see cref="HostError.NoCharacter"/>), nothing sent. An item's
/// <see cref="HostItem.ForCharacter"/> comes back as the SteamID of the player it dropped for
/// when that player is held here.
/// </summary>
public interface IHostItems
{
    /// <summary><c>Characters</c>' leased character, <c>Items.GetBackpack</c>, and <c>Items.GetStash</c> when <paramref name="stash"/>.</summary>
    Task<HostResult<HostInventory>> Inventory(string steamId, bool stash);

    /// <summary><c>IReserve.NextForTier</c>: a queue pop on the SDK thread, no I/O; null when the tier is empty or no reserve is open.</summary>
    Task<HostItem?> TakeReserved(string steamId, int tier);

    /// <summary><c>IReserve.Reveal</c>: reports the kill that revealed a reserve item, after it was spawned.</summary>
    Task<HostResult<RevealOutcome>> Reveal(string steamId, HostItem item, uint killSeq, string robot, IReadOnlyList<string> party, Vector position);

    /// <summary><c>Items.MintDrops</c>: the empty-reserve fallback (tier ≥ 0) or a boss's table (tier -1).</summary>
    Task<HostResult<IReadOnlyList<HostItem>>> MintDrops(string steamId, uint killSeq, string robot, int tier, IReadOnlyList<string> party, Vector position);

    Task<HostResult<HostItem>> Claim(string steamId, string itemId);

    Task<HostResult<HostItem>> Drop(string steamId, string itemId, Vector position);

    Task<HostResult<HostItem>> Move(string steamId, string itemId, int slot);

    Task<HostResult<HostItem>> StashMove(string steamId, string itemId, bool toStash, int slot);

    Task<HostResult<IReadOnlyList<HostItem>>> VendorStock(string vendor);

    Task<HostResult<HostItem>> Buy(string steamId, string itemId, string vendor);

    /// <summary>Answers the character's Australium after the sale.</summary>
    Task<HostResult<long>> Sell(string steamId, string itemId, string vendor);

    Task<HostResult<HostItem>> BuyBack(string steamId, string itemId, string vendor);

    Task<HostResult<HostItem>> Identify(string steamId, string itemId);

    Task<HostResult<HostItem>> Repair(string steamId, string itemId);

    /// <summary>The host answers only the first item a crate produced.</summary>
    Task<HostResult<HostItem>> OpenCrate(string steamId, string itemId);

    /// <summary>The host answers only the first item a recipe produced.</summary>
    Task<HostResult<HostItem>> Craft(string steamId, string recipe, IReadOnlyList<string> inputs);

    Task<HostResult<IReadOnlyList<HostItem>>> Salvage(string steamId, string itemId);

    /// <summary>SDK 2.7 <c>Items.Move</c> into a slot of a container: an equip, an unequip, the belt (the slot of an equip is its index in the rules' <c>EquipSlots</c>).</summary>
    Task<HostResult<HostItem>> Move(string steamId, string itemId, HostContainer container, int slot);

    /// <summary>SDK 2.7 <c>Items.Consume</c>: the item as it is after (its count lowered, or its state <see cref="HostItemState.Consumed"/>).</summary>
    Task<HostResult<HostItem>> Consume(string steamId, string itemId, int count);

    /// <summary>SDK 2.7 <c>Items.Wear</c>: up to 64 items, each at most once, in one write; each item answered as it is after.</summary>
    Task<HostResult<IReadOnlyList<HostItem>>> Wear(string steamId, IReadOnlyList<HostWear> entries);

    /// <summary>SDK 2.7 <c>Items.Repair</c> of a kind: the town's (hub only, priced) or the field's with <paramref name="kitId"/> (the kit used up in the same write).</summary>
    Task<HostResult<HostItem>> Repair(string steamId, string itemId, HostRepairKind kind, string? kitId);

    /// <summary>SDK 2.7 <c>Items.OpenContainer</c>: the rules' container roll replayed by the host and minted to this floor for the opener; refused <c>already_opened</c> the second time.</summary>
    Task<HostResult<IReadOnlyList<HostItem>>> OpenContainer(string steamId, uint containerSeq);

    /// <summary>
    /// SDK 2.8 <c>Items.KillAustralium</c> under the killer's lease: the host replays the kill's roll
    /// (<paramref name="killSeq"/> is the seat-0 kill sequence, whose roll carries the Australium) and
    /// mints one pile on this floor (<see cref="HostItem.IsAustraliumPile"/>, <c>Count</c> the roll);
    /// <c>already_minted</c> a second time, <c>no_australium</c> for a roll of none.
    /// </summary>
    Task<HostResult<HostItem>> KillAustralium(string steamId, uint killSeq, string robot, int partySize, Vector position);

    /// <summary>
    /// SDK 2.8 <c>Items.ClaimAustralium</c>: the pile picked up by the player (the killer or one of
    /// his party); every party member on this level is credited his share at once. Each credit names
    /// its member by SteamID when that player is held here (else by the host's character id).
    /// </summary>
    Task<HostResult<IReadOnlyList<HostAustraliumCredit>>> ClaimAustralium(string steamId, string pileId);

    /// <summary>
    /// SDK 2.8 <c>Items.Break</c> under the player's lease: the host replays the rules'
    /// <c>BreakableDrops</c> for him, credits its Australium to him and puts its items on this floor
    /// for him; <c>already_broken</c> a second time.
    /// </summary>
    Task<HostResult<HostBreak>> Break(string steamId, uint breakableSeq);

    /// <summary>SDK 2.8 <c>Items.Swap</c>: two carried items exchange their container and slot in one write; answers [a, b] as they are after.</summary>
    Task<HostResult<IReadOnlyList<HostItem>>> Swap(string steamId, string itemA, string itemB);

    /// <summary>SDK 2.8 <c>Items.Merge</c>: <paramref name="fromItemId"/>'s stack into <paramref name="intoItemId"/>'s; answers [into, from] as they are after (from <see cref="HostItemState.Consumed"/> when used up).</summary>
    Task<HostResult<IReadOnlyList<HostItem>>> Merge(string steamId, string fromItemId, string intoItemId);

    /// <summary><c>Items.RecordDeath</c> under the player's lease (<see cref="IHostLink.RecordDeath"/>, the same method): true when the host took it.</summary>
    Task<bool> RecordDeath(string steamId, Vector position);

    /// <summary><c>Items.LootCorpse</c> under the player's lease (<see cref="IHostLink.LootCorpse"/>, the same method).</summary>
    Task<LootAnswer> LootCorpse(string steamId);
}
