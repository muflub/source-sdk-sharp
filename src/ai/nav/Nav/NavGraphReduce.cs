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
/// <item><see cref="DeriveCoarse"/>: the derivation and <see cref="Coarsen"/> together, a tile of
/// columns at a time, so a big room's graph is never whole at the voxel (the user's Q-R5 of
/// 2026-11-16); the graph is <see cref="Coarsen"/>'s of <see cref="NavGraphFromNav3d.Derive"/>'s, cell
/// for cell and edge for edge.</item>
/// </list>
/// </summary>
public static class NavGraphReduce
{
    /// <summary>
    /// The corner of a column that <see cref="DeriveCoarse"/> counts columns and blocks from: the
    /// world's origin, moved to the source's columns (the remainder of a leaf's corner over the
    /// voxel), so a room cut from a map has the map's blocks.
    /// </summary>
    public static (float X, float Y) GridOrigin(INavSource src)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (src.LeafCount == 0)
            return (0f, 0f);
        NavLeaf l = src.Leaf(0);
        float v = src.VoxelSize;
        return (l.MinX - MathF.Floor(l.MinX / v) * v, l.MinY - MathF.Floor(l.MinY / v) * v);
    }

    /// <summary>
    /// How many columns past a tile <see cref="DeriveCoarse"/> derives, for the tile's own cells
    /// and edges to be the whole derivation's: a cell's edges reach two columns, a climb's on and
    /// off the hull's half-width and half a voxel, an extra hull's floor (its highest step) the
    /// columns its box overlaps, and a flat cell asks the column beside of its floor and bodies.
    /// </summary>
    public static int TileMargin(NavNumbers n, float voxel)
    {
        int edges = Math.Max(2, (int)MathF.Ceiling((n.HullWidth * 0.5f + voxel * 0.5f) / voxel));
        int hulls = 0;
        for (int i = 0; i < n.ExtraHulls.Count; i++)
            hulls = Math.Max(hulls, (int)MathF.Ceiling((n.ExtraHulls[i].Width * 0.5f + voxel * 0.5f) / voxel - 1e-4f) - 1);
        return Math.Max(edges, 1 + hulls);
    }

    /// <summary>
    /// <see cref="Coarsen"/>(<see cref="NavGraphFromNav3d.Derive"/>(...), k, <see cref="GridOrigin"/>),
    /// made a tile of <paramref name="tileColumns"/> by <paramref name="tileColumns"/> columns at a
    /// time (the user's Q-R5 of 2026-11-16: a big room is never made whole at the voxel first).
    /// Each tile is derived over a <see cref="NavRegionSource"/> of its columns, the columns
    /// <see cref="TileMargin"/> around them, and the far ends of its jump links, so its own cells
    /// and their edges are the whole derivation's; its blocks are merged there (a tile is a whole
    /// number of blocks), and only its merged cells and their edges are kept. An edge to a cell of
    /// another tile is joined to that cell's block once every tile is done. Cells inside
    /// <paramref name="hurt"/> are left out as the derivation leaves them; each tile is asked only
    /// of the volumes over its leaves.
    /// </summary>
    /// <param name="tileColumns">A tile's columns a side: a whole number of blocks.</param>
    /// <param name="stats">When given, filled with what the work took.</param>
    /// <param name="threads">How many tiles are derived at once.</param>
    /// <param name="scheduler">The threads they are derived on (the pool's without one).</param>
    public static DerivedNav DeriveCoarse(INavSource src, NavNumbers n, NavEdgeCosts costs, IReadOnlyList<HurtVolume> hurt,
                                          IReadOnlyList<LadderClimb>? ladders, int k = 4, int tileColumns = 64, CoarseDeriveStats? stats = null,
                                          int threads = 1, TaskScheduler? scheduler = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(hurt);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
        if (tileColumns < k || tileColumns % k != 0)
            throw new ArgumentOutOfRangeException(nameof(tileColumns), tileColumns, $"a tile is a whole number of {k}-column blocks");
        int leaves = src.LeafCount;
        float v = src.VoxelSize;
        (float ox, float oy) = GridOrigin(src);
        int margin = TileMargin(n, v);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        // Each leaf's column, and the tiles whose columns or margin hold it.
        var colX = new int[leaves];
        var colY = new int[leaves];
        if (src is NavHullNavSource hullSource)
        {
            hullSource.LeafColumns(colX, colY, (ox, oy));
        }
        else
        {
            for (int leaf = 0; leaf < leaves; leaf++)
            {
                NavLeaf l = src.Leaf(leaf);
                colX[leaf] = (int)MathF.Round((l.MinX - ox) / v);
                colY[leaf] = (int)MathF.Round((l.MinY - oy) / v);
            }
        }

        var tiles = new Dictionary<(int X, int Y), CoarseTile>();
        CoarseTile TileAt(int tx, int ty)
        {
            if (!tiles.TryGetValue((tx, ty), out CoarseTile? tile))
                tiles[(tx, ty)] = tile = new CoarseTile(tx, ty);
            return tile;
        }

        for (int leaf = 0; leaf < leaves; leaf++)
        {
            int cx = colX[leaf], cy = colY[leaf];
            int tx0 = FloorDiv(cx - margin, tileColumns), tx1 = FloorDiv(cx + margin, tileColumns);
            int ty0 = FloorDiv(cy - margin, tileColumns), ty1 = FloorDiv(cy + margin, tileColumns);
            for (int ty = ty0; ty <= ty1; ty++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                    TileAt(tx, ty).Leaves.Add(leaf);
            }

            TileAt(FloorDiv(cx, tileColumns), FloorDiv(cy, tileColumns)).Core++;
        }

        // The far ends of each tile's jump links, so its cells' links are the whole derivation's.
        void Far(int from, int to) => tiles[(FloorDiv(colX[from], tileColumns), FloorDiv(colY[from], tileColumns))].Far.Add(to);
        if (src is NavHullNavSource pairs)
        {
            foreach ((int a, int b) in pairs.JumpPairs())
            {
                Far(a, b);
                Far(b, a);
            }
        }
        else
        {
            var links = new NavJumpLink[512];
            for (int leaf = 0; leaf < leaves; leaf++)
            {
                int count = src.Jumps(leaf, links);
                for (int j = 0; j < count; j++)
                    Far(leaf, links[j].Leaf);
            }
        }

        var infos = new NavObstacleInfo[src.ObstacleCount];
        for (int o = 0; o < infos.Length; o++)
            infos[o] = src.Obstacle(o);
        var keyOfLeaf = new int[leaves];
        Array.Fill(keyOfLeaf, -1);
        CoarseTile[] work = [.. tiles.Values.Where(t => t.Core > 0).OrderBy(t => t.Y).ThenBy(t => t.X)];
        tiles.Clear();
        var scratch = NavRegionScratch.For(src);
        var spare = new System.Collections.Concurrent.ConcurrentBag<NavRegionScratch> { scratch };
        double prepareMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long tilesStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads, TaskScheduler = scheduler ?? TaskScheduler.Default, CancellationToken = cancel };
        Parallel.For(0, work.Length, options, i =>
        {
            CoarseTile tile = work[i];
            NavRegionScratch mine = spare.TryTake(out NavRegionScratch? s) ? s : scratch.Fork();
            try
            {
                DeriveTile(tile, src, n, costs, hurt, ladders, k, tileColumns, margin, v, (ox, oy), colX, colY, mine, keyOfLeaf);
            }
            finally
            {
                spare.Add(mine);
            }
        });

        cancel.ThrowIfCancellationRequested();
        double tilesMs = System.Diagnostics.Stopwatch.GetElapsedTime(tilesStart).TotalMilliseconds;
        long assembleStart = System.Diagnostics.Stopwatch.GetTimestamp();
        DerivedNav result = Assemble(src, work, keyOfLeaf, infos, costs);
        double assembleMs = System.Diagnostics.Stopwatch.GetElapsedTime(assembleStart).TotalMilliseconds;
        if (stats is not null)
        {
            stats.Tiles = work.Length;
            stats.LeavesDerived = work.Sum(t => (long)t.Derived);
            stats.Leaves = leaves;
            stats.VoxelCells = work.Sum(t => (long)t.Own);
            stats.BiggestTileCells = work.Length == 0 ? 0 : work.Max(t => t.Cells);
            stats.Margin = margin;
            stats.PrepareMs = prepareMs;
            stats.TilesMs = tilesMs;
            stats.AssembleMs = assembleMs;
            stats.DeriveMs = work.Sum(t => t.DeriveMs);
            stats.MergeMs = work.Sum(t => t.MergeMs);
        }

        return result;
    }

    // A tile of DeriveCoarse: the leaves of its columns and margin, how many are its own, the far ends of its own leaves' links;
    // once derived, its groups and what it took.
    private sealed class CoarseTile(int x, int y)
    {
        public int X { get; } = x;

        public int Y { get; } = y;

        public List<int> Leaves { get; } = new();

        public List<int> Far { get; } = new();

        public int Core { get; set; }

        public List<CoarseGroup> Groups { get; } = new();

        public int Derived { get; set; }

        public int Own { get; set; }

        public int Cells { get; set; }

        public double DeriveMs { get; set; }

        public double MergeMs { get; set; }
    }

    private static void DeriveTile(CoarseTile tile, INavSource src, NavNumbers n, NavEdgeCosts costs, IReadOnlyList<HurtVolume> hurt,
                                   IReadOnlyList<LadderClimb>? ladders, int k, int tileColumns, int margin, float v, (float X, float Y) origin,
                                   int[] colX, int[] colY, NavRegionScratch scratch, int[] keyOfLeaf)
    {
        (float ox, float oy) = origin;
        (int tx, int ty) = (tile.X, tile.Y);

        // The volumes over the tile's columns and its margin, and over each far end of a link.
        float wx0 = ox + (tx * tileColumns - margin) * v, wy0 = oy + (ty * tileColumns - margin) * v;
        float wx1 = ox + ((tx + 1) * tileColumns + margin) * v, wy1 = oy + ((ty + 1) * tileColumns + margin) * v;
        var tileHurt = new List<HurtVolume>();
        var taken = new bool[hurt.Count];
        for (int h = 0; h < hurt.Count; h++)
        {
            if (Overlaps(hurt[h], wx0, wy0, wx1, wy1))
            {
                taken[h] = true;
                tileHurt.Add(hurt[h]);
            }
        }

        foreach (int far in tile.Far)
        {
            float x0 = ox + colX[far] * v, y0 = oy + colY[far] * v;
            for (int h = 0; h < hurt.Count; h++)
            {
                if (!taken[h] && Overlaps(hurt[h], x0, y0, x0 + v, y0 + v))
                {
                    taken[h] = true;
                    tileHurt.Add(hurt[h]);
                }
            }
        }

        bool Core(int leaf) => FloorDiv(colX[leaf], tileColumns) == tx && FloorDiv(colY[leaf], tileColumns) == ty;
        NavRegionSource region = NavRegionSource.Over(scratch, tile.Leaves.Concat(tile.Far));
        try
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            DerivedNav d = NavGraphFromNav3d.Derive(region, n, costs, tileHurt, ladders);
            tile.DeriveMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            tile.Derived = region.LeafCount;
            tile.Cells = d.Graph.CellCount;
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            tile.Own = MergeTile(d, region, Core, v, origin, k, keyOfLeaf, tile.Groups);
            tile.MergeMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        finally
        {
            region.Release();
        }

        // The tile's leaves are not needed again.
        tile.Leaves.Clear();
        tile.Leaves.TrimExcess();
    }

    private static bool Overlaps(in HurtVolume h, float x0, float y0, float x1, float y1)
        => x1 > h.Min.X && x0 < h.Max.X && y1 > h.Min.Y && y0 < h.Max.Y;

    // A merged cell (or one kept as it was) of DeriveCoarse: its first member's leaf, the cell, and its edges.
    private readonly record struct CoarseGroup(int Key, NavCell Cell, CoarseEdge[] Edges);

    // An edge of a group to a target: a group's key when the target's tile was done (Resolved), else the target cell's leaf;
    // the first edge met to it and the first walk (WalkAt -1 for none), by where each came in the group's edges.
    private struct CoarseEdge
    {
        public int Target;
        public bool Resolved;
        public NavEdgeType Type;
        public int Obstacle;
        public int WalkAt;
        public int WalkObstacle;
    }

    // One tile: Coarsen's blocks of its own cells, as groups with their edges. Answers how many of its own cells there were.
    private static int MergeTile(DerivedNav d, NavRegionSource region, Func<int, bool> core, float voxel, (float X, float Y) origin, int k,
                                 int[] keyOfLeaf, List<CoarseGroup> groups)
    {
        NavGraph g = d.Graph;
        int cells = g.CellCount;
        var mine = new bool[cells];
        var leafOf = new int[cells];
        int own = 0;
        for (int c = 0; c < cells; c++)
        {
            leafOf[c] = region.InnerLeaf(d.LeafOfCell[c]);
            mine[c] = core(leafOf[c]);
            own += mine[c] ? 1 : 0;
        }

        var columns = Columns(g, voxel, origin, out var at);
        var flat = new bool[cells];
        for (int c = 0; c < cells; c++)
            flat[c] = mine[c] && IsFlat(g, columns, at, c);

        // Blocks, as Coarsen makes them; a group's key is its first member's leaf.
        var key = new int[cells];
        Array.Fill(key, -1);
        var members = new int[k * k];
        var tileGroups = new List<(int Key, NavCell Cell, int[] Members)>();
        for (int c = 0; c < cells; c++)
        {
            if (!mine[c] || key[c] >= 0)
                continue;
            (int x, int y) = at[c];
            NavCell cell = g.Cell(c);
            bool merge = flat[c];
            int bx = FloorDiv(x, k) * k, by = FloorDiv(y, k) * k, m = 0;
            for (int dy = 0; dy < k && merge; dy++)
            {
                for (int dx = 0; dx < k && merge; dx++)
                {
                    int o = Same(g, columns, bx + dx, by + dy, cell);
                    merge = o >= 0 && flat[o] && key[o] < 0;
                    if (merge)
                        members[m++] = o;
                }
            }

            if (!merge)
            {
                key[c] = leafOf[c];
                tileGroups.Add((leafOf[c], cell, [c]));
                continue;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int[] block = members.AsSpan(0, m).ToArray();
            Array.Sort(block);
            foreach (int o in block)
            {
                key[o] = leafOf[c];
                NavCell mc = g.Cell(o);
                (minX, minY, maxX, maxY) = (MathF.Min(minX, mc.MinX), MathF.Min(minY, mc.MinY), MathF.Max(maxX, mc.MaxX), MathF.Max(maxY, mc.MaxY));
            }

            tileGroups.Add((leafOf[c], cell with { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY }, block));
        }

        for (int c = 0; c < cells; c++)
        {
            if (mine[c])
                keyOfLeaf[leafOf[c]] = key[c];
        }

        // Each group's edges, in Rebuild's order: its members in order, each one's edges in order.
        var resolved = new Dictionary<int, int>();
        var open = new Dictionary<int, int>();
        var edges = new List<CoarseEdge>();
        foreach ((int groupKey, NavCell cell, int[] block) in tileGroups)
        {
            resolved.Clear();
            open.Clear();
            edges.Clear();
            int seen = 0;
            foreach (int c in block)
            {
                foreach (NavEdge e in g.EdgesFrom(c))
                {
                    int position = seen++;
                    bool known = mine[e.To];
                    int target = known ? key[e.To] : leafOf[e.To];
                    if (known && target == groupKey)
                        continue;
                    Dictionary<int, int> index = known ? resolved : open;
                    if (!index.TryGetValue(target, out int i))
                    {
                        index[target] = i = edges.Count;
                        edges.Add(new CoarseEdge { Target = target, Resolved = known, Type = e.Type, Obstacle = e.Obstacle, WalkAt = -1, WalkObstacle = -1 });
                    }

                    if (e.Type == NavEdgeType.Walk && edges[i].WalkAt < 0)
                    {
                        CoarseEdge p = edges[i];
                        p.WalkAt = position;
                        p.WalkObstacle = e.Obstacle;
                        edges[i] = p;
                    }
                }
            }

            groups.Add(new CoarseGroup(groupKey, cell, [.. edges]));
        }

        return own;
    }

    // The graph of every tile's groups, numbered by their first member's leaf, as Rebuild numbers them.
    private static DerivedNav Assemble(INavSource src, CoarseTile[] tiles, int[] keyOfLeaf, NavObstacleInfo[] infos, NavEdgeCosts costs)
    {
        var groups = new List<CoarseGroup>(tiles.Sum(t => t.Groups.Count));
        foreach (CoarseTile tile in tiles)
        {
            groups.AddRange(tile.Groups);
            tile.Groups.Clear();
        }

        groups.Sort((a, b) => a.Key.CompareTo(b.Key));
        int leaves = src.LeafCount;
        var indexOfKey = new Dictionary<int, int>(groups.Count);
        for (int i = 0; i < groups.Count; i++)
            indexOfKey[groups[i].Key] = i;
        var b = new NavGraphBuilder();
        foreach (CoarseGroup group in groups)
            b.AddCell(group.Cell);
        for (int o = 0; o < infos.Length; o++)
            b.AddObstacle();

        var chosen = new Dictionary<int, (NavEdgeType Type, int Obstacle, int WalkAt, int WalkObstacle)>();
        var order = new List<int>();
        for (int i = 0; i < groups.Count; i++)
        {
            chosen.Clear();
            order.Clear();
            CoarseGroup group = groups[i];
            foreach (CoarseEdge e in group.Edges)
            {
                int targetKey = e.Resolved ? e.Target : keyOfLeaf[e.Target];
                if (targetKey < 0 || targetKey == group.Key)
                    continue;
                int to = indexOfKey[targetKey];
                if (!chosen.TryGetValue(to, out var had))
                {
                    chosen[to] = (e.Type, e.Obstacle, e.WalkAt, e.WalkObstacle);
                    order.Add(to);
                }
                else if (e.WalkAt >= 0 && (had.WalkAt < 0 || e.WalkAt < had.WalkAt))
                {
                    chosen[to] = had with { WalkAt = e.WalkAt, WalkObstacle = e.WalkObstacle };
                }
            }

            foreach (int to in order)
            {
                var e = chosen[to];
                (NavEdgeType type, int obstacle) = e.WalkAt >= 0 ? (NavEdgeType.Walk, e.WalkObstacle) : (e.Type, e.Obstacle);
                b.AddEdge(new CellId(i), new CellId(to), type, costs.ExtraFor(type), obstacle);
            }
        }

        var cellOfLeaf = new int[leaves];
        for (int leaf = 0; leaf < leaves; leaf++)
            cellOfLeaf[leaf] = keyOfLeaf[leaf] < 0 ? -1 : indexOfKey[keyOfLeaf[leaf]];
        int[] leafOfCell = [.. groups.Select(g => g.Key)];
        return new DerivedNav(b.Build(), cellOfLeaf, leafOfCell, infos, leaves);
    }

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
    /// <param name="origin">A corner of a column, which the columns are counted from; null for the lowest corner of any cell.</param>
    public static bool[] Flat(DerivedNav nav, float voxel, (float X, float Y)? origin = null)
    {
        ArgumentNullException.ThrowIfNull(nav);
        NavGraph g = nav.Graph;
        var columns = Columns(g, voxel, origin, out var at);
        var flat = new bool[g.CellCount];
        for (int c = 0; c < g.CellCount; c++)
            flat[c] = IsFlat(g, columns, at, c);
        return flat;
    }

    private static bool IsFlat(NavGraph g, Dictionary<(int, int), int[]> columns, (int X, int Y)[] at, int c)
    {
        NavCell cell = g.Cell(c);
        if ((cell.Flags & NavCellFlags.Ladder) != 0 || CrossesObstacle(g, c))
            return false;
        (int x, int y) = at[c];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && Same(g, columns, x + dx, y + dy, cell) < 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary><paramref name="nav"/> with each k by k block of flat columns on one floor merged into a cell (see the type's summary).</summary>
    /// <param name="voxel">The columns' width: the file's voxel.</param>
    /// <param name="k">The block's columns a side, 2 or more.</param>
    /// <param name="origin">A corner of a column, which the columns and the blocks are counted from; null for the lowest corner of any cell.</param>
    public static DerivedNav Coarsen(DerivedNav nav, float voxel, int k, NavEdgeCosts costs, (float X, float Y)? origin = null)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 2);
        NavGraph g = nav.Graph;
        bool[] flat = Flat(nav, voxel, origin);
        var columns = Columns(g, voxel, origin, out var at);
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

    // Each column's cells, the column a cell's footprint's low corner is in, counted from the origin, else the lowest corner of any.
    private static Dictionary<(int, int), int[]> Columns(NavGraph g, float voxel, (float X, float Y)? origin, out (int X, int Y)[] at)
    {
        at = new (int, int)[g.CellCount];
        float x0 = float.MaxValue, y0 = float.MaxValue;
        if (origin is { } o)
        {
            (x0, y0) = o;
        }
        else
        {
            for (int c = 0; c < g.CellCount; c++)
            {
                x0 = MathF.Min(x0, g.Cell(c).MinX);
                y0 = MathF.Min(y0, g.Cell(c).MinY);
            }
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

/// <summary>What <see cref="NavGraphReduce.DeriveCoarse"/> did: the tiles, the leaves it derived (a tile's margin and links counted again), the cells at the voxel.</summary>
public sealed class CoarseDeriveStats
{
    public int Tiles { get; set; }

    public int Leaves { get; set; }

    public long LeavesDerived { get; set; }

    /// <summary>The cells at the voxel, before their blocks were merged: the whole derivation's count.</summary>
    public long VoxelCells { get; set; }

    public int BiggestTileCells { get; set; }

    public int Margin { get; set; }

    /// <summary>Wall milliseconds: the columns and tiles laid out; every tile; the graph put together.</summary>
    public double PrepareMs { get; set; }

    public double TilesMs { get; set; }

    public double AssembleMs { get; set; }

    /// <summary>Wall milliseconds summed over the tiles (on all threads): the derivation, and the merge.</summary>
    public double DeriveMs { get; set; }

    public double MergeMs { get; set; }
}
