using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Detail;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One behaviour of stock's detailobjectsystem.cpp per fact, as
/// <see cref="DetailPropSystem"/> carries it (DetailPropsOracleTests holds the
/// whole port to stock's recording; these name what it holds).
/// </summary>
public class DetailPropsTests
{
    // -------------------------------------------------------------------------
    // A detail lump, written as vbsp writes one.
    // -------------------------------------------------------------------------
    private sealed class Lump
    {
        public readonly List<string> Models = [];
        public readonly List<float[]> Sprites = [];
        public readonly List<DetailObjectLump> Objects = [];

        public static DetailObjectLump Sprite(Vector origin, int leaf, byte orientation = 2, ushort sprite = 0, float scale = 1.0f)
            => new()
            {
                Origin = origin,
                Leaf = (ushort)leaf,
                Type = (byte)DetailPropType.Sprite,
                Orientation = orientation,
                DetailModel = sprite,
                Scale = scale,
                Lighting = new ColorRgbExp32(128, 64, 32, 0),
            };

        public static DetailObjectLump Model(Vector origin, int leaf, ushort model = 0, byte orientation = 0)
            => new()
            {
                Origin = origin,
                Leaf = (ushort)leaf,
                Type = (byte)DetailPropType.Model,
                Orientation = orientation,
                DetailModel = model,
                Lighting = new ColorRgbExp32(255, 255, 255, 0),
            };

        public byte[] Bytes()
        {
            var b = new List<byte>();
            void Int(int v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(s, v); b.AddRange(s.ToArray()); }
            void Flt(float v) => Int(BitConverter.SingleToInt32Bits(v));

            Int(Models.Count);
            foreach (string m in Models)
            {
                byte[] name = new byte[DetailGameLumps.NameLength];
                Encoding.ASCII.GetBytes(m).CopyTo(name, 0);
                b.AddRange(name);
            }
            Int(Sprites.Count);
            foreach (float[] s in Sprites)
            {
                foreach (float f in s)
                    Flt(f);
            }
            Int(Objects.Count);
            foreach (DetailObjectLump o in Objects)
            {
                Flt(o.Origin.X); Flt(o.Origin.Y); Flt(o.Origin.Z);
                Flt(o.Angles.X); Flt(o.Angles.Y); Flt(o.Angles.Z);
                b.Add((byte)o.DetailModel); b.Add((byte)(o.DetailModel >> 8));
                b.Add((byte)o.Leaf); b.Add((byte)(o.Leaf >> 8));
                b.Add(o.Lighting.R); b.Add(o.Lighting.G); b.Add(o.Lighting.B); b.Add(unchecked((byte)o.Lighting.Exponent));
                Int(unchecked((int)o.LightStyles));
                b.Add(o.LightStyleCount); b.Add(o.SwayAmount); b.Add(o.ShapeAngle); b.Add(o.ShapeSize);
                b.Add(o.Orientation); b.Add(0); b.Add(0); b.Add(0);
                b.Add(o.Type); b.Add(0); b.Add(0); b.Add(0);
                Flt(o.Scale);
            }
            return [.. b];
        }
    }

    // -------------------------------------------------------------------------
    // The engine and convars, answering simply.
    // -------------------------------------------------------------------------
    private sealed class Engine : IDetailPropEngine, IDetailPropSettings
    {
        public readonly Dictionary<int, (int Version, byte[] Bytes)> Lumps = [];
        public readonly List<string> Loaded = [];
        public readonly List<string> Warnings = [];
        public readonly List<int> LumpsRead = [];
        public int StyleValue = 1;
        public long ModelBase;

        public float DetailDist { get; set; } = 1200;
        public float DetailFade { get; set; } = 400;
        public int Multiplier { get; set; } = 1;
        public int FastSprites { get; set; } = 1;

        public int GameLumpVersion(int lumpId) => Lumps.TryGetValue(lumpId, out var l) ? l.Version : 0;
        public int GameLumpSize(int lumpId) => Lumps.TryGetValue(lumpId, out var l) ? l.Bytes.Length : 0;

        public bool LoadGameLump(int lumpId, Span<byte> buffer)
        {
            LumpsRead.Add(lumpId);
            if (!Lumps.TryGetValue(lumpId, out var l) || l.Bytes.Length != buffer.Length)
                return false;
            l.Bytes.CopyTo(buffer);
            return true;
        }

        public nint LoadModel(string name, bool prop)
        {
            Loaded.Add(name + (prop ? " prop" : ""));
            int i = Loaded.FindIndex(n => n.StartsWith(name, StringComparison.Ordinal));
            return (nint)(ModelBase + i + 1);
        }

        public bool IsModelVertexLit(nint model) => Loaded[(int)(model - ModelBase) - 1].Contains("vlit", StringComparison.Ordinal);
        public bool ModelHasMaterialProxy(nint model) => Loaded[(int)(model - ModelBase) - 1].Contains("proxy", StringComparison.Ordinal);
        public Vector LinearToGamma(Vector linear) => new(MathF.Min(linear.X, 1), MathF.Min(linear.Y, 1), MathF.Min(linear.Z, 1));
        public float LightStyleValue(int style) => style == 0 ? StyleValue : (style == 7 ? 0 : 0.5f);
        public Vector ComputeDynamicLighting(Vector point, Vector normal) => new(0, 0, 0);
        public void Warning(string text) => Warnings.Add(text);
    }

