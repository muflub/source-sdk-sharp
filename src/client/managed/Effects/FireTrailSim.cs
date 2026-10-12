//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>C_FireTrail</c> (c_smoke_trail.cpp, carried in shell_trail_sim.cpp until
/// lane c2-sims): each frame flames filling the gap the trail moved since the
/// last (at least one, at most 32) and one smoke puff at the trail, all into
/// one <see cref="SmokeParticleRules"/> emitter. Seven materials, stock's
/// <c>FTRAIL_*</c> order: 0 and 1 the smoke puffs, 2..6
/// <c>sprites/flamelet1..5</c>. Replayed bit for bit against
/// <c>Fixtures/firetrail-stock.tsv</c>.
/// </summary>
public sealed class FireTrailSim
{
    public const int MaterialCount = 7;

    public static string MaterialName(int material) => material switch
    {
        0 => "particle/particle_smokegrenade",
        1 => "particle/particle_noisesphere",
        _ => "sprites/flamelet" + (material - 1),
    };

    // Stock's #defines.
    private const int StartSizeDefine = 8;
    private const float ParticleLifetimeDefine = 0.075f;
    private const int MinSpeedDefine = 32;
    private const int MaxSpeedDefine = 64;

    /// <summary>Stock's <c>m_flLifetime</c>: the curtime it stops at; zero for never.</summary>
    public float Lifetime;

    /// <summary>Where the trail was at the end of the last update (stock's <c>m_vecLastPosition</c>).</summary>
    public Vector LastPosition;

    private int _nextId = 1;

    /// <summary>The <c>CSmokeParticle</c> emitter, near-clipped 64..128 as stock sets it.</summary>
    public SimpleEmitter Emitter { get; } = new(new SmokeParticleRules { NearClipMin = 64.0f, NearClipMax = 128.0f });

    /// <summary>Stock's <c>Start</c>, less the registration: the gap is measured from here.</summary>
    public void Start(Vector origin) => LastPosition = origin;

    /// <summary>Stock's <c>Update</c>, with its draws in its order.</summary>
    /// <returns>The particles added, oldest first.</returns>
    public int Emit(float curTime, Vector origin, IParticleRandom random, List<SimpleParticle> added)
    {
        if (Lifetime != 0 && Lifetime <= curTime)
            return 0;

        int before = added.Count;
        Vector moveDiff = origin - LastPosition;
        float moveLength = MoveVec.Normalize(ref moveDiff);

        int numPuffs = (int)(moveLength / (StartSizeDefine / 2.0f));
        numPuffs = Math.Clamp(numPuffs, 1, 32);

        float step = moveLength / numPuffs;
        for (int i = 1; i < numPuffs + 1; i++)
        {
            // RandomVector is Vector::Random: rand() divided down, as FRand.
            float rx = random.FRand(-4.0f, 4.0f);
            float ry = random.FRand(-4.0f, 4.0f);
            float rz = random.FRand(-4.0f, 4.0f);
            Vector offset = LastPosition + (moveDiff * step * i) + new Vector(rx, ry, rz);

            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = random.RandomInt(2, 6),
                Position = offset,
                Lifetime = 0.0f,
                DieTime = 0.5f,
            };

            float vx = random.FRand(0.0f, 1.0f);
            float vy = random.FRand(0.0f, 1.0f);
            float vz = random.FRand(0.0f, 1.0f);
            Vector v = new Vector(vx, vy, vz) * random.RandomFloat(MinSpeedDefine, MaxSpeedDefine);
            v.Z += 50;
            p.Velocity = v;

            p.R = 255; p.G = 255; p.B = 255;
            p.StartSize = (byte)(StartSizeDefine * 2.0f);
            p.EndSize = (byte)(StartSizeDefine * 0.5f);
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.Roll = 0.0f;
            p.RollDelta = random.RandomFloat(-16.0f, 16.0f);
            Emitter.Add(p);
            added.Add(p);
        }

        // The smoke.
        {
            float rx = random.FRand(-StartSizeDefine * 0.5f, StartSizeDefine * 0.5f);
            float ry = random.FRand(-StartSizeDefine * 0.5f, StartSizeDefine * 0.5f);
            float rz = random.FRand(-StartSizeDefine * 0.5f, StartSizeDefine * 0.5f);
            Vector offset = new Vector(rx, ry, rz) + origin;

            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = random.RandomInt(0, 1),
                Position = offset,
                Lifetime = 0.0f,
            };
            p.DieTime = (ParticleLifetimeDefine * 10.0f) + random.RandomFloat(ParticleLifetimeDefine * 0.75f, ParticleLifetimeDefine * 1.25f);

            float vx = random.FRand(0.0f, 1.0f);
            float vy = random.FRand(0.0f, 1.0f);
            float vz = random.FRand(0.0f, 1.0f);
            Vector v = new Vector(vx, vy, vz) * random.RandomFloat(MinSpeedDefine, MaxSpeedDefine);
            v.Z += random.RandomFloat(50, 100);
            p.Velocity = v;

            p.R = (byte)(255.0f * 0.5f);
            p.G = (byte)(245.0f * 0.5f);
            p.B = (byte)(205.0f * 0.5f);
            p.StartSize = (byte)(16 * random.RandomFloat(0.75f, 1.25f));
            p.EndSize = (byte)(p.StartSize * 2.5f);
            p.StartAlpha = 64;
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-16.0f, 16.0f);
            Emitter.Add(p);
            added.Add(p);
        }

        LastPosition = origin;
        return added.Count - before;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Lifetime); w.Write(LastPosition.X); w.Write(LastPosition.Y); w.Write(LastPosition.Z); w.Write(_nextId);
        Emitter.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        Lifetime = r.ReadSingle(); LastPosition = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); _nextId = r.ReadInt32();
        Emitter.Restore(r);
    }
}
