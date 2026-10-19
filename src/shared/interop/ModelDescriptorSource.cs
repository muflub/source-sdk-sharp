namespace SourceSharp.Abi;

/// <summary>
/// Where a side's <see cref="ModelDescriptor"/>s are read from: the side's
/// managed studio header (studio.cpp's eviction). model_descriptor.cpp used to
/// walk the model natively through CStudioHdr and cross the answer as a blob;
/// the reader that replaces the walk lives with the bone port
/// (<c>SourceSharp.Shared.Bones.StudioModelDescriptor</c>), which this assembly
/// cannot reference, so the module that owns the side's <c>StudioModels</c>
/// attaches one (<c>ModelDescriptors.AttachSource</c>), as it attached the
/// sequence timing before.
/// </summary>
public interface IModelDescriptorSource
{
    /// <summary>
    /// The whole descriptor of the model at <paramref name="modelIndex"/>,
    /// durations included; or null when the side cannot open the model's
    /// studio header (no studio model at that index, or one whose includes the
    /// engine cannot find).
    /// </summary>
    ModelDescriptor? Describe(int modelIndex);
}
