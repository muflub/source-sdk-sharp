using System.Runtime.CompilerServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

// The managed client shadow manager's seams (C7 cluster 12, S1 of
// docs/plans/c7_shadow_manager.md): what SourceSharp.Client's
// Render/Shadows asks of the engine and carries out through it. Each is a
// capability, call for call as stock clientshadowmgr.cpp made it; S2's
// binding carries them over the client forwarder library's ShadowNative
// table (queueing what answers nothing, crossing for what does). Native
// decides nothing: every `if` stays on the managed side.

/// <summary>A <c>VMatrix</c>: sixteen floats, row by row (<c>m[row][col]</c> at row * 4 + col).</summary>
[InlineArray(16)]
public struct ShadowMatrix
{
    private float _e0;
}

/// <summary>A <c>matrix3x4_t</c>: twelve floats, row by row (row * 4 + col).</summary>
[InlineArray(12)]
public struct ShadowMatrix3x4
{
    private float _e0;
}

/// <summary>A <c>cplane_t</c> as a frustum holds it.</summary>
public struct ShadowPlane
{
    public Vector Normal;
    public float Dist;
    public byte Type;
    public byte SignBits;
}

/// <summary>A <c>Frustum_t</c>'s six planes (right, left, top, bottom, near, far: FRUSTUM_*).</summary>
[InlineArray(6)]
public struct ShadowFrustum
{
    private ShadowPlane _p0;
}

/// <summary>The engine's <c>ShadowInfo_t</c> for one shadow, as <c>IShadowMgr::GetInfo</c> answers it.</summary>
public struct ShadowEngineInfo
{
    public ShadowMatrix WorldToShadow;
    public float FalloffOffset;
    public float MaxDist;
    public float FalloffAmount;
    public float TexOriginX;
    public float TexOriginY;
    public float TexSizeX;
    public float TexSizeY;
    public byte FalloffBias;
}

/// <summary>What the shadow manager reads of a caller's <c>CViewSetup</c>.</summary>
public struct ShadowViewSetup
{
    public Vector Origin;
    public QAngle Angles;
    public float ZNear;
    public float ZFar;
    public float Fov;
    public float AspectRatio;
}

/// <summary>
/// The <c>CViewSetup</c> a flashlight's depth view is rendered with: the
/// fields ComputeShadowDepthTextures sets (every other is the default
/// constructor's).
/// </summary>
public struct ShadowDepthView
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public float Fov;
    public float FovViewmodel;
    public Vector Origin;
    public QAngle Angles;
    public float ZNear;
    public float ZFar;
    public float ZNearViewmodel;
    public float ZFarViewmodel;
    public float AspectRatio;
    public bool Ortho;
    public bool DoBloomAndToneMapping;
}

/// <summary>
/// The engine's shadow manager (<c>IShadowMgr</c>, <c>shadowmgr</c>) and the
/// static prop manager's two shadow calls: the shadows the engine draws, and
/// what each is projected onto. Shadows are the engine's ShadowHandle_t
/// numbers; materials and textures are <see cref="IShadowMaterials"/>' ids
/// (0 for NULL); renderables and models are their pointers, held as numbers
/// and never followed.
/// </summary>
public interface IShadowEngine
{
    int CreateShadowEx(int material, int modelMaterial, int bindProxy, int creationFlags);

    void DestroyShadow(int shadow);

    void SetShadowMaterial(int shadow, int material, int modelMaterial, int bindProxy);

    void ProjectShadow(int shadow, in Vector origin, in Vector projectionDir, in ShadowMatrix worldToShadow, float sizeX, float sizeY,
                       ReadOnlySpan<int> leaves, float maxHeight, float falloffOffset, float falloffAmount, in Vector casterOrigin);

    void ProjectFlashlight(int shadow, in ShadowMatrix worldToShadow, ReadOnlySpan<int> leaves);

    void GetInfo(int shadow, out ShadowEngineInfo info);

    void GetFlashlightFrustum(int shadow, out ShadowFrustum frustum);

    void AddShadowToBrushModel(int shadow, nint model, in Vector origin, in QAngle angles);

    void RemoveAllShadowsFromBrushModel(nint model);

    void SetShadowTexCoord(int shadow, float x, float y, float w, float h);

    void AddShadowToModel(int shadow, int modelInstance);

