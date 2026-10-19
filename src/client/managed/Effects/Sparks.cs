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

/// <summary><c>TrailParticle</c> (fx_sparks.h) plus the executor's id.</summary>
public struct TrailParticle
{
    public int Id;
    public Vector Position;
    public Vector Velocity;
    public byte R, G, B, A;
    public float DieTime;
    public float Lifetime;

    /// <summary>The tail's length, in seconds of travel.</summary>
    public float Length;

    public float Width;
}

/// <summary><c>bitsPARTICLE_TRAIL_*</c> (fx_sparks.h).</summary>
[Flags]
public enum TrailFlags
{
    None = 0,
    VelocityDampen = 0x1,
    Collide = 0x2,
    Fade = 0x4,
    FadeIn = 0x8,
}

/// <summary>
/// <c>CTrailParticles</c>' per-particle rules (fx_sparks.cpp), replayed against
/// <c>Fixtures/sparks-stock.tsv.gz</c> (lane c5-particles).
/// </summary>
public static class TrailRules
{
    /// <summary>
    /// <c>SimulateParticles</c> for one spark: moved against the collision
    /// (no roll), laterally dampened when the flag asks, then aged; false when
    /// it dies.
    /// </summary>
    public static bool Step(ref TrailParticle p, float dt, TrailFlags flags, float velocityDampen,
                            ParticleCollision collision, IParticleWorldTrace world, IParticleRandom random)
    {
        var trace = new ParticleTrace();
        float noRoll = 0.0f;
        collision.MoveParticle(ref p.Position, ref p.Velocity, ref noRoll, hasRoll: false, dt, world, random, ref trace);

        if ((flags & TrailFlags.VelocityDampen) != 0)
        {
            float attenuation = 1.0f - (dt * velocityDampen);
            if (attenuation < 0.0f)
                attenuation = 0.0f;
            p.Velocity = new Vector(p.Velocity.X * attenuation, p.Velocity.Y * attenuation, p.Velocity.Z * attenuation);
        }

        p.Lifetime += dt;
        return p.Lifetime < p.DieTime;
    }

    /// <summary>
    /// <c>RenderParticles</c> for one spark, in the view being drawn: the tail
    /// shrinks over the life (to a floor of 0.01), the colour fades in or out
    /// as the flags ask, the width is held under the tail's length, and the
    /// quad is <see cref="TracerGeometry.ComputeVerts"/>'s, sorted by the
    /// head's view depth.
    /// </summary>
    public static ParticleQuad Draw(in TrailParticle p, TrailFlags flags, in ParticleView view)
    {
        float lifePerc = 1.0f - (p.Lifetime / p.DieTime);
        float scale = p.Length * lifePerc;
        if (scale < 0.01f)
            scale = 0.01f;

        Vector start = view.TransformPoint(p.Position);
        Vector delta = view.TransformDirection(p.Velocity);

        float ramp = 1.0f;
        if ((double)p.Lifetime <= 0.3 && (flags & TrailFlags.FadeIn) != 0)
            ramp = p.Lifetime;
        else if ((flags & TrailFlags.Fade) != 0)
            ramp = 1.0f - (p.Lifetime / p.DieTime);

        const float inv255 = 1.0f / 255.0f;
        Vector color = new(p.R * ramp * inv255, p.G * ramp * inv255, p.B * ramp * inv255);
        float alpha = p.A * ramp * inv255;

        Vector tail = p.Velocity * scale;
        float length = MathF.Sqrt(tail.X * tail.X + tail.Y * tail.Y + tail.Z * tail.Z);
        float width = length < p.Width ? length : p.Width;

        Span<Vector> verts = stackalloc Vector[4];
        bool drawn = TracerGeometry.ComputeVerts(start, delta * scale, width, verts);
        return new ParticleQuad
        {
            Kind = ParticleQuadKind.Tracer,
            Position = verts[0],
            Normal = verts[1],
            Corner2 = verts[2],
            Corner3 = verts[3],
            Color = color,
            Alpha = alpha,
            Size = drawn ? 1.0f : 0.0f,
            FixedSortKey = true,
            SortKey = start.Z,
        };
    }
}

