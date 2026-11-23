using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>A region's voxels in one cell of the whole grid: the lowest and the highest voxel any of its boxes there holds.</summary>
public readonly record struct NavRoomSpan(int Region, ushort ZLo, ushort ZHi);

/// <summary>
/// Which region of a hull navigation a place is in, without the hull navigation open
/// (plan_bots_teams_tf2_part_g, the user's Q-R1 of 2026-11-13: a file per sealed space, loaded on
/// demand, needs to know whose file a bot found after a teleport stands in). Per cell of the whole
/// grid, each region with a box there and the span of voxels its boxes there cover, low to high.
/// <see cref="RegionsAt"/> answers the regions whose span in the point's cell holds the point's
/// voxel: one where regions do not share a cell's height, and every candidate where they do
/// (<see cref="SharedCells"/> counts those cells); the candidate's own file then settles it.
/// </summary>
public sealed class NavRoomIndex
{
    private static ReadOnlySpan<byte> Magic => "SSROOMS1"u8;

    private readonly Dictionary<int, NavRoomSpan[]> _cells;

    private NavRoomIndex(Vec3 origin, float voxelSize, int cellVoxels, int columns, int rows, int regions, Dictionary<int, NavRoomSpan[]> cells)
    {
        Origin = origin;
        VoxelSize = voxelSize;
        CellVoxels = cellVoxels;
        Columns = columns;
        Rows = rows;
        RegionCount = regions;
        _cells = cells;
    }

    public Vec3 Origin { get; }

    public float VoxelSize { get; }

    public int CellVoxels { get; }

    public int Columns { get; }

    public int Rows { get; }

    public int RegionCount { get; }

    /// <summary>How many cells of the grid hold a box of some region.</summary>
    public int Cells => _cells.Count;

    /// <summary>How many spans the index holds: a region's boxes in a cell, one each.</summary>
    public int Spans => _cells.Values.Sum(s => s.Length);

    /// <summary>The cells where two regions' spans overlap in height: there a point may answer more than one region.</summary>
    public int SharedCells => _cells.Values.Count(Shared);

    private static bool Shared(NavRoomSpan[] spans)
    {
        for (int i = 1; i < spans.Length; i++)
        {
            if (spans[i].ZLo <= spans[i - 1].ZHi)
                return true;
        }

        return false;
    }

    /// <summary>The index of <paramref name="reader"/>'s regions.</summary>
    public static NavRoomIndex Of(NavHullReader reader, NavHullRegions regions)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        var per = new Dictionary<int, Dictionary<int, (int Lo, int Hi)>>();
        for (int box = 0; box < reader.BoxCount; box++)
        {
            int cell = reader.BoxCell(box), region = regions.RegionOf(box);
            NavHullBox b = reader.Box(box);
            if (!per.TryGetValue(cell, out Dictionary<int, (int, int)>? spans))
                per[cell] = spans = new Dictionary<int, (int, int)>();
            spans[region] = spans.TryGetValue(region, out (int Lo, int Hi) s) ? (Math.Min(s.Lo, b.ZLo), Math.Max(s.Hi, b.ZHi)) : (b.ZLo, b.ZHi);
        }

        var cells = new Dictionary<int, NavRoomSpan[]>(per.Count);
        foreach ((int cell, Dictionary<int, (int Lo, int Hi)> spans) in per)
        {
            cells[cell] = [.. spans.Select(p => new NavRoomSpan(p.Key, (ushort)p.Value.Lo, (ushort)p.Value.Hi)).OrderBy(s => s.ZLo).ThenBy(s => s.Region)];
        }

