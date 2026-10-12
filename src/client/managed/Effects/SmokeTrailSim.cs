//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>C_SmokeTrail</c>'s emitter (c_smoke_trail.cpp, carried in
/// shell_trail_sim.cpp until lane c2-sims): puffs at <see cref="SpawnRate"/> a
/// second inside <see cref="SpawnRadius"/> of the trail, flung at a random
/// speed plus a directed one along the trail's facing, tinted around the start
/// colour, into a <see cref="SmokeParticleRules"/> emitter that fades, darkens
/// and rolls them. Two materials, picked per puff (the executor's channels 0
/// and 1). Replayed bit for bit against <c>Fixtures/smoketrail-stock.tsv</c>.
/// </summary>
public sealed class SmokeTrailSim
{
    /// <summary>Stock's <c>g_Mat_DustPuff[0]</c> and <c>[1]</c>.</summary>
    public static string MaterialName(int material) => material == 0 ? "particle/particle_smokegrenade" : "particle/particle_noisesphere";

    public const int MaterialCount = 2;

    // Stock's constructor defaults.
    public float SpawnRate = 10;
    public Vector StartColor = new(0.5f, 0.5f, 0.5f);
    public float ParticleLifetime = 5;
    public float StopEmitTime;
    public float MinSpeed = 2, MaxSpeed = 4;
    public float MinDirectedSpeed, MaxDirectedSpeed;
    public float StartSize = 35, EndSize = 55;
    public float SpawnRadius = 2;
    public float Opacity = 0.5f;
    public bool Emitting = true;

    private TimedEvent _spawn = TimedEvent.Uninitialised;
    private int _nextId = 1;

    /// <summary>The <c>CSmokeParticle</c> emitter, near-clipped 64..128 as stock sets it.</summary>
    public SimpleEmitter Emitter { get; } = new(new SmokeParticleRules { NearClipMin = 64.0f, NearClipMax = 128.0f });

    /// <summary>Stock's <c>SetSpawnRate</c>: the rate and the timer re-initialised.</summary>
    public void SetSpawnRate(float rate)
    {
        SpawnRate = rate;
        _spawn.Init(rate);
    }

    /// <summary>Stock's <c>Start</c>, less the registration: the spawn timer at the rate.</summary>
    public void Start() => _spawn.Init(SpawnRate);

    /// <summary>
    /// Stock's <c>Update</c>: nothing when not emitting or past the stop time;
    /// otherwise each spawn event places a puff where the trail was at that
    /// moment of the frame (the origin wound back by the velocity) and gives it
    /// stock's draws, in stock's order.
    /// </summary>
    /// <returns>The puffs added, oldest first.</returns>
    public int Emit(float dt, float curTime, Vector origin, Vector angles, Vector velocity, IParticleRandom random, List<SimpleParticle> added)
    {
        if (!Emitting)
            return 0;
        if (StopEmitTime != 0 && StopEmitTime <= curTime)
            return 0;

        float tempDelta = dt;
        Vector vecOrigin = origin + velocity * -dt;
        Vector forward = MoveVec.AngleForward(angles);

        int count = 0;
        while (_spawn.Next(ref tempDelta))
        {
            float fldt = dt - tempDelta;

            float ox = random.FRand(-SpawnRadius, SpawnRadius);
            float oy = random.FRand(-SpawnRadius, SpawnRadius);
            float oz = random.FRand(-SpawnRadius, SpawnRadius);
            Vector offset = new Vector(ox, oy, oz) + vecOrigin;
            offset = offset + velocity * fldt;

            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = random.RandomInt(0, 1),
                Position = offset,
                Lifetime = 0.0f,
                DieTime = ParticleLifetime,
            };

            float vx = random.FRand(-1.0f, 1.0f);
            float vy = random.FRand(-1.0f, 1.0f);
            float vz = random.FRand(-1.0f, 1.0f);
            Vector v = new Vector(vx, vy, vz) * random.RandomFloat(MinSpeed, MaxSpeed);
            v = v + velocity;
            float directed = random.RandomFloat(MinDirectedSpeed, MaxDirectedSpeed);
            p.Velocity = v + forward * directed;

            float maxVal = ParticleMath.MaxF(StartColor.X, StartColor.Y);
            if (maxVal < StartColor.Z)
                maxVal = StartColor.Z;
            Vector offsetColor = ParticleMath.Divide(StartColor, maxVal);
            offsetColor = offsetColor * random.RandomFloat(-0.2f, 0.2f);
            offsetColor = offsetColor + StartColor;
            p.R = (byte)(Math.Clamp(offsetColor.X, 0.0f, 1.0f) * 255.0f);
            p.G = (byte)(Math.Clamp(offsetColor.Y, 0.0f, 1.0f) * 255.0f);
            p.B = (byte)(Math.Clamp(offsetColor.Z, 0.0f, 1.0f) * 255.0f);

            p.StartSize = (byte)StartSize;
            p.EndSize = (byte)EndSize;

            float alpha = Math.Clamp(random.RandomFloat(Opacity * 0.75f, Opacity * 1.25f), 0.0f, 1.0f);
            p.StartAlpha = (byte)(alpha * 255);
            p.EndAlpha = 0;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-1.0f, 1.0f);

            Emitter.Add(p);
            added.Add(p);
            count++;
        }

        return count;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(SpawnRate); w.Write(StartColor.X); w.Write(StartColor.Y); w.Write(StartColor.Z);
        w.Write(ParticleLifetime); w.Write(StopEmitTime); w.Write(MinSpeed); w.Write(MaxSpeed);
        w.Write(MinDirectedSpeed); w.Write(MaxDirectedSpeed); w.Write(StartSize); w.Write(EndSize);
        w.Write(SpawnRadius); w.Write(Opacity); w.Write(Emitting);
        w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent); w.Write(_nextId);
        Emitter.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        SpawnRate = r.ReadSingle(); StartColor = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        ParticleLifetime = r.ReadSingle(); StopEmitTime = r.ReadSingle(); MinSpeed = r.ReadSingle(); MaxSpeed = r.ReadSingle();
        MinDirectedSpeed = r.ReadSingle(); MaxDirectedSpeed = r.ReadSingle(); StartSize = r.ReadSingle(); EndSize = r.ReadSingle();
        SpawnRadius = r.ReadSingle(); Opacity = r.ReadSingle(); Emitting = r.ReadBoolean();
        _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle(); _nextId = r.ReadInt32();
        Emitter.Restore(r);
    }
}
