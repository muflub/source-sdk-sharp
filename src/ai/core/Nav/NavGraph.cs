using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>Which body sizes may stand in a cell (plan_hl2mp_ai §6.2).</summary>
[Flags]
public enum ClearanceClasses : byte
{
    None = 0,
    Stand = 1 << 0,
    Crouch = 1 << 1,
}

/// <summary>The body size a path is planned for.</summary>
public enum ClearanceClass : byte
{
    Stand = 0,
    Crouch = 1,
}

/// <summary>How an edge is crossed (§6.2): the six types, each a different usercmd.</summary>
public enum NavEdgeType : byte
{
    Walk = 0,
    Jump,
    CrouchJump,
    Drop,
    Ladder,
    Door,
}

/// <summary>A cell's flags beyond its clearance.</summary>
[Flags]
public enum NavCellFlags : byte
{
    None = 0,
    Water = 1 << 0,
    Ladder = 1 << 1,
}

/// <summary>
/// One cell: its footprint in the ground plane (min/max x, y), its floor
/// height, who may stand there, its flags, and its cost multiplier (≥ 1).
/// </summary>
public readonly record struct NavCell(
    float MinX,
    float MinY,
    float MaxX,
    float MaxY,
    float FloorZ,
    ClearanceClasses Classes,
    NavCellFlags Flags,
    float CostMultiplier)
{
    /// <summary>The centre of the footprint, on the floor.</summary>
    public AiVector Centre => new((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f, FloorZ);

    public bool Admits(ClearanceClass c) => (Classes & (c == ClearanceClass.Stand ? ClearanceClasses.Stand : ClearanceClasses.Crouch)) != 0;

    /// <summary>Only a crouched body fits.</summary>
    public bool CrouchOnly => Classes == ClearanceClasses.Crouch;
}

/// <summary>
/// One directed edge: where it goes, how, what it costs, and the dynamic
/// obstacle (a door) that can block it, or -1.
/// </summary>
public readonly record struct NavEdge(int From, int To, NavEdgeType Type, float Cost, int Obstacle);

/// <summary>
/// The AI's navigation graph (plan_hl2mp_ai §6.2): cells, directed edges in
/// CSR order both ways, and the number of dynamic obstacles. Immutable once
/// built, shared by every brain; an obstacle's open/shut state lives outside
/// it (<see cref="NavObstacleState"/>), so a door that moves changes no graph.
/// Built by <see cref="NavGraphBuilder"/>, our own: nothing here is stock nav
/// code (§6, ruling 2026-11-01).
/// </summary>
public sealed class NavGraph
{
    private readonly ImmutableArray<NavCell> _cells;
    private readonly ImmutableArray<NavEdge> _out;
    private readonly ImmutableArray<int> _outStart;
    private readonly ImmutableArray<NavEdge> _in;
    private readonly ImmutableArray<int> _inStart;

    // The ground-plane index: every cell is listed in each Bucket-sized square
    // its footprint overlaps, so a point's cells are one lookup away.
    private const float Bucket = 64f;
    private readonly Dictionary<(int, int), int[]> _buckets;
    private readonly int[] _component;

    internal NavGraph(ImmutableArray<NavCell> cells, ImmutableArray<NavEdge> edges, int obstacles)
    {
        _cells = cells;
        ObstacleCount = obstacles;
        (_out, _outStart) = Csr(cells.Length, edges, e => e.From);
        (_in, _inStart) = Csr(cells.Length, edges, e => e.To);
        _buckets = Index(cells);
        _component = Components(cells.Length, _out);
        Hash = ComputeHash();
    }

    public int CellCount => _cells.Length;

    public int EdgeCount => _out.Length;

    public int ObstacleCount { get; }

    /// <summary>A stable FNV-1a hash of every cell and edge: what a recording names its nav by (§2.6).</summary>
    public ulong Hash { get; }

    public NavCell Cell(CellId id) => _cells[id.Index];

    public NavCell Cell(int index) => _cells[index];

    /// <summary>Which joined-up part of the map a cell is in (edges either way): a place in another part can never be walked to.</summary>
    public int ComponentOf(int cell) => _component[cell];

    /// <summary>The cell without a copy, for the predictors' hops.</summary>
    internal ref readonly NavCell CellRef(int index) => ref _cells.ItemRef(index);

    /// <summary>The edges leaving <paramref name="cell"/>, in the order they were added.</summary>
    public ReadOnlySpan<NavEdge> EdgesFrom(int cell) => _out.AsSpan(_outStart[cell], _outStart[cell + 1] - _outStart[cell]);

    /// <summary>The edges arriving at <paramref name="cell"/>.</summary>
    public ReadOnlySpan<NavEdge> EdgesTo(int cell) => _in.AsSpan(_inStart[cell], _inStart[cell + 1] - _inStart[cell]);

    /// <summary>The edge from <paramref name="from"/> to <paramref name="to"/>, if there is one.</summary>
    public bool TryEdge(int from, int to, out NavEdge edge)
    {
        foreach (NavEdge e in EdgesFrom(from))
        {
            if (e.To == to)
            {
                edge = e;
                return true;
            }
        }

        edge = default;
        return false;
    }

    /// <summary>The cell whose footprint holds <paramref name="p"/> with the floor nearest below it, or None.</summary>
    public CellId FindCell(AiVector p)
    {
        int best = -1;
        float bestDz = float.MaxValue;
        foreach (int i in CellsAt(p.X, p.Y))
        {
            NavCell c = _cells[i];
            if (!Holds(c, p.X, p.Y))
                continue;
            float dz = p.Z - c.FloorZ;
            if (dz >= -1f && dz < bestDz)
            {
                bestDz = dz;
                best = i;
            }
        }

        return best >= 0 ? new CellId(best) : CellId.None;
    }

    /// <summary>
    /// Whether a body of <paramref name="cls"/> can walk the straight line from
    /// <paramref name="a"/> to <paramref name="b"/>, starting in cell
    /// <paramref name="from"/>: every cell the line crosses in the ground plane
    /// admits it, each is reached from the one before by a passable Walk or
    /// Door edge, and the last is on <paramref name="b"/>'s floor. What lets a path leave the cell corridor's corners and go
    /// straight across open ground (<see cref="PathSmoother.Shortcut"/>).
    /// </summary>
    public bool WalkableLine(int from, AiVector a, AiVector b, ClearanceClass cls, NavObstacleState obstacles)
    {
        int at = from;
        float length = (b - a).Length2D;
        int samples = Math.Max(1, (int)MathF.Ceiling(length / 4f));
        for (int s = 1; s <= samples; s++)
        {
            float t = (float)s / samples;
            float x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
            if (Holds(_cells[at], x, y))
                continue;
            int next = -1;
            foreach (NavEdge e in EdgesFrom(at))
            {
                if ((e.Type is NavEdgeType.Walk or NavEdgeType.Door) && Holds(_cells[e.To], x, y) && Passable(e, cls, obstacles))
                {
                    next = e.To;
                    break;
                }
            }

            if (next < 0)
                return false;
            at = next;
        }

        return MathF.Abs(_cells[at].FloorZ - b.Z) <= CornerFloorReach;
    }

    /// <summary>
    /// How far from the floor the line ends on a corner may be. A line over
    /// one cell's footprint to a corner on the floor under it is no walk: live,
    /// dm_lockdown's stair landing cut straight to the corner beneath it.
    /// </summary>
    public const float CornerFloorReach = 20f;

    /// <summary>
    /// The cell nearest <paramref name="goal"/> an agent of <paramref name="cls"/>
    /// in cell <paramref name="from"/> can get to: where it goes for a goal with no
    /// route, as a person goes as near as they can.
    /// </summary>
    public CellId NearestReachable(CellId from, AiVector goal, ClearanceClass cls, NavObstacleState obstacles)
    {
        var seen = new bool[_cells.Length];
        var open = new Stack<int>();
        seen[from.Index] = true;
        open.Push(from.Index);
        int best = from.Index;
        float bestDistance = _cells[from.Index].Centre.DistanceTo(goal);
        while (open.Count > 0)
        {
            foreach (NavEdge e in EdgesFrom(open.Pop()))
            {
                if (seen[e.To] || !Passable(e, cls, obstacles))
                    continue;
                seen[e.To] = true;
                open.Push(e.To);
                float distance = _cells[e.To].Centre.DistanceTo(goal);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = e.To;
                }
            }
        }

        return new CellId(best);
    }

    /// <summary>
    /// Whether an agent of <paramref name="cls"/> may take <paramref name="e"/>
    /// with the obstacles as <paramref name="obstacles"/> says: both ends admit
    /// it, no shut obstacle blocks it, and it does not step into a blocked cell.
    /// </summary>
    public bool Passable(in NavEdge e, ClearanceClass cls, NavObstacleState obstacles)
        => Admits(e, cls, obstacles) && !obstacles.CellBlocked(e.To);

    /// <summary>
    /// <see cref="Passable"/> without the cell it steps into: a field's search, which
    /// goes backwards from the objective and says for itself which cells it goes on from
    /// (a route may end in a blocked cell, or start in one).
    /// </summary>
    public bool Admits(in NavEdge e, ClearanceClass cls, NavObstacleState obstacles)
        => _cells[e.From].Admits(cls) && _cells[e.To].Admits(cls)
           && (e.Obstacle < 0 || !obstacles.Blocking(e.Obstacle) || (obstacles.OpenDoors && e.Type == NavEdgeType.Door));

    /// <summary>
    /// The cells whose footprints may meet the box from (<paramref name="minX"/>,
    /// <paramref name="minY"/>) to (<paramref name="maxX"/>, <paramref name="maxY"/>), each
    /// once, into <paramref name="into"/>: the index's squares, so a caller still tests each.
    /// </summary>
    public void CellsNear(float minX, float minY, float maxX, float maxY, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var seen = new HashSet<int>();
        for (int bx = (int)MathF.Floor(minX / Bucket); bx <= (int)MathF.Floor(maxX / Bucket); bx++)
        {
            for (int by = (int)MathF.Floor(minY / Bucket); by <= (int)MathF.Floor(maxY / Bucket); by++)
            {
                if (_buckets.TryGetValue((bx, by), out int[]? cells))
                {
                    foreach (int c in cells)
                    {
                        if (seen.Add(c))
                            into.Add(c);
                    }
                }
            }
        }
    }

    private static int[] Components(int cells, ImmutableArray<NavEdge> edges)
    {
        var parent = new int[cells];
        for (int c = 0; c < cells; c++)
            parent[c] = c;
        int Root(int c)
        {
            while (parent[c] != c)
            {
                parent[c] = parent[parent[c]];
                c = parent[c];
            }

            return c;
        }

        foreach (NavEdge e in edges)
        {
            int a = Root(e.From), b = Root(e.To);
            if (a != b)
                parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        for (int c = 0; c < cells; c++)
            parent[c] = Root(c);
        return parent;
    }

    private static bool Holds(in NavCell c, float x, float y) => x >= c.MinX && x < c.MaxX && y >= c.MinY && y < c.MaxY;

    private ReadOnlySpan<int> CellsAt(float x, float y)
        => _buckets.TryGetValue(((int)MathF.Floor(x / Bucket), (int)MathF.Floor(y / Bucket)), out int[]? cells) ? cells : [];

    private static Dictionary<(int, int), int[]> Index(ImmutableArray<NavCell> cells)
    {
        var lists = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < cells.Length; i++)
        {
            NavCell c = cells[i];
            for (int bx = (int)MathF.Floor(c.MinX / Bucket); bx <= (int)MathF.Floor((c.MaxX - 0.001f) / Bucket); bx++)
            {
                for (int by = (int)MathF.Floor(c.MinY / Bucket); by <= (int)MathF.Floor((c.MaxY - 0.001f) / Bucket); by++)
                {
                    if (!lists.TryGetValue((bx, by), out List<int>? list))
                        lists[(bx, by)] = list = new List<int>();
                    list.Add(i);
                }
            }
        }

        return lists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static (ImmutableArray<NavEdge>, ImmutableArray<int>) Csr(int cells, ImmutableArray<NavEdge> edges, Func<NavEdge, int> key)
    {
        var start = new int[cells + 1];
        foreach (NavEdge e in edges)
            start[key(e) + 1]++;
        for (int i = 0; i < cells; i++)
            start[i + 1] += start[i];
        var fill = (int[])start.Clone();
        var ordered = new NavEdge[edges.Length];
        foreach (NavEdge e in edges)
            ordered[fill[key(e)]++] = e;
        return (ImmutableArray.Create(ordered), ImmutableArray.Create(start));
    }

    private ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }

        void MixF(float f) => Mix(BitConverter.SingleToInt32Bits(f));

        Mix(_cells.Length);
        foreach (NavCell c in _cells)
        {
            MixF(c.MinX);
            MixF(c.MinY);
            MixF(c.MaxX);
            MixF(c.MaxY);
            MixF(c.FloorZ);
            Mix((int)c.Classes | ((int)c.Flags << 8));
            MixF(c.CostMultiplier);
        }

        Mix(_out.Length);
        foreach (NavEdge e in _out)
        {
            Mix(e.From);
            Mix(e.To);
            Mix((int)e.Type);
            MixF(e.Cost);
            Mix(e.Obstacle);
        }

        Mix(ObstacleCount);
        return h;
    }
}

