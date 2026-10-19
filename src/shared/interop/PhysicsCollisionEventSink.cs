namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>IPhysicsCollisionEvent</c>'s managed side (plan_physics_managed
/// B2.2): libss_fwd builds the object vphysics calls
/// (<see cref="PhysicsCallbackObjects"/>), and its overrides call this table,
/// which managed writes. Ten slots, called from inside the
/// step, all answered by the side's binding; on the server PostSimulationFrame's one native duty, draining
/// the event ring, is an outbound call (plan_physics_bones_native_zero 2.1).
/// </summary>
[ManagedVtable(typeof(ManagedPhysicsCollisionEventTable))]
public unsafe interface IPhysicsCollisionEventSink
{
    void PreCollision(void* pEvent);

    void PostCollision(void* pEvent);

    void Friction(void* pObject, float energy, int surfaceProps, int surfacePropsHit, void* pData);

    void StartTouch(void* pObject1, void* pObject2, void* pTouchData);

    void EndTouch(void* pObject1, void* pObject2, void* pTouchData);

    void FluidStartTouch(void* pObject, void* pFluid);

    void FluidEndTouch(void* pObject, void* pFluid);

    void PostSimulationFrame();

    void ObjectEnterTrigger(void* pTrigger, void* pObject);

    void ObjectLeaveTrigger(void* pTrigger, void* pObject);
}
