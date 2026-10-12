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
/// <c>CExplosionParticle</c>'s rules (fx_explosion.cpp): a roll that slows
/// by e^-8 a second to a floor of 0.5, a velocity decayed to a ten-thousandth
/// in half a second but never below 32 units/s, and a colour and alpha that
/// fade along <c>Bias( 1 - life, 0.25 )</c>.
/// </summary>
public class ExplosionRules : SimpleEmitterRules
{
    /// <summary>The roll's floor and the speed's.</summary>
    protected virtual float MinRoll => 0.5f;

    protected virtual float MinSpeed => 32.0f;

    /// <summary>
    /// <c>exp( log( 0.0001f ) * dt / 0.5 )</c> in float. Stock caches it in two
    /// statics keyed by the dt, stored before it is computed: a function of dt.
    /// </summary>
    public static float Decay(float dt) => MathF.Exp(MathF.Log(0.0001f) * dt / 0.5f);

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -8.0f);
        if (MathF.Abs(p.RollDelta) < MinRoll)
            p.RollDelta = p.RollDelta > 0.0f ? MinRoll : -MinRoll;
        return p.Roll;
    }

    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        Vector save = p.Velocity;
        float decay = Decay(dt);
        p.Velocity = p.Velocity * decay;
        float lengthSqr = p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y + p.Velocity.Z * p.Velocity.Z;
        if (lengthSqr < MinSpeed * MinSpeed)
        {
            FxMath.Normalize(ref save);
            p.Velocity = save * MinSpeed;
        }
    }

    public override float UpdateAlpha(in SimpleParticle p)
    {
        float tLifetime = p.Lifetime / p.DieTime;
        return FxMath.Bias(1.0f - tLifetime, 0.25f);
    }

    public override Vector UpdateColor(in SimpleParticle p)
    {
        float tLifetime = p.Lifetime / p.DieTime;
        float ramp = FxMath.Bias(1.0f - tLifetime, 0.25f);
        return new Vector(((float)p.R * ramp) / 255.0f, ((float)p.G * ramp) / 255.0f, ((float)p.B * ramp) / 255.0f);
    }
}

/// <summary>
/// <c>CWaterExplosionParticle</c>'s rules: the explosion's, with a roll floor
/// of 0.25, a speed floor of 8, the default colour, and an alpha that falls
/// linearly and then, below 0.75, as its square.
/// </summary>
public sealed class WaterExplosionRules : ExplosionRules
{
    protected override float MinRoll => 0.25f;

    protected override float MinSpeed => 8.0f;

    public override float UpdateAlpha(in SimpleParticle p)
    {
        float tLifetime = p.Lifetime / p.DieTime;
        float ramp = 1.0f - tLifetime;
        if (ramp < 0.75f)
            ramp *= ramp;
        return ramp;
    }

    public override Vector UpdateColor(in SimpleParticle p) => new(p.R / 255.0f, p.G / 255.0f, p.B / 255.0f);
}

/// <summary>fx_explosion.cpp's effects as the explosion temp entity reaches them.</summary>
public interface IClientExplosions
{
    /// <summary><c>BaseExplosionEffect().Create</c> or <c>WaterExplosionEffect().Create</c>.</summary>
    void Create(ExplosionKind kind, Vector position, float force, float scale, int flags);
}

/// <summary>What the explosions read of the client: a masked line, the light, the sounds.</summary>
public interface IExplosionEnvironment
{
    MaskTrace Trace(Vector start, Vector end, uint mask);

    /// <summary><c>WorldGetLightForPoint( position, true )</c> / <c>engine-&gt;GetLightForPoint</c>.</summary>
    Vector LightForPoint(Vector position);

    /// <summary>A game sound to the local player, from the world, at the point.</summary>
    void Sound(string script, Vector origin);
}

/// <summary>The real one.</summary>
public sealed class ExplosionEnvironment(IParticleMaskTrace trace, IClientWorldLight light, IFxGameSounds sounds)
    : IExplosionEnvironment
{
    public MaskTrace Trace(Vector start, Vector end, uint mask) => trace.Trace(start, end, mask);

    public Vector LightForPoint(Vector position) => light.LightForPoint(position, true);

    public void Sound(string script, Vector origin)
    {
        if (sounds.Parameters(script, out GameSoundParams p))
            sounds.EmitAtPosition(p, p.Volume, p.Pitch, origin);
    }
}

