using System.Globalization;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// <see cref="INavSource"/> over map-tools' <see cref="NavHullReader"/>
/// (plan_bots_teams_tf2_part_g G.7, "a nav for a map this tall"): the hull
/// navigation's boxes, read as the derivation reads a <c>.nav3d</c>'s leaves.
/// <list type="bullet">
/// <item><b>A pseudo-leaf per (box, column).</b> A box covers a rectangle of
/// a cell's voxel columns and a leaf covers one, so a box of w by h columns
/// is w × h leaves, numbered box by box, row by row (y, then x): leaf
/// <c>first(box) + (y − y0) × w + (x − x0)</c>. A leaf's bounds are its
/// column's footprint with the box's bottom and top; its floor, flags and
/// cost are the box's key's.</item>
/// <item><b>Columns</b> are the reader's: the boxes of the column beside
/// (into the next cell too), the box touching from below or above in the
/// leaf's own column, and the jump links whose end is this box at this
/// column.</item>
/// <item><b>Fits</b> are the key's ceiling for the width asked
/// (<see cref="NavHullReader.FitsAt"/>), inside the box's height as
/// <see cref="Nav3dNavSource"/> asks inside a leaf's. The file holds ceilings
/// only for the widths it was built for: <see cref="MissingWidth"/> says
/// which of a game's widths it lacks, and the loader refuses the file by
/// that before anything is derived; asked here, such a width throws.</item>
/// </list>
/// Where the two files part: a box is a column's run of voxels whose answers
/// for the built widths agree, and a <c>.nav3d</c> leaf is a run whose answers
/// for every width agree, so a box is one leaf or several stacked. The places
/// a body stands are the same (a floor starts a box and a leaf alike); what a
/// question about "the leaf that holds this height" sees can differ, where a
/// leaf ends inside a box (NavHullDifferentialTests counts it on real maps).
/// </summary>
public sealed class NavHullNavSource : INavSource
{
    /// <summary>
    /// The numbering's revision: part of a cached graph's name (<see cref="NavLoader.HullKey"/>),
    /// since a cached graph names its cells' leaves. Raise it with any change to which leaf is
    /// which column of which box.
    /// </summary>
    public const int Revision = 1;

    private readonly NavHullReader _reader;

    // Box b's leaves are [_first[b], _first[b + 1]).
    private readonly int[] _first;

    private NavHullNavSource(NavHullReader reader, int[] first)
    {
        _reader = reader;
        _first = first;
    }

    /// <summary>The source over <paramref name="reader"/>: numbers each box's columns.</summary>
    public static NavHullNavSource Over(NavHullReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var first = new int[reader.BoxCount + 1];
        long at = 0;
        for (int box = 0; box < reader.BoxCount; box++)
        {
            first[box] = (int)at;
            NavHullBox b = reader.Box(box);
            at += (b.X1 - b.X0) * (b.Y1 - b.Y0);
            if (at > int.MaxValue)
                throw new InvalidDataException($"the hull navigation's {reader.BoxCount} boxes cover more than {int.MaxValue} columns: too many to number");
        }

        first[reader.BoxCount] = (int)at;
        return new NavHullNavSource(reader, first);
    }

    /// <summary>
    /// The widths the derivation asks of a file for <paramref name="n"/>: the
    /// game's hull, the body it drops past a ledge as (the hull, or two voxels
    /// for a wide one), and each extra hull's.
    /// </summary>
    public static IReadOnlyList<float> WidthsFor(NavNumbers n, float voxelSize)
    {
        var widths = new List<float> { n.HullWidth };
        float fall = n.WideHull ? MathF.Min(n.HullWidth, 2f * voxelSize) : n.HullWidth;
        if (!widths.Contains(fall))
            widths.Add(fall);
        for (int i = 0; i < n.ExtraHulls.Count; i++)
        {
            if (!widths.Contains(n.ExtraHulls[i].Width))
                widths.Add(n.ExtraHulls[i].Width);
        }

        return widths;
    }

