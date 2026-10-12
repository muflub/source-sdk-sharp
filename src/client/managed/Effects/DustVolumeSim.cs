//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// One dust mote: stock's <c>CFuncDustParticle</c> (c_func_dust.h).
/// </summary>
public struct DustMote
{
    public int Id;
    public Vector Position;
    public Vector Velocity;
    public float Lifetime;
    public float DieTime;
    public float Size;
}

/// <summary>
/// Where a dust volume's motes may go: the engine's point test against the
/// volume's brush (<c>enginetrace-&gt;GetPointContents_Collideable</c>), which
/// native carries out over the owner's collideable.
/// </summary>
public interface IDustBrushPointTest
{
    /// <summary>The contents at <paramref name="point"/> inside the volume's brush.</summary>
    int PointContents(Vector point);
}

/// <summary>
/// <c>func_dustmotes</c> and <c>func_dustcloud</c>'s motes: stock's
/// <c>CDustEffect</c> and the half of <c>C_Func_Dust</c> that fed it
/// (c_func_dust.cpp, carried as <c>CSourceSharpDustVolume</c> in
/// shell_dust_render.cpp until lane c5-particles). Each frame the spawner fires
/// at <see cref="SpawnRate"/> a second and each event tries ten random points of
/// the brush's box, keeping the first the brush is solid at; a mote drifts
/// towards the wind and fades in and out over its life, and is drawn only inside
/// <see cref="DistMax"/> of the eye. One material, <c>particle/sparkles</c>.
/// Replayed bit for bit against <c>Fixtures/dust-stock.tsv.gz</c>.
/// </summary>
public sealed class DustVolumeSim
{
    public const string Material = "particle/sparkles";

    /// <summary><c>DUSTFLAGS_ON</c> (func_dust_shared.h): the spawner runs.</summary>
    public const int FlagOn = 1;

    /// <summary><c>DUSTFLAGS_SCALEMOTES</c>: a mote's size scales with its distance (func_dustmotes).</summary>
    public const int FlagScaleMotes = 2;

    /// <summary><c>DUSTFLAGS_FROZEN</c>: one batch at creation, then nothing moves or fades.</summary>
    public const int FlagFrozen = 4;

    /// <summary><c>CONTENTS_SOLID</c>.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary><c>DUST_ACCEL</c>: how fast a mote's horizontal velocity closes on the wind's.</summary>
    public const int DustAccel = 50;

    /// <summary>Stock's <c>nTests</c>: points tried per spawn event.</summary>
    public const int SpawnTests = 10;

    public byte R = 255, G = 255, B = 255, A = 255;
    public int SpawnRate;
    public float SizeMin, SizeMax;
    public int SpeedMax;
    public int LifetimeMin, LifetimeMax;
    public int DistMax;
    public float FallSpeed;
    public int Flags;

    /// <summary>The brush's model bounds, local to <see cref="Origin"/> (stock's WorldAlignMins/Maxs).</summary>
    public Vector VolumeMins, VolumeMaxs;

    /// <summary>The owner's origin, followed every tick.</summary>
    public Vector Origin;

    private TimedEvent _spawner = TimedEvent.Uninitialised;
    private bool _spawnFrozenBatch;
    private int _nextId = 1;
    private readonly List<DustMote> _motes = new();

    /// <summary>Spawn attempts, those that found the brush solid, and motes made (stock's three counters).</summary>
    public int SpawnCalls, SpawnSolid, SpawnMade;

    public IReadOnlyList<DustMote> Motes => _motes;

    /// <summary>
    /// Stock's <c>C_Func_Dust::OnDataChanged</c>: on creation a frozen volume
    /// owes one batch (made on the next tick, which has the brush in hand); every
    /// update re-arms the spawner at the rate.
    /// </summary>
    public void ApplyUpdate(bool created)
    {
        if (created && (Flags & FlagFrozen) != 0)
            _spawnFrozenBatch = true;
        _spawner.Init(SpawnRate);
    }

    /// <summary>The sort origin stock set on every update: the middle of the world-space box.</summary>
    public Vector SortOrigin => Origin + (VolumeMins + VolumeMaxs) * 0.5f;

    /// <summary>
    /// Stock's <c>C_Func_Dust::ClientThink</c>: the frozen batch if one is owed,
    /// else, while on, the frame's spawner events (at most a tenth of a second's).
    /// The spawner's events this tick (each one spawn attempt).
    /// </summary>
    public int Tick(float frameTime, Vector origin, IDustBrushPointTest brush, IParticleRandom random, List<int>? added = null)
    {
        Origin = origin;

        if (_spawnFrozenBatch)
        {
            _spawnFrozenBatch = false;
            for (int i = 0; i < SpawnRate; i++)
                AttemptSpawn(brush, random, added);
        }

        int events = 0;
        if ((Flags & FlagFrozen) == 0 && (Flags & FlagOn) != 0)
        {
            float delta = Math.Min(frameTime, 0.1f);
            while (_spawner.Next(ref delta))
            {
                events++;
                AttemptSpawn(brush, random, added);
            }
        }

        return events;
    }