/// <summary>Which dynamic obstacles block right now (a shut door). The binding keeps it current (§6.2).</summary>
public sealed class NavObstacleState
{
    private readonly bool[] _blocking;
    private bool[]? _shovable;
    private NavCellBlocks? _cells;
    private NavObstacleState? _live;

    /// <summary>
    /// The cells the entities this state's owner knows of take, or null for none. A
    /// <see cref="Shoved"/> copy follows its original's; a <see cref="Clone"/> keeps what it
    /// was cloned with, which a worker reads while the main thread publishes newer ones.
    /// </summary>
    public NavCellBlocks? Cells
    {
        get => _live is not null ? _live.Cells : _cells;
        set => _cells = value;
    }

    /// <summary>Whether no route goes through <paramref name="cell"/>.</summary>
    public bool CellBlocked(int cell) => Cells is { } cells && cells.State(cell) == NavCellBlocks.Blocked;

    /// <summary>What a step into <paramref name="cell"/> costs over the edge's own.</summary>
    public float CellExtra(int cell) => Cells?.State(cell) switch
    {
        NavCellBlocks.Costly => NavCellBlocks.ShoveCost,
        NavCellBlocks.Breakable => NavCellBlocks.BreakCost,
        _ => 0f,
    };

    public NavObstacleState(int obstacles) => _blocking = new bool[obstacles];

