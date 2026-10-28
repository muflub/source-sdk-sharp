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
/// <c>CBloodSprayEmitter::UpdateVelocity</c>'s two statics, shared by every
/// blood spray as stock's were: the decay is recomputed when the step's dt
/// changes, from the dt BEFORE the change (stock computes, then stores the new
/// dt), so it lags one change behind. Zero at the start, as statics are.
/// </summary>
public sealed class BloodDecay
{
    public float DTime;
    public float Decay;

    /// <summary><c>ExponentialDecay( decayTo, decayTime, dt )</c>: <c>expf( logf( decayTo ) / decayTime * dt )</c>.</summary>
    public static float ExponentialDecay(float decayTo, float decayTime, float dt)
        => MathF.Exp(MathF.Log(decayTo) / decayTime * dt);

    public float For(float dt)
    {
        if (DTime != dt)
        {
            Decay = ExponentialDecay(0.1f, 0.4f, DTime);
            DTime = dt;
        }
        return Decay;
    }
}

/// <summary>
/// <c>CBloodSprayEmitter</c>'s rules (fx_blood.h): a roll that slows by e^-4
/// a second with no floor, and, unless the particle's
/// <c>SIMPLE_PARTICLE_FLAG_NO_VEL_DECAY</c> bit is set, a velocity decayed by
/// the shared <see cref="BloodDecay"/> under the emitter's gravity.
/// </summary>
public sealed class BloodSprayRules(BloodDecay decay) : SimpleEmitterRules
{
    /// <summary><c>SIMPLE_PARTICLE_FLAG_NO_VEL_DECAY</c>.</summary>
    public const byte FlagNoVelocityDecay = 0x2;

    public float Gravity;

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -4.0f);
        return p.Roll;
    }

    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        if ((p.Flags & FlagNoVelocityDecay) != 0)
            return;
        float d = decay.For(dt);
        p.Velocity = new Vector(p.Velocity.X * d, p.Velocity.Y * d, p.Velocity.Z * d);
        p.Velocity.Z -= Gravity * dt;
    }
}

/// <summary>What the blood effects read of the client: low violence, the light, <c>gpGlobals-&gt;maxClients</c>.</summary>
public interface IBloodEnvironment
{
    bool LowViolence { get; }

    /// <summary><c>WorldGetLightForPoint( position, true )</c>.</summary>
    Vector LightForPoint(Vector position);

    int MaxClients { get; }
}

/// <summary>The real one.</summary>
public sealed class BloodEnvironment(IClientLowViolence lowViolence, IClientWorldLight light, IEngineEnvironment environment)
    : IBloodEnvironment
{
    public bool LowViolence => lowViolence.IsLowViolence;

    public Vector LightForPoint(Vector position) => light.LightForPoint(position, true);

    public int MaxClients => environment.Globals.MaxClients;
}

/// <summary>The effects one blood routine started, in stock's creation order.</summary>
public sealed class BloodBurst
{
    public TrailEmitter? Drops { get; set; }
    public List<SimpleSheet> Sheets { get; } = new();
    public Simple3DSystem? Flecks { get; set; }
}

