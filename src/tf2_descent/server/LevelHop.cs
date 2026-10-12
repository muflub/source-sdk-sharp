using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// A same-process hop's level change (docs/sdk-integration.md §6, the host's
/// local SameProcess travel): once the service has acknowledged Ready, link
/// the target's level files into the mod's <c>pool/maps</c> and change level to
/// the target map. The new level's ordinary start (HostService: Booting with
/// the target map, then MapReady) makes the SDK the target instance, and the
/// players join again there (PlayerService).
///
/// <para>
/// Once per target map: every player's hop names the same map, and only the
/// first changes level. Never across processes, where the service's Retry
/// moves the client instead (CommandService).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class LevelHop(IModInfo mod, IServerConsole console, ILog log) : IHostCommandHook
{
    private string? _changingTo;

    /// <summary>Where the target's level files are linked: the mod's pool/maps (gameinfo searches |gameinfo_path|pool).</summary>
    public string PoolMaps => Path.Combine(mod.GameDir, "pool", "maps");

    public void OnHopped(HopNotice hop)
    {
        if (!hop.SameProcess || !hop.Ready || hop.Map == _changingTo)
            return;

        // The name reaches the console: refuse anything that is not a plain map name.
        if (!IsMapName(hop.Map))
        {
            log.Warning($"[TF2Descent] refusing a hop to '{hop.Map}': not a map name\n");
            return;
        }

        _changingTo = hop.Map;

        Directory.CreateDirectory(PoolMaps);
        foreach (var file in hop.LevelFiles)
            Link(file);

        log.Msg($"[TF2Descent] this process moves to {hop.Map} ({hop.LevelFiles.Count} level file(s) linked)\n");
        console.Execute($"changelevel {hop.Map}");
    }

    private void Link(string file)
    {
        string target = Path.Combine(PoolMaps, Path.GetFileName(file));

        try
        {
            if (File.Exists(target) || new FileInfo(target).LinkTarget is not null)
                File.Delete(target);
            File.CreateSymbolicLink(target, Path.GetFullPath(file));
        }
        catch (IOException e)
        {
            log.Warning($"[TF2Descent] could not link {file} into {PoolMaps}: {e.Message}\n");
        }
    }

    private static bool IsMapName(string map)
        => map.Length > 0 && map.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
