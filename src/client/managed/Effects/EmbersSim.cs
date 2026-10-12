//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CEmberEmitter</c> (c_embers.cpp): a velocity that slows by a unit a
/// second and wanders sideways by up to 1/40 a step (the vertical draw is
/// taken and thrown away, as stock's is), a colour that darkens to black.
/// </summary>
public sealed class EmberEmitterRules : SimpleEmitterRules
{
    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        float speed = MoveVec.Normalize(ref p.Velocity);
        speed -= 1.0f * dt;
        float ox = random.FRand(-0.025f, 0.025f);
        float oy = random.FRand(-0.025f, 0.025f);
        random.FRand(-0.025f, 0.025f);
        p.Velocity = p.Velocity + new Vector(ox, oy, 0.0f);
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
/// <c>C_Embers</c> (c_embers.cpp, carried in shell_effects_sim.cpp until lane
/// c2-sims): while emitting, <see cref="Density"/> embers a second, each at a
/// random point of the brush's model bounds, flying along the entity's facing
/// at <see cref="Speed"/> with a spread of an eighth of it, living
/// <see cref="Lifetime"/> seconds. One material, <c>particle/fire</c>.
/// Replayed bit for bit against <c>Fixtures/embers-stock.tsv</c>.
/// </summary>
public sealed class EmbersSim
{
    public const string Material = "particle/fire";

    public int Density, Lifetime, Speed;
    public bool Emitting;
    public Vector Mins, Maxs;

    /// <summary>Stock's <c>m_clrRender</c>: white; nothing in this build sets it.</summary>
    public byte R = 255, G = 255, B = 255;

    private TimedEvent _spawn = TimedEvent.Uninitialised;
    private int _nextId = 1;

    public SimpleEmitter Emitter { get; } = new(new EmberEmitterRules());

    /// <summary>Stock's <c>Start</c>: the spawn timer at the density.</summary>
    public void Start() => _spawn.Init(Density);

    /// <summary>Stock's <c>Simulate</c> (the spawn half): the frame's events, each an ember.</summary>
    public int Emit(float frameTime, Vector angles, IParticleRandom random, List<SimpleParticle> added)
    {
        if (!Emitting)
            return 0;
        int before = added.Count;
        float tempDelta = frameTime;
        while (_spawn.Next(ref tempDelta))
            SpawnEmber(angles, random, added);
        return added.Count - before;
    }

    private void SpawnEmber(Vector angles, IParticleRandom random, List<SimpleParticle> added)
    {
        float lifetime = Lifetime;
        if (!float.IsFinite(lifetime) || !(lifetime > 0.0f))
            return;

        float x = random.RandomFloat(Mins.X, Maxs.X);
        float y = random.RandomFloat(Mins.Y, Maxs.Y);
        float z = random.RandomFloat(Mins.Z, Maxs.Z);
        float cScale = random.RandomFloat(0.75f, 1.0f);
        var p = new SimpleParticle
        {
            Id = _nextId++,
            Position = new Vector(x, y, z),
            Lifetime = 0.0f,
            DieTime = Lifetime,
            R = (byte)(R * cScale),
            G = (byte)(G * cScale),
            B = (byte)(B * cScale),
            StartAlpha = 255,
            EndAlpha = 0,
            StartSize = 1,
            EndSize = 0,
            RollDelta = 0,
            Roll = 0,
        };
        Vector v = MoveVec.AngleForward(angles) * Speed;
        float spread = Speed / 8;
        v.X += random.RandomFloat(-spread, spread);
        v.Y += random.RandomFloat(-spread, spread);
        v.Z += random.RandomFloat(-spread, spread);
        p.Velocity = v;
        Emitter.Add(p);
        added.Add(p);
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Density); w.Write(Lifetime); w.Write(Speed); w.Write(Emitting);
        w.Write(Mins.X); w.Write(Mins.Y); w.Write(Mins.Z); w.Write(Maxs.X); w.Write(Maxs.Y); w.Write(Maxs.Z);
        w.Write(R); w.Write(G); w.Write(B);
        w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent); w.Write(_nextId);
        Emitter.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        Density = r.ReadInt32(); Lifetime = r.ReadInt32(); Speed = r.ReadInt32(); Emitting = r.ReadBoolean();
        Mins = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); Maxs = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        R = r.ReadByte(); G = r.ReadByte(); B = r.ReadByte();
        _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle(); _nextId = r.ReadInt32();
        Emitter.Restore(r);
    }
}
