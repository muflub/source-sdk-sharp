using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The time a walk takes over the bot's own graph
/// (plan_bots_teams_tf2_part_g G.5 step 4): the cheapest path between the two
/// places' cells, as a standing body with no jump reach beyond the graph's
/// plain ones finds it with every door open, at the class's run speed. An
/// edge's cost is its length and what its kind adds, in units, so the time
/// is that over the speed. It holds a pathfinder, so it serves one thread at
/// a time.
/// </summary>
public sealed class GraphJumpWalk : IJumpWalk
{
    private readonly NavGraph _graph;
    private readonly NavPathfinder _finder;
    private readonly NavObstacleState _obstacles;
    private readonly float _runSpeed;
    private readonly List<CellId> _path = [];
    private readonly Stack<int> _open = new();
    private bool[] _reach = [];
    private int _reachTo = -1;

    public GraphJumpWalk(NavGraph graph, NavPathfinder finder, NavObstacleState obstacles, float runSpeed)
    {
        _graph = graph;
        _finder = finder;
        _obstacles = obstacles;
        _runSpeed = runSpeed;
    }

    /// <summary>Over <paramref name="graph"/>, every door open, at <paramref name="runSpeed"/>.</summary>
    public static GraphJumpWalk Of(NavGraph graph, float runSpeed)
        => new(graph, new NavPathfinder(), new NavObstacleState(graph.ObstacleCount) { OpenDoors = true }, runSpeed);

    public float? Seconds(Vector from, Vector to)
    {
        CellId a = _graph.FindCell(new AiVector(from.X, from.Y, from.Z)), b = _graph.FindCell(new AiVector(to.X, to.Y, to.Z));
        return a == CellId.None || b == CellId.None ? null : Seconds(a, b);
    }

    /// <summary>Seconds on foot from cell <paramref name="from"/> to cell <paramref name="to"/>, or null when there is no way.</summary>
    public float? Seconds(CellId from, CellId to)
        => Cost(from, to) is { } cost ? cost / _runSpeed : null;

    /// <summary>The cheapest path's cost in units, or null when there is no way.</summary>
    public float? Cost(CellId from, CellId to)
        => Reaches(from, to) && _finder.Find(_graph, from, to, ClearanceClass.Stand, _obstacles, _path, _graph.CellCount, out float cost) == PathStatus.Found
            ? cost
            : null;

    /// <summary>
    /// Whether there is any way on foot from <paramref name="from"/> to
    /// <paramref name="to"/>: the cells with a way to <paramref name="to"/>
    /// are flooded back along the edges once and kept while the next asks are
    /// for the same cell. A ledge nothing walks up to is asked about from
    /// every stretch of ground before it, and a search for a way that is not
    /// there would look through everything the takeoff can reach each time.
    /// </summary>
    public bool Reaches(CellId from, CellId to)
    {
        if (_reachTo != to.Index || _reach.Length != _graph.CellCount)
        {
            if (_reach.Length != _graph.CellCount)
                _reach = new bool[_graph.CellCount];
            else
                Array.Clear(_reach);
            _reachTo = to.Index;
            _open.Clear();
            _reach[to.Index] = true;
            _open.Push(to.Index);
            while (_open.Count > 0)
            {
                foreach (NavEdge e in _graph.EdgesTo(_open.Pop()))
                {
                    if (!_reach[e.From] && _graph.Passable(e, ClearanceClass.Stand, _obstacles))
                    {
                        _reach[e.From] = true;
                        _open.Push(e.From);
                    }
                }
            }
        }

        return _reach[from.Index];
    }
}
