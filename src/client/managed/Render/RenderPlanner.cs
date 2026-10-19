using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using static SourceSharp.Client.Render.RenderConst;

namespace SourceSharp.Client.Render;

/// <summary>
/// The render plan's decisions (plan_client_novalve.md §3.3, C7): stock's
/// <c>CViewRender::RenderView</c> decision tree (viewrender.cpp) -- which views
/// draw, from where, with which clear and draw flags, fog, clip heights and
/// render targets, and in what order -- turned into the op list
/// shell_view_executor_client.cpp carries out. A pure function of the frame's
/// <see cref="RenderInputs"/>; it reads nothing else.
///
/// <para>
/// Each method is named for the stock function it ports and keeps its order of
/// operations. Where stock's next decision depends on an engine answer given
/// mid-frame (ViewSetupVisEx's radial-vis bit), the op says what to do with the
/// answer and the executor applies it.
/// </para>
///
/// <para>
/// NOT PLANNED, ANSWERED STOCK: reflective glass in view
/// (IsReflectiveGlassInView, which answers false in this build). <see cref="Plan"/>
/// returns null for that frame and View_Render draws it with stock's code.
/// A freeze frame (test_freezeframe) is planned: <see cref="RenderInputs.FreezeActive"/>
/// shows the captured frame, <see cref="RenderInputs.FreezeTake"/> captures it.
/// </para>
/// </summary>
public static class RenderPlanner
{
    /// <summary>The ops for one RenderView, or null when stock should draw the frame.</summary>
    public static RenderOp[]? Plan(in RenderInputs input)
    {
        var b = new RenderPlanBuilder();
        return Plan(input, b) ? b.ToArray() : null;
    }

    /// <summary>The same, with this frame's second-camera views (C7 item 6).</summary>
    public static RenderOp[]? Plan(in RenderInputs input, ReadOnlySpan<MonitorView> monitors)
    {
        var b = new RenderPlanBuilder();
        return Plan(input, b, monitors) ? b.ToArray() : null;
    }

    /// <summary>
    /// The ops for one RenderView into <paramref name="b"/>, cleared first; false
    /// (and nothing planned) when stock should draw the frame. A caller that
    /// keeps its builder plans a frame without allocating.
    /// </summary>
    public static bool Plan(in RenderInputs input, RenderPlanBuilder b) => Plan(input, b, default);

    public static bool Plan(in RenderInputs input, RenderPlanBuilder b, ReadOnlySpan<MonitorView> monitors)
        => Plan(input, b, monitors, DebugOverlayMaterials.None);

    /// <summary>The plan with the debug overlays' materials (C7 step 2), which only the host can look up.</summary>
    public static bool Plan(in RenderInputs input, RenderPlanBuilder b, ReadOnlySpan<MonitorView> monitors,
                            DebugOverlayMaterials debug)
    {
        b.Clear();
        if (input.GlassInView != 0)
            return false;
        var frame = new Frame(input, b, debug);
        frame.RenderView(monitors);
        return true;
    }

    /// <summary>
    /// The ops for one shadow-depth view (CViewRender::UpdateShadowDepthTexture,
    /// i.e. CShadowDepthView::Draw), asked by the shadow manager from inside the
    /// frame's ShadowComputeDepthTextures op; <paramref name="frame"/> is that
    /// frame's inputs, <paramref name="shadow"/> the flashlight's view.
    /// </summary>
    public static void PlanShadowDepthView(in RenderInputs frame, in ViewSetup shadow, RenderPlanBuilder b)
    {
        b.Clear();
        new Frame(frame, b).ShadowDepthView(shadow);
    }

    private static float Cv(in RenderInputs input, RenderCvar c) => input.Cvar(c);

    private static bool CvBool(in RenderInputs input, RenderCvar c) => input.Cvar(c) != 0f;

    private static int CvInt(in RenderInputs input, RenderCvar c) => (int)input.Cvar(c);

