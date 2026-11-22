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

/// <summary>A room's teleports as <see cref="NavCellsTool"/> sorts them: a reset lands in the room it is in, an exit elsewhere.</summary>
public sealed record RoomTeleports(IReadOnlyList<HurtVolume> Resets, IReadOnlyList<HurtVolume> Exits, IReadOnlyList<AiVector> WaysIn, IReadOnlyList<(AiVector Mins, AiVector Maxs)> Captures);

/// <summary>
/// <c>ai-replay navcells</c>: where a room's cells are, and what makes its graph smaller, offline
/// (plan_bots_teams_tf2_part_g, the user's Q-R3 of 2026-11-13). The room is a hull navigation
/// of its own (<c>navsplit</c> writes one per sealed space), so no step opens the whole map.
/// <c>where</c> counts the cells inside teleport volumes, the cells no way in reaches (by the
/// graph's edges, then with a blast jump's bound), the flat cells, and the edges per cell by kind.
/// <c>variant</c> measures one way of making the graph, step by step as <c>navsize</c> does, in a
/// process of its own so the peak is the variant's: the tokens, joined by <c>+</c>, are
/// <c>tele</c> (the cells inside a teleport back are left out, as a hurt volume's are),
/// <c>reach</c> / <c>reach2</c> (only what the ways in reach, with one rocket's bound or twice it),
/// <c>k2</c> / <c>k4</c> (flat floor merged in blocks), <c>nosight</c>; <c>base</c> is none.
/// </summary>
public static class NavCellsTool
{
    /// <summary>One rocket's reach, the simulator's envelope (plan_bots_teams_tf2_part_g: the pass's 522 and 936 are 0.9 of it).</summary>
    public static readonly BlastBound OneRocket = new(580f, 1040f);

    /// <summary>Twice it: a bound for a course of pogos and syncs, which climb past one rocket's rise.</summary>
    public static readonly BlastBound TwiceOneRocket = new(1160f, 2080f);

