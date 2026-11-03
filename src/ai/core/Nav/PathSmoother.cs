namespace SourceSharp.Ai;

/// <summary>A point on a smoothed path, and how it is reached from the one before.</summary>
public readonly record struct PathCorner(AiVector Point, NavEdgeType Arrive);

/// <summary>
/// Turns a cell corridor into the corners a body walks (plan_hl2mp_ai §2.3,
/// §3.4.3): the funnel ("string pulling") over the portals between adjacent
/// cells, so a path is the taut line through the corridor and never a grid
/// zigzag; and a Catmull-Rom pass that samples it at an even spacing, so a
/// turn is a curve. An edge that is not a walk across a shared side (a jump,
/// a drop, a ladder, cells that do not touch) is a forced corner: the funnel
/// ends at its take-off and starts again at its landing.
/// </summary>
public static class PathSmoother
{
    private const float Touch = 0.01f;

    /// <summary>The corners from <paramref name="start"/> to <paramref name="end"/> along <paramref name="corridor"/>.</summary>
    public static void Funnel(NavGraph graph, IReadOnlyList<CellId> corridor, AiVector start, AiVector end, List<PathCorner> corners)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(corners);
        corners.Clear();
        corners.Add(new PathCorner(start, NavEdgeType.Walk));
        if (corridor.Count == 0)
            return;

        var portals = new List<(AiVector Left, AiVector Right)> { (start, start) };
        for (int i = 0; i + 1 < corridor.Count; i++)
        {
            NavCell a = graph.Cell(corridor[i]), b = graph.Cell(corridor[i + 1]);
            graph.TryEdge(corridor[i].Index, corridor[i + 1].Index, out NavEdge edge);
            if ((edge.Type is NavEdgeType.Walk or NavEdgeType.Door) && SharedSide(a, b, out AiVector p, out AiVector q))
            {
                portals.Add(Orient(a.Centre, b.Centre, p, q));
                continue;
            }

            // A forced corner: walk the funnel to the take-off, then the link.
            AiVector takeOff = a.Centre, landing = b.Centre;
            portals.Add((takeOff, takeOff));
            Pull(portals, corners);
            corners.Add(new PathCorner(landing, edge.Type));
            portals.Clear();
            portals.Add((landing, landing));
        }

