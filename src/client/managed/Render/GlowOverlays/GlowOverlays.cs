//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.PixelVisibility;

namespace SourceSharp.Client.Render.GlowOverlays;

/// <summary>glow_overlay.h's CGlowSprite: a colour (0-1), sizes (1 = the sun's) and a material (0: the default, found at the first draw).</summary>
public sealed class GlowSprite
{
    public Vector Color;
    public float HorzSize = 1.0f;
    public float VertSize = 1.0f;
    public int Material;
}

/// <summary>The view a draw is made in: CurrentViewOrigin, ...Forward, the view setup's zFar and the frustum's first five planes.</summary>
public readonly record struct GlowView(Vector Origin, Vector Forward, float ZFar, GlowPlane[] Frustum);

/// <summary>A frustum plane, VPlane's normal and distance.</summary>
public readonly record struct GlowPlane(Vector Normal, float Dist)
{
    /// <summary>VPlane::DistTo.</summary>
    public float DistTo(in Vector v) => FxMath.Dot(v, Normal) - Dist;
}

/// <summary>A vertex of a glow quad: position, colour (r, g, b; alpha 1) and texture coordinate.</summary>
public readonly record struct GlowVertex(Vector Position, Vector Color, float S, float T);

/// <summary>EnableClipping's argument as the overlays pass it: on (the answer is the state to restore), off, or that state.</summary>
public enum GlowClip
{
    EnableAndSave = 0,
    Off = 1,
    Saved = 2,
}

/// <summary>The draw, carried out natively in the order given.</summary>
public interface IGlowDraw
{
    void Clip(GlowClip clip);

    /// <summary>The material's $hdrcolorscale, when it has one.</summary>
    void HdrColorScale(int material, float scale);

    /// <summary>A quad through GetDynamicMesh( false, 0, 0, material ), drawn.</summary>
    void Quad(int material, ReadOnlySpan<GlowVertex> vertices);

    /// <summary>The material bound and a quad drawn in it, its vertices red.</summary>
    void WireframeQuad(int material, ReadOnlySpan<Vector> positions);
}

/// <summary>What the overlays ask the engine besides the pixel visibility: a material by name, and a solid line.</summary>
public interface IGlowEngine
{
    /// <summary>materials-&gt;FindMaterial( name, group ): no reference taken, as stock took none.</summary>
    int FindMaterial(string name, string group);

    /// <summary>UTIL_TraceLine( start, end, CONTENTS_SOLID, NULL, COLLISION_GROUP_NONE ): the fraction and the surface's flags.</summary>
    void TraceSolid(in Vector start, in Vector end, out float fraction, out int surfaceFlags);
}

/// <summary>The ConVars the overlays read: building_cubemaps, cl_sun_decay_rate, r_drawsprites and the wireframe question.</summary>
public interface IGlowSettings
{
    bool BuildingCubemaps { get; }

    float SunDecayRate { get; }

    /// <summary>r_drawsprites.</summary>
    int DrawSprites { get; }

    /// <summary>ShouldDrawInWireFrameMode.</summary>
    bool Wireframe { get; }
}

/// <summary>
/// glow_overlay.h's CGlowOverlay: what the creator fills in (the position or
/// direction, the sky flag, the sprites, the proxy radius, the HDR scale), and
/// the overlay's obstruction, drawn by <see cref="GlowOverlaySystem"/>. An
/// <see cref="Update"/> that answers false removes it at the next draw.
/// </summary>
public class GlowOverlay
{
    /// <summary>MAX_SUN_LAYERS.</summary>
    public const int MaxSunLayers = 4;

    public Vector Pos;
    public bool Directional;
    public Vector Direction;
    public bool InSky;
    public float SkyObstructionScale = 1.0f;
    public readonly GlowSprite[] Sprites = [new(), new(), new(), new()];
    public int NSprites;
    public float ProxyRadius = 2.0f;
    public float HdrColorScale = 1.0f;

    protected internal float GlowObstructionScale;
    protected internal bool CacheGlowObstruction;
    protected internal bool CacheSkyObstruction;
    internal bool Activated;
    internal LinkedListNode<GlowOverlay>? Node;
    internal int QueryHandle;

    /// <summary>Return false to remove (and delete) the overlay; <paramref name="frameTime"/> is gpGlobals-&gt;frametime.</summary>
    public virtual bool Update(float frameTime) => true;

    /// <summary>The overlay left the list (stock's destructor).</summary>
    protected internal virtual void OnRemoved() { }

