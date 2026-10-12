//========= SourceSharp — a model's sequence durations, from the managed header =//
//
// plan physics_bones_native_zero: the last ask of studio_sequence_timing.cpp.
// model_descriptor.cpp's walk used to compute each sequence's duration in
// native code (Studio_CalcDefaultPoseParameters, then Studio_Duration over
// those defaults, per sequence). Both are ported (BoneSetupHdrRead), and the
// side's StudioModels already holds every model's image, so the descriptor
// reads them here instead.
//
// THE MODEL'S OWN DEFAULTS, not a zeroed array: a zeroed pose-parameter array
// is a DIFFERENT pose on a model whose parameters do not have zero in range, so
// the duration it reports would be a different animation's.
//=============================================================================

using System;
using SourceSharp.Abi;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// <see cref="IModelSequenceTiming"/> over a side's model cache.
/// </summary>
public sealed class StudioSequenceTiming : IModelSequenceTiming
{
    private readonly IBoneModels _models;

    public StudioSequenceTiming(IBoneModels models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    /// <inheritdoc/>
    public float[]? DefaultDurations(int modelIndex)
    {
        StudioHdr? hdr = modelIndex > 0 ? _models.OpenStudioHdr(modelIndex) : null;
        return hdr is null ? null : DefaultDurations(hdr);
    }

    /// <summary>
    /// Each sequence's <c>Studio_Duration</c> at the pose
    /// <c>Studio_CalcDefaultPoseParameters</c> seeds, in model order.
    /// </summary>
    public static float[] DefaultDurations(StudioHdr hdr)
    {
        ArgumentNullException.ThrowIfNull(hdr);

        float[] defaults = new float[BoneLibrary.MaxStudioPoseParam];
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, defaults, BoneLibrary.MaxStudioPoseParam);

        float[] durations = new float[hdr.NumSequences];

        for (int i = 0; i < durations.Length; i++)
        {
            durations[i] = BoneSetupHdrRead.Duration(hdr, i, defaults);
        }

        return durations;
    }
}
