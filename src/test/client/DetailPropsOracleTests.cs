using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Detail;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed detail props (<see cref="DetailPropSystem"/>) against stock's
/// detailobjectsystem.cpp, run offline by
/// src/test/oracle/native/detailprops_oracle_test.cpp
/// (<c>Fixtures/detailprops-stock.tsv</c>, README beside it). The replay gives
/// the harness's stand-in answers, plays the part native carries out
/// (shell_detail_props.cpp: the materials, the renderables, the leaf system's
/// tables, the BSP query, the render context and the mesh) and writes the same
/// rows, which are compared with stock's line for line.
///
/// <para>
/// Three differences are the port's, and each is held rather than skipped:
/// native carries out a level load's leaf-system rows and the material's
/// precache after the managed parse returns, so a load's engine rows and its
/// native rows are compared as two sequences; the fast sprites' corners rest on
/// the CPU's <c>rsqrtps</c>, so they are compared exactly when this CPU's
/// estimate matches the recording's (its <c>RSQ</c> rows) and to the
/// estimate's error bound when it does not; and stock's <c>rand()</c> draws
/// (<c>R</c>) have no managed row to print, so their effect, the copies'
/// corners, is what is compared.
/// </para>
/// </summary>
public class DetailPropsOracleTests
{
    private static uint U(string hex) => uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static float F(string hex) => BitConverter.Int32BitsToSingle(unchecked((int)U(hex)));