    /// <summary>CalcGlowAspect.</summary>
    protected internal float CalcGlowAspect()
    {
        if (NSprites != 0)
        {
            if (Sprites[0].HorzSize != 0 && Sprites[0].VertSize != 0)
                return Sprites[0].HorzSize / Sprites[0].VertSize;
        }
        return 1.0f;
    }

    /// <summary>The draw: <see cref="GlowOverlaySystem.DrawGlow"/>, or the warp overlay's.</summary>
    protected internal virtual void Draw(GlowOverlaySystem system, in GlowView view, bool cacheFullSceneState, IGlowDraw draw)
        => system.DrawGlow(this, view, cacheFullSceneState, draw);
}

/// <summary>glow_overlay.h's CWarpOverlay: warp_overlay.cpp's draw (the range check, sun/overlay, no cull).</summary>
public class WarpOverlay : GlowOverlay
{
    protected internal override void Draw(GlowOverlaySystem system, in GlowView view, bool cacheFullSceneState, IGlowDraw draw)
        => system.DrawWarp(this, view, cacheFullSceneState, draw);
}

/// <summary>
/// glow_overlay.cpp and warp_overlay.cpp: CGlowOverlaySystem's list of
/// overlays (CUtlLinkedList's order: activated at the tail, removed in place,
/// the level's end deleting them all), the obstruction of each (the pixel
/// visibility question, or the trace and its fade when the card cannot
/// answer), the sprites' size, colour, basis and quad, and the sky overlays'
/// obstruction. Held to the pristine files by GlowOverlayOracleTests over
/// Fixtures/glowoverlay-stock.tsv.
/// </summary>
public sealed class GlowOverlaySystem
{
    /// <summary>The client effect precache registrations stock made, in their files' order.</summary>
    public static IReadOnlyList<(string Name, string[] Materials)> Precaches
        => [("PrecacheEffectGlow", ["sun/overlay", "sprites/light_glow02_add_noz"]), ("PrecacheEffectWarp", ["sun/overlay"])];

    public const string DefaultMaterial = "sprites/light_glow02_add_noz";
    public const string WarpMaterial = "sun/overlay";
    public const string WireframeMaterial = "debug/debugwireframevertexcolor";
    public const string ClientEffectsGroup = "ClientEffect textures";
    public const string OtherGroup = "Other textures";

    /// <summary>SURF_SKY.</summary>
    public const int SurfSky = 0x0004;

    /// <summary>MAX_TRACE_LENGTH, as Vector's operator* takes it.</summary>
    public static float MaxTraceLength => (float)( 1.732050807569 * ( 2 * 16384 ) );

    /// <summary>g_flOverlayRange: the overlays are seen within 40 degrees of the source.</summary>
    public static float OverlayRange => MathF.Cos(40.0f * ( (float)Math.PI / 180.0f ));

    private readonly LinkedList<GlowOverlay> _overlays = new();
    private readonly IPixelVisibility _pixelVis;
    private readonly IGlowEngine _engine;
    private readonly IGlowSettings _settings;
    private readonly IPixelVisFrame _frame;

    public GlowOverlaySystem(IPixelVisibility pixelVis, IGlowEngine engine, IGlowSettings settings, IPixelVisFrame frame)
    {
        _pixelVis = pixelVis;
        _engine = engine;
        _settings = settings;
        _frame = frame;
    }

    /// <summary>The overlays listed, active or not, in the list's order.</summary>
    public IEnumerable<GlowOverlay> Overlays => _overlays;

    /// <summary>How many are listed.</summary>
    public int Count => _overlays.Count;

    //-------------------------------------------------------------------------
    // The list
    //-------------------------------------------------------------------------

    /// <summary>CGlowOverlay::Activate: active, and listed once.</summary>
    public void Activate(GlowOverlay overlay)
    {
        overlay.Activated = true;
        overlay.Node ??= _overlays.AddLast(overlay);
    }

    /// <summary>CGlowOverlay::Deactivate: still listed.</summary>
    public static void Deactivate(GlowOverlay overlay) => overlay.Activated = false;

    /// <summary>The owner's delete: the overlay's removal from the list.</summary>
    public void Remove(GlowOverlay overlay)
    {
        overlay.OnRemoved();
        if (overlay.Node is { } node)
        {
            _overlays.Remove(node);
            overlay.Node = null;
        }
    }

    /// <summary>LevelShutdownPostEntity: every overlay deleted (PurgeAndDeleteElements, head first).</summary>
    public void LevelShutdownPostEntity()
    {
        for (LinkedListNode<GlowOverlay>? node = _overlays.First; node is not null;)
        {
            LinkedListNode<GlowOverlay>? next = node.Next;
            Remove(node.Value);
            node = next;
        }
        _overlays.Clear();
    }

