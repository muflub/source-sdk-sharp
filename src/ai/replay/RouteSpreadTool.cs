using System.Globalization;
using System.Text;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using Planned = SourceSharp.Ai.TF2.Nav.TFRouteCorridors.Planned;

namespace SourceSharp.Ai.Replay;

/// <summary>
/// <c>ai-replay spread</c> (lane Groutes): the routes the bots' searches plan from each team's first spawn room to the point
/// in play (a flag map's other team's flag, else the control point in the middle), offline on the map's TF2 navigation with
/// each team's respawn room solids and every obstacle open (live, those a bot knows shut stay shut, so the game can find
/// fewer corridors). The team's corridors are found as the game finds them (<see cref="TFRouteCorridors"/>,
/// from the room's first spawn); then for bots of seed index 1 to 8, each in lives 1 to <c>lives</c> (each life a wave of
/// the eight leaving together, spread over the corridors as the game spreads bots arriving together), starting at the room's
/// spawns in turn as bots come out of them, the route with no field (every bot's at base) and the route minding the
/// corridor that life draws (<see cref="TFRouteVariation.Choose"/>, at a Teamwork). It says how the routes spread (the live
/// route's measure, <see cref="TFRouteSpread"/>, over the planned ways), what each costs against the way with no field from
/// the same spawn (the cap: none over 1.15 times), and how wide the room is across each band for ways within 1.05 and 1.15
/// times the shortest.
/// </summary>
public static class RouteSpreadTool
{
    /// <summary>The seed indexes the planned bots have, as a roster of eight adds them.</summary>
    public const int Bots = 8;

    /// <summary>Spawns this near a team's furthest one from its goal are its first room's.</summary>
    public const float RoomUnits = 1500f;

