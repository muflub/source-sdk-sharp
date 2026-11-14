namespace SourceSharp.MapGen.Descent;

/// <summary>One lint finding: the rule, the room, and what is wrong.</summary>
public sealed record LintFinding(string Rule, string Room, string Message)
{
    public override string ToString() => $"{Rule}: room '{Room}': {Message}";
}

/// <summary>
/// The room lint (plan task B1, plan_tf2_rpg.md 6a): the rules a room keeps
/// so any legal assembly links into a level that plays, each measured from
/// the room's brushes and markers, never from what the room says of itself.
/// Every rule has a refusing fixture in <c>DescentRoomLintTests</c>.
/// </summary>
/// <remarks>
/// The geometry is read on a grid of 16-unit columns (the navigation
/// voxel): in each column the solid brushes (the world but its door plugs,
/// and the <c>func_brush</c> frames) give the surfaces a player can stand
/// on, each with the clear air above it. Displacements count as their base
/// brush.
/// </remarks>
public static class RoomLint
{
    public const string Sealed = "sealed";
    public const string DoorsOnKit = "doors_on_kit";
    public const string MarkerFloor = "marker_floor";
    public const string JumpClearAir = "jump_clear_air";
    public const string LedgePays = "ledge_pays";
    public const string InsideCell = "inside_cell";
    public const string FrameClear = "frame_clear";

    /// <summary>TF2's standing hull: 48 square, 82 high (TFViewVectors).</summary>
    public const int HullWidth = 48, HullHeight = 82;

    /// <summary>A step: what a player walks up without jumping.</summary>
    public const int Step = 18;

    /// <summary>The sampling grid.</summary>
    public const int Grid = 16;

    /// <summary>Every finding for the room, rule by rule.</summary>
    public static IReadOnlyList<LintFinding> Check(Room room)
    {
        var columns = new RoomColumns(room);
        return [.. CheckSealed(room), .. CheckDoorsOnKit(room), .. CheckMarkerFloor(room, columns), .. CheckJumpClearAir(room, columns),
                .. CheckLedgePays(room, columns), .. CheckInsideCell(room), .. CheckFrameClear(room)];
    }

