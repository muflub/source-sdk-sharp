using System.Globalization;
using SourceSharp.Ai;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Replay;

// tools/ai-replay's entry point (plan_hl2mp_ai §7.3):
//   nav  <map.nav3d> <map.bsp> <out.png> [pixels-per-unit]
//   heat <map.nav3d> <map.bsp> <recording.airec> <out-dir> [every=66] [pixels-per-unit]
//   verify <map.nav3d> <map.bsp> <recording.airec> [threads=0,1,2,4]   (0 is inline)
//   metrics <recording.airec>...   (§3.4.4: per actor, humans' and bots')
//   jank <recording.airec>...      (§3.4.5: what makes a bot look wrong, as numbers)
if (args.Length >= 2 && args[0] == "metrics")
{
    foreach (string rec in args.Skip(1))
    {
        using FileStream input = File.OpenRead(rec);
        IReadOnlyList<ActorMetrics> actors = AiMetrics.Read(input);
        Console.WriteLine($"ai-replay: metrics {Path.GetFileName(rec)}: {actors.Count} actor(s), {actors.Count(a => !a.Bot)} human, {actors.Count(a => a.Bot)} bot");
        Console.Write(AiMetrics.Table(actors));
    }

    return 0;
}

//   calibrate <hitrates.tsv> <probes.tsv> <hl2mp_skills.json> [min-samples=50]
//   hitrates <metrics.txt> <out.tsv>   a person's band rates from ai-replay metrics' output
if (args.Length >= 3 && args[0] == "hitrates")
{
    try
    {
        var rows = AimCalibration.FromMetrics(File.ReadAllText(args[1]));
        File.WriteAllText(args[2], AimCalibration.WriteFile(rows));
        Console.WriteLine($"{args[2]}: {rows.Length} band(s)");
        return 0;
    }
    catch (Exception e) when (e is InvalidDataException or IOException)
    {
        Console.Error.WriteLine($"ai-replay hitrates: {e.Message}");
        return 1;
    }
}

if (args.Length >= 4 && args[0] == "calibrate")
{
    try
    {
        long minSamples = args.Length > 4 ? long.Parse(args[4], CultureInfo.InvariantCulture) : 50;
        var rows = AimCalibration.Usable(AimCalibration.ParseFile(File.ReadAllText(args[1])), minSamples);
        var probes = AimCalibration.ParseProbes(File.ReadAllText(args[2]));
        string skills = File.ReadAllText(args[3]);
        var found = AimCalibration.Calibrate(rows, probes, SkillCurves.HitRateRatios(skills));
        foreach (HitRateRow r in rows)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"target  {r.Weapon,-15} {r.Band,-9} hit rate {100.0 * r.HitRate:F1}% over {r.Samples} shots"));
        Console.WriteLine("preset  level  target  aimErrorDeg  shipped  reached");
        SkillCurves.Curve shipped = SkillCurves.Parse(skills)["aimErrorDeg"];
        foreach (PresetCalibration c in found)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Preset,-7} {c.Level,5}  {100.0 * c.Target,5:F1}%  {c.AimErrorDeg,11:F2}  {shipped.At(c.Level),7:F2}  {(c.Reached ? "yes" : "NO: the probes' nearest end")}"));
        }

        if (found.Length == 0)
        {
            Console.Error.WriteLine("ai-replay calibrate: no probe is of a row in the file");
            return 1;
        }

        float[] curve = AimCalibration.Curve([.. found.Select(c => (c.Level, c.AimErrorDeg))], shipped.Levels);
        Console.WriteLine("aimErrorDeg levels: [" + string.Join(", ", curve.Select(v => v.ToString("F3", CultureInfo.InvariantCulture))) + "]");
        return found.All(c => c.Reached) ? 0 : 2;
    }
    catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException or FormatException)
    {
        Console.Error.WriteLine($"ai-replay calibrate: {e.Message}");
        return 1;
    }
}

if (args.Length >= 2 && args[0] == "jank")
{
    int empty = 0;
    foreach (string rec in args.Skip(1))
    {
        JankReport report = JankTool.Read(rec);
        Console.WriteLine($"ai-replay: jank {Path.GetFileName(rec)}: {report.Actors.Count(a => !a.Bot)} human, {report.Actors.Count(a => a.Bot)} bot; leaves named by {report.TreeSource}");
        // A recording with no one in it measured nothing, and says so.
        if (report.Actors.Count == 0)
        {
            Console.Error.WriteLine($"ai-replay: {rec} holds no actor's frames (before format 3, or nobody played): no score");
            empty++;
            continue;
        }

        Console.Write(AiJank.Table(report));
    }

    return empty == 0 ? 0 : 1;
}
//   chat-eval <url> <console.log> <chat.json> <out.md> <lines> <model>...
if (args.Length >= 1 && args[0] == "chat-eval")
    return ChatEval.Run(args[1..]);

