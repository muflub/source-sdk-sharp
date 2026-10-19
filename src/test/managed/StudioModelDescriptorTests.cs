using System;
using System.Collections.Generic;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;
using Xunit;
using AbiVector = SourceSharp.Abi.Vector;
using ModelDescriptor = SourceSharp.Abi.ModelDescriptor;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="StudioModelDescriptor"/>: the descriptor model_descriptor.cpp's
/// walk read through the native CStudioHdr, read from the managed header
/// instead. The parity run over dm_lockdown's models (both sides, load and
/// changelevel) is in the commit that wires it; these pin each field.
/// </summary>
public class StudioModelDescriptorTests
{
    private sealed class OneModel(int index, StudioHdr? hdr) : IBoneModels
    {
        public List<int> Asked { get; } = [];

        public bool HasStudioModel(int modelIndex) => OpenStudioHdr(modelIndex) is not null;

        public StudioHdr? OpenStudioHdr(int modelIndex)
        {
            Asked.Add(modelIndex);
            return modelIndex == index ? hdr : null;
        }
    }

    private static StudioHdr Build(StudioHdrBuilder b) =>
        StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? h) && h is not null
            ? h
            : throw new InvalidOperationException("fixture image rejected by TryCreate");

    private static StudioHdrBuilder TwoBones()
    {
        var b = new StudioHdrBuilder { ModelName = "m" };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root" });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "hand", Parent = 0 });
        return b;
    }

    private static StudioHdrBuilder.SeqDef Seq(string label) => new()
    {
        Label = label,
        AnimTable = [0],
    };

    private static StudioHdrBuilder WithOneAnim(StudioHdrBuilder b)
    {
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a", NumFrames = 31, Fps = 30f });
        return b;
    }

    private static ModelDescriptor Describe(StudioHdrBuilder b) => StudioModelDescriptor.Describe(Build(b));

    [Fact]
    public void TheBoneCountIsTheModels()
    {
        Assert.Equal(2, Describe(TwoBones()).BoneCount);
    }

    [Fact]
    public void AnAttachmentCarriesItsNameAndTheTranslationColumnOfItsLocalMatrix()
    {
        StudioHdrBuilder b = TwoBones();
        BoneMatrix3x4 local = BoneMatrix3x4.Identity;
        local.Row0.W = 1.5f;
        local.Row1.W = -2f;
        local.Row2.W = 3.25f;
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "muzzle", LocalBone = 1, Local = local });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "eject", LocalBone = 1 });

        ModelDescriptor d = Describe(b);

        Assert.Equal(new[] { "muzzle", "eject" }, d.Attachments);
        Assert.Equal(new AbiVector(1.5f, -2f, 3.25f), d.AttachmentOrigins[0]);
        Assert.Equal(new AbiVector(0f, 0f, 0f), d.AttachmentOrigins[1]);
    }

    [Fact]
    public void SequencesCarryLabelActivityNameAndFlagsInModelOrder()
    {
        StudioHdrBuilder b = WithOneAnim(TwoBones());
        StudioHdrBuilder.SeqDef fire = Seq("fire01");
        fire.ActivityName = "ACT_VM_PRIMARYATTACK";
        fire.Flags = StudioConstants.STUDIO_LOOPING;
        b.Sequences.Add(Seq("idle"));
        b.Sequences.Add(fire);

        ModelDescriptor d = Describe(b);

        Assert.Equal(2, d.Sequences.Count);
        Assert.Equal(("idle", "", 0, 0), (d.Sequences[0].Label, d.Sequences[0].ActivityName, d.Sequences[0].Flags, d.Sequences[0].Index));
        Assert.Equal(("fire01", "ACT_VM_PRIMARYATTACK", StudioConstants.STUDIO_LOOPING, 1),
                     (d.Sequences[1].Label, d.Sequences[1].ActivityName, d.Sequences[1].Flags, d.Sequences[1].Index));
    }

    [Fact]
    public void AnEventCarriesItsCycleNumberTypeAndOptions()
    {
        StudioHdrBuilder b = WithOneAnim(TwoBones());
        StudioHdrBuilder.SeqDef fire = Seq("fire");
        fire.Events.Add(new StudioHdrBuilder.EventDef { Cycle = 0.25f, Event = 5001, Type = 0, Options = "2" });
        b.Sequences.Add(fire);

        Abi.ModelSequenceEvent e = Assert.Single(Describe(b).Sequences[0].Events);

        Assert.Equal((0, 0.25f, 5001, 0, "2"), (e.Index, e.Cycle, e.Id, e.Type, e.Options));
    }

    [Fact]
    public void ANewSystemEventCarriesItsName()
    {
        StudioHdrBuilder b = WithOneAnim(TwoBones());
        StudioHdrBuilder.SeqDef fire = Seq("fire");
        fire.Events.Add(new StudioHdrBuilder.EventDef
        {
            Event = 3, Type = AnimationEventTypes.NewEventSystem, Name = "AE_MUZZLEFLASH",
        });
        b.Sequences.Add(fire);

        Assert.Equal("AE_MUZZLEFLASH", Describe(b).Sequences[0].Events[0].Name);
    }

    [Fact]
    public void AnOldSystemEventCarriesNoNameWhateverItsIndexPointsAt()
    {
        // The positive arm is the fact above: the same name with the type bit
        // set is read.
        StudioHdrBuilder b = WithOneAnim(TwoBones());
        StudioHdrBuilder.SeqDef fire = Seq("fire");
        fire.Events.Add(new StudioHdrBuilder.EventDef { Event = 6001, Type = 0, Name = "AE_MUZZLEFLASH" });
        b.Sequences.Add(fire);

        Assert.Equal(string.Empty, Describe(b).Sequences[0].Events[0].Name);
    }

    [Fact]
    public void EachSequenceRunsForItsDurationAtTheModelsDefaultPose()
    {
        StudioHdrBuilder b = WithOneAnim(TwoBones());
        b.Sequences.Add(Seq("walk"));
        StudioHdr hdr = Build(b);

        ModelDescriptor d = StudioModelDescriptor.Describe(hdr);

        Assert.Equal(StudioSequenceTiming.DefaultDurations(hdr), new[] { d.Sequences[0].DefaultDuration });
        Assert.Equal(1.0f, d.Sequences[0].DefaultDuration, 6);
    }

    [Fact]
    public void AnIncludedModelsSequencesAreDescribedAfterTheBaseModels()
    {
        StudioHdrBuilder g = WithOneAnim(TwoBones());
        g.Sequences.Add(Seq("from_include"));

        StudioHdrBuilder b = WithOneAnim(TwoBones());
        b.Sequences.Add(Seq("own"));
        b.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "g", Name = "grp" });
        b.AddIncludeImage("grp", g.Build());

        ModelDescriptor d = Describe(b);

        Assert.Equal(new[] { "own", "from_include" }, new[] { d.Sequences[0].Label, d.Sequences[1].Label });
    }

    [Fact]
    public void TheModelIsReadFromTheSidesCacheByIndex()
    {
        StudioHdrBuilder b = TwoBones();
        var models = new OneModel(7, Build(b));

        ModelDescriptor? d = new StudioModelDescriptor(models).Describe(7);

        Assert.NotNull(d);
        Assert.Equal(2, d!.BoneCount);
        Assert.Equal(new[] { 7 }, models.Asked);
    }

    [Fact]
    public void AModelTheCacheCannotOpenAnswersNull()
    {
        var models = new OneModel(7, Build(TwoBones()));

        Assert.Null(new StudioModelDescriptor(models).Describe(8));
    }

    [Fact]
    public void AnIndexThatNamesNoModelIsNeverAsked()
    {
        var models = new OneModel(0, Build(TwoBones()));

        Assert.Null(new StudioModelDescriptor(models).Describe(0));
        Assert.Empty(models.Asked);
    }

    [Fact]
    public void CreateRefusesOriginsThatDoNotMatchTheAttachments()
    {
        Assert.Throws<ArgumentException>(() => ModelDescriptor.Create(
            1, new[] { "a" }, Array.Empty<AbiVector>(), Array.Empty<Abi.ModelSequence>()));
    }
}
