using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The frame View_Render hands managed (<c>SourceSharpRenderFrameIn_t</c>,
/// sourcesharp.h): the rect the engine gave it and the frame's clock. Held by
/// RenderPlanAbiParityTests.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderFrameIn
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int FrameCount;
    public float CurTime;
    public int LastPath;
    public float LastRenderMs;

    // CViewRender::Render's inputs (view.cpp; C7 step 3 phase 2, appended).
    /// <summary>The mono eye as SetUpViews and the client mode's override left it.</summary>
    public ViewSetup Eye;
    /// <summary>engine->GetScreenAspectRatio().</summary>
    public float ScreenAspect;
    public int Windowed;
    public int MaxClients;
    /// <summary>sv_restrict_aspect_ratio_fov.</summary>
    public int RestrictAspectFov;
    /// <summary>mat_viewportscale.</summary>
    public float ViewportScale;
    public int GlClear;
    public int GlClearRandomColor;
    /// <summary>Posix on an ATI adapter (vendor 0x1002) with more than one AA sample: stock clears colour.</summary>
    public int ClearColorForAntialiasing;
    /// <summary>The client mode's ShouldDrawViewModel.</summary>
    public int ClientModeDrawViewModel;
    /// <summary>cl_leveloverview.</summary>
    public float LevelOverview;
    /// <summary>The view rectangle after the client mode's and the tools' AdjustEngineViewport.</summary>
    public int ViewportX;
    public int ViewportY;
    public int ViewportWidth;
    public int ViewportHeight;
}

/// <summary>
/// Managed's answer for the frame (<c>SourceSharpRenderFrameOut_t</c>): Path
/// (always 1, the plan: the stock path left at C7 step 3; the field is the
/// ABI's), whether native records the frame, the tag the recording comes back
/// with, and the frame's view decision.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderFrameOut
{
    public int Path;
    public int Trace;
    public int TraceTag;
    public int Reserved;

    // CViewRender::Render's decisions (C7 step 3 phase 2, appended).
    /// <summary>The eye RenderView draws: FOVs scaled to the screen, the viewport, the aspect, the overview's ortho.</summary>
    public ViewSetup Eye;
    public int ClearFlags;
    /// <summary>RenderView's whatToDraw (RENDERVIEW_*).</summary>
    public int DrawFlags;
    /// <summary>gl_clear_randomcolor: clear the colour buffer to ( ClearR, ClearG, ClearB ) first.</summary>
    public int RandomClear;
    public int ClearR;
    public int ClearG;
    public int ClearB;
    /// <summary>cl_leveloverview: SetUpOverView ran, and the clear colour is 0,255,0,255.</summary>
    public int Overview;
}

/// <summary>SetUpViews' inputs before the client mode's override (view.cpp).</summary>
public struct ViewSetUpIn
{
    public float FarZ;
    public float MapExtents;
    /// <summary>The viewer's fog controller's far Z, or 0 with none.</summary>
    public float FogFarZ;
    public float DefaultFov;
    /// <summary>The client mode's GetViewModelFOV.</summary>
    public float ViewModelFov;
}

/// <summary>
/// SetUpViews' decisions. After the client mode's override, native carries out
/// <c>fovViewmodel = fov + ViewModelFovOffset</c>: the offset is managed's
/// (stock's <c>GetViewModelFOV() - ( default_fov - fov )</c>, rearranged), and the
/// override's fov is the one operand only native has. A second crossing after
/// the override would ask managed for one addition.
/// </summary>
public struct ViewSetUpOut
{
    public float ZFar;
    public float ZFarViewmodel;
    public float ZNear;
    public float ZNearViewmodel;
    public float Fov;
    public float ViewModelFovOffset;
}

/// <summary>
/// The render plan as View_Render asks it (plan_client_novalve C7): implemented
/// by the game assembly's <c>RenderPlanHost</c>.
/// </summary>
public interface IClientRenderPlan
{
    /// <summary>Whether the frame is recorded, and its view decision.</summary>
    void Frame(in RenderFrameIn frame, ref RenderFrameOut answer);

    /// <summary>A recorded frame's text (shell_render_trace_client.cpp's lines), with the tag <see cref="Frame"/> gave it.</summary>
    void TraceDone(int tag, string text);

    /// <summary>
    /// The plan for one RenderView: the ops written into <paramref name="ops"/>
    /// and their count, or -1 when stock should draw this one (a frame the plan
    /// does not express, or more ops than native's buffer holds).
    /// </summary>
    int BuildPlan(in RenderInputs input, Span<RenderOp> ops);

    /// <summary>
    /// One world view's draws over its lists (C7 step 2): the scene native
    /// gathered at the frame's plan's ViewScene op (<c>SourceSharpViewScene_t</c>
    /// and what follows it, as bytes), answered with the view's program -- one
    /// section per list-dependent draw op -- and its op count, or -1 when native
    /// should draw the view with stock's primitives.
    /// </summary>
    int BuildViewProgram(ReadOnlySpan<byte> scene, Span<RenderOp> ops);

