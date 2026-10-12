namespace SourceSharp.Abi;

/// <summary>
/// What the client's ragdolls answer about an entity's jointed body
/// (SourceSharp.Client's <c>IClientRagdolls.State</c>; it was the
/// <c>StandaloneRagdollState</c> slot's answer while the bodies were native).
///
/// <para>
/// A BITFIELD rather than a list of states: "has a jointed body" and "was made
/// on this tick" are independent facts about the same object, and the second is
/// only ever asked of something the first is already true of.
/// </para>
/// </summary>
[Flags]
public enum RagdollState
{
    /// <summary>No jointed body. Zero, so the whole answer is the yes/no.</summary>
    None = 0,

    /// <summary>It has one, and the origin came back with this.</summary>
    Present = 1,

    /// <summary>
    /// And it was made on THIS TICK — stock's
    /// <c>WasRagdollCreatedOnCurrentTick</c> (<c>ragdoll.cpp:866</c>). A body
    /// made this tick already has its creation force in it, so a blast added on
    /// top would push it twice.
    /// </summary>
    MadeThisTick = 2,
}