    /// <summary>
    /// Why <paramref name="reader"/> cannot answer a derivation by
    /// <paramref name="n"/>, naming <paramref name="file"/> and the width it
    /// holds no ceilings for; null when it holds every one.
    /// </summary>
    public static string? MissingWidth(NavHullReader reader, NavNumbers n, string file)
    {
        ArgumentNullException.ThrowIfNull(reader);
        foreach (float width in WidthsFor(n, reader.VoxelSize))
        {
            if (reader.WidthIndex(width) < 0)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{file} holds no ceilings for a body {width} wide (it was built for {string.Join(", ", reader.Widths.Select(w => w.ToString(CultureInfo.InvariantCulture)))}): "
                    + $"build it with --hull {string.Join(",", WidthsFor(n, reader.VoxelSize).Select(w => w.ToString(CultureInfo.InvariantCulture)))}");
            }
        }

        return null;
    }

    public NavHullReader Reader => _reader;

    public float StepHeight => _reader.StepHeight;

    public float JumpHeight => _reader.JumpHeight;

    public float VoxelSize => _reader.VoxelSize;

    public int LeafCount => _first[^1];

    /// <summary>The box a leaf is a column of.</summary>
    public int BoxOf(int leaf)
    {
        if ((uint)leaf >= (uint)LeafCount)
            throw new ArgumentOutOfRangeException(nameof(leaf), leaf, "no such leaf");
        int at = Array.BinarySearch(_first, leaf);
        return at >= 0 ? at : ~at - 1;
    }

    /// <summary>The leaf of <paramref name="box"/> at column (<paramref name="x"/>, <paramref name="y"/>) of its cell, which its footprint holds.</summary>
    public int LeafOf(int box, int x, int y)
    {
        NavHullBox b = _reader.Box(box);
        return _first[box] + (y - b.Y0) * (b.X1 - b.X0) + (x - b.X0);
    }

    private (int Box, NavHullBox Record, int X, int Y) Locate(int leaf)
    {
        int box = BoxOf(leaf);
        NavHullBox b = _reader.Box(box);
        int offset = leaf - _first[box], width = b.X1 - b.X0;
        return (box, b, b.X0 + offset % width, b.Y0 + offset / width);
    }

    public NavLeaf Leaf(int leaf)
    {
        (int box, NavHullBox b, int x, int y) = Locate(leaf);
        int cell = _reader.BoxCell(box), n = _reader.CellVoxels;
        // The column's corner as the .nav3d reader places a leaf's (Nav3dReader.LeafBounds), to the bit.
        double s = _reader.VoxelSize;
        double x0 = _reader.Origin.X + (((cell % _reader.Columns) * n) + x) * s;
        double y0 = _reader.Origin.Y + (((cell / _reader.Columns) * n) + y) * s;
        float bottom = (float)(_reader.Origin.Z + b.ZLo * s), top = (float)(_reader.Origin.Z + (b.ZLo + b.Height) * s);
        NavHullFlags flags = _reader.Flags(box);
        bool grounded = (flags & NavHullFlags.Grounded) != 0;
        return new NavLeaf((float)x0, (float)y0, (float)(x0 + s), (float)(y0 + s), bottom, top, grounded ? _reader.FloorZ(box) : bottom,
                           grounded && (flags & NavHullFlags.Walkable) != 0, (flags & NavHullFlags.Water) != 0,
                           (flags & NavHullFlags.Ladder) != 0, _reader.CostMultiplier(box));
    }

    public bool Stands(int leaf, float width, float height) => _reader.Stands(BoxOf(leaf), width, height);

    public bool Climbs(int leaf, float width, float height) => _reader.Climbs(BoxOf(leaf), width, height);

    public bool FitsAt(int leaf, float originZ, float width, float height)
    {
        int box = BoxOf(leaf);
        NavHullBox b = _reader.Box(box);
        double bottom = _reader.Origin.Z + b.ZLo * (double)_reader.VoxelSize;
        int z = (int)MathF.Floor((originZ - (float)bottom) / _reader.VoxelSize);
        return z >= 0 && z < b.Height && _reader.FitsAt(box, originZ, width, height);
    }

    public bool FitsAbove(int leaf, float feet, float width, float height)
    {
        int box = BoxOf(leaf);
        NavHullBox b = _reader.Box(box);
        double bottom = _reader.Origin.Z + b.ZLo * (double)_reader.VoxelSize;
        int z = (int)MathF.Ceiling((feet - (float)bottom) / _reader.VoxelSize - 1e-6f);
        return z >= 0 && z < b.Height && _reader.FitsAt(box, feet, width, height);
    }

    public int ColumnLeaves(int leaf, int dx, int dy, Span<int> into)
    {
        (int box, _, int x, int y) = Locate(leaf);
        int cell = _reader.BoxCell(box), n = _reader.CellVoxels;
        int gx = (cell % _reader.Columns) * n + x + dx, gy = (cell / _reader.Columns) * n + y + dy;
        if (gx < 0 || gy < 0 || gx >= _reader.Columns * n || gy >= _reader.Rows * n)
            return 0;
        ReadOnlySpan<int> boxes = _reader.ColumnBoxes((gy / n) * _reader.Columns + gx / n, gx % n, gy % n);
        if (boxes.Length > into.Length)
            throw new InvalidDataException($"a column holds {boxes.Length} boxes; the derivation reads at most {into.Length}");
        for (int i = 0; i < boxes.Length; i++)
            into[i] = LeafOf(boxes[i], gx % n, gy % n);
        return boxes.Length;
    }

    public int Below(int leaf)
    {
        (int box, _, int x, int y) = Locate(leaf);
        int other = _reader.Below(box, x, y);
        return other < 0 ? -1 : LeafOf(other, x, y);
    }

    public int Above(int leaf)
    {
        (int box, _, int x, int y) = Locate(leaf);
        int other = _reader.Above(box, x, y);
        return other < 0 ? -1 : LeafOf(other, x, y);
    }

    public int Jumps(int leaf, Span<NavJumpLink> into)
    {
        (int box, _, int x, int y) = Locate(leaf);
        int count = 0;
        foreach (int j in _reader.Jumps(box))
        {
            // A box's links leave from its columns one by one: this leaf's are those whose end is its column.
            NavHullJump jump = _reader.Jump(j);
            int other;
            if (jump.BoxA == (uint)box && jump.AX == x && jump.AY == y)
                other = LeafOf((int)jump.BoxB, jump.BX, jump.BY);
            else if (jump.BoxB == (uint)box && jump.BX == x && jump.BY == y)
                other = LeafOf((int)jump.BoxA, jump.AX, jump.AY);
            else
                continue;
            if (count >= into.Length)
                throw new InvalidDataException($"leaf {leaf} has more than {into.Length} jump links");
            into[count++] = new NavJumpLink(other, jump.Columns);
        }

        return count;
    }

    public int ObstacleCount => _reader.ObstacleCount;

    public NavObstacleInfo Obstacle(int obstacle) => Nav3dNavSource.InfoOf(_reader.Obstacle(obstacle));

    public int ObstacleLeaves(int obstacle, Span<int> into)
    {
        int count = 0;
        foreach (int box in _reader.ObstacleBoxes(obstacle))
        {
            int leaves = _first[box + 1] - _first[box];
            if (count + leaves > into.Length)
                throw new InvalidDataException($"obstacle {obstacle} names more than {into.Length} leaves");
            for (int i = 0; i < leaves; i++)
                into[count++] = _first[box] + i;
        }

        return count;
    }

    public bool StandsWithShut(int leaf, int obstacle, float width, float height) => _reader.StandsWithShut(BoxOf(leaf), obstacle, width, height);
}

/// <summary><see cref="IVoxelSpace"/> over the hull navigation: a point is free when some box holds it (a player clip's space too, as a <c>.nav3d</c> leaf holds it).</summary>
public sealed class NavHullVoxelSpace(NavHullReader reader) : IVoxelSpace
{
    public bool Free(AiVector p) => reader.FindBox(new Vec3(p.X, p.Y, p.Z)) >= 0;
}
