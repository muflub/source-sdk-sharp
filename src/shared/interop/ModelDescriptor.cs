using System;
using System.Collections.Generic;

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
/// descriptor rather than behind a per-call crossing. Computed from the side's
/// managed studio header (<c>StudioSequenceTiming</c>).</param>
/// <param name="Events">What it fires, in model order.</param>
/// <param name="ActivityWeight">Its <c>actweight</c>: its share of the
/// activity's weighted pick, and when negative, a sequence a pick keeps.</param>
/// <param name="EntryNode">The graph node it starts on, from 1; 0 for none.</param>
/// <param name="ExitNode">The graph node it ends on, from 1; 0 for none.</param>
/// <param name="NodeFlags">Non-zero when it may be played backwards between its nodes.</param>
public sealed record ModelSequence(
    int Index, string Label, string ActivityName, int Flags, float DefaultDuration,
    IReadOnlyList<ModelSequenceEvent> Events, int ActivityWeight = 0, int EntryNode = 0, int ExitNode = 0,
    int NodeFlags = 0);

/// <summary>
/// EVERYTHING MANAGED CODE ASKS A MODEL, read once and kept for the level.
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
/// calls; this cost one, once per model. Since 2026-10-18 it costs none: the
/// descriptor is read from the side's managed studio header
/// (<c>SourceSharp.Shared.Bones.StudioModelDescriptor</c>), and the native
/// walk that packed it (model_descriptor.cpp) and its slot are gone.
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
/// </summary>
public sealed class ModelDescriptor : ISequenceNodes
{
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
                            IReadOnlyList<ModelSequence> sequences,
                            int nodeCount = 0, byte[]? transitions = null, bool folded = false)
    {
        _nodeCount = nodeCount;
        _transitions = transitions ?? Array.Empty<byte>();
        _folded = folded;
        BoneCount = boneCount;
        Attachments = attachments;
        AttachmentOrigins = attachmentOrigins;
        Sequences = sequences;
    }

    /// <summary>
    /// A descriptor over values already read: what the side's managed studio
    /// header answers (<c>SourceSharp.Shared.Bones.StudioModelDescriptor</c>).
    /// <paramref name="attachmentOrigins"/> runs parallel to
    /// <paramref name="attachments"/>, and each sequence's
    /// <see cref="ModelSequence.Index"/> is its position.
    /// </summary>
    public static ModelDescriptor Create(int boneCount,
                                         IReadOnlyList<string> attachments,
                                         IReadOnlyList<Vector> attachmentOrigins,
                                         IReadOnlyList<ModelSequence> sequences,
                                         int nodeCount = 0, byte[]? transitions = null, bool folded = false)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(attachmentOrigins);
        ArgumentNullException.ThrowIfNull(sequences);

        if (attachmentOrigins.Count != attachments.Count)
            throw new ArgumentException(
                $"{attachmentOrigins.Count} origin(s) for {attachments.Count} attachment(s)", nameof(attachmentOrigins));

        for (int i = 0; i < sequences.Count; i++)
        {
            if (sequences[i].Index != i)
                throw new ArgumentException($"sequence {i} carries index {sequences[i].Index}", nameof(sequences));
        }

        if (transitions is not null && transitions.Length != nodeCount * nodeCount)
            throw new ArgumentException($"{transitions.Length} transition(s) for {nodeCount} node(s)", nameof(transitions));

        return new ModelDescriptor(boneCount, attachments, attachmentOrigins, sequences, nodeCount, transitions, folded);
    }

    private readonly int _nodeCount;
    private readonly byte[] _transitions;
    private readonly bool _folded;

    /// <inheritdoc/>
    public int NumSequences => Sequences.Count;

    /// <inheritdoc/>
    public int EntryNode(int sequence) => Sequences[sequence].EntryNode;

    /// <inheritdoc/>
    public int ExitNode(int sequence) => Sequences[sequence].ExitNode;

    /// <inheritdoc/>
    public int NodeFlags(int sequence) => Sequences[sequence].NodeFlags;

    /// <summary>
    /// <inheritdoc/> The studio header's table, nodes from 1; a folded model
    /// answers the goal node itself, as its header does.
    /// </summary>
    public int GetTransition(int from, int to)
    {
        if (_folded)
            return to;

        if (from < 1 || to < 1 || from > _nodeCount || to > _nodeCount)
            return 0;

        return _transitions[(from - 1) * _nodeCount + (to - 1)];
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
}
