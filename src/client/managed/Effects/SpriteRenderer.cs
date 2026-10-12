//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

namespace SourceSharp.Client.Effects;

/// <summary>
/// What one sprite draw is decided from: everything <c>C_SpriteRenderer::DrawSprite</c>
/// read that native has to answer (the sprite model's orientation, extents and
/// size, the attachment's place, the renderer's blend, the view) and the
/// caller's arguments.
/// </summary>
public struct SpriteDrawRequest
{
    /// <summary><c>r_drawsprites</c>: 0 off, 1 on, 2 wireframe.</summary>
    public int DrawSprites;

    /// <summary><c>ShouldDrawInWireFrameMode()</c>.</summary>
    public bool WireframeMode;

    /// <summary>The sprite's <c>$spriteorientation</c> (a SPRITETYPE).</summary>
    public int Orientation;

    /// <summary><c>CEngineSprite::GetLeft/GetRight/GetUp/GetDown</c>.</summary>
    public float Left, Right, Up, Down;

    /// <summary><c>modelinfo-&gt;GetModelSpriteWidth/Height</c>.</summary>
    public float Width, Height;

    /// <summary>The origin the caller passed (the axes are worked from it).</summary>
    public Vector Origin;

    /// <summary>Where the quad is drawn: the origin, or the attachment's place.</summary>
    public Vector EffectOrigin;

    public QAngle Angles;
    public int RenderMode, RenderFx, Alpha, R, G, B;
    public float Scale;

    /// <summary>The scale is in world units (a managed sprite's <c>m_bWorldSpaceScale</c>).</summary>
    public bool WorldSpaceScale;

    public float HdrColorScale;

    /// <summary><c>render-&gt;GetBlend()</c> on entry.</summary>
    public float Blend;

    public Vector ViewForward, ViewRight, ViewUp;

    /// <summary>The glow's pixel-visibility proxy size.</summary>
    public float GlowProxySize;
}

/// <summary>What native carries out for one sprite draw.</summary>
public struct SpriteDrawPlan
{
    /// <summary><c>DrawSprite</c>'s answer.</summary>
    public bool Drawn;

    /// <summary>Whether <see cref="Blend"/> is set on the renderer.</summary>
    public bool SetBlend;

    public float Blend;

    /// <summary>Whether the quad is built (it is when <see cref="Drawn"/>).</summary>
    public bool Quad;

    /// <summary>Bind <c>debug/debugspritewireframe</c> rather than the sprite's material.</summary>
    public bool Wireframe;

    /// <summary>The colour bytes every corner carries.</summary>
    public byte R, G, B, A;

    /// <summary>The corners in stock's order: (minU,maxV), (minU,minV), (maxU,minV), (maxU,maxV).</summary>
    public Vector P0, P1, P2, P3;

    public float HdrColorScale;
}

/// <summary>
/// The sprite renderer's decisions (c_sprite.cpp's <c>C_SpriteRenderer::DrawSprite</c>,
/// <c>GetSpriteAxes</c>, <c>DrawSpriteModel</c> and c_pixel_visibility.cpp's
/// <c>StandardGlowBlend</c>, as our shell_sprite_leaf.cpp carried them): whether
/// a sprite draws, the blend and colour it draws with, and the quad. Native
/// asks the model and the engine and builds the mesh from the plan.
/// Replayed against the recorded draws (ClientSpriteRendererTests).
/// </summary>
public sealed class SpriteRenderer
{
    /// <summary>SPRITETYPE.</summary>
    public const int VpParallelUpright = 0, FacingUpright = 1, VpParallel = 2, Oriented = 3, VpParallelOriented = 4;

    private readonly ILog _log;

    public SpriteRenderer(ILog log) => _log = log;

