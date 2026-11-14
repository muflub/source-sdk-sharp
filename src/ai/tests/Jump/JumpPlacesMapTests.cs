using System.Diagnostics;
using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The place-finding pass on the game's own jump map (plan_bots_teams_tf2_part_g G.5 step 4):
/// this tree's nav of ss_ai_tf_jump, its graph as the TF2 binding derives it, and the whole pass
/// run once for the class. What it finds is the blast's physics', so it is written down
/// (<c>bin/&lt;config&gt;/ai-jump/places.json</c>) and not held: the facts hold what must be true
/// of whatever it finds.
/// </summary>
public class JumpPlacesMapTests
{
    /// <summary>The low lane's slab as the map's generator has it (TFAiTestMaps: x 500 to 1,800, y 464 to 800, its underside 100 over the floor's 16).</summary>
    private const float SlabWest = 500f, SlabEast = 1800f, SlabSouth = 464f, SlabNorth = 800f, SlabUnder = 116f;

    /// <summary>
    /// The pass run once, and what it cost the thread that ran it, in microseconds of <paramref name="Clock"/>: measuring
    /// the reach, proposing alone, the whole pass (proposing again, then the searches), one flood of the whole graph back
    /// from a landing, the walks that exist searched again, and, in the release build, the whole pass a second time
    /// (0 in debug): the first is run on code the runtime has only just compiled, quickly and badly, the second on what
    /// it has compiled again since.
    /// </summary>
    internal sealed record Passed(VoxelColumns Columns, NavGraph Graph, JumpEnvelope Reach, JumpPlacesSettings Settings, JumpPlacesResult Result,
                                  string Clock, double ReachMicros, double ProposeMicros, double PassMicros, double FloodMicros, double WalksMicros,
                                  double AgainMicros);

    /// <summary>The CPU seconds the whole pass on this map may take, in any build: some thirteen times the debug build's on an idle box.</summary>
    internal const double CeilingSeconds = 30d;

#if DEBUG
    private const string Build = "debug";
#else
    private const string Build = "release";
#endif

    internal static JumpPlaces Places(VoxelColumns columns, NavGraph graph, JumpEnvelope reach, JumpPlacesSettings settings)
    {
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        return new JumpPlaces(graph, columns, Search(columns, walk: walk), walk, reach, settings);
    }

