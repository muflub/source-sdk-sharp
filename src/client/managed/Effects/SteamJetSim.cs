//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>C_SteamJet</c>'s simulation (c_steamjet.cpp, carried in
/// shell_steamjet_sim.cpp until lane c2-sims): the puff emitter, the five-step
/// lighting ramp sampled along the jet, and every puff's flight, roll, colour,
/// fade and growth. Stock's three particle-manager calls are its three steps:
/// <see cref="Emit"/> is <c>Update</c>, <see cref="Simulate"/> is
/// <c>SimulateParticles</c> and <see cref="Draw"/> is <c>RenderParticles</c>'s
/// arithmetic; the native executor (shell_managed_particles.cpp) builds the
/// quads. Replayed bit for bit against <c>Fixtures/steamjet-stock.tsv</c>.
/// </summary>
public sealed class SteamJetSim
{
    /// <summary>Stock's <c>STEAM_NORMAL</c> / <c>STEAM_HEATWAVE</c>.</summary>
    public const int TypeNormal = 0;
    public const int TypeHeatwave = 1;

    /// <summary><c>STEAMJET_NUMRAMPS</c>.</summary>
    public const int RampCount = 5;

    /// <summary><c>SF_EMISSIVE</c>.</summary>
    public const int SpawnFlagEmissive = 0x1;

    /// <summary>One puff: <c>SteamJetParticle</c>, plus the id the executor pairs it with.</summary>
    public struct Puff
    {
        public int Id;
        public Vector Position;
        public Vector Velocity;
        public float Roll;
        public float RollDelta;
        public float Lifetime;
        public float DieTime;
        public byte StartSize;
        public byte EndSize;
    }

    // The datatable's members, as stock's constructor defaults them.
    public float SpreadSpeed = 15;
    public float Speed = 120;
    public float StartSize = 10;
    public float EndSize = 25;
    public float Rate = 26;
    public float JetLength = 80;
    public bool Emitting = true;
    public int Type;
    public bool FaceLeft;
    public int SpawnFlags;
    public float RollSpeed;

    /// <summary>The render colour (<c>m_clrRender</c>), which tints the ramp and fades the puffs.</summary>
    public byte Red = 255, Green = 255, Blue = 255, Alpha = 255;

    /// <summary><c>m_Lifetime</c>: JetLength / Speed, through jet_lifetime.h's rule.</summary>
    public float Lifetime = JetLifetime.SteamJet(80, 120);

    private readonly Vector[] _ramps = new Vector[RampCount];
    private Vector _lastRampPosition = new(1e24f, 1e24f, 1e24f);
    private Vector _lastRampAngles = new(1e24f, 1e24f, 1e24f);
    private TimedEvent _spawn = TimedEvent.Uninitialised;
    private readonly List<Puff> _puffs = new();
    private int _nextId = 1;

    /// <summary>Live puffs, newest first (the order the particle manager links them).</summary>
    public IReadOnlyList<Puff> Puffs => _puffs;

    /// <summary>The ramp as last sampled.</summary>
    public ReadOnlySpan<Vector> Ramps => _ramps;

    /// <summary>The material stock picks for <see cref="Type"/>.</summary>
    public string Material => Type == TypeHeatwave ? "sprites/heatwave" : "particle/particle_smokegrenade";

    /// <summary>The particle cull radius stock sets on every update: <c>MAX(StartSize, EndSize)</c>.</summary>
    public float CullRadius => ParticleMath.MaxF(StartSize, EndSize);

    private bool IsEmissive => (SpawnFlags & SpawnFlagEmissive) != 0;

    /// <summary>
    /// Stock's <c>Start</c>, less the registration the executor does: the spawn
    /// timer at <see cref="Rate"/>, the lifetime, and the first ramp.
    /// </summary>
    public void Start(Vector origin, Vector angles, Func<Vector, Vector> lightForPoint)
    {
        _spawn.Init(Rate);
        Lifetime = JetLifetime.SteamJet(JetLength, Speed);
        UpdateLightingRamp(origin, angles, lightForPoint);
    }

    /// <summary>What every update re-derives after the members land (stock's update tail).</summary>
    public void Rederive() => Lifetime = JetLifetime.SteamJet(JetLength, Speed);

    private void Basis(Vector angles, out Vector forward, out Vector right)
    {
        MoveVec.AngleVectors(angles, out forward, out right, out _);

        // Legacy env_steamjet entities faced left instead of forward.
        if (FaceLeft)
        {
            Vector temp = forward;
            forward = new Vector(-right.X, -right.Y, -right.Z);
            right = temp;
        }
    }