        return new NavRoomIndex(reader.Origin, reader.VoxelSize, reader.CellVoxels, reader.Columns, reader.Rows, regions.Count, cells);
    }

    /// <summary>The regions whose span in <paramref name="point"/>'s cell holds its voxel, into <paramref name="into"/> (cleared first), lowest span first.</summary>
    public void RegionsAt(Vec3 point, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        double lx = point.X - Origin.X, ly = point.Y - Origin.Y, lz = point.Z - Origin.Z;
        if (!(lx >= 0 && ly >= 0 && lz >= 0))
            return;
        long gx = (long)Math.Floor(lx / VoxelSize), gy = (long)Math.Floor(ly / VoxelSize), gz = (long)Math.Floor(lz / VoxelSize);
        if (gx >= (long)Columns * CellVoxels || gy >= (long)Rows * CellVoxels || gz > ushort.MaxValue)
            return;
        int cell = (int)((gy / CellVoxels * Columns) + (gx / CellVoxels));
        if (!_cells.TryGetValue(cell, out NavRoomSpan[]? spans))
            return;
        foreach (NavRoomSpan s in spans)
        {
            if (gz >= s.ZLo && gz <= s.ZHi)
                into.Add(s.Region);
        }
    }

    /// <summary>The index as bytes: a header, then each cell (ascending) with its spans.</summary>
    public byte[] Write()
    {
        int[] order = [.. _cells.Keys.Order()];
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Magic);
        w.Write(Origin.X);
        w.Write(Origin.Y);
        w.Write(Origin.Z);
        w.Write(VoxelSize);
        w.Write(CellVoxels);
        w.Write(Columns);
        w.Write(Rows);
        w.Write(RegionCount);
        w.Write(order.Length);
        foreach (int cell in order)
        {
            NavRoomSpan[] spans = _cells[cell];
            w.Write(cell);
            w.Write((ushort)spans.Length);
            foreach (NavRoomSpan s in spans)
            {
                w.Write((ushort)s.Region);
                w.Write(s.ZLo);
                w.Write(s.ZHi);
            }
        }

        w.Flush();
        return stream.ToArray();
    }

    /// <summary>An index <see cref="Write"/> wrote.</summary>
    /// <exception cref="InvalidDataException">The bytes are not one.</exception>
    public static NavRoomIndex Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException("not a room index");
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes, 8, bytes.Length - 8, writable: false));
            var origin = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float voxel = r.ReadSingle();
            int cellVoxels = r.ReadInt32(), columns = r.ReadInt32(), rows = r.ReadInt32(), regions = r.ReadInt32(), count = r.ReadInt32();
            var cells = new Dictionary<int, NavRoomSpan[]>(count);
            for (int i = 0; i < count; i++)
            {
                int cell = r.ReadInt32();
                var spans = new NavRoomSpan[r.ReadUInt16()];
                for (int s = 0; s < spans.Length; s++)
                    spans[s] = new NavRoomSpan(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16());
                cells[cell] = spans;
            }

            if (r.BaseStream.Position != r.BaseStream.Length)
                throw new InvalidDataException("bytes follow the last cell");
            return new NavRoomIndex(origin, voxel, cellVoxels, columns, rows, regions, cells);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("the room index ends early", e);
        }
    }
}

/// <summary>A teleport as a room's table names it: its own name and its target's, the room it lands in, where, and its brushes.</summary>
public sealed record NavRoomTeleport(string? Name, string? Target, int To, AiVector? Destination, IReadOnlyList<HurtVolume> Brushes);

/// <summary>
/// One room of a map in rooms (plan_bots_teams_tf2_part_g R.2 items 2 and 7): its region and
/// file, its leaves, where a player comes into it (spawns and teleport destinations), its
/// teleports by what they are, and its capture areas.
/// <list type="bullet">
/// <item><see cref="Fails"/>: teleports landing in the room that no body steps into from a way in
/// without a fall (<see cref="NavRoomTeleports.Classify"/>): a fail floor, whose cells the room's
/// derivation leaves out.</item>
/// <item><see cref="Kept"/>: teleports landing in the room that a body walks into from a way in (a
/// hub's transport, a checkpoint): their cells stay (R.5's checkpoint risk).</item>
/// <item><see cref="Exits"/>: teleports landing in another room, or nowhere.</item>
/// </list>
/// </summary>
public sealed record NavRoom(int Region, string File, int Leaves, IReadOnlyList<AiVector> WaysIn, IReadOnlyList<NavRoomTeleport> Fails,
                             IReadOnlyList<NavRoomTeleport> Kept, IReadOnlyList<NavRoomTeleport> Exits, IReadOnlyList<HurtVolume> Captures)
{
    /// <summary>The player spawns among the ways in (the rest are teleport destinations): the room a level starts in holds the most.</summary>
    public IReadOnlyList<AiVector> Spawns { get; init; } = [];

    /// <summary>The volumes the room's derivation leaves out: every fail floor's brushes.</summary>
    public IReadOnlyList<HurtVolume> FailVolumes => [.. Fails.SelectMany(t => t.Brushes)];
}

