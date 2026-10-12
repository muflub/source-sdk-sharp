//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CDustFollower</c> (c_smoke_trail.cpp): high drag, no colour change. A
/// sine fade, the velocity halved every 0.3 s and the roll speed every 0.5 s.
/// </summary>
public sealed class DustFollowerRules : SimpleEmitterRules
{
    public override float UpdateAlpha(in SimpleParticle p) => SimpleEmitterMath.SineAlpha(p);

    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
        => p.Velocity = p.Velocity * SimpleEmitterMath.ExponentialDecay(0.3f, dt);

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta *= SimpleEmitterMath.ExponentialDecay(0.5f, dt);
        return p.Roll;
    }
}

/// <summary>
/// <c>C_DustTrail</c>'s emitter (c_smoke_trail.cpp, carried in
/// shell_trail_sim.cpp until lane c2-sims): <see cref="SmokeTrailSim"/>'s twin
/// with one colour, one material (<c>particle/smokesprites_0001</c>, the
/// executor's channel 0), a <see cref="DustFollowerRules"/> emitter, and a fade
/// of new puffs towards the stop time. Replayed bit for bit against
/// <c>Fixtures/dusttrail-stock.tsv</c>.
/// </summary>
public sealed class DustTrailSim
{
    public const string Material = "particle/smokesprites_0001";

    // Stock's constructor defaults.
    public float SpawnRate = 10;
    public Vector Color = new(0.5f, 0.5f, 0.5f);
    public float ParticleLifetime = 5;
    public float StartEmitTime;
    public float StopEmitTime;
    public float MinSpeed = 2, MaxSpeed = 4;
    public float MinDirectedSpeed, MaxDirectedSpeed;
    public float StartSize = 35, EndSize = 55;
    public float SpawnRadius = 2;
    public float Opacity = 0.5f;
    public bool Emitting = true;

    private TimedEvent _spawn = TimedEvent.AtRate(10);
    private int _nextId = 1;

    /// <summary>The <c>CDustFollower</c> emitter, near-clipped 64..128 as stock sets it.</summary>
    public SimpleEmitter Emitter { get; } = new(new DustFollowerRules { NearClipMin = 64.0f, NearClipMax = 128.0f });

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
    /// moment of the frame and gives it stock's draws, in stock's order.
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
                Material = random.RandomInt(0, 0),
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

            float maxVal = ParticleMath.MaxF(Color.X, Color.Y);
            if (maxVal < Color.Z)
                maxVal = Color.Z;
            Vector offsetColor = ParticleMath.Divide(Color, maxVal);
            offsetColor = offsetColor * random.RandomFloat(-0.2f, 0.2f);
            offsetColor = offsetColor + Color;
            p.R = (byte)(Math.Clamp(offsetColor.X, 0.0f, 1.0f) * 255.0f);
            p.G = (byte)(Math.Clamp(offsetColor.Y, 0.0f, 1.0f) * 255.0f);
            p.B = (byte)(Math.Clamp(offsetColor.Z, 0.0f, 1.0f) * 255.0f);

            p.StartSize = (byte)StartSize;
            p.EndSize = (byte)EndSize;

            float alpha = Math.Clamp(random.RandomFloat(Opacity * 0.75f, Opacity * 1.25f), 0.0f, 1.0f);
            if (StopEmitTime != 0 && StopEmitTime > StartEmitTime)
                alpha *= MathF.Sqrt((StopEmitTime - curTime) / (StopEmitTime - StartEmitTime));

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
        w.Write(SpawnRate); w.Write(Color.X); w.Write(Color.Y); w.Write(Color.Z);
        w.Write(ParticleLifetime); w.Write(StartEmitTime); w.Write(StopEmitTime); w.Write(MinSpeed); w.Write(MaxSpeed);
        w.Write(MinDirectedSpeed); w.Write(MaxDirectedSpeed); w.Write(StartSize); w.Write(EndSize);
        w.Write(SpawnRadius); w.Write(Opacity); w.Write(Emitting);
        w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent); w.Write(_nextId);
        Emitter.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        SpawnRate = r.ReadSingle(); Color = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        ParticleLifetime = r.ReadSingle(); StartEmitTime = r.ReadSingle(); StopEmitTime = r.ReadSingle(); MinSpeed = r.ReadSingle(); MaxSpeed = r.ReadSingle();
        MinDirectedSpeed = r.ReadSingle(); MaxDirectedSpeed = r.ReadSingle(); StartSize = r.ReadSingle(); EndSize = r.ReadSingle();
        SpawnRadius = r.ReadSingle(); Opacity = r.ReadSingle(); Emitting = r.ReadBoolean();
        _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle(); _nextId = r.ReadInt32();
        Emitter.Restore(r);
    }
}
