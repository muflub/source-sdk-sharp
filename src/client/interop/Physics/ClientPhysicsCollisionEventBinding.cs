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

/// <summary>The client's contact touches (ClientContactTouches).</summary>
public interface IClientTouchSink
{
    void StartTouch(nint object1, nint object2, nint touchData);

    void EndTouch(nint object1, nint object2, nint touchData);
}

/// <summary>The client's fluid entries (ClientFluidEvents).</summary>
public interface IClientFluidSink
{
    void FluidStart(nint physicsObject, nint fluid);
}

/// <summary>
/// The client's IPhysicsCollisionEvent, written by managed (plan_client_novalve
/// C6, cluster 9), the server's B2.2 shape on the client's own environment.
/// Every slot is answered here: fork game/client/physics.cpp's g_Collisions,
/// which the untaken slots once forwarded to, is off client.so.
/// <para>
/// Stock's client answers for the four slots no managed piece takes:
/// PreCollision, PostSimulationFrame and the two trigger slots have empty
/// bodies. StartTouch and EndTouch go to the contact touches
/// (ClientContactTouches), which name the owners and buffer the pair.
/// </para>
/// </summary>
public sealed unsafe class ClientPhysicsCollisionEventBinding(Crossings crossings, IClientImpactSink impacts,
                                                              IClientFluidSink fluids, IClientFrictionSink friction,
                                                              IClientTouchSink? touches = null)
    : IPhysicsCollisionEventSink
{
    // Stock's body is empty.
    public void PreCollision(void* pEvent) => crossings.Count(CrossingOp.ClientPhysicsPreCollision);

    // The impact sounds (ClientImpactSounds), answered in the step. The event
    // is read in place; it does not outlive the call.
    public void PostCollision(void* pEvent)
    {
        crossings.Count(CrossingOp.ClientPhysicsPostCollision);
        if (pEvent != null)
            impacts.PostCollision(in *(VCollisionEvent*)pEvent);
    }

    // The scrape (ClientFriction over the shared FrictionSounds).
    public void Friction(void* pObject, float energy, int surfaceProps, int surfacePropsHit, void* pData)
    {
        crossings.Count(CrossingOp.ClientPhysicsFriction);
        friction.Friction((nint)pObject, energy, surfaceProps, surfacePropsHit, (nint)pData);
    }

    // The contact touches (ClientContactTouches), buffered for the step's end.
    public void StartTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ClientPhysicsStartTouch);
        touches?.StartTouch((nint)pObject1, (nint)pObject2, (nint)pTouchData);
    }

    public void EndTouch(void* pObject1, void* pObject2, void* pTouchData)
    {
        crossings.Count(CrossingOp.ClientPhysicsEndTouch);
        touches?.EndTouch((nint)pObject1, (nint)pObject2, (nint)pTouchData);
    }

    // The splash (ClientFluidEvents), answered in the step.
    public void FluidStartTouch(void* pObject, void* pFluid)
    {
        crossings.Count(CrossingOp.ClientPhysicsFluidStartTouch);
        fluids.FluidStart((nint)pObject, (nint)pFluid);
    }

    // Stock's client body is "FIXME: Do nothing for now".
    public void FluidEndTouch(void* pObject, void* pFluid)
        => crossings.Count(CrossingOp.ClientPhysicsFluidEndTouch);

    // Stock's bodies are empty.
    public void PostSimulationFrame() => crossings.Count(CrossingOp.ClientPhysicsPostSimulationFrame);

    public void ObjectEnterTrigger(void* pTrigger, void* pObject) => crossings.Count(CrossingOp.ClientPhysicsObjectEnterTrigger);

    public void ObjectLeaveTrigger(void* pTrigger, void* pObject) => crossings.Count(CrossingOp.ClientPhysicsObjectLeaveTrigger);

    /// <summary>The native-callable table over <paramref name="target"/>, in the side's registry.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsCollisionEventSink target)
        => IPhysicsCollisionEventSink__ManagedVtable.Create(vtables, target);
}
