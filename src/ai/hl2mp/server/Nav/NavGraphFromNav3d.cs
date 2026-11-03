namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>A volume no bot may stand in: a <c>trigger_hurt</c> from the entity lump (the file does not mark them).</summary>
public readonly record struct HurtVolume(AiVector Min, AiVector Max);

/// <summary>The derived graph and how it maps back onto the file.</summary>
public sealed record DerivedNav(NavGraph Graph, int[] CellOfLeaf, int[] LeafOfCell, string[] ObstacleNames, int Leaves);

/// <summary>
/// The AI's graph for the HL2MP player, derived from a <c>.nav3d</c>'s answers
/// (plan_hl2mp_ai §6.2). Pure over <see cref="INavSource"/>, so it runs as a
/// pool job and its facts run on a hand-built source. Ours, not stock's
/// (§6, ruling 2026-11-01).
/// <list type="bullet">
/// <item>A cell per walkable leaf a player fits in: Stand and Crouch when the
/// standing box fits, Crouch only when only the crouched one does; none
/// inside a hurt volume.</item>
/// <item>Walk between neighbours whose rise is within the step; Drop down a
/// neighbour or a jump link within the safe drop; Jump up a link within jump
/// height + step, CrouchJump up one within the file's jump height; Ladder
/// between ladder leaves; Door when either end is a door's leaf, blocked by
/// that door's obstacle.</item>
/// </list>
/// </summary>
public static class NavGraphFromNav3d
{
    // The most neighbours or jump links one leaf may answer; the source refuses more.
    private const int MaxLinks = 256;

    public static DerivedNav Derive(INavSource src, Hl2mpNavNumbers n, NavEdgeCosts costs, IReadOnlyList<HurtVolume> hurt)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(hurt);
        var b = new NavGraphBuilder();
        int leaves = src.LeafCount;
        var cellOfLeaf = new int[leaves];
        Array.Fill(cellOfLeaf, -1);
        var leafOfCell = new List<int>();

        for (int leaf = 0; leaf < leaves; leaf++)
        {
            NavLeaf l = src.Leaf(leaf);
            if (!l.Walkable || InHurt(l, hurt))
                continue;
            ClearanceClasses classes = src.Standable(leaf, l.FloorZ, n.HullWidth, n.StandHeight, shut: false)
                ? ClearanceClasses.Stand | ClearanceClasses.Crouch
                : src.Standable(leaf, l.FloorZ, n.HullWidth, n.CrouchHeight, shut: false) ? ClearanceClasses.Crouch : ClearanceClasses.None;
            if (classes == ClearanceClasses.None)
                continue;
            NavCellFlags flags = (l.Water ? NavCellFlags.Water : 0) | (l.Ladder ? NavCellFlags.Ladder : 0);
            cellOfLeaf[leaf] = b.AddCell(new NavCell(l.MinX, l.MinY, l.MaxX, l.MaxY, l.FloorZ, classes, flags,
                                                     MathF.Max(1f, l.CostMultiplier))).Index;
            leafOfCell.Add(leaf);
        }

        // Each door's leaves, so an edge into one is a Door edge blocked by it.
        var doorOfLeaf = new int[leaves];
        Array.Fill(doorOfLeaf, -1);
        var names = new List<string>();
        Span<int> buf = stackalloc int[MaxLinks];
        for (int o = 0; o < src.ObstacleCount; o++)
        {
            NavObstacleInfo info = src.Obstacle(o);
            if (info.Kind != NavObstacleKind.Door)
                continue;
            int index = b.AddObstacle();
            names.Add(info.Name);
            int count = src.ObstacleLeaves(o, buf);
            for (int i = 0; i < count; i++)
                doorOfLeaf[buf[i]] = index;
        }

        var added = new HashSet<(int, int)>();
        for (int leaf = 0; leaf < leaves; leaf++)
        {
            int from = cellOfLeaf[leaf];
            if (from < 0)
                continue;

            int count = src.Neighbours(leaf, buf);
            for (int i = 0; i < count; i++)
            {
                int other = buf[i], to = cellOfLeaf[other];
                if (to < 0)
                    continue;
                float rise = src.StepUp(leaf, other);
                int door = doorOfLeaf[leaf] >= 0 ? doorOfLeaf[leaf] : doorOfLeaf[other];
                if (MathF.Abs(rise) <= n.StepHeight)
                    Add(b, added, from, to, door >= 0 ? NavEdgeType.Door : NavEdgeType.Walk, door >= 0 ? costs.DoorWait : 0f, door);
                else if (src.Leaf(leaf).Ladder && src.Leaf(other).Ladder)
                    Add(b, added, from, to, NavEdgeType.Ladder, costs.Ladder, -1);
                else if (rise < 0f && -rise <= n.SafeDrop)
                    Add(b, added, from, to, NavEdgeType.Drop, costs.Drop, -1);
            }

            count = src.Jumps(leaf, buf);
            for (int i = 0; i < count; i++)
            {
                int other = buf[i], to = cellOfLeaf[other];
                if (to < 0)
                    continue;
                float rise = src.StepUp(leaf, other);
                if (rise <= 0f)
                {
                    if (-rise <= n.SafeDrop)
                        Add(b, added, from, to, NavEdgeType.Drop, costs.Drop, -1);
                }
                else if (rise <= n.MaxJumpRise)
                    Add(b, added, from, to, NavEdgeType.Jump, costs.Jump, -1);
                else if (rise <= n.MaxCrouchJumpRise)
                    Add(b, added, from, to, NavEdgeType.CrouchJump, costs.CrouchJump, -1);
            }
        }

        return new DerivedNav(b.Build(), cellOfLeaf, [.. leafOfCell], [.. names], leaves);
    }

    private static void Add(NavGraphBuilder b, HashSet<(int, int)> added, int from, int to, NavEdgeType type, float extra, int obstacle)
    {
        if (added.Add((from, to)))
            b.AddEdge(new CellId(from), new CellId(to), type, extra, obstacle);
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