        portals.Add((end, end));
        Pull(portals, corners);
    }

    /// <summary>
    /// Drops every walked corner the body can cut: from each kept corner, the
    /// next kept one is the furthest whose straight line is walkable
    /// (<see cref="NavGraph.WalkableLine"/>) and passes no forced corner (a
    /// jump, drop, ladder or link is kept). The funnel is taut only inside the
    /// cell corridor; on a grid that corridor is one of many equally cheap
    /// staircases, and this is what makes a path across open ground straight.
    /// </summary>
    public static void Shortcut(NavGraph graph, ClearanceClass cls, NavObstacleState obstacles, List<PathCorner> corners)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Count < 3)
            return;
        var kept = new List<PathCorner> { corners[0] };
        int anchor = 0;
        while (anchor < corners.Count - 1)
        {
            int best = anchor + 1;
            CellId from = graph.FindCell(corners[anchor].Point + new AiVector(0, 0, 1f));
            for (int j = anchor + 2; j < corners.Count && from.IsValid; j++)
            {
                if (corners[j - 1].Arrive != NavEdgeType.Walk || corners[j].Arrive != NavEdgeType.Walk)
                    break;
                if (graph.WalkableLine(from.Index, corners[anchor].Point, corners[j].Point, cls, obstacles))
                    best = j;
            }

            kept.Add(corners[best]);
            anchor = best;
        }

        corners.Clear();
        corners.AddRange(kept);
    }

    /// <summary>
    /// <paramref name="corners"/> resampled along a centripetal-free (uniform)
    /// Catmull-Rom spline through them, a point every <paramref name="spacing"/>
    /// units; each sample keeps the arrival type of the corner it leads to.
    /// </summary>
    public static void CatmullRom(IReadOnlyList<PathCorner> corners, float spacing, List<PathCorner> samples)
    {
        ArgumentNullException.ThrowIfNull(corners);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);
        samples.Clear();
        if (corners.Count == 0)
            return;
        samples.Add(corners[0]);
        for (int i = 0; i + 1 < corners.Count; i++)
        {
            AiVector p0 = corners[Math.Max(i - 1, 0)].Point, p1 = corners[i].Point, p2 = corners[i + 1].Point;
            AiVector p3 = corners[Math.Min(i + 2, corners.Count - 1)].Point;
            int steps = Math.Max(1, (int)MathF.Ceiling(p1.DistanceTo(p2) / spacing));
            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                samples.Add(new PathCorner(s == steps ? p2 : Spline(p0, p1, p2, p3, t), corners[i + 1].Arrive));
            }
        }
    }

    private static AiVector Spline(AiVector p0, AiVector p1, AiVector p2, AiVector p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return (p1 * 2f + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
    }

    /// <summary>The side two touching footprints share, at the higher floor, if they share one.</summary>
    private static bool SharedSide(in NavCell a, in NavCell b, out AiVector p, out AiVector q)
    {
        float z = MathF.Max(a.FloorZ, b.FloorZ);
        float lo, hi;
        if (MathF.Abs(a.MaxX - b.MinX) < Touch || MathF.Abs(b.MaxX - a.MinX) < Touch)
        {
            float x = MathF.Abs(a.MaxX - b.MinX) < Touch ? a.MaxX : a.MinX;
            lo = MathF.Max(a.MinY, b.MinY);
            hi = MathF.Min(a.MaxY, b.MaxY);
            p = new AiVector(x, lo, z);
            q = new AiVector(x, hi, z);
            return hi - lo > Touch;
        }

        if (MathF.Abs(a.MaxY - b.MinY) < Touch || MathF.Abs(b.MaxY - a.MinY) < Touch)
        {
            float y = MathF.Abs(a.MaxY - b.MinY) < Touch ? a.MaxY : a.MinY;
            lo = MathF.Max(a.MinX, b.MinX);
            hi = MathF.Min(a.MaxX, b.MaxX);
            p = new AiVector(lo, y, z);
            q = new AiVector(hi, y, z);
            return hi - lo > Touch;
        }

        p = q = default;
        return false;
    }

    /// <summary>Twice the signed area of the triangle a, b, c in the ground plane (positive: c is left of a→b).</summary>
    private static float Area2(AiVector a, AiVector b, AiVector c) => (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);

    /// <summary>The portal's ends as (left, right) for travel from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static (AiVector, AiVector) Orient(AiVector from, AiVector to, AiVector p, AiVector q)
        => Area2(from, to, p) > 0f ? (p, q) : (q, p);

    private static bool Same(AiVector a, AiVector b) => MathF.Abs(a.X - b.X) < Touch && MathF.Abs(a.Y - b.Y) < Touch;

    /// <summary>The simple stupid funnel over <paramref name="portals"/>, whose first and last are points; appends all but the first.</summary>
    private static void Pull(List<(AiVector Left, AiVector Right)> portals, List<PathCorner> corners)
    {
        AiVector apex = portals[0].Left, left = portals[0].Left, right = portals[0].Right;
        int apexIndex = 0, leftIndex = 0, rightIndex = 0;
        for (int i = 1; i < portals.Count; i++)
        {
            (AiVector pl, AiVector pr) = portals[i];

            // Tighten the right side.
            if (Area2(apex, right, pr) >= 0f)
            {
                if (Same(apex, right) || Area2(apex, left, pr) < 0f)
                {
                    right = pr;
                    rightIndex = i;
                }
                else
                {
                    Emit(corners, left);
                    apex = left;
                    apexIndex = leftIndex;
                    right = left = apex;
                    rightIndex = leftIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            // Tighten the left side.
            if (Area2(apex, left, pl) <= 0f)
            {
                if (Same(apex, left) || Area2(apex, right, pl) > 0f)
                {
                    left = pl;
                    leftIndex = i;
                }
                else
                {
                    Emit(corners, right);
                    apex = right;
                    apexIndex = rightIndex;
                    right = left = apex;
                    rightIndex = leftIndex = apexIndex;
                    i = apexIndex;
                }
            }
        }

        Emit(corners, portals[^1].Left);
    }

    private static void Emit(List<PathCorner> corners, AiVector p)
    {
        if (corners.Count == 0 || !Same(corners[^1].Point, p) || MathF.Abs(corners[^1].Point.Z - p.Z) > Touch)
            corners.Add(new PathCorner(p, NavEdgeType.Walk));
    }
}
