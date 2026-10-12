using System;
using System.Collections.Generic;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>One thing a sequence fires as it plays.</summary>
/// <param name="Index">Its position in the sequence's own event list.</param>
/// <param name="Cycle">Where in the animation it fires, 0..1.</param>
/// <param name="Id">The event number. For a new-system event this is the
/// registry ordinal the model was indexed with; for an old-system one it is the
/// bare number the .mdl carries.</param>
/// <param name="Type">The native <c>AE_TYPE_*</c> mask.</param>
/// <param name="Name">The event name, or empty for an old-system event — which
/// has none, and whose <see cref="Id"/> is the only thing identifying it.</param>
/// <param name="Options">Whatever the .qc put after the event.</param>
public readonly record struct ModelSequenceEvent(
    int Index, float Cycle, int Id, int Type, string Name, string Options);

/// <summary>One sequence of a model, whole.</summary>
/// <param name="Index">Its sequence index on this model.</param>
/// <param name="Label">The name the .qc gave it — <c>fire01</c>, <c>IR_fire</c>.</param>
/// <param name="ActivityName">The activity it is tagged with —
/// <c>ACT_VM_PRIMARYATTACK</c> — or empty for an untagged sequence.</param>
/// <param name="Flags">The native <c>STUDIO_*</c> sequence flags.</param>
/// <param name="DefaultDuration">How long it runs at the model's OWN default
/// pose parameters. A fixed number per sequence, which is why it belongs in the
/// descriptor rather than behind a per-call crossing. The blob does not carry
/// it (v3): <see cref="ModelDescriptors"/> computes it from the side's managed
/// studio header (<see cref="ModelDescriptor.WithDurations"/>), and it is 0
/// straight out of <see cref="ModelDescriptor.Decode"/>.</param>
/// <param name="Events">What it fires, in model order.</param>
public sealed record ModelSequence(
    int Index, string Label, string ActivityName, int Flags, float DefaultDuration,
    IReadOnlyList<ModelSequenceEvent> Events);

/// <summary>
/// EVERYTHING MANAGED CODE ASKS A MODEL, crossed once and kept for the level.
///
/// <para>
/// This replaced seven per-call ABI entry points, removed from the struct at
/// ABI 248 — <c>EntityLookupSequence</c>, <c>EntityLookupSequenceByActivity</c>,
/// <c>EntitySequenceDuration</c>, <c>EntitySequenceEventCount</c>,
/// <c>EntitySequenceEvent</c>, <c>StandaloneAttachmentCount</c> and
/// <c>StandaloneAttachmentName</c> — each of
/// which answered one scalar about a file that does not change while the level
/// is loaded. <c>StandaloneBoneCount</c> was counted as an eighth and is not:
/// its <c>pOutCached</c> half is per-ENTITY, so that slot stays. Reading one weapon's fire animation cost <c>1 + 1 + (E+1)</c>
/// calls; this costs one, once per model.
/// </para>
///
/// <para>
/// <b>AND IT IS WHY THE TWO SIDES CAN STOP DISAGREEING.</b> The client's
/// <c>EntityLookupSequence</c> called native <c>::LookupSequence</c>, which on
/// a label miss falls through to an activity lookup resolved by
/// <c>SelectWeightedSequence</c> — a weighted RANDOM pick. The server's refused
/// that fallback and answered -1. A sequence index is PREDICTED state, so the
/// same <c>ACT_</c> name resolving to a random index on one side and to nothing
/// on the other is a misprediction per lookup. With the TABLE crossing instead
/// of the ANSWER, <see cref="LookupSequence"/> is one implementation both sides
/// run, which is the only shape that is the same on both sides by construction.
/// </para>
///
/// <para>
/// The format is defined in <c>sourcesharp/game/shared/model_descriptor.h</c>
/// and <see cref="Decode"/> is read against it;
/// <c>ModelDescriptorTests</c> asserts the two agree on a blob the native
/// packer's own rules build.
/// </para>
/// </summary>
public sealed class ModelDescriptor
{
    /// <summary>The version word the native packer writes first.</summary>
    public const int Version = 3;

    /// <summary>
    /// What a model index that names nothing answers — no bones, no
    /// attachments, no sequences. Every lookup on it answers "not found", which
    /// is what the per-call entry points answered for such a handle too.
    /// </summary>
    public static readonly ModelDescriptor Empty =
        new(0, Array.Empty<string>(), Array.Empty<Vector>(), Array.Empty<ModelSequence>());

