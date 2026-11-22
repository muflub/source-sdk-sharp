namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// A derived graph made smaller after the derivation, offline (plan_bots_teams_tf2_part_g, the
/// user's Q-R3 of 2026-11-13: "we need to find a way to simplify the nav mesh for these sealed
/// sections"). Neither rule changes <see cref="NavGraphFromNav3d.Derive"/>: each takes a
/// <see cref="DerivedNav"/> and answers another.
/// <list type="bullet">
/// <item><see cref="Keep"/>: the cells a mask keeps, renumbered in order, and the edges between
/// two of them; a cell of a leaf left out answers -1 for it.</item>
/// <item><see cref="Coarsen"/>: a k by k block of columns of one flat floor as one cell. A cell is
/// <i>flat</i> when the eight columns around it each hold a cell on the same floor that admits
/// the same bodies with the same flags and cost, and no edge of it crosses an obstacle. A block
/// (k by k columns, aligned to k) is merged when all its columns hold such cells on one floor;
/// every other cell stays as it was, so the cells along an edge, a wall, a ledge or a door are
/// the derivation's own. A merged cell's footprint is the block's, its floor and classes the
/// members'; its edges are the members' edges to outside the block, one per neighbour (a walk
/// kept before any other kind, costed as the derivation costs its kind), and its leaf the block's
/// first member's.</item>
/// </list>
/// </summary>
public static class NavGraphReduce
{
    /// <summary>The cells <paramref name="keep"/> marks, and the edges between two of them.</summary>
    public static DerivedNav Keep(DerivedNav nav, bool[] keep, NavEdgeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(keep);
        NavGraph g = nav.Graph;
        if (keep.Length != g.CellCount)
            throw new ArgumentException($"the mask has {keep.Length} entries for {g.CellCount} cells", nameof(keep));
        var groups = new int[g.CellCount];
        int kept = 0;
        for (int c = 0; c < g.CellCount; c++)
            groups[c] = keep[c] ? kept++ : -1;
        return Rebuild(nav, groups, kept, null, costs);
    }

    /// <summary>Every flat cell of <paramref name="nav"/> (see the type's summary), by index.</summary>
    /// <param name="voxel">The columns' width: the file's voxel.</param>
    public static bool[] Flat(DerivedNav nav, float voxel)
    {
        ArgumentNullException.ThrowIfNull(nav);
        NavGraph g = nav.Graph;
        var columns = Columns(g, voxel, out var at);
        var flat = new bool[g.CellCount];
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            if ((cell.Flags & NavCellFlags.Ladder) != 0 || CrossesObstacle(g, c))
                continue;
            (int x, int y) = at[c];
            bool all = true;
            for (int dy = -1; dy <= 1 && all; dy++)
            {
                for (int dx = -1; dx <= 1 && all; dx++)
                {
                    if (dx != 0 || dy != 0)
                        all = Same(g, columns, x + dx, y + dy, cell) >= 0;
                }
            }

            flat[c] = all;
        }