/// <summary>
/// fx_blood.cpp's routines (lane c5-particles): the blood spray (drops, a
/// gore blossom, a cloud), the bullet impact on a body whose blood has no
/// particle system (a core, gore and colliding drops), the blood impact's
/// choice between the two, and the hunter's damage flecks. Sampled from a
/// managed stream, as the sparks are; replayed against
/// <c>Fixtures/bloodspray-stock.tsv.gz</c>.
/// </summary>
public sealed class BloodEffects(ClientParticleEffects effects, IParticleWorldTrace world, ParticleViews views,
                                 IParticlePixelVisibility visibility, IBloodEnvironment environment,
                                 IClientParticleOneShots oneShots, IRandomSeeds seeds)
{
    /// <summary><c>FX_BLOODSPRAY_DROPS</c>, <c>_GORE</c>, <c>_CLOUD</c>.</summary>
    public const int SprayDrops = 0x01, SprayGore = 0x02, SprayCloud = 0x04;

    /// <summary><c>BLOOD_COLOR_RED</c>, <c>_YELLOW</c>, <c>_GREEN</c>, <c>_MECH</c>.</summary>
    public const int Red = 0, Yellow = 1, Green = 2, Mech = 3;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    /// <summary>The decay every blood spray emitter shares (stock's statics).</summary>
    public BloodDecay Decay { get; } = new();

    public int Sprays { get; private set; }

    public int Impacts { get; private set; }

    /// <summary><c>GetBloodColor</c> over effect_color_tables.h's <c>bloodcolors</c>; magenta for none.</summary>
    public static (byte R, byte G, byte B) BloodColor(int bloodType) => bloodType switch
    {
        Red => (72, 0, 0),
        Yellow => (195, 195, 0),
        Mech => (20, 20, 20),
        Green => (195, 195, 0),
        _ => (255, 0, 255),
    };

    /// <summary><c>bloodCallbacks</c>: the particle system a blood colour has (HL2_EPISODIC's three are not compiled here).</summary>
    public static string? ImpactSystem(int bloodType) => bloodType switch
    {
        Red => "blood_impact_red_01",
        Green => "blood_impact_green_01",
        Yellow => "blood_impact_yellow_01",
        _ => null,
    };

    private static Vector Tint(Vector light, byte r, byte g, byte b)
        => new((float)(light.X * r) / 255.0f, (float)(light.Y * g) / 255.0f, (float)(light.Z * b) / 255.0f);

    private static Vector Cross(Vector a, Vector b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static Vector RandomVector(IParticleRandom random, float min, float max)
    {
        float x = random.RandomFloat(min, max);
        float y = random.RandomFloat(min, max);
        float z = random.RandomFloat(min, max);
        return new Vector(x, y, z);
    }

    /// <summary><c>(unsigned char)</c> of a float, as x86 truncates it (the low byte of the integer).</summary>
    private static byte UChar(float f) => unchecked((byte)(int)f);

    private static byte Ramped(float channel, float ramp) => UChar(ParticleMath.MinF(1.0f, channel * ramp) * 255.0f);

    private static void SetColor(ref TrailParticle p, Vector color)
    {
        // FloatToColor32( color, r, g, b, 1 ).
        p.R = UChar(color.X * 255);
        p.G = UChar(color.Y * 255);
        p.B = UChar(color.Z * 255);
        p.A = UChar(1.0f * 255);
    }

    private SimpleSheet Sheet(BloodBurst burst, string material, BloodSprayRules rules, IParticleRandom random)
    {
        var sheet = new SimpleSheet(material, random, views, visibility, () => 0, rules: rules);
        burst.Sheets.Add(sheet);
        return sheet;
    }

    private void Start(BloodBurst burst, Vector origin)
    {
        if (burst.Drops is { EffectId: 0 } drops && drops.Pending.Count > 0)
            drops.EffectId = effects.Start(drops, origin);
        foreach (var sheet in burst.Sheets)
        {
            if (sheet.EffectId == 0 && sheet.Pending.Count > 0)
                sheet.EffectId = effects.Start(sheet, origin);
        }
    }

    public void BloodSpray(Vector origin, Vector normal, float scale, byte r, byte g, byte b, int flags)
        => BloodSpray(origin, normal, scale, r, g, b, flags, _random);

    /// <summary><c>FX_BloodSpray</c>.</summary>
    public BloodBurst BloodSpray(Vector origin, Vector normal, float scale, byte r, byte g, byte b, int flags,
                                 IParticleRandom random)
    {
        var burst = new BloodBurst();
        if (environment.LowViolence)
            return burst;
        Sprays++;

        Vector color = Tint(environment.LightForPoint(origin), r, g, b);

        Vector right, up;
        if (normal.X != 0 || normal.Y != 0 || normal.Z != 1)
        {
            right = Cross(normal, new Vector(0, 0, 1));
            up = Cross(right, normal);
        }
        else
        {
            right = new Vector(0, 0, 1);
            up = Cross(right, normal);
        }

        if ((flags & SprayDrops) != 0)
        {
            var drops = new TrailEmitter("effects/blood_drop", views, world, random);
            drops.Collision.Gravity = 600.0f;
            drops.Box = (origin - new Vector(32, 32, 32), origin + new Vector(32, 32, 32));
            drops.Flags |= TrailFlags.VelocityDampen;
            drops.VelocityDampen = 0.2f;
            burst.Drops = drops;

            // Long stringy drops of blood.
            for (int i = 0; i < 14; i++)
            {
                Vector offset = origin;
                offset = offset + right * random.RandomFloat(-0.5f, 0.5f) * scale;
                offset = offset + up * random.RandomFloat(-0.5f, 0.5f) * scale;
                var p = new TrailParticle { Position = offset, Lifetime = 0.0f };
                Vector offDir = normal + RandomVector(random, -0.3f, 0.3f);
                p.Velocity = offDir * random.RandomFloat(4.0f * scale, 40.0f * scale);
                p.Velocity.Z += random.RandomFloat(4.0f, 16.0f) * scale;
                p.Width = random.RandomFloat(0.125f, 0.275f) * scale;
                p.Length = random.RandomFloat(0.02f, 0.03f) * scale;
                p.DieTime = random.RandomFloat(0.5f, 1.0f);
                SetColor(ref p, color);
                drops.Add(p);
            }

            // Shorter droplets.
            for (int i = 0; i < 24; i++)
            {
                Vector offset = origin;
                offset = offset + right * random.RandomFloat(-0.5f, 0.5f) * scale;
                offset = offset + up * random.RandomFloat(-0.5f, 0.5f) * scale;
                var p = new TrailParticle { Position = offset, Lifetime = 0.0f };
                Vector offDir = normal + RandomVector(random, -1.0f, 1.0f);
                offDir.Z += random.RandomFloat(0, 1.0f);
                p.Velocity = offDir * random.RandomFloat(2.0f * scale, 25.0f * scale);
                p.Velocity.Z += random.RandomFloat(4.0f, 16.0f) * scale;
                p.Width = random.RandomFloat(0.25f, 0.375f) * scale;
                p.Length = random.RandomFloat(0.0025f, 0.005f) * scale;
                p.DieTime = random.RandomFloat(0.5f, 1.0f);
                SetColor(ref p, color);
                drops.Add(p);
            }
        }

        if ((flags & (SprayGore | SprayCloud)) != 0)
        {
            var rules = new BloodSprayRules(Decay) { Gravity = 0 };

            // Tight blossom of blood at the centre.
            if ((flags & SprayGore) != 0)
            {
                SimpleSheet gore = Sheet(burst, "effects/blood_gore", rules, random);
                for (int i = 0; i < 6; i++)
                {
                    Vector offset = origin + normal * (float)(0.5 * scale);
                    offset = offset + right * random.RandomFloat(-0.5f, 0.5f) * scale;
                    offset = offset + up * random.RandomFloat(-0.5f, 0.5f) * scale;
                    var p = new SimpleParticle { Position = offset, Lifetime = 0.0f, DieTime = 0.3f };
                    p.Velocity = RandomVector(random, -0.2f, 0.2f);
                    p.Velocity = p.Velocity + normal * random.RandomInt(10, 100);
                    float colorRamp = random.RandomFloat(0.75f, 1.25f);
                    p.R = Ramped(color.X, colorRamp);
                    p.G = Ramped(color.Y, colorRamp);
                    p.B = Ramped(color.Z, colorRamp);
                    p.StartSize = UChar(random.RandomFloat((float)(scale * 0.25), scale));
                    p.EndSize = unchecked((byte)(p.StartSize * 2));
                    p.StartAlpha = (byte)random.RandomInt(200, 255);
                    p.EndAlpha = 0;
                    p.Roll = random.RandomInt(0, 360);
                    p.RollDelta = 0.0f;
                    gore.Add(p);
                }
            }

            // Diffuse cloud just in front of the exit wound.
            if ((flags & SprayCloud) != 0)
            {
                SimpleSheet cloud = Sheet(burst, "effects/blood_puff", rules, random);
                for (int i = 0; i < 6; i++)
                {
                    Vector offset = origin + normal * scale;
                    offset = offset + right * random.RandomFloat(-1, 1) * scale;
                    offset = offset + up * random.RandomFloat(-1, 1) * scale;
                    var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
                    p.DieTime = random.RandomFloat(0.5f, 0.8f);
                    p.Velocity = RandomVector(random, -0.5f, 0.5f);
                    p.Velocity = p.Velocity + normal * random.RandomInt(100, 200);
                    float colorRamp = random.RandomFloat(0.75f, 1.25f);
                    p.R = Ramped(color.X, colorRamp);
                    p.G = Ramped(color.Y, colorRamp);
                    p.B = Ramped(color.Z, colorRamp);
                    p.StartSize = UChar(random.RandomFloat(scale * 1.5f, scale * 2.0f));
                    p.EndSize = unchecked((byte)(p.StartSize * 4));
                    p.StartAlpha = (byte)random.RandomInt(80, 128);
                    p.EndAlpha = 0;
                    p.Roll = random.RandomInt(0, 360);
                    p.RollDelta = 0.0f;
                    cloud.Add(p);
                }
            }
        }

        Start(burst, origin);
        return burst;
    }

    public void BloodBulletImpact(Vector origin, Vector normal, float scale, byte r, byte g, byte b)
        => BloodBulletImpact(origin, normal, scale, r, g, b, _random);

    /// <summary><c>FX_BloodBulletImpact</c> (the scale is unused, as stock's note says).</summary>
    public BloodBurst BloodBulletImpact(Vector origin, Vector normal, float scale, byte r, byte g, byte b,
                                        IParticleRandom random)
    {
        var burst = new BloodBurst();
        if (environment.LowViolence)
            return burst;
        Impacts++;

        Vector worldLight = environment.LightForPoint(origin);
        if (environment.MaxClients > 1)
        {
            worldLight = new Vector(1.0f, 1.0f, 1.0f);
            r = 96;
            g = 0;
            b = 10;
        }
        Vector color = Tint(worldLight, r, g, b);

        var rules = new BloodSprayRules(Decay) { Gravity = 200 };
        var box = (origin - new Vector(16, 16, 16), origin + new Vector(16, 16, 16));

        Vector rv = RandomVector(random, -0.5f, 0.5f);
        Vector dir = new(normal.X * rv.X, normal.Y * rv.Y, normal.Z * rv.Z);
        Vector offset = origin + normal * 2.0f;

        SimpleSheet core = Sheet(burst, "effects/blood_core", rules, random);
        core.Box = box;
        {
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.25f, 0.5f);
            p.Velocity = dir * random.RandomFloat(16.0f, 32.0f);
            p.Velocity.Z -= random.RandomFloat(8.0f, 16.0f);
            float colorRamp = random.RandomFloat(0.75f, 2.0f);
            p.R = Ramped(color.X, colorRamp);
            p.G = Ramped(color.Y, colorRamp);
            p.B = Ramped(color.Z, colorRamp);
            p.StartSize = (byte)random.RandomInt(2, 4);
            p.EndSize = unchecked((byte)(p.StartSize * 8));
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = 0.0f;
            core.Add(p);
        }

        SimpleSheet gore = Sheet(burst, "effects/blood_gore", rules, random);
        gore.Box = box;
        for (int i = 0; i < 4; i++)
        {
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.5f, 0.75f);
            p.Velocity = dir * random.RandomFloat(16.0f, 32.0f) * (i + 1);
            p.Velocity.Z -= random.RandomFloat(32.0f, 64.0f) * (i + 1);
            float colorRamp = random.RandomFloat(0.75f, 2.0f);
            p.R = Ramped(color.X, colorRamp);
            p.G = Ramped(color.Y, colorRamp);
            p.B = Ramped(color.Z, colorRamp);
            p.StartSize = (byte)random.RandomInt(2, 4);
            p.EndSize = unchecked((byte)(p.StartSize * 4));
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = 0.0f;
            gore.Add(p);
        }

        // Shorter droplets, colliding with the surfaces nearby.
        var drops = new TrailEmitter("effects/blood_drop", views, world, random);
        drops.Collision.Gravity = 400.0f;
        drops.Setup(origin, normal, 1, 10, 100, 400, 0.2f, TrailFlags.None);
        burst.Drops = drops;
        for (int i = 0; i < 8; i++)
        {
            var p = new TrailParticle { Position = origin, Lifetime = 0.0f };
            Vector offDir = RandomVector(random, -1.0f, 1.0f);
            p.Velocity = offDir * random.RandomFloat(64.0f, 128.0f);
            p.Width = random.RandomFloat(0.5f, 2.0f);
            p.Length = random.RandomFloat(0.05f, 0.15f);
            p.DieTime = random.RandomFloat(0.25f, 0.5f);
            SetColor(ref p, color);
            drops.Add(p);
        }

        Start(burst, origin);
        return burst;
    }

    /// <summary>
    /// <c>BloodImpactCallback</c>: the colour's particle system against the
    /// normal, or, for a colour with none, the bullet impact.
    /// </summary>
    public void BloodImpact(in EffectData data)
    {
        if (ImpactSystem(data.Color) is { } name)
        {
            Vector angles = ParticleCollision.VectorAngles(new Vector(-data.Normal.X, -data.Normal.Y, -data.Normal.Z));
            MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
            Span<ParticleControlPoint> points = [new ParticleControlPoint(0, data.Origin), new ParticleControlPoint(1, data.Origin)];
            oneShots.Start(name, data.Origin, points, forward, right, up);
            return;
        }
        var (r, g, b) = BloodColor(data.Color);
        BloodBulletImpact(data.Origin, data.Normal, data.Scale, r, g, b);
    }

    public void HunterDamage(Vector origin, Vector normal) => HunterDamage(origin, normal, _random);

    /// <summary>
    /// <c>HunterDamageCallback</c>: thirty-two antlion flecks thrown off the
    /// normal, spinning, colliding, each white on its front and a quarter of
    /// that on its back.
    /// </summary>
    public Simple3DSystem HunterDamage(Vector origin, Vector normal, IParticleRandom random)
    {
        var system = new Simple3DSystem(effects, views, world, random, origin);
        const float maxSpeed = 400.0f, minSpeed = 50.0f, angularSpray = 1.0f;
        system.Collision.Setup(origin, normal, angularSpray, minSpeed, maxSpeed, 600.0f, 0.2f, world);

        for (int i = 0; i < 32; i++)
        {
            Vector spawnOffset = origin + RandomVector(random, -32.0f, 32.0f);
            string material = random.RandomInt(0, 1) == 0 ? "effects/fleck_antlion1" : "effects/fleck_antlion2";
            var p = new Particle3D { Position = spawnOffset };
            p.LifeRemaining = random.RandomFloat(2.0f, 3.0f);
            float dx = normal.X + random.RandomFloat(-angularSpray, angularSpray);
            float dy = normal.Y + random.RandomFloat(-angularSpray, angularSpray);
            float dz = normal.Z + random.RandomFloat(-angularSpray, angularSpray);
            p.Size = (byte)random.RandomInt(3, 8);
            p.Velocity = new Vector(dx, dy, dz) * random.RandomFloat(minSpeed, maxSpeed);
            p.Angles = RandomVector(random, 0, 360);
            p.AngSpeed = random.RandomFloat(-800, 800);
            const byte white = 255;
            p.FrontR = p.FrontG = p.FrontB = white;
            p.BackR = p.BackG = p.BackB = UChar(white * 0.25f);
            system.Add(material, p);
        }
        system.Start();
        return system;
    }
}

/// <summary>The <c>bloodspray</c> client effect.</summary>
public sealed class BloodSprayEffect(BloodEffects blood) : IClientEffectHandler
{
    public string EffectName => "bloodspray";

    public void Dispatch(in EffectData data)
    {
        var (r, g, b) = BloodEffects.BloodColor(data.Color);
        blood.BloodSpray(data.Origin, data.Normal, data.Scale, r, g, b, data.Flags);
    }
}

/// <summary>The <c>BloodImpact</c> client effect.</summary>
public sealed class BloodImpactEffect(BloodEffects blood) : IClientEffectHandler
{
    public string EffectName => "BloodImpact";

    public void Dispatch(in EffectData data) => blood.BloodImpact(data);
}

/// <summary>The <c>HunterDamage</c> client effect.</summary>
public sealed class HunterDamageEffect(BloodEffects blood) : IClientEffectHandler
{
    public string EffectName => "HunterDamage";

    public void Dispatch(in EffectData data) => blood.HunterDamage(data.Origin, data.Normal);
}
