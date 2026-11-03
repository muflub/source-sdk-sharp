namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>A volume no bot may stand in: a <c>trigger_hurt</c> from the entity lump (the file does not mark them).</summary>
public readonly record struct HurtVolume(AiVector Min, AiVector Max);

/// <summary>The derived graph and how it maps back onto the file: each cell's leaf, each leaf's cell (-1 for none), and each graph obstacle's file record.</summary>
public sealed record DerivedNav(NavGraph Graph, int[] CellOfLeaf, int[] LeafOfCell, NavObstacleInfo[] Obstacles, int Leaves);

/// <summary>
/// The AI's graph for the HL2MP player, derived from a <c>.nav3d</c>'s answers
/// (plan_hl2mp_ai §6.2). Pure over <see cref="INavSource"/>, so it runs as a
/// pool job and its facts run on a hand-built source. Ours, not stock's
/// (§6, ruling 2026-11-01); the places and moves are the ones map-tools'
/// reference <c>Nav3dWalk</c> derives for its <c>Nav3dWalker.Player</c>
/// (format §7.6), and a fact holds the two to the same components.
/// <list type="bullet">
/// <item>Places: a cell per leaf a player stands on at the column's centre
/// (Stand and Crouch when the standing box fits, Crouch only when only the
/// crouched one does), and per ladder leaf it climbs; none inside a hurt
/// volume.</item>
/// <item>Walk to a place in a column beside whose floor is within the step;
/// Drop to a lower one within the safe drop that the body reaches over the
/// edge; Jump, CrouchJump or Drop along the file's jump links, split by
/// HL2MP's numbers; Ladder up and down a climb's column and on and off it to
/// floors the body reaches; Door when either end is a door's leaf.</item>
/// <item>Every dynamic obstacle is a graph obstacle; an edge with an end in
/// its leaves carries it (a door's first), so the binding can block it from
/// the entity's state.</item>
/// </list>
/// </summary>
public static class NavGraphFromNav3d
{
    // The most leaves one column, or jump links one leaf, may answer.
    private const int MaxLinks = 512;

    public static DerivedNav Derive(INavSource src, Hl2mpNavNumbers n, NavEdgeCosts costs, IReadOnlyList<HurtVolume> hurt)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(hurt);
        var b = new NavGraphBuilder();
        int leaves = src.LeafCount;
        var stand = new bool[leaves];
        var climb = new bool[leaves];
        var tall = new bool[leaves];
        var cellOfLeaf = new int[leaves];
        Array.Fill(cellOfLeaf, -1);
        var leafOfCell = new List<int>();

        for (int leaf = 0; leaf < leaves; leaf++)
        {
            NavLeaf l = src.Leaf(leaf);
            if (InHurt(l, hurt))
                continue;
            ClearanceClasses classes = ClearanceClasses.None;
            if (l.Walkable)
            {
                classes = src.Stands(leaf, n.HullWidth, n.StandHeight) ? ClearanceClasses.Stand | ClearanceClasses.Crouch
                    : src.Stands(leaf, n.HullWidth, n.CrouchHeight) ? ClearanceClasses.Crouch : ClearanceClasses.None;
            }

            stand[leaf] = classes != ClearanceClasses.None;
            tall[leaf] = (classes & ClearanceClasses.Stand) != 0;
            climb[leaf] = l.Ladder && src.Climbs(leaf, n.HullWidth, n.CrouchHeight);
            if (!stand[leaf] && !climb[leaf])
                continue;
            if (!stand[leaf])
                classes = ClearanceClasses.Stand | ClearanceClasses.Crouch;
            NavCellFlags flags = (l.Water ? NavCellFlags.Water : 0) | (climb[leaf] ? NavCellFlags.Ladder : 0);
            float floor = stand[leaf] ? l.FloorZ : l.BottomZ;
            cellOfLeaf[leaf] = b.AddCell(new NavCell(l.MinX, l.MinY, l.MaxX, l.MaxY, floor, classes, flags,
                                                     MathF.Max(1f, l.CostMultiplier))).Index;
            leafOfCell.Add(leaf);
        }

