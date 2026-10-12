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

/// <summary>
/// The list on a side that has none — the server, and every test that never set
/// one up.
///
/// <para>
/// AN EMPTY LIST AND NOT A REFUSAL, and the distinction is worth stating because
/// §7 says a thing the port does not implement is a printed refusal. This is not
/// that: there genuinely are no client entities to interpolate on the server, so
/// "none" is the true answer rather than a capability being declined. A pass
/// over an empty list does nothing and says nothing, which is what a server
/// frame should cost.
/// </para>
/// </summary>
public sealed class NoInterpolationBlocks : IInterpolationBlocks
{
    public static readonly NoInterpolationBlocks Instance = new();

    private NoInterpolationBlocks() { }

    public int Begin(out float interpolationAmount)
    {
        interpolationAmount = 0f;
        return 0;
    }

    public Span<byte> Block(int index) => Span<byte>.Empty;
}