    internal static readonly Lazy<Passed> Shared = new(() =>
    {
        VoxelColumns columns = JumpRecording.Columns();
        NavGraph graph = JumpRecording.Graph();
        JumpPlacesSettings settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);

        // The thread's own CPU time where there is one (Linux): the box runs every leg at once, and the wall clock counts them all.
        Func<double>? cpu = ThreadCpuClock.Micros();
        Func<double> micros = cpu ?? (static () => Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency);
        double start = micros();
        JumpEnvelope reach = Envelope();
        double reached = micros();
        JumpPlaces places = Places(columns, graph, reach, settings);
        places.Propose();
        double proposed = micros();
        JumpPlacesResult result = places.Find();
        double passed = micros();

        // The dearest flood back from a landing: one everything walks to. Asked of two cells of the floor turn about, so
        // each ask floods again; the floor is where the first takeoff stands (the graph's first cell, with no candidate).
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        var a = new CellId(result.Candidates.Count > 0 ? result.Candidates[0].From : 0);
        CellId b = a;
        foreach (NavEdge e in graph.EdgesFrom(a.Index))
            b = e.Type == NavEdgeType.Walk ? new CellId(e.To) : b;
        const int Floods = 10;
        double before = micros();
        for (int i = 0; i < Floods; i++)
            walk.Reaches(a, i % 2 == 0 ? b : a);
        double flooded = micros();

        // The walks that exist, searched again: the candidates the pass weighed a way on foot for.
        foreach (JumpCandidate c in result.Candidates)
        {
            if (c.WalkCost is not null)
                walk.Cost(new CellId(c.From), new CellId(c.To));
        }

        double walked = micros(), again = walked;
#if !DEBUG
        Places(columns, graph, reach, settings).Find();
        again = micros();
#endif
        return new Passed(columns, graph, reach, settings, result, cpu is null ? "wall" : "thread cpu", reached - start, proposed - reached, passed - proposed,
                          (flooded - before) / Floods, walked - flooded, again - walked);
    });

    private static bool UnderTheSlab(Vector at) => at.X > SlabWest && at.X < SlabEast && at.Y > SlabSouth && at.Y < SlabNorth && at.Z < SlabUnder;

    [JumpMapFact]
    public void EveryLinkFlownAgainLandsOnItsLandingCell()
    {
        Passed p = Shared.Value;
        JumpSearch search = Search(p.Columns);
        Assert.All(p.Result.Links, link =>
        {
            JumpOutcome flown = search.Fly(link.Recipe);
            Assert.True(flown.Flew && flown.OnGround, $"the link from {link.From}: {flown}");
            Assert.Equal(link.To, p.Graph.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z)).Index);
            Assert.Equal(link.From, p.Graph.FindCell(new AiVector(link.Recipe.Takeoff.X, link.Recipe.Takeoff.Y, link.Recipe.Takeoff.Z)).Index);
            // On the landing it was searched for: within the tolerance of it, at its height.
            Assert.True(Flat(flown.Rest, link.Recipe.Landing) <= p.Settings.Tolerance, $"the link from {link.From} rests {Flat(flown.Rest, link.Recipe.Landing)} from its landing");
            Assert.Equal(link.Recipe.Landing.Z, flown.Rest.Z, 0);
            Assert.Contains(p.Result.Candidates, c => c.From == link.From && c.Takeoff.Equals(link.Recipe.Takeoff) && c.Landing.Equals(link.Recipe.Landing));
            Assert.True(link.Robustness >= new JumpSearchSettings().Keep);
        });
    }

    [JumpMapFact]
    public void TheMapsLedgesAreProposedWhenTheBlastRisesThatFar()
    {
        // Geometry, given the reach: a ledge no higher than the share of the simulator's rise the pass proposes within, and
        // with no way up on foot, is a landing of some candidate from the floor. The yard's three are 150, 250 and 350 up.
        Passed p = Shared.Value;
        foreach (float rise in (float[])[150f, 250f, 350f])
        {
            bool within = rise <= p.Settings.ReachShare * p.Reach.Rise;
            Assert.Equal(within, p.Result.Candidates.Any(c => c.Kind == JumpPlaceKind.Up && c.Rise == rise && p.Graph.Cell(c.From).FloorZ == 16f && c.WalkCost is null));
        }
    }

    [JumpMapFact]
    public void NothingIsProposedFromOrToUnderTheLowLanesSlab()
    {
        // Four of the pass's rules each keep this true on this map (the way out stops at the lane's walls, the ground
        // is looked for from the ledge's level down, a takeoff needs air over it, and a pair of levels keeps its best
        // takeoff): it goes red with all four gone, and holds the pass to the slab if it learns another kind of jump.
        Passed p = Shared.Value;
        // The control: the slab is where the fact thinks, a body's standing room under it and no more.
        FreeSpan under = p.Columns.Spans(p.Columns.ColumnX(1000f), p.Columns.ColumnY(632f))[0];
        Assert.True(under.Bottom == 16f && under.Top < SlabUnder && UnderTheSlab(new Vector(1000f, 632f, 17f)), $"under the slab: {under}");
        Assert.True(p.Graph.FindCell(new AiVector(1000f, 632f, 17f)).IsValid, "the control: the graph has cells under the slab to propose from");
        Assert.True(p.Result.Candidates.Count > 0 || p.Settings.ReachShare * p.Reach.Rise < 132f, "the control: the pass proposes something on this map");
        Assert.All(p.Result.Candidates, c =>
        {
            Assert.False(UnderTheSlab(c.Takeoff), $"a takeoff under the slab: {c}");
            Assert.False(UnderTheSlab(c.Landing), $"a landing under the slab: {c}");
            // And over every takeoff there is air up to the landing's height and a crouched body more.
            FreeSpan air = p.Columns.Spans(p.Columns.ColumnX(c.Takeoff.X), p.Columns.ColumnY(c.Takeoff.Y)).ToArray().Single(s => s.Bottom <= c.Takeoff.Z && c.Takeoff.Z < s.Top);
            Assert.True(air.Top >= MathF.Max(c.Landing.Z, c.Takeoff.Z) + p.Settings.CrouchHeight, $"{c}: the air over the takeoff ends at {air.Top}");
        });
    }

    [JumpMapFact]
    public void FromUnderTheSlabTheSimulatorFliesNoJumpOntoItsTop()
    {
        // What the pass is right not to propose: the slab's top is 132 over the low lane's floor and 64 along from a
        // body under it, a jump the pass links from open ground when the blast rises that far, and the slab is in the way.
        Passed p = Shared.Value;
        var under = new Vector(1000f, 632f, 16f + VoxelMoveWorld.DistEpsilon);
        var top = new Vector(1064f, 632f, 148f + VoxelMoveWorld.DistEpsilon);
        Assert.True(UnderTheSlab(under) && p.Graph.Cell(p.Graph.FindCell(new AiVector(top.X, top.Y, top.Z))).FloorZ == 148f, "the control: a body under the slab, and the slab's top over it");
        JumpSearch search = Search(p.Columns);
        JumpSearchResult result = search.Find(new JumpAsk(under, top, p.Settings.Tolerance));
        Assert.Null(result.Found);
        Assert.True(result.Flights > 20, $"the control: the search flew for it: {result.Flights} flights");
        // The control: flights from under the slab do fly and come down, under it.
        JumpOutcome flown = search.Fly(new JumpRecipe(under, top, 0f, 60f, 180f, 2, true));
        Assert.True(flown.Flew && flown.OnGround && flown.Rest.Z < 20f && flown.Apex < 100f, $"under the slab: {flown}");
    }

    [JumpMapFact]
    public void OnAWorkerThreadThePassFindsWhatItFoundHere()
    {
        Passed p = Shared.Value;
        JumpPlaces places = Places(p.Columns, p.Graph, p.Reach, p.Settings);
        JumpPlacesResult? there = null;
        var worker = new Thread(() => there = places.Find());
        worker.Start();
        worker.Join();
        Assert.NotNull(there);
        Assert.Equal(p.Result.Candidates, there.Candidates);
        Assert.Equal(p.Result.Links, there.Links);
        Assert.Equal((p.Result.Flights, p.Result.Work), (there.Flights, there.Work));
    }

    [JumpMapFact]
    public void TheWholePassIsCostedWrittenDownAndUnderItsCeiling()
    {
        // Step 5: what the pass costs one core on this map, and by that arithmetic a map ten times the size. Ten
        // times the cells and ten times the ledges: ten times the edges to look along and ten times the searches, and
        // ten times the floods back from a landing, each over ten times the graph.
        Passed p = Shared.Value;
        JumpPlacesResult r = p.Result;
        double proposeMs = p.ProposeMicros / 1e3, passMs = p.PassMicros / 1e3, floodMs = p.FloodMicros / 1e3, walksMs = p.WalksMicros / 1e3;
        int walks = r.Candidates.Count(c => c.WalkCost is not null);
        // Proposing: its looking (levels, edges, the ways out) grows as the map; its walks that exist are each a search
        // of a graph ten times the size and there are ten times as many; and at the worst every landing asked on foot is
        // one the whole graph walks to, a flood of all of it. The searches are a number of flights a candidate.
        double lookMs = Math.Max(0d, proposeMs - walksMs), floodsMs = r.Work.Landings * floodMs, searchMs = Math.Max(0d, passMs - proposeMs);
        double tenTimesMs = 10d * lookMs + 100d * walksMs + 100d * floodsMs + 10d * searchMs;

        // In release the first pass is mostly the runtime's first, unoptimised compile of the code: ten times the
        // searches run at the second pass's rate, and what the first pass cost over the second is paid once.
        double againMs = p.AgainMicros / 1e3;
        double tenTimesWarmMs = againMs > 0d ? 10d * lookMs + 100d * walksMs + 100d * floodsMs + 10d * Math.Max(0d, againMs - proposeMs) + Math.Max(0d, passMs - againMs) : tenTimesMs;
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\n \"map\": \"{JumpRecording.Map}\",\n \"build\": \"{Build}\",\n \"clock\": \"{p.Clock}\",\n \"threads\": 1,\n \"cells\": {p.Graph.CellCount},\n \"graph_edges\": {p.Graph.EdgeCount},\n \"columns\": {p.Columns.Width * p.Columns.Depth},\n" +
            $" \"column_edges\": {r.Work.Edges},\n \"runs\": {r.Work.Runs},\n \"landings_asked_on_foot\": {r.Work.Landings},\n \"candidates\": {r.Candidates.Count},\n \"links\": {r.Links.Count},\n \"flights\": {r.Flights},\n" +
            $" \"reach_ms\": {p.ReachMicros / 1e3:F1},\n \"reach_flights\": {p.Reach.Flights},\n \"propose_ms\": {proposeMs:F1},\n \"pass_ms\": {passMs:F1},\n \"pass_again_ms\": {(p.AgainMicros > 0d ? (p.AgainMicros / 1e3).ToString("F1", CultureInfo.InvariantCulture) : "null")},\n" +
            $" \"search_ms\": {searchMs:F1},\n \"flights_a_second\": {(searchMs > 0d ? r.Flights / (searchMs / 1e3) : 0d):F0},\n" +
            $" \"walks_that_exist\": {walks},\n \"walks_that_exist_ms\": {walksMs:F2},\n \"whole_graph_flood_ms\": {floodMs:F3},\n \"propose_looking_ms\": {lookMs:F1},\n" +
            $" \"ten_times_the_map_ms\": {tenTimesMs:F0},\n \"ten_times_arithmetic\": \"10 x looking {lookMs:F1} + 100 x walks {walksMs:F2} + 100 x {r.Work.Landings} floods of {floodMs:F3} + 10 x searches {searchMs:F1}\",\n" +
            $" \"ten_times_the_map_at_the_second_pass_rate_ms\": {tenTimesWarmMs:F0},\n \"ceiling_seconds\": {CeilingSeconds}\n}}\n");
        string path = BlastJumpDifferentialTests.ReportPath("cost.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        // The control: what was timed is the pass the other facts hold, and it did its work.
        Assert.True(r.Work.Edges > r.Work.Runs && r.Work.Runs >= r.Work.Landings && r.Work.Landings >= r.Candidates.Count, $"{r.Work} for {r.Candidates.Count} candidates");
        Assert.True(r.Candidates.Count == 0 || (r.Flights > 0 && p.PassMicros > p.ProposeMicros && p.ProposeMicros > 0d && p.FloodMicros > 0d), json);
        Assert.True(passMs / 1e3 <= CeilingSeconds, json);
    }

    private static string V(Vector v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:F1}, {v.Y:F1}, {v.Z:F1}]");

    private static string N(float? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "null";

    [JumpMapFact]
    public void WhatThePassFindsIsWrittenDown()
    {
        Passed p = Shared.Value;
        JumpPlacesResult r = p.Result;
        var json = new StringBuilder("{\n");
        json.Append(CultureInfo.InvariantCulture, $" \"map\": \"{JumpRecording.Map}\",\n \"config\": \"{ServerNav.PerTreeNav.Config}\",\n \"cells\": {p.Graph.CellCount},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"reach_rise\": {p.Reach.Rise:F1},\n \"reach_range\": {p.Reach.Range:F1},\n \"candidates\": {r.Candidates.Count},\n \"links\": {r.Links.Count},\n \"flights\": {r.Flights},\n");
        json.Append(" \"proposed\": [\n");
        json.AppendJoin(",\n", r.Candidates.Select(c => string.Create(CultureInfo.InvariantCulture,
            $"  {{\"kind\": \"{c.Kind}\", \"from\": {c.From}, \"to\": {c.To}, \"takeoff\": {V(c.Takeoff)}, \"landing\": {V(c.Landing)}, \"rise\": {c.Rise:F0}, \"distance\": {c.Distance:F0}, \"walk_cost\": {N(c.WalkCost)}, \"linked\": {(r.Links.Any(l => l.From == c.From && l.Recipe.Landing.Equals(c.Landing)) ? "true" : "false")}}}")));
        json.Append("\n ],\n \"found\": [\n");
        json.AppendJoin(",\n", r.Links.Select(l => string.Create(CultureInfo.InvariantCulture,
            $"  {{\"from\": {l.From}, \"to\": {l.To}, \"from_floor\": {p.Graph.Cell(l.From).FloorZ:F0}, \"to_floor\": {p.Graph.Cell(l.To).FloorZ:F0}, \"family\": \"{l.Family}\", \"run_up\": {l.Recipe.RunUp:F0}, \"pitch\": {l.Recipe.Pitch:F1}, \"yaw_offset\": {l.Recipe.YawOffset:F0}, \"shot_delay\": {l.Recipe.ShotDelay}, \"robustness\": {l.Robustness:F2}, \"seconds\": {l.Seconds:F3}, \"air_seconds\": {l.AirSeconds:F3}, \"health\": {l.Health:F1}, \"walk_seconds\": {N(l.WalkSeconds)}}}")));
        json.Append("\n ]\n}\n");
        string path = BlastJumpDifferentialTests.ReportPath("places.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToString());

        using var read = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal((r.Candidates.Count, r.Links.Count), (read.RootElement.GetProperty("proposed").GetArrayLength(), read.RootElement.GetProperty("found").GetArrayLength()));
        // No more links than candidates, one search a candidate and the flight that finds a link's cell.
        Assert.True(r.Links.Count <= r.Candidates.Count);
        Assert.InRange(r.Flights, r.Links.Count, r.Candidates.Count * (new JumpSearchSettings().MaxFlights + 1));
    }

    [JumpMapFact]
    public void TheReplayToolsPassFindsWhatTheFactsPassFinds()
    {
        // tools/ai-replay jumps builds the simulator with no game (OfflineTF2: the class script, the mod's convars at their
        // defaults, the stock launcher) and derives the graph with the map's own entities: on this map it is the rig's pass.
        Passed p = Shared.Value;
        string bsp = Path.ChangeExtension(JumpRecording.NavPath, ".bsp");
        Func<double> micros = ThreadCpuClock.Micros() ?? (static () => Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency);
        Replay.JumpPlacesReport r = Replay.JumpPlacesTool.Measure(JumpRecording.Map, File.ReadAllBytes(JumpRecording.NavPath), File.ReadAllBytes(bsp),
                                                                  SourceSharp.Tests.RepoLayout.Repo("src", "tf2", "tests", "TestData", "playerclasses", "soldier.txt"), micros);
        Assert.False(r.Cancelled);
        Assert.Equal(p.Graph.Hash, r.Graph.Hash);
        Assert.Equal(p.Reach, r.Reach);
        Assert.Equal(p.Result.Candidates, r.Result.Candidates);
        Assert.Equal(p.Result.Links, r.Result.Links);
        Assert.Equal((p.Result.Flights, p.Result.Work), (r.Result.Flights, r.Result.Work));
        Assert.True(r.Result.Links.Count > 0 && r.Cost.PassMs > 0d && r.Cost.PeakMb > 0d, "the control: the tool found links and measured them");
    }

    /// <summary>Which level stretch each cell is in, worked out here: cells joined by walks between floors no more than a unit apart.</summary>
    private static int[] Levels(NavGraph graph)
    {
        int[] level = [.. Enumerable.Repeat(-1, graph.CellCount)];
        int next = 0;
        for (int start = 0; start < level.Length; start++)
        {
            if (level[start] >= 0)
                continue;
            var open = new Queue<int>([start]);
            level[start] = next;
            while (open.TryDequeue(out int cell))
            {
                foreach (NavEdge e in graph.EdgesFrom(cell).ToArray())
                {
                    if (e.Type == NavEdgeType.Walk && level[e.To] < 0 && MathF.Abs(graph.Cell(e.To).FloorZ - graph.Cell(cell).FloorZ) <= 1f)
                    {
                        level[e.To] = next;
                        open.Enqueue(e.To);
                    }
                }
            }

            next++;
        }

        return level;
    }

    [JumpMapFact]
    public void ALedgeIsProposedOnceFromEachLevelOfGroundNotOnceACellPair()
    {
        // The yard's ledges have 289 cells each and the floor some 12,000: one candidate from one level stretch to another.
        Passed p = Shared.Value;
        int[] level = Levels(p.Graph);
        Assert.True(level.Max() + 1 < p.Graph.CellCount / 100, $"the control: {level.Max() + 1} level stretches over {p.Graph.CellCount} cells");
        Assert.True(p.Result.Candidates.Count > 0 || p.Settings.ReachShare * p.Reach.Rise < 132f, "the control: the pass proposes something on this map");
        Assert.Equal(p.Result.Candidates.Count, p.Result.Candidates.Select(c => (level[c.From], level[c.To])).Distinct().Count());
        Assert.All(p.Result.Candidates, c => Assert.NotEqual(level[c.From], level[c.To]));
    }
}
