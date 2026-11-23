using System.Globalization;

namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// Where a giant robot's box walks a dressed room as the level's navigation
/// sees it (ask M8, brief p7): standing on the floor (z = the sill) and never
/// raised over anything. The <c>.nav3d</c> keeps a column's clear air but no
/// solid's top, so a solid that rises above the floor by any amount (a rail
/// two units high, a sill plank, a mound) is a wall to a box wider than the
/// gap beside it, though a player steps over it. The walk is on the 16-unit
/// columns the navigation voxels are (the cell's grid, which a level's
/// placement keeps); a box of <see cref="Width"/> centred on a column covers
/// every column it overlaps, and it stands where each of them has floor at
/// the sill and nothing solid between the floor and <see cref="Height"/>
/// over it. Solid is the room's world but its door plugs, its brush entities
/// that stay solid, and its solid props as their models' boxes, with every
/// door joined: what stands only where a door is capped
/// (<c>room_needs !joined_...</c>) is left out.
/// </summary>
public sealed class FlatHullWalk
{
    /// <summary>A box over the x1.90 giant (91 by 156) with margin.</summary>
    public const int Width = 96, Height = 160;

    private const float Voxel = RoomLint.Grid;

    private readonly int n;
    private readonly bool[] free;
    private readonly bool[] stands;
    private readonly int reach;

    /// <param name="room">The dressed room.</param>
    /// <param name="models">The models its props may name, by path (<see cref="DressLint.Models"/>); a prop of any other model throws.</param>
    public FlatHullWalk(Room room, IReadOnlyDictionary<string, PropModel> models, int width = Width, int height = Height)
    {
        Room = room;
        n = room.Extent / RoomLint.Grid;
        // A box centred on a column's middle reaches the columns its half overlaps.
        reach = (int)Math.Ceiling((width / 2f - Voxel / 2f) / Voxel - 1e-4f);
        float sill = room.Kit.Sill, top = sill + height;
        var plugs = room.Sockets.Select(s => room.PlugBox(s.Face, s.Offset)).ToHashSet();
        var solids = room.World.Where(b => !(b.IsBox && plugs.Contains((b.Mins, b.Maxs))))
            .Concat(room.Entities.Where(e => e.ClassName == "func_brush" && e.Key("Solidity") != "1" && !CappedOnly(e)).SelectMany(e => e.Brushes))
            .ToArray();
        var props = new List<(Point Mins, Point Maxs)>();
        foreach (RoomEntity e in room.Entities.Where(e => e.ClassName is "prop_static" or "prop_dynamic" && e.Key("solid") != "0" && !CappedOnly(e)))
        {
            string model = e.Key("model") ?? "";
            if (!models.TryGetValue(model, out PropModel? m))
                throw new InvalidOperationException($"{room.Name}: a solid prop of {model}, which no theme names: its box is unknown");
            int yaw = (int)float.Parse((e.Key("angles") ?? "0 0 0").Split(' ')[1], CultureInfo.InvariantCulture);
            var (lo, hi) = m.Turned(yaw);
            props.Add((e.Origin!.Value + lo, e.Origin!.Value + hi));
        }

        // A breakable (M34) is solid until broken.
        foreach (RoomEntity e in room.Entities.Where(Breakables.Is))
        {
            Box b = Breakables.BoxOf(e);
            props.Add((b.Mins, b.Maxs));
        }

        var floored = new bool[n * n];
        var blocked = new bool[n * n];
        foreach (Brush b in solids)
        {
            var (i0, j0, i1, j1) = Span(b.Mins, b.Maxs);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    Point lo = new(i * Voxel, j * Voxel, sill), hi = new((i + 1) * Voxel, (j + 1) * Voxel, top);
                    if (b.Overlaps(lo, hi))
                        blocked[i * n + j] = true;
                    // The floor: a solid whose top is the sill (or the doorway's dip just under it) at the column's middle.
                    if (b.Column(i * Voxel + RoomColumns.Probe, j * Voxel + RoomColumns.Probe) is { } c && c.High >= sill - RoomBuilder.DoorwayDip - 0.5f && c.High <= sill + 0.5f)
                        floored[i * n + j] = true;
                }
        }

        foreach (var (lo, hi) in props.Where(p => p.Maxs.Z > sill + Brush.Epsilon && p.Mins.Z < top - Brush.Epsilon))
        {
            var (i0, j0, i1, j1) = Span(lo, hi);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    blocked[i * n + j] = true;
        }

