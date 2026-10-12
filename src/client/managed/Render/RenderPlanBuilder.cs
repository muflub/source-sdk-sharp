using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>Stock's render constants the plan uses (view_shared.h, iviewrender.h, viewrender.h, cdll_int.h).</summary>
public static class RenderConst
{
    public const int ClearColor = 0x1;
    public const int ClearDepth = 0x2;
    public const int ClearFullTarget = 0x4;
    public const int ClearObeyStencil = 0x10;
    public const int ClearStencil = 0x20;

    public const int DrawViewModel = 1 << 0;
    public const int DrawHud = 1 << 1;

    public const int DfRenderRefraction = 0x1;
    public const int DfRenderReflection = 0x2;
    public const int DfClipZ = 0x4;
    public const int DfClipBelow = 0x8;
    public const int DfRenderUnderwater = 0x10;
    public const int DfRenderAbovewater = 0x20;
    public const int DfRenderWater = 0x40;
    public const int DfDrawSkybox = 0x800;
    public const int DfFudgeUp = 0x1000;
    public const int DfDrawEntities = 0x2000;
    public const int DfClipSkybox = 0x40000;

    public const int ViewNone = -1;
    public const int ViewMain = 0;
    public const int View3DSky = 1;
    public const int ViewMonitor = 2;
    public const int ViewReflection = 3;
    public const int ViewRefraction = 4;
    public const int ViewIntroCamera = 6;
    public const int ViewShadowDepthTexture = 7;

    public const int SkyboxNotVisible = 0;
    public const int Skybox3DVisible = 1;
    public const int Skybox2DVisible = 2;

    public const int HdrInteger = 1;
    public const int HdrFloat = 2;

    public const int HeightClipDisable = 0;
    public const int HeightClipRenderAboveHeight = 1;
    public const int HeightClipRenderBelowHeight = 2;

    public const int DepthModeNormal = 0;

    /// <summary>worldsize.h's MAX_TRACE_LENGTH, as a float the way CSkyboxView stores it in zFar.</summary>
    public const float MaxTraceLength = (float)(1.732050807569 * 2.0 * 16384.0);
}

/// <summary>Appends <see cref="RenderOp"/>s, one helper per kind.</summary>
public sealed class RenderPlanBuilder
{
    // Kept across frames: a host reuses one builder, so a warm plan allocates
    // nothing (the arguments are collection expressions over spans, which
    // live on the stack).
    private readonly List<RenderOp> _ops = [];

    public IReadOnlyList<RenderOp> Ops => _ops;

