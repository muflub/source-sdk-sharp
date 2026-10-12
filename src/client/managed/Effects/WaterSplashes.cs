//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>fx_water's arithmetic: the tint and luminosity of a light, and the splash's lighting.</summary>
public static class WaterMath
{
    /// <summary>
    /// <c>UTIL_GetNormalizedColorTintAndLuminosity</c>: the luminosity weighs
    /// red 0.30, green 0.59, blue 0.11; the tint is the colour over its largest
    /// channel (zero for black).
    /// </summary>
    public static (Vector Tint, float Luminosity) TintAndLuminosity(Vector color)
    {
        float luminosity = (color.X * 0.30f) + (color.Y * 0.59f) + (color.Z * 0.11f);
        if (color.X == 0 && color.Y == 0 && color.Z == 0)
            return (default, luminosity);
        float maxComponent = ParticleMath.MaxF(color.X, ParticleMath.MaxF(color.Y, color.Z));
        float inv = 1.0f / maxComponent;
        return (new Vector(color.X * inv, color.Y * inv, color.Z * inv), luminosity);
    }

    /// <summary>
    /// <c>FX_GetSplashLighting</c> (fx_water.h) over the world's light at the
    /// point: a quarter of the tint over white, and the luminosity brightened
    /// four times, at most 1 and at least 0.25.
    /// </summary>
    public static (Vector Color, float Luminosity) SplashLighting(Vector light)
    {
        var (tint, luminosity) = TintAndLuminosity(light);
        luminosity = ParticleMath.MinF(1.0f, luminosity * 4.0f);
        if (luminosity < 0.25f)
            luminosity = 0.25f;
        Vector color = new(tint.X * 0.25f + 0.75f, tint.Y * 0.25f + 0.75f, tint.Z * 0.25f + 0.75f);
        return (color, luminosity);
    }

    /// <summary><c>RemapValClamped</c> (mathlib.h).</summary>
    public static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float cVal = (val - a) / (b - a);
        cVal = FxMath.Clamp(cVal, 0.0f, 1.0f);
        return c + (d - c) * cVal;
    }
}

/// <summary>
/// <c>CSplashParticle</c>'s rules (fx_water.cpp): a roll that slows by e^-4
/// a second to a floor of 0.5, a velocity that decays to a ten-thousandth in
/// three seconds under 800 units/s/s of gravity, and an alpha that fades out
/// as the particle sinks half its size below the clip height.
/// </summary>
public sealed class SplashRules : SimpleEmitterRules
{
    public bool UseClipHeight;
    public float ClipHeight;

    /// <summary>
    /// <c>UpdateVelocity</c>'s decay: <c>exp( log( 0.0001f ) * dt / 3 )</c>,
    /// in float. Stock cached it in two statics keyed by the last dt; the
    /// value is the function of dt either way.
    /// </summary>
    public static float Decay(float dt) => MathF.Exp(MathF.Log(0.0001f) * dt / 3.0f);

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -4.0f);
        if (MathF.Abs(p.RollDelta) < 0.5f)
            p.RollDelta = p.RollDelta > 0.0f ? 0.5f : -0.5f;
        return p.Roll;
    }

    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        float decay = Decay(dt);
        p.Velocity = new Vector(p.Velocity.X * decay, p.Velocity.Y * decay, p.Velocity.Z * decay);
        p.Velocity.Z -= 800.0f * dt;
    }

    public override float UpdateAlpha(in SimpleParticle p)
    {
        if (!UseClipHeight)
            return base.UpdateAlpha(p);
        float alpha = p.StartAlpha / 255.0f;
        return alpha * WaterMath.RemapValClamped(p.Position.Z, ClipHeight, ClipHeight - (UpdateScale(p) * 0.5f), 1.0f, 0.0f);
    }

    /// <summary>
    /// <c>CSplashParticle::SimulateParticles</c> for one particle: false when
    /// it sinks below the clip height (by its size) or dies.
    /// </summary>
    public bool Step(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        UpdateVelocity(ref p, dt, random);
        p.Position = p.Position + p.Velocity * dt;
        if (UseClipHeight && p.Position.Z + UpdateScale(p) < ClipHeight)
            return false;
        p.Lifetime += dt;
        UpdateRoll(ref p, dt);
        return p.Lifetime < p.DieTime;
    }
}