    //-------------------------------------------------------------------------
    // The draws
    //-------------------------------------------------------------------------

    /// <summary>
    /// CGlowOverlay::DrawOverlays: clipping on (its old state kept), each
    /// active overlay updated and drawn with clipping off in the sky and the
    /// old state elsewhere (an Update that answers false removes it), and the
    /// old state put back.
    /// </summary>
    public void DrawOverlays(in GlowView view, bool cacheFullSceneState, IGlowDraw draw)
    {
        draw.Clip(GlowClip.EnableAndSave);
        for (LinkedListNode<GlowOverlay>? node = _overlays.First; node is not null;)
        {
            LinkedListNode<GlowOverlay>? next = node.Next;
            GlowOverlay overlay = node.Value;
            node = next;
            if (!overlay.Activated)
                continue;

            if (overlay.Update(_frame.FrameTime))
            {
                draw.Clip(overlay.InSky ? GlowClip.Off : GlowClip.Saved);
                overlay.Draw(this, view, cacheFullSceneState, draw);
            }
            else
            {
                Remove(overlay);
            }
        }
        draw.Clip(GlowClip.Saved);
    }

    /// <summary>CGlowOverlay::UpdateSkyOverlays: each active directional overlay in the sky asks its obstruction.</summary>
    public void UpdateSkyOverlays(in Vector viewOrigin, float zFar, bool cacheFullSceneState)
    {
        for (LinkedListNode<GlowOverlay>? node = _overlays.First; node is not null;)
        {
            LinkedListNode<GlowOverlay>? next = node.Next;
            GlowOverlay overlay = node.Value;
            node = next;
            if (!overlay.Activated || !overlay.Directional || !overlay.InSky)
                continue;
            UpdateSkyGlowObstruction(overlay, viewOrigin, zFar, cacheFullSceneState);
        }
    }

    private void UpdateSkyGlowObstruction(GlowOverlay o, in Vector viewOrigin, float zFar, bool cacheFullSceneState)
    {
        // If we already cached the sky obstruction and are still using that, early-out
        if (cacheFullSceneState && o.CacheSkyObstruction)
            return;
        // Turning on sky obstruction caching mode
        if (cacheFullSceneState && !o.CacheSkyObstruction)
            o.CacheSkyObstruction = true;
        // Turning off sky obstruction caching mode
        if (!cacheFullSceneState && o.CacheSkyObstruction)
            o.CacheSkyObstruction = false;

        if (_pixelVis.IsAvailable)
        {
            Vector pos = FxMath.Add(viewOrigin, FxMath.Mul(FxMath.Mul(o.Direction, zFar), 0.999f));
            var p = new PixelVisParams(pos, o.ProxyRadius, 1.0f, PixelVisParams.DefaultFadeTime, SizeInScreenspace: true);
            o.SkyObstructionScale = _pixelVis.FractionVisible(p, ref o.QueryHandle);
            return;
        }

        _engine.TraceSolid(viewOrigin, FxMath.Add(viewOrigin, FxMath.Mul(o.Direction, MaxTraceLength)), out _, out int flags);
        o.SkyObstructionScale = ( flags & SurfSky ) != 0 ? 1.0f : 0.0f;
    }

    private void UpdateGlowObstruction(GlowOverlay o, in GlowView view, in Vector toGlow, bool cacheFullSceneState)
    {
        // If we already cached the glow obstruction and are still using that, early-out
        if (cacheFullSceneState && o.CacheGlowObstruction)
            return;
        if (cacheFullSceneState && !o.CacheGlowObstruction)
            o.CacheGlowObstruction = true;
        if (!cacheFullSceneState && o.CacheGlowObstruction)
            o.CacheGlowObstruction = false;

        if (_pixelVis.IsAvailable)
        {
            if (o.InSky)
            {
                Vector pos = FxMath.Add(view.Origin, FxMath.Mul(o.Direction, view.ZFar * 0.999f));
                var p = new PixelVisParams(pos, o.ProxyRadius, o.CalcGlowAspect(), PixelVisParams.DefaultFadeTime, SizeInScreenspace: true);
                // use a pixel query to occlude with models
                o.GlowObstructionScale = _pixelVis.FractionVisible(p, ref o.QueryHandle) * o.SkyObstructionScale;
            }
            else
            {
                // If it's not in the sky, then we need a valid position or else we don't
                // know what's in front of it.
                var p = new PixelVisParams(o.Pos, o.ProxyRadius, o.CalcGlowAspect());
                o.GlowObstructionScale = _pixelVis.FractionVisible(p, ref o.QueryHandle);
            }
            return;
        }

        bool fade;
        if (o.InSky)
        {
            // Trace a ray at the object.
            _engine.TraceSolid(view.Origin, FxMath.Add(view.Origin, FxMath.Mul(toGlow, MaxTraceLength)), out float fraction, out int flags);
            fade = fraction < 1 && ( flags & SurfSky ) == 0;
        }
        else
        {
            var p = new PixelVisParams(o.Pos, o.ProxyRadius);
            fade = _pixelVis.FractionVisible(p, ref o.QueryHandle) < 1.0f;
        }

        if (fade)
        {
            if (_settings.BuildingCubemaps)
            {
                o.GlowObstructionScale = 0.0f;
            }
            else
            {
                o.GlowObstructionScale -= _frame.FrameTime / _settings.SunDecayRate;
                o.GlowObstructionScale = FxMath.Max(o.GlowObstructionScale, 0.0f);
            }
        }
        else
        {
            if (_settings.BuildingCubemaps)
            {
                o.GlowObstructionScale = 1.0f;
            }
            else
            {
                o.GlowObstructionScale += _frame.FrameTime / _settings.SunDecayRate;
                o.GlowObstructionScale = FxMath.Min(o.GlowObstructionScale, 1.0f);
            }
        }
    }

