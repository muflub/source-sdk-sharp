//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary><c>CParticleLightInfo</c> (particle_util.h): a light's place, colour (0..1) and intensity.</summary>
public readonly record struct ParticleLight(Vector Position, Vector Color, float Intensity);

/// <summary>
/// <c>CParticleSphereRenderer</c>'s lighting (particlesphererenderer.h):
/// what colour a lit particle is drawn, before the quad builder rounds it to
/// bytes. Replayed bit for bit against <c>Fixtures/sphere-stock.tsv.gz</c>.
/// </summary>
public static class SphereLighting
{
    /// <summary>
    /// <c>RenderParticle</c>'s colour: the base colour plus the ambient
    /// light, plus the directional one unless the material lights on the GPU
    /// (<c>$USINGPIXELSHADER</c>), then <c>ClampColor</c> to 0..255.
    /// </summary>
    public static Vector Lit(Vector baseColor, in ParticleLight ambient, in ParticleLight directional,
                             bool usingPixelShaders, Vector position)
    {
        Vector color = baseColor;
        AddLightColor(position, ambient, ref color);
        if (!usingPixelShaders)
            AddLightColor(position, directional, ref color);
        return ClampColor(color);
    }

    /// <summary><c>AddLightColor</c>: intensity over the squared distance, 1000 inside 0.01 units.</summary>
    private static void AddLightColor(Vector position, in ParticleLight light, ref Vector color)
    {
        if (light.Intensity == 0)
            return;

        float dx = position.X - light.Position.X;
        float dy = position.Y - light.Position.Y;
        float dz = position.Z - light.Position.Z;
        float dist = dx * dx + dy * dy + dz * dz;
        float amount = dist > 0.0001f ? light.Intensity / dist : 1000.0f;
        color = color + light.Color * amount;
    }

    /// <summary><c>ClampColor</c>: scaled so the brightest channel is at most 255.</summary>
    private static Vector ClampColor(Vector color)
    {
        float max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        return max > 1 ? color * (255.0f / max) : color * 255.0f;
    }
}
