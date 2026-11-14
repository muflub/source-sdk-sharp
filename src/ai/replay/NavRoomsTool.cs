using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats;
using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Bsp.Structs;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Replay;

/// <summary>An entity of the map as <see cref="NavRoomsTool"/> places it: a point (<see cref="Mins"/> null) or a brush's bounds, and its <c>TeamNum</c> (0 for none).</summary>
public sealed record RoomEntity(string ClassName, string? TargetName, string? Target, AiVector Origin, AiVector? Mins, AiVector? Maxs, int Team = 0);

/// <summary>One region of free space: its boxes, its leaves (a box's columns), its bounds, the entities in it by class, the regions its teleports send a player to, and its teleport destinations' names.</summary>
public sealed record RoomRow(int Region, int Boxes, int Leaves, AiVector Mins, AiVector Maxs, IReadOnlyDictionary<string, int> Entities, IReadOnlyList<int> TeleportsTo,
                             IReadOnlyList<string> Destinations);

/// <summary>
/// The regions of a hull navigation and what the map puts in each: <see cref="Rows"/> by region,
/// <see cref="Unplaced"/> the counted entities no box holds, and each teleport as (the region of
/// its trigger, the region of its destination; -1 where either is not placed or not found).
/// </summary>
public sealed record RoomsSurvey(IReadOnlyList<RoomRow> Rows, IReadOnlyDictionary<string, int> Unplaced, IReadOnlyList<(int From, int To)> Teleports);

/// <summary>
/// <c>ai-replay navrooms</c>: whether a map's navigation can be had a room at a time, offline
/// (plan_bots_teams_tf2_part_g G.15). <c>rooms</c> finds the regions of connected free space in
/// a <c>.navhull</c> (<see cref="NavHullRegions"/>) and places the map's spawns, teleports,
/// teleport destinations, resupply cabinets and capture areas in them, reading the entity lump
/// through map-tools' bsp reader (which unpacks a shipped map's LZMA lumps). <c>derive</c>
/// costs one region as <c>navsize</c> costs a whole navigation: the region's leaves read through
/// <see cref="NavRegionSource"/> and derived by the game's own derivation, unchanged; each step
/// in thread CPU and wall time, with the managed heap after it and the process's peak.
/// </summary>
public static class NavRoomsTool
{
    /// <summary>The classes a room is described by, in the order the survey prints them.</summary>
    public static readonly IReadOnlyList<string> Counted =
        ["info_player_teamspawn", "info_player_deathmatch", "info_teleport_destination", "trigger_teleport", "func_regenerate", "trigger_capture_area"];

    public static readonly IReadOnlyList<string> StepNames = ["open", "regions", "region", "derive", "sight", "cache"];

    private const int ModelBytes = 48;

