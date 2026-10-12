using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>The answers the solver slots ask of the server, synchronously, mid-step.</summary>
public interface IPhysicsSolverAnswers
{
    /// <summary>The pair, and each object's game data as vphysics hands it (its GetGameData).</summary>
    int ShouldCollide(nint object0, nint object1, nint gameData0, nint gameData1);

    /// <summary>The pair, and each object's game data as vphysics hands it.</summary>
    int ShouldSolvePenetration(nint object0, nint object1, nint gameData0, nint gameData1);

    bool ShouldFreezeObject(nint physicsObject);

    bool ShouldFreezeContacts(int objectCount);

    int AdditionalCollisionChecksThisTick(int currentChecksDone);
}

/// <summary>The sink: each slot counted, then answered by the server.</summary>
public sealed unsafe class PhysicsCollisionSolverBinding(Crossings crossings, IPhysicsSolverAnswers answers)
    : IPhysicsCollisionSolverSink
{
    // TAKEN OVER (2.1): the pair filter. The game data vphysics hands is each
    // object's GetGameData, so the owner is decoded
    // from it rather than asked for again (perf pass B1).
    public int ShouldCollide(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1)
    {
        crossings.Count(CrossingOp.ServerPhysicsSolverShouldCollide);
        return answers.ShouldCollide((nint)pObj0, (nint)pObj1, (nint)pGameData0, (nint)pGameData1);
    }

    // TAKEN OVER (2.1): the penetration ledger, and its give-up, decided here.
    public int ShouldSolvePenetration(void* pObj0, void* pObj1, void* pGameData0, void* pGameData1, float dt)
    {
        crossings.Count(CrossingOp.ServerPhysicsSolverShouldSolvePenetration);
        return answers.ShouldSolvePenetration((nint)pObj0, (nint)pObj1, (nint)pGameData0, (nint)pGameData1);
    }

    // TAKEN OVER (2.1): the freeze, and the crush it may queue.
    public byte ShouldFreezeObject(void* pObject)
    {
        crossings.Count(CrossingOp.ServerPhysicsSolverShouldFreezeObject);
        return answers.ShouldFreezeObject((nint)pObject) ? (byte)1 : (byte)0;
    }

    // TAKEN OVER (2.1): the 1200-check budget.
    public int AdditionalCollisionChecksThisTick(int currentChecksDone)
    {
        crossings.Count(CrossingOp.ServerPhysicsSolverAdditionalCollisionChecksThisTick);
        return answers.AdditionalCollisionChecksThisTick(currentChecksDone);
    }

    // TAKEN OVER (2.1): the large-friction warning; the list is not read.
    public byte ShouldFreezeContacts(void* pObjectList, int objectCount)
    {
        crossings.Count(CrossingOp.ServerPhysicsSolverShouldFreezeContacts);
        return answers.ShouldFreezeContacts(objectCount) ? (byte)1 : (byte)0;
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsCollisionSolverSink target)
        => IPhysicsCollisionSolverSink__ManagedVtable.Create(vtables, target);
}
