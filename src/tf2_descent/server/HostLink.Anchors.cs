using System.Text;

using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>A placed teleporter as the host keeps it (D-H16's anchor): whose, which level copy and depth, where.</summary>
public sealed record PortalAnchor(string Owner, string LevelId, int Depth, Vector Position, float Yaw);

/// <summary>
/// The host's teleporter anchors (D-H16, SDK 2.5.0's <c>ITravel.PlaceAnchor</c> and its
/// siblings, plan_descent_death §4 M3) for the town portal (plan_descent_logic WO4). Hosted, a
/// level and the town are two processes: the level tells the host where a hero's teleporter
/// stands, the town reads the party's teleporters to lay out its exit pads, and a return goes
/// through the host, which sends the caller alone to that level copy: "the running instance, or
/// a fresh instance of the same level" (plan_descent_death §4 M3; the SDK's
/// <c>ReturnToAnchor</c>). Every task completes on the game thread. Unhosted, nothing calls it.
/// </summary>
public interface IHostAnchors
{
    /// <summary>On a level: the hero's teleporter is here (a new one replaces the old). False on a refusal, logged.</summary>
    Task<bool> Place(string steamId, Vector at, float yaw);

    /// <summary>The hero's teleporter is gone. False on a refusal, logged.</summary>
    Task<bool> Clear(string steamId);

    /// <summary>In town: the teleporters of the hero's party (his own when in none), one exit pad each. Empty on a refusal.</summary>
    Task<IReadOnlyList<PortalAnchor>> Party(string steamId);

    /// <summary>
    /// In town: the hero alone through <paramref name="anchorOwner"/>'s teleporter on level copy
    /// <paramref name="levelId"/>. The arrival (<see cref="ArrivalKey"/>, naming the anchor's
    /// owner) is written into that level copy's store first, as the SDK asks.
    /// </summary>
    Task<TravelAnswer> Return(string steamId, string anchorOwner, string levelId);

    /// <summary>On a level: whose teleporter brought the hero here (the arrival the town wrote), taken once; null for none.</summary>
    Task<string?> TakeArrival(string steamId);

    /// <summary>On a level: the teleporters the host lists for this level copy at MapReady (a fresh instance re-creates them).</summary>
    Task<IReadOnlyList<PortalAnchor>> OnThisLevel();
}

public sealed partial class HostLink : IHostAnchors
{
    /// <summary>The level-copy store key of a hero arriving through a teleporter (the SDK's own example).</summary>
    public static string ArrivalKey(string characterId) => "arrival/" + characterId;

    private static PortalAnchor ToAnchor(HostAnchor a) =>
        new(a.Owner, a.LevelId, a.Depth, new Vector(a.Position.X, a.Position.Y, a.Position.Z), a.Yaw);

    public Task<bool> Place(string steamId, Vector at, float yaw) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return false;
        var placed = await sdk.Travel.PlaceAnchor(held.Lease, new HostVec3(at.X, at.Y, at.Z), yaw);
        if (!placed.Ok)
            _log.Warning($"[TF2Descent/Portal] the host refused {steamId}'s teleporter: {placed.Refusal!.Error} ({placed.Refusal.Reason})\n");
        return placed.Ok;
    });

    public Task<bool> Clear(string steamId) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return false;
        var cleared = await sdk.Travel.ClearAnchor(held.Lease);
        if (!cleared.Ok)
            _log.Warning($"[TF2Descent/Portal] the host refused to clear {steamId}'s teleporter: {cleared.Refusal!.Error} ({cleared.Refusal.Reason})\n");
        return cleared.Ok;
    });

    public Task<IReadOnlyList<PortalAnchor>> Party(string steamId) => Call<IReadOnlyList<PortalAnchor>>(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return [];
        var listed = await sdk.Travel.ListPartyAnchors(held.Lease);
        if (!listed.Ok)
        {
            _log.Warning($"[TF2Descent/Portal] the host refused {steamId}'s party teleporters: {listed.Refusal!.Error} ({listed.Refusal.Reason})\n");
            return [];
        }
        return listed.Value.Select(ToAnchor).ToArray();
    });

    public Task<TravelAnswer> Return(string steamId, string anchorOwner, string levelId) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return new TravelAnswer(TravelState.Refused, TimeSpan.Zero, "no character held here");

        var lease = held.Lease;
        var wrote = await sdk.Store.LevelCopy(levelId).Set(ArrivalKey(lease.CharacterId), Encoding.UTF8.GetBytes(anchorOwner));
        if (!wrote.Ok)
            _log.Warning($"[TF2Descent/Portal] the arrival through {anchorOwner}'s teleporter was not written: {wrote.Refusal!.Error} ({wrote.Refusal.Reason}); the level will place {steamId} at its arrival points\n");

        var outcome = await sdk.Travel.ReturnToAnchor(lease, anchorOwner);
        return outcome.Ok
            ? new TravelAnswer(outcome.Value.State, outcome.Value.Eta, outcome.Value.Reason)
            : new TravelAnswer(TravelState.Refused, TimeSpan.Zero, $"{outcome.Refusal!.Error}: {outcome.Refusal.Reason}");
    });

    public Task<string?> TakeArrival(string steamId) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return null;
        string key = ArrivalKey(held.Lease.CharacterId);
        var entry = await sdk.Store.Level.Get(key);
        if (!entry.Ok || entry.Value is null)
            return null;
        await sdk.Store.Level.Delete(key);
        return Encoding.UTF8.GetString(entry.Value.Value);
    });

    public Task<IReadOnlyList<PortalAnchor>> OnThisLevel() =>
        Call(sdk => Task.FromResult<IReadOnlyList<PortalAnchor>>(sdk.Session.LevelAnchors.Select(ToAnchor).ToArray()));
}
