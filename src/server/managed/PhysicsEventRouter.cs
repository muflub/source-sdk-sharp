using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// THE PHYSICS EVENT RING, DELIVERED TO THE ENTITIES IT NAMES (physics stock
/// parity, step 3).
///
/// <para>
/// <see cref="PhysicsCollisionResponse"/> is the frame-wide consumer: it counts
/// and emits the impact response. This one hands each event to the entity it
/// is about, the way stock's IPhysicsObjectEvent and VPhysicsUpdate reach a
/// CBaseEntity: a wake becomes <see cref="ScriptedEntity.OnPhysicsWake"/>, a
/// sleep <see cref="ScriptedEntity.OnPhysicsSleep"/>, the solver's per-frame
/// update <see cref="ScriptedEntity.OnPhysicsUpdate"/>. Called once per drain, in
/// the order the solver reported.
/// </para>
/// </summary>
public sealed class PhysicsEventRouter
{
    private readonly ScriptedEntityRegistry _entities;

    private readonly PhysicsDamageQueue _impacts;

    private readonly FluidTouches _fluid;
    private readonly FrictionSounds _friction;
    private readonly PlayerShadowRecovery _shadows;
    private readonly IGameClock _clock;
    private readonly PhysicsEntitySolver _solver;

    public PhysicsEventRouter(ScriptedEntityRegistry entities, PhysicsDamageQueue impacts, FluidTouches fluid,
                              FrictionSounds friction, PlayerShadowRecovery shadows, IGameClock clock,
                              PhysicsEntitySolver solver)
    {
        _solver = solver;
        _shadows = shadows;
        _entities = entities;
        _impacts = impacts;
        _fluid = fluid;
        _friction = friction;
        _clock = clock;
    }

    /// <summary>The entity solvers for stuck debris (A9), for cs_sv_physics_events.</summary>
    public PhysicsEntitySolver Solver => _solver;

    /// <summary>A level ends: what lives per level goes with it.</summary>
    public void LevelShutdown() => _solver.Clear();

    /// <summary>The players' shadow reconciles (B7), for cs_sv_physics_events.</summary>
    public PlayerShadowRecovery Shadows => _shadows;

    /// <summary>The scrapes (D2), for cs_sv_physics_events.</summary>
    public FrictionSounds Friction => _friction;

    /// <summary>The impact and stress damage queue (C7, B5), for cs_sv_physics_events.</summary>
    public PhysicsDamageQueue Impacts => _impacts;

    /// <summary>
    /// Stress events whose hull had a moving, non-friendly pusher on it
    /// (hasNonStaticStress), for cs_sv_physics_events: B5's live reading.
    /// </summary>
    public long StressFelt { get; private set; }

    /// <summary>OnPhysicsUpdate calls delivered since the module started. For cs_sv_physics_events.</summary>
    public long UpdatesDelivered { get; private set; }

    /// <summary>
    /// The last wake or sleep each entity's object reported, by handle: what the
    /// dev API answers for "is it asleep". Recorded, not decided -- an entity
    /// whose object never changed state has no entry and reads as unknown.
    /// Keyed by handle, so a reused index cannot inherit a stale state; cleared
    /// with the level (<see cref="ForgetSleep"/>).
    /// </summary>
    private readonly Dictionary<uint, bool> _asleep = new();

    /// <summary>Whether the entity's object last reported sleeping; null when it never reported.</summary>
    public bool? IsAsleep(EntityHandle entity)
        => _asleep.TryGetValue(entity.Value, out bool asleep) ? asleep : null;

    /// <summary>Drops every recorded sleep state: the level that held those objects is going.</summary>
    public void ForgetSleep() => _asleep.Clear();

    /// <summary>Queued by Route, delivered by Flush. Reused: Frame is allocation-free.</summary>
    private readonly List<PhysicsEvent> _pending = new(256);

    /// <summary>
    /// One drain's events, in order. The drain runs inside the solver step
    /// (CCollisionEvent::PostSimulationFrame), where stock never lets an entity
    /// act -- PhysCallbackDamage queues inside a callback, VPhysicsUpdate runs in
    /// PhysFrame after the step -- so the ones an entity hears are queued here
    /// and delivered by <see cref="Flush"/>.
    /// </summary>
    /// <summary>
    /// One event from a callback managed answers itself (plan_physics_managed
    /// B2), queued with the ring's to be delivered at the next Flush.
    /// </summary>
    public void Post(PhysicsEvent ev)
    {
        _pending.Add(ev);
        Posted++;
    }

    /// <summary>Events posted by managed's own vphysics callbacks, for cs_sv_physics_events.</summary>
    public long Posted { get; private set; }

    public void Route(ReadOnlySpan<PhysicsEvent> events)
    {
        foreach (ref readonly var ev in events)
        {
            switch (ev.Kind)
            {
                case PhysicsEventKind.PostCollision:
                case PhysicsEventKind.Wake:
                case PhysicsEventKind.Sleep:
                case PhysicsEventKind.Update:
                case PhysicsEventKind.FluidStart:
                case PhysicsEventKind.FluidEnd:
                case PhysicsEventKind.Friction:
                case PhysicsEventKind.Stress:
                case PhysicsEventKind.ShadowUpdate:
                case PhysicsEventKind.EntitySolver:
                case PhysicsEventKind.ConstraintBroken:
                    _pending.Add(ev);
                    break;
            }
        }
    }

