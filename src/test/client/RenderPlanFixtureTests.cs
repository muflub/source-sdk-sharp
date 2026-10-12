using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render plan against stock (plan_client_novalve C7 step 1): each
/// <c>Fixtures/render-stock/*.txt</c> is one frame stock's viewrender drew,
/// recorded call by call with the inputs RenderView had (README beside them).
/// The plan made from those inputs must say the same calls in the same order:
/// every view pushed with its whole CViewSetup, clear flags and render target,
/// every clip height, fog setting, world-list build and world draw with its
/// flags. Only the draws that depend on what is in view (entities, translucent
/// surfaces, the helpers' own meshes) are gaps, and lines whose arguments only
/// the engine knows (a fog colour read back) are patterns.
/// </summary>
public class RenderPlanFixtureTests
{
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        string dir = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock");
        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    private static RenderTrace Load(string name)
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock", name);
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        return RenderTrace.Parse(File.ReadAllText(path));
    }

    // The ViewSetupVisEx answer the frame got (a decision reads its radial bit).
    private static uint VisFlags(RenderTrace trace)
    {
        string? line = trace.Calls.FirstOrDefault(c => c.StartsWith("render.ViewSetupVisEx\t", StringComparison.Ordinal));
        Assert.NotNull(line);
        string hex = line![(line.LastIndexOf("flags=0x", StringComparison.Ordinal) + 8)..];
        return Convert.ToUInt32(hex, 16);
    }

    /// <summary>
    /// The frame's expected calls: Render's own SetMainView, the plan's
    /// RenderView, and Render's 2D UI paint after it (view.cpp, still stock).
    /// </summary>
    internal static List<RenderPlanText.Expect> FrameExpectation(RenderOp[] plan, in RenderInputs input, uint visFlags,
                                                                 IReadOnlyList<ViewSceneData>? scenes = null)
    {
        var e = new List<RenderPlanText.Expect> { RenderPlanText.Expect.Exact("render.SetMainView\t*") };
        e.AddRange(RenderPlanText.Expected(plan, input, visFlags, scenes));
        e.Add(RenderPlanText.Expect.Exact("render.Push2DView\tclear=0x0 rt=- xywh=*"));
        e.Add(RenderPlanText.Expect.Exact("render.VGui_Paint\tmode=0x5"));
        e.Add(RenderPlanText.Expect.Exact("render.PopView"));
        return e;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ThePlanFromAStockFramesInputsMakesThatFramesCalls(string name)
    {
        RenderTrace trace = Load(name);
        RenderInputs? input = trace.Inputs();
        Assert.True(input.HasValue, $"{name}: no inputs this build can read (re-record it: the RenderInputs struct changed?)");
        Assert.True(trace.Calls.Count > 50, $"{name}: {trace.Calls.Count} calls is not a frame");

        RenderOp[]? plan = RenderPlanner.Plan(input!.Value);
        Assert.NotNull(plan);

        var expected = FrameExpectation(plan!, input.Value, VisFlags(trace));
        bool ok = RenderPlanText.Matches(expected, trace.Calls, out int furthest);
        Assert.True(ok, $"{name}: the plan's calls part from stock's after expected item {furthest} of {expected.Count}: " +
                        $"next expected '{(furthest < expected.Count ? expected[furthest].ToString() : "<end>")}'");
    }

    // The ViewScene ops of the views that built a renderables list, in order:
    // each is one scene the recorder noted (the list is built only at r_drawentities 1).
    internal static List<int> EntitySceneFlags(RenderOp[] plan, in RenderInputs input)
    {
        var flags = new List<int>();
        var built = new Stack<bool>();
        bool entities = input.Cvar(RenderCvar.r_drawentities) != 0f;
        foreach (RenderOp op in plan)
        {
            switch ((RenderOpKind)op.Kind)
            {
                case RenderOpKind.WorldViewBegin: built.Push(false); break;
                case RenderOpKind.WorldViewEnd: if (built.Count > 0) built.Pop(); break;
                case RenderOpKind.BuildRenderables: if (built.Count > 0) { built.Pop(); built.Push(true); } break;
                case RenderOpKind.ViewScene:
                    if (entities && built.Count > 0 && built.Peek())
                        flags.Add(op.GetI(0));
                    break;
            }
        }
        return flags;
    }

    internal static List<ViewSceneData> Scenes(string name, RenderTrace trace, RenderOp[] plan, in RenderInputs input)
    {
        List<int> flags = EntitySceneFlags(plan, input);
        Assert.True(flags.Count > 0, $"{name}: the plan has no entity view");
        Assert.True(trace.SceneBytes.Count == flags.Count,
                    $"{name}: {trace.SceneBytes.Count} scenes recorded for {flags.Count} entity views (re-record it: scenes are C7 step 2's)");
        List<ViewSceneData>? scenes = trace.ScenesWithAnswers(k => flags[k], out string? why);
        Assert.True(scenes != null, $"{name}: {why}");
        return scenes!;
    }

    /// <summary>
    /// C7 step 2: each entity view's draws over its lists, made by
    /// <see cref="ViewDrawProgram"/> from the scene stock's lists held, are
    /// stock's calls: every colour and blend, static-prop batch, translucent
    /// surface leaf and depth-texture call, in order. Only an entity's own
    /// draw is a gap.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EachViewsDrawsOverItsListsAreStocksCalls(string name)
    {
        RenderTrace trace = Load(name);
        RenderInputs input = trace.Inputs()!.Value;
        RenderOp[] plan = RenderPlanner.Plan(input)!;
        List<ViewSceneData> scenes = Scenes(name, trace, plan, input);

        var expected = FrameExpectation(plan, input, VisFlags(trace), scenes);
        int exact = expected.Count(x => !x.Gap);
        bool ok = RenderPlanText.Matches(expected, trace.Calls, out int furthest);
        Assert.True(ok, $"{name}: the view programs' calls part from stock's after expected item {furthest} of {expected.Count}: " +
                        $"next expected '{(furthest < expected.Count ? expected[furthest].ToString() : "<end>")}'");
        int withoutScenes = FrameExpectation(plan, input, VisFlags(trace)).Count(x => !x.Gap);
        Assert.True(exact > withoutScenes + 10, $"{name}: the scenes added {exact - withoutScenes} exact calls; they must reach the draws");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AnEmptyPlanDoesNotMatchAStockFrame(string name)
    {
        // The positive arm's control: the matcher is not satisfied by gaps alone.
        RenderTrace trace = Load(name);
        RenderInputs input = trace.Inputs()!.Value;
        var expected = FrameExpectation([], input, VisFlags(trace));
        Assert.False(RenderPlanText.Matches(expected, trace.Calls, out _));
    }

    [Fact]
    public void TheFixturesAreThreePosesOfOneMapAndOneUnderWater()
    {
        string dir = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock");
        var names = Directory.GetFiles(dir, "*.txt").Select(Path.GetFileName).ToList();
        Assert.Contains("dm_lockdown-pose1.txt", names);
        Assert.Contains("dm_lockdown-pose2.txt", names);
        Assert.Contains("dm_lockdown-pose3.txt", names);
        Assert.Contains("ss_sandbox-underwater.txt", names);
    }
}
