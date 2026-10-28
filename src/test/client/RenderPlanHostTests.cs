using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="RenderPlanHost"/> and <see cref="RenderClientBinding"/>, the way
/// View_Render drives them (plan_client_novalve C7): a per-frame question that
/// answers the path and whether the frame is recorded, and the recorded text
/// coming back to whoever asked.
/// </summary>
public unsafe class RenderPlanHostTests
{
    private static RenderFrameOut Ask(RenderPlanHost host, int frame = 1)
    {
        var input = new RenderFrameIn { Width = 1280, Height = 720, FrameCount = frame };
        var answer = default(RenderFrameOut);
        host.Frame(in input, ref answer);
        return answer;
    }

    [Fact]
    public void TheFramesPlayerViewIsKeptAsRenderScaledIt()
    {
        // GetPlayerViewSetup after CViewRender::Render: the eye, and both fields
        // of view widened for the screen's aspect (16:9 here).
        var record = new PlayerViewRecord();
        var input = new RenderFrameIn { Width = 1280, Height = 720, FrameCount = 1, ScreenAspect = 16f / 9f };
        input.Eye.Fov = 75f;
        input.Eye.FovViewmodel = 54f;
        input.Eye.Origin = new Vector(1, 2, 3);
        var answer = default(RenderFrameOut);

        new RenderPlanHost(record).Frame(in input, ref answer);

        Assert.True(record.TryGet(out PlayerView view));
        Assert.Equal(new Vector(1, 2, 3), view.Origin);
        Assert.Equal(answer.Eye.Fov, view.Fov);
        Assert.Equal(answer.Eye.FovViewmodel, view.FovViewmodel);
        Assert.NotEqual(54f, view.FovViewmodel);
    }

    // Every frame is the plan's: the stock path left (C7 step 3).
    [Fact]
    public void WithNoRequestAFrameIsThePlansAndUnrecorded()
    {
        RenderFrameOut answer = Ask(new RenderPlanHost());
        Assert.Equal(1, answer.Path);
        Assert.Equal(0, answer.Trace);
    }

    [Fact]
    public void ARequestRecordsTheNextFrameAndOnlyThatOne()
    {
        var host = new RenderPlanHost();
        int tag = host.RequestTrace(_ => { });

        RenderFrameOut first = Ask(host, 1);
        Assert.Equal(1, first.Trace);
        Assert.Equal(tag, first.TraceTag);
        Assert.Equal(0, Ask(host, 2).Trace);
    }

    [Fact]
    public void TheRecordedTextReachesTheRequester()
    {
        var host = new RenderPlanHost();
        string? got = null;
        host.RequestTrace(text => got = text);
        RenderFrameOut answer = Ask(host);
        Assert.Equal(1, host.PendingTraces);

        host.TraceDone(answer.TraceTag, "render.SceneBegin\n");
        Assert.Equal("render.SceneBegin\n", got);
        Assert.Equal(0, host.PendingTraces);
    }

    [Fact]
    public void RequestsAreServedOneFrameEachInOrder()
    {
        var host = new RenderPlanHost();
        var got = new List<string>();
        int a = host.RequestTrace(t => got.Add("a:" + t));
        int b = host.RequestTrace(t => got.Add("b:" + t));
        Assert.NotEqual(a, b);

        RenderFrameOut first = Ask(host, 1);
        RenderFrameOut second = Ask(host, 2);
        Assert.Equal(a, first.TraceTag);
        Assert.Equal(b, second.TraceTag);

        host.TraceDone(second.TraceTag, "2");
        host.TraceDone(first.TraceTag, "1");
        Assert.Equal(["b:2", "a:1"], got);
    }

    [Fact]
    public void AnUnknownOrRepeatedTagIsIgnored()
    {
        var host = new RenderPlanHost();
        int calls = 0;
        host.RequestTrace(_ => calls++);
        RenderFrameOut answer = Ask(host);

        host.TraceDone(answer.TraceTag + 100, "x");
        Assert.Equal(0, calls);
        host.TraceDone(answer.TraceTag, "x");
        host.TraceDone(answer.TraceTag, "x");
        Assert.Equal(1, calls);
    }

