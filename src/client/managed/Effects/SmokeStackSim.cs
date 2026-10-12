//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>C_SmokeStack</c>'s simulation (c_smokestack.cpp, carried in
/// shell_smokestack_sim.cpp until lane c2-sims), the non-episodic arms this
/// build compiled: puffs born on a ring of <see cref="BaseSpread"/> around the
/// base, rising at <see cref="Speed"/> with a sideways spread, pushed by the
/// wind, twisted about the stack's axis, rolled, faded on a table cosine and
/// grown over their life. Lit by the native sphere renderer with the lights the
/// entity networks. Replayed bit for bit against
/// <c>Fixtures/smokestack-stock.tsv</c>.
/// </summary>
public sealed class SmokeStackSim
{
    /// <summary>One puff: <c>SmokeStackParticle</c> plus the executor's id.</summary>
    public struct Puff
    {
        public int Id;
        public Vector Position;
        public Vector Velocity;
        public Vector Accel;
        public float Lifetime;
        public float Angle;
        public float RollDelta;
    }

    // The datatable's members, at stock's constructor defaults.
    public float SpreadSpeed = 15;
    public float Speed = 30;
    public float StartSize = 10;
    public float EndSize = 15;
    public float Rate = 80;
    public float JetLength = 180;
    public float RollSpeed;
    public float BaseSpread = 20;
    public float Twist;
    public bool Emitting = true;
    public Vector Wind;

    /// <summary>The render colour's alpha, which scales every puff's (stock's <c>m_flAlphaScale</c>, 0..255).</summary>
    public byte Alpha = 255;

    /// <summary><c>m_InvLifetime</c>: Speed / JetLength through jet_lifetime.h's rule.</summary>
    public float InvLifetime = JetLifetime.SmokeStackInverse(30, 180);

    /// <summary>
    /// <c>m_iMaxFrames</c>. The non-episodic Start never assigns it, so it is
    /// the zeroed allocation's 0, and the frame draw still happens.
    /// </summary>
    public int MaxFrames;

    /// <summary>Whether the stack simulates out of view; stock turns it off five seconds after Start.</summary>
    public bool AlwaysSimulate;

    /// <summary>When <see cref="AlwaysSimulate"/> turns off (<c>m_flAlwaysSimulateUntil</c>).</summary>
    public float AlwaysSimulateUntil;

    private TimedEvent _spawn = TimedEvent.Uninitialised;
    private bool _twist;
    private float _m00, _m01, _m10, _m11;
    private bool _inView;
    private Vector _origin;
    private readonly List<Puff> _puffs = new();
    private int _nextId = 1;

    /// <summary>Live puffs, newest first.</summary>
    public IReadOnlyList<Puff> Puffs => _puffs;

    /// <summary>Stock's <c>Start</c>, less the registration and the material: the timer, the lifetime, five seconds of always-simulate.</summary>
    public void Start(float curTime)
    {
        _spawn.Init(Rate);
        Rederive();
        AlwaysSimulate = true;
        AlwaysSimulateUntil = curTime + 5;
    }

    /// <summary>What every update re-derives after the members land.</summary>
    public void Rederive() => InvLifetime = JetLifetime.SmokeStackInverse(Speed, JetLength);

    /// <summary>
    /// Stock's <c>Update</c>: the always-simulate deadline, this frame's puffs
    /// (when emitting and either drawn last frame or always simulating), and the
    /// twist matrix for this frame's time.
    /// </summary>
    /// <returns>How many puffs were added.</returns>
    public int Emit(float dt, bool wasDrawnPrevFrame, float curTime, float frameTime,
                    Vector origin, Vector angles, IParticleRandom random)
    {
        _origin = origin;

        if (AlwaysSimulate && curTime >= AlwaysSimulateUntil)
            AlwaysSimulate = false;

        int added = 0;
        if (Emitting && (wasDrawnPrevFrame || AlwaysSimulate))
        {
            MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);

            float delta = dt;
            while (_spawn.Next(ref delta))
            {
                // The frame is drawn and then forced to 0: the draw still happens.
                random.RandomInt(0, MaxFrames);

                float angle = random.FRand(0, 2.0f * ParticleMath.PiF);
                Vector position = origin + right * (MathF.Cos(angle) * BaseSpread) + forward * (MathF.Sin(angle) * BaseSpread);

                float a = random.FRand(-SpreadSpeed, SpreadSpeed);
                Vector sideways = a * right;
                float b = random.FRand(-SpreadSpeed, SpreadSpeed);
                Vector velocity = sideways + b * forward + Speed * up;

                var puff = new Puff
                {
                    Id = _nextId++,
                    Position = position,
                    Velocity = velocity,
                    Accel = Wind,
                    Lifetime = 0,
                    Angle = 0.0f,
                };
                puff.RollDelta = random.RandomFloat(-RollSpeed, RollSpeed);
                _puffs.Insert(0, puff);
                added++;
            }
        }

        float twist = (Twist * (ParticleMath.PiF * 2.0f) / 360.0f) * frameTime;
        _twist = twist != 0;
        if (_twist)
        {
            _m00 = MathF.Cos(twist);
            _m01 = MathF.Sin(twist);
            _m10 = -MathF.Sin(twist);
            _m11 = MathF.Cos(twist);
        }

