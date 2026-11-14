using System.Globalization;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Objectives;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// What TF2's bots make of the experience store (lane G4a), the game thread's side:
/// <list type="bullet">
/// <item>Before the bots' commands run, each bot's route weight (its Cunning) goes into
/// <see cref="TFRouteWeights"/> for the navigation profile, whenever the bots change.</item>
/// <item><c>/ai/experience/use</c>: each bot's weight and how its look used the store (the decides on a learned
/// yaw and those that already faced it, the periods), and the routes published.</item>
/// <item><c>/ai/experience/routes</c>: for each team with a field, the route between each two of the level's
/// named places (each team's spawn and its flag's home) as a bot's flow field takes it with the field and
/// without, how long each is to walk and whether they differ. Worked on a task, never on the game's thread:
/// the first ask starts it, a later one reads it.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class TFExperienceUse(AiBots bots, TFRouteWeights weights, ExperienceRouteSlot routes, IAiNavigation nav, ServerAiSession ai,
                                    ITFLevelEntities placed, ITFObjectives objectives)
    : IFrameLifecycle, IDebugRouteProvider, IOrderedHook
{
    /// <summary>The units a second of a route's walk is: a Soldier's run, as the links are weighed (<see cref="NavLinkCosts.RunSpeed"/>).</summary>
    public const float RunSpeed = 240f;

    /// <summary>The weight <c>/ai/experience/routes</c> builds with when it is not asked for one: Cunning 8.</summary>
    public const float DefaultWeight = 0.8f;

    public const string UseRoute = "/ai/experience/use", RoutesRoute = "/ai/experience/routes", FamiliesRoute = "/ai/experience/families";

    private int _botsVersion = -1;
    private int _objectivesVersion = -1;
    private readonly Dictionary<TFTeam, AiVector> _flagHomes = new();
    private Task<string>? _routesWork;
    private string _routesKey = "";
    private Task<string>? _familiesWork;

    /// <summary>Before the bots' commands (First + 500), which ask the profile for each bot's obstacles.</summary>
    public int FrameOrder => ModStages.First + 495;

    /// <summary>Among the dev API's route providers: after the binding's own and the core's.</summary>
    public int HookOrder => ModStages.First + 925;

    public void OnPreThink(float frameTime)
    {
        NoteFlagHomes();
        if (bots.Version == _botsVersion)
            return;
        _botsVersion = bots.Version;
        weights.Set(TFRouteWeights.Table(bots.Bots, VisionPass.Slots));
    }

    /// <summary>
    /// Where each flag stands at home, kept as the HUD's rows change: a flag's row says where it is now, which is
    /// a carrier's back or a drop as often as its stand.
    /// </summary>
    private void NoteFlagHomes()
    {
        if (objectives.Version == _objectivesVersion)
            return;
        _objectivesVersion = objectives.Version;
        foreach (TFObjective o in objectives.All)
        {
            if (o.Kind == TFObjectiveKind.Flag && o.State == TFObjectiveState.Home)
                _flagHomes[o.Owner] = new AiVector(o.Position.X, o.Position.Y, o.Position.Z);
        }
    }

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", UseRoute, "what TF2's bots make of the experience store: each bot's route weight (Cunning / 10), its learned looks (decides on a learned yaw, those whose view already faced it, periods) its danger checks (bins with a danger place entered, checks drawn, decides on a check) its learned holds (picked, decides holding one), and the ways into each side's enemy flag room (each door's draws, tries and scores' sum), and the routes published with the snapshot (sides, cells costed, build cpu)"),
        new("GET", RoutesRoute, "for each team with a field of where its players died, the route between each two named places (each team's spawn and flag's home) with the field and without, in walked units and seconds at 240 u/s, at ?cunning=N (8 if not asked); the first ask starts the work on a task, later asks read it (?again=1 starts it over)"),
        new("GET", FamiliesRoute, "the ways into each flag's room as a bot finds them (RouteFamilies over the level's graph, from each flag's home): each door's bin and point, the room's cells, and the thread CPU the search took; the first ask starts the work on a task, later asks read it (?again=1 starts it over)"),
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
        => path == RoutesRoute ? new DebugRouteReply(200, RoutesJson(query))
            : path == FamiliesRoute ? new DebugRouteReply(200, FamiliesJson(query))
            : new DebugRouteReply(200, UseJson());

    /// <summary>The doors of each flag's room, found on a task as a decider finds them, with what the search cost in thread CPU.</summary>
    private string FamiliesJson(IReadOnlyDictionary<string, string> query)
    {
        if (_familiesWork is { } work && !query.ContainsKey("again"))
        {
            if (!work.IsCompleted)
                return "{\"state\":\"working\"}";
            return work.IsCompletedSuccessfully ? work.Result : AiJson.Write(new { state = "failed", error = work.Exception?.GetBaseException().Message ?? "" });
        }

        if (nav.Current?.Nav.Graph is not { } graph)
            return "{\"state\":\"no navigation\"}";
        (TFTeam Team, AiVector Home)[] homes = [.. _flagHomes.OrderBy(f => f.Key).Select(f => (f.Key, f.Value))];
        _familiesWork = Task.Run(() =>
        {
            Func<double>? clock = ThreadCpuClock.Micros();
            var rows = new List<object>();
            foreach ((TFTeam team, AiVector home) in homes)
            {
                double start = clock?.Invoke() ?? 0d;
                CellId cell = graph.FindCell(home + new AiVector(0f, 0f, 1f));
                RouteFamilies f = RouteFamilies.Around(graph, cell.IsValid ? cell.Index : -1);
                double us = (clock?.Invoke() ?? 0d) - start;
                rows.Add(new
                {
                    flag = TeamName((int)team), cell = cell.IsValid ? cell.Index : -1, room_cells = f.RoomCells, cpu_us = Math.Round(us, 0),
                    doors = f.Doors.Select(d => new { d.Index, key = d.Bin.Packed, bin = d.Bin.ToString(), x = Math.Round(d.At.X), y = Math.Round(d.At.Y), z = Math.Round(d.At.Z), reach = Math.Round(d.Reach) }).ToArray(),
                });
            }

            return AiJson.Write(new { state = "done", graph_cells = graph.CellCount, room_cost = RouteFamilies.RoomCost, flags = rows });
        });
        return "{\"state\":\"working\"}";
    }

    /// <summary>What one bot's decider made of the store, read under its brain's lock.</summary>
    private readonly record struct BotUse(int LookDecides, int LookFaced, int LookPeriods, int LookLearned, int DangerDecides, int DangerEntries,
                                          int DangerChecks, int DangerPlaces, int HoldPicks, int HoldDecides, int HoldSpots);

    private static BotUse Use(TFDecider d)
        => new(d.LearnedLookDecides, d.LearnedLookFaced, d.LearnedLooks.Periods, d.LearnedLooks.LearnedPeriods, d.DangerLookDecides, d.DangerLooks.Entries,
               d.DangerLooks.Checks, d.Dangers?.Count ?? 0, d.LearnedHoldPicks, d.LearnedHoldDecides, d.Holds?.All.Count ?? 0);

    /// <summary>What one bot's runs at the enemy's flag came to: its side, the doors it found, its draws of each, its runs and its tries.</summary>
    private readonly record struct FamilyUse(int Team, RouteDoor[] Doors, int[] Draws, int Runs, FamilyTry[] Tries);

    private static FamilyUse Families(TFDecider d)
        => new(d.FamilyTeam, d.Families is { } f ? [.. f.Doors] : [], [.. d.FamilyDraws],
               d.FamilyRuns, [.. d.FamilyTries]);

    /// <summary>Per side and door (by its bin's key): where it is, the bots' draws of it, and the tries of it with their scores' sum.</summary>
    private sealed class DoorSum
    {
        public AiVector At;
        public int Draws, Tries, DrawnTries;
        public double Score;
    }

    private string UseJson()
    {
        var rows = new List<object>();
        int unread = 0, runs = 0;
        BotUse sum = default;
        var doors = new SortedDictionary<(int Team, long Key), DoorSum>();
        foreach (AiBot b in bots.Bots)
        {
            if (ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? Families(d) : default, out FamilyUse fam) && fam.Doors is not null)
            {
                runs += fam.Runs;
                foreach (RouteDoor door in fam.Doors)
                {
                    DoorSum at = Door(doors, fam.Team, door.Bin.Packed);
                    at.At = door.At;
                    at.Draws += door.Index < fam.Draws.Length ? fam.Draws[door.Index] : 0;
                }

                foreach (FamilyTry t in fam.Tries)
                {
                    DoorSum at = Door(doors, t.Team, t.Choice);
                    at.Tries++;
                    at.DrawnTries += t.Drawn ? 1 : 0;
                    at.Score += t.Score;
                }
            }

            bool read = ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? Use(d) : default, out BotUse u);
            unread += read ? 0 : 1;
            sum = new BotUse(sum.LookDecides + u.LookDecides, sum.LookFaced + u.LookFaced, sum.LookPeriods + u.LookPeriods, sum.LookLearned + u.LookLearned,
                             sum.DangerDecides + u.DangerDecides, sum.DangerEntries + u.DangerEntries, sum.DangerChecks + u.DangerChecks,
                             Math.Max(sum.DangerPlaces, u.DangerPlaces), sum.HoldPicks + u.HoldPicks, sum.HoldDecides + u.HoldDecides,
                             Math.Max(sum.HoldSpots, u.HoldSpots));
            rows.Add(new
            {
                slot = b.Id.Slot, name = b.Name, weight = weights.Of(b.Id.Slot), read,
                look_decides = u.LookDecides, look_faced = u.LookFaced, look_periods = u.LookPeriods, look_learned_periods = u.LookLearned,
                danger_decides = u.DangerDecides, danger_entries = u.DangerEntries, danger_checks = u.DangerChecks, danger_places = u.DangerPlaces,
                hold_picks = u.HoldPicks, hold_decides = u.HoldDecides, hold_spots = u.HoldSpots,
            });
        }

        ExperienceRoutes r = routes.Current;
        return AiJson.Write(new
        {
            routes = new { snapshot = r.Of.ContentHash.ToString("x16", CultureInfo.InvariantCulture), sides = r.Roles, cells = r.Costed, bytes = r.Bytes, build_us = Math.Round(r.BuildMicros, 1) },
            looks = new { decides = sum.LookDecides, faced = sum.LookFaced, periods = sum.LookPeriods, learned_periods = sum.LookLearned, bots_unread = unread },
            dangers = new { decides = sum.DangerDecides, entries = sum.DangerEntries, checks = sum.DangerChecks, most_places = sum.DangerPlaces },
            holds = new { picks = sum.HoldPicks, decides = sum.HoldDecides, most_spots = sum.HoldSpots },
            families = new
            {
                runs,
                doors = doors.Select(d => new
                {
                    team = d.Key.Team, key = d.Key.Key, bin = WorldBin.Unpack(d.Key.Key).ToString(), x = Math.Round(d.Value.At.X), y = Math.Round(d.Value.At.Y),
                    z = Math.Round(d.Value.At.Z), draws = d.Value.Draws, tries = d.Value.Tries, drawn_tries = d.Value.DrawnTries, score = Math.Round(d.Value.Score, 2),
                }).ToArray(),
            },
            bots = rows,
        });
    }

    private static DoorSum Door(SortedDictionary<(int Team, long Key), DoorSum> doors, int team, long key)
    {
        if (!doors.TryGetValue((team, key), out DoorSum? at))
            doors[(team, key)] = at = new DoorSum();
        return at;
    }

    /// <summary>A named place the routes run between, and its cell.</summary>
    private readonly record struct Place(string Name, AiVector At, int Cell);

    private string RoutesJson(IReadOnlyDictionary<string, string> query)
    {
        float weight = query.TryGetValue("cunning", out string? c) && float.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out float level)
            ? Math.Clamp(level, 0f, StandardSkills.Cap) / StandardSkills.Cap
            : DefaultWeight;
        string key = weight.ToString("R", CultureInfo.InvariantCulture);
        if (_routesWork is { } work && key == _routesKey && !query.ContainsKey("again"))
        {
            if (!work.IsCompleted)
                return "{\"state\":\"working\"}";
            return work.IsCompletedSuccessfully ? work.Result : AiJson.Write(new { state = "failed", error = work.Exception?.GetBaseException().Message ?? "" });
        }

        if (nav.Current?.Nav.Graph is not { } graph)
            return "{\"state\":\"no navigation\"}";
        ExperienceRoutes published = routes.Current;
        if (published.Roles.Count == 0 || !ReferenceEquals(published.Graph, graph))
            return "{\"state\":\"no field: the store is off, empty of deaths, or its routes are of another graph\"}";

        // On the game's thread, only what reads the game: the places, and a bot's own obstacles per team.
        Place[] places = Places(graph);
        var states = new Dictionary<int, NavObstacleState>();
        foreach (int team in published.Roles)
            states[team] = TeamState(published.For(team)!, graph);
        _routesKey = key;
        _routesWork = Task.Run(() => Compare(graph, published, places, states, weight));
        return "{\"state\":\"working\"}";
    }

    /// <summary>
    /// The obstacles of a bot whose searches mind <paramref name="hazard"/> (so of its team: its team's solids, its
    /// links) as the profile fills them, cloned; a plain state with doors open without one.
    /// </summary>
    private NavObstacleState TeamState(NavCellHazard hazard, NavGraph graph)
    {
        foreach (AiBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p != EntityHandle.None && nav.ObstaclesFor(p) is { } state && ReferenceEquals(state.Hazard, hazard))
                return state.Clone();
        }

        return new NavObstacleState(graph.ObstacleCount) { OpenDoors = true };
    }

    /// <summary>
    /// Each team's first spawn and each flag's home, where they stand on the graph. Not the capture zones: the HUD's
    /// rows give them no place (they read the origin).
    /// </summary>
    private Place[] Places(NavGraph graph)
    {
        var places = new List<Place>();
        void Add(string name, AiVector at)
        {
            CellId cell = NavPoints.CellNear(graph, at, ClearanceClass.Stand);
            if (cell.IsValid && places.All(p => p.Name != name))
                places.Add(new Place(name, at, cell.Index));
        }

        foreach (MapEntity spawn in placed.Read().All.Where(e => e.ClassName == "info_player_teamspawn"))
        {
            if (spawn.Team is (int)TFTeam.Red or (int)TFTeam.Blue)
                Add($"{TeamName(spawn.Team)} spawn", spawn.Origin);
        }

        foreach ((TFTeam team, AiVector home) in _flagHomes.OrderBy(f => f.Key))
            Add($"{TeamName((int)team)} flag", home);

        return [.. places];
    }

    private static string TeamName(int team) => team == (int)TFTeam.Red ? "red" : team == (int)TFTeam.Blue ? "blue" : $"team {team}";

    /// <summary>The routes, on a task: per team and destination two fields (with the team's hazard at <paramref name="weight"/>, and without), walked from every other place.</summary>
    private static string Compare(NavGraph graph, ExperienceRoutes published, Place[] places, Dictionary<int, NavObstacleState> states, float weight)
    {
        var rows = new List<object>();
        var with = new FlowField();
        var without = new FlowField();
        int differ = 0;
        double bestSeconds = 0d;
        foreach (int team in published.Roles)
        {
            NavObstacleState plain = states[team].Clone();
            (plain.Hazard, plain.HazardWeight) = (null, 0f);
            NavObstacleState minding = states[team].Clone();
            (minding.Hazard, minding.HazardWeight) = (published.For(team), weight);
            foreach (Place to in places)
            {
                without.Build(graph, new CellId(to.Cell), ClearanceClass.Stand, plain);
                with.Build(graph, new CellId(to.Cell), ClearanceClass.Stand, minding);
                foreach (Place from in places.Where(p => p.Name != to.Name))
                {
                    List<int> a = Walk(without, from.Cell), b = Walk(with, from.Cell);
                    if (a.Count == 0 || b.Count == 0)
                        continue;
                    float ua = Length(graph, a), ub = Length(graph, b);
                    bool changed = !a.SequenceEqual(b);
                    differ += changed ? 1 : 0;
                    bestSeconds = Math.Max(bestSeconds, changed ? (ub - ua) / RunSpeed : 0d);
                    var onlyA = new HashSet<int>(a);
                    onlyA.ExceptWith(b);
                    rows.Add(new
                    {
                        team = TeamName(team), from = from.Name, to = to.Name, differs = changed,
                        walk_u = Math.Round(ua, 0), walk_s = Math.Round(ua / RunSpeed, 2),
                        minding_u = Math.Round(ub, 0), minding_s = Math.Round(ub / RunSpeed, 2),
                        extra_s = Math.Round((ub - ua) / RunSpeed, 2), cells_left_out = onlyA.Count,
                        cost_without = Math.Round(without.CostAt(from.Cell), 0), cost_with = Math.Round(with.CostAt(from.Cell), 0),
                    });
                }
            }
        }

        return AiJson.Write(new
        {
            state = "done", weight, run_speed = RunSpeed, places = places.Select(p => new { p.Name, x = p.At.X, y = p.At.Y, z = p.At.Z }).ToArray(),
            routes = rows.Count, differ, most_extra_s = Math.Round(bestSeconds, 2), rows,
        });
    }

    private static List<int> Walk(FlowField f, int from)
    {
        var cells = new List<int>();
        if (!float.IsFinite(f.CostAt(from)))
            return cells;
        cells.Add(from);
        for (CellId next = f.NextFrom(from); next.IsValid && cells.Count < 100_000; next = f.NextFrom(next.Index))
            cells.Add(next.Index);
        return cells;
    }

    private static float Length(NavGraph graph, List<int> cells)
    {
        float u = 0f;
        for (int i = 1; i < cells.Count; i++)
            u += graph.Cell(cells[i - 1]).Centre.DistanceTo(graph.Cell(cells[i]).Centre);
        return u;
    }
}