/// <summary>One <c>CSplashParticle</c> emitter's material: the executor's effect for it.</summary>
public sealed class SplashSheet(string material, IParticleRandom random) : IClientParticleEffect
{
    private readonly List<SimpleParticle> _pending = new();
    private int _nextId = 1;

    public SplashRules Rules { get; } = new();

    public SimpleEmitter Emitter { get; private set; } = null!;

    public string Material { get; } = material;

    public string ReloadKind => "splash";

    public (Vector Mins, Vector Maxs)? Box { get; set; }

    public int EffectId { get; set; }

    public IReadOnlyList<SimpleParticle> Pending => _pending;

    public bool Finished => EffectId != 0 && Emitter.Particles.Count == 0 && _pending.Count == 0;

    public SplashSheet Init()
    {
        Emitter = new SimpleEmitter(Rules);
        return this;
    }

    public void Add(SimpleParticle p)
    {
        p.Id = _nextId++;
        _pending.Add(p);
    }

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            Emitter.Add(_pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        TrailEmitter.SetBounds(ref emit, Box, Emitter.Particles.Select(p => p.Position));
        return n;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        var survivors = new List<SimpleParticle>(Emitter.Particles.Count);
        foreach (SimpleParticle particle in Emitter.Particles)
        {
            SimpleParticle p = particle;
            if (Rules.Step(ref p, dt, random))
                survivors.Add(p);
        }
        Emitter.Clear();
        for (int i = survivors.Count - 1; i >= 0; i--)
            Emitter.Add(survivors[i]);
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = Emitter.IndexOf(ids[i]) >= 0 ? (byte)1 : (byte)0;
        return 1;
    }

    public int Draw(Span<ParticleQuadRaw> quadsOut)
    {
        var particles = Emitter.Particles;
        if (particles.Count > quadsOut.Length)
            return particles.Count;
        int n = 0;
        foreach (var p in particles.OrderByDescending(p => p.Id))
            quadsOut[n++] = Emitter.Draw(p).ToRaw(p.Id);
        return n;
    }

    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}

/// <summary>What the splashes read of the client: <c>cl_show_splashes</c>, the light, the water, the sound.</summary>
public interface ISplashEnvironment
{
    bool ShowSplashes { get; }

    /// <summary><c>engine-&gt;GetLightForPoint( position, true )</c>.</summary>
    Vector LightForPoint(Vector position);

    /// <summary>A line against water (<c>MASK_WATER</c>): the fraction, the end, the plane's normal.</summary>
    (float Fraction, Vector End, Vector Normal) WaterTrace(Vector start, Vector end);

    /// <summary><c>Physics.WaterSplash</c> to the local player, from the world, at the point.</summary>
    void SplashSound(Vector origin);
}

/// <summary>The real one.</summary>
public sealed class SplashEnvironment(IEngineConsole console, IClientWorldLight light, IParticleMaskTrace trace,
                                      IFxGameSounds sounds) : ISplashEnvironment
{
    // cl_show_splashes is still fx_water.cpp's ConVar until that TU leaves the build.
    public bool ShowSplashes => NativeConsole.ValueOf(console, "cl_show_splashes") is not { } v || v.Trim() != "0";

    public Vector LightForPoint(Vector position) => light.LightForPoint(position, true);

    public (float Fraction, Vector End, Vector Normal) WaterTrace(Vector start, Vector end)
    {
        MaskTrace tr = trace.Trace(start, end, IParticleMaskTrace.MaskWater);
        return (tr.Fraction, tr.End, tr.Normal);
    }

    public void SplashSound(Vector origin)
    {
        if (sounds.Parameters("Physics.WaterSplash", out GameSoundParams p))
            sounds.EmitAtPosition(p, p.Volume, p.Pitch, origin);
    }
}

/// <summary>The effects one splash started, in stock's creation order.</summary>
public sealed class SplashBurst
{
    public TrailEmitter? Drops { get; set; }
    public SplashSheet? Gout { get; set; }
}

