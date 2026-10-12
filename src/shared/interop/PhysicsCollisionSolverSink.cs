namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>IPhysicsCollisionSolver</c>, in Valve's layout and written by
/// managed: five slots asked from inside the step. One layout
/// (PhysicsCollisionSolverVtbl) and one sink for both sides: the server's
/// binding answers through <c>IPhysicsSolverAnswers</c>
/// (plan_physics_bones_native_zero 2.1), the client's through
/// <c>IClientCollisionSolverDecisions</c> (plan_client_novalve C6, cluster 9).
/// </summary>
[ManagedVtable(typeof(PhysicsCollisionSolverVtbl))]
public unsafe interface IPhysicsCollisionSolverSink
{
    int ShouldCollide(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1);

    int ShouldSolvePenetration(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1, float dt);

    byte ShouldFreezeObject(void* pObject);

    int AdditionalCollisionChecksThisTick(int currentChecksDone);

    byte ShouldFreezeContacts(void* pObjectList, int objectCount);
}
