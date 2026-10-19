namespace SourceSharp.Abi;

/// <summary>
/// THE CLIENT'S RENDER-POLICY LIST, FETCHED ONCE A PASS (plan_entity.md step 6b).
///
/// <para>
/// <b>Why a list and not a call per entity, and why not <see cref="IInterpolationBlocks"/>.</b>
/// The same argument as 6a's for the list shape — a per-entity accessor would put
/// a native call in a loop that runs over every entity every frame — and one
/// extra reason for it being a SECOND list: 6a's is filtered by
/// <c>WantsInterpolation()</c>, which excludes the viewmodel and the predicted
/// local player. Those are exactly the two entities whose render group, draw
/// answer and shadow answer differ from everything else's, so deciding policy
/// over 6a's list would decide it for every entity except the ones that need
/// deciding.
/// </para>
///
/// <para>
/// <b>The spans are BORROWED and live until the next <see cref="Begin"/>.</b> They
/// address the entity's engine block in native memory, which is how a decision
/// made here becomes a load in the shell — see
/// <c>SourceSharp.Client.Standalone.ClientRenderPolicy</c>.
/// </para>
/// </summary>
public interface IRenderPolicyBlocks
{
    /// <summary>Rebuilds the list and answers how long it is.</summary>
    int Begin();

    /// <summary>
    /// One entity's engine block, as of the last <see cref="Begin"/>. Empty for
    /// an index out of range, which is a caller bug rather than an entity that
    /// went away — the list does not change under a pass.
    /// </summary>
    Span<byte> Block(int index);
}
