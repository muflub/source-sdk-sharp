using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>
/// Installs the client's managed collision-event table on the client's physics
/// environment, once per environment (plan_client_novalve C6, cluster 9; lane
/// c6-input) -- the client's twin of the server's PhysicsCallbacks.
/// </summary>
public sealed class ClientPhysicsCallbacks(Func<nint> environmentPointer, IVPhysicsEnvironment environment,
                                           Action<nint, nint>? setCollisionSolver = null)
{
    /// <summary>The managed solver table's address, once built; 0 leaves native's solver in place.</summary>
    public nint CollisionSolver { get; set; }

    /// <summary>The managed table's address, once built; 0 leaves native's handler in place.</summary>
    public nint CollisionEvents { get; set; }

    /// <summary>How many environments the table was installed on.</summary>
    public long Installs { get; private set; }

    private nint _installedOn;

    /// <summary>
    /// Whether this level's environment has the table. The client module asks
    /// each frame until it does: the environment is made in CPhysicsSystem's
    /// LevelInitPostEntity, which the client systems' table runs AFTER the
    /// managed level init, so the first managed moment that can see it is the
    /// first frame of the level.
    /// </summary>
    public bool Installed => _installedOn != 0;

    /// <summary>
    /// After the client's PhysicsLevelInit made the level's environment (the
    /// client module's LevelInit runs after the client systems' post-entity
    /// init): the table goes on it, once.
    /// </summary>
    public void EnsureInstalled()
    {
        if (CollisionEvents == 0)
            return;

        nint env = environmentPointer();
        if (env == 0 || env == _installedOn)
            return;

        environment.SetCollisionEventHandler(env, CollisionEvents);
        if (CollisionSolver != 0)
            setCollisionSolver?.Invoke(env, CollisionSolver);
        _installedOn = env;
        Installs++;
    }

    /// <summary>The environment goes with the level; the next one is new even at the same address.</summary>
    public void LevelShutdown() => _installedOn = 0;
}