    /// <summary>
    /// Stock's <c>Update</c>: count this frame's spawns; when there are any,
    /// set the bounds and (if the jet was drawn last frame) emit them, then
    /// re-sample the ramp if the jet moved.
    /// </summary>
    /// <param name="bounds">The bounds stock hands <c>SetBBox</c>, when it does.</param>
    /// <returns>How many puffs were added.</returns>
    public int Emit(float dt, bool wasDrawnPrevFrame, Vector origin, Vector angles,
                    IParticleRandom random, Func<Vector, Vector> lightForPoint, out (Vector Min, Vector Max)? bounds)
    {
        bounds = null;
        if (!Emitting)
            return 0;

        int toEmit = 0;
        float delta = dt;
        while (_spawn.Next(ref delta))
            ++toEmit;

        if (toEmit <= 0)
            return 0;

        MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
        if (FaceLeft)
        {
            Vector temp = forward;
            forward = new Vector(-right.X, -right.Y, -right.Z);
            right = temp;
        }

        Vector end = origin + forward * Speed;
        bounds = (ParticleMath.Min(origin, end), ParticleMath.Max(origin, end));

        int added = 0;
        if (wasDrawnPrevFrame)
        {
            while (toEmit-- > 0)
            {
                // Left to right, as the compiled expression draws them.
                float a = random.FRand(-SpreadSpeed, SpreadSpeed);
                Vector sideways = a * right;
                float b = random.FRand(-SpreadSpeed, SpreadSpeed);
                Vector velocity = sideways + b * up + Speed * forward;

                var puff = new Puff
                {
                    Id = _nextId++,
                    Position = origin,
                    Velocity = velocity,
                    Lifetime = 0,
                    DieTime = Lifetime,
                    StartSize = (byte)StartSize,
                    EndSize = (byte)EndSize,
                };
                puff.Roll = random.RandomFloat(0, 360);
                puff.RollDelta = random.RandomFloat(-RollSpeed, RollSpeed);

                _puffs.Insert(0, puff);
                added++;
            }
        }

        UpdateLightingRamp(origin, angles, lightForPoint);
        return added;
    }