    //-------------------------------------------------------------------------
    // sealed: a flood from inside every door never reaches the cell's edge.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckSealed(Room room)
    {
        RoomKit kit = room.Kit;
        int n = kit.Cell / Grid, nz = room.Height / Grid;
        var solid = new bool[n, n, nz];
        foreach (Brush b in room.World)
        {
            int x0 = Lo(b.Mins.X), x1 = Hi(b.Maxs.X, n), y0 = Lo(b.Mins.Y), y1 = Hi(b.Maxs.Y, n), z0 = Lo(b.Mins.Z), z1 = Hi(b.Maxs.Z, nz);
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                    for (int z = z0; z < z1; z++)
                        solid[x, y, z] |= b.Contains(Centre(x, y, z));
        }

        var seen = new bool[n, n, nz];
        var queue = new Queue<(int X, int Y, int Z)>();
        foreach (CellFace face in room.Doors)
        {
            Point inside = DoorInside(kit, face, 48) with { Z = kit.Sill + 40 };
            var (x, y, z) = ((int)(inside.X / Grid), (int)(inside.Y / Grid), (int)(inside.Z / Grid));
            if (!solid[x, y, z] && !seen[x, y, z])
            {
                seen[x, y, z] = true;
                queue.Enqueue((x, y, z));
            }
        }

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            if (x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == nz - 1)
            {
                yield return new LintFinding(Sealed, room.Name,
                    $"the room leaks: open air from its doors reaches the cell's edge at ({Centre(x, y, z)})");
                yield break;
            }

            foreach (var (dx, dy, dz) in Neighbours)
            {
                int a = x + dx, b = y + dy, c = z + dz;
                if (!seen[a, b, c] && !solid[a, b, c])
                {
                    seen[a, b, c] = true;
                    queue.Enqueue((a, b, c));
                }
            }
        }
    }

    //-------------------------------------------------------------------------
    // doors on the kit: each door's plug is exactly the kit's opening.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckDoorsOnKit(Room room)
    {
        RoomKit kit = room.Kit;
        var plugMaterials = new HashSet<string>(StringComparer.Ordinal);
        foreach (CellFace face in room.Doors)
        {
            var (mins, maxs) = kit.PlugBox(face);
            var plugs = room.World.Where(b => b.IsBox && b.Mins == mins && b.Maxs == maxs).ToArray();
            if (plugs.Length != 1)
            {
                yield return new LintFinding(DoorsOnKit, room.Name,
                    $"its {RoomBuilder.WallName(face)} door has {plugs.Length} plugs filling the kit's opening ({mins})-({maxs}); a socket has one");
                continue;
            }

            plugMaterials.Add(plugs[0].Material);
        }

        var openings = room.Doors.Select(kit.PlugBox).ToHashSet();
        foreach (Brush b in room.World.Where(b => plugMaterials.Contains(b.Material) && !openings.Contains((b.Mins, b.Maxs))))
            yield return new LintFinding(DoorsOnKit, room.Name,
                $"a {b.Material} brush ({b.Mins})-({b.Maxs}) is a plug off the kit's openings");
    }

    //-------------------------------------------------------------------------
    // markers on walkable floor, with a standing player's room above.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckMarkerFloor(Room room, RoomColumns columns)
    {
        foreach (RoomEntity e in room.Entities.Where(e => e.ClassName == "info_poi" && e.Origin is not null))
        {
            Point at = e.Origin!.Value;
            string what = e.Key("poi_type") ?? "info_poi";
            var under = columns.SurfaceUnder(at.X, at.Y, at.Z);
            if (under is not { } s || at.Z - s.Z > Step)
            {
                yield return new LintFinding(MarkerFloor, room.Name, $"{what} at ({at}) stands on no floor within a step below it");
                continue;
            }

            if (s.Clear < HullHeight || s.NormalZ < 0.7f)
            {
                yield return new LintFinding(MarkerFloor, room.Name,
                    $"{what} at ({at}) stands on a surface with {s.Clear} of headroom and a {s.NormalZ:0.00} slope normal; a player needs {HullHeight} on walkable floor");
                continue;
            }

            float h = HullWidth / 2f;
            foreach (var (dx, dy) in ((float, float)[])[(-h, -h), (h, -h), (-h, h), (h, h)])
                if (columns.SurfaceUnder(at.X + dx, at.Y + dy, s.Z + Step) is not { } c || s.Z - c.Z > Step || c.Clear < HullHeight)
                {
                    yield return new LintFinding(MarkerFloor, room.Name,
                        $"{what} at ({at}): a standing player's hull there has no floor or no headroom at ({at.X + dx} {at.Y + dy})");
                    break;
                }
        }
    }

    //-------------------------------------------------------------------------
    // jump rooms: 576 of clear air over half the floor and a straight 768 run.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckJumpClearAir(Room room, RoomColumns columns)
    {
        if (!room.Entry.Jump)
            yield break;
        var reached = columns.ReachableAtT1(room.Doors.Select(face => DoorInside(room.Kit, face, 48)));
        var (clear, floor, run) = columns.ClearAir(JumpTable.RocketClearAir, reached);
        if (clear * 2 < floor)
            yield return new LintFinding(JumpClearAir, room.Name,
                $"it is a jump room with {JumpTable.RocketClearAir} of clear air over {clear} of its {floor} floor columns; it needs half");
        if (run < JumpTable.RocketRun)
            yield return new LintFinding(JumpClearAir, room.Name,
                $"it is a jump room whose longest straight run under {JumpTable.RocketClearAir} of clear air is {run}; it needs {JumpTable.RocketRun}");
    }

    //-------------------------------------------------------------------------
    // every ledge above T1 pays: a reward on it, or a T1 way up to it.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckLedgePays(Room room, RoomColumns columns)
    {
        RoomKit kit = room.Kit;
        var starts = room.Doors.Select(face => DoorInside(kit, face, 48)).ToArray();
        var reached = columns.ReachableAtT1(starts);
        int reach = JumpTable.CriticalPathReach;
        float floor = kit.Sill;

        // The ledges: surfaces higher than T1 above the floor that no T1 route reaches,
        // grouped into the stretches a player can walk or crouch-jump about on.
        // A group with nowhere a player's hull fits (a frame's reveal, a wall's top) is no ledge.
        var ledges = columns.Surfaces().Where(s => s.Z - floor > reach && !reached.Contains(s.Key)).ToList();
        foreach (var group in columns.Group(ledges, reach, JumpTable.Of(JumpTier.T1).GapMax))
        {
            if (!group.Any(columns.HullFits))
                continue;
            bool paid = room.Markers.Any(m => Markers.IsReward(m.Kind)
                                              && group.Any(s => Math.Abs(s.X - m.At.X) <= Grid / 2f && Math.Abs(s.Y - m.At.Y) <= Grid / 2f
                                                                && m.At.Z - s.Z is >= 0 and <= Step));
            if (!paid)
            {
                var top = group.MaxBy(s => s.Z);
                yield return new LintFinding(LedgePays, room.Name,
                    $"a ledge of {group.Count} columns at {top.Z - floor} above the floor (near {top.X} {top.Y}) has no reward and no way up but a jump: every jump spot pays");
            }
        }
    }

    //-------------------------------------------------------------------------
    // nothing outside the cell.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckInsideCell(Room room)
    {
        float c = room.Kit.Cell, h = room.Height;
        bool Inside(Point p) => p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X <= c && p.Y <= c && p.Z <= h;
        foreach (Brush b in room.World.Concat(room.Entities.SelectMany(e => e.Brushes)))
            if (!Inside(b.Mins) || !Inside(b.Maxs))
                yield return new LintFinding(InsideCell, room.Name, $"a brush ({b.Mins})-({b.Maxs}) leaves the cell [0, {c}]² by [0, {h}]");
        foreach (RoomEntity e in room.Entities.Where(e => e.Origin is { } o && !Inside(o)))
            yield return new LintFinding(InsideCell, room.Name, $"{e.ClassName} at ({e.Origin}) is outside the cell");
    }

    //-------------------------------------------------------------------------
    // every door framed, and the frame leaving a giant's 92 by 156.
    //-------------------------------------------------------------------------

    public static IEnumerable<LintFinding> CheckFrameClear(Room room)
    {
        RoomKit kit = room.Kit;
        foreach (CellFace face in room.Doors)
        {
            string wall = RoomBuilder.WallName(face);
            var frame = room.Entities.Where(e => e.Key("room_needs") == "joined_" + wall).SelectMany(e => e.Brushes).ToArray();
            if (frame.Length == 0)
            {
                yield return new LintFinding(FrameClear, room.Name, $"its {wall} door has no frame; the joined doorway would draw as a gap");
                continue;
            }

            // The giant's box through the doorway, standing on whatever the frame puts on the sill.
            var (plugMins, plugMaxs) = kit.PlugBox(face);
            Point mid = new((plugMins.X + plugMaxs.X) / 2, (plugMins.Y + plugMaxs.Y) / 2, 0);
            float sill = frame.Select(b => b.Column(mid.X, mid.Y)).Where(c => c is { } v && v.High < kit.Sill + Step)
                .Select(c => c!.Value.High).DefaultIfEmpty(kit.Sill).Max();
            bool alongX = face is CellFace.North or CellFace.South;
            float gw = RoomKit.GiantWidth / 2f;
            Point mins = alongX ? new(mid.X - gw, plugMins.Y, sill) : new(plugMins.X, mid.Y - gw, sill);
            Point maxs = alongX ? new(mid.X + gw, plugMaxs.Y, sill + RoomKit.GiantHeight) : new(plugMaxs.X, mid.Y + gw, sill + RoomKit.GiantHeight);
            foreach (Brush b in frame.Where(b => b.Overlaps(mins, maxs)))
                yield return new LintFinding(FrameClear, room.Name,
                    $"its {wall} door's frame ({b.Mins})-({b.Maxs}) stands in a giant's {RoomKit.GiantWidth} by {RoomKit.GiantHeight}");
        }
    }

    //-------------------------------------------------------------------------

    /// <summary>A point <paramref name="depth"/> inside the room from a door's wall, at the door's middle, on the floor.</summary>
    public static Point DoorInside(RoomKit kit, CellFace face, float depth)
    {
        float c = kit.Cell, m = c / 2f, d = kit.WallDepth + depth;
        return face switch
        {
            CellFace.East => new(c - d, m, kit.Sill),
            CellFace.West => new(d, m, kit.Sill),
            CellFace.North => new(m, c - d, kit.Sill),
            _ => new(m, d, kit.Sill),
        };
    }

    private static readonly (int, int, int)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private static Point Centre(int x, int y, int z) => new(x * Grid + Grid / 2f, y * Grid + Grid / 2f, z * Grid + Grid / 2f);

    private static int Lo(float v) => Math.Max(0, (int)Math.Floor(v / Grid));

    private static int Hi(float v, int n) => Math.Min(n, (int)Math.Ceiling(v / Grid));
}