    private sealed class Sink : IDetailLevelSink, IDetailViewSink, IDetailDrawSink
    {
        public readonly List<(nint Model, Vector Origin, Vector Angles)> Objects = [];
        public readonly List<(int Leaf, int First, int Count, bool Fast)> Runs = [];
        public readonly List<nint> Recomputed = [];
        public readonly List<(int Ordinal, byte Alpha, bool SetAngles, Vector Angles, Vector Color)> States = [];
        public readonly List<int> Begins = [];
        public readonly List<(float X, float Y, float Z, byte A, float S, float T, byte R)> Vertices = [];
        public readonly List<(int Index, byte Alpha)> Alphas = [];
        public int Draws;

        public void Object(nint model, Vector origin, Vector angles) => Objects.Add((model, origin, angles));
        public void LeafRun(int leaf, int first, int count, bool fastSprites) => Runs.Add((leaf, first, count, fastSprites));
        public void RecomputeTranslucency(nint model) => Recomputed.Add(model);
        public void ModelState(int modelOrdinal, byte alpha, bool setAngles, Vector angles, Vector color) => States.Add((modelOrdinal, alpha, setAngles, angles, color));
        public void Begin(int quads) => Begins.Add(quads);
        public void Vertex(float x, float y, float z, byte r, byte g, byte b, byte a, float s, float t) => Vertices.Add((x, y, z, a, s, t, r));
        public void EndDraw() => Draws++;
        public void ModelAlpha(int objectIndex, byte alpha) => Alphas.Add((objectIndex, alpha));

        public void ClearDraws()
        {
            Begins.Clear();
            Vertices.Clear();
            Alphas.Clear();
            Draws = 0;
        }
    }

    private static readonly DetailView ViewDownX = new(new Vector(0, 0, 0), new Vector(1, 0, 0), new Vector(0, -1, 0), new Vector(0, 0, 1), new Vector(0, 0, 0));

    private static (DetailPropSystem System, Engine Engine, Sink Sink) Load(Lump lump, int version = 4, Action<Engine>? setup = null)
    {
        var engine = new Engine();
        setup?.Invoke(engine);
        engine.Lumps[DetailGameLumps.DetailProps] = (version, lump.Bytes());
        var system = new DetailPropSystem(engine, engine);
        system.Init();
        var sink = new Sink();
        system.LevelInitPreEntity(256, 256, false, sink);
        return (system, engine, sink);
    }

    private static Lump OneSpriteSheet()
    {
        var lump = new Lump();
        lump.Sprites.Add([-8, 16, 8, 0, 0.0f, 0.0f, 0.5f, 0.25f]);
        return lump;
    }

    // Draws leaf 0's old sprites as RenderTranslucentDetailObjects' second half.
    private static void DrawOld(DetailPropSystem system, Sink sink, DetailView view, int maxQuads = 1000, DetailSwitches? switches = null)
    {
        var s = switches ?? new DetailSwitches(1, false, 0);
        if (system.PrepareList(DetailDrawKind.OldList, view, [0], s, sink) != DetailDrawMaterial.None)
            system.Emit(maxQuads, sink);
    }

    private static void DrawFast(DetailPropSystem system, Sink sink, DetailView view, int maxQuads = 1000, DetailSwitches? switches = null, int leaf = 0)
    {
        var s = switches ?? new DetailSwitches(1, false, 0);
        if (system.PrepareList(DetailDrawKind.FastList, view, [leaf], s, sink) != DetailDrawMaterial.None)
            system.Emit(maxQuads, sink);
    }

    // -------------------------------------------------------------------------
    // mathlib and tier1
    // -------------------------------------------------------------------------

    [Fact]
    public void AHalfFloatTruncatesItsMantissaRatherThanRounding()
    {
        Assert.Equal(1.0009765625f, DetailMath.Half(1.0009765625f));     // 1 + 2^-10 is a half
        // One float ulp under 1 + 2^-10: rounding would answer 1 + 2^-10.
        Assert.Equal(1.0f, DetailMath.Half(BitConverter.Int32BitsToSingle(0x3F801FFF)));
    }

    [Fact]
    public void AHalfFloatClampsMapsInfinityToItsLargestAndNaNAndFloatDenormalsToZero()
    {
        Assert.Equal(65504.0f, DetailMath.Half(70000.0f));
        Assert.Equal(-65504.0f, DetailMath.Half(-1e30f));
        Assert.Equal(65504.0f, DetailMath.Half(float.PositiveInfinity));
        Assert.Equal(0.0f, DetailMath.Half(float.NaN));
        Assert.Equal(0.0f, DetailMath.Half(1e-40f));
        // 2^-16 is a half denormal: 2^(10 - 2) / 1024 * 2^-14.
        Assert.Equal(MathF.ScaleB(1, -16), DetailMath.Half(MathF.ScaleB(1, -16)));
    }

    [Fact]
    public void ThePowerTwoTableIsColorConversionsLiterals()
    {
        // color_conversion.cpp's power2_n, stored as floats.
        Assert.Equal((float)1.152445441982634800E-041, DetailMath.Power2N(-128));
        Assert.Equal((float)2.304890883965269600E-041, DetailMath.Power2N(-127));
        Assert.Equal((float)1.531862745098039200E-005, DetailMath.Power2N(-8));
        Assert.Equal((float)1.960784313725490200E-003, DetailMath.Power2N(-1));
        Assert.Equal((float)3.921568627450980300E-003, DetailMath.Power2N(0));
        Assert.Equal((float)7.843137254901960700E-003, DetailMath.Power2N(1));
        Assert.Equal((float)6.672203272959577600E+035, DetailMath.Power2N(127));
        Assert.Equal(128 * DetailMath.Power2N(1), DetailMath.TexLightToLinear(128, 1));
    }

