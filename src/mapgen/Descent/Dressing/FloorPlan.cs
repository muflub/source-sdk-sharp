namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// A room's floor as the dresser and its lint read it, on the lint's
/// 16-unit columns (<see cref="RoomColumns"/>): the floor everyone walks
/// from the doors (the cell's floor, z = the sill), the raised surfaces
/// (ledges, decks, steps, pillar tops) whose air a jump needs, and the
/// route a giant robot (92 wide, 156 tall) takes from each door to the
/// others, which is also the room's lit path.
/// </summary>
public sealed class FloorPlan
{
    /// <summary>A giant robot at scale 1.9 (R16): its width and height.</summary>
    public const int GiantWidth = RoomKit.GiantWidth, GiantHeight = RoomKit.GiantHeight;

    /// <summary>The clear air a ledge needs above it (§4.1: the hull plus a margin).</summary>
    public const int LedgeAir = 114;

    /// <summary>Columns either side of a giant's centre column its hull reaches (46 is less than three columns).</summary>
    public const int GiantReach = 3;

    private readonly int n;
    private readonly bool[] floor;
    private readonly float[] clear;
    private readonly bool[] raised;

    public FloorPlan(Room room)
    {
        Room = room;
        Columns = new RoomColumns(room);
        n = room.Extent / RoomLint.Grid;
        floor = new bool[n * n];
        clear = new float[n * n];
        raised = new bool[n * n];
        float sill = room.Kit.Sill;
        Solids = new RoomSolids(room);
        foreach (RoomColumns.Surface s in Columns.Surfaces())
        {
            int k = s.I * n + s.J;
            // The floor, and anything on it a step high or less (a rail, a kerb): a giant walks over it.
            if (s.Z >= sill - 1 && s.Z <= sill + RoomLint.Step && s.K == 0)
                clear[k] = s.Clear;
            else if (s.Z > sill + RoomLint.Step)
                raised[k] = true;
        }

        // The floor is what a flood from the doors reaches across open air at
        // knee height: exact, where the columns alone miss a thin wall that
        // falls between two probes.
        var queue = new Queue<(int, int)>();
        foreach (var (face, offset) in room.Sockets)
        {
            var (i, j) = DoorColumn(face, offset);
            if (In(i, j) && clear[i * n + j] > 0 && !floor[i * n + j])
            {
                floor[i * n + j] = true;
                queue.Enqueue((i, j));
            }
        }

        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            Point from = At(i, j) with { Z = sill + 40 };
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
            {
                int a = i + di, b = j + dj;
                if (!In(a, b) || floor[a * n + b] || clear[a * n + b] <= 0)
                    continue;
                if (Solids.Ray(from, di, dj) is not { } r || r < RoomLint.Grid)
                    continue;
                floor[a * n + b] = true;
                queue.Enqueue((a, b));
            }
        }

