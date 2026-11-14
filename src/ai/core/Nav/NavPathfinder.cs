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
///
/// <para>
/// With <see cref="NavLinks"/> (lane G5c-2a) it takes the links its agent's state admits
/// (<see cref="NavLinks.Admits"/>) as more steps, after the edges of each cell, and scales its
/// estimate by the set's <see cref="NavLinks.EstimateScale"/> so a link cheaper than the straight
/// line keeps the path a cheapest one. An agent the set admits nothing for (no reach for any of its
/// kinds) searches as it would with no set at all: the same estimate, the same order, the same path.
/// </para>
/// </summary>
public sealed class NavPathfinder
{
    private float[] _g = [];
    private int[] _parent = [];
    private int[] _via = [];
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
        => Find(graph, null, start, goal, cls, obstacles, path, null, budget, out cost);

    /// <summary>
    /// <see cref="Find(NavGraph, CellId, CellId, ClearanceClass, NavObstacleState, List{CellId}, int, out float)"/>
    /// with <paramref name="links"/> beside the graph's edges (null for none). <paramref name="steps"/>,
    /// when given, is cleared and gets one entry a step of the path found: at <c>i</c>, how the
    /// path goes from <c>path[i]</c> to <c>path[i + 1]</c>: -1 by an edge of the graph, else the
    /// index in <paramref name="links"/> of the link it takes (whose <see cref="NavLink.Id"/> names it
    /// to its finder). A set over another graph is refused.
    /// </summary>
    public PathStatus Find(NavGraph graph, NavLinks? links, CellId start, CellId goal, ClearanceClass cls, NavObstacleState obstacles,
                           List<CellId> path, List<int>? steps, int budget, out float cost)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (links is not null && !links.IsOver(graph))
            throw new ArgumentException($"the links are over another graph ({links.GraphHash:x16}, {links.CellCount} cells; this is {graph.Hash:x16}, {graph.CellCount})", nameof(links));
        path.Clear();
        steps?.Clear();
        cost = float.PositiveInfinity;
        int n = graph.CellCount;
        if (_g.Length != n)
        {
            _g = new float[n];
            _parent = new int[n];
            _via = new int[n];
            _stamp = new int[n];
            _search = 0;
        }

        _search++;
        _open.Clear();
        if (!graph.Cell(start).Admits(cls) || !graph.Cell(goal).Admits(cls))
            return PathStatus.Unreachable;

        // The links only for an agent with the reach of some of them: else not even the estimate changes.
        NavLinks? taken = links is not null && (obstacles.Reach & links.Reaches) != 0 ? links : null;
        float scale = taken?.EstimateScale ?? 1f;
        AiVector origin = graph.Cell(start).Centre, target = graph.Cell(goal).Centre;
        Touch(start.Index, 0f, -1, -1);
        _open.Enqueue(start.Index, (Estimate(origin, target, scale), 0f, start.Index));
        int expanded = 0;
        while (_open.TryDequeue(out int cell, out (float F, float, int) key))
        {
            float h = Estimate(graph.Cell(cell).Centre, target, scale);
            if (key.F > _g[cell] + h + 1e-3f)
                continue;
            if (cell == goal.Index)
            {
                cost = _g[cell];
                for (int c = cell; c >= 0; c = _parent[c])
                    path.Add(new CellId(c));
                path.Reverse();
                if (steps is not null)
                {
                    for (int i = 1; i < path.Count; i++)
                        steps.Add(_via[path[i].Index]);
                }

                return PathStatus.Found;
            }

            if (++expanded > budget)
                return PathStatus.OutOfBudget;

            foreach (NavEdge e in graph.EdgesFrom(cell))
            {
                if (!graph.Passable(e, cls, obstacles))
                    continue;
                Relax(graph, cell, e.To, _g[cell] + e.Cost, -1, origin, target, scale);
            }

            if (taken is null)
                continue;
            foreach (int link in taken.From(cell))
            {
                if (taken.Admits(graph, link, cls, obstacles))
                    Relax(graph, cell, taken[link].To, _g[cell] + taken.CostOf(link), link, origin, target, scale);
            }
        }

        return PathStatus.Unreachable;
    }

    private void Relax(NavGraph graph, int cell, int to, float g, int via, AiVector origin, AiVector target, float scale)
    {
        if (_stamp[to] != _search || g < _g[to])
        {
            Touch(to, g, cell, via);
            AiVector at = graph.Cell(to).Centre;
            _open.Enqueue(to, (g + Estimate(at, target, scale), OffLine(origin, target, at), to));
        }
    }

    /// <summary>The straight line to the goal, scaled when links cheaper than it may be taken (exactly the line at 1).</summary>
    private static float Estimate(AiVector at, AiVector target, float scale)
        => scale == 1f ? at.DistanceTo(target) : at.DistanceTo(target) * scale;

    /// <summary>How far <paramref name="p"/> is from the line through <paramref name="a"/> and <paramref name="b"/>, in the ground plane.</summary>
    private static float OffLine(AiVector a, AiVector b, AiVector p)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        return len < 1e-3f ? 0f : MathF.Abs(dx * (p.Y - a.Y) - dy * (p.X - a.X)) / len;
    }

    private void Touch(int cell, float g, int parent, int via)
    {
        _stamp[cell] = _search;
        _g[cell] = g;
        _parent[cell] = parent;
        _via[cell] = via;
    }
}
