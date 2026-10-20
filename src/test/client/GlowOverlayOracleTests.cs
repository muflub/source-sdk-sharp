using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.GlowOverlays;
using SourceSharp.Client.Render.PixelVisibility;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed glow overlays (<see cref="GlowOverlaySystem"/>) against stock's
/// glow_overlay.cpp and warp_overlay.cpp, run offline from the pristine files
/// by src/test/oracle/native/glowoverlay_oracle_test.cpp
/// (<c>Fixtures/glowoverlay-stock.tsv</c>, README-glowoverlay-stock.md beside it).
///
/// <para>
/// The golden carries its stimulus: each <c>S</c> row is a step, driven here on
/// the managed system over a double that answers from the rows (a pixel
/// visibility question's answer, a trace's fraction and flags) and carries the
/// draw out as the oracle's material-system doubles did (the clipping state
/// EnableClipping returns, the materials found by name, $hdrcolorscale on all
/// but a <c>*_noscale</c> material, the mesh builder's float-to-byte colour).
/// Every row stock wrote after a step must be the row the managed system made
/// the double write, in stock's order.
/// </para>
/// </summary>
public class GlowOverlayOracleTests
{
    private static string Bits(float f) => BitConverter.SingleToUInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    private static float Float(string bits) => BitConverter.UInt32BitsToSingle(uint.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static string V3(in Vector v) => $"{Bits(v.X)}\t{Bits(v.Y)}\t{Bits(v.Z)}";

    private static Vector Vec(string[] a, int at) => new(Float(a[at]), Float(a[at + 1]), Float(a[at + 2]));

    // mathlib's FastFToC: the float's bits after c * 255 + 2^23.
    private static int FToC(float c) => BitConverter.SingleToInt32Bits(c * 255.0f + (float)( 1 << 23 )) & 255;

    private sealed class NamedGlow(string name, List<string> log) : GlowOverlay
    {
        public readonly string Name = name;
        protected internal override void OnRemoved() => log.Add("DEL\t" + Name);
    }

    private sealed class NamedWarp(string name, int lives, List<string> log) : WarpOverlay
    {
        public readonly string Name = name;
        private int _lives = lives;

        public override bool Update(float frameTime)
        {
            if (_lives < 0)
                return true;
            return _lives-- > 0;
        }

        protected internal override void OnRemoved() => log.Add("DEL\t" + Name);
    }

    private sealed class Rig : IPixelVisibility, IGlowEngine, IGlowSettings, IPixelVisFrame, IGlowDraw
    {
        public readonly List<string> Log = [];
        public readonly GlowOverlaySystem System;
        public readonly Dictionary<string, GlowOverlay> Overlays = new(StringComparer.Ordinal);
        private readonly List<string> _materials = [];
        private readonly ConVarLike _drawSprites = new("1");
        public Queue<string[]> Expected = new();
        private bool _available = true;
        private bool _clipping = true;
        private bool _saved;
        private float _traceFraction = 1;
        private int _traceFlags;
        private int _nextHandle = 1;
        public GlowView View;

        public Rig() => System = new GlowOverlaySystem(this, this, this, this);

        // ConVars as the oracle set them.
        private sealed class ConVarLike(string value)
        {
            public string Value = value;
        }

        public bool BuildingCubemaps { get; private set; }
        public float SunDecayRate { get; private set; } = 0.05f;
        public int DrawSprites => (int)VariantParse.Atof(_drawSprites.Value);
        public bool Wireframe { get; private set; }
        public int FrameCount { get; private set; }
        public float FrameTime { get; private set; }

        // The answers come from the expected rows: the next row of the kind.
        private string[] Next(string kind)
        {
            while (Expected.Count > 0)
            {
                string[] row = Expected.Dequeue();
                if (row[0] == kind)
                    return row;
            }
            Assert.Fail("the managed system asked a " + kind + " stock did not");
            return [];
        }

        public bool IsAvailable
        {
            get
            {
                Log.Add($"PA\t{( _available ? 1 : 0 )}");
                return _available;
            }
        }

        public float FractionVisible(in PixelVisParams p, ref int queryHandle)
        {
            int hIn = queryHandle;
            if (queryHandle == 0)
                queryHandle = _nextHandle++;
            string[] row = Next("PV");
            float answer = Float(row[9]);
            Log.Add(string.Create(CultureInfo.InvariantCulture,
                $"PV\t{V3(p.Position)}\t{Bits(p.ProxySize)}\t{Bits(p.ProxyAspect)}\t{Bits(p.FadeTime)}\t{( p.SizeInScreenspace ? 1 : 0 )}\t{hIn}\t{Bits(answer)}\t{queryHandle}"));
            return answer;
        }

        public float FractionVisibleNoHandle(in Vector position) => throw new InvalidOperationException("not asked");

        public float GlowSightDistance(in Vector glowOrigin, bool shouldTrace) => throw new InvalidOperationException("not asked");

        public int FindMaterial(string name, string group)
        {
            Log.Add($"MF\t{name}\t{group}\t1");
            int at = _materials.IndexOf(name);
            if (at < 0)
            {
                _materials.Add(name);
                at = _materials.Count - 1;
            }
            return at + 1;
        }

        private string Name(int material) => material > 0 ? _materials[material - 1] : "(null)";

        public void TraceSolid(in Vector start, in Vector end, out float fraction, out int surfaceFlags)
        {
            fraction = _traceFraction;
            surfaceFlags = _traceFlags;
            Log.Add(string.Create(CultureInfo.InvariantCulture,
                $"T\t{V3(start)}\t{V3(end)}\t{1:x8}\t0\t0\t{Bits(fraction)}\t{surfaceFlags}"));
        }

        public void Clip(GlowClip clip)
        {
            bool enable = clip switch { GlowClip.EnableAndSave => true, GlowClip.Off => false, _ => _saved };
            bool old = _clipping;
            _clipping = enable;
            if (clip == GlowClip.EnableAndSave)
                _saved = old;
            Log.Add($"EC\t{( enable ? 1 : 0 )}\t{( old ? 1 : 0 )}");
        }

        public void HdrColorScale(int material, float scale)
        {
            string name = Name(material);
            bool found = !name.Contains("_noscale", StringComparison.Ordinal);
            Log.Add($"FV\t{name}\t$hdrcolorscale\t{( found ? 1 : 0 )}");
            if (found)
                Log.Add($"HS\t{name}\t{Bits(scale)}");
        }

        public void Quad(int material, ReadOnlySpan<GlowVertex> vertices)
        {
            Log.Add($"GM\t0\t0\t0\t{Name(material)}");
            var row = new StringBuilder("MESH\t2\t4");
            foreach (GlowVertex v in vertices)
            {
                row.Append(string.Create(CultureInfo.InvariantCulture,
                    $"\t{Bits(v.Position.X)},{Bits(v.Position.Y)},{Bits(v.Position.Z)}/{FToC(v.Color.Z):x2}{FToC(v.Color.Y):x2}{FToC(v.Color.X):x2}{FToC(1.0f):x2}/{Bits(v.S)},{Bits(v.T)}"));
            }
            Log.Add(row.ToString());
        }

        public void WireframeQuad(int material, ReadOnlySpan<Vector> positions)
        {
            Log.Add($"B\t{Name(material)}");
            Log.Add($"GM\t0\t0\t0\t{Name(material)}");
            var row = new StringBuilder("MESH\t2\t4");
            foreach (Vector p in positions)
            {
                row.Append(string.Create(CultureInfo.InvariantCulture,
                    $"\t{Bits(p.X)},{Bits(p.Y)},{Bits(p.Z)}/{FToC(0):x2}{FToC(0):x2}{FToC(1):x2}ff/{Bits(0)},{Bits(0)}"));
            }
            Log.Add(row.ToString());
        }

        public void Step(string[] a)
        {
            switch (a[0])
            {
                case "overlay":
                {
                    string kind = a[1], name = a[2];
                    int lives = Int(a[3]);
                    GlowOverlay o = kind == "warp" ? new NamedWarp(name, lives, Log) : new NamedGlow(name, Log);
                    o.Pos = Vec(a, 4);
                    o.Directional = a[7] == "1";
                    o.Direction = Vec(a, 8);
                    o.InSky = a[11] == "1";
                    o.ProxyRadius = Float(a[12]);
                    o.HdrColorScale = Float(a[13]);
                    o.NSprites = Int(a[14]);
                    for (int i = 0; i < o.NSprites; i++)
                    {
                        int at = 15 + i * 6;
                        o.Sprites[i].Color = Vec(a, at);
                        o.Sprites[i].HorzSize = Float(a[at + 3]);
                        o.Sprites[i].VertSize = Float(a[at + 4]);
                        string material = a[at + 5];
                        o.Sprites[i].Material = 0;
                        if (material != "-")
                            o.Sprites[i].Material = -i - 1;     // found below, as the creator found it
                    }
                    for (int i = 0; i < o.NSprites; i++)
                    {
                        if (o.Sprites[i].Material < 0)
                            o.Sprites[i].Material = FindMaterial(a[15 + i * 6 + 5], GlowOverlaySystem.ClientEffectsGroup);
                    }
                    System.Activate(o);
                    Overlays[name] = o;
                    break;
                }
                case "frame":
                    FrameCount++;
                    FrameTime = Float(a[1]);
                    break;
                case "view":
                {
                    var planes = new GlowPlane[5];
                    for (int i = 0; i < 5; i++)
                        planes[i] = new GlowPlane(Vec(a, 8 + i * 4), Float(a[11 + i * 4]));
                    View = new GlowView(Vec(a, 1), Vec(a, 4), Float(a[7]), planes);
                    break;
                }
                case "clipping":
                    _clipping = a[1] == "1";
                    break;
                case "world":
                    _traceFraction = Float(a[1]);
                    _traceFlags = Int(a[2]);
                    break;
                case "available":
                    _available = a[1] == "1";
                    break;
                case "wireframe":
                    Wireframe = a[1] == "1";
                    break;
                case "cvar":
                    switch (a[1])
                    {
                        case "r_drawsprites": _drawSprites.Value = a[2]; break;
                        case "building_cubemaps": BuildingCubemaps = a[2] != "0"; break;
                        case "cl_sun_decay_rate": SunDecayRate = VariantParse.Atof(a[2]); break;
                        default: Assert.Fail("no ConVar " + a[1]); break;
                    }
                    break;
                case "draw":
                    System.DrawOverlays(View, a[1] == "1", this);
                    break;
                case "sky":
                    System.UpdateSkyOverlays(View.Origin, Float(a[1]), a[2] == "1");
                    break;
                case "activate":
                    System.Activate(Overlays[a[1]]);
                    break;
                case "deactivate":
                    GlowOverlaySystem.Deactivate(Overlays[a[1]]);
                    break;
                case "delete":
                    System.Remove(Overlays[a[1]]);
                    Overlays.Remove(a[1]);
                    break;
                case "shutdown":
                    System.LevelShutdownPostEntity();
                    Overlays.Clear();
                    break;
                default:
                    Assert.Fail("no step " + a[0]);
                    break;
            }
        }
    }

    private static List<string> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "glowoverlay-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock glow overlays are missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        Assert.True(rows.Count(r => r.StartsWith("S\t", StringComparison.Ordinal)) > 100 && rows.Count(r => r.StartsWith("MESH\t", StringComparison.Ordinal)) > 100,
                    "the oracle parsed");
        return rows;
    }

    [Fact]
    public void ThePrecacheListsAreStocks()
    {
        List<string> stock = Stock();
        var rows = GlowOverlaySystem.Precaches.Select(p => $"P\t{p.Name}\t{string.Join('\t', p.Materials)}").ToList();
        Assert.Equal(stock.TakeWhile(r => r.StartsWith("P\t", StringComparison.Ordinal)), rows);
    }

    // A computed position's allowance on a CPU whose rsqrtss is not the
    // recording machine's (StockRsqrt): 2^-18 of the row's largest coordinate.
    // The quads and the sight traces are built on normalised vectors, and the
    // estimate's last bits move a corner or an end point by a few ulps after
    // the Newton step (13 ulps of a 1.9 in a row of 98.8, 1.5e-8 of it, on an
    // Intel Xeon); a wrong size, axis or distance moves it by far more than
    // 3.8e-6 of the row.
    private const float RsqBound = 1.0f / 262144.0f;

    /// <summary>
    /// Whether a managed row is stock's but for its computed positions' last
    /// bits. A MESH row: the same vertex count, the same colours and texture
    /// coordinates bit for bit, each position coordinate within
    /// <see cref="RsqBound"/> of the row's largest. A T row (a sight trace):
    /// the start and end within the bound, the mask and the answer bit for
    /// bit. Any other row must be equal.
    /// </summary>
    internal static bool CloseRow(string expected, string actual)
    {
        if (expected == actual)
            return true;

        string[] e = expected.Split('\t'), a = actual.Split('\t');
        if (e.Length != a.Length || e[0] != a[0])
            return false;

        var stock = new List<float>();
        var port = new List<float>();
        switch (e[0])
        {
            case "MESH":
                if (e[1] != a[1] || e[2] != a[2])
                    return false;
                for (int i = 3; i < e.Length; i++)
                {
                    string[] ev = e[i].Split('/'), av = a[i].Split('/');
                    if (ev.Length != 3 || av.Length != 3 || ev[1] != av[1] || ev[2] != av[2])
                        return false;
                    if (!Positions(ev[0].Split(','), av[0].Split(','), stock, port))
                        return false;
                }
                break;

            case "T":
                if (e.Length != 12 || !e.AsSpan(7).SequenceEqual(a.AsSpan(7)))
                    return false;
                if (!Positions(e[1..7], a[1..7], stock, port))
                    return false;
                break;

            default:
                return false;
        }

        float scale = Math.Max(1f, stock.Max(MathF.Abs));
        return stock.Zip(port).All(p => MathF.Abs(p.First - p.Second) <= scale * RsqBound);
    }

    private static bool Positions(string[] expected, string[] actual, List<float> stock, List<float> port)
    {
        if (expected.Length % 3 != 0 || expected.Length != actual.Length)
            return false;
        stock.AddRange(expected.Select(Float));
        port.AddRange(actual.Select(Float));
        return true;
    }

    /// <summary>
    /// A step's rows against stock's: equal, or, on a CPU whose rsqrtss is not
    /// the recording machine's, equal but for computed positions within
    /// <see cref="RsqBound"/> (<see cref="CloseRow"/>).
    /// </summary>
    internal static bool SameRows(IReadOnlyList<string> expected, IReadOnlyList<string> actual, bool exact)
        => expected.SequenceEqual(actual)
           || (!exact && expected.Count == actual.Count && expected.Zip(actual).All(p => CloseRow(p.First, p.Second)));

    [Fact]
    public void EveryStepDoesWhatStocksDid()
    {
        var rig = new Rig();
        bool exact = StockRsqrt.MatchesRecording();
        List<string> stock = Stock();
        int i = stock.FindIndex(r => r.StartsWith("S\t", StringComparison.Ordinal));
        int steps = 0, held = 0;
        while (i < stock.Count)
        {
            string s = stock[i++];
            Assert.StartsWith("S\t", s);
            var expected = new List<string>();
            while (i < stock.Count && !stock[i].StartsWith("S\t", StringComparison.Ordinal))
                expected.Add(stock[i++]);
            rig.Log.Clear();
            rig.Expected = new Queue<string[]>(expected.Select(r => r.Split('\t')));
            rig.Step(s[2..].Split('\t'));
            Assert.True(SameRows(expected, rig.Log, exact),
                        $"step {steps} {s.Replace('\t', ' ')} ({(exact ? "bit for bit" : "positions within the rsqrtss bound")}):\n  stock   {string.Join("\n          ", expected)}\n  managed {string.Join("\n          ", rig.Log)}");
            steps++;
            held += expected.Count;
        }
        Assert.True(steps > 120 && held > 900, $"{steps} steps, {held} rows held");
    }

    // Step 14's first sun quad as stock drew it and as the port drew it on an
    // Intel Xeon (CI run 39386441712): the positions a few ulps apart, the rest
    // the same bits.
    private const string StockSunQuad =
        "MESH\t2\t4\t42c59e82,c17daa09,3ff53fc5/29353aff/00000000,3f800000\t42c29cf4,c1baf428,3ff53fc5/29353aff/3f800000,3f800000" +
        "\t42c29cf4,c1baf428,bff53fc5/29353aff/3f800000,00000000\t42c59e82,c17daa09,bff53fc5/29353aff/00000000,00000000";

    private const string IntelSunQuad =
        "MESH\t2\t4\t42c59e80,c17daa02,3ff53fd2/29353aff/00000000,3f800000\t42c29cf2,c1baf427,3ff53fd2/29353aff/3f800000,3f800000" +
        "\t42c29cf2,c1baf427,bff53fd2/29353aff/3f800000,00000000\t42c59e80,c17daa02,bff53fd2/29353aff/00000000,00000000";

    [Fact]
    public void AnotherMakersRsqrtMovesTheSunQuadWithinTheBound()
    {
        Assert.NotEqual(StockSunQuad, IntelSunQuad);
        Assert.True(CloseRow(StockSunQuad, IntelSunQuad), "the Xeon's quad is stock's but for its positions' last bits");
    }

    [Fact]
    public void AQuadMovedByMoreThanTheBoundIsRefused()
    {
        // 42c59e82 is 98.8096; 42c5a000 is 98.8125, 2.9e-3 away: 2.9e-5 of the row, about eight times the bound.
        string moved = StockSunQuad.Replace("42c59e82,c17daa09,3ff53fc5/29353aff/00000000", "42c5a000,c17daa09,3ff53fc5/29353aff/00000000",
                                            StringComparison.Ordinal);
        Assert.NotEqual(StockSunQuad, moved);
        Assert.False(CloseRow(StockSunQuad, moved));
    }

    [Fact]
    public void AQuadsColourAndTextureCoordinatesStayExact()
    {
        string recoloured = IntelSunQuad.Replace("/29353aff/3f800000,00000000", "/29353bff/3f800000,00000000", StringComparison.Ordinal);
        string remapped = IntelSunQuad.Replace("/29353aff/3f800000,00000000", "/29353aff/3f800001,00000000", StringComparison.Ordinal);
        Assert.True(recoloured != IntelSunQuad && remapped != IntelSunQuad, "the stimuli changed the row");
        Assert.False(CloseRow(StockSunQuad, recoloured));
        Assert.False(CloseRow(StockSunQuad, remapped));
    }

    // Step 72's first sight trace as stock asked it and as the port asked it
    // on an Intel Xeon: the end point an ulp apart, the mask and answer equal.
    private const string StockTrace = "T\t00000000\t00000000\t00000000\t47400000\t46315cac\t46cb3197\t00000001\t0\t0\t3f800000\t0";
    private const string IntelTrace = "T\t00000000\t00000000\t00000000\t473fffff\t46315cab\t46cb3196\t00000001\t0\t0\t3f800000\t0";

    [Fact]
    public void AnotherMakersRsqrtMovesATracesEndWithinTheBound()
    {
        Assert.NotEqual(StockTrace, IntelTrace);
        Assert.True(CloseRow(StockTrace, IntelTrace), "the Xeon's trace is stock's but for its end's last bit");
    }

    [Fact]
    public void ATracesAnswerAndMaskStayExact()
    {
        string answered = IntelTrace.Replace("\t3f800000\t0", "\t3f000000\t0", StringComparison.Ordinal);
        string masked = IntelTrace.Replace("\t00000001\t", "\t00000003\t", StringComparison.Ordinal);
        string moved = StockTrace.Replace("\t47400000\t", "\t47410000\t", StringComparison.Ordinal);
        Assert.True(answered != IntelTrace && masked != IntelTrace && moved != StockTrace, "the stimuli changed the row");
        Assert.False(CloseRow(StockTrace, answered));
        Assert.False(CloseRow(StockTrace, masked));
        Assert.False(CloseRow(StockTrace, moved));
    }

    [Fact]
    public void OnlyMeshAndTraceRowsAreGivenTheBound()
    {
        Assert.False(CloseRow("HS\tsun/overlay\t3fc00000", "HS\tsun/overlay\t3fc00001"));
        Assert.False(SameRows(["PA\t1", StockSunQuad], ["PA\t2", IntelSunQuad], exact: false));
        Assert.True(SameRows(["PA\t1", StockSunQuad], ["PA\t1", IntelSunQuad], exact: false));
    }

    [Fact]
    public void OnTheRecordingMakersCpuTheRowsMustBeBitForBit()
        => Assert.False(SameRows([StockSunQuad], [IntelSunQuad], exact: true));

    [Fact]
    public void TheRsqrtProbesAreRead()
        => Assert.True(StockRsqrt.Probes().Count >= 8, $"{StockRsqrt.Probes().Count} probes");

    [Fact]
    public void ARampOverlayFadesAndGrowsForATenthOfASecond()
    {
        var o = new RampOverlay(RampOverlay.ExplosionGrowth) { NSprites = 1 };
        o.BaseColors[0] = new Vector(1.0f, 0.9f, 0.7f);
        o.Sprites[0].HorzSize = 0.05f;
        o.Sprites[0].VertSize = 0.025f;

        Assert.True(o.Update(0.025f));
        Assert.Equal(0.025f, o.Lifetime);
        Assert.Equal(0.75f, o.Sprites[0].Color.X);
        Assert.Equal(0.05f + 16.0f * 0.025f, o.Sprites[0].HorzSize);
        Assert.Equal(0.025f + 16.0f * 0.025f, o.Sprites[0].VertSize);
        Assert.True(o.Update(0.05f));
        Assert.False(o.Update(0.05f));      // 0.125 s: no longer under the tenth

        var impact = new RampOverlay(RampOverlay.ImpactGrowth) { NSprites = 1 };
        impact.Sprites[0].HorzSize = 0.01f;
        Assert.True(impact.Update(0.05f));
        Assert.Equal(0.01f + 1.0f * 0.05f, impact.Sprites[0].HorzSize);
    }
}
