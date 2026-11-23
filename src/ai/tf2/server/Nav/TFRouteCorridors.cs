using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// One of the near-equal ways from a place to a goal (lane Groutes): its cost against the shortest (<see cref="Ratio"/>,
/// from 1 to <see cref="TFRouteCorridors.Cap"/>), its cells' centres from the start, and the field that keeps a search on
/// it: each cell's share of a step it adds, 0 within <see cref="TFRouteCorridors.Reach"/> of the way and
/// <see cref="TFRouteVariation.MaxExtra"/> everywhere else.
/// </summary>
public sealed record RouteCorridor(float Ratio, IReadOnlyList<AiVector> Points, float[] Extra)
{
    /// <summary>How long the way is, along its points.</summary>
    public float Length { get; } = Enumerable.Range(1, Math.Max(0, Points.Count - 1)).Sum(i => Points[i].DistanceTo(Points[i - 1]));

    /// <summary>How long a bot takes to walk it, at <see cref="TFRouteVariation.RunSpeed"/>.</summary>
    public float Seconds => Length / TFRouteVariation.RunSpeed;
}

/// <summary>
/// The distinct ways from a place to a goal that cost no more than <see cref="Cap"/> times the shortest (lane Groutes), found
/// as a person sees them: the shortest; then, with the cells along every way found so far made dearer, the shortest of the
/// rest, kept when it costs no more than the cap and most of it runs away from the ways already kept (a way that only
/// steps round a corner of another is that one). A search that minds a corridor's field (<see cref="RouteCorridor.Extra"/>)
/// keeps to the corridor whenever leaving it would not save more than the field adds off it, and no search pays more than
/// 1.15 times a step.
/// </summary>
public static class TFRouteCorridors
{
    /// <summary>The most a corridor costs over the shortest: the ruling's 1.15 times (the user, 2026-11-16, raised 2026-11-20).</summary>
    public const float Cap = 1f + TFRouteVariation.MaxExtra;

    /// <summary>How near a way a cell is in its corridor, across: a hall's half-width and a body's.</summary>
    public const float Reach = 192f;

    /// <summary>How far above or below a way's point a cell is still in its corridor: a floor apart is another way.</summary>
    public const float ReachUp = 120f;

    /// <summary>The share of a way that must run outside every corridor kept before it is another corridor.</summary>
    public const float OwnShare = 0.3f;

    /// <summary>The most corridors a goal keeps, and the most searches made for them.</summary>
    public const int MaxCorridors = 4, MaxSearches = 8;

    /// <summary>
    /// How much dearer a search for the next corridor makes the cells of the ways found: half a step again. More than the
    /// cap, so the same way again costs more than a distinct one within it; little enough that the next way still shares the
    /// doors and the ends the ways all pass through, and leaves the found ones only where another way runs beside them.
    /// </summary>
    public const float FoundScale = 0.5f;

    /// <summary>
    /// The corridors from <paramref name="from"/> to <paramref name="goal"/> over <paramref name="obstacles"/> (its links and
    /// hazard left out: a walk), the shortest first, then each next one cheapest first; none when the goal cannot be reached.
    /// </summary>
    /// <param name="tries">When given, each search's way as found: its cost over the shortest and its share outside the corridors kept before it (-1: no way).</param>
    public static IReadOnlyList<RouteCorridor> Find(NavGraph graph, CellId from, CellId goal, NavObstacleState obstacles, CancellationToken cancel = default,
                                                    List<(float Ratio, float Own)>? tries = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(obstacles);
        NavObstacleState plain = Walker(obstacles);
        var field = new FlowField();
        var corridors = new List<RouteCorridor>();
        if (Plan(graph, field, from.Index, goal, plain, plain) is not { } shortest || shortest.Cost <= 0f)
            return corridors;
        corridors.Add(new RouteCorridor(1f, shortest.Points, Extra(graph, shortest.Points)));
        var found = new float[graph.CellCount];
        Mark(found, corridors[0].Extra);
        for (int search = 1; search < MaxSearches && corridors.Count < MaxCorridors; search++)
        {
            cancel.ThrowIfCancellationRequested();
            NavObstacleState away = plain.Clone();
            (away.Hazard, away.HazardWeight) = (new NavCellHazard(graph, (float[])found.Clone()), 1f);
            if (Plan(graph, field, from.Index, goal, away, plain) is not { } next)
            {
                tries?.Add((-1f, -1f));
                break;
            }

            float ratio = next.Cost / shortest.Cost;
            float own = OwnShareOf(graph, next.Points, corridors);
            tries?.Add((ratio, own));
            float[] extra = Extra(graph, next.Points);
            Mark(found, extra);
            // A way over the cap is no corridor, but the next search steps round it too: a cheaper one may lie past it.
            if (ratio <= Cap && own >= OwnShare)
                corridors.Add(new RouteCorridor(ratio, next.Points, extra));
        }

        return corridors;
    }

