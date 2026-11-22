using System.Diagnostics;
using System.Globalization;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Replay;

/// <summary>One step of loading a navigation, as <see cref="NavSizeTool"/> measured it: its thread CPU and wall time, and the process after it.</summary>
/// <param name="ManagedMb">The managed heap after a full collection.</param>
/// <param name="PeakMb">The process's peak resident memory so far.</param>
public sealed record NavSizeStep(string Name, double CpuMs, double WallMs, double ManagedMb, double PeakMb, string What);

/// <summary>What a navigation file costs a server to load: the steps in order, and the graph and sight table when it got that far.</summary>
public sealed record NavSizeReport(IReadOnlyList<NavSizeStep> Steps, DerivedNav? Nav, NavSight? Sight, long CacheBytes);

/// <summary>
/// <c>ai-replay navsize</c>: what a map's navigation costs to load, offline, as the server
/// loads it (plan_bots_teams_tf2_part_g G.7): the file opened (a <c>.nav3d</c> or a
/// <c>.navhull</c>, by its extension), read as leaves, the graph derived by the game's numbers,
/// the sight table built, the graph cache encoded and decoded. Each step is timed in the
/// thread's CPU time and the wall clock, with the managed heap after it (collected) and the
/// process's peak resident memory, so a map too big for a step says which and at what size:
/// <c>stop</c> names the last step to run, and the sight table can be left out.
/// </summary>
public static class NavSizeTool
{
    public static readonly IReadOnlyList<string> StepNames = ["open", "leaves", "derive", "sight", "cache"];

    public static NavSizeReport Measure(string game, string map, string navName, byte[] nav, byte[] bsp, string stop, Func<double> cpuMicros, bool sight = true)
    {
        ArgumentNullException.ThrowIfNull(nav);
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

        bool isHull = navName.EndsWith(NavHullFormat.Extension, StringComparison.Ordinal);
        (INavSource source, IVoxelSpace space, NavHullReader? hull) = Step<(INavSource, IVoxelSpace, NavHullReader?)>("open", () =>
        {
            if (isHull)
            {
                NavHullReader reader = NavHullReader.Open(nav);
                return (NavHullNavSource.Over(reader), new NavHullVoxelSpace(reader), reader);
            }

            Nav3dReader nav3d = Nav3dReader.Open(nav);
            return (new Nav3dNavSource(nav3d), new Nav3dVoxelSpace(nav3d), null);
        }, r => r.Item3 is { } h
            ? string.Create(CultureInfo.InvariantCulture, $"{nav.Length:N0} bytes, {h.BoxCount:N0} boxes, {h.KeyCount:N0} keys, {h.JumpCount:N0} jump links, {h.Columns} x {h.Rows} cells of {h.CellVoxels} columns, {h.ColumnVoxels} voxels up, widths {string.Join(", ", h.Widths)}; {r.Item1.LeafCount:N0} leaves (a box's columns)")
            : string.Create(CultureInfo.InvariantCulture, $"{nav.Length:N0} bytes, {r.Item1.LeafCount:N0} leaves"));
        NavNumbers numbers = game == CostTool.TF2 ? TFNavProfile.ModNumbers : NavNumbers.Stock(source.JumpHeight);
        if (hull is not null && NavHullNavSource.MissingWidth(hull, numbers, navName) is { } why)
            throw new InvalidDataException(why);
        if (last < 1)
            return new NavSizeReport(steps, null, null, 0);

        Step("leaves", () =>
        {
            // Every leaf read once, as the derivation's first pass reads them.
            int walkable = 0, stands = 0, sloped = 0;
            for (int leaf = 0; leaf < source.LeafCount; leaf++)
            {
                NavLeaf l = source.Leaf(leaf);
                if (!l.Walkable)
                    continue;
                walkable++;
                bool stand = source.Stands(leaf, numbers.HullWidth, numbers.CrouchHeight);
                stands += stand ? 1 : 0;
                if (!stand && hull is not null && source is NavHullNavSource boxes)
                {
                    // A key's overhang records are its sloped brushes (.navhull version 2).
                    sloped += hull.Key((int)hull.Box(boxes.BoxOf(leaf)).Key).OverhangCount > 0 ? 1 : 0;
                }
            }

            return (walkable, stands, sloped);
        }, r => string.Create(CultureInfo.InvariantCulture, $"{r.walkable:N0} leaves on a walkable floor, {r.stands:N0} hold the crouched hull on it")
                + (hull is null ? "" : string.Create(CultureInfo.InvariantCulture, $", {r.sloped:N0} of those that do not are beside a sloped brush (their key has overhang records)")));
        if (last < 2)
            return new NavSizeReport(steps, null, null, 0);

        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav derived = Step("derive", () => NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs), d =>
        {
            var byType = new SortedDictionary<NavEdgeType, int>();
            for (int c = 0; c < d.Graph.CellCount; c++)
            {
                foreach (NavEdge e in d.Graph.EdgesFrom(c))
                    byType[e.Type] = byType.GetValueOrDefault(e.Type) + 1;
            }

            var parts = new Dictionary<int, int>();
            for (int c = 0; c < d.Graph.CellCount; c++)
                parts[d.Graph.ComponentOf(c)] = parts.GetValueOrDefault(d.Graph.ComponentOf(c)) + 1;
            return string.Create(CultureInfo.InvariantCulture, $"{d.Graph.CellCount:N0} cells, {d.Graph.EdgeCount:N0} edges ({string.Join(", ", byType.Select(p => $"{p.Key} {p.Value:N0}"))}), {d.Graph.ObstacleCount} obstacles; ")
                   + string.Create(CultureInfo.InvariantCulture, $"{parts.Count:N0} joined-up parts, the largest {(parts.Count == 0 ? 0 : parts.Values.Max()):N0} cells; ")
                   + (entities.Unread is { } unread ? $"entities not read ({unread})" : $"{entities.Hurt.Count} hurt volumes");
        });
        if (last < 3)
            return new NavSizeReport(steps, derived, null, 0);

