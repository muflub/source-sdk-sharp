using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Replay;

/// <summary>One region's file as <see cref="NavSplitTool"/> wrote it.</summary>
public sealed record SplitRow(int Region, int Boxes, int Leaves, int Columns, int Rows, int ZVoxels, int Keys, int Jumps, int CrossingJumps, long Bytes, string File);

/// <summary>
/// <c>ai-replay navsplit</c>: a hull navigation as a file per sealed space, offline
/// (plan_bots_teams_tf2_part_g, the user's Q-R1 of 2026-11-13: "can we make a nav3d file per
/// sealed space and load it on demand?"). <c>write</c> cuts each region the map's counted entities
/// are in (or the regions named) into a <c>.navhull</c> of its own (<see cref="NavHullSplit"/>,
/// written by map-tools' own writer) and writes the room index (<see cref="NavRoomIndex"/>) and
/// a table of what each room holds and where its teleports go. <c>check</c> derives one region
/// both ways, out of the whole file through <see cref="NavRegionSource"/> and out of its own file,
/// and says whether the graphs are the same. A region's own file is then measured alone, in a
/// process that never opens the whole map's, by <c>navsize</c>.
/// </summary>
public static class NavSplitTool
{
    /// <summary>The file a region is written to.</summary>
    public static string PartName(string map, int region) => string.Create(CultureInfo.InvariantCulture, $"{map}.r{region}{NavHullFormat.Extension}");

    /// <summary>The regions that hold one of the survey's counted entities.</summary>
    public static IReadOnlyList<int> CountedRegions(RoomsSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return [.. survey.Rows.Where(r => r.Entities.Count > 0).Select(r => r.Region)];
    }

