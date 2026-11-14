using System.Diagnostics;
using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.Replay;

/// <summary>What the place-finding pass took on a map: each part's thread CPU, and the process's peak memory after it.</summary>
/// <param name="ColumnsMs">Reading the nav's free space into columns.</param>
/// <param name="ReachMs">Measuring the simulator's reach in open air.</param>
/// <param name="ProposeMs">Proposing alone (the levels, the edges, the ways out, the walks).</param>
/// <param name="PassMs">The whole pass (proposing again, then a search a candidate).</param>
public sealed record JumpPlacesCost(double DeriveMs, double ColumnsMs, double ReachMs, double ProposeMs, double PassMs, double PeakMb, double ManagedMb, string Clock);

/// <summary>The pass on one map: its graph and free space, the reach, what the pass found, and what it cost.</summary>
/// <param name="Seeds">The spawns found on the graph: a takeoff is proposed only in a part of the graph one is in (none: anywhere).</param>
public sealed record JumpPlacesReport(string Map, NavGraph Graph, VoxelColumns Columns, JumpEnvelope Reach, JumpPlacesResult Result, JumpPlacesCost Cost, bool Cancelled, int Seeds = 0);

/// <summary>
/// <c>ai-replay jumps</c> (plan_bots_teams_tf2_part_g G.5 step 4, lane G5c-2a): the blast-jump
/// place-finding pass (<see cref="JumpPlaces"/>) run offline on a map's navigation, as the game would
/// run it on a worker: the graph derived by TF2's numbers, the nav's free space read into columns,
/// the simulator's reach measured, then the pass. It says what the pass proposed and kept, the
/// flights it flew, the thread CPU each part took and the process's peak memory, and how the links
/// it kept are spread (their rise, their distance, their time against the walk's, their health
/// and robustness); every link is written to a json beside the map's other reports.
///
/// <para>It is a tool of the replay project because that project reaches TF2's binding (the
/// simulator) with no game: <see cref="OfflineTF2"/> builds the simulator as the Jump facts' rig does.</para>
/// </summary>
public static class JumpPlacesTool
{
    /// <summary>Runs the pass on the map; <paramref name="candidateLimit"/> caps the searches (0: none), <paramref name="cancel"/> stops it between candidates.</summary>
    public static JumpPlacesReport Measure(string map, byte[] nav, byte[] bsp, string soldierScript, Func<double> cpuMicros, int candidateLimit = 0,
                                           CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(cpuMicros);
        double t0 = cpuMicros();
        var source = new Nav3dNavSource(Nav3dReader.Open(nav));
        MapEntities entities = MapEntities.Read(bsp);
        NavGraph graph = NavGraphFromNav3d.Derive(source, TFNavProfile.ModNumbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs).Graph;
        double t1 = cpuMicros();
        IReadOnlyList<int> seeds = Seeds(JumpRoutesTool.ReadEntities(bsp), graph);
        VoxelColumns columns = VoxelColumns.FromNav(source);
        double t2 = cpuMicros();

        TFClassTable classes = OfflineTF2.Classes(soldierScript);
        BlastJumpSims sims = OfflineTF2.Sims(classes);
        JumpLauncher launcher = JumpLauncher.Rocket(OfflineTF2.RocketDamage, OfflineTF2.RocketRefire);
        float run = classes[TFClass.Soldier].MaxSpeed;
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, run, BlastJumpSims.TickSeconds, TFJumps.PlannerTurn);
        var settings = new JumpSearchSettings();
        JumpEnvelope reach = JumpEnvelope.Measure(new JumpFlier(sims.Create(JumpEnvelope.OpenAir(), launcher), limits), settings);
        double t3 = cpuMicros();

