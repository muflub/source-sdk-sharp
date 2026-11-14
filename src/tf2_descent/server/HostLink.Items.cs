using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server;

/// <summary>
/// The item calls (docs/sdk-integration.md §7) under each player's held lease, for the hosted
/// item ledger. Every body runs on the SDK thread through <see cref="Call{T}"/>, so the lease
/// and the reserve never leave it; the answers are records with no state.
/// </summary>
public sealed partial class HostLink : IHostItems
{
    /// <summary>Runs <paramref name="call"/> with the player's held lease; refused locally, nothing sent, when none is held here.</summary>
    private Task<HostResult<T>> Leased<T>(string steamId, Func<HostSdk, Held, Task<HostResult<T>>> call) => Call(async sdk =>
        _held.TryGetValue(steamId, out var held) && held.Lease.IsHeld
            ? await call(sdk, held)
            : HostResult<T>.Refused(new HostRefusal(HostError.NoCharacter, "no_lease", $"no character of {steamId} is held here", Local: true)));

    private static System.Numerics.Vector3 Point(Vector v) => new(v.X, v.Y, v.Z);

    /// <summary>The character ids of the held players among <paramref name="steamIds"/> (the host names party members by character). SDK thread.</summary>
    private List<string> CharacterIds(IEnumerable<string> steamIds) =>
        [.. steamIds.Select(s => _held.TryGetValue(s, out var h) ? h.Lease.CharacterId : null).OfType<string>()];

    /// <summary>The item with its owner named by SteamID when that character is held here. SDK thread.</summary>
    private HostItem Named(HostItem item)
    {
        if (string.IsNullOrEmpty(item.ForCharacter))
            return item;
        foreach (var (steamId, held) in _held)
            if (held.Lease.CharacterId == item.ForCharacter)
                return item with { ForCharacter = steamId };
        return item;
    }

    private HostResult<HostItem> Named(HostResult<HostItem> r) => r.Ok ? HostResult<HostItem>.Success(Named(r.Value)) : r;

    private HostResult<IReadOnlyList<HostItem>> Named(HostResult<IReadOnlyList<HostItem>> r) =>
        r.Ok ? HostResult<IReadOnlyList<HostItem>>.Success([.. r.Value.Select(Named)]) : r;

    public Task<HostResult<HostInventory>> Inventory(string steamId, bool stash) => Leased(steamId, async (sdk, held) =>
    {
        var character = held.Lease.Character;
        var backpack = await sdk.Items.GetBackpack(held.Lease.CharacterId);
        if (!backpack.Ok)
            return HostResult<HostInventory>.Refused(backpack.Refusal!);
        IReadOnlyList<HostItem> stashed = [];
        if (stash)
        {
            var s = await sdk.Items.GetStash(character.Account, character.Hardcore);
            if (!s.Ok)
                return HostResult<HostInventory>.Refused(s.Refusal!);
            stashed = s.Value;
        }
        return HostResult<HostInventory>.Success(new HostInventory(character, backpack.Value, stashed));
    });

    public Task<HostItem?> TakeReserved(string steamId, int tier) => Call(_ =>
        Task.FromResult(_held.TryGetValue(steamId, out var held) && held.Reserve is { IsOpen: true } reserve
            ? reserve.NextForTier(tier) is { } item ? Named(item) : null
            : null));

    public Task<HostResult<RevealOutcome>> Reveal(string steamId, HostItem item, uint killSeq, string robot, IReadOnlyList<string> party, Vector position) =>
        Leased(steamId, (_, held) => held.Reserve is { } reserve
            ? reserve.Reveal(item, killSeq, robot, CharacterIds(party), Point(position))
            : Task.FromResult(HostResult<RevealOutcome>.Refused(new HostRefusal(HostError.NoItem, "no_reserve", $"no reserve is open for {steamId}", Local: true))));

    public Task<HostResult<IReadOnlyList<HostItem>>> MintDrops(string steamId, uint killSeq, string robot, int tier, IReadOnlyList<string> party, Vector position) =>
        Leased(steamId, async (sdk, held) => Named(await sdk.Items.MintDrops(held.Lease, killSeq, robot, tier, CharacterIds(party), Point(position))));

    public Task<HostResult<HostItem>> Claim(string steamId, string itemId) =>
        Leased(steamId, async (sdk, held) => Named(await sdk.Items.Claim(held.Lease, itemId)));

    public Task<HostResult<HostItem>> Drop(string steamId, string itemId, Vector position) =>
        Leased(steamId, async (sdk, held) => Named(await sdk.Items.Drop(held.Lease, itemId, Point(position))));

    public Task<HostResult<HostItem>> Move(string steamId, string itemId, int slot) =>
        Leased(steamId, (sdk, held) => sdk.Items.Move(held.Lease, itemId, slot));

    public Task<HostResult<HostItem>> StashMove(string steamId, string itemId, bool toStash, int slot) =>
        Leased(steamId, (sdk, held) => sdk.Items.StashMove(held.Lease, itemId, toStash, slot));

    public Task<HostResult<IReadOnlyList<HostItem>>> VendorStock(string vendor) => Call(sdk => sdk.Items.GetVendorStock(vendor));

    public Task<HostResult<HostItem>> Buy(string steamId, string itemId, string vendor) =>
        Leased(steamId, (sdk, held) => sdk.Items.Buy(held.Lease, itemId, vendor));

    public Task<HostResult<long>> Sell(string steamId, string itemId, string vendor) =>
        Leased(steamId, (sdk, held) => sdk.Items.Sell(held.Lease, itemId, vendor));

    public Task<HostResult<HostItem>> BuyBack(string steamId, string itemId, string vendor) =>
        Leased(steamId, (sdk, held) => sdk.Items.BuyBack(held.Lease, itemId, vendor));

    public Task<HostResult<HostItem>> Identify(string steamId, string itemId) =>
        Leased(steamId, (sdk, held) => sdk.Items.Identify(held.Lease, itemId));

    public Task<HostResult<HostItem>> Repair(string steamId, string itemId) =>
        Leased(steamId, (sdk, held) => sdk.Items.Repair(held.Lease, itemId));

    public Task<HostResult<HostItem>> OpenCrate(string steamId, string itemId) =>
        Leased(steamId, (sdk, held) => sdk.Items.OpenCrate(held.Lease, itemId));

    public Task<HostResult<HostItem>> Craft(string steamId, string recipe, IReadOnlyList<string> inputs) =>
        Leased(steamId, (sdk, held) => sdk.Items.Craft(held.Lease, recipe, inputs));

    public Task<HostResult<IReadOnlyList<HostItem>>> Salvage(string steamId, string itemId) =>
        Leased(steamId, (sdk, held) => sdk.Items.Salvage(held.Lease, itemId));
}
