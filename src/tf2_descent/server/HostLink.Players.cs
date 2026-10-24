using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// What a join came to, for the game thread: names and bytes, never a lease.
/// <paramref name="SteamId"/> is the player as the HOST knows them (SDK 2.6.0,
/// PlayerJoin.SteamId): the gateway validated it with Steam, while the engine,
/// hosted with sv_lan 1 and no Steam, reports a LAN or invalid id. Everything
/// about the player is keyed on it. <paramref name="ClientAddr"/> is the
/// player's real address; the engine sees a per-player loopback one.
/// </summary>
public sealed record JoinOutcome(
    bool Joined,
    string? Refusal = null,
    string? CharacterId = null,
    string? ClassName = null,
    string? Name = null,
    int ReachedDepth = 0,
    string? SteamId = null,
    string? ClientAddr = null);

public sealed partial class HostLink
{
    /// <summary>A player's lease and kill-drop reserve, held on the SDK thread only.</summary>
    private sealed record Held(ICharacterLease Lease, IReserve? Reserve, string Peer);

    // SDK thread only: written and read inside OnSdkThread / Send bodies.
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);

    /// <summary>The hook told when a player's lease is lost; set by the constructor.</summary>
    private readonly IHooks<ILeaseHook> _leaseHooks;

    /// <summary>
    /// Spec §5, the join: PlayerJoined, the character (created on the hub only),
    /// its lease, and on a level the reserve and the depth reached. All on the
    /// SDK thread, in order; the game gets the outcome.
    /// <paramref name="engineSteamId"/> is what the engine reports, used only
    /// when the host knows nothing of <paramref name="peer"/> (local mode).
    /// </summary>
    public Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className)
    {
        bool hub = string.Equals(boot.InstanceKind, "hub", StringComparison.Ordinal);

        return Call(async sdk =>
        {
            var joined = await sdk.Session.PlayerJoined(peer, engineSteamId);
            if (!joined.Ok)
                return new JoinOutcome(false, $"{joined.Refusal!.Error}: {joined.Refusal.Reason}");
            if (!joined.Value.Allowed)
                return new JoinOutcome(false, joined.Value.Reason);

            // From here on the player is who the host says, never who the engine says.
            if (HostSteamId(joined.Value) is not { } steamId)
            {
                await sdk.Session.PlayerLeft(joined.Value.SteamId, peer);
                return new JoinOutcome(false, NoSteamId);
            }

            string? clientAddr = joined.Value.ClientAddr;
            if (string.IsNullOrEmpty(clientAddr))
            {
                var known = await sdk.Peers.Resolve(peer);
                clientAddr = known.Ok ? known.Value.ClientAddr : null;
            }

            var list = await sdk.Characters.List(steamId);
            if (!list.Ok)
                return new JoinOutcome(false, $"{list.Refusal!.Error}: {list.Refusal.Reason}");

            var character = list.Value.FirstOrDefault();
            if (character is null)
            {
                if (!hub)
                    return new JoinOutcome(false, "no character: create one in town first");

                var created = await sdk.Characters.Create(steamId, className, $"hero-{steamId}");
                if (!created.Ok)
                    return new JoinOutcome(false, $"{created.Refusal!.Error}: {created.Refusal.Reason}");
                character = created.Value;
            }

            var leased = await sdk.Characters.Lease(character.Id);
            if (!leased.Ok)
                return new JoinOutcome(false, $"{leased.Refusal!.Error}: {leased.Refusal.Reason}");

            var lease = leased.Value;
            lease.LeaseLost += (_, why, detail) =>
                _game.Post(() => _leaseHooks.Each(nameof(ILeaseHook.OnLeaseLost), h => h.OnLeaseLost(steamId, why, detail)));

            IReserve? reserve = null;
            int reached = character.ReachedDepth;
            if (!hub)
            {
                reserve = sdk.Items.OpenReserve(lease);
                if (boot.Depth > reached)
                {
                    var deeper = await lease.SetReachedDepth(boot.Depth);
                    if (deeper.Ok)
                        reached = deeper.Value.ReachedDepth;
                }
            }

            _held[steamId] = new Held(lease, reserve, peer);
            return new JoinOutcome(true, null, character.Id, character.ClassName, character.Name, reached, steamId, clientAddr);
        });
    }

    /// <summary>The refusal for a player nobody can name: no host identity and no engine one.</summary>
    public const string NoSteamId = "Steam authentication is required on this server.";

    /// <summary>
    /// The SteamID a join answered (SDK 2.6.0): the host's when it knows the
    /// peer, else the engine's that was passed in. Null when that is no
    /// SteamID at all: empty, not a number, or 0 (an engine without Steam,
    /// and no gateway that knows the player).
    /// </summary>
    internal static string? HostSteamId(PlayerJoin joined) =>
        ulong.TryParse(joined.SteamId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id) && id != 0
            ? joined.SteamId
            : null;

    /// <summary>
    /// Spec §5: checkpoints the player's sheet. Until gameplay writes a sheet of
    /// its own, the sheet is the one the lease holds, sent back unchanged.
    /// </summary>
    public Task<bool> Checkpoint(string steamId) => Call(async _ =>
    {
        if (!_held.TryGetValue(steamId, out var held) || !held.Lease.IsHeld)
            return false;

        var result = await held.Lease.Checkpoint(held.Lease.Character.Sheet);
        if (!result.Ok)
            _log.Warning($"[TF2Descent] checkpoint for {steamId} refused: {result.Refusal!.Error} ({result.Refusal.Reason})\n");
        return result.Ok;
    });

    /// <summary>
    /// Spec §5, the leave: checkpoint, release (the last call), PlayerLeft. A
    /// lease already released (a hop) or lost is not checkpointed again.
    /// </summary>
    public Task<bool> Leave(string steamId, string peer) => Call(async sdk =>
    {
        _reauths.Forget(steamId);

        if (_held.Remove(steamId, out var held) && held.Lease.IsHeld)
        {
            await held.Lease.Checkpoint(held.Lease.Character.Sheet);
            await held.Lease.Release();
        }

        var left = await sdk.Session.PlayerLeft(steamId, peer);
        return left.Ok;
    });

    /// <summary>The players whose lease is held: what the checkpoint timer walks.</summary>
    public Task<IReadOnlyList<string>> HeldPlayers() =>
        Call(_ => Task.FromResult<IReadOnlyList<string>>(_held.Where(p => p.Value.Lease.IsHeld).Select(p => p.Key).ToArray()));

    /// <summary>
    /// Spec §6, a hop: checkpoint, release, then Ready, in that order, each
    /// awaited, all here on the SDK thread so nothing can put Ready first.
    /// </summary>
    /// <summary>Whose characters a hop releases: everyone held for a same-process hop, else the one player.</summary>
    internal static IReadOnlyList<string> Leaving(bool sameProcess, string steamId, IEnumerable<string> held)
        => sameProcess ? held.ToList() : [steamId];

    private async Task<bool> Hop(HopRequest hop)
    {
        // SAME PROCESS: the whole process becomes the target, so EVERY held
        // player is released first (docs/sdk-integration.md §6) -- a second
        // player's lease would otherwise still sit on the instance the process
        // is leaving. A later player's PrepareHop finds nothing left to release
        // and only says Ready for its own hop.
        foreach (var steamId in Leaving(hop.IsSameProcess, hop.SteamId, _held.Keys))
        {
            if (_held.Remove(steamId, out var held) && held.Lease.IsHeld)
            {
                await held.Lease.Checkpoint(held.Lease.Character.Sheet);
                await held.Lease.Release();
            }
        }

        // The screen before the word: every hold is awaited before Ready, so
        // the game keeps the instance down while a dead player's death screen
        // shows (plan_descent_death §4 M2.3). A hold that throws is logged
        // and the hop goes on — Ready is never lost to a broken hold.
        var ready = await ReadyAfterHold(_readyHolds.All, hop.SteamId, hop.Ready, _log);
        if (!ready.Ok)
            _log.Warning($"[TF2Descent] hop of {hop.SteamId} not acknowledged: {ready.Refusal!.Error} ({ready.Refusal.Reason})\n");
        return ready.Ok;
    }

    /// <summary>
    /// The holds, in order, then the answer of <paramref name="ready"/>: what
    /// the game waits on before a hop may be acknowledged
    /// (plan_descent_death §4 M2.3). A hold that throws is logged, never
    /// fatal — a service left waiting for nothing is worse than a screen cut
    /// short.
    /// </summary>
    internal static async Task<HostResult> ReadyAfterHold(IReadOnlyList<IHopReadyHold> holds, string steamId,
                                                          Func<Task<HostResult>> ready, ILog log)
    {
        foreach (var hold in holds)
        {
            try
            {
                await hold.BeforeReady(steamId);
            }
            catch (Exception e)
            {
                log.Warning($"[TF2Descent] hop of {steamId} held before Ready failed: {e.Message}\n");
            }
        }

        return await ready();
    }
}
