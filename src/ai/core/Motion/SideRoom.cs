namespace SourceSharp.Ai;

/// <summary>What lies a step along a way from a body, on the navigation graph (<see cref="SideRoom"/>).</summary>
public enum SideFloor : byte
{
    /// <summary>Floor all the way, cell to cell by walking or a door.</summary>
    Open,

    /// <summary>No floor to walk onto: a wall, or a ledge higher than a step.</summary>
    Wall,

    /// <summary>Floor there, but lower than a step: a drop the body would fall off.</summary>
    Drop,

    /// <summary>The body stands on no cell: the graph cannot say.</summary>
    OffGraph,
}

/// <summary>
/// Whether a body has room to move a step along a way (lane Gmove, 2026-11-16: a fighting Soldier
/// sidestepped into walls): the graph's cells along the flat line from its feet, each reached from
/// the last by a Walk or Door edge its body may take, as <see cref="NavGraph.WalkableLine"/> walks
/// a line but with no floor test at the end (a step is a Walk edge however the stairs rise). Pure
/// over the graph and the obstacles' state the caller hands it.
/// </summary>
public static class SideRoom
{
    /// <summary>A step: two thirds of a body's width, what a Soldier at a run covers in an eighth of a second.</summary>
    public const float StepUnits = 32f;

    private const float SampleUnits = 4f;

    /// <summary>
    /// What lies <paramref name="units"/> along <paramref name="way"/> (flat; its length does not
    /// matter) from <paramref name="origin"/> for a body of <paramref name="cls"/>.
    /// </summary>
    public static SideFloor Ahead(NavGraph graph, NavObstacleState obstacles, AiVector origin, AiVector way, ClearanceClass cls, float units = StepUnits)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(obstacles);
        CellId start = graph.FindCell(origin + new AiVector(0f, 0f, 1f));
        if (!start.IsValid)
            return SideFloor.OffGraph;
        var flat = new AiVector(way.X, way.Y, 0f);
        if (flat.Length < 1e-3f || units <= 0f)
            return SideFloor.Open;
        flat = flat.Normalised();
        int at = start.Index;
        int samples = Math.Max(1, (int)MathF.Ceiling(units / SampleUnits));
        for (int s = 1; s <= samples; s++)
        {
            float t = units * s / samples;
            float x = origin.X + flat.X * t, y = origin.Y + flat.Y * t;
            if (Holds(graph.Cell(at), x, y))
                continue;
            int next = -1;
            foreach (NavEdge e in graph.EdgesFrom(at))
            {
                if (e.Type is NavEdgeType.Walk or NavEdgeType.Door && Holds(graph.Cell(e.To), x, y) && graph.Passable(e, cls, obstacles))
                {
                    next = e.To;
                    break;
                }
            }

            if (next < 0)
            {
                float floor = graph.Cell(at).FloorZ;
                CellId below = graph.FindCell(new AiVector(x, y, floor + 1f));
                return below.IsValid && graph.Cell(below).FloorZ < floor - NavGraph.CornerFloorReach ? SideFloor.Drop : SideFloor.Wall;
            }

            at = next;
        }

        return SideFloor.Open;
    }

    private static bool Holds(in NavCell c, float x, float y) => x >= c.MinX && x < c.MaxX && y >= c.MinY && y < c.MaxY;
}