    /// <summary>
    /// Stock's <c>SimulateParticles</c>: age, roll and move every puff; one past
    /// its time is removed. Skipped while emitting and not drawn last frame, so
    /// looking away and back does not break the stream.
    /// </summary>
    /// <returns>False when stock's early return skipped the step.</returns>
    public bool Simulate(float dt, bool wasDrawnPrevFrame)
    {
        if (!wasDrawnPrevFrame && Emitting)
            return false;

        for (int i = 0; i < _puffs.Count; i++)
        {
            Puff p = _puffs[i];
            p.Lifetime += dt;

            if (p.Lifetime > p.DieTime)
            {
                _puffs.RemoveAt(i--);
                continue;
            }

            p.Roll += p.RollDelta * dt;
            p.Position = p.Position + p.Velocity * dt;
            _puffs[i] = p;
        }

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
    /// Stock's <c>RenderParticles</c> for one puff: the ramp colour at its age,
    /// clamped; a sine fade over its life; the size lerped by its age in
    /// SECONDS (stock's, unnormalised, so a puff keeps growing past EndSize);
    /// its roll passed through as the angle.
    /// </summary>
    public ParticleQuad Draw(in Puff p, IParticleRandom random)
    {
        // `m_DieTime + 0.001` is a double: the quotient is taken in double and stored as float.
        float lifetimeT = (float)(p.Lifetime / (p.DieTime + 0.001));
        float fRamp = lifetimeT * (RampCount - 1);
        int iRamp = (int)fRamp;
        float fraction = fRamp - iRamp;

        Vector color = _ramps[iRamp] + (_ramps[iRamp + 1] - _ramps[iRamp]) * fraction;
        color = new Vector(ParticleMath.MinF(1.0f, color.X), ParticleMath.MinF(1.0f, color.Y), ParticleMath.MinF(1.0f, color.Z));

        float sinLifetime = MathF.Sin(p.Lifetime * 3.14159f / p.DieTime);
        float alpha = sinLifetime * (Alpha / 255.0f);

        if (Type == TypeHeatwave)
        {
            // Vector::Random(-1, 1): three rand()s, x then y then z.
            float x = random.FRand(-1.0f, 1.0f);
            float y = random.FRand(-1.0f, 1.0f);
            float z = random.FRand(-1.0f, 1.0f);
            return new ParticleQuad
            {
                Position = p.Position, Color = color, Alpha = alpha,
                Size = ParticleMath.FLerp(StartSize, EndSize, p.Lifetime),
                Normal = new Vector(x, y, z), Kind = ParticleQuadKind.ColorSizePerturbNormal,
            };
        }

        return new ParticleQuad
        {
            Position = p.Position, Color = color, Alpha = alpha,
            Size = ParticleMath.FLerp(p.StartSize, p.EndSize, p.Lifetime),
            Roll = p.Roll, Kind = ParticleQuadKind.ColorSizeAngle,
        };
    }

    /// <summary>
    /// Stock's <c>UpdateLightingRamp</c>: unless the jet has not moved (0.1
    /// either way), sample the world's light at five points along the jet's
    /// reach, tint by the render colour (added when emissive, multiplied when
    /// not), and renormalise a sample brighter than 1.
    /// </summary>
    public void UpdateLightingRamp(Vector origin, Vector angles, Func<Vector, Vector> lightForPoint)
    {
        if (ParticleMath.AreEqual(_lastRampPosition, origin, 0.1f) && ParticleMath.AreEqual(_lastRampAngles, angles, 0.1f))
            return;

        _lastRampPosition = origin;
        _lastRampAngles = angles;

        Basis(angles, out Vector forward, out _);

        Vector start = origin;
        Vector end = origin + forward * (Speed * Lifetime);

        for (int i = 0; i < RampCount; i++)
        {
            float t = (float)i / (RampCount - 1);
            Vector at = start + (end - start) * t;
            Vector ramp = lightForPoint(at);

            if (IsEmissive)
            {
                ramp = new Vector(ramp.X + (Red / 255.0f), ramp.Y + (Green / 255.0f), ramp.Z + (Blue / 255.0f));
                ramp = new Vector(Math.Clamp(ramp.X, 0.0f, 1.0f), Math.Clamp(ramp.Y, 0.0f, 1.0f), Math.Clamp(ramp.Z, 0.0f, 1.0f));
            }
            else
            {
                ramp = new Vector(ramp.X * (Red / 255.0f), ramp.Y * (Green / 255.0f), ramp.Z * (Blue / 255.0f));
            }

            float maxVal = ParticleMath.MaxF(ramp.X, ParticleMath.MaxF(ramp.Y, ramp.Z));
            if (maxVal > 1)
                ramp = ParticleMath.Divide(ramp, maxVal);

            _ramps[i] = ramp;
        }
    }

    //-------------------------------------------------------------------------
    // The reload carries everything above as bytes (plan_client_novalve §3.8):
    // the puffs in flight, the timer, the ramp and where it was sampled.
    //-------------------------------------------------------------------------

    public void Save(BinaryWriter w)
    {
        w.Write(SpreadSpeed); w.Write(Speed); w.Write(StartSize); w.Write(EndSize); w.Write(Rate);
        w.Write(JetLength); w.Write(Emitting); w.Write(Type); w.Write(FaceLeft); w.Write(SpawnFlags);
        w.Write(RollSpeed); w.Write(Red); w.Write(Green); w.Write(Blue); w.Write(Alpha); w.Write(Lifetime);
        foreach (var r in _ramps)
            WriteVector(w, r);
        WriteVector(w, _lastRampPosition);
        WriteVector(w, _lastRampAngles);
        w.Write(_spawn.TimeBetweenEvents); w.Write(_spawn.NextEvent);
        w.Write(_nextId);
        w.Write(_puffs.Count);
        foreach (var p in _puffs)
        {
            w.Write(p.Id); WriteVector(w, p.Position); WriteVector(w, p.Velocity);
            w.Write(p.Roll); w.Write(p.RollDelta); w.Write(p.Lifetime); w.Write(p.DieTime);
            w.Write(p.StartSize); w.Write(p.EndSize);
        }
    }

    public void Restore(BinaryReader r)
    {
        SpreadSpeed = r.ReadSingle(); Speed = r.ReadSingle(); StartSize = r.ReadSingle(); EndSize = r.ReadSingle();
        Rate = r.ReadSingle(); JetLength = r.ReadSingle(); Emitting = r.ReadBoolean(); Type = r.ReadInt32();
        FaceLeft = r.ReadBoolean(); SpawnFlags = r.ReadInt32(); RollSpeed = r.ReadSingle();
        Red = r.ReadByte(); Green = r.ReadByte(); Blue = r.ReadByte(); Alpha = r.ReadByte(); Lifetime = r.ReadSingle();
        for (int i = 0; i < RampCount; i++)
            _ramps[i] = ReadVector(r);
        _lastRampPosition = ReadVector(r);
        _lastRampAngles = ReadVector(r);
        _spawn.TimeBetweenEvents = r.ReadSingle(); _spawn.NextEvent = r.ReadSingle();
        _nextId = r.ReadInt32();
        int count = r.ReadInt32();
        _puffs.Clear();
        for (int i = 0; i < count; i++)
        {
            _puffs.Add(new Puff
            {
                Id = r.ReadInt32(), Position = ReadVector(r), Velocity = ReadVector(r),
                Roll = r.ReadSingle(), RollDelta = r.ReadSingle(), Lifetime = r.ReadSingle(), DieTime = r.ReadSingle(),
                StartSize = r.ReadByte(), EndSize = r.ReadByte(),
            });
        }
    }

    private static void WriteVector(BinaryWriter w, Vector v)
    {
        w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
    }

    private static Vector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
