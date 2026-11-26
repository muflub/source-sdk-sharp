namespace SourceSharp.Ai;

/// <summary>One way into a goal's room: its number, the bin it is kept by, its point, its cell, and how near the point a body passes through it.</summary>
public readonly record struct RouteDoor(int Index, WorldBin Bin, AiVector At, int Cell, float Reach);

/// <summary>
/// The families of routes into a goal (plan_bots_teams_tf2_part_g G.4's "outcomes", lane G4b): a route is of the family of
/// the door by which it enters the goal's room, the one choke a raid on a flag must cross. The room is every cell from which
/// the goal is at most <see cref="RoomCost"/> to walk (a search back from the goal along the graph's edges, bounded there);
/// its doors are the cells just outside with an edge into it, joined into pieces where they lie within
/// <see cref="DoorJoin"/> of each other; a piece of fewer than <see cref="MinDoorCells"/> cells is no door. A door's point is
/// its piece's cell nearest the piece's middle and its key the bin of that point, which outlives a rebuilt graph's new
/// cell numbers. Only the room's cells are visited: the work is the room's, not the map's.
/// </summary>
public sealed class RouteFamilies
{
    /// <summary>How far to walk from the goal the room reaches.</summary>
    public const float RoomCost = 1200f;

    /// <summary>Door cells this near each other are one door.</summary>
    public const float DoorJoin = 128f;

    /// <summary>The fewest cells a door has: a lone cell at the room's edge is the graph's grain, not a way in.</summary>
    public const int MinDoorCells = 3;

    /// <summary>A door's reach is its piece's furthest cell from its point and this much more: a body that came this near has come through it.</summary>
    public const float PassMargin = 64f;

    private readonly RouteDoor[] _doors;

    private RouteFamilies(NavGraph graph, int goal, RouteDoor[] doors, int roomCells)
    {
        Graph = graph;
        Goal = goal;
        _doors = doors;
        RoomCells = roomCells;
    }

    public NavGraph Graph { get; }

    /// <summary>The goal's cell.</summary>
    public int Goal { get; }

    /// <summary>The doors, by their index.</summary>
    public IReadOnlyList<RouteDoor> Doors => _doors;

    /// <summary>How many cells the room has: what the search visited.</summary>
    public int RoomCells { get; }

    /// <summary>The door whose key is <paramref name="bin"/>, or -1.</summary>
    public int IndexOf(WorldBin bin)
    {
        foreach (RouteDoor d in _doors)
        {
            if (d.Bin == bin)
                return d.Index;
        }

        return -1;
    }

    /// <summary>The door a body at <paramref name="at"/> is passing through (the nearest whose reach it is within), or -1.</summary>
    public int Passing(AiVector at)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        foreach (RouteDoor d in _doors)
        {
            float distance = at.DistanceTo(d.At);
            if (distance <= d.Reach && distance < bestDistance)
                (best, bestDistance) = (d.Index, distance);
        }