    private ModelDescriptor(int boneCount,
                            IReadOnlyList<string> attachments,
                            IReadOnlyList<Vector> attachmentOrigins,
                            IReadOnlyList<ModelSequence> sequences)
    {
        BoneCount = boneCount;
        Attachments = attachments;
        AttachmentOrigins = attachmentOrigins;
        Sequences = sequences;
    }

    /// <summary>How many bones the model has.</summary>
    public int BoneCount { get; }

    /// <summary>Its attachment names, in model order. Attachment NUMBERS are
    /// one-based in the engine's convention; this list is not.</summary>
    public IReadOnlyList<string> Attachments { get; }

    /// <summary>Each attachment's local origin, relative to its bone, in the
    /// same order as <see cref="Attachments"/> (v2). Static model data: column 3
    /// of the attachment's local matrix.</summary>
    public IReadOnlyList<Vector> AttachmentOrigins { get; }

    /// <summary>
    /// The local origin of the attachment named <paramref name="name"/>
    /// (case-insensitive, as <see cref="LookupAttachment"/>), or false when the
    /// model has no such attachment.
    /// </summary>
    public bool TryAttachmentOrigin(string? name, out Vector origin)
    {
        int oneBased = LookupAttachment(name);

        if (oneBased <= 0)
        {
            origin = Vector.Zero;
            return false;
        }

        origin = AttachmentOrigins[oneBased - 1];
        return true;
    }

    /// <summary>Its sequences, in model order.</summary>
    public IReadOnlyList<ModelSequence> Sequences { get; }