    /// <summary>
    /// viewpostprocess.cpp's passes at the plan's EnginePostProcessing or
    /// MotionBlur op (C7 post-processing): the ops written into
    /// <paramref name="ops"/> and their count, or -1 when stock's function
    /// should run.
    /// </summary>
    int BuildPostProgram(in PostProcessInputs input, Span<RenderOp> ops);

    /// <summary>
    /// A shadow-depth view (CViewRender::UpdateShadowDepthTexture), asked inside
    /// the frame's ShadowComputeDepthTextures op: the ops and their count, with
    /// <paramref name="indexBase"/> the index the view's ViewScene ops are
    /// counted from; -1 when there is no plan to draw it in.
    /// </summary>
    int BuildShadowDepthPlan(in ViewSetup shadow, Span<RenderOp> ops, out int indexBase);

    /// <summary>CViewRender::SetUpViews' planes and FOV, asked before the client mode's override.</summary>
    void ViewSetUp(in ViewSetUpIn input, out ViewSetUpOut output);

    /// <summary>The save-game screenshot's view of <paramref name="eye"/> at <paramref name="width"/> by <paramref name="height"/>.</summary>
    ViewSetup ViewScreenshot(in ViewSetup eye, int width, int height);

    /// <summary>CViewRender::FreezeFrame: <paramref name="freezeTime"/> seconds of the captured frame from <paramref name="curTime"/>; 0 ends it.</summary>
    void ViewFreezeFrame(float freezeTime, float curTime);

    /// <summary>
    /// The demo free camera: stage 0 with the view (origin, angles) answers 1 and
    /// the engine angles to set when cl_demoviewoverride is on, else 0; stage 1
    /// with the mouse's angles and the frame time writes the view (origin, angles).
    /// </summary>
    int ViewDemoCamera(int stage, ReadOnlySpan<float> input, Span<float> output);

    /// <summary>A fade from native into the view effects (the engine's View_Fade, the world's start-dark fade).</summary>
    void ViewEffectsFade(int duration, int holdTime, int flags, byte r, byte g, byte b, byte a, float curTime);

    /// <summary>The view's CalcShake: the offset (three) and roll, written to <paramref name="output"/>.</summary>
    void ViewEffectsCalcShake(float curTime, float frameTime, Span<float> output);

    /// <summary>GetFadeParams: r, g, b, a and modulate, written to <paramref name="output"/>.</summary>
    void ViewEffectsFadeParams(float curTime, Span<int> output);

    /// <summary>0 LevelInit, 1 ClearAllFades, 2 ClearPermanentFades.</summary>
    void ViewEffectsReset(int which);
}

/// <summary>A <c>CViewSetup</c> field for field (<c>SourceSharpViewSetup_t</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ViewSetup
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int UnscaledX;
    public int UnscaledY;
    public int UnscaledWidth;
    public int UnscaledHeight;
    public int StereoEye;
    public int Ortho;
    public float OrthoLeft;
    public float OrthoTop;
    public float OrthoRight;
    public float OrthoBottom;
    public float Fov;
    public float FovViewmodel;
    public fixed float OriginXYZ[3];
    public fixed float AnglesXYZ[3];
    public float ZNear;
    public float ZFar;
    public float ZNearViewmodel;
    public float ZFarViewmodel;
    public int RenderToSubrect;
    public float AspectRatio;
    public int OffCenter;
    public float OffCenterTop;
    public float OffCenterBottom;
    public float OffCenterLeft;
    public float OffCenterRight;
    public int DoBloomAndToneMapping;
    public int CacheFullSceneState;
    public int ViewToProjectionOverride;
    public fixed float ViewToProjection[16];

    public float GetViewToProjection(int i) { fixed (float* p = ViewToProjection) return p[i]; }
    public void SetViewToProjection(int i, float v) => ViewToProjection[i] = v;

    public Vector Origin
    {
        readonly get { fixed (float* p = OriginXYZ) return new Vector(p[0], p[1], p[2]); }
        set { OriginXYZ[0] = value.X; OriginXYZ[1] = value.Y; OriginXYZ[2] = value.Z; }
    }

    public Vector Angles
    {
        readonly get { fixed (float* p = AnglesXYZ) return new Vector(p[0], p[1], p[2]); }
        set { AnglesXYZ[0] = value.X; AnglesXYZ[1] = value.Y; AnglesXYZ[2] = value.Z; }
    }
}

/// <summary>A <c>fogparams_t</c> as the plan reads it (<c>SourceSharpFogParams_t</c>); colours 0..255.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RenderFogParams
{
    public int Present;
    public fixed float DirPrimaryXYZ[3];
    public fixed int ColorPrimary[3];
    public fixed int ColorSecondary[3];
    public fixed int ColorPrimaryLerpTo[3];
    public fixed int ColorSecondaryLerpTo[3];
    public float Start;
    public float End;
    public float MaxDensity;
    public float StartLerpTo;
    public float EndLerpTo;
    public float LerpTime;
    public float Duration;
    public int Enable;
    public int Blend;
    public int Radial;
}

