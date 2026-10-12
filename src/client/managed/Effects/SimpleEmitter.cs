//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>SimpleParticle</c> (particles_simple.h): a particle a
/// <see cref="SimpleEmitter"/> moves, rolls, fades and grows, plus the
/// executor's id and the index of the material (the executor channel) it was
/// spawned with.
/// </summary>
public struct SimpleParticle
{
    public int Id;
    public int Material;
    public Vector Position;
    public Vector Velocity;
    public float Roll;
    public float DieTime;
    public float Lifetime;
    public byte R, G, B;
    public byte StartAlpha, EndAlpha;
    public byte StartSize, EndSize;
    /// <summary>
    /// <c>m_iFlags</c>. Stock never writes it on a spawn (the pool allocation
    /// skips <c>SimpleParticle</c>'s constructor), so a stock puff is wind-blown
    /// when the reused memory's bit 0 happens to be set. The port spawns 0: a
    /// deliberate divergence from that accident (Fixtures/README-smoketrail-stock.md);
    /// the oracle replays the recorded byte.
    /// </summary>
    public byte Flags;
    public float RollDelta;
}

/// <summary>
/// <c>CSimpleEmitter</c>'s per-particle rules (particles_simple.cpp), each
/// virtual as stock's is, so an emitter subclass (the smoke trail's
/// <c>CSmokeParticle</c>, the dust trail's <c>CDustFollower</c>, ...)
/// overrides exactly what stock's did.
/// </summary>
public class SimpleEmitterRules
{
    /// <summary><c>SIMPLE_PARTICLE_FLAG_WINDBLOWN</c>.</summary>
    public const byte FlagWindBlown = 0x1;

    /// <summary>
    /// The near-clip fade the renderer applies at the particle's view depth
    /// (<c>SetNearClip</c>). Stock's default is <c>CSimpleEmitter</c>'s
    /// constructor's, 16 and 64: an emitter that never sets one (the embers,
    /// the muzzle flashes, the sparks' sheets, the splashes) fades over those.
    /// </summary>
    public float NearClipMin = 16.0f, NearClipMax = 64.0f;

    /// <summary><c>CSimpleEmitter::UpdateAlpha</c>: start to end over the life.</summary>
    public virtual float UpdateAlpha(in SimpleParticle p)
        => (p.StartAlpha / 255.0f) + ((float)(p.EndAlpha / 255.0f) - (float)(p.StartAlpha / 255.0f)) * (p.Lifetime / p.DieTime);

    /// <summary><c>CSimpleEmitter::UpdateScale</c>: start to end size over the life.</summary>
    public virtual float UpdateScale(in SimpleParticle p)
        => (float)p.StartSize + ((float)p.EndSize - (float)p.StartSize) * (p.Lifetime / p.DieTime);

    /// <summary>
    /// The wind <c>GetWindspeedAtTime( curtime )</c> answers, for a wind-blown
    /// particle. Zero until something sets it: nothing this lane spawns is
    /// wind-blown on purpose (see <see cref="SimpleParticle.Flags"/>).
    /// </summary>
    public Vector Wind;

    /// <summary><c>WIND_ACCEL</c>.</summary>
    private const float WindAccel = 50;

    /// <summary>
    /// <c>CSimpleEmitter::UpdateVelocity</c>: only a wind-blown particle's
    /// velocity changes, x and y each moving towards the wind at 50 units/s/s
    /// and clamped at it.
    /// </summary>
    public virtual void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        if ((p.Flags & FlagWindBlown) == 0)
            return;

        p.Velocity.X = TowardsWind(p.Velocity.X, Wind.X, dt);
        p.Velocity.Y = TowardsWind(p.Velocity.Y, Wind.Y, dt);
    }

    private static float TowardsWind(float v, float wind, float dt)
    {
        if (v < wind)
        {
            v += dt * WindAccel;
            if (v > wind)
                v = wind;
        }
        else if (v > wind)
        {
            v -= dt * WindAccel;
            if (v < wind)
                v = wind;
        }

        return v;
    }

    /// <summary><c>CSimpleEmitter::UpdateRoll</c>.</summary>
    public virtual float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        return p.Roll;
    }

    /// <summary><c>CSimpleEmitter::UpdateColor</c>: the colour bytes over 255.</summary>
    public virtual Vector UpdateColor(in SimpleParticle p)
        => new(p.R / 255.0f, p.G / 255.0f, p.B / 255.0f);
}

/// <summary>
/// stock's <c>sin( M_PI * (life / die) )</c> fades, taken in double as stock's
/// <c>M_PI</c> makes them: the arc several emitters share.
/// </summary>
public static class SimpleEmitterMath
{
    /// <summary><c>((float)startAlpha / 255.0f) * sin( M_PI * (life / die) )</c>, the double product returned as float.</summary>
    public static float SineAlpha(in SimpleParticle p)
        => (float)((p.StartAlpha / 255.0f) * Math.Sin(Math.PI * (p.Lifetime / p.DieTime)));

    /// <summary><c>ExponentialDecay( halflife, dt )</c> (mathlib.h): <c>expf( -0.69314718f / halflife * dt )</c>.</summary>
    public static float ExponentialDecay(float halflife, float dt)
        => MathF.Exp(-0.69314718f / halflife * dt);
}

/// <summary>
/// <c>CSimpleEmitter</c>: a list of <see cref="SimpleParticle"/>s, simulated
/// and drawn under one <see cref="SimpleEmitterRules"/>. Newest first, as the
/// particle manager links them.
/// </summary>
public sealed class SimpleEmitter(SimpleEmitterRules rules)
{
    private readonly List<SimpleParticle> _particles = new();

    public SimpleEmitterRules Rules { get; } = rules;