/// <summary>
/// One <c>CTrailParticles</c>: stretched sparks on one material, moved
/// against one collision, the executor's effect for it.
/// </summary>
public sealed class TrailEmitter(string material, ParticleViews views, IParticleWorldTrace world, IParticleRandom random)
    : IClientParticleEffect
{
    private readonly List<TrailParticle> _particles = new();
    private readonly List<TrailParticle> _pending = new();
    private int _nextId = 1;

    public string Material { get; } = material;

    public string ReloadKind => "trail";

    public TrailFlags Flags { get; set; }

    public float VelocityDampen { get; set; }

    public ParticleCollision Collision { get; } = new();

    /// <summary>A box set by hand (<c>GetBinding().SetBBox</c>); else the particles' own bounds.</summary>
    public (Vector Mins, Vector Maxs)? Box { get; set; }

    public int EffectId { get; set; }

    public IReadOnlyList<TrailParticle> Particles => _particles;

    /// <summary>Sparks added since the executor last asked, in the order they were made.</summary>
    public IReadOnlyList<TrailParticle> Pending => _pending;

    public bool Finished => EffectId != 0 && _particles.Count == 0 && _pending.Count == 0;

    /// <summary><c>CTrailParticles::Setup</c>: the collision tested from the origin, and the collide flag unless refused.</summary>
    public void Setup(Vector origin, Vector? direction, float angularSpread, float minSpeed, float maxSpeed, float gravity,
                      float dampen, TrailFlags flags, bool notCollideable = false)
    {
        Flags |= notCollideable ? flags : flags | TrailFlags.Collide;
        Collision.Setup(origin, direction, angularSpread, minSpeed, maxSpeed, gravity, dampen, world);
    }

    public void Add(TrailParticle p)
    {
        p.Id = _nextId++;
        _pending.Add(p);
    }

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            _particles.Insert(0, _pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        SetBounds(ref emit, Box, _particles.Select(p => p.Position));
        return n;
    }

    internal static void SetBounds(ref ParticleEmit emit, (Vector Mins, Vector Maxs)? box, IEnumerable<Vector> positions)
    {
        if (box is { } b)
        {
            emit.HasBounds = 1;
            emit.Mins = b.Mins;
            emit.Maxs = b.Maxs;
            return;
        }

        bool any = false;
        Vector mins = default, maxs = default;
        foreach (Vector v in positions)
        {
            mins = any ? ParticleMath.Min(mins, v) : v;
            maxs = any ? ParticleMath.Max(maxs, v) : v;
            any = true;
        }
        if (!any)
            return;
        emit.HasBounds = 1;
        emit.Mins = mins;
        emit.Maxs = maxs;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if ((Flags & TrailFlags.Collide) == 0)
            Collision.ClearActivePlanes();

        for (int i = 0; i < _particles.Count; i++)
        {
            TrailParticle p = _particles[i];
            if (!TrailRules.Step(ref p, dt, Flags, VelocityDampen, Collision, world, random))
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
        ParticleView view = views.Current;
        if (!view.IsSet())
            return 0;
        int n = 0;
        foreach (var p in _particles.OrderByDescending(p => p.Id))
            quadsOut[n++] = TrailRules.Draw(p, Flags, view).ToRaw(p.Id);
        return n;
    }

    // Sparks live a fraction of a second: a reload lets them go (the kind is
    // not registered, so ClientParticleEffects releases the executor).
    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}

/// <summary>
/// <c>CSimpleGlowEmitter</c>'s gate (fx_sparks.cpp), shared by the sheets of
/// one glow: nothing simulates until the main view has tested the glow's
/// sort origin, nothing draws in a view where the test found it hidden (a
/// fresh glow, younger than a tenth of a second, is not marked hidden yet: it
/// is tested again), and everything goes at the death time.
/// </summary>
public sealed class GlowGate(Vector sortOrigin, float deathTime, float startTime)
{
    public Vector SortOrigin { get; } = sortOrigin;
    public float DeathTime { get; } = deathTime;
    public float StartTime { get; } = startTime;

    /// <summary>The pixel-visibility query's handle (<c>m_queryHandle</c>).</summary>
    public int QueryHandle;

    public byte WasTested;
    public byte IsVisible;

    /// <summary><c>CurrentViewMask</c>: the view id clamped to 0..7, as a bit.</summary>
    public static byte ViewMask(int viewId) => (byte)(1 << Math.Clamp(viewId, 0, 7));

    /// <summary>The start of <c>SimulateParticles</c>: whether to kill all, skip, or step.</summary>
    public GlowStep Step(float curTime)
    {
        if (curTime > DeathTime)
            return GlowStep.Die;
        return (WasTested & 1) == 0 ? GlowStep.Wait : GlowStep.Simulate;
    }

    /// <summary>The start of <c>RenderParticles</c>: true when this view draws the glow.</summary>
    public bool Draws(int viewId, float curTime, IParticlePixelVisibility visibility)
    {
        byte mask = ViewMask(viewId);
        if ((WasTested & mask) == 0)
        {
            float visible = visibility.FractionVisible(SortOrigin, ref QueryHandle);
            if (visible == 0.0f)
            {
                if ((curTime - StartTime) <= 0.1f)
                    return false;
                IsVisible = (byte)(IsVisible & ~mask);
            }
            else
            {
                IsVisible |= mask;
            }
            WasTested |= mask;
        }
        return (IsVisible & mask) != 0;
    }
}

/// <summary>What a <see cref="GlowGate"/> lets a simulate do.</summary>
public enum GlowStep
{
    Simulate,
    Wait,
    Die,
}

/// <summary>
/// One material of a <c>CSimpleEmitter</c> (or of a <c>CSimpleGlowEmitter</c>,
/// with its <see cref="GlowGate"/>): the executor's effect for it.
/// </summary>
public sealed class SimpleSheet(string material, IParticleRandom random, ParticleViews views,
                                IParticlePixelVisibility visibility, Func<float> curTime, GlowGate? gate = null,
                                SimpleEmitterRules? rules = null)
    : IClientParticleEffect, IClientParticleSimulateSpawns
{
    private readonly List<SimpleParticle> _pending = new();
    private readonly List<int> _simulateSpawns = new();
    private int _nextId = 1;

    public SimpleEmitter Emitter { get; } = new(rules ?? new SimpleEmitterRules());

    /// <summary>A box set by hand (<c>GetBinding().SetBBox</c>); else the particles' own bounds.</summary>
    public (Vector Mins, Vector Maxs)? Box { get; set; }

    public GlowGate? Gate { get; } = gate;

    public string Material { get; } = material;

    public string ReloadKind => "simple-sheet";

    public int EffectId { get; set; }

    public IReadOnlyList<SimpleParticle> Pending => _pending;

    /// <summary>
    /// Run at the head of each step, before the particles move: an emitter that
    /// adds as it simulates (<c>CSmokeEmitter</c>) adds here, and what it adds is
    /// stepped with the rest, as stock's head-inserted particles were. The
    /// executor learns the new ids from <see cref="TakeSimulateSpawns"/> in the
    /// same crossing.
    /// </summary>
    public Action? Spawner { get; set; }

    public bool Finished => EffectId != 0 && Emitter.Particles.Count == 0 && _pending.Count == 0;

    public void Add(SimpleParticle p)
    {
        p.Id = _nextId++;
        _pending.Add(p);
    }

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            Emitter.Add(_pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        TrailEmitter.SetBounds(ref emit, Box, Emitter.Particles.Select(p => p.Position));
        return n;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        switch (Gate?.Step(curTime()) ?? GlowStep.Simulate)
        {
            case GlowStep.Wait:
                return 0;
            case GlowStep.Die:
                aliveOut[..ids.Length].Clear();
                Emitter.Clear();
                return 1;
        }

        if (Spawner is { } spawner)
        {
            int before = _pending.Count;
            spawner();
            for (int i = before; i < _pending.Count; i++)
            {
                Emitter.Add(_pending[i]);
                _simulateSpawns.Add(_pending[i].Id);
            }
            _pending.RemoveRange(before, _pending.Count - before);
        }

        Emitter.Simulate(dt, random);
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = Emitter.IndexOf(ids[i]) >= 0 ? (byte)1 : (byte)0;
        return 1;
    }

    public int TakeSimulateSpawns(Span<int> idsOut)
    {
        int n = 0;
        foreach (int id in _simulateSpawns)
        {
            if (n < idsOut.Length && Emitter.IndexOf(id) >= 0)
                idsOut[n++] = id;
        }
        _simulateSpawns.Clear();
        return n;
    }

    public int Draw(Span<ParticleQuadRaw> quadsOut)
    {
        var particles = Emitter.Particles;
        if (particles.Count > quadsOut.Length)
            return particles.Count;
        if (Gate is { } gate && !gate.Draws(views.Current.ViewId, curTime(), visibility))
            return 0;
        int n = 0;
        foreach (var p in particles.OrderByDescending(p => p.Id))
            quadsOut[n++] = Emitter.Draw(p).ToRaw(p.Id);
        return n;
    }

    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}

/// <summary>The three spark routines IEffects ends in (lane c5-particles: fx_sparks.cpp's, in C#).</summary>
public interface IClientSparks
{
    /// <summary><c>FX_ElectricSpark</c>; a null direction is stock's NULL.</summary>
    void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction);

    /// <summary><c>FX_MetalSpark</c>.</summary>
    void MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale = 1);

    /// <summary><c>FX_EnergySplash</c>.</summary>
    void EnergySplash(Vector position, Vector normal, bool explosive);

    /// <summary><c>FX_GaussExplosion</c> (the gauss discharge's temp entity).</summary>
    void GaussExplosion(Vector position, Vector direction, int type);
}