    private static string H(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    // The harness's LeafCenter.
    private static Vector LeafCenter(int i) => new((i % 4) * 500.0f, (i / 4) * 500.0f, (i % 3) * 16.0f);

    private const int LeafSlots = 64;

    /// <summary>The harness's engine and convars, for one level at a time.</summary>
    private sealed class World : IDetailPropEngine, IDetailPropSettings
    {
        public List<string> Log = [];
        public readonly Dictionary<int, (int Version, byte[] Bytes)> Lumps = [];
        public readonly List<string> Models = [];

        public float DetailDist { get; set; }
        public float DetailFade { get; set; }
        public int Multiplier { get; set; }
        public int FastSprites { get; set; }

        public int GameLumpVersion(int lumpId) => Lumps.TryGetValue(lumpId, out var l) ? l.Version : 0;

        public int GameLumpSize(int lumpId) => Lumps.TryGetValue(lumpId, out var l) ? l.Bytes.Length : 0;

        public bool LoadGameLump(int lumpId, Span<byte> buffer)
        {
            bool has = Lumps.TryGetValue(lumpId, out var l);
            Log.Add($"GL\t{(uint)lumpId:x8}\t{buffer.Length}\t{(has ? 1 : 0)}");
            if (!has || buffer.Length != l.Bytes.Length)
                return false;
            l.Bytes.CopyTo(buffer);
            return true;
        }

        public nint LoadModel(string name, bool prop)
        {
            int id = Models.IndexOf(name) + 1;
            if (id == 0)
            {
                Models.Add(name);
                id = Models.Count;
            }
            Log.Add($"LM\t{name}\t{(prop ? 1 : 0)}\t{id}");
            return id;
        }

        private string NameOf(nint model) => model > 0 && model <= Models.Count ? Models[(int)model - 1] : "";

        public bool IsModelVertexLit(nint model)
        {
            bool b = NameOf(model).Contains("vlit", StringComparison.Ordinal);
            Log.Add($"VL\t{model}\t{(b ? 1 : 0)}");
            return b;
        }

        public bool ModelHasMaterialProxy(nint model) => NameOf(model).Contains("proxy", StringComparison.Ordinal);

        public bool IsTranslucent(nint model) => NameOf(model).Contains("glass", StringComparison.Ordinal);

        public Vector LinearToGamma(Vector linear)
        {
            static float G(float x)
            {
                if (x < 0.0f)
                    x = 0.0f;
                int n = (int)(MathF.Sqrt(x) * 255.0f);
                if (n > 255)
                    n = 255;
                return n / 255.0f;
            }
            return new Vector(G(linear.X), G(linear.Y), G(linear.Z));
        }

        public float LightStyleValue(int style) => (style % 5) * 0.25f;

        public Vector ComputeDynamicLighting(Vector point, Vector normal)
        {
            int cell = (int)MathF.Abs(point.X) % 7;
            return new Vector(cell * 0.03125f, ((int)MathF.Abs(point.Y) % 3) * 0.0625f, cell == 0 ? 0.0f : 0.015625f);
        }

        public void Warning(string text)
        {
            string t = text.Replace('\n', ' ').Replace('\t', ' ').TrimEnd(' ');
            Log.Add($"W\t1\t{t}");
        }
    }

    /// <summary>What native keeps and carries out (shell_detail_props.cpp), played for one level.</summary>
    private sealed class Native(World world) : IDetailLevelSink, IDetailViewSink, IDetailDrawSink
    {
        private sealed class Renderable
        {
            public nint Model;
            public Vector Origin, Angles, Color;
            public byte Alpha = 255;
            public int Ordinal;
        }

        private readonly List<Renderable?> _objects = [];
        private readonly List<Renderable> _byOrdinal = [];
        private readonly int[] _leafFirst = new int[LeafSlots], _leafCount = new int[LeafSlots];
        private readonly List<(byte Alpha, bool SetAngles, Vector Angles, Vector Color)> _states = [];
        private readonly List<nint> _recompute = [];
        private int _objectsEnd;
        private int _verts;

        public readonly List<string> LoadRows = [];
        public string Sprite = "-", Wireframe = "-";
        public bool FastDraw;
        public readonly HashSet<int> FastVertexLines = [];

        public void Object(nint model, Vector origin, Vector angles)
        {
            if (model == 0)
            {
                _objects.Add(null);
                return;
            }
            var r = new Renderable { Model = model, Origin = origin, Angles = angles, Ordinal = _byOrdinal.Count };
            _byOrdinal.Add(r);
            _objects.Add(r);
        }

        public void LeafRun(int leaf, int first, int count, bool fastSprites)
        {
            if (fastSprites)
                LoadRows.Add($"LS\t{leaf}\t0\t1");
            LoadRows.Add($"LD\t{leaf}\t{first}\t{count}");
            _leafFirst[leaf] = first;
            _leafCount[leaf] = count;
            _objectsEnd = Math.Max(_objectsEnd, first + count);
        }

        public void RecomputeTranslucency(nint model) => _recompute.Add(model);

        public void ModelState(int modelOrdinal, byte alpha, bool setAngles, Vector angles, Vector color)
        {
            Assert.Equal(_states.Count, modelOrdinal);
            _states.Add((alpha, setAngles, angles, color));
        }

        /// <summary>BuildDetailObjectRenderLists' native half: the proxies, the sphere, the marks and the states.</summary>
        public void BuildLists(DetailPropSystem system, Vector origin, Vector currentViewOrigin, bool clientModeDraws, int drawDetailProps, int fullbright, int frame)
        {
            _states.Clear();
            _recompute.Clear();
            float? built = system.BuildRenderLists(origin, currentViewOrigin, clientModeDraws, drawDetailProps, fullbright, this);
            if (built is not float radius)
                return;
            Assert.Equal(_byOrdinal.Count, _states.Count);
            // The colours are every model prop's: no view changes them.
            foreach (Renderable r in _byOrdinal)
                r.Color = _states[r.Ordinal].Color;
            foreach (nint m in _recompute)
                world.Log.Add($"RT\t{m}\t0\t0\t0");

            var leaves = new StringBuilder();
            for (int i = 0; i < LeafSlots; i++)
            {
                if ((LeafCenter(i) - currentViewOrigin).Length <= radius + 212.0f)
                    leaves.Append('\t').Append(i.ToString(CultureInfo.InvariantCulture));
            }
            world.Log.Add($"Q\t{H(currentViewOrigin.X)}\t{H(currentViewOrigin.Y)}\t{H(currentViewOrigin.Z)}\t{H(radius)}{leaves}");
            for (int i = 0; i < LeafSlots; i++)
            {
                if ((LeafCenter(i) - currentViewOrigin).Length > radius + 212.0f)
                    continue;
                world.Log.Add($"LK\t{i}\t{frame}");
                for (int k = 0; k < _leafCount[i]; k++)
                {
                    if (_objects[_leafFirst[i] + k] is not Renderable r)
                        continue;
                    var s = _states[r.Ordinal];
                    r.Alpha = s.Alpha;
                    if (s.SetAngles)
                        r.Angles = s.Angles;
                }
            }
        }

        /// <summary>The harness's DumpModels: every renderable, then its DrawModel( 1 ).</summary>
        public void Dump(bool color)
        {
            for (int i = 0; i < _objectsEnd; i++)
            {
                if (_objects[i] is not Renderable r)
                    continue;
                bool transparent = r.Alpha < 255 || world.IsTranslucent(r.Model);
                world.Log.Add($"M\t{i}\t{r.Model}\t{H(r.Origin.X)}\t{H(r.Origin.Y)}\t{H(r.Origin.Z)}\t{H(r.Angles.X)}\t{H(r.Angles.Y)}\t{H(r.Angles.Z)}\t{r.Alpha}\t{(transparent ? 1 : 0)}");
                if (color)
                    world.Log.Add($"MC\t{i}\t{H(r.Color.X)}\t{H(r.Color.Y)}\t{H(r.Color.Z)}");
                if (r.Alpha == 0)
                    continue;
                world.Log.Add($"DM\t1\t65535\t-1\t{r.Model}\t{H(r.Origin.X)}\t{H(r.Origin.Y)}\t{H(r.Origin.Z)}\t{H(r.Angles.X)}\t{H(r.Angles.Y)}\t{H(r.Angles.Z)}\t0\t0\t0");
            }
        }

        public void Begin(int quads)
        {
            world.Log.Add($"MB\t7\t{quads}");
            _verts = 0;
        }

        public void Vertex(float x, float y, float z, byte r, byte g, byte b, byte a, float s, float t)
        {
            if (FastDraw)
                FastVertexLines.Add(world.Log.Count);
            world.Log.Add($"V\t{H(x)}\t{H(y)}\t{H(z)}\t{r:x2}{g:x2}{b:x2}{a:x2}\t{H(s)}\t{H(t)}");
            _verts++;
        }

        public void EndDraw()
        {
            world.Log.Add($"ME\t{_verts}");
            world.Log.Add("D");
        }

        public void ModelAlpha(int objectIndex, byte alpha)
        {
            if (_objects[objectIndex] is Renderable r)
                r.Alpha = alpha;
        }

        /// <summary>
        /// One prepared draw as native carries it out: the render context, the
        /// mesh and its limit, then managed's quads. False when the limit is 0
        /// (stock returns there, with the matrix still pushed).
        /// </summary>
        public bool Draw(DetailPropSystem system, DetailDrawMaterial material, int maxVerts, int maxIndices, bool fast)
        {
            if (material == DetailDrawMaterial.None)
                return true;
            world.Log.Add("XM\t10");
            world.Log.Add("XP");
            world.Log.Add("XI");
            world.Log.Add($"XG\t1\t{(material == DetailDrawMaterial.Wireframe ? Wireframe : Sprite)}");
            int maxQuads = maxIndices / 6;
            if (maxQuads > maxVerts / 4)
                maxQuads = maxVerts / 4;
            if (maxQuads == 0)
                return false;
            FastDraw = fast;
            system.Emit(maxQuads, this);
            FastDraw = false;
            world.Log.Add("XO");
            return true;
        }
    }

    private sealed class Replay
    {
        public readonly List<string> Golden = [];
        public readonly List<string> Actual = [];
        public readonly HashSet<int> FastVertexLines = [];
        public bool RsqMatches = true;
        public int Scenarios;
    }

    private static Replay Run()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "detailprops-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock detail props are missing: " + path);