    // RemapValClamped (mathlib.h).
    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float cVal = ( val - a ) / ( b - a );
        cVal = FxMath.Clamp(cVal, 0.0f, 1.0f);
        return c + ( d - c ) * cVal;
    }

    // CalcSpriteColorAndSize: largest and wholly translucent at the overlay range, smaller and opaque head on.
    private static void CalcSpriteColorAndSize(GlowOverlay o, float dot, GlowSprite sprite, out float horzSize, out float vertSize,
                                               out Vector color)
    {
        const float sizeAtOverlayRangeMul = 150;
        const float sizeAtOneMul = 70;
        const float opacityAtOverlayRange = 0;
        const float opacityAtOne = 1;
        float range = OverlayRange;

        horzSize = RemapValClamped(dot, range, 1, sizeAtOverlayRangeMul * sprite.HorzSize, sizeAtOneMul * sprite.HorzSize);
        vertSize = RemapValClamped(dot, range, 1, sizeAtOverlayRangeMul * sprite.VertSize, sizeAtOneMul * sprite.VertSize);
        float opacity = RemapValClamped(dot, range, 1, opacityAtOverlayRange, opacityAtOne);
        opacity = opacity * o.GlowObstructionScale;
        color = FxMath.Mul(sprite.Color, opacity);
    }

    // CalcBasis: a hundred units out along the line to the glow, its up and right scaled to the sprite's size.
    private static void CalcBasis(in Vector viewOrigin, in Vector toGlow, float horzSize, float vertSize, out Vector basePt, out Vector up,
                                  out Vector right)
    {
        const float overlayDist = 100;
        basePt = FxMath.Add(viewOrigin, FxMath.Mul(toGlow, overlayDist));
        up = new Vector(0, 0, 1);
        right = FxMath.Cross(toGlow, up);
        FxMath.Normalize(ref right);
        up = FxMath.Cross(right, toGlow);
        FxMath.Normalize(ref up);
        right = FxMath.Mul(right, horzSize);
        up = FxMath.Mul(up, vertSize);
    }

    private static Vector ToGlow(GlowOverlay o, in GlowView view)
    {
        Vector toGlow = o.Directional ? o.Direction : FxMath.Sub(o.Pos, view.Origin);
        FxMath.Normalize(ref toGlow);
        return toGlow;
    }

    // The quad's corners in stock's order, with its texture coordinates.
    private static void Corners(in Vector basePt, in Vector right, in Vector up, in Vector color, Span<GlowVertex> v)
    {
        v[0] = new GlowVertex(FxMath.Add(FxMath.Sub(basePt, right), up), color, 0, 1);
        v[1] = new GlowVertex(FxMath.Add(FxMath.Add(basePt, right), up), color, 1, 1);
        v[2] = new GlowVertex(FxMath.Sub(FxMath.Add(basePt, right), up), color, 1, 0);
        v[3] = new GlowVertex(FxMath.Sub(FxMath.Sub(basePt, right), up), color, 0, 0);
    }

    // R_CullSphere over the frustum's first five planes.
    private static bool CullSphere(GlowPlane[] planes, in Vector center, float radius)
    {
        for (int i = 0; i < 5 && i < planes.Length; i++)
        {
            if (planes[i].DistTo(center) < -radius)
                return true;
        }
        return false;
    }

    /// <summary>CGlowOverlay::Draw.</summary>
    internal void DrawGlow(GlowOverlay o, in GlowView view, bool cacheFullSceneState, IGlowDraw draw)
    {
        int drawSprites = _settings.DrawSprites;
        if (drawSprites == 0)
            return;

        // Get the vector to the sun.
        Vector toGlow = ToGlow(o, view);
        float dot = FxMath.Dot(toGlow, view.Forward);

        UpdateGlowObstruction(o, view, toGlow, cacheFullSceneState);
        if (o.GlowObstructionScale == 0)
            return;

        bool wireframe = _settings.Wireframe || drawSprites == 2;
        Span<GlowVertex> quad = stackalloc GlowVertex[4];
        Span<Vector> positions = stackalloc Vector[4];
        for (int i = 0; i < o.NSprites; i++)
        {
            GlowSprite sprite = o.Sprites[i];

            // Figure out the color and size to draw it.
            CalcSpriteColorAndSize(o, dot, sprite, out float horzSize, out float vertSize, out Vector color);

            // If we're alpha'd out, then don't bother
            if (color.LengthSquared < 0.00001f)
                continue;

            // Setup the basis to draw the sprite.
            CalcBasis(view.Origin, toGlow, horzSize, vertSize, out Vector basePt, out Vector up, out Vector right);

            //Get our diagonal radius
            float radius = FxMath.Add(right, up).Length;
            if (CullSphere(view.Frustum, basePt, radius))
                continue;

            // Get our material (deferred default load)
            if (sprite.Material == 0)
                sprite.Material = _engine.FindMaterial(DefaultMaterial, ClientEffectsGroup);

            draw.HdrColorScale(sprite.Material, o.HdrColorScale);

            Corners(basePt, right, up, color, quad);
            draw.Quad(sprite.Material, quad);

            if (wireframe)
            {
                int wire = _engine.FindMaterial(WireframeMaterial, OtherGroup);
                for (int k = 0; k < 4; k++)
                    positions[k] = quad[k].Position;
                draw.WireframeQuad(wire, positions);
            }
        }
    }

    /// <summary>CWarpOverlay::Draw.</summary>
    internal void DrawWarp(GlowOverlay o, in GlowView view, bool cacheFullSceneState, IGlowDraw draw)
    {
        // Get the vector to the sun.
        Vector toGlow = ToGlow(o, view);
        float dot = FxMath.Dot(toGlow, view.Forward);
        if (dot <= OverlayRange)
            return;

        UpdateGlowObstruction(o, view, toGlow, cacheFullSceneState);
        if (o.GlowObstructionScale == 0)
            return;

        Span<GlowVertex> quad = stackalloc GlowVertex[4];
        for (int i = 0; i < o.NSprites; i++)
        {
            GlowSprite sprite = o.Sprites[i];

            // Figure out the color and size to draw it.
            CalcSpriteColorAndSize(o, dot, sprite, out float horzSize, out float vertSize, out Vector color);

            // Setup the basis to draw the sprite.
            CalcBasis(view.Origin, toGlow, horzSize, vertSize, out Vector basePt, out Vector up, out Vector right);

            int material = _engine.FindMaterial(WarpMaterial, ClientEffectsGroup);
            Corners(basePt, right, up, color, quad);
            draw.Quad(material, quad);
        }
    }
}