/// <summary>Which of fx_explosion.cpp's three effects.</summary>
public enum ExplosionKind
{
    Base,
    Water,
    MegaBomb,
}

/// <summary>What one explosion made, for the replay: the effects in creation order.</summary>
public sealed class ExplosionBurst
{
    public Vector Direction;
    public float Force;
    public List<string> Sounds { get; } = new();
    public List<SimpleSheet> Core { get; } = new();
    public TrailEmitter? Sparks { get; set; }
    public FleckSystem? Flecks { get; set; }
    public List<FleckParticle> FlecksMade { get; } = new();
    public List<SimpleSheet> Bubbles { get; } = new();
    public TrailEmitter? Drops { get; set; }
    public SplashSheet? Splish { get; set; }
    public Vector WaterSurface;
    public float Depth;
}

/// <summary>
/// fx_explosion.cpp's three effects (lane c5-particles): the base explosion
/// (its force probed off the six walls, a core of smoke, embers and
/// fireballs, sparks and cement flecks, the sound), the water explosion (the
/// surface found, a wake, two water shafts, bubbles in deep water, drops and
/// a splash) and the helicopter's mega bomb (fireballs only). Sampled from a
/// managed stream; replayed against <c>Fixtures/explosion-stock.tsv.gz</c>.
/// </summary>
public sealed class ExplosionEffects(ClientParticleEffects effects, IParticleWorldTrace world, ParticleViews views,
                                     IParticlePixelVisibility visibility, IExplosionEnvironment environment,
                                     FleckSystems flecks, IFxQuadSink quads, IFxLineSink lines, IRandomSeeds seeds)
    : IClientExplosions
{
    /// <summary><c>TE_EXPLFLAG_*</c> (tempentity.h).</summary>
    public const int NoSound = 0x4, NoParticles = 0x8, NoFireball = 0x40, NoFireballSmoke = 0x80;

    /// <summary><c>EXPLOSION_FORCE_MIN</c> and <c>_MAX</c>, both 2.</summary>
    public const float ForceMin = 2, ForceMax = 2;

    private const float MaxWaterSurfaceDistance = 512;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    public int Explosions { get; private set; }

    public void Create(ExplosionKind kind, Vector position, float force, float scale, int flags)
        => Create(kind, position, force, scale, flags, _random);

    private static Vector RandomVector(IParticleRandom random, float min, float max)
    {
        float x = random.RandomFloat(min, max);
        float y = random.RandomFloat(min, max);
        float z = random.RandomFloat(min, max);
        return new Vector(x, y, z);
    }

    private static byte UChar(float f) => unchecked((byte)(int)f);

    private static float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static bool IsZero(Vector v) => v.X == 0 && v.Y == 0 && v.Z == 0;

    /// <summary><c>ScaleForceByDeviation</c>: the spread times how far the particle runs along the main direction.</summary>
    public static float ScaleForceByDeviation(Vector deviant, Vector source, float spread, ref float force)
    {
        if (IsZero(deviant) || IsZero(source))
            return 1.0f;
        float dot = Dot(source, deviant);
        dot = spread * MathF.Abs(dot);
        force *= dot;
        return dot;
    }

    private static float ScaleForceByDeviation(Vector deviant, Vector source, float spread)
    {
        float unused = 0;
        if (IsZero(deviant) || IsZero(source))
            return 1.0f;
        return ScaleForceByDeviation(deviant, source, spread, ref unused);
    }

    /// <summary>
    /// <c>GetForceDirection</c>: a line out to each of the six walls at the
    /// magnitude; each wall struck pushes back along its line by how near it
    /// was and adds to the force. Up, at the minimum force, when nothing pushes.
    /// </summary>
    public (Vector Direction, float Force) ForceDirection(Vector origin, float magnitude)
    {
        Span<Vector> d =
        [
            new Vector(1, 0, 0), new Vector(-1, 0, 0), new Vector(0, 1, 0),
            new Vector(0, -1, 0), new Vector(0, 0, 1), new Vector(0, 0, -1),
        ];
        Vector direction = default;
        float force = 1.0f;
        for (int i = 0; i < 6; i++)
        {
            Vector endpos = origin + d[i] * magnitude;
            MaskTrace tr = environment.Trace(origin, endpos, IParticleMaskTrace.ContentsSolid);
            d[i] = new Vector(-d[i].X, -d[i].Y, -d[i].Z) * (1.0f - tr.Fraction);
            force += 1.0f - tr.Fraction;
            direction = direction + d[i];
        }
        if (IsZero(direction))
        {
            direction = new Vector(0, 0, 1);
            force = ForceMin;
        }
        FxMath.Normalize(ref direction);
        return (direction, force);
    }

    /// <summary><c>C_BaseExplosionEffect::Create</c> and its two subclasses'.</summary>
    public ExplosionBurst Create(ExplosionKind kind, Vector origin, float force, float scale, int flags, IParticleRandom random)
    {
        Explosions++;
        var burst = new ExplosionBurst();
        Vector color = default;
        float luminosity = 0;

        if (kind == ExplosionKind.Water)
        {
            // Find the water surface by tracing up until out of the water.
            Vector up = new(0, 0, MaxWaterSurfaceDistance);
            MaskTrace tr = environment.Trace(origin, origin + up, IParticleMaskTrace.MaskWater);
            if (!tr.StartSolid)
            {
                // Above it: look down.
                tr = environment.Trace(origin, origin + new Vector(0, 0, -MaxWaterSurfaceDistance), IParticleMaskTrace.MaskWater);
                burst.WaterSurface = tr.Fraction < 1.0f ? tr.End : origin;
                burst.Depth = 0.0f;
            }
            else if (tr.FractionLeftSolid != 0)
            {
                burst.WaterSurface = origin + up * tr.FractionLeftSolid;
                burst.Depth = MaxWaterSurfaceDistance * tr.FractionLeftSolid;
            }
            else
            {
                burst.WaterSurface = origin;
                burst.Depth = MaxWaterSurfaceDistance;
            }
            (color, luminosity) = WaterMath.SplashLighting(environment.LightForPoint(origin + new Vector(0, 0, 32)));
        }

        (burst.Direction, burst.Force) = ForceDirection(origin, force);

        // PlaySound (the mega bomb's is empty, as are its debris and misc).
        if ((flags & NoSound) == 0 && kind != ExplosionKind.MegaBomb)
        {
            if (kind == ExplosionKind.Water)
            {
                Sound(burst, "Physics.WaterSplash", burst.WaterSurface);
                Sound(burst, burst.Depth > 128 ? "WaterExplosionEffect.Sound" : "BaseExplosionEffect.Sound", origin);
            }
            else
            {
                Sound(burst, "BaseExplosionEffect.Sound", origin);
            }
        }

        if (scale != 0)
        {
            switch (kind)
            {
                case ExplosionKind.Water: WaterCore(burst, flags, random); break;
                case ExplosionKind.MegaBomb: MegaBombCore(burst, origin, flags, random); break;
                default: Core(burst, origin, flags, random); break;
            }
        }

        if (kind == ExplosionKind.Water)
        {
            WaterDebris(burst, origin, flags, color, luminosity, random);
            WaterMisc(burst, color, luminosity, random);
        }
        else if (kind == ExplosionKind.Base)
        {
            Debris(burst, origin, flags, random);
        }

        StartAll(burst, origin, kind);
        return burst;
    }

    private void Sound(ExplosionBurst burst, string script, Vector at)
    {
        burst.Sounds.Add(script);
        environment.Sound(script, at);
    }

    private SimpleSheet Sheet(List<SimpleSheet> list, string material, SimpleEmitterRules rules, IParticleRandom random,
                              (Vector, Vector)? box)
    {
        foreach (var s in list)
        {
            if (s.Material == material)
                return s;
        }
        var sheet = new SimpleSheet(material, random, views, visibility, () => 0, rules: rules) { Box = box };
        list.Add(sheet);
        return sheet;
    }

    private void StartAll(ExplosionBurst burst, Vector origin, ExplosionKind kind)
    {
        Vector sortWater = burst.WaterSurface;
        foreach (var s in burst.Core.Concat(burst.Bubbles))
        {
            if (s.Pending.Count > 0)
                s.EffectId = effects.Start(s, kind == ExplosionKind.Water && burst.Core.Contains(s) ? sortWater : origin);
        }
        if (burst.Sparks is { } sparks && sparks.Pending.Count > 0)
            sparks.EffectId = effects.Start(sparks, origin);
        if (burst.Drops is { } drops && drops.Pending.Count > 0)
            drops.EffectId = effects.Start(drops, sortWater);
        if (burst.Splish is { } splish && splish.Pending.Count > 0)
            splish.EffectId = effects.Start(splish, sortWater);
    }

    private static void ClampForce(ref float force)
    {
        if (force < ForceMin)
            force = ForceMin;
        if (force > ForceMax)
            force = ForceMax;
    }

    /// <summary><c>C_BaseExplosionEffect::CreateCore</c>.</summary>
    private void Core(ExplosionBurst burst, Vector origin, int flags, IParticleRandom random)
    {
        if ((flags & NoFireball) != 0)
            return;

        float force = burst.Force;
        ClampForce(ref force);
        float spread = 1.0f - (0.15f * force);
        Vector dir = burst.Direction;

        var rules = new ExplosionRules { NearClipMin = 64, NearClipMax = 128 };
        (Vector, Vector) box = (origin - new Vector(128, 128, 128), origin + new Vector(128, 128, 128));

        Vector offset = origin + dir * 32.0f;
        Vector worldLight = environment.LightForPoint(offset);
        float luminosity;
        if (IsZero(worldLight))
            luminosity = 0.0f;
        else
            (_, luminosity) = WaterMath.TintAndLuminosity(worldLight);
        luminosity *= 255;

        const string smoke = "particle/particle_noisesphere";
        if ((flags & NoFireballSmoke) == 0)
        {
            // Smoke - basic internal filler.
            for (int i = 0; i < 4; i++)
            {
                var p = new SimpleParticle { Position = origin, Lifetime = 0.0f };
                p.DieTime = random.RandomFloat(2.0f, 3.0f);
                p.Velocity = RandomVector(random, -spread, spread) + dir * random.RandomFloat(1.0f, 6.0f);
                FxMath.Normalize(ref p.Velocity);
                float fForce = random.RandomFloat(1, 750) * force;
                ScaleForceByDeviation(p.Velocity, dir, spread, ref fForce);
                p.Velocity = p.Velocity * fForce;
                SmokeColor(ref p, worldLight, random.RandomInt((int)(luminosity * 0.5f), (int)luminosity));
                p.StartSize = 72;
                p.EndSize = unchecked((byte)(p.StartSize * 2));
                p.StartAlpha = 255;
                p.EndAlpha = 0;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = random.RandomFloat(-2.0f, 2.0f);
                Sheet(burst.Core, smoke, rules, random, box).Add(p);
            }

            // Inner core.
            for (int i = 0; i < 8; i++)
            {
                offset = RandomVector(random, -16.0f, 16.0f) + origin;
                var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
                p.DieTime = random.RandomFloat(0.5f, 1.0f);
                p.Velocity = RandomVector(random, -spread, spread) + dir * random.RandomFloat(1.0f, 6.0f);
                FxMath.Normalize(ref p.Velocity);
                float fForce = random.RandomFloat(1, 2000) * force;
                ScaleForceByDeviation(p.Velocity, dir, spread, ref fForce);
                p.Velocity = p.Velocity * fForce;
                SmokeColor(ref p, worldLight, random.RandomInt((int)(luminosity * 0.5f), (int)luminosity));
                p.StartSize = (byte)random.RandomInt(32, 64);
                p.EndSize = unchecked((byte)(p.StartSize * 2));
                p.StartAlpha = UChar(random.RandomFloat(128, 255));
                p.EndAlpha = 0;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
                Sheet(burst.Core, smoke, rules, random, box).Add(p);
            }

            // Ground ring.
            const int numRingSprites = 32;
            float flIncr = (float)((2 * Math.PI) / (float)numRingSprites);
            float flYaw = 0.0f;
            for (int i = 0; i < numRingSprites; i++)
            {
                flYaw += flIncr;
                var forward = new Vector(MathF.Cos(flYaw), MathF.Sin(flYaw), 0.0f);
                Vector rv = RandomVector(random, -4.0f, 4.0f);
                offset = (rv + origin) + forward * random.RandomFloat(8.0f, 16.0f);
                var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
                p.DieTime = random.RandomFloat(0.5f, 1.5f);
                p.Velocity = forward;
                float fForce = random.RandomFloat(500, 2000) * force;
                ScaleForceByDeviation(p.Velocity, p.Velocity, spread, ref fForce);
                p.Velocity = p.Velocity * fForce;
                SmokeColor(ref p, worldLight, random.RandomInt((int)(luminosity * 0.5f), (int)luminosity));
                p.StartSize = (byte)random.RandomInt(16, 32);
                p.EndSize = unchecked((byte)(p.StartSize * 4));
                p.StartAlpha = UChar(random.RandomFloat(16, 32));
                p.EndAlpha = 0;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
                Sheet(burst.Core, smoke, rules, random, box).Add(p);
            }
        }

        // Embers.
        for (int i = 0; i < 16; i++)
        {
            offset = RandomVector(random, -32.0f, 32.0f) + origin;
            string material = random.RandomInt(0, 1) == 0 ? "effects/fire_embers1" : "effects/fire_embers2";
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(2.0f, 3.0f);
            p.Velocity = RandomVector(random, -spread * 2, spread * 2) + dir;
            FxMath.Normalize(ref p.Velocity);
            float fForce = random.RandomFloat(1.0f, 400.0f);
            float vDev = ScaleForceByDeviation(p.Velocity, dir, spread);
            p.Velocity = p.Velocity * (fForce * (16.0f * (vDev * vDev * 0.5f)));
            byte c = (byte)random.RandomInt(192, 255);
            p.R = p.G = p.B = c;
            byte size = UChar(random.RandomInt(8, 16) * vDev);
            p.StartSize = Math.Clamp(size, (byte)4, (byte)32);
            p.EndSize = p.StartSize;
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
            Sheet(burst.Core, material, rules, random, box).Add(p);
        }

        Fireballs(burst, origin, dir, spread, rules, random, box);
    }

    /// <summary>The fireballs, the base core's last part and the whole of the mega bomb's.</summary>
    private void Fireballs(ExplosionBurst burst, Vector origin, Vector dir, float spread, SimpleEmitterRules rules,
                           IParticleRandom random, (Vector, Vector)? box)
    {
        for (int i = 0; i < 32; i++)
        {
            Vector offset = RandomVector(random, -48.0f, 48.0f) + origin;
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.2f, 0.4f);
            p.Velocity = RandomVector(random, -spread * 0.75f, spread * 0.75f) + dir;
            FxMath.Normalize(ref p.Velocity);
            float fForce = random.RandomFloat(400.0f, 800.0f);
            float vDev = ScaleForceByDeviation(p.Velocity, dir, spread);
            p.Velocity = p.Velocity * (fForce * (16.0f * (vDev * vDev * 0.5f)));
            byte c = (byte)random.RandomInt(128, 255);
            p.R = p.G = p.B = c;
            byte size = UChar(random.RandomInt(32, 85) * vDev);
            p.StartSize = Math.Clamp(size, (byte)32, (byte)85);
            p.EndSize = unchecked((byte)(int)((float)p.StartSize * 1.5f));
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-16.0f, 16.0f);
            Sheet(burst.Core, "effects/fire_cloud2", rules, random, box).Add(p);
        }
    }

    private static void SmokeColor(ref SimpleParticle p, Vector worldLight, int nColor)
    {
        p.R = UChar(worldLight.X * nColor);
        p.G = UChar(worldLight.Y * nColor);
        p.B = UChar(worldLight.Z * nColor);
    }

    /// <summary><c>C_MegaBombExplosionEffect::CreateCore</c>: fireballs only, with a nearer clip.</summary>
    private void MegaBombCore(ExplosionBurst burst, Vector origin, int flags, IParticleRandom random)
    {
        if ((flags & NoFireball) != 0)
            return;
        float force = burst.Force;
        ClampForce(ref force);
        float spread = 1.0f - (0.15f * force);
        var rules = new ExplosionRules { NearClipMin = 32, NearClipMax = 64 };
        Fireballs(burst, origin, burst.Direction, spread, rules, random, null);
    }

    /// <summary><c>C_BaseExplosionEffect::CreateDebris</c>: sparks, then cement flecks on the fleck merge list.</summary>
    private void Debris(ExplosionBurst burst, Vector origin, int flags, IParticleRandom random)
    {
        if ((flags & NoParticles) != 0)
            return;
        Vector direction = burst.Direction;

        var sparks = new TrailEmitter("effects/fire_cloud2", views, world, random);
        sparks.Collision.Gravity = 200.0f;
        sparks.Flags |= TrailFlags.VelocityDampen;
        sparks.VelocityDampen = 8.0f;
        sparks.Box = (origin - new Vector(128, 128, 128), origin + new Vector(128, 128, 128));
        burst.Sparks = sparks;

        int numSparks = random.RandomInt(8, 16);
        for (int i = 0; i < numSparks; i++)
        {
            var p = new TrailParticle { Position = origin, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.1f, 0.15f);
            Vector dir = RandomVector(random, -1.0f, 1.0f) + direction;
            FxMath.Normalize(ref dir);
            p.Width = random.RandomFloat(2.0f, 16.0f);
            p.Length = random.RandomFloat(0.05f, 0.1f);
            p.Velocity = dir * random.RandomFloat(1500, 2500);
            p.R = p.G = p.B = p.A = 255;
            sparks.Add(p);
        }

        // Chunks.
        FleckSystem system = flecks.Create("CreateDebris 2", origin, new Vector(128, 128, 128),
                                           "effects/fleck_cement1", "effects/fleck_cement2");
        burst.Flecks = system;
        system.Collision.Setup(origin, direction, 0.9f, 512, 1024, 800, 0.5f, world);

        int numFlecks = random.RandomInt(16, 32);
        for (int i = 0; i < numFlecks; i++)
        {
            Vector offset = origin + direction * 16.0f;
            offset.X += random.RandomFloat(-8.0f, 8.0f);
            offset.Y += random.RandomFloat(-8.0f, 8.0f);
            offset.Z += random.RandomFloat(-8.0f, 8.0f);
            var p = new FleckParticle { Sheet = random.RandomInt(0, 1), Position = offset, Lifetime = 0.0f, DieTime = 3.0f };
            float dx = direction.X + random.RandomFloat(-1.0f, 1.0f);
            float dy = direction.Y + random.RandomFloat(-1.0f, 1.0f);
            float dz = direction.Z + random.RandomFloat(-1.0f, 1.0f);
            p.Size = (byte)random.RandomInt(1, 3);
            Vector dir = new(dx, dy, dz);
            FxMath.Normalize(ref dir);
            float fForce = random.RandomFloat(64, 256) * (4 - p.Size);
            float fDev = ScaleForceByDeviation(dir, direction, 0.8f);
            p.Velocity = dir * (fForce * (16.0f * (fDev * fDev * 0.5f)));
            p.Roll = random.RandomFloat(0, 360);
            p.RollDelta = random.RandomFloat(0, 360);
            float colorRamp = random.RandomFloat(0.5f, 1.5f);
            p.R = p.G = p.B = UChar(ParticleMath.MinF(1.0f, 0.25f * colorRamp) * 255.0f);
            burst.FlecksMade.Add(p);
            flecks.Add(system, p);
        }
    }

    /// <summary><c>C_WaterExplosionEffect::CreateCore</c>: a wake on the surface and two water shafts.</summary>
    private void WaterCore(ExplosionBurst burst, int flags, IParticleRandom random)
    {
        if ((flags & NoFireball) != 0)
            return;
        Vector surface = burst.WaterSurface;
        var (color, luminosity) = WaterMath.SplashLighting(environment.LightForPoint(surface + new Vector(0, 0, 8)));
        float lifetime = random.RandomFloat(0.8f, 1.0f);

        // Ground splash.
        float yaw = random.RandomInt(0, 360);
        float deltaYaw = random.RandomFloat(-4, 4);
        quads.AddQuad(surface + new Vector(0, 0, 2), new Vector(0, 0, 1), 64, 64 * 4.0f, 0.85f, luminosity, 0.0f, 0.25f,
                      yaw, deltaYaw, color, 2.0f, "effects/splashwake1", FxQuadFlags.BiasScale | FxQuadFlags.BiasAlpha);

        const float radius = 50.0f;
        var up = new Vector(0, 0, 1);

        // Base vertical shaft.
        Vector end = surface + up * random.RandomFloat(radius, radius * 1.5f);
        random.RandomInt(0, 1);   // stock's FXSTATICLINE_FLIP_HORIZONTAL choice, which no line reads
        var line = new FxLineData
        {
            DieTime = lifetime * 0.5f, StartAlpha = luminosity, EndAlpha = 0.0f, StartScale = radius * 0.5f, EndScale = radius * 2,
            Start = surface, StartVelocity = default, End = end,
        };
        line.EndVelocity = new Vector(0, 0, random.RandomFloat(650, 750));
        lines.AddLine(line, "effects/splash3");

        // Inner filler shaft.
        end = surface + up * random.RandomFloat(32, 64);
        random.RandomInt(0, 1);
        line.DieTime = lifetime * 0.5f;
        line.StartAlpha = luminosity;
        line.EndAlpha = 0.0f;
        line.StartScale = radius;
        line.EndScale = radius * 2;
        line.Start = surface;
        line.StartVelocity = default;
        line.End = end;
        line.EndVelocity = up * random.RandomFloat(64, 128);
        lines.AddLine(line, "effects/splash3");
    }

    /// <summary><c>C_WaterExplosionEffect::CreateDebris</c>: bubbles, in water deeper than 128.</summary>
    private void WaterDebris(ExplosionBurst burst, Vector origin, int flags, Vector color, float luminosity, IParticleRandom random)
    {
        if ((flags & NoParticles) != 0)
            return;
        if (burst.Depth <= 128)
            return;

        float force = burst.Force;
        ClampForce(ref force);
        float spread = 1.0f - (0.15f * force);
        Vector dir = burst.Direction;
        var rules = new WaterExplosionRules { NearClipMin = 64, NearClipMax = 128 };

        // The light at the offset is read and unused, as stock's was.
        Vector offset = origin + dir * 64.0f;
        environment.LightForPoint(offset);

        for (int i = 0; i < 16; i++)
        {
            offset = RandomVector(random, -32.0f, 32.0f) + origin;
            string material = random.RandomInt(0, 1) == 0 ? "effects/splash1" : "effects/splash2";
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(2.0f, 3.0f);
            p.Velocity = RandomVector(random, -spread, spread) + dir * random.RandomFloat(1.0f, 6.0f);
            FxMath.Normalize(ref p.Velocity);
            float fForce = 1500 * force;
            ScaleForceByDeviation(p.Velocity, dir, spread, ref fForce);
            p.Velocity = p.Velocity * fForce;
            p.R = UChar(color.X * 255);
            p.G = UChar(color.Y * 255);
            p.B = UChar(color.Z * 255);
            p.StartSize = (byte)random.RandomInt(32, 64);
            p.EndSize = unchecked((byte)(p.StartSize * 2));
            p.StartAlpha = UChar(luminosity);
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
            Sheet(burst.Bubbles, material, rules, random, null).Add(p);
        }
    }

    /// <summary><c>C_WaterExplosionEffect::CreateMisc</c>: drops off the surface and a splash clipped at it.</summary>
    private void WaterMisc(ExplosionBurst burst, Vector color, float luminosity, IParticleRandom random)
    {
        Vector surface = burst.WaterSurface;
        const float flScale = 2.0f, length = 0.1f;
        const string material = "effects/splash2";

        var drops = new TrailEmitter(material, views, world, random);
        drops.Collision.Gravity = 800.0f;
        drops.Flags |= TrailFlags.VelocityDampen;
        drops.VelocityDampen = 2.0f;
        burst.Drops = drops;

        void Drop(float spray, float speedScale, float widthMin)
        {
            float ox = surface.X + random.RandomFloat(-16.0f, 16.0f) * flScale;
            float oy = surface.Y + random.RandomFloat(-16.0f, 16.0f) * flScale;
            var p = new TrailParticle { Position = new Vector(ox, oy, surface.Z), Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.5f, 1.0f);
            Vector offDir = new Vector(0, 0, 1) + RandomVector(random, -spray, spray);
            p.Velocity = offDir * random.RandomFloat(50.0f * flScale * speedScale, 100.0f * flScale * speedScale);
            p.Velocity.Z += random.RandomFloat(32.0f, 128.0f) * flScale;
            p.Width = FxMath.Clamp(random.RandomFloat(widthMin, 3.0f) * flScale, 0.1f, 4.0f);
            p.Length = random.RandomFloat(length * 0.25f, length);
            float colorRamp = random.RandomFloat(1.5f, 2.0f);
            p.R = UChar(ParticleMath.MinF(1.0f, color.X * colorRamp) * 255);
            p.G = UChar(ParticleMath.MinF(1.0f, color.Y * colorRamp) * 255);
            p.B = UChar(ParticleMath.MinF(1.0f, color.Z * colorRamp) * 255);
            p.A = UChar(luminosity * 255);
            drops.Add(p);
        }

        for (int i = 0; i < 32; i++)
            Drop(1.0f, 2.0f, 1.0f);
        for (int i = 0; i < 4; i++)
            Drop(0.2f, 3.0f, 2.0f);

        var splish = new SplashSheet(material, random).Init();
        splish.Rules.UseClipHeight = true;
        splish.Rules.ClipHeight = surface.Z;
        splish.Box = (surface - new Vector(32.0f, 32.0f, 32.0f) * flScale, surface + new Vector(32.0f, 32.0f, 32.0f) * flScale);
        burst.Splish = splish;

        for (int i = 0; i < 16; i++)
        {
            var p = new SimpleParticle { Position = surface, Lifetime = 0.0f, DieTime = 2.0f };
            Vector v = RandomVector(random, -0.2f, 0.2f) + new Vector(0, 0, random.RandomFloat(4.0f, 6.0f));
            FxMath.Normalize(ref v);
            p.Velocity = v * (50 * flScale * (8 - i));
            float colorRamp = random.RandomFloat(0.75f, 1.25f);
            p.R = UChar(ParticleMath.MinF(1.0f, color.X * colorRamp) * 255.0f);
            p.G = UChar(ParticleMath.MinF(1.0f, color.Y * colorRamp) * 255.0f);
            p.B = UChar(ParticleMath.MinF(1.0f, color.Z * colorRamp) * 255.0f);
            p.StartSize = UChar(24 * flScale * WaterMath.RemapValClamped(i, 7, 0, 1, 0.5f));
            p.EndSize = (byte)Math.Min(255, p.StartSize * 2);
            p.StartAlpha = UChar(WaterMath.RemapValClamped(i, 7, 0, 255, 32) * luminosity);
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-4.0f, 4.0f);
            splish.Add(p);
        }
    }
}

/// <summary>The <c>Explosion</c>, <c>WaterSurfaceExplosion</c> and <c>HelicopterMegaBomb</c> client effects.</summary>
public sealed class ExplosionEffect(ExplosionEffects explosions, string name, ExplosionKind kind) : IClientEffectHandler
{
    public string EffectName { get; } = name;

    public void Dispatch(in EffectData data)
    {
        if (kind == ExplosionKind.MegaBomb)
            explosions.Create(kind, data.Origin, 1.0f, 1.0f, 0);
        else
            explosions.Create(kind, data.Origin, data.Magnitude, data.Scale, data.Flags);
    }
}
