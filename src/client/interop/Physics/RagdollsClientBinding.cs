using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>What game/client/physics.cpp asks of the client's ragdolls, through the native library's two forwarders.</summary>
public interface IClientRagdollStep
{
    /// <summary>The physics step's pose write (<c>RagdollLibrary_StepPoses</c>, after <c>Simulate</c>).</summary>
    int StepPoses();

    /// <summary>Every body taken apart before the environment goes (<c>RagdollLibrary_DestroyAll</c>).</summary>
    int DestroyAll();
}

/// <summary>
/// <see cref="IManagedRagdollsClient"/>, managed-built (plan_physics_bones_native_zero
/// Phase 3): each call counted and handed to the client's ragdolls, or
/// answered 0 when there are none composed. A throw is logged and answers 0:
/// a frame must not end inside managed code.
/// </summary>
public sealed class RagdollsClientBinding(Crossings crossings, ILog log, Func<IClientRagdollStep?> ragdolls)
    : IManagedRagdollsClient
{
    public int StepPoses()
    {
        crossings.Count(CrossingOp.ClientRagdollsStepPoses);

        try
        {
            return ragdolls()?.StepPoses() ?? 0;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the ragdoll step threw: {e.Message}\n");
            return 0;
        }
    }

    public int DestroyAll()
    {
        crossings.Count(CrossingOp.ClientRagdollsDestroyAll);

        try
        {
            return ragdolls()?.DestroyAll() ?? 0;
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp/Client] the ragdolls' teardown threw: {e.Message}\n");
            return 0;
        }
    }
}
