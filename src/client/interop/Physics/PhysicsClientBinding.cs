using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What game/client/physics.cpp asks of physics_shared.cpp's client half, through
/// the native forwarders that stay while that fork file links them
/// (plan_physics_bones_native_zero 2.6).
/// </summary>
public interface IClientPhysicsWorld
{
    /// <summary><c>PhysCreateWorld_Shared</c>: the level's world objects; the world's object, or 0.</summary>
    nint CreateWorld();

    /// <summary><c>PhysParseSurfaceData</c>: the manifest's files handed to vphysics; the count parsed.</summary>
    int ParseSurfaceData();

    /// <summary><c>PrecachePhysicsSounds</c>: every surface's sound handles written back; the count precached.</summary>
    int PrecacheSurfaceSounds();

    /// <summary>The solid key handler (stock's <c>g_SolidSetup</c>) the static props' parse is answered by.</summary>
    SolidKeyDefaults? Solids { get; }
}

/// <summary>
/// <see cref="IManagedPhysicsClient"/>, managed-built (plan_physics_bones_native_zero
/// 2.6): each call counted and handed to the client's physics world, or
/// answered 0 / nothing when none is composed. A throw is logged and answers
/// 0: the engine is in the middle of its own start-up or level load.
/// </summary>
public sealed unsafe class PhysicsClientBinding(Crossings crossings, ILog log, Func<IClientPhysicsWorld?> world)
    : IManagedPhysicsClient
{
    public void* CreateWorld()
    {
        crossings.Count(CrossingOp.ClientPhysicsCreateWorld);

        try
        {
            nint made = world()?.CreateWorld() ?? 0;
            if (made == 0)
                log.Warning("[SourceSharp/Client] the level has no world physics object\n");
            return (void*)made;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the world's physics build threw: {e.Message}\n");
            return null;
        }
    }

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

    public int PrecacheSurfaceSounds()
    {
        crossings.Count(CrossingOp.ClientPhysicsPrecacheSurfaceSounds);

        try
        {
            return world()?.PrecacheSurfaceSounds() ?? -1;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the surface sounds' precache threw: {e.Message}\n");
            return -1;
        }
    }

    // g_SolidSetup's two slots, forwarded by the native object fork
    // game/client/physics.cpp hands the static prop manager.
    public void SolidKeyValue(void* a0, byte* a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientSolidKeyValue);
        world()?.Solids?.ParseKeyValue(a1, a2);
    }

    public void SolidDefaults(void* a0)
    {
        crossings.Count(CrossingOp.ClientSolidDefaults);
        world()?.Solids?.SetDefaults(a0);
    }
}
