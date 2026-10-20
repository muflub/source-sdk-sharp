using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's physics frame, managed-built (plan_physics_bones_native_zero
/// 2.3): CPhysicsHook::FrameUpdatePostEntityThink forwards here once per
/// server frame, and <paramref name="frame"/> decides and makes the step.
/// </summary>
public sealed unsafe class PhysicsFrameBinding(Crossings crossings, Action frame, Func<IServerPhysicsWorld?>? world)
    : IManagedPhysicsFrame
{
    public PhysicsFrameBinding(Crossings crossings, Action frame)
        : this(crossings, frame, null)
    {
    }

    public void Frame()
    {
        crossings.Count(CrossingOp.ServerPhysicsFrame);
        frame();
    }

    // CPhysicsHook::LevelInitPreEntity, after it makes the environment: 0 when
    // no world is built (no load, or no world model), and native's
    // g_PhysWorldObject is then NULL, as stock's would be.
    public void* CreateWorld()
    {
        crossings.Count(CrossingOp.ServerPhysicsCreateWorld);
        return (void*)(world?.Invoke()?.CreateWorld() ?? 0);
    }

    // The count of names precached, or -1 when nothing could be asked.
    public int PrecacheSurfaceSounds()
    {
        crossings.Count(CrossingOp.ServerPhysicsPrecacheSurfaceSounds);
        return world?.Invoke()?.PrecacheSurfaceSounds() ?? -1;
    }

}

/// <summary>
/// What native's CPhysicsHook asks of managed (plan_physics_bones_native_zero
/// 2.6): physics_shared.cpp's world build and sound precache, decided in
/// managed code. (A model's solid parse left with native's InitPhysics:
/// EntityPhysicsCreation calls ModelSolidParse itself.)
/// </summary>
public interface IServerPhysicsWorld
{
    /// <summary>The level's static props' and world's physics objects; the world's object, or 0.</summary>
    nint CreateWorld();

    /// <summary>Every surface's sounds precached and their handles written back; the count precached.</summary>
    int PrecacheSurfaceSounds();
}

/// <summary>
/// The server's <c>IVPhysicsKeyHandler</c> table (stock's <c>g_SolidSetup</c>):
/// always the solid handler, for the world's solids, the static props' and a
/// model's. One per registry, as stock's was one object.
/// </summary>
public sealed unsafe class SolidKeyHandlerBinding(Crossings crossings, SolidKeyDefaults solids) : IVPhysicsKeyHandlerSink, ISolidKeyTable
{
    public SolidKeyDefaults Solids { get; } = solids;

    /// <summary>The table vphysics is handed, once <see cref="Create"/> has built it.</summary>
    public nint Table { get; set; }

    /// <summary>Always the solid handler: the flag a shared table reads is kept and ignored.</summary>
    public bool SolidInFlight { get; set; }

    public void ParseKeyValue(void* pData, byte* pKey, byte* pValue)
    {
        crossings.Count(CrossingOp.ServerSolidKeyValue);
        Solids.ParseKeyValue(pKey, pValue);
    }

    public void SetDefaults(void* pData)
    {
        crossings.Count(CrossingOp.ServerSolidDefaults);
        Solids.SetDefaults(pData);
    }

    /// <summary>
    /// The block built over <paramref name="target"/>, whose <see cref="Table"/>
    /// it fills with the IVPhysicsKeyHandler vphysics is handed: libss_fwd_server's
    /// object, built in the block (plan_forwarders_everywhere.md phase 5).
    /// </summary>
    public static ManagedVtable Create(ManagedVtables vtables, SolidKeyHandlerBinding target, PhysicsCallbackObjects objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ManagedVtable table = IVPhysicsKeyHandlerSink__ManagedVtable.Create(vtables, target);
        target.Table = objects.KeyHandler(table);
        return table;
    }
}

/// <summary>
/// The physics frame's native half over the server's own side: the level's
/// environment, and the owners' transform update carried out.
/// </summary>
public static class EnginePhysicsFrameNative
{
    /// <summary>Over <paramref name="engine"/>'s own side.</summary>
    public static IPhysicsFrameNative ForEngine(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new SideFrameNative(engine.Api);
    }
}

internal sealed class SideFrameNative(EngineSide side) : IPhysicsFrameNative
{
    public nint Environment() => side.PhysicsEnvironmentPointer();

    public int UpdateOwners(ReadOnlySpan<nint> objects, ReadOnlySpan<float> velocities, Span<uint> owners)
        => side.PhysicsUpdateOwners(objects, velocities, owners);
}
