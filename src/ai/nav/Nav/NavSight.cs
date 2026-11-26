using System.Collections;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>Whether a point is in the file's free space (not solid, inside the grid).</summary>
public interface IVoxelSpace
{
    bool Free(AiVector p);
}

/// <summary>A sight table that also answers past its near range: the long-line pass (C.3.2), for a Sniper's lines.</summary>
public interface ILongSight
{
    /// <summary>How far <see cref="VisibleFar"/> answers.</summary>
    float LongRange { get; }

    /// <summary>Whether a standing eye at cell <paramref name="a"/> sees one at cell <paramref name="b"/>, out to <see cref="LongRange"/>.</summary>
    bool VisibleFar(int a, int b);
}

/// <summary><see cref="IVoxelSpace"/> over the reader: a point is free when some leaf holds it.</summary>
public sealed class Nav3dVoxelSpace(Nav3dReader reader) : IVoxelSpace
{
    public bool Free(AiVector p) => reader.FindLeaf(new Vec3(p.X, p.Y, p.Z)) >= 0;
}

/// <summary>
/// The static line-of-sight table for cover and vantage (plan_hl2mp_ai §6.2,
/// §5.4): which places see which within 1,024 units, by a voxel ray through
/// the file's free space between standing eyes, with no engine trace. A
/// 16-unit cell per pair would be ~10^8 rays on dm_lockdown, so cells are
/// grouped into sight nodes (128-unit squares per 64-unit floor band, the
/// eye 64 above the group's mean floor) and the table holds, per node, the
/// nodes within range and a bit per one: seen or not. Planning only:
/// perception never reads it (§4.1), and dynamic obstacles are open space
/// here, as in the file.
/// <para>
/// Beside it, the long-line pass (plan_bots_teams_tf2 C.3.2, the Sniper's §4.3 "Spots"): which places see which from
/// <see cref="Range"/> out to <see cref="LongRange"/>, on a coarser grid (<see cref="LongNodeSize"/> squares per floor
/// band), so the pairs it holds stay near the near table's count though its reach is three times as long. The near
/// table's answers, its range and its exposure are unchanged by it: only a reader asking <see cref="ILongSight"/> sees it.
/// </para>
/// </summary>
public sealed class NavSight : ICellSight, ILongSight
{
    public const float Range = 1024f;
    public const float NodeSize = 128f;
    public const float BandHeight = 64f;
    public const float EyeHeight = 64f;
    private const float Step = 8f;

    /// <summary>How far the long-line pass reaches (ours: 2fort's courtyard and granary's yard lines pass 2,048 u).</summary>
    public const float LongRange = 3000f;

    /// <summary>The long pass's grid: a node is a square this wide per <see cref="BandHeight"/> floor band.</summary>
    public const float LongNodeSize = 256f;

    private readonly int[] _nodeOfCell;
    private readonly AiVector[] _eye;
    private readonly int[][] _near;
    private readonly BitArray[] _seen;
    private readonly Layer? _long;
    private readonly float[] _exposure;

    /// <summary>A grid of sight nodes: each cell's node, each node's eye, and per node the nodes in its band of range and whether each is seen.</summary>
    private sealed record Layer(int[] NodeOfCell, AiVector[] Eye, int[][] Near, BitArray[] Seen, long Pairs);

    private NavSight(int[] nodeOfCell, AiVector[] eye, int[][] near, BitArray[] seen, float[] exposure, Layer? longLines)
    {
        _nodeOfCell = nodeOfCell;
        _eye = eye;
        _near = near;
        _seen = seen;
        _long = longLines;
        _exposure = exposure;
    }

    public int NodeCount => _eye.Length;

    float ICellSight.Range => Range;

    /// <summary>How many node pairs the table holds (each direction once).</summary>
    public long Pairs { get; private init; }

    /// <summary>How many node pairs the long-line pass holds (each direction once); 0 without it.</summary>
    public long LongPairs => _long?.Pairs ?? 0;

    /// <summary>How many nodes the long-line pass has; 0 without it.</summary>
    public int LongNodeCount => _long?.Eye.Length ?? 0;

    float ILongSight.LongRange => _long is null ? Range : LongRange;

    /// <summary>
    /// Whether a standing eye over <paramref name="a"/> sees one over <paramref name="b"/> out to <see cref="LongRange"/>: the
    /// near table's answer within <see cref="Range"/>, the long pass's beyond it; false beyond the long range (or without the pass).
    /// </summary>
    public bool VisibleFar(int a, int b)
    {
        int na = _nodeOfCell[a], nb = _nodeOfCell[b];
        if (na == nb || _eye[na].DistanceTo(_eye[nb]) <= Range)
            return Visible(a, b);
        if (_long is null)
            return false;
        int la = _long.NodeOfCell[a], lb = _long.NodeOfCell[b];
        if (la == lb)
            return true;
        int at = Array.BinarySearch(_long.Near[la], lb);
        return at >= 0 && _long.Seen[la][at];
    }

    public int NodeOf(int cell) => _nodeOfCell[cell];

    public AiVector Eye(int node) => _eye[node];

    /// <summary>Whether a standing eye over <paramref name="a"/>'s node sees one over <paramref name="b"/>'s; false beyond <see cref="Range"/>.</summary>
    public bool Visible(int a, int b)
    {
        int na = _nodeOfCell[a], nb = _nodeOfCell[b];
        if (na == nb)
            return true;
        int at = Array.BinarySearch(_near[na], nb);
        return at >= 0 && _seen[na][at];
    }