    /// <summary>
    /// <c>navsplit write &lt;map.navhull&gt; &lt;map.bsp&gt; &lt;out-dir&gt; [counted|all|r,r,..] [none|deflate]</c>,
    /// or <c>navsplit check &lt;hl2mp|tf2&gt; &lt;map.navhull&gt; &lt;map.bsp&gt; &lt;region&gt; &lt;part.navhull&gt;</c>.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            if (args.Length >= 4 && args[0] == "write")
                return Write(args[1], args[2], args[3], args.Length > 4 ? args[4] : "counted", args.Length > 5 ? args[5] : "same", output);
            if (args.Length >= 6 && args[0] == "check")
                return Check(args[1], args[2], args[3], int.Parse(args[4], CultureInfo.InvariantCulture), args[5], output);
            if (args.Length >= 4 && args[0] == "graph")
                return Graph(args[1], args[2], args[3], output);
            if (args.Length >= 5 && args[0] == "rooms")
                return Rooms(args[1], args[2], args[3], args[4], args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : Math.Max(1, Environment.ProcessorCount / 2), output);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or FormatException)
        {
            error.WriteLine($"ai-replay navsplit: {e.GetType().Name}: {e.Message}");
            return 1;
        }

        error.WriteLine("usage: ai-replay navsplit write <map.navhull> <map.bsp> <out-dir> [counted|all|r,r,..] [same|none|deflate]");
        error.WriteLine($"       ai-replay navsplit graph <{CostTool.Hl2mp}|{CostTool.TF2}> <map.nav3d|map.navhull> <map.bsp>");
        error.WriteLine($"       ai-replay navsplit rooms <{CostTool.Hl2mp}|{CostTool.TF2}> <map.navhull> <map.bsp> <out-dir> [threads]");
        error.WriteLine($"       ai-replay navsplit check <{CostTool.Hl2mp}|{CostTool.TF2}> <map.navhull> <map.bsp> <region> <part.navhull>");
        return 2;
    }

    private static int Write(string navPath, string bspPath, string outDir, string which, string codec, TextWriter output, bool table = true)
    {
        string map = Path.GetFileNameWithoutExtension(navPath);
        byte[] nav = File.ReadAllBytes(navPath), bsp = File.ReadAllBytes(bspPath);
        var clock = Stopwatch.StartNew();
        NavHullReader reader = NavHullReader.Open(nav);
        NavHullLevel level = reader.ToLevel();
        NavHullRegions regions = NavHullRegions.Of(reader);
        RoomsSurvey survey = NavRoomsTool.Survey(reader, regions, NavRoomsTool.ReadEntities(bsp));
        double openS = clock.Elapsed.TotalSeconds;
        IReadOnlyList<int> chosen = which switch
        {
            "counted" => CountedRegions(survey),
            "all" => [.. Enumerable.Range(0, regions.Count)],
            _ => [.. which.Split(',').Select(r => int.Parse(r, CultureInfo.InvariantCulture))],
        };
        NavCompression compression = codec switch
        {
            "none" => NavCompression.None,
            "deflate" => new NavCompression(NavCodec.Deflate, 6),
            _ => reader.Codec == NavCodec.None ? NavCompression.None : new NavCompression(reader.Codec, reader.Codec == NavCodec.Deflate ? 6 : 9),
        };
        Directory.CreateDirectory(outDir);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-replay navsplit {Path.GetFileName(navPath)}: {nav.Length:N0} bytes ({reader.Codec}), {reader.BoxCount:N0} boxes, {regions.Count:N0} regions, {reader.Columns} x {reader.Rows} cells of {reader.CellVoxels} columns, {reader.ColumnVoxels} voxels up; opened, read and surveyed in {openS:F1} s"));
        output.WriteLine("  region    boxes     leaves  cells(c x r)  z-voxels   keys  jumps crossing        bytes  file");
        var rows = new List<SplitRow>();
        foreach (int region in chosen)
        {
            NavHullPart part = NavHullSplit.Region(reader, level, regions, region);
            byte[] bytes = NavHullWriter.Write(part.Level, compression);
            string file = Path.Combine(outDir, PartName(map, region));
            File.WriteAllBytes(file, bytes);
            int zLo = part.Level.Boxes.Min(b => (int)b.ZLo), zHi = part.Level.Boxes.Max(b => b.ZHi);
            long leaves = part.Level.Boxes.Sum(b => (long)(b.X1 - b.X0) * (b.Y1 - b.Y0));
            var row = new SplitRow(region, part.Level.Boxes.Length, (int)leaves, part.Level.Columns, part.Level.Rows, zHi - zLo + 1, part.Level.Keys.Count, part.Level.Jumps.Count, part.CrossingJumps, bytes.Length, Path.GetFileName(file));
            rows.Add(row);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {row.Region,6} {row.Boxes,8:N0} {row.Leaves,10:N0}  {row.Columns,5} x {row.Rows,-5} {row.ZVoxels,8:N0} {row.Keys,6:N0} {row.Jumps,6:N0} {row.CrossingJumps,8:N0} {row.Bytes,12:N0}  {row.File}"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {rows.Count} files, {rows.Sum(r => r.Bytes):N0} bytes; {rows.Count(r => r.ZVoxels <= 255)} of them span at most 255 voxels up; {rows.Sum(r => r.CrossingJumps):N0} jump links cross out of a region"));

        clock.Restart();
        NavRoomIndex index = NavRoomIndex.Of(reader, regions);
        double indexS = clock.Elapsed.TotalSeconds;
        byte[] indexBytes = index.Write();
        string indexFile = Path.Combine(outDir, map + ".navrooms");
        File.WriteAllBytes(indexFile, indexBytes);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  room index: {index.Cells:N0} cells, {index.Spans:N0} spans, {index.SharedCells:N0} cells where two regions' spans share a height; {indexBytes.Length:N0} bytes, built in {indexS:F2} s"));
        foreach (string line in IndexChecks(reader, regions, index, survey, NavRoomsTool.ReadEntities(bsp)))
            output.WriteLine(line);

        if (!table)
            return 0;
        string tableFile = Path.Combine(outDir, map + ".navrooms.json");
        File.WriteAllText(tableFile, JsonSerializer.Serialize(RoomTable(map, survey, chosen, NavRoomsTool.ReadEntities(bsp), reader, regions), new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  room table: {new FileInfo(tableFile).Length:N0} bytes, {Path.GetFileName(tableFile)}"));
        return 0;
    }

    /// <summary>The file names of a map in rooms: its index and its table.</summary>
    public static (string Index, string Table) RoomFiles(string map) => (map + ".navrooms", map + ".navrooms.json");

    /// <summary>
    /// <c>navsplit rooms</c>: what the navigation build makes of a map's hull navigation (plan_bots_teams_tf2_part_g
    /// R.2 items 1, 2 and 7). Under the leaf limit (<see cref="NavBuildSettings.DefaultMaxLeaves"/>) the map loads whole
    /// and nothing is written (a room table already there is removed). Over it, the counted regions are written a file
    /// each, with the index, and the table (<see cref="NavRoomTable"/>): each room's ways in, its teleports back, sorted
    /// into fail floors and kept ones on the room's own graph (<see cref="NavRoomTeleports.Classify"/>), its exits and its
    /// capture areas.
    /// </summary>
    private static int Rooms(string game, string navPath, string bspPath, string outDir, int threads, TextWriter output)
    {
        string map = Path.GetFileNameWithoutExtension(navPath);
        byte[] nav = File.ReadAllBytes(navPath), bsp = File.ReadAllBytes(bspPath);
        NavHullReader reader = NavHullReader.Open(nav);
        long leaves = NavHullNavSource.Over(reader).LeafCount;
        (string indexName, string tableName) = RoomFiles(map);
        if (leaves <= NavBuildSettings.DefaultMaxLeaves)
        {
            foreach (string stale in (string[])[indexName, tableName])
                File.Delete(Path.Combine(outDir, stale));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ai-replay navsplit rooms {map}: {leaves:N0} leaves, within {NavBuildSettings.DefaultMaxLeaves:N0}: the map loads whole, no rooms written"));
            return 0;
        }

        int rc = Write(navPath, bspPath, outDir, "counted", "same", output, table: false);
        if (rc != 0)
            return rc;
        var clock = Stopwatch.StartNew();
        NavHullRegions regions = NavHullRegions.Of(reader);
        IReadOnlyList<RoomEntity> entities = NavRoomsTool.ReadEntities(bsp);
        IReadOnlyList<IReadOnlyList<HurtVolume>> brushes = NavCellsTool.BrushBoxes(bsp);
        RoomsSurvey survey = NavRoomsTool.Survey(reader, regions, entities);
        MapEntities mapEntities = MapEntities.Read(bsp);
        var named = new Dictionary<string, RoomEntity>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (e.TargetName is { } n)
                named.TryAdd(n, e);
        }

        int[] regionOf = [.. entities.Select(e => NavRoomsTool.Counted.Contains(e.ClassName) ? NavRoomsTool.RegionOf(reader, regions, e) : -1)];
        var rooms = new List<NavRoom>();
        foreach (int region in CountedRegions(survey))
        {
            string file = PartName(map, region);
            var source = NavHullNavSource.Over(NavHullReader.Open(File.ReadAllBytes(Path.Combine(outDir, file))));
            NavNumbers numbers = game == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(source.JumpHeight);
            var ways = new List<AiVector>();
            var spawns = new List<AiVector>();
            var resets = new List<NavRoomTeleport>();
            var exits = new List<NavRoomTeleport>();
            var captures = new List<HurtVolume>();
            for (int i = 0; i < entities.Count; i++)
            {
                RoomEntity e = entities[i];
                if (regionOf[i] != region)
                    continue;
                switch (e.ClassName)
                {
                    case "info_teleport_destination":
                        ways.Add(e.Origin);
                        break;
                    case "info_player_teamspawn" or "info_player_deathmatch":
                        ways.Add(e.Origin);
                        spawns.Add(e.Origin);
                        break;
                    case "trigger_capture_area" when e.Mins is { } lo && e.Maxs is { } hi:
                        captures.Add(new HurtVolume(lo, hi));
                        break;
                    case "trigger_teleport":
                        RoomEntity? d = e.Target is { } t && named.TryGetValue(t, out RoomEntity? x) ? x : null;
                        int to = d is null ? -1 : NavRoomsTool.RegionOf(reader, regions, d);
                        var teleport = new NavRoomTeleport(e.TargetName, e.Target, to, d?.Origin, brushes[i]);
                        (to == region ? resets : exits).Add(teleport);
                        break;
                }
            }

            DerivedNav graph = NavGraphReduce.DeriveCoarse(source, numbers, NavEdgeCosts.Default, mapEntities.Hurt, mapEntities.LadderClimbs, threads: threads);
            bool[] fail = NavRoomTeleports.Classify(graph.Graph, ways, [.. resets.Select(t => t.Brushes)]);
            rooms.Add(new NavRoom(region, file, source.LeafCount, ways, [.. resets.Where((_, i) => fail[i])], [.. resets.Where((_, i) => !fail[i])], exits, captures)
            {
                Spawns = spawns,
            });
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  r{region}: {graph.Graph.CellCount:N0} cells merged, {ways.Count} ways in; teleports back {resets.Count}: {fail.Count(f => f)} fail floors, {fail.Count(f => !f)} kept"
                + $"{(fail.Any(f => !f) ? " (" + string.Join(", ", resets.Where((_, i) => !fail[i]).Select(t => t.Name ?? t.Target ?? "?")) + ")" : "")}; {exits.Count} exits"));
        }

        var tableOut = new NavRoomTable(map, NavRoomTable.RulesRevision, leaves, rooms);
        string tablePath = Path.Combine(outDir, tableName);
        File.WriteAllText(tablePath, tableOut.ToJson());
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  room table: {rooms.Count} rooms, the start r{tableOut.StartRoom?.Region ?? -1} ({tableOut.StartRoom?.Spawns.Count ?? 0} spawns), {rooms.Sum(r => r.Fails.Count)} fail floors, {rooms.Sum(r => r.Kept.Count)} kept teleports back, {new FileInfo(tablePath).Length:N0} bytes, made in {clock.Elapsed.TotalSeconds:F1} s on {threads} thread(s)"));
        return 0;
    }

    /// <summary>
    /// How the index answers: for every box's centre, whether its region is among the answers and
    /// how many answers it gets; for every counted entity, whether the index names the survey's
    /// region; and what a question costs.
    /// </summary>
    public static IEnumerable<string> IndexChecks(NavHullReader reader, NavHullRegions regions, NavRoomIndex index, RoomsSurvey survey, IReadOnlyList<RoomEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(entities);
        var into = new List<int>();
        long missed = 0, one = 0, more = 0;
        var clock = Stopwatch.StartNew();
        for (int box = 0; box < reader.BoxCount; box++)
        {
            (Vec3 lo, Vec3 hi) = reader.BoxBounds(box);
            index.RegionsAt(new Vec3((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f), into);
            if (!into.Contains(regions.RegionOf(box)))
                missed++;
            else if (into.Count == 1)
                one++;
            else
                more++;
        }

        double perQuery = clock.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, reader.BoxCount);
        yield return string.Create(CultureInfo.InvariantCulture,
            $"  every box's centre: {one:N0} answer its region alone, {more:N0} answer it among others, {missed:N0} miss it; {perQuery:F3} µs a question");
        // A point entity (a spawn, a destination: where a bot stands after a teleport) is asked as the survey asks it,
        // at its origin and a little above; a brush (a trigger, an area) at the survey's own sample points.
        var by = new SortedDictionary<string, (int Agree, int Among, int Elsewhere, int Unplaced)>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (!NavRoomsTool.Counted.Contains(e.ClassName))
                continue;
            (int agree, int among, int elsewhere, int unplaced) = by.GetValueOrDefault(e.ClassName);
            int r = NavRoomsTool.RegionOf(reader, regions, e);
            if (r < 0)
            {
                by[e.ClassName] = (agree, among, elsewhere, unplaced + 1);
                continue;
            }

            bool found = false;
            foreach (AiVector at in Samples(e))
            {
                index.RegionsAt(new Vec3(at.X, at.Y, at.Z), into);
                if (into.Count == 0)
                    continue;
                found = into.Contains(r);
                among += found && into.Count > 1 ? 1 : 0;
                break;
            }

            by[e.ClassName] = found ? (agree + 1, among, elsewhere, unplaced) : (agree, among, elsewhere + 1, unplaced);
        }

        foreach ((string cls, (int agree, int among, int elsewhere, int unplaced)) in by)
        {
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {cls}: {agree:N0} the index places in the survey's region ({among:N0} of them among others), {elsewhere:N0} elsewhere or nowhere, {unplaced:N0} in no box");
        }

        // An exact index would hold a run per column instead: the boxes of one column, low to high, a run per change of region.
        long runs = 0;
        for (int cell = 0; cell < reader.CellCount; cell++)
        {
            for (int y = 0; y < reader.CellVoxels; y++)
            {
                for (int x = 0; x < reader.CellVoxels; x++)
                {
                    int last = -1;
                    foreach (int box in reader.ColumnBoxes(cell, x, y))
                    {
                        int region = regions.RegionOf(box);
                        runs += region != last ? 1 : 0;
                        last = region;
                    }
                }
            }
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"  an exact index, a run per column and region: {runs:N0} runs, about {runs * 6:N0} bytes");
    }

    private static IEnumerable<AiVector> Samples(RoomEntity e)
    {
        if (e.Mins is not { } lo || e.Maxs is not { } hi)
        {
            foreach (float up in new[] { 1f, 8f, 16f, 32f, 48f })
                yield return e.Origin + new AiVector(0, 0, up);
            yield break;
        }

        const int Steps = 5;
        for (int i = 0; i < Steps; i++)
        {
            for (int j = 0; j < Steps; j++)
            {
                for (int k = 0; k < Steps; k++)
                    yield return new AiVector(lo.X + (hi.X - lo.X) * (i + 0.5f) / Steps, lo.Y + (hi.Y - lo.Y) * (j + 0.5f) / Steps, lo.Z + (hi.Z - lo.Z) * (k + 0.5f) / Steps);
            }
        }
    }

    private static object RoomTable(string map, RoomsSurvey survey, IReadOnlyList<int> chosen, IReadOnlyList<RoomEntity> entities, NavHullReader reader, NavHullRegions regions)
    {
        var named = new Dictionary<string, RoomEntity>(StringComparer.Ordinal);
        foreach (RoomEntity e in entities)
        {
            if (e.TargetName is { } name)
                named.TryAdd(name, e);
        }

        var teleports = new List<object>();
        foreach (RoomEntity e in entities.Where(e => e.ClassName == "trigger_teleport"))
        {
            int from = NavRoomsTool.RegionOf(reader, regions, e);
            RoomEntity? d = e.Target is { } t && named.TryGetValue(t, out RoomEntity? x) ? x : null;
            int to = d is null ? -1 : NavRoomsTool.RegionOf(reader, regions, d);
            teleports.Add(new { name = e.TargetName, from, to, target = e.Target, mins = V(e.Mins), maxs = V(e.Maxs), destination = d is null ? null : V(d.Origin) });
        }

        return new
        {
            map,
            rooms = survey.Rows.Where(r => chosen.Contains(r.Region)).Select(r => new
            {
                region = r.Region,
                file = PartName(map, r.Region),
                boxes = r.Boxes,
                leaves = r.Leaves,
                mins = V(r.Mins),
                maxs = V(r.Maxs),
                entities = r.Entities,
                teleportsTo = r.TeleportsTo,
                destinations = r.Destinations,
            }),
            teleports,
        };
    }

    private static float[]? V(AiVector? v) => v is { } x ? [x.X, x.Y, x.Z] : null;

    /// <summary>
    /// A whole navigation's graph as the game derives it (a <c>.nav3d</c> or a <c>.navhull</c>), with
    /// the cache file's bytes hashed: what says a map's graph did not change.
    /// </summary>
    private static int Graph(string game, string navPath, string bspPath, TextWriter output)
    {
        byte[] nav = File.ReadAllBytes(navPath), bsp = File.ReadAllBytes(bspPath);
        bool hull = navPath.EndsWith(NavHullFormat.Extension, StringComparison.Ordinal);
        INavSource source = hull ? NavHullNavSource.Over(NavHullReader.Open(nav)) : new Nav3dNavSource(Nav3dReader.Open(nav));
        NavNumbers numbers = game == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(source.JumpHeight);
        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav d = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        string key = NavGraphCache.NumbersKey(numbers, source.JumpHeight);
        if (hull)
            key = NavLoader.HullKey(key);
        byte[] cache = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default).Encode(Path.GetFileNameWithoutExtension(navPath), BuiltNavStore.Sha256(bsp), BuiltNavStore.Sha256(nav), d, key);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-replay navsplit graph {Path.GetFileName(navPath)}: {d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges, graph hash {d.Graph.Hash:x16}, cache {cache.Length:N0} bytes sha256 {BuiltNavStore.Sha256(cache)}"));
        return 0;
    }

    private static int Check(string game, string navPath, string bspPath, int region, string partPath, TextWriter output)
    {
        byte[] nav = File.ReadAllBytes(navPath), part = File.ReadAllBytes(partPath), bsp = File.ReadAllBytes(bspPath);
        NavHullReader whole = NavHullReader.Open(nav), mine = NavHullReader.Open(part);
        NavHullNavSource wholeSource = NavHullNavSource.Over(whole), partSource = NavHullNavSource.Over(mine);
        NavNumbers numbers = game == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(wholeSource.JumpHeight);
        NavHullRegions regions = NavHullRegions.Of(whole);
        var inRegion = NavRegionSource.Over(wholeSource, regions.Leaves(wholeSource, region));
        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav a = NavGraphFromNav3d.Derive(inRegion, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        DerivedNav b = NavGraphFromNav3d.Derive(partSource, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        bool same = a.Graph.Hash == b.Graph.Hash && a.Graph.CellCount == b.Graph.CellCount && a.Graph.EdgeCount == b.Graph.EdgeCount
                    && a.LeafOfCell.AsSpan().SequenceEqual(b.LeafOfCell);
        NavSight sa = NavSight.Build(a.Graph, new NavHullVoxelSpace(whole)), sb = NavSight.Build(b.Graph, new NavHullVoxelSpace(mine));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-replay navsplit check r{region}: out of the whole file {a.Graph.CellCount:N0} cells, {a.Graph.EdgeCount:N0} edges, hash {a.Graph.Hash:x16}, sight {sa.NodeCount:N0} nodes {sa.Pairs:N0} pairs"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  out of its own file {b.Graph.CellCount:N0} cells, {b.Graph.EdgeCount:N0} edges, hash {b.Graph.Hash:x16}, sight {sb.NodeCount:N0} nodes {sb.Pairs:N0} pairs"));
        bool sameSight = sa.NodeCount == sb.NodeCount && sa.Pairs == sb.Pairs && SameSight(a.Graph, sa, sb);
        output.WriteLine(same && sameSight ? "  the same graph, leaf for leaf, and the same sight table" : $"  DIFFERENT: graph {(same ? "same" : "differs")}, sight {(sameSight ? "same" : "differs")}");
        return same && sameSight ? 0 : 1;
    }

    private static bool SameSight(NavGraph g, NavSight a, NavSight b)
    {
        // Every cell's node; each node's eye and the share of its near nodes it sees; and a sample
        // of cell pairs near each other, seen or not.
        var first = new int[a.NodeCount];
        Array.Fill(first, -1);
        for (int c = 0; c < g.CellCount; c++)
        {
            if (a.NodeOf(c) != b.NodeOf(c))
                return false;
            if (first[a.NodeOf(c)] < 0)
                first[a.NodeOf(c)] = c;
        }

        for (int node = 0; node < a.NodeCount; node++)
        {
            if (a.Eye(node) != b.Eye(node) || (first[node] >= 0 && a.Exposure(first[node]) != b.Exposure(first[node])))
                return false;
        }

        var random = new Random(1);
        var near = new List<int>();
        for (int i = 0; i < 5_000 && g.CellCount > 0; i++)
        {
            int x = random.Next(g.CellCount);
            NavCell c = g.Cell(x);
            near.Clear();
            g.CellsNear(c.MinX - 512f, c.MinY - 512f, c.MaxX + 512f, c.MaxY + 512f, near);
            foreach (int y in near)
            {
                if (a.Visible(x, y) != b.Visible(x, y))
                    return false;
            }
        }

        return true;
    }
}
