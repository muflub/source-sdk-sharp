using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>What <see cref="NavHullSplit.Region"/> made: the region's own level, and the whole file's cells its grid starts at.</summary>
/// <param name="Level">The region alone, ready for <see cref="NavHullWriter"/>.</param>
/// <param name="FirstColumn">The whole grid's cell column the region's grid starts at.</param>
/// <param name="FirstRow">The whole grid's cell row it starts at.</param>
/// <param name="CrossingJumps">Jump links of the whole file with one end in the region and the other outside it, left out.</param>
public sealed record NavHullPart(NavHullLevel Level, int FirstColumn, int FirstRow, int CrossingJumps);

/// <summary>
/// One region of a hull navigation as a navigation of its own (plan_bots_teams_tf2_part_g, the
/// user's Q-R1 of 2026-11-13: "a nav3d file per sealed space"): the region's boxes only, on a grid
/// cut down to the cells they are in, so a game can open the file of the space a bot is in and
/// never the whole map's.
/// <list type="bullet">
/// <item><b>The grid</b> is the whole file's, cut to the rectangle of cells the region's boxes are
/// in: the same voxel, cell size and column height, its origin moved by whole cells, so every
/// box keeps its columns and voxels.</item>
/// <item><b>Boxes</b> keep their order (cells are numbered row by row in both grids, so the
/// region's boxes, ascending in the whole file, are in cell order in the cut one). A leaf of
/// <see cref="NavHullNavSource"/> over the part is therefore the leaf
/// <see cref="NavRegionSource"/> numbers the same in the region of the whole file.</item>
/// <item><b>Keys and shut records</b> are those the region's boxes name, renumbered in the order
/// its boxes first name them.</item>
/// <item><b>Obstacles</b> are all the whole file's, in its order, so a graph obstacle is the same
/// record in both; one whose cell is outside the cut grid is placed in its first cell (a reader
/// finds an obstacle's boxes by the shut records, not by its cell).</item>
/// <item><b>Jump links</b> are those with both ends in the region, renumbered.</item>
/// </list>
/// </summary>
public static class NavHullSplit
{
    /// <summary>Region <paramref name="region"/> of <paramref name="regions"/> as a level of its own.</summary>
    /// <param name="reader">The whole file.</param>
    /// <param name="level">The whole file read as a level (<see cref="NavHullReader.ToLevel"/>), shared by every region cut from it.</param>
    /// <param name="regions">The whole file's regions.</param>
    /// <param name="region">The region to cut.</param>
    public static NavHullPart Region(NavHullReader reader, NavHullLevel level, NavHullRegions regions, int region)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(regions);
        if (region < 0 || region >= regions.Count)
            throw new ArgumentOutOfRangeException(nameof(region), region, $"the file has {regions.Count} regions");
        ReadOnlySpan<int> boxes = regions.Boxes(region);
        int cols = reader.Columns;
        int c0 = int.MaxValue, r0 = int.MaxValue, c1 = int.MinValue, r1 = int.MinValue;
        foreach (int box in boxes)
        {
            int cell = reader.BoxCell(box);
            c0 = Math.Min(c0, cell % cols);
            c1 = Math.Max(c1, cell % cols);
            r0 = Math.Min(r0, cell / cols);
            r1 = Math.Max(r1, cell / cols);
        }

        int columns = c1 - c0 + 1, rows = r1 - r0 + 1;
        var starts = new uint[(columns * rows) + 1];
        var newBox = new Dictionary<int, uint>(boxes.Length);
        var outBoxes = new NavHullBox[boxes.Length];
        var keyOf = new Dictionary<uint, uint>();
        var keys = new List<NavHullKey>();
        var shuts = new List<NavHullShut>();
        for (int i = 0; i < boxes.Length; i++)
        {
            int box = boxes[i], cell = reader.BoxCell(box);
            int mine = ((cell / cols) - r0) * columns + ((cell % cols) - c0);
            starts[mine + 1]++;
            NavHullBox b = level.Boxes[box];
            if (!keyOf.TryGetValue(b.Key, out uint key))
            {
                NavHullKey k = level.Keys[(int)b.Key];
                key = (uint)keys.Count;
                keyOf[b.Key] = key;
                keys.Add(k with { ShutFirst = (uint)shuts.Count });
                for (uint s = 0; s < k.ShutCount; s++)
                    shuts.Add(level.Shuts[(int)(k.ShutFirst + s)]);
            }

            outBoxes[i] = b with { Key = key };
            newBox[box] = (uint)i;
        }

        for (int c = 0; c < columns * rows; c++)
            starts[c + 1] += starts[c];

        var jumps = new List<NavHullJump>();
        int crossing = 0;
        foreach (int box in boxes)
        {
            foreach (int j in reader.Jumps(box))
            {
                NavHullJump jump = level.Jumps[j];
                bool a = newBox.TryGetValue((int)jump.BoxA, out uint na), b = newBox.TryGetValue((int)jump.BoxB, out uint nb);
                if (a && b)
                {
                    if (jump.BoxA == (uint)box)
                        jumps.Add(jump with { BoxA = na, BoxB = nb });
                }
                else
                {
                    crossing++;
                }
            }
        }

        jumps.Sort((x, y) =>
        {
            int by = x.BoxA.CompareTo(y.BoxA);
            if (by == 0)
                by = x.BoxB.CompareTo(y.BoxB);
            if (by == 0)
                by = (x.AX, x.AY, x.BX, x.BY).CompareTo((y.AX, y.AY, y.BX, y.BY));
            return by;
        });

        var obstacles = new List<Nav3dObstacle>(level.Obstacles.Count);
        foreach (Nav3dObstacle o in level.Obstacles)
        {
            int oc = (int)(o.Cell % (uint)cols) - c0, or = (int)(o.Cell / (uint)cols) - r0;
            uint cell = oc >= 0 && oc < columns && or >= 0 && or < rows ? (uint)(or * columns + oc) : 0u;
            obstacles.Add(o with { Cell = cell });
        }

        double across = level.CellVoxels * (double)level.VoxelSize;
        var part = new NavHullLevel
        {
            VoxelSize = level.VoxelSize,
            CellVoxels = level.CellVoxels,
            Columns = columns,
            Rows = rows,
            Origin = new Vec3((float)(level.Origin.X + c0 * across), (float)(level.Origin.Y + r0 * across), level.Origin.Z),
            ColumnVoxels = level.ColumnVoxels,
            FloorNormalZ = level.FloorNormalZ,
            StepHeight = level.StepHeight,
            JumpHeight = level.JumpHeight,
            JumpDistance = level.JumpDistance,
            Widths = level.Widths,
            CellStarts = starts,
            Boxes = outBoxes,
            Keys = keys,
            Shuts = shuts,
            Obstacles = obstacles,
            Jumps = jumps,
            Bsp = level.Bsp,
        };
        return new NavHullPart(part, c0, r0, crossing);
    }
}