    /// <summary>
    /// Stock's PostCollision dispatch, whole: VPhysicsCollision to each side
    /// that is a managed entity, with its own index, when the solver called it
    /// a collision and the two are not both shadows; VPhysicsShadowCollision to
    /// a side that is a shadow when it called it a shadow collision. An entity
    /// hitting itself gets one non-shadow call.
    /// </summary>
    private void Collide(in PhysicsEvent ev)
    {
        if (ev.HasEntity0 && ev.Entity0 == ev.Entity1)
        {
            if (ev.IsCollision)
                _entities.PhysicsCollision(new EntityHandle(ev.Entity0), in ev, 0);
            return;
        }

        bool collision = ev.IsCollision && !(ev.IsShadow(0) && ev.IsShadow(1));

        for (int side = 0; side < 2; side++)
        {
            if (!(side == 0 ? ev.HasEntity0 : ev.HasEntity1))
                continue;

            var entity = new EntityHandle(side == 0 ? ev.Entity0 : ev.Entity1);

            if (collision)
                _entities.PhysicsCollision(entity, in ev, side);
            if (ev.IsShadowCollision && ev.IsShadow(side))
                _entities.PhysicsShadowCollision(entity, in ev, side);
        }
    }

    /// <summary>
    /// Delivers what Route queued, in order: the managed frame, which runs after
    /// PhysFrame (SourceSharpManaged is after CPhysicsHook in the server systems).
    /// </summary>
    public void Flush()
    {
        // By index: an entity's answer may cause more physics, which reaches the
        // ring and the NEXT drain, never this list.
        for (int i = 0; i < _pending.Count; i++)
        {
            PhysicsEvent ev = _pending[i];

            if (ev.Kind == PhysicsEventKind.PostCollision)
            {
                Collide(in ev);
                continue;
            }

            // CPhysConstraintEvents::ConstraintBroken: the owner hears it, and
            // either end may be the world, so before the entity-0 check (A23).
            if (ev.Kind == PhysicsEventKind.ConstraintBroken)
            {
                _entities.ConstraintBroken(new PhysicsId(ev.Object0));
                continue;
            }

            if (!ev.HasEntity0)
                continue;

            var entity = new EntityHandle(ev.Entity0);

            switch (ev.Kind)
            {
                case PhysicsEventKind.Wake:
                    _asleep[ev.Entity0] = false;
                    _entities.PhysicsWake(entity);
                    break;

                case PhysicsEventKind.Sleep:
                    _asleep[ev.Entity0] = true;
                    _entities.PhysicsSleep(entity);
                    break;

                case PhysicsEventKind.Update:
                    if (_entities.PhysicsUpdate(entity))
                        UpdatesDelivered++;
                    break;

                // EFL_TOUCHING_FLUID: FluidStart carries the object's own
                // material, which is what IsFloating reads.
                case PhysicsEventKind.FluidStart:
                    _fluid.Start(entity, (int)ev.SurfaceProps0);
                    break;

                case PhysicsEventKind.FluidEnd:
                    _fluid.End(entity);
                    break;

                // VPhysicsFriction: the scrape (D2).
                case PhysicsEventKind.Friction:
                    _friction.Friction(entity, ev.Energy, (int)ev.SurfaceProps0, (int)ev.SurfaceProps1, _clock.Now,
                                       new Vector(ev.PointX, ev.PointY, ev.PointZ),
                                       new Vector(ev.NormalX, ev.NormalY, ev.NormalZ));
                    break;

                // CBasePlayer::VPhysicsUpdate: the hull's stress (B5).
                case PhysicsEventKind.Stress:
                    if ((ev.Flags & PhysicsEvent.StressNonStatic) != 0)
                        StressFelt++;
                    _entities.PhysicsStress(entity, in ev);
                    break;

                // VPhysicsShadowUpdate: the solver's answer for a player's shadow (B7).
                case PhysicsEventKind.ShadowUpdate:
                    _shadows.Update(entity);
                    break;

                // UpdatePenetrateEvents' TRYENTITYSOLVER: stuck debris pulled apart (A9).
                case PhysicsEventKind.EntitySolver:
                    if (ev.HasEntity1)
                        _solver.Solve(entity, new EntityHandle(ev.Entity1), ev.Flags, _clock.Now);
                    break;
            }
        }
        _pending.Clear();

        // The entity solvers' think: a second on, the pair collides again.
        _solver.Update(_clock.Now);

        // UpdateFrictionSounds: a scrape not kept alive for 0.1 s stops, whether
        // or not this frame heard any friction.
        _friction.Update(_clock.Now);

        // UpdateDamageEvents, after every collision of the step has been
        // heard: the impact damage the answers queued, and the inflictors put
        // back where a hit changed its victim.
        _impacts.Deliver();
    }
}