/// <summary>Safe readers over <see cref="RenderFogParams"/>' arrays, for code built without unsafe.</summary>
public static unsafe class RenderFogParamsExtensions
{
    public static Vector DirPrimary(this in RenderFogParams f) { fixed (float* p = f.DirPrimaryXYZ) return new Vector(p[0], p[1], p[2]); }
    public static Vector Primary(this in RenderFogParams f) { fixed (int* p = f.ColorPrimary) return new Vector(p[0], p[1], p[2]); }
    public static Vector Secondary(this in RenderFogParams f) { fixed (int* p = f.ColorSecondary) return new Vector(p[0], p[1], p[2]); }
    public static Vector PrimaryLerpTo(this in RenderFogParams f) { fixed (int* p = f.ColorPrimaryLerpTo) return new Vector(p[0], p[1], p[2]); }
    public static Vector SecondaryLerpTo(this in RenderFogParams f) { fixed (int* p = f.ColorSecondaryLerpTo) return new Vector(p[0], p[1], p[2]); }
}

/// <summary>
/// Everything RenderView's decisions read (<c>SourceSharpRenderInputs_t</c>),
/// gathered by the executor at RenderView's entry. <see cref="Cvars"/> is
/// indexed by <see cref="RenderCvar"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RenderInputs
{
    public ViewSetup View;
    public int ClearFlags;
    public int WhatToDraw;
    public float CurTime;
    public float FrameTime;
    public int FrameCount;
    public int MaxClients;
    public int HdrType;
    public int DxLevel;
    public int UseFastClipping;
    public int IsHammerRunning;
    public float ScreenAspect;
    public int ClientModeDrawFog;
    public int FreezeActive;
    public int ForceNoVis;
    public int PointContentsSolid;
    public int SkyboxVisible;
    public int SkyPresent;
    public int SkyScale;
    public fixed float SkyOriginXYZ[3];
    public int SkyArea;
    public RenderFogParams SkyFog;
    public RenderFogParams Fog;
    public int FogVolume;
    public int FogVolumeLeaf;
    public int EyeInFogVolume;
    public float DistanceToWater;
    public float WaterHeight;
    public int WaterMaterial;
    public int WaterTranslucent;
    public int WaterForceCheapDefined;
    public int WaterForceCheap;
    public int WaterForceExpensiveDefined;
    public int WaterForceExpensive;
    public int WaterReflectTexture;
    public int WaterRefractTexture;
    public int WaterReflectEntities;
    public int WaterUnderwaterOverlay;
    public int ViewPlaneIntersectsWater;
    public float CheapWaterStart;
    public float CheapWaterEnd;
    public int GlassInView;
    public fixed float GlassPlane[4];
    public int ReflectionWidth;
    public int ReflectionHeight;
    public int RefractionWidth;
    public int RefractionHeight;
    public int ThirdPersonSeesOwner;
    public int ViewEntity;
    public int ToolsEnabled;
    public fixed int FadeColor[4];
    public int FadeBlend;
    public int ScreenOverlay;
    public int ScreenOverlayFullFrame;
    public int ScreenOverlayPowerOfTwo;
    public int FlashlightOn;
    public fixed float Cvars[64];
    /// <summary>The client mode's ShouldDrawDetailObjects (appended: the struct only grows at its end).</summary>
    public int ClientModeDrawDetail;
    /// <summary>
    /// The freeze frame's capture is due (stock's m_rbTakeFreezeFrame, appended):
    /// this frame draws and copies itself to _rt_FullScreen for the frames
    /// <see cref="FreezeActive"/> shows it.
    /// </summary>
    public int FreezeTake;
    /// <summary>Stock's g_flFreezeFlash: the freeze frame's white flash fades out until this time.</summary>
    public float FreezeFlash;
    /// <summary>engine->IsTakingScreenshot(): the freeze flash is left off a screenshot.</summary>
    public int TakingScreenshot;

    public readonly float Cvar(RenderCvar c) { fixed (float* p = Cvars) return p[(int)c]; }
    public void SetCvar(RenderCvar c, float v) => Cvars[(int)c] = v;

    public readonly Vector SkyOrigin { get { fixed (float* p = SkyOriginXYZ) return new Vector(p[0], p[1], p[2]); } }
    public readonly int Fade(int i) { fixed (int* p = FadeColor) return p[i]; }
}

