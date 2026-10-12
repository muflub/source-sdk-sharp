using SourceSharp.Abi;
using SourceSharp.Client;

namespace TF2.Descent.Client;

/// <summary>
/// Keeps download/maps to the generated levels likely to be loaded again: the
/// current map, the one announced as next, and the most recently used others
/// up to <see cref="KeepLevels"/>. Both this mod's prefetches and the engine's
/// own downloads at connect land there, so without a bound every level joined
/// stays on disk.
///
/// <para>
/// BY RECENCY, NOT "CURRENT AND NEXT". The host keeps a level running after
/// the party leaves it, and going back down from town re-enters the same map:
/// deleting it on the way up cost every down hop a fresh download (about
/// 4.5 s, measured on k3s 2026-10-02). A kept file is always the right map,
/// since the name carries the level's hash; only disk bounds how many to keep.
/// A level's last use is its .bsp's write time, touched at each level init.
/// </para>
///
/// <para>
/// Runs at each level init (the first one covers what an earlier session
/// left). Only names in the host's generated-level form,
/// <c>&lt;mod&gt;-&lt;depth&gt;-&lt;16 hex&gt;</c>, are touched, so the town and
/// any other map are left alone. A .part or .bz2 is a download's leftover and
/// goes unless its level is the current or next one.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class LevelDownloads(IModInfo mod, MapPrefetcher prefetcher, ILog log) : IClientLevelHook, IOrderedHook
{
    /// <summary>
    /// After the core's level hooks: the client's dispatch covers the whole
    /// container, and the core's ClientRopeSystem holds order 0. Tied there,
    /// every level init and shutdown threw before any hook ran.
    /// </summary>
    public int HookOrder => 10;

    /// <summary>How many generated levels to keep besides the current and next one (about 170 KB each).</summary>
    public int KeepLevels { get; init; } = 16;

    public void OnLevelInit(string mapName)
    {
        string dir = prefetcher.DownloadMaps;
        if (!Directory.Exists(dir))
            return;

        string current = Path.Combine(dir, mapName + ".bsp");
        if (IsGeneratedLevel(mapName) && File.Exists(current))
            Touch(current);

        bool Pinned(string map) => map == mapName || map == prefetcher.Next;

        var levels = Directory.EnumerateFiles(dir)
            .Select(file => (File: file, Map: GeneratedMapOf(Path.GetFileName(file))))
            .Where(f => f.Map is not null)
            .GroupBy(f => f.Map!)
            .ToList();

        // The kept: pinned levels, and the most recently used others with a
        // whole map on disk, up to the bound.
        var kept = levels
            .Where(g => !Pinned(g.Key) && File.Exists(Path.Combine(dir, g.Key + ".bsp")))
            .OrderByDescending(g => File.GetLastWriteTimeUtc(Path.Combine(dir, g.Key + ".bsp")))
            .Take(Math.Max(0, KeepLevels))
            .Select(g => g.Key)
            .ToHashSet();

        int removed = 0;
        foreach (var level in levels)
        {
            if (Pinned(level.Key))
                continue;

            foreach (var (file, _) in level)
            {
                // A kept level keeps its map; its leftovers go.
                if (kept.Contains(level.Key) && file.EndsWith(".bsp", StringComparison.Ordinal))
                    continue;

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log.Warning($"[TF2Descent] could not remove the old level {file}: {e.Message}\n");
                }
            }
        }

        if (removed > 0)
            log.Msg($"[TF2Descent] removed {removed} old level file(s) from download/maps\n");
    }

    /// <summary>Marks a level as just used: its write time is the recency the bound sorts by.</summary>
    private void Touch(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning($"[TF2Descent] could not mark {file} as used: {e.Message}\n");
        }
    }

    /// <summary>
    /// The map a file in download/maps belongs to, when it is one of this mod's
    /// generated levels (the map itself, a prefetch's .part, or a packed
    /// .bz2); otherwise null.
    /// </summary>
    public string? GeneratedMapOf(string fileName)
    {
        string? map = StripSuffix(fileName, ".bsp")
                   ?? StripSuffix(fileName, ".bsp.part")
                   ?? StripSuffix(fileName, ".bsp.bz2");
        return map is not null && IsGeneratedLevel(map) ? map : null;
    }

    /// <summary><c>&lt;mod&gt;-&lt;depth&gt;-&lt;16 lowercase hex&gt;</c>, the host's level name.</summary>
    public bool IsGeneratedLevel(string map)
    {
        string prefix = mod.Id + "-";
        if (!map.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string rest = map[prefix.Length..];
        int dash = rest.IndexOf('-');
        if (dash <= 0)
            return false;

        string depth = rest[..dash], hash = rest[(dash + 1)..];
        return depth.All(char.IsAsciiDigit)
            && hash.Length == 16
            && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    }

    private static string? StripSuffix(string name, string suffix)
        => name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : null;
}
