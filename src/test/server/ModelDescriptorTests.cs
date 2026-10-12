using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE BLOB, DECODED — plan_shim.md Phase 2d.
///
/// <para>
/// <see cref="ModelDescriptor.Decode"/> reads what
/// <c>sourcesharp/game/shared/model_descriptor.cpp</c> writes: one model's
/// sequence table, attachment names and bone count, crossed once instead of
/// through eight per-call ABI slots. The two halves are in different
/// languages, so the format is the thing that can silently disagree — and a
/// disagreement does not crash, it produces a model whose sequences resolve to
/// plausible wrong indices.
/// </para>
///
/// <para>
/// <b>THE ENCODER HERE IS A STAND-IN and is treated as one.</b> It writes the
/// format as <c>model_descriptor.h</c> states it, so these facts pin the
/// DECODER against the written format. That the native WALK writes the fields
/// in that order is a separate claim, pinned separately by
/// <c>model_descriptor_format_test.cpp</c>, which compares the two sources'
/// field order directly. Neither check subsumes the other: this one would pass
/// for a decoder matching a stand-in that had drifted, and that one would pass
/// for two sides agreeing on a format neither implements correctly.
/// </para>
/// </summary>
public class ModelDescriptorTests
{
    //=========================================================================
    // The stand-in encoder: model_descriptor.h's format, written out.
    //=========================================================================

    private sealed class Blob
    {
        private readonly List<byte> _bytes = new();

        public Blob Int(int v) { _bytes.AddRange(BitConverter.GetBytes(v)); return this; }

        public Blob Float(float v) { _bytes.AddRange(BitConverter.GetBytes(v)); return this; }

        public Blob Str(string? s)
        {
            byte[] utf8 = s is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s);
            Int(utf8.Length);
            _bytes.AddRange(utf8);
            return this;
        }

