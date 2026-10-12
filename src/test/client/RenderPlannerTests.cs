using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="RenderPlanner"/>'s branches that dm_lockdown's recorded frames do
/// not reach (plan_client_novalve C7): the 3D skybox, expensive water above and
/// below the surface, forced-cheap water, the radial-vis clear, the viewmodel
/// switch, and the frames handed back to stock. Each fact names the stock
/// function whose rule it holds (viewrender.cpp).
/// </summary>
public class RenderPlannerTests
{
    internal static RenderInputs Inputs()
    {
        var input = new RenderInputs
        {
            View = new ViewSetup
            {
                Width = 1280, Height = 720, UnscaledWidth = 1280, UnscaledHeight = 720,
                Fov = 90, FovViewmodel = 54, ZNear = 7, ZFar = 28000, ZNearViewmodel = 1, ZFarViewmodel = 28000,
                AspectRatio = 1.7777778f, DoBloomAndToneMapping = 1,
                Origin = new Vector(0, 0, 64), Angles = new Vector(10, 90, 0),
            },
            ClearFlags = RenderConst.ClearDepth | RenderConst.ClearStencil,
            WhatToDraw = RenderConst.DrawHud | RenderConst.DrawViewModel,
            MaxClients = 8,
            HdrType = 0,
            DxLevel = 95,
            ScreenAspect = 1.7777778f,
            ClientModeDrawFog = 1,
            SkyboxVisible = 2,
            FogVolume = -1,
            FogVolumeLeaf = -1,
            CheapWaterEnd = 0.1f,
            ReflectionWidth = 512, ReflectionHeight = 512, RefractionWidth = 512, RefractionHeight = 512,
            ViewEntity = 1,
        };
        foreach (RenderCvar c in Enum.GetValues<RenderCvar>())
            if (c != RenderCvar.Count)
                input.SetCvar(c, 0f);
        foreach (RenderCvar c in new[] { RenderCvar.r_skybox, RenderCvar.r_3dsky, RenderCvar.mat_drawwater, RenderCvar.r_WaterDrawReflection,
                                         RenderCvar.r_WaterDrawRefraction, RenderCvar.r_ForceWaterLeaf, RenderCvar.mat_clipz,
                                         RenderCvar.r_drawviewmodel, RenderCvar.r_drawentities, RenderCvar.mat_viewportscale,
                                         RenderCvar.r_drawopaqueworld, RenderCvar.r_drawopaquerenderables, RenderCvar.r_drawtranslucentrenderables,
                                         RenderCvar.r_drawtranslucentworld, RenderCvar.r_entityclips, RenderCvar.r_DrawDetailProps,
                                         RenderCvar.r_worldlistcache })
            input.SetCvar(c, 1f);
        input.SetCvar(RenderCvar.r_eyewaterepsilon, 10f);
        foreach (RenderCvar c in new[] { RenderCvar.fog_color_r, RenderCvar.fog_color_g, RenderCvar.fog_color_b,
                                         RenderCvar.fog_colorskybox_r, RenderCvar.fog_colorskybox_g, RenderCvar.fog_colorskybox_b,
                                         RenderCvar.fog_start, RenderCvar.fog_end, RenderCvar.fog_enable })
            input.SetCvar(c, -1f);
        return input;
    }

    // RenderInputs is a struct: the edit works on a box and is copied back.
    private static RenderInputs With(Func<RenderInputs, RenderInputs> edit) => edit(Inputs());

    private static RenderInputs Water(RenderInputs input, bool eyeIn = false)
    {
        input.FogVolume = 3;
        input.FogVolumeLeaf = 12;
        input.EyeInFogVolume = eyeIn ? 1 : 0;
        input.WaterHeight = 0f;
        input.DistanceToWater = 64f;
        input.WaterMaterial = 1;
        input.WaterTranslucent = 1;
        input.WaterForceExpensiveDefined = 1;
        input.WaterForceExpensive = 1;
        input.WaterReflectTexture = 1;
        input.WaterRefractTexture = 1;
        return input;
    }

    private static List<RenderOp> Plan(in RenderInputs input)
    {
        RenderOp[]? plan = RenderPlanner.Plan(input);
        Assert.NotNull(plan);
        return [.. plan!];
    }

