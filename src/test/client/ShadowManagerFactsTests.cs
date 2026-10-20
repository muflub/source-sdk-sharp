using System.Globalization;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.Shadows;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shadow manager's families where the golden is thin (S1 of
/// docs/plans/c7_shadow_manager.md): the flashlight target's tail (which no
/// caller can reach, ported from the pristine file), the walk's end at a
/// retired shadow on an input built for it, the reload's carry of a
/// flashlight's shadow (docs/todo.md, the flashlight's reload), the atlas's
/// fragment order and exhaustion, the fork's report, the layouts and the
/// mathlib factors. Each runs the managed manager over the oracle replay's
/// stand-ins (<see cref="ShadowManagerOracleTests.Replay"/>), which answer as
/// the golden's README says.
/// </summary>
public class ShadowManagerFactsTests
{
    private static string X(float f) => ShadowManagerOracleTests.X(f);

    private static string V(float x, float y, float z) => X(x) + "," + X(y) + "," + X(z);

    private const string Zero3 = "00000000,00000000,00000000";

    /// <summary>A T row: a box of half size h at an origin, no rotation, with the given model type and receive flags.</summary>
    private static string T(int id, int modelType, float x, float y, float z, float h, int receive = 0)
    {
        string transform = string.Join(',', new[] { 1f, 0f, 0f, x, 0f, 1f, 0f, y, 0f, 0f, 1f, z }.Select(X));
        string mins = V(-h, -h, -h), maxs = V(h, h, h);
        return string.Join('\t', "T", id, 1, modelType, V(x, y, z), Zero3, mins, maxs, mins, maxs, V(x - h, y - h, z - h), V(x + h, y + h, z + h), transform,
                           0, X(0), 0, Zero3, -1, -1, -1, receive, 65535, 0, 65535, 0, 0, 1, 0, 0);
    }

    private static readonly string[] World =
    [
        "ENV\tleaves\t40\t" + X(100f),
        "ENV\tviewbox\t" + V(-100, -1000, -500) + "\t" + V(3000, 1000, 500),
        "ENV\tambient\t" + V(0.1f, 0.2f, 0.5f),
        "ENV\tppu\t" + X(4f),
        "ENV\tdxlevel\t90",
        "ENV\tsupportsdepth\t1",
        "ENV\tmaxrendertarget\t2048",
        "ENV\tfog\t1",
        "ENV\theightclip\t1",
        "ENV\ttools\t0",
        "F\t1\t" + X(0.015f),
        "C\tInit",
        "C\tLevelInitPreEntity",
    ];

    // The golden's spotlight state, as its C rows spell one: origin, then the rest.
    private static string Light(float x, float y, float z, int shadows)
    {
        // pointing straight down (pitch 90), 60 by 50, near 4 far 300
        float q = MathF.Sqrt(0.5f);
        return string.Join('\t', V(x, y, z), X(0) + "," + X(q) + "," + X(0) + "," + X(q), X(4f), X(300f), X(60f), X(50f),
                           X(0) + "," + X(100f) + "," + X(0), X(1f) + "," + X(0.9f) + "," + X(0.8f) + "," + X(0f), "effects/flashlight001",
                           shadows, 0, X(1024f), X(0.35f), X(16f), X(0.0005f));
    }

    private static ShadowManagerOracleTests.Replay Run(params string[] rows)
    {
        var replay = new ShadowManagerOracleTests.Replay();
        foreach (string row in World.Concat(rows))
            replay.Run(row);
        return replay;
    }

    private static int LastAnswer(ShadowManagerOracleTests.Replay replay) =>
        int.Parse(replay.Log.Last(l => l.StartsWith("R\t", StringComparison.Ordinal))[2..], CultureInfo.InvariantCulture);