        public byte[] Done() => _bytes.ToArray();
    }

    /// <summary>
    /// v_smg1-shaped: two sequences, the first tagged with an activity and
    /// firing two events, the second untagged and firing none; one attachment;
    /// 53 bones.
    /// </summary>
    private static byte[] SmallModel() =>
        new Blob()
            .Int(ModelDescriptor.Version)
            .Int(53)                                  // bones
            .Int(1)                                   // attachments
            .Str("muzzle").Float(1.5f).Float(-2.0f).Float(8.25f)
            .Int(2)                                   // sequences
                .Str("fire01").Str("ACT_VM_PRIMARYATTACK").Int(0x4)
                .Int(2)
                    .Float(0.0f).Int(21).Int(1 << 10).Str("AE_MUZZLEFLASH").Str("SMG1 MUZZLE")
                    .Float(0.25f).Int(6001).Int(0).Str(null).Str("")
                .Str("idle01").Str("").Int(0x1)
                .Int(0)
            .Done();

    private static ModelDescriptor Small()
    {
        ModelDescriptor d = ModelDescriptor.Decode(SmallModel(), out string why);
        Assert.Equal(string.Empty, why);
        return d;
    }

    //=========================================================================
    // The round trip.
    //=========================================================================

    [Fact]
    public void TheBoneCountAndAttachmentsComeBack()
    {
        ModelDescriptor d = Small();

        Assert.Equal(53, d.BoneCount);
        Assert.Equal(new[] { "muzzle" }, d.Attachments);
    }

    [Fact]
    public void TheSequenceTableComesBackWithItsLabelsAndActivities()
    {
        ModelDescriptor d = Small();

        Assert.Equal(2, d.Sequences.Count);
        Assert.Equal("fire01", d.Sequences[0].Label);
        Assert.Equal("ACT_VM_PRIMARYATTACK", d.Sequences[0].ActivityName);
        Assert.Equal(0x4, d.Sequences[0].Flags);

        // An UNTAGGED sequence has an EMPTY activity name, not a missing one.
        // "This sequence has no activity" has to be tellable from "the field
        // was not written", which is why the format has no optional fields.
        Assert.Equal(string.Empty, d.Sequences[1].ActivityName);
    }

    [Fact]
    public void EachSequenceKnowsItsOwnIndex()
    {
        ModelDescriptor d = Small();

        Assert.Equal(0, d.Sequences[0].Index);
        Assert.Equal(1, d.Sequences[1].Index);
    }

    [Fact]
    public void TheEventsComeBackInModelOrderWithTheirOptions()
    {
        IReadOnlyList<ModelSequenceEvent> events = Small().EventsOf(0);

        Assert.Equal(2, events.Count);

        Assert.Equal(0, events[0].Index);
        Assert.Equal(0f, events[0].Cycle);
        Assert.Equal("AE_MUZZLEFLASH", events[0].Name);
        Assert.Equal("SMG1 MUZZLE", events[0].Options);

        Assert.Equal(1, events[1].Index);
        Assert.Equal(0.25f, events[1].Cycle);
    }

    [Fact]
    public void AnOldSystemEventComesBackWithItsNumberAndNoName()
    {
        // The RPG's fire carries a bare 5001 and no name at all. Managed code
        // compares such an event by ID, so an empty name must not read as a
        // missing event.
        ModelSequenceEvent old = Small().EventsOf(0)[1];

        Assert.Equal(6001, old.Id);
        Assert.Equal(string.Empty, old.Name);
        Assert.Equal(string.Empty, old.Options);
    }

    [Fact]
    public void ASequenceWithNoEventsAnswersAnEmptyListRatherThanThrowing()
        => Assert.Empty(Small().EventsOf(1));

    //=========================================================================
    // The lookups — the RULE the two sides now share.
    //=========================================================================

    [Fact]
    public void LookupSequenceMatchesTheLabelCaseInsensitively()
    {
        ModelDescriptor d = Small();

        Assert.Equal(0, d.LookupSequence("fire01"));
        Assert.Equal(0, d.LookupSequence("FIRE01"));
        Assert.Equal(1, d.LookupSequence("idle01"));
    }

    [Fact]
    public void LookupSequenceDoesNotFallThroughToAnActivity()
    {
        // THE WHOLE POINT. Native ::LookupSequence fell through to an activity
        // lookup resolved by SelectWeightedSequence — a weighted RANDOM pick —
        // on the client, while the server's BoneLibrary_LookupSequence
        // refused. So this name answered a random index on one side and -1 on
        // the other, on a number that is predicted state.
        //
        // Here it answers -1 on both sides, because there is only one body.
        Assert.Equal(-1, Small().LookupSequence("ACT_VM_PRIMARYATTACK"));
    }

    [Fact]
    public void LookupSequenceByActivityTakesTheFirstMatchAndIsCaseInsensitive()
    {
        ModelDescriptor d = Small();

        Assert.Equal(0, d.LookupSequenceByActivity("ACT_VM_PRIMARYATTACK"));
        Assert.Equal(0, d.LookupSequenceByActivity("act_vm_primaryattack"));
    }

    [Fact]
    public void AnUntaggedSequenceIsNeverMatchedByAnEmptyActivityName()
    {
        // "" is not an activity, and a sequence with no activity must not be
        // the answer to asking for one. An implementation that compared the
        // strings without checking for empty would answer 1 here.
        Assert.Equal(-1, Small().LookupSequenceByActivity(""));
        Assert.Equal(-1, Small().LookupSequenceByActivity(null));
    }

    //=========================================================================
    // THE CROWBAR, inherited from sequence_by_activity_test.cpp.
    //
    // That binary went with its header — the rule is here now, one
    // implementation both sides run, rather than a header two native bodies
    // included. The CLAIM it made is what mattered and is carried over whole:
    // v_crowbar has THREE sequences tagged ACT_VM_HITCENTER, and stock's
    // SelectWeightedSequence picks among them at RANDOM. A sequence index is
    // predicted state, so the rule must be a pure function of the model.
    //=========================================================================

    /// <summary>v_crowbar's sequence table, as the model carries it.</summary>
    private static ModelDescriptor Crowbar()
    {
        var b = new Blob().Int(ModelDescriptor.Version).Int(0).Int(0).Int(8);

        (string label, string activity)[] rows =
        {
            ("idle01",      "ACT_VM_IDLE"),
            ("draw",        "ACT_VM_DRAW"),
            ("misscenter1", "ACT_VM_MISSCENTER"),
            ("misscenter2", "ACT_VM_MISSCENTER"),
            ("hitcenter1",  "ACT_VM_HITCENTER"),
            ("hitcenter2",  "ACT_VM_HITCENTER"),
            ("hitcenter3",  "ACT_VM_HITCENTER"),
            ("hitkill",     "ACT_VM_HITKILL"),
        };

        foreach ((string label, string activity) in rows)
            b.Str(label).Str(activity).Int(0).Int(0);

        ModelDescriptor d = ModelDescriptor.Decode(b.Done(), out string why);
        Assert.Equal(string.Empty, why);
        return d;
    }

    [Fact]
    public void TheFirstOfThreeSequencesSharingAnActivityIsTheAnswer()
    {
        // 4, 5 and 6 all carry ACT_VM_HITCENTER. Stock picks among them at
        // random; the server and the client's replay must compute the same
        // number from the same model, so the first one wins on both sides.
        Assert.Equal(4, Crowbar().LookupSequenceByActivity("ACT_VM_HITCENTER"));
        Assert.Equal(2, Crowbar().LookupSequenceByActivity("ACT_VM_MISSCENTER"));
    }

    [Fact]
    public void TheCrowbarsOtherActivitiesResolveToTheirOwnSequences()
    {
        ModelDescriptor d = Crowbar();

        Assert.Equal(0, d.LookupSequenceByActivity("ACT_VM_IDLE"));
        Assert.Equal(1, d.LookupSequenceByActivity("ACT_VM_DRAW"));
        Assert.Equal(7, d.LookupSequenceByActivity("ACT_VM_HITKILL"));
    }

    [Fact]
    public void TheCrowbarHasNoFireAndSaysSoRatherThanGuessing()
    {
        // It really has none: the crowbar's attack is hitcenter/misscenter.
        // This is the answer the weapon machine falls through on, and a
        // lookup that had fallen back to "some sequence" would hide it.
        Assert.Equal(-1, Crowbar().LookupSequenceByActivity("ACT_VM_PRIMARYATTACK"));
    }

    [Fact]
    public void TheActivityLookupIsCaseInsensitiveOnTheCrowbarToo()
        => Assert.Equal(4, Crowbar().LookupSequenceByActivity("act_vm_hitcenter"));

    [Fact]
    public void AModelWithNoSequencesAnswersMinusOneForEveryActivity()
    {
        byte[] blob = new Blob()
            .Int(ModelDescriptor.Version).Int(0).Int(0).Int(0).Done();

        ModelDescriptor d = ModelDescriptor.Decode(blob, out _);

        Assert.Equal(-1, d.LookupSequenceByActivity("ACT_VM_IDLE"));
    }

    [Fact]
    public void AMissingNameAnswersMinusOneRatherThanZero()
    {
        ModelDescriptor d = Small();

        // Native ::LookupSequence answered 0 -- a VALID index -- for a model
        // with no sequences at all, so "this model has no animations" and
        // "play the first one" came back as the same number.
        Assert.Equal(-1, d.LookupSequence("nosuchsequence"));
        Assert.Equal(-1, d.LookupSequence(null));
        Assert.Equal(-1, d.LookupSequenceByActivity("ACT_NOBODY_TAGGED_THIS"));
        Assert.Equal(-1, ModelDescriptor.Empty.LookupSequence("anything"));
    }

    [Fact]
    public void DurationAnswersZeroForAnIndexTheModelDoesNotHave()
    {
        ModelDescriptor d = Small().WithDurations(new[] { 0.75f, 2.0f });

        Assert.Equal(0.75f, d.SequenceDuration(0));
        Assert.Equal(0f, d.SequenceDuration(-1));
        Assert.Equal(0f, d.SequenceDuration(2));
    }

    [Fact]
    public void EachAttachmentCarriesItsLocalOrigin()
    {
        ModelDescriptor d = Small();

        Assert.Equal(new Vector(1.5f, -2.0f, 8.25f), d.AttachmentOrigins[0]);
        Assert.True(d.TryAttachmentOrigin("MUZZLE", out Vector origin));
        Assert.Equal(new Vector(1.5f, -2.0f, 8.25f), origin);
    }

    [Fact]
    public void AnAttachmentTheModelDoesNotHaveHasNoOrigin()
    {
        Assert.False(Small().TryAttachmentOrigin("placementOrigin", out Vector origin));
        Assert.Equal(Vector.Zero, origin);
        Assert.False(ModelDescriptor.Empty.TryAttachmentOrigin("muzzle", out _));
    }

    [Fact]
    public void ABlobCutInsideAnAttachmentsOriginIsRefused()
    {
        byte[] blob = new Blob()
            .Int(ModelDescriptor.Version).Int(1).Int(1)
            .Str("muzzle").Float(1f).Float(2f)          // z missing
            .Done();

        Assert.Same(ModelDescriptor.Empty, ModelDescriptor.Decode(blob, out string why));
        Assert.Contains("attachment 0", why, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachmentNumbersAreOneBasedAsTheEngineCountsThem()
    {
        ModelDescriptor d = Small();

        Assert.Equal(1, d.LookupAttachment("muzzle"));
        Assert.Equal(1, d.LookupAttachment("MUZZLE"));
        Assert.Equal(0, d.LookupAttachment("nosuchattachment"));

        Assert.Equal("muzzle", d.AttachmentName(1));
        Assert.Equal(string.Empty, d.AttachmentName(0));
        Assert.Equal(string.Empty, d.AttachmentName(2));
    }

    //=========================================================================
    // Refusals. A decoder that threw would take the frame with it.
    //=========================================================================

    [Fact]
    public void AnEmptyBlobDecodesAsEmptyAndSaysSo()
    {
        ModelDescriptor d = ModelDescriptor.Decode(Array.Empty<byte>(), out string why);

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.Contains("empty", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWrongVersionDecodesAsEmptyAndNamesBothVersions()
    {
        byte[] blob = new Blob().Int(ModelDescriptor.Version + 1).Int(0).Int(0).Int(0).Done();

        ModelDescriptor d = ModelDescriptor.Decode(blob, out string why);

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.Contains("version", why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ModelDescriptor.Version.ToString(), why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(40)]
    public void ATruncatedBlobDecodesAsEmptyAndSaysWhereItRanOut(int keep)
    {
        byte[] full = SmallModel();

        Assert.True(keep < full.Length, "the truncation point is not inside the blob");

        ModelDescriptor d = ModelDescriptor.Decode(full.AsSpan(0, keep), out string why);

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.NotEqual(string.Empty, why);
    }

    [Fact]
    public void ANegativeCountIsRefusedRatherThanAllocated()
    {
        // A count read out of a corrupt blob is an allocation size. -1
        // attachments must be a refusal, not an exception from `new
        // string[-1]` taken on whichever frame asked.
        byte[] blob = new Blob()
            .Int(ModelDescriptor.Version)
            .Int(53)
            .Int(-1)
            .Done();

        ModelDescriptor d = ModelDescriptor.Decode(blob, out string why);

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.NotEqual(string.Empty, why);
    }

    [Fact]
    public void AStringLengthPastTheEndIsRefusedRatherThanRead()
    {
        byte[] blob = new Blob()
            .Int(ModelDescriptor.Version)
            .Int(53)
            .Int(1)
            .Int(1000)          // an attachment name 1000 bytes long, with none following
            .Done();

        ModelDescriptor d = ModelDescriptor.Decode(blob, out string why);

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.NotEqual(string.Empty, why);
    }

    //=========================================================================
    // The durations (v3): not in the blob; ModelDescriptors fills them in from
    // the side's managed studio header (StudioSequenceTiming).
    //=========================================================================

    [Fact]
    public void TheBlobCarriesNoDurationSoADecodedSequenceAnswersZero()
    {
        ModelDescriptor d = Small();

        Assert.Equal(0f, d.Sequences[0].DefaultDuration);
        Assert.Equal(0f, d.Sequences[1].DefaultDuration);
    }

    [Fact]
    public void WithDurationsFillsEachSequenceInModelOrderAndKeepsTheRest()
    {
        ModelDescriptor d = Small().WithDurations(new[] { 0.75f, 2.0f });

        Assert.Equal(0.75f, d.Sequences[0].DefaultDuration);
        Assert.Equal(2.0f, d.Sequences[1].DefaultDuration);
        Assert.Equal("fire01", d.Sequences[0].Label);
        Assert.Equal(2, d.EventsOf(0).Count);
        Assert.Equal(53, d.BoneCount);
        Assert.Equal(new[] { "muzzle" }, d.Attachments);
    }

    [Fact]
    public void WithDurationsRefusesACountThatIsNotTheSequenceCount()
    {
        Assert.Throws<ArgumentException>(() => Small().WithDurations(new[] { 1f }));
    }

    private sealed class Timing(Func<int, float[]?> answer) : IModelSequenceTiming
    {
        public List<int> Asked { get; } = new();

        public float[]? DefaultDurations(int modelIndex)
        {
            Asked.Add(modelIndex);
            return answer(modelIndex);
        }
    }

    private static ModelDescriptors Descriptors(RecordingLog log) =>
        new(name => name == "models/v_smg1.mdl" ? 9 : -1,
            index => index == 9 ? SmallModel() : null,
            _ => string.Empty,
            log);

    [Fact]
    public void TheDescriptorsTakeEachDurationFromTheAttachedTiming()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);
        var timing = new Timing(_ => new[] { 0.75f, 2.0f });

        using IDisposable attached = models.AttachTiming(timing);
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Equal(new[] { 9 }, timing.Asked);
        Assert.Equal(0.75f, d.SequenceDuration(0));
        Assert.Equal(2.0f, d.SequenceDuration(1));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void WithNoTimingAttachedEveryDurationIsZeroAndTheLogSaysSo()
    {
        var log = new RecordingLog();

        ModelDescriptor d = Descriptors(log).ForModel("models/v_smg1.mdl");

        Assert.Equal(2, d.Sequences.Count);
        Assert.Equal(0f, d.SequenceDuration(0));
        Assert.Contains("no sequence timing attached", log.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimingThatCannotOpenTheHeaderIsSaidAndAnswersZero()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);

        using IDisposable attached = models.AttachTiming(new Timing(_ => null));
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Equal(0f, d.SequenceDuration(1));
        Assert.Contains("no studio header", log.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimingWhoseSequenceCountDisagreesIsSaidAndAnswersZero()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);

        using IDisposable attached = models.AttachTiming(new Timing(_ => new[] { 0.75f }));
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Equal(0f, d.SequenceDuration(0));
        Assert.Contains("sequence timing has 1 sequence(s), the descriptor 2", log.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisposedTimingIsNoLongerAsked()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);
        var timing = new Timing(_ => new[] { 0.75f, 2.0f });

        models.AttachTiming(timing).Dispose();
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Empty(timing.Asked);
        Assert.Equal(0f, d.SequenceDuration(0));
    }

    [Fact]
    public void AModelWithNothingInItIsStillAValidDescriptor()
    {
        // A brush model or a sprite: no bones, no attachments, no sequences.
        // That is a legitimate answer, distinct from a blob that failed to
        // decode, and the `why` says which.
        byte[] blob = new Blob()
            .Int(ModelDescriptor.Version).Int(0).Int(0).Int(0).Done();

        ModelDescriptor d = ModelDescriptor.Decode(blob, out string why);

        Assert.Equal(string.Empty, why);
        Assert.NotSame(ModelDescriptor.Empty, d);
        Assert.Equal(0, d.BoneCount);
        Assert.Empty(d.Sequences);
        Assert.Equal(-1, d.LookupSequence("anything"));
    }
}