        GraphJumpWalk walk = GraphJumpWalk.Of(graph, run);
        var places = new JumpPlaces(graph, columns, new JumpSearch(new JumpFlier(sims.Create(columns, launcher), limits), walk, settings), walk, reach,
                                    JumpPlacesSettings.Of(TFNavProfile.ModNumbers) with { MaxCandidates = candidateLimit }, seeds);
        places.Propose();
        double t4 = cpuMicros();
        JumpPlacesResult result;
        bool cancelled = false;
        try
        {
            result = places.Find(cancel);
        }
        catch (OperationCanceledException)
        {
            result = new JumpPlacesResult([], [], 0, default);
            cancelled = true;
        }

        double t5 = cpuMicros();
        double managed = GC.GetTotalMemory(forceFullCollection: true) / 1048576.0;
        using var process = Process.GetCurrentProcess();
        var cost = new JumpPlacesCost((t1 - t0) / 1e3, (t2 - t1) / 1e3, (t3 - t2) / 1e3, (t4 - t3) / 1e3, (t5 - t4) / 1e3, process.PeakWorkingSet64 / 1048576.0, managed,
                                      "thread cpu");
        return new JumpPlacesReport(map, graph, columns, reach, result, cost, cancelled, seeds.Count);
    }

    /// <summary>The cells a map's players start in: its team spawns and deathmatch spawns, each on the graph.</summary>
    public static IReadOnlyList<int> Seeds(MapEntities entities, NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var seeds = new List<int>();
        foreach (MapEntity e in entities.All.Where(e => e.ClassName is "info_player_teamspawn" or "info_player_deathmatch"))
        {
            CellId cell = NavPoints.CellNear(graph, e.Origin, ClearanceClass.Stand);
            if (cell.IsValid)
                seeds.Add(cell.Index);
        }

        return seeds;
    }

    /// <summary>The smallest, the quartiles and the largest of <paramref name="values"/>, or "none".</summary>
    public static string Spread(IEnumerable<float> values, string format = "F0")
    {
        float[] v = [.. values.OrderBy(x => x)];
        if (v.Length == 0)
            return "none";
        string F(float x) => x.ToString(format, CultureInfo.InvariantCulture);
        float Q(double q) => v[(int)Math.Round(q * (v.Length - 1))];
        return $"min {F(v[0])}  p25 {F(Q(0.25))}  median {F(Q(0.5))}  p75 {F(Q(0.75))}  max {F(v[^1])}";
    }

    /// <summary>A link's rise: its landing cell's floor over its takeoff cell's.</summary>
    public static float Rise(NavGraph graph, in JumpLink link) => graph.Cell(link.To).FloorZ - graph.Cell(link.From).FloorZ;

    /// <summary>A link's distance over the ground, from its takeoff to its landing.</summary>
    public static float Distance(in JumpLink link)
        => MathF.Sqrt(((link.Recipe.Landing.X - link.Recipe.Takeoff.X) * (link.Recipe.Landing.X - link.Recipe.Takeoff.X))
                      + ((link.Recipe.Landing.Y - link.Recipe.Takeoff.Y) * (link.Recipe.Landing.Y - link.Recipe.Takeoff.Y)));

    private static string V(Vector v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:F0}, {v.Y:F0}, {v.Z:F0}]");

    /// <summary>The report's text: the pass's numbers and the links' spread.</summary>
    public static string Text(JumpPlacesReport r, string build, string commit)
    {
        ArgumentNullException.ThrowIfNull(r);
        JumpPlacesResult p = r.Result;
        JumpPlacesCost c = r.Cost;
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"ai-replay jumps {r.Map} ({build}, {commit}; {c.Clock}, one thread):\n");
        s.Append(CultureInfo.InvariantCulture, $"  graph       {r.Graph.CellCount:N0} cells, {r.Graph.EdgeCount:N0} edges; free space {r.Columns.Width} x {r.Columns.Depth} columns of {r.Columns.VoxelSize:F0}\n");
        s.Append(CultureInfo.InvariantCulture, $"  reach       rise {r.Reach.Rise:F1}, range {r.Reach.Range:F1} ({r.Reach.Flights} flights)\n");
        s.Append(CultureInfo.InvariantCulture, $"  looked at   {p.Work.Edges:N0} column edges, {p.Work.Runs:N0} runs, {p.Work.Landings:N0} landings asked on foot; {r.Seeds} spawns on the graph, {p.Work.Unplayed} takeoffs passed over in parts no spawn is in\n");
        s.Append(CultureInfo.InvariantCulture, $"  pass        {p.Candidates.Count} candidates ({p.Candidates.Count(x => x.Kind == JumpPlaceKind.Up)} up, {p.Candidates.Count(x => x.Kind == JumpPlaceKind.Gap)} gap, {p.Candidates.Count(x => x.WalkCost is null)} with no walk), {p.Links.Count} links kept, {p.Flights:N0} flights{(r.Cancelled ? "; CANCELLED" : "")}\n");
        s.Append(CultureInfo.InvariantCulture, $"  cpu s       derive {c.DeriveMs / 1e3:F2}, columns {c.ColumnsMs / 1e3:F2}, reach {c.ReachMs / 1e3:F2}, propose {c.ProposeMs / 1e3:F2}, pass {c.PassMs / 1e3:F2} (searches {(c.PassMs - c.ProposeMs) / 1e3:F2}; {(c.PassMs > c.ProposeMs ? p.Flights / ((c.PassMs - c.ProposeMs) / 1e3) : 0):F0} flights a second)\n");
        s.Append(CultureInfo.InvariantCulture, $"  memory      peak {c.PeakMb:F0} MB resident, {c.ManagedMb:F0} MB managed after the pass\n");
        IReadOnlyList<JumpLink> links = p.Links;
        s.Append("  links' spread:\n");
        s.Append("    rise        ").Append(Spread(links.Select(l => Rise(r.Graph, l)))).Append('\n');
        s.Append("    distance    ").Append(Spread(links.Select(l => Distance(l)))).Append('\n');
        s.Append("    seconds     ").Append(Spread(links.Select(l => l.Seconds), "F2")).Append('\n');
        s.Append("    air seconds ").Append(Spread(links.Select(l => l.AirSeconds), "F2")).Append('\n');
        int walks = links.Count(l => l.WalkSeconds is not null);
        s.Append(CultureInfo.InvariantCulture, $"    walk        {links.Count - walks} with no way on foot; of {walks} with one, the jump's time over the walk's: ")
         .Append(Spread(links.Where(l => l.WalkSeconds is > 0f).Select(l => l.Seconds / l.WalkSeconds!.Value), "F2")).Append('\n');
        s.Append("    health      ").Append(Spread(links.Select(l => l.Health), "F1")).Append('\n');
        s.Append("    robustness  ").Append(Spread(links.Select(l => l.Robustness), "F2")).Append('\n');
        s.Append(CultureInfo.InvariantCulture, $"    family      {links.Count(l => l.Family == JumpFamily.Up)} up, {links.Count(l => l.Family == JumpFamily.Forward)} forward\n");
        return s.ToString();
    }

    /// <summary>Every candidate and link as json, for judging by eye and for the routes after it.</summary>
    public static string Json(JumpPlacesReport r, string build, string commit)
    {
        ArgumentNullException.ThrowIfNull(r);
        JumpPlacesResult p = r.Result;
        var json = new StringBuilder("{\n");
        json.Append(CultureInfo.InvariantCulture,
            $" \"map\": \"{r.Map}\",\n \"build\": \"{build}\",\n \"commit\": \"{commit}\",\n \"clock\": \"{r.Cost.Clock}\",\n \"cells\": {r.Graph.CellCount},\n \"graph_hash\": \"{r.Graph.Hash:x16}\",\n");
        json.Append(CultureInfo.InvariantCulture,
            $" \"reach_rise\": {r.Reach.Rise:F1},\n \"reach_range\": {r.Reach.Range:F1},\n \"candidates\": {p.Candidates.Count},\n \"links\": {p.Links.Count},\n \"flights\": {p.Flights},\n \"cancelled\": {(r.Cancelled ? "true" : "false")},\n");
        json.Append(CultureInfo.InvariantCulture,
            $" \"derive_ms\": {r.Cost.DeriveMs:F0},\n \"columns_ms\": {r.Cost.ColumnsMs:F0},\n \"reach_ms\": {r.Cost.ReachMs:F0},\n \"propose_ms\": {r.Cost.ProposeMs:F0},\n \"pass_ms\": {r.Cost.PassMs:F0},\n \"peak_mb\": {r.Cost.PeakMb:F0},\n");
        json.Append(" \"proposed\": [\n");
        json.AppendJoin(",\n", p.Candidates.Select(c => string.Create(CultureInfo.InvariantCulture,
            $"  {{\"kind\": \"{c.Kind}\", \"from\": {c.From}, \"to\": {c.To}, \"takeoff\": {V(c.Takeoff)}, \"landing\": {V(c.Landing)}, \"rise\": {c.Rise:F0}, \"distance\": {c.Distance:F0}, \"walk_cost\": {(c.WalkCost is { } w ? w.ToString("F0", CultureInfo.InvariantCulture) : "null")}}}")));
        json.Append("\n ],\n \"found\": [\n");
        json.AppendJoin(",\n", p.Links.Select(l => string.Create(CultureInfo.InvariantCulture,
            $"  {{\"from\": {l.From}, \"to\": {l.To}, \"takeoff\": {V(l.Recipe.Takeoff)}, \"landing\": {V(l.Recipe.Landing)}, \"from_floor\": {r.Graph.Cell(l.From).FloorZ:F0}, \"to_floor\": {r.Graph.Cell(l.To).FloorZ:F0}, \"rise\": {Rise(r.Graph, l):F0}, \"distance\": {Distance(l):F0}, \"family\": \"{l.Family}\", \"run_up\": {l.Recipe.RunUp:F0}, \"pitch\": {l.Recipe.Pitch:F1}, \"yaw_offset\": {l.Recipe.YawOffset:F0}, \"shot_delay\": {l.Recipe.ShotDelay}, \"robustness\": {l.Robustness:F2}, \"seconds\": {l.Seconds:F3}, \"air_seconds\": {l.AirSeconds:F3}, \"health\": {l.Health:F1}, \"walk_seconds\": {(l.WalkSeconds is { } w ? w.ToString("F2", CultureInfo.InvariantCulture) : "null")}}}")));
        json.Append("\n ]\n}\n");
        return json.ToString();
    }

    /// <summary><c>jumps &lt;map.nav3d&gt; &lt;map.bsp&gt; &lt;soldier.txt&gt; &lt;out.json&gt; &lt;build&gt; &lt;commit&gt; [candidate-limit] [routes]</c>.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 6)
        {
            error.WriteLine("usage: ai-replay jumps <map.nav3d> <map.bsp> <soldier.txt> <out.json> <build> <commit> [candidate-limit] [routes]");
            return 2;
        }

        if (ThreadCpuClock.Micros() is not { } clock)
        {
            error.WriteLine("ai-replay: no thread CPU clock on this system");
            return 1;
        }

        try
        {
            string map = Path.GetFileNameWithoutExtension(args[0]);
            int limit = args.Length > 6 ? int.Parse(args[6], CultureInfo.InvariantCulture) : 0;
            JumpPlacesReport report = Measure(map, File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]), args[2], clock, limit);
            output.Write(Text(report, args[4], args[5]));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
            File.WriteAllText(args[3], Json(report, args[4], args[5]));
            output.WriteLine($"  written     {args[3]}");
            if (args.Length > 7 && args[7] == "routes")
                output.Write(JumpRoutesTool.Text(report, JumpRoutesTool.ReadEntities(File.ReadAllBytes(args[1])), OfflineTF2.Classes(args[2])[TFClass.Soldier].MaxSpeed,
                                                 Path.ChangeExtension(args[3], ".png")));
            return report.Cancelled ? 1 : 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException)
        {
            error.WriteLine($"ai-replay jumps: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
