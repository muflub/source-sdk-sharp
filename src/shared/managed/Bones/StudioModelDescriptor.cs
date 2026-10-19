//========= SourceSharp — a model's descriptor, from the managed header ========//
//
// studio.cpp's eviction: model_descriptor.cpp's walk read a model through the
// native CStudioHdr (studio.cpp) and crossed what it read as a blob. The
// side's StudioModels already holds every model's folded image, so the walk
// is here instead, field for field:
//
//   bones        studiohdr_t::numbones, the base model's
//   attachments  GetNumAttachments / pAttachment(i) over the virtual model:
//                the name and column 3 of the local matrix
//   sequences    GetNumSeq / pSeqdesc(i) over the virtual model: label,
//                activity name, flags and the event table
//   durations    StudioSequenceTiming, at the model's own default pose
//
// AN EVENT'S NAME IS READ ONLY FOR A NEW-SYSTEM EVENT, as the walk did:
// szeventindex means nothing on an old-system event (the RPG's fire carries a
// bare 5001), so following it would read whatever the .mdl holds there.
//=============================================================================

using System;
using SourceSharp.Abi;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// <see cref="IModelDescriptorSource"/> over a side's model cache.
/// </summary>
public sealed class StudioModelDescriptor : IModelDescriptorSource
{
    private readonly IBoneModels _models;

    public StudioModelDescriptor(IBoneModels models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    /// <inheritdoc/>
    public ModelDescriptor? Describe(int modelIndex)
    {
        StudioHdr? hdr = modelIndex > 0 ? _models.OpenStudioHdr(modelIndex) : null;
        return hdr is null ? null : Describe(hdr);
    }

    /// <summary>The descriptor of one opened header.</summary>
    public static ModelDescriptor Describe(StudioHdr hdr)
    {
        ArgumentNullException.ThrowIfNull(hdr);

        int attachmentCount = hdr.NumGlobalAttachments;
        var names = new string[attachmentCount];
        var origins = new Vector[attachmentCount];

        for (int i = 0; i < attachmentCount; i++)
        {
            MstudioAttachment attachment = hdr.Attachment(i);
            BoneMatrix3x4 local = attachment.Local;

            names[i] = attachment.Name;
            origins[i] = new Vector(local.Row0.W, local.Row1.W, local.Row2.W);
        }

        float[] durations = StudioSequenceTiming.DefaultDurations(hdr);
        var sequences = new ModelSequence[hdr.NumSequences];

        for (int i = 0; i < sequences.Length; i++)
        {
            MstudioSeqDesc seq = hdr.SeqDesc(i);
            int eventCount = seq.NumEvents;
            ModelSequenceEvent[] events = eventCount > 0
                ? new ModelSequenceEvent[eventCount]
                : Array.Empty<ModelSequenceEvent>();

            for (int j = 0; j < eventCount; j++)
            {
                MstudioEvent e = seq.Event(j);
                string name = (e.Type & AnimationEventTypes.NewEventSystem) != 0 ? e.Name : string.Empty;

                events[j] = new ModelSequenceEvent(j, e.Cycle, e.EventId, e.Type, name, e.Options);
            }

            sequences[i] = new ModelSequence(i, seq.Label, seq.ActivityName, seq.Flags, durations[i], events);
        }

        return ModelDescriptor.Create(hdr.NumBones, names, origins, sequences);
    }
}
