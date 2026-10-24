using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="PostProcessPlanner"/> against stock's post-processing, over the
/// frozen stock frames (Fixtures/post-stock, README there): the program made
/// from a frame's convars and engine answers, read by name, must make that
/// frame's calls between its last SoundExtraUpdate and GrabPreColorCorrectedFrame
/// (DoEnginePostProcessing's place), and its motion blur draw. What one frame
/// cannot reproduce -- the tonemap goal's value, which is ten frames' history,
/// the histogram display's bars, the moving bloom debug box -- is held as a
/// pattern; the values themselves are TonemapTests' and MotionBlurTests'.
/// </summary>
public class PostProcessPlannerTests
{
    public static TheoryData<string> Frames()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(Dir(), "*.txt.gz").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path)[..^".txt.gz".Length]);
        return data;
    }

    private static string Dir() => RepoLayout.Repo("src", "test", "client", "Fixtures", "post-stock");

    private static (RenderTrace Trace, string Text) Load(string name)
    {
        string path = Path.Combine(Dir(), name + ".txt.gz");
        Assert.True(File.Exists(path), "the frame is missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        string text = reader.ReadToEnd();
        return (RenderTrace.Parse(text), text);
    }

    private static int[] Ints(string s) => s.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    private static float Num(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    /// <summary>A frame's post inputs, by name. Render targets are the screen's size (1280x720), the quarter targets a quarter.</summary>
    internal static PostProcessInputs InputsOf(RenderTrace t, string text, PostProgramKind kind)
    {
        string frame = text.Split('\n').First(l => l.StartsWith("frame\t", StringComparison.Ordinal));
        int[] rect = Ints(frame[(frame.IndexOf("rect=", StringComparison.Ordinal) + 5)..].Trim());
        int[] viewport = Ints(t.Environment["viewport"]);
        string[] custom = t.Environment["customtonemap"].Split(',');
        var i = new PostProcessInputs
        {
            Kind = (int)kind,
            X = rect[0], Y = rect[1], Width = rect[2], Height = rect[3],
            HdrType = int.Parse(t.Environment["hdrtype"], CultureInfo.InvariantCulture),
            EngineDxLevel = int.Parse(t.Environment["dxlevel"], CultureInfo.InvariantCulture),
            HardwareDxLevel = int.Parse(t.Environment["hwdxlevel"], CultureInfo.InvariantCulture),
            MapHasHdrLighting = int.Parse(t.Environment["maphashdrlighting"], CultureInfo.InvariantCulture),
            CanDoSrgbReadFromRts = int.Parse(t.Environment["candosrgbreadfromrts"], CultureInfo.InvariantCulture),
            FakeSrgbWrite = int.Parse(t.Environment["fakesrgbwrite"], CultureInfo.InvariantCulture),
            SupportsPixelShaders20 = int.Parse(t.Environment["supportsps20"], CultureInfo.InvariantCulture),
            ColorCorrectionActive = 0, // no colour-correction entity in either map
            FullFrameWidth = rect[2], FullFrameHeight = rect[3],
            SmallFB1Width = rect[2] / 4, SmallFB1Height = rect[3] / 4,
            ViewportX = viewport[0], ViewportY = viewport[1], ViewportWidth = viewport[2], ViewportHeight = viewport[3],
            BackBufferWidth = rect[2], BackBufferHeight = rect[3],
            UseCustomAutoExposureMin = int.Parse(custom[0], CultureInfo.InvariantCulture),
            UseCustomAutoExposureMax = int.Parse(custom[1], CultureInfo.InvariantCulture),
            UseCustomBloomScale = int.Parse(custom[2], CultureInfo.InvariantCulture),
            CustomAutoExposureMin = Num(custom[3]), CustomAutoExposureMax = Num(custom[4]), CustomBloomScale = Num(custom[5]),
            RealTime = Num(t.Environment["realtime"]), CurTime = Num(t.Environment["curtime"]), FrameTime = Num(t.Environment["frametime"]),
            ToneScaleLinear = Num(t.Environment["tonemapscalelinear"].Split(',')[0]),
        };
        for (int b = 0; b < 32; b++)
            i.SetReadback(b, -1);
        for (var c = (PostCvar)0; c < PostCvar.Count; c++)
        {
            Assert.True(t.ConVars.TryGetValue(c.ToString(), out string? value), $"the frame has no convar {c}");
            i.SetCvar(c, Num(value!));
        }
        return i;
    }

    // Material ids for the facts: the order a program asks.
    private sealed class Materials
    {
        private readonly List<string> _names = [""];

        public int Of(string name)
        {
            int at = _names.IndexOf(name);
            if (at > 0)
                return at;
            _names.Add(name);
            return _names.Count - 1;
        }

        public string Name(int id) => id > 0 && id < _names.Count ? _names[id] : "<none>";
    }

    private static (RenderOp[] Ops, Materials Names, PostProgramEffects Effects) Program(in PostProcessInputs input, int warmFrames,
                                                                                    PostProcessPlanner? planner = null)
    {
        var names = new Materials();
        planner ??= new PostProcessPlanner(() => "mods/test", new ListHooks<IPostProcessContributor>());
        var builder = new RenderPlanBuilder();
        var effects = new PostProgramEffects();
        var writer = new PostOpsWriter(builder, names.Of);
        for (int f = 0; f <= warmFrames; f++)
        {
            builder.Clear();
            effects.Clear();
            planner.Plan(input, writer, effects);
        }
        return (builder.ToArray(), names, effects);
    }

    // DoEnginePostProcessing's calls in a recorded frame.
    private static List<string> PostSegment(RenderTrace t)
    {
        int grab = t.Calls.ToList().FindIndex(c => c.StartsWith("engine.GrabPreColorCorrectedFrame\t", StringComparison.Ordinal));
        Assert.True(grab > 0, "the frame has no GrabPreColorCorrectedFrame");
        int sound = t.Calls.Take(grab).ToList().FindLastIndex(c => c == "engine.Sound_ExtraUpdate");
        Assert.True(sound >= 0, "no SoundExtraUpdate before the post passes");
        return t.Calls.Skip(sound + 1).Take(grab - sound - 1).ToList();
    }

    // The ops one frame cannot reproduce: the goal is ten frames' history, the
    // histogram's bars its read-backs, the debug box moves a pixel a frame.
    private static Func<RenderOp, bool> Unknowable(in PostProcessInputs input)
    {
        bool histogram = input.Cvar(PostCvar.mat_show_histogram) != 0f;
        bool debugBloom = (int)input.Cvar(PostCvar.mat_debug_bloom) == 1;
        return op => (RenderOpKind)op.Kind switch
        {
            RenderOpKind.PostGoalToneScale => true,
            RenderOpKind.PostViewport or RenderOpKind.PostClearColor3ub => histogram || debugBloom,
            _ => false,
        };
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void TheProgramFromAFramesInputsMakesItsPostProcessingCalls(string name)
    {
        (RenderTrace t, string text) = Load(name);
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        (RenderOp[] ops, Materials names, _) = Program(input, warmFrames: 12);
        var expected = PostPlanText.Expected(ops, names.Name, input.SmallFB1Width, input.SmallFB1Height, Unknowable(input));
        // Stock's pyro vision draws after the core's passes; it is the TF2 client
        // module's (user ruling 2026-10-12), held by that module's facts.
        if (input.Cvar(PostCvar.localplayer_visionflags) != 0f)
            expected.Add(RenderPlanText.Expect.AnyLines);
        List<string> calls = PostSegment(t);
        bool ok = RenderPlanText.Matches(expected, calls, out int furthest);
        Assert.True(ok, $"{name}: the program parts from stock after item {furthest} of {expected.Count}: expected " +
                        $"'{(furthest < expected.Count ? expected[furthest].ToString() : "<end>")}', stock's calls:\n  " +
                        string.Join("\n  ", calls.Take(40)));
    }

    [Fact]
    public void TheSandboxFrameDrawsTheBloomChainAndOneHistogramQuery()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        (RenderOp[] ops, Materials names, _) = Program(input, warmFrames: 12);
        string[] drawn = ops.Where(o => o.Kind == (int)RenderOpKind.PostDrawRect).Select(o => names.Name(o.GetI(0))).ToArray();
        Assert.Equal(["dev/lumcompare", "dev/downsample_non_hdr", "dev/blurfilterx_nohdr", "dev/blurfiltery_nohdr", "dev/bloomadd"], drawn);
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.PostQueryBegin);
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.PostQueryEnd);
    }

    [Fact]
    public void TheBloomAmountIsTheOneTheBlurBound()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        (RenderOp[] ops, _, _) = Program(input, warmFrames: 0);
        RenderOp set = Assert.Single(ops, o => o.Kind == (int)RenderOpKind.PostMaterialVarFloat && o.GetI(1) == (int)PostVar.BloomAmount);
        string bound = t.Queries.Select(q => q.Text).Single(q => q.StartsWith("material.var\tdev/blurfiltery_nohdr\t$bloomamount\t", StringComparison.Ordinal));
        Assert.Equal("f " + RenderPlanText.G9(set.GetF(0)), bound[(bound.LastIndexOf('\t') + 1)..]);
    }

    [Fact]
    public void LockdownDrawsNoPostPassUnlessBloomIsForced()
    {
        (RenderTrace t, string text) = Load("post-lockdown-default");
        (RenderOp[] ops, _, _) = Program(InputsOf(t, text, PostProgramKind.EnginePost), warmFrames: 0);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.PostDrawRect);
        (t, text) = Load("post-lockdown-forcebloom");
        (ops, _, _) = Program(InputsOf(t, text, PostProgramKind.EnginePost), warmFrames: 0);
        Assert.Equal(4, ops.Count(o => o.Kind == (int)RenderOpKind.PostDrawRect));
    }

    [Theory]
    [InlineData("post-sandbox-motionblur")]
    [InlineData("post-lockdown-motionblur")]
    public void TheMotionBlurProgramMakesStocksDraw(string name)
    {
        (RenderTrace t, string text) = Load(name);
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.MotionBlur);
        (RenderOp[] ops, Materials names, _) = Program(input, warmFrames: 0);
        var expected = PostPlanText.Expected(ops, names.Name, input.SmallFB1Width, input.SmallFB1Height);
        Assert.Equal(3, expected.Count);
        List<string> calls = t.Calls.ToList();
        int draw = calls.FindIndex(c => c.StartsWith("ctx.DrawScreenSpaceRectangle\tmaterial=dev/motion_blur ", StringComparison.Ordinal));
        Assert.True(draw >= 2, $"{name}: no motion blur draw");
        Assert.True(RenderPlanText.Matches(expected, calls.Skip(draw - 2).Take(3).ToList(), out int furthest),
                    $"{name}: part from stock at {furthest}: {string.Join(" | ", calls.Skip(draw - 2).Take(3))}");
    }

    [Fact]
    public void MotionBlurOffOrOnOldHardwareDrawsNothingAndKeepsItsValues()
    {
        (RenderTrace t, string text) = Load("post-sandbox-motionblur");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.MotionBlur);
        input.SetCvar(PostCvar.mat_motion_blur_enabled, 0f);
        (RenderOp[] ops, _, _) = Program(input, warmFrames: 0);
        Assert.Empty(ops);
        input.SetCvar(PostCvar.mat_motion_blur_enabled, 1f);
        input.HardwareDxLevel = 80;
        (ops, _, _) = Program(input, warmFrames: 0);
        Assert.Empty(ops);
    }

    // Arms the frozen frames never take: colour correction, the separate passes
    // with it, the centre quarter, the dumps and the first-frame guard.
    [Fact]
    public void ColourCorrectionDrawsEnginePostWithItsUvTransformAfterTheFirstFrame()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        input.ColorCorrectionActive = 1;
        input.SetCvar(PostCvar.mat_colorcorrection, 1f); // 0 in the recorded config
        var planner = new PostProcessPlanner(() => "mods/test", new ListHooks<IPostProcessContributor>());
        (RenderOp[] first, Materials names, _) = Program(input, warmFrames: 0, planner);
        Assert.DoesNotContain(first, o => o.Kind == (int)RenderOpKind.PostDrawRect && names.Name(o.GetI(0)) == "dev/engine_post");
        (RenderOp[] second, names, _) = Program(input, warmFrames: 0, planner);
        Assert.Contains(second, o => o.Kind == (int)RenderOpKind.PostDrawRect && names.Name(o.GetI(0)) == "dev/engine_post");
        EnginePostValues v = planner.EnginePost;
        Assert.Equal(1, v.BloomEnable);
        Assert.Equal(0f, v.Aa0);
        // Bloom texel (0,-0.5)..(319,179) onto frame buffer (0,0)..(1279,719), 1280x720.
        Assert.Equal((0.5f / 1280f) - (0.5f / 320f) + (0.5f / 320f) * (1 - (1279f / 1280f) / (319f / 320f)), v.Uv0, 5);
        Assert.Equal((1279f / 1280f) / (319f / 320f), v.Uv2, 5);
        Assert.Contains(second, o => o.Kind == (int)RenderOpKind.PostEnableColorCorrection && o.GetI(0) == 1);
    }

    [Fact]
    public void TheCentreQuarterModeShrinksTheDestinationAndItsUvs()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        input.SetCvar(PostCvar.mat_debug_postprocessing_effects, 2f);
        input.SetCvar(PostCvar.mat_postprocessing_combine, 0f);
        (RenderOp[] ops, Materials names, _) = Program(input, warmFrames: 0);
        RenderOp add = Assert.Single(ops, o => o.Kind == (int)RenderOpKind.PostDrawRect && names.Name(o.GetI(0)) == "dev/bloomadd");
        Assert.Equal([320, 180, 640, 360], new[] { add.GetI(1), add.GetI(2), add.GetI(3), add.GetI(4) });
        Assert.True(add.GetF(2) < 319f && add.GetF(0) > 0f, "the source corners moved in");
    }

    [Fact]
    public void ADumpFilesEachPassOnceAndClearsItsConvar()
    {
        (RenderTrace t, string text) = Load("post-lockdown-forcebloom");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        input.SetCvar(PostCvar.mat_dump_rts, 1f);
        Assert.Equal(-1f, input.Cvar(PostCvar.mat_queue_mode)); // the recorded config: stock refuses to dump
        input.SetCvar(PostCvar.mat_queue_mode, 0f);
        (RenderOp[] ops, _, PostProgramEffects effects) = Program(input, warmFrames: 0);
        int[] names = ops.Where(o => o.Kind == (int)RenderOpKind.PostDumpTga).Select(o => o.GetI(2)).ToArray();
        Assert.Equal([(int)PostDump.BackBuffer, (int)PostDump.QuarterSizeFB, (int)PostDump.BlurX, (int)PostDump.BlurYAndBloom, (int)PostDump.EnginePost], names);
        Assert.Equal([0, 1, 2, 3, 4], ops.Where(o => o.Kind == (int)RenderOpKind.PostDumpTga).Select(o => o.GetI(3)));
        Assert.Contains(("mat_dump_rts", "0"), effects.CvarWrites);
        input.SetCvar(PostCvar.mat_queue_mode, 2f);
        (ops, _, effects) = Program(input, warmFrames: 0);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.PostDumpTga);
        Assert.NotEmpty(effects.DevMessages);
    }

    [Fact]
    public void AnUnsetSoftwareAaStrengthIsWrittenZeroOnce()
    {
        (RenderTrace t, string text) = Load("post-lockdown-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        input.SetCvar(PostCvar.mat_software_aa_strength, -1f);
        input.SetCvar(PostCvar.mat_software_aa_strength_vgui, -1f);
        (_, _, PostProgramEffects effects) = Program(input, warmFrames: 0);
        Assert.Contains(("mat_software_aa_strength", "0.000000"), effects.CvarWrites);
        Assert.Contains(("mat_software_aa_strength_vgui", "1.000000"), effects.CvarWrites);
    }

    [Fact]
    public void TheStencilPathCountsTheMarkedPixelsInsideTheQuery()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        input.SetCvar(PostCvar.mat_tonemapping_occlusion_use_stencil, 1f);
        (RenderOp[] ops, Materials names, _) = Program(input, warmFrames: 0);
        var kinds = ops.Select(o => (RenderOpKind)o.Kind).ToList();
        int begin = kinds.IndexOf(RenderOpKind.PostQueryBegin);
        Assert.True(begin > 0 && kinds[begin - 2] == RenderOpKind.PostStencil && kinds[begin - 1] == RenderOpKind.PostDrawRect);
        Assert.Equal([RenderOpKind.PostStencil, RenderOpKind.PostDrawRect, RenderOpKind.PostStencil, RenderOpKind.PostQueryEnd], kinds.Skip(begin + 1).Take(4));
        Assert.Equal("dev/no_pixel_write", names.Name(ops[begin + 2].GetI(0)));
        Assert.Equal([1, 2, 0], ops.Where(o => o.Kind == (int)RenderOpKind.PostStencil).Select(o => o.GetI(0)));
    }

    [Fact]
    public void TheReadbackMaskNamesTheQueriesTwoFramesOld()
    {
        (RenderTrace t, string text) = Load("post-sandbox-default");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        var planner = new PostProcessPlanner(() => "mods/test", new ListHooks<IPostProcessContributor>());
        int Mask() => Program(input, warmFrames: 0, planner).Ops.Single(o => o.Kind == (int)RenderOpKind.PostReadbackNext).GetI(0);
        Assert.Equal(0, Mask());      // frame 1 issued bucket 0
        Assert.Equal(0, Mask());      // frame 2 issued bucket 1
        Assert.Equal(1, Mask());      // frame 3: bucket 0 is read at frame 4
        input.SetReadback(0, 5000);
        Assert.Equal(1 << 1, Mask()); // frame 4 reads bucket 0; bucket 1 is due at frame 5
        Assert.Equal(HistogramState.QueryDone, planner.Histogram.Buckets[0].State);
    }

    // The core keeps no mod's pass: with no contributor, nothing draws at the vision point.
    [Fact]
    public void WithNoContributorTheVisionPointDrawsNothingAndAContributorDrawsThere()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        (RenderOp[] plain, _, _) = Program(input, warmFrames: 0);
        Assert.Equal(5, plain.Count(o => o.Kind == (int)RenderOpKind.PostDrawRect));

        var contributor = new TestContributor();
        var planner = new PostProcessPlanner(() => "mods/test", new FixedHooks<IPostProcessContributor>(contributor));
        (RenderOp[] with, Materials names, _) = Program(input, warmFrames: 0, planner);
        Assert.Equal(1, contributor.Calls);
        Assert.True(contributor.SawFrameBufferUpdated);
        RenderOp last = with.Last(o => o.Kind == (int)RenderOpKind.PostDrawRect);
        Assert.Equal("dev/test_vision", names.Name(last.GetI(0)));
    }

    // DisplayHistogram over a full histogram: every bucket read back at 1000
    // pixels draws a red bar of the full 200, from 10 pixels in from the right.
    [Fact]
    public void AFullHistogramDrawsRedBarsEndingTenPixelsFromTheRight()
    {
        (RenderTrace t, string text) = Load("post-sandbox-histogram");
        PostProcessInputs input = InputsOf(t, text, PostProgramKind.EnginePost);
        for (int b = 0; b < 32; b++)
            input.SetReadback(b, 1000);
        var planner = new PostProcessPlanner(() => "mods/test", new ListHooks<IPostProcessContributor>());
        RenderOp[] ops = [];
        for (int f = 0; f < 40; f++)
            ops = Program(input, warmFrames: 0, planner).Ops;
        int push = Array.FindLastIndex(ops, o => o.Kind == (int)RenderOpKind.PostPushRenderTargetAndViewport);
        Assert.True(push > 0, "no histogram display");
        RenderOp[] display = ops[(push + 1)..];
        // Stock's bar widths, MAX( 1, 500 * ( max - min ) ), over the new algorithm's 16 shown buckets.
        int total = 0;
        for (int b = 0; b < 16; b++)
        {
            float lo = b == 0 ? 0f : MathF.Pow(b / 16f, 1.5f), hi = MathF.Pow((b + 1) / 16f, 1.5f);
            float span = 500 * (hi - lo);
            total += (int)(1 > span ? 1 : span) + 2;
        }
        RenderOp firstColour = display.First(o => o.Kind == (int)RenderOpKind.PostClearColor3ub);
        RenderOp firstBar = display.First(o => o.Kind == (int)RenderOpKind.PostViewport);
        Assert.Equal([255, 0, 0], new[] { firstColour.GetI(0), firstColour.GetI(1), firstColour.GetI(2) });
        Assert.Equal(input.ViewportWidth - total - 10, firstBar.GetI(0));
        Assert.Equal([4, 200], new[] { firstBar.GetI(1), firstBar.GetI(3) });
    }

    // Pyro vision is the TF2 client library's (user ruling 2026-10-12): the core
    // keeps no pyro type and names none of its materials.
    [Fact]
    public void TheCoreClientHasNoPyroCode()
    {
        Assert.DoesNotContain(typeof(PostProcessPlanner).Assembly.GetTypes(),
                              t => t.Name.Contains("Pyro", StringComparison.OrdinalIgnoreCase));
        string core = RepoLayout.Repo("src", "client", "managed");
        Assert.True(Directory.Exists(core), "the core client's source is missing");
        var naming = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                              .Where(f => File.ReadAllText(f).Contains("\"dev/pyro", StringComparison.Ordinal)).ToList();
        Assert.Empty(naming);
    }

    private sealed class TestContributor : IPostProcessContributor
    {
        public int Calls;
        public bool SawFrameBufferUpdated;

        public void AtVisionPoint(PostVisionContext context, IPostOps ops)
        {
            Calls++;
            SawFrameBufferUpdated = context.FrameBufferUpdated;
            ops.DrawRect(ops.Material("dev/test_vision"), context.DestX, context.DestY, context.DestWidth, context.DestHeight,
                         context.SrcX0, context.SrcY0, context.SrcX1, context.SrcY1, context.SmallWidth, context.SmallHeight, true);
        }
    }

    private sealed class FixedHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;

        public void Each(string what, Action<T> call)
        {
            foreach (T h in hooks)
                call(h);
        }
    }
}