    /// <summary>The Teamwork the planned bots have unless asked: a Normal preset's (its mean level, 3).</summary>
    public const float NormalTeamwork = 0.3f;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 2)
        {
            error.WriteLine("ai-replay spread <map.nav3d> <map.bsp> [lives=3] [teamwork=0.3] [out.png]");
            return 2;
        }

        int lives = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 3;
        float teamwork = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : NormalTeamwork;
        string? png = args.Length > 4 ? args[4] : null;
        byte[] bsp = File.ReadAllBytes(args[1]);
        var source = new Nav3dNavSource(Nav3dReader.Open(File.ReadAllBytes(args[0])));
        MapEntities entities = JumpRoutesTool.ReadEntities(bsp);
        NavNumbers numbers = TFNavProfile.ModNumbers;
        NavGraph graph = NavGraphFromNav3d.Derive(source, numbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs).Graph;
        NavTeamBlockers blockers = NavTeamBlockers.From(graph, TFTeamSolids.Read(entities), numbers);
        var drawn = new List<(int Team, List<RouteWalk> Base, List<RouteWalk> Varied)>();
        output.Write(Text(Path.GetFileNameWithoutExtension(args[0]), graph, entities, blockers, lives, teamwork, out bool held, drawn));
        if (png is not null)
        {
            File.WriteAllBytes(png, Png(graph, drawn, 0.06f));
            output.WriteLine($"  the routes drawn: {png}");
        }

        return held ? 0 : 1;
    }

    /// <summary>The spawns of <paramref name="team"/> in its room furthest from <paramref name="goal"/>, on the graph.</summary>
    public static IReadOnlyList<int> FirstRoom(MapEntities entities, NavGraph graph, int team, AiVector goal)
    {
        ArgumentNullException.ThrowIfNull(entities);
        MapEntity[] spawns = [.. entities.All.Where(e => e.ClassName == "info_player_teamspawn" && e.Team == team)];
        if (spawns.Length == 0)
            return [];
        AiVector far = spawns.MaxBy(s => s.Origin.DistanceTo(goal)).Origin;
        return [.. spawns.Where(s => s.Origin.DistanceTo(far) <= RoomUnits).Select(s => NavPoints.CellNear(graph, s.Origin, ClearanceClass.Stand))
                         .Where(c => c.IsValid).Select(c => c.Index).Distinct()];
    }

    /// <summary>
    /// The point in play for <paramref name="team"/> as a level starts: the other team's flag on a flag map, else the control
    /// point nearest the middle between the teams' furthest spawns.
    /// </summary>
    public static AiVector? GoalOf(MapEntities entities, int team)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (entities.All.FirstOrDefault(e => e.ClassName == "item_teamflag" && e.Team != team && e.Team != 0) is { ClassName.Length: > 0 } flag)
            return flag.Origin;
        MapEntity[] points = [.. entities.All.Where(e => e.ClassName == "team_control_point")];
        MapEntity[] spawns = [.. entities.All.Where(e => e.ClassName == "info_player_teamspawn" && e.Team is 2 or 3)];
        if (points.Length == 0 || spawns.Length == 0)
            return null;
        AiVector centre = new(spawns.Average(s => s.Origin.X), spawns.Average(s => s.Origin.Y), spawns.Average(s => s.Origin.Z));
        return points.MinBy(p => p.Origin.DistanceTo(centre)).Origin;
    }

    public static string Text(string map, NavGraph graph, MapEntities entities, NavTeamBlockers blockers, int lives, float teamwork, out bool held,
                              List<(int Team, List<RouteWalk> Base, List<RouteWalk> Varied)>? drawn = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blockers);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"ai-replay spread {map}: {graph.CellCount} cells; {Bots} bots x {lives} lives a team at Teamwork {teamwork:F1}\n");
        // Live, a bot's obstacles hold what it knows shut (the map's layout as it starts); here every obstacle is open.
        text.Append("  every obstacle open (live, those a bot knows shut are not: on cp_granary the first rooms' next way to mid was 1.103-1.113 times live, 1.024-1.075 here)\n");
        held = true;
        var field = new FlowField();
        foreach (int team in (int[])[2, 3])
        {
            string name = team == 2 ? "red" : "blue";
            if (GoalOf(entities, team) is not { } goalAt || NavPoints.CellNear(graph, goalAt, ClearanceClass.Stand) is not { IsValid: true } goal)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {name}: no goal on the graph\n");
                continue;
            }

            IReadOnlyList<int> room = FirstRoom(entities, graph, team, goalAt);
            if (room.Count == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {name}: no spawn on the graph\n");
                continue;
            }

            var plain = new NavObstacleState(graph.ObstacleCount) { OpenDoors = true, TeamCells = blockers.For(team) };
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(graph, new CellId(room[0]), goal, plain);
            double findMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            text.Append(CultureInfo.InvariantCulture,
                        $"  {name}: {room.Count} spawn cell(s) to ({goalAt.X:F0} {goalAt.Y:F0} {goalAt.Z:F0}); corridors {corridors.Count}, their costs over the shortest [{string.Join(", ", corridors.Select(c => c.Ratio.ToString("F3", CultureInfo.InvariantCulture)))}], found in {findMs:F0} ms\n");
            if (TFRouteCorridors.Plan(graph, field, room[0], goal, plain, plain) is not null)
            {
                foreach (float within in (float[])[1.05f, TFRouteCorridors.Cap])
                    text.Append(CultureInfo.InvariantCulture, $"    cells on a way within {within:F2} times the shortest from spawn 1, per band (lanes 128 u apart: their width): {Lanes(graph, room[0], goal, plain, within)}\n");
            }

            var basePlans = new List<RouteWalk>();
            var variedPlans = new List<RouteWalk>();
            var ratios = new List<float>();
            var chosen = new int[Math.Max(1, corridors.Count)];
            int walk = 0, unreached = 0;
            // Each life is a wave: the team's bots of one life leave together, and each takes a corridor none of the wave
            // before it took while one is free (TFRouteVariation's together rule).
            for (int life = 1; life <= lives; life++)
            {
                var taken = new int[Math.Max(1, corridors.Count)];
                for (int bot = 1; bot <= Bots; bot++)
                {
                    int from = room[walk % room.Count];
                    walk++;
                    if (TFRouteCorridors.Plan(graph, field, from, goal, plain, plain) is not { } shortest)
                    {
                        unreached++;
                        continue;
                    }

                    NavObstacleState minding = plain;
                    if (corridors.Count >= 2)
                    {
                        int choice = TFRouteVariation.Choose(corridors, AiRandom.SeedFor(TFRouteVariation.SeedOf(bot, life), 0), teamwork, taken);
                        chosen[choice]++;
                        taken[choice]++;
                        minding = plain.Clone();
                        (minding.Hazard, minding.HazardWeight) = (TFRouteVariation.Compose(graph, corridors[choice].Extra, null, 0f), 1f);
                    }

                    if (TFRouteCorridors.Plan(graph, field, from, goal, minding, plain) is not { } varied)
                    {
                        unreached++;
                        continue;
                    }

                    basePlans.Add(Walk(bot, life, team, goalAt, shortest, walk));
                    variedPlans.Add(Walk(bot, life, team, goalAt, varied, walk));
                    ratios.Add(varied.Cost / shortest.Cost);
                }
            }

            float worst = ratios.Count == 0 ? 0f : ratios.Max();
            held &= worst <= TFRouteCorridors.Cap + 1e-4f;
            text.Append(CultureInfo.InvariantCulture, $"    {ratios.Count} routes, {unreached} with none; lives drew the corridors {string.Join("/", chosen)}\n");
            if (ratios.Count == 0)
                continue;
            text.Append(CultureInfo.InvariantCulture,
                        $"    cost against no field from the same spawn: {JumpPlacesTool.Spread(ratios, "F3")}; over the cap: {ratios.Count(r => r > TFRouteCorridors.Cap + 1e-4f)}\n");
            drawn?.Add((team, basePlans, variedPlans));
            Line(text, "no field", TFRouteSpread.MeasureGroup(basePlans, 1f));
            Line(text, "field   ", TFRouteSpread.MeasureGroup(variedPlans, 1f));
        }

        return text.ToString();
    }

    /// <summary>
    /// The cells on some way from <paramref name="from"/> to <paramref name="goal"/> within <paramref name="within"/> times the
    /// shortest (the cost from the start to the cell and from it to the goal), by band along the line from the start to the
    /// goal: in each middle band, the lanes they make across it (runs of cells no more than 128 u apart sideways) and each
    /// one's width.
    /// </summary>
    public static string Lanes(NavGraph graph, int from, CellId goal, NavObstacleState plain, float within)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var toGoal = new FlowField();
        toGoal.Build(graph, goal, ClearanceClass.Stand, plain);
        var toStart = new FlowField();
        toStart.Build(graph, new CellId(from), ClearanceClass.Stand, plain);
        float best = toGoal.CostAt(from);
        AiVector start = graph.Cell(from).Centre, end = graph.Cell(goal).Centre;
        AiVector axis = TFRouteSpread.Flat(end - start);
        float length = axis.Length2D, ux = axis.X / length, uy = axis.Y / length;
        int last = (int)MathF.Floor(length / TFRouteSpread.BandUnits);
        var byBand = new SortedDictionary<int, List<float>>();
        for (int c = 0; c < graph.CellCount; c++)
        {
            float via = toGoal.CostAt(c) + toStart.CostAt(c);
            if (!(via <= best * within))
                continue;
            AiVector at = graph.Cell(c).Centre;
            float dx = at.X - start.X, dy = at.Y - start.Y;
            int band = (int)MathF.Floor(((dx * ux) + (dy * uy)) / TFRouteSpread.BandUnits);
            if (band < 1 || band >= last)
                continue;
            if (!byBand.TryGetValue(band, out List<float>? sides))
                byBand[band] = sides = [];
            sides.Add((ux * dy) - (uy * dx));
        }

        var parts = new List<string>();
        foreach ((int band, List<float> sides) in byBand)
        {
            sides.Sort();
            var lanes = new List<float>();
            float low = sides[0], high = sides[0];
            foreach (float v in sides.Skip(1))
            {
                if (v - high > 128f)
                {
                    lanes.Add(high - low);
                    low = v;
                }

                high = v;
            }

            lanes.Add(high - low);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{band}:[{string.Join(' ', lanes.Select(w => w.ToString("F0", CultureInfo.InvariantCulture)))}]"));
        }

        return string.Join("  ", parts);
    }

    /// <summary>The graph from above (cells grey by height), each team's routes with no field in white and with each bot's field in its team's colour.</summary>
    public static byte[] Png(NavGraph g, IReadOnlyList<(int Team, List<RouteWalk> Base, List<RouteWalk> Varied)> routes, float pixelsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(routes);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, lo = float.MaxValue, hi = float.MinValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            (minX, minY, maxX, maxY) = (MathF.Min(minX, cell.MinX), MathF.Min(minY, cell.MinY), MathF.Max(maxX, cell.MaxX), MathF.Max(maxY, cell.MaxY));
            (lo, hi) = (MathF.Min(lo, cell.FloorZ), MathF.Max(hi, cell.FloorZ));
        }

        int width = (int)MathF.Ceiling((maxX - minX) * pixelsPerUnit) + (2 * NavPng.Margin), height = (int)MathF.Ceiling((maxY - minY) * pixelsPerUnit) + (2 * NavPng.Margin);
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor(16, 18, 23));
        float X(float x) => NavPng.Margin + ((x - minX) * pixelsPerUnit);
        float Y(float y) => height - NavPng.Margin - ((y - minY) * pixelsPerUnit);
        using var fill = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill };
        foreach (int c in Enumerable.Range(0, g.CellCount).OrderBy(c => g.Cell(c).FloorZ))
        {
            NavCell cell = g.Cell(c);
            byte v = (byte)(50 + (110 * (hi > lo ? (cell.FloorZ - lo) / (hi - lo) : 1f)));
            fill.Color = new SkiaSharp.SKColor(v, v, v);
            canvas.DrawRect(SkiaSharp.SKRect.Create(X(cell.MinX), Y(cell.MaxY), (cell.MaxX - cell.MinX) * pixelsPerUnit, (cell.MaxY - cell.MinY) * pixelsPerUnit), fill);
        }

        using var line = new SkiaSharp.SKPaint { IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2f };
        foreach ((int team, List<RouteWalk> plain, List<RouteWalk> varied) in routes)
        {
            line.Color = team == 2 ? new SkiaSharp.SKColor(255, 80, 60, 150) : new SkiaSharp.SKColor(70, 150, 255, 150);
            foreach (RouteWalk w in varied)
                Draw(w);
            line.Color = new SkiaSharp.SKColor(255, 255, 255, 220);
            foreach (RouteWalk w in plain)
                Draw(w);
        }

        void Draw(RouteWalk w)
        {
            for (int i = 1; i < w.Points.Count; i++)
                canvas.DrawLine(X(w.Points[i - 1].X), Y(w.Points[i - 1].Y), X(w.Points[i].X), Y(w.Points[i].Y), line);
        }

        canvas.Flush();
        using SkiaSharp.SKData data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static RouteWalk Walk(int bot, int life, int team, AiVector goal, Planned plan, int tick)
    {
        var w = new RouteWalk(bot, life, team, 0, goal, tick, plan.Points[0]);
        for (int i = 1; i < plan.Points.Count; i++)
            w.Add(plan.Points[i], tick + i);
        w.ArriveTick = tick + plan.Points.Count;
        w.Open = false;
        return w;
    }

    private static void Line(StringBuilder text, string what, RouteGroup g)
    {
        RouteBand[] middle = [.. g.Middle];
        string sd = middle.Length == 0 ? "none" : string.Join(' ', middle.Select(b => b.Sd.ToString("F0", CultureInfo.InvariantCulture)));
        text.Append(CultureInfo.InvariantCulture,
                    $"    {what}: corridors [{string.Join(", ", g.Corridors)}] (largest {g.Corridors[0] / (double)g.Reached:P0}); middle bands' sideways sd median {TFRouteSpread.Median(middle.Select(b => b.Sd)):F0} u, max {(middle.Length == 0 ? 0f : middle.Max(b => b.Sd)):F0}: {sd}\n");
    }
}