    /// <summary>The box stock handed the particle manager: the world-space brush grown by the largest mote.</summary>
    public void Bounds(out Vector mins, out Vector maxs)
    {
        Vector grow = new(SizeMax, SizeMax, SizeMax);
        mins = Origin + VolumeMins - grow;
        maxs = Origin + VolumeMaxs + grow;
    }

    /// <summary>
    /// Stock's <c>AttemptSpawnNewParticle</c>: ten random points of the box, the
    /// first the brush is solid at gets a mote.
    /// </summary>
    public bool AttemptSpawn(IDustBrushPointTest brush, IParticleRandom random, List<int>? added = null)
    {
        SpawnCalls++;

        for (int test = 0; test < SpawnTests; test++)
        {
            Vector percent = RandomVector(random, 0, 1);
            Vector extent = VolumeMaxs - VolumeMins;
            Vector point = Origin + VolumeMins + new Vector(extent.X * percent.X, extent.Y * percent.Y, extent.Z * percent.Z);

            if ((brush.PointContents(point) & ContentsSolid) == 0)
                continue;

            SpawnSolid++;
            var mote = new DustMote { Id = _nextId++, Position = point };
            mote.Velocity = RandomVector(random, -SpeedMax, SpeedMax);
            mote.Velocity.Z -= FallSpeed;
            mote.Lifetime = 0;
            mote.DieTime = RemapVal(random.Rand(), 0, ParticleRandomExtensions.ValveRandMax, LifetimeMin, LifetimeMax);
            mote.Size = (Flags & FlagScaleMotes) != 0
                ? RemapVal(random.Rand(), 0, ParticleRandomExtensions.ValveRandMax, SizeMin / 10000.0f, SizeMax / 10000.0f)
                : RemapVal(random.Rand(), 0, ParticleRandomExtensions.ValveRandMax, SizeMin, SizeMax);
            _motes.Add(mote);
            SpawnMade++;
            added?.Add(mote.Id);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Stock's <c>CDustEffect::SimulateParticles</c>: unless frozen, each mote
    /// ages by <paramref name="dt"/> and dies at its time; the living close
    /// their horizontal velocity on the wind by <c>DUST_ACCEL</c> a second (of
    /// the FRAME's time, as stock spent it) and move.
    /// </summary>
    public void Simulate(float dt, float frameTime, Vector wind)
    {
        if ((Flags & FlagFrozen) != 0)
            return;

        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            DustMote m = _motes[i];
            if (!Step(ref m, dt, frameTime, wind))
            {
                _motes.RemoveAt(i);
                continue;
            }

            _motes[i] = m;
        }
    }

    /// <summary>One mote's step; false when it died.</summary>
    public static bool Step(ref DustMote m, float dt, float frameTime, Vector wind)
    {
        m.Lifetime += dt;
        if (m.Lifetime >= m.DieTime)
            return false;

        float accel = frameTime * DustAccel;
        m.Velocity.X = Approach(m.Velocity.X, wind.X, accel);
        m.Velocity.Y = Approach(m.Velocity.Y, wind.Y, accel);

        // Vector::MulAdd: a + b * scalar, per component.
        m.Position = new Vector(m.Position.X + m.Velocity.X * dt,
                                m.Position.Y + m.Velocity.Y * dt,
                                m.Position.Z + m.Velocity.Z * dt);
        return true;
    }

    private static float Approach(float v, float target, float accel)
    {
        if (v < target)
        {
            v += accel;
            if (v > target)
                v = target;
        }
        else if (v > target)
        {
            v -= accel;
            if (v < target)
                v = target;
        }

        return v;
    }

    /// <summary>
    /// Stock's <c>CDustEffect::RenderParticles</c>, one mote: its view-space
    /// place, its alpha (a sine over its life, 1 when frozen, faded towards
    /// <see cref="DistMax"/>) and its size (times its distance for motes). False
    /// past <see cref="DistMax"/>, where stock drew nothing.
    /// </summary>
    public bool Render(in DustMote m, in ParticleView view, out Vector viewPos, out float alpha, out float size)
    {
        float flAlpha;
        if ((Flags & FlagFrozen) != 0)
        {
            flAlpha = 1;
        }
        else
        {
            // M_PI is a double: the angle is computed in double and stored to a
            // float, and the sine is taken in double, as stock's expression is.
            float angle = (float)((m.Lifetime / m.DieTime) * Math.PI * 2);
            flAlpha = (float)(Math.Sin(angle - (Math.PI * 0.5f)) * 0.5f + 0.5f);
        }

        viewPos = view.TransformPoint(m.Position);
        alpha = 0;
        size = 0;
        if (!(-viewPos.Z <= DistMax))
            return false;

        flAlpha *= 1 + (viewPos.Z / DistMax);
        size = m.Size;
        if ((Flags & FlagScaleMotes) != 0)
            size *= -viewPos.Z;
        alpha = flAlpha * A;
        return true;
    }

    /// <summary>Stock's sort key for a mote: its view-space depth, truncated.</summary>
    public static float SortKey(Vector viewPos) => (int)viewPos.Z;

    /// <summary>
    /// Every mote inside <see cref="DistMax"/> as a view-space quad
    /// (<see cref="ParticleQuadKind.ViewColor255Size"/>), newest first; the
    /// count of quads written.
    /// </summary>
    public int Draw(in ParticleView view, Span<ParticleQuadRaw> quadsOut)
    {
        int n = 0;
        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            DustMote m = _motes[i];
            if (!Render(m, view, out Vector viewPos, out float alpha, out float size))
                continue;
            if (n < quadsOut.Length)
            {
                quadsOut[n] = new ParticleQuadRaw
                {
                    Id = m.Id,
                    Kind = (int)ParticleQuadKind.ViewColor255Size,
                    Position = viewPos,
                    Color = new Vector(R, G, B),
                    Alpha = alpha,
                    Size = size,
                    FixedSort = 1,
                    SortKey = SortKey(viewPos),
                };
            }

            n++;
        }

        return n;
    }

