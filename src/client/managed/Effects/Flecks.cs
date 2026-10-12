//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Effects;

/// <summary><c>FleckParticle</c> (fx_fleck.h) plus the executor's id and which of its system's two sheets it uses.</summary>
public struct FleckParticle
{
    public int Id;

    /// <summary>The order the system's sampler made it in, across both sheets.</summary>
    public int Sequence;

    public int Sheet;
    public Vector Position;
    public Vector Velocity;
    public float Roll;
    public float RollDelta;
    public float DieTime;
    public float Lifetime;
    public byte R, G, B;
    public byte Size;
}

/// <summary>
/// <c>CFleckParticles</c>' per-particle rules (fx_fleck.cpp), replayed against
/// <c>Fixtures/fleck-stock.tsv.gz</c> (lane c5-particles).
/// </summary>
public static class FleckRules
{
    /// <summary>
    /// <c>SimulateParticles</c> for one fleck: age it (false when it dies),
    /// roll it, move it against the system's collision, and stop it dead in
    /// solid. Stock read <c>trace.allsolid</c> from a <c>trace_t</c> that
    /// <c>MoveParticle</c> leaves unwritten when the collision has no planes
    /// (uninitialised stack); the port reads false there.
    /// </summary>
    public static bool Step(ref FleckParticle p, float dt, ParticleCollision collision, IParticleWorldTrace world,
                            IParticleRandom random)
    {
        p.Lifetime += dt;
        if (p.Lifetime >= p.DieTime)
            return false;

        p.Roll += p.RollDelta * dt;
        var trace = new ParticleTrace();
        collision.MoveParticle(ref p.Position, ref p.Velocity, ref p.RollDelta, hasRoll: true, dt, world, random, ref trace);
        if (trace.AllSolid)
        {
            p.Velocity = default;
            p.RollDelta = 0.0f;
        }
        return true;
    }

    /// <summary><c>RenderParticles</c> for one fleck: its colour, fading out over its life, at its size and roll; sorted by the truncated view depth.</summary>
    public static ParticleQuad Draw(in FleckParticle p) => new()
    {
        Position = p.Position,
        Color = new Vector(p.R / 255.0f, p.G / 255.0f, p.B / 255.0f),
        Alpha = 1.0f - (p.Lifetime / p.DieTime),
        Size = p.Size,
        Roll = p.Roll,
        Kind = ParticleQuadKind.ColorSizeAngle,
        TruncatedDepthSort = true,
    };
}

/// <summary>
/// One <c>CFleckParticles</c>: a named system with a box, one collision, and
/// its particles on two sheets. Each sheet is its own executor (one material
/// each), started on the first fleck that uses it.
/// </summary>
public sealed class FleckSystem
{
    /// <summary><c>MAX_RADIUS_BBOX_MERGE</c>.</summary>
    public const float MaxMergeRadius = 120.0f;

    public FleckSystem(string name, Vector mins, Vector maxs, string sheet0, string sheet1)
    {
        Name = name;
        Mins = mins;
        Maxs = maxs;
        Sheets = [new FleckSheet(this, sheet0), new FleckSheet(this, sheet1)];
    }

    public string Name { get; }
    public Vector Mins { get; private set; }
    public Vector Maxs { get; private set; }
    public ParticleCollision Collision { get; } = new();
    public FleckSheet[] Sheets { get; }

    /// <summary>Started (or waiting to be): a system with no sheet left is out of the merge list.</summary>
    public bool Finished => Sheets[0].Finished && Sheets[1].Finished;

    /// <summary>
    /// <c>CMergeSameNameBbox::MergeParticleSystems</c>: the same name, and the
    /// box grown to take the new one stays under the merge radius; then the
    /// box is grown.
    /// </summary>
    public bool TryMerge(string name, Vector center, Vector extents)
    {
        if (!string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
            return false;
        Vector mins = Mins, maxs = Maxs;
        AddPointToBounds(center - extents, ref mins, ref maxs);
        AddPointToBounds(center + extents, ref mins, ref maxs);
        Vector size = maxs - mins;
        float radius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
        if (radius >= MaxMergeRadius)
            return false;
        Mins = mins;
        Maxs = maxs;
        return true;
    }

    private static void AddPointToBounds(Vector v, ref Vector mins, ref Vector maxs)
    {
        mins = new Vector(v.X < mins.X ? v.X : mins.X, v.Y < mins.Y ? v.Y : mins.Y, v.Z < mins.Z ? v.Z : mins.Z);
        maxs = new Vector(v.X > maxs.X ? v.X : maxs.X, v.Y > maxs.Y ? v.Y : maxs.Y, v.Z > maxs.Z ? v.Z : maxs.Z);
    }
}

/// <summary>One sheet of a <see cref="FleckSystem"/>: the executor's effect for that material.</summary>
public sealed class FleckSheet(FleckSystem system, string material) : IClientParticleEffect
{
    private readonly List<FleckParticle> _particles = new();
    private readonly List<FleckParticle> _pending = new();
    private int _nextId = 1;

