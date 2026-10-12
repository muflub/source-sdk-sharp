namespace SourceSharp.Abi;

/// <summary>
/// The real one: <c>ClientPolicyBlocks</c>, and the pointer arithmetic over what
/// it answers.
///
/// <para>
/// The unsafe half lives here rather than in <c>SourceSharp.Client</c> for the
/// reason <see cref="IInterpolationBlocks"/>'s implementation gives: the game
/// assemblies are compiled without <c>/unsafe</c> and the render policy has no
/// business being the exception.
/// </para>
/// </summary>
internal sealed unsafe class RenderPolicyBlocks : IRenderPolicyBlocks
{
    /// <summary>
    /// The side's own slot routing, handed over by the area that holds this
    /// (plan_abi_hotreload D8): it was <c>Engine.Current.Slots</c>, which
    /// followed the active side. One field read per call, as before.
    /// </summary>
    private readonly EngineSide _api;

    internal RenderPolicyBlocks(EngineSide api) => _api = api;

    private void** _blocks;
    private int _count;

    public int Begin()
    {
        _blocks = null;
        _count = 0;

        if (!_api.ClientAttached)
            return 0;

        int count;
        void** blocks = _api.ClientShims.ClientPolicyBlocks(&count);

        if (blocks == null || count <= 0)
            return 0;

        _blocks = blocks;
        _count = count;
        return count;
    }

    public Span<byte> Block(int index)
    {
        if (_blocks == null || (uint)index >= (uint)_count)
            return Span<byte>.Empty;

        void* block = _blocks[index];
        if (block == null)
            return Span<byte>.Empty;

        // EngineBlock.SizeBytes and NOT the networked payload's length. The two
        // overlap and have different lengths -- 32 bytes for a class that
        // declared no state, against the whole block -- and reading the block
        // through the payload span refuses everything past the entity's tier
        // SILENTLY, because a short span is a legal span. Every render-policy
        // field lives in the client tail, which is past every tier's end, so
        // this is the difference between the pass working and the pass writing
        // nothing at all. See Engine.GetEntityBlock.
        return new Span<byte>(block, EngineBlock.SizeBytes);
    }
}
