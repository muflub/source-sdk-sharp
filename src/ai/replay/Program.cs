using System.Globalization;
using SourceSharp.Ai;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Replay;

// tools/ai-replay's entry point (plan_hl2mp_ai §7.3):
//   nav  <map.nav3d> <map.bsp> <out.png> [pixels-per-unit]
//   heat <map.nav3d> <map.bsp> <recording.airec> <out-dir> [every=66] [pixels-per-unit]
//   verify <map.nav3d> <map.bsp> <recording.airec> [threads=0,1,2,4]   (0 is inline)
//   metrics <recording.airec>...   (§3.4.4: per actor, humans' and bots')
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