    void RemoveAllShadowsFromModel(int modelInstance);

    void ClearExtraClipPlanes(int shadow);

    void AddExtraClipPlane(int shadow, in Vector normal, float dist);

    void EnableShadow(int shadow, bool enable);

    void SetFalloffBias(int shadow, byte bias);

    void UpdateFlashlightState(int shadow, in FlashlightState state);

    void AddFlashlightRenderable(int shadow, nint renderable);

    /// <summary><c>SetFlashlightDepthTexture( shadow, texture, stencilBit )</c>; texture 0 is NULL.</summary>
    void SetFlashlightDepthTexture(int shadow, int texture, byte stencilBit);

    void SetFlashlightRenderState(int shadow);

    /// <summary><c>staticpropmgr-&gt;AddShadowToStaticProp</c>.</summary>
    void AddShadowToStaticProp(int shadow, nint renderable);

    /// <summary><c>staticpropmgr-&gt;RemoveAllShadowsFromStaticProp</c>.</summary>
    void RemoveAllShadowsFromStaticProp(nint renderable);
}

/// <summary>
/// The material system as the shadow manager uses it: its three materials and
/// its render targets (references the engine holds pointers to, so native
/// keeps them and managed names them by id), the restore callback, the depth
/// texture formats and support, and the hardware and command line it is told
/// to judge by.
/// </summary>
public interface IShadowMaterials
{
    /// <summary><c>CMaterialReference::Init( name, group )</c>: the material's id.</summary>
    int InitMaterial(string name, string textureGroup);

    void ShutdownMaterial(int material);

    void ColorModulate(int material, float r, float g, float b);

    /// <summary><c>CTextureReference::InitRenderTarget</c>: the target's id.</summary>
    int InitRenderTarget(string name, int width, int height, int sizeMode, int format, int depth, bool hdr);

    void ShutdownTexture(int texture);

    int ActualWidth(int texture);

    int ActualHeight(int texture);

    bool SupportsShadowDepthTextures();

    int ShadowDepthTextureFormat();

    int NullTextureFormat();

    void BeginRenderTargetAllocation();

    void EndRenderTargetAllocation();

    void UncacheUnusedMaterials();

    /// <summary><c>materials-&gt;AddRestoreFunc</c>: native's restore callback calls the manager's RestoreRenderState.</summary>
    void AddRestoreFunc();

    void RemoveRestoreFunc();

    /// <summary><c>g_pMaterialSystemHardwareConfig-&gt;GetDXSupportLevel()</c>.</summary>
    int DXSupportLevel();

    /// <summary><c>CommandLine()-&gt;CheckParm( "-tools" )</c>.</summary>
    bool ToolsMode();
}

/// <summary>
/// The render context, the model and brush shadow draws and the shadow depth
/// view, as the render-to-texture and depth passes drive them. Materials and
/// textures are <see cref="IShadowMaterials"/>' ids.
/// </summary>
public interface IShadowRenderContext
{
    float ComputePixelDiameterOfSphere(in Vector center, float radius);

    int GetFogMode();

    void FogMode(int mode);

    int GetHeightClipMode();

    void SetHeightClipMode(int mode);

    void ClearColor4ub(byte r, byte g, byte b, byte a);

    void ClearColor3ub(byte r, byte g, byte b);

    void MatrixMode(int mode);

    void PushMatrix();

    void PopMatrix();

    void LoadIdentity();

    void LoadMatrix(in ShadowMatrix matrix);

    void Scale(float x, float y, float z);

    void Ortho(double left, double top, double right, double bottom, double zNear, double zFar);

    void PushRenderTargetAndViewport(int texture);

    void PopRenderTargetAndViewport();

    void ClearBuffers(bool color, bool depth, bool stencil);

    void Viewport(int x, int y, int width, int height);

    void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias);

    /// <summary>
    /// RenderShadowTexture's quad: the material bound and one quad of w by h
    /// drawn through a dynamic mesh (texcoords 0..1, colour 0), as stock built it.
    /// </summary>
    void DrawTextureQuad(int material, float width, float height);

    /// <summary>
    /// <c>modelrender-&gt;DrawModelShadowSetup( renderable, body, skin, &amp;info )</c>,
    /// then <c>DrawModelShadow</c> when it answered bones. Whether it did.
    /// </summary>
    bool DrawModelShadow(nint renderable, int body, int skin);

    /// <summary><c>render-&gt;DrawBrushModelShadow( renderable )</c>.</summary>
    void DrawBrushModelShadow(nint renderable);

    /// <summary><c>view-&gt;UpdateShadowDepthTexture( dummy, depth, view )</c>.</summary>
    void UpdateShadowDepthTexture(int renderTarget, int depthTexture, in ShadowDepthView view);
}

