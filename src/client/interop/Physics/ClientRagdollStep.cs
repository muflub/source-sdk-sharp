using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>What the client's physics step asks of the client's ragdolls (ClientPhysicsSystem).</summary>
public interface IClientRagdollStep
{
    /// <summary>The physics step's pose write (stock's step, after <c>Simulate</c>).</summary>
    int StepPoses();

    /// <summary>Every body taken apart before the environment goes (before <c>DestroyEnvironment</c>).</summary>
    int DestroyAll();
}
