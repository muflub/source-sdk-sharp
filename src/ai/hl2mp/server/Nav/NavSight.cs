using System.Collections;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>Whether a point is in the file's free space (not solid, inside the grid).</summary>
public interface IVoxelSpace
{
    bool Free(AiVector p);
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
/// </summary>
public sealed class NavSight
{
    public const float Range = 1024f;
    public const float NodeSize = 128f;
    public const float BandHeight = 64f;
    public const float EyeHeight = 64f;
    private const float Step = 8f;

    private readonly int[] _nodeOfCell;
    private readonly AiVector[] _eye;
    private readonly int[][] _near;
    private readonly BitArray[] _seen;

    private NavSight(int[] nodeOfCell, AiVector[] eye, int[][] near, BitArray[] seen)
    {
        _nodeOfCell = nodeOfCell;
        _eye = eye;
        _near = near;
        _seen = seen;
    }

    public int NodeCount => _eye.Length;

    /// <summary>How many node pairs the table holds (each direction once).</summary>
    public long Pairs { get; private init; }

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

    public static NavSight Build(NavGraph graph, IVoxelSpace space)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(space);
        var key = new Dictionary<(int, int, int), int>();
        var nodeOfCell = new int[graph.CellCount];
        var sum = new List<(double X, double Y, double Z, int N)>();
        for (int c = 0; c < graph.CellCount; c++)
        {
            NavCell cell = graph.Cell(c);
            AiVector centre = cell.Centre;
            var k = ((int)MathF.Floor(centre.X / NodeSize), (int)MathF.Floor(centre.Y / NodeSize), (int)MathF.Floor(cell.FloorZ / BandHeight));
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
                if (a != b && eye[a].DistanceTo(eye[b]) <= Range)
                    list.Add(b);
            }

            near[a] = [.. list];
            seen[a] = new BitArray(list.Count);
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

        return new NavSight(nodeOfCell, eye, near, seen) { Pairs = pairs };
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