/// <summary>
/// The two warp overlays this client makes: an explosion's fireball flash
/// (c_te_explosion.cpp's CExplosionOverlay, sprites growing sixteen units a
/// second) and the gunship impact's (fx.cpp's CImpactOverlay, one a second).
/// Each fades its sprites from their base colours over a tenth of a second,
/// then answers false.
/// </summary>
public sealed class RampOverlay(float growth) : WarpOverlay
{
    /// <summary>CExplosionOverlay's growth.</summary>
    public const float ExplosionGrowth = 16.0f;

    /// <summary>CImpactOverlay's growth.</summary>
    public const float ImpactGrowth = 1.0f;

    public float Lifetime;
    public readonly Vector[] BaseColors = new Vector[MaxSunLayers];

    public override bool Update(float frameTime)
    {
        Lifetime += frameTime;

        const float totalLifetime = 0.1f;
        if (Lifetime < totalLifetime)
        {
            float colorScale = 1.0f - ( Lifetime / totalLifetime );
            for (int i = 0; i < NSprites; i++)
            {
                Sprites[i].Color = FxMath.Mul(BaseColors[i], colorScale);
                Sprites[i].HorzSize += growth * frameTime;
                Sprites[i].VertSize += growth * frameTime;
            }
            return true;
        }
        return false;
    }
}
