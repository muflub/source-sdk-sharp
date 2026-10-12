using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The emitters the temp entities' muzzle flashes fill (<see cref="IFxEmitters"/>),
/// on the managed executors (lane c5-particles): what the pool's native
/// <c>FxEmitter*</c> rows made in the particle library. A flash builds one
/// while its emitter is open; at <see cref="Release"/> (stock's
/// <c>CSmartPtr</c> leaving scope) each material's particles start as one
/// effect: a <see cref="SimpleSheet"/> for the simple, local-space and ember
/// emitters (the embers under <see cref="EmberRules"/>), a
/// <see cref="TrailEmitter"/> for the trail. A local-space emitter's executor
/// follows the entity's attachment natively, as <c>CLocalSpaceEmitter</c> did.
/// </summary>
public sealed class PoolEmitters(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                                 IParticleWorldTrace world, IEngineHost host, IRandomSeeds seeds) : IFxEmitters
{
    /// <summary>The pool's row held 64 particles; an add past them was stock's NULL there.</summary>
    public const int MaxParticles = 64;

    // FLE_VIEWMODEL (particles_localspace.h).
    private const int FleViewModel = 0x1;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());
    private readonly Dictionary<int, Open> _open = [];
    private int _nextId = 1;

    /// <summary>Emitters started since the load, and the effects they became (cs_cl_fx_report).</summary>
    public int Emitters { get; private set; }

    public int Effects { get; private set; }

    private sealed class Open(FxEmitterKind kind, uint handle, int attachment, int flags)
    {
        public readonly FxEmitterKind Kind = kind;
        public readonly uint Handle = handle;
        public readonly int Attachment = attachment;
        public readonly int Flags = flags;
        public bool BeforeViewModel;
        public (Vector Mins, Vector Maxs)? Box;
        public Vector? SortOrigin;
        public TrailFlags TrailFlags;
        public float Gravity;
        public readonly List<(string Material, Vector Position, FxSimpleParticle Simple, FxTrailParticle Trail)> Particles = [];
    }

    public int Create(FxEmitterKind kind, string name, uint handle, int attachment, int flags)
    {
        int id = _nextId++;
        _open[id] = new Open(kind, handle, attachment, flags);
        return id;
    }

    public void DrawBeforeViewModel(int emitter)
    {
        if (_open.TryGetValue(emitter, out var e))
            e.BeforeViewModel = true;
    }

    public void SetBBox(int emitter, in Vector mins, in Vector maxs)
    {
        if (_open.TryGetValue(emitter, out var e))
            e.Box = (mins, maxs);
    }

    public void SetSortOrigin(int emitter, in Vector origin)
    {
        if (_open.TryGetValue(emitter, out var e))
            e.SortOrigin = origin;
    }

    public void SetTrailFlag(int emitter, int flag)
    {
        if (_open.TryGetValue(emitter, out var e) && e.Kind == FxEmitterKind.Trail)
            e.TrailFlags |= (TrailFlags)flag;
    }

    public void SetCollisionGravity(int emitter, float gravity)
    {
        if (_open.TryGetValue(emitter, out var e) && e.Kind == FxEmitterKind.Trail)
            e.Gravity = gravity;
    }

    public int Add(int emitter, string material, in Vector position)
    {
        if (!_open.TryGetValue(emitter, out var e) || e.Particles.Count >= MaxParticles)
            return -1;
        e.Particles.Add((material, position, default, default));
        return e.Particles.Count - 1;
    }

    public void SetSimple(int emitter, int particle, in FxSimpleParticle fields)
    {
        if (_open.TryGetValue(emitter, out var e) && e.Kind != FxEmitterKind.Trail && particle >= 0 && particle < e.Particles.Count)
            e.Particles[particle] = e.Particles[particle] with { Simple = fields };
    }

    public void SetTrail(int emitter, int particle, in FxTrailParticle fields)
    {
        if (_open.TryGetValue(emitter, out var e) && e.Kind == FxEmitterKind.Trail && particle >= 0 && particle < e.Particles.Count)
            e.Particles[particle] = e.Particles[particle] with { Trail = fields };
    }

    public void Release(int emitter)
    {
        if (!_open.Remove(emitter, out var e))
            return;
        Emitters++;
        foreach (IClientParticleEffect effect in Build(e))
        {
            Vector origin = e.SortOrigin ?? (e.Box is { } b ? (b.Mins + b.Maxs) * 0.5f : e.Particles[0].Position);
            int id = effects.Start(effect, origin);
            if (id == 0)
                continue;
            Effects++;
            if (effect is SimpleSheet sheet)
                sheet.EffectId = id;
            else if (effect is TrailEmitter trail)
                trail.EffectId = id;

            ParticleSpace space = e.BeforeViewModel ? ParticleSpace.BeforeViewModel : ParticleSpace.World;
            if (e.Kind == FxEmitterKind.LocalSpace)
                space |= ParticleSpace.Attachment | ((e.Flags & FleViewModel) != 0 ? ParticleSpace.ViewModel : 0);
            if (space != ParticleSpace.World)
                effects.SetSpace(id, e.Handle, e.Attachment, space);
        }
    }

    /// <summary>One effect per material, its particles in the order they were added.</summary>
    public List<IClientParticleEffect> Build(int emitter) => _open.TryGetValue(emitter, out var e) ? Build(e) : [];

    private List<IClientParticleEffect> Build(Open e)
    {
        var made = new List<IClientParticleEffect>();
        foreach (var group in e.Particles.GroupBy(p => p.Material))
        {
            if (e.Kind == FxEmitterKind.Trail)
            {
                var trail = new TrailEmitter(group.Key, views, world, _random) { Flags = e.TrailFlags };
                trail.Collision.Gravity = e.Gravity;
                // A local-space box is the executor's to replace; a world one is the binding's, as stock's SetBBox.
                trail.Box = e.Box;
                foreach (var (_, position, _, t) in group)
                {
                    trail.Add(new TrailParticle
                    {
                        Position = position, Lifetime = t.Lifetime, DieTime = t.DieTime, Velocity = t.Velocity,
                        R = t.R, G = t.G, B = t.B, A = t.A, Length = t.Length, Width = t.Width,
                    });
                }
                made.Add(trail);
                continue;
            }

            SimpleEmitterRules? rules = e.Kind == FxEmitterKind.Ember ? new EmberRules() : null;
            var sheet = new SimpleSheet(group.Key, _random, views, visibility, () => host.CurrentTime, rules: rules)
            {
                Box = e.Kind == FxEmitterKind.LocalSpace ? null : e.Box,
            };
            foreach (var (_, position, f, _) in group)
            {
                sheet.Add(new SimpleParticle
                {
                    Position = position, Lifetime = f.Lifetime, DieTime = f.DieTime, Velocity = f.Velocity, R = f.R, G = f.G, B = f.B,
                    StartAlpha = f.StartAlpha, EndAlpha = f.EndAlpha, StartSize = f.StartSize, EndSize = f.EndSize, Roll = f.Roll,
                    RollDelta = f.RollDelta,
                });
            }
            made.Add(sheet);
        }
        return made;
    }
}
