//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

namespace TF2.Client;

/// <summary>
/// The pyro's vision filter (viewpostprocess.cpp's DrawPyroVignette,
/// CreatePyroSide and DrawPyroPost): with localplayer_visionflags bit 0 set,
/// a vignette of randomised sides that fade in and out is drawn into
/// _rt_ResolvedFullFrameDepth, and dev/pyro_post draws its border bars over
/// the frame. The core calls it at stock's vision point; its pyro_* convars
/// are read by name.
/// </summary>
[SideService(Side.Client)]
public sealed class PyroVision(IUniformRandom random, IEngineConsole console) : IPostProcessContributor
{
    private const int MaxSides = 50;
    private const int Segments = 8;

    /// <summary>One side (TPyroSide).</summary>
    public struct PyroSide
    {
        public float X0, Y0, X1, Y1;
        public float Intensity, IntensityLimit, Rate;
        public bool Horizontal, Increasing, Alive;
    }

    private readonly PyroSide[] _sides = new PyroSide[MaxSides];
    private bool _initialised;
    private int _nextSide;

    /// <summary>The sides, read-only (for the facts).</summary>
    public ReadOnlySpan<PyroSide> Sides => _sides;

    // PryoSideIndexes: which corner's x and y each of a side's six vertices takes.
    private static (int X, int Y) SideIndex(int v) => v switch
    {
        0 => (0, 0), 1 => (0, 1), 2 => (1, 1), 3 => (1, 1), 4 => (0, 0), _ => (1, 0),
    };

    // PryoVignetteSTHorizontal / PryoVignetteSTVertical.
    private static (float S, float T) VignetteSt(bool horizontal, int v) => horizontal
        ? v switch { 0 => (0f, 0f), 1 => (0f, 1f), 2 => (1f, 1f), 3 => (1f, 1f), 4 => (0f, 0f), _ => (1f, 0f) }
        : v switch { 0 => (0f, 0f), 1 => (1f, 0f), 2 => (1f, 1f), 3 => (1f, 1f), 4 => (0f, 0f), _ => (0f, 1f) };

