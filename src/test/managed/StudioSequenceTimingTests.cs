using System;
using System.Collections.Generic;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="StudioSequenceTiming"/>: each sequence's duration at the model's
/// own default pose, which model_descriptor.cpp's walk used to compute natively
/// (Studio_CalcDefaultPoseParameters, then Studio_Duration per sequence).
/// Expectations are hand-computed from the stock formulas
/// (bone_setup.cpp: Studio_CalcDefaultPoseParameters 5070-5091,
/// Studio_LocalPoseParameter, Studio_SeqAnims, Studio_CPS 5532-5549,
/// Studio_Duration 5555-5564).
/// </summary>
public class StudioSequenceTimingTests
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
        StudioHdr.TryCreate(b.Build(), null, out StudioHdr? h)
            ? h!
            : throw new InvalidOperationException("fixture image rejected by TryCreate");

    private static StudioHdrBuilder BaseBuilder()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0" });
        b.ModelName = "b0";
        return b;
    }

    /// <summary>
    /// Two sequences: "walk" plays one anim (31 frames at 30 fps: cps 1,
    /// duration 1); "run" blends "walk" and "sprint" (11 frames at 30 fps:
    /// cps 3) across pose parameter 0, whose range is -1..3. Its default is
    /// the zero state -start/(end-start) = 0.25; a zeroed array would put the
    /// blend at the range's start and the 0.5 fallback at its middle, and each
    /// of the three answers a different duration.
    /// </summary>
    private static StudioHdr TwoSequences()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "move", Start = -1f, End = 3f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "walk", NumFrames = 31, Fps = 30f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "sprint", NumFrames = 11, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "walk",
            ParamIndex0 = 0,
            GroupSize0 = 1,
            ParamStart = new float[] { -1f, 0f },
            ParamEnd = new float[] { 3f, 1f },
            AnimTable = new short[] { 0 },
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "run",
            ParamIndex0 = 0,
            GroupSize0 = 2,
            ParamStart = new float[] { -1f, 0f },
            ParamEnd = new float[] { 3f, 1f },
            AnimTable = new short[] { 0, 1 },
        });
        return Build(b);
    }

    [Fact]
    public void ThereIsOneDurationPerSequenceInModelOrder()
    {
        float[] d = StudioSequenceTiming.DefaultDurations(TwoSequences());

        Assert.Equal(2, d.Length);
        Assert.Equal(1.0f, d[0], 6);
    }

    [Fact]
    public void TheBlendIsTimedAtTheModelsOwnDefaultPose()
    {
        // default pose 0.25 -> cell weights 0.75 / 0.25 -> cps 0.75*1 + 0.25*3
        // = 1.5 -> duration 2/3. A zeroed array answers 1 (all "walk"); the
        // 0.5 fallback answers 0.5 (cps 2).
        float[] d = StudioSequenceTiming.DefaultDurations(TwoSequences());

        Assert.Equal(2f / 3f, d[1], 5);
    }

    [Fact]
    public void TheDurationsAreStudioDurationOverTheDefaultSeed()
    {
        // The same answer the two ported stock functions give when chained by
        // hand, as model_descriptor.cpp's walk chained them.
        StudioHdr hdr = TwoSequences();
        float[] seed = new float[BoneLibrary.MaxStudioPoseParam];
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, seed, seed.Length);

        float[] d = StudioSequenceTiming.DefaultDurations(hdr);

        Assert.Equal(0.25f, seed[0], 6);
        for (int i = 0; i < d.Length; i++)
            Assert.Equal(BoneSetupHdrRead.Duration(hdr, i, seed), d[i]);
    }

    [Fact]
    public void AModelWithNoSequencesAnswersAnEmptyList()
    {
        var b = BaseBuilder();
        Assert.Empty(StudioSequenceTiming.DefaultDurations(Build(b)));
    }

    [Fact]
    public void TheModelIsReadFromTheSidesCacheByIndex()
    {
        var models = new OneModel(7, TwoSequences());

        float[]? d = new StudioSequenceTiming(models).DefaultDurations(7);

        Assert.NotNull(d);
        Assert.Equal(2, d!.Length);
        Assert.Equal(new[] { 7 }, models.Asked);
    }

    [Fact]
    public void AModelTheCacheCannotOpenAnswersNull()
    {
        var models = new OneModel(7, TwoSequences());

        Assert.Null(new StudioSequenceTiming(models).DefaultDurations(8));
    }

    [Fact]
    public void ANonPositiveIndexIsNotAsked()
    {
        var models = new OneModel(0, TwoSequences());

        Assert.Null(new StudioSequenceTiming(models).DefaultDurations(0));
        Assert.Empty(models.Asked);
    }
}
