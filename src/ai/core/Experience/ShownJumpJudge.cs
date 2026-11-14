namespace SourceSharp.Ai;

/// <summary>What the judge made of a jump a player showed: two of them are learned (<see cref="ShownJumpJudge.Learned"/>).</summary>
public enum ShownJumpVerdict : byte
{
    /// <summary>The takeoff or the landing is in no cell of the graph, or the landing stands well above its cell's floor (a prop, a building).</summary>
    NoCell,

    /// <summary>The takeoff and the landing are one cell, or one bin: a hop, and no row could tell its ends apart.</summary>
    Same,

    /// <summary>One of the level's links already goes there from there, for the class.</summary>
    Known,

    /// <summary>A walk goes there for no more than <see cref="ShownJumpJudge.WalkFactor"/> times the straight line.</summary>
    Walkable,

    /// <summary>Learned: the walk there costs more than <see cref="ShownJumpJudge.WalkFactor"/> times the straight line.</summary>
    LongWalk,

    /// <summary>Learned: there is no walk there at all.</summary>
    NoWay,
}

/// <summary>
/// Whether a jump a player showed is one the level has no way for (plan_bots_teams_tf2_part_g G.6, lane G6a): the walk on
/// the graph from the takeoff's cell to the landing's (every edge kind: walks, jumps, drops; doors open; a standing body),
/// held against the straight line by the finder's own rule (<see cref="WalkFactor"/>, TF2's
/// <c>JumpPlacesSettings.WalkFactor</c>), and the level's links. It holds a pathfinder and a flood of the graph, so it
/// serves one thread at a time (the experience worker's).
/// </summary>
public sealed class ShownJumpJudge
{
    /// <summary>A walk that costs no more than this many times the straight line is walked, not jumped (the finder's rule).</summary>
    public const float WalkFactor = 2.5f;

    /// <summary>
    /// A place's cell is the one whose floor is within this of the feet, above or below (a ramp's cell is one floor height);
    /// a landing further from every floor stood on something the graph has none for (a prop, a building).
    /// </summary>
    public const float FloorReach = 24f;

    /// <summary>The least a learned jump saves on the walk, in units: two bins. A hop over a rail saves a few steps, not a route.</summary>
    public const float MinSavedUnits = 128f;

    /// <summary>A link lands where the jump did when its landing bin is within this many bins across (and one up or down).</summary>
    public const int LandingSlackBins = 1;

    /// <summary>A link takes off where the jump did when its start bin is within this many bins across (a run-up's length).</summary>
    public const int TakeoffSlackBins = 3;

    private readonly NavGraph _graph;
    private readonly NavPathfinder _finder;
    private readonly NavObstacleState _obstacles;
    private readonly List<CellId> _path;
    private readonly Stack<int> _open;
    private bool[] _reach = [];
    private int _reachTo = -1;

    public ShownJumpJudge(NavGraph graph, NavPathfinder finder, NavObstacleState obstacles, List<CellId> path, Stack<int> open)
    {
        _graph = graph;
        _finder = finder;
        _obstacles = obstacles;
        _path = path;
        _open = open;
    }

    /// <summary>A judge over <paramref name="graph"/>, every door open.</summary>
    public static ShownJumpJudge Of(NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new ShownJumpJudge(graph, new NavPathfinder(), new NavObstacleState(graph.ObstacleCount) { OpenDoors = true }, [], new Stack<int>());
    }

    /// <summary>The graph it judges over.</summary>
    public NavGraph Graph => _graph;

    /// <summary>Whether <paramref name="verdict"/> makes a jump one to learn.</summary>
    public static bool Learned(ShownJumpVerdict verdict) => verdict is ShownJumpVerdict.LongWalk or ShownJumpVerdict.NoWay;

    /// <summary>
    /// What <paramref name="jump"/>, made by a player of <paramref name="cls"/>, is to the level: <paramref name="links"/>
    /// are the level's links with their rows' keys (null for none), whose start and landing bins say where each goes.
    /// </summary>
    public ShownJumpVerdict Judge(ShownJumpRecipe jump, int cls, ExperienceLinkKeys? links)
    {
        ArgumentNullException.ThrowIfNull(jump);
        CellId from = CellAt(jump.Takeoff), to = CellAt(jump.Landing);
        if (from == CellId.None || to == CellId.None)
            return ShownJumpVerdict.NoCell;
        if (from == to || jump.From == jump.To)
            return ShownJumpVerdict.Same;
        if (links is not null && Knows(links, cls, jump.From, jump.To))
            return ShownJumpVerdict.Known;
        if (!Reaches(from, to) || _finder.Find(_graph, from, to, ClearanceClass.Stand, _obstacles, _path, _graph.CellCount, out float cost) != PathStatus.Found)
            return ShownJumpVerdict.NoWay;
        float straight = (jump.Landing - jump.Takeoff).Length;
        return cost > WalkFactor * straight && cost - straight >= MinSavedUnits ? ShownJumpVerdict.LongWalk : ShownJumpVerdict.Walkable;
    }

    /// <summary>The cell under the feet at <paramref name="at"/>: the nearest floor below a point <see cref="FloorReach"/> over them, within that of them.</summary>
    private CellId CellAt(AiVector at)
    {
        CellId cell = _graph.FindCell(at + new AiVector(0f, 0f, FloorReach));
        return cell != CellId.None && MathF.Abs(at.Z - _graph.Cell(cell).FloorZ) <= FloorReach ? cell : CellId.None;
    }

    /// <summary>Whether one of <paramref name="links"/> of <paramref name="cls"/> starts near <paramref name="from"/> and lands near <paramref name="to"/>.</summary>
    public static bool Knows(ExperienceLinkKeys links, int cls, WorldBin from, WorldBin to)
    {
        ArgumentNullException.ThrowIfNull(links);
        for (int i = 0; i < links.Count; i++)
        {
            LinkKey k = links[i];
            if (k.Class == cls && Near(k.To, to, LandingSlackBins) && Near(k.From, from, TakeoffSlackBins))
                return true;
        }

        return false;
    }

    private static bool Near(WorldBin a, WorldBin b, int across)
        => Math.Abs(a.X - b.X) <= across && Math.Abs(a.Y - b.Y) <= across && Math.Abs(a.Z - b.Z) <= 1;

    /// <summary>Whether any way on foot leads from <paramref name="from"/> to <paramref name="to"/>: the cells with one are flooded back from it, kept while the next asks are for the same cell.</summary>
    private bool Reaches(CellId from, CellId to)
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