/// <summary>What the spark routines read of the client: the clock, the particle throttle, <c>fx_drawmetalspark</c>.</summary>
public interface ISparkEnvironment
{
    float CurTime { get; }

    float ThrottleScaling { get; }

    bool DrawMetalSpark { get; }
}

/// <summary><c>fx_drawmetalspark</c> (fx_sparks.cpp's ConVar, managed since lane c5-particles).</summary>
public interface IDrawMetalSpark
{
    bool On { get; }
}

/// <summary>The real one: the engine's clock, the particle library's throttle, the managed ConVar.</summary>
public sealed class SparkEnvironment(IEngineHost host, IParticleThrottle throttle, IDrawMetalSpark drawMetalSpark) : ISparkEnvironment
{
    public float CurTime => host.CurrentTime;

    public float ThrottleScaling => throttle.ThrottleScaling();

    public bool DrawMetalSpark => drawMetalSpark.On;
}

/// <summary>A line on the client-side effect list, its material named (<c>FX_AddLine</c>).</summary>
public interface IFxLineSink
{
    void AddLine(FxLineData data, string material);
}

/// <summary>A moving line on the client-side effect list (<c>FX_AddDiscreetLine</c>).</summary>
public interface IFxDiscreetLineSink
{
    void AddDiscreetLine(Vector start, Vector direction, float velocity, float length, float clipLength, float scale,
                         float life, string material);
}

