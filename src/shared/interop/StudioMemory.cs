namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IStudioMemory"/> over one side's <c>IVModelInfo</c>.
/// </summary>
internal sealed class EngineStudioMemory : IStudioMemory
{
    // The side's IVModelInfo, as its composition registered it.
    private readonly IFwdModelInfo _info;

    internal EngineStudioMemory(IFwdModelInfo info)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
    }

    private IFwdModelInfo Info => _info;

    public byte[]? ModelImage(int modelIndex) => Info.StudioImage(modelIndex, null);

    public byte[]? ModelAnimBlock(int modelIndex, int block) => Info.StudioAnimBlock(modelIndex, null, block);

    public byte[]? IncludeImage(int modelIndex, string includeName) => Info.StudioImage(modelIndex, includeName);

    public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block)
        => Info.StudioAnimBlock(modelIndex, includeName, block);
}