    /// <summary>One <c>DrawSprite</c>.</summary>
    public SpriteDrawPlan Plan(in SpriteDrawRequest req, IFxSpriteGlow glow)
    {
        var plan = new SpriteDrawPlan();

        if (req.DrawSprites == 0)
            return plan;

        // SourceSharpDrawSpriteCore's: a world-space scale is in units, and the
        // quad multiplies by the sprite's own size, so the size comes back out.
        float scale = req.Scale;
        if (req.WorldSpaceScale)
        {
            float minSize = req.Width < req.Height ? req.Width : req.Height;
            if (minSize > 0.0f)
                scale /= minSize;
        }

        int r = req.R, g = req.G, b = req.B;

        if (!ClientSpriteRenderRules.Opaque(req.RenderMode))
        {
            float blend = req.Blend;

            if (ClientSpriteRenderRules.UsesGlowBlend(req.RenderMode))
            {
                blend *= GlowBlend(req, glow, ref scale);

                // Fade out the sprite depending on distance from the view origin.
                r = (int)(r * blend);
                g = (int)(g * blend);
                b = (int)(b * blend);
            }

            plan.SetBlend = true;
            plan.Blend = blend;

            if (blend <= 0.0f)
                return plan;
        }

        AxesFor(req.Orientation, req.Origin, req.Angles, req.ViewForward, req.ViewRight, req.ViewUp,
                out _, out Vector right, out Vector up);

        plan.Drawn = true;
        DrawSpriteModel(req, ref plan, scale, r, g, b, right, up);
        return plan;
    }

    // DrawSpriteModel: the quad about the effect origin.
    private static void DrawSpriteModel(in SpriteDrawRequest req, ref SpriteDrawPlan plan, float fscale,
                                        int r, int g, int b, in Vector right, in Vector up)
    {
        float scale = fscale > 0 ? fscale : 1.0f;

        if (ClientSpriteRenderRules.Opaque(req.RenderMode))
        {
            plan.SetBlend = true;
            plan.Blend = 1.0f;
        }

        plan.Quad = true;
        plan.Wireframe = req.WireframeMode || req.DrawSprites == 2;
        plan.R = (byte)r;
        plan.G = (byte)g;
        plan.B = (byte)b;
        plan.A = (byte)req.Alpha;
        plan.HdrColorScale = req.HdrColorScale;

        Vector vecA = FxMath.MA(req.EffectOrigin, req.Down * scale, up);
        Vector vecB = FxMath.Mul(right, req.Left * scale);
        Vector vecC = FxMath.MA(req.EffectOrigin, req.Up * scale, up);
        Vector vecD = FxMath.Mul(right, req.Right * scale);

        plan.P0 = FxMath.Add(vecA, vecB);
        plan.P1 = FxMath.Add(vecC, vecB);
        plan.P2 = FxMath.Add(vecC, vecD);
        plan.P3 = FxMath.Add(vecA, vecD);
    }

    // C_SpriteRenderer::GlowBlend, then StandardGlowBlend.
    private static float GlowBlend(in SpriteDrawRequest req, IFxSpriteGlow glow, ref float scale)
    {
        float brightness = glow.FractionVisible(req.EffectOrigin, req.GlowProxySize, Aspect(req));
        if (brightness <= 0.0f)
            return 0.0f;

        float dist = glow.SightDistance(req.EffectOrigin);
        if (dist <= 0.0f)
            return 0.0f;

        if (req.RenderFx == ClientSpriteRenderRules.RenderFxNoDissipation)
            return (float)req.Alpha * (1.0f / 255.0f) * brightness;

        // UNDONE: Tweak these magic numbers (stock's).
        const float full = ClientSpriteRenderRules.GlowFullDistance;
        float fadeOut = (full * full) / (dist * dist);
        fadeOut = FxMath.Clamp(fadeOut, 0.0f, 1.0f);

        if (ClientSpriteRenderRules.GlowIsScreenSpace(req.RenderMode))
        {
            // Make the glow fixed size in screen space, taking into consideration the scale setting.
            if (scale == 0.0f)
                scale = 1.0f;

            scale *= dist * (1.0f / ClientSpriteRenderRules.GlowScaleDivisor);
        }

        return fadeOut * brightness;
    }

    // SpriteAspect: width over height from the extents, 1 when either is zero.
    private static float Aspect(in SpriteDrawRequest req)
    {
        float x = MathF.Abs(req.Right - req.Left);
        float y = MathF.Abs(req.Down - req.Up);
        if (y != 0 && x != 0)
            return x / y;
        return 1.0f;
    }