/// <summary>
/// The table beside a map's room index (<c>&lt;map&gt;.navrooms.json</c>): the map, the rules'
/// revision, the whole navigation's leaves (what put it in rooms: over the leaf limit), and each
/// room. Written by the navigation build (<c>ai-replay navsplit write ... rooms</c>), read by the
/// game, which never opens the whole map's file.
/// </summary>
public sealed record NavRoomTable(string Map, int Revision, long WholeLeaves, IReadOnlyList<NavRoom> Rooms)
{
    /// <summary>
    /// The revision of the rules a table is written by (<see cref="NavRoomTeleports.Classify"/>): part of a
    /// room's cache name, so a graph made by other rules is not read back.
    /// </summary>
    public const int RulesRevision = 1;

    /// <summary>The room of <paramref name="region"/>, or null.</summary>
    public NavRoom? Room(int region) => Rooms.FirstOrDefault(r => r.Region == region);

    /// <summary>The room a level starts in: the one holding the most spawns (the lowest region of those that tie), or null with none.</summary>
    public NavRoom? StartRoom => Rooms.Where(r => r.Spawns.Count > 0).OrderByDescending(r => r.Spawns.Count).ThenBy(r => r.Region).FirstOrDefault();

    /// <summary>The table as JSON.</summary>
    public string ToJson()
    {
        static float[] V(AiVector v) => [v.X, v.Y, v.Z];
        static float[][] Boxes(IEnumerable<HurtVolume> b) => [.. b.Select(h => (float[])[h.Min.X, h.Min.Y, h.Min.Z, h.Max.X, h.Max.Y, h.Max.Z])];
        static object T(NavRoomTeleport t) => new { name = t.Name, target = t.Target, to = t.To, destination = t.Destination is { } d ? V(d) : null, brushes = Boxes(t.Brushes) };
        var o = new
        {
            map = Map,
            revision = Revision,
            wholeLeaves = WholeLeaves,
            rooms = Rooms.Select(r => new
            {
                region = r.Region,
                file = r.File,
                leaves = r.Leaves,
                waysIn = r.WaysIn.Select(V),
                spawns = r.Spawns.Select(V),
                fails = r.Fails.Select(T),
                kept = r.Kept.Select(T),
                exits = r.Exits.Select(T),
                captures = Boxes(r.Captures),
            }),
        };
        return System.Text.Json.JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>A table <see cref="ToJson"/> wrote.</summary>
    /// <exception cref="InvalidDataException">The text is not one.</exception>
    public static NavRoomTable FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement root = doc.RootElement;
            static AiVector V(System.Text.Json.JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            static HurtVolume Box(System.Text.Json.JsonElement e) => new(new AiVector(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle()),
                                                                         new AiVector(e[3].GetSingle(), e[4].GetSingle(), e[5].GetSingle()));
            static NavRoomTeleport T(System.Text.Json.JsonElement e)
                => new(e.GetProperty("name").GetString(), e.GetProperty("target").GetString(), e.GetProperty("to").GetInt32(),
                       e.GetProperty("destination").ValueKind == System.Text.Json.JsonValueKind.Null ? null : V(e.GetProperty("destination")),
                       [.. e.GetProperty("brushes").EnumerateArray().Select(Box)]);
            var rooms = new List<NavRoom>();
            foreach (System.Text.Json.JsonElement r in root.GetProperty("rooms").EnumerateArray())
            {
                rooms.Add(new NavRoom(r.GetProperty("region").GetInt32(), r.GetProperty("file").GetString() ?? throw new InvalidDataException("a room names no file"),
                                      r.GetProperty("leaves").GetInt32(), [.. r.GetProperty("waysIn").EnumerateArray().Select(V)],
                                      [.. r.GetProperty("fails").EnumerateArray().Select(T)], [.. r.GetProperty("kept").EnumerateArray().Select(T)],
                                      [.. r.GetProperty("exits").EnumerateArray().Select(T)], [.. r.GetProperty("captures").EnumerateArray().Select(Box)])
                {
                    Spawns = [.. r.GetProperty("spawns").EnumerateArray().Select(V)],
                });
            }

            return new NavRoomTable(root.GetProperty("map").GetString() ?? "", root.GetProperty("revision").GetInt32(), root.GetProperty("wholeLeaves").GetInt64(), rooms);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            throw new InvalidDataException($"not a room table: {e.Message}", e);
        }
    }
}