/// <summary>A quad on the client-side effect list (<c>FX_AddQuad</c>'s positional form).</summary>
public interface IFxQuadSink
{
    void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias,
                 float startAlpha, float endAlpha, float alphaBias, float yaw, float deltaYaw,
                 Vector color, float lifeTime, string material, FxQuadFlags flags);
}

/// <summary>The effects one spark routine started, in the order stock created their emitters.</summary>
public sealed class SparkBurst
{
    public List<IClientParticleEffect> Emitters { get; } = new();
}

/// <summary>
/// fx_sparks.cpp's three live routines (lane c5-particles): the electric
/// spark (two trail emitters and a glow), the metal spark (a trail emitter
/// and an impact quad) and the energy splash (two quads and a simple
/// emitter), each emitter started on <see cref="ClientParticleEffects"/>
/// and drawn by the native executor. Sampled from a managed stream (the
/// draws are decoration, as the c2-sims ports ruled); replayed against
/// <c>Fixtures/sparks-stock.tsv.gz</c> with stock's draws fed back.
/// </summary>
public sealed class SparkEffects(ClientParticleEffects effects, IParticleWorldTrace world, ParticleViews views,
                                 IParticlePixelVisibility visibility, IFxQuadSink quads, ISparkEnvironment environment,
                                 IRandomSeeds seeds) : IClientSparks, IClientImpactSparks
{
    public const string SparkMaterial = "effects/spark";

    private const float ElectricSpread = 0.0f, ElectricMinSpeed = 64.0f, ElectricMaxSpeed = 300.0f,
                        ElectricGravity = 800.0f, ElectricDampen = 0.3f;

    private const float MetalSparkSpread = 0.5f, MetalSparkMinSpeed = 128.0f, MetalSparkMaxSpeed = 512.0f,
                        MetalSparkGravity = 400.0f, MetalSparkDampen = 0.25f;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    /// <summary>Routines run, for the effects route.</summary>
    public int Bursts { get; private set; }

    private TrailEmitter Trail(SparkBurst burst, IParticleRandom random)
    {
        var emitter = new TrailEmitter(SparkMaterial, views, world, random);
        burst.Emitters.Add(emitter);
        return emitter;
    }

    private SimpleSheet Sheet(SparkBurst burst, string material, IParticleRandom random, GlowGate? gate = null)
    {
        var sheet = new SimpleSheet(material, random, views, visibility, () => environment.CurTime, gate);
        burst.Emitters.Add(sheet);
        return sheet;
    }

    private void Start(TrailEmitter emitter, Vector sortOrigin)
    {
        if (emitter.EffectId == 0 && emitter.Pending.Count > 0)
            emitter.EffectId = effects.Start(emitter, sortOrigin);
    }

    private void Start(SimpleSheet sheet, Vector sortOrigin)
    {
        if (sheet.EffectId == 0 && sheet.Pending.Count > 0)
            sheet.EffectId = effects.Start(sheet, sortOrigin);
    }

    private static Vector RandomVector(IParticleRandom random, float min, float max)
    {
        float x = random.RandomFloat(min, max);
        float y = random.RandomFloat(min, max);
        float z = random.RandomFloat(min, max);
        return new Vector(x, y, z);
    }

    public void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction)
        => ElectricSpark(position, magnitude, trailLength, direction, _random);

    /// <summary><c>FX_ElectricSpark</c>: big sparks that collide, little ones that do not, and a glow.</summary>
    public SparkBurst ElectricSpark(Vector pos, int magnitude, int trailLength, Vector? direction, IParticleRandom random)
    {
        Bursts++;
        var burst = new SparkBurst();

        // Big sparks.
        TrailEmitter big = Trail(burst, random);
        big.Setup(pos, null, ElectricSpread, ElectricMinSpeed, ElectricMaxSpeed, ElectricGravity, ElectricDampen,
                  TrailFlags.VelocityDampen);

        int numSparks = (int)((magnitude * magnitude) * random.RandomFloat(2, 4));
        for (int i = 0; i < numSparks; i++)
        {
            var p = new TrailParticle { Position = pos, Lifetime = 0.0f };
            p.DieTime = magnitude * random.RandomFloat(1.0f, 2.0f);

            Vector dir = RandomVector(random, -1.0f, 1.0f);
            dir.Z = random.RandomFloat(0.5f, 1.0f);
            if (direction is { } d)
            {
                dir = dir + 2 * d;
                FxMath.Normalize(ref dir);
            }

            p.Width = random.RandomFloat(2.0f, 5.0f);
            p.Length = trailLength * random.RandomFloat(0.02f, 0.05f);
            p.Velocity = dir * random.RandomFloat(ElectricMinSpeed, ElectricMaxSpeed);
            p.R = p.G = p.B = p.A = 255;
            big.Add(p);
        }
        Start(big, pos);

        // Little sparks.
        TrailEmitter little = Trail(burst, random);
        little.Collision.Gravity = 400.0f;
        little.Flags |= TrailFlags.VelocityDampen;

        numSparks = magnitude * random.RandomInt(16, 32);
        for (int i = 0; i < numSparks; i++)
        {
            var p = new TrailParticle { Position = pos, Lifetime = 0.0f };
            Vector dir = RandomVector(random, -1.0f, 1.0f);
            if (direction is { } d)
            {
                dir = dir + d;
                FxMath.Normalize(ref dir);
            }

            p.Width = random.RandomFloat(2.0f, 4.0f);
            p.Length = trailLength * random.RandomFloat(0.02f, 0.03f);
            p.DieTime = magnitude * random.RandomFloat(0.1f, 0.2f);
            p.Velocity = dir * random.RandomFloat(128, 256);
            p.R = p.G = p.B = p.A = 255;
            little.Add(p);
        }
        Start(little, pos);

        // Caps: drawn only where the glow's origin is seen.
        float now = environment.CurTime;
        var gate = new GlowGate(pos, (float)(now + 0.2), now);
        SimpleSheet flare = Sheet(burst, "effects/yellowflare_noz", random, gate);
        SimpleSheet smoke = Sheet(burst, "particle/particle_noisesphere", random, gate);

        // Inner glow.
        flare.Add(new SimpleParticle
        {
            Position = pos, Lifetime = 0.0f, DieTime = 0.2f, R = 255, G = 255, B = 255, StartAlpha = 255, EndAlpha = 255,
            StartSize = unchecked((byte)(magnitude * random.RandomInt(4, 8))), EndSize = 0,
            Roll = random.RandomInt(0, 360), RollDelta = 0.0f,
        });

        var outer = new SimpleParticle { Position = pos, Lifetime = 0.0f, DieTime = 0.2f };
        float fColor = random.RandomInt(32, 64);
        outer.R = outer.G = outer.B = (byte)fColor;
        outer.StartAlpha = (byte)fColor;
        outer.EndAlpha = 0;
        outer.StartSize = unchecked((byte)(magnitude * random.RandomInt(32, 64)));
        outer.EndSize = 0;
        outer.Roll = random.RandomInt(0, 360);
        outer.RollDelta = random.RandomFloat(-1.0f, 1.0f);
        flare.Add(outer);

        // Smoke.
        float offX = pos.X + random.RandomFloat(-4.0f, 4.0f);
        float offY = pos.Y + random.RandomFloat(-4.0f, 4.0f);
        var puff = new SimpleParticle { Position = new Vector(offX, offY, pos.Z), Lifetime = 0.0f, DieTime = 1.0f };
        float vx = random.RandomFloat(-16.0f, 16.0f);
        float vy = random.RandomFloat(-16.0f, 16.0f);
        puff.Velocity = new Vector(vx, vy, 16.0f);
        puff.R = 255;
        puff.G = 255;
        puff.B = 200;
        puff.StartAlpha = (byte)random.RandomInt(16, 32);
        puff.EndAlpha = 0;
        puff.StartSize = (byte)random.RandomInt(4, 8);
        puff.EndSize = (byte)(puff.StartSize * 4.0f);
        puff.Roll = random.RandomInt(0, 360);
        puff.RollDelta = random.RandomFloat(-2.0f, 2.0f);
        smoke.Add(puff);

        Start(flare, pos);
        Start(smoke, pos);
        return burst;
    }

    public void MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale = 1)
    {
        if (!environment.DrawMetalSpark)
            return;
        MetalSpark(position, direction, surfaceNormal, scale, environment.ThrottleScaling, _random);
    }

    /// <summary><c>FX_MetalSpark</c>: a spray off the surface, thinned by the throttle, and a flash on it.</summary>
    public SparkBurst MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale, float throttle,
                                 IParticleRandom random)
    {
        Bursts++;
        var burst = new SparkBurst();
        Vector offset = position + surfaceNormal * 1.0f;

        TrailEmitter emitter = Trail(burst, random);
        emitter.Flags |= TrailFlags.VelocityDampen;
        emitter.VelocityDampen = 8.0f;
        emitter.Collision.Gravity = MetalSparkGravity;
        emitter.Collision.CollisionDampen = MetalSparkDampen;
        emitter.Box = (offset - new Vector(32, 32, 32), offset + new Vector(32, 32, 32));

        int numSparks = random.RandomInt(4, 8) * (scale * 2);
        numSparks = (int)(0.5f + (float)numSparks * throttle);

        const float length = 0.1f;
        for (int i = 0; i < numSparks; i++)
        {
            var p = new TrailParticle { Position = offset, Lifetime = 0.0f };
            p.DieTime = scale > 1 && i % 3 == 0
                ? random.RandomFloat(0.15f, 0.25f)   // every third flies far in a big batch
                : random.RandomFloat(0.05f, 0.1f);

            float spreadOfs = random.RandomFloat(0.0f, 2.0f);
            float x = direction.X + random.RandomFloat(-(MetalSparkSpread * spreadOfs), MetalSparkSpread * spreadOfs);
            float y = direction.Y + random.RandomFloat(-(MetalSparkSpread * spreadOfs), MetalSparkSpread * spreadOfs);
            float z = direction.Z + random.RandomFloat(-(MetalSparkSpread * spreadOfs), MetalSparkSpread * spreadOfs);
            Vector dir = new(x, y, z);
            FxMath.Normalize(ref dir);

            p.Width = random.RandomFloat(1.0f, 4.0f);
            p.Length = random.RandomFloat(length * 0.25f, length);
            p.Velocity = dir * random.RandomFloat(MetalSparkMinSpeed * (2.0f - spreadOfs), MetalSparkMaxSpeed * (2.0f - spreadOfs));
            p.R = p.G = p.B = p.A = 255;
            emitter.Add(p);
        }
        Start(emitter, offset);

        // Impact point glow.
        float yaw = random.RandomInt(0, 360);
        int size = random.RandomInt(24, 28);
        quads.AddQuad(offset, surfaceNormal, size, 0, 0.0f, 1.0f, 0.0f, 0.0f, yaw, 0.0f, new Vector(1, 1, 1), 0.1f,
                      "effects/yellowflare", FxQuadFlags.None);
        return burst;
    }

    int IClientImpactSparks.ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle)
    {
        if (!environment.DrawMetalSpark)
            return 0;
        SparkBurst burst = ImpactMetalSpark(origin, shotDir, normal, scale, throttle, _random);
        return burst.Emitters.OfType<TrailEmitter>().Sum(e => e.Pending.Count);
    }

    /// <summary>The shot reflected off the surface: <c>shotDir + normal * ( shotDir . normal * -2 )</c>.</summary>
    public static Vector Reflect(Vector shotDir, Vector normal)
    {
        float dot = shotDir.X * normal.X + shotDir.Y * normal.Y + shotDir.Z * normal.Z;
        return shotDir + normal * (dot * -2.0f);
    }

    /// <summary>
    /// ApplyImpactChoice's metal arm (shell_impact_effects.cpp, from stock's
    /// PerformCustomEffects): the reflected shot jittered by up to 0.2 on each
    /// axis, then <c>FX_MetalSpark</c> at the impact's scale.
    /// </summary>
    public SparkBurst ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle,
                                       IParticleRandom random)
    {
        Vector reflect = Reflect(shotDir, normal);
        reflect.X += random.RandomFloat(-0.2f, 0.2f);
        reflect.Y += random.RandomFloat(-0.2f, 0.2f);
        reflect.Z += random.RandomFloat(-0.2f, 0.2f);
        return MetalSpark(origin, reflect, normal, scale, throttle, random);
    }

    public void GaussExplosion(Vector position, Vector direction, int type)
        => GaussExplosion(position, direction, _random);

    /// <summary>
    /// <c>FX_GaussExplosion</c> (shell_impact_effects.cpp, from stock's
    /// c_impact_effects.cpp): a ring of colliding sparks sprayed about the
    /// wobbled direction, its numbers ImpactEffectPolicy's Gauss block, then an
    /// electric spark along the last spark's direction. The kind is unread, as
    /// stock's body left it.
    /// </summary>
    public SparkBurst GaussExplosion(Vector pos, Vector dir, IParticleRandom random)
    {
        var block = new float[ImpactEffectPolicy.MaxFloats];
        if (ImpactEffectPolicy.Build(ImpactEffect.Gauss, 0, 1.0f, false, block) < ImpactEffectPolicy.HeaderFloats)
            return new SparkBurst();
        float H(ImpactHeader f) => block[(int)f];
        float B(ImpactBurst f) => block[ImpactEffectPolicy.BurstAt(0) + (int)f];
        float RandF(float min, float max) => min == max ? min : random.RandomFloat(min, max);
        int RandI(float min, float max)
        {
            int lo = (int)min, hi = (int)max;
            return lo == hi ? lo : random.RandomInt(lo, hi);
        }

        Bursts++;
        var burst = new SparkBurst();
        float wobble = H(ImpactHeader.HeadA);
        float wx = dir.X + random.RandomFloat(-wobble, wobble);
        float wy = dir.Y + random.RandomFloat(-wobble, wobble);
        float wz = dir.Z + random.RandomFloat(-wobble, wobble);
        Vector vDir = new(wx, wy, wz);
        FxMath.Normalize(ref vDir);

        TrailEmitter emitter = Trail(burst, random);
        emitter.Collision.Gravity = H(ImpactHeader.Gravity);
        emitter.Flags |= TrailFlags.VelocityDampen | TrailFlags.Collide;
        emitter.Collision.Setup(pos, vDir, H(ImpactHeader.AngularSpray), H(ImpactHeader.CollMin), H(ImpactHeader.CollMax),
                                H(ImpactHeader.Gravity), H(ImpactHeader.Dampen), world);

        int numSparks = RandI(B(ImpactBurst.CountMin), B(ImpactBurst.CountMax));
        for (int i = 0; i < numSparks; i++)
        {
            var p = new TrailParticle { Position = pos, Lifetime = 0.0f };
            vDir = RandomVector(random, B(ImpactBurst.SprayMin), B(ImpactBurst.SprayMax)) + dir;
            FxMath.Normalize(ref vDir);
            p.Width = RandF(B(ImpactBurst.SizeMin), B(ImpactBurst.SizeMax));
            p.Length = RandF(B(ImpactBurst.ExtraA), B(ImpactBurst.ExtraB));
            p.DieTime = RandF(B(ImpactBurst.LifeMin), B(ImpactBurst.LifeMax));
            p.Velocity = vDir * RandF(B(ImpactBurst.SpeedMin), B(ImpactBurst.SpeedMax));
            p.R = p.G = p.B = 255;
            p.A = (byte)B(ImpactBurst.AlphaMax);
            emitter.Add(p);
        }
        Start(emitter, pos);

        SparkBurst spark = ElectricSpark(pos, 1, 1, vDir, random);
        burst.Emitters.AddRange(spark.Emitters);
        return burst;
    }

    // METAL_SCRAPE_* (fx_sparks.cpp).
    private const float MetalScrapeMinSpeed = 128.0f, MetalScrapeMaxSpeed = 512.0f, MetalScrapeSpread = 0.3f,
                        MetalScrapeGravity = 800.0f, MetalScrapeDampen = 0.4f;

    public void SparkFan(Vector position, Vector normal) => SparkFan(position, normal, _random);

    /// <summary>
    /// <c>FX_SparkFan</c>, the ManhackSparks client effect: thirty-five white
    /// sparks fanned off the normal from a point just off the surface,
    /// colliding and laterally dampened, each two seconds long.
    /// </summary>
    public SparkBurst SparkFan(Vector position, Vector normal, IParticleRandom random)
    {
        Bursts++;
        var burst = new SparkBurst();
        Vector offset = position + normal * 1.0f;

        TrailEmitter emitter = Trail(burst, random);
        emitter.Setup(offset, normal, MetalScrapeSpread, MetalScrapeMinSpeed, MetalScrapeMaxSpeed, MetalScrapeGravity,
                      MetalScrapeDampen, TrailFlags.VelocityDampen);

        const float length = 0.06f;
        for (int i = 0; i < 35; i++)
        {
            var p = new TrailParticle { Position = offset, Lifetime = 0.0f };
            float spreadOfs = random.RandomFloat(0.0f, 2.0f);
            float x = normal.X + random.RandomFloat(-(MetalScrapeSpread * spreadOfs), MetalScrapeSpread * spreadOfs);
            float y = normal.Y + random.RandomFloat(-(MetalScrapeSpread * spreadOfs), MetalScrapeSpread * spreadOfs);
            float z = normal.Z + random.RandomFloat(-(MetalScrapeSpread * spreadOfs), MetalScrapeSpread * spreadOfs);
            p.Width = random.RandomFloat(2.0f, 5.0f);
            p.Length = random.RandomFloat(length * 0.25f, length);
            // Stock's RandomFloat( 2.0f, 2.0f ): a draw all the same.
            p.DieTime = random.RandomFloat(2.0f, 2.0f);
            p.Velocity = new Vector(x, y, z) * random.RandomFloat(MetalScrapeMinSpeed * (2.0f - spreadOfs), MetalScrapeMaxSpeed * (2.0f - spreadOfs));
            p.R = p.G = p.B = p.A = 255;
            emitter.Add(p);
        }
        Start(emitter, offset);
        return burst;
    }

    public void EnergySplash(Vector position, Vector normal, bool explosive)
        => EnergySplash(position, normal, _random);

    /// <summary><c>FX_EnergySplash</c>: a quick flash, a lingering burn, and eight rising sparks.</summary>
    public SparkBurst EnergySplash(Vector pos, Vector normal, IParticleRandom random)
    {
        Bursts++;
        var burst = new SparkBurst();
        const FxQuadFlags bias = FxQuadFlags.BiasScale | FxQuadFlags.BiasAlpha;

        quads.AddQuad(pos, normal, 64.0f, 0, 0.75f, 1.0f, 0.0f, 0.4f, random.RandomInt(0, 360), 0, new Vector(1, 1, 1),
                      0.25f, "effects/combinemuzzle1_nocull", bias);
        quads.AddQuad(pos, normal, 16, 32, 0.75f, 1.0f, 0.0f, 0.4f, random.RandomInt(0, 360), 0, new Vector(1, 1, 1),
                      0.5f, "effects/combinemuzzle2_nocull", bias);

        SimpleSheet sheet = Sheet(burst, SparkMaterial, random);
        for (int j = 0; j < 8; j++)
        {
            float ox = random.RandomFloat(-8.0f, 8.0f);
            float oy = random.RandomFloat(-8.0f, 8.0f);
            float oz = random.RandomFloat(0.0f, 4.0f);
            var p = new SimpleParticle { Position = new Vector(ox, oy, oz) + pos };
            float vx = random.RandomFloat(-4.0f, 4.0f);
            float vy = random.RandomFloat(-4.0f, 4.0f);
            float vz = random.RandomFloat(16.0f, 64.0f);
            p.Velocity = new Vector(vx, vy, vz);
            p.StartSize = (byte)random.RandomFloat(2, 4);
            p.DieTime = random.RandomFloat(0.4f, 0.6f);
            p.Lifetime = 0.0f;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = random.RandomFloat(-4.0f, 4.0f);
            p.R = p.G = p.B = 255;
            p.StartAlpha = 255;
            p.EndAlpha = 0;
            p.EndSize = 0;
            sheet.Add(p);
        }
        Start(sheet, pos);
        return burst;
    }
}

/// <summary>The <c>ManhackSparks</c> client effect (fx_sparks.cpp's ManhackSparkCallback).</summary>
public sealed class ManhackSparksEffect(SparkEffects sparks) : IClientEffectHandler
{
    public string EffectName => "ManhackSparks";

    public void Dispatch(in EffectData data) => sparks.SparkFan(data.Origin, data.Normal);
}