/// <summary>
/// The convars the plan reads, by their slot in <see cref="RenderInputs.Cvars"/>;
/// shell_view_executor_client.cpp's RENDER_CVAR_ table fills them in this order
/// (RenderPlanAbiParityTests holds the two lists equal).
/// </summary>
public enum RenderCvar
{
    r_skybox = 0,
    r_3dsky,
    mat_motion_blur_enabled,
    building_cubemaps,
    r_flashlightdepthtexture,
    mat_viewportscale,
    mat_viewportupscale,
    cl_leveloverview,
    fog_override,
    fog_start,
    fog_end,
    fog_enable,
    fog_maxdensity,
    fog_radial,
    fog_startskybox,
    fog_endskybox,
    fog_maxdensityskybox,
    fog_enableskybox,
    fog_radialskybox,
    mat_drawwater,
    r_waterforceexpensive,
    r_waterforcereflectentities,
    r_WaterDrawReflection,
    r_WaterDrawRefraction,
    r_ForceWaterLeaf,
    mat_clipz,
    r_eyewaterepsilon,
    r_drawviewmodel,
    r_drawentities,
    r_drawopaquerenderables,
    r_drawtranslucentrenderables,
    r_drawtranslucentworld,
    r_entityclips,
    vcollide_wireframe,
    r_drawopaqueworld,
    r_DrawDetailProps,
    cl_maxrenderable_dist,
    r_worldlistcache,
    mat_yuv,
    mat_hsv,
    mat_showlightmappage,
    cl_drawshadowtexture,
    cl_shadowtextureoverlaysize,
    mat_showwatertextures,
    mat_wateroverlaysize,
    mat_showcamerarendertarget,
    mat_showframebuffertexture,
    mat_framebuffercopyoverlaysize,
    mat_drawTextureScale,
    r_flashlightdrawdepth,
    cl_overdraw_test,
    fog_color_r,
    fog_color_g,
    fog_color_b,
    fog_colorskybox_r,
    fog_colorskybox_g,
    fog_colorskybox_b,
    Count,
}

/// <summary>One step of the plan (<c>SourceSharpRenderOp_t</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RenderOp
{
    public int Kind;
    public fixed int I[7];
    public fixed float F[8];
    public ViewSetup View;
}

/// <summary>
/// What one world view's lists hold (<c>SourceSharpViewScene_t</c>, C7 step 2):
/// the counts; the leaves (<see cref="ViewSceneLeaf"/>) and entries
/// (<see cref="ViewSceneEntry"/>) follow it in the same buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ViewScene
{
    public const int GroupCount = 14;

    public int LeafCount;
    public int EntryCount;
    public fixed int GroupCounts[GroupCount];
    public fixed int TranslucentFlags[2];
    public int HasRenderables;
    public int OpIndex;

    public readonly int GroupCountOf(int group) { fixed (int* p = GroupCounts) return p[group]; }
    public void SetGroupCount(int group, int n) => GroupCounts[group] = n;
    public readonly int TranslucentFlagsOf(int i) { fixed (int* p = TranslucentFlags) return p[i]; }
    public void SetTranslucentFlags(int i, int v) => TranslucentFlags[i] = v;

    /// <summary>Where group <paramref name="group"/>'s entries start.</summary>
    public readonly int GroupStart(int group)
    {
        int n = 0;
        for (int g = 0; g < group; g++)
            n += GroupCountOf(g);
        return n;
    }
}

/// <summary>One leaf of a view's world list (<c>SourceSharpViewSceneLeaf_t</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ViewSceneLeaf
{
    public int Leaf;
    public int ActualLeaf;
    public int Detail;
    public fixed int Translucent[2];

    public readonly bool TranslucentOf(int i) { fixed (int* p = Translucent) return p[i] != 0; }
    public void SetTranslucent(int i, bool v) => Translucent[i] = v ? 1 : 0;
}

/// <summary>Bits of <see cref="ViewSceneEntry.Flags"/>.</summary>
[Flags]
public enum ViewSceneEntryFlags
{
    None = 0,
    Renderable = 1,
    TwoPass = 2,
    IgnoresZ = 4,
    PowerOfTwoFrameBuffer = 8,
    FullFrameBuffer = 16,
    Brush = 32,
    StudioStats = 64,
}

/// <summary>One renderable of a view's list (<c>SourceSharpViewSceneEntry_t</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ViewSceneEntry
{
    public int Group;
    public int Flags;
    public int Leaf;
    public int EntIndex;
    public int FxBlend;
    public fixed float Color[3];

    public readonly bool Has(ViewSceneEntryFlags f) => (Flags & (int)f) != 0;
    public readonly float ColorOf(int i) { fixed (float* p = Color) return p[i]; }
    public void SetColor(int i, float v) => Color[i] = v;
}

/// <summary>Safe accessors over <see cref="RenderOp"/>'s arrays.</summary>
public static unsafe class RenderOpExtensions
{
    public static int GetI(this in RenderOp op, int i) { fixed (int* p = op.I) return p[i]; }
    public static float GetF(this in RenderOp op, int i) { fixed (float* p = op.F) return p[i]; }
    public static void SetI(this ref RenderOp op, int i, int v) => op.I[i] = v;
    public static void SetF(this ref RenderOp op, int i, float v) => op.F[i] = v;
}