        return best;
    }

    /// <summary>The doors of <paramref name="goal"/>'s room on <paramref name="graph"/>.</summary>
    public static RouteFamilies Around(NavGraph graph, int goal, float roomCost = RoomCost)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if ((uint)goal >= (uint)graph.CellCount)
            return new RouteFamilies(graph, goal, [], 0);

        // The room: a search back from the goal (who can reach the goal from where), bounded at the room's cost.
        var cost = new Dictionary<int, float> { [goal] = 0f };
        var settled = new HashSet<int>();
        var open = new PriorityQueue<int, (float Cost, int Cell)>();
        open.Enqueue(goal, (0f, goal));
        while (open.TryDequeue(out int cell, out (float Cost, int Cell) key))
        {
            if (!settled.Add(cell))
                continue;
            foreach (ref readonly NavEdge e in graph.EdgesTo(cell))
            {
                if (e.Type is NavEdgeType.DoubleJump or NavEdgeType.TripleJump || !float.IsFinite(e.Cost))
                    continue;
                float c = key.Cost + e.Cost;
                if (c > roomCost || (cost.TryGetValue(e.From, out float known) && known <= c))
                    continue;
                cost[e.From] = c;
                open.Enqueue(e.From, (c, e.From));
            }
        }

        // The door cells: outside the room, with an edge into it; in cell order, so the pieces come out the same every time.
        var frontier = new SortedSet<int>();
        foreach (int cell in settled)
        {
            foreach (ref readonly NavEdge e in graph.EdgesTo(cell))
            {
                if (e.Type is not (NavEdgeType.DoubleJump or NavEdgeType.TripleJump) && !settled.Contains(e.From))
                    frontier.Add(e.From);
            }
        }

        int[] cells = [.. frontier];
        var parent = new int[cells.Length];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        float join2 = DoorJoin * DoorJoin;
        for (int i = 0; i < cells.Length; i++)
        {
            AiVector a = graph.Cell(cells[i]).Centre;
            for (int j = i + 1; j < cells.Length; j++)
            {
                if ((graph.Cell(cells[j]).Centre - a).LengthSquared <= join2)
                    parent[Find(j)] = Find(i);
            }
        }

        var pieces = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < cells.Length; i++)
        {
            int root = Find(i);
            if (!pieces.TryGetValue(root, out List<int>? piece))
                pieces[root] = piece = new List<int>();
            piece.Add(cells[i]);
        }

        var doors = new List<RouteDoor>();
        foreach (List<int> piece in pieces.Values)
        {
            if (piece.Count < MinDoorCells)
                continue;
            AiVector middle = AiVector.Zero;
            foreach (int c in piece)
                middle += graph.Cell(c).Centre;
            middle *= 1f / piece.Count;
            int point = piece[0];
            foreach (int c in piece)
            {
                if (graph.Cell(c).Centre.DistanceTo(middle) < graph.Cell(point).Centre.DistanceTo(middle))
                    point = c;
            }

            AiVector at = graph.Cell(point).Centre;
            float reach = 0f;
            foreach (int c in piece)
                reach = MathF.Max(reach, graph.Cell(c).Centre.DistanceTo(at));
            doors.Add(new RouteDoor(doors.Count, WorldBin.Of(at), at, point, reach + PassMargin));
        }

        return new RouteFamilies(graph, goal, [.. doors], settled.Count);
    }
}

/// <summary>
/// Seeded Thompson sampling over choices with tries and scores in [0, 1] (plan_bots_teams_tf2_part_g G.4's "outcomes", lane
/// G4b): each choice's chance of working is drawn from Beta(1 + score, 1 + tries - score) and the highest draw is taken, so
/// a choice that failed five times is taken less than one that worked, one never tried keeps an even prior, and the same
/// stream with the same counts takes the same choice.
/// </summary>
public static class OutcomeSampler
{
    /// <summary>The choice taken among <paramref name="stats"/>, drawn from <paramref name="random"/> (the lower index on a tie); -1 for none.</summary>
    public static int Choose(ReadOnlySpan<OutcomeStat> stats, AiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int best = -1;
        double bestDraw = double.MinValue;
        for (int i = 0; i < stats.Length; i++)
        {
            double tries = Math.Max(0d, stats[i].Tries), score = Math.Clamp(stats[i].Score, 0d, tries);
            double draw = Beta(random, 1d + score, 1d + tries - score);
            if (draw > bestDraw)
                (best, bestDraw) = (i, draw);
        }

        return best;
    }

    /// <summary>A draw from Beta(<paramref name="a"/>, <paramref name="b"/>), both above 0.</summary>
    public static double Beta(AiRandom random, double a, double b)
    {
        double x = Gamma(random, a), y = Gamma(random, b);
        return x + y <= 0d ? 0.5d : x / (x + y);
    }

    /// <summary>A draw from Gamma(<paramref name="shape"/>, 1): Marsaglia and Tsang's method, lifted for a shape under 1.</summary>
    public static double Gamma(AiRandom random, double shape)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (shape < 1d)
            return Gamma(random, shape + 1d) * Math.Pow(Uniform(random), 1d / shape);
        double d = shape - 1d / 3d, c = 1d / Math.Sqrt(9d * d);
        for (int guard = 0; guard < 1000; guard++)
        {
            double x = Normal(random), v = 1d + c * x;
            if (v <= 0d)
                continue;
            v = v * v * v;
            double u = Uniform(random);
            if (u < 1d - 0.0331d * x * x * x * x || Math.Log(u) < 0.5d * x * x + d * (1d - v + Math.Log(v)))
                return d * v;
        }

        return d;
    }

    /// <summary>A uniform draw in (0, 1).</summary>
    private static double Uniform(AiRandom random) => ((random.NextUInt64() >> 11) + 0.5d) * (1d / (1UL << 53));

    /// <summary>A standard normal draw (Box and Muller).</summary>
    private static double Normal(AiRandom random) => Math.Sqrt(-2d * Math.Log(Uniform(random))) * Math.Cos(2d * Math.PI * Uniform(random));
}