if (args.Length >= 4 && args[0] == "verify")
{
    string vNav = args[1], vBsp = args[2], vRec = args[3];
    int[] counts = (args.Length > 4 ? args[4] : "0,1,2,4").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    string vMap = Path.GetFileNameWithoutExtension(vNav);
    NavLoadResult v = NavLoader.Load(vMap, p => p.EndsWith(".nav3d", StringComparison.Ordinal) ? File.ReadAllBytes(vNav)
                                               : File.Exists(vBsp) ? File.ReadAllBytes(vBsp) : null, null);
    if (v.Nav is not { } vn)
    {
        Console.Error.WriteLine($"ai-replay: {v.Refusal}");
        return 1;
    }

    NavSight vSight = NavSight.Build(vn.Nav.Graph, new Nav3dVoxelSpace(vn.Reader));
    byte[] bytes = File.ReadAllBytes(vRec);
    int differ = 0;
    foreach (int threads in counts)
    {
        using var pool = new AiWorkerPool("ai-verify");
        IAiTransport transport = threads <= 0 ? new InlineTransport() : new PooledTransport(pool);
        if (threads > 0)
            pool.Start(threads);
        AiReplayResult r = AiReplayer.ReplayWithRecordedWorld(new MemoryStream(bytes), transport, _ => (vn.Nav.Graph, vSight),
                                                              setup => new SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider(setup));
        bool same = r.Identical && r.RecordedIntentCount == r.ReplayedIntentCount && r.RecordedIntentCount > 0;
        if (!same)
            differ++;
        Console.WriteLine($"ai-replay: verify {Path.GetFileName(vRec)} {(threads <= 0 ? "inline" : $"{threads} thread(s)")}: "
                          + $"{r.RecordedIntentCount} intents recorded, {r.ReplayedIntentCount} replayed, {(same ? "identical" : "DIFFERENT")}");
    }

    return differ == 0 ? 0 : 1;
}

if (args.Length >= 5 && args[0] == "heat")
{
    string heatNav = args[1], heatBsp = args[2], rec = args[3], dir = args[4];
    int every = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 66;
    float px = args.Length > 6 ? float.Parse(args[6], CultureInfo.InvariantCulture) : 0.25f;
    string heatMap = Path.GetFileNameWithoutExtension(heatNav);
    NavLoadResult l = NavLoader.Load(heatMap, p => p.EndsWith(".nav3d", StringComparison.Ordinal) ? File.ReadAllBytes(heatNav)
                                                  : File.Exists(heatBsp) ? File.ReadAllBytes(heatBsp) : null, null);
    if (l.Nav is not { } n)
    {
        Console.Error.WriteLine($"ai-replay: {l.Refusal}");
        return 1;
    }

    var world = new MemoryWorld(new MapKnowledge(heatMap, [], [], new WeaponKnowledge([])), n.Nav.Graph, 0.015f, id => id.ToString());
    using FileStream input = File.OpenRead(rec);
    List<HeatFrame> frames = HeatPng.Collect(input, world, every);
    Directory.CreateDirectory(dir);
    foreach (HeatFrame f in frames.Where(f => f.Mass.Count > 0))
        File.WriteAllBytes(Path.Combine(dir, $"heat-bot{f.Bot}-t{f.Tick:D7}.png"), HeatPng.Render(n.Nav, f, px));
    Console.WriteLine($"ai-replay: {frames.Count(f => f.Mass.Count > 0)} heat frames of {frames.Count} into {dir}");
    return 0;
}

if (args.Length < 4 || args[0] != "nav")
{
    Console.Error.WriteLine("usage: SourceSharp.Ai.Replay nav <map.nav3d> <map.bsp> <out.png> [pixels-per-unit=0.25]\n"
                            + "       SourceSharp.Ai.Replay heat <map.nav3d> <map.bsp> <recording.airec> <out-dir> [every=66] [pixels-per-unit=0.25]");
    return 2;
}

string navPath = args[1], bspPath = args[2], outPath = args[3];
float scale = args.Length > 4 ? float.Parse(args[4], CultureInfo.InvariantCulture) : 0.25f;
string map = Path.GetFileNameWithoutExtension(navPath);
NavLoadResult loaded = NavLoader.Load(map, p => p.EndsWith(".nav3d", StringComparison.Ordinal) ? File.ReadAllBytes(navPath)
                                                : File.Exists(bspPath) ? File.ReadAllBytes(bspPath) : null, null);
if (loaded.Nav is not { } nav)
{
    Console.Error.WriteLine($"ai-replay: {loaded.Refusal}");
    return 1;
}

File.WriteAllBytes(outPath, NavPng.Render(nav.Nav, scale));
Console.WriteLine($"ai-replay: {outPath}: {nav.Nav.Graph.CellCount} cells, {nav.Nav.Graph.EdgeCount} edges, {scale} px/unit");
return 0;