/// <summary>
/// fx_water.cpp's routines (lane c5-particles): the gunshot splash (a spray
/// of drops, a gout clipped at the surface, a ripple and a sound), the slime
/// splash (a named particle system and the sound) and the ripple (a quad on
/// the water under the point). Sampled from a managed stream, as the sparks
/// are; replayed against <c>Fixtures/watersplash-stock.tsv.gz</c>.
/// </summary>
public sealed class WaterSplashes(ClientParticleEffects effects, IParticleWorldTrace world, ParticleViews views,
                                  ISplashEnvironment environment, IFxQuadSink quads, IClientParticleOneShots oneShots,
                                  IRandomSeeds seeds) : IClientWaterSplashes
{
    public const string SplashMaterial = "effects/splash2";

    private const float SplashMinSpeed = 50.0f, SplashMaxSpeed = 100.0f;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    /// <summary>Splashes and ripples run, for the effects route.</summary>
    public int Splashes { get; private set; }

    public int Ripples { get; private set; }

    public void GunshotSplash(Vector origin, Vector normal, float scale) => GunshotSplash(origin, normal, scale, _random);

    /// <summary><c>FX_GunshotSplash</c>.</summary>
    public SplashBurst GunshotSplash(Vector origin, Vector normal, float scale, IParticleRandom random)
    {
        var burst = new SplashBurst();
        if (!environment.ShowSplashes)
            return burst;
        Splashes++;

        var (color, luminosity) = WaterMath.SplashLighting(environment.LightForPoint(origin + normal * scale));

        float flScale = scale / 8.0f;
        if (flScale > 4.0f)
            flScale = 4.0f;

        // The drops.
        var drops = new TrailEmitter(SplashMaterial, views, world, random);
        drops.Collision.Gravity = 800.0f;
        drops.Flags |= TrailFlags.VelocityDampen;
        drops.VelocityDampen = 2.0f;
        drops.Box = (origin - new Vector(32, 32, 32), origin + new Vector(32, 32, 32));
        burst.Drops = drops;

        for (int i = 0; i < 16; i++)
        {
            float ox = origin.X + random.RandomFloat(-8.0f, 8.0f) * flScale;
            float oy = origin.Y + random.RandomFloat(-8.0f, 8.0f) * flScale;
            var p = new TrailParticle { Position = new Vector(ox, oy, origin.Z), Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.25f, 0.5f);

            float rx = random.RandomFloat(-0.8f, 0.8f);
            float ry = random.RandomFloat(-0.8f, 0.8f);
            float rz = random.RandomFloat(-0.8f, 0.8f);
            Vector offDir = normal + new Vector(rx, ry, rz);

            p.Velocity = offDir * random.RandomFloat(SplashMinSpeed * flScale * 3.0f, SplashMaxSpeed * flScale * 3.0f);
            p.Velocity.Z += random.RandomFloat(32.0f, 64.0f) * flScale;

            p.Width = random.RandomFloat(1.0f, 3.0f);
            p.Length = random.RandomFloat(0.025f, 0.05f);

            float colorRamp = random.RandomFloat(0.75f, 1.25f);
            p.R = (byte)(ParticleMath.MinF(1.0f, color.X * colorRamp) * 255);
            p.G = (byte)(ParticleMath.MinF(1.0f, color.Y * colorRamp) * 255);
            p.B = (byte)(ParticleMath.MinF(1.0f, color.Z * colorRamp) * 255);
            p.A = (byte)(luminosity * 255);
            drops.Add(p);
        }
        drops.EffectId = effects.Start(drops, origin);

        // The main gout, clipped at the surface.
        var gout = new SplashSheet(SplashMaterial, random).Init();
        gout.Rules.UseClipHeight = true;
        gout.Rules.ClipHeight = origin.Z;
        gout.Box = (origin - new Vector(32, 32, 32), origin + new Vector(32, 32, 32));
        burst.Gout = gout;

        for (int i = 0; i < 8; i++)
        {
            var p = new SimpleParticle { Position = origin, Lifetime = 0.0f, DieTime = 2.0f };
            float vx = random.RandomFloat(-0.2f, 0.2f);
            float vy = random.RandomFloat(-0.2f, 0.2f);
            float vz = random.RandomFloat(-0.2f, 0.2f);
            Vector v = new Vector(vx, vy, vz) + normal * random.RandomFloat(4.0f, 6.0f);
            FxMath.Normalize(ref v);
            p.Velocity = v * (50 * flScale * (8 - i));

            float colorRamp = random.RandomFloat(0.75f, 1.25f);
            p.R = (byte)(ParticleMath.MinF(1.0f, color.X * colorRamp) * 255.0f);
            p.G = (byte)(ParticleMath.MinF(1.0f, color.Y * colorRamp) * 255.0f);
            p.B = (byte)(ParticleMath.MinF(1.0f, color.Z * colorRamp) * 255.0f);

            p.StartSize = (byte)(24 * flScale * WaterMath.RemapValClamped(i, 7, 0, 1, 0.5f));
            p.EndSize = (byte)Math.Min(255, p.StartSize * 2);

            p.StartAlpha = (byte)(WaterMath.RemapValClamped(i, 7, 0, 255, 32) * luminosity);
            p.EndAlpha = 0;

            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-4.0f, 4.0f);
            gout.Add(p);
        }
        gout.EffectId = effects.Start(gout, origin, scale * 2.0f);

        WaterRipple(origin, flScale, color, 1.5f, luminosity, random);
        environment.SplashSound(origin);
        return burst;
    }

    public void GunshotSlimeSplash(Vector origin, Vector normal, float scale)
    {
        if (!environment.ShowSplashes)
            return;
        Splashes++;

        // stock's live arm (its #if 0 arm drew slime drops and a gout): a named
        // system by the splash's size, along the normal.
        Vector angles = ParticleCollision.VectorAngles(normal);
        string name = scale < 2.0f ? "slime_splash_01" : scale < 4.0f ? "slime_splash_02" : "slime_splash_03";
        MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
        Span<ParticleControlPoint> points = [new ParticleControlPoint(0, origin), new ParticleControlPoint(1, origin)];
        oneShots.Start(name, origin, points, forward, right, up);

        environment.SplashSound(origin);
    }

    public void WaterRipple(Vector origin, float scale, Vector? color, float lifetime, float alpha)
        => WaterRipple(origin, scale, color, lifetime, alpha, _random);

    /// <summary><c>FX_WaterRipple</c>: a spreading wake on the water under the point, if there is water within 64 units.</summary>
    public bool WaterRipple(Vector origin, float scale, Vector? color, float lifetime, float alpha, IParticleRandom random)
    {
        Vector tint = color ?? new Vector(0.8f, 0.8f, 0.75f);
        var (fraction, end, normal) = environment.WaterTrace(origin + new Vector(0, 0, 8), origin + new Vector(0, 0, -64));
        if (fraction >= 1.0f)
            return false;
        Ripples++;

        float yaw = random.RandomFloat(0, 360);
        float deltaYaw = random.RandomFloat(-16.0f, 16.0f);
        quads.AddQuad(end + normal * 0.5f, normal, 16.0f * scale, 128.0f * scale, 0.7f, alpha, 0.0f, 0.25f, yaw, deltaYaw,
                      tint, lifetime, "effects/splashwake1", FxQuadFlags.BiasScale | FxQuadFlags.BiasAlpha);
        return true;
    }

    void IClientWaterSplashes.Splash(int kind, Vector origin, Vector normal, float scale, Vector? color, float lifetime, float alpha)
    {
        switch (kind)
        {
            case 0: GunshotSplash(origin, normal, scale); break;
            case 1: GunshotSlimeSplash(origin, normal, scale); break;
            default: WaterRipple(origin, scale, color, lifetime, alpha); break;
        }
    }

    (Vector Tint, float Luminosity) IClientWaterSplashes.Tint(Vector color) => WaterMath.TintAndLuminosity(color);

    /// <summary><c>RippleCallback</c>: the ripple at the effect's point, lit as a splash is.</summary>
    public void RippleEffect(Vector origin, float effectScale)
    {
        float flScale = effectScale / 8.0f;
        var (color, luminosity) = WaterMath.SplashLighting(environment.LightForPoint(origin + new Vector(0, 0, 1) * 4.0f));
        WaterRipple(origin, flScale, color, 1.5f, luminosity);
    }
}

/// <summary>The <c>watersplash</c> and <c>gunshotsplash</c> client effects: slime or water, at the point, straight up.</summary>
public sealed class WaterSplashEffect(WaterSplashes splashes, string name) : IClientEffectHandler
{
    /// <summary><c>FX_WATER_IN_SLIME</c>.</summary>
    public const int InSlime = 0x1;

    public string EffectName { get; } = name;

    public void Dispatch(in EffectData data)
    {
        if ((data.Flags & InSlime) != 0)
            splashes.GunshotSlimeSplash(data.Origin, new Vector(0, 0, 1), data.Scale);
        else
            splashes.GunshotSplash(data.Origin, new Vector(0, 0, 1), data.Scale);
    }
}

/// <summary>The <c>waterripple</c> client effect.</summary>
public sealed class WaterRippleEffect(WaterSplashes splashes) : IClientEffectHandler
{
    public string EffectName => "waterripple";

    public void Dispatch(in EffectData data) => splashes.RippleEffect(data.Origin, data.Scale);
}