    /// <summary>The room's teleports, ways in and capture areas, from the map's entities, placed by the room's own file.</summary>
    /// <param name="brushes">Each entity's brushes as boxes, in <paramref name="entities"/>' order (<see cref="BrushBoxes"/>): a teleport's volume is its brushes, not its bounds. Null for its bounds.</param>
    public static RoomTeleports Teleports(NavHullReader room, IReadOnlyList<RoomEntity> entities, IReadOnlyList<IReadOnlyList<HurtVolume>>? brushes = null)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(entities);
        if (brushes is not null && brushes.Count != entities.Count)
            throw new ArgumentException($"{brushes.Count} brush lists for {entities.Count} entities", nameof(brushes));
        NavHullRegions regions = NavHullRegions.Of(room);
        var named = new Dictionary<string, RoomEntity>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (e.TargetName is { } n)
                named.TryAdd(n, e);
        }

        var resets = new List<HurtVolume>();
        var exits = new List<HurtVolume>();
        var ways = new List<AiVector>();
        var caps = new List<(AiVector, AiVector)>();
        for (int i = 0; i < entities.Count; i++)
        {
            RoomEntity e = entities[i];
            bool here = NavRoomsTool.RegionOf(room, regions, e) >= 0;
            if (!here)
                continue;
            switch (e.ClassName)
            {
                case "trigger_teleport" when e.Mins is { } lo && e.Maxs is { } hi:
                    bool reset = e.Target is { } t && named.TryGetValue(t, out RoomEntity? d) && NavRoomsTool.RegionOf(room, regions, d) >= 0;
                    if (brushes is null)
                        (reset ? resets : exits).Add(new HurtVolume(lo, hi));
                    else
                        (reset ? resets : exits).AddRange(brushes[i]);
                    break;
                case "info_teleport_destination" or "info_player_teamspawn" or "info_player_deathmatch":
                    ways.Add(e.Origin);
                    break;
                case "trigger_capture_area" when e.Mins is { } lo && e.Maxs is { } hi:
                    caps.Add((lo, hi));
                    break;
            }
        }

        return new RoomTeleports(resets, exits, ways, caps);
    }

    /// <summary>Each entity's brushes as boxes (<see cref="BrushModelBoxes"/>), in the order <see cref="NavRoomsTool.ReadEntities"/> gives the entities; none for a point entity.</summary>
    public static IReadOnlyList<IReadOnlyList<HurtVolume>> BrushBoxes(byte[] bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        BspData data;
        using (var stream = new MemoryStream(bsp, writable: false))
            data = BspFile.LoadAsync(stream).GetAwaiter().GetResult();
        var lumps = new BspTreeLumps(data[BspLump.Planes].Data, data[BspLump.Nodes].Data, data[BspLump.Leafs].Data, data[BspLump.Leafs].Version,
                                     data[BspLump.Models].Data, data[BspLump.LeafBrushes].Data, data[BspLump.Brushes].Data, data[BspLump.BrushSides].Data);
        var all = new List<IReadOnlyList<HurtVolume>>();
        foreach (BspEntity e in EntityLump.Parse(data[BspLump.Entities]))
        {
            if (e.Get("model") is { Length: > 1 } model && model[0] == '*'
                && int.TryParse(model.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                string[] o = (e.Get("origin") ?? "0 0 0").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                AiVector origin = o.Length == 3 ? new AiVector(float.Parse(o[0], CultureInfo.InvariantCulture), float.Parse(o[1], CultureInfo.InvariantCulture), float.Parse(o[2], CultureInfo.InvariantCulture)) : default;
                all.Add(BrushModelBoxes.Of(lumps, index, origin));
            }
            else
            {
                all.Add([]);
            }
        }

        return all;
    }

    /// <summary>Whether a cell stands inside a volume, by the derivation's own test for a hurt volume.</summary>
    public static bool Inside(in NavCell c, IReadOnlyList<HurtVolume> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        foreach (HurtVolume h in volumes)
        {
            if (c.MaxX > h.Min.X && c.MinX < h.Max.X && c.MaxY > h.Min.Y && c.MinY < h.Max.Y && c.FloorZ >= h.Min.Z - 1f && c.FloorZ < h.Max.Z)
                return true;
        }

        return false;
    }

    /// <summary>The cells a way in stands on (the nearest a standing body touches).</summary>
    public static List<int> Seeds(NavGraph graph, IEnumerable<AiVector> ways)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ways);
        var seeds = new List<int>();
        foreach (AiVector w in ways)
        {
            CellId c = NavPoints.CellNear(graph, w, ClearanceClass.Stand);
            if (c.IsValid)
                seeds.Add(c.Index);
        }

        return seeds;
    }

    /// <summary>
    /// <c>navcells &lt;hl2mp|tf2&gt; &lt;room.navhull&gt; &lt;map.bsp&gt; where</c>, or
    /// <c>... variant &lt;tokens&gt;</c>.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 4 || (args[3] != "where" && args[3] != "rects" && args[3] != "teleports" && args[3] != "kept" && !(args[3] == "variant" && args.Length >= 5)))
        {
            error.WriteLine($"usage: ai-replay navcells <{CostTool.Hl2mp}|{CostTool.TF2}> <room.navhull> <map.bsp> where [noreach]");
            error.WriteLine($"       ai-replay navcells <{CostTool.Hl2mp}|{CostTool.TF2}> <room.navhull> <map.bsp> rects");
            error.WriteLine($"       ai-replay navcells <{CostTool.Hl2mp}|{CostTool.TF2}> <room.navhull> <map.bsp> teleports");
            error.WriteLine($"       ai-replay navcells <{CostTool.Hl2mp}|{CostTool.TF2}> <room.navhull> <map.bsp> kept");
            error.WriteLine($"       ai-replay navcells <{CostTool.Hl2mp}|{CostTool.TF2}> <room.navhull> <map.bsp> variant <base|tele+reach+k4+nosight..>");
            return 2;
        }

        if (ThreadCpuClock.Micros() is not { } clock)
        {
            error.WriteLine("ai-replay: no thread CPU clock on this system");
            return 1;
        }

        try
        {
            byte[] nav = File.ReadAllBytes(args[1]), bsp = File.ReadAllBytes(args[2]);
            long opening = Stopwatch.GetTimestamp();
            NavHullReader room = NavHullReader.Open(nav);
            var source = NavHullNavSource.Over(room);
            double openS = Stopwatch.GetElapsedTime(opening).TotalSeconds;
            NavNumbers numbers = args[0] == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(source.JumpHeight);
            IReadOnlyList<RoomEntity> entities = NavRoomsTool.ReadEntities(bsp);
            MapEntities map = MapEntities.Read(bsp);
            RoomTeleports t = Teleports(room, entities, BrushBoxes(bsp));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ai-replay navcells {args[0]} {Path.GetFileName(args[1])}: {room.BoxCount:N0} boxes, {source.LeafCount:N0} leaves; {t.Resets.Count} teleports back, {t.Exits.Count} out, {t.WaysIn.Count} ways in, {t.Captures.Count} capture areas"));
            if (args[3] == "rects")
            {
                DerivedNav d = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, map.Hurt, map.LadderClimbs);
                bool[] flat = NavGraphReduce.Flat(d, source.VoxelSize);
                int all = Rectangles(d.Graph, source.VoxelSize, null), flats = Rectangles(d.Graph, source.VoxelSize, flat);
                int flatCells = flat.Count(f => f);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {d.Graph.CellCount:N0} cells; as rectangles of one floor, bodies, flags and cost: {all:N0}; flat cells merged into rectangles and the rest kept: {flats + d.Graph.CellCount - flatCells:N0} ({flats:N0} rectangles of {flatCells:N0} flat cells)"));
                return 0;
            }

            if (args[3] == "kept")
            {
                // The game's graph of the room (the table's fail floors out, merged as it derives): the cells each
                // teleport back keeps (a kept one some, a fail floor none).
                string tablePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, NavSplitTool.RoomFiles(Path.GetFileNameWithoutExtension(args[1]).Split(".r")[0]).Table);
                NavRoom mine = NavRoomTable.FromJson(File.ReadAllText(tablePath)).Rooms.FirstOrDefault(r => r.File == Path.GetFileName(args[1]))
                               ?? throw new InvalidDataException($"{tablePath} has no room {Path.GetFileName(args[1])}");
                DerivedNav game = NavGraphReduce.DeriveCoarse(source, numbers, NavEdgeCosts.Default, [.. map.Hurt, .. mine.FailVolumes], map.LadderClimbs,
                                                              threads: Math.Max(1, Environment.ProcessorCount / 2));
                foreach ((string kind, NavRoomTeleport tp) in mine.Kept.Select(k => ("kept", k)).Concat(mine.Fails.Select(f => ("fail floor", f))))
                {
                    int inside = 0;
                    for (int c = 0; c < game.Graph.CellCount; c++)
                        inside += Inside(game.Graph.Cell(c), tp.Brushes) ? 1 : 0;
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {kind} '{tp.Name ?? tp.Target}': {inside} cells inside its {tp.Brushes.Count} brushes"));
                }

                return 0;
            }

            if (args[3] == "teleports")
            {
                foreach (string line in TeleportLines(source, room, numbers, map, entities, BrushBoxes(bsp)))
                    output.WriteLine(line);
                return 0;
            }

            if (args[3] == "where")
            {
                foreach (string line in Where(source, room, numbers, map, t, reach: !(args.Length > 4 && args[4] == "noreach")))
                    output.WriteLine(line);
                return 0;
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  open     {openS,7:F2} s wall ({nav.Length:N0} bytes)"));
            if (args[4].Split('+').Contains("table"))
            {
                // The game's rule: the fail floors the room table names (navsplit rooms), not every teleport back.
                string tablePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, NavSplitTool.RoomFiles(Path.GetFileNameWithoutExtension(args[1]).Split(".r")[0]).Table);
                NavRoom? mine = NavRoomTable.FromJson(File.ReadAllText(tablePath)).Rooms.FirstOrDefault(r => r.File == Path.GetFileName(args[1]))
                                ?? throw new InvalidDataException($"{tablePath} has no room {Path.GetFileName(args[1])}");
                t = t with { Resets = mine.FailVolumes };
                output.WriteLine($"  table: {mine.Fails.Count} fail floors ({mine.FailVolumes.Count} brushes), {mine.Kept.Count} kept teleports back");
            }

            foreach (string line in Variant(args[4], Path.GetFileNameWithoutExtension(args[1]), nav, bsp, source, room, numbers, map, t, clock))
                output.WriteLine(line);
            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or FormatException or OutOfMemoryException)
        {
            error.WriteLine($"ai-replay navcells: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> Where(NavHullNavSource source, NavHullReader room, NavNumbers numbers, MapEntities map, RoomTeleports t, bool reach)
    {
        DerivedNav d = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, map.Hurt, map.LadderClimbs);
        NavGraph g = d.Graph;
        int cells = g.CellCount;
        int inReset = 0, inExit = 0;
        var stops = new bool[cells];
        for (int c = 0; c < cells; c++)
        {
            NavCell cell = g.Cell(c);
            if (Inside(cell, t.Resets))
            {
                inReset++;
                stops[c] = true;
            }
            else if (Inside(cell, t.Exits))
            {
                inExit++;
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  {cells:N0} cells, {g.EdgeCount:N0} edges, {Islands(g):N0} joined-up parts");
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  inside a teleport back: {inReset:N0} cells ({Share(inReset, cells)}); inside a teleport out: {inExit:N0} ({Share(inExit, cells)})");
        List<int> seeds = Seeds(g, t.WaysIn);
        var space = new NavHullVoxelSpace(room);
        foreach (string line in reach ? Reached(g, space, seeds, stops, t) : [])
            yield return line;
        foreach (string line in Rest(d, g, source, room, cells))
            yield return line;
    }

    /// <summary>
    /// Each teleport back of the room (one landing in the room): its brushes, the cells inside them
    /// (the graph made with no rule), the floors they are on, how a cell outside every teleport back
    /// steps into them, by kind, and its destination's height.
    /// </summary>
    private static IEnumerable<string> TeleportLines(NavHullNavSource source, NavHullReader room, NavNumbers numbers, MapEntities map,
                                                     IReadOnlyList<RoomEntity> entities, IReadOnlyList<IReadOnlyList<HurtVolume>> brushes)
    {
        RoomTeleports t = Teleports(room, entities, brushes);
        DerivedNav d = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, map.Hurt, map.LadderClimbs);
        NavGraph g = d.Graph;
        var inAny = new bool[g.CellCount];
        for (int c = 0; c < g.CellCount; c++)
            inAny[c] = Inside(g.Cell(c), t.Resets);
        NavHullRegions regions = NavHullRegions.Of(room);
        var named = new Dictionary<string, RoomEntity>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (e.TargetName is { } n)
                named.TryAdd(n, e);
        }

        // What a body reaches from the ways in without a fall, stopping at a teleport back.
        var walked = new bool[g.CellCount];
        var queue = new Queue<int>();
        foreach (int seed in Seeds(g, t.WaysIn))
        {
            if (!inAny[seed] && !walked[seed])
            {
                walked[seed] = true;
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            foreach (NavEdge edge in g.EdgesFrom(c))
            {
                if (edge.Type != NavEdgeType.Drop && !inAny[edge.To] && !walked[edge.To])
                {
                    walked[edge.To] = true;
                    queue.Enqueue(edge.To);
                }
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  reached from the ways in without a fall: {walked.Count(w => w):N0} of {g.CellCount:N0} cells");
        for (int i = 0; i < entities.Count; i++)
        {
            RoomEntity e = entities[i];
            if (e.ClassName != "trigger_teleport" || NavRoomsTool.RegionOf(room, regions, e) < 0 || e.Target is not { } target
                || !named.TryGetValue(target, out RoomEntity? dest) || NavRoomsTool.RegionOf(room, regions, dest) < 0)
                continue;
            var cells = new List<int>();
            for (int c = 0; c < g.CellCount; c++)
            {
                if (Inside(g.Cell(c), brushes[i]))
                    cells.Add(c);
            }

            var kinds = new SortedDictionary<NavEdgeType, int>();
            int fromWalked = 0;
            foreach (int c in cells)
            {
                foreach (NavEdge edge in g.EdgesTo(c))
                {
                    if (!inAny[edge.From])
                        kinds[edge.Type] = kinds.GetValueOrDefault(edge.Type) + 1;
                    if (walked[edge.From] && edge.Type != NavEdgeType.Drop)
                        fromWalked++;
                }
            }

            float lo = cells.Count == 0 ? float.NaN : cells.Min(c => g.Cell(c).FloorZ), hi = cells.Count == 0 ? float.NaN : cells.Max(c => g.Cell(c).FloorZ);
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  teleport {i} '{e.TargetName}' -> '{target}' at ({dest.Origin.X:F0} {dest.Origin.Y:F0} {dest.Origin.Z:F0}): {brushes[i].Count} brushes ({e.Mins!.Value.Z:F0}..{e.Maxs!.Value.Z:F0}), {cells.Count} cells on floors {lo:F0}..{hi:F0}; "
                + $"into them from outside every teleport back: {(kinds.Count == 0 ? "none" : string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")))}; "
                + $"stepped into from the ways in without a fall: {fromWalked}; box ({e.Mins.Value.X:F0} {e.Mins.Value.Y:F0})-({e.Maxs.Value.X:F0} {e.Maxs.Value.Y:F0})");
        }
    }

    private static IEnumerable<string> Reached(NavGraph g, IVoxelSpace space, List<int> seeds, bool[] stops, RoomTeleports t)
    {
        int cells = g.CellCount;
        bool[] walk = NavReach.From(g, seeds, stops: stops);
        var clock = Stopwatch.StartNew();
        bool[] one = NavReach.From(g, seeds, OneRocket, space, stops);
        double oneS = clock.Elapsed.TotalSeconds;
        clock.Restart();
        bool[] two = NavReach.From(g, seeds, TwiceOneRocket, space, stops);
        double twoS = clock.Elapsed.TotalSeconds;
        int Count(bool[] m) => m.Count(x => x);
        int notStops(bool[] m)
        {
            int n = 0;
            for (int c = 0; c < cells; c++)
                n += !m[c] && !stops[c] ? 1 : 0;
            return n;
        }

        yield return string.Create(CultureInfo.InvariantCulture,
            $"  ways in: {t.WaysIn.Count} ({seeds.Count} on a cell); reached by the graph's edges {Count(walk):N0}; with one rocket's bound (rise {OneRocket.Rise}, range {OneRocket.Range}) {Count(one):N0} ({oneS:F1} s); with twice it {Count(two):N0} ({twoS:F1} s)");
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  reached by none, outside the teleports back: by the edges {notStops(walk):N0} ({Share(notStops(walk), cells)}), one rocket {notStops(one):N0} ({Share(notStops(one), cells)}), twice it {notStops(two):N0} ({Share(notStops(two), cells)})");
        foreach ((AiVector lo, AiVector hi) in t.Captures)
            yield return CaptureLine(g, space, lo, hi, walk, one, two, stops);
    }

    private static IEnumerable<string> Rest(DerivedNav d, NavGraph g, NavHullNavSource source, NavHullReader room, int cells)
    {
        bool[] flat = NavGraphReduce.Flat(d, source.VoxelSize);
        int flatCells = flat.Count(x => x);
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  flat (all eight columns around on the same floor, no obstacle, no ladder): {flatCells:N0} ({Share(flatCells, cells)}); edge cells {cells - flatCells:N0}");
        var byType = new SortedDictionary<NavEdgeType, long>();
        for (int c = 0; c < cells; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
                byType[e.Type] = byType.GetValueOrDefault(e.Type) + 1;
        }

        yield return "  edges per cell: " + string.Join(", ", byType.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key} {p.Value / (double)Math.Max(1, cells):F2}")))
                     + string.Create(CultureInfo.InvariantCulture, $"; all {g.EdgeCount / (double)Math.Max(1, cells):F2}");

        // How the leaves the derivation reads are spread: per box, its columns and the jump links its leaves each scan.
        long scans = 0, columnBoxes = 0, columnAsks = 0;
        int maxColumn = 0;
        for (int box = 0; box < room.BoxCount; box++)
        {
            NavHullBox b = room.Box(box);
            int columns = (b.X1 - b.X0) * (b.Y1 - b.Y0);
            scans += (long)columns * room.Jumps(box).Length;
            for (int y = b.Y0; y < b.Y1; y++)
            {
                for (int x = b.X0; x < b.X1; x++)
                {
                    int n = room.ColumnBoxes(room.BoxCell(box), x, y).Length;
                    columnBoxes += n;
                    columnAsks++;
                    maxColumn = Math.Max(maxColumn, n);
                }
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture,
            $"  the file: {room.BoxCount:N0} boxes, {source.LeafCount:N0} leaves ({source.LeafCount / (double)Math.Max(1, cells):F2} a cell), {room.JumpCount:N0} jump links; a leaf's column holds {columnBoxes / (double)Math.Max(1, columnAsks):F2} boxes on average, at most {maxColumn}; the leaves' scans of their box's links {scans:N0} ({scans / (double)Math.Max(1, cells):F1} a cell)");
    }

    /// <summary>
    /// How many rectangles a greedy merge makes of the cells (only those <paramref name="only"/> marks, when given):
    /// row by row, a run along x of one floor, bodies, flags and cost, grown along y while the whole run below matches.
    /// An estimate of a true mesh's count; nothing is built from it.
    /// </summary>
    public static int Rectangles(NavGraph g, float voxel, bool[]? only)
    {
        ArgumentNullException.ThrowIfNull(g);
        float x0 = float.MaxValue, y0 = float.MaxValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            x0 = MathF.Min(x0, g.Cell(c).MinX);
            y0 = MathF.Min(y0, g.Cell(c).MinY);
        }

        var at = new Dictionary<(int X, int Y, float Z, ClearanceClasses K, NavCellFlags F, float C), int>();
        var keys = new List<(int X, int Y, float Z, ClearanceClasses K, NavCellFlags F, float C)>();
        for (int c = 0; c < g.CellCount; c++)
        {
            if (only is not null && !only[c])
                continue;
            NavCell n = g.Cell(c);
            var key = ((int)MathF.Round((n.MinX - x0) / voxel), (int)MathF.Round((n.MinY - y0) / voxel), n.FloorZ, n.Classes, n.Flags, n.CostMultiplier);
            at[key] = c;
            keys.Add(key);
        }

        keys.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var used = new HashSet<(int, int, float, ClearanceClasses, NavCellFlags, float)>();
        int rects = 0;
        foreach (var k in keys)
        {
            if (used.Contains(k))
                continue;
            int w = 1;
            while (at.ContainsKey(k with { X = k.X + w }) && !used.Contains(k with { X = k.X + w }))
                w++;
            int h = 1;
            while (Enumerable.Range(0, w).All(i => at.ContainsKey((k.X + i, k.Y + h, k.Z, k.K, k.F, k.C)) && !used.Contains((k.X + i, k.Y + h, k.Z, k.K, k.F, k.C))))
                h++;
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                    used.Add((k.X + i, k.Y + j, k.Z, k.K, k.F, k.C));
            }

            rects++;
        }

        return rects;
    }

    private static int Islands(NavGraph g)
    {
        var parts = new HashSet<int>();
        for (int c = 0; c < g.CellCount; c++)
            parts.Add(g.ComponentOf(c));
        return parts.Count;
    }

    private static string Share(long part, long whole) => whole == 0 ? "0 %" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * part / whole:F1} %");

    private static IEnumerable<string> Variant(string tokens, string name, byte[] nav, byte[] bsp, NavHullNavSource source, NavHullReader room, NavNumbers numbers,
                                               MapEntities map, RoomTeleports t, Func<double> cpuMicros)
    {
        var set = tokens.Split('+').ToHashSet(StringComparer.Ordinal);
        string[] known = ["base", "tele", "reach", "reach2", "k2", "k4", "nosight", "tiled", "check", "table"];
        int threads = 1, tile = 128;
        if (set.FirstOrDefault(s => s.StartsWith("tile", StringComparison.Ordinal) && int.TryParse(s.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out tile)) is { } sized)
            set.Remove(sized);
        else
            tile = 128;
        if (set.FirstOrDefault(s => s.Length > 1 && s[0] == 't' && int.TryParse(s.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out threads)) is { } many)
            set.Remove(many);
        if (set.FirstOrDefault(s => !known.Contains(s)) is { } bad)
            throw new ArgumentException($"no variant token {bad}: {string.Join(", ", known)}");
        var lines = new List<string>();
        void Step(string what, Action work, Func<string> said)
        {
            double cpu = cpuMicros();
            TimeSpan all = Process.GetCurrentProcess().TotalProcessorTime;
            long wall = Stopwatch.GetTimestamp();
            work();
            double cpuS = (cpuMicros() - cpu) / 1e6, wallS = Stopwatch.GetElapsedTime(wall).TotalSeconds;
            using var process = Process.GetCurrentProcess();
            double allS = (process.TotalProcessorTime - all).TotalSeconds;
            double managed = GC.GetTotalMemory(forceFullCollection: true) / 1048576.0;
            process.Refresh();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {what,-8} {cpuS,7:F2} s cpu {wallS,7:F2} s wall {managed,7:F0} MB managed {process.PeakWorkingSet64 / 1048576.0,7:F0} MB peak {allS,7:F2} s process cpu  {said()}"));
        }

        IReadOnlyList<HurtVolume> hurt = set.Contains("tele") || set.Contains("table") ? [.. map.Hurt, .. t.Resets] : map.Hurt;
        DerivedNav d = null!;
        int tiledK = set.Contains("k2") ? 2 : 4;
        if (set.Contains("tiled"))
        {
            // The derivation and the merge together, a tile at a time (NavGraphReduce.DeriveCoarse).
            var stats = new CoarseDeriveStats();
            Step("tiled", () => d = NavGraphReduce.DeriveCoarse(source, numbers, NavEdgeCosts.Default, hurt, map.LadderClimbs, tiledK, tile, stats, Math.Max(1, threads)),
                 () => string.Create(CultureInfo.InvariantCulture,
                     $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges; {stats.VoxelCells:N0} cells at the voxel, {stats.Tiles} tiles on {Math.Max(1, threads)} thread(s), {stats.LeavesDerived:N0} leaves derived of {stats.Leaves:N0}, the biggest tile {stats.BiggestTileCells:N0} cells, margin {stats.Margin}; ms: layout {stats.PrepareMs:F0}, tiles {stats.TilesMs:F0} (derive {stats.DeriveMs:F0}, merge {stats.MergeMs:F0} summed), assemble {stats.AssembleMs:F0}"));
        }
        else
        {
            Step("derive", () => d = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, hurt, map.LadderClimbs),
                 () => string.Create(CultureInfo.InvariantCulture, $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges"));
        }

        DerivedNav before = d;
        var space = new NavHullVoxelSpace(room);
        if (set.Contains("reach") || set.Contains("reach2"))
        {
            Step("reach", () =>
            {
                var stops = new bool[d.Graph.CellCount];
                for (int c = 0; c < stops.Length; c++)
                    stops[c] = Inside(d.Graph.Cell(c), t.Resets);
                bool[] keep = NavReach.From(d.Graph, Seeds(d.Graph, t.WaysIn), set.Contains("reach2") ? TwiceOneRocket : OneRocket, space, stops);
                d = NavGraphReduce.Keep(d, keep, NavEdgeCosts.Default);
            }, () => string.Create(CultureInfo.InvariantCulture, $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges"));
        }

        foreach (int k in (ReadOnlySpan<int>)[2, 4])
        {
            if (!set.Contains("tiled") && set.Contains("k" + k.ToString(CultureInfo.InvariantCulture)))
                Step("k" + k.ToString(CultureInfo.InvariantCulture), () => d = NavGraphReduce.Coarsen(d, source.VoxelSize, k, NavEdgeCosts.Default),
                     () => string.Create(CultureInfo.InvariantCulture, $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges"));
        }

        NavSight? sight = null;
        if (!set.Contains("nosight"))
            Step("sight", () => sight = NavSight.Build(d.Graph, space), () => string.Create(CultureInfo.InvariantCulture, $"{sight!.NodeCount:N0} nodes, {sight.Pairs:N0} pairs"));

        var cache = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default);
        string key = NavLoader.HullKey(NavGraphCache.NumbersKey(numbers, source.JumpHeight)) + "-" + tokens;
        string bspSha = BuiltNavStore.Sha256(bsp), navSha = BuiltNavStore.Sha256(nav);
        byte[] bytes = [];
        Step("encode", () => bytes = cache.Encode(name, bspSha, navSha, d, key), () => string.Create(CultureInfo.InvariantCulture, $"{bytes.Length:N0} bytes"));
        DerivedNav back = null!;
        Step("decode", () => back = cache.Decode(bytes, name, bspSha, navSha, key), () => back.Graph.Hash == d.Graph.Hash ? "the same graph" : "A DIFFERENT GRAPH");

        if (set.Contains("check"))
        {
            // The tiled graph against the whole derivation merged (after the steps measured): the same, cell for cell.
            DerivedNav whole = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, hurt, map.LadderClimbs);
            if (set.Contains("tiled"))
                whole = NavGraphReduce.Coarsen(whole, source.VoxelSize, tiledK, NavEdgeCosts.Default, NavGraphReduce.GridOrigin(source));
            bool same = whole.Graph.Hash == d.Graph.Hash && whole.CellOfLeaf.AsSpan().SequenceEqual(d.CellOfLeaf) && whole.LeafOfCell.AsSpan().SequenceEqual(d.LeafOfCell);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  check: the whole derivation{(set.Contains("tiled") ? " merged" : "")} {whole.Graph.CellCount:N0} cells, {whole.Graph.EdgeCount:N0} edges, hash {whole.Graph.Hash:x16}; this {d.Graph.Hash:x16}: {(same ? "the same graph" : "A DIFFERENT GRAPH")}"));
        }

        // What a course needs still there, against the graph made with no rule (derived again here, after the steps measured):
        // each way in and capture area on a cell, and the joined-up parts that keep a cell.
        if (set.Contains("tele") || set.Contains("table"))
            before = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, map.Hurt, map.LadderClimbs);
        int waysBefore = Seeds(before.Graph, t.WaysIn).Count, waysAfter = Seeds(d.Graph, t.WaysIn).Count;
        int capsBefore = t.Captures.Count(c => HasCell(before.Graph, c.Mins, c.Maxs)), capsAfter = t.Captures.Count(c => HasCell(d.Graph, c.Mins, c.Maxs));
        (int parts, int kept, int keptBig, int big) = PartsKept(before, d);
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  kept: ways in on a cell {waysAfter} of {waysBefore}; capture areas with a cell {capsAfter} of {capsBefore}; joined-up parts with a cell left {kept:N0} of {parts:N0}, of those of 16 cells or more {keptBig:N0} of {big:N0}; islands after {Islands(d.Graph):N0}"));
        return lines;
    }

    /// <summary>A capture area's cells, which reach reaches them, and, when one rocket does not, the reached cell nearest one of them.</summary>
    private static string CaptureLine(NavGraph g, IVoxelSpace space, AiVector lo, AiVector hi, bool[] walk, bool[] one, bool[] two, bool[] stops)
    {
        var inside = new List<int>();
        var near = new List<int>();
        g.CellsNear(lo.X, lo.Y, hi.X, hi.Y, near);
        foreach (int c in near)
        {
            AiVector at = g.Cell(c).Centre;
            if (at.X >= lo.X && at.X <= hi.X && at.Y >= lo.Y && at.Y <= hi.Y && at.Z >= lo.Z - 1f && at.Z <= hi.Z)
                inside.Add(c);
        }

        string line = string.Create(CultureInfo.InvariantCulture,
            $"  capture area ({lo.X:F0} {lo.Y:F0} {lo.Z:F0})-({hi.X:F0} {hi.Y:F0} {hi.Z:F0}): {inside.Count} cells, {inside.Count(c => stops[c])} in a teleport back; reached by the edges {inside.Count(c => walk[c])}, one rocket {inside.Count(c => one[c])}, twice it {inside.Count(c => two[c])}");
        if (inside.Count == 0 || inside.Any(c => one[c]))
            return line;
        AiVector target = g.Cell(inside[0]).Centre;
        int best = -1;
        float bestD = float.MaxValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            if (!one[c] || stops[c])
                continue;
            float dd = (g.Cell(c).Centre - target).Length;
            if (dd < bestD)
            {
                bestD = dd;
                best = c;
            }
        }

        if (best < 0)
            return line + "; no cell reached outside a teleport back";
        AiVector b = g.Cell(best).Centre;

        // Every reached cell against every capture cell within one rocket's bound: whether sampling missed a flight.
        long pairs = 0, flies = 0;
        int every = Math.Max(1, inside.Count / 32);
        for (int i = 0; i < inside.Count; i += every)
        {
            int l = inside[i];
            AiVector to = g.Cell(l).Centre;
            for (int c = 0; c < g.CellCount; c++)
            {
                if (!one[c] || stops[c])
                    continue;
                AiVector from = g.Cell(c).Centre;
                if ((to - from).Length2D > OneRocket.Range || to.Z - from.Z > OneRocket.Rise)
                    continue;
                pairs++;
                if (NavReach.Flies(space, from, to, from.Z + OneRocket.Rise))
                    flies++;
            }
        }

        var probe = new System.Text.StringBuilder();
        for (float z = b.Z + 36f; z <= b.Z + OneRocket.Rise + 36f; z += 64f)
        {
            probe.Append(CultureInfo.InvariantCulture, $" z+{z - b.Z:F0}:");
            probe.Append(FirstBlocked(space, b + new AiVector(0, 0, 36f), new AiVector(b.X, b.Y, z), "up"));
            probe.Append(FirstBlocked(space, new AiVector(b.X, b.Y, z), new AiVector(target.X, target.Y, z), "across"));
            probe.Append(FirstBlocked(space, new AiVector(target.X, target.Y, z), target + new AiVector(0, 0, 36f), "down"));
        }

        return line + string.Create(CultureInfo.InvariantCulture,
            $"; nearest reached cell at ({b.X:F0} {b.Y:F0} {b.Z:F0}), {(target - b).Length2D:F0} across, {target.Z - b.Z:F0} up; flies with one rocket's rise: {NavReach.Flies(space, b, target, b.Z + OneRocket.Rise)}; every reached cell to one capture cell in {every} in the bound: {pairs:N0} pairs, {flies:N0} fly;{probe}");
    }

    private static string FirstBlocked(IVoxelSpace space, AiVector a, AiVector b, string leg)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling((b - a).Length / NavReach.Step));
        for (int i = 0; i <= steps; i++)
        {
            AiVector p = a + (b - a) * (i / (float)steps);
            if (!space.Free(p))
                return string.Create(CultureInfo.InvariantCulture, $" {leg} blocked at ({p.X:F0} {p.Y:F0} {p.Z:F0})");
        }

        return "";
    }

    private static bool HasCell(NavGraph g, AiVector lo, AiVector hi)
    {
        var near = new List<int>();
        g.CellsNear(lo.X, lo.Y, hi.X, hi.Y, near);
        foreach (int c in near)
        {
            AiVector at = g.Cell(c).Centre;
            if (at.X >= lo.X && at.X <= hi.X && at.Y >= lo.Y && at.Y <= hi.Y && at.Z >= lo.Z - 1f && at.Z <= hi.Z)
                return true;
        }

        return false;
    }

    /// <summary>How many of <paramref name="before"/>'s joined-up parts keep a cell in <paramref name="after"/>, by leaf: all, and those of 16 cells or more.</summary>
    public static (int Parts, int Kept, int KeptBig, int Big) PartsKept(DerivedNav before, DerivedNav after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var size = new Dictionary<int, int>();
        var kept = new HashSet<int>();
        for (int c = 0; c < before.Graph.CellCount; c++)
        {
            int part = before.Graph.ComponentOf(c);
            size[part] = size.GetValueOrDefault(part) + 1;
            int leaf = before.LeafOfCell[c];
            if (leaf < after.CellOfLeaf.Length && after.CellOfLeaf[leaf] >= 0)
                kept.Add(part);
        }

        return (size.Count, kept.Count, kept.Count(p => size[p] >= 16), size.Count(p => p.Value >= 16));
    }
}