    private static List<RenderOp> Pushes(List<RenderOp> ops, RenderTarget target)
        => ops.Where(o => o.Kind == (int)RenderOpKind.Push3DView && o.GetI(1) == (int)target).ToList();

    [Fact]
    public void WithoutWaterOneWorldViewDrawsAndNoWaterTargetIsPushed()
    {
        var ops = Plan(Inputs());
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.DrawWorld);
        Assert.Empty(Pushes(ops, RenderTarget.WaterReflection));
        Assert.Empty(Pushes(ops, RenderTarget.WaterRefraction));
    }

    [Fact]
    public void ExpensiveWaterAboveTheSurfaceReflectsThenRefractsThenDrawsTheWorld()
    {
        var ops = Plan(With(i => Water(i)));
        var reflect = Pushes(ops, RenderTarget.WaterReflection);
        var refract = Pushes(ops, RenderTarget.WaterRefraction);
        Assert.Single(reflect);
        Assert.Single(refract);
        int r = ops.IndexOf(reflect[0]), f = ops.IndexOf(refract[0]);
        int main = ops.FindLastIndex(o => o.Kind == (int)RenderOpKind.DrawWorld);
        Assert.True(r < f && f < main, $"reflection {r}, refraction {f}, the world {main}");
        Assert.Equal(3, ops.Count(o => o.Kind == (int)RenderOpKind.DrawWorld));
    }

    [Fact]
    public void TheReflectionViewIsTheEyeMirroredInTheWaterAtTheTargetsSize()
    {
        // CBaseWorldView::AdjustView's reflection arm.
        var ops = Plan(With(i => { i = Water(i); i.WaterHeight = 16f; return i; }));
        ViewSetup v = Pushes(ops, RenderTarget.WaterReflection)[0].View;
        Assert.Equal(512, v.Width);
        Assert.Equal(512, v.Height);
        Assert.Equal(-10f, v.Angles.X);
        Assert.Equal(90f, v.Angles.Y);
        Assert.Equal(64f - 2f * (64f - 16f), v.Origin.Z);
    }

    [Fact]
    public void ForcedCheapWaterDrawsNoReflectionOrRefraction()
    {
        var ops = Plan(With(i => { i = Water(i); i.WaterForceCheapDefined = 1; i.WaterForceCheap = 1; return i; }));
        Assert.Empty(Pushes(ops, RenderTarget.WaterReflection));
        Assert.Empty(Pushes(ops, RenderTarget.WaterRefraction));
    }

    [Fact]
    public void UnderWaterTheRefractionIsCopiedOutAndTheOverlayIsSet()
    {
        var ops = Plan(With(i => { i = Water(i, eyeIn: true); i.WaterUnderwaterOverlay = 2; return i; }));
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.CopyRefractionTarget);
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.SetUnderwaterOverlay);
        Assert.Empty(Pushes(ops, RenderTarget.WaterReflection));
    }

    [Fact]
    public void AnOverlayNamedZeroIsNotSet()
    {
        var ops = Plan(With(i => { i = Water(i, eyeIn: true); i.WaterUnderwaterOverlay = 1; return i; }));
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.SetUnderwaterOverlay);
    }

    [Fact]
    public void AVisible3DSkyDrawsFirstWithItsOwnNearPlaneAndForcedLeaf()
    {
        var ops = Plan(With(i =>
        {
            i.SkyboxVisible = 1;
            i.SkyPresent = 1;
            i.SkyScale = 16;
            i.SkyArea = 2;
            return i;
        }));
        int area = ops.FindIndex(o => o.Kind == (int)RenderOpKind.Skybox3DAreaBits);
        int scene = ops.FindIndex(o => o.Kind == (int)RenderOpKind.ParticleIncrementFrameCode);
        Assert.True(area >= 0 && area < scene, $"the sky {area}, the scene {scene}");
        RenderOp build = ops.First(o => o.Kind == (int)RenderOpKind.BuildWorldLists);
        Assert.Equal(1, build.GetI(1));
        RenderOp skyPush = ops.Where(o => o.Kind == (int)RenderOpKind.Push3DView).ElementAt(1);
        Assert.Equal(2.0f, skyPush.View.ZNear);
        Assert.Equal(64f / 16f, skyPush.View.Origin.Z);
    }

    [Fact]
    public void TheRadialVisClearIsAskedOnlyWithoutAnySky()
    {
        Assert.Contains(Plan(With(i => { i.SkyboxVisible = 0; return i; })), o => o.Kind == (int)RenderOpKind.ClearColorToFogIfRadialVis);
        Assert.DoesNotContain(Plan(Inputs()), o => o.Kind == (int)RenderOpKind.ClearColorToFogIfRadialVis);
    }

    [Fact]
    public void TheViewmodelSwitchFollowsRDrawviewmodelAndTheThirdPersonCamera()
    {
        Assert.Equal(1, Plan(Inputs()).Single(o => o.Kind == (int)RenderOpKind.ViewModels).GetI(0));
        Assert.Equal(0, Plan(With(i => { i.SetCvar(RenderCvar.r_drawviewmodel, 0); return i; }))
                            .Single(o => o.Kind == (int)RenderOpKind.ViewModels).GetI(0));
        Assert.Equal(0, Plan(With(i => { i.ThirdPersonSeesOwner = 1; return i; }))
                            .Single(o => o.Kind == (int)RenderOpKind.ViewModels).GetI(0));
    }

    [Fact]
    public void ReflectiveGlassIsHandedBackToStock()
        => Assert.Null(RenderPlanner.Plan(With(i => { i.GlassInView = 1; return i; })));

    private static List<int> Kinds(List<RenderOp> ops) => ops.Select(o => o.Kind).ToList();

    // CViewRender::RenderView's freeze arm: CFreezeFrameView stands in for the scene.
    [Fact]
    public void AShowingFreezeFrameDrawsTheCaptureInsteadOfTheScene()
    {
        // No flash: g_flFreezeFlash is zeroed and nothing sets it, so at any
        // time after the first instant the flash is over.
        var ops = Plan(With(i => { i.FreezeActive = 1; i.CurTime = 5f; return i; }));
        var draw = Assert.Single(ops, o => o.Kind == (int)RenderOpKind.FreezeFrameDraw);
        Assert.Equal([0, 0, 1280, 720, -1], Enumerable.Range(0, 5).Select(k => draw.GetI(k)));
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.SceneBegin);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.DrawWorld);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.ShadowAdvanceFrame);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.FreezeFrameCapture);
    }

    // Stock sets s_bCanAccessCurrentView in the freeze arm as the scene does, and
    // the 2D half (the HUD over the frozen frame) is the same as a scene frame's.
    [Fact]
    public void AFreezeFrameKeepsTheTwoDimensionalHalf()
    {
        var scene = Kinds(Plan(Inputs()));
        var freeze = Kinds(Plan(With(i => { i.FreezeActive = 1; return i; })));
        int s2d = scene.IndexOf((int)RenderOpKind.Push2DView), f2d = freeze.IndexOf((int)RenderOpKind.Push2DView);
        Assert.True(s2d > 0 && f2d > 0);
        Assert.Equal(scene[s2d..], freeze[f2d..]);
        int draw = freeze.IndexOf((int)RenderOpKind.FreezeFrameDraw);
        Assert.Equal((int)RenderOpKind.AllowCurrentViewAccess, freeze[draw + 1]);
        Assert.Contains((int)RenderOpKind.HudPaint, freeze);
    }

    // The frame a new freeze shows is captured from the scene it draws: after
    // CleanupMain3DView's pop, before the saved target comes back.
    [Fact]
    public void AFreezeFrameWhoseCaptureIsDueDrawsTheSceneThenCopiesIt()
    {
        var ops = Plan(With(i => { i.FreezeActive = 1; i.FreezeTake = 1; return i; }));
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.FreezeFrameDraw);
        int capture = ops.FindIndex(o => o.Kind == (int)RenderOpKind.FreezeFrameCapture);
        Assert.True(capture > 0, "no capture");
        Assert.Equal([0, 0, 1280, 720], Enumerable.Range(0, 4).Select(k => ops[capture].GetI(k)));
        Assert.Equal((int)RenderOpKind.PopView, ops[capture - 1].Kind);
        Assert.Equal((int)RenderOpKind.SetRenderTargetSaved, ops[capture + 1].Kind);
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.SceneBegin);
    }

    [Fact]
    public void AFrameWithNoFreezeNeitherDrawsNorCapturesOne()
    {
        var ops = Plan(With(i => { i.FreezeTake = 1; i.FreezeActive = 0; return i; }));
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.FreezeFrameCapture);
        ops = Plan(Inputs());
        Assert.DoesNotContain(ops, o => o.Kind is (int)RenderOpKind.FreezeFrameCapture or (int)RenderOpKind.FreezeFrameDraw);
    }

    private static ViewSetup ShadowView() => new()
    {
        Width = 1024, Height = 1024, Fov = 60, ZNear = 4, ZFar = 750, AspectRatio = 1f,
        Origin = new Vector(100, 200, 64), Angles = new Vector(30, 45, 0),
    };

    private static List<RenderOp> ShadowPlan(in RenderInputs input)
    {
        var b = new RenderPlanBuilder();
        RenderPlanner.PlanShadowDepthView(input, ShadowView(), b);
        return [.. b.Span.ToArray()];
    }

    // CShadowDepthView::Draw, step for step.
    [Fact]
    public void AShadowDepthViewIsStocksCShadowDepthViewDraw()
    {
        Assert.Equal(new[]
        {
            RenderOpKind.WorldViewBegin, RenderOpKind.ViewSetupVisEx, RenderOpKind.ClearColor3ub, RenderOpKind.Push3DView,
            RenderOpKind.SetupCurrentView, RenderOpKind.BuildWorldLists, RenderOpKind.BuildRenderables, RenderOpKind.SoundExtraUpdate,
            RenderOpKind.ViewScene, RenderOpKind.DrawWorld, RenderOpKind.ForcedMaterialOverride, RenderOpKind.DrawOpaque,
            RenderOpKind.ForcedMaterialOverride, RenderOpKind.PopView, RenderOpKind.WorldViewEnd,
        }, ShadowPlan(Inputs()).Select(o => (RenderOpKind)o.Kind));
    }

    [Fact]
    public void AShadowDepthViewPushesTheHandedTargetsAndClearsDepthToWhite()
    {
        var ops = ShadowPlan(Inputs());
        var push = ops.Single(o => o.Kind == (int)RenderOpKind.Push3DView);
        Assert.Equal([RenderConst.ClearDepth, (int)RenderTarget.ShadowColor, (int)RenderTarget.ShadowDepth, 1],
                     Enumerable.Range(0, 4).Select(k => push.GetI(k)));
        Assert.Equal(new Vector(100, 200, 64), push.View.Origin);
        var clear = ops.Single(o => o.Kind == (int)RenderOpKind.ClearColor3ub);
        Assert.Equal([255, 255, 255], Enumerable.Range(0, 3).Select(k => clear.GetI(k)));
        Assert.Equal(RenderConst.ViewShadowDepthTexture, ops.Single(o => o.Kind == (int)RenderOpKind.SetupCurrentView).GetI(0));
    }

    // BuildWorldRenderLists( true, -1, true, true ) and the shadow view's
    // renderables: no translucents, the distance capped at the view's far Z.
    [Fact]
    public void AShadowDepthViewBuildsShadowDepthLists()
    {
        var ops = ShadowPlan(With(i => { i.SetCvar(RenderCvar.cl_maxrenderable_dist, 3000f); return i; }));
        var world = ops.Single(o => o.Kind == (int)RenderOpKind.BuildWorldLists);
        Assert.Equal([1, -1, 1, 1, 0], Enumerable.Range(0, 5).Select(k => world.GetI(k)));
        var renderables = ops.Single(o => o.Kind == (int)RenderOpKind.BuildRenderables);
        Assert.Equal(RenderConst.ViewShadowDepthTexture, renderables.GetI(0));
        Assert.Equal(0, renderables.GetI(3));
        Assert.Equal(750f * 750f, renderables.GetF(0));
    }

    // The opaque renderables draw in shadow depth mode between the depth-write
    // override and its clear; the world draws with both water sides and the map flag.
    [Fact]
    public void AShadowDepthViewDrawsOpaquesUnderTheDepthWriteOverride()
    {
        var ops = ShadowPlan(Inputs());
        int opaque = ops.FindIndex(o => o.Kind == (int)RenderOpKind.DrawOpaque);
        Assert.Equal(ViewDrawProgram.DepthModeShadow, ops[opaque].GetI(0));
        Assert.Equal(2, ops[opaque - 1].GetI(0));
        Assert.Equal(0, ops[opaque + 1].GetI(0));
        int draw = RenderConst.DfRenderUnderwater | RenderConst.DfRenderAbovewater | ViewDrawProgram.DfShadowDepthMap;
        Assert.Equal(draw, ops.Single(o => o.Kind == (int)RenderOpKind.WorldViewBegin).GetI(0));
        Assert.DoesNotContain(ops, o => o.Kind is (int)RenderOpKind.DrawTranslucent or (int)RenderOpKind.DrawNoZTranslucent);
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.DrawWorld);
        Assert.DoesNotContain(ShadowPlan(With(i => { i.SetCvar(RenderCvar.r_drawopaqueworld, 0); return i; })),
                              o => o.Kind == (int)RenderOpKind.DrawWorld);
    }

    // The view program's context is the shadow view's own: its draws are the one opaque pass.
    [Fact]
    public void AShadowDepthViewsSceneHasItsOwnProgramContext()
    {
        var ops = ShadowPlan(Inputs()).ToArray();
        int scene = Array.FindIndex(ops, o => o.Kind == (int)RenderOpKind.ViewScene);
        var context = ViewDrawProgram.ContextOf(ops, scene, Inputs());
        Assert.NotNull(context);
        var only = Assert.Single(context!.Draws);
        Assert.Equal(RenderOpKind.DrawOpaque, only.Kind);
        Assert.Equal(RenderConst.ViewShadowDepthTexture, only.ViewId);
    }

    // CFreezeFrameView::Draw's flash: 340 a second, clamped, off a screenshot.
    [Theory]
    [InlineData(10f, 10f, false, 0)]
    [InlineData(10.5f, 10f, false, 170)]
    [InlineData(11f, 10f, false, 255)]
    [InlineData(9.9f, 10f, false, -1)]
    [InlineData(10.5f, 10f, true, -1)]
    public void TheFreezeFlashFadesAtStocksSpeed(float flash, float curTime, bool screenshot, int alpha)
    {
        var ops = Plan(With(i => { i.FreezeActive = 1; i.FreezeFlash = flash; i.CurTime = curTime; i.TakingScreenshot = screenshot ? 1 : 0; return i; }));
        Assert.Equal(alpha, ops.Single(o => o.Kind == (int)RenderOpKind.FreezeFrameDraw).GetI(4));
    }

    [Fact]
    public void ThePlanIsBalanced()
    {
        foreach (RenderInputs input in new[] { Inputs(), With(i => Water(i)), With(i => Water(i, eyeIn: true)) })
        {
            var ops = Plan(input);
            int push = ops.Count(o => o.Kind is (int)RenderOpKind.Push3DView or (int)RenderOpKind.Push2DView);
            int pop = ops.Count(o => o.Kind == (int)RenderOpKind.PopView);
            Assert.Equal(push, pop);
            Assert.Equal(ops.Count(o => o.Kind == (int)RenderOpKind.WorldViewBegin), ops.Count(o => o.Kind == (int)RenderOpKind.WorldViewEnd));
            Assert.Equal(ops.Count(o => o.Kind == (int)RenderOpKind.FrameBufferCopyPush), ops.Count(o => o.Kind == (int)RenderOpKind.FrameBufferCopyPop));
        }
    }

    private static MonitorView Monitor(bool viewer = false)
        => MonitorView.From(new ViewTargetSetup
        {
            OriginX = 10, OriginY = 20, OriginZ = 300, Pitch = 90, Fov = 75, ZNear = 4, ZFar = 4000,
            Width = 384, Height = 288, Flags = viewer ? ViewTargetFlags.DrawViewer : 0,
        }, RenderTarget.ViewTarget0, 0);

    [Fact]
    public void AMonitorIsDrawnBeforeTheSceneIntoItsTargetAsVIEW_MONITOR()
    {
        // CViewRender::RenderView: DrawMonitors before SceneBegin;
        // DrawOneMonitor: Push3DView( clear depth|colour, the target ),
        // ViewDrawScene( 2D sky, VIEW_MONITOR ), PopView.
        var ops = RenderPlanner.Plan(Inputs(), [Monitor()])!.ToList();
        int push = ops.FindIndex(o => o.Kind == (int)RenderOpKind.Push3DView && o.GetI(1) == (int)RenderTarget.ViewTarget0);
        int scene = ops.FindIndex(o => o.Kind == (int)RenderOpKind.SceneBegin);
        Assert.True(push >= 0 && push < scene, $"the monitor {push}, the scene {scene}");
        Assert.Equal(RenderConst.ClearDepth | RenderConst.ClearColor, ops[push].GetI(0));
        Assert.Equal(384, ops[push].View.Width);
        Assert.Equal(1, ops[push].View.RenderToSubrect);
        RenderOp setup = ops.Skip(push).First(o => o.Kind == (int)RenderOpKind.SetupCurrentView);
        Assert.Equal(RenderConst.ViewMonitor, setup.GetI(0));
        int drawn = ops.FindIndex(o => o.Kind == (int)RenderOpKind.ViewTargetDrawn);
        Assert.True(drawn > push && drawn < scene);
        Assert.Equal(2, ops.Count(o => o.Kind == (int)RenderOpKind.DrawWorld));
    }

    [Fact]
    public void AMonitorThatSeesTheViewerBracketsItsPass()
    {
        var ops = RenderPlanner.Plan(Inputs(), [Monitor(viewer: true)])!.ToList();
        int begin = ops.FindIndex(o => o.Kind == (int)RenderOpKind.ViewerSeenBegin);
        int push = ops.FindIndex(o => o.Kind == (int)RenderOpKind.Push3DView && o.GetI(1) == (int)RenderTarget.ViewTarget0);
        int end = ops.FindIndex(o => o.Kind == (int)RenderOpKind.ViewerSeenEnd);
        Assert.True(begin >= 0 && begin < push && push < end, $"{begin} {push} {end}");
        Assert.DoesNotContain(RenderPlanner.Plan(Inputs(), [Monitor()])!, o => o.Kind == (int)RenderOpKind.ViewerSeenBegin);
    }

    [Fact]
    public void BelowDX7NoMonitorIsDrawn()
    {
        var ops = RenderPlanner.Plan(With(i => { i.DxLevel = 60; return i; }), [Monitor()])!;
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.ViewTargetDrawn);
    }

    [Fact]
    public void AMonitorDrawsNoWaterWhateverTheEyeIsIn()
    {
        // The frame's water inputs are the main eye's; the monitor's own fog
        // volume is not asked, so it draws dry rather than with the eye's.
        var ops = RenderPlanner.Plan(With(i => Water(i, eyeIn: true)), [Monitor()])!.ToList();
        int scene = ops.FindIndex(o => o.Kind == (int)RenderOpKind.SceneBegin);
        Assert.DoesNotContain(ops.Take(scene), o => o.Kind == (int)RenderOpKind.FogVolumeState);
        Assert.Contains(ops.Skip(scene), o => o.Kind == (int)RenderOpKind.FogVolumeState);
    }

    [Fact]
    public void TheHostQueuesAMonitorForTheOneTargetAndForOneFrame()
    {
        var host = new RenderPlanHost();
        var setup = new ViewTargetSetup { Width = 64, Height = 64, Fov = 90, ZNear = 4, ZFar = 100 };
        Assert.True(host.QueueMonitor(0, setup));
        Assert.False(host.QueueMonitor(1, setup));
        var ops = new RenderOp[512];
        host.BuildPlan(Inputs(), ops);
        Assert.Equal(1, host.MonitorPasses);
        int n = host.BuildPlan(Inputs(), ops);
        Assert.DoesNotContain(ops.AsSpan(0, n).ToArray(), o => o.Kind == (int)RenderOpKind.ViewTargetDrawn);
        Assert.Equal(1, host.MonitorPasses);
    }

    private static readonly DebugOverlayMaterials Materials = new(11, 12, 13, 14, 15, 16, 17, 18, true);

    private static List<RenderOp> PlanWithDebug(Func<RenderInputs, RenderInputs> edit, DebugOverlayMaterials? materials = null)
    {
        var b = new RenderPlanBuilder();
        Assert.True(RenderPlanner.Plan(With(edit), b, default, materials ?? Materials));
        return [.. b.Ops];
    }

    private static RenderInputs Debug(RenderInputs i)
    {
        i.SetCvar(RenderCvar.mat_showlightmappage, -1);
        return i;
    }

    [Fact]
    public void NoDebugSwitchDrawsNoOverlay()
    {
        // Draw2DDebuggingInfo with every switch off (and mat_showlightmappage -1).
        var ops = PlanWithDebug(i => Debug(i), DebugOverlayMaterials.None with { });
        Assert.DoesNotContain(ops, o => o.Kind is >= (int)RenderOpKind.DebugQuad and <= (int)RenderOpKind.DebugFlashlightDepth);
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.SmokeFogOverlayAlpha);
    }

    [Fact]
    public void TheWaterTexturesAreTheReflectionThenTheFlippedRefractionAtTheOverlaySize()
    {
        var ops = PlanWithDebug(i => { i = Debug(i); i.SetCvar(RenderCvar.mat_showwatertextures, 1); i.SetCvar(RenderCvar.mat_wateroverlaysize, 256); return i; },
                                Materials with { DrawMaterial = 0 });
        var quads = ops.Where(o => o.Kind == (int)RenderOpKind.DebugQuad).ToList();
        Assert.Equal(2, quads.Count);
        Assert.Equal(13, quads[0].GetI(0));
        Assert.Equal(14, quads[1].GetI(0));
        Assert.Equal(0f, quads[0].GetF(1));
        Assert.Equal(256f, quads[1].GetF(1));
        Assert.True(quads[0].GetF(5) > quads[0].GetF(7), "the reflection's t runs 1 -> 0");
        Assert.True(quads[1].GetF(5) < quads[1].GetF(7), "the refraction is flipped");
    }

    [Fact]
    public void YuvAndHsvNeedTheirDxLevels()
    {
        var at80 = PlanWithDebug(i => { i = Debug(i); i.DxLevel = 80; i.SetCvar(RenderCvar.mat_yuv, 1); i.SetCvar(RenderCvar.mat_hsv, 1); return i; });
        Assert.Single(at80, o => o.Kind == (int)RenderOpKind.DebugScreenMaterial);
        Assert.Equal(11, at80.Single(o => o.Kind == (int)RenderOpKind.DebugScreenMaterial).GetI(0));
        var at90 = PlanWithDebug(i => { i = Debug(i); i.DxLevel = 90; i.SetCvar(RenderCvar.mat_yuv, 1); i.SetCvar(RenderCvar.mat_hsv, 1); return i; });
        Assert.Equal(2, at90.Count(o => o.Kind == (int)RenderOpKind.DebugScreenMaterial));
    }

    [Fact]
    public void TheFrameBufferTexturesDrawSideBySideIntoTheDefaultTarget()
    {
        var ops = PlanWithDebug(i => { i = Debug(i); i.SetCvar(RenderCvar.mat_showframebuffertexture, 1); i.SetCvar(RenderCvar.mat_framebuffercopyoverlaysize, 128); return i; });
        int push = ops.FindIndex(o => o.Kind == (int)RenderOpKind.PushDefaultTarget);
        int pop = ops.FindIndex(o => o.Kind == (int)RenderOpKind.PopRenderTargetAndViewport);
        var quads = ops.Skip(push).Take(pop - push).Where(o => o.Kind == (int)RenderOpKind.DebugQuad).ToList();
        Assert.Equal([16, 17], quads.Select(q => q.GetI(0)));
        Assert.Equal([0f, 128f], quads.Select(q => q.GetF(0)));
    }

    [Fact]
    public void TheOtherSwitchesEachPlaceTheirOverlay()
    {
        var ops = PlanWithDebug(i =>
        {
            i = Debug(i);
            i.SetCvar(RenderCvar.mat_showlightmappage, 2);
            i.SetCvar(RenderCvar.cl_drawshadowtexture, 1);
            i.SetCvar(RenderCvar.cl_shadowtextureoverlaysize, 300);
            i.SetCvar(RenderCvar.mat_showcamerarendertarget, 1);
            i.SetCvar(RenderCvar.mat_wateroverlaysize, 64);
            i.SetCvar(RenderCvar.mat_drawTextureScale, 2);
            i.SetCvar(RenderCvar.r_flashlightdrawdepth, 1);
            return i;
        });
        Assert.Equal(2, ops.Single(o => o.Kind == (int)RenderOpKind.DebugLightmapPage).GetI(0));
        Assert.Equal(300, ops.Single(o => o.Kind == (int)RenderOpKind.DebugShadowTexture).GetI(0));
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.DebugQuad && o.GetI(0) == 15 && o.GetF(2) == 64f);
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.DebugQuad && o.GetI(0) == 18 && o.GetI(1) == 1);
        Assert.Equal(2f, ops.Single(o => o.Kind == (int)RenderOpKind.DebugShowTexture).GetF(0));
        Assert.Single(ops, o => o.Kind == (int)RenderOpKind.DebugFlashlightDepth);
    }

    [Fact]
    public void TheOverdrawTestIsFortySmokeOverlaysThenAlphaZero()
    {
        var ops = PlanWithDebug(i => { i = Debug(i); i.SetCvar(RenderCvar.cl_overdraw_test, 1); return i; });
        var off = PlanWithDebug(i => Debug(i));
        // The frame's own smoke-fog overlay is in both.
        Assert.Equal(40, ops.Count(o => o.Kind == (int)RenderOpKind.SmokeFogOverlay) - off.Count(o => o.Kind == (int)RenderOpKind.SmokeFogOverlay));
        var alphas = ops.Where(o => o.Kind == (int)RenderOpKind.SmokeFogOverlayAlpha).Select(o => o.GetF(0)).ToList();
        Assert.Equal(41, alphas.Count);
        Assert.Equal(0f, alphas[^1]);
        Assert.Equal(20f / 255f, alphas[0]);
    }

    [Fact]
    public void G9PrintsAsCPrintfDoes()
    {
        Assert.Equal("1.77777779", RenderPlanText.G9(1.7777778f));
        Assert.Equal("28377.9199", RenderPlanText.G9(28377.9199f));
        Assert.Equal("0", RenderPlanText.G9(0f));
        Assert.Equal("-0", RenderPlanText.G9(-0f));
        Assert.Equal("0.00999999978", RenderPlanText.G9(0.01f));
        Assert.Equal("0.607843161", RenderPlanText.G9(155f / 255f));
        Assert.Equal("1e+10", RenderPlanText.G9(1e10f));
        Assert.Equal("56755.8398", RenderPlanText.G9(RenderConst.MaxTraceLength));
    }

    // C7 step 3 phase 1: the list builds' decisions are the plan's.
    private static RenderOp MainRenderables(RenderInputs input)
        => RenderPlanner.Plan(input)!.First(o => o.Kind == (int)RenderOpKind.BuildRenderables && o.GetI(0) == RenderConst.ViewMain);

    [Fact]
    public void TheRenderablesBuildCarriesSetupRenderablesListsDecisions()
    {
        RenderOp op = MainRenderables(With(i =>
        {
            i.ClientModeDrawDetail = 1;
            i.SetCvar(RenderCvar.r_drawentities, 1);
            i.SetCvar(RenderCvar.r_DrawDetailProps, 1);
            i.SetCvar(RenderCvar.cl_maxrenderable_dist, 3000);
            return i;
        }));
        Assert.Equal([1, 1, 1], new[] { op.GetI(1), op.GetI(2), op.GetI(3) });
        Assert.Equal(3000f * 3000f, op.GetF(0));

        RenderOp noDetail = MainRenderables(With(i =>
        {
            i.ClientModeDrawDetail = 0;
            i.SetCvar(RenderCvar.r_drawentities, 1);
            i.SetCvar(RenderCvar.r_DrawDetailProps, 1);
            return i;
        }));
        Assert.Equal(0, noDetail.GetI(2));
    }

    [Fact]
    public void TheLevelOverviewStopsPropFadeInTheWorldListBuild()
    {
        RenderOp normal = RenderPlanner.Plan(Inputs())!.First(o => o.Kind == (int)RenderOpKind.BuildWorldLists);
        Assert.Equal(1f, normal.GetF(1));
        RenderOp overview = RenderPlanner.Plan(With(i => { i.SetCvar(RenderCvar.cl_leveloverview, 2); return i; }))!
            .First(o => o.Kind == (int)RenderOpKind.BuildWorldLists);
        Assert.Equal(-1f, overview.GetF(1));
    }
}