/// <summary>
/// What one <see cref="RenderOp"/> asks the executor to do
/// (shell_view_executor_client.cpp's RENDER_OP_ list, same names and numbers;
/// RenderPlanAbiParityTests holds them equal). Each is one engine call or one
/// stock primitive; what its I[]/F[]/View mean is said beside it.
/// </summary>
public enum RenderOpKind
{
    // 56, 57 and 58 were Draw3DDebuggingInfo, Draw2DDebuggingInfo and
    // GenerateOverdraw, viewdebug.cpp's entry points; the plan places their
    // pieces now (C7 step 2), and the numbers are not reused.
    None = 0,
    SceneBegin = 1,
    SceneEnd = 2,
    TurnOnToneMapping = 3,
    ShadowAdvanceFrame = 4,
    /// <summary>View; I0 clear flags, I1 <see cref="RenderTarget"/>, I2 depth target, I3 1 = the overload with a depth texture.</summary>
    Push3DView = 5,
    /// <summary>View; I0 clear flags, I1 <see cref="RenderTarget"/>.</summary>
    Push2DView = 6,
    PopView = 7,
    /// <summary>View's origin and angles; I0 the view id.</summary>
    SetupCurrentView = 8,
    FinishCurrentView = 9,
    /// <summary>I0 allow.</summary>
    AllowCurrentViewAccess = 10,
    ShadowPreRender = 11,
    /// <summary>View.</summary>
    ShadowComputeDepthTextures = 12,
    ShadowUnlockDepthTextures = 13,
    ClientSystemsPreRender = 14,
    ClientSystemsPostRender = 15,
    /// <summary>I0 no-vis; F0..2 the vis origin. The engine's answer is kept for <see cref="ClearColorToFogIfRadialVis"/> and <see cref="ClearForView"/>.</summary>
    ViewSetupVisEx = 16,
    /// <summary>When the last ViewSetupVisEx answered radial vis: the clear colour becomes the fog colour, and a view's clear may add colour.</summary>
    ClearColorToFogIfRadialVis = 17,
    SetClearColorToFogColor = 18,
    /// <summary>I0..I3 r, g, b, a.</summary>
    ClearColor4ub = 19,
    ParticleIncrementFrameCode = 20,
    /// <summary>View; I0 draw flags (DF_), I1 clear flags. Makes the view the one the following ops draw.</summary>
    WorldViewBegin = 21,
    /// <summary>The current view is done: its lists are released.</summary>
    WorldViewEnd = 22,
    BeginUpdateLightmaps = 23,
    EndUpdateLightmaps = 24,
    /// <summary>I0 draw entities, I1 forced view leaf, I2 use the list cache, I3 shadow depth, I4 1 = F0 is the reflection's water height.</summary>
    BuildWorldLists = 25,
    PruneWorldList = 26,
    /// <summary>I0 the view id the lists are built for.</summary>
    BuildRenderables = 27,
    /// <summary>F0..2 colour (0..1), F3 start, F4 end, F5 max density; I0 radial.</summary>
    FogLinear = 28,
    FogNone = 29,
    /// <summary>I0 fog volume, I1 height fog.</summary>
    FogVolumeState = 30,
    /// <summary>I0 alpha: the clear colour becomes the current fog colour.</summary>
    ClearColorFromFogColor = 31,
    WorldViewShadowTextures = 32,
    SoundExtraUpdate = 33,
    /// <summary>F0.</summary>
    SetFogZ = 34,
    /// <summary>F0.</summary>
    SetHeightClipZ = 35,
    /// <summary>I0 MaterialHeightClipMode_t.</summary>
    SetHeightClipMode = 36,
    /// <summary>I0 clear flags; I1 1 = add colour when the last ViewSetupVisEx answered radial vis.</summary>
    ClearForView = 37,
    WaterLightmapScale = 38,
    RestoreWaterLightmapScale = 39,
    /// <summary>I0 1 = set the power-of-two frame buffer as copy texture 0 (the previous one is kept).</summary>
    FrameBufferCopyPush = 40,
    FrameBufferCopyPop = 41,
    /// <summary>F0 water z adjust.</summary>
    DrawWorld = 42,
    /// <summary>I0 ERenderDepthMode.</summary>
    DrawOpaque = 43,
    /// <summary>I0 in skybox, I1 shadow depth.</summary>
    DrawTranslucent = 44,
    DrawNoZTranslucent = 45,
    /// <summary>I0 shadow depth.</summary>
    DrawTranslucentWorldInLeaves = 46,
    PixelVisEndCurrentView = 47,
    PixelVisEndScene = 48,
    /// <summary>I0 hardware.</summary>
    Flush = 49,
    /// <summary>I0..I3 the source rect.</summary>
    CopyRefractionTarget = 50,
    /// <summary>F0..F3 the plane.</summary>
    PushCustomClipPlane = 51,
    PopCustomClipPlane = 52,
    /// <summary>I0 cache full scene state.</summary>
    GlowDrawOverlays = 53,
    /// <summary>F0 z far, I0 cache full scene state.</summary>
    GlowUpdateSkyOverlays = 54,
    DrawPrecipitation = 55,
    ClientEffectsDraw = 59,
    /// <summary>I0 allowed.</summary>
    SetFXCreationAllowed = 60,
    DrawPortals = 61,
    DrawLights = 62,
    RenderPlayerSprites = 63,
    /// <summary>View.</summary>
    MotionBlur = 64,
    /// <summary>View.</summary>
    PostScreenSpaceEffects = 65,
    /// <summary>View (the viewmodel's setup); I0 draw the player's viewmodel, I1 draw the tools' viewmodels.</summary>
    ViewModels = 66,
    UnderwaterOverlay = 67,
    SmokeFogOverlay = 68,
    /// <summary>I0..I3 colour, I4 1 = the modulating material (blend), 0 the translucent one.</summary>
    ViewDrawFade = 69,
    /// <summary>I0..I3 x, y, w, h; I4 the branch (<see cref="ScreenOverlayBranch"/>).</summary>
    ScreenOverlay = 70,
    /// <summary>I0..I3 x, y, w, h; I4 the flashlight is on.</summary>
    EnginePostProcessing = 71,
    /// <summary>I0..I3 x, y, w, h.</summary>
    GrabPreColorCorrectedFrame = 72,
    /// <summary>I0..I3 x, y, w, h.</summary>
    ScreenSpaceEffects = 73,
    /// <summary>F0..F2.</summary>
    ToneMapScaleLinear = 74,
    SaveRenderTarget = 75,
    SetRenderTargetSaved = 76,
    /// <summary>View.</summary>
    HudPaint = 77,
    WorldListCacheFlush = 78,
    /// <summary>I0 the sky's area.</summary>
    Skybox3DAreaBits = 79,
    Skybox3DAreaBitsRestore = 80,
    /// <summary>I0 no-vis; F0..2 the vis origin.</summary>
    ViewSetupVis = 81,
    SetUnderwaterOverlay = 82,
    ClearUnderwaterOverlay = 83,
    /// <summary>View.</summary>
    Upscale = 84,
    /// <summary>View: what view->GetViewSetup() answers from here on.</summary>
    SetCurrentViewSetup = 85,
    /// <summary>I0, I1 the two engine draw-flag values LeafContainsTranslucentSurfaces is asked with. The executor gathers the view's scene here and asks managed for the view's draw program (C7 step 2); the program's sections are what the view's DrawOpaque, DrawTranslucent, DrawNoZTranslucent and DrawTranslucentWorldInLeaves ops then run, in that order.</summary>
    ViewScene = 86,
    /// <summary>Program only: ends the section one list-dependent draw op runs.</summary>
    SectionEnd = 87,
    /// <summary>F0 the blend.</summary>
    SetBlend = 88,
    /// <summary>F0..2 the colour.</summary>
    SetColorModulation = 89,
    /// <summary>Program only: I0 the scene entry, I1 the studio flags, I2 1 = honour the renderable's clip plane (r_entityclips). Stock's DrawOpaqueRenderable/DrawTranslucentRenderable body after its colour and blend.</summary>
    DrawEntity = 90,
    /// <summary>Program only: I0 the scene entry, queued for the next DrawStaticPropsBatch.</summary>
    StaticProp = 91,
    /// <summary>Program only: I0 depth mode, I1 vcollide wireframe. Draws the queued static props in one IStaticPropMgrClient::DrawStaticProps call and empties the queue.</summary>
    DrawStaticPropsBatch = 92,
    RopeResetCache = 93,
    ParticleSystemResetCache = 94,
    /// <summary>I0 depth mode.</summary>
    RopeDrawCache = 95,
    /// <summary>I0 depth mode.</summary>
    ParticleSystemDrawCache = 96,
    /// <summary>I0 in the 3D sky. particles_ez.cpp's DrawParticleSingletons at stock's place in the translucent passes: the point a managed particle source draws its singletons at.</summary>
    ParticleSingletons = 97,
    UpdateFullScreenDepthTexture = 98,
    /// <summary>I0 the flag.</summary>
    FullScreenDepthTextureValidity = 99,
    DetailBeginTranslucent = 100,
    /// <summary>Program only: I0 a leaf, appended to the detail leaf list the next DetailRenderTranslucent draws.</summary>
    DetailLeaf = 101,
    /// <summary>Program only: the detail system's translucent objects in the listed leaves; empties the list.</summary>
    DetailRenderTranslucent = 102,
    /// <summary>Program only: I0 the leaf, I1 the scene entry whose render origin bounds the draw, or -1 for the rest.</summary>
    DetailRenderInLeaf = 103,
    /// <summary>Program only: I0 the actual leaf, I1 the engine draw flags, I2 shadow depth.</summary>
    TranslucentSurfaces = 104,
    /// <summary>Program only: I0 uses the power-of-two frame buffer, I1 uses the full one, I2 0 = the detail branch's rule, 1 = the other's. Stock's refresh before a translucent entity that samples the frame buffer; the size is the render target's in force.</summary>
    FrameBufferForEntity = 105,
    UpdateRefractTexture = 106,
    UpdateScreenEffectTexture = 107,
    /// <summary>The local player's body draws in the views that follow (a second camera that sees the viewer).</summary>
    ViewerSeenBegin = 108,
    /// <summary>The body goes back to how it was before ViewerSeenBegin.</summary>
    ViewerSeenEnd = 109,
    /// <summary>I0 the view target: one more view drawn into it (ViewTargetInfo's count).</summary>
    ViewTargetDrawn = 110,
    /// <summary>A debug overlay quad (viewdebug.cpp's helpers): I0 a material id (IFxRender's), I1 1 = sized by the material's mapping; F0..3 x, y, w, h; F4..7 s0, t0, s1, t1. Nothing for an error material.</summary>
    DebugQuad = 111,
    /// <summary>I0 a material id: DrawScreenEffectMaterial over View's rect (mat_yuv, mat_hsv).</summary>
    DebugScreenMaterial = 112,
    /// <summary>I0 the lightmap page: View's world lists, then DrawLightmaps (mat_showlightmappage).</summary>
    DebugLightmapPage = 113,
    /// <summary>I0 the size: the shadow manager's texture (cl_drawshadowtexture).</summary>
    DebugShadowTexture = 114,
    /// <summary>F0 the scale: mat_drawTexture's texture on ___debug at 32, 32.</summary>
    DebugShowTexture = 115,
    /// <summary>The flashlight's depth texture (r_flashlightdrawdepth).</summary>
    DebugFlashlightDepth = 116,
    Draw3DDebugOverlays = 117,
    DrawLineFile = 118,
    /// <summary>F0: g_SmokeFogOverlayAlpha (cl_overdraw_test's 20/255, then 0).</summary>
    SmokeFogOverlayAlpha = 119,
    /// <summary>PushRenderTargetAndViewport( NULL ).</summary>
    PushDefaultTarget = 120,
    PopRenderTargetAndViewport = 121,