    public IReadOnlyList<SimpleParticle> Particles => _particles;

    /// <summary><c>AddParticle</c>: linked at the head.</summary>
    public void Add(in SimpleParticle p) => _particles.Insert(0, p);

    /// <summary><c>RemoveAllParticles</c>.</summary>
    public void Clear() => _particles.Clear();

    /// <summary>
    /// <c>CSimpleEmitter::SimulateParticles</c> over the particles of one
    /// material (the executor's channel; every particle when null): velocity,
    /// position, age, roll; one at or past its die time is removed.
    /// </summary>
    /// <param name="bornBefore">
    /// Only particles with a lower id are stepped: an emitter stock creates
    /// fresh each frame (the rocket trail's flash and embers) skips its first
    /// frame's simulate, so what it spawned this frame is drawn unstepped.
    /// </param>
    public void Simulate(float dt, IParticleRandom random, int? material = null, int bornBefore = int.MaxValue)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            SimpleParticle p = _particles[i];
            if (material is { } m && p.Material != m)
                continue;
            if (p.Id >= bornBefore)
                continue;

            Rules.UpdateVelocity(ref p, dt, random);
            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            Rules.UpdateRoll(ref p, dt);

            if (p.Lifetime >= p.DieTime)
            {
                _particles.RemoveAt(i--);
                continue;
            }

            _particles[i] = p;
        }
    }

    /// <summary>Sets one particle's flags byte (the oracle's seam; see <see cref="SimpleParticle.Flags"/>).</summary>
    internal void SetFlags(int id, byte flags)
    {
        int i = IndexOf(id);
        if (i < 0)
            return;
        SimpleParticle p = _particles[i];
        p.Flags = flags;
        _particles[i] = p;
    }

    /// <summary>
    /// Puts the given particles first, in the given order, the rest after as
    /// they were: the oracle's seam into stock's iteration order. Stock walks
    /// an emitter's particles in the order its last render's depth bucket sort
    /// left them, so the order an emitter whose step draws (the embers, the
    /// spores) takes its draws in follows the camera; the port steps in its
    /// own order (deliberately: the draws are decoration either way), and the
    /// replay lines the two up before comparing.
    /// </summary>
    internal void Reorder(IEnumerable<int> ids)
    {
        var first = new List<SimpleParticle>();
        foreach (int id in ids)
        {
            int i = IndexOf(id);
            if (i < 0)
                continue;
            first.Add(_particles[i]);
            _particles.RemoveAt(i);
        }

        _particles.InsertRange(0, first);
    }

    public int IndexOf(int id)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            if (_particles[i].Id == id)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// <c>CSimpleEmitter::RenderParticles</c> for one particle: the rules'
    /// colour, alpha and scale, the roll, the near-clip fade, and stock's sort
    /// key, the view depth truncated to an integer.
    /// </summary>
    public ParticleQuad Draw(in SimpleParticle p)
        => new()
        {
            Position = p.Position,
            Color = Rules.UpdateColor(p),
            Alpha = Rules.UpdateAlpha(p),
            Size = Rules.UpdateScale(p),
            Roll = p.Roll,
            Kind = ParticleQuadKind.ColorSizeAngle,
            FadeNear = Rules.NearClipMin,
            FadeFar = Rules.NearClipMax,
            TruncatedDepthSort = true,
        };

    public void Save(BinaryWriter w)
    {
        w.Write(_particles.Count);
        foreach (var p in _particles)
        {
            w.Write(p.Id); w.Write(p.Material);
            w.Write(p.Position.X); w.Write(p.Position.Y); w.Write(p.Position.Z);
            w.Write(p.Velocity.X); w.Write(p.Velocity.Y); w.Write(p.Velocity.Z);
            w.Write(p.Roll); w.Write(p.DieTime); w.Write(p.Lifetime);
            w.Write(p.R); w.Write(p.G); w.Write(p.B); w.Write(p.StartAlpha); w.Write(p.EndAlpha);
            w.Write(p.StartSize); w.Write(p.EndSize); w.Write(p.Flags); w.Write(p.RollDelta);
        }
    }

    public void Restore(BinaryReader r)
    {
        _particles.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            _particles.Add(new SimpleParticle
            {
                Id = r.ReadInt32(), Material = r.ReadInt32(),
                Position = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Velocity = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Roll = r.ReadSingle(), DieTime = r.ReadSingle(), Lifetime = r.ReadSingle(),
                R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte(), StartAlpha = r.ReadByte(), EndAlpha = r.ReadByte(),
                StartSize = r.ReadByte(), EndSize = r.ReadByte(), Flags = r.ReadByte(), RollDelta = r.ReadSingle(),
            });
        }
    }
}

/// <summary>
/// The smoke trail's and the fire trail's smoke emitter: <c>CSmokeParticle</c>
/// (c_smoke_trail.cpp). A sine fade, a colour that darkens to black over the
/// life, and a roll that slows by a factor of e^-8 a second to a floor of 0.5.
/// </summary>
public sealed class SmokeParticleRules : SimpleEmitterRules
{
    public override float UpdateAlpha(in SimpleParticle p) => SimpleEmitterMath.SineAlpha(p);

    public override Vector UpdateColor(in SimpleParticle p)
    {
        float tLifetime = p.Lifetime / p.DieTime;
        float ramp = 1.0f - tLifetime;
        return new Vector((p.R * ramp) / 255.0f, (p.G * ramp) / 255.0f, (p.B * ramp) / 255.0f);
    }

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -8.0f);
        if (MathF.Abs(p.RollDelta) < 0.5f)
            p.RollDelta = p.RollDelta > 0.0f ? 0.5f : -0.5f;
        return p.Roll;
    }
}
