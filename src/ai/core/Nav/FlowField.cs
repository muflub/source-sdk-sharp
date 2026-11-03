namespace SourceSharp.Ai;

/// <summary>
/// The cost from every cell to one objective cell for one clearance class,
/// and the edge each cell takes towards it (plan_hl2mp_ai §2.3): Dijkstra
/// over the incoming edges from the objective. Owned by the bot that asked
/// for it (no shared mutable cache, §2.7); <see cref="Build"/> refills it in
/// place, so a bot's ring of fields allocates once.
///
/// <para>Where two next cells are equally cheap, the one nearer the
/// objective in a straight line wins, then the lower index: so a field walks a
/// staircase along the line rather than a grid's L, and is the same however
/// often and on whichever thread it is built.</para>
/// </summary>
public sealed class FlowField
{
    private float[] _cost = [];
    private int[] _next = [];
    private NavEdgeType[] _via = [];
    private readonly PriorityQueue<int, (float, int)> _open = new();

    public CellId Objective { get; private set; }

    /// <summary>What a partial build settles beyond half as far again as the bot's cell, in cost.</summary>
    public const float PartialMargin = 512f;

    /// <summary>Whether the last build stopped short of the whole graph (it was given the bot's cell).</summary>
    public bool IsPartial { get; private set; }

    public ClearanceClass Class { get; private set; }

    /// <summary>How many cells can reach the objective.</summary>
    public int Reachable { get; private set; }

    /// <summary>The cost to the objective, or +∞ when it cannot be reached.</summary>
    public float CostAt(int cell) => (uint)cell < (uint)_cost.Length ? _cost[cell] : float.PositiveInfinity;

    /// <summary>The cell to go to next from <paramref name="cell"/>, or None at the objective or when unreachable.</summary>
    public CellId NextFrom(int cell) => (uint)cell < (uint)_next.Length && _next[cell] >= 0 ? new CellId(_next[cell]) : CellId.None;

    /// <summary>How the next step from <paramref name="cell"/> is crossed.</summary>
    public NavEdgeType ViaFrom(int cell) => (uint)cell < (uint)_via.Length ? _via[cell] : NavEdgeType.Walk;

    /// <summary>Whether <paramref name="a"/> beats <paramref name="b"/> as a next cell at equal cost.</summary>
    private static bool Nearer(NavGraph graph, int a, int b, AiVector target)
    {
        if (b < 0)
            return true;
        float da = graph.Cell(a).Centre.DistanceTo(target), db = graph.Cell(b).Centre.DistanceTo(target);
        return da < db || (da == db && a < b);
    }

    /// <summary>
    /// Builds the field to <paramref name="objective"/>; no route passes
    /// through a cell of <paramref name="avoided"/> (a follower's stuck spot,
    /// for a while), though the objective itself is never avoided.
    /// </summary>
    /// <para>
    /// With <paramref name="from"/> (the cell the bot stands in) the search
    /// stops once it has settled every cell up to half as far again as the
    /// bot plus <see cref="PartialMargin"/>: every cell of the bot's route has
    /// its cost by then, and a bot that strays off the settled cells finds no
    /// cost there and asks again (<see cref="IsPartial"/>).
    /// </para>
    public void Build(NavGraph graph, CellId objective, ClearanceClass cls, NavObstacleState obstacles, IReadOnlySet<int>? avoided = null,
                      CellId from = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!objective.IsValid || objective.Index >= graph.CellCount)
            throw new ArgumentOutOfRangeException(nameof(objective), objective.Index, "the objective is not a cell of this graph");

        int n = graph.CellCount;
        if (_cost.Length != n)
        {
            _cost = new float[n];
            _next = new int[n];
            _via = new NavEdgeType[n];
        }

        Array.Fill(_cost, float.PositiveInfinity);
        Array.Fill(_next, -1);
        Array.Clear(_via);
        _open.Clear();
        Objective = objective;
        Class = cls;
        Reachable = 0;

        if (!graph.Cell(objective).Admits(cls))
            return;

        AiVector target = graph.Cell(objective).Centre;
        _cost[objective.Index] = 0f;
        _open.Enqueue(objective.Index, (0f, objective.Index));
        float stop = float.PositiveInfinity;
        IsPartial = false;
        while (_open.TryDequeue(out int cell, out (float Cost, int) key))
        {
            if (key.Cost > _cost[cell])
                continue;
            if (key.Cost > stop)
            {
                IsPartial = true;
                break;
            }

            if (from.IsValid && cell == from.Index)
                stop = key.Cost * 1.5f + PartialMargin;
            Reachable++;
            if (avoided is not null && cell != objective.Index && avoided.Contains(cell))
                continue;
            foreach (NavEdge e in graph.EdgesTo(cell))
            {
                if (!graph.Passable(e, cls, obstacles))
                    continue;
                float c = _cost[cell] + e.Cost;
                if (c < _cost[e.From] || (c == _cost[e.From] && Nearer(graph, cell, _next[e.From], target)))
                {
                    bool improved = c < _cost[e.From];
                    _cost[e.From] = c;
                    _next[e.From] = cell;
                    _via[e.From] = e.Type;
                    if (improved)
                        _open.Enqueue(e.From, (c, e.From));
                }
            }
        }
    }
}
