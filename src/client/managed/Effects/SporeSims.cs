//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>SporeEffect</c> (c_smoke_trail.cpp): a velocity that slows by 64
/// units/s/s and wanders by up to half a unit a step, a sine fade, and a colour
/// that follows the same sine arc.
/// </summary>
public sealed class SporeEffectRules : SimpleEmitterRules
{
    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        float speed = MoveVec.Normalize(ref p.Velocity);
        speed -= 64.0f * dt;
        float ox = random.FRand(-0.5f, 0.5f);
        float oy = random.FRand(-0.5f, 0.5f);
        float oz = random.FRand(-0.5f, 0.5f);
        p.Velocity = p.Velocity + new Vector(ox, oy, oz);
        MoveVec.Normalize(ref p.Velocity);
        p.Velocity = p.Velocity * speed;
    }

    public override Vector UpdateColor(in SimpleParticle p)
    {
        float ramp = SimpleEmitterMath.SineAlpha(p);
        return new Vector((p.R * ramp) / 255.0f, (p.G * ramp) / 255.0f, (p.B * ramp) / 255.0f);
    }

    public override float UpdateAlpha(in SimpleParticle p) => SimpleEmitterMath.SineAlpha(p);
}

/// <summary>
/// <c>C_SporeExplosion</c> (c_smoke_trail.cpp, carried in shell_trail_sim.cpp
/// until lane c2-sims): while emitting, four spores (material 0,
/// <c>particle/fire</c>) shot along the facing and one smoke puff (material 1,
/// the second dust puff) per spawn event, at half the rate when the view is at
/// least the clip distance away. Replayed bit for bit against
/// <c>Fixtures/sporeexplosion-stock.tsv</c>.
/// </summary>
public sealed class SporeExplosionSim
{
    public static string MaterialName(int material) => material == 0 ? "particle/fire" : "particle/particle_noisesphere";

    public const int MaterialCount = 2;

    // Stock's constructor defaults.
    public float SpawnRate = 32;
    public float ParticleLifetime = 5;
    public float StartSize = 32, EndSize = 64;
    public float SpawnRadius = 32;
    public bool Emitting = true;

    /// <summary>Stock's <c>m_flPreviousSpawnRate</c>: the rate the timer runs at now.</summary>
    public float PreviousSpawnRate;

    private TimedEvent _spawn = TimedEvent.AtRate(32);
    private int _nextId = 1;

    public SimpleEmitter Emitter { get; } = new(new SporeEffectRules { NearClipMin = 64, NearClipMax = 128 });

    /// <summary>
    /// Stock's arm, from its OnDataChanged: on creation, and on any later
    /// update that finds it emitting (a map's Enable).
    /// </summary>
    public void Arm(float rate)
    {
        PreviousSpawnRate = rate;
        _spawn.Init(rate);
    }

    /// <summary>Stock's <c>Update</c>, with its draws in its order.</summary>
    public int Emit(float dt, Vector origin, Vector angles, Vector viewOrigin, float clipDistance, IParticleRandom random, List<SimpleParticle> added)
    {
        if (!Emitting)
            return 0;

        float tempDelta = dt;
        Vector d = viewOrigin - origin;
        float dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);

        if (clipDistance <= dist)
        {
            if (SpawnRate == PreviousSpawnRate)
            {
                PreviousSpawnRate = SpawnRate * 0.5f;
                _spawn.ResetRate(PreviousSpawnRate);
            }
        }
        else if (SpawnRate != PreviousSpawnRate)
        {
            PreviousSpawnRate = SpawnRate;
            _spawn.ResetRate(PreviousSpawnRate);
        }

