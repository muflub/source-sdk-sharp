using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;

namespace SourceSharp.Ai.Replay;

/// <summary>What one part of the brains' steps cost over a replay (<see cref="CostTool"/>).</summary>
public readonly record struct PartCost(StepPart Part, double Micros, double MaxMicros, long Units);

/// <summary>
/// What the brains of a recording cost, replayed: each part's time over every brain
/// (<see cref="StepCost"/>), the steps run, and the whole step's time by the session's own clock
/// (the game's <c>ai.brain.cpu.us</c>, offline), which the parts must add up to.
/// </summary>
public sealed record CostReport(string Map, int Bots, long Steps, double StepMicros, double StepMicrosMax, IReadOnlyList<PartCost> Parts, int Intents)
{
    /// <summary>The mean step, in microseconds.</summary>
    public double PerStep => Steps == 0 ? 0 : StepMicros / Steps;

    /// <summary>Every part's time over the steps: what a step costs, by the parts' own marks.</summary>
    public double PartsPerStep => Steps == 0 ? 0 : Parts.Sum(p => p.Micros) / Steps;

    /// <summary><paramref name="part"/>'s mean time a step.</summary>
    public double PerStepOf(StepPart part) => Steps == 0 ? 0 : Parts.First(p => p.Part == part).Micros / Steps;
}