    // C7 post-processing (PostProcessPlanner): viewpostprocess.cpp's passes,
    // carried out as stock's calls. A material is the fx material id
    // (IFxRender.MaterialFind); a texture is a PostTexture; a var a PostVar.

    /// <summary>UpdateScreenEffectTexture( 0, I0, I1, I2, I3, I4 != 0 ).</summary>
    PostCopyScreenEffect = 122,
    /// <summary>SetRenderTarget( I0 as PostTexture, NULL for 0 ).</summary>
    PostSetRenderTarget = 123,
    /// <summary>viewpostprocess.cpp's SetRenderTargetAndViewPort: SetRenderTarget, then Viewport( 0, 0, the target's actual size ).</summary>
    PostSetRenderTargetAndViewport = 124,
    /// <summary>PushRenderTargetAndViewport(), no arguments.</summary>
    PostPushRenderTargetAndViewport = 125,
    /// <summary>
    /// DrawScreenSpaceRectangle( material I0, dest I1..I4, src F0..F3, src size I5 x I6,
    /// the world renderable when F6 != 0 else NULL, dice F4 x F5 ).
    /// </summary>
    PostDrawRect = 126,
    /// <summary>Material I0's var I1 (a PostVar) SetFloatValue( F0 ).</summary>
    PostMaterialVarFloat = 127,
    /// <summary>BeginOcclusionQueryDrawing on the histogram bucket I0's query (created on first use).</summary>
    PostQueryBegin = 128,
    /// <summary>EndOcclusionQueryDrawing on bucket I0's query.</summary>
    PostQueryEnd = 129,
    /// <summary>The histogram's stencil states (I0: 0 off, 1 mark where the compare passes, 2 count the marked).</summary>
    PostStencil = 130,
    /// <summary>SetGoalToneMappingScale( F0 ).</summary>
    PostGoalToneScale = 131,
    /// <summary>ResetToneMappingScale( F0 ).</summary>
    PostResetToneScale = 132,
    /// <summary>EnableColorCorrection( I0 != 0 ).</summary>
    PostEnableColorCorrection = 133,
    /// <summary>SetScissorRect( I0, I1, I2, I3, I4 != 0 ).</summary>
    PostScissor = 134,
    /// <summary>ClearColor3ub( I0, I1, I2 ).</summary>
    PostClearColor3ub = 135,
    /// <summary>Viewport( I0, I1, I2, I3 ).</summary>
    PostViewport = 136,
    /// <summary>ClearBuffers( I0 != 0, I1 != 0 ).</summary>
    PostClearBuffers = 137,
    /// <summary>DumpTGAofRenderTarget( I0, I1 ) as file I3 of the frame, named by I2 (a PostDump).</summary>
    PostDumpTga = 138,
    /// <summary>The histogram buckets (bit per bucket, I0) whose queries are read back at the next post crossing.</summary>
    PostReadbackNext = 139,
    /// <summary>PushRenderTargetAndViewport( I0 as PostTexture ).</summary>
    PostPushRenderTarget = 140,
    /// <summary>
    /// A dynamic mesh: material I0, primitive I1 (a PostPrimitive), I2 primitives;
    /// I3 bit 0 loads identity view and projection matrices around it (pushed and
    /// popped), bit 1 binds the world renderable, bit 2 draws nothing when the
    /// material's own $disabled is set.
    /// </summary>
    PostMeshBegin = 141,
    /// <summary>
    /// A vertex: Position3f( F0, F1, F2 ), Color4f( F5, F6, F7, I1 as float bits )
    /// when I0 bit 0, TexCoord2f( 0, F3, F4 ); advanced unless I0 bit 1 (a
    /// PostMeshFrame follows).
    /// </summary>
    PostMeshVertex = 142,
    /// <summary>The vertex's TangentS3f( F0..F2 ), TangentT3f( F3..F5 ), Normal3f( F6, F7, I0 as float bits ), then advanced.</summary>
    PostMeshFrame = 143,
    /// <summary>The mesh ends and draws; the matrices pop.</summary>
    PostMeshEnd = 144,
    /// <summary>ClearColor4ub( I0, I1, I2, I3 ).</summary>
    PostClearColor4ub = 145,
    /// <summary>
    /// CFreezeFrameView::Draw: _rt_FullScreen over the view at ( I0, I1, I2, I3 ),
    /// then the flash at alpha I4 (debug/debugtranslucentsinglecolor, white,
    /// ignoring Z) unless I4 is negative.
    /// </summary>
    FreezeFrameDraw = 146,
    /// <summary>The freeze frame's capture: the rectangle ( I0, I1, I2, I3 ) of the frame copied into _rt_FullScreen.</summary>
    FreezeFrameCapture = 147,
    /// <summary>ClearColor3ub( I0, I1, I2 ).</summary>
    ClearColor3ub = 148,
    /// <summary>modelrender->ForcedMaterialOverride( NULL, I0 as OverrideType_t ).</summary>
    ForcedMaterialOverride = 149,
}