/// <summary>
/// A room's floor plan in 16-unit columns: in each column, the surfaces a
/// standing player fits on (the top of a run of solid brushes with at least
/// the hull's height clear above it), each with its clear air.
/// </summary>
public sealed class RoomColumns
{
    /// <summary>One surface: its column, height, the clear air above it and its face's upward normal.</summary>
    public readonly record struct Surface(int I, int J, int K, float X, float Y, float Z, float Clear, float NormalZ)
    {
        public (int, int, int) Key => (I, J, K);
    }

    /// <summary>
    /// Where in its 16-unit square a column is probed: just off the middle,
    /// at a half unit, so a brush face on a whole unit (every face here is)
    /// never lies on the probe and the column always falls to one side.
    /// </summary>
    public const float Probe = 8.5f;

    private readonly Room room;
    private readonly int n;
    private readonly Surface[][] columns;

    public RoomColumns(Room room)
    {
        this.room = room;
        RoomKit kit = room.Kit;
        n = kit.Cell / RoomLint.Grid;
        var plugs = room.Doors.Select(kit.PlugBox).ToHashSet();
        var solids = room.World.Where(b => !(b.IsBox && plugs.Contains((b.Mins, b.Maxs))))
            .Concat(room.Entities.Where(e => e.ClassName == "func_brush").SelectMany(e => e.Brushes)).ToArray();
        columns = new Surface[n * n][];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                columns[i * n + j] = Column(solids, i, j);
    }