    public int Count => _blocking.Length;

    public bool Blocking(int obstacle) => _blocking[obstacle];

    /// <summary>
    /// Whether a shut door's edges are a way through (a player opens a door
    /// by walking into it or with +use; the door edge's wait is its cost): the
    /// game's binding says so; off, a shut door is a wall.
    /// </summary>
    public bool OpenDoors { get; set; }

    public void Set(int obstacle, bool blocking) => _blocking[obstacle] = blocking;

    /// <summary>Says a player can get through <paramref name="obstacle"/> by walking into it: a prop that moves when pushed.</summary>
    public void SetShovable(int obstacle) => (_shovable ??= new bool[_blocking.Length])[obstacle] = true;

    /// <summary>A copy with the obstacles a player can shove aside out of the way: where a bot goes with no other way out.</summary>
    public NavObstacleState Shoved()
    {
        NavObstacleState copy = Clone();
        copy._cells = null;
        copy._live = _live ?? this;
        for (int o = 0; _shovable is not null && o < _blocking.Length; o++)
        {
            if (_shovable[o])
                copy._blocking[o] = false;
        }

        return copy;
    }

    /// <summary>
    /// Makes this private copy as <paramref name="other"/> stands now, allocating nothing:
    /// a bot's lane refreshes its own while none of its steps runs (§2.7). The sizes match.
    /// </summary>
    public void CopyFrom(NavObstacleState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other._blocking.CopyTo(_blocking, 0);
        _shovable = other._shovable;
        OpenDoors = other.OpenDoors;
        _cells = other.Cells;
    }