        return added;
    }

    /// <summary>The twist matrix as the last Emit set it (for the oracle).</summary>
    public (bool On, float M00, float M01, float M10, float M11) TwistMatrix => (_twist, _m00, _m01, _m10, _m11);

    /// <summary>
    /// Stock's <c>SimulateParticles</c>: skipped while emitting, not drawn last
    /// frame and not always simulating; otherwise age every puff, remove one at
    /// the end of its life, twist it about the base, integrate the wind, roll it.
    /// </summary>
    public bool Simulate(float dt, bool wasDrawnPrevFrame)
    {
        if (Emitting && !wasDrawnPrevFrame && !AlwaysSimulate)
            return false;

        for (int i = 0; i < _puffs.Count; i++)
        {
            Puff p = _puffs[i];
            p.Lifetime += dt;

            float t = p.Lifetime * InvLifetime;
            if (t >= 1)
            {
                _puffs.RemoveAt(i--);
                continue;
            }

            if (_twist)
            {
                float tx = p.Position.X - _origin.X;
                float ty = p.Position.Y - _origin.Y;
                p.Position.X = tx * _m00 + ty * _m01 + _origin.X;
                p.Position.Y = tx * _m10 + ty * _m11 + _origin.Y;
            }

            p.Position = p.Position + p.Velocity * dt + p.Accel * (0.5f * dt * dt);
            p.Velocity = p.Velocity + p.Accel * dt;
            p.Angle += p.RollDelta * dt;
            _puffs[i] = p;
        }

        _inView = wasDrawnPrevFrame;
        return true;
    }

    /// <summary>The index of the live puff with <paramref name="id"/>, or -1.</summary>
    public int IndexOf(int id)
    {
        for (int i = 0; i < _puffs.Count; i++)
        {
            if (_puffs[i].Id == id)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Stock's <c>RenderParticles</c> for one puff: a table-cosine fade over its
    /// life, squared in the second half, scaled by the render alpha; the size
    /// lerped by the life fraction; the roll in radians. The sphere renderer
    /// lights it.
    /// </summary>
    public ParticleQuad Draw(in Puff p)
    {
        float t = p.Lifetime * InvLifetime;
        float alpha = ParticleMath.TableCos(-ParticleMath.PiF + t * ParticleMath.PiF * 2.0f) * 0.5f + 0.5f;
        if (t > 0.5f)
            alpha *= alpha;

        return new ParticleQuad
        {
            Position = p.Position,
            Alpha = alpha * (float)Alpha,
            Size = ParticleMath.FLerp(StartSize, EndSize, t),
            Roll = ParticleMath.Deg2Rad(p.Angle),
            Kind = ParticleQuadKind.SphereLit,
        };
    }

    public void Save(BinaryWriter w)
    {
        w.Write(SpreadSpeed); w.Write(Speed); w.Write(StartSize); w.Write(EndSize); w.Write(Rate); w.Write(JetLength);
        w.Write(RollSpeed); w.Write(BaseSpread); w.Write(Twist); w.Write(Emitting);
        w.Write(Wind.X); w.Write(Wind.Y); w.Write(Wind.Z); w.Write(Alpha); w.Write(InvLifetime); w.Write(MaxFrames);
        w.Write(AlwaysSimulate); w.Write(AlwaysSimulateUntil);
        w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent);
        w.Write(_twist); w.Write(_m00); w.Write(_m01); w.Write(_m10); w.Write(_m11); w.Write(_inView);
        w.Write(_origin.X); w.Write(_origin.Y); w.Write(_origin.Z);
        w.Write(_nextId);
        w.Write(_puffs.Count);
        foreach (var p in _puffs)
        {
            w.Write(p.Id);
            w.Write(p.Position.X); w.Write(p.Position.Y); w.Write(p.Position.Z);
            w.Write(p.Velocity.X); w.Write(p.Velocity.Y); w.Write(p.Velocity.Z);
            w.Write(p.Accel.X); w.Write(p.Accel.Y); w.Write(p.Accel.Z);
            w.Write(p.Lifetime); w.Write(p.Angle); w.Write(p.RollDelta);
        }
    }

    public void Restore(BinaryReader r)
    {
        SpreadSpeed = r.ReadSingle(); Speed = r.ReadSingle(); StartSize = r.ReadSingle(); EndSize = r.ReadSingle();
        Rate = r.ReadSingle(); JetLength = r.ReadSingle(); RollSpeed = r.ReadSingle(); BaseSpread = r.ReadSingle();
        Twist = r.ReadSingle(); Emitting = r.ReadBoolean();
        Wind = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); Alpha = r.ReadByte();
        InvLifetime = r.ReadSingle(); MaxFrames = r.ReadInt32();
        AlwaysSimulate = r.ReadBoolean(); AlwaysSimulateUntil = r.ReadSingle();
        _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle();
        _twist = r.ReadBoolean(); _m00 = r.ReadSingle(); _m01 = r.ReadSingle(); _m10 = r.ReadSingle(); _m11 = r.ReadSingle();
        _inView = r.ReadBoolean();
        _origin = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        _nextId = r.ReadInt32();
        int count = r.ReadInt32();
        _puffs.Clear();
        for (int i = 0; i < count; i++)
        {
            _puffs.Add(new Puff
            {
                Id = r.ReadInt32(),
                Position = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Velocity = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Accel = new Vector(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Lifetime = r.ReadSingle(), Angle = r.ReadSingle(), RollDelta = r.ReadSingle(),
            });
        }
    }
}