        // Each obstacle's leaves: the places it takes away when shut (its
        // records name every leaf within reach of it; only these change). A
        // door wins a leaf two obstacles take.
        var obstacleOfLeaf = new int[leaves];
        Array.Fill(obstacleOfLeaf, -1);
        var infos = new NavObstacleInfo[src.ObstacleCount];
        Span<int> buf = new int[Math.Max(MaxLinks, leaves)];
        for (int o = 0; o < src.ObstacleCount; o++)
        {
            infos[o] = src.Obstacle(o);
            b.AddObstacle();
            int count = src.ObstacleLeaves(o, buf);
            for (int i = 0; i < count; i++)
            {
                int leaf = buf[i];
                if (!stand[leaf])
                    continue;
                float height = tall[leaf] ? n.StandHeight : n.CrouchHeight;
                if (src.StandsWithShut(leaf, o, n.HullWidth, height))
                    continue;
                int at = obstacleOfLeaf[leaf];
                if (at < 0 || (infos[o].Kind == NavObstacleKind.Door && infos[at].Kind != NavObstacleKind.Door))
                    obstacleOfLeaf[leaf] = o;
            }
        }

        var added = new HashSet<(int, int)>();
        Span<NavJumpLink> jumps = new NavJumpLink[MaxLinks];
        Span<int> between = new int[MaxLinks];
        float voxel = src.VoxelSize;
        int reach = (int)MathF.Ceiling((n.HullWidth * 0.5f + voxel * 0.5f) / voxel);
        for (int leaf = 0; leaf < leaves; leaf++)
        {
            int from = cellOfLeaf[leaf];
            if (from < 0)
                continue;
            NavLeaf l = src.Leaf(leaf);

            if (stand[leaf])
            {
                // Near moves: to a place in the column beside, or two columns
                // along over the one between, which a player standing at the
                // foot of a sheer step cannot hold at its centre (its box
                // overlaps the step) but crosses on the way up.
                for (int side = 0; side < 4; side++)
                {
                    (int dx, int dy) = side switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
                    for (int k = 1; k <= 2; k++)
                    {
                        int count = src.ColumnLeaves(leaf, dx * k, dy * k, buf);
                        for (int i = 0; i < count; i++)
                        {
                            int other = buf[i];
                            if (!stand[other])
                                continue;
                            float floorO = src.Leaf(other).FloorZ, rise = floorO - l.FloorZ;
                            if (k == 2 && OpenOver(src, leaf, dx, dy, MathF.Max(l.FloorZ, floorO), n.CrouchHeight, between) < 0
                                && !CrossedAbove(src, leaf, dx, dy, MathF.Max(l.FloorZ, floorO), n.HullWidth, n.CrouchHeight, between))
                                continue;
                            NavEdgeType? type = MathF.Abs(rise) <= n.StepHeight ? NavEdgeType.Walk
                                : rise > n.MaxCrouchJumpRise ? null
                                : rise > n.MaxJumpRise ? NavEdgeType.CrouchJump
                                : rise > 0f ? NavEdgeType.Jump
                                : -rise <= n.SafeDrop && (k == 2 || src.FitsAt(other, l.FloorZ, n.HullWidth, n.CrouchHeight)) ? NavEdgeType.Drop
                                : null;
                            if (type is { } t)
                                AddMove(b, added, infos, obstacleOfLeaf, costs, leaf, other, from, cellOfLeaf[other], t);
                        }
                    }
                }

                int links = src.Jumps(leaf, jumps);
                for (int i = 0; i < links; i++)
                {
                    NavJumpLink link = jumps[i];
                    if (!stand[link.Leaf])
                        continue;
                    float rise = src.Leaf(link.Leaf).FloorZ - l.FloorZ;
                    NavEdgeType? type = rise > n.MaxCrouchJumpRise ? null
                        : rise > n.MaxJumpRise ? NavEdgeType.CrouchJump
                        : rise > 0f || link.Columns > 1 ? NavEdgeType.Jump
                        : -rise <= n.SafeDrop ? NavEdgeType.Drop : null;
                    if (type is { } t)
                        AddMove(b, added, infos, obstacleOfLeaf, costs, leaf, link.Leaf, from, cellOfLeaf[link.Leaf], t);
                }
            }

            if (!climb[leaf])
                continue;

            // Up and down the ladder's column.
            foreach (int other in (ReadOnlySpan<int>)[src.Below(leaf), src.Above(leaf)])
            {
                if (other >= 0 && cellOfLeaf[other] >= 0)
                    AddLadder(b, added, from, cellOfLeaf[other], costs);
            }

            // On and off: floors the box reaches at the climb's span.
            float bottom = l.BottomZ - n.StepHeight, top = l.TopZ + n.StepHeight;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int count = src.ColumnLeaves(leaf, dx, dy, buf);
                    for (int i = 0; i < count; i++)
                    {
                        int other = buf[i];
                        float floor = src.Leaf(other).FloorZ;
                        if (other != leaf && stand[other] && floor >= bottom && floor <= top)
                            AddLadder(b, added, from, cellOfLeaf[other], costs);
                    }
                }
            }
        }

        return new DerivedNav(b.Build(), cellOfLeaf, [.. leafOfCell], infos, leaves);
    }

    /// <summary>The leaf of the column beside <paramref name="leaf"/> towards (dx, dy) that is free from <paramref name="z"/> up through <paramref name="height"/> (a body passes over it there), or -1.</summary>
    /// <summary>
    /// Whether a crouched body crosses the column between at the higher floor
    /// as map-tools' reference walker asks it (1.0.2): fitted at the column's
    /// centre in the first voxel at or above the feet. A step whose top floor
    /// sits inside a voxel the column-free test above refuses.
    /// </summary>
    private static bool CrossedAbove(INavSource src, int leaf, int dx, int dy, float feet, float width, float height, Span<int> into)
    {
        int count = src.ColumnLeaves(leaf, dx, dy, into);
        for (int i = 0; i < count; i++)
        {
            if (src.FitsAbove(into[i], feet, width, height))
                return true;
        }

        return false;
    }

    private static int OpenOver(INavSource src, int leaf, int dx, int dy, float z, float height, Span<int> into)
    {
        int count = src.ColumnLeaves(leaf, dx, dy, into);
        for (int i = 0; i < count; i++)
        {
            NavLeaf b = src.Leaf(into[i]);
            if (b.BottomZ <= z + 0.01f && b.TopZ >= z + height)
                return into[i];
        }

        return -1;
    }

    private static void AddMove(NavGraphBuilder b, HashSet<(int, int)> added, NavObstacleInfo[] infos, int[] obstacleOfLeaf,
                                NavEdgeCosts costs, int fromLeaf, int toLeaf, int from, int to, NavEdgeType type)
    {
        int a = obstacleOfLeaf[fromLeaf], c = obstacleOfLeaf[toLeaf];
        int obstacle = a >= 0 && (c < 0 || infos[a].Kind == NavObstacleKind.Door) ? a : c;
        if (obstacle >= 0 && infos[obstacle].Kind == NavObstacleKind.Door && type == NavEdgeType.Walk)
            type = NavEdgeType.Door;
        float extra = costs.ExtraFor(type);
        if (from != to && added.Add((from, to)))
            b.AddEdge(new CellId(from), new CellId(to), type, extra, obstacle);
    }

    private static void AddLadder(NavGraphBuilder b, HashSet<(int, int)> added, int a, int c, NavEdgeCosts costs)
    {
        if (a == c)
            return;
        if (added.Add((a, c)))
            b.AddEdge(new CellId(a), new CellId(c), NavEdgeType.Ladder, costs.Ladder);
        if (added.Add((c, a)))
            b.AddEdge(new CellId(c), new CellId(a), NavEdgeType.Ladder, costs.Ladder);
    }

    private static bool InHurt(in NavLeaf l, IReadOnlyList<HurtVolume> hurt)
    {
        foreach (HurtVolume h in hurt)
        {
            if (l.MaxX > h.Min.X && l.MinX < h.Max.X && l.MaxY > h.Min.Y && l.MinY < h.Max.Y
                && l.FloorZ >= h.Min.Z - 1f && l.FloorZ < h.Max.Z)
                return true;
        }

        return false;
    }
}
