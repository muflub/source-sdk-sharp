using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>What looting a corpse came to: how many items came back, or why nothing did.</summary>
public sealed record LootAnswer(bool Ok, int Recovered, string Reason);

public sealed partial class HostLink
{
    /// <summary>
    /// D-H16, the death record: <c>Items.RecordDeath</c> under the player's
    /// held lease, which moves everything they carried onto their corpse and
    /// records where they died. A refusal — or no lease held here, which is
    /// the same answer the player sees — is false: the caller retries, so this
    /// never throws at it (plan_descent_death §4 M2.4).
    /// </summary>
    public Task<bool> RecordDeath(string steamId, Vector position) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return false;

        // Vector3 is the host SDK's own point type (System.Numerics), not the
        // managed Abi.Vector the game names its positions with.
        var record = await sdk.Items.RecordDeath(held.Lease,
            new System.Numerics.Vector3(position.X, position.Y, position.Z));
        if (!record.Ok)
            _log.Warning($"[TF2Descent] death of {steamId} refused: {record.Refusal!.Error} ({record.Refusal.Reason})\n");
        return record.Ok;
    });

    /// <summary>
    /// D-H16, looting: <c>Items.LootCorpse</c> under the looter's own held
    /// lease — the host only ever moves the caller's own corpse's items, so
    /// whose body it is stays the game's question (plan_descent_death §4 M4).
    /// </summary>
    public Task<LootAnswer> LootCorpse(string steamId) => Call(async sdk =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return new LootAnswer(false, 0, "no character held here");

        var loot = await sdk.Items.LootCorpse(held.Lease);
        return loot.Ok
            ? new LootAnswer(true, loot.Value.Count, "")
            : new LootAnswer(false, 0, $"{loot.Refusal!.Error}: {loot.Refusal.Reason}");
    });
}
