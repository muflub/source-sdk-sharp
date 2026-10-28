//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// What the entity solver asks of the world: the pair hash, the collision
/// group and a contact recheck. One adapter over the engine
/// (<see cref="EngineEntitySolverWorld"/>) and a hand fake in the facts.
/// </summary>
public interface IEntitySolverWorld
{
    /// <summary>Stock's <c>PhysEntityCollisionsAreDisabled</c>.</summary>
    bool PairDisabled(EntityHandle a, EntityHandle b);

    /// <summary>Stock's <c>PhysEnableEntityCollisions</c> / <c>PhysDisableEntityCollisions</c>.</summary>
    void SetPairEnabled(EntityHandle a, EntityHandle b, bool enabled);

    CollisionGroup GetGroup(EntityHandle entity);

    void SetGroup(EntityHandle entity, CollisionGroup group);

    /// <summary><c>IPhysicsObject::RecheckContactPoints</c> on the entity's object, if it has one.</summary>
    void RecheckContactPoints(EntityHandle entity);

    bool Exists(EntityHandle entity);
}

/// <summary>
/// Debris stuck in a non-static object, pulled apart: stock's
/// <c>CPhysicsEntitySolver</c> (physics_npc_solver.cpp:365-467), which
/// <c>UpdatePenetrateEvents</c> makes for a pair the penetration ledger marked
/// <c>COLLSTATE_TRYENTITYSOLVER</c> (physics.cpp:888-902).
///
/// <para>
/// For <see cref="SeparationSeconds"/> the pair does not collide and the debris
/// is in <see cref="CollisionGroup.Debris"/>, with its contacts rechecked, so
/// the solver lets the two drift apart; then the pair collides again and the
/// debris gets its group back. Stock does this with a logical entity whose one
/// think removes it; here it is a per-level list, ticked after each step, with
/// the same two halves (<c>Spawn</c>, <c>UpdateOnRemove</c>).
/// </para>
///
/// <para>
/// <b>WHICH IS THE BLOCKER.</b> Stock swaps the pair so the second is the debris
/// that moves by physics: <c>if ( !IsDebris( e1 ) || e1-&gt;GetMoveType() !=
/// MOVETYPE_VPHYSICS ) swap</c>. Native records that fact per side
/// (<see cref="PhysicsEvent.SolverDebris0"/>, <see cref="PhysicsEvent.SolverDebris1"/>);
/// this class applies stock's rule to it.
/// </para>
///
/// <para>
/// <b>ACROSS A RELOAD</b> the list is carried (<see cref="ISaveCustom"/>, the
/// LifecycleDriver tier), as stock's DATADESC carries every field of the
/// solver entity (physics_npc_solver.cpp:400-406). The native halves outlive
/// the managed module: the pair stays disabled in the engine's pair hash and
/// the blocker stays in the debris group. An empty list after a reload never
/// ran <c>UpdateOnRemove</c>, so the debris group stuck and the pair never
/// collided again. The think time crosses as seconds remaining, which is
/// stock's FIELD_TIME (m_cancelTime and the think tick are both stored
/// relative to the save). The handles are re-checked on the way in, and a
/// gone one is left to <see cref="Update"/>, which is stock's restore too:
/// a dead EHANDLE comes back NULL and <c>UpdateOnRemove</c> tests it.
/// </para>
/// </summary>
public sealed class PhysicsEntitySolver : IGameLifecycle, ISaveCustom
{
    /// <summary>EntityPhysics_CreateSolver's separationDuration from UpdatePenetrateEvents.</summary>
    public const float SeparationSeconds = 1.0f;

    private readonly IEntitySolverWorld _world;

    /// <summary>What "now" is when the list is saved and restored. Solve and Update are handed theirs.</summary>
    private readonly IGameClock _clock;

    /// <summary>One CPhysicsEntitySolver: m_hMovingEntity, m_hPhysicsBlocker, the think time, m_savedCollisionGroup.</summary>
    private readonly record struct Solver(EntityHandle Moving, EntityHandle Blocker, float RemoveAt, CollisionGroup SavedGroup);

    private readonly List<Solver> _solvers = new();

