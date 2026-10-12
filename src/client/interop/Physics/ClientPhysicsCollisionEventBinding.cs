namespace SourceSharp.Abi;

/// <summary>The client's impact work, answered inside the step (ClientImpactSounds).</summary>
public interface IClientImpactSink
{
    void PostCollision(in VCollisionEvent ev);
}

/// <summary>The client's scrapes (ClientFriction).</summary>
public interface IClientFrictionSink
{
    void Friction(nint physicsObject, float energy, int surfaceProps, int surfacePropsHit, nint collisionData);
}

/// <summary>The client's fluid entries (ClientFluidEvents).</summary>
public interface IClientFluidSink
{
    void FluidStart(nint physicsObject, nint fluid);
}

/// <summary>
/// The client's IPhysicsCollisionEvent, written by managed (plan_client_novalve
/// C6, cluster 9; lane c6-input), the server's B2.2 shape on the client's own
/// environment. A slot not taken over forwards unchanged to native's handler
/// (game/client/physics.cpp's g_Collisions) through that handler's vtable.
/// </summary>
public sealed unsafe class ClientPhysicsCollisionEventBinding(Crossings crossings, nint native, IClientImpactSink impacts,
                                                              IClientFluidSink fluids, IClientFrictionSink friction)
    : IPhysicsCollisionEventSink
{
    private PhysicsCollisionEventVtbl* Native => *(PhysicsCollisionEventVtbl**)native;

    public void PreCollision(void* pEvent)
    {
        crossings.Count(CrossingOp.ClientPhysicsPreCollision);
        Native->PreCollision((void*)native, pEvent);
    }

    // TAKEN OVER: the impact sounds (ClientImpactSounds), answered in the step.
    // The event is read in place; it does not outlive the call.
    public void PostCollision(void* pEvent)
    {
        crossings.Count(CrossingOp.ClientPhysicsPostCollision);
        if (pEvent != null)
            impacts.PostCollision(in *(VCollisionEvent*)pEvent);
    }

    // TAKEN OVER: the scrape (ClientFriction over the shared FrictionSounds).
    public void Friction(void* pObject, float energy, int surfaceProps, int surfacePropsHit, void* pData)
    {
        crossings.Count(CrossingOp.ClientPhysicsFriction);
        friction.Friction((nint)pObject, energy, surfaceProps, surfacePropsHit, (nint)pData);
    }

    public void StartTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ClientPhysicsStartTouch);
        Native->StartTouch((void*)native, pObject1, pObject2, pTouchData);
    }

    public void EndTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ClientPhysicsEndTouch);
        Native->EndTouch((void*)native, pObject1, pObject2, pTouchData);
    }

    // TAKEN OVER: the splash (ClientFluidEvents), answered in the step.
    public void FluidStartTouch(void* pObject, void* pFluid)
    {
        crossings.Count(CrossingOp.ClientPhysicsFluidStartTouch);
        fluids.FluidStart((nint)pObject, (nint)pFluid);
    }

    // TAKEN OVER: stock's client body is "FIXME: Do nothing for now".
    public void FluidEndTouch(void* pObject, void* pFluid)
        => crossings.Count(CrossingOp.ClientPhysicsFluidEndTouch);

    public void PostSimulationFrame()
    {
        crossings.Count(CrossingOp.ClientPhysicsPostSimulationFrame);
        Native->PostSimulationFrame((void*)native);
    }

    public void ObjectEnterTrigger(void* pTrigger, void* pObject)
    {
        crossings.Count(CrossingOp.ClientPhysicsObjectEnterTrigger);
        Native->ObjectEnterTrigger((void*)native, pTrigger, pObject);
    }

    public void ObjectLeaveTrigger(void* pTrigger, void* pObject)
    {
        crossings.Count(CrossingOp.ClientPhysicsObjectLeaveTrigger);
        Native->ObjectLeaveTrigger((void*)native, pTrigger, pObject);
    }

    /// <summary>The native-callable table over <paramref name="target"/>, in the side's registry.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsCollisionEventSink target)
        => IPhysicsCollisionEventSink__ManagedVtable.Create(vtables, target);
}
