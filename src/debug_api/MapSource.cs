using System.Reflection;

namespace Debug.Api;

/// <summary>
/// Finds map files and remembers what was read out of them.
///
/// Parsing is cached because the static view is polled like everything else and
/// a map is an eleven megabyte file: re-reading it for every request would turn
/// a debugging tool into the reason the disk is busy. The cache is keyed on the
/// file's write time as well as its path, so recompiling a map and reloading the
/// page shows the new one -- which is the entire loop this is meant to support.
///
/// Nothing here touches the engine. The static view therefore works with no game
/// running at all, which is the point of having one: a map's I/O graph is a
/// property of the map, and needing to launch the game to look at it is exactly
/// the slow loop this whole API exists to remove.
/// </summary>
public sealed class MapSource
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(DateTime WrittenAt, bool HasGeometry, BspFile.Contents Contents);

    /// <summary>
    /// The whole-file reports, cached the same way and for the same reason.
    ///
    /// Separate from the parse cache above rather than folded into it: a report
    /// reads every lump the file has, and making the entity views pay for the
    /// pakfile listing and the static props would be charging the common case
    /// for the rare one.
    /// </summary>
    private readonly Dictionary<string, (DateTime WrittenAt, BspReport Report)> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where maps live. Worked out from the assembly rather than the working
    /// directory: the engine's working directory is the Steam install, not the
    /// mod, and the two are only sometimes the same place.
    /// </summary>
    public string MapDirectory { get; }

    /// <summary>Maps read from the directory <paramref name="layout"/> deduces.</summary>
    internal MapSource(ModLayout layout)
    {
        MapDirectory = DefaultMapDirectory(layout);
    }

    /// <summary>Maps read from <paramref name="mapDirectory"/>, named outright.</summary>
    public MapSource(string mapDirectory)
    {
        MapDirectory = mapDirectory;
    }

    /// <summary>
    /// The mod's maps directory, deduced from where this assembly was loaded
    /// from: it sits in &lt;mod&gt;/bin/managed, so the maps are two levels up.
    /// </summary>
    /// <summary>Names the maps directory outright, for a layout this cannot deduce.</summary>
    public const string DirectoryVariable = "SOURCESHARP_MAPS";

    // An explicit answer (DirectoryVariable) beats a deduced one, for the cases
    // the deduction cannot cover -- a mod laid out differently, or a harness
    // running the API with no mod around it at all. It is read where the
    // server is composed (DebugApiDirectories.FromEnvironment), which then
    // names the directory outright; this constructor only deduces.
    private static string DefaultMapDirectory(ModLayout layout)
    {
        string mod = layout.ModDirectory;

        // "maps" alone, resolved against the working directory, is the answer
        // that used to be given here when the layout could not be worked out.
        // In the game that directory is the Steam install and the list came back
        // empty, which reads as "this map has no entities" rather than "I looked
        // in the wrong place".
        return mod.Length == 0 ? "maps" : Path.Combine(mod, "maps");
    }

    /// <summary>Every map that could be opened, newest first -- which is usually the one being worked on.</summary>
    public IReadOnlyList<string> Available()
    {
        try
        {
            // Top level only. Overwritten maps are copied into a subdirectory
            // beside them, and offering those back as maps to open would fill
            // the list with every version of everything.
            return new DirectoryInfo(MapDirectory)
                .GetFiles("*.bsp", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                .ToArray();
        }
        catch
        {
            // No directory, no permission, no maps. An empty list is the honest
            // answer and the caller has a nothing-to-show state already.
            return [];
        }
    }

    public string PathFor(string mapName) => Path.Combine(MapDirectory, mapName + ".bsp");

    /// <summary>
    /// Checks a name that arrived from a request before it is joined onto a
    /// directory. Public because writing a map takes a destination name from the
    /// same untrusted place reading one does.
    /// </summary>
    public static void CheckName(string mapName, string parameter)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            throw new ArgumentException("a map name is required", parameter);

        if (mapName.Contains('/') || mapName.Contains('\\') || mapName.Contains(".."))
            throw new ArgumentException("a map name is a name, not a path", parameter);
    }

    /// <summary>Forgets a map, so the next read comes from disk. Called after writing one.</summary>
    public void Forget(string mapName)
    {
        lock (_lock)
        {
            _cache.Remove(mapName);
            _reports.Remove(mapName);
        }
    }

    /// <summary>
    /// Everything in a map file, from the cache when it can.
    ///
    /// Keyed on the write time like the parse above, so recompiling a map and
    /// asking again shows the new one.
    /// </summary>
    public BspReport ReadReport(string mapName)
    {
        CheckName(mapName, nameof(mapName));

        string path = PathFor(mapName);
        DateTime writtenAt = File.GetLastWriteTimeUtc(path);

        lock (_lock)
        {
            if (_reports.TryGetValue(mapName, out var entry) && entry.WrittenAt == writtenAt)
                return entry.Report;
        }

        // Outside the lock, like the parse: this one walks the pakfile as well,
        // and holding a lock across it would stall every other request.
        var report = BspReport.Read(path, mapName);

        lock (_lock)
        {
            _reports[mapName] = (writtenAt, report);
        }

        return report;
    }

    /// <summary>
    /// One lump of a map, decoded.
    ///
    /// Deliberately not cached. These are the big lumps -- a map's faces and
    /// brush sides run to tens of thousands of records -- and holding every one
    /// somebody opened would grow without bound across a session for the sake of
    /// a second read that nobody is waiting on.
    /// </summary>
    public BspLumpDetail ReadLump(string mapName, int lump)
    {
        CheckName(mapName, nameof(mapName));

        return BspLumpDetail.Read(PathFor(mapName), mapName, lump);
    }

    /// <summary>
    /// Reads a map, from the cache when it can.
    ///
    /// A cached parse without geometry does not satisfy a request that wants it,
    /// so the flag is part of the key rather than something to hope about.
    /// </summary>
    public BspFile.Contents Read(string mapName, bool withGeometry)
    {
        // A map name arrives from a query string, and joining it onto a
        // directory is the classic way to read a file somewhere else entirely.
        CheckName(mapName, nameof(mapName));

        string path = PathFor(mapName);
        DateTime writtenAt = File.GetLastWriteTimeUtc(path);

        lock (_lock)
        {
            if (_cache.TryGetValue(mapName, out var entry)
                && entry.WrittenAt == writtenAt
                && (entry.HasGeometry || !withGeometry))
            {
                return entry.Contents;
            }
        }

        // Parsed outside the lock. It takes long enough that holding one would
        // stall every other request on the listener thread, and the worst case
        // of two callers racing is that the same map is read twice.
        var contents = BspFile.Read(path, withGeometry);

        lock (_lock)
        {
            _cache[mapName] = new Entry(writtenAt, withGeometry, contents);
        }

        return contents;
    }
}