/// <summary>
/// The engine's world, as the shadow manager asks it: the view's frustum
/// cull, the ambient light, the BSP leaves a shadow's volume touches, and the
/// frame.
/// </summary>
public interface IShadowWorld
{
    bool CullBox(in Vector mins, in Vector maxs);

    Vector AmbientLightColor();

    /// <summary><c>GetBSPTreeQuery()-&gt;EnumerateLeavesAlongRay</c> of a swept box, the leaves appended in the order the engine named them.</summary>
    void EnumerateLeavesAlongRay(in Vector start, in Vector delta, in Vector extents, List<int> leaves);

    /// <summary><c>GetBSPTreeQuery()-&gt;EnumerateLeavesInBox</c>, the leaves appended in the order the engine named them.</summary>
    void EnumerateLeavesInBox(in Vector mins, in Vector maxs, List<int> leaves);

    /// <summary><c>gpGlobals-&gt;framecount</c>.</summary>
    int FrameCount { get; }
}

/// <summary>
/// What the shadow manager asks of the renderables (IClientRenderable's
/// shadow surface) and the two things it does to one. A renderable is its
/// pointer, a model its pointer; an entity is its ClientEntityHandle_t.
/// </summary>
public interface IShadowRenderables
{
    /// <summary>The renderable an entity handle names now, or 0.</summary>
    nint RenderableOf(uint entity);

    Vector RenderOrigin(nint renderable);

    QAngle RenderAngles(nint renderable);

    void RenderableToWorldTransform(nint renderable, out ShadowMatrix3x4 transform);

    /// <summary><c>GetShadowHandle()</c>, 0xFFFF for none.</summary>
    int ShadowHandle(nint renderable);

    /// <summary><c>GetModel()</c>, 0 for none.</summary>
    nint Model(nint renderable);

    /// <summary><c>modelinfo-&gt;GetModelType( model )</c>.</summary>
    int ModelType(nint model);

    int Body(nint renderable);

    int Skin(nint renderable);

    void RenderBoundsWorldspace(nint renderable, out Vector mins, out Vector maxs);

    void ShadowRenderBounds(nint renderable, int shadowType, out Vector mins, out Vector maxs);

    bool ShouldReceiveProjectedTextures(nint renderable, int flags);

    /// <summary><c>GetShadowCastDistance( &amp;dist, type )</c>: dist comes in as the default and leaves as the answer.</summary>
    bool ShadowCastDistance(nint renderable, int shadowType, ref float distance);

    /// <summary><c>GetShadowCastDirection( &amp;dir, type )</c>.</summary>
    bool ShadowCastDirection(nint renderable, int shadowType, ref Vector direction);

    bool IsShadowDirty(nint renderable);

    void MarkShadowDirty(nint renderable, bool dirty);

    nint ShadowParent(nint renderable);

    nint FirstShadowChild(nint renderable);

    nint NextShadowPeer(nint renderable);

    void CreateModelInstance(nint renderable);

    /// <summary><c>GetModelInstance()</c>, 0xFFFF for none.</summary>
    int ModelInstance(nint renderable);

    /// <summary>The entity index of the renderable's networkable, or -1 for none (the fork's debug overlay text).</summary>
    int NetworkableEntIndex(nint renderable);

    /// <summary>The first child in the renderable's move hierarchy, or 0 (a flashlight target's receivers).</summary>
    nint FirstMoveChild(nint renderable);

    /// <summary>The next peer in the move hierarchy, or 0.</summary>
    nint NextMovePeer(nint renderable);
}

/// <summary>The engine's debug overlay, as the shadow manager's debug arms draw on it.</summary>
public interface IShadowDebugOverlay
{
    void AddLineOverlay(in Vector origin, in Vector dest, int r, int g, int b, bool noDepthTest, float duration);

    void AddTextOverlay(in Vector origin, int lineOffset, string text);
}