        for (int k = 0; k < n * n; k++)
            if (!floor[k])
                clear[k] = 0;
        Raised = [.. Columns.Surfaces().Where(s => s.Z > sill + RoomLint.Step)];
    }

    /// <summary>The room's solids.</summary>
    public RoomSolids Solids { get; }

    public Room Room { get; }

    public RoomColumns Columns { get; }

    /// <summary>Every surface above a step over the floor: the ledges, decks, steps and pillar tops whose air a jump needs.</summary>
    public IReadOnlyList<RoomColumns.Surface> Raised { get; }

    /// <summary>Columns along one side of the room (its cells').</summary>
    public int Size => n;

    /// <summary>Whether column (i, j) is floor everyone reaches from the doors.</summary>
    public bool IsFloor(int i, int j) => In(i, j) && floor[i * n + j];

    /// <summary>Whether column (i, j) holds a reached surface above a step over the floor.</summary>
    public bool IsRaised(int i, int j) => In(i, j) && raised[i * n + j];

    /// <summary>The clear air over the floor of column (i, j), or 0.</summary>
    public float Clear(int i, int j) => IsFloor(i, j) ? clear[i * n + j] : 0;

    /// <summary>The centre of column (i, j)'s probe, on the floor.</summary>
    public Point At(int i, int j) => new(i * RoomLint.Grid + RoomColumns.Probe, j * RoomLint.Grid + RoomColumns.Probe, Room.Kit.Sill);

    /// <summary>The column a point stands in.</summary>
    public static (int I, int J) ColumnOf(float x, float y) => ((int)Math.Floor(x / RoomLint.Grid), (int)Math.Floor(y / RoomLint.Grid));

    private bool In(int i, int j) => i >= 0 && j >= 0 && i < n && j < n;

    /// <summary>How far inside a door its route starts: a giant centred there reaches no column of the doorway's plug.</summary>
    public const int RouteStart = 64;

    /// <summary>The floor column a door's route starts from: <see cref="RouteStart"/> inside the door, on its centre line.</summary>
    public (int I, int J) DoorColumn(CellFace face, int offset = 0)
    {
        Point p = Room.DoorInside(face, offset, RouteStart);
        return ColumnOf(p.X, p.Y);
    }

    /// <summary>The route start of every socket, in the room's socket order.</summary>
    private (int I, int J)[] DoorColumns() => [.. Room.Sockets.Select(s => DoorColumn(s.Face, s.Offset))];

    /// <summary>
    /// Whether a giant fits standing centred on column (i, j): every column
    /// its hull covers is floor with a giant's headroom, and none is
    /// <paramref name="blocked"/>.
    /// </summary>
    public bool GiantFits(int i, int j, ISet<(int, int)>? blocked = null)
    {
        for (int a = i - GiantReach; a <= i + GiantReach; a++)
            for (int b = j - GiantReach; b <= j + GiantReach; b++)
                if (!IsFloor(a, b) || Clear(a, b) < GiantHeight || (blocked?.Contains((a, b)) ?? false))
                    return false;
        return true;
    }

    /// <summary>
    /// The routes a giant takes: from the first door to each other door, the
    /// shortest walk on columns it fits on (<see cref="GiantFits"/>); for a
    /// room of one door, from the door to the floor furthest from it. Each
    /// route is its columns in order. A door a giant cannot reach gives no
    /// route; <see cref="RouteBroken"/> says whether any pair is cut.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(int I, int J)>> GiantRoutes(ISet<(int, int)>? blocked = null)
    {
        var doors = DoorColumns();
        if (doors.Length == 0)
            return [];
        var (dist, parent) = Walk(doors[0], blocked);
        var routes = new List<IReadOnlyList<(int, int)>>();
        if (doors.Length == 1)
        {
            // Furthest reachable column, ties broken by index for a stable choice.
            int best = -1, far = -1;
            for (int k = 0; k < n * n; k++)
                if (dist[k] > far)
                {
                    far = dist[k];
                    best = k;
                }

            if (best >= 0 && far > 0)
                routes.Add(Path(parent, best));
            return routes;
        }

        foreach (var d in doors.Skip(1))
        {
            int k = d.I * n + d.J;
            if (dist[k] >= 0)
                routes.Add(Path(parent, k));
        }

        return routes;
    }

    /// <summary>
    /// The columns a giant reaches walking from any of the room's doors
    /// (<see cref="GiantFits"/> at every step), none of
    /// <paramref name="blocked"/> under it.
    /// </summary>
    public HashSet<(int I, int J)> GiantReached(ISet<(int, int)>? blocked = null)
    {
        var reached = new HashSet<(int, int)>();
        foreach (var door in DoorColumns())
        {
            var (dist, _) = Walk(door, blocked);
            for (int k = 0; k < n * n; k++)
                if (dist[k] >= 0)
                    reached.Add((k / n, k % n));
        }

        return reached;
    }

    /// <summary>Whether some door cannot reach another by a giant's route (rooms of one door never are).</summary>
    public bool RouteBroken(ISet<(int, int)>? blocked = null)
    {
        var doors = DoorColumns();
        if (doors.Length < 2)
            return false;
        var (dist, _) = Walk(doors[0], blocked);
        return doors.Skip(1).Any(d => dist[d.I * n + d.J] < 0);
    }

    private (int[] Dist, int[] Parent) Walk((int I, int J) start, ISet<(int, int)>? blocked)
    {
        var dist = Enumerable.Repeat(-1, n * n).ToArray();
        var parent = Enumerable.Repeat(-1, n * n).ToArray();
        if (!GiantFits(start.I, start.J, blocked))
            return (dist, parent);
        var queue = new Queue<(int, int)>();
        dist[start.I * n + start.J] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
            {
                int a = i + di, b = j + dj;
                if (!In(a, b) || dist[a * n + b] >= 0 || !GiantFits(a, b, blocked))
                    continue;
                dist[a * n + b] = dist[i * n + j] + 1;
                parent[a * n + b] = i * n + j;
                queue.Enqueue((a, b));
            }
        }

        return (dist, parent);
    }

    private List<(int, int)> Path(int[] parent, int end)
    {
        var path = new List<(int, int)>();
        for (int k = end; k >= 0; k = parent[k])
            path.Add((k / n, k % n));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The columns within <paramref name="reach"/> columns of any column of
    /// <paramref name="routes"/>: the lane the dresser keeps clear.
    /// </summary>
    public HashSet<(int, int)> Lane(IEnumerable<IReadOnlyList<(int I, int J)>> routes, int reach)
    {
        var lane = new HashSet<(int, int)>();
        foreach (var route in routes)
            foreach (var (i, j) in route)
                for (int a = i - reach; a <= i + reach; a++)
                    for (int b = j - reach; b <= j + reach; b++)
                        if (In(a, b))
                            lane.Add((a, b));
        return lane;
    }

    /// <summary>The columns a box's footprint covers.</summary>
    public IEnumerable<(int, int)> Covered(Box box)
    {
        var (i0, j0) = ColumnOf(box.Mins.X, box.Mins.Y);
        var (i1, j1) = ColumnOf(box.Maxs.X - 0.01f, box.Maxs.Y - 0.01f);
        for (int i = Math.Max(0, i0); i <= Math.Min(n - 1, i1); i++)
            for (int j = Math.Max(0, j0); j <= Math.Min(n - 1, j1); j++)
                yield return (i, j);
    }

    /// <summary>The distance in columns from (i, j) to the nearest column that is neither floor nor out of the room (a wall, a pillar, a feature), up to <paramref name="max"/>.</summary>
    public int WallDistance(int i, int j, int max)
    {
        for (int r = 0; r <= max; r++)
            for (int a = i - r; a <= i + r; a++)
                for (int b = j - r; b <= j + r; b++)
                    if ((Math.Abs(a - i) == r || Math.Abs(b - j) == r) && !IsFloor(a, b))
                        return r;
        return max + 1;
    }
}
