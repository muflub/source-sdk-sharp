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