    /// <summary>A copy as the obstacles stand now, for a field built on a worker while the main thread keeps this one current.</summary>
    public NavObstacleState Clone()
    {
        var copy = new NavObstacleState(_blocking.Length) { OpenDoors = OpenDoors };
        _blocking.CopyTo(copy._blocking, 0);
        copy._shovable = _shovable;
        copy._cells = Cells;
        return copy;
    }
}

/// <summary>
/// What the solid entities a bot knows of do to the graph's cells (plan_hl2mp_ai §6.2,
/// ruling 2026-11-02): one byte a cell, immutable once made, so a field built on a
/// worker reads it as it stood when asked for. A <see cref="Blocked"/> cell is on no
/// route's way (a route can still start in one, or end in one); a <see cref="Costly"/>
/// one is a big prop a player can shove, gone through only for more than the way round;
/// a <see cref="Breakable"/> one is something the bot can shoot out of its way.
/// </summary>
public sealed class NavCellBlocks(byte[] cells)
{
    public const byte Free = 0, Costly = 1, Blocked = 2, Breakable = 3;

    /// <summary>
    /// What a step into a breakable's cell costs: a crate across a corridor, four or
    /// five cells, is worth some 500 u of walking round. Shooting it takes a second or two.
    /// </summary>
    public const float BreakCost = 100f;