/// <summary>A post mesh's primitive (MaterialPrimitiveType_t).</summary>
public enum PostPrimitive
{
    Triangles = 2,
    Quads = 7,
}

/// <summary>A texture a post op names.</summary>
public enum PostTexture
{
    None = 0,
    FullFrameFB = 1,
    SmallFB0 = 2,
    SmallFB1 = 3,
    /// <summary>_rt_ResolvedFullFrameDepth.</summary>
    ResolvedFullFrameDepth = 4,
}

/// <summary>A material var a post op sets.</summary>
public enum PostVar
{
    None = 0,
    /// <summary>dev/lumcompare's $C0_X, the luminance range's bottom.</summary>
    C0X = 1,
    /// <summary>$C0_Y, its top.</summary>
    C0Y = 2,
    /// <summary>$C0_Z, the tonemap scale.</summary>
    C0Z = 3,
    /// <summary>dev/blurfiltery_nohdr's $bloomamount.</summary>
    BloomAmount = 4,
}

/// <summary>The names a render-target dump is filed under (DumpTGAofRenderTarget's pFilename).</summary>
public enum PostDump
{
    BackBuffer = 0,
    QuarterSizeFB = 1,
    BlurX = 2,
    BlurYAndBloom = 3,
    EnginePost = 4,
    MotionBlur = 5,
}

/// <summary>Render targets an op can name (I1 of Push3DView/Push2DView).</summary>
public enum RenderTarget
{
    None = 0,
    FullFrameFB0 = 1,
    WaterReflection = 2,
    WaterRefraction = 3,
    Saved = 4,
    /// <summary>The second camera's target, _rt_SSView0 (C7 item 6).</summary>
    ViewTarget0 = 5,
    /// <summary>The colour target UpdateShadowDepthTexture was handed (the shadow manager's dummy).</summary>
    ShadowColor = 6,
    /// <summary>The depth texture UpdateShadowDepthTexture was handed.</summary>
    ShadowDepth = 7,
}

/// <summary>Which of PerformScreenOverlay's three ways the overlay draws (I4 of ScreenOverlay).</summary>
public enum ScreenOverlayBranch
{
    None = 0,
    FullFrameBuffer = 1,
    PowerOfTwoFrameBuffer = 2,
    Fade = 3,
}
