namespace SourceSharp.Abi;

/// <summary>
/// The real one: <c>ClientInterpBlocks</c>, and the pointer arithmetic over what
/// it answers.
///
/// <para>
/// The unsafe half lives here rather than in <c>SourceSharp.Client</c> on
/// purpose — the game assemblies are compiled without <c>/unsafe</c> and the
/// interpolation code has no business being the exception. What crosses the
/// boundary is a <see cref="Span{T}"/>, which is bounds-checked on the far side.
/// </para>
/// </summary>
internal sealed unsafe class InterpolationBlocks : IInterpolationBlocks
{
    /// <summary>
    /// The side's own slot routing, handed over by the area that holds this
    /// (plan_abi_hotreload D8): it was <c>Engine.Current.Slots</c>, which
    /// followed the active side. One field read per call, as before.
    /// </summary>
    private readonly EngineSide _api;

    internal InterpolationBlocks(EngineSide api) => _api = api;

    private void** _blocks;
    private int _count;

    public int Begin(out float interpolationAmount)
    {
        interpolationAmount = 0f;
        _blocks = null;
        _count = 0;

        if (!_api.ClientAttached)
            return 0;

        int count;
        float amount;
        void** blocks = _api.ClientShims.ClientInterpBlocks(&count, &amount);

        if (blocks == null || count <= 0)
            return 0;

        _blocks = blocks;
        _count = count;
        interpolationAmount = amount;
        return count;
    }

    public Span<byte> Block(int index)
    {
        if (_blocks == null || (uint)index >= (uint)_count)
            return Span<byte>.Empty;

        void* block = _blocks[index];
        if (block == null)
            return Span<byte>.Empty;

        // EngineBlock.SizeBytes and not the payload's length: the two overlap
        // and have different lengths, and reading the block through the payload
        // span refuses everything past the entity's tier -- silently, because a
        // short span is a legal span. See Engine.GetEntityBlock.
        return new Span<byte>(block, EngineBlock.SizeBytes);
    }
}
