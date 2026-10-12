namespace SourceSharp.Abi;

/// <summary>The client's solver decisions (ClientCollisionSolver).</summary>
public interface IClientCollisionSolverDecisions
{
    int ShouldCollide(nint obj0, nint obj1, nint gameData0, nint gameData1);

    int ShouldSolvePenetration(nint obj0, nint obj1, nint gameData0, nint gameData1, float dt);

    bool ShouldFreezeObject(nint obj);

    int AdditionalCollisionChecksThisTick(int currentChecksDone);

    bool ShouldFreezeContacts(int objectCount);
}

/// <summary>
/// The client's solver table: every slot answered by managed's decisions,
/// each one counted. Stock's client solver is game/client/physics.cpp's
/// g_Collisions; nothing is forwarded to it, because every one of its five
/// answers is ported (ClientCollisionSolver).
/// </summary>
public sealed unsafe class ClientCollisionSolverBinding(Crossings crossings, IClientCollisionSolverDecisions decisions)
    : IPhysicsCollisionSolverSink
{
    public int ShouldCollide(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1)
    {
        crossings.Count(CrossingOp.ClientPhysicsShouldCollide);
        return decisions.ShouldCollide((nint)pObj0, (nint)pObj1, (nint)pGameData0, (nint)pGameData1);
    }

    public int ShouldSolvePenetration(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1, float dt)
    {
        crossings.Count(CrossingOp.ClientPhysicsShouldSolvePenetration);
        return decisions.ShouldSolvePenetration((nint)pObj0, (nint)pObj1, (nint)pGameData0, (nint)pGameData1, dt);
    }

    public byte ShouldFreezeObject(void* pObject)
    {
        crossings.Count(CrossingOp.ClientPhysicsShouldFreezeObject);
        return decisions.ShouldFreezeObject((nint)pObject) ? (byte)1 : (byte)0;
    }

    public int AdditionalCollisionChecksThisTick(int currentChecksDone)
    {
        crossings.Count(CrossingOp.ClientPhysicsAdditionalCollisionChecks);
        return decisions.AdditionalCollisionChecksThisTick(currentChecksDone);
    }

    public byte ShouldFreezeContacts(void* pObjectList, int objectCount)
    {
        crossings.Count(CrossingOp.ClientPhysicsShouldFreezeContacts);
        return decisions.ShouldFreezeContacts(objectCount) ? (byte)1 : (byte)0;
    }

    /// <summary>The native-callable table over <paramref name="target"/>, in the side's registry.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsCollisionSolverSink target)
        => IPhysicsCollisionSolverSink__ManagedVtable.Create(vtables, target);
}