    // The compare's stock side is a frozen frame: the next frame is the plan
    // frame it is held to, recorded, and the compare numbers entities by first
    // appearance (two sessions).
    [Fact]
    public void AFrozenCompareRecordsTheNextPlanFrameAgainstTheFrozenOne()
    {
        var host = new RenderPlanHost();
        RenderTrace frozen = RenderTrace.Parse("frame\tpath=0 framecount=1 rect=0,0,8,8\nmodel.DrawModelEx\tent=5 model=x.mdl\n");
        Assert.True(host.RequestFrozenCompare(frozen, false, out int sequence));
        Assert.False(host.RequestFrozenCompare(frozen, false, out _));

        RenderFrameOut plan = Ask(host, 2);
        Assert.Equal(1, plan.Trace);
        host.TraceDone(plan.TraceTag, "frame\tpath=1 framecount=2 rect=0,0,8,8\nplan\tops=1\nmodel.DrawModelEx\tent=17 model=x.mdl\n");
        RenderCompareReading c = host.CompareReading();
        Assert.Equal((sequence, "done", true), (c.Sequence, c.State, c.Equal));
    }

    // The reading says which copy texture each frame began with: the frozen
    // frame's derived start and the plan frame's noted one.
    [Fact]
    public void AFrozenCompareReportsEachFramesStartingCopyTexture()
    {
        var host = new RenderPlanHost();
        RenderTrace frozen = RenderTrace.Parse("frame\tpath=0 framecount=1 rect=0,0,8,8\nctx.SetFrameBufferCopyTexture\ttexture=- index=0\n");
        Assert.True(host.RequestFrozenCompare(frozen, false, out _));
        RenderFrameOut plan = Ask(host, 2);
        host.TraceDone(plan.TraceTag, "env\tfbcopy0\t_rt_fullframefb\nframe\tpath=1 framecount=2 rect=0,0,8,8\nplan\tops=1\n"
                                      + "ctx.SetFrameBufferCopyTexture\ttexture=_rt_fullframefb index=0\n");
        RenderCompareReading c = host.CompareReading();
        Assert.Equal(("-", "_rt_fullframefb"), (c.StockCopyStart, c.PlanCopyStart));
    }