        int before = added.Count;
        while (_spawn.Next(ref tempDelta))
            AddParticles(origin, angles, random, added);
        return added.Count - before;
    }

    private void AddParticles(Vector origin, Vector angles, IParticleRandom random, List<SimpleParticle> added)
    {
        Vector dir = MoveVec.AngleForward(angles);

        for (int i = 0; i < 4; i++)
        {
            float ox = random.FRand(-SpawnRadius, SpawnRadius);
            float oy = random.FRand(-SpawnRadius, SpawnRadius);
            float oz = random.FRand(-SpawnRadius, SpawnRadius);
            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = 0,
                Position = origin + new Vector(ox, oy, oz),
                Lifetime = 0.0f,
                DieTime = 2.0f,
                Roll = 0,
                RollDelta = 0,
                R = 225, G = 140, B = 64,
            };
            p.StartAlpha = (byte)random.RandomInt(128, 255);
            p.EndAlpha = 0;
            p.StartSize = (byte)random.RandomInt(1, 2);
            p.EndSize = 1;
            p.Velocity = dir * random.RandomFloat(128.0f, 256.0f);
            Emitter.Add(p);
            added.Add(p);
        }

        {
            float half = (float)(SpawnRadius * 0.5);
            float ox = random.FRand(-half, half);
            float oy = random.FRand(-half, half);
            float oz = random.FRand(-half, half);
            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = 1,
                Position = origin + new Vector(ox, oy, oz),
                Lifetime = 0.0f,
                DieTime = 1.0f,
            };
            p.Roll = random.RandomFloat(0, 360);
            p.RollDelta = random.RandomFloat(-2.0f, 2.0f);
            p.R = 225; p.G = 140; p.B = 64;
            p.StartAlpha = (byte)random.RandomInt(32, 64);
            p.EndAlpha = 0;
            p.StartSize = (byte)StartSize;
            p.EndSize = (byte)EndSize;
            p.Velocity = dir * random.RandomFloat(64.0f, 128.0f);
            Emitter.Add(p);
            added.Add(p);
        }
    }

    public void Save(BinaryWriter w)
    {
        w.Write(SpawnRate); w.Write(ParticleLifetime); w.Write(StartSize); w.Write(EndSize); w.Write(SpawnRadius);
        w.Write(Emitting); w.Write(PreviousSpawnRate); w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent); w.Write(_nextId);
        Emitter.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        SpawnRate = r.ReadSingle(); ParticleLifetime = r.ReadSingle(); StartSize = r.ReadSingle(); EndSize = r.ReadSingle(); SpawnRadius = r.ReadSingle();
        Emitting = r.ReadBoolean(); PreviousSpawnRate = r.ReadSingle(); _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle(); _nextId = r.ReadInt32();
        Emitter.Restore(r);
    }
}

/// <summary>
/// <c>C_SporeTrail</c> (c_smoke_trail.cpp, carried in shell_trail_sim.cpp
/// until lane c2-sims): 64 embers a second at a flat rate, kept in the
/// trail's own space and drawn white at size 4 through the trail's attachment
/// matrix, only while emitting. Its particles are not a
/// <see cref="SimpleEmitter"/>'s: no roll, no fade, no velocity rule. One
/// material, the second dust puff. Replayed bit for bit against
/// <c>Fixtures/sporetrail-stock.tsv</c>.
/// </summary>
public sealed class SporeTrailSim
{
    public const string Material = "particle/particle_noisesphere";

    public bool Emitting;

    private TimedEvent _spawn = TimedEvent.AtRate(5);
    private int _nextId = 1;
    private readonly List<SimpleParticle> _particles = new();

    /// <summary>Newest first, positions in the trail's own space.</summary>
    public IReadOnlyList<SimpleParticle> Particles => _particles;

    /// <summary>Stock's <c>Start</c>, less the registration: the timer at a flat 64 a second.</summary>
    public void Start() => _spawn.Init(64);

    /// <summary>Stock's <c>Update</c> and <c>AddParticles</c>, with their draws in their order.</summary>
    public int Emit(float dt, IParticleRandom random, List<SimpleParticle> added)
    {
        if (!Emitting)
            return 0;

        int before = added.Count;
        float tempDelta = dt;
        while (_spawn.Next(ref tempDelta))
        {
            // RandomVector is Vector::Random: rand() divided down, as FRand.
            float ox = random.FRand(-4.0f, 4.0f);
            float oy = random.FRand(-4.0f, 4.0f);
            float oz = random.FRand(-4.0f, 4.0f);
            var p = new SimpleParticle
            {
                Id = _nextId++,
                Position = new Vector(ox, oy, oz),
            };
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-2.0f, 2.0f);
            p.Lifetime = 0.0f;
            p.DieTime = 0.5f;
            p.R = 225; p.G = 140; p.B = 64;
            p.StartAlpha = (byte)random.RandomInt(64, 128);
            p.EndAlpha = 0;
            p.StartSize = 1;
            p.EndSize = 1;
            float vx = random.FRand(-8.0f, 8.0f);
            float vy = random.FRand(-8.0f, 8.0f);
            float vz = random.FRand(-8.0f, 8.0f);
            p.Velocity = new Vector(vx, vy, vz);
            _particles.Insert(0, p);
            added.Add(p);
        }