    public bool Holds(int id)
    {
        foreach (var m in _motes)
        {
            if (m.Id == id)
                return true;
        }

        return false;
    }

    /// <summary><c>Vector::Random</c>: three draws of <c>rand()</c>, x then y then z.</summary>
    public static Vector RandomVector(IParticleRandom random, float min, float max)
    {
        float x = min + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        float y = min + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        float z = min + ((float)random.Rand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        return new Vector(x, y, z);
    }

    /// <summary><c>RemapVal</c> (mathlib.h).</summary>
    public static float RemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        return c + (d - c) * (val - a) / (b - a);
    }

    public void Save(BinaryWriter w)
    {
        w.Write(R); w.Write(G); w.Write(B); w.Write(A);
        w.Write(SpawnRate); w.Write(SizeMin); w.Write(SizeMax); w.Write(SpeedMax);
        w.Write(LifetimeMin); w.Write(LifetimeMax); w.Write(DistMax); w.Write(FallSpeed); w.Write(Flags);
        WriteVector(w, VolumeMins); WriteVector(w, VolumeMaxs); WriteVector(w, Origin);
        w.Write(_spawner.TimeBetweenEvents); w.Write(_spawner.NextEvent); w.Write(_spawnFrozenBatch); w.Write(_nextId);
        w.Write(_motes.Count);
        foreach (var m in _motes)
        {
            w.Write(m.Id); WriteVector(w, m.Position); WriteVector(w, m.Velocity);
            w.Write(m.Lifetime); w.Write(m.DieTime); w.Write(m.Size);
        }
    }

    public void Restore(BinaryReader r)
    {
        R = r.ReadByte(); G = r.ReadByte(); B = r.ReadByte(); A = r.ReadByte();
        SpawnRate = r.ReadInt32(); SizeMin = r.ReadSingle(); SizeMax = r.ReadSingle(); SpeedMax = r.ReadInt32();
        LifetimeMin = r.ReadInt32(); LifetimeMax = r.ReadInt32(); DistMax = r.ReadInt32(); FallSpeed = r.ReadSingle(); Flags = r.ReadInt32();
        VolumeMins = ReadVector(r); VolumeMaxs = ReadVector(r); Origin = ReadVector(r);
        _spawner.TimeBetweenEvents = r.ReadSingle(); _spawner.NextEvent = r.ReadSingle(); _spawnFrozenBatch = r.ReadBoolean(); _nextId = r.ReadInt32();
        int count = r.ReadInt32();
        _motes.Clear();
        for (int i = 0; i < count; i++)
        {
            _motes.Add(new DustMote
            {
                Id = r.ReadInt32(), Position = ReadVector(r), Velocity = ReadVector(r),
                Lifetime = r.ReadSingle(), DieTime = r.ReadSingle(), Size = r.ReadSingle(),
            });
        }
    }

    private static void WriteVector(BinaryWriter w, Vector v)
    {
        w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
    }

    private static Vector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
