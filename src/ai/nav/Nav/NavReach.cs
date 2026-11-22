namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// How far a blast jump is taken to carry a body, for <see cref="NavReach"/>: up to
/// <see cref="Rise"/> over the takeoff's floor and <see cref="Range"/> across it, down any depth.
/// </summary>
public readonly record struct BlastBound(float Rise, float Range);

/// <summary>
/// Which cells of a graph a player reaches from its ways in (plan_bots_teams_tf2_part_g, Q-R3 of
/// 2026-11-13): first along the graph's edges (walk, drop, jump, ladder, door), from the cells
/// given; then, when a <see cref="BlastBound"/> is given, by blast jumps as well, as a bound.
/// <list type="bullet">
/// <item><b>A blast jump</b> goes from a reached cell to a cell not reached within the bound's
/// rise and range, when a body's centre has free space all the way along one of a few paths: up
/// from the takeoff to a height, across to over the landing, and down to it (a height from the
/// higher floor plus a crouched body up to the takeoff's floor plus the rise, every 64 units);
/// sampled every <see cref="Step"/> units through <see cref="IVoxelSpace"/>.</item>
/// <item><b>Samples, not every pair.</b> Takeoffs are one reached cell per 64-unit square and
/// 64-unit band of floor; landings one cell per square, band and joined-up part
/// (<see cref="NavGraph.ComponentOf"/>), so one landing found reaches its part's cells along the
/// edges again.</item>
/// </list>
/// A bound, not the bots' search: a platform across a gap is reached, a floor behind a wall is
/// not unless the air above the wall joins them, which in a sealed space it may.
/// </summary>
public static class NavReach
{
    /// <summary>How far apart a flight path is sampled for free space.</summary>
    public const float Step = 16f;

    private const float Bucket = 64f;
    private const float Band = 64f;
    private const float Body = 36f;

    /// <summary>The cells reached from <paramref name="seeds"/>; with <paramref name="blast"/>, by blast jumps through <paramref name="space"/> too.</summary>
    /// <param name="stops">Cells a player is taken away from when he gets there (a teleport back): reached, but nothing is reached through them. Null for none.</param>
    public static bool[] From(NavGraph graph, IEnumerable<int> seeds, BlastBound? blast = null, IVoxelSpace? space = null, bool[]? stops = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(seeds);
        if (blast is not null)
            ArgumentNullException.ThrowIfNull(space);
        var reached = new bool[graph.CellCount];
        var queue = new Queue<int>();
        var fresh = new List<int>();
        void Walk(int start)
        {
            if (reached[start])
                return;
            reached[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                fresh.Add(c);
                if (stops is not null && stops[c])
                    continue;
                foreach (NavEdge e in graph.EdgesFrom(c))
                {
                    if (!reached[e.To])
                    {
                        reached[e.To] = true;
                        queue.Enqueue(e.To);
                    }
                }
            }
        }

        foreach (int s in seeds)
            Walk(s);
        if (blast is not { } bound)
            return reached;

        // Landings: one cell per square, band and part, not reached yet.
        var landings = new Dictionary<(int, int), List<int>>();
        var seen = new HashSet<(int, int, int, int)>();
        for (int c = 0; c < graph.CellCount; c++)
        {
            if (reached[c] || (stops is not null && stops[c]))
                continue;
            NavCell cell = graph.Cell(c);
            (int bx, int by) = BucketOf(cell);
            if (!seen.Add((bx, by, (int)MathF.Floor(cell.FloorZ / Band), graph.ComponentOf(c))))
                continue;
            if (!landings.TryGetValue((bx, by), out List<int>? list))
                landings[(bx, by)] = list = new List<int>();
            list.Add(c);
        }

        var takeoffs = new HashSet<(int, int, int)>();
        int reach = (int)MathF.Ceiling(bound.Range / Bucket);
        while (fresh.Count > 0)
        {
            int[] batch = [.. fresh];
            fresh.Clear();
            foreach (int t in batch)
            {
                if (stops is not null && stops[t])
                    continue;
                NavCell from = graph.Cell(t);
                (int bx, int by) = BucketOf(from);
                if (!takeoffs.Add((bx, by, (int)MathF.Floor(from.FloorZ / Band))))
                    continue;
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (!landings.TryGetValue((bx + dx, by + dy), out List<int>? list))
                            continue;
                        foreach (int l in list)
                        {
                            if (reached[l])
                                continue;
                            NavCell to = graph.Cell(l);
                            float across = (to.Centre - from.Centre).Length2D;
                            if (across > bound.Range || to.FloorZ - from.FloorZ > bound.Rise || !Flies(space!, from.Centre, to.Centre, from.FloorZ + bound.Rise))
                                continue;
                            Walk(l);
                        }
                    }
                }
            }
        }

        return reached;
    }

    private static (int, int) BucketOf(in NavCell c) => ((int)MathF.Floor(c.Centre.X / Bucket), (int)MathF.Floor(c.Centre.Y / Bucket));

    /// <summary>Whether a body's centre has free space up from <paramref name="a"/>, across and down to <paramref name="b"/> at some height up to <paramref name="top"/>.</summary>
    public static bool Flies(IVoxelSpace space, AiVector a, AiVector b, float top)
    {
        ArgumentNullException.ThrowIfNull(space);
        float low = MathF.Max(a.Z, b.Z) + Body;
        for (float z = low; z <= MathF.Max(low, top + Body); z += Band)
        {
            if (Line(space, a + new AiVector(0, 0, Body), new AiVector(a.X, a.Y, z))
                && Line(space, new AiVector(a.X, a.Y, z), new AiVector(b.X, b.Y, z))
                && Line(space, new AiVector(b.X, b.Y, z), b + new AiVector(0, 0, Body)))
                return true;
        }

        return false;
    }

    private static bool Line(IVoxelSpace space, AiVector a, AiVector b)
    {
        float length = (b - a).Length;
        int steps = Math.Max(1, (int)MathF.Ceiling(length / Step));
        for (int i = 0; i <= steps; i++)
        {
            if (!space.Free(a + (b - a) * (i / (float)steps)))
                return false;
        }

        return true;
    }
}
