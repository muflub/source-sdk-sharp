namespace SourceSharp.Abi;

/// <summary>
/// THE CLIENT'S INTERPOLATION LIST, FETCHED ONCE A PASS (plan_entity.md step 6a).
///
/// <para>
/// <b>Why a list and not a call per entity.</b> Interpolation runs for every
/// standalone client entity every frame. A per-entity accessor would put a
/// native call in that loop and the step's cost would scale with the map's
/// entity count — which is exactly what 6a's gate forbids. One call answers the
/// whole list; everything after it is arithmetic over memory the two sides
/// already share.
/// </para>
///
/// <para>
/// <b>The spans are BORROWED and live until the next <see cref="Begin"/>.</b> They
/// address the entity's engine block in native memory, so writing one is how the
/// drawn pose is published — see <c>SourceSharp.Client.Interpolation</c>.
/// </para>
/// </summary>
public interface IInterpolationBlocks
{
    /// <summary>
    /// Rebuilds the list and answers how long it is, along with how far behind
    /// the server the client draws.
    /// </summary>
    int Begin(out float interpolationAmount);

    /// <summary>
    /// One entity's engine block, as of the last <see cref="Begin"/>. Empty for
    /// an index out of range, which is a caller bug rather than an entity that
    /// went away — the list does not change under a pass.
    /// </summary>
    Span<byte> Block(int index);
}
