using System;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// What managed's joint tables read of the world that is not a vphysics call
/// (plan_physics_bones_native_zero 2.4): the level's environment, the world's
/// own object, an entity's object, and the three numbers native read from
/// gpGlobals and sv_gravity. An interface so the tables' facts can stand a
/// world up without an engine.
/// </summary>
public interface IPhysicsJointWorld
{
    /// <summary>The level's <c>IPhysicsEnvironment</c>, or 0 between levels.</summary>
    nint Environment();

    /// <summary>The world's own physics object (stock's g_PhysWorldObject).</summary>
    nint WorldObject();

    /// <summary>Whether the handle names a live entity, in either hierarchy.</summary>
    bool IsValid(EntityHandle entity);

    /// <summary>The entity's physics object, or 0 when it has none.</summary>
    nint PhysicsObject(EntityHandle entity);

    /// <summary><c>IPhysicsObject::GetInertia</c>, through the shim's proxy (a by-value Vector return).</summary>
    Vector Inertia(nint physicsObject);

    /// <summary><c>gpGlobals-&gt;interval_per_tick</c>.</summary>
    float TickInterval();

    /// <summary><c>gpGlobals-&gt;curtime</c>.</summary>
    float CurTime();

    /// <summary>stock's GetCurrentGravity: sv_gravity.</summary>
    float Gravity();
}

/// <summary>The engine, for <see cref="IPhysicsJointWorld"/>. Every member is one read.</summary>
public sealed class EnginePhysicsJointWorld(IEngineEntityPhysics physics, IEngineEntities entities,
                                            IEngineEnvironment environment, IGameClock clock,
                                            Func<float> gravity) : IPhysicsJointWorld
{
    public nint Environment() => physics.EnvironmentPointer();

    public nint WorldObject() => physics.WorldObject();

    public bool IsValid(EntityHandle entity) => entities.IsValid(entity);

    public nint PhysicsObject(EntityHandle entity) => physics.PhysicsObject(entity);

    public Vector Inertia(nint physicsObject) => physics.Inertia(physicsObject);

    public float TickInterval() => environment.Globals.IntervalPerTick;

    public float CurTime() => clock.Now;

    public float Gravity() => gravity();
}
