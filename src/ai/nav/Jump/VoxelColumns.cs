using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>One stretch of a column a body may be in: from the floor under it to the solid over it.</summary>
public readonly record struct FreeSpan(float Bottom, float Top);

/// <summary>
/// The free space a flight is simulated through (plan_bots_teams_tf2_part_g
/// G.5 step 3): a grid of square columns, and per column the vertical spans
/// that are free. Everything else is solid, and so is everything outside the
/// grid. Immutable once built, so any number of worker threads read one.
///
/// <para>
/// <b>What it knows of a map, and how well.</b> Built from the nav
/// (<see cref="FromNav"/>): a column is one voxel column of the
/// <c>.nav3d</c>, and a span is a run of its leaves that touch. Sideways it
/// is no finer than a voxel: a wall that stands inside a column fills the
/// column, so free space is narrower than the map's by up to a voxel a side.
/// A span's floor is the leaf's own floor height where the file gives one
/// (the surface under a grounded leaf, to the unit), else the voxel's
/// bottom. A span's top is the top of its highest free voxel: a ceiling
/// that hangs inside a voxel is up to a voxel low. On a map built on the
/// voxel grid all three are exact.
/// </para>
/// </summary>
public sealed class VoxelColumns
{
    private readonly int[] _start;
    private readonly FreeSpan[] _spans;

    private VoxelColumns(float originX, float originY, float voxelSize, int width, int depth, int[] start, FreeSpan[] spans)
    {
        OriginX = originX;
        OriginY = originY;
        VoxelSize = voxelSize;
        Width = width;
        Depth = depth;
        _start = start;
        _spans = spans;
    }

    /// <summary>The grid's low corner.</summary>
    public float OriginX { get; }

    public float OriginY { get; }

    /// <summary>A column's side.</summary>
    public float VoxelSize { get; }

    /// <summary>Columns along x.</summary>
    public int Width { get; }

    /// <summary>Columns along y.</summary>
    public int Depth { get; }

    /// <summary>The free spans of column (<paramref name="x"/>, <paramref name="y"/>), bottom to top; none outside the grid.</summary>
    public ReadOnlySpan<FreeSpan> Spans(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Depth)
            return default;
        int column = y * Width + x;
        return _spans.AsSpan(_start[column], _start[column + 1] - _start[column]);
    }

    /// <summary>The column a coordinate lies in along one axis (it may be outside the grid).</summary>
    public int ColumnX(float x) => (int)MathF.Floor((x - OriginX) / VoxelSize);

    public int ColumnY(float y) => (int)MathF.Floor((y - OriginY) / VoxelSize);

    /// <summary>
    /// A grid whose columns are what <paramref name="spans"/> answers for each
    /// (x, y): bottom to top, no two touching or overlapping.
    /// </summary>
    public static VoxelColumns Of(float originX, float originY, float voxelSize, int width, int depth, Func<int, int, FreeSpan[]> spans)
    {
        if (!(voxelSize > 0f) || width <= 0 || depth <= 0)
            throw new ArgumentException($"a grid of {width} by {depth} columns of {voxelSize} units is no grid");
        var start = new int[width * depth + 1];
        var all = new List<FreeSpan>(width * depth);
        for (int y = 0; y < depth; y++)
        {
            for (int x = 0; x < width; x++)
            {
                start[y * width + x] = all.Count;
                float below = float.NegativeInfinity;
                foreach (FreeSpan s in spans(x, y))
                {
                    if (!(s.Top > s.Bottom) || !(s.Bottom > below))
                        throw new ArgumentException($"column ({x}, {y}): the span {s.Bottom} to {s.Top} is empty or not above the one under it");
                    below = s.Top;
                    all.Add(s);
                }
            }
        }

        start[width * depth] = all.Count;
        return new VoxelColumns(originX, originY, voxelSize, width, depth, start, [.. all]);
    }

    /// <summary>
    /// The nav's free space: each leaf's column from its floor to its top,
    /// leaves that touch joined into one span.
    /// </summary>
    public static VoxelColumns FromNav(INavSource nav)
    {
        float voxel = nav.VoxelSize;
        if (nav.LeafCount == 0)
            throw new ArgumentException("the nav has no leaf: there is no free space to fly through");
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < nav.LeafCount; i++)
        {
            NavLeaf l = nav.Leaf(i);
            minX = MathF.Min(minX, l.MinX);
            minY = MathF.Min(minY, l.MinY);
            maxX = MathF.Max(maxX, l.MaxX);
            maxY = MathF.Max(maxY, l.MaxY);
        }

        int width = (int)MathF.Round((maxX - minX) / voxel), depth = (int)MathF.Round((maxY - minY) / voxel);
        var columns = new List<NavLeaf>?[width * depth];
        for (int i = 0; i < nav.LeafCount; i++)
        {
            NavLeaf l = nav.Leaf(i);
            int x = (int)MathF.Round((l.MinX - minX) / voxel), y = (int)MathF.Round((l.MinY - minY) / voxel);
            (columns[y * width + x] ??= []).Add(l);
        }

        return Of(minX, minY, voxel, width, depth, (x, y) =>
        {
            if (columns[y * width + x] is not { } leaves)
                return [];
            leaves.Sort(static (a, b) => a.BottomZ.CompareTo(b.BottomZ));
            var spans = new List<FreeSpan>(leaves.Count);
            foreach (NavLeaf l in leaves)
            {
                // A leaf that starts where the one under it ends is the same free space, split where the file's clearance changes.
                if (spans.Count > 0 && l.BottomZ <= spans[^1].Top)
                    spans[^1] = spans[^1] with { Top = MathF.Max(spans[^1].Top, l.TopZ) };
                else
                    spans.Add(new FreeSpan(MathF.Min(l.FloorZ, l.BottomZ), l.TopZ));
            }

            return [.. spans];
        });
    }
}
