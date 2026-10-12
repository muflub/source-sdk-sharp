//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// What a legacy tempent's blend is worked out from: its render fx, alpha and
/// mode, its (always -1) index, the time, its place and the view's.
/// </summary>
public readonly record struct TempEntBlendInput(int RenderFx, int Alpha, int RenderMode, int Index, float CurTime,
                                                Vector Origin, Vector ViewOrigin, Vector ViewForward);

/// <summary>
/// The render choices c_te_legacytempents.cpp's <c>C_LocalTempEntity</c> made
/// for every drawn tempent: <c>ComputeFxBlend</c> (stock C_BaseEntity's, the
/// render fx's pulse, fade, strobe, flicker and hologram), <c>IsTransparent</c>,
/// <c>GetRenderGroup</c> and <c>DrawModel</c>'s occlusion fade
/// (<c>FTENT_BEOCCLUDED</c>). Native holds the tempent and asks; this answers.
/// Replayed against the recorded tempents (TempEntRenderTests).
/// </summary>
public sealed class TempEntRender(IEngineHost random)
{
    // RenderFx_t (public/const.h): an implicit enum, counted.
    public const int FxNone = 0, PulseSlow = 1, PulseFast = 2, PulseSlowWide = 3, PulseFastWide = 4, FadeSlow = 5,
        FadeFast = 6, SolidSlow = 7, SolidFast = 8, StrobeSlow = 9, StrobeFast = 10, StrobeFaster = 11,
        FlickerSlow = 12, FlickerFast = 13, Distort = 15, Hologram = 16, PulseFastWider = 24;

    // RenderMode_t.
    private const int RenderNormal = 0, RenderNone = 10;

    // RenderGroup_t, as the leaf system numbers them.
    public const int GroupOpaqueEntity = 7, GroupTranslucentEntity = 8, GroupTwoPass = 9;

    /// <summary><c>FTENT_BEOCCLUDED</c>.</summary>
    public const int FlagBeOccluded = 0x00800000;

    /// <summary>
    /// <c>ComputeFxBlend</c> past its once-a-frame guard: the blend, and the
    /// alpha the fade and solid effects (and the hologram) leave behind.
    /// </summary>
    public (int Blend, int Alpha) ComputeFxBlend(in TempEntBlendInput t)
    {
        int a = t.Alpha;
        int blend;
        double offset = t.Index * 363.0;   // Use ent index to de-sync these fx

        switch (t.RenderFx)
        {
            case PulseSlowWide:
                blend = (int)(a + 0x40 * Math.Sin(t.CurTime * 2 + offset));
                break;
            case PulseFastWide:
                blend = (int)(a + 0x40 * Math.Sin(t.CurTime * 8 + offset));
                break;
            case PulseFastWider:
                blend = (int)(0xff * Math.Abs(Math.Sin(t.CurTime * 12 + offset)));
                break;
            case PulseSlow:
                blend = (int)(a + 0x10 * Math.Sin(t.CurTime * 2 + offset));
                break;
            case PulseFast:
                blend = (int)(a + 0x10 * Math.Sin(t.CurTime * 8 + offset));
                break;

            // JAY: HACK for now -- not time based
            case FadeSlow:
                a = a > 0 ? a - 1 : 0;
                blend = a;
                break;
            case FadeFast:
                a = a > 3 ? a - 4 : 0;
                blend = a;
                break;
            case SolidSlow:
                a = a < 255 ? a + 1 : 255;
                blend = a;
                break;
            case SolidFast:
                a = a < 252 ? a + 4 : 255;
                blend = a;
                break;

            case StrobeSlow:
                blend = Strobe((int)(20 * Math.Sin(t.CurTime * 4 + offset)), a);
                break;
            case StrobeFast:
                blend = Strobe((int)(20 * Math.Sin(t.CurTime * 16 + offset)), a);
                break;
            case StrobeFaster:
                blend = Strobe((int)(20 * Math.Sin(t.CurTime * 36 + offset)), a);
                break;
            case FlickerSlow:
                blend = Strobe((int)(20 * (MathF.Sin(t.CurTime * 2) + Math.Sin(t.CurTime * 17 + offset))), a);
                break;
            case FlickerFast:
                blend = Strobe((int)(20 * (MathF.Sin(t.CurTime * 16) + Math.Sin(t.CurTime * 23 + offset))), a);
                break;

            case Hologram:
            case Distort:
            {
                Vector tmp = FxMath.Sub(t.Origin, t.ViewOrigin);
                float dist = FxMath.Dot(tmp, t.ViewForward);

                // Turn off distance fade
                if (t.RenderFx == Distort)
                    dist = 1;

                if (dist <= 0)
                {
                    blend = 0;
                }
                else
                {
                    a = 180;
                    if (dist <= 100)
                        blend = a;
                    else
                        blend = (int)((1.0 - (dist - 100) * (1.0 / 400.0)) * a);
                    blend += random.RandomInt(-32, 31);
                }
                break;
            }

            default:
                blend = t.RenderMode == RenderNormal ? 255 : a;
                break;
        }

        blend = blend < 0 ? 0 : blend > 255 ? 255 : blend;
        return (blend, a);
    }

    // The strobes and flickers: the wave decides on or off, the alpha is the level.
    private static int Strobe(int wave, int alpha) => wave < 0 ? 0 : alpha;

    // The time arguments are float products widened to double, as stock's are:
    // `gpGlobals->curtime * 2 + offset` multiplies in float, then adds a double;
    // the flickers' first `sin( gpGlobals->curtime * 2 )` has a float argument
    // and is C++'s float overload (sinf).

    /// <summary><c>IsTransparent</c>: a translucent model, or any mode but normal.</summary>
    public static bool IsTransparent(bool translucentModel, int renderMode)
        => translucentModel || renderMode != RenderNormal;

    /// <summary>
    /// <c>GetRenderGroup</c>, given the blend it reads (computed this frame)
    /// and the model's two answers.
    /// </summary>
    public static int RenderGroup(int renderMode, int blend, bool translucentModel, bool twoPass)
    {
        // Don't sort things that don't need rendering
        if (renderMode == RenderNone)
            return GroupOpaqueEntity;

        // Don't need to sort invisible stuff
        if (blend == 0)
            return GroupOpaqueEntity;

        int group = GroupOpaqueEntity;
        if (blend != 255 || IsTransparent(translucentModel, renderMode))
            group = GroupTranslucentEntity;

        if (group == GroupTranslucentEntity && twoPass)
            group = GroupTwoPass;

        return group;
    }

    /// <summary>
    /// <c>DrawModel</c>'s occlusion arm: a tempent marked
    /// <c>FTENT_BEOCCLUDED</c> fades as the view comes round behind the
    /// surface it sits on. The alpha it draws with; the alpha it had when the
    /// arm does not apply.
    /// </summary>
    public static int OccludedAlpha(int flags, int alpha, int renderAmt, in Vector normal, in Vector origin, in Vector mainViewOrigin)
    {
        if ((flags & FlagBeOccluded) == 0)
            return alpha;

        // Check normal
        Vector delta = FxMath.Sub(origin, mainViewOrigin);
        FxMath.Normalize(ref delta);
        float dot = FxMath.Dot(normal, delta);
        if (dot > 0)
        {
            float a = FxMath.RemapVal((float)Math.Min(dot, 0.3), 0, 0.3f, 0, 1);
            a = (float)Math.Max(1.0, renderAmt - (renderAmt * a));
            return (byte)(int)a;
        }

        return alpha;
    }
}
