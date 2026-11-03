using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// The derived graph cached on disk (plan_hl2mp_ai §6.2):
/// <c>&lt;dir&gt;/&lt;map&gt;-&lt;sha256(bsp)[:16]&gt;-&lt;sha256(nav3d)[:16]&gt;-v&lt;MapFormatsVersion&gt;-g&lt;revision&gt;.navgraph</c>.
/// The name and the header both carry the two hashes, the reader version and
/// the derivation's revision, so a changed map, a changed nav, another reader
/// or a changed derivation finds no file, and a
/// file whose header disagrees with its name is ignored and rewritten. A
/// cached graph rebuilds through <see cref="NavGraphBuilder"/>, so it hashes
/// equal to a fresh derivation.
/// </summary>
/// <remarks>
/// Format (little-endian, length-prefixed strings): magic <c>SSNAVG01</c>;
/// reader version, bsp sha256, nav sha256, map; leaf count; cells (min x, min
/// y, max x, max y, floor, classes, flags, cost); the obstacles (name, kind,
/// classname, mins, maxs); edges (from, to, type, obstacle); each cell's leaf.
/// </remarks>
public sealed class NavGraphCache(string directory, NavEdgeCosts costs)
{
    private static ReadOnlySpan<byte> Magic => "SSNAVG01"u8;

    /// <summary>What a cached graph was written by: the reader's package and the derivation's revision.</summary>
    private static string Version => $"{NavReaderVersion.Package}-g{NavGraphFromNav3d.Revision}";

    public string Directory => directory;

    public string PathFor(string map, string bspSha256, string navSha256)
        => Path.Combine(directory, $"{map}-{bspSha256[..16]}-{navSha256[..16]}-v{Version}.navgraph");

    /// <summary>The cached graph, or null when there is none or it does not describe these files.</summary>
    public DerivedNav? TryRead(string map, string bspSha256, string navSha256)
    {
        string path = PathFor(map, bspSha256, navSha256);
        if (!File.Exists(path))
            return null;
        try
        {
            return Decode(File.ReadAllBytes(path), map, bspSha256, navSha256);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public void Write(string map, string bspSha256, string navSha256, DerivedNav nav)
    {
        System.IO.Directory.CreateDirectory(directory);
        string path = PathFor(map, bspSha256, navSha256);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, Encode(map, bspSha256, navSha256, nav));
        File.Move(temp, path, overwrite: true);
    }

    public byte[] Encode(string map, string bspSha256, string navSha256, DerivedNav nav)
    {
        ArgumentNullException.ThrowIfNull(nav);
        var w = new Writer();
        w.Bytes(Magic);
        w.Str(Version);
        w.Str(bspSha256);
        w.Str(navSha256);
        w.Str(map);
        w.I32(nav.Leaves);
        NavGraph g = nav.Graph;
        w.I32(g.CellCount);
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            w.F32(cell.MinX);
            w.F32(cell.MinY);
            w.F32(cell.MaxX);
            w.F32(cell.MaxY);
            w.F32(cell.FloorZ);
            w.I32((int)cell.Classes | ((int)cell.Flags << 8));
            w.F32(cell.CostMultiplier);
        }

        w.I32(nav.Obstacles.Length);
        foreach (NavObstacleInfo o in nav.Obstacles)
        {
            w.Str(o.Name);
            w.I32((int)o.Kind);
            w.Str(o.ClassName);
            w.Vec(o.Mins);
            w.Vec(o.Maxs);
        }

        w.I32(g.EdgeCount);
        for (int c = 0; c < g.CellCount; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                w.I32(e.From);
                w.I32(e.To);
                w.I32((int)e.Type);
                w.I32(e.Obstacle);
            }
        }

        foreach (int leaf in nav.LeafOfCell)
            w.I32(leaf);
        return w.ToArray();
    }

    /// <summary>The graph a cache file holds, or an exception when it is not one for these files.</summary>
    public DerivedNav Decode(ReadOnlySpan<byte> bytes, string map, string bspSha256, string navSha256)
    {
        var r = new Reader(bytes);
        if (!r.Bytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("not a .navgraph");
        string version = r.Str(), bsp = r.Str(), nav = r.Str(), name = r.Str();
        if (version != Version || bsp != bspSha256 || nav != navSha256 || name != map)
            throw new InvalidDataException($"the cache names {name} {bsp[..Math.Min(16, bsp.Length)]} {nav[..Math.Min(16, nav.Length)]} v{version}");
        int leaves = r.I32();
        var b = new NavGraphBuilder();
        int cells = r.I32();
        for (int c = 0; c < cells; c++)
        {
            float minX = r.F32(), minY = r.F32(), maxX = r.F32(), maxY = r.F32(), floor = r.F32();
            int bits = r.I32();
            b.AddCell(new NavCell(minX, minY, maxX, maxY, floor, (ClearanceClasses)(bits & 0xFF), (NavCellFlags)(bits >> 8), r.F32()));
        }

        var obstacles = new NavObstacleInfo[r.I32()];
        for (int o = 0; o < obstacles.Length; o++)
        {
            obstacles[o] = new NavObstacleInfo(r.Str(), (NavObstacleKind)r.I32(), r.Str(), r.Vec(), r.Vec());
            b.AddObstacle();
        }

        int edges = r.I32();
        for (int e = 0; e < edges; e++)
        {
            int from = r.I32(), to = r.I32();
            var type = (NavEdgeType)r.I32();
            int obstacle = r.I32();
            b.AddEdge(new CellId(from), new CellId(to), type, costs.ExtraFor(type), obstacle);
        }

        int[] leafOfCell = new int[cells];
        int[] cellOfLeaf = new int[leaves];
        Array.Fill(cellOfLeaf, -1);
        for (int c = 0; c < cells; c++)
        {
            leafOfCell[c] = r.I32();
            cellOfLeaf[leafOfCell[c]] = c;
        }

        if (!r.AtEnd)
            throw new InvalidDataException("bytes follow the last cell");
        return new DerivedNav(b.Build(), cellOfLeaf, leafOfCell, obstacles, leaves);
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = new();
        private readonly byte[] _scratch = new byte[4];

        public void Bytes(ReadOnlySpan<byte> b) => _bytes.AddRange(b.ToArray());

        public void I32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_scratch, v);
            _bytes.AddRange(_scratch);
        }

        public void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));

        public void Vec(AiVector v)
        {
            F32(v.X);
            F32(v.Y);
            F32(v.Z);
        }

        public void Str(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            I32(b.Length);
            _bytes.AddRange(b);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        public readonly bool AtEnd => _at == _bytes.Length;

        public ReadOnlySpan<byte> Bytes(int n)
        {
            if (n < 0 || _at + n > _bytes.Length)
                throw new InvalidDataException("the .navgraph ends early");
            ReadOnlySpan<byte> s = _bytes.Slice(_at, n);
            _at += n;
            return s;
        }

        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));

        public float F32() => BitConverter.Int32BitsToSingle(I32());

        public AiVector Vec() => new(F32(), F32(), F32());

        public string Str() => Encoding.UTF8.GetString(Bytes(I32()));
    }
}