    [Fact]
    public void TheBindingWritesTheAnswerIntoNativesStructAndCountsTheCrossing()
    {
        var host = new RenderPlanHost();
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), host);
        int tag = host.RequestTrace(_ => { });

        var input = new RenderFrameIn { Width = 640, Height = 480, FrameCount = 9 };
        var answer = new RenderFrameOut { Path = 77, Trace = 77, TraceTag = 77, Reserved = 77 };
        Assert.Equal(1, binding.RenderFrame(&input, &answer));
        Assert.Equal(1, answer.Path);
        Assert.Equal(1, answer.Trace);
        Assert.Equal(tag, answer.TraceTag);
        Assert.Equal(0, answer.Reserved);
        Assert.Contains(crossings.Active(), op => op.Name == nameof(CrossingOp.ClientRenderFrame) && op.Count == 1);
    }

    [Fact]
    public void TheBindingRefusesNullBuffersButStillCounts()
    {
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), new RenderPlanHost());
        Assert.Equal(0, binding.RenderFrame(null, null));
        Assert.Contains(crossings.Active(), op => op.Name == nameof(CrossingOp.ClientRenderFrame) && op.Count == 1);
    }

    [Fact]
    public void TheBindingDecodesTheTraceByItsLengthNotATerminator()
    {
        var host = new RenderPlanHost();
        var crossings = new Crossings();
        var binding = new RenderClientBinding(crossings, new StandardErrorLog(), host);
        string? got = null;
        host.RequestTrace(t => got = t);
        RenderFrameOut answer = Ask(host);

        byte[] bytes = Encoding.UTF8.GetBytes("ctx.Flush\thardware=0\nGARBAGE");
        fixed (byte* p = bytes)
            binding.RenderTraceDone(answer.TraceTag, p, "ctx.Flush\thardware=0\n".Length);
        Assert.Equal("ctx.Flush\thardware=0\n", got);
        Assert.Contains(crossings.Active(), op => op.Name == nameof(CrossingOp.ClientRenderTraceDone) && op.Count == 1);
    }

    // A shadow-depth view is asked mid-frame: with no plan this frame there is
    // nothing to draw it in; with one, its ops follow the frame's, and its
    // ViewScene finds its own context there.
    [Fact]
    public void AShadowDepthViewIsPlannedAfterTheFramesPlan()
    {
        var host = new RenderPlanHost();
        var shadow = new ViewSetup { Width = 512, Height = 512, Fov = 60, ZNear = 4, ZFar = 500, AspectRatio = 1f };
        var ops = new RenderOp[4096];
        Assert.Equal(-1, host.BuildShadowDepthPlan(shadow, ops, out _));

        int frame = host.BuildPlan(RenderPlannerTests.Inputs(), ops);
        Assert.True(frame > 0);
        int n = host.BuildShadowDepthPlan(shadow, ops, out int indexBase);
        Assert.True(n > 0);
        Assert.Equal(frame, indexBase);
        Assert.Equal((int)RenderOpKind.WorldViewBegin, ops[0].Kind);
        Assert.Equal(1, host.ShadowDepthViews);

        int scene = Array.FindIndex(ops, 0, n, o => o.Kind == (int)RenderOpKind.ViewScene);
        Assert.Equal(-1, host.BuildShadowDepthPlan(shadow, ops.AsSpan(0, n - 1), out _));
        Assert.True(scene > 0);
    }

    // Every frame's view is decided, the compare's frames included.
    [Fact]
    public void EveryFrameDecidesItsView()
    {
        var host = new RenderPlanHost();
        var frame = new RenderFrameIn
        {
            Width = 1280, Height = 720, ViewportWidth = 1280, ViewportHeight = 720, ViewportScale = 1f, ScreenAspect = 16f / 9f,
            Eye = new ViewSetup { Fov = 90, FovViewmodel = 54 }, ClientModeDrawViewModel = 1,
        };
        var answer = default(RenderFrameOut);
        host.Frame(in frame, ref answer);
        Assert.Equal((1280, 720), (answer.Eye.Width, answer.Eye.Height));
        Assert.Equal(RenderConst.DrawHud | RenderConst.DrawViewModel, answer.DrawFlags);
        host.Frame(in frame, ref answer);
        Assert.Equal(2, host.ViewDecisions);
    }

    [Fact]
    public void SetUpViewsIsViewFrames()
    {
        var host = new RenderPlanHost();
        var input = new ViewSetUpIn { FarZ = -1, MapExtents = 16384, DefaultFov = 75, ViewModelFov = 54 };
        ((IClientRenderPlan)host).ViewSetUp(in input, out ViewSetUpOut output);
        Assert.Equal(ViewFrame.SetUpView(input), output);
    }

    // CViewRender::FreezeFrame's rule: a new freeze runs from now and
    // captures the next frame; one already running is lengthened; 0 ends it.
    [Fact]
    public void AFreezeRunsFromNowCapturesOnceAndIsLengthenedOrEnded()
    {
        var freeze = new FreezeFrameState();
        freeze.Freeze(3f, 10f);
        Assert.True(freeze.CaptureDue);
        Assert.True(freeze.Showing(12.9f));
        Assert.False(freeze.Showing(13f));
        freeze.Captured();
        freeze.Freeze(2f, 11f);
        Assert.False(freeze.CaptureDue);
        Assert.True(freeze.Showing(14.9f));
        freeze.Freeze(0f, 12f);
        Assert.False(freeze.Showing(12f));
        Assert.Equal(0f, freeze.Flash);
    }

    // The host's plan reads its freeze state: the first plan after a freeze
    // captures, the next shows the capture, and when it runs out the scene is back.
    [Fact]
    public void ThePlanCapturesThenShowsTheFreezeFrame()
    {
        var host = new RenderPlanHost();
        var ops = new RenderOp[4096];
        RenderInputs input = RenderPlannerTests.Inputs();
        input.CurTime = 10f;
        host.ViewFreezeFrame(3f, 10f);

        int n = host.BuildPlan(input, ops);
        Assert.Contains(ops.Take(n), o => o.Kind == (int)RenderOpKind.FreezeFrameCapture);
        n = host.BuildPlan(input, ops);
        Assert.Contains(ops.Take(n), o => o.Kind == (int)RenderOpKind.FreezeFrameDraw);
        Assert.DoesNotContain(ops.Take(n), o => o.Kind == (int)RenderOpKind.SceneBegin);
        input.CurTime = 13.5f;
        n = host.BuildPlan(input, ops);
        Assert.DoesNotContain(ops.Take(n), o => o.Kind is (int)RenderOpKind.FreezeFrameDraw or (int)RenderOpKind.FreezeFrameCapture);
    }
}
