namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IStudioMemory"/> over one side's <c>IVModelInfo</c>.
/// </summary>
internal sealed class EngineStudioMemory : IStudioMemory
{
    private readonly EngineSide _side;

    internal EngineStudioMemory(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    private IFwdModelInfo Info => EngineBound.ModelInfo(_side);

    public byte[]? ModelImage(int modelIndex) => Info.StudioImage(modelIndex, null);

    public byte[]? ModelAnimBlock(int modelIndex, int block) => Info.StudioAnimBlock(modelIndex, null, block);

    public byte[]? IncludeImage(int modelIndex, string includeName) => Info.StudioImage(modelIndex, includeName);

    public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block)
        => Info.StudioAnimBlock(modelIndex, includeName, block);
}
