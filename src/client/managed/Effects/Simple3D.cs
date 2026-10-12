//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary><c>Particle3D</c> (particle_simple3d.h) plus the executor's id.</summary>
public struct Particle3D
{
    public int Id;
    public Vector Position;
    public Vector Velocity;

    /// <summary>Pitch, yaw, roll.</summary>
    public Vector Angles;

    /// <summary>The spin, the same on every axis.</summary>
    public float AngSpeed;

    public float LifeRemaining;
    public byte FrontR, FrontG, FrontB;
    public byte Size;
    public byte BackR, BackG, BackB;
}

/// <summary>
/// <c>CSimple3DEmitter</c>'s rules (particle_simple3d.cpp), replayed against
/// <c>Fixtures/simple3d-stock.tsv.gz</c> (lane c5-particles): a shard or a
/// fleck that spins as it falls against its collision and settles flat once
/// it stops or strikes something; drawn as a world-space quad at its angles,
/// its front colour or its back by which face the camera sees, fading over
/// its last two seconds.
/// </summary>
public static class Simple3DRules
{
    /// <summary><c>anglemod</c> (mathlib): the angle wrapped to 0..360 in 65536 steps.</summary>
    public static float AngleMod(float a) => (360.0f / 65536) * ((int)(a * (65536.0f / 360.0f)) & 65535);

    /// <summary>
    /// <c>SimulateParticles</c> for one particle: false when its life runs
    /// out. A step whose collision had no planes leaves stock's trace_t
    /// unwritten; the port reads that as a miss.
    /// </summary>
    public static bool Step(ref Particle3D p, float dt, ParticleCollision collision, IParticleWorldTrace world,
                            IParticleRandom random)
    {
        p.LifeRemaining -= dt;
        if (!(p.LifeRemaining >= 0))
            return false;

        p.Angles.X += p.AngSpeed * dt;
        p.Angles.Y += p.AngSpeed * dt;
        p.Angles.Z += p.AngSpeed * dt;

        var trace = new ParticleTrace { Fraction = 1.0f };
        collision.MoveParticle(ref p.Position, ref p.Velocity, ref p.AngSpeed, hasRoll: true, dt, world, random, ref trace);

        // Decay towards flat.
        if (p.AngSpeed == 0 || trace.Fraction != 1.0)
        {
            p.Angles.X = AngleMod(p.Angles.X);
            if (p.Angles.X < 180)
            {
                if (MathF.Abs(p.Angles.X - 90) > 0.5)
                    p.Angles.X = (float)(0.5 * p.Angles.X + 46);
            }
            else
            {
                if (MathF.Abs(p.Angles.X - 270) > 0.5)
                    p.Angles.X = (float)(0.5 * p.Angles.X + 135);
            }

            p.Angles.Y = AngleMod(p.Angles.Y);
            if (MathF.Abs(p.Angles.Y) > 0.5)
                p.Angles.Y = (float)(0.5 * p.Angles.Z);
        }
        return true;
    }

    /// <summary><c>GetFadeFraction</c>: whole until the last two seconds, then fading with them.</summary>
    public static float Fade(in Particle3D p) => p.LifeRemaining >= 2.0f ? 1 : p.LifeRemaining * 0.5f;

    /// <summary>
    /// <c>RenderParticles</c> for one particle, in the view being drawn: sorted
    /// by its depth along the view, coloured by the face the camera sees.
    /// </summary>
    public static ParticleQuad Draw(in Particle3D p, in ParticleView view)
    {
        Vector toEye = view.Origin - p.Position;
        float sortKey = view.Forward.X * toEye.X + view.Forward.Y * toEye.Y + view.Forward.Z * toEye.Z;

        MoveVec.AngleVectors(p.Angles, out Vector norm, out Vector width, out Vector height);
        Vector cameraToFace = p.Position - view.Origin;
        float facing = cameraToFace.X * norm.X + cameraToFace.Y * norm.Y + cameraToFace.Z * norm.Z;
        Vector color = facing <= 0
            ? new Vector(p.FrontR / 255.0f, p.FrontG / 255.0f, p.FrontB / 255.0f)
            : new Vector(p.BackR / 255.0f, p.BackG / 255.0f, p.BackB / 255.0f);

        return new ParticleQuad
        {
            Kind = ParticleQuadKind.Angles3D,
            Position = p.Position,
            Normal = norm,
            Corner2 = width * p.Size,
            Corner3 = height * p.Size,
            Color = color,
            Alpha = Fade(p),
            Size = p.Size,
            FixedSortKey = true,
            SortKey = sortKey,
        };
    }
}