    /// <summary>The share of the places within <see cref="Range"/> whose eye sees this cell's: the planner's measure of cover.</summary>
    public float Exposure(int cell) => _exposure[_nodeOfCell[cell]];

    /// <summary>Each node's exposure, counted once at the build (a vantage search asks it of every cell it scores: the code review's cost note).</summary>
    private static float[] Exposures(int[][] near, BitArray[] seen)
    {
        var exposure = new float[near.Length];
        for (int node = 0; node < near.Length; node++)
        {
            if (near[node].Length == 0)
                continue;
            int count = 0;
            for (int i = 0; i < near[node].Length; i++)
                count += seen[node][i] ? 1 : 0;
            exposure[node] = count / (float)near[node].Length;
        }

        return exposure;
    }

    /// <summary>The table over <paramref name="graph"/>'s cells, by rays through <paramref name="space"/>; with <paramref name="longLines"/>, the long-line pass too.</summary>
    public static NavSight Build(NavGraph graph, IVoxelSpace space, bool longLines = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(space);
        Layer near = BuildLayer(graph, space, NodeSize, 0f, Range);
        // The long pass answers past the near table's range; its band starts a coarse node's width inside it, so a pair the
        // near table's eyes put just past its range is still in the long pass's.
        Layer? far = longLines ? BuildLayer(graph, space, LongNodeSize, Range - LongNodeSize, LongRange, parallel: true) : null;
        return new NavSight(near.NodeOfCell, near.Eye, near.Near, near.Seen, Exposures(near.Near, near.Seen), far) { Pairs = near.Pairs };
    }

    private static Layer BuildLayer(NavGraph graph, IVoxelSpace space, float nodeSize, float least, float most, bool parallel = false)
    {
        var key = new Dictionary<(int, int, int), int>();
        var nodeOfCell = new int[graph.CellCount];
        var sum = new List<(double X, double Y, double Z, int N)>();
        for (int c = 0; c < graph.CellCount; c++)
        {
            NavCell cell = graph.Cell(c);
            AiVector centre = cell.Centre;
            var k = ((int)MathF.Floor(centre.X / nodeSize), (int)MathF.Floor(centre.Y / nodeSize), (int)MathF.Floor(cell.FloorZ / BandHeight));
            if (!key.TryGetValue(k, out int node))
            {
                node = sum.Count;
                key[k] = node;
                sum.Add((0, 0, 0, 0));
            }

            (double x, double y, double z, int n) = sum[node];
            sum[node] = (x + centre.X, y + centre.Y, z + cell.FloorZ, n + 1);
            nodeOfCell[c] = node;
        }

        // The eye over a node: its cells' mean, or the cell nearest that
        // mean when the mean lands in solid (an L-shaped group).
        var eye = new AiVector[sum.Count];
        for (int node = 0; node < sum.Count; node++)
        {
            (double x, double y, double z, int n) = sum[node];
            eye[node] = new AiVector((float)(x / n), (float)(y / n), (float)(z / n) + EyeHeight);
        }

        for (int c = 0; c < graph.CellCount; c++)
        {
            int node = nodeOfCell[c];
            if (space.Free(eye[node]))
                continue;
            AiVector own = graph.Cell(c).Centre + new AiVector(0, 0, EyeHeight);
            if (space.Free(own))
                eye[node] = own;
        }

        var near = new int[sum.Count][];
        var seen = new BitArray[sum.Count];
        long pairs = 0;
        for (int a = 0; a < sum.Count; a++)
        {
            var list = new List<int>();
            for (int b = 0; b < sum.Count; b++)
            {
                float d = eye[a].DistanceTo(eye[b]);
                if (a != b && d <= most && (least <= 0f || d > least))
                    list.Add(b);
            }

            near[a] = [.. list];
            seen[a] = new BitArray(list.Count);
        }

        if (parallel)
        {
            // Each pair's ray once (a < b), each node's rays on a worker of its own into that node's own row, then the bits set
            // both ways in order: the same answers as the loop below, in a fraction of its time (the voxel space is read-only).
            var clearOf = new bool[sum.Count][];
            Parallel.For(0, sum.Count, a =>
            {
                var row = new bool[near[a].Length];
                for (int i = 0; i < row.Length; i++)
                {
                    int b = near[a][i];
                    if (b > a)
                        row[i] = Clear(space, eye[a], eye[b]);
                }

                clearOf[a] = row;
            });
            for (int a = 0; a < sum.Count; a++)
            {
                for (int i = 0; i < near[a].Length; i++)
                {
                    int b = near[a][i];
                    if (b < a)
                        continue;
                    seen[a][i] = clearOf[a][i];
                    seen[b][Array.BinarySearch(near[b], a)] = clearOf[a][i];
                    pairs++;
                }
            }

            return new Layer(nodeOfCell, eye, near, seen, pairs);
        }

        for (int a = 0; a < sum.Count; a++)
        {
            for (int i = 0; i < near[a].Length; i++)
            {
                int b = near[a][i];
                if (b < a)
                    continue;
                bool clear = Clear(space, eye[a], eye[b]);
                seen[a][i] = clear;
                seen[b][Array.BinarySearch(near[b], a)] = clear;
                pairs++;
            }
        }

        return new Layer(nodeOfCell, eye, near, seen, pairs);
    }

    private static bool Clear(IVoxelSpace space, AiVector a, AiVector b)
    {
        float length = a.DistanceTo(b);
        int samples = Math.Max(1, (int)MathF.Ceiling(length / Step));
        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            if (!space.Free(a + (b - a) * t))
                return false;
        }

        return true;
    }
}
