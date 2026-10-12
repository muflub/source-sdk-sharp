//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CRocketTrailParticle</c> (c_smoke_trail.cpp): a sine fade and
/// <see cref="SmokeParticleRules"/>' slowing roll, the colour left alone.
/// </summary>
public sealed class RocketTrailParticleRules : SimpleEmitterRules
{
    public override float UpdateAlpha(in SimpleParticle p) => SimpleEmitterMath.SineAlpha(p);

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -8.0f);
        if (MathF.Abs(p.RollDelta) < 0.5f)
            p.RollDelta = p.RollDelta > 0.0f ? 0.5f : -0.5f;
        return p.Roll;
    }
}

/// <summary>
/// <c>CEmberEffect</c> (particles_simple.cpp): a velocity that slows by 12
/// units/s/s and wanders by up to 1/8 a step, a colour that darkens to black.
/// </summary>
public sealed class EmberRules : SimpleEmitterRules
{
    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        float speed = MoveVec.Normalize(ref p.Velocity);
        speed -= 12.0f * dt;
        float ox = random.FRand(-0.125f, 0.125f);
        float oy = random.FRand(-0.125f, 0.125f);
        float oz = random.FRand(-0.125f, 0.125f);
        p.Velocity = p.Velocity + new Vector(ox, oy, oz);
        MoveVec.Normalize(ref p.Velocity);
        p.Velocity = p.Velocity * speed;
    }

    public override Vector UpdateColor(in SimpleParticle p)
    {
        float ramp = 1.0f - (p.Lifetime / p.DieTime);
        return new Vector((p.R * ramp) / 255.0f, (p.G * ramp) / 255.0f, (p.B * ramp) / 255.0f);
    }
}

/// <summary>
/// <c>C_RocketTrail</c> (c_smoke_trail.cpp, carried in shell_trail_sim.cpp
/// until lane c2-sims): each frame a muzzle flash of eight sprites behind the
/// nozzle that live a hundredth of a second, puffs filling the gap the trail
/// moved since last frame, and, when damaged, eight embers. Three emitters, as
/// stock has: <see cref="Puffs"/> (materials 0 and 1, the dust puffs),
/// <see cref="Flash"/> (materials 1..4, <c>effects/muzzleflash</c><i>n</i>) and
/// <see cref="Embers"/> (1..4, <c>sprites/flamelet</c><i>n</i>). Replayed bit
/// for bit against <c>Fixtures/rockettrail-stock.tsv</c>.
/// </summary>
public sealed class RocketTrailSim
{
    public static string PuffMaterial(int material) => material == 0 ? "particle/particle_smokegrenade" : "particle/particle_noisesphere";

    public static string FlashMaterial(int material) => "effects/muzzleflash" + material;

    public static string EmberMaterial(int material) => "sprites/flamelet" + material;

    // Stock's constructor defaults.
    public float SpawnRate = 10;
    public Vector StartColor = new(0.5f, 0.5f, 0.5f);
    public float ParticleLifetime = 5;
    public float StopEmitTime;
    public float MinSpeed = 2, MaxSpeed = 4;
    public float StartSize = 35, EndSize = 55;
    public float SpawnRadius = 2;
    public float Opacity = 0.5f;
    public bool Emitting = true;
    public bool Damaged;
    public float FlareScale;

    /// <summary>Where the trail was at the end of the last update (stock's <c>m_vecLastPosition</c>).</summary>
    public Vector LastPosition;

    private int _nextId = 1;

    /// <summary>The first id this frame's <see cref="Emit"/> handed out: the flash and embers born at or after it are not stepped this frame.</summary>
    public int FrameFirstId { get; private set; } = 1;

    public SimpleEmitter Puffs { get; } = new(new RocketTrailParticleRules { NearClipMin = 64.0f, NearClipMax = 128.0f });

    public SimpleEmitter Flash { get; } = new(new SimpleEmitterRules());

    public SimpleEmitter Embers { get; } = new(new EmberRules());

    /// <summary>Stock's <c>Start</c>, less the registration: the gap is measured from here.</summary>
    public void Start(Vector origin) => LastPosition = origin;