    private Surface[] Column(Brush[] solids, int i, int j)
    {
        float x = i * RoomLint.Grid + Probe, y = j * RoomLint.Grid + Probe;
        var spans = new List<(float Low, float High, Brush Brush)>();
        foreach (Brush b in solids)
            if (b.Mins.X < x && b.Maxs.X > x && b.Mins.Y < y && b.Maxs.Y > y && b.Column(x, y) is { } c)
                spans.Add((c.Low, c.High, b));
        spans.Sort((a, b) => a.Low.CompareTo(b.Low));

        var merged = new List<(float Low, float High, Brush Top)>();
        foreach (var s in spans)
            if (merged.Count > 0 && s.Low <= merged[^1].High + Brush.Epsilon)
            {
                if (s.High > merged[^1].High)
                    merged[^1] = (merged[^1].Low, s.High, s.Brush);
            }
            else
                merged.Add(s);

        var surfaces = new List<Surface>();
        for (int k = 0; k < merged.Count; k++)
        {
            float top = merged[k].High, ceiling = k + 1 < merged.Count ? merged[k + 1].Low : room.Height;
            float clear = ceiling - top;
            if (clear < RoomLint.HullHeight)
                continue;
            float nz = merged[k].Top.Faces.Where(f => f.Normal.Z > 0)
                .Select(f => (f.Normal.Z, Z: (f.Distance - f.Normal.X * x - f.Normal.Y * y) / f.Normal.Z))
                .OrderBy(f => f.Z).Select(f => f.Item1).DefaultIfEmpty(1).First();
            surfaces.Add(new Surface(i, j, surfaces.Count, x, y, top, clear, nz));
        }

        return [.. surfaces];
    }

    private Surface[] At(int i, int j) => i < 0 || j < 0 || i >= n || j >= n ? [] : columns[i * n + j];

    /// <summary>The highest surface at or below <paramref name="z"/> in the column holding (x, y), or null.</summary>
    public Surface? SurfaceUnder(float x, float y, float z)
    {
        var col = At((int)Math.Floor(x / RoomLint.Grid), (int)Math.Floor(y / RoomLint.Grid));
        Surface? best = null;
        foreach (Surface s in col)
            if (s.Z <= z + Brush.Epsilon && (best is null || s.Z > best.Value.Z))
                best = s;
        return best;
    }

    /// <summary>
    /// Whether a standing player's hull fits on the surface: the eight
    /// columns round it (48 units across with it) hold a surface within a
    /// step of it. A frame's 8-unit reveal or a wall's top is not a ledge.
    /// </summary>
    public bool HullFits(Surface s)
    {
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                if (!At(s.I + di, s.J + dj).Any(t => Math.Abs(t.Z - s.Z) <= RoomLint.Step))
                    return false;
        return true;
    }

    /// <summary>Every surface of the room.</summary>
    public IEnumerable<Surface> Surfaces() => columns.SelectMany(c => c);