    [Fact]
    public void TheSortPutsTheFarthestFirstAndOrdersTiesAsTier1sHeapSort()
    {
        DetailSortInfo[] a = [new() { Index = 0, Distance = 5 }, new() { Index = 1, Distance = 9 }, new() { Index = 2, Distance = 1 }];
        DetailHeapSort.Sort(a);
        Assert.Equal([1, 0, 2], a.Select(x => x.Index));

        // Three equal distances come out 1, 2, 0: heapify moves nothing, then
        // the two swaps of the sort phase.
        DetailSortInfo[] tie = [new() { Index = 0, Distance = 1 }, new() { Index = 1, Distance = 1 }, new() { Index = 2, Distance = 1 }];
        DetailHeapSort.Sort(tie);
        Assert.Equal([1, 2, 0], tie.Select(x => x.Index));
    }

    [Fact]
    public void AReadPastTheLumpsEndAnswersZeroAndEveryLaterReadFailsToo()
    {
        var r = new DetailLumpReader([1, 0, 0, 0, 2, 0]);
        Assert.Equal(1, r.GetInt());
        Assert.Equal(0, r.GetInt());	// two bytes left
        r.Seek(0);
        Assert.Equal(0, r.GetInt());	// the overflow stays (GET_OVERFLOW)
    }

    // -------------------------------------------------------------------------
    // The level's load
    // -------------------------------------------------------------------------