    /// <summary>
    /// <c>C_SpriteRenderer::GetSpriteAxes</c>. The two upright types return
    /// with the outputs untouched when the view (or, for FACING_UPRIGHT, the
    /// origin taken as a direction, as stock writes it) is within a degree of
    /// vertical; the outputs are then whatever the caller passed in.
    /// </summary>
    public void GetSpriteAxes(int type, in Vector origin, in QAngle angles,
                              in Vector viewForward, in Vector viewRight, in Vector viewUp,
                              ref Vector forward, ref Vector right, ref Vector up)
    {
        if (angles.Roll != 0 && type == VpParallel)
            type = VpParallelOriented;

        switch (type)
        {
            case FacingUpright:
            {
                // generate the sprite's axes, with vup straight up in worldspace, and
                // r_spritedesc.vright perpendicular to modelorg.
                // This will not work if the view direction is very close to straight up or
                // down, because the cross product will be between two nearly parallel
                // vectors and starts to approach an undefined state, so we don't draw if
                // the two vectors are less than 1 degree apart
                var tvec = new Vector(-origin.X, -origin.Y, -origin.Z);
                FxMath.Normalize(ref tvec);
                float dot = tvec.Z;
                if ((dot > 0.999848f) || (dot < -0.999848f))
                    return;
                up = new Vector(0, 0, 1);
                right = new Vector(tvec.Y, -tvec.X, 0);
                FxMath.Normalize(ref right);
                forward = new Vector(-right.Y, right.X, 0);
                break;
            }
            case VpParallel:
                // generate the sprite's axes, completely parallel to the viewplane.
                up = viewUp;
                right = viewRight;
                forward = viewForward;
                break;
            case VpParallelUpright:
            {
                // generate the sprite's axes, with vup straight up in worldspace, and
                // r_spritedesc.vright perpendicular to vpn.
                float dot = viewForward.Z;
                if ((dot > 0.999848f) || (dot < -0.999848f))
                    return;
                up = new Vector(0, 0, 1);
                right = new Vector(viewForward.Y, -viewForward.X, 0);
                FxMath.Normalize(ref right);
                forward = new Vector(-right.Y, right.X, 0);
                break;
            }
            case Oriented:
                MoveVec.AngleVectors(new Vector(angles.Pitch, angles.Yaw, angles.Roll),
                                     out forward, out right, out up);
                break;
            case VpParallelOriented:
            {
                // generate the sprite's axes, parallel to the viewplane, but rotated in
                // that plane around the center according to the sprite entity's roll
                // angle. So vpn stays the same, but vright and vup rotate.
                float angle = (float)(angles.Roll * (Math.PI * 2.0f / 360.0f));
                float sr = MathF.Sin(angle), cr = MathF.Cos(angle);   // SinCos: sinf, cosf
                forward = viewForward;
                right = new Vector(viewRight.X * cr + viewUp.X * sr,
                                   viewRight.Y * cr + viewUp.Y * sr,
                                   viewRight.Z * cr + viewUp.Z * sr);
                up = new Vector(viewRight.X * -sr + viewUp.X * cr,
                                viewRight.Y * -sr + viewUp.Y * cr,
                                viewRight.Z * -sr + viewUp.Z * cr);
                break;
            }
            default:
                _log.Warning($"GetSpriteAxes: Bad sprite type {type}\n");
                break;
        }
    }

    private void AxesFor(int type, in Vector origin, in QAngle angles,
                         in Vector viewForward, in Vector viewRight, in Vector viewUp,
                         out Vector forward, out Vector right, out Vector up)
    {
        // Stock's are uninitialised locals, so an upright sprite seen from
        // straight above drew with whatever the stack held. Zero axes draw the
        // quad as a point: nothing, which is what the early return meant.
        forward = right = up = new Vector(0, 0, 0);
        GetSpriteAxes(type, origin, angles, viewForward, viewRight, viewUp, ref forward, ref right, ref up);
    }
}
