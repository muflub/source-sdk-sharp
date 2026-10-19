using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// physics_shared.cpp's client half (plan_physics_bones_native_zero 2.6), as
/// the client's physics system asks it: the world, the static props, the
/// manifest (native's PhysicsDLLInit asks that one) and the precache.
/// </summary>
public interface IClientPhysicsWorld
{
    /// <summary><c>PhysCreateWorld_Shared</c>: the level's world objects; the world's object, or 0.</summary>
    nint CreateWorld();

    /// <summary>
    /// <c>staticpropmgr-&gt;CreateVPhysicsRepresentations( physenv, &amp;g_SolidSetup, NULL )</c>:
    /// the static props' objects, made after the world's.
    /// </summary>
    void CreateStaticProps();

    /// <summary><c>PhysParseSurfaceData</c>: the manifest's files handed to vphysics; the count parsed.</summary>
    int ParseSurfaceData();

    /// <summary><c>PrecachePhysicsSounds</c>: every surface's sound handles written back; the count precached.</summary>
    int PrecacheSurfaceSounds();

}

/// <summary>
/// The client physics system's level and frame events, as native's client
/// system row and its PhysicsSimulate / PhysicsReset forward them.
/// </summary>
public interface IClientPhysicsFrame
{
    void LevelInitPreEntity();
    void LevelInitPostEntity();
    void LevelShutdownPreEntity();
    void LevelShutdownPostEntity();
    void Simulate();
    void Reset();
}

/// <summary>
/// <see cref="IManagedPhysicsClient"/>, managed-built (plan_physics_bones_native_zero
/// 2.6): each call counted and handed to the client's physics world, or
/// answered 0 / nothing when none is composed. A throw is logged and answers
/// 0: the engine is in the middle of its own start-up or level load.
/// </summary>
public sealed unsafe class PhysicsClientBinding(Crossings crossings, ILog log, Func<IClientPhysicsWorld?> world,
                                                Func<IClientPhysicsFrame?> frame)
    : IManagedPhysicsClient
{
    public int ParseSurfaceData()
    {
        crossings.Count(CrossingOp.ClientPhysicsParseSurfaceData);

        try
        {
            return world()?.ParseSurfaceData() ?? -1;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the surface property manifest threw: {e.Message}\n");
            return -1;
        }
    }

    // The physics system's events: each counted, each a throw logged and
    // swallowed (native is inside the engine's level load or frame).
    public void LevelInitPreEntity()
    {
        crossings.Count(CrossingOp.ClientPhysicsLevelInitPreEntity);
        Run("level init (pre-entity)", static f => f.LevelInitPreEntity());
    }

    public void LevelInitPostEntity()
    {
        crossings.Count(CrossingOp.ClientPhysicsLevelInitPostEntity);
        Run("level init (post-entity)", static f => f.LevelInitPostEntity());
    }

    public void LevelShutdownPreEntity()
    {
        crossings.Count(CrossingOp.ClientPhysicsLevelShutdownPreEntity);
        Run("level shutdown (pre-entity)", static f => f.LevelShutdownPreEntity());
    }

    public void LevelShutdownPostEntity()
    {
        crossings.Count(CrossingOp.ClientPhysicsLevelShutdownPostEntity);
        Run("level shutdown (post-entity)", static f => f.LevelShutdownPostEntity());
    }

    public void Simulate()
    {
        crossings.Count(CrossingOp.ClientPhysicsSimulate);
        Run("step", static f => f.Simulate());
    }

    public void Reset()
    {
        crossings.Count(CrossingOp.ClientPhysicsReset);
        Run("reset", static f => f.Reset());
    }

    private void Run(string what, Action<IClientPhysicsFrame> call)
    {
        try
        {
            if (frame() is { } f)
                call(f);
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the physics {what} threw: {e.Message}\n");
        }
    }
}
