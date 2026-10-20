using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What the managed pixel visibility asks the engine (C7, c_pixel_visibility.cpp):
/// the material system's occlusion query objects and sphere diameters, the
/// two materials, the DX level and the sight line. A query object is native's
/// id for it (shell_pixelvis_client.cpp's table, 0 for none), never the render
/// context's handle.
/// </summary>
public interface IPixelVisEngine
{
    /// <summary><c>engine-&gt;GetDXSupportLevel()</c>.</summary>
    int DxSupportLevel();

    /// <summary><c>CreateOcclusionQueryObject</c>; 0 when none could be made.</summary>
    int CreateQuery();

    /// <summary><c>DestroyOcclusionQueryObject</c>.</summary>
    void DestroyQuery(int query);

    /// <summary><c>ResetOcclusionQueryObject</c>.</summary>
    void ResetQuery(int query);

    /// <summary>
    /// <c>OcclusionQuery_GetNumPixelsRendered</c>: the counting query's first
    /// when <paramref name="count"/> is one (stock reads it first), then the
    /// visible query's. A count it does not read is -1.
    /// </summary>
    void PixelsRendered(int visible, int count, out int visiblePixels, out int countPixels);

    /// <summary>
    /// <c>ComputePixelDiameterOfSphere</c> for each (origin x, y, z, radius) of
    /// <paramref name="spheres"/>, in order, into <paramref name="diameters"/>.
    /// </summary>
    void PixelDiameters(ReadOnlySpan<float> spheres, Span<float> diameters);

    /// <summary>A material found in TEXTURE_GROUP_CLIENT_EFFECTS and referenced; 0 for none.</summary>
    int FindMaterial(string name);

    /// <summary>The reference <see cref="FindMaterial"/> took, dropped.</summary>
    void ReleaseMaterial(int material);

    /// <summary>
    /// GlowSightDistance's line: MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS
    /// with CTraceFilterGlow (a CTraceFilterSimple with no pass entity that
    /// only lets a vphysics or brush entity stop it); the fraction.
    /// </summary>
    float TraceGlowSight(in Vector start, in Vector end);

    /// <summary><c>DevMsg( level, ... )</c>.</summary>
    void DevMsg(int level, string text);

    /// <summary><c>Msg( ... )</c>.</summary>
    void Msg(string text);
}

/// <summary>The current view as native's view render holds it (view.h's CurrentViewID, CurrentViewOrigin, ...Forward, Right, Up).</summary>
public readonly record struct CurrentViewState(int Id, Vector Origin, Vector Forward, Vector Right, Vector Up);

/// <summary>The current view, read in one crossing.</summary>
public interface ICurrentView
{
    CurrentViewState Read();
}

/// <summary>
/// c_pixel_visibility.h's pixelvis_queryparams_t, as Init fills it (a proxy of
/// 2 units, aspect 1, a fade of a sixteenth of a second, world-space size).
/// </summary>
public readonly record struct PixelVisParams(Vector Position, float ProxySize = PixelVisParams.DefaultProxySize,
                                             float ProxyAspect = 1.0f, float FadeTime = PixelVisParams.DefaultFadeTime,
                                             bool SizeInScreenspace = false)
{
    /// <summary>PIXELVIS_DEFAULT_PROXY_SIZE.</summary>
    public const float DefaultProxySize = 2.0f;

    /// <summary>PIXELVIS_DEFAULT_FADE_TIME.</summary>
    public const float DefaultFadeTime = 0.0625f;
}

/// <summary>
/// IShimViewClient's pixel visibility slots (shell_pixelvis_client.cpp): the
/// render context's occlusion query objects by native id, their pixel counts,
/// sphere diameters for a view, and the current view.
/// </summary>
public interface IPixelVisSlots
{
    /// <summary>A new query object's id; 0 when none could be made.</summary>
    int QueryCreate();

    void QueryDestroy(int query);

    void QueryReset(int query);

    /// <summary>The counting query's pixels read first (when it is one), then the visible query's; -1 for one not read.</summary>
    void QueryPixels(int visible, int count, out int visiblePixels, out int countPixels);

    /// <summary>ComputePixelDiameterOfSphere for each (x, y, z, radius), in one crossing.</summary>
    void Diameters(ReadOnlySpan<float> spheres, Span<float> diameters);

    /// <summary>CurrentViewID and the current view's origin and axes.</summary>
    CurrentViewState View();
}

/// <summary>
/// What native's view end and scene end reach (IManagedPixelVisClient): a
/// view's end hands the view (<c>SS_PIXELVIS_VIEW_FLOATS</c>) and takes the
/// proxies to draw as command words; the scene's end frees what nobody asked
/// about.
/// </summary>
public interface IClientPixelVisHost
{
    /// <summary>The view's commands into <paramref name="commands"/>; how many words they are (more than fit: ask <see cref="Commands"/>).</summary>
    int EndView(ReadOnlySpan<float> view, Span<int> commands);

    /// <summary>The last view's commands again, into a larger buffer; how many were written.</summary>
    int Commands(Span<int> commands);

    void EndScene();
}

/// <summary>
/// The command words a view's end answers with (shell_pixelvis_client.h's
/// SS_PIXELVIS_CMD_*): Bind material; DepthRange near far (float bits); Proxy
/// query material and twelve vertices (float bits).
/// </summary>
public enum PixelVisCommand
{
    Bind = 1,
    DepthRange = 2,
    Proxy = 3,
}

/// <summary>c_pixel_visibility.h's questions, as a glow's owner asks them.</summary>
public interface IPixelVisibility
{
    /// <summary><c>PixelVisibility_FractionVisible</c> with the caller's handle, kept by the caller between frames.</summary>
    float FractionVisible(in PixelVisParams p, ref int queryHandle);

    /// <summary><c>PixelVisibility_FractionVisible</c> with no handle: the sight line, 1 or 0.</summary>
    float FractionVisibleNoHandle(in Vector position);

    /// <summary><c>PixelVisibility_IsAvailable</c>.</summary>
    bool IsAvailable { get; }

    /// <summary><c>GlowSightDistance</c>: the distance from the eye, or -1 when traced and stopped.</summary>
    float GlowSightDistance(in Vector glowOrigin, bool shouldTrace);
}