    /// <summary>The map's entities, its brush entities with their model's bounds, from a whole bsp file (compressed lumps too).</summary>
    public static IReadOnlyList<RoomEntity> ReadEntities(byte[] bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        BspData data;
        try
        {
            using var stream = new MemoryStream(bsp, writable: false);
            data = BspFile.LoadAsync(stream).GetAwaiter().GetResult();
        }
        catch (InvalidBspException e)
        {
            throw new InvalidDataException($"the bsp does not read: {e.Message}", e);
        }

        ReadOnlySpan<byte> models = data[BspLump.Models].Data.Span;
        var all = new List<RoomEntity>();
        foreach (BspEntity e in EntityLump.Parse(data[BspLump.Entities]))
        {
            AiVector origin = Vec(e.Get("origin")) ?? default;
            AiVector? mins = null, maxs = null;
            if (e.Get("model") is { Length: > 1 } model && model[0] == '*'
                && int.TryParse(model.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                && (index + 1) * ModelBytes <= models.Length)
            {
                ReadOnlySpan<byte> m = models.Slice(index * ModelBytes, ModelBytes);
                mins = origin + new AiVector(F(m, 0), F(m, 4), F(m, 8));
                maxs = origin + new AiVector(F(m, 12), F(m, 16), F(m, 20));
            }

            int team = int.TryParse(e.Get("TeamNum"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ? t : 0;
            all.Add(new RoomEntity(e.ClassName, e.Get("targetname"), e.Get("target"), origin, mins, maxs, team));
        }

        return all;
    }

    private static float F(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b[at..]);

    private static AiVector? Vec(string? text)
    {
        if (text is null)
            return null;
        string[] p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 3
               && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
               && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
               && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            ? new AiVector(x, y, z) : null;
    }

    /// <summary>
    /// The region an entity is in, or -1. A point is asked at its origin and a little above it (a
    /// spawn or a destination stands on the floor, which a box starts at); a brush at points
    /// spread through its bounds, and is in the region most of them are.
    /// </summary>
    public static int RegionOf(NavHullReader reader, NavHullRegions regions, RoomEntity e)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(e);
        if (e.Mins is not { } lo || e.Maxs is not { } hi)
        {
            foreach (float up in (ReadOnlySpan<float>)[1f, 8f, 16f, 32f, 48f])
            {
                int box = reader.FindBox(new Vec3(e.Origin.X, e.Origin.Y, e.Origin.Z + up));
                if (box >= 0)
                    return regions.RegionOf(box);
            }

            return -1;
        }

        var votes = new Dictionary<int, int>();
        const int Steps = 5;
        for (int i = 0; i < Steps; i++)
        {
            for (int j = 0; j < Steps; j++)
            {
                for (int k = 0; k < Steps; k++)
                {
                    var at = new Vec3(lo.X + (hi.X - lo.X) * (i + 0.5f) / Steps, lo.Y + (hi.Y - lo.Y) * (j + 0.5f) / Steps, lo.Z + (hi.Z - lo.Z) * (k + 0.5f) / Steps);
                    int box = reader.FindBox(at);
                    if (box >= 0)
                        votes[regions.RegionOf(box)] = votes.GetValueOrDefault(regions.RegionOf(box)) + 1;
                }
            }
        }

        return votes.Count == 0 ? -1 : votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;
    }

    /// <summary>The regions of <paramref name="reader"/> with the counted entities placed in them, and each teleport's two regions.</summary>
    public static RoomsSurvey Survey(NavHullReader reader, NavHullRegions regions, IReadOnlyList<RoomEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(entities);
        var counts = new Dictionary<string, int>[regions.Count];
        var to = new SortedSet<int>[regions.Count];
        var names = new List<string>[regions.Count];
        for (int r = 0; r < regions.Count; r++)
        {
            counts[r] = new Dictionary<string, int>();
            to[r] = new SortedSet<int>();
            names[r] = new List<string>();
        }

        var unplaced = new Dictionary<string, int>();
        var regionOf = new Dictionary<RoomEntity, int>(ReferenceEqualityComparer.Instance);
        foreach (RoomEntity e in entities)
        {
            if (!Counted.Contains(e.ClassName))
                continue;
            int r = RegionOf(reader, regions, e);
            regionOf[e] = r;
            if (r < 0)
                unplaced[e.ClassName] = unplaced.GetValueOrDefault(e.ClassName) + 1;
            else
                counts[r][e.ClassName] = counts[r].GetValueOrDefault(e.ClassName) + 1;
            if (r >= 0 && e.ClassName == "info_teleport_destination")
                names[r].Add(e.TargetName ?? "");
        }

        // A trigger_teleport's target is any entity's targetname: a destination, or a landmark of another class.
        var named = new Dictionary<string, RoomEntity>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (e.TargetName is { } name)
                named.TryAdd(name, e);
        }

        var teleports = new List<(int, int)>();
        foreach (RoomEntity e in entities)
        {
            if (e.ClassName != "trigger_teleport")
                continue;
            int from = regionOf[e];
            int dest = e.Target is { } t && named.TryGetValue(t, out RoomEntity? d) ? regionOf.TryGetValue(d, out int dr) ? dr : RegionOf(reader, regions, d) : -1;
            teleports.Add((from, dest));
            if (from >= 0 && dest >= 0 && dest != from)
                to[from].Add(dest);
        }

        var rows = new RoomRow[regions.Count];
        for (int r = 0; r < regions.Count; r++)
        {
            int leaves = 0;
            var lo = new AiVector(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = new AiVector(float.MinValue, float.MinValue, float.MinValue);
            foreach (int box in regions.Boxes(r))
            {
                NavHullBox b = reader.Box(box);
                leaves += (b.X1 - b.X0) * (b.Y1 - b.Y0);
                (Vec3 mins, Vec3 maxs) = reader.BoxBounds(box);
                lo = new AiVector(MathF.Min(lo.X, (float)mins.X), MathF.Min(lo.Y, (float)mins.Y), MathF.Min(lo.Z, (float)mins.Z));
                hi = new AiVector(MathF.Max(hi.X, (float)maxs.X), MathF.Max(hi.Y, (float)maxs.Y), MathF.Max(hi.Z, (float)maxs.Z));
            }

            rows[r] = new RoomRow(r, regions.Boxes(r).Length, leaves, lo, hi, counts[r], [.. to[r]], names[r]);
        }

        return new RoomsSurvey(rows, unplaced, teleports);
    }

    /// <summary>The survey as text: the size distribution, then a row per region that holds a counted entity or is among the largest <paramref name="largest"/>.</summary>
    public static IEnumerable<string> Describe(RoomsSurvey s, int largest)
    {
        ArgumentNullException.ThrowIfNull(s);
        int[] sizes = [.. s.Rows.Select(r => r.Leaves).OrderDescending()];
        long total = sizes.Sum(x => (long)x);
        int median = sizes.Length == 0 ? 0 : sizes[sizes.Length / 2];
        var with = s.Rows.Where(r => r.Entities.Count > 0).ToList();
        int[] withSizes = [.. with.Select(r => r.Leaves).OrderDescending()];
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  {s.Rows.Count:N0} regions of free space, {total:N0} leaves; the largest {(sizes.Length > 0 ? sizes[0] : 0):N0}, the median {median:N0}; "
            + $"{with.Count:N0} hold a counted entity ({withSizes.Sum(x => (long)x):N0} leaves; the largest {(withSizes.Length > 0 ? withSizes[0] : 0):N0}, the median {(withSizes.Length > 0 ? withSizes[withSizes.Length / 2] : 0):N0})");
        int within = s.Teleports.Count(t => t.From >= 0 && t.From == t.To), across = s.Teleports.Count(t => t.From >= 0 && t.To >= 0 && t.From != t.To);
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  {s.Teleports.Count:N0} teleports: {within:N0} land in their own region, {across:N0} in another, {s.Teleports.Count - within - across:N0} not placed or with no destination");
        if (s.Unplaced.Count > 0)
            yield return "  not in any box: " + string.Join(", ", s.Unplaced.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key} {p.Value}")));
        var big = s.Rows.OrderByDescending(r => r.Leaves).Take(largest).Select(r => r.Region).ToHashSet();
        yield return "  region    boxes     leaves  bounds                                             entities  -> teleports to";
        foreach (RoomRow r in s.Rows.Where(r => r.Entities.Count > 0 || big.Contains(r.Region)))
        {
            string ents = string.Join(" ", Counted.Where(r.Entities.ContainsKey).Select(c => string.Create(CultureInfo.InvariantCulture, $"{Short(c)}{r.Entities[c]}")));
            string to = r.TeleportsTo.Count > 0 ? "  -> " + string.Join(",", r.TeleportsTo) : "";
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {r.Region,6} {r.Boxes,8:N0} {r.Leaves,10:N0}  ({r.Mins.X:F0} {r.Mins.Y:F0} {r.Mins.Z:F0})..({r.Maxs.X:F0} {r.Maxs.Y:F0} {r.Maxs.Z:F0})  {ents}{to}");
            if (r.Destinations.Count > 0)
                yield return "           destinations: " + string.Join(" ", r.Destinations);
        }
    }

    private static string Short(string className) => className switch
    {
        "info_player_teamspawn" or "info_player_deathmatch" => "spawn:",
        "info_teleport_destination" => "dest:",
        "trigger_teleport" => "tele:",
        "func_regenerate" => "regen:",
        "trigger_capture_area" => "cap:",
        _ => className + ":",
    };

    /// <summary>
    /// What region <paramref name="region"/> of a hull navigation costs to derive alone: the file
    /// opened, its regions found, the region's leaves read into a <see cref="NavRegionSource"/>,
    /// the graph derived by the game's numbers, the sight table built, the graph cache encoded and
    /// decoded under the hull's name with the region's own; <paramref name="stop"/> names the last step.
    /// </summary>
    public static NavSizeReport Measure(string game, string map, byte[] nav, byte[] bsp, int region, string stop, Func<double> cpuMicros, bool sight = true)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentNullException.ThrowIfNull(cpuMicros);
        int last = StepNames.ToList().IndexOf(stop);
        if (last < 0)
            throw new ArgumentException($"no step {stop}: {string.Join(", ", StepNames)}", nameof(stop));
        var steps = new List<NavSizeStep>();
        T Step<T>(string name, Func<T> work, Func<T, string> what)
        {
            double cpu = cpuMicros();
            long wall = Stopwatch.GetTimestamp();
            T result = work();
            double cpuMs = (cpuMicros() - cpu) / 1000.0, wallMs = Stopwatch.GetElapsedTime(wall).TotalMilliseconds;
            double managed = GC.GetTotalMemory(forceFullCollection: true) / 1048576.0;
            using var process = Process.GetCurrentProcess();
            steps.Add(new NavSizeStep(name, cpuMs, wallMs, managed, process.PeakWorkingSet64 / 1048576.0, what(result)));
            return result;
        }

        (NavHullReader reader, NavHullNavSource whole) = Step<(NavHullReader, NavHullNavSource)>("open", () =>
        {
            NavHullReader r = NavHullReader.Open(nav);
            return (r, NavHullNavSource.Over(r));
        }, r => string.Create(CultureInfo.InvariantCulture, $"{nav.Length:N0} bytes, {r.Item1.BoxCount:N0} boxes, {r.Item2.LeafCount:N0} leaves, {r.Item1.ObstacleCount:N0} obstacles"));
        NavNumbers numbers = game == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(whole.JumpHeight);
        if (NavHullNavSource.MissingWidth(reader, numbers, map + NavHullFormat.Extension) is { } why)
            throw new InvalidDataException(why);
        if (last < 1)
            return new NavSizeReport(steps, null, null, 0);

        NavHullRegions regions = Step("regions", () => NavHullRegions.Of(reader), r => string.Create(CultureInfo.InvariantCulture, $"{r.Count:N0} regions of free space"));
        if (region < 0 || region >= regions.Count)
            throw new ArgumentException($"no region {region}: the file has {regions.Count}", nameof(region));
        if (last < 2)
            return new NavSizeReport(steps, null, null, 0);

        NavRegionSource source = Step("region", () => NavRegionSource.Over(whole, regions.Leaves(whole, region)),
                                      s => string.Create(CultureInfo.InvariantCulture, $"region {region}: {regions.Boxes(region).Length:N0} boxes, {s.LeafCount:N0} leaves"));
        if (last < 3)
            return new NavSizeReport(steps, null, null, 0);

        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav derived = Step("derive", () => NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs), d =>
        {
            var parts = new Dictionary<int, int>();
            for (int c = 0; c < d.Graph.CellCount; c++)
                parts[d.Graph.ComponentOf(c)] = parts.GetValueOrDefault(d.Graph.ComponentOf(c)) + 1;
            return string.Create(CultureInfo.InvariantCulture,
                $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges, {parts.Count:N0} joined-up parts, the largest {(parts.Count == 0 ? 0 : parts.Values.Max()):N0} cells; ")
                + (entities.Unread is { } unread ? $"hurt volumes not read ({unread})" : $"{entities.Hurt.Count} hurt volumes");
        });
        if (last < 4)
            return new NavSizeReport(steps, derived, null, 0);

        NavSight? table = !sight ? null : Step("sight", () => NavSight.Build(derived.Graph, new NavHullVoxelSpace(reader)),
                                               s => string.Create(CultureInfo.InvariantCulture, $"{s.NodeCount:N0} nodes, {s.Pairs:N0} pairs"));
        if (last < 5)
            return new NavSizeReport(steps, derived, table, 0);

        var cache = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default);
        string key = string.Create(CultureInfo.InvariantCulture, $"{NavLoader.HullKey(NavGraphCache.NumbersKey(numbers, whole.JumpHeight))}-r{region}");
        string bspSha = BuiltNavStore.Sha256(bsp), navSha = BuiltNavStore.Sha256(nav);
        byte[] encoded = Step("cache", () =>
        {
            byte[] bytes = cache.Encode(map, bspSha, navSha, derived, key);
            DerivedNav back = cache.Decode(bytes, map, bspSha, navSha, key);
            if (back.Graph.Hash != derived.Graph.Hash)
                throw new InvalidDataException("the cached graph is not the derived one");
            return bytes;
        }, b => string.Create(CultureInfo.InvariantCulture, $"{b.Length:N0} bytes encoded, and decoded to the same graph"));
        return new NavSizeReport(steps, derived, table, encoded.Length);
    }

    /// <summary>
    /// <c>navrooms &lt;hl2mp|tf2&gt; &lt;map.navhull&gt; &lt;map.bsp&gt; rooms [largest=20]</c>, or
    /// <c>... derive &lt;region&gt; [stop=cache] [no-sight]</c>.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        bool rooms = args.Length >= 4 && args[3] == "rooms", derive = args.Length >= 5 && args[3] == "derive";
        if (!rooms && !derive)
        {
            error.WriteLine($"usage: ai-replay navrooms <{CostTool.Hl2mp}|{CostTool.TF2}> <map.navhull> <map.bsp> rooms [largest=20]");
            error.WriteLine($"       ai-replay navrooms <{CostTool.Hl2mp}|{CostTool.TF2}> <map.navhull> <map.bsp> derive <region> [stop: {string.Join("|", StepNames)}] [no-sight]");
            return 2;
        }

        if (ThreadCpuClock.Micros() is not { } clock)
        {
            error.WriteLine("ai-replay: no thread CPU clock on this system");
            return 1;
        }

        try
        {
            string map = Path.GetFileNameWithoutExtension(args[1]);
            byte[] nav = File.ReadAllBytes(args[1]), bsp = File.ReadAllBytes(args[2]);
            output.WriteLine($"ai-replay navrooms {args[0]} {Path.GetFileName(args[1])}:");
            if (rooms)
            {
                int largest = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 20;
                NavHullReader reader = NavHullReader.Open(nav);
                NavHullRegions regions = NavHullRegions.Of(reader);
                foreach (string line in Describe(Survey(reader, regions, ReadEntities(bsp)), largest))
                    output.WriteLine(line);
                return 0;
            }

            int region = int.Parse(args[4], CultureInfo.InvariantCulture);
            NavSizeReport report = Measure(args[0], map, nav, bsp, region, args.Length > 5 ? args[5] : "cache", clock, sight: !(args.Length > 6 && args[6] == "no-sight"));
            foreach (NavSizeStep s in report.Steps)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {s.Name,-7} {s.CpuMs / 1000.0,8:F2} s cpu {s.WallMs / 1000.0,8:F2} s wall  {s.ManagedMb,8:F0} MB managed  {s.PeakMb,8:F0} MB peak  {s.What}"));
            }

            if (report.CacheBytes > 0)
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  cache file {report.CacheBytes:N0} bytes"));
            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or FormatException or OutOfMemoryException)
        {
            error.WriteLine($"ai-replay navrooms: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
