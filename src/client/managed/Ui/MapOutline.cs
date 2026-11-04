using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// A map's outline for the minimap, from its BSP: the world's upward faces drawn on a grid,
/// kept where they connect to a spawn point (which leaves out the 3D skybox and anything else
/// a player can never stand near), and the edge of what is left, as line segments in a unit
/// square with north up. Several floors flatten into one shape.
/// </summary>
public sealed class MapOutline
{
    /// <summary>The finest grid, in world units. Coarser on a map too big for <see cref="MaxCells"/>.</summary>
    public const float FinestCell = 32f;

    /// <summary>The most cells across the longer side.</summary>
    public const int MaxCells = 256;

    /// <summary>How far apart floors may be and still connect, in cells: a door's frame, a step, a thin wall.</summary>
    public const int ReachCells = 2;

    /// <summary>The most segments an outline may keep; a busier one is built again on a coarser grid.</summary>
    public const int MaxSegments = 700;

    private const int LumpEntities = 0, LumpPlanes = 1, LumpVertexes = 3, LumpTexInfo = 6, LumpFaces = 7,
                      LumpEdges = 12, LumpSurfEdges = 13, LumpModels = 14;

    private const int FaceBytes = 56, PlaneBytes = 20, TexInfoBytes = 72, ModelBytes = 48;

    /// <summary>A face nobody stands on: SURF_SKY2D, SKY, NODRAW, HINT and SKIP.</summary>
    private const int NotAFloor = 0x2 | 0x4 | 0x80 | 0x100 | 0x200;

    /// <summary>How far up a face must look to be a floor: about 45 degrees.</summary>
    private const float FloorNormal = 0.7f;

    private readonly float _originX, _originY, _extent;

    private MapOutline(float[] segments, float originX, float originY, float extent)
    {
        Segments = segments;
        _originX = originX;
        _originY = originY;
        _extent = extent;
    }

    /// <summary>x0, y0, x1, y1 for each segment, 0..1 across the square.</summary>
    public float[] Segments { get; }

    /// <summary>Where a world position falls on the square: u east, v south. May fall outside 0..1.</summary>
    public (float U, float V) Project(Vector world)
        => ((world.X - _originX) / _extent, 1f - (world.Y - _originY) / _extent);

    /// <summary>A length in the world as a share of the square.</summary>
    public float ProjectLength(float world) => world / _extent;

    /// <summary>The outline of a map's file, or null for one that is not a BSP or has no floors.</summary>
    public static MapOutline? Build(ReadOnlySpan<byte> bsp)
    {
        if (bsp.Length < 8 + 64 * 16 || bsp[0] != 'V' || bsp[1] != 'B' || bsp[2] != 'S' || bsp[3] != 'P')
            return null;

        List<float[]> floors = Floors(bsp);
        if (floors.Count == 0)
            return null;

        List<(float X, float Y)> spawns = SpawnPoints(bsp);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (float[] floor in floors)
            for (int i = 0; i < floor.Length; i += 2)
            {
                minX = MathF.Min(minX, floor[i]);
                maxX = MathF.Max(maxX, floor[i]);
                minY = MathF.Min(minY, floor[i + 1]);
                maxY = MathF.Max(maxY, floor[i + 1]);
            }

        float cell = MathF.Max(FinestCell, MathF.Max(maxX - minX, maxY - minY) / MaxCells);
        for (; ; cell *= 1.5f)
        {
            MapOutline? outline = AtCell(floors, spawns, minX, minY, maxX, maxY, cell);
            if (outline is null || outline.Segments.Length / 4 <= MaxSegments)
                return outline;
        }
    }