/// <summary>
/// Which of a room's teleports back are fail floors (R.5's checkpoint risk, the user's Q-R5 of
/// 2026-11-16): one whose cells no body reaches on its feet from where the room is come into. On
/// the room's graph with every teleport's cells in it, a body walks from the ways in (spawns,
/// teleport destinations) by every move but a drop, stopping at a teleport back's cells; a
/// teleport stepped into from a cell so reached, by a move other than a drop, is one a route uses
/// (a hub's transport, a checkpoint) and keeps its cells; the rest are fallen into.
/// </summary>
public static class NavRoomTeleports
{
    /// <summary>Whether each of <paramref name="teleports"/> (each its brushes) is a fail floor on <paramref name="graph"/>, from <paramref name="waysIn"/>.</summary>
    public static bool[] Classify(NavGraph graph, IReadOnlyList<AiVector> waysIn, IReadOnlyList<IReadOnlyList<HurtVolume>> teleports)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(waysIn);
        ArgumentNullException.ThrowIfNull(teleports);
        int cells = graph.CellCount;
        var inside = new bool[cells];
        var near = new List<int>();
        var of = new List<int>[teleports.Count];
        for (int t = 0; t < teleports.Count; t++)
        {
            of[t] = new List<int>();
            foreach (HurtVolume b in teleports[t])
            {
                near.Clear();
                graph.CellsNear(b.Min.X, b.Min.Y, b.Max.X, b.Max.Y, near);
                foreach (int c in near)
                {
                    if (Inside(graph.Cell(c), b) && !of[t].Contains(c))
                    {
                        of[t].Add(c);
                        inside[c] = true;
                    }
                }
            }
        }

        var walked = new bool[cells];
        var queue = new Queue<int>();
        foreach (AiVector w in waysIn)
        {
            CellId seed = NavPoints.CellNear(graph, w, ClearanceClass.Stand);
            if (seed.IsValid && !inside[seed.Index] && !walked[seed.Index])
            {
                walked[seed.Index] = true;
                queue.Enqueue(seed.Index);
            }
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (e.Type != NavEdgeType.Drop && !inside[e.To] && !walked[e.To])
                {
                    walked[e.To] = true;
                    queue.Enqueue(e.To);
                }
            }
        }

        var fail = new bool[teleports.Count];
        for (int t = 0; t < teleports.Count; t++)
        {
            bool steppedInto = false;
            foreach (int c in of[t])
            {
                foreach (NavEdge e in graph.EdgesTo(c))
                    steppedInto |= walked[e.From] && e.Type != NavEdgeType.Drop;
            }

            fail[t] = !steppedInto;
        }

        return fail;
    }

    /// <summary>Whether a cell stands inside a volume, by the derivation's own test for a hurt volume.</summary>
    public static bool Inside(in NavCell c, in HurtVolume h)
        => c.MaxX > h.Min.X && c.MinX < h.Max.X && c.MaxY > h.Min.Y && c.MinY < h.Max.Y && c.FloorZ >= h.Min.Z - 1f && c.FloorZ < h.Max.Z;
}
