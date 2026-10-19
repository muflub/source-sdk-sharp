using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// vphysics' <c>IPhysicsObjectEvent</c>'s managed side (plan_physics_managed B0b,
/// B2.1): libss_fwd builds the object vphysics calls (PhysicsCallbackObjects),
/// whose overrides call this table. The environment calls it from
/// inside its own step, so each call only names the owner and queues the
/// event; the router delivers it after the step, as the native ring did.
/// </summary>
[ManagedVtable(typeof(ManagedPhysicsObjectEventTable))]
public unsafe interface IPhysicsObjectEventSink
{
    void ObjectWake(void* pObject);

    void ObjectSleep(void* pObject);
}

/// <summary>
/// The sink: the physics object's owner, from its game data, and the event
/// handed on. An object with no managed owner is dropped, as the ring drops
/// SS_PHYSEVENT_NO_ENTITY.
/// </summary>
public sealed unsafe class PhysicsObjectEventBinding(Crossings crossings, Func<nint, nint> gameData,
                                                     Action<PhysicsEvent> post) : IPhysicsObjectEventSink
{
    public void ObjectWake(void* pObject)
    {
        crossings.Count(CrossingOp.ServerPhysicsObjectWake);
        Post(PhysicsEventKind.Wake, (nint)pObject);
    }

    public void ObjectSleep(void* pObject)
    {
        crossings.Count(CrossingOp.ServerPhysicsObjectSleep);
        Post(PhysicsEventKind.Sleep, (nint)pObject);
    }

    private void Post(PhysicsEventKind kind, nint physicsObject)
    {
        if (physicsObject == 0)
            return;

        EntityHandle owner = PhysicsGameData.Owner(gameData(physicsObject));
        if (owner == EntityHandle.None)
            return;

        post(new PhysicsEvent { KindRaw = (uint)kind, Entity0 = owner.Value, Entity1 = PhysicsEvent.NoEntity });
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsObjectEventSink target)
        => IPhysicsObjectEventSink__ManagedVtable.Create(vtables, target);
}