        return added.Count - before;
    }

    /// <summary>Stock's <c>SimulateParticles</c>: drift and age, only while emitting.</summary>
    public void Simulate(float dt)
    {
        if (!Emitting)
            return;

        for (int i = 0; i < _particles.Count; i++)
        {
            SimpleParticle p = _particles[i];
            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            if (p.Lifetime >= p.DieTime)
            {
                _particles.RemoveAt(i--);
                continue;
            }

            _particles[i] = p;
        }
    }

    /// <summary>
    /// Stock's <c>RenderParticles</c> for one particle: its place through the
    /// attachment matrix (rows of a 3x4: rotation and translation), white, alpha
    /// one, size four. Stock's sort key is the untruncated view depth.
    /// </summary>
    public static ParticleQuad Draw(in SimpleParticle p, ReadOnlySpan<float> attachment)
    {
        Vector l = p.Position;
        var world = new Vector(
            attachment[0] * l.X + attachment[1] * l.Y + attachment[2] * l.Z + attachment[3],
            attachment[4] * l.X + attachment[5] * l.Y + attachment[6] * l.Z + attachment[7],
            attachment[8] * l.X + attachment[9] * l.Y + attachment[10] * l.Z + attachment[11]);
        return new ParticleQuad
        {
            Position = world,
            Color = new Vector(1.0f, 1.0f, 1.0f),
            Alpha = 1.0f,
            Size = 4,
            Roll = 0,
            Kind = ParticleQuadKind.ColorSize,
        };
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
    /// <c>AngleMatrix( angles, origin, matrix )</c> (mathlib_base.cpp): the
    /// trail's place as the 3x4 its particles are drawn through, rows in
    /// <paramref name="matrix"/>. What our follow sweep gave stock's
    /// <c>m_mAttachmentMatrix</c> from the entity's own place.
    /// </summary>
    public static void AttachmentMatrix(Vector angles, Vector origin, Span<float> matrix)
    {
        float sy = MathF.Sin(ParticleMath.Deg2Rad(angles.Y)), cy = MathF.Cos(ParticleMath.Deg2Rad(angles.Y));
        float sp = MathF.Sin(ParticleMath.Deg2Rad(angles.X)), cp = MathF.Cos(ParticleMath.Deg2Rad(angles.X));
        float sr = MathF.Sin(ParticleMath.Deg2Rad(angles.Z)), cr = MathF.Cos(ParticleMath.Deg2Rad(angles.Z));
        float crcy = cr * cy, crsy = cr * sy, srcy = sr * cy, srsy = sr * sy;
        matrix[0] = cp * cy; matrix[1] = sp * srcy - crsy; matrix[2] = sp * crcy + srsy; matrix[3] = origin.X;
        matrix[4] = cp * sy; matrix[5] = sp * srsy + crcy; matrix[6] = sp * crsy - srcy; matrix[7] = origin.Y;
        matrix[8] = -sp; matrix[9] = sr * cp; matrix[10] = cr * cp; matrix[11] = origin.Z;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Emitting); w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent); w.Write(_nextId);
        w.Write(_particles.Count);
        foreach (var p in _particles)
        {
            w.Write(p.Id);
            w.Write(p.Position.X); w.Write(p.Position.Y); w.Write(p.Position.Z);
            w.Write(p.Velocity.X); w.Write(p.Velocity.Y); w.Write(p.Velocity.Z);
            w.Write(p.Roll); w.Write(p.RollDelta); w.Write(p.Lifetime); w.Write(p.DieTime); w.Write(p.StartAlpha);
        }
    }

    public void Restore(BinaryReader r)
    {
        Emitting = r.ReadBoolean(); _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle(); _nextId = r.ReadInt32();
        _particles.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            _particles.Add(new SimpleParticle
            {
                Id = r.ReadInt32(),
                Position = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Velocity = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Roll = r.ReadSingle(), RollDelta = r.ReadSingle(), Lifetime = r.ReadSingle(), DieTime = r.ReadSingle(), StartAlpha = r.ReadByte(),
                R = 225, G = 140, B = 64, StartSize = 1, EndSize = 1,
            });
        }
    }
}