    private float Cvar(string name, float fallback) =>
        float.TryParse(console.FindConVarValue(name), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

    public void AtVisionPoint(PostVisionContext context, IPostOps ops)
    {
        PostProcessInputs input = context.Inputs;
        bool vision = ((int)input.Cvar(PostCvar.localplayer_visionflags) & 0x01) != 0;
        int vignette = (int)Cvar("pyro_vignette", 2);
        if (!vision || input.SupportsPixelShaders20 == 0 || vignette <= 0)
            return;

        if (context.FrameBufferUpdated)
            ops.CopyScreenEffect(input.X, input.Y, input.Width, input.Height, false);

        DrawVignette(context, ops, vignette, input.FrameTime);
        DrawPost(ops, ops.Material("dev/pyro_post"), context.DestX, context.DestY, context.DestWidth, context.DestHeight,
                 context.SrcX0, context.SrcY0, context.SrcX1, context.SrcY1, context.SmallWidth, context.SmallHeight,
                 input.ViewportWidth, input.ViewportHeight);
    }

    private void DrawVignette(PostVisionContext context, IPostOps ops, int vignette, float frameTime)
    {
        int border = ops.Material("dev/pyro_vignette_border");
        int material = ops.Material("dev/pyro_vignette");

        ops.PushRenderTarget(PostTexture.ResolvedFullFrameDepth);
        ops.ClearColor4ub(0, 0, 0, 0);
        ops.ClearBuffers(true, false);
        int screenWidth = context.TextureWidth(PostTexture.ResolvedFullFrameDepth);
        int screenHeight = context.TextureHeight(PostTexture.ResolvedFullFrameDepth);
        ops.DrawRect(border, 0, 0, screenWidth, screenHeight, 0, 0, screenWidth - 1, screenHeight - 1, screenWidth, screenHeight, worldRenderable: true);

        if (vignette > 1)
        {
            const float segment = 2.0f / Segments;
            if (!_initialised)
            {
                for (int i = 0; i < MaxSides; i++)
                    _sides[i].Alive = false;
                CreateSide(_nextSide, segment, segment);
                _nextSide = (_nextSide + 1) & 7;
                _initialised = true;
            }

            int alive = 0;
            for (int i = 0; i < MaxSides; i++)
            {
                ref PyroSide side = ref _sides[i];
                if (side.Alive)
                {
                    if (side.Increasing)
                    {
                        side.Intensity += side.Rate * frameTime;
                        if (side.Intensity >= side.IntensityLimit)
                            side.Increasing = false;
                    }
                    else
                    {
                        side.Intensity -= side.Rate * frameTime;
                        if (side.Intensity <= 0.0f)
                            side.Alive = false;
                    }
                }
                if (side.Alive)
                    alive++;
            }

            if (alive > 0)
            {
                ops.MeshBegin(material, PostPrimitive.Triangles, alive * 2, identityMatrices: true, worldRenderable: true, unlessDisabled: false);
                var tangentS = new Vector(0.0f, 1.0f, 0.0f);
                var tangentT = new Vector(1.0f, 0.0f, 0.0f);
                var normal = new Vector(0.0f, 0.0f, 1.0f);
                for (int i = 0; i < MaxSides; i++)
                {
                    PyroSide side = _sides[i];
                    if (!side.Alive)
                        continue;
                    for (int v = 0; v < 6; v++)
                    {
                        (int cx, int cy) = SideIndex(v);
                        float x = cx == 0 ? side.X0 : side.X1;
                        float y = cy == 0 ? side.Y0 : side.Y1;
                        (float s, float t) = VignetteSt(side.Horizontal, v);
                        ops.MeshVertex(x, y, 0.0f, s, t, (side.Intensity, side.Intensity, side.Intensity, 1.0f),
                                       tangentS, tangentT, normal);
                    }
                }
                ops.MeshEnd();
            }

            if (alive < 25)
            {
                CreateSide(_nextSide, segment, segment);
                _nextSide = (_nextSide + 1) & 7;
            }
        }

        ops.PopRenderTargetAndViewport();
    }

    // CreatePyroSide: the first dead side, sized and placed for screen edge nSide.
    private void CreateSide(int sideNumber, float maxX, float maxY)
    {
        int found = 0;
        for (; found < MaxSides; found++)
        {
            if (!_sides[found].Alive)
                break;
        }
        if (found >= MaxSides)
            return;

        ref PyroSide side = ref _sides[found];
        side.Intensity = 0.0f;
        side.IntensityLimit = random.RandomFloat(Cvar("pyro_min_intensity", 0.1f), Cvar("pyro_max_intensity", 0.35f));
        side.Rate = random.RandomFloat(Cvar("pyro_min_rate", 0.05f), Cvar("pyro_max_rate", 0.2f));
        side.Increasing = true;
        side.Horizontal = ((sideNumber >> 1) & 1) == 0;
        side.Alive = true;

        float width = random.RandomFloat(Cvar("pyro_min_side_width", 0.65f), Cvar("pyro_max_side_width", 0.95f)) * 2.0f;
        float length = random.RandomFloat(Cvar("pyro_min_side_length", 0.3f), Cvar("pyro_max_side_length", 0.55f));

        (side.X0, side.Y0, side.X1, side.Y1) = sideNumber switch
        {
            0 => (-1.0f, 1.0f, -1.0f + width, 1.0f - length * maxY),
            1 => (1.0f, 1.0f, 1.0f - width, 1.0f - length * maxY),
            2 => (1.0f, 1.0f, 1.0f - length * maxX, 1.0f - width),
            3 => (1.0f, -1.0f, 1.0f - length * maxX, -1.0f + width),
            4 => (1.0f, -1.0f, 1.0f - width, -1.0f + length * maxY),
            5 => (-1.0f, -1.0f, -1.0f + width, -1.0f + length * maxY),
            6 => (-1.0f, -1.0f, -1.0f + length * maxX, -1.0f + width),
            _ => (-1.0f, 1.0f, -1.0f + length * maxX, 1.0f - width),
        };
    }

    private static float FLerp(float f1, float f2, float i1, float i2, float x) => f1 + (f2 - f1) * (x - i1) / (i2 - i1);

    // DrawPyroPost: the four border bars of an 8x8 grid over the frame, in clip space.
    private static void DrawPost(IPostOps ops, int material, int destX, int destY, int width, int height,
                                 float srcX0, float srcY0, float srcX1, float srcY1, int srcWidth, int srcHeight,
                                 int viewportWidth, int viewportHeight)
    {
        ops.MeshBegin(material, PostPrimitive.Quads, 4, identityMatrices: true, worldRenderable: true, unlessDisabled: true);

        const float offset = 0.0f; // POSIX
        float leftX = destX - offset, rightX = destX + width - offset;
        float topY = destY - offset, bottomY = destY + height - offset;

        float subrectWidth = srcX1 - srcX0, subrectHeight = srcY1 - srcY0;
        float texelsPerPixelX = width > 1 ? subrectWidth / (width - 1) : 0.0f;
        float texelsPerPixelY = height > 1 ? subrectHeight / (height - 1) : 0.0f;

        float leftU = srcX0 + 0.5f - 0.5f * texelsPerPixelX;
        float rightU = srcX1 + 0.5f + 0.5f * texelsPerPixelX;
        float topV = srcY0 + 0.5f - 0.5f * texelsPerPixelY;
        float bottomV = srcY1 + 0.5f + 0.5f * texelsPerPixelY;
        float ooWidth = 1.0f / srcWidth, ooHeight = 1.0f / srcHeight;
        leftU *= ooWidth;
        rightU *= ooWidth;
        topV *= ooHeight;
        bottomV *= ooHeight;

        rightX = FLerp(-1, 1, 0, viewportWidth, rightX);
        leftX = FLerp(-1, 1, 0, viewportWidth, leftX);
        topY = FLerp(1, -1, 0, viewportHeight, topY);
        bottomY = FLerp(1, -1, 0, viewportHeight, bottomY);

        float w = (rightX - leftX) / (float)Segments, h = (topY - bottomY) / (float)Segments;
        float uw = (rightU - leftU) / (float)Segments, vh = (bottomV - topV) / (float)Segments;

        void V(int x, int y) =>
            ops.MeshVertex(leftX + (float)x * w, topY - (float)y * h, 0.0f, leftU + (float)x * uw, topV + (float)y * vh);

        const int xs = Segments, ys = Segments;
        // Top bar, bottom bar, left bar, right bar.
        V(0, 0); V(xs + 1, 0); V(xs + 1, 1); V(0, 1);
        V(0, ys - 1); V(xs, ys - 1); V(xs, ys); V(0, ys);
        V(0, 1); V(1, 1); V(1, ys - 1); V(0, ys - 1);
        V(xs - 1, 1); V(xs, 1); V(xs, ys - 1); V(xs - 1, ys - 1);

        ops.MeshEnd();
    }
}
