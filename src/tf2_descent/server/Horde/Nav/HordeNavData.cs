using System.Diagnostics;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.MapFormats.Nav;

using TF2.Descent.Ai.Brains;
using TF2.Shared.Combat;
using TF2.Shared.Movement;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// The robots' body and movement numbers as the graph derivation takes them (plan_descent_logic
/// 3.7): TF's player hull and movement (TF2's own numbers, worked as the TF2 AI's profile works
/// them, which Descent may not reference), no double jump (a robot scout does not dash), a
/// crouch-only place eight times the cost, and an extra hull for each robot scale band
/// (<see cref="HordeHulls.Scales"/>), so a giant's path is planned for a giant's box.
/// </summary>
public static class HordeNavNumbers
{
    /// <summary>What a crouch-only place costs to cross, times a standing one's: a robot does not duck to get round.</summary>
    public const float CrouchOnlyCost = 8f;

    public static NavNumbers For()
    {
        MovementParams body = TFViewVectors.Params;
        float g = body.Gravity, jump = TFViewVectors.JumpHeight;
        float tuck = body.HullMax.Z - body.DuckHullMax.Z, fall = TFCombatDefs.PlayerMaxSafeFallSpeed;
        int n = HordeHulls.Scales.Length;
        var hulls = new NavHull[n];
        for (int i = 0; i < n; i++)
            hulls[i] = new NavHull(HordeHulls.Width(i), HordeHulls.Height(i));
        return new NavNumbers(body.HullMax.X - body.HullMin.X, body.HullMax.Z, body.DuckHullMax.Z, body.StepSize,
                              jump + body.StepSize, jump + tuck, fall * fall / (2f * g), 0f, WideHull: true, CrouchOnlyCost: CrouchOnlyCost)
        {
            ExtraHulls = NavHulls.Of(hulls),
        };
    }
}

/// <summary>
/// A level's navigation as the horde holds it (plan_descent_logic 3.7): the graph derived from
/// the level's <c>.nav3d</c> for the robots' numbers (<see cref="HordeNavNumbers"/>), the
/// file's free space as voxel columns (what <see cref="VoxelMoveWorld"/> sweeps a worker's step
/// against), the obstacle state with every dynamic obstacle open (a robot's doors are open
/// doorways; the arena's standing brushes block on top of it, <see cref="HordeArenaObstacles"/>),
/// and how long the derivation took. Immutable once made, so
/// any number of AI workers read it.
/// </summary>
public sealed class HordeNavData
{
    private readonly int[][] _components;
    private readonly int[] _main;

