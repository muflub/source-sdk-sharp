using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// Puts managed's own vphysics callback tables on each level's environment
/// (plan_physics_managed B2). At the level's pre-entity init, which runs after
/// CPhysicsHook's row has created the environment, so its first step already
/// calls managed; checked again after every drain as a backstop.
///
/// <para>
/// Keyed by the environment's address AND reset at level shutdown: the next
/// level's environment can be allocated at the address the last one had, and
/// a handler kept on an address alone would never be installed on it.
/// </para>
/// </summary>
public sealed class PhysicsCallbacks(IEngineEntityPhysics physics, IVPhysicsEnvironment environment) : IGameLifecycle
{
    private nint _installedOn;

    /// <summary>The object-event table, once the side registry has built it (BuildManagedRoot).</summary>
    public nint ObjectEvents { get; set; }

    /// <summary>The collision-event table (B2.2), the same; 0 leaves native's handler in place.</summary>
    public nint CollisionEvents { get; set; }

    /// <summary>The collision-solver table (plan_physics_bones_native_zero 2.1); 0 leaves native's in place.</summary>
    public nint CollisionSolver { get; set; }

    /// <summary>The constraint-event table (plan_physics_bones_native_zero 2.2); 0 leaves native's in place.</summary>
    public nint ConstraintEvents { get; set; }

    /// <summary>How many environments the object-event table was installed on.</summary>
    public long Installs { get; private set; }

    public void EnsureInstalled()
    {
        if (ObjectEvents == 0)
            return;

        nint env = physics.EnvironmentPointer();
        if (env == 0 || env == _installedOn)
            return;

        environment.SetObjectEventHandler(env, ObjectEvents);
        if (CollisionEvents != 0)
            environment.SetCollisionEventHandler(env, CollisionEvents);
        if (CollisionSolver != 0)
            environment.SetCollisionSolver(env, CollisionSolver);
        if (ConstraintEvents != 0)
            environment.SetConstraintEventHandler(env, ConstraintEvents);
        _installedOn = env;
        Installs++;
    }

    public void LevelShutdown() => _installedOn = 0;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsCallbacks;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName) => EnsureInstalled();

    void IGameLifecycle.OnLevelShutdown() => LevelShutdown();
}
