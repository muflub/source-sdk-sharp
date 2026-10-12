//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>The large funnel temp entity's sprites (<c>C_TELargeFunnel::CreateFunnel</c>).</summary>
public interface ILargeFunnel
{
    /// <summary>The particles started.</summary>
    int Funnel(Vector origin, int reversed);
}

/// <summary>
/// <c>C_TELargeFunnel::CreateFunnel</c> (c_te_largefunnel.cpp, lane
/// c5-particles): 22 x 22 <c>sprites/flare6</c> particles on a 24-unit grid,
/// half small and green and half large and white. Reversed starts each at the
/// origin and throws it up and out, fading out; forward starts each on the grid
/// up to 800 units above and pulls it in, fading in. Replayed against
/// <c>Fixtures/funnel-stock.tsv.gz</c>.
/// </summary>
public sealed class LargeFunnel(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                                IEngineHost host, IRandomSeeds seeds) : ILargeFunnel
{
    private const float Ratio = 0.25f;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    public int Funnel(Vector origin, int reversed)
    {
        SimpleSheet sheet = Create(origin, reversed != 0, _random);
        int n = sheet.Pending.Count;
        sheet.EffectId = effects.Start(sheet, origin);
        return n;
    }

    /// <summary>The funnel's sheet, not yet started.</summary>
    public SimpleSheet Create(Vector origin, bool reversed, IParticleRandom random)
    {
        var sheet = new SimpleSheet("sprites/flare6", random, views, visibility, () => host.CurrentTime);
        const float invRatio = 1 / Ratio;
        for (int i = -256; i <= 256; i += 24)
        {
            for (int j = -256; j <= 256; j += 24)
            {
                var p = new SimpleParticle();
                Vector dir;
                if (reversed)
                {
                    p.Position = origin;
                    dir = new Vector(i, j, random.RandomFloat(100, 800));
                    p.StartAlpha = 255;
                    p.EndAlpha = 0;
                }
                else
                {
                    p.Position = new Vector(origin.X + i, origin.Y + j, origin.Z + random.RandomFloat(100, 800));
                    // Heading to the origin at a random speed.
                    dir = origin - p.Position;
                    p.StartAlpha = 0;
                    p.EndAlpha = 255;
                }

                p.Velocity = dir * Ratio;
                p.Lifetime = 0;
                p.DieTime = invRatio;

                if (random.RandomInt(0, 10) < 5)
                {
                    // Small and green.
                    p.R = 0;
                    p.G = 255;
                    p.B = 0;
                    p.StartSize = 4;
                }
                else
                {
                    // Large and white.
                    p.R = 255;
                    p.G = 255;
                    p.B = 255;
                    p.StartSize = 15;
                }

                p.EndSize = p.StartSize;
                p.Roll = i; // pseudorandom
                p.RollDelta = 0;
                p.Flags = 0;
                sheet.Add(p);
            }
        }
        return sheet;
    }
}
