using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ModelDescriptor"/>'s lookups -- the RULE both sides run over one
/// model's table (plan_shim.md Phase 2d) -- and <see cref="ModelDescriptors"/>,
/// the per-side cache that reads each model once from the attached
/// <see cref="IModelDescriptorSource"/>.
///
/// <para>
/// The descriptor was a blob that model_descriptor.cpp's native walk packed
/// and <c>ModelDescriptor.Decode</c> read; since studio.cpp's eviction
/// (2026-10-18) the managed <c>StudioModelDescriptor</c> reads it from the
/// side's StudioHdr, and <c>StudioModelDescriptorTests</c> pins each field it
/// reads. The fixtures here are built with <see cref="ModelDescriptor.Create"/>.
/// </para>
/// </summary>
public class ModelDescriptorTests
{
    private static ModelSequence Seq(int index, string label, string activity, int flags,
                                     float duration = 0f, params ModelSequenceEvent[] events)
        => new(index, label, activity, flags, duration, events);

    /// <summary>
    /// v_smg1-shaped: two sequences, the first tagged with an activity and
    /// firing two events, the second untagged and firing none; one attachment;
    /// 53 bones.
    /// </summary>
    private static ModelDescriptor Small() => ModelDescriptor.Create(
        53,
        new[] { "muzzle" },
        new[] { new Vector(1.5f, -2.0f, 8.25f) },
        new[]
        {
            Seq(0, "fire01", "ACT_VM_PRIMARYATTACK", 0x4, 0.75f,
                new ModelSequenceEvent(0, 0.0f, 21, 1 << 10, "AE_MUZZLEFLASH", "SMG1 MUZZLE"),
                new ModelSequenceEvent(1, 0.25f, 6001, 0, "", "")),
            Seq(1, "idle01", "", 0x1, 2.0f),
        });

    [Fact]
    public void ASequenceWithNoEventsAnswersAnEmptyListRatherThanThrowing()
        => Assert.Empty(Small().EventsOf(1));

    [Fact]
    public void EventsOfAnIndexTheModelDoesNotHaveIsEmpty()
    {
        Assert.Empty(Small().EventsOf(-1));
        Assert.Empty(Small().EventsOf(2));
        Assert.Equal(2, Small().EventsOf(0).Count);
    }

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

        var sequences = new ModelSequence[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            sequences[i] = Seq(i, rows[i].label, rows[i].activity, 0);

        return ModelDescriptor.Create(0, Array.Empty<string>(), Array.Empty<Vector>(), sequences);
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
        ModelDescriptor d = ModelDescriptor.Create(
            0, Array.Empty<string>(), Array.Empty<Vector>(), Array.Empty<ModelSequence>());

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
        ModelDescriptor d = Small();

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
    // The cache: ModelDescriptors reads each model from the attached source
    // (the side's managed studio header, StudioModelDescriptor).
    //=========================================================================

    private sealed class Source(Func<int, ModelDescriptor?> answer) : IModelDescriptorSource
    {
        public List<int> Asked { get; } = new();

        public ModelDescriptor? Describe(int modelIndex)
        {
            Asked.Add(modelIndex);
            return answer(modelIndex);
        }
    }

    private static Source Smg() => new(index => index == 9 ? Small() : null);

    private static ModelDescriptors Descriptors(RecordingLog log) =>
        new(name => name switch { "models/v_smg1.mdl" => 9, "models/sprite.vmt" => 4, _ => -1 },
            _ => string.Empty,
            log);

    [Fact]
    public void TheDescriptorsReadEachModelFromTheAttachedSourceByIndex()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);
        Source source = Smg();

        using IDisposable attached = models.AttachSource(source);
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Equal(new[] { 9 }, source.Asked);
        Assert.Equal(0.75f, d.SequenceDuration(0));
        Assert.Equal(2.0f, d.SequenceDuration(1));
        Assert.Equal(1, models.Reads);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void ASecondAskIsAnsweredFromTheCache()
    {
        ModelDescriptors models = Descriptors(new RecordingLog());
        Source source = Smg();

        using IDisposable attached = models.AttachSource(source);
        ModelDescriptor first = models.ForModel("models/v_smg1.mdl");
        ModelDescriptor second = models.ForModel("MODELS/V_SMG1.MDL");

        Assert.Same(first, second);
        Assert.Equal(new[] { 9 }, source.Asked);
    }

    [Fact]
    public void AModelTheSourceCannotOpenAnswersEmptyQuietlyAndIsNotAskedAgain()
    {
        var log = new RecordingLog();
        ModelDescriptors models = Descriptors(log);
        Source source = Smg();

        using IDisposable attached = models.AttachSource(source);
        ModelDescriptor first = models.ForModel("models/sprite.vmt");
        ModelDescriptor second = models.ForModel("models/sprite.vmt");

        Assert.Same(ModelDescriptor.Empty, first);
        Assert.Same(ModelDescriptor.Empty, second);
        Assert.Equal(new[] { 4 }, source.Asked);
        Assert.Equal(0, models.Reads);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void AnUnknownModelNameIsNeverAskedOfTheSource()
    {
        ModelDescriptors models = Descriptors(new RecordingLog());
        Source source = Smg();

        using IDisposable attached = models.AttachSource(source);

        Assert.Same(ModelDescriptor.Empty, models.ForModel("models/nothing.mdl"));
        Assert.Empty(source.Asked);
    }

    [Fact]
    public void WithNoSourceAttachedAModelAnswersEmptyAndTheLogSaysSo()
    {
        var log = new RecordingLog();

        ModelDescriptor d = Descriptors(log).ForModel("models/v_smg1.mdl");

        Assert.Same(ModelDescriptor.Empty, d);
        Assert.Contains("no descriptor source attached", log.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisposedSourceIsNoLongerAsked()
    {
        ModelDescriptors models = Descriptors(new RecordingLog());
        Source source = Smg();

        models.AttachSource(source).Dispose();
        ModelDescriptor d = models.ForModel("models/v_smg1.mdl");

        Assert.Empty(source.Asked);
        Assert.Same(ModelDescriptor.Empty, d);
    }

    [Fact]
    public void ClearForgetsEveryModelAndTheReadCount()
    {
        ModelDescriptors models = Descriptors(new RecordingLog());
        Source source = Smg();

        using IDisposable attached = models.AttachSource(source);
        models.ForModel("models/v_smg1.mdl");
        models.Clear();

        Assert.Equal((0, 0), (models.CachedCount, models.Reads));
        models.ForModel("models/v_smg1.mdl");
        Assert.Equal(new[] { 9, 9 }, source.Asked);
    }

}