/// <summary>
/// One <c>CSimple3DEmitter</c>: one collision, its particles on as many
/// materials as its spawner picks among, each material its own executor
/// effect (<see cref="Simple3DSheet"/>), started on its first particle.
/// </summary>
public sealed class Simple3DSystem(ClientParticleEffects effects, ParticleViews views, IParticleWorldTrace world,
                                   IParticleRandom random, Vector sortOrigin)
{
    private readonly Dictionary<string, Simple3DSheet> _sheets = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public ParticleCollision Collision { get; } = new();

    public Vector SortOrigin { get; } = sortOrigin;

    public IReadOnlyCollection<Simple3DSheet> Sheets => _sheets.Values;

    internal ParticleViews Views => views;

    internal IParticleWorldTrace World => world;

    internal IParticleRandom Random => random;

    /// <summary>The particles added, across the sheets, in the order they were made.</summary>
    public List<Particle3D> Made { get; } = new();

    public void Add(string material, Particle3D p)
    {
        p.Id = _nextId++;
        Made.Add(p);
        if (!_sheets.TryGetValue(material, out var sheet))
        {
            sheet = new Simple3DSheet(this, material);
            _sheets[material] = sheet;
        }
        sheet.Add(p);
    }

    /// <summary>Starts each sheet's executor at the sort origin.</summary>
    public void Start()
    {
        foreach (var sheet in _sheets.Values)
        {
            if (sheet.EffectId == 0)
                sheet.EffectId = effects.Start(sheet, SortOrigin);
        }
    }
}

/// <summary>One material of a <see cref="Simple3DSystem"/>: the executor's effect for it.</summary>
public sealed class Simple3DSheet(Simple3DSystem system, string material) : IClientParticleEffect
{
    private readonly List<Particle3D> _particles = new();
    private readonly List<Particle3D> _pending = new();

    public string Material { get; } = material;

    public string ReloadKind => "simple3d";

    public int EffectId { get; set; }

    public IReadOnlyList<Particle3D> Particles => _particles;

    /// <summary>Particles added since the executor last asked, in the order they were made.</summary>
    public IReadOnlyList<Particle3D> Pending => _pending;

    public bool Finished => EffectId != 0 && _particles.Count == 0 && _pending.Count == 0;

    internal void Add(Particle3D p) => _pending.Add(p);

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            _particles.Insert(0, _pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        TrailEmitter.SetBounds(ref emit, null, _particles.Select(p => p.Position));
        return n;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            Particle3D p = _particles[i];
            if (!Simple3DRules.Step(ref p, dt, system.Collision, system.World, system.Random))
            {
                _particles.RemoveAt(i--);
                continue;
            }
            _particles[i] = p;
        }
        for (int i = 0; i < ids.Length; i++)
        {
            aliveOut[i] = 0;
            foreach (var p in _particles)
            {
                if (p.Id == ids[i])
                {
                    aliveOut[i] = 1;
                    break;
                }
            }
        }
        return 1;
    }

    public int Draw(Span<ParticleQuadRaw> quadsOut)
    {
        if (_particles.Count > quadsOut.Length)
            return _particles.Count;
        ParticleView view = system.Views.Current;
        if (!view.IsSet)
            return 0;
        int n = 0;
        foreach (var p in _particles.OrderByDescending(p => p.Id))
            quadsOut[n++] = Simple3DRules.Draw(p, view).ToRaw(p.Id);
        return n;
    }

    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}
