namespace SourceSharp.Ai;

/// <summary>How an A* search ended.</summary>
public enum PathStatus : byte
{
    Found = 0,
    Unreachable,

    /// <summary>The expansion budget ran out first (§2.3: a search is time-sliced); try again next decide.</summary>
    OutOfBudget,
}

/// <summary>
/// A* over a <see cref="NavGraph"/> for the odd trip a flow field is not
/// worth building for (plan_hl2mp_ai §2.3). The straight-line distance is the
/// estimate; edge costs are never below it (<see cref="NavGraphBuilder.AddEdge"/>),
/// so the path found is a cheapest one. Among equally cheap cells the one
/// nearest the straight start-goal line is expanded first, then the lower
/// index: on a grid many cheapest paths tie, and the one along the line is
/// the corridor the funnel can pull straight (§3.4.3).
/// Reusable: one per bot, its arrays sized once per graph.
/// </summary>
public sealed class NavPathfinder
{
    private float[] _g = [];
    private int[] _parent = [];
    private int[] _stamp = [];
    private int _search;
    private readonly PriorityQueue<int, (float, float, int)> _open = new();

    /// <summary>
    /// The cells from <paramref name="start"/> to <paramref name="goal"/>,
    /// both included, into <paramref name="path"/> (cleared first), expanding
    /// at most <paramref name="budget"/> cells.
    /// </summary>
    public PathStatus Find(NavGraph graph, CellId start, CellId goal, ClearanceClass cls, NavObstacleState obstacles,
                           List<CellId> path, int budget, out float cost)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        cost = float.PositiveInfinity;
        int n = graph.CellCount;
        if (_g.Length != n)
        {
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _search = 0;
        }

        _search++;
        _open.Clear();
        if (!graph.Cell(start).Admits(cls) || !graph.Cell(goal).Admits(cls))
            return PathStatus.Unreachable;

        AiVector origin = graph.Cell(start).Centre, target = graph.Cell(goal).Centre;
        Touch(start.Index, 0f, -1);
        _open.Enqueue(start.Index, (origin.DistanceTo(target), 0f, start.Index));
        int expanded = 0;
        while (_open.TryDequeue(out int cell, out (float F, float, int) key))
        {
            float h = graph.Cell(cell).Centre.DistanceTo(target);
            if (key.F > _g[cell] + h + 1e-3f)
                continue;
            if (cell == goal.Index)
            {
                cost = _g[cell];
                for (int c = cell; c >= 0; c = _parent[c])
                    path.Add(new CellId(c));
                path.Reverse();
                return PathStatus.Found;
            }

            if (++expanded > budget)
                return PathStatus.OutOfBudget;

            foreach (NavEdge e in graph.EdgesFrom(cell))
            {
                if (!graph.Passable(e, cls, obstacles))
                    continue;
                float g = _g[cell] + e.Cost;
                if (_stamp[e.To] != _search || g < _g[e.To])
                {
                    Touch(e.To, g, cell);
                    AiVector at = graph.Cell(e.To).Centre;
                    _open.Enqueue(e.To, (g + at.DistanceTo(target), OffLine(origin, target, at), e.To));
                }
            }
        }

        return PathStatus.Unreachable;
    }

    /// <summary>How far <paramref name="p"/> is from the line through <paramref name="a"/> and <paramref name="b"/>, in the ground plane.</summary>
    private static float OffLine(AiVector a, AiVector b, AiVector p)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        return len < 1e-3f ? 0f : MathF.Abs(dx * (p.Y - a.Y) - dy * (p.X - a.X)) / len;
    }

    private void Touch(int cell, float g, int parent)
    {
        _stamp[cell] = _search;
        _g[cell] = g;
        _parent[cell] = parent;
    }
}