        var replay = new Replay();
        var world = new World { Log = replay.Actual };
        // The one system object, as stock's static: every level is the same object's.
        var system = new DetailPropSystem(world, world);
        Native native = new(world);
        int maxVerts = 0, maxIndices = 0, frame = 0, matW = 0, matH = 0, listsInView = 0;
        bool hdr = false, clientModeDraws = true;
        string? worldMaterial = null;
        DetailSwitches switches = default;
        Vector vo = default, vc = default, vf = default, vr = default, vu = default;

        // The harness asks the renderables' colours only in a view that built
        // its lists, as the renderer gathers them only there.
        bool Built() => clientModeDraws && switches.DrawDetailProps != 0;

        foreach (string text in File.ReadLines(path))
        {
            if (text.Length == 0 || text[0] == '#')
                continue;
            string[] c = text.Split('\t');
            if (c[0] == "RSQ")
            {
                replay.RsqMatches &= BitConverter.SingleToInt32Bits(DetailMath.ReciprocalSqrtEstimate(F(c[1]))) == unchecked((int)U(c[2]));
                continue;
            }
            if (c[0] == "R")
                continue;
            replay.Golden.Add(text);

            switch (c[0])
            {
                case "S":
                    world.Log.Add(text);
                    replay.Scenarios++;
                    world.Lumps.Clear();
                    world.Models.Clear();
                    native = new Native(world);
                    system.SeedRandom(uint.Parse(c[2], CultureInfo.InvariantCulture) & 0xffff);
                    hdr = c[4] == "1";
                    matW = I(c[6]);
                    matH = I(c[7]);
                    maxVerts = I(c[8]);
                    maxIndices = I(c[9]);
                    world.DetailDist = F(c[10]);
                    world.DetailFade = F(c[11]);
                    world.FastSprites = I(c[12]);
                    world.Multiplier = I(c[13]);
                    worldMaterial = c[14] == "-" ? null : c[14];
                    break;

                case "G":
                    world.Lumps[unchecked((int)U(c[1]))] = (I(c[2]), Convert.FromHexString(c[3]));
                    world.Log.Add(text);
                    break;

                case "VO": vo = new Vector(F(c[1]), F(c[2]), F(c[3])); world.Log.Add(text); break;
                case "VC": vc = new Vector(F(c[1]), F(c[2]), F(c[3])); world.Log.Add(text); break;
                case "VF": vf = new Vector(F(c[1]), F(c[2]), F(c[3])); world.Log.Add(text); break;
                case "VR": vr = new Vector(F(c[1]), F(c[2]), F(c[3])); world.Log.Add(text); break;
                case "VU":
                    vu = new Vector(F(c[1]), F(c[2]), F(c[3]));
                    world.Log.Add(text);
                    // The view's list build (CPlanView::Build), then the renderables.
                    native.BuildLists(system, vo, vc, clientModeDraws, switches.DrawDetailProps, switches.Fullbright, frame);
                    native.Dump(Built());
                    break;

                case "U":
                    world.Log.Add(text);
                    world.DetailDist = F(c[1]);
                    break;

                case "C":
                    world.Log.Add(text);
                    switch (c[1])
                    {
                        case "init":
                        {
                            system.Init();
                            // LevelInitPreEntity as native carries it out around
                            // the managed parse: the two materials, the parse,
                            // then the parse's leaf rows and the precache.
                            native.Sprite = "detail/detailsprites";
                            native.Wireframe = "debug/debugspritewireframe";
                            world.Log.Add("MI\tdetail/detailsprites\tOther textures");
                            world.Log.Add("MI\tdebug/debugspritewireframe\tOther textures");
                            bool precache = system.LevelInitPreEntity(matW, matH, hdr, native);
                            world.Log.AddRange(native.LoadRows);
                            native.LoadRows.Clear();
                            if (precache)
                                world.Log.Add("PM\tdetail/detailsprites");
                            break;
                        }

                        case "post":
                        {
                            string material = system.LevelInitPostEntity(worldMaterial);
                            world.Log.Add($"MI\t{material}\tOther textures");
                            native.Sprite = material;
                            break;
                        }

                        case "view":
                            listsInView = 0;
                            switches = new DetailSwitches(I(c[3]), I(c[4]) != 0, I(c[5]));
                            clientModeDraws = I(c[6]) != 0;
                            frame = I(c[7]);
                            break;

                        case "begin":
                            system.BeginTranslucent();
                            break;

                        case "list":
                        {
                            int[] leaves = c.Skip(2).Select(I).ToArray();
                            var view = new DetailView(vo, vf, vr, vu, vc);
                            var m = system.PrepareList(DetailDrawKind.FastList, view, leaves, switches, native);
                            native.Draw(system, m, maxVerts, maxIndices, true);
                            m = system.PrepareList(DetailDrawKind.OldList, view, leaves, switches, native);
                            native.Draw(system, m, maxVerts, maxIndices, false);
                            // The view's second list is its last draw: the
                            // harness reads the renderables again after it.
                            if (++listsInView == 2)
                                native.Dump(Built());
                            break;
                        }

                        case "inleaf":
                        {
                            int leaf = I(c[2]);
                            Vector? closest = c[3] == "-" ? null : new Vector(F(c[3]), F(c[4]), F(c[5]));
                            var view = new DetailView(vo, vf, vr, vu, vc);
                            var m = system.PrepareInLeaf(DetailDrawKind.FastInLeaf, view, leaf, closest, switches, native);
                            native.Draw(system, m, maxVerts, maxIndices, true);
                            m = system.PrepareInLeaf(DetailDrawKind.OldInLeaf, view, leaf, closest, switches, native);
                            native.Draw(system, m, maxVerts, maxIndices, false);
                            break;
                        }

                        case "shutdown":
                            system.LevelShutdownPreEntity();
                            world.Log.Add($"MS\t{native.Sprite}");
                            world.Log.Add($"MS\t{native.Wireframe}");
                            break;
                    }
                    break;

                case "CV":
                    world.Log.Add($"CV\t{H(world.DetailFade)}\t{H(world.DetailDist)}");
                    native.Dump(false);
                    break;
            }

            foreach (int line in native.FastVertexLines)
                replay.FastVertexLines.Add(line);
            native.FastVertexLines.Clear();
        }

