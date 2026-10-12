using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// vphysics' IPhysicsCollisionEvent, every slot answered by managed.
/// </summary>
public sealed unsafe class PhysicsCollisionEventBinding(Crossings crossings, IVPhysicsObjects objects,
                                                        SolverHeldOverlaps solverHeld, Action<PhysicsEvent> post,
                                                        IPhysicsFluidSink fluids, IPhysicsCollisionSink impacts)
    : IPhysicsCollisionEventSink
{
    // TAKEN OVER (B2.2): the impact, answered in the step by the server's
    // PhysicsCollisionImpacts: the held-object rule and the velocities before
    // at Pre, the POST_COLLISION record at Post. The event is read in place;
    // it does not outlive the call.
    public void PreCollision(void* pEvent)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionPreCollision);
        if (pEvent != null)
            impacts.PreCollision(in *(VCollisionEvent*)pEvent);
    }

    public void PostCollision(void* pEvent)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionPostCollision);
        if (pEvent != null)
            impacts.PostCollision(in *(VCollisionEvent*)pEvent);
    }

    // TAKEN OVER (B2.2): the scrape. Native recorded FRICTION with the contact
    // point and the object's velocity there, then threw PhysFrictionEffect
    // unthrottled; the router now hands both to FrictionSounds, which plays
    // the sound and the effect inside stock's 500 ms MP throttle, as stock's
    // CCollisionEvent::Friction did. An object with no managed owner reported
    // nothing natively either (the ring drops it).
    public void Friction(void* pObject, float energy, int surfaceProps, int surfacePropsHit, void* pData)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionFriction);
        if (pObject == null || pData == null)
            return;

        EntityHandle owner = PhysicsGameData.Owner(objects.GameData((nint)pObject));
        if (owner == EntityHandle.None)
            return;

        Vector point = objects.ContactPoint((nint)pData);
        Vector velocity = objects.VelocityAtPoint((nint)pObject, point);
        post(new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.Friction,
            Entity0 = owner.Value,
            Entity1 = PhysicsEvent.NoEntity,
            PointX = point.X, PointY = point.Y, PointZ = point.Z,
            NormalX = velocity.X, NormalY = velocity.Y, NormalZ = velocity.Z,
            Speed = velocity.Length,
            Energy = energy,
            SurfaceProps0 = (uint)surfaceProps,
            SurfaceProps1 = (uint)surfacePropsHit,
        });
    }

    // TAKEN OVER (B2.2): contact touch, the same held pair as a trigger's
    // (physics stock parity E1: native's DispatchStartTouch made each owner
    // StartTouch the other, which appended the pair to g_SolverHeld). Its
    // START/END_TOUCH ring records only fed cs_sv_physics_events' counts.
    public void StartTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionStartTouch);
        if (Owners(pObject1, pObject2) is var (first, second))
            solverHeld.Add(first, second);
    }

    // The pair stays held while the two still touch at another contact:
    // stock's CountPhysicsObjectEntityContacts( pObject1, owner2 ) > 1.
    public void EndTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionEndTouch);
        if (Owners(pObject1, pObject2) is not var (first, second))
            return;

        if (objects.ContactsWith((nint)pObject1, second) > 1)
            return;

        solverHeld.Remove(first, second);
    }

    // TAKEN OVER (B2.2): water, answered in the step by the server's
    // PhysicsFluidEvents (the splash and the entry drag land mid-step, as
    // native's did).
    public void FluidStartTouch(void* pObject, void* pFluid)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionFluidStartTouch);
        fluids.FluidStart((nint)pObject, (nint)pFluid);
    }

    public void FluidEndTouch(void* pObject, void* pFluid)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionFluidEndTouch);
        fluids.FluidEnd((nint)pObject, (nint)pFluid);
    }

    // TAKEN OVER (2.1): native's body was UpdateDamageEvents (the step's
    // damage is managed's PhysicsDamageQueue, delivered at the router's
    // flush), the post-simulation queue and the remove list (no producer
    // left), and the ring's drain (2.3: the ring went with its last
    // producer, PhysFrame, which is managed's PhysicsFrame).
    public void PostSimulationFrame()
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionPostSimulationFrame);
    }

    // TAKEN OVER (B2.2): a physics trigger and what entered it are held open,
    // reported every frame into the touch diff until the solver says they
    // parted. Native's CCollisionEvent::ObjectEnterTrigger called the trigger's
    // StartTouch, whose whole effect was that append to g_SolverHeld; its
    // ENTER_TRIGGER ring record had no consumer, and m_currentTriggerEvent no
    // reader.
    public void ObjectEnterTrigger(void* pTrigger, void* pObject)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionObjectEnterTrigger);
        if (Owners(pTrigger, pObject) is var (trigger, entity))
            solverHeld.Add(trigger, entity);
    }

    public void ObjectLeaveTrigger(void* pTrigger, void* pObject)
    {
        crossings.Count(CrossingOp.ServerPhysicsCollisionObjectLeaveTrigger);
        if (Owners(pTrigger, pObject) is var (trigger, entity))
            solverHeld.Remove(trigger, entity);
    }

    /// <summary>Both objects' managed owners, or null when either has none (native's IsValid pair).</summary>
    private (EntityHandle, EntityHandle)? Owners(void* a, void* b)
    {
        if (a == null || b == null)
            return null;

        EntityHandle first = PhysicsGameData.Owner(objects.GameData((nint)a));
        EntityHandle second = PhysicsGameData.Owner(objects.GameData((nint)b));
        return first == EntityHandle.None || second == EntityHandle.None ? null : (first, second);
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsCollisionEventSink target)
        => IPhysicsCollisionEventSink__ManagedVtable.Create(vtables, target);
}

/// <summary>
/// The fluid slots' work, answered synchronously inside the step by the
/// server (plan_physics_managed B2.2): the touching-fluid events, the splash
/// and the entry drag, from the object and fluid controller vphysics hands.
/// </summary>
public interface IPhysicsFluidSink
{
    void FluidStart(nint physicsObject, nint fluid);

    void FluidEnd(nint physicsObject, nint fluid);
}

/// <summary>
/// The impact slots' work, answered synchronously inside the step by the
/// server (plan_physics_managed B2.2): PreCollision's held-object rule and the
/// velocities before, and PostCollision's impact record.
/// </summary>
public interface IPhysicsCollisionSink
{
    void PreCollision(in VCollisionEvent ev);

    void PostCollision(in VCollisionEvent ev);
}
