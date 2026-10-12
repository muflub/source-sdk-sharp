namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>IPhysicsCollisionEvent</c>, in Valve's layout and written by
/// managed (plan_physics_managed B2.2). Ten slots, called from inside the
/// step, all answered by the side's binding; on the server PostSimulationFrame's one native duty, draining
/// the event ring, is an outbound call (plan_physics_bones_native_zero 2.1).
/// </summary>
[ManagedVtable(typeof(PhysicsCollisionEventVtbl))]
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
