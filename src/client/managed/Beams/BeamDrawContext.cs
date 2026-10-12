using SourceSharp.Abi;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Beams;

/// <summary>How a mesh vertex's colour reaches the mesh builder.</summary>
public enum BeamColorKind
{
    /// <summary><c>Color3fv</c> / <c>Color3f</c>: three floats.</summary>
    Float = 0,

    /// <summary><c>Color3ubv</c>: three bytes.</summary>
    Rgb = 1,

    /// <summary><c>Color4ubv</c>: four bytes.</summary>
    Rgba = 2,
}

/// <summary>The mesh primitives beamdraw.cpp builds (MaterialPrimitiveType_t).</summary>
public enum BeamPrimitive
{
    TriangleStrip = 3,
    Quads = 7,
}

/// <summary>One mesh vertex as a beam hands it over: position, colour (floats,
/// or byte values held in floats), one texture coordinate.</summary>
public struct BeamVertex
{
    public Vector Pos;
    public float R, G, B, A;
    public float U, V;
}

/// <summary>
/// Where a beam's draw goes: the native executor (shell_beam_render.cpp) in
/// the game, a recorder in a test. Every call happens inside one beam's draw.
/// </summary>
public interface IBeamDrawSink
{
    /// <summary><c>Draw_SetSpriteTexture</c>: bind a sprite model's material for a
    /// frame and render mode; false when it has none.</summary>
    bool BindSprite(int modelIndex, int frame, int renderMode);

    /// <summary>Set <c>$hdrcolorscale</c> on a sprite model's material for a render mode.</summary>
    void SetHdrColorScale(int modelIndex, int renderMode, float scale);

    /// <summary>Whether a halo sprite has a glow material (<c>GetMaterial( kRenderGlow )</c>).</summary>
    bool HasGlow(int modelIndex);

    /// <summary>Bind a halo sprite's glow material as it is (no frame set).</summary>
    void BindGlow(int modelIndex);

    /// <summary><c>CBeamSegDraw</c> over the bound material: one strip.</summary>
    void Strip(ReadOnlySpan<BeamSeg> segments);

    /// <summary>One dynamic mesh over the bound material.</summary>
    void Mesh(BeamPrimitive primitive, int primitives, BeamColorKind colorKind, ReadOnlySpan<BeamVertex> vertices);
}

/// <summary>The engine's answers a beam's draw reads.</summary>
public interface IBeamDrawWorld
{
    /// <summary><c>engine-&gt;IsBoxVisible</c>.</summary>
    bool IsBoxVisible(in Vector mins, in Vector maxs);

    /// <summary><c>engine-&gt;CullBox</c>.</summary>
    bool CullBox(in Vector mins, in Vector maxs);

    /// <summary><c>PixelVisibility_FractionVisible</c> for this beam's halo.</summary>
    float HaloVisibility(Beam beam, in Vector origin, float proxySize);

    /// <summary><c>debugoverlay-&gt;ScreenPosition</c>.</summary>
    Vector ScreenPosition(in Vector point);
}

/// <summary>A 4x4 row-major matrix: the engine's world-to-screen transform.</summary>
public readonly struct BeamMatrix
{
    private readonly float[] _m;

    public BeamMatrix(float[] m)
    {
        if (m.Length != 16)
            throw new ArgumentException("a 4x4 matrix is 16 floats", nameof(m));
        _m = m;
    }

    public float this[int row, int col] => _m[row * 4 + col];
}

/// <summary>The view a beam is drawn in, and the clock it is drawn at.</summary>
public readonly record struct BeamView(
    Vector Origin, Vector Forward, Vector Right, Vector Up, Vector Angles,
    Vector MainUp, Vector MainRight, BeamMatrix WorldToScreen,
    float CurTime, float FrameTime, int DrawBeams)
{
    /// <summary><c>FrustumTransform</c> (view_scene.cpp) through <see cref="WorldToScreen"/>:
    /// the point in the unit screen, and whether it is behind the eye.</summary>
    public bool ScreenTransform(in Vector point, out Vector screen)
    {
        BeamMatrix m = WorldToScreen;
        float x = m[0, 0] * point.X + m[0, 1] * point.Y + m[0, 2] * point.Z + m[0, 3];
        float y = m[1, 0] * point.X + m[1, 1] * point.Y + m[1, 2] * point.Z + m[1, 3];
        float w = m[3, 0] * point.X + m[3, 1] * point.Y + m[3, 2] * point.Z + m[3, 3];

        bool behind;
        if (w < 0.001f)
        {
            behind = true;
            x *= 100000;
            y *= 100000;
        }
        else
        {
            behind = false;
            float invw = 1.0f / w;
            x *= invw;
            y *= invw;
        }

        screen = new Vector(x, y, 0.0f);
        return behind;
    }
}
