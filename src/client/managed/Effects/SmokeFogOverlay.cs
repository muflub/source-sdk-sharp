//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// smoke_fog_overlay.cpp's <c>DrawSmokeFogOverlay</c> decision: whether the
/// screen-space smoke quad is drawn this frame, and the colour and alpha it is
/// drawn with. Stock overwrites the overlay colour with a fixed grey on every
/// draw (whatever was set before), then clamps both to 0..1; a zero alpha, or
/// no material, draws nothing. Native holds the material and builds the quad.
/// Replayed against the recorded overlay (ClientSmokeFogOverlayTests).
/// </summary>
public sealed class SmokeFogOverlay : IClientSmokeFogHost
{
    /// <summary>Quads drawn since the load (cs_cl_fx_report).</summary>
    public long Drawn { get; private set; }

    public bool Plan(float alpha, bool hasMaterial, ref Vector color, out Vector drawColor, out float drawAlpha)
    {
        drawColor = default;
        drawAlpha = 0;

        if (alpha == 0 || !hasMaterial)
            return false;

        color = new Vector(0.3f, 0.3f, 0.3f);

        drawColor = new Vector(Clamp01(color.X), Clamp01(color.Y), Clamp01(color.Z));
        drawAlpha = Clamp01(alpha);
        Drawn++;
        return true;
    }

    // MIN( MAX( x, 0 ), 1 ), the macros' comparisons in their order.
    private static float Clamp01(float x)
    {
        float lo = x > 0 ? x : 0;
        return lo < 1 ? lo : 1;
    }
}