    /// <summary>
    /// The room's floor: each column whose lowest surface everyone reaches
    /// (<paramref name="reached"/>, from the doors). How many of those have at
    /// least <paramref name="air"/> clear above, how many there are, and the
    /// longest straight run (along x or y) of clear ones, in units.
    /// </summary>
    public (int Clear, int Floor, int Run) ClearAir(float air, IReadOnlySet<(int, int, int)> reached)
    {
        bool Floor(int i, int j) => At(i, j) is { Length: > 0 } c && reached.Contains(c[0].Key);
        bool Ok(int i, int j) => Floor(i, j) && At(i, j)[0].Clear >= air;
        int clear = 0, floor = 0, run = 0;
        for (int i = 0; i < n; i++)
        {
            int along = 0, across = 0;
            for (int j = 0; j < n; j++)
            {
                if (Floor(i, j))
                    floor++;
                if (Ok(i, j))
                    clear++;
                along = Ok(i, j) ? along + 1 : 0;
                across = Ok(j, i) ? across + 1 : 0;
                run = Math.Max(run, Math.Max(along, across) * RoomLint.Grid);
            }
        }

        return (clear, floor, run);
    }

    /// <summary>
    /// The surfaces everyone reaches from <paramref name="starts"/> (floor
    /// points) by T1 moves: to a neighbouring column's surface up to a
    /// crouch-jump higher, or any distance down.
    /// </summary>
    public HashSet<(int, int, int)> ReachableAtT1(IEnumerable<Point> starts)
    {
        int up = JumpTable.CriticalPathReach;
        var seen = new HashSet<(int, int, int)>();
        var queue = new Queue<Surface>();
        foreach (Point p in starts)
            if (SurfaceUnder(p.X, p.Y, p.Z + RoomLint.Step) is { } s && seen.Add(s.Key))
                queue.Enqueue(s);
        while (queue.Count > 0)
        {
            Surface s = queue.Dequeue();
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (-1, 0), (0, 1), (0, -1)])
                foreach (Surface t in At(s.I + di, s.J + dj))
                    if (t.Z - s.Z <= up && seen.Add(t.Key))
                        queue.Enqueue(t);
        }

        return seen;
    }

    /// <summary>
    /// The surfaces grouped into ledges: neighbouring columns within a step
    /// of each other are one surface, and surfaces within a crouch-jump
    /// (<paramref name="rise"/> up or down, <paramref name="gap"/> across)
    /// of each other are one ledge, so a row of pillars is one spot.
    /// </summary>
    public IEnumerable<List<Surface>> Group(IReadOnlyList<Surface> surfaces, int rise, int gap)
    {
        var index = surfaces.Select((s, i) => (s.Key, i)).ToDictionary(p => p.Key, p => p.i);
        var parent = Enumerable.Range(0, surfaces.Count).ToArray();
        int Find(int a) => parent[a] == a ? a : parent[a] = Find(parent[a]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        // Walkable stretches first.
        for (int a = 0; a < surfaces.Count; a++)
            foreach (var (di, dj) in ((int, int)[])[(1, 0), (0, 1)])
                foreach (Surface t in At(surfaces[a].I + di, surfaces[a].J + dj))
                    if (index.TryGetValue(t.Key, out int b) && Math.Abs(t.Z - surfaces[a].Z) <= RoomLint.Step)
                        Union(a, b);

        // Then stretches a crouch-jump apart, compared by their bounds.
        var stretches = surfaces.Select((s, i) => (s, i)).GroupBy(p => Find(p.i)).Select(g => g.ToList()).ToList();
        for (int a = 0; a < stretches.Count; a++)
            for (int b = a + 1; b < stretches.Count; b++)
            {
                var (ra, rb) = (Bounds(stretches[a]), Bounds(stretches[b]));
                float dx = Math.Max(0, Math.Max(ra.X0 - rb.X1, rb.X0 - ra.X1)), dy = Math.Max(0, Math.Max(ra.Y0 - rb.Y1, rb.Y0 - ra.Y1));
                if (Math.Sqrt(dx * dx + dy * dy) <= gap && Math.Abs(ra.Z - rb.Z) <= rise)
                    Union(stretches[a][0].i, stretches[b][0].i);
            }

        return surfaces.Select((s, i) => (s, i)).GroupBy(p => Find(p.i)).Select(g => g.Select(p => p.s).ToList());

        static (float X0, float X1, float Y0, float Y1, float Z) Bounds(List<(Surface s, int i)> g)
        {
            float h = RoomLint.Grid / 2f;
            return (g.Min(p => p.s.X) - h, g.Max(p => p.s.X) + h, g.Min(p => p.s.Y) - h, g.Max(p => p.s.Y) + h, g.Average(p => p.s.Z));
        }
    }
}
