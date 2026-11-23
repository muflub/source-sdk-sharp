using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// Whether the level now starting is a same-process hop this process began
/// (HostService asks at each level start): a hop makes the process another
/// instance, which Booting tells; any other level change (an operator's
/// changelevel) keeps the instance the process was.
/// </summary>
public interface ISameProcessHop
{
    /// <summary>True once, for the level start a hop to <paramref name="map"/> began; false for any other.</summary>
    bool Take(string map);
}

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
public sealed class LevelHop(IModInfo mod, IServerConsole console, ILog log) : IHostCommandHook, ISameProcessHop
{
    private string? _changingTo;

    // The map this process changed level to for a hop, until that level starts.
    private string? _starting;

    public bool Take(string map)
    {
        bool hop = string.Equals(map, _starting, StringComparison.OrdinalIgnoreCase);
        if (hop)
            _starting = null;
        return hop;
    }

    /// <summary>Where the target's level files are linked: the mod's pool/maps (gameinfo searches |gameinfo_path|pool).</summary>
    public string PoolMaps => Path.Combine(mod.GameDir, "pool", "maps");

    /// <summary>
    /// Unhosted, the installed level of <paramref name="depth"/>: <c>tf2_descent-&lt;depth&gt;-s&lt;seed&gt;</c>
    /// in <see cref="PoolMaps"/>, the lowest seed; null for none. What the town's cage and a level's
    /// way down change level to when no host picks the instance.
    /// </summary>
    public string? InstalledLevel(int depth)
    {
        string dir = PoolMaps;
        if (!Directory.Exists(dir))
            return null;
        return Directory.EnumerateFiles(dir, $"tf2_descent-{depth}-s*.bsp")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(IsMapName)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Unhosted, each depth with a level installed in <see cref="PoolMaps"/> (<see cref="InstalledLevel"/> answers it), shallowest first.</summary>
    public IReadOnlyList<int> InstalledDepths()
    {
        string dir = PoolMaps;
        if (!Directory.Exists(dir))
            return [];
        var depths = new SortedSet<int>();
        foreach (string file in Directory.EnumerateFiles(dir, "tf2_descent-*-s*.bsp"))
        {
            string map = Path.GetFileNameWithoutExtension(file);
            string[] parts = map.Split('-');
            if (IsMapName(map) && parts.Length == 3 && parts[0] == "tf2_descent" && parts[2].StartsWith('s')
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int depth) && depth > 0)
                depths.Add(depth);
        }
        return [.. depths];
    }

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
        _starting = hop.Map;

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
