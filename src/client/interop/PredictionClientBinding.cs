using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's movement, prediction frame and third-person camera,
/// managed-built (plan_client_vtables CE8): the bodies of the Entry.cs thunks
/// ClientMoveProcessMovement, ClientPredictionUpdate and
/// ClientThirdPersonCamera, moved. The generated thunk has already made the
/// client the active side and catches what a body throws; its catch answers 0
/// for the two int slots, which is what each thunk's own catch answered, and
/// the native callers read 0 as "declined" (movement) and "not filled"
/// (camera).
/// </summary>
/// <remarks>
/// <paramref name="movement"/> is the client composition's own movement
/// runner (plan_abi_hotreload D8), the one its replay also drives.
/// </remarks>
public sealed unsafe class PredictionClientBinding(Crossings crossings, ISideModule module, IClientModule game,
                                                    MovementRunner movement) : IManagedPredictionClient
{
    // The module's three movement decisions, built once (statics to zero):
    // handed to the loop for each run and taken back after, so no closure
    // over this module is left in a loop that outlives it.
    private readonly MovementRules _rules = new(
        r => game.GetJumpScale(in r),
        (i, f, sp, d, m) => game.GetFrictionScale(i, f, sp, d, m),
        (i, ms, d, m) => game.GetMaxSpeedScale(i, ms, d, m));

    /// <summary>
    /// The WHOLE of ProcessMovement, in C#, for the client's native forwarder.
    /// A server state answers 0. As in the server's PlayerCommandsBinding, the
    /// three movement decisions are this module's for the call only: the
    /// runner hands them to its loop and takes them back after, so a closure
    /// over this module is never left in the loop.
    /// </summary>
    public int MoveProcessMovement(void* a0)
    {
        crossings.Count(CrossingOp.ClientMoveProcessMovement);

        var state = (MoveStateBlob*)a0;
        if (state == null || state->IsServer != 0)
            return 0;

        return movement.ProcessMovement(state, _rules);
    }

    /// <summary>
    /// <c>IPrediction::Update</c>, forwarded whole: the ONE crossing the
    /// client's prediction frame costs (plan_entity.md step 6f).
    /// </summary>
    public void ClientPredictionUpdate(int a0, int a1, int a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientPredictionUpdate);
        module.OnClientPredictionUpdate(a0, a1 != 0, a2, a3, a4 != 0);
    }

    /// <summary>
    /// Where the third-person camera sits, both phases (plan_entity.md
    /// §3.10). Answers 1 when it filled the struct.
    /// </summary>
    public int ThirdPersonCamera(void* a0)
    {
        crossings.Count(CrossingOp.ClientThirdPersonCamera);
        if (a0 == null)
            return 0;

        return global::SourceSharp.Abi.ThirdPersonCamera.Answer(ref *(ThirdPersonCameraBlob*)a0);
    }
}
