namespace SourceSharp.Abi;

/// <summary>
/// The list on a side that has none — the server, and every test that never set
/// one up. An empty list and not a printed refusal, for the reason
/// <see cref="NoInterpolationBlocks"/> gives: there genuinely are no client
/// entities on the server, so "none" is the true answer rather than a capability
/// being declined.
/// </summary>
public sealed class NoRenderPolicyBlocks : IRenderPolicyBlocks
{
    public static readonly NoRenderPolicyBlocks Instance = new();

    private NoRenderPolicyBlocks() { }

    public int Begin() => 0;

    public Span<byte> Block(int index) => Span<byte>.Empty;
}