    /// <summary>The walk's obstacles: <paramref name="obstacles"/>' doors and team's cells, every door open, no link, no hazard.</summary>
    public static NavObstacleState Walker(NavObstacleState obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        NavObstacleState plain = obstacles.Clone();
        plain.OpenDoors = true;
        plain.Reach = JumpReach.None;
        (plain.Links, plain.LinksShut, plain.LinkHealth) = (null, null, 0f);
        (plain.Hazard, plain.HazardWeight) = (null, 0f);
        return plain;
    }

    /// <summary>Makes every cell of a corridor (an <paramref name="extra"/> of 0) dearer by <see cref="FoundScale"/> in <paramref name="found"/>.</summary>
    private static void Mark(float[] found, float[] extra)
    {
        for (int c = 0; c < found.Length; c++)
        {
            if (extra[c] == 0f)
                found[c] = FoundScale;
        }
    }

    /// <summary>The share of <paramref name="points"/> outside every one of <paramref name="corridors"/>.</summary>
    private static float OwnShareOf(NavGraph graph, IReadOnlyList<AiVector> points, List<RouteCorridor> corridors)
    {
        var buckets = corridors.Select(c => Buckets(c.Points)).ToArray();
        int own = points.Count(p => !buckets.Any(b => Near(b, p)));
        return points.Count == 0 ? 0f : own / (float)points.Count;
    }

    /// <summary>A way planned: its cells' centres (<see cref="SampleUnits"/> apart at least, its ends kept) and its cost to a walker.</summary>
    public readonly record struct Planned(List<AiVector> Points, float Cost);

    /// <summary>A planned way's points are its cells' centres this far apart at least.</summary>
    public const float SampleUnits = 64f;

    /// <summary>
    /// The way a field over <paramref name="obstacles"/> to <paramref name="goal"/> takes from <paramref name="from"/>, and what
    /// it costs a walker over <paramref name="plain"/>; null when there is none.
    /// </summary>
    public static Planned? Plan(NavGraph graph, FlowField field, int from, CellId goal, NavObstacleState obstacles, NavObstacleState plain)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(field);
        field.Build(graph, goal, ClearanceClass.Stand, obstacles);
        if (!float.IsFinite(field.CostAt(from)))
            return null;
        var points = new List<AiVector> { graph.Cell(from).Centre };
        float cost = 0f;
        int at = from, steps = 0;
        while (at != goal.Index)
        {
            CellId next = field.NextFrom(at);
            if (!next.IsValid || ++steps > graph.CellCount)
                return null;
            cost += StepCost(graph, at, next.Index, plain);
            at = next.Index;
            AiVector c = graph.Cell(at).Centre;
            if (c.DistanceTo(points[^1]) >= SampleUnits || at == goal.Index)
                points.Add(c);
        }

        return new Planned(points, cost);
    }

    /// <summary>What the cheapest edge from <paramref name="from"/> to <paramref name="to"/> costs a walker over <paramref name="plain"/>.</summary>
    public static float StepCost(NavGraph graph, int from, int to, NavObstacleState plain)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plain);
        float best = float.PositiveInfinity;
        foreach (NavEdge e in graph.EdgesTo(to))
        {
            if (e.From == from && graph.Admits(e, ClearanceClass.Stand, plain))
                best = MathF.Min(best, e.Cost + plain.CellExtra(to));
        }

        return best;
    }

    /// <summary>The corridor's field of a way: 0 for each cell within <see cref="Reach"/> across and <see cref="ReachUp"/> up or down of one of its points, <see cref="TFRouteVariation.MaxExtra"/> for every other.</summary>
    public static float[] Extra(NavGraph graph, IReadOnlyList<AiVector> points)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(points);
        Dictionary<(int, int), List<AiVector>> buckets = Buckets(points);
        var extra = new float[graph.CellCount];
        for (int c = 0; c < extra.Length; c++)
            extra[c] = Near(buckets, graph.Cell(c).Centre) ? 0f : TFRouteVariation.MaxExtra;
        return extra;
    }

    private static (int, int) Key(AiVector p) => ((int)MathF.Floor(p.X / Reach), (int)MathF.Floor(p.Y / Reach));

    private static Dictionary<(int, int), List<AiVector>> Buckets(IReadOnlyList<AiVector> points)
    {
        var buckets = new Dictionary<(int, int), List<AiVector>>();
        foreach (AiVector p in points)
        {
            if (!buckets.TryGetValue(Key(p), out List<AiVector>? list))
                buckets[Key(p)] = list = [];
            list.Add(p);
        }

        return buckets;
    }

    private static bool Near(Dictionary<(int, int), List<AiVector>> buckets, AiVector at)
    {
        (int kx, int ky) = Key(at);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!buckets.TryGetValue((kx + dx, ky + dy), out List<AiVector>? list))
                    continue;
                foreach (AiVector p in list)
                {
                    float x = p.X - at.X, y = p.Y - at.Y;
                    if ((x * x) + (y * y) < Reach * Reach && MathF.Abs(p.Z - at.Z) < ReachUp)
                        return true;
                }
            }
        }

        return false;
    }
}