    private static List<string> After(ShadowManagerOracleTests.Replay replay, string callRow)
    {
        int at = replay.Log.LastIndexOf(callRow);
        Assert.True(at >= 0, "no row " + callRow);
        return replay.Log.Skip(at + 1).TakeWhile(l => !l.StartsWith("C\t", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void AShadowRetiredUnderTheWalkEndsItAndTheOnesAfterItStayUnprojected()
    {
        ShadowManagerOracleTests.Replay replay = Run(T(1, 3, 150, 0, 40, 10), T(2, 3, 250, 0, 40, 10), T(3, 3, 350, 0, 40, 10));
        for (int id = 1; id <= 3; id++)
        {
            replay.Run($"C\tCreateShadow\t{id}\t2");
            replay.ThingById(id).ShadowHandle = LastAnswer(replay);
        }
        replay.Run("C\tPreRender");
        Assert.Equal(3, After(replay, "C\tPreRender").Count(l => l.StartsWith("E\tsm.ProjectShadow", StringComparison.Ordinal)));

        // Thing 2's entity goes; 2 and 3 are dirtied; the walk retires 2 and stops.
        replay.ThingById(2).Gone = true;
        replay.Run("C\tAddToDirtyShadowListHandle\t1\t1");
        replay.Run("C\tAddToDirtyShadowList\t3\t1");
        replay.Run("C\tPreRender");
        List<string> walk = After(replay, "C\tPreRender");
        Assert.Contains("E\tsm.DestroyShadow\t1", walk);
        Assert.DoesNotContain("E\tsm.EnableShadow\t2\t1", walk);
        Assert.True(replay.ThingById(3).Dirty, "the unvisited shadow's renderable is still marked dirty");

        // And its renderable is refused from then on: still dirty, so not dirtied again.
        replay.Run("C\tAddToDirtyShadowList\t3\t0");
        Assert.Empty(After(replay, "C\tAddToDirtyShadowList\t3\t0"));
        replay.Run("C\tPreRender");
        Assert.DoesNotContain(After(replay, "C\tPreRender"), l => l.StartsWith("E\tsm.EnableShadow\t2", StringComparison.Ordinal));
    }

    [Fact]
    public void AFlashlightAimedAtATargetLightsTheTargetAndItsMoveChildrenAndNothingElse()
    {
        ShadowManagerOracleTests.Replay replay = Run(T(5, 3, 2000, 0, 0, 10, receive: 3), T(6, 1, 2010, 0, 0, 5, receive: 3), T(7, 3, 1990, 0, 0, 10, receive: 3),
                                                     "RCV\t20\t7\t2");
        replay.MoveFirstChild[5] = 6;
        replay.Run("C\tCreateFlashlight\t" + Light(2000, 0, 100, 0));
        int f = LastAnswer(replay);
        // Untargeted, the leaf system offers it to the receiver in leaf 20.
        Assert.Contains(replay.Log, l => l.StartsWith("E\tleaf.ProjectFlashlight", StringComparison.Ordinal));

        replay.Run($"C\tSetFlashlightTarget\t{f}\t5");
        replay.Run($"C\tUpdateProjectedTexture\t{f}\t1");
        List<string> rows = After(replay, $"C\tUpdateProjectedTexture\t{f}\t1");
        Assert.DoesNotContain(rows, l => l.StartsWith("E\tleaf.ProjectFlashlight", StringComparison.Ordinal));
        string[] effects = rows.Where(l => l.StartsWith("E\t", StringComparison.Ordinal)).Select(l => l.Split('\t')[1] + " " + l.Split('\t').Last()).ToArray();
        // The child (a brush) first, then the target (a studio model).
        Assert.Equal(["sm.EnableShadow 1", "sm.ProjectFlashlight " + rows.First(l => l.StartsWith("E\tsm.ProjectFlashlight", StringComparison.Ordinal)).Split('\t').Last(),
                      "sm.AddShadowToBrushModel " + rows.First(l => l.StartsWith("E\tsm.AddShadowToBrushModel", StringComparison.Ordinal)).Split('\t').Last(),
                      "sm.AddFlashlightRenderable r6", "r.CreateModelInstance r5", "sm.AddShadowToModel 105", "sm.AddFlashlightRenderable r5"],
                     effects);

        // A receiver that is not the target (nor a move child of it) is refused.
        replay.Run($"C\tAddShadowToReceiver\t{f}\t7\t2");
        Assert.DoesNotContain(After(replay, $"C\tAddShadowToReceiver\t{f}\t7\t2"), l => l.StartsWith("E\t", StringComparison.Ordinal));
        replay.Run($"C\tAddShadowToReceiver\t{f}\t5\t2");
        Assert.Contains("E\tsm.AddFlashlightRenderable\t0\tr5", After(replay, $"C\tAddShadowToReceiver\t{f}\t5\t2"));
    }

    [Fact]
    public void AHotReloadCarriesAFlashlightsShadow()
    {
        ShadowManagerOracleTests.Replay replay = Run();
        replay.Run("C\tCreateFlashlight\t" + Light(2000, 0, 100, 0));
        int f = LastAnswer(replay);
        string created = replay.Log.Last(l => l.StartsWith("E\tsm.CreateShadowEx", StringComparison.Ordinal));
        string engine = created.Split('\t').Last()[2..];

        Assert.True(replay.Reload() > 0);

        Assert.True(replay.Manager.IsShadow(f), "the reloaded manager has forgotten the flashlight");
        replay.Run($"C\tUpdateFlashlightState\t{f}\t" + Light(2050, 0, 100, 0));
        replay.Run($"C\tUpdateProjectedTexture\t{f}\t1");
        Assert.Contains(After(replay, $"C\tUpdateProjectedTexture\t{f}\t1"), l => l.StartsWith("E\tsm.ProjectFlashlight\t" + engine + "\t", StringComparison.Ordinal));
        replay.Run($"C\tDestroyFlashlight\t{f}");
        Assert.Contains("E\tsm.DestroyShadow\t" + engine, After(replay, $"C\tDestroyFlashlight\t{f}"));
        Assert.False(replay.Manager.IsShadow(f));
    }

    [Fact]
    public void TheAtlasHandsOutStocksFragmentsLeastRecentlyUsedFirst()
    {
        var replay = new ShadowManagerOracleTests.Replay();
        var allocator = new ShadowTextureAllocator(replay);
        allocator.Init();
        allocator.Reset();
        var rects = new List<string>();
        var textures = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            int t = allocator.AllocateTexture(256, 256);
            textures.Add(t);
            Assert.True(allocator.UseTexture(t, true, 1e9f));
            allocator.GetTextureRect(t, out int x, out int y, out int w, out _);
            rects.Add($"{x},{y},{w}");
        }
        // The six 256 blocks from the last one back (each block's fragment 0 at its list's head), then a 128.
        Assert.Equal(["768,768,256", "512,768,256", "256,768,256", "0,768,256", "768,512,256", "512,512,256", "256,512,128"], rects);

        // Next frame, a freed fragment goes to its list's head and is the next given.
        allocator.AdvanceFrame();
        allocator.DeallocateTexture(textures[3]);
        int again = allocator.AllocateTexture(256, 256);
        Assert.True(allocator.UseTexture(again, true, 1e9f));
        allocator.GetTextureRect(again, out int ax, out int ay, out _, out _);
        Assert.Equal((0, 768), (ax, ay));
    }

    [Fact]
    public void TheSmallestFragmentsRunOutAt256AndTheNextTextureGetsNone()
    {
        var replay = new ShadowManagerOracleTests.Replay();
        var allocator = new ShadowTextureAllocator(replay);
        allocator.Init();
        allocator.Reset();
        for (int i = 0; i < 256; i++)
        {
            int t = allocator.AllocateTexture(16, 16);
            Assert.True(allocator.UseTexture(t, true, 1f));
        }
        int last = allocator.AllocateTexture(4, 4);
        Assert.False(allocator.UseTexture(last, true, 1f));
        Assert.False(allocator.HasValidTexture(last));
    }

    [Fact]
    public void TheReportCountsTheShadowsByKindAndTheLostRenderables()
    {
        ShadowManagerOracleTests.Replay replay = Run(T(1, 3, 150, 0, 40, 10), T(2, 3, 250, 0, 40, 10));
        replay.Run("C\tCreateShadow\t1\t2");
        replay.Run("C\tCreateShadow\t2\t6");
        replay.Run("C\tCreateFlashlight\t" + Light(2000, 0, 100, 1));
        replay.ThingById(2).Gone = true;
        int before = replay.Log.Count;
        replay.Manager.PrintShadowReport(false);
        string summary = replay.Log.Skip(before).Single(l => l.Contains("shadow report: held", StringComparison.Ordinal));
        Assert.Contains("held 3  rtt 0  simple 2  depth 1  none 0  lostRenderable 2", summary);
    }

    [Fact]
    public void TheEngineAnswersLayoutsAreTheOraclesMeasurement()
    {
        Dictionary<string, string[]> rows = ShadowManagerOracleTests.Golden().Where(l => l.StartsWith("L\t", StringComparison.Ordinal))
                                                                     .ToDictionary(l => l.Split('\t')[1], l => l.Split('\t'));
        Assert.Equal(3, rows.Count);

        string[] info = rows["ShadowInfo_t"];
        Assert.Equal(int.Parse(info[2], CultureInfo.InvariantCulture), Marshal.SizeOf<ShadowInfoRaw>());
        int[] infoOffsets = info[3].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        string[] infoFields = ["WorldToShadow", "FalloffOffset", "MaxDist", "FalloffAmount", "TexOriginX", "TexSizeX", "FalloffBias"];
        Assert.Equal(infoOffsets, infoFields.Select(f => (int)Marshal.OffsetOf<ShadowInfoRaw>(f)).ToArray());

        string[] plane = rows["cplane_t"];
        Assert.Equal(int.Parse(plane[2], CultureInfo.InvariantCulture), Marshal.SizeOf<PlaneRaw>());
        Assert.Equal(plane[3].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
                     new[] { "NormalX", "Dist", "Type", "SignBits" }.Select(f => (int)Marshal.OffsetOf<PlaneRaw>(f)).ToArray());

        string[] frustum = rows["Frustum_t"];
        Assert.Equal(int.Parse(frustum[2], CultureInfo.InvariantCulture), Marshal.SizeOf<FrustumRaw>());
        Assert.Equal(frustum[3].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
                     new[] { "Right", "AbsNormals" }.Select(f => (int)Marshal.OffsetOf<FrustumRaw>(f)).ToArray());
    }

    [Fact]
    public void TheDegreeFactorsAreMathlibsToTheBit()
    {
        // DEG2RAD's (float)(M_PI_F / 180.f) and RAD2DEG's (float)(180.f / M_PI_F),
        // the second a float quotient one ulp below (float)(180 / M_PI).
        Assert.Equal(0x3c8efa35, BitConverter.SingleToInt32Bits(ShadowMath.DegToRad));
        Assert.Equal(0x42652ee0, BitConverter.SingleToInt32Bits(ShadowMath.RadToDeg));
        Assert.NotEqual(BitConverter.SingleToInt32Bits((float)(180.0 / Math.PI)), BitConverter.SingleToInt32Bits(ShadowMath.RadToDeg));
    }

    [Fact]
    public void TheFlagWordsAgreeWithTheRenderPolicysShadowFlags()
    {
        Assert.Equal((int)ClientShadowFlag.Shadow, ShadowFlags.Shadow);
        Assert.Equal((int)ClientShadowFlag.UseRenderToTexture, ShadowFlags.UseRenderToTexture);
        Assert.Equal((int)ClientShadowFlag.AnimatingSource, ShadowFlags.AnimatingSource);
        Assert.Equal((int)ShadowCast.Simple, ShadowTypes.Simple);
        Assert.Equal((int)ShadowCast.RenderToTexture, ShadowTypes.RenderToTexture);
    }
}