        free = new bool[n * n];
        for (int k = 0; k < n * n; k++)
            free[k] = floored[k] && !blocked[k];
        stands = new bool[n * n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                stands[i * n + j] = Fits(i, j);
    }

    public Room Room { get; }

    /// <summary>Columns along one side of the room.</summary>
    public int Size => n;

    /// <summary>Whether column (i, j) has floor at the sill and nothing solid over it up to the box's height.</summary>
    public bool IsFree(int i, int j) => In(i, j) && free[i * n + j];

    /// <summary>Whether the box stands centred on column (i, j).</summary>
    public bool Stands(int i, int j) => In(i, j) && stands[i * n + j];

    /// <summary>The column a socket's walk starts from: <see cref="FloorPlan.RouteStart"/> inside it, on its centre line.</summary>
    public (int I, int J) DoorColumn(CellFace face, int offset)
    {
        Point p = Room.DoorInside(face, offset, FloorPlan.RouteStart);
        return FloorPlan.ColumnOf(p.X, p.Y);
    }

    /// <summary>The columns the box reaches from (i, j), stepping a column at a time; empty when it cannot stand there.</summary>
    public HashSet<(int I, int J)> Reached(int i, int j)
    {
        var seen = new HashSet<(int, int)>();
        if (!Stands(i, j))
            return seen;
        var queue = new Queue<(int, int)>();
        seen.Add((i, j));
        queue.Enqueue((i, j));
        while (queue.Count > 0)
        {
            var (a, b) = queue.Dequeue();
            foreach (var (da, db) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                if (Stands(a + da, b + db) && seen.Add((a + da, b + db)))
                    queue.Enqueue((a + da, b + db));
        }

        return seen;
    }

    /// <summary>
    /// Every way the room fails a giant: a socket where the box cannot stand
    /// inside its door, and each socket the first socket's walk does not
    /// reach. Empty when the box walks from every door to every other.
    /// </summary>
    public IReadOnlyList<string> Cuts()
    {
        var sockets = Room.Sockets.ToArray();
        var cuts = new List<string>();
        foreach (var (face, offset) in sockets)
        {
            var (i, j) = DoorColumn(face, offset);
            if (!Stands(i, j))
                cuts.Add($"the {Room.SocketName(face, offset)} door: no room for the box at column {i} {j}");
        }

        if (cuts.Count > 0 || sockets.Length < 2)
            return cuts;
        var (fi, fj) = DoorColumn(sockets[0].Face, sockets[0].Offset);
        var from = Reached(fi, fj);
        foreach (var (face, offset) in sockets.Skip(1))
            if (!from.Contains(DoorColumn(face, offset)))
                cuts.Add($"the {Room.SocketName(sockets[0].Face, sockets[0].Offset)} door does not reach the {Room.SocketName(face, offset)} door");
        return cuts;
    }

    /// <summary>The room as text, north up: <c>#</c> blocked or no floor, <c>.</c> free, <c>o</c> where the box stands.</summary>
    public string Picture()
    {
        var sb = new System.Text.StringBuilder();
        for (int j = n - 1; j >= 0; j--)
        {
            for (int i = 0; i < n; i++)
                sb.Append(Stands(i, j) ? 'o' : IsFree(i, j) ? '.' : '#');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private bool Fits(int i, int j)
    {
        for (int a = i - reach; a <= i + reach; a++)
            for (int b = j - reach; b <= j + reach; b++)
                if (!IsFree(a, b))
                    return false;
        return true;
    }

    private bool In(int i, int j) => i >= 0 && j >= 0 && i < n && j < n;

    /// <summary>The columns a box's open footprint overlaps.</summary>
    private (int I0, int J0, int I1, int J1) Span(Point mins, Point maxs) =>
        (Math.Max(0, (int)Math.Floor((mins.X + Brush.Epsilon) / Voxel)), Math.Max(0, (int)Math.Floor((mins.Y + Brush.Epsilon) / Voxel)),
         Math.Min(n - 1, (int)Math.Ceiling((maxs.X - Brush.Epsilon) / Voxel) - 1), Math.Min(n - 1, (int)Math.Ceiling((maxs.Y - Brush.Epsilon) / Voxel) - 1));

    /// <summary>Whether an entity stands only where some door is capped.</summary>
    private static bool CappedOnly(RoomEntity e) => e.Key("room_needs") is { } needs && needs.Contains('!', StringComparison.Ordinal);
}