    /// <summary>
    /// Stock's <c>Update</c>. Nothing on a frame with no frame time (a paused
    /// game); otherwise the flash, the puffs if emitting and the embers if
    /// damaged, with stock's draws in stock's order.
    /// </summary>
    public void Emit(float frameTime, Vector origin, Vector angles, IParticleRandom random,
                     List<SimpleParticle> flash, List<SimpleParticle> puffs, List<SimpleParticle> embers)
    {
        FrameFirstId = _nextId;
        if (frameTime == 0.0f)
            return;

        Vector forward = MoveVec.AngleForward(angles);
        forward = new Vector(-forward.X, -forward.Y, -forward.Z);

        float flScale = random.RandomFloat(FlareScale - 0.5f, FlareScale + 0.5f);

        for (int i = 1; i < 9; i++)
        {
            Vector offset = origin + forward * (i * 2.0f * FlareScale);
            var p = new SimpleParticle
            {
                Id = _nextId++,
                Material = random.RandomInt(1, 4),
                Position = offset,
                Lifetime = 0.0f,
                DieTime = 0.01f,
                R = 255, G = 255, B = 255,
                StartAlpha = 255, EndAlpha = 128,
            };
            p.StartSize = (byte)((random.RandomFloat(5.0f, 6.0f) * (12 - i) / 9) * flScale);
            p.EndSize = p.StartSize;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = 0.0f;
            Flash.Add(p);
            flash.Add(p);
        }

        if (Emitting)
        {
            Vector moveDiff = origin - LastPosition;
            float moveLength = MoveVec.Normalize(ref moveDiff);

            int numPuffs = (int)(moveLength / (StartSize / 2.0f));
            if (numPuffs > 50)
                numPuffs = 50;

            float step = moveLength / numPuffs;
            for (int i = 1; i < numPuffs + 1; i++)
            {
                Vector offset = LastPosition + (moveDiff * step * i);
                var p = new SimpleParticle
                {
                    Id = _nextId++,
                    Material = random.RandomInt(0, 1),
                    Position = offset,
                    Lifetime = 0.0f,
                };
                p.DieTime = ParticleLifetime + random.RandomFloat(ParticleLifetime * 0.9f, ParticleLifetime * 1.1f);

                float vx = random.FRand(-1.0f, 1.0f);
                float vy = random.FRand(-1.0f, 1.0f);
                float vz = random.FRand(-1.0f, 1.0f);
                p.Velocity = new Vector(vx, vy, vz) * random.RandomFloat(MinSpeed, MaxSpeed);

                SetColor(ref p, StartColor * random.RandomFloat(0.75f, 1.25f));

                p.StartSize = (byte)(StartSize * random.RandomFloat(0.75f, 1.25f));
                p.EndSize = (byte)(EndSize * random.RandomFloat(1.0f, 1.25f));

                float alpha = random.RandomFloat(Opacity * 0.75f, Opacity * 1.25f);
                if (alpha > 1.0f)
                    alpha = 1.0f;
                if (alpha < 0.0f)
                    alpha = 0.0f;
                p.StartAlpha = (byte)(alpha * 255);
                p.EndAlpha = 0;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
                Puffs.Add(p);
                puffs.Add(p);
            }
        }

        if (Damaged)
        {
            int flame = random.RandomInt(1, 4);
            for (int i = 0; i < 8; i++)
            {
                // RandomVector is Vector::Random: rand() divided down, as FRand.
                float rx = random.FRand(-8, 8);
                float ry = random.FRand(-8, 8);
                float rz = random.FRand(-8, 8);
                var p = new SimpleParticle
                {
                    Id = _nextId++,
                    Material = flame,
                    Position = new Vector(rx, ry, rz) + origin,
                    Lifetime = 0.0f,
                    DieTime = 0.25f,
                };

                float vx = random.FRand(-1.0f, 1.0f);
                float vy = random.FRand(-1.0f, 1.0f);
                float vz = random.FRand(-1.0f, 1.0f);
                p.Velocity = new Vector(vx, vy, vz) * random.RandomFloat(32, 128);

                SetColor(ref p, StartColor * random.RandomFloat(0.75f, 1.25f));
                p.StartSize = 8;
                p.EndSize = 32;
                p.StartAlpha = 255;
                p.EndAlpha = 0;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = random.RandomFloat(-8.0f, 8.0f);
                Embers.Add(p);
                embers.Add(p);
            }
        }

        LastPosition = origin;
    }

    private static void SetColor(ref SimpleParticle p, Vector c)
    {
        p.R = (byte)(Math.Clamp(c.X, 0.0f, 1.0f) * 255.0f);
        p.G = (byte)(Math.Clamp(c.Y, 0.0f, 1.0f) * 255.0f);
        p.B = (byte)(Math.Clamp(c.Z, 0.0f, 1.0f) * 255.0f);
    }

    /// <summary>
    /// Every emitter, stepped as the particle manager steps each. Stock makes
    /// a new flash emitter and a new ember emitter every frame, and a new
    /// emitter's first frame is not simulated: this frame's flash and embers are
    /// drawn as spawned and stepped from the next frame on.
    /// </summary>
    public void Simulate(float dt, IParticleRandom random)
    {
        Puffs.Simulate(dt, random);
        Flash.Simulate(dt, random, bornBefore: FrameFirstId);
        Embers.Simulate(dt, random, bornBefore: FrameFirstId);
    }

    public void Save(BinaryWriter w)
    {
        w.Write(SpawnRate); w.Write(StartColor.X); w.Write(StartColor.Y); w.Write(StartColor.Z);
        w.Write(ParticleLifetime); w.Write(StopEmitTime); w.Write(MinSpeed); w.Write(MaxSpeed);
        w.Write(StartSize); w.Write(EndSize); w.Write(SpawnRadius); w.Write(Opacity);
        w.Write(Emitting); w.Write(Damaged); w.Write(FlareScale);
        w.Write(LastPosition.X); w.Write(LastPosition.Y); w.Write(LastPosition.Z); w.Write(_nextId); w.Write(FrameFirstId);
        Puffs.Save(w);
        Flash.Save(w);
        Embers.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        SpawnRate = r.ReadSingle(); StartColor = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        ParticleLifetime = r.ReadSingle(); StopEmitTime = r.ReadSingle(); MinSpeed = r.ReadSingle(); MaxSpeed = r.ReadSingle();
        StartSize = r.ReadSingle(); EndSize = r.ReadSingle(); SpawnRadius = r.ReadSingle(); Opacity = r.ReadSingle();
        Emitting = r.ReadBoolean(); Damaged = r.ReadBoolean(); FlareScale = r.ReadSingle();
        LastPosition = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); _nextId = r.ReadInt32(); FrameFirstId = r.ReadInt32();
        Puffs.Restore(r);
        Flash.Restore(r);
        Embers.Restore(r);
    }
}