    public FleckSystem System { get; } = system;

    public string Material { get; } = material;

    public string ReloadKind => "fleck";

    /// <summary>The executor id, 0 until the first fleck started it.</summary>
    public int EffectId { get; set; }

    /// <summary>The world trace and the random stream the step uses (the service's).</summary>
    internal IParticleWorldTrace? World { get; set; }

    internal IParticleRandom? Random { get; set; }

    public IReadOnlyList<FleckParticle> Particles => _particles;

    /// <summary>Flecks added since the executor last asked (they reach it at its next Update).</summary>
    public IReadOnlyList<FleckParticle> Pending => _pending;

    public bool Finished => EffectId != 0 && _particles.Count == 0 && _pending.Count == 0;

    public void Add(FleckParticle p)
    {
        p.Id = _nextId++;
        _pending.Add(p);
    }

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            // Linked at the head, as the particle manager links them.
            _particles.Insert(0, _pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        emit.HasBounds = 1;
        emit.Mins = System.Mins;
        emit.Maxs = System.Maxs;
        return n;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (World is null || Random is null)
            return 0;
        for (int i = 0; i < _particles.Count; i++)
        {
            FleckParticle p = _particles[i];
            if (!FleckRules.Step(ref p, dt, System.Collision, World, Random))
            {
                _particles.RemoveAt(i--);
                continue;
            }
            _particles[i] = p;
        }
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = IndexOf(ids[i]) >= 0 ? (byte)1 : (byte)0;
        return 1;
    }

    private int IndexOf(int id)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            if (_particles[i].Id == id)
                return i;
        }
        return -1;
    }

    public int Draw(Span<ParticleQuadRaw> quadsOut)
    {
        if (_particles.Count > quadsOut.Length)
            return _particles.Count;
        // Ids descending, as the executor's lookup expects: newest first.
        int n = 0;
        foreach (var p in _particles.OrderByDescending(p => p.Id))
            quadsOut[n++] = FleckRules.Draw(p).ToRaw(p.Id);
        return n;
    }

    // Flecks live a second or two: a reload lets them go rather than carry a
    // collision's planes and a merge list across the swap (the kind is not
    // registered, so ClientParticleEffects releases the executors).
    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}