    [Fact]
    public void ALumpOlderThanVersionFourIsRefusedWithAWarningAndNoLightingIsRead()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(10, 0, 0), 0, orientation: 0));
        var (system, engine, sink) = Load(lump, version: 3);
        Assert.Single(engine.Warnings, w => w.Contains("old detail prop file format", StringComparison.Ordinal));
        Assert.Empty(engine.LumpsRead);
        Assert.Empty(sink.Objects);
        Assert.False(system.HasDetail);
    }

    [Fact]
    public void AVersionFiveLumpLoadsItsModelsAndNothingElse()
    {
        var lump = OneSpriteSheet();
        lump.Models.Add("models/grass.mdl");
        lump.Objects.Add(Lump.Sprite(new Vector(10, 0, 0), 0, orientation: 0));
        var (system, engine, sink) = Load(lump, version: 5);
        Assert.Equal(["models/grass.mdl prop"], engine.Loaded);
        Assert.Empty(sink.Objects);
        Assert.Empty(sink.Runs);
    }

    [Fact]
    public void AVertexLitModelIsReplacedByTheErrorModelWithAWarning()
    {
        var lump = new Lump();
        lump.Models.Add("models/vlit_fern.mdl");
        lump.Objects.Add(Lump.Model(new Vector(5, 0, 0), 0));
        var (_, engine, sink) = Load(lump);
        Assert.Equal(["models/vlit_fern.mdl prop", "models/error.mdl"], engine.Loaded);
        Assert.Single(engine.Warnings, w => w.Contains("vertex-lit", StringComparison.Ordinal));
        Assert.Equal(2, sink.Objects[0].Model);
    }

    [Fact]
    public void OnlyAPlainScreenAlignedSpriteWithNoStyleShapeOrSwayIsAFastSprite()
    {
        static bool Fast(Func<DetailObjectLump, DetailObjectLump> edit, int cvar = 1)
        {
            var lump = OneSpriteSheet();
            lump.Objects.Add(edit(Lump.Sprite(new Vector(10, 0, 0), 0)));
            var (_, _, sink) = Load(lump, setup: e => e.FastSprites = cvar);
            return sink.Runs.Single().Fast;
        }

        Assert.True(Fast(o => o));
        Assert.False(Fast(o => o, cvar: 0));
        Assert.False(Fast(o => { o.Orientation = 1; return o; }));
        Assert.False(Fast(o => { o.Orientation = 6; return o; }));		// the lump's value, not the bitfield's
        Assert.False(Fast(o => { o.LightStyleCount = 1; return o; }));
        Assert.False(Fast(o => { o.ShapeAngle = 1; return o; }));
        Assert.False(Fast(o => { o.ShapeSize = 1; return o; }));
        Assert.False(Fast(o => { o.SwayAmount = 1; return o; }));
        Assert.False(Fast(o => { o.Type = (byte)DetailPropType.ShapeCross; return o; }));
    }

    [Fact]
    public void EachLeafsRunIsHandedOverWhenTheLeafChangesAndARevisitedLeafIsHandedOverAgain()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(1, 0, 0), 3, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(2, 0, 0), 3, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(3, 0, 0), 5));
        lump.Objects.Add(Lump.Sprite(new Vector(4, 0, 0), 3, orientation: 0));
        var (_, _, sink) = Load(lump);
        Assert.Equal([(3, 0, 2, false), (5, 2, 0, true), (3, 2, 1, false)], sink.Runs);
    }

    [Fact]
    public void ARevisitedLeafDrawsItsLastRunsObjects()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 3, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(101, 0, 0), 3, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(102, 0, 0), 5, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(103, 0, 0), 3, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(104, 0, 0), 7, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        Assert.NotEqual(DetailDrawMaterial.None, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [3], new(1, false, 0), sink));
        system.Emit(1000, sink);
        Assert.Equal([1], sink.Begins);
        Assert.Equal(4, sink.Vertices.Count);
    }

    [Fact]
    public void ARevisitWithNoFastSpritesKeepsTheLeafsEarlierFastSprites()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 3));
        lump.Objects.Add(Lump.Sprite(new Vector(102, 0, 0), 5, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(103, 0, 0), 3, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        system.BeginTranslucent();
        DrawFast(system, sink, ViewDownX, leaf: 3);
        Assert.Equal(4, sink.Vertices.Count);
    }

    [Fact]
    public void AnInvalidTypeIsSkippedButStillTogglesTheFlip()
    {
        var lump = OneSpriteSheet();
        var bad = Lump.Sprite(new Vector(1, 0, 0), 0, orientation: 0);
        bad.Type = 6;
        lump.Objects.Add(bad);
        lump.Objects.Add(Lump.Sprite(new Vector(2, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        Assert.Single(sink.Objects);
        Assert.Equal(1, system.ObjectCount);

        // The second object reads flipped (the first toggled to false): its
        // texture corners are the sheet's, unswapped.
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX with { Origin = new Vector(-100, 0, 0), CurrentViewOrigin = new Vector(-100, 0, 0) });
        Assert.Equal(0.0f, sink.Vertices[0].S);
    }

    [Fact]
    public void TheFirstObjectIsUnflippedAndItsTextureRunsRightToLeft()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(2, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX with { Origin = new Vector(-100, 0, 0), CurrentViewOrigin = new Vector(-100, 0, 0) });
        Assert.Equal(0.5f, sink.Vertices[0].S);		// TexLR.x on the upper left
    }

    [Fact]
    public void TheMultipliersExtraCopiesMoveWithinFiftyUnitsAndKeepTheirHeight()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 200, 30), 0, orientation: 0));
        var (system, _, sink) = Load(lump, setup: e => e.Multiplier = 3);
        Assert.Equal(3, sink.Objects.Count);
        Assert.Equal(new Vector(100, 200, 30), sink.Objects[0].Origin);
        for (int i = 1; i < 3; i++)
        {
            Vector o = sink.Objects[i].Origin;
            Assert.InRange(o.X, 50, 150);
            Assert.InRange(o.Y, 150, 250);
            Assert.Equal(30, o.Z);
            Assert.NotEqual(new Vector(100, 200, 30), o);
        }
    }

    [Fact]
    public void AWideSpriteSheetStretchesTheTextureRowsAndASquareOneDoesNot()
    {
        static float TopRow(int w, int h)
        {
            var lump = new Lump();
            lump.Sprites.Add([-8, 16, 8, 0, 0.0f, 0.25f, 0.5f, 0.5f]);
            lump.Objects.Add(Lump.Sprite(new Vector(2, 0, 0), 0, orientation: 0));
            var engine = new Engine();
            engine.Lumps[DetailGameLumps.DetailProps] = (4, lump.Bytes());
            var system = new DetailPropSystem(engine, engine);
            var sink = new Sink();
            system.LevelInitPreEntity(w, h, false, sink);
            system.BeginTranslucent();
            DrawOld(system, sink, ViewDownX with { Origin = new Vector(-100, 0, 0), CurrentViewOrigin = new Vector(-100, 0, 0) });
            return sink.Vertices[0].T;
        }

        Assert.Equal(0.5f, TopRow(512, 256));
        Assert.Equal(0.25f, TopRow(256, 256));
        Assert.Equal(0.25f, TopRow(128, 256));
    }

    [Fact]
    public void TheMapsSpriteMaterialWinsAndTheFadesGoBackToThePlayersAtEveryLevel()
    {
        var engine = new Engine { DetailDist = 900, DetailFade = 300 };
        var system = new DetailPropSystem(engine, engine);
        system.Init();
        engine.DetailDist = 100;
        engine.DetailFade = 10;
        Assert.Equal("detail/map", system.LevelInitPostEntity("detail/map"));
        Assert.Equal(900, engine.DetailDist);
        Assert.Equal(300, engine.DetailFade);
        Assert.Equal("detail/detailsprites", system.LevelInitPostEntity(""));
        Assert.Equal("detail/detailsprites", system.LevelInitPostEntity(null));
    }

    // -------------------------------------------------------------------------
    // A view's lists
    // -------------------------------------------------------------------------

    private static Lump Models(params DetailObjectLump[] objects)
    {
        var lump = new Lump();
        lump.Models.Add("models/a.mdl");
        lump.Models.Add("models/proxy_b.mdl");
        lump.Models.Add("models/proxy_c.mdl");
        lump.Objects.AddRange(objects);
        return lump;
    }

    [Fact]
    public void NoListIsBuiltWhenTheClientModeOrRDrawDetailPropsSaysNoOrTheLevelHasNoDetail()
    {
        var (system, _, sink) = Load(Models(Lump.Model(new Vector(5, 0, 0), 0)));
        Assert.Null(system.BuildRenderLists(default, default, false, 1, 0, sink));
        Assert.Null(system.BuildRenderLists(default, default, true, 0, 0, sink));
        Assert.Empty(sink.States);
        Assert.Empty(sink.Recomputed);

        var (empty, _, emptySink) = Load(new Lump());
        Assert.Null(empty.BuildRenderLists(default, default, true, 1, 0, emptySink));
    }

    [Fact]
    public void ProxiedModelsRecomputeTheirTranslucencyLastFirst()
    {
        var (system, _, sink) = Load(Models(Lump.Model(new Vector(5, 0, 0), 0)));
        Assert.Equal(1200f, system.BuildRenderLists(default, default, true, 1, 0, sink));
        Assert.Equal([3, 2], sink.Recomputed);
    }

    [Fact]
    public void AModelPropFadesOverTheLastStretchAndVanishesPastTheDistance()
    {
        var (system, engine, sink) = Load(Models(
            Lump.Model(new Vector(100, 0, 0), 0),
            Lump.Model(new Vector(1000, 0, 0), 0),
            Lump.Model(new Vector(1300, 0, 0), 0)));
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        Assert.Equal(255, sink.States[0].Alpha);
        // falloff 255 / (1200^2 - 800^2), at 1000^2: 255 * 0.55 = 140.25, truncated.
        Assert.Equal(140, sink.States[1].Alpha);
        Assert.Equal(0, sink.States[2].Alpha);
    }

    [Fact]
    public void AFadeLongerThanTheDistanceStillLeavesOneUnitSquaredToFadeOver()
    {
        var (system, engine, sink) = Load(Models(Lump.Model(new Vector(9.99f, 0, 0), 0)), setup: e => { e.DetailDist = 10; e.DetailFade = -5; });
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        // fade (10 - -5)^2 = 225, clamped to 10^2 - 1: 255 * (100 - 99.8001) / 1.
        Assert.Equal(DetailMath.ToByte(255.0f / (100.0f - 99.0f) * (100.0f - 9.99f * 9.99f)), sink.States[0].Alpha);
    }

    [Fact]
    public void AModelPropsColourIsItsLightmapThroughGammaAndWhiteUnderFullbright()
    {
        var (system, _, sink) = Load(Models(Lump.Model(new Vector(100, 0, 0), 0)));
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        // Style 0 at 1, no dynamic light: 255 * 2^0 / 255 per channel, through the gamma (a clamp here).
        Assert.Equal(new Vector(1, 1, 1), sink.States[0].Color);
        sink.States.Clear();
        var (dark, _, darkSink) = Load(Models(Lump.Model(new Vector(100, 0, 0), 0)), setup: e => e.StyleValue = 0);
        dark.BuildRenderLists(default, default, true, 1, 0, darkSink);
        Assert.Equal(new Vector(0, 0, 0), darkSink.States[0].Color);
        darkSink.States.Clear();
        dark.BuildRenderLists(default, default, true, 1, 1, darkSink);
        Assert.Equal(new Vector(1, 1, 1), darkSink.States[0].Color);
    }

    [Fact]
    public void AScreenAlignedModelFacesTheCurrentViewAndAnUprightOneTurnsOnlyAboutZ()
    {
        var (system, _, sink) = Load(Models(
            Lump.Model(new Vector(100, 0, 0), 0, orientation: 1),
            Lump.Model(new Vector(100, 0, 0), 0, orientation: 2),
            Lump.Model(new Vector(100, 0, 0), 0, orientation: 0),
            Lump.Model(new Vector(5000, 0, 0), 0, orientation: 1)));
        Vector eye = new(0, 0, 100);
        system.BuildRenderLists(default, eye, true, 1, 0, sink);
        Assert.True(sink.States[0].SetAngles);
        Assert.Equal(DetailMath.VectorAngles(eye - new Vector(100, 0, 0)), sink.States[0].Angles);
        Assert.True(sink.States[1].SetAngles);
        Assert.Equal(0, sink.States[1].Angles.X);
        Assert.Equal(180, sink.States[1].Angles.Y);
        Assert.False(sink.States[2].SetAngles);
        Assert.False(sink.States[3].SetAngles);		// out of range: the angles stay
    }

    // -------------------------------------------------------------------------
    // The sorted sprites
    // -------------------------------------------------------------------------

    [Fact]
    public void TheSortFadesWithoutTheListBuildsClampAndHandsModelPropsTheirAlpha()
    {
        var lump = Models(Lump.Model(new Vector(900, 0, 0), 0));
        lump.Sprites.Add([-8, 16, 8, 0, 0, 0, 0.5f, 0.25f]);
        lump.Objects.Add(Lump.Sprite(new Vector(1100, 0, 0), 0, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(1300, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX);
        // The model's alpha from the sort's own fade, and no quad for it.
        float falloff = 255.0f / (1200.0f * 1200.0f - 800.0f * 800.0f);
        Assert.Equal([(0, DetailMath.ToByte(falloff * (1200.0f * 1200.0f - 900.0f * 900.0f)))], sink.Alphas);
        // One quad: the sprite at 1100; the one at 1300 is past the distance.
        Assert.Equal(4, sink.Vertices.Count);
        Assert.Equal(DetailMath.ToByte(falloff * (1200.0f * 1200.0f - 1100.0f * 1100.0f)), sink.Vertices[0].A);
    }

    [Fact]
    public void ASpriteWhoseFadeTruncatesToZeroIsNotDrawn()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(1199.9999f, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX);
        Assert.Empty(sink.Vertices);
        Assert.Equal([1], sink.Begins);		// the batch still began for its one object
    }

    [Fact]
    public void ShapedSpritesWeighTheirQuadsInABatchButDrawNothing()
    {
        var lump = OneSpriteSheet();
        var cross = Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0);
        cross.Type = (byte)DetailPropType.ShapeCross;
        lump.Objects.Add(cross);
        lump.Objects.Add(Lump.Sprite(new Vector(50, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX);
        // Two objects declared; the cross weighs 4 and splits the batch twice,
        // and the count left runs below zero.
        Assert.Equal([2, 2, -2], sink.Begins);
        Assert.Equal(4, sink.Vertices.Count);
    }

    [Fact]
    public void AFullListBatchBeginsAgainWithWhatIsLeftOfTheCount()
    {
        var lump = OneSpriteSheet();
        for (int i = 0; i < 5; i++)
            lump.Objects.Add(Lump.Sprite(new Vector(100 + i, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX, maxQuads: 2);
        Assert.Equal([2, 2, 1], sink.Begins);
        Assert.Equal(3, sink.Draws);
    }

    [Fact]
    public void FullbrightIsWhiteAndAStyleThatIsOffAddsNothing()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        var (system, engine, sink) = Load(lump, setup: e => e.StyleValue = 0);
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX, switches: new DetailSwitches(1, false, 1));
        Assert.Equal(255, sink.Vertices[0].R);
        sink.ClearDraws();
        system.BeginTranslucent();
        DrawOld(system, sink, ViewDownX);
        Assert.Equal(0, sink.Vertices[0].R);	// style 0 is off and there is no dynamic light
    }

    [Fact]
    public void TheWireframeMaterialIsChosenBySwitchOrByRDrawDetailPropsTwo()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [0], new(1, false, 0), sink));
        Assert.Equal(DetailDrawMaterial.Wireframe, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [0], new(1, true, 0), sink));
        Assert.Equal(DetailDrawMaterial.Wireframe, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [0], new(2, false, 0), sink));
        Assert.Equal(DetailDrawMaterial.None, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [], new(1, false, 0), sink));
    }

    [Fact]
    public void RDrawDetailPropsZeroStopsTheFastSpritesButNotTheSortedOnes()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(100, 5, 0), 0));
        var (system, _, sink) = Load(lump);
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        var off = new DetailSwitches(0, false, 0);
        Assert.Equal(DetailDrawMaterial.None, system.PrepareList(DetailDrawKind.FastList, ViewDownX, [0], off, sink));
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareList(DetailDrawKind.OldList, ViewDownX, [0], off, sink));
    }

    [Fact]
    public void ALeafDrawnUpToAnEntityDrawsOnlyWhatIsFartherAndTheRestNext()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(300, 0, 0), 0, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        var (system, _, sink) = Load(lump);
        system.BeginTranslucent();
        var s = new DetailSwitches(1, false, 0);
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.OldInLeaf, ViewDownX, 0, new Vector(200, 0, 0), s, sink));
        system.Emit(1000, sink);
        Assert.Equal(4, sink.Vertices.Count);
        Assert.Equal([8], sink.Begins);		// (2 sorted - 0 drawn) * 4
        sink.ClearDraws();
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.OldInLeaf, ViewDownX, 0, null, s, sink));
        system.Emit(1000, sink);
        Assert.Equal(4, sink.Vertices.Count);
        Assert.Equal([4], sink.Begins);		// (2 - 1) * 4: the cursor kept its place
        Assert.Equal(DetailDrawMaterial.None, system.PrepareInLeaf(DetailDrawKind.OldInLeaf, ViewDownX, 0, null, s, sink));

        // A new translucent pass sorts the leaf again.
        system.BeginTranslucent();
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.OldInLeaf, ViewDownX, 0, null, s, sink));
    }

    // -------------------------------------------------------------------------
    // The fast sprites
    // -------------------------------------------------------------------------

    private static (DetailPropSystem, Sink) FastRow(params float[] xs)
    {
        var lump = OneSpriteSheet();
        foreach (float x in xs)
            lump.Objects.Add(Lump.Sprite(new Vector(x, 0, 0), 0));
        var (system, _, sink) = Load(lump);
        system.BuildRenderLists(default, default, true, 1, 0, sink);
        system.BeginTranslucent();
        return (system, sink);
    }

    [Fact]
    public void AGroupOfFourBehindTheViewIsCulledWholeAndAPartlyVisibleOneDrawsAllFourLanes()
    {
        // Lanes 0-3 behind the camera, lanes 4-7 one behind and three ahead.
        var (system, sink) = FastRow(-10, -20, -30, -40, -50, 60, 70, 80);
        DrawFast(system, sink, ViewDownX);
        Assert.Equal(16, sink.Vertices.Count);
    }

    [Fact]
    public void ANegativeZeroDotCullsLikeANegativeOneAndAPositiveZeroDoesNot()
    {
        // A sprite straight beside the camera: its offset (0, 100, 10) dotted
        // with a forward of (-1, -0, -0) is -0, whose sign bit the cull tests.
        static int Drawn(Vector forward)
        {
            var lump = OneSpriteSheet();
            lump.Objects.Add(Lump.Sprite(new Vector(0, 100, 10), 0));
            var (system, _, sink) = Load(lump);
            system.BuildRenderLists(default, default, true, 1, 0, sink);
            system.BeginTranslucent();
            DrawFast(system, sink, ViewDownX with { Forward = forward });
            return sink.Vertices.Count;
        }

        Assert.Equal(0, Drawn(new Vector(-1, -0.0f, -0.0f)));
        Assert.Equal(4, Drawn(new Vector(-1, 0.0f, 0.0f)));
    }

    [Fact]
    public void TheLastGroupsPaddingIsDroppedOnlyWhenTheGroupWasDrawn()
    {
        var (system, sink) = FastRow(10, 20, 30, 40, 50);	// two groups, the second padded with three copies
        DrawFast(system, sink, ViewDownX);
        Assert.Equal(20, sink.Vertices.Count);

        // The second group culled whole (its one sprite and its copies are
        // behind the camera): nothing of it is listed, so nothing is dropped.
        var (culled, culledSink) = FastRow(10, 20, 30, 40, -50);
        DrawFast(culled, culledSink, ViewDownX);
        Assert.Equal(16, culledSink.Vertices.Count);
    }

    [Fact]
    public void AFastSpritesAlphaIsTheMagicNumbersLowByte()
    {
        Assert.Equal(255, DetailMath.MagicByte(1.0f));
        Assert.Equal(0, DetailMath.MagicByte(0.0f));
        Assert.Equal(128, DetailMath.MagicByte(0.5f));		// 127.5 rounds to even
        Assert.Equal(127, DetailMath.MagicByte(127.4f / 255.0f));
    }

    [Fact]
    public void AFullFastBatchBeginsAgainWithTheSameCount()
    {
        var (system, sink) = FastRow(10, 20, 30, 40, 50);
        DrawFast(system, sink, ViewDownX, maxQuads: 2);
        Assert.Equal([2, 2, 2], sink.Begins);
        Assert.Equal(3, sink.Draws);
    }

    [Fact]
    public void OnceAFastLeafsFirstPendingSpritePassesTheEntityEveryPendingSpriteDraws()
    {
        var (system, sink) = FastRow(500, 400, 300, 100);
        var s = new DetailSwitches(1, false, 0);
        // The entity at 450: the farthest (500) passes, and so do all the nearer ones.
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.FastInLeaf, ViewDownX, 0, new Vector(450, 0, 0), s, sink));
        system.Emit(1000, sink);
        Assert.Equal(16, sink.Vertices.Count);
        Assert.Equal(DetailDrawMaterial.None, system.PrepareInLeaf(DetailDrawKind.FastInLeaf, ViewDownX, 0, null, s, sink));
    }

    [Fact]
    public void AFastSpriteExactlyAsFarAsTheEntityDrawsBeforeIt()
    {
        var (system, sink) = FastRow(500);
        var s = new DetailSwitches(1, false, 0);
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.FastInLeaf, ViewDownX, 0, new Vector(500, 0, 0), s, sink));
        system.Emit(1000, sink);
        Assert.Equal(4, sink.Vertices.Count);
    }

    [Fact]
    public void AFastLeafWhoseFarthestSpriteIsNearerThanTheEntityWaitsForIt()
    {
        var (system, sink) = FastRow(300, 100);
        var s = new DetailSwitches(1, false, 0);
        Assert.Equal(DetailDrawMaterial.None, system.PrepareInLeaf(DetailDrawKind.FastInLeaf, ViewDownX, 0, new Vector(400, 0, 0), s, sink));
        Assert.Equal(DetailDrawMaterial.Sprite, system.PrepareInLeaf(DetailDrawKind.FastInLeaf, ViewDownX, 0, null, s, sink));
    }

    // -------------------------------------------------------------------------
    // The wire native reads (DetailPropsWire; detail_props_wire.h reads the
    // same layouts in detail_props_wire_test)
    // -------------------------------------------------------------------------

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static DetailPropsHost Host(Lump lump, Action<Engine>? setup = null)
    {
        var engine = new Engine();
        setup?.Invoke(engine);
        engine.Lumps[DetailGameLumps.DetailProps] = (4, lump.Bytes());
        return new DetailPropsHost(new DetailPropSystem(engine, engine));
    }

    [Fact]
    public void ALevelsAnswerIsItsFlagsObjectsAndRunsLengthFirst()
    {
        var lump = Models(Lump.Model(new Vector(1, 2, 3), 4));
        lump.Sprites.Add([-8, 16, 8, 0, 0, 0, 0.5f, 0.25f]);
        lump.Objects.Add(Lump.Sprite(new Vector(5, 6, 7), 9));
        var host = Host(lump);
        host.LevelInitPreEntity(256, 256, false);
        int[] w = host.Words.ToArray();
        Assert.Equal(w.Length, w[0]);
        Assert.Equal(DetailPropsWire.LevelPrecache, w[1]);
        Assert.Equal(1, w[2]);									// the fast sprite is no object
        Assert.Equal([1, 0, Bits(1), Bits(2), Bits(3), 0, 0, 0], w[3..11]);	// model 1, origin, angles
        Assert.Equal(2, w[11]);
        Assert.Equal([4, 0, 1, 0, 9, 1, 0, 1], w[12..20]);		// leaf 4: one object; leaf 9: fast sprites
        Assert.Equal(20, w.Length);

        // A model above 4 GiB keeps its high word.
        var high = Host(Models(Lump.Model(new Vector(1, 2, 3), 4)), e => e.ModelBase = 0x7_0000_0000);
        high.LevelInitPreEntity(256, 256, false);
        Assert.Equal([1, 7], high.Words.ToArray()[3..5]);
    }

    [Fact]
    public void AViewsAnswerSaysWhetherItBuiltAndThenItsProxiesAndModelStates()
    {
        var host = Host(Models(Lump.Model(new Vector(100, 0, 0), 0, model: 1, orientation: 1)));
        host.LevelInitPreEntity(256, 256, false);
        host.BuildLists(default, default, true, 1, 0);
        int[] w = host.Words.ToArray();
        Assert.Equal(w.Length, w[0]);
        Assert.Equal([1, Bits(1200)], w[1..3]);
        Assert.Equal([2, 3, 0, 2, 0], w[3..8]);					// proxies, last first, two words each
        Assert.Equal(1, w[8]);
        Assert.Equal(255 | DetailPropsWire.ModelSetAngles, w[9]);
        Assert.Equal(16, w.Length);								// angles and colour, three words each

        host.BuildLists(default, default, true, 0, 0);
        Assert.Equal([5, 0, 0, 0, 0], host.Words.ToArray());
    }

    [Fact]
    public void APreparedDrawsAnswerIsItsMaterialThenTheSortsModelAlphas()
    {
        var lump = Models(Lump.Model(new Vector(900, 0, 0), 0));
        lump.Sprites.Add([-8, 16, 8, 0, 0, 0, 0.5f, 0.25f]);
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        var host = Host(lump);
        host.LevelInitPreEntity(256, 256, false);
        host.BeginTranslucent();
        host.Prepare((int)DetailDrawKind.OldInLeaf, ViewDownX, [0], null, new DetailSwitches(1, true, 0));
        int[] w = host.Words.ToArray();
        Assert.Equal(w.Length, w[0]);
        Assert.Equal((int)DetailDrawMaterial.Wireframe, w[1]);
        Assert.Equal(1, w[2]);
        Assert.Equal(0, w[3]);									// the model, object 0
        Assert.Equal(5, w.Length);
    }

    [Fact]
    public void AnEmitsAnswerIsItsBeginsVerticesAndDrawsUntilEnd()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        lump.Objects.Add(Lump.Sprite(new Vector(101, 0, 0), 0, orientation: 0));
        var host = Host(lump);
        host.LevelInitPreEntity(256, 256, false);
        host.BeginTranslucent();
        host.Prepare((int)DetailDrawKind.OldList, ViewDownX, [0], null, new DetailSwitches(1, false, 0));
        host.Emit(1);
        int[] w = host.Words.ToArray();
        Assert.Equal(w.Length, w[0]);
        int at = 1;
        Assert.Equal([DetailPropsWire.Begin, 1, DetailPropsWire.Vertices, 4], w[at..(at + 4)]);
        at += 4 + 4 * DetailPropsWire.VertexWords;
        Assert.Equal([DetailPropsWire.EndDraw, DetailPropsWire.Begin, 1, DetailPropsWire.Vertices, 4], w[at..(at + 5)]);
        at += 5 + 4 * DetailPropsWire.VertexWords;
        Assert.Equal([DetailPropsWire.EndDraw, DetailPropsWire.End], w[at..]);
        // A vertex's colour is one word, red lowest.
        int rgba = w[1 + 4 + 3];
        Assert.Equal(255, (rgba >> 24) & 0xff);
    }

    // -------------------------------------------------------------------------
    // A managed reload (cs_cl_reload): the new build's host carries on
    // -------------------------------------------------------------------------

    [Fact]
    public void AReloadedHostDrawsWhatTheOldOneWouldHaveAndKeepsThePlayersFades()
    {
        var lump = OneSpriteSheet();
        lump.Objects.Add(Lump.Sprite(new Vector(100, 0, 0), 0, orientation: 0));
        var engine = new Engine { DetailDist = 900, DetailFade = 300, Multiplier = 3 };
        engine.Lumps[DetailGameLumps.DetailProps] = (4, lump.Bytes());
        DetailPropsHost Fresh() => new(new DetailPropSystem(engine, engine));
        int[] Draw(DetailPropsHost h)
        {
            h.BuildLists(default, default, true, 1, 0);
            h.BeginTranslucent();
            h.Prepare((int)DetailDrawKind.OldList, ViewDownX, [0], null, new DetailSwitches(1, false, 0));
            h.Emit(64);
            return h.Words.ToArray();
        }

        var old = Fresh();
        old.Init();
        old.System.SeedRandom(12345);				// an earlier level drew from the stream
        old.LevelInitPreEntity(256, 256, false);
        engine.DetailDist = 1000;					// the map moved the player's fades mid-level
        engine.DetailFade = 500;
        var writer = new ReloadWriter();
        ((IReloadParticipant)old).Save(writer);
        int[] expected = Draw(old);
        Assert.Contains(DetailPropsWire.Vertices, expected);

        var reloaded = Fresh();						// the new build: its own defaults would be 1000/500
        var unrestored = Fresh();
        var reader = new ReloadReader(writer.ToArray());
        ((IReloadParticipant)reloaded).Restore(reader);
        Assert.True(reader.AtEnd);
        Assert.Equal(expected, Draw(reloaded));

        // The control: a host that was not handed the old one's state parses
        // other copies and gives the map's fades back as the player's.
        unrestored.LevelInitPreEntity(256, 256, false);
        Assert.NotEqual(expected, Draw(unrestored));
        unrestored.LevelInitPostEntity(null);
        Assert.Equal(1000, engine.DetailDist);

        reloaded.LevelInitPostEntity(null);
        Assert.Equal(900, engine.DetailDist);
        Assert.Equal(300, engine.DetailFade);
    }
}