        return flat;
    }

    /// <summary><paramref name="nav"/> with each k by k block of flat columns on one floor merged into a cell (see the type's summary).</summary>
    /// <param name="voxel">The columns' width: the file's voxel.</param>
    /// <param name="k">The block's columns a side, 2 or more.</param>
    public static DerivedNav Coarsen(DerivedNav nav, float voxel, int k, NavEdgeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 2);
        NavGraph g = nav.Graph;
        bool[] flat = Flat(nav, voxel);
        var columns = Columns(g, voxel, out var at);
        var group = new int[g.CellCount];
        Array.Fill(group, -1);
        var footprint = new List<(float MinX, float MinY, float MaxX, float MaxY)>();
        int groups = 0;
        var members = new int[k * k];
        for (int c = 0; c < g.CellCount; c++)
        {
            if (group[c] >= 0)
                continue;
            (int x, int y) = at[c];
            NavCell cell = g.Cell(c);
            bool merge = flat[c];
            int bx = FloorDiv(x, k) * k, by = FloorDiv(y, k) * k, n = 0;
            for (int dy = 0; dy < k && merge; dy++)
            {
                for (int dx = 0; dx < k && merge; dx++)
                {
                    int m = Same(g, columns, bx + dx, by + dy, cell);
                    merge = m >= 0 && flat[m] && group[m] < 0;
                    if (merge)
                        members[n++] = m;
                }
            }

            if (!merge)
            {
                group[c] = groups++;
                footprint.Add((cell.MinX, cell.MinY, cell.MaxX, cell.MaxY));
                continue;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                group[members[i]] = groups;
                NavCell mc = g.Cell(members[i]);
                (minX, minY, maxX, maxY) = (MathF.Min(minX, mc.MinX), MathF.Min(minY, mc.MinY), MathF.Max(maxX, mc.MaxX), MathF.Max(maxY, mc.MaxY));
            }

            footprint.Add((minX, minY, maxX, maxY));
            groups++;
        }

        return Rebuild(nav, group, groups, footprint, costs);
    }

    // Cells are numbered by group in the order of their first member; a group's cell is its first member's with the footprint given.
    private static DerivedNav Rebuild(DerivedNav nav, int[] group, int groups, List<(float MinX, float MinY, float MaxX, float MaxY)>? footprint, NavEdgeCosts costs)
    {
        NavGraph g = nav.Graph;
        var first = new int[groups];
        Array.Fill(first, -1);
        for (int c = 0; c < g.CellCount; c++)
        {
            if (group[c] >= 0 && first[group[c]] < 0)
                first[group[c]] = c;
        }

        var b = new NavGraphBuilder();
        for (int i = 0; i < groups; i++)
        {
            NavCell cell = g.Cell(first[i]);
            if (footprint is not null)
                cell = cell with { MinX = footprint[i].MinX, MinY = footprint[i].MinY, MaxX = footprint[i].MaxX, MaxY = footprint[i].MaxY };
            b.AddCell(cell);
        }

        for (int o = 0; o < g.ObstacleCount; o++)
            b.AddObstacle();

        // A group's edges to each other group: a walk first, else the first of another kind met.
        var chosen = new Dictionary<int, NavEdge>();
        var order = new List<int>();
        var byGroup = new List<int>[groups];
        for (int c = 0; c < g.CellCount; c++)
        {
            if (group[c] >= 0)
                (byGroup[group[c]] ??= new List<int>()).Add(c);
        }

        for (int i = 0; i < groups; i++)
        {
            chosen.Clear();
            order.Clear();
            foreach (int c in byGroup[i])
            {
                foreach (NavEdge e in g.EdgesFrom(c))
                {
                    int to = group[e.To];
                    if (to < 0 || to == i)
                        continue;
                    if (!chosen.TryGetValue(to, out NavEdge had))
                    {
                        chosen[to] = e;
                        order.Add(to);
                    }
                    else if (had.Type != NavEdgeType.Walk && e.Type == NavEdgeType.Walk)
                    {
                        chosen[to] = e;
                    }
                }
            }

            foreach (int to in order)
            {
                // The kind's own extra, as the derivation and the cache give it.
                NavEdge e = chosen[to];
                b.AddEdge(new CellId(i), new CellId(to), e.Type, costs.ExtraFor(e.Type), e.Obstacle);
            }
        }

        var cellOfLeaf = new int[nav.Leaves];
        Array.Fill(cellOfLeaf, -1);
        for (int leaf = 0; leaf < nav.Leaves; leaf++)
        {
            int c = nav.CellOfLeaf[leaf];
            if (c >= 0)
                cellOfLeaf[leaf] = group[c];
        }

        var leafOfCell = new int[groups];
        for (int i = 0; i < groups; i++)
            leafOfCell[i] = nav.LeafOfCell[first[i]];
        return new DerivedNav(b.Build(), cellOfLeaf, leafOfCell, nav.Obstacles, nav.Leaves);
    }

    private static int FloorDiv(int a, int k) => a >= 0 ? a / k : -((-a + k - 1) / k);

    private static bool CrossesObstacle(NavGraph g, int c)
    {
        foreach (NavEdge e in g.EdgesFrom(c))
        {
            if (e.Obstacle >= 0)
                return true;
        }

        foreach (NavEdge e in g.EdgesTo(c))
        {
            if (e.Obstacle >= 0)
                return true;
        }

        return false;
    }

    // The cell of column (x, y) on the same floor as <paramref name="like"/>, admitting the same bodies with the same flags and cost, or -1.
    private static int Same(NavGraph g, Dictionary<(int, int), int[]> columns, int x, int y, in NavCell like)
    {
        if (!columns.TryGetValue((x, y), out int[]? cells))
            return -1;
        foreach (int c in cells)
        {
            NavCell o = g.Cell(c);
            if (o.FloorZ == like.FloorZ && o.Classes == like.Classes && o.Flags == like.Flags && o.CostMultiplier == like.CostMultiplier)
                return c;
        }

        return -1;
    }

    // Each column's cells, the column a cell's footprint's low corner is in, counted from the lowest corner of any.
    private static Dictionary<(int, int), int[]> Columns(NavGraph g, float voxel, out (int X, int Y)[] at)
    {
        at = new (int, int)[g.CellCount];
        float x0 = float.MaxValue, y0 = float.MaxValue;
        for (int c = 0; c < g.CellCount; c++)
        {
            x0 = MathF.Min(x0, g.Cell(c).MinX);
            y0 = MathF.Min(y0, g.Cell(c).MinY);
        }

        var lists = new Dictionary<(int, int), List<int>>();
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            var key = ((int)MathF.Round((cell.MinX - x0) / voxel), (int)MathF.Round((cell.MinY - y0) / voxel));
            at[c] = key;
            if (!lists.TryGetValue(key, out List<int>? list))
                lists[key] = list = new List<int>(1);
            list.Add(c);
        }

        return lists.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }
}
