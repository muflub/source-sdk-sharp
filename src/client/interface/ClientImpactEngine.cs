using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// One MASK_SHOT trace's answer, as much of <c>trace_t</c> as a bullet impact
/// reads.
/// </summary>
/// <param name="Hit"><c>tr.DidHit()</c>: it stopped short, or started solid.</param>
/// <param name="Fraction"><c>tr.fraction</c>.</param>
/// <param name="EndPos"><c>tr.endpos</c>.</param>
/// <param name="Normal"><c>tr.plane.normal</c>.</param>
/// <param name="Hitbox"><c>tr.hitbox</c>: on the world, a static prop's index plus one.</param>
/// <param name="EntityIndex">The hit's entity index; −1 for nothing or a client-only entity.</param>
/// <param name="SurfaceProps"><c>tr.surface.surfaceProps</c>.</param>
/// <param name="SurfaceFlags"><c>tr.surface.flags</c>.</param>
/// <param name="HitWorld"><c>tr.DidHitWorld()</c>: the hit is the world (a static prop's hit is too).</param>
public readonly record struct ShotTrace(
    bool Hit,
    float Fraction,
    Vector EndPos,
    Vector Normal,
    int Hitbox,
    int EntityIndex,
    int SurfaceProps,
    int SurfaceFlags,
    bool HitWorld);

/// <summary>
/// What a bullet impact (the managed "Impact" client effect) asks the engine,
/// and the one effect it carries out natively: IShimTempEntBackendsClient's and
/// IShimParticlesClient's lane M1-C slots (shell_impact_effects.cpp).
/// </summary>
public interface IClientImpactEngine
{
    /// <summary>
    /// <c>enginetrace-&gt;ClipRayToEntity( ray, MASK_SHOT, entity )</c>; null
    /// when <paramref name="entityIndex"/> names no client entity.
    /// </summary>
    ShotTrace? ClipToEntity(Vector start, Vector end, int entityIndex);

    /// <summary><c>UTIL_TraceLine( start, end, MASK_SHOT, NULL, COLLISION_GROUP_NONE )</c>.</summary>
    ShotTrace TraceWorld(Vector start, Vector end);

    /// <summary><c>engine-&gt;TraceLineMaterialAndLighting( start, end )</c>: the diffuse light and the base colour.</summary>
    (Vector Diffuse, Vector Base) WorldLighting(Vector start, Vector end);

    /// <summary>
    /// <c>staticpropmgr-&gt;GetStaticPropMaterialColorAndLighting</c> over a
    /// trace of <paramref name="start"/>, <paramref name="end"/> and
    /// <paramref name="normal"/>.
    /// </summary>
    (Vector Diffuse, Vector Base) StaticPropLighting(Vector start, Vector end, Vector normal, int staticProp);

    /// <summary>The effect ledger's impact rows (<c>cs_cl_effects_report</c>).</summary>
    void Ledger(int flecks, int dust);

    /// <summary>
    /// A new-style impact particle system: control points 0..2 at
    /// <paramref name="points"/>, each oriented and bound to
    /// <paramref name="entityIndex"/>, point 3 the scale and point 4 the colour
    /// when the definition reads it. False when no definition answers the name.
    /// </summary>
    bool ImpactParticle(string name, ReadOnlySpan<ImpactControlPoint> points, int entityIndex, float scale,
                        Vector color);
}

/// <summary>One oriented control point: its position and its forward, right and up.</summary>
public readonly record struct ImpactControlPoint(Vector Position, Vector Forward, Vector Right, Vector Up);