    private static MapOutline? AtCell(List<float[]> floors, List<(float X, float Y)> spawns,
                                      float minX, float minY, float maxX, float maxY, float cell)
    {
        // One empty row and column all round, so the edge walk never leaves the grid.
        int w = (int)MathF.Ceiling((maxX - minX) / cell) + 3, h = (int)MathF.Ceiling((maxY - minY) / cell) + 3;
        float gridX = minX - cell, gridY = minY - cell;
        var filled = new bool[w * h];

        foreach (float[] floor in floors)
            Fill(floor, filled, w, h, gridX, gridY, cell);

        bool[] kept = spawns.Count > 0 ? Reachable(filled, w, h, spawns, gridX, gridY, cell) : filled;

        int fromX = w, fromY = h, toX = -1, toY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (kept[x + y * w])
                {
                    fromX = Math.Min(fromX, x);
                    toX = Math.Max(toX, x);
                    fromY = Math.Min(fromY, y);
                    toY = Math.Max(toY, y);
                }

        if (toX < 0)
            return null;

        // A square frame round what was kept, the shorter side centred in it.
        float keptW = (toX - fromX + 1) * cell, keptH = (toY - fromY + 1) * cell;
        float extent = MathF.Max(keptW, keptH);
        float originX = gridX + fromX * cell - (extent - keptW) / 2f;
        float originY = gridY + fromY * cell - (extent - keptH) / 2f;

        var segments = new List<float>();
        void Add(float x0, float y0, float x1, float y1)
        {
            segments.Add((x0 - originX) / extent);
            segments.Add(1f - (y0 - originY) / extent);
            segments.Add((x1 - originX) / extent);
            segments.Add(1f - (y1 - originY) / extent);
        }

        // Every horizontal edge, joined along its row into one segment; below it, the columns.
        for (int side = 0; side < 2; side++)
            for (int y = 1; y < h; y++)
            {
                int run = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool edge = x < w && kept[x + y * w] != kept[x + (y - 1) * w]
                                      && (kept[x + y * w] == (side == 0));
                    if (edge && run < 0)
                        run = x;
                    else if (!edge && run >= 0)
                    {
                        float at = gridY + y * cell;
                        Add(gridX + run * cell, at, gridX + x * cell, at);
                        run = -1;
                    }
                }
            }

        for (int side = 0; side < 2; side++)
            for (int x = 1; x < w; x++)
            {
                int run = -1;
                for (int y = 0; y <= h; y++)
                {
                    bool edge = y < h && kept[x + y * w] != kept[x - 1 + y * w]
                                      && (kept[x + y * w] == (side == 0));
                    if (edge && run < 0)
                        run = y;
                    else if (!edge && run >= 0)
                    {
                        float at = gridX + x * cell;
                        Add(at, gridY + run * cell, at, gridY + y * cell);
                        run = -1;
                    }
                }
            }

