using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;
using TF2.Descent.Shared;

namespace TF2.Descent.Server;

/// <summary>
/// Passes the host's NextLevel (D-H14) on to clients: the next level's map
/// and the fastdl URL of its .bsp.bz2, so each client fetches it while the
/// party plays and the hop loads it from disk. Nothing is sent when the
/// service has no fastdl (no URL).
///
/// <para>
/// The level keeps the announcement until it shuts down, and sends it again to
/// each client that becomes active. NextLevel arrives as soon as the host has
/// linked the level below, which after a hop is while the party is still
/// loading this level: a single broadcast then reaches nobody.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class LevelPrefetch(ClientMessages messages, ILog log)
    : IHostCommandHook, IOrderedHook, IClientLifecycle, IGameLifecycle
{
    /// <summary>After LevelHop (0) and CommandService (10): IHostCommandHook's orders are unique.</summary>
    public int HookOrder => 20;

    /// <summary>After PlayerService (First + 10).</summary>
    public int ClientLifecycleOrder => ModStages.First + 20;

    /// <summary>After HostService (First + 10).</summary>
    public int LifecycleOrder => ModStages.First + 20;

    private sealed record Announcement(int Depth, string Map, string Url, byte[]? Sha256);

    private Announcement? _next;

    public void OnNextLevel(NextLevelInfo next)
    {
        if (next.MapUrl is not { } url)
        {
            log.Msg($"[TF2Descent] next level {next.Map} (depth {next.Depth}) has no download URL; clients will fetch it at connect\n");
            return;
        }

        byte[]? sha256 = next.MapSha256;
        if (sha256 is not null && sha256.Length != NextLevel.HashBytes)
        {
            log.Warning($"[TF2Descent] the host's hash of {next.Map} is {sha256.Length} bytes, not a SHA-256; sending none\n");
            sha256 = null;
        }

        _next = new Announcement(next.Depth, next.Map, url, sha256);

        if (Send(0, _next))
            log.Msg($"[TF2Descent] told clients to prefetch {next.Map} (depth {next.Depth})\n");
        else
            log.Warning($"[TF2Descent] the next level's URL does not fit a user message: {url}\n");
    }

    public void OnClientActive(int entIndex)
    {
        if (_next is { } next)
            Send(entIndex, next);
    }

    public void OnLevelShutdown() => _next = null;

    private bool Send(int entIndex, Announcement next) =>
        messages.NextLevel(entIndex, next.Depth, next.Map, next.Url, next.Sha256);
}