    public PhysicsEntitySolver(IEntitySolverWorld world, IGameClock clock)
    {
        _world = world;
        _clock = clock;
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsSolvers;

    /// <summary>Solvers alive now. For cs_sv_physics_events and the facts.</summary>
    public int Active => _solvers.Count;

    /// <summary>Solvers made since the module started. For cs_sv_physics_events.</summary>
    public long Created { get; private set; }

    /// <summary>
    /// <c>EntityPhysics_CreateSolver</c> for one ledgered pair, with the flags
    /// native recorded: which side is debris moved by physics.
    /// </summary>
    public void Solve(EntityHandle entity0, EntityHandle entity1, uint flags, float now)
    {
        // UpdatePenetrateEvents: the second entity is the debris that moves by
        // physics, so swap when it is not.
        var (moving, blocker) = (flags & PhysicsEvent.SolverDebris1) != 0
            ? (entity0, entity1)
            : (entity1, entity0);

        Create(moving, blocker, SeparationSeconds, now);
    }

    /// <summary>
    /// <c>EntityPhysics_CreateSolver( moving, blocker, true, separation )</c>:
    /// none for a pair already not colliding; otherwise the pair stops
    /// colliding and the blocker is debris until <paramref name="separationSeconds"/>
    /// have passed (CPhysicsEntitySolver::Spawn). A door that crushes no one
    /// asks for one with 4 s (CBaseDoor::Blocked).
    /// </summary>
    public void Create(EntityHandle moving, EntityHandle blocker, float separationSeconds, float now)
    {
        if (_world.PairDisabled(moving, blocker))
            return;

        // CPhysicsEntitySolver::Spawn.
        _world.SetPairEnabled(moving, blocker, enabled: false);
        CollisionGroup saved = _world.GetGroup(blocker);
        _world.SetGroup(blocker, CollisionGroup.Debris);
        _world.RecheckContactPoints(blocker);

        _solvers.Add(new Solver(moving, blocker, now + separationSeconds, saved));
        Created++;
    }

    /// <summary>Each solver whose time is up does its <c>UpdateOnRemove</c>.</summary>
    public void Update(float now)
    {
        for (int i = _solvers.Count - 1; i >= 0; i--)
        {
            Solver s = _solvers[i];
            if (now < s.RemoveAt)
                continue;

            _solvers.RemoveAt(i);

            bool blocker = _world.Exists(s.Blocker);
            if (blocker && _world.Exists(s.Moving))
                _world.SetPairEnabled(s.Moving, s.Blocker, enabled: true);
            if (blocker)
                _world.SetGroup(s.Blocker, s.SavedGroup);
        }
    }

    /// <summary>A level ends: stock's solvers go with the level's entities.</summary>
    public void Clear() => _solvers.Clear();

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        float now = _clock.Now;

        foreach (Solver s in _solvers)
        {
            writer.AddRow()
                  .Set("moving", s.Moving)
                  .Set("blocker", s.Blocker)
                  .Set("remaining", s.RemoveAt - now)
                  .Set("group", (int)s.SavedGroup);
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        float now = _clock.Now;
        _solvers.Clear();

        foreach (ReloadRow row in reader.Rows)
        {
            _solvers.Add(new Solver(reader.Resolve(row.GetHandle("moving")),
                                    reader.Resolve(row.GetHandle("blocker")),
                                    now + row.GetFloat("remaining"),
                                    (CollisionGroup)row.GetInt("group")));
        }
    }
}

/// <summary>The engine, for <see cref="IEntitySolverWorld"/>. Every member is one call.</summary>
public sealed class EngineEntitySolverWorld : IEntitySolverWorld
{
    private readonly IEngineEntityPhysics _physics;
    private readonly IEngineEntities _entities;
    private readonly IVPhysicsObjects _objects;
    private readonly EntityPairCollisions _pairs;

    public EngineEntitySolverWorld(IEngineEntityPhysics physics, IEngineEntities entities, IVPhysicsObjects objects,
                                   EntityPairCollisions pairs)
    {
        _physics = physics;
        _entities = entities;
        _objects = objects;
        _pairs = pairs;
    }

    public bool PairDisabled(EntityHandle a, EntityHandle b) => _pairs.AreDisabled(a, b);

    public void SetPairEnabled(EntityHandle a, EntityHandle b, bool enabled) => _pairs.SetEnabled(a, b, enabled);

    public CollisionGroup GetGroup(EntityHandle entity) => _physics.GetCollisionGroup(entity);

    public void SetGroup(EntityHandle entity, CollisionGroup group) => _physics.SetCollisionGroup(entity, group);

    // Through the generated vtable, directly (plan_physics_managed).
    public void RecheckContactPoints(EntityHandle entity) => _objects.RecheckContactPoints(_physics.PhysicsObject(entity));

    public bool Exists(EntityHandle entity) => _entities.IsValid(entity);
}