        return new MapOutline(segments.ToArray(), originX, originY, extent);
    }

    /// <summary>The cells a floor's polygon covers, by each cell's middle (even-odd).</summary>
    private static void Fill(float[] poly, bool[] filled, int w, int h, float gridX, float gridY, float cell)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < poly.Length; i += 2)
        {
            minX = MathF.Min(minX, poly[i]);
            maxX = MathF.Max(maxX, poly[i]);
            minY = MathF.Min(minY, poly[i + 1]);
            maxY = MathF.Max(maxY, poly[i + 1]);
        }

        int x0 = Math.Max(0, (int)((minX - gridX) / cell)), x1 = Math.Min(w - 1, (int)((maxX - gridX) / cell));
        int y0 = Math.Max(0, (int)((minY - gridY) / cell)), y1 = Math.Min(h - 1, (int)((maxY - gridY) / cell));

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Inside(poly, gridX + (x + 0.5f) * cell, gridY + (y + 0.5f) * cell))
                    filled[x + y * w] = true;
    }

    private static bool Inside(float[] poly, float px, float py)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 2; i < poly.Length; j = i, i += 2)
        {
            float xi = poly[i], yi = poly[i + 1], xj = poly[j], yj = poly[j + 1];
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>The filled cells a spawn can reach, stepping over gaps of up to <see cref="ReachCells"/>.</summary>
    private static bool[] Reachable(bool[] filled, int w, int h, List<(float X, float Y)> spawns,
                                    float gridX, float gridY, float cell)
    {
        var kept = new bool[filled.Length];
        var open = new Stack<int>();

        foreach ((float sx, float sy) in spawns)
        {
            int cx = (int)((sx - gridX) / cell), cy = (int)((sy - gridY) / cell);
            Visit(cx, cy);
        }

        while (open.Count > 0)
        {
            int at = open.Pop();
            Visit(at % w, at / w);
        }

        return kept;

        void Visit(int cx, int cy)
        {
            for (int dy = -ReachCells; dy <= ReachCells; dy++)
                for (int dx = -ReachCells; dx <= ReachCells; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h)
                        continue;

                    int i = x + y * w;
                    if (filled[i] && !kept[i])
                    {
                        kept[i] = true;
                        open.Push(i);
                    }
                }
        }
    }

    /// <summary>The world model's faces that look up, each as x, y pairs.</summary>
    private static List<float[]> Floors(ReadOnlySpan<byte> bsp)
    {
        var floors = new List<float[]>();

        ReadOnlySpan<byte> models = Lump(bsp, LumpModels), faces = Lump(bsp, LumpFaces), planes = Lump(bsp, LumpPlanes);
        ReadOnlySpan<byte> vertexes = Lump(bsp, LumpVertexes), edges = Lump(bsp, LumpEdges);
        ReadOnlySpan<byte> surfEdges = Lump(bsp, LumpSurfEdges), texInfo = Lump(bsp, LumpTexInfo);

        if (models.Length < ModelBytes)
            return floors;

        int first = BitConverter.ToInt32(models[40..]), count = BitConverter.ToInt32(models[44..]);

        for (int f = first; f < first + count && (f + 1) * FaceBytes <= faces.Length; f++)
        {
            ReadOnlySpan<byte> face = faces.Slice(f * FaceBytes, FaceBytes);
            int plane = BitConverter.ToUInt16(face), side = face[2];
            int firstEdge = BitConverter.ToInt32(face[4..]), edgeCount = BitConverter.ToInt16(face[8..]);
            int info = BitConverter.ToInt16(face[10..]);

            if ((plane + 1) * PlaneBytes > planes.Length || edgeCount < 3)
                continue;

            if (info >= 0 && (info + 1) * TexInfoBytes <= texInfo.Length
                && (BitConverter.ToInt32(texInfo[(info * TexInfoBytes + 64)..]) & NotAFloor) != 0)
                continue;

            float normalZ = BitConverter.ToSingle(planes[(plane * PlaneBytes + 8)..]);
            if ((side == 0 ? normalZ : -normalZ) < FloorNormal)
                continue;

            var poly = new float[edgeCount * 2];
            for (int e = 0; e < edgeCount; e++)
            {
                int surfEdge = BitConverter.ToInt32(surfEdges[((firstEdge + e) * 4)..]);
                int edge = Math.Abs(surfEdge) * 4 + (surfEdge < 0 ? 2 : 0);
                int vertex = BitConverter.ToUInt16(edges[edge..]);
                poly[e * 2] = BitConverter.ToSingle(vertexes[(vertex * 12)..]);
                poly[e * 2 + 1] = BitConverter.ToSingle(vertexes[(vertex * 12 + 4)..]);
            }

            floors.Add(poly);
        }

        return floors;
    }

    /// <summary>Where the map's spawn points stand, from its entity lump.</summary>
    private static List<(float X, float Y)> SpawnPoints(ReadOnlySpan<byte> bsp)
    {
        var spawns = new List<(float X, float Y)>();
        string text = Encoding.ASCII.GetString(Lump(bsp, LumpEntities));

        foreach (Match entity in Regex.Matches(text, "\\{[^{}]*\\}"))
        {
            if (!Regex.IsMatch(entity.Value, "\"classname\"\\s+\"info_player_[a-z_]+\""))
                continue;

            Match origin = Regex.Match(entity.Value, "\"origin\"\\s+\"(\\S+) (\\S+) \\S+\"");
            if (origin.Success
                && float.TryParse(origin.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(origin.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                spawns.Add((x, y));
        }

        return spawns;
    }

    private static ReadOnlySpan<byte> Lump(ReadOnlySpan<byte> bsp, int lump)
    {
        int offset = BitConverter.ToInt32(bsp[(8 + lump * 16)..]), length = BitConverter.ToInt32(bsp[(12 + lump * 16)..]);
        return offset < 0 || length <= 0 || offset + length > bsp.Length ? default : bsp.Slice(offset, length);
    }
}