        NavSight? table = !sight ? null : Step("sight", () => NavSight.Build(derived.Graph, space),
                                               s => string.Create(CultureInfo.InvariantCulture, $"{s.NodeCount:N0} nodes, {s.Pairs:N0} pairs"));
        if (last < 4)
            return new NavSizeReport(steps, derived, table, 0);

        var cache = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default);
        string key = NavGraphCache.NumbersKey(numbers, source.JumpHeight);
        if (hull is not null)
            key = NavLoader.HullKey(key);
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

    /// <summary><c>navsize &lt;hl2mp|tf2&gt; &lt;map.nav3d|map.navhull&gt; &lt;map.bsp&gt; [stop=cache] [no-sight]</c>.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 3)
        {
            error.WriteLine($"usage: ai-replay navsize <{CostTool.Hl2mp}|{CostTool.TF2}> <map.nav3d|map.navhull> <map.bsp> [stop: {string.Join("|", StepNames)}] [no-sight]");
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
            NavSizeReport report = Measure(args[0], map, args[1], File.ReadAllBytes(args[1]), File.ReadAllBytes(args[2]), args.Length > 3 ? args[3] : "cache", clock,
                                           sight: !(args.Length > 4 && args[4] == "no-sight"));
            output.WriteLine($"ai-replay navsize {args[0]} {Path.GetFileName(args[1])}:");
            foreach (NavSizeStep s in report.Steps)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {s.Name,-7} {s.CpuMs / 1000.0,8:F2} s cpu {s.WallMs / 1000.0,8:F2} s wall  {s.ManagedMb,8:F0} MB managed  {s.PeakMb,8:F0} MB peak  {s.What}"));
            }

            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or OutOfMemoryException)
        {
            error.WriteLine($"ai-replay navsize: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