    /// <summary>The ops so far, valid until the next <see cref="Clear"/> or add.</summary>
    public ReadOnlySpan<RenderOp> Span => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ops);

    public void Clear() => _ops.Clear();

    public RenderOp[] ToArray() => [.. _ops];

    public void Add(RenderOpKind kind, ReadOnlySpan<int> ints = default, ReadOnlySpan<float> floats = default, in ViewSetup view = default)
    {
        var op = new RenderOp { Kind = (int)kind, View = view };
        for (int i = 0; i < ints.Length; i++)
            op.SetI(i, ints[i]);
        for (int i = 0; i < floats.Length; i++)
            op.SetF(i, floats[i]);
        _ops.Add(op);
    }

    public void Op(RenderOpKind kind) => Add(kind);

    // The helpers below take their arguments as parameters on purpose: a
    // collection expression of constants ([0], [1f, 1f, 1f]) compiles to
    // RuntimeHelpers.CreateSpan, which unoptimised (debug) code runs by
    // materialising a field stub on the heap every call -- 288 bytes a frame
    // at dm_lockdown before they were (RenderPlanAllocationTests).

    /// <summary>An op whose one argument is an int.</summary>
    public void AddInt(RenderOpKind kind, int value) => Add(kind, [value]);

    public void ToneMapScaleLinear(float r, float g, float b) => Add(RenderOpKind.ToneMapScaleLinear, default, [r, g, b]);

    public void Push2DView(in ViewSetup view, int clear, RenderTarget target) => Add(RenderOpKind.Push2DView, [clear, (int)target], default, view);

    public void ViewSetupVis(bool noVis, Vector origin) => Add(RenderOpKind.ViewSetupVis, [noVis ? 1 : 0], [origin.X, origin.Y, origin.Z]);

    public void Push3DView(in ViewSetup view, int clear, RenderTarget target, bool depthOverload)
        => Add(RenderOpKind.Push3DView, [clear, (int)target, 0, depthOverload ? 1 : 0], null, view);

    /// <summary>Push3DView with a depth texture (the overload stock's shadow-depth view calls).</summary>
    public void Push3DView(in ViewSetup view, int clear, RenderTarget target, RenderTarget depth)
        => Add(RenderOpKind.Push3DView, [clear, (int)target, (int)depth, 1], null, view);

    public void ClearColor3ub(int r, int g, int b) => Add(RenderOpKind.ClearColor3ub, [r, g, b]);

    /// <summary>ForcedMaterialOverride( NULL, <paramref name="overrideType"/> ): 0 clears it, 2 is OVERRIDE_DEPTH_WRITE.</summary>
    public void ForcedMaterialOverride(int overrideType) => Add(RenderOpKind.ForcedMaterialOverride, [overrideType]);

    public void SetupCurrentView(Vector origin, Vector angles, int viewId)
    {
        var view = new ViewSetup { Origin = origin, Angles = angles };
        Add(RenderOpKind.SetupCurrentView, [viewId], null, view);
    }

    public void ViewSetupVisEx(bool noVis, Vector origin)
        => Add(RenderOpKind.ViewSetupVisEx, [noVis ? 1 : 0], [origin.X, origin.Y, origin.Z]);

    public void ClearColor4ub(int r, int g, int b, int a) => Add(RenderOpKind.ClearColor4ub, [r, g, b, a]);

    public void WorldViewBegin(in ViewSetup view, int drawFlags, int clearFlags)
        => Add(RenderOpKind.WorldViewBegin, [drawFlags, clearFlags], null, view);

    /// <summary>
    /// BuildWorldRenderLists: F1 is UpdateRenderablesOpacity's prop-fade factor
    /// (-1 under cl_leveloverview, else 1), used when <paramref name="entities"/>.
    /// </summary>
    public void BuildWorldLists(bool entities, int forceLeaf, bool useCache, bool shadowDepth, float? reflectionHeight, float opacityFactor = 1f)
        => Add(RenderOpKind.BuildWorldLists,
               [entities ? 1 : 0, forceLeaf, useCache ? 1 : 0, shadowDepth ? 1 : 0, reflectionHeight.HasValue ? 1 : 0],
               [reflectionHeight ?? 0f, opacityFactor]);

    /// <summary>
    /// BuildRenderableRenderLists for <paramref name="viewId"/>, with
    /// SetupRenderablesList's decisions: whether entities are drawn
    /// (r_drawentities), detail objects (the client mode and r_DrawDetailProps),
    /// translucent objects (not in a shadow-depth view), and the squared
    /// render distance.
    /// </summary>
    public void BuildRenderables(int viewId, bool entities = true, bool detail = true, bool translucent = true, float renderDistSq = 0f)
        => Add(RenderOpKind.BuildRenderables, [viewId, entities ? 1 : 0, detail ? 1 : 0, translucent ? 1 : 0], [renderDistSq]);

    public void FogLinear(Vector color, float start, float end, float maxDensity, bool radial)
        => Add(RenderOpKind.FogLinear, [radial ? 1 : 0], [color.X, color.Y, color.Z, start, end, maxDensity]);

    public void FogVolumeState(int volume, bool heightFog) => Add(RenderOpKind.FogVolumeState, [volume, heightFog ? 1 : 0]);

    public void ClearColorFromFogColor(int alpha) => Add(RenderOpKind.ClearColorFromFogColor, [alpha]);

    public void SetFogZ(float z) => Add(RenderOpKind.SetFogZ, null, [z]);

    public void SetHeightClipZ(float z) => Add(RenderOpKind.SetHeightClipZ, null, [z]);

    public void SetHeightClipMode(int mode) => Add(RenderOpKind.SetHeightClipMode, [mode]);

    public void ClearForView(int clear, bool orColorIfRadialVis) => Add(RenderOpKind.ClearForView, [clear, orColorIfRadialVis ? 1 : 0]);

    public void FrameBufferCopyPush(bool setPowerOfTwo) => Add(RenderOpKind.FrameBufferCopyPush, [setPowerOfTwo ? 1 : 0]);

    /// <summary>CRendering3dView::DrawWorld: the engine's draw flags for the view's, and nothing at r_drawopaqueworld 0.</summary>
    public void DrawWorld(float waterZAdjust, int viewDrawFlags, bool drawOpaqueWorld)
    {
        if (drawOpaqueWorld)
            Add(RenderOpKind.DrawWorld, [(int)RenderPlanText.EngineDrawWorldListFlags(viewDrawFlags)], [waterZAdjust]);
    }

    /// <summary>
    /// The view's scene and draw program (C7 step 2): asked with the two draw
    /// flags its translucent passes ask leaves with -- the engine's flags for
    /// the view's without the sky (DrawTranslucentRenderables) and the view's
    /// own (DrawTranslucentWorldInLeaves, as stock has it).
    /// </summary>
    public void ViewScene(int viewDrawFlags)
        => Add(RenderOpKind.ViewScene, [(int)RenderPlanText.EngineDrawWorldListFlags(viewDrawFlags & ~RenderConst.DfDrawSkybox), viewDrawFlags]);

    public void DrawOpaque(int depthMode) => Add(RenderOpKind.DrawOpaque, [depthMode]);

    public void DrawTranslucent(bool inSkybox, bool shadowDepth) => Add(RenderOpKind.DrawTranslucent, [inSkybox ? 1 : 0, shadowDepth ? 1 : 0]);

    public void DrawTranslucentWorldInLeaves(bool shadowDepth) => Add(RenderOpKind.DrawTranslucentWorldInLeaves, [shadowDepth ? 1 : 0]);

    public void Flush(bool hardware) => Add(RenderOpKind.Flush, [hardware ? 1 : 0]);

    public void CopyRefractionTarget(int x, int y, int w, int h) => Add(RenderOpKind.CopyRefractionTarget, [x, y, w, h]);

    public void Rect(RenderOpKind kind, int x, int y, int w, int h, int extra = 0) => Add(kind, [x, y, w, h, extra]);

    public void WithView(RenderOpKind kind, in ViewSetup view, params ReadOnlySpan<int> ints) => Add(kind, ints, default, view);
}