/// <summary>
/// <c>ai-replay cost</c> (plan_bots_teams_tf2_part_g G1's cost): replays a recording made with
/// <c>ai_record</c> through a game's own trees on one thread and says where a decide's time goes,
/// in the thread's CPU time (the machine is shared: wall time is somebody else's load).
///
/// <para>What a recording does not carry is not replayed: a view's class and the HUD's objectives
/// (so TF2's tree runs with no flag to go for: its fight, its search and its roam), and a game's
/// bodies, which <c>bodies</c> gives back (each class's run, by the class a scoreboard row shows).
/// The memory's inputs, every event and every view's sighted actors, are the recording's own.</para>
/// </summary>
public static class CostTool
{
    private sealed class TF2Profile : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => TFNavProfile.ModNumbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => TFTeamSolids.Read(entities);
    }

    /// <summary>The games whose trees the tool replays with.</summary>
    public const string Hl2mp = "hl2mp", TF2 = "tf2";

    /// <summary>TF2's classes' runs as the shipped class scripts have them (the game's load line prints them), for <c>bodies=tf2</c>.</summary>
    public const string TF2Bodies = "1=400,2=300,3=240,4=280,5=320,6=230,7=300,8=320,9=300";

    /// <summary>
    /// The bodies a <c>bodies</c> argument names: "-" or "" for the recording's own (none: everybody
    /// at <see cref="BodyKnowledge.DefaultRunSpeed"/>), "tf2" for <see cref="TF2Bodies"/>, one number
    /// for everybody's run, or <c>class=speed</c> rows (an unseen class runs at their mean).
    /// </summary>
    public static BodyKnowledge? Bodies(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text is "" or "-")
            return null;
        if (text == TF2)
            text = TF2Bodies;
        if (!text.Contains('=', StringComparison.Ordinal))
            return BodyKnowledge.Same(new BodyFacts(float.Parse(text, CultureInfo.InvariantCulture)));
        (int Class, BodyFacts Facts)[] rows = [.. text.Split(',').Select(row =>
        {
            string[] kv = row.Split('=');
            return (int.Parse(kv[0], CultureInfo.InvariantCulture), new BodyFacts(float.Parse(kv[1], CultureInfo.InvariantCulture)));
        })];
        return new BodyKnowledge(new BodyFacts(rows.Average(r => r.Facts.RunSpeed)), rows);
    }

    /// <summary>The navigation a game's binding loads for <paramref name="map"/>, by its own numbers.</summary>
    public static NavLoadResult LoadNav(string game, string map, string navPath, string bspPath)
        => NavLoader.Load(map, p => p.EndsWith(Path.GetExtension(navPath), StringComparison.Ordinal) ? File.ReadAllBytes(navPath)
                                    : p.EndsWith(".bsp", StringComparison.Ordinal) && File.Exists(bspPath) ? File.ReadAllBytes(bspPath) : null, null,
                          game == TF2 ? new TF2Profile() : null);

    /// <summary>A game's decider for a replayed bot: its tree of this build.</summary>
    public static Func<BrainSetup, IDecider?> Deciders(string game) => game switch
    {
        TF2 => setup => new TFDecider(setup, TFKnowledge.Classes(c => (0f, 0))),
        Hl2mp => setup => new SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider(setup),
        _ => throw new ArgumentException($"no trees for {game}: {Hl2mp} or {TF2}", nameof(game)),
    };

    /// <summary>What a game's brains know that a recording's world does not carry: TF2's abilities, and the bodies asked for.</summary>
    public static MapKnowledge Knows(string game, MapKnowledge recorded, BodyKnowledge? bodies)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        return new MapKnowledge(recorded.Map, recorded.Items, recorded.Spawns, recorded.Weapons, recorded.TeamSpawns,
                                game == TF2 ? TFAbilities.Table() : recorded.Abilities, recorded.Objectives, recorded.TeamZones, bodies ?? recorded.Bodies);
    }

    /// <summary>
    /// Replays <paramref name="recording"/> on the calling thread over <paramref name="graph"/>
    /// with <paramref name="game"/>'s trees, every brain's parts timed by <paramref name="clock"/>.
    /// </summary>
    public static CostReport Measure(Stream recording, string game, NavGraph graph, ICellSight? sight, BodyKnowledge? bodies, Func<double> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var costs = new List<StepCost>();
        AiSession? replayed = null;
        AiReplayResult r = AiReplayer.ReplayForCost(recording, new InlineTransport(), _ => (graph, sight), Deciders(game), session =>
        {
            replayed = session;
            session.CpuClock = clock;
            session.Costs = _ =>
            {
                var cost = new StepCost(clock);
                costs.Add(cost);
                return cost;
            };
        }, map => Knows(game, map, bodies));
        AiCounters counters = replayed!.Counters;
        PartCost[] parts = [.. Enum.GetValues<StepPart>().Select(p => new PartCost(p, costs.Sum(c => c.Micros(p)), costs.Count == 0 ? 0 : costs.Max(c => c.Max(p)), costs.Sum(c => c.Units(p))))];
        return new CostReport(r.Header.Map, costs.Count, counters.StepsRun, counters.BrainCpuMicros, counters.BrainMicrosMax, parts, r.ReplayedIntentCount);
    }

    /// <summary>The report as the tool prints it: a row a part, then the whole step.</summary>
    public static string Table(CostReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var s = new StringBuilder();
        s.AppendLine("part        us/step  share   worst us  units/step");
        double all = report.Parts.Sum(p => p.Micros);
        foreach (PartCost p in report.Parts)
        {
            s.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{p.Part.ToString().ToLowerInvariant(),-10} {p.Micros / Math.Max(1, report.Steps),8:F1}  {(all > 0 ? 100 * p.Micros / all : 0),5:F1}%  {p.MaxMicros,9:F0}  {(p.Units > 0 ? ((double)p.Units / Math.Max(1, report.Steps)).ToString("F2", CultureInfo.InvariantCulture) : "")}"));
        }

        s.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{"parts",-10} {report.PartsPerStep,8:F1}\n{"step",-10} {report.PerStep,8:F1}  (the session's clock round the whole step: ai.brain.cpu.us offline), worst wall {report.StepMicrosMax:F0}"));
        return s.ToString();
    }

    /// <summary><c>cost &lt;hl2mp|tf2&gt; &lt;map.nav3d&gt; &lt;map.bsp&gt; &lt;recording.airec&gt; [bodies=-] [passes=2]</c>: the last pass's table (the first warms the code up).</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 4)
        {
            error.WriteLine("usage: SourceSharp.Ai.Replay cost <hl2mp|tf2> <map.nav3d> <map.bsp> <recording.airec> [bodies=-] [passes=2]");
            return 2;
        }

        string game = args[0], navPath = args[1], bspPath = args[2], rec = args[3];
        string bodiesText = args.Length > 4 ? args[4] : "-";
        int passes = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 2;
        if (!File.Exists(rec))
        {
            error.WriteLine($"ai-replay: no recording {rec}: a cost recording is build output, made with `ai_record start <path>` before the bots are added (tools/ai-replay's header says how)");
            return 1;
        }

        if (ThreadCpuClock.Micros() is not { } clock)
        {
            error.WriteLine("ai-replay: no thread CPU clock on this system: the cost is not measured in wall time");
            return 1;
        }

        string map = Path.GetFileNameWithoutExtension(navPath);
        NavLoadResult loaded = LoadNav(game, map, navPath, bspPath);
        if (loaded.Nav is not { } nav)
        {
            error.WriteLine($"ai-replay: {loaded.Refusal}");
            return 1;
        }

        NavSight sight = NavSight.Build(nav.Nav.Graph, nav.File.Space);
        byte[] bytes = File.ReadAllBytes(rec);
        BodyKnowledge? bodies = Bodies(bodiesText);
        CostReport? report = null;
        try
        {
            for (int pass = 0; pass < Math.Max(1, passes); pass++)
                report = Measure(new MemoryStream(bytes), game, nav.Nav.Graph, sight, bodies, clock);
        }
        catch (InvalidDataException e)
        {
            error.WriteLine($"ai-replay: {e.Message}");
            return 1;
        }

        string runs = bodies is null ? $"the recording's (everybody {BodyKnowledge.DefaultRunSpeed:F0} u/s)"
            : bodies.Classes.Length == 0 ? string.Create(CultureInfo.InvariantCulture, $"everybody {bodies.Unknown.RunSpeed:F0} u/s")
            : string.Create(CultureInfo.InvariantCulture, $"by class {string.Join(",", bodies.Classes.Select(c => $"{c.Class}={c.Facts.RunSpeed:F0}"))}, unseen {bodies.Unknown.RunSpeed:F0} u/s");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-replay: cost {Path.GetFileName(rec)}: {report!.Map}, {game}'s trees, {report.Bots} bots, {report.Steps} steps, {report.Intents} intents, {nav.Nav.Graph.CellCount} cells; believed runs: {runs}; thread CPU time, pass {Math.Max(1, passes)}"));
        output.Write(Table(report));
        return report.Steps > 0 ? 0 : 1;
    }
}
