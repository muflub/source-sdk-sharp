//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// beamdraw.cpp's two camera-facing quads, as the weapons' draws use them over
/// a bound material: <c>DrawSprite</c> (turned to face the eye, a colour in
/// bytes) and <c>DrawHalo</c> (laid on the view's own axes, a colour in
/// floats).
/// </summary>
public static class EffectQuads
{
    /// <summary>
    /// <c>DrawSprite( origin, width, height, color )</c>: a quad
    /// <paramref name="width"/> by <paramref name="height"/> about
    /// <paramref name="origin"/>, its right the view's up crossed with the way
    /// back to the eye. The colour is bytes (<c>Color4ub</c>).
    /// </summary>
    public static void Sprite(Span<FxVertex> quad, Vector origin, float width, float height,
                              byte r, byte g, byte b, byte a, in CurrentViewState view)
    {
        width *= 0.5f;
        height *= 0.5f;

        Vector right = new(1, 0, 0), up = new(0, 1, 0);
        Vector toEye = view.Origin - origin;

        if (toEye.Length >= 1e-3f)
        {
            Vector fwd = toEye.Normalized;
            Vector side = view.Up.Cross(fwd);

            if (side.Length >= 1e-3f)
            {
                right = side.Normalized;
                up = fwd.Cross(right);
            }
            else
            {
                up = fwd.Cross(view.Right).Normalized;
                right = up.Cross(fwd);
            }
        }

        Vector n = Vector.Zero;
        quad[0] = new FxVertex(origin - up * height - right * width, 0f, 1f, r, g, b, a, n);
        quad[1] = new FxVertex(origin + up * height - right * width, 0f, 0f, r, g, b, a, n);
        quad[2] = new FxVertex(origin + up * height + right * width, 1f, 0f, r, g, b, a, n);
        quad[3] = new FxVertex(origin - up * height + right * width, 1f, 1f, r, g, b, a, n);
    }

    /// <summary>
    /// <c>DrawHalo( material, source, scale, color )</c>: a quad
    /// <paramref name="scale"/> out from <paramref name="source"/> along the
    /// view's up and right. The colour is floats (<c>Color3fv</c>, which writes
    /// an opaque alpha).
    /// </summary>
    public static void Halo(Span<FxVertex> quad, Vector source, float scale, Vector color, in CurrentViewState view)
    {
        Vector up = view.Up * scale, right = view.Right * scale, n = Vector.Zero;
        quad[0] = new FxVertex(source - up - right, 0f, 1f, color.X, color.Y, color.Z, 1f, n);
        quad[1] = new FxVertex(source + up - right, 0f, 0f, color.X, color.Y, color.Z, 1f, n);
        quad[2] = new FxVertex(source + up + right, 1f, 0f, color.X, color.Y, color.Z, 1f, n);
        quad[3] = new FxVertex(source - up + right, 1f, 1f, color.X, color.Y, color.Z, 1f, n);
    }
}