/// <summary>
/// The fleck systems alive this level (<c>g_FleckMergeList</c>), and the
/// impact's flecks (<c>CreateFleckParticles</c>, the sampling
/// shell_impact_effects.cpp did): one call per impact, sampled from a
/// managed stream (lane c5-particles; the draws are decoration, as the
/// c2-sims ports ruled), started on <see cref="ClientParticleEffects"/>.
/// </summary>
public sealed class FleckSystems(ClientParticleEffects effects, IParticleWorldTrace world, IRandomSeeds seeds)
{
    private readonly List<FleckSystem> _systems = new();
    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());
    private int _sequence;

    public IReadOnlyList<FleckSystem> Live
    {
        get
        {
            _systems.RemoveAll(s => s.Finished);
            return _systems;
        }
    }

    /// <summary><c>CFleckParticles::Create</c>: a live system of the name that takes the box, or a new one.</summary>
    public FleckSystem Create(string name, Vector center, Vector extents, string sheet0, string sheet1)
    {
        foreach (var s in Live)
        {
            if (s.TryMerge(name, center, extents))
                return s;
        }
        var system = new FleckSystem(name, center - extents, center + extents, sheet0, sheet1);
        _systems.Insert(0, system);
        return system;
    }

    /// <summary>Starts a sheet's executor on its first fleck, at the system's centre.</summary>
    public void Add(FleckSystem system, FleckParticle p)
    {
        p.Sequence = ++_sequence;
        FleckSheet sheet = system.Sheets[p.Sheet];

        // A merged system's sheet may have emptied and let its executor go
        // while its sibling lived: it starts again.
        if (sheet.EffectId != 0 && effects.Get(sheet.EffectId) is null)
            sheet.EffectId = 0;
        if (sheet.EffectId == 0)
        {
            sheet.World = world;
            sheet.Random = _random;
            Vector center = (system.Mins + system.Maxs) * 0.5f;
            sheet.EffectId = effects.Start(sheet, center);
            if (sheet.EffectId == 0)
                return;
        }
        sheet.Add(p);
    }

    /// <summary>
    /// <c>CreateFleckParticles</c> (stock's FX_DebrisFlecks burst 0): the
    /// system at the spawn offset, its collision set up along the surface
    /// normal, and the flecks sampled inside the ranges <c>pP</c> (the
    /// impact's block, ImpactEffectPolicy) holds. The count started.
    /// </summary>
    public int ImpactFlecks(Vector endPos, Vector normal, Vector color, int scale, float throttle,
                            ReadOnlySpan<float> pP, IParticleRandom random)
    {
        float[] block = pP.ToArray();
        float H(ImpactHeader f) => block[(int)f];
        float B(ImpactBurst f) => block[ImpactEffectPolicy.BurstAt(0) + (int)f];
        float RandF(float min, float max) => min == max ? min : random.RandomFloat(min, max);
        int RandI(float min, float max)
        {
            int lo = (int)min, hi = (int)max;
            return lo == hi ? lo : random.RandomInt(lo, hi);
        }

        Vector spawnOffset = endPos + normal * H(ImpactHeader.SpawnOffset);
        var (sheet0, sheet1) = Sheets((ImpactMaterial)(int)B(ImpactBurst.Material));
        FleckSystem system = Create("FX_DebrisFlecks", spawnOffset, new Vector(5, 5, 5), sheet0, sheet1);

        float maxSpeed = H(ImpactHeader.CollMax) * scale;
        float angularSpray = MathF.Max(H(ImpactHeader.HeadB), H(ImpactHeader.AngularSpray) - ((float)scale * H(ImpactHeader.HeadA)));
        system.Collision.Setup(spawnOffset, normal, angularSpray, H(ImpactHeader.CollMin), maxSpeed,
                               H(ImpactHeader.Gravity), H(ImpactHeader.Dampen), world);

        float fScale = throttle * (float)scale;
        int count = (int)(0.5f + fScale * (float)RandI(B(ImpactBurst.CountMin), B(ImpactBurst.CountMax)));
        for (int i = 0; i < count; i++)
        {
            var p = new FleckParticle { Sheet = random.RandomInt(0, 1), Position = spawnOffset };
            p.Lifetime = 0.0f;
            p.DieTime = RandF(B(ImpactBurst.LifeMin), B(ImpactBurst.LifeMax));
            Vector dir = new(normal.X + random.RandomFloat(-angularSpray, angularSpray), 0, 0);
            dir.Y = normal.Y + random.RandomFloat(-angularSpray, angularSpray);
            dir.Z = normal.Z + random.RandomFloat(-angularSpray, angularSpray);
            p.Size = unchecked((byte)RandI(B(ImpactBurst.SizeMin), B(ImpactBurst.SizeMax)));
            p.Velocity = dir * (random.RandomFloat(B(ImpactBurst.SpeedMin), maxSpeed) * (B(ImpactBurst.ExtraA) - p.Size));
            p.Roll = RandF(B(ImpactBurst.RollMin), B(ImpactBurst.RollMax));
            p.RollDelta = RandF(B(ImpactBurst.RollDeltaMin), B(ImpactBurst.RollDeltaMax));
            float ramp = RandF(B(ImpactBurst.RampMin), B(ImpactBurst.RampMax));
            p.R = (byte)(MathF.Min(1.0f, color.X * ramp) * 255.0f);
            p.G = (byte)(MathF.Min(1.0f, color.Y * ramp) * 255.0f);
            p.B = (byte)(MathF.Min(1.0f, color.Z * ramp) * 255.0f);
            Add(system, p);
        }
        return count;
    }

    /// <summary>The impact's burst-0 sheet pair (ImpactMaterialPair's fleck rows; anything else is the dust puff).</summary>
    public static (string, string) Sheets(ImpactMaterial slot) => slot switch
    {
        ImpactMaterial.FleckCement => ("effects/fleck_cement1", "effects/fleck_cement2"),
        ImpactMaterial.FleckWood => ("effects/fleck_wood1", "effects/fleck_wood2"),
        ImpactMaterial.FleckAntlion => ("effects/fleck_antlion1", "effects/fleck_antlion2"),
        ImpactMaterial.FleckGlass => ("effects/fleck_glass1", "effects/fleck_glass2"),
        _ => ("particle/particle_smokegrenade", "particle/particle_smokegrenade"),
    };

    /// <summary>The level is going: the executors go with it.</summary>
    public void Reset() => _systems.Clear();
}
