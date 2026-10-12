using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// Players in the hosting service (spec §5): a client that becomes active joins
/// with its character leased; one that leaves checkpoints, releases and leaves;
/// everyone is checkpointed every minute; a lost lease removes the player.
/// </summary>
[SideService(Side.Server)]
public sealed class PlayerService(
    IHostLink link,
    HostService host,
    IConnectedClients clients,
    ClientMessages messages,
    IFrameScheduler scheduler,
    ILog log) : IClientLifecycle, IHostBootHook, ILeaseHook, IHostConnectionHook, IOrderedHook
{
    /// <summary>A joined player: who they are to the engine and to the service.
    /// <paramref name="Name"/> is the character's name as the join answered it —
    /// what the party is told when this player dies (plan_descent_death §4 M2.3).</summary>
    public sealed record Entry(int EntIndex, string SteamId, string Peer, string CharacterId, string? Name = null);

    private readonly Dictionary<int, Entry> _byEntity = new();

    // Clients that became active before this level's Booting answered: after
    // a same-process hop the engine re-activates every client on the new map
    // before the SDK knows which instance it is (sdk-integration.md §6, step 5).
    private readonly SortedSet<int> _waitingForBoot = new();

    // Players whose lease was lost because the service could not be reached
    // (plan_host_sdk P9, criterion 4: a service restart longer than the lease
    // TTL). Held here, out of the checkpoint loop, until a re-join gives them
    // a new lease or SuspendBound runs out.
    private readonly Dictionary<string, Entry> _suspended = new();
    private CancellationTokenSource? _checkpoints;

    public int ClientLifecycleOrder => ModStages.First + 10;
    public int HookOrder => 10;

    /// <summary>The class a new character starts as, on the hub.</summary>
    public string NewCharacterClass { get; init; } = "soldier";

    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Set by the Drain command: no new players.</summary>
    public bool Draining { get; set; }

    /// <summary>How long a player whose lease was lost to an unreachable service is held before being kicked.</summary>
    public TimeSpan SuspendBound { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Whether this player is held while their character is re-leased.</summary>
    public bool IsSuspended(string steamId) => _suspended.ContainsKey(steamId);

    public IReadOnlyCollection<Entry> Players => _byEntity.Values;

    public Entry? BySteamId(string steamId) => _byEntity.Values.FirstOrDefault(e => e.SteamId == steamId);

    public Entry? ByEntity(int entIndex) => _byEntity.GetValueOrDefault(entIndex);

    private bool Hosted => host.Mode == HostingMode.Hosted;

    public string? OnClientConnecting(string name, string address)
    {
        if (!Hosted)
            return null;
        if (Draining)
            return "This server is closing.";
        if (host.Boot is null)
            return "This server is starting; try again in a moment.";
        return null;
    }

    public void OnClientActive(int entIndex)
    {
        if (!Hosted)
            return;

        if (host.Boot is not { } boot)
        {
            _waitingForBoot.Add(entIndex);
            return;
        }

        var peer = clients.Address(entIndex);
        if (peer is null)
            return;    // a bot: not a player of the service

        // The engine's SteamID is only a fallback for a peer the host does not
        // know (local mode): hosted, the engine runs with sv_lan 1 and no
        // Steam, so it reports a LAN or invalid id, 0 included, for players the
        // gateway has validated (sdk-integration.md §5). The join answers who
        // the player really is; nothing here is keyed on the engine's id.
        string engineSteamId = clients.SteamId64(entIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = JoinAsync(entIndex, peer, engineSteamId, boot);
    }

    private async Task JoinAsync(int entIndex, string peer, string engineSteamId, BootInfo boot)
    {
        JoinOutcome outcome;
        try
        {
            outcome = await link.Join(peer, engineSteamId, boot, NewCharacterClass);
        }
        catch (Exception e)
        {
            outcome = new JoinOutcome(false, "the hosting service failed: " + e.Message);
        }

        // Back on the game thread, a frame or more later: the client may have gone.
        if (!outcome.Joined || outcome.SteamId is not { } steamId)
        {
            log.Msg($"[TF2Descent] peer {peer} refused: {outcome.Refusal}\n");
            clients.Kick(entIndex, outcome.Refusal ?? "refused");
            return;
        }

        _byEntity[entIndex] = new Entry(entIndex, steamId, peer, outcome.CharacterId!, outcome.Name);
        link.Players(_byEntity.Count);
        messages.Instance(entIndex, boot.InstanceKind, boot.Depth);
        log.Msg($"[TF2Descent] {steamId} ({outcome.ClientAddr ?? "address unknown"}) joined as {outcome.Name} ({outcome.ClassName}), reached depth {outcome.ReachedDepth}\n");
    }

    /// <summary>
    /// This level's Booting answered. Whoever was here before belongs to the
    /// level this process left, and the hop already released them, so they are
    /// forgotten without a second leave; the clients waiting for the boot join
    /// now, as the instance the process has become.
    /// </summary>
    public void OnBooted(BootInfo info)
    {
        _byEntity.Clear();
        _suspended.Clear();
        link.Players(0);

        var waiting = _waitingForBoot.ToList();
        _waitingForBoot.Clear();

        foreach (int entIndex in waiting)
            OnClientActive(entIndex);
    }

    public void OnClientDisconnected(int entIndex)
    {
        _waitingForBoot.Remove(entIndex);

        // A suspended player's lease is already gone: nothing to leave.
        foreach (var held in _suspended.Values.Where(e => e.EntIndex == entIndex).ToList())
            _suspended.Remove(held.SteamId);

        if (_byEntity.Remove(entIndex, out var entry))
            Leave(entry);
    }

    /// <summary>Checkpoint, release and PlayerLeft for one player, and the count reported.</summary>
    public Task Leave(Entry entry)
    {
        _byEntity.Remove(entry.EntIndex);
        link.Players(_byEntity.Count);
        return link.Leave(entry.SteamId, entry.Peer);
    }

    public void OnLive()
    {
        _checkpoints?.Cancel();
        _checkpoints = new CancellationTokenSource();
        _ = CheckpointEveryInterval(_checkpoints.Token);
    }

    private async Task CheckpointEveryInterval(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await scheduler.Delay(CheckpointInterval, stop);
                foreach (var entry in _byEntity.Values.ToArray())
                    _ = link.Checkpoint(entry.SteamId);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void OnLeaseLost(string steamId, LeaseLostReason reason, string detail)
    {
        if (BySteamId(steamId) is not { } entry)
            return;

        // Stop them acting rather than let their actions fail silently (spec §5).
        _byEntity.Remove(entry.EntIndex);
        link.Players(_byEntity.Count);

        // Unreachable means the service was away, not that the character is
        // someone else's: hold the player and lease the character again, now
        // and whenever the connection comes back. Stale or revoked is final.
        if (reason != LeaseLostReason.Unreachable)
        {
            clients.Kick(entry.EntIndex, $"Your character is no longer held here ({reason}).");
            return;
        }

        _suspended[steamId] = entry;
        messages.Notice(entry.EntIndex, "Lost contact with the hosting service: holding your character while it reconnects.");
        log.Msg($"[TF2Descent] {steamId}'s lease was lost ({detail}); holding them and re-leasing\n");

        _ = Rejoin(entry);
        _ = KickIfStillSuspended(entry);
    }

    public void OnConnectionChanged(bool connected)
    {
        if (!connected)
            return;

        foreach (var entry in _suspended.Values.ToList())
            _ = Rejoin(entry);
    }

    private async Task Rejoin(Entry entry)
    {
        if (host.Boot is not { } boot)
            return;

        JoinOutcome outcome;
        try
        {
            outcome = await link.Join(entry.Peer, entry.SteamId, boot, NewCharacterClass);
        }
        catch (Exception e)
        {
            outcome = new JoinOutcome(false, e.Message);
        }

        // Back on the game thread: they may have left, or a sibling attempt won.
        // A peer that now names someone else is refused by the host
        // (steamid_mismatch), so a re-join that succeeds is this player.
        if (!outcome.Joined || !_suspended.Remove(entry.SteamId))
        {
            if (!outcome.Joined)
                log.Msg($"[TF2Descent] re-leasing {entry.SteamId} failed: {outcome.Refusal}; still holding\n");
            return;
        }

        _byEntity[entry.EntIndex] = entry with { CharacterId = outcome.CharacterId! };
        link.Players(_byEntity.Count);
        messages.Notice(entry.EntIndex, "Reconnected: your character is held here again.");
        log.Msg($"[TF2Descent] {entry.SteamId} re-leased\n");
    }

    private async Task KickIfStillSuspended(Entry entry)
    {
        await scheduler.Delay(SuspendBound);

        if (_suspended.Remove(entry.SteamId))
            clients.Kick(entry.EntIndex, "Your character could not be held here again (the hosting service did not come back).");
    }
}
