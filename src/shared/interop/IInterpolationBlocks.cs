namespace SourceSharp.Abi;

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
