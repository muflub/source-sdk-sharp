using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Descent.Server;

/// <summary>
/// Dying, hosted (plan_descent_death §4 M2): the body is kept, the screen
/// shows, the death is recorded with the host at once, and the hop's Ready
/// waits until the screen has shown for <see cref="ScreenHold"/>. Respawn
/// stays refused while the death waits to be told — a record the host never
/// took is a character whose items are nowhere, so after
/// <see cref="DeathTimeout"/> the player is disconnected instead. Unhosted,
/// none of this runs: the stock respawn cycle stands (decision 6.1).
/// </summary>
[SideService(Side.Server)]
public sealed class DeathService(
    IHostLink link,
    HostService host,
    PlayerService players,
    Corpses corpses,
    IScreenEffects screen,
    ClientMessages messages,
    IConnectedClients clients,
    IFrameScheduler scheduler,
    ILog log) : IPlayerDeathHook, IRespawnGate, IHopReadyHold, IClientLifecycle, IGameLifecycle
{
    /// <summary>How long the death screen shows before the hop may say Ready (plan §1.3).</summary>
    public TimeSpan ScreenHold { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a failing <c>RecordDeath</c> is retried before the player is disconnected (plan §4 M2.4).</summary>
    public TimeSpan DeathTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The first retry waits this long, then doubles to <see cref="MaxRetryBackoff"/>.</summary>
    public TimeSpan FirstRetryBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The retry road's ceiling per step.</summary>
    public TimeSpan MaxRetryBackoff { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>For tests: a clock other than the process'.</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    private sealed record DeadPlayer(int EntIndex, string? Name, long DiedAtMs, EntityHandle Body);

    // Keyed by SteamID: the hop asks by SteamID, the gate by entity. A death
    // lives from the kill until the record is taken (then the level change
    // clears it), a leave (then the reconnect joins as a town player), or the
    // timeout kicks them.
    private readonly Dictionary<string, DeadPlayer> _dead = new(StringComparer.Ordinal);

    public int ClientLifecycleOrder => ModStages.First + 40;

    public int LifecycleOrder => ModStages.First + 30;

    /// <summary>
    /// What <c>PlayerDeaths.OnKilled</c> hands the mods: keep the body the
    /// stock cycle just made, show the screen, tell the party, and put the
    /// record with the host before anything else can end the level.
    /// </summary>
    public void OnPlayerDied(PlayerDeath death)
    {
        if (host.Mode != HostingMode.Hosted)
            return;
        if (players.ByEntity(death.EntIndex) is not { } entry)
            return;     // a bot, or a player the join never finished

        _dead[entry.SteamId] = new DeadPlayer(death.EntIndex, entry.Name, Clock(), death.Body);

        // The ragdoll the stock cycle retired at its lifetime: a kept body
        // stands when its think comes (Corpses.Keep).
        if (death.Body != EntityHandle.None)
            corpses.Keep(death.Body);

        // The screen: dark red, held until the HUD's own timer lifts it.
        screen.Fade(death.Player, 120, 0, 0, 255, duration: 0f, holdTime: 0f, FadeFlags.StayOut);
        messages.Notice(death.EntIndex, "You have died");
        messages.Notice(death.EntIndex, "Returning to town…");
        messages.Notice(0, $"{entry.Name ?? "Someone"} has died");

        // Fire-and-forget task: the first RecordDeath call runs synchronously
        // to its await, so a link that answers at once is fully asked before
        // OnPlayerDied returns (plan §4 M2.3); a link that stalls leaves the
        // loop on the frame scheduler. A throw the loop's own try does not
        // cover (IsDead, the kick) would otherwise land as an unobserved
        // Task: the log tail must show why the retry road vanished.
        _ = RetryRecord(death.EntIndex, entry.SteamId, death.Origin)
            .ContinueWith(
                t => log.Warning(
                    $"[TF2Descent] RetryRecord faulted: {t.Exception?.ToString()}\n"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    /// <summary>
    /// The road plan §4 M2.4: ask, and on a refusal or a throw wait a doubled
    /// back-off, until the host took the record, the player is no longer this
    /// death's, or the 60 s ran out and they are disconnected — the host still
    /// holds the character, as for any drop.
    /// </summary>
    private async Task RetryRecord(int entIndex, string steamId, Vector origin)
    {
        var started = Clock();
        var backoff = FirstRetryBackoff;
        while (true)
        {
            // Top of the road: a player who left during the last delay stops
            // being asked, and is never kicked. The first pass finds the
            // death this very call made.
            if (!IsDead(steamId, entIndex))
                return;
            bool ok;
            try
            {
                ok = await link.RecordDeath(steamId, origin);
            }
            catch (Exception e)
            {
                ok = false;
                log.Warning($"[TF2Descent] RecordDeath of {steamId} failed: {e.Message}\n");
            }
            if (ok)
                return;
            if (!IsDead(steamId, entIndex))
                return;     // left during a delay, or a new level: stop asking, never kick

            if (Clock() - started >= DeathTimeout.TotalMilliseconds)
            {
                clients.Kick(entIndex, "Could not reach the hosting service");
                return;
            }

            var waited = Clock() - started;
            var backMs = (long)backoff.TotalMilliseconds;
            if (backMs > (long)MaxRetryBackoff.TotalMilliseconds)
                backMs = (long)MaxRetryBackoff.TotalMilliseconds;
            if (waited + backMs > (long)DeathTimeout.TotalMilliseconds)
                backMs = (long)DeathTimeout.TotalMilliseconds - waited;   // the last ask sits on the line
            backoff = TimeSpan.FromMilliseconds(Math.Min(backMs * 2, MaxRetryBackoff.TotalMilliseconds));
            await scheduler.Delay(TimeSpan.FromMilliseconds(backMs));
        }
    }

    private bool IsDead(string steamId, int entIndex)
    {
        return _dead.TryGetValue(steamId, out var dead) && dead.EntIndex == entIndex;
    }

    /// <summary>A death the host has not taken yet keeps its player dead (plan §4 M2.1).</summary>
    public bool MayRespawn(EntityHandle player, int entIndex)
    {
        return host.Mode != HostingMode.Hosted || !_dead.Values.Any(d => d.EntIndex == entIndex);
    }

    /// <summary>
    /// The hop's Ready waits here until the screen has shown (plan §4 M2.3).
    /// The death is already recorded when a hold is asked, so this is the only
    /// part of dying that makes the service wait.
    /// </summary>
    public async Task BeforeReady(string steamId)
    {
        if (!_dead.TryGetValue(steamId, out var dead))
            return;
        var remaining = dead.DiedAtMs + ScreenHold.TotalMilliseconds - Clock();
        if (remaining <= 0)
            return;
        await scheduler.Delay(TimeSpan.FromMilliseconds(remaining));
    }

    /// <summary>Left during a retry: stop asking, never kick (the host keeps the lease anyway).</summary>
    public void OnClientDisconnected(int entIndex)
    {
        foreach (var steamId in _dead.Where(kv => kv.Value.EntIndex == entIndex).Select(kv => kv.Key).ToArray())
            _dead.Remove(steamId);
    }

    /// <summary>
    /// A level change (a same-process hop to town included): the deaths of the
    /// level that ended are that level's news. Players who stay connected are
    /// alive on the new map; a death whose record is still outstanding had
    /// already run out its road, and a reconnect joins as a town player.
    /// </summary>
    public void OnLevelInit(string mapName) => _dead.Clear();
}
