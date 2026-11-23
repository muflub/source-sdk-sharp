using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.Replay;

/// <summary>
/// <c>ai-replay jumps ... heals</c> (lane G9b, step 1): where a map's blast links land against where its health is, and what
/// jumps would land by health. Four tables: the heal places (each pack and cabinet, the cells within a walk of it); each link's
/// landing with the walk on foot from it to the nearest pack and the nearest cabinet; the proposals the place-finding pass
/// makes when no walk is too short to be worth a jump, those that land within a walk of a heal place, each searched; and
/// the stretch planner asked along the routes from the CTF places to each heal place, what it answered.
/// </summary>
public static class JumpHealsTool
{
    /// <summary>How far on foot the nearest pack or cabinet is looked for from a landing.</summary>
    public const float FarWalk = 8000f;

    /// <summary>The stretch planner is asked along the last this many units of a route to a heal place, every <see cref="AskEvery"/>.</summary>
    public const float AskAlong = 2400f, AskEvery = 96f;

    /// <summary>The bot's heading on a route: towards the route's point this far ahead.</summary>
    public const float HeadingAhead = 128f;

    private static string V(AiVector v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F0} {v.Y:F0} {v.Z:F0})");

    private static string V(Vector v) => V(new AiVector(v.X, v.Y, v.Z));

    private static string Name(in JumpHealPlace p) => p.Cabinet ? $"cabinet t{p.Team}" : p.Kind.ToString();

    /// <summary>The walk on foot (the graph's edges, every door open) from <paramref name="from"/> to each cell, up to <paramref name="most"/>.</summary>
    public static Dictionary<int, float> WalkFrom(NavGraph graph, int from, float most)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var obstacles = new NavObstacleState(graph.ObstacleCount) { OpenDoors = true };
        var near = new Dictionary<int, float> { [from] = 0f };
        var open = new PriorityQueue<int, float>();
        open.Enqueue(from, 0f);
        while (open.TryDequeue(out int cell, out float cost))
        {
            if (cost > near[cell])
                continue;
            foreach (NavEdge e in graph.EdgesFrom(cell))
            {
                if (!graph.Passable(e, ClearanceClass.Stand, obstacles))
                    continue;
                float c = cost + e.Cost;
                if (c > most || (near.TryGetValue(e.To, out float had) && had <= c))
                    continue;
                near[e.To] = c;
                open.Enqueue(e.To, c);
            }
        }

        return near;
    }

    /// <summary>The tables (see the class).</summary>
    public static string Text(JumpPlacesReport report, byte[] bsp, string soldierScript)
    {
        ArgumentNullException.ThrowIfNull(report);
        NavGraph g = report.Graph;
        MapEntities entities = JumpRoutesTool.ReadEntities(bsp);
        JumpHealPlace[] places = JumpHeals.PlacesOf(entities);
        JumpHeals heals = JumpHeals.Of(g, places);
        var s = new StringBuilder();
        var cells = new int[places.Length];
        s.Append(CultureInfo.InvariantCulture, $"  heals       {places.Length} places ({places.Count(p => !p.Cabinet)} packs, {places.Count(p => p.Cabinet)} cabinets); walk {JumpHealthRule.HealWalk:F0} u\n");
        for (int p = 0; p < places.Length; p++)
        {
            CellId at = NavPoints.CellNear(g, places[p].At, ClearanceClass.Stand);
            cells[p] = at.IsValid ? at.Index : -1;
            s.Append(CultureInfo.InvariantCulture, $"    place #{p,-2} {Name(places[p]),-13} {V(places[p].At)} cell {cells[p]} floor {(at.IsValid ? g.Cell(at).FloorZ : float.NaN):F0}; {heals.CellsNear(p)} cells within the walk\n");
        }

        TFClassTable classes = OfflineTF2.Classes(soldierScript);
        BlastJumpSims sims = OfflineTF2.Sims(classes);
        JumpLauncher launcher = JumpLauncher.Rocket(OfflineTF2.RocketDamage, OfflineTF2.RocketRefire);
        float run = classes[TFClass.Soldier].MaxSpeed;
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, run, BlastJumpSims.TickSeconds, TFJumps.PlannerTurn);
        var search = new JumpSearchSettings();
        GraphJumpWalk gw = GraphJumpWalk.Of(g, run);
        var jumpSearch = new JumpSearch(new JumpFlier(sims.Create(report.Columns, launcher), limits), gw, search);

        // Each link's landing: the nearest pack and cabinet on foot; what a search spends on it and what a route weighs it at
        // with the switch on (a Soldier's 200 against the floor), each link's plan flown once for its fall.
        IReadOnlyList<JumpLink> links = report.Result.Links;
        var costs = new NavLinkCosts { RunSpeed = run };
        JumpLinkSet set = JumpLinkSet.Of(g, links, costs);
        JumpPlan[] linkPlans = [.. Enumerable.Range(0, links.Count).Select(i => set.PlanOf(i, jumpSearch.Fly(set.JumpOf(i).Recipe)))];
        float[] spends = heals.Spends(set, linkPlans, SourceSharp.Ai.TF2.Trees.TFDecider.SoldierHealth, SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor);
        float[] weights = heals.Weights(set, linkPlans, SourceSharp.Ai.TF2.Trees.TFDecider.SoldierHealth, costs);
        JumpLinkSet weighed = set.WithSpends(g, spends, costs, weights);
        s.Append(CultureInfo.InvariantCulture, $"  links' landings against the heal places ({links.Count} links):\n");
        for (int i = 0; i < links.Count; i++)
        {
            JumpLink l = links[i];
            Dictionary<int, float> walk = WalkFrom(g, l.To, FarWalk);
            (int Place, float Walk) pack = (-1, float.PositiveInfinity), cabinet = (-1, float.PositiveInfinity);
            for (int p = 0; p < places.Length; p++)
            {
                float w = cells[p] >= 0 && walk.TryGetValue(cells[p], out float had) ? had : float.PositiveInfinity;
                if (!places[p].Cabinet && w < pack.Walk)
                    pack = (p, w);
                if (places[p].Cabinet && w < cabinet.Walk)
                    cabinet = (p, w);
            }

            string W((int Place, float Walk) x) => x.Place < 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"#{x.Place} {x.Walk:F0} u");
            s.Append(CultureInfo.InvariantCulture,
                $"    link #{i,-2} {l.Family,-7} {V(l.Recipe.Takeoff)} -> {V(l.Recipe.Landing)} health {l.Health:F1}; nearest pack {W(pack)}, cabinet {W(cabinet)}; within the walk: {heals.NearCell(l.To).Length}; spend {spends[i]:F1}, weighed at {weights[i]:F1}, a route's cost {set.Links.CostOf(i) / run:F2} s -> {weighed.Links.CostOf(i) / run:F2} s\n");

            // From the link's start to each place by its landing: on foot, and with the weighed links for a Soldier at 100 and at 200.
            foreach (JumpHeal h in heals.NearCell(l.To))
            {
                CellId start = NavPoints.CellNear(g, new AiVector(set.Links[i].Start.X, set.Links[i].Start.Y, set.Links[i].Start.Z), ClearanceClass.Stand);
                if (!start.IsValid || cells[h.Place] < 0)
                    continue;
                var a = new RoutePlace($"link #{i} start", g.Cell(start).Centre, start.Index);
                var b = new RoutePlace($"place #{h.Place}", places[h.Place].At, cells[h.Place]);
                RouteChange at100 = JumpRoutesTool.Route(g, weighed, a, b, run, 100f - SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor);
                RouteChange at200 = JumpRoutesTool.Route(g, weighed, a, b, run, 200f - SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor);
                string S(float? x) => x is { } v ? v.ToString("F2", CultureInfo.InvariantCulture) : "none";
                s.Append(CultureInfo.InvariantCulture,
                    $"      to place #{h.Place} ({h.Walk:F0} u from the landing): on foot {S(at100.WalkSeconds)} s; with links at 100 {S(at100.LinkSeconds)} s by [{string.Join(",", at100.Links)}], at 200 {S(at200.LinkSeconds)} s by [{string.Join(",", at200.Links)}]\n");
            }
        }

        // The pass's proposals with no walk too short to jump (the walk factor 0), landing within the walk of a heal place.
        IReadOnlyList<int> seeds = JumpPlacesTool.Seeds(entities, g);
        var all = new JumpPlaces(g, report.Columns, jumpSearch, gw, report.Reach, JumpPlacesSettings.Of(TFNavProfile.ModNumbers) with { WalkFactor = 0f }, seeds);
        IReadOnlyList<JumpCandidate> proposed = all.Propose();
        var byHeal = proposed.Where(c => !heals.NearCell(c.To).IsEmpty).ToList();
        s.Append(CultureInfo.InvariantCulture, $"  proposals with any walk (walk factor 0): {proposed.Count}, {byHeal.Count} landing within the walk of a heal place; each searched:\n");
        foreach (JumpCandidate c in byHeal)
        {
            float straight = MathF.Sqrt((c.Distance * c.Distance) + (c.Rise * c.Rise));
            JumpSearchResult found = jumpSearch.Find(new JumpAsk(c.Takeoff, c.Landing, 24f, c.Families));
            string got = found.Found is { } f
                ? string.Create(CultureInfo.InvariantCulture, $"found {f.Family} robust {f.Robustness:F2} {f.Seconds:F2} s (walk {(f.WalkSeconds is { } ws ? ws.ToString("F2", CultureInfo.InvariantCulture) : "none")} s) health {f.Health:F1}{(f.Robustness >= search.Keep ? "" : " (under keep)")}")
                : "no recipe";
            JumpHeal near = heals.NearCell(c.To)[0];
            s.Append(CultureInfo.InvariantCulture,
                $"    {c.Kind,-3} {V(c.Takeoff)} -> {V(c.Landing)} rise {c.Rise:F0} dist {c.Distance:F0} walk {(c.WalkCost is { } w ? $"{w:F0} ({w / straight:F1}x)" : "none")}; place #{near.Place} {near.Walk:F0} u; {got}\n");
        }

        // The stretch planner along the routes from the CTF places to each heal place.
        IReadOnlyList<RoutePlace> from = JumpRoutesTool.CtfPlaces(entities, g);
        JumpPlanner planner = JumpPlanner.Of(g, sims.Create(report.Columns, launcher), sims.Create(JumpEnvelope.OpenAir(), launcher), limits, search, new JumpPlanSettings());
        var finder = new NavPathfinder();
        var path = new List<CellId>();
        var walker = new NavObstacleState(g.ObstacleCount) { OpenDoors = true };
        var refusals = new int[Enum.GetValues<JumpRefusal>().Length];
        int asks = 0;
        var plans = new List<string>();
        for (int p = 0; p < places.Length; p++)
        {
            if (cells[p] < 0)
                continue;
            foreach (RoutePlace a in from)
            {
                if (finder.Find(g, new CellId(a.Cell), new CellId(cells[p]), ClearanceClass.Stand, walker, path, g.CellCount, out float total) != PathStatus.Found)
                    continue;
                // Along the route, its length so far at each cell.
                var pts = path.Select(c => g.Cell(c).Centre with { Z = g.Cell(c).FloorZ }).ToList();
                var along = new float[pts.Count];
                for (int k = 1; k < pts.Count; k++)
                    along[k] = along[k - 1] + pts[k].DistanceTo(pts[k - 1]);
                float length = along[^1], next = MathF.Max(0f, length - AskAlong);
                for (int k = 0; k < pts.Count - 1; k++)
                {
                    if (along[k] < next)
                        continue;
                    next = along[k] + AskEvery;
                    int ahead = k;
                    while (ahead < pts.Count - 1 && along[ahead] - along[k] < HeadingAhead)
                        ahead++;
                    AiVector d = pts[ahead] - pts[k];
                    var heading = new AiVector(d.X, d.Y, 0f);
                    if (heading.Length < 1f)
                        continue;
                    asks++;
                    JumpPlanned planned = planner.Plan(new JumpStretchAsk(pts[k], heading.Normalised(), places[p].At, new Tick(1)));
                    refusals[(int)planned.Refusal]++;
                    if (planned.Plan is { } plan)
                    {
                        var touch = new AiVector(plan.Touch.X, plan.Touch.Y, plan.Touch.Z);
                        System.Collections.Immutable.ImmutableArray<JumpHeal> by = heals.Near(touch);
                        float toPlace = by.FirstOrDefault(h => h.Place == p) is { Walk: var tw } hp && hp.Place == p ? tw : float.NaN;
                        plans.Add(string.Create(CultureInfo.InvariantCulture,
                            $"    plan to #{p} from {a.Name}, {length - along[k]:F0} u before it: {V(plan.Recipe.Takeoff)} -> touch {V(touch)} {plan.Seconds:F2} s against the walk's {plan.WalkSeconds:F2} s, health {plan.Health:F1}, robust {plan.Robustness:F2}; the place {(float.IsNaN(toPlace) ? "not within the walk" : $"{toPlace:F0} u on foot")} of the touch"));
                    }
                }
            }
        }

        s.Append(CultureInfo.InvariantCulture, $"  stretches toward the heal places: {asks} asks along the last {AskAlong:F0} u of the routes from {from.Count} CTF places, every {AskEvery:F0} u; answers:");
        foreach (JumpRefusal why in Enum.GetValues<JumpRefusal>())
            s.Append(CultureInfo.InvariantCulture, $" {(why == JumpRefusal.None ? "planned" : why.ToString().ToLowerInvariant())} {refusals[(int)why]};");
        s.Append('\n');
        foreach (string line in plans)
            s.Append(line).Append('\n');
        return s.ToString();
    }
}
