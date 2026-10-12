namespace SourceSharp.Abi;

/// <summary>
/// A model's sequence timing, read from the side's managed studio header
/// (plan physics_bones_native_zero: studio_sequence_timing.cpp's eviction).
///
/// <para>
/// <see cref="ModelDescriptors"/> takes each sequence's default duration from
/// here rather than from the native descriptor blob. The reader lives with the
/// bone port (<c>SourceSharp.Shared.Bones.StudioSequenceTiming</c>), which this
/// assembly cannot reference, so the module that owns the side's
/// <c>StudioModels</c> attaches one (<c>ModelDescriptors.AttachTiming</c>).
/// </para>
/// </summary>
public interface IModelSequenceTiming
{
    /// <summary>
    /// How long each of the model's sequences runs at the model's OWN default
    /// pose parameters, in model order; or null when the side cannot open the
    /// model's studio header.
    /// </summary>
    float[]? DefaultDurations(int modelIndex);
}
