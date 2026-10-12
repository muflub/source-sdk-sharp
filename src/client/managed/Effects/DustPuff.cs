//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>FX_Dust</c> (c_func_dust.cpp, carried in shell_dust_render.cpp until lane
/// c5-particles): a puff of up to 32 simple particles thrown from a point along
/// a direction, tinted by the world light there. Replayed bit for bit against
/// <c>Fixtures/dustpuff-stock.tsv.gz</c>.
///
/// <para>
/// WHERE THEY DRAW. Stock handed each particle to <c>AddSimpleParticle</c>
/// (particles_ez.cpp), whose world singleton is drawn by viewrender.cpp's own
/// <c>DrawParticleSingletons( false )</c> after the world's translucent pass,
/// not through the leaf system. The port's sheets are put in that space
/// (<see cref="ParticleSpace.Singleton"/>), so the same call draws them, on the
/// stock path and at the render plan's ParticleSingletons op alike.
/// </para>
/// </summary>
public static class DustPuff
{
    /// <summary><c>g_Mat_DustPuff</c>: the two materials a puff particle picks between (0 or 1).</summary>
    public static string Material(int index) => index == 0 ? "particle/particle_smokegrenade" : "particle/particle_noisesphere";

    /// <summary>How many particles, and the speed they are thrown at, after stock's clamps.</summary>
    public static (int Puffs, float Speed) Clamp(float size, float speed)
    {
        int puffs = (int)(size * 0.5f);
        if (puffs < 1)
            puffs = 1;
        if (puffs > 32)
            puffs = 32;

        float s = speed * 0.1f;
        if (s < 0)
            s = 1.0f;
        if (s > 48.0f)
            s = 48.0f;
        return (puffs, s);
    }

    /// <summary>Where stock sampled the world light: the origin pushed out along the direction by the size.</summary>
    public static Vector LightSample(Vector origin, Vector direction, float size) => origin + direction * size;

    /// <summary>
    /// The puff's particles in stock's order, each with its material index
    /// (<see cref="SimpleParticle.Material"/>), from the world light at
    /// <see cref="LightSample"/>. Every draw is <paramref name="random"/>'s:
    /// <c>rand()</c> for the scatter, the uniform stream for the rest.
    /// </summary>
    public static List<SimpleParticle> Make(Vector origin, Vector direction, float size, float speed,
                                            Vector worldLight, IParticleRandom random)
    {
        var (puffs, s) = Clamp(size, speed);
        var made = new List<SimpleParticle>(puffs);
        float lo = -(size * 0.25f), hi = size * 0.25f;
        Vector centre = origin + direction * size;

        for (int i = 0; i < puffs; i++)
        {
            // Vector::Random over rand(), then the centre added back.
            float x = lo + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (hi - lo);
            float y = lo + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (hi - lo);
            float z = lo + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (hi - lo);
            Vector offset = new Vector(x, y, z) + centre;

            var p = new SimpleParticle
            {
                Position = offset,
                Lifetime = 0.0f,
                DieTime = random.RandomFloat(0.4f, 1.0f),
            };

            Vector v = direction * random.RandomFloat(s * 0.5f, s) * i;
            v.Z = 0.0f;
            p.Velocity = v;

            int color = random.RandomInt(48, 64);
            p.R = (byte)(int)((color + 16) + (worldLight.X * (float)color));
            p.G = (byte)(int)((color + 8) + (worldLight.Y * (float)color));
            p.B = (byte)(int)(color + (worldLight.Z * (float)color));

            p.StartAlpha = (byte)random.RandomInt(64, 128);
            p.EndAlpha = 0;
            p.StartSize = (byte)random.RandomInt(2, 8);
            p.EndSize = (byte)random.RandomInt(24, 48);
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-0.5f, 0.5f);
            p.Material = random.RandomInt(0, 1);
            made.Add(p);
        }

        return made;
    }
}

/// <summary>The dust puff, started (<c>FX_Dust</c>): IEffects::Dust and the dust temp entity end here.</summary>
public interface IClientDustPuff
{
    /// <summary>The particles started.</summary>
    int Puff(Vector origin, Vector direction, float size, float speed);
}

/// <summary>
/// <see cref="DustPuff"/> started on the particle executor: one sheet per
/// material the puff used, lit by the world at <see cref="DustPuff.LightSample"/>,
/// each in the world singleton space (drawn at <c>DrawParticleSingletons</c>).
/// </summary>
public sealed class ClientDustPuffs(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                                    IClientWorldLight light, IEngineHost host, IRandomSeeds seeds) : IClientDustPuff
{
    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    /// <summary>Puff particles started since load: step 7a's "dust puffs" row (cs_cl_fx_report).</summary>
    public int Particles { get; private set; }

    public int Puff(Vector origin, Vector direction, float size, float speed)
    {
        Vector worldLight = light.LightForPoint(DustPuff.LightSample(origin, direction, size), true);
        List<SimpleParticle> made = DustPuff.Make(origin, direction, size, speed, worldLight, _random);
        var sheets = new SimpleSheet?[2];
        foreach (SimpleParticle p in made)
        {
            int m = p.Material == 0 ? 0 : 1;
            sheets[m] ??= new SimpleSheet(DustPuff.Material(m), _random, views, visibility, () => host.CurrentTime);
            sheets[m]!.Add(p);
        }
        foreach (SimpleSheet? sheet in sheets)
        {
            if (sheet is null)
                continue;
            sheet.EffectId = effects.Start(sheet, origin);
            // AddSimpleParticle's bInSkybox defaulted false: the world singleton.
            effects.SetSpace(sheet.EffectId, 0, 0, ParticleSpace.Singleton);
        }
        Particles += made.Count;
        return made.Count;
    }
}
