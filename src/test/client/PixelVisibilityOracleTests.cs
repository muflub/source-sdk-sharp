using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.PixelVisibility;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed pixel visibility (<see cref="PixelVisibilitySystem"/>) against
/// stock's c_pixel_visibility.cpp, run offline from the pristine file by
/// src/test/oracle/native/pixelvis_oracle_test.cpp
/// (<c>Fixtures/pixelvis-stock.tsv</c>, README-pixelvis-stock.md beside it).
///
/// <para>
/// The golden carries its stimulus: each <c>S</c> row is a step, driven here on
/// the managed system over an engine double that answers by the oracle's rules
/// (query ids from 1, the pixel-count table, a sphere's diameter as radius * 400
/// / distance, the scripted world's trace fraction) and writes the oracle's rows.
/// Every row stock wrote after a step must be a row the managed system made the
/// double write, in stock's order, with two exceptions the port names: the
/// sphere diameters (<c>PD</c>) are held as their own ordered list per step,
/// because a view's end asks them all before it draws; and the glow filter's
/// per-entity rows (<c>F</c>, <c>FB</c>) are native's, held by the native
/// filter's own test.
/// </para>
/// </summary>
public class PixelVisibilityOracleTests
{
    private static string Bits(float f) => BitConverter.SingleToUInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    private static float Float(string bits) => BitConverter.UInt32BitsToSingle(uint.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string V3(in Vector v) => $"{Bits(v.X)}\t{Bits(v.Y)}\t{Bits(v.Z)}";

    private static Vector Vec(string[] a, int at) => new(Float(a[at]), Float(a[at + 1]), Float(a[at + 2]));

    /// <summary>The oracle's material system, trace and view, as one double writing the oracle's rows.</summary>
    private sealed class Rig : IPixelVisEngine, ICurrentView, IPixelVisFrame, IPixelVisSettings, IPixelVisDraw
    {
        public readonly List<string> Log = [];
        public readonly PixelVisibilitySystem System;
        private readonly List<string> _materials = [];
        private int _nextQuery = 1;
        private bool _createFails;
        private int _dx = 95;
        private int _traceEntities;
        private float _traceWorld = 1.0f;

        public int Id { get; set; }
        public Vector Origin { get; set; }
        public Vector Forward { get; set; }
        public Vector Right { get; set; }
        public Vector Up { get; set; }
        public float[] Matrix = new float[16];
        public int FrameCount { get; set; }
        public float FrameTime { get; set; }
        public bool Partial { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public bool Spew { get; set; }
        public bool BuildingCubemaps { get; set; }

        public Rig() => System = new PixelVisibilitySystem(this, this, this, this);

        public CurrentViewState Read() => new(Id, Origin, Forward, Right, Up);

        public int DxSupportLevel() => _dx;

        public int CreateQuery()
        {
            if (_createFails)
            {
                Log.Add("QC\t0");
                return 0;
            }
            int id = _nextQuery++;
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"QC\t{id}"));
            return id;
        }

        public void DestroyQuery(int query) => Log.Add(string.Create(CultureInfo.InvariantCulture, $"QD\t{query}"));

        public void ResetQuery(int query) => Log.Add(string.Create(CultureInfo.InvariantCulture, $"QR\t{query}"));

        private int Pixels(int query)
        {
            int id = query;
            int k = ( id & 1 ) != 0 ? ( id * 31 + FrameCount * 17 ) % 7 : ( id * 13 + FrameCount * 5 ) % 6;
            int[] visible = [-1, 0, 10, 32, 61, 64, 64];
            int[] count = [-1, 0, 64, 64, 64, 64];
            int n = ( id & 1 ) != 0 ? visible[k] : count[k];
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"QP\t{id}\t{n}"));
            return n;
        }

        public void PixelsRendered(int visible, int count, out int visiblePixels, out int countPixels)
        {
            countPixels = count != 0 ? Pixels(count) : -1;
            visiblePixels = Pixels(visible);
        }

        public void PixelDiameters(ReadOnlySpan<float> spheres, Span<float> diameters)
        {
            for (int i = 0; i < diameters.Length; i++)
            {
                var origin = new Vector(spheres[i * 4], spheres[i * 4 + 1], spheres[i * 4 + 2]);
                float radius = spheres[i * 4 + 3];
                float length = ( origin - Origin ).Length;
                float dist = length > 1.0f ? length : 1.0f;
                float d = radius * 400.0f / dist;
                Log.Add($"PD\t{V3(origin)}\t{Bits(radius)}\t{Bits(d)}");
                diameters[i] = d;
            }
        }

        public int FindMaterial(string name)
        {
            Log.Add($"MF\t{name}\tClientEffect textures\t1");
            int at = _materials.IndexOf(name);
            if (at < 0)
            {
                _materials.Add(name);
                at = _materials.Count - 1;
            }
            Log.Add($"MI\t{name}");
            return at + 1;
        }

        private string Name(int material) => material > 0 ? _materials[material - 1] : "(null)";

        public void ReleaseMaterial(int material) => Log.Add($"MD\t{Name(material)}");

        public float TraceGlowSight(in Vector start, in Vector end)
        {
            Log.Add($"T\t{V3(start)}\t{V3(end)}\t{PixelVisibilitySystem.GlowSightMask:x8}");
            // The scripted line crosses the first N of prop, brush, player and
            // trigger; the glow filter lets the first two stop it, at 0.25.
            float fraction = _traceWorld;
            for (int i = 0; i < _traceEntities; i++)
            {
                if (i < 2 && fraction > 0.25f)
                    fraction = 0.25f;
            }
            Log.Add($"TR\t{Bits(fraction)}");
            return fraction;
        }

        public void DevMsg(int level, string text) => Log.Add(string.Create(CultureInfo.InvariantCulture, $"D{level}\t{text.TrimEnd('\n')}"));

        public void Msg(string text) => Log.Add("M\t" + text.TrimEnd('\n'));

        public void Bind(int material) => Log.Add($"B\t{Name(material)}");

        public void DepthRange(float near, float far) => Log.Add($"DR\t{Bits(near)}\t{Bits(far)}");

        public void Proxy(int query, int material, ReadOnlySpan<Vector> triangles)
        {
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"BQ\t{query}"));
            Log.Add($"GM\t0\t0\t0\t{Name(material)}");
            var row = new System.Text.StringBuilder(string.Create(CultureInfo.InvariantCulture, $"MESH\t2\t{triangles.Length}"));
            foreach (Vector v in triangles)
                row.Append($"\t{Bits(v.X)},{Bits(v.Y)},{Bits(v.Z)}");
            Log.Add(row.ToString());
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"EQ\t{query}"));
        }

        private PixelVisView View => new(Id, Origin, Forward, Right, Up, Matrix);

        private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

        public void Step(string[] a)
        {
            switch (a[0])
            {
                case "init":
                    _dx = Int(a[1]);
                    _createFails = a[2] == "1";
                    System.LevelInitPreEntity();
                    _createFails = false;
                    break;
                case "shutdown":
                    System.LevelShutdownPostEntity();
                    break;
                case "frame":
                    FrameCount = Int(a[1]);
                    FrameTime = Float(a[2]);
                    break;
                case "view":
                    Id = Int(a[1]);
                    Origin = Vec(a, 2);
                    Forward = Vec(a, 5);
                    Right = Vec(a, 8);
                    Up = Vec(a, 11);
                    Matrix = new float[16];
                    for (int i = 0; i < 16; i++)
                        Matrix[i] = Float(a[14 + i]);
                    break;
                case "noview":
                    Id = -1;
                    break;
                case "cvar":
                    bool on = a[2] != "0";
                    switch (a[1])
                    {
                        case "r_pixelvisibility_partial": Partial = on; break;
                        case "r_dopixelvisibility": Enabled = on; break;
                        case "r_drawpixelvisibility": System.ShowQueries(on); break;
                        case "r_pixelvisibility_spew": Spew = on; break;
                        case "building_cubemaps": BuildingCubemaps = on; break;
                        default: Assert.Fail("no ConVar " + a[1]); break;
                    }
                    break;
                case "fv":
                {
                    var p = new PixelVisParams(Vec(a, 2), Float(a[5]), Float(a[6]), Float(a[7]), a[8] == "1");
                    int handle = Int(a[9]);
                    float f = System.FractionVisible(p, ref handle);
                    Log.Add(string.Create(CultureInfo.InvariantCulture, $"R\t{Bits(f)}\t{handle}"));
                    break;
                }
                case "fvnull":
                    Log.Add($"R\t{Bits(System.FractionVisibleNoHandle(Vec(a, 1)))}");
                    break;
                case "endview":
                    System.EndView(View, this);
                    break;
                case "endscene":
                    System.EndScene();
                    break;
                case "world":
                    _traceEntities = Int(a[1]);
                    _traceWorld = Float(a[2]);
                    break;
                case "sight":
                    Log.Add($"R\t{Bits(System.GlowSightDistance(Vec(a, 1), a[4] == "1"))}");
                    break;
                case "avail":
                    Log.Add($"R\t{( System.IsAvailable ? 1 : 0 )}");
                    break;
                case "shift":
                    System.ShiftVisibilityViews(Int(a[1]), Int(a[2]));
                    break;
                case "debug":
                    System.DebugInfo();
                    break;
                default:
                    Assert.Fail("no step " + a[0]);
                    break;
            }
        }
    }

    private static List<string> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "pixelvis-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock pixel visibility is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        Assert.True(rows.Count(r => r.StartsWith("S\t", StringComparison.Ordinal)) > 200 && rows.Count(r => r.StartsWith("MESH\t", StringComparison.Ordinal)) > 100,
                    "the oracle parsed");
        return rows;
    }

    private static bool IsDiameter(string row) => row.StartsWith("PD\t", StringComparison.Ordinal);

    private static bool IsNativeFilter(string row) => row.StartsWith("F\t", StringComparison.Ordinal) || row.StartsWith("FB\t", StringComparison.Ordinal);

    [Fact]
    public void EveryStepDoesWhatStocksDid()
    {
        var rig = new Rig();
        List<string> stock = Stock();
        int i = 0;
        Assert.Equal($"P\t{PixelVisibilitySystem.PrecacheName}\t{string.Join('\t', PixelVisibilitySystem.PrecacheMaterials)}", stock[i++]);
        int steps = 0, held = 0, diameters = 0;
        while (i < stock.Count)
        {
            string s = stock[i++];
            Assert.StartsWith("S\t", s);
            var expected = new List<string>();
            while (i < stock.Count && !stock[i].StartsWith("S\t", StringComparison.Ordinal))
                expected.Add(stock[i++]);
            rig.Log.Clear();
            rig.Step(s[2..].Split('\t'));
            var stockRows = expected.Where(r => !IsDiameter(r) && !IsNativeFilter(r)).ToList();
            var ourRows = rig.Log.Where(r => !IsDiameter(r)).ToList();
            Assert.True(stockRows.SequenceEqual(ourRows),
                        $"step {steps} {s.Replace('\t', ' ')}:\n  stock   {string.Join("\n          ", stockRows)}\n  managed {string.Join("\n          ", ourRows)}");
            var stockDiameters = expected.Where(IsDiameter).ToList();
            Assert.True(stockDiameters.SequenceEqual(rig.Log.Where(IsDiameter)),
                        $"step {steps} {s.Replace('\t', ' ')}: diameters\n  stock   {string.Join(" | ", stockDiameters)}\n  managed {string.Join(" | ", rig.Log.Where(IsDiameter))}");
            steps++;
            held += stockRows.Count;
            diameters += stockDiameters.Count;
        }
        // The stimulus reached the subject: every step ran, and the rows held cover the draws and the diameters.
        Assert.True(steps > 250 && held > 900 && diameters > 150, $"{steps} steps, {held} rows held, {diameters} diameters");
    }

    [Fact]
    public void AHandleIsTheSerialOverTheNodePlusOne()
    {
        var rig = new Rig();
        rig.Step(["init", "95", "0"]);
        rig.Step(["frame", "1", Bits(1 / 60f)]);
        rig.Id = 0;
        rig.Forward = new Vector(1, 0, 0);
        int first = 0, second = 0;
        rig.System.FractionVisible(new PixelVisParams(new Vector(100, 0, 0)), ref first);
        rig.System.FractionVisible(new PixelVisParams(new Vector(200, 0, 0)), ref second);
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void ASetNobodyAsksAboutIsFreedAndItsSerialMoves()
    {
        var rig = new Rig();
        rig.Step(["init", "95", "0"]);
        rig.Id = 0;
        rig.Forward = new Vector(1, 0, 0);
        int handle = 0;
        rig.Step(["frame", "1", Bits(1 / 60f)]);
        rig.System.FractionVisible(new PixelVisParams(new Vector(100, 0, 0)), ref handle);
        rig.Step(["frame", "4", Bits(1 / 60f)]);
        rig.System.EndScene();
        Assert.Equal((1, 1, 1, 1), rig.System.Counts);
        int stale = handle;
        rig.System.FractionVisible(new PixelVisParams(new Vector(100, 0, 0)), ref handle);
        Assert.NotEqual(stale, handle);
        Assert.Equal(0x00010001, handle);
    }
}