    /// <summary>One RenderView's planning state: the inputs and the ops so far.</summary>
    private readonly struct Frame(RenderInputs input, RenderPlanBuilder b, DebugOverlayMaterials debug = default)
    {
        private readonly RenderInputs _in = input;
        private readonly DebugOverlayMaterials _debug = debug;

        private struct WaterInfo
        {
            public bool CheapWater;
            public bool Refract;
            public bool Reflect;
            public bool ReflectEntities;
            public bool DrawWaterSurface;
            public bool OpaqueWater;
        }

        // ---------------------------------------------------------------------
        // CViewRender::RenderView
        // ---------------------------------------------------------------------
        public void RenderView(ReadOnlySpan<MonitorView> monitors)
        {
            ViewSetup view = _in.View;
            int clear = _in.ClearFlags;
            int what = _in.WhatToDraw;

            b.Op(RenderOpKind.ClearUnderwaterOverlay);
            b.WithView(RenderOpKind.SetCurrentViewSetup, view);
            b.Op(RenderOpKind.SaveRenderTarget);

            // The freeze frame shows: stock's CFreezeFrameView stands in for
            // the whole 3D scene, and the 2D half below draws over it.
            if (_in.FreezeTake == 0 && _in.FreezeActive != 0)
            {
                b.Rect(RenderOpKind.FreezeFrameDraw, view.X, view.Y, view.Width, view.Height,
                       FreezeFlashAlpha(_in.FreezeFlash, _in.CurTime, _in.TakingScreenshot != 0));
                b.AddInt(RenderOpKind.AllowCurrentViewAccess, 1);
            }
            else
            {
                Scene(view, clear, what, monitors);
            }

            if (CvBool(_in, RenderCvar.mat_viewportupscale) && Cv(_in, RenderCvar.mat_viewportscale) < 1f)
                b.WithView(RenderOpKind.Upscale, view);

            b.Push2DView(view, 0, RenderTarget.Saved);
            if ((what & DrawHud) != 0)
                b.WithView(RenderOpKind.HudPaint, view);
            Draw2DDebuggingInfo(view);
            b.AddInt(RenderOpKind.AllowCurrentViewAccess, 0);
            GenerateOverdrawForTesting();
            b.Op(RenderOpKind.PopView);
            b.Op(RenderOpKind.WorldListCacheFlush);
        }

        // RenderView's `else`: the frame's own 3D scene, which a freeze frame
        // stands in for.
        private void Scene(in ViewSetup view, int clear, int what, ReadOnlySpan<MonitorView> monitors)
        {
            b.Op(RenderOpKind.ShadowAdvanceFrame);

            // CViewRender::DrawMonitors' place: before the scene, with
            // monitors on and the hardware able (DX7 and up).
            if (_in.DxLevel >= 70)
                foreach (MonitorView monitor in monitors)
                    DrawMonitor(monitor);

            b.Op(RenderOpKind.SceneBegin);
            b.Op(RenderOpKind.TurnOnToneMapping);

            clear = SetupMain3DView(view, clear);

            bool drew3dSkybox = SkyboxSetup(view, ref clear, out int skyboxVisible);

            if ((clear & ClearColor) == 0 && _in.PointContentsSolid != 0)
                clear |= ClearColor;

            ViewDrawScene(drew3dSkybox, skyboxVisible, view, clear, ViewMain, (what & DrawViewModel) != 0);

            b.AddInt(RenderOpKind.AllowCurrentViewAccess, 1);
            b.Op(RenderOpKind.DrawPortals);
            b.Op(RenderOpKind.FogNone);
            b.Op(RenderOpKind.SceneEnd);
            b.Op(RenderOpKind.DrawLights);
            b.Op(RenderOpKind.RenderPlayerSprites);

            bool bloom = view.DoBloomAndToneMapping != 0;
            bool buildingCubemaps = CvBool(_in, RenderCvar.building_cubemaps);
            if (!buildingCubemaps && bloom && CvInt(_in, RenderCvar.mat_motion_blur_enabled) != 0 && _in.DxLevel >= 90)
                b.WithView(RenderOpKind.MotionBlur, view);

            b.WithView(RenderOpKind.PostScreenSpaceEffects, view);
            DrawViewModels(view, (what & DrawViewModel) != 0);
            b.Op(RenderOpKind.UnderwaterOverlay);
            b.Op(RenderOpKind.PixelVisEndScene);
            b.Op(RenderOpKind.SmokeFogOverlay);
            b.Add(RenderOpKind.ViewDrawFade, [_in.Fade(0), _in.Fade(1), _in.Fade(2), _in.Fade(3), _in.FadeBlend != 0 ? 1 : 0]);
            b.Rect(RenderOpKind.ScreenOverlay, view.X, view.Y, view.Width, view.Height, (int)ScreenOverlayBranchOf());
            b.Op(RenderOpKind.SoundExtraUpdate);

            if (!buildingCubemaps && bloom)
                b.Rect(RenderOpKind.EnginePostProcessing, view.X, view.Y, view.Width, view.Height, _in.FlashlightOn != 0 ? 1 : 0);

            b.Rect(RenderOpKind.GrabPreColorCorrectedFrame, view.X, view.Y, view.Width, view.Height);
            if (_in.IsHammerRunning == 0)
                b.Rect(RenderOpKind.ScreenSpaceEffects, 0, 0, view.Width, view.Height);
            if (_in.HdrType == HdrInteger)
                b.ToneMapScaleLinear(1f, 1f, 1f);

            b.Op(RenderOpKind.PopView); // CleanupMain3DView

            // The frame a freeze is about to show is this one.
            if (_in.FreezeTake != 0)
                b.Rect(RenderOpKind.FreezeFrameCapture, view.X, view.Y, view.Width, view.Height);

            b.Op(RenderOpKind.SetRenderTargetSaved);
        }

        // ---------------------------------------------------------------------
        // CShadowDepthView::Draw, added to the scene by UpdateShadowDepthTexture
        // (the view is the active renderer while it draws). Its draw flags are
        // the main view's base flags -- 0: every ViewDrawScene passes the
        // default -- with both sides of the water and the shadow-depth map.
        // ---------------------------------------------------------------------
        public void ShadowDepthView(in ViewSetup shadow)
        {
            const int draw = DfRenderUnderwater | DfRenderAbovewater | ViewDrawProgram.DfShadowDepthMap;
            b.WorldViewBegin(shadow, draw, ClearDepth);
            b.ViewSetupVisEx(_in.ForceNoVis != 0, shadow.Origin);
            b.ClearColor3ub(0xFF, 0xFF, 0xFF);
            b.Push3DView(shadow, ClearDepth, RenderTarget.ShadowColor, RenderTarget.ShadowDepth);
            b.SetupCurrentView(shadow.Origin, shadow.Angles, ViewShadowDepthTexture);
            b.BuildWorldLists(true, -1, true, true, null, OpacityFactor());
            BuildRenderables(ViewShadowDepthTexture, shadow);
            b.Op(RenderOpKind.SoundExtraUpdate);
            b.ViewScene(draw);
            b.DrawWorld(0f, draw, CvBool(_in, RenderCvar.r_drawopaqueworld));
            b.ForcedMaterialOverride(OverrideDepthWrite);
            b.DrawOpaque(ViewDrawProgram.DepthModeShadow);
            b.ForcedMaterialOverride(OverrideNormal);
            b.Op(RenderOpKind.PopView);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        // OverrideType_t (ivmodelrender.h).
        private const int OverrideNormal = 0;
        private const int OverrideDepthWrite = 2;

        // CFreezeFrameView::Draw's flash: FREEZECAM_SNAPSHOT_FADE_SPEED (340) a
        // second, clamped to a byte, while the flash lasts and the frame is not a
        // screenshot; -1 when there is none.
        internal static int FreezeFlashAlpha(float flash, float curTime, bool screenshot)
        {
            if (!(flash >= curTime) || screenshot)
                return -1;
            int alpha = (int)(340f * (flash - curTime));
            return Math.Max(0, Math.Min(alpha, 255));
        }

        // ---------------------------------------------------------------------
        // CDebugViewRender::Draw2DDebuggingInfo (viewdebug.cpp), its helpers'
        // quads spelled out: OverlayWaterTexture, OverlayCameraRenderTarget,
        // OverlayFrameBufferTexture and RenderMaterial
        // ---------------------------------------------------------------------
        private void Draw2DDebuggingInfo(in ViewSetup view)
        {
            const float half = 0.5f / 256.0f;

            if (CvBool(_in, RenderCvar.mat_yuv) && _in.DxLevel >= 80 && _debug.Yuv != 0)
                b.WithView(RenderOpKind.DebugScreenMaterial, view, _debug.Yuv);
            if (CvBool(_in, RenderCvar.mat_hsv) && _in.DxLevel >= 90 && _debug.Hsv != 0)
                b.WithView(RenderOpKind.DebugScreenMaterial, view, _debug.Hsv);

            int page = CvInt(_in, RenderCvar.mat_showlightmappage);
            if (page != -1)
                b.Add(RenderOpKind.DebugLightmapPage, [page], [OpacityFactor()], view);

            if (CvInt(_in, RenderCvar.cl_drawshadowtexture) != 0)
                b.AddInt(RenderOpKind.DebugShadowTexture, CvInt(_in, RenderCvar.cl_shadowtextureoverlaysize));

            if (_debug.DrawMaterial != 0)
                b.Add(RenderOpKind.DebugQuad, [_debug.DrawMaterial, 1], [100f, 100f, 0f, 0f, 0f, 0f, 1f, 1f]);

            if (CvBool(_in, RenderCvar.mat_showwatertextures))
            {
                float size = Cv(_in, RenderCvar.mat_wateroverlaysize);
                // The reflection unflipped at row 0, the refraction flipped at row 1.
                b.Add(RenderOpKind.DebugQuad, [_debug.Reflect, 0], [0f, 0f, size, size, half, 1f + half, 1f + half, half]);
                b.Add(RenderOpKind.DebugQuad, [_debug.Refract, 0], [0f, size, size, size, half, half, 1f + half, 1f + half]);
            }

            if (CvBool(_in, RenderCvar.mat_showcamerarendertarget))
            {
                float size = Cv(_in, RenderCvar.mat_wateroverlaysize);
                b.Add(RenderOpKind.DebugQuad, [_debug.CameraTarget, 0], [0f, 0f, size, size, half, half, 1f + half, 1f + half]);
            }

            if (CvBool(_in, RenderCvar.mat_showframebuffertexture))
            {
                float size = Cv(_in, RenderCvar.mat_framebuffercopyoverlaysize);
                b.Op(RenderOpKind.PushDefaultTarget);
                b.Add(RenderOpKind.DebugQuad, [_debug.FrameBuffer0, 0], [0f, 0f, size, size, half, half, 1f + half, 1f + half]);
                b.Add(RenderOpKind.DebugQuad, [_debug.FrameBuffer1, 0], [size, 0f, size, size, half, half, 1f + half, 1f + half]);
                b.Op(RenderOpKind.PopRenderTargetAndViewport);
            }

            if (_debug.DrawTexture)
                b.Add(RenderOpKind.DebugShowTexture, default, [Cv(_in, RenderCvar.mat_drawTextureScale)]);

            if (CvBool(_in, RenderCvar.r_flashlightdrawdepth))
                b.Op(RenderOpKind.DebugFlashlightDepth);
        }

        // CDebugViewRender::GenerateOverdrawForTesting: forty smoke-fog
        // overlays at alpha 20/255, then the alpha back to 0.
        private void GenerateOverdrawForTesting()
        {
            if (CvInt(_in, RenderCvar.cl_overdraw_test) == 0)
                return;
            for (int i = 0; i < 40; i++)
            {
                b.Add(RenderOpKind.SmokeFogOverlayAlpha, default, [20f / 255f]);
                b.Op(RenderOpKind.SmokeFogOverlay);
            }
            b.Add(RenderOpKind.SmokeFogOverlayAlpha, default, [0f]);
        }

        private ScreenOverlayBranch ScreenOverlayBranchOf()
        {
            if (_in.ScreenOverlay == 0)
                return ScreenOverlayBranch.None;
            if (_in.ScreenOverlayFullFrame != 0)
                return ScreenOverlayBranch.FullFrameBuffer;
            if (_in.ScreenOverlayPowerOfTwo != 0)
                return ScreenOverlayBranch.PowerOfTwoFrameBuffer;
            return ScreenOverlayBranch.Fade;
        }

        // CViewRender::SetupMain3DView: returns the clear flags the scene still owes.
        private int SetupMain3DView(in ViewSetup view, int clear)
        {
            int depthStencil = clear & (ClearDepth | ClearStencil);
            clear &= ~depthStencil;
            if ((clear & ClearColor) != 0)
                clear |= depthStencil;

            RenderTarget target = _in.HdrType == HdrFloat ? RenderTarget.FullFrameFB0 : RenderTarget.None;
            b.Push3DView(view, clear, target, depthOverload: _in.HdrType != HdrFloat);

            clear ^= depthStencil;
            if ((clear & ClearColor) != 0)
                clear &= ~(ClearColor | ClearFullTarget);
            return clear;
        }

        // ---------------------------------------------------------------------
        // CSkyboxView::Setup, PreRender3dSkyboxWorld and DrawInternal
        // ---------------------------------------------------------------------
        private bool SkyboxSetup(in ViewSetup view, ref int clear, out int skyboxVisible)
        {
            skyboxVisible = _in.SkyboxVisible;
            if (skyboxVisible != Skybox3DVisible && CvInt(_in, RenderCvar.r_3dsky) != 2)
                return false;
            if (CvInt(_in, RenderCvar.r_3dsky) == 0)
                return false;
            if (_in.SkyPresent == 0 || _in.SkyArea == 255)
                return false;

            int skyClear = clear;
            clear &= ~(ClearColor | ClearDepth | ClearStencil | ClearFullTarget);
            clear |= ClearDepth;

            int draw = DfRenderUnderwater | DfRenderAbovewater | DfRenderWater;
            if (CvBool(_in, RenderCvar.r_skybox))
                draw |= DfDrawSkybox;

            SkyboxDrawInternal(view, skyClear, draw);
            return true;
        }

        private void SkyboxDrawInternal(ViewSetup sky, int skyClear, int draw)
        {
            b.Add(RenderOpKind.Skybox3DAreaBits, [_in.SkyArea]);

            sky.ZNear = 2.0f;
            sky.ZFar = MaxTraceLength;
            Vector origin = sky.Origin;
            if (_in.SkyScale > 0)
            {
                float scale = 1.0f / _in.SkyScale;
                origin = new Vector(origin.X * scale, origin.Y * scale, origin.Z * scale);
            }
            Enable3dSkyboxFog(sky.Angles);
            Vector skyOrigin = SkyOrigin();
            origin = new Vector(origin.X + skyOrigin.X, origin.Y + skyOrigin.Y, origin.Z + skyOrigin.Z);
            sky.Origin = origin;

            b.ViewSetupVis(false, skyOrigin);
            // The sky view is CSkyboxView's own CViewSetup after the near/far,
            // scale and origin changes above; every op after this reads it.
            b.WorldViewBegin(sky, draw, skyClear);
            b.Push3DView(sky, skyClear, RenderTarget.None, depthOverload: true);
            b.SetupCurrentView(origin, sky.Angles, View3DSky);
            b.Op(RenderOpKind.ClientSystemsPreRender);

            b.Op(RenderOpKind.BeginUpdateLightmaps);
            // Stock passes `true` as iForceViewLeaf here (viewrender.cpp: BuildWorldRenderLists( true, true, -1 )):
            // the forced leaf is 1 and bUseCacheIfEnabled is -1 (true). Carried as stock has it.
            b.BuildWorldLists(true, 1, true, false, null, OpacityFactor());
            BuildRenderables(View3DSky, sky);
            b.Op(RenderOpKind.EndUpdateLightmaps);

            b.Op(RenderOpKind.WorldViewShadowTextures);
            b.ViewScene(draw);
            b.DrawWorld(0f, draw, CvBool(_in, RenderCvar.r_drawopaqueworld));
            b.DrawOpaque(DepthModeNormal);
            b.DrawTranslucent(true, false);
            b.Op(RenderOpKind.DrawNoZTranslucent);
            b.Op(RenderOpKind.FogNone);
            b.Add(RenderOpKind.GlowUpdateSkyOverlays, [sky.CacheFullSceneState], [sky.ZFar]);
            b.Op(RenderOpKind.PixelVisEndCurrentView);
            b.Op(RenderOpKind.Skybox3DAreaBitsRestore);
            b.Op(RenderOpKind.ClientSystemsPostRender);
            b.Op(RenderOpKind.FinishCurrentView);
            b.Op(RenderOpKind.PopView);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private Vector SkyOrigin() => _in.SkyOrigin;

        // ---------------------------------------------------------------------
        // CViewRender::ViewDrawScene
        // ---------------------------------------------------------------------
        public void ViewDrawScene(bool drew3dSkybox, int skyboxVisible, in ViewSetup view, int clear, int viewId, bool drawViewModel)
        {
            b.Op(RenderOpKind.ShadowPreRender);
            if (CvBool(_in, RenderCvar.r_flashlightdepthtexture) && viewId == ViewMain)
                b.WithView(RenderOpKind.ShadowComputeDepthTextures, view);

            b.SetupCurrentView(view.Origin, view.Angles, viewId);
            b.Op(RenderOpKind.ClientSystemsPreRender);
            b.ViewSetupVisEx(_in.ForceNoVis != 0, view.Origin);

            bool radialClear = false;
            if (!drew3dSkybox && skyboxVisible == SkyboxNotVisible)
            {
                b.Op(RenderOpKind.ClearColorToFogIfRadialVis);
                radialClear = true;
            }

            bool drawSkybox = CvBool(_in, RenderCvar.r_skybox);
            if (drew3dSkybox || skyboxVisible == SkyboxNotVisible)
                drawSkybox = false;

            b.Op(RenderOpKind.ParticleIncrementFrameCode);
            DrawWorldAndEntities(drawSkybox, view, clear, radialClear);

            b.Op(RenderOpKind.FogNone);
            b.Add(RenderOpKind.GlowDrawOverlays, [view.CacheFullSceneState]);
            if (viewId == ViewMain)
                b.Op(RenderOpKind.PixelVisEndCurrentView);
            b.Op(RenderOpKind.DrawPrecipitation);
            b.Op(RenderOpKind.SoundExtraUpdate);
            // CDebugViewRender::Draw3DDebuggingInfo
            b.Op(RenderOpKind.Draw3DDebugOverlays);
            b.Op(RenderOpKind.DrawLineFile);
            b.Op(RenderOpKind.ClientEffectsDraw);
            b.AddInt(RenderOpKind.SetFXCreationAllowed, 0);
            b.Op(RenderOpKind.ClientSystemsPostRender);
            b.Op(RenderOpKind.FinishCurrentView);
            if (CvBool(_in, RenderCvar.r_flashlightdepthtexture))
                b.Op(RenderOpKind.ShadowUnlockDepthTextures);
        }

        // ---------------------------------------------------------------------
        // CViewRender::DrawOneMonitor, for a managed second camera: its view
        // pushed into its target with colour and depth cleared, the scene drawn
        // as VIEW_MONITOR with the 2D sky, popped. The water the frame's inputs
        // describe is the main eye's, so a monitor view draws none (its own fog
        // volume is not asked; docs/todo.md).
        // ---------------------------------------------------------------------
        private void DrawMonitor(in MonitorView monitor)
        {
            RenderInputs dry = _in;
            dry.FogVolume = -1;
            dry.FogVolumeLeaf = -1;
            dry.EyeInFogVolume = 0;
            dry.WaterMaterial = 0;
            var frame = new Frame(dry, b);

            if (monitor.DrawViewer)
                b.Op(RenderOpKind.ViewerSeenBegin);
            b.Push3DView(monitor.View, ClearDepth | ClearColor, monitor.Target, depthOverload: false);
            frame.ViewDrawScene(false, Skybox2DVisible, monitor.View, 0, ViewMonitor, false);
            b.Op(RenderOpKind.PopView);
            if (monitor.DrawViewer)
                b.Op(RenderOpKind.ViewerSeenEnd);
            b.AddInt(RenderOpKind.ViewTargetDrawn, monitor.TargetNumber);
        }

        // ---------------------------------------------------------------------
        // CViewRender::DetermineWaterRenderInfo
        // ---------------------------------------------------------------------
        private WaterInfo DetermineWaterRenderInfo()
        {
            var info = new WaterInfo { CheapWater = true, OpaqueWater = true };
            if (_in.FogVolume == -1 || _in.WaterMaterial == 0)
                return info;

            info.DrawWaterSurface = CvBool(_in, RenderCvar.mat_drawwater);
            if (!info.DrawWaterSurface)
            {
                info.OpaqueWater = false;
                return info;
            }

            bool forceExpensive = CvBool(_in, RenderCvar.r_waterforceexpensive);
            bool forceReflectEntities = CvBool(_in, RenderCvar.r_waterforcereflectentities);

            info.OpaqueWater = _in.WaterTranslucent == 0;
            if (_in.DxLevel < 80)
                return info;

            bool forceCheap = false;
            if (_in.WaterForceCheapDefined != 0)
            {
                forceCheap = _in.WaterForceCheap != 0;
                if (forceCheap)
                    forceExpensive = false;
            }
            if (!forceCheap && _in.WaterForceExpensiveDefined != 0)
                forceExpensive = forceExpensive || _in.WaterForceExpensive != 0;

            bool localReflection = forceExpensive && CvBool(_in, RenderCvar.r_WaterDrawReflection) && _in.WaterReflectTexture != 0;

            if ((_in.DistanceToWater >= _in.CheapWaterEnd && !localReflection) || forceCheap)
                return info;

            if (!CvBool(_in, RenderCvar.r_WaterDrawRefraction))
            {
                info.Refract = false;
            }
            else
            {
                info.Refract = _in.WaterRefractTexture != 0;
                if (info.Refract)
                    info.OpaqueWater = false;
            }

            info.Reflect = localReflection;
            if (info.Reflect)
                info.ReflectEntities = forceReflectEntities || _in.WaterReflectEntities != 0;

            info.CheapWater = !info.Reflect && !info.Refract;
            return info;
        }

        // ---------------------------------------------------------------------
        // CViewRender::DrawWorldAndEntities
        // ---------------------------------------------------------------------
        private void DrawWorldAndEntities(bool drawSkybox, in ViewSetup view, int clear, bool radialClear)
        {
            WaterInfo info = DetermineWaterRenderInfo();
            if (info.CheapWater)
            {
                SimpleWorldView(view, clear, radialClear, drawSkybox, info);
                return;
            }

            int fogLeaf = CvBool(_in, RenderCvar.r_ForceWaterLeaf) ? _in.FogVolumeLeaf : -1;
            if (_in.EyeInFogVolume == 0)
                AboveWaterView(view, drawSkybox, info, fogLeaf);
            else
                UnderWaterView(view, drawSkybox, info);
        }

        // ---------------------------------------------------------------------
        // CSimpleWorldView::Setup and Draw
        // ---------------------------------------------------------------------
        private void SimpleWorldView(in ViewSetup view, int clear, bool radialClear, bool drawSkybox, in WaterInfo info)
        {
            bool eyeIn = _in.EyeInFogVolume != 0;
            int draw = DfDrawEntities;
            if (!info.OpaqueWater)
            {
                draw |= DfRenderUnderwater | DfRenderAbovewater;
            }
            else
            {
                bool intersects = _in.FogVolume != -1 && _in.ViewPlaneIntersectsWater != 0;
                if (intersects)
                    draw |= DfRenderUnderwater | DfRenderAbovewater;
                else if (eyeIn)
                    draw |= DfRenderUnderwater;
                else
                    draw |= DfRenderAbovewater;
            }
            if (info.DrawWaterSurface)
                draw |= DfRenderWater;
            if (!eyeIn && drawSkybox)
                draw |= DfDrawSkybox;

            b.WorldViewBegin(view, draw, clear);
            DrawSetup(view, 0f, draw, -1, ViewMain, adjusted: null);

            if (!eyeIn)
            {
                EnableWorldFog(view.Angles);
            }
            else
            {
                clear |= ClearColor;
                b.FogVolumeState(_in.FogVolume, false);
                b.ClearColorFromFogColor(255);
            }

            DrawExecute(view, draw, clear, radialClear, 0f, ViewMain, 0f, null);
            b.ClearColor4ub(0, 0, 0, 255);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        // CRendering3dView::UpdateRenderablesOpacity's factor: no prop fade in the level overview.
        private float OpacityFactor() => Cv(_in, RenderCvar.cl_leveloverview) > 0f ? -1f : 1f;

        // CRendering3dView::BuildRenderableRenderLists with SetupRenderablesList's decisions.
        private void BuildRenderables(int viewId, in ViewSetup view)
        {
            bool entities = CvInt(_in, RenderCvar.r_drawentities) != 0;
            bool detail = _in.ClientModeDrawDetail != 0 && CvInt(_in, RenderCvar.r_DrawDetailProps) != 0;
            float maxDist = Cv(_in, RenderCvar.cl_maxrenderable_dist);
            float dist = viewId == ViewShadowDepthTexture ? (view.ZFar < maxDist ? view.ZFar : maxDist) : maxDist;
            b.BuildRenderables(viewId, entities, detail, viewId != ViewShadowDepthTexture, dist * dist);
        }

        // ---------------------------------------------------------------------
        // CBaseWorldView::DrawSetup (AdjustView folded in: `adjusted` is the
        // view AdjustView produced, null when it changed nothing)
        // ---------------------------------------------------------------------
        private void DrawSetup(in ViewSetup view, float waterHeight, int setupFlags, int forceLeaf, int savedViewId, ViewSetup? adjusted)
        {
            if (adjusted is { } a)
                b.Push3DView(a, 0, RenderTarget.None, depthOverload: false);

            b.Op(RenderOpKind.BeginUpdateLightmaps);
            bool entities = (setupFlags & DfDrawEntities) != 0;
            bool reflection = (setupFlags & DfRenderReflection) != 0;
            b.BuildWorldLists(entities, forceLeaf, true, false, reflection ? waterHeight : null, OpacityFactor());
            b.Op(RenderOpKind.PruneWorldList);
            if (entities)
                BuildRenderables(savedViewId, view);
            b.Op(RenderOpKind.EndUpdateLightmaps);

            if (adjusted is not null)
                b.Op(RenderOpKind.PopView);
        }

        // ---------------------------------------------------------------------
        // CBaseWorldView::DrawExecute, with PushView and PopView
        // ---------------------------------------------------------------------
        private void DrawExecute(in ViewSetup view, int draw, int clear, bool radialClear, float waterHeight, int viewId, float waterZAdjust, ViewSetup? adjusted)
        {
            ViewSetup self = adjusted ?? view;

            b.Op(RenderOpKind.WorldViewShadowTextures);
            b.Op(RenderOpKind.SoundExtraUpdate);

            PushView(self, draw, clear, radialClear, waterHeight);

            b.FrameBufferCopyPush(_in.DxLevel >= 80);

            b.ViewScene(draw);
            bool opaqueWorld = CvBool(_in, RenderCvar.r_drawopaqueworld);
            if ((draw & DfDrawEntities) != 0)
            {
                b.DrawWorld(waterZAdjust, draw, opaqueWorld);
                b.DrawOpaque(DepthModeNormal);
                b.DrawTranslucent(false, false);
                b.Op(RenderOpKind.DrawNoZTranslucent);
            }
            else
            {
                b.DrawWorld(waterZAdjust, draw, opaqueWorld);
                b.DrawTranslucentWorldInLeaves(false);
            }

            if (viewId != ViewMain && viewId != ViewIntroCamera)
                b.Op(RenderOpKind.PixelVisEndCurrentView);

            b.Op(RenderOpKind.FrameBufferCopyPop);

            // CBaseWorldView::PopView
            b.SetHeightClipMode(HeightClipDisable);
            if ((draw & (DfRenderRefraction | DfRenderReflection)) != 0)
            {
                b.Op(RenderOpKind.PopView);
                b.Op(RenderOpKind.RestoreWaterLightmapScale);
            }
        }

        private void PushView(in ViewSetup self, int draw, int clear, bool radialClear, float waterHeight)
        {
            const float spread = 2.0f;
            if ((draw & DfFudgeUp) != 0)
                waterHeight += spread;
            else
                waterHeight -= spread;

            int clipMode = HeightClipDisable;
            if ((draw & DfClipZ) != 0 && CvBool(_in, RenderCvar.mat_clipz))
                clipMode = (draw & DfClipBelow) != 0 ? HeightClipRenderAboveHeight : HeightClipRenderBelowHeight;

            if ((draw & DfRenderRefraction) != 0)
            {
                b.SetFogZ(waterHeight);
                b.SetHeightClipZ(waterHeight);
                b.SetHeightClipMode(clipMode);
                b.Push3DView(self, clear, RenderTarget.WaterRefraction, depthOverload: false);
                return;
            }

            if ((draw & DfRenderReflection) != 0)
            {
                b.SetFogZ(waterHeight);
                float eps = Cv(_in, RenderCvar.r_eyewaterepsilon);
                if (_in.UseFastClipping != 0 && self.Origin.Z > waterHeight - eps)
                    waterHeight = self.Origin.Z + eps;
                b.SetHeightClipZ(waterHeight);
                b.SetHeightClipMode(clipMode);
                b.Push3DView(self, clear, RenderTarget.WaterReflection, depthOverload: false);
                if (_in.HdrType == HdrInteger)
                    b.Op(RenderOpKind.WaterLightmapScale);
                return;
            }

            b.ClearForView(clear, radialClear);
            b.SetHeightClipMode(clipMode);
            if (clipMode != HeightClipDisable)
                b.SetHeightClipZ(waterHeight);
        }

        // ---------------------------------------------------------------------
        // CBaseWaterView::CalcWaterEyeAdjustments
        // ---------------------------------------------------------------------
        private void CalcWaterEyeAdjustments(in ViewSetup view, bool softwareClip, out float newWaterHeight, out float waterZAdjust)
        {
            newWaterHeight = _in.WaterHeight;
            waterZAdjust = 0f;
            if (!softwareClip)
                return;
            float delta = view.Origin.Z - _in.WaterHeight;
            float eps = Cv(_in, RenderCvar.r_eyewaterepsilon);
            if (MathF.Abs(delta) < eps)
            {
                newWaterHeight = delta > 0 ? view.Origin.Z - eps : view.Origin.Z + eps;
                waterZAdjust = newWaterHeight - _in.WaterHeight;
            }
        }

        // ---------------------------------------------------------------------
        // CAboveWaterView and its reflection, refraction and intersection views
        // ---------------------------------------------------------------------
        private void AboveWaterView(in ViewSetup view, bool drawSkybox, in WaterInfo info, int fogLeaf)
        {
            bool softwareClip = _in.UseFastClipping != 0;
            CalcWaterEyeAdjustments(view, softwareClip, out float waterHeight, out float waterZAdjust);
            if (waterZAdjust == 0f)
                softwareClip = false;

            int draw = DfRenderAbovewater | DfDrawEntities;
            int clear = ClearDepth;
            if (drawSkybox)
                draw |= DfDrawSkybox;
            if (info.DrawWaterSurface)
                draw |= DfRenderWater;
            if (!info.Refract && !info.OpaqueWater)
                draw |= DfRenderUnderwater;

            if (info.Reflect)
                ReflectionView(view, info.ReflectEntities, fogLeaf);

            bool intersects = false;
            if (info.Refract)
            {
                RefractionViewAbove(view, waterHeight, waterZAdjust);
                if (!softwareClip)
                    intersects = _in.FogVolume != -1 && _in.ViewPlaneIntersectsWater != 0;
            }
            else if ((draw & DfDrawSkybox) == 0)
            {
                clear |= ClearColor;
            }

            bool hardwareClip = _in.UseFastClipping == 0;
            if (intersects && hardwareClip)
                draw |= DfClipZ | DfClipBelow;

            b.WorldViewBegin(view, draw, clear);
            DrawSetup(view, waterHeight, draw, -1, ViewMain, adjusted: null);
            EnableWorldFog(view.Angles);
            DrawExecute(view, draw, clear, false, waterHeight, ViewMain, waterZAdjust, null);

            if (info.Refract)
            {
                if (softwareClip)
                    SoftwareIntersectionView(view, aboveWater: true, waterHeight, waterZAdjust);
                else if (intersects)
                    IntersectionView(view);
            }
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private void ReflectionView(in ViewSetup view, bool reflectEntities, int fogLeaf)
        {
            int clear = ClearDepth;
            int draw = DfRenderReflection | DfClipZ | DfClipBelow | DfRenderAbovewater | DfDrawSkybox;
            if (reflectEntities)
                draw |= DfDrawEntities;

            // CBaseWorldView::AdjustView, the reflection's arm.
            ViewSetup adjusted = view;
            adjusted.X = 0;
            adjusted.Y = 0;
            adjusted.Width = _in.ReflectionWidth;
            adjusted.Height = _in.ReflectionHeight;
            if (adjusted.ViewToProjectionOverride != 0)
                adjusted.SetViewToProjection(1 * 4 + 2, -adjusted.GetViewToProjection(1 * 4 + 2));
            Vector angles = adjusted.Angles;
            adjusted.Angles = new Vector(-angles.X, angles.Y, -angles.Z);
            Vector origin = adjusted.Origin;
            adjusted.Origin = new Vector(origin.X, origin.Y, origin.Z - 2.0f * (origin.Z - _in.WaterHeight));

            b.WorldViewBegin(adjusted, draw, clear);
            b.SetupCurrentView(view.Origin, view.Angles, ViewReflection);
            DrawSetup(view, _in.WaterHeight, draw, fogLeaf, ViewReflection, adjusted);
            EnableWorldFog(view.Angles);
            DrawExecute(view, draw, clear, false, _in.WaterHeight, ViewReflection, 0f, adjusted);
            // Stock restores the saved id with the view's own origin and angles,
            // which AdjustView reflected.
            b.SetupCurrentView(adjusted.Origin, adjusted.Angles, ViewMain);
            b.Flush(false);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private void RefractionViewAbove(in ViewSetup view, float waterHeight, float waterZAdjust)
        {
            int clear = ClearColor | ClearDepth;
            int draw = DfRenderRefraction | DfClipZ | DfRenderUnderwater | DfFudgeUp | DfDrawEntities;

            ViewSetup adjusted = view;
            adjusted.X = 0;
            adjusted.Y = 0;
            adjusted.Width = _in.RefractionWidth;
            adjusted.Height = _in.RefractionHeight;

            b.WorldViewBegin(adjusted, draw, clear);
            b.SetupCurrentView(view.Origin, view.Angles, ViewRefraction);
            DrawSetup(view, waterHeight, draw, -1, ViewRefraction, adjusted);
            b.FogVolumeState(_in.FogVolume, true);
            b.Op(RenderOpKind.SetClearColorToFogColor);
            DrawExecute(view, draw, clear, false, waterHeight, ViewRefraction, waterZAdjust, adjusted);
            b.SetupCurrentView(adjusted.Origin, adjusted.Angles, ViewMain);
            b.ClearColor4ub(0, 0, 0, 255);
            b.Flush(false);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private void IntersectionView(in ViewSetup view)
        {
            int draw = DfRenderUnderwater | DfClipZ | DfDrawEntities;
            b.WorldViewBegin(view, draw, 0);
            DrawSetup(view, _in.WaterHeight, draw, -1, ViewMain, adjusted: null);
            b.FogVolumeState(_in.FogVolume, true);
            b.Op(RenderOpKind.SetClearColorToFogColor);
            DrawExecute(view, draw, 0, false, _in.WaterHeight, ViewNone, 0f, null);
            b.ClearColor4ub(0, 0, 0, 255);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private void SoftwareIntersectionView(in ViewSetup view, bool aboveWater, float waterHeight, float waterZAdjust)
        {
            int draw = aboveWater ? DfRenderUnderwater : DfRenderAbovewater;
            b.WorldViewBegin(view, draw, 0);
            DrawSetup(view, waterHeight, draw, -1, ViewMain, adjusted: null);
            DrawExecute(view, draw, 0, false, waterHeight, ViewMain, waterZAdjust, null);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        // ---------------------------------------------------------------------
        // CUnderWaterView and its refraction view
        // ---------------------------------------------------------------------
        private void UnderWaterView(in ViewSetup view, bool drawSkybox, in WaterInfo info)
        {
            bool softwareClip = _in.UseFastClipping != 0;
            CalcWaterEyeAdjustments(view, softwareClip, out float waterHeight, out float waterZAdjust);

            if (_in.DxLevel >= 90 && _in.WaterUnderwaterOverlay == 2)
                b.Op(RenderOpKind.SetUnderwaterOverlay);

            int draw = DfFudgeUp | DfRenderUnderwater | DfDrawEntities;
            int clear = ClearDepth;
            if (!softwareClip)
                draw |= DfClipZ;
            if (info.DrawWaterSurface)
                draw |= DfRenderWater;
            if (!info.Refract && !info.OpaqueWater)
                draw |= DfRenderAbovewater;

            if (info.Refract)
                RefractionViewUnder(view, drawSkybox, waterHeight, waterZAdjust);

            if (!info.Refract)
            {
                b.FogVolumeState(_in.FogVolume, true);
                b.ClearColorFromFogColor(255);
            }

            b.WorldViewBegin(view, draw, clear);
            DrawSetup(view, waterHeight, draw, -1, ViewMain, adjusted: null);
            b.FogVolumeState(_in.FogVolume, false);
            DrawExecute(view, draw, clear, false, waterHeight, ViewMain, waterZAdjust, null);

            if (waterZAdjust != 0f && softwareClip && info.Refract)
                SoftwareIntersectionView(view, aboveWater: false, waterHeight, waterZAdjust);
            b.ClearColor4ub(0, 0, 0, 255);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        private void RefractionViewUnder(in ViewSetup view, bool drawSkybox, float waterHeight, float waterZAdjust)
        {
            int draw = DfClipZ | DfClipBelow | DfRenderAbovewater | DfDrawEntities;
            int clear = ClearDepth;
            if (drawSkybox)
            {
                clear |= ClearColor;
                draw |= DfDrawSkybox | DfClipSkybox;
            }

            b.WorldViewBegin(view, draw, clear);
            b.FogVolumeState(_in.FogVolume, true);
            b.ClearColorFromFogColor(255);
            DrawSetup(view, waterHeight, draw, -1, ViewMain, adjusted: null);
            EnableWorldFog(view.Angles);
            DrawExecute(view, draw, clear, false, waterHeight, ViewRefraction, waterZAdjust, null);
            b.CopyRefractionTarget(view.X, view.Y, view.Width, view.Height);
            b.Op(RenderOpKind.WorldViewEnd);
        }

        // ---------------------------------------------------------------------
        // CViewRender::DrawViewModels: the viewmodel's view and whether the
        // player's viewmodel draws. (Stock's tools' viewmodels are deleted: SFM
        // is dead, user ruling 2026-10-19.)
        // ---------------------------------------------------------------------
        private void DrawViewModels(in ViewSetup view, bool drawViewModel)
        {
            ViewSetup vm = view;
            vm.ZNear = view.ZNearViewmodel;
            vm.ZFar = view.ZFarViewmodel;
            vm.Fov = view.FovViewmodel;
            vm.AspectRatio = _in.ScreenAspect;
            b.Add(RenderOpKind.ViewModels, [ShouldDrawViewModel(drawViewModel) ? 1 : 0], null, vm);
        }

        private bool ShouldDrawViewModel(bool drawViewModel)
        {
            if (!drawViewModel)
                return false;
            if (!CvBool(_in, RenderCvar.r_drawviewmodel))
                return false;
            if (_in.ThirdPersonSeesOwner != 0)
                return false;
            if (!CvBool(_in, RenderCvar.r_drawentities))
                return false;
            if (_in.ViewEntity > _in.MaxClients)
                return false;
            return true;
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::EnableWorldFog and its GetFog* helpers
        // ---------------------------------------------------------------------
        private void EnableWorldFog(Vector currentViewAngles)
        {
            if (FogMath.FogEnable(_in))
            {
                Vector color = FogMath.FogColor(_in, FogMath.Forward(currentViewAngles));
                b.FogLinear(color, FogMath.FogStart(_in), FogMath.FogEnd(_in), FogMath.FogMaxDensity(_in), FogMath.FogRadial(_in));
            }
            else
            {
                b.Op(RenderOpKind.FogNone);
            }
        }

        // CSkyboxView::Enable3dSkyboxFog
        private void Enable3dSkyboxFog(Vector skyAngles)
        {
            if (_in.SkyPresent == 0)
                return;
            if (FogMath.SkyFogEnable(_in))
            {
                Vector color = FogMath.SkyFogColor(_in, FogMath.Forward(skyAngles));
                float scale = 1.0f;
                if (_in.SkyScale > 0)
                    scale = 1.0f / _in.SkyScale;
                b.FogLinear(color, FogMath.SkyFogStart(_in) * scale, FogMath.SkyFogEnd(_in) * scale,
                            FogMath.SkyFogMaxDensity(_in), FogMath.SkyFogRadial(_in));
            }
            else
            {
                b.Op(RenderOpKind.FogNone);
            }
        }
    }
}

/// <summary>
/// One second-camera view for this frame's plan (C7 item 6): its setup, the
/// target it draws into, which view target that is (for its count), and
/// whether the local player's body draws in it.
/// </summary>
public readonly record struct MonitorView(ViewSetup View, RenderTarget Target, int TargetNumber, bool DrawViewer)
{
    /// <summary>
    /// The CViewSetup shell_view_targets.cpp built from a managed
    /// ViewTargetSetup: the top left of the target at the setup's size, its
    /// origin, angles and projection, no bloom, no full-scene cache.
    /// </summary>
    public static MonitorView From(in ViewTargetSetup s, RenderTarget target, int number)
    {
        var v = new ViewSetup
        {
            X = 0, Y = 0, UnscaledX = 0, UnscaledY = 0,
            Width = s.Width, Height = s.Height, UnscaledWidth = s.Width, UnscaledHeight = s.Height,
            Origin = new Vector(s.OriginX, s.OriginY, s.OriginZ),
            Angles = new Vector(s.Pitch, s.Yaw, s.Roll),
            Fov = s.Fov, FovViewmodel = s.Fov,
            ZNear = s.ZNear, ZNearViewmodel = s.ZNear, ZFar = s.ZFar, ZFarViewmodel = s.ZFar,
            Ortho = (s.Flags & ViewTargetFlags.Ortho) != 0 ? 1 : 0,
            OrthoLeft = s.OrthoLeft, OrthoTop = s.OrthoTop, OrthoRight = s.OrthoRight, OrthoBottom = s.OrthoBottom,
            AspectRatio = s.Height > 0 ? (float)s.Width / s.Height : 1f,
            RenderToSubrect = 1,
            DoBloomAndToneMapping = 0,
            CacheFullSceneState = 0,
        };
        return new MonitorView(v, target, number, (s.Flags & ViewTargetFlags.DrawViewer) != 0);
    }
}

/// <summary>
/// The debug overlays' materials for one plan (C7 step 2), as IFxRender ids:
/// the host finds them by name (viewdebug.cpp's fixed names, and
/// cl_drawmaterial's), the planner places them. 0 is no material. DrawTexture
/// is mat_drawTexture non-empty; native reads its name to carry the overlay out.
/// </summary>
public readonly record struct DebugOverlayMaterials(int Yuv, int Hsv, int Reflect, int Refract, int CameraTarget,
                                                   int FrameBuffer0, int FrameBuffer1, int DrawMaterial, bool DrawTexture)
{
    public static readonly DebugOverlayMaterials None = default;
}