    /// <summary>
    /// The sequence with this LABEL, case-insensitively, or -1.
    ///
    /// <para>
    /// LABEL ONLY. There is deliberately no fallback to an activity lookup:
    /// that fallback is the client/server disagreement this type exists to
    /// close, and callers that want an activity ask
    /// <see cref="LookupSequenceByActivity"/>, which is deterministic.
    /// </para>
    /// </summary>
    public int LookupSequence(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        for (int i = 0; i < Sequences.Count; i++)
        {
            if (string.Equals(Sequences[i].Label, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The FIRST sequence tagged with this activity name, case-insensitively,
    /// or -1. First and not stock's weighted pick, for
    /// <c>sequence_by_activity.h</c>'s reason: the index is predicted state, so
    /// the rule must be a pure function of the model.
    /// </summary>
    public int LookupSequenceByActivity(string? activityName)
    {
        if (string.IsNullOrEmpty(activityName))
            return -1;

        for (int i = 0; i < Sequences.Count; i++)
        {
            string name = Sequences[i].ActivityName;

            if (name.Length > 0 &&
                string.Equals(name, activityName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// How long a sequence runs at the model's default pose, or 0 for an index
    /// this model does not have.
    /// </summary>
    public float SequenceDuration(int sequence)
        => sequence >= 0 && sequence < Sequences.Count
            ? Sequences[sequence].DefaultDuration
            : 0f;

    /// <summary>What a sequence fires, or an empty list for an index this model
    /// does not have.</summary>
    public IReadOnlyList<ModelSequenceEvent> EventsOf(int sequence)
        => sequence >= 0 && sequence < Sequences.Count
            ? Sequences[sequence].Events
            : Array.Empty<ModelSequenceEvent>();

    /// <summary>
    /// The one-based number of a named attachment, or 0 — the engine's own
    /// convention, which <c>BoneLibrary_LookupAttachment</c> also answers in.
    /// </summary>
    public int LookupAttachment(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;

        for (int i = 0; i < Attachments.Count; i++)
        {
            if (string.Equals(Attachments[i], name, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        return 0;
    }

    /// <summary>
    /// The name of a one-based attachment number, or empty.
    /// </summary>
    public string AttachmentName(int oneBased)
        => oneBased >= 1 && oneBased <= Attachments.Count
            ? Attachments[oneBased - 1]
            : string.Empty;

    /// <summary>
    /// This descriptor with each sequence's <see cref="ModelSequence.DefaultDuration"/>
    /// taken from <paramref name="durations"/>, in model order. The blob
    /// carries no duration (v3): <see cref="ModelDescriptors"/> computes them
    /// from the side's managed studio header and fills them in here.
    /// </summary>
    public ModelDescriptor WithDurations(IReadOnlyList<float> durations)
    {
        ArgumentNullException.ThrowIfNull(durations);

        if (durations.Count != Sequences.Count)
            throw new ArgumentException(
                $"{durations.Count} duration(s) for {Sequences.Count} sequence(s)", nameof(durations));

        var sequences = new ModelSequence[Sequences.Count];

        for (int i = 0; i < sequences.Length; i++)
            sequences[i] = Sequences[i] with { DefaultDuration = durations[i] };

        return new ModelDescriptor(BoneCount, Attachments, AttachmentOrigins, sequences);
    }

    /// <summary>
    /// Read a blob the native packer wrote.
    ///
    /// <para>
    /// A blob that is truncated, or whose first word is not
    /// <see cref="Version"/>, decodes as <see cref="Empty"/> rather than
    /// throwing: a decoder that threw would take the frame that asked with it,
    /// and "this model answers nothing" is a state the caller already handles.
    /// <paramref name="why"/> says which it was, so a caller that wants to
    /// complain can.
    /// </para>
    /// </summary>
    public static ModelDescriptor Decode(ReadOnlySpan<byte> blob, out string why)
    {
        why = string.Empty;

        var r = new Reader(blob);

        if (!r.Int(out int version))
        {
            why = "blob is empty";
            return Empty;
        }

        if (version != Version)
        {
            why = $"blob version {version}, expected {Version}";
            return Empty;
        }

        if (!r.Int(out int bones) || !r.Int(out int attachmentCount) || attachmentCount < 0)
        {
            why = "truncated before the attachment count";
            return Empty;
        }

        var attachments = new string[attachmentCount];
        var origins = new Vector[attachmentCount];

        for (int i = 0; i < attachmentCount; i++)
        {
            if (!r.String(out string name) ||
                !r.Float(out float x) || !r.Float(out float y) || !r.Float(out float z))
            {
                why = $"truncated in attachment {i}";
                return Empty;
            }

            attachments[i] = name;
            origins[i] = new Vector(x, y, z);
        }

        if (!r.Int(out int sequenceCount) || sequenceCount < 0)
        {
            why = "truncated before the sequence count";
            return Empty;
        }

        var sequences = new ModelSequence[sequenceCount];

        for (int i = 0; i < sequenceCount; i++)
        {
            if (!r.String(out string label) ||
                !r.String(out string activity) ||
                !r.Int(out int flags) ||
                !r.Int(out int eventCount) ||
                eventCount < 0)
            {
                why = $"truncated in sequence {i}";
                return Empty;
            }

            var events = eventCount == 0
                ? (IReadOnlyList<ModelSequenceEvent>)Array.Empty<ModelSequenceEvent>()
                : new ModelSequenceEvent[eventCount];

            for (int j = 0; j < eventCount; j++)
            {
                if (!r.Float(out float cycle) ||
                    !r.Int(out int id) ||
                    !r.Int(out int type) ||
                    !r.String(out string name) ||
                    !r.String(out string options))
                {
                    why = $"truncated in sequence {i} event {j}";
                    return Empty;
                }

                ((ModelSequenceEvent[])events)[j] =
                    new ModelSequenceEvent(j, cycle, id, type, name, options);
            }

            sequences[i] = new ModelSequence(i, label, activity, flags, 0f, events);
        }

        return new ModelDescriptor(bones, attachments, origins, sequences);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _blob;
        private int _at;

        public Reader(ReadOnlySpan<byte> blob)
        {
            _blob = blob;
            _at = 0;
        }

        public bool Int(out int value)
        {
            value = 0;

            if (_at + 4 > _blob.Length)
                return false;

            value = BitConverter.ToInt32(_blob.Slice(_at, 4));
            _at += 4;
            return true;
        }

        public bool Float(out float value)
        {
            value = 0f;

            if (_at + 4 > _blob.Length)
                return false;

            value = BitConverter.ToSingle(_blob.Slice(_at, 4));
            _at += 4;
            return true;
        }

        public bool String(out string value)
        {
            value = string.Empty;

            if (!Int(out int length) || length < 0 || _at + length > _blob.Length)
                return false;

            if (length > 0)
            {
                value = Encoding.UTF8.GetString(_blob.Slice(_at, length));
                _at += length;
            }

            return true;
        }
    }
}
