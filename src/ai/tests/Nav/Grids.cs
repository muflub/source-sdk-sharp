namespace SourceSharp.Ai.Tests.Nav;

/// <summary>Hand-built graphs for the nav facts: a map of characters, one cell per character.</summary>
internal static class Grids
{
    internal const float Size = 32f;

    /// <summary>
    /// A graph from rows of text, row 0 at the top (largest y). '.' a cell anyone
    /// stands in, 'c' a crouch-only cell, 'w' water, 'D' a door cell (its edges
    /// are Door edges of obstacle 0), anything else no cell. 4-connected walks.
    /// </summary>
    internal static (NavGraph Graph, int[,] Ids) Parse(float doorWait, params string[] rows)
    {
        var b = new NavGraphBuilder();
        int h = rows.Length, w = rows.Max(r => r.Length);
        var ids = new int[w, h];
        bool door = rows.Any(r => r.Contains('D'));
        int obstacle = door ? b.AddObstacle() : -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : ' ';
                ids[x, y] = -1;
                if (c is not ('.' or 'c' or 'w' or 'D'))
                    continue;
                float wx = x * Size, wy = (h - 1 - y) * Size;
                ids[x, y] = b.AddCell(new NavCell(wx, wy, wx + Size, wy + Size, 0f,
                                                  c == 'c' ? ClearanceClasses.Crouch : ClearanceClasses.Stand | ClearanceClasses.Crouch,
                                                  c == 'w' ? NavCellFlags.Water : NavCellFlags.None,
                                                  c == 'w' ? 2f : 1f)).Index;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (ids[x, y] < 0)
                    continue;
                foreach ((int dx, int dy) in new[] { (1, 0), (0, 1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= w || ny >= h || ids[nx, ny] < 0)
                        continue;
                    bool isDoor = rows[y][x] == 'D' || (nx < rows[ny].Length && rows[ny][nx] == 'D');
                    b.AddBoth(new CellId(ids[x, y]), new CellId(ids[nx, ny]),
                              isDoor ? NavEdgeType.Door : NavEdgeType.Walk, isDoor ? doorWait : 0f, isDoor ? obstacle : -1);
                }
            }
        }

        return (b.Build(), ids);
    }

    internal static (NavGraph Graph, int[,] Ids) Parse(params string[] rows) => Parse(0f, rows);

    internal static CellId At(int[,] ids, int x, int y) => new(ids[x, y]);

    /// <summary>The cells a field's next pointers walk from <paramref name="from"/> to its objective.</summary>
    internal static List<int> Walk(FlowField f, int from)
    {
        var cells = new List<int> { from };
        for (CellId next = f.NextFrom(from); next.IsValid; next = f.NextFrom(next.Index))
        {
            cells.Add(next.Index);
            if (cells.Count > 100_000)
                throw new InvalidOperationException("the field loops");
        }

        return cells;
    }

    /// <summary>The largest heading change, in degrees, between successive legs of a path.</summary>
    internal static float MaxTurnDegrees(IReadOnlyList<PathCorner> path)
    {
        float worst = 0f;
        for (int i = 1; i + 1 < path.Count; i++)
        {
            AiVector a = path[i].Point - path[i - 1].Point, c = path[i + 1].Point - path[i].Point;
            if (a.Length2D < 1e-3f || c.Length2D < 1e-3f)
                continue;
            float cos = (a.X * c.X + a.Y * c.Y) / (a.Length2D * c.Length2D);
            worst = MathF.Max(worst, MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI);
        }

        return worst;
    }
}