    private HordeNavData(DerivedNav nav, VoxelColumns columns, NavObstacleState open, double deriveMs, double columnsMs, int fileBytes, ulong identity,
                         int blocked)
    {
        Nav = nav;
        Columns = columns;
        Open = open;
        DeriveMs = deriveMs;
        ColumnsMs = columnsMs;
        FileBytes = fileBytes;
        Identity = identity;
        BlockedCells = blocked;
        World = new VoxelMoveWorld(columns);
        _components = new int[2 + HordeHulls.Scales.Length][];
        _main = new int[_components.Length];
        for (int c = 0; c < _components.Length; c++)
        {
            _components[c] = c == (int)ClearanceClass.Crouch ? [] : Components(nav.Graph, (ClearanceClass)c, open);
            _main[c] = Largest(_components[c]);
        }
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is on <paramref name="cls"/>'s main floor: the largest
    /// component of its cells (the one the level's rooms share; on depth 1 seed 1, the arrival's
    /// for every class). A cell in a smaller one is a floor that class cannot leave.
    /// </summary>
    public bool OnMainFloor(CellId cell, ClearanceClass cls)
    {
        int c = (int)cls;
        if ((uint)c >= (uint)_components.Length || _components[c].Length == 0)
            return cell.IsValid;
        return cell.IsValid && _components[c][cell.Index] >= 0 && _components[c][cell.Index] == _main[c];
    }

    /// <summary>The component holding the most cells (the lower on a tie); -1 for none.</summary>
    private static int Largest(int[] comp)
    {
        if (comp.Length == 0)
            return -1;
        var counts = new Dictionary<int, int>();
        foreach (int k in comp)
        {
            if (k >= 0)
                counts[k] = counts.GetValueOrDefault(k) + 1;
        }

        int best = -1, most = 0;
        foreach (var (k, n) in counts)
        {
            if (n > most || (n == most && k < best))
                (best, most) = (k, n);
        }

        return best;
    }

    public DerivedNav Nav { get; }

    public NavGraph Graph => Nav.Graph;

    public VoxelColumns Columns { get; }

    public VoxelMoveWorld World { get; }

    /// <summary>
    /// The obstacles all open, with the level's prop cells (<see cref="WithBlocks"/>): what the
    /// arena's state starts from (<see cref="HordeArenaObstacles.State"/>; the paths plan with that).
    /// </summary>
    public NavObstacleState Open { get; }

    /// <summary>The graph's identity, as a recording and a view name it: the graph's own hash, and the cells blocked on it (<see cref="WithBlocks"/>).</summary>
    public ulong Identity { get; }

    /// <summary>Cells blocked on the graph (props the file does not hold: <see cref="WithBlocks"/>).</summary>
    public int BlockedCells { get; }

    public double DeriveMs { get; }

    public double ColumnsMs { get; }

    public int FileBytes { get; }

    /// <summary>
    /// The level's navigation from its <c>.nav3d</c> bytes: the graph for the robots' numbers and
    /// the voxel columns. Pure; seconds of work on a large level, so a worker runs it.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not a <c>.nav3d</c>, or the file has no free space.</exception>
    public static HordeNavData Derive(byte[] nav3d)
    {
        ArgumentNullException.ThrowIfNull(nav3d);
        long t0 = Stopwatch.GetTimestamp();
        var source = new Nav3dNavSource(Nav3dReader.Open(nav3d));
        DerivedNav nav = NavGraphFromNav3d.Derive(source, HordeNavNumbers.For(), NavEdgeCosts.Default, []);
        double derive = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        long t1 = Stopwatch.GetTimestamp();
        VoxelColumns columns;
        try
        {
            columns = VoxelColumns.FromNav(source);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }

        double cols = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
        return Of(nav, columns, derive, cols, nav3d.Length);
    }

    /// <summary>
    /// A level's navigation from a derived graph and its voxel columns, every obstacle open (what
    /// <see cref="Derive"/> makes of a file, and a fact of a graph it builds by hand).
    /// </summary>
    public static HordeNavData Of(DerivedNav nav, VoxelColumns columns, double deriveMs = 0, double columnsMs = 0, int fileBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(columns);
        var open = new NavObstacleState(nav.Graph.ObstacleCount) { OpenDoors = true };
        return new HordeNavData(nav, columns, open, deriveMs, columnsMs, fileBytes, nav.Graph.Hash, 0);
    }

    /// <summary>
    /// The same navigation with the cells <paramref name="blocks"/> marks
    /// (<see cref="NavCellBlocks.Blocked"/>) taken out of every path: what the file does not know
    /// (a static prop) found another way. Its identity names the blocks as well.
    /// </summary>
    public HordeNavData WithBlocks(byte[] blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Length != Graph.CellCount)
            throw new ArgumentException($"{blocks.Length} cell states for a graph of {Graph.CellCount} cells", nameof(blocks));
        var open = new NavObstacleState(Graph.ObstacleCount) { OpenDoors = true, Cells = new NavCellBlocks(blocks) };
        ulong identity = Graph.Hash;
        int blocked = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != NavCellBlocks.Blocked)
                continue;
            blocked++;
            identity = (identity ^ (ulong)i) * 0x100000001B3UL;
        }

        return new HordeNavData(Nav, Columns, open, DeriveMs, ColumnsMs, FileBytes, identity, blocked);
    }

    /// <summary>
    /// Whether two cells can be joined for <paramref name="cls"/> at all: in one component of the
    /// class's cells joined by passable edges taken both ways. Cells in two components have no
    /// path; one component can still lack one (a drop taken one way), which the search finds.
    /// </summary>
    public bool Joined(CellId a, CellId b, ClearanceClass cls)
    {
        int c = (int)cls;
        if ((uint)c >= (uint)_components.Length || _components[c].Length == 0)
            return true;
        int[] comp = _components[c];
        return comp[a.Index] >= 0 && comp[a.Index] == comp[b.Index];
    }

    /// <summary>Each cell's component for <paramref name="cls"/> over the passable edges, both ways (-1 for a cell the class does not stand on).</summary>
    private static int[] Components(NavGraph graph, ClearanceClass cls, NavObstacleState open)
    {
        int n = graph.CellCount;
        var comp = new int[n];
        Array.Fill(comp, -1);
        var stack = new Stack<int>();
        int next = 0;
        for (int seed = 0; seed < n; seed++)
        {
            if (comp[seed] >= 0 || !graph.Cell(seed).Admits(cls) || open.CellBlocked(seed))
                continue;
            comp[seed] = next;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                foreach (NavEdge e in graph.EdgesFrom(at))
                {
                    if (comp[e.To] < 0 && graph.Passable(e, cls, open))
                    {
                        comp[e.To] = next;
                        stack.Push(e.To);
                    }
                }

                foreach (NavEdge e in graph.EdgesTo(at))
                {
                    if (comp[e.From] < 0 && graph.Passable(new NavEdge(e.To, e.From, e.Type, e.Cost, e.Obstacle), cls, open))
                    {
                        comp[e.From] = next;
                        stack.Push(e.From);
                    }
                }
            }

            next++;
        }

        return comp;
    }

    /// <summary>The cell a body of <paramref name="cls"/> standing at <paramref name="feet"/> is on (its own, or one within a hull's reach), or none.</summary>
    /// <remarks>
    /// A cell blocked on the graph (beside a prop: <see cref="WithBlocks"/>) is not one: a body
    /// standing there is planned from the nearest free cell of its class within
    /// <see cref="NavPoints.TouchReach"/> (live, a robot pressed against a crate stood in a blocked
    /// cell, every search from it answered unreachable, and it walked the room route into the
    /// crate).
    /// </remarks>
    public CellId CellAt(AiVector feet, ClearanceClass cls)
    {
        CellId c = Graph.FindCell(feet + new AiVector(0f, 0f, 18f));
        if (c.IsValid && Graph.Cell(c).Admits(cls) && !Open.CellBlocked(c.Index))
            return c;
        if (BlockedCells == 0)
            return NavPoints.CellNear(Graph, feet, cls);

        var near = new List<int>();
        float r = NavPoints.TouchReach;
        Graph.CellsNear(feet.X - r, feet.Y - r, feet.X + r, feet.Y + r, near);
        int best = -1;
        float bestD = float.MaxValue;
        foreach (int i in near)
        {
            NavCell cell = Graph.Cell(i);
            if (!cell.Admits(cls) || Open.CellBlocked(i) || cell.FloorZ < feet.Z - 72f || cell.FloorZ > feet.Z + 24f)
                continue;
            float d = (cell.Centre - feet).Length2D;
            if (d < bestD || (d == bestD && i < best))
                (best, bestD) = (i, d);
        }

        return best >= 0 && bestD <= r ? new CellId(best) : CellId.None;
    }

    /// <summary>The cells of <paramref name="cls"/> joined to <paramref name="from"/> by passable edges, as a set by cell.</summary>
    public bool[] Reachable(CellId from, ClearanceClass cls)
    {
        var seen = new bool[Graph.CellCount];
        if (!from.IsValid || !Graph.Cell(from).Admits(cls))
            return seen;
        var open = new Stack<int>();
        seen[from.Index] = true;
        open.Push(from.Index);
        while (open.Count > 0)
        {
            foreach (NavEdge e in Graph.EdgesFrom(open.Pop()))
            {
                if (seen[e.To] || !Graph.Passable(e, cls, Open))
                    continue;
                seen[e.To] = true;
                open.Push(e.To);
            }
        }

        return seen;
    }

    /// <summary>The cells that admit <paramref name="cls"/>.</summary>
    public int CellsOf(ClearanceClass cls)
    {
        int n = 0;
        for (int i = 0; i < Graph.CellCount; i++)
        {
            if (Graph.Cell(i).Admits(cls))
                n++;
        }

        return n;
    }
}
