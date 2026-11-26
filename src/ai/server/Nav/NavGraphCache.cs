using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// The derived graph cached on disk (plan_hl2mp_ai §6.2):
/// <c>&lt;dir&gt;/&lt;map&gt;-&lt;sha256(bsp)[:16]&gt;-&lt;sha256(nav3d)[:16]&gt;-v&lt;MapFormatsVersion&gt;-g&lt;revision&gt;[-n&lt;numbers&gt;].navgraph</c>,
/// the last part only for a game whose numbers are not the core's default movement's (<see cref="NumbersKey"/>).
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

    /// <summary>
    /// What a cached graph was written by: the reader's package, the derivation's revision,
    /// and the numbers it derived by when they are not the default's (<see cref="NumbersKey"/>).
    /// </summary>
    private static string Version(string numbersKey)
        => numbersKey.Length == 0 ? $"{NavReaderVersion.Package}-g{NavGraphFromNav3d.Revision}"
                                  : $"{NavReaderVersion.Package}-g{NavGraphFromNav3d.Revision}-n{numbersKey}";

    public string Directory => directory;

    /// <summary>
    /// The name a derivation's numbers carry in the cache: "" for the core's default movement over
    /// a file of jump height <paramref name="fileJumpHeight"/> (so HL2MP's graphs keep their names),
    /// else the first 16 hex digits of the SHA-256 of their bits. The extra hulls
    /// (<see cref="NavNumbers.ExtraHulls"/>) are bits after the body's ten numbers, a width and a
    /// height each in their order, then the revision of the rule that admits them
    /// (<see cref="NavGraphFromNav3d.ExtraHullRevision"/>), and none when there are none: numbers
    /// without them are named as they were before a graph could be classed for more hulls, and a
    /// change to the extra hulls' rule renames only the graphs that carry their bits. The triple jump's rise
    /// (<see cref="NavNumbers.MaxTripleJumpRise"/>) and the revision of its rule (<see cref="NavGraphFromNav3d.TripleJumpRevision"/>)
    /// are the last bits, and none at 0: numbers without one keep their names.
    /// </summary>
    public static string NumbersKey(NavNumbers numbers, float fileJumpHeight)
    {
        if (numbers == NavNumbers.Stock(fileJumpHeight))
            return "";
        NavHulls hulls = numbers.ExtraHulls;
        bool triple = numbers.MaxTripleJumpRise > 0f;
        int body = 10 + 2 * hulls.Count + (hulls.Count > 0 ? 1 : 0);
        Span<byte> bits = stackalloc byte[(13 + 2 * NavHulls.Max) * sizeof(float)];
        bits = bits[..((body + (triple ? 2 : 0)) * sizeof(float))];
        ReadOnlySpan<float> values = [numbers.HullWidth, numbers.StandHeight, numbers.CrouchHeight, numbers.StepHeight,
                                      numbers.MaxJumpRise, numbers.MaxCrouchJumpRise, numbers.SafeDrop, numbers.MaxDoubleJumpRise,
                                      numbers.WideHull ? 1f : 0f, numbers.CrouchOnlyCost];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bits[(i * sizeof(float))..], values[i]);
        for (int i = 0; i < hulls.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bits[((10 + 2 * i) * sizeof(float))..], hulls[i].Width);
            BinaryPrimitives.WriteSingleLittleEndian(bits[((11 + 2 * i) * sizeof(float))..], hulls[i].Height);
        }

        if (hulls.Count > 0)
            BinaryPrimitives.WriteInt32LittleEndian(bits[((10 + 2 * hulls.Count) * sizeof(float))..], NavGraphFromNav3d.ExtraHullRevision);
        if (triple)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bits[(body * sizeof(float))..], numbers.MaxTripleJumpRise);
            BinaryPrimitives.WriteInt32LittleEndian(bits[((body + 1) * sizeof(float))..], NavGraphFromNav3d.TripleJumpRevision);
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bits))[..16];
    }

    public string PathFor(string map, string bspSha256, string navSha256, string numbersKey = "")
        => Path.Combine(directory, $"{map}-{bspSha256[..16]}-{navSha256[..16]}-v{Version(numbersKey)}.navgraph");

    /// <summary>The cached graph, or null when there is none or it does not describe these files.</summary>
    public DerivedNav? TryRead(string map, string bspSha256, string navSha256, string numbersKey = "")
    {
        string path = PathFor(map, bspSha256, navSha256, numbersKey);
        if (!File.Exists(path))
            return null;
        try
        {
            return Decode(File.ReadAllBytes(path), map, bspSha256, navSha256, numbersKey);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public void Write(string map, string bspSha256, string navSha256, DerivedNav nav, string numbersKey = "")
    {
        System.IO.Directory.CreateDirectory(directory);
        // A temporary name of its own: two games on one tree derive the same key at once.
        BuiltNavStore.Replace(PathFor(map, bspSha256, navSha256, numbersKey), Encode(map, bspSha256, navSha256, nav, numbersKey));
    }

    /// <summary>
    /// A file of work derived from a graph, beside it: the graph's own name and <paramref name="suffix"/>, which
    /// names the work and its revision (the choke points' is <c>chokes</c> and theirs; a game's binding names its
    /// own, as TF2's blast links do), so a changed graph or a changed revision finds no file.
    /// </summary>
    public string SidePathFor(string map, string bspSha256, string navSha256, string numbersKey, string suffix)
        => $"{PathFor(map, bspSha256, navSha256, numbersKey)}.{suffix}";

    /// <summary>A graph's choke points beside it: the graph's own name, and the map's revision.</summary>
    public string ChokesPathFor(string map, string bspSha256, string navSha256, string numbersKey = "")
        => SidePathFor(map, bspSha256, navSha256, numbersKey, $"chokes{ChokeMap.Revision}");

    /// <summary>The cached choke points, or null when there are none or they are of another graph.</summary>
    public ChokeMap? TryReadChokes(string map, string bspSha256, string navSha256, NavGraph graph, string numbersKey = "")
    {
        string path = ChokesPathFor(map, bspSha256, navSha256, numbersKey);
        if (!File.Exists(path))
            return null;
        try
        {
            ReadOnlySpan<byte> bytes = File.ReadAllBytes(path);
            if (bytes.Length != 16 + graph.CellCount * sizeof(float)
                || BinaryPrimitives.ReadUInt64LittleEndian(bytes) != graph.Hash
                || BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != ChokeMap.Revision
                || BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != graph.CellCount)
                return null;
            var score = new float[graph.CellCount];
            for (int c = 0; c < score.Length; c++)
                score[c] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[(16 + c * sizeof(float))..]));
            return ChokeMap.FromScores(graph, score);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void WriteChokes(string map, string bspSha256, string navSha256, NavGraph graph, ChokeMap chokes, string numbersKey = "")
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chokes);
        System.IO.Directory.CreateDirectory(directory);
        var bytes = new byte[16 + chokes.CellCount * sizeof(float)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, graph.Hash);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), ChokeMap.Revision);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), chokes.CellCount);
        for (int c = 0; c < chokes.CellCount; c++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16 + c * sizeof(float)), BitConverter.SingleToInt32Bits(chokes.ScoreOf(c)));
        BuiltNavStore.Replace(ChokesPathFor(map, bspSha256, navSha256, numbersKey), bytes);
    }

    public byte[] Encode(string map, string bspSha256, string navSha256, DerivedNav nav, string numbersKey = "")
    {
        ArgumentNullException.ThrowIfNull(nav);
        var w = new Writer();
        w.Bytes(Magic);
        w.Str(Version(numbersKey));
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
    public DerivedNav Decode(ReadOnlySpan<byte> bytes, string map, string bspSha256, string navSha256, string numbersKey = "")
    {
        var r = new Reader(bytes);
        if (!r.Bytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("not a .navgraph");
        string version = r.Str(), bsp = r.Str(), nav = r.Str(), name = r.Str();
        if (version != Version(numbersKey) || bsp != bspSha256 || nav != navSha256 || name != map)
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