        return replay;
    }

    // A level load's rows: the engine's, in order, and native's, in order.
    private static bool IsNativeLoadRow(string row)
        => row.StartsWith("LS\t", StringComparison.Ordinal) || row.StartsWith("LD\t", StringComparison.Ordinal)
           || row.StartsWith("PM\t", StringComparison.Ordinal);

    private static List<string> NormalizeLoads(List<string> rows)
    {
        var output = new List<string>(rows.Count);
        var engine = new List<string>();
        var native = new List<string>();
        bool inLoad = false;
        foreach (string row in rows)
        {
            if (row.StartsWith("C\t", StringComparison.Ordinal))
            {
                if (inLoad)
                {
                    output.AddRange(engine);
                    output.AddRange(native);
                    engine.Clear();
                    native.Clear();
                }
                inLoad = row == "C\tinit";
                output.Add(row);
                continue;
            }
            if (!inLoad)
            {
                output.Add(row);
                continue;
            }
            (IsNativeLoadRow(row) ? native : engine).Add(row);
        }
        output.AddRange(engine);
        output.AddRange(native);
        return output;
    }

    // The fast corners' error bound when this CPU's rsqrtps is not the
    // recording's: each corner is the sprite's centre plus or minus the
    // half-width times a unit vector whose length rests on the estimate
    // (relative error 1.5 * 2^-12 on either maker's), so a coordinate may move
    // by 2^-10 of the half-width plus a few of its own ulps.
    private static bool CloseVertex(string expected, string actual)
    {
        string[] e = expected.Split('\t'), a = actual.Split('\t');
        if (e.Length != a.Length || e[0] != "V" || a[0] != "V")
            return false;
        for (int i = 4; i < e.Length; i++)
        {
            if (e[i] != a[i])
                return false;
        }
        for (int i = 1; i <= 3; i++)
        {
            float x = F(e[i]), y = F(a[i]);
            float bound = MathF.Max(MathF.Abs(x), 64.0f) * 2.0e-6f + 0.05f;
            if (MathF.Abs(x - y) > bound)
                return false;
        }
        return true;
    }

    [Fact]
    public void TheManagedPortWritesStocksRows()
    {
        Replay r = Run();
        Assert.True(r.Scenarios == 11, $"the golden holds {r.Scenarios} scenarios, not the README's eleven");

        List<string> golden = NormalizeLoads(r.Golden);
        // The fast-vertex lines are indices into the unnormalized actual rows;
        // normalisation moves only load rows, which are never vertices, so
        // each vertex keeps its index relative to the rows around it.
        List<string> actual = NormalizeLoads(r.Actual);
        Assert.Equal(r.Actual.Count, actual.Count);

        int vertices = 0, fast = 0;
        for (int i = 0; i < Math.Min(golden.Count, actual.Count); i++)
        {
            if (golden[i] == actual[i])
            {
                if (golden[i].StartsWith("V\t", StringComparison.Ordinal))
                    vertices++;
                continue;
            }
            bool fastCorner = !r.RsqMatches && r.FastVertexLines.Contains(i) && CloseVertex(golden[i], actual[i]);
            if (fastCorner)
            {
                fast++;
                vertices++;
                continue;
            }
            Assert.Fail($"row {i + 1}: stock wrote\n  {golden[i]}\nand the port\n  {actual[i]}\n" +
                        $"(previous row {(i > 0 ? golden[i - 1] : "-")})");
        }
        Assert.Equal(golden.Count, actual.Count);
        Assert.True(vertices > 9000, $"only {vertices} vertices were compared");
    }
}