    /// <summary>What a step into a costly cell costs, in units: a crate five cells across is worth 2,500 u of walking round.</summary>
    public const float ShoveCost = 512f;

    public byte State(int cell) => (uint)cell < (uint)cells.Length ? cells[cell] : Free;

    /// <summary>How many cells are <see cref="Blocked"/> and how many <see cref="Costly"/>, by counting.</summary>
    public (int Blocked, int Costly) Count()
    {
        int blocked = 0, costly = 0;
        foreach (byte c in cells)
        {
            blocked += c == Blocked ? 1 : 0;
            costly += c == Costly ? 1 : 0;
        }

        return (blocked, costly);
    }
}

/// <summary>Builds a <see cref="NavGraph"/>: cells first, then edges between them.</summary>
public sealed class NavGraphBuilder
{
    private readonly List<NavCell> _cells = new();
    private readonly List<NavEdge> _edges = new();
    private int _obstacles;

    public int CellCount => _cells.Count;

    public CellId AddCell(in NavCell cell)
    {
        if (cell.MaxX <= cell.MinX || cell.MaxY <= cell.MinY)
            throw new ArgumentException("a cell needs a footprint", nameof(cell));
        if (cell.Classes == ClearanceClasses.None)
            throw new ArgumentException("a cell nobody can stand in is not a cell", nameof(cell));
        if (!(cell.CostMultiplier >= 1f))
            throw new ArgumentException($"a cost multiplier is at least 1, not {cell.CostMultiplier}", nameof(cell));
        _cells.Add(cell);
        return new CellId(_cells.Count - 1);
    }

    /// <summary>Declares a dynamic obstacle and answers its index.</summary>
    public int AddObstacle() => _obstacles++;

    /// <summary>
    /// Adds a directed edge. Its cost is the floor-to-floor distance times the
    /// mean of the two cells' multipliers, plus <paramref name="extraCost"/>
    /// (a jump's effort, a door's wait), so a cost is never below the
    /// distance and A*'s straight-line estimate stays admissible.
    /// </summary>
    public void AddEdge(CellId from, CellId to, NavEdgeType type, float extraCost = 0f, int obstacle = -1)
    {
        if (!from.IsValid || !to.IsValid || from.Index >= _cells.Count || to.Index >= _cells.Count)
            throw new ArgumentOutOfRangeException(nameof(from), "both ends must be cells of this builder");
        if (from == to)
            throw new ArgumentException("an edge joins two cells", nameof(to));
        if (obstacle >= _obstacles)
            throw new ArgumentOutOfRangeException(nameof(obstacle), obstacle, $"{_obstacles} obstacles are declared");
        ArgumentOutOfRangeException.ThrowIfNegative(extraCost);

        NavCell a = _cells[from.Index], b = _cells[to.Index];
        float cost = a.Centre.DistanceTo(b.Centre) * (a.CostMultiplier + b.CostMultiplier) * 0.5f + extraCost;
        _edges.Add(new NavEdge(from.Index, to.Index, type, cost, obstacle));
    }

    /// <summary>Adds the edge both ways.</summary>
    public void AddBoth(CellId a, CellId b, NavEdgeType type, float extraCost = 0f, int obstacle = -1)
    {
        AddEdge(a, b, type, extraCost, obstacle);
        AddEdge(b, a, type, extraCost, obstacle);
    }

    public NavGraph Build() => new([.. _cells], [.. _edges], _obstacles);
}
