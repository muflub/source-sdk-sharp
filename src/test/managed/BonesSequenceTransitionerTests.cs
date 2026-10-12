using System;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="SequenceTransitioner"/> and its use in
/// <see cref="ClientBoneDriver"/>'s pose build, hand-derived from stock:
/// CSequenceTransitioner (game/shared/sequence_Transitioner.cpp 17-114),
/// C_AnimationLayer::GetFadeout (game/client/animationlayer.h 84-106),
/// C_BaseAnimating::MaintainSequenceTransitions (c_baseanimating.cpp
/// 1815-1864), its call in StandardBlendingRules (1985-1992), ClampCycle
/// (1431-1447), GetSequenceCycleRate (5258-5267) and the RemoveAll in
/// OnNewModel (1026).
/// </summary>
public class BonesSequenceTransitionerTests
{
    private const int Model = 1;
    private const int OtherModel = 2;

    private const int Ramp = 0;   // looping, fade in 0.4, fade out 0.3
    private const int Steep = 1;  // looping, fade in 0.25, fade out 0.2
    private const int Snap = 2;   // looping, STUDIO_SNAP
    private const int Once = 3;   // not looping, fade in 0.4, fade out 0.2
    private const int Instant = 4; // looping, fade in 0, fade out 0

    /// <summary>The chain with five sequences whose fades differ, so each
    /// fade-time rule picks a different value.</summary>
    private static StudioHdr Fades()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Sequences[0].FadeInTime = 0.4f;
        b.Sequences[0].FadeOutTime = 0.3f;
        b.Anims.Add(BonesDriverModels.Ramp(0, 3, 30f, 40, "steep"));
        b.Anims.Add(BonesDriverModels.Ramp(0, 3, 30f, -30, "back"));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "steep", AnimTable = [1], Flags = StudioConstants.STUDIO_LOOPING,
            FadeInTime = 0.25f, FadeOutTime = 0.2f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "snap", AnimTable = [2], Flags = StudioConstants.STUDIO_LOOPING | StudioConstants.STUDIO_SNAP,
            FadeInTime = 0.2f, FadeOutTime = 0.2f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "once", AnimTable = [1], Flags = 0, FadeInTime = 0.4f, FadeOutTime = 0.2f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "instant", AnimTable = [2], Flags = StudioConstants.STUDIO_LOOPING,
        });
        return Make(b);
    }

    private static readonly StudioHdr Hdr = Fades();

    /// <summary>One StandardBlendingRules' worth of the two calls, as stock
    /// sequences them.</summary>
    private static void Step(SequenceTransitioner t, int sequence, bool force, float cycle, float now,
                             bool interpolate = true, float rate = 1f)
    {
        t.CheckForSequenceChange(Hdr, sequence, force, interpolate);
        t.UpdateCurrent(Hdr, sequence, cycle, rate, now);
    }

    /// <summary>GetFadeout, written out from animationlayer.h 84-106.</summary>
    private static float StockFadeout(float fadeOut, float animTime, float now)
    {
        if (fadeOut <= 0.0f)
            return 0f;
        float s = (float)(1.0 - (now - animTime) / fadeOut);
        if (s > 0 && s <= 1.0)
            s = 3 * s * s - 2 * s * s * s;
        else if (s > 1.0f)
            s = 1.0f;
        return s;
    }

    /// <summary>ClampCycle, c_baseanimating.cpp 1431-1447.</summary>
    private static float StockClampCycle(float cycle, bool looping)
    {
        if (looping)
        {
            cycle -= (int)cycle;
            if (cycle < 0.0f)
                cycle += 1.0f;
            return cycle;
        }

        return Math.Clamp(cycle, 0.0f, 0.999f);
    }

    private static float[] Defaults()
    {
        float[] pose = new float[BoneLibrary.MaxStudioPoseParam];
        BoneSetupHdrRead.CalcDefaultPoseParameters(Hdr, pose, BoneLibrary.MaxStudioPoseParam);
        return pose;
    }

    /// <summary>GetSequenceCycleRate: 1 / SequenceDuration, or 0.</summary>
    private static float CycleRate(int sequence)
    {
        float t = BoneSetupHdrRead.Duration(Hdr, sequence, Defaults());
        return t != 0.0f ? 1.0f / t : t;
    }

    // ---- CheckForSequenceChange --------------------------------------------------

    [Fact]
    public void ASequenceChangeQueuesThePreviousSequence()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.05f);
        Assert.Equal((2, Ramp, Steep), (t.Count, t[0].Sequence, t[1].Sequence));
    }

    [Fact]
    public void AnUnchangedSequenceKeepsOneEntry()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Ramp, false, 0.5f, 1.05f);
        Assert.Equal((1, Ramp), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void AForcedChangeOfTheSameSequenceQueuesARestart()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Ramp, true, 0f, 1.05f);
        Assert.Equal((2, Ramp, 0.25f, Ramp, 0f), (t.Count, t[0].Sequence, t[0].Cycle, t[1].Sequence, t[1].Cycle));
    }

    [Fact]
    public void ASequenceNeverUpdatedIsNotQueued()
    {
        // The tail's m_flLayerAnimtime is still 0: nothing was drawn to fade from.
        SequenceTransitioner t = new();
        t.CheckForSequenceChange(Hdr, Ramp, false, true);
        Step(t, Steep, true, 0f, 1.0f);
        Assert.Equal((1, Steep), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void ANoInterpolationChangeDropsEveryQueuedSequence()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.02f);
        Assert.Equal(2, t.Count);
        Step(t, Ramp, false, 0f, 1.04f, interpolate: false);
        Assert.Equal((1, Ramp), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void AChangeToASnapSequenceDropsEveryQueuedSequence()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.02f);
        Assert.Equal(2, t.Count);
        Step(t, Snap, false, 0f, 1.04f);
        Assert.Equal((1, Snap), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void TheFadeIsTheNewSequencesFadeInWhenThatIsShorter()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);   // fade out 0.3
        Step(t, Steep, false, 0f, 1.0f);     // fade in 0.25
        Assert.Equal(0.25f, t[0].LayerFadeOuttime);
    }

    [Fact]
    public void TheFadeIsTheOldSequencesFadeOutWhenThatIsShorter()
    {
        SequenceTransitioner t = new();
        Step(t, Steep, false, 0.25f, 1.0f);  // fade out 0.2
        Step(t, Ramp, false, 0f, 1.0f);      // fade in 0.4
        Assert.Equal(0.2f, t[0].LayerFadeOuttime);
    }

    [Fact]
    public void TheQueuedSequenceKeepsTheClockOfItsLastUpdate()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f, rate: 2f);
        Step(t, Steep, false, 0f, 1.05f);
        Assert.Equal((1.0f, 0.25f, 2f), (t[0].LayerAnimtime, t[0].Cycle, t[0].PlaybackRate));
    }

    // ---- UpdateCurrent / GetFadeout ------------------------------------------------

    [Fact]
    public void AQueuedWeightFollowsStocksSplineOverTheFade()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.1f);
        Assert.Equal(StockFadeout(0.25f, 1.0f, 1.1f), t[0].Weight);
        Assert.InRange(t[0].Weight, 0.6f, 0.7f); // 0.648: the spline of 0.6, not 0.6
    }

    [Fact]
    public void AWeightIsOneWhileTheClockIsBehindTheChange()
    {
        TransitionBlend blend = new() { LayerAnimtime = 2f, LayerFadeOuttime = 0.25f };
        Assert.Equal(1f, blend.GetFadeout(1.5f));
    }

    [Fact]
    public void ANonPositiveFadeTimeFadesToNothing()
    {
        TransitionBlend blend = new() { LayerAnimtime = 1f, LayerFadeOuttime = 0f };
        Assert.Equal(0f, blend.GetFadeout(1f));
    }

    [Fact]
    public void AQueuedSequenceIsTrimmedWhenItsWeightReachesZero()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.2f);
        Assert.Equal(2, t.Count);
        Step(t, Steep, false, 0.1f, 1.25f);
        Assert.Equal((1, Steep), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void AChangeWithNoFadeTimeIsTrimmedAtOnce()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Instant, false, 0f, 1.0f);   // fade in 0
        Assert.Equal((1, Instant), (t.Count, t[0].Sequence));
    }

    [Fact]
    public void TrimmingKeepsTheSurvivorsInOrder()
    {
        // Ramp fades over 0.25 from 1.0; Steep over 0.2 from 1.2; Once is current.
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.2f);
        Step(t, Once, false, 0f, 1.21f);
        Assert.Equal(3, t.Count);
        Step(t, Once, false, 0.1f, 1.3f);
        Assert.Equal((2, Steep, Once), (t.Count, t[0].Sequence, t[1].Sequence));
    }

    // ---- RemoveAll ---------------------------------------------------------------------

    [Fact]
    public void RemoveAllEmptiesTheQueue()
    {
        SequenceTransitioner t = new();
        Step(t, Ramp, false, 0.25f, 1.0f);
        Step(t, Steep, false, 0f, 1.05f);
        Assert.Equal(2, t.Count);
        t.RemoveAll();
        Assert.Equal(0, t.Count);
    }

    // ---- MaintainSequenceTransitions ---------------------------------------------------

    private static int Maintain(SequenceTransitioner t, int sequence, float cycle, int parity, float now,
                                out BoneLayerPose[] previous, bool noInterp = false, bool inPrediction = false,
                                float rate = 1f)
    {
        previous = [];
        return t.Maintain(Hdr, sequence, cycle, rate, parity, noInterp, inPrediction, now, Defaults(), ref previous);
    }

    [Fact]
    public void InPredictionTheParityIsTakenAndNothingIsQueuedOrBlended()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _);
        int count = Maintain(t, Steep, 0f, 2, 1.05f, out _, inPrediction: true);
        Assert.Equal((0, 2, 1, Ramp, 1.0f), (count, t.PrevNewSequenceParity, t.Count, t[0].Sequence, t[0].LayerAnimtime));
    }

    [Fact]
    public void AParityChangeIsActedOnOnce()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _);
        Maintain(t, Ramp, 0f, 2, 1.05f, out _);
        Assert.Equal(2, t.Count);
        Maintain(t, Ramp, 0.1f, 2, 1.1f, out _);
        Assert.Equal(2, t.Count);
    }

    [Fact]
    public void ANoInterpolationFrameBlendsNothing()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _);
        int count = Maintain(t, Steep, 0f, 2, 1.05f, out _, noInterp: true);
        Assert.Equal((0, 1), (count, t.Count));
    }

    [Fact]
    public void ThePreviousSequencesAreListedNewestFirst()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _);
        Maintain(t, Steep, 0f, 2, 1.05f, out _);
        int count = Maintain(t, Once, 0f, 3, 1.1f, out BoneLayerPose[] previous);
        Assert.Equal((2, Steep, Ramp), (count, previous[0].Sequence, previous[1].Sequence));
    }

    [Fact]
    public void APreviousSequenceBlendsAtItsQueuedWeight()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _);
        Maintain(t, Steep, 0f, 2, 1.1f, out BoneLayerPose[] previous);
        Assert.Equal(t[0].Weight, previous[0].Weight);
        Assert.Equal(StockFadeout(0.25f, 1.0f, 1.1f), previous[0].Weight);
    }

    [Fact]
    public void AQueuedCycleAdvancesAtItsOwnRate()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.25f, 1, 1.0f, out _, rate: 0.5f);
        Maintain(t, Steep, 0f, 2, 1.02f, out BoneLayerPose[] previous, rate: 3f);
        float dt = 1.02f - 1.0f;
        float expected = StockClampCycle(0.25f + dt * 0.5f * CycleRate(Ramp), true);
        Assert.Equal(expected, previous[0].Cycle);
        Assert.NotEqual(0.25f, previous[0].Cycle);
    }

    [Fact]
    public void ALoopingQueuedCycleWraps()
    {
        SequenceTransitioner t = new();
        Maintain(t, Ramp, 0.9f, 1, 1.0f, out _);
        Maintain(t, Steep, 0f, 2, 1.01f, out BoneLayerPose[] previous);
        float raw = 0.9f + (1.01f - 1.0f) * 1f * CycleRate(Ramp);
        Assert.True(raw > 1f);
        Assert.Equal(StockClampCycle(raw, true), previous[0].Cycle);
    }

    [Fact]
    public void ANonLoopingQueuedCycleClampsShortOfTheEnd()
    {
        SequenceTransitioner t = new();
        Maintain(t, Once, 0.9f, 1, 1.0f, out _);
        Maintain(t, Ramp, 0f, 2, 1.01f, out BoneLayerPose[] previous);
        Assert.Equal(0.999f, previous[0].Cycle);
    }

    // ---- the no-interpolation frame -----------------------------------------------------

    [Fact]
    public void AMovedInterpolationFrameIsANoInterpolationFrameOnce()
    {
        SequenceTransitioner t = new();
        Assert.Equal((false, true, false),
                     (t.TakeNoInterpolationFrame(0), t.TakeNoInterpolationFrame(1), t.TakeNoInterpolationFrame(1)));
    }

    // ---- the blend order (StandardBlendingRules 1985-1992) -------------------------------

    private static (Vector[] Pos, Quaternion[] Q) Manual(params (int Seq, float Cycle, float Weight)[] steps)
    {
        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        CBoneSetup setup = new(Hdr, UsedByAnything, Defaults());
        setup.InitPose(pos, q);
        foreach ((int seq, float cycle, float weight) in steps)
            setup.AccumulatePose(pos, q, seq, cycle, weight, 1f, null, true);
        return (pos, q);
    }

    [Fact]
    public void PreviousSequencesBlendAfterTheBaseAndBeforeTheLayers()
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.BoneMask = UsedByAnything;
        inputs.Sequence = Ramp;
        inputs.Cycle = 0.5f;
        inputs.CurTime = 1f;
        inputs.PoseParameter = Defaults();
        inputs.Previous = [new BoneLayerPose(Steep, 0.25f, 0.6f)];
        inputs.PreviousCount = 1;
        inputs.Layers = [new BoneLayerPose(Instant, 0.75f, 0.5f)];
        inputs.LayerCount = 1;

        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        Assert.True(BoneLibrary.AccumulatePose(Hdr, inputs, pos, q));

        (Vector[] want, _) = Manual((Ramp, 0.5f, 1f), (Steep, 0.25f, 0.6f), (Instant, 0.75f, 0.5f));
        (Vector[] layerFirst, _) = Manual((Ramp, 0.5f, 1f), (Instant, 0.75f, 0.5f), (Steep, 0.25f, 0.6f));
        Assert.NotEqual(want[0], layerFirst[0]); // the order is visible in the root
        Assert.Equal(want[0], pos[0]);
    }

    [Fact]
    public void APreviousCountOfZeroBlendsNothing()
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.BoneMask = UsedByAnything;
        inputs.Sequence = Ramp;
        inputs.Cycle = 0.5f;
        inputs.PoseParameter = Defaults();
        inputs.Previous = [new BoneLayerPose(Steep, 0.25f, 0.6f)];

        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        Assert.True(BoneLibrary.AccumulatePose(Hdr, inputs, pos, q));
        Assert.Equal(Manual((Ramp, 0.5f, 1f)).Pos[0], pos[0]);
    }

    // ---- through ClientBoneDriver ------------------------------------------------------

    private static (FakeBoneWorld W, ClientBoneDriver D, BoneEntityState S) Rig()
    {
        FakeBoneWorld w = new();
        w.Models[Model] = Hdr;
        w.Models[OtherModel] = Chain();
        return (w, new ClientBoneDriver(w), new BoneEntityState());
    }

    private static ClientBonePose Pose(int interpolationFrame = 0) => new()
    {
        ModelIndex = Model,
        NetOrigin = new Vector(100f, 0f, 0f),
        NetAngles = new BoneAngle(0f, 30f, 0f),
        RenderOrigin = new Vector(-5f, -6f, -7f),
        RenderAngles = new BoneAngle(0f, 90f, 0f),
        FrameCount = 7,
        InterpolationFrame = interpolationFrame,
    };

    private static BoneMatrix3x4[] Reference(int sequence, float cycle, float now, BoneLayerPose[]? previous = null)
    {
        ClientBonePose pose = Pose();
        BonePlacement placement = new() { Origin = pose.NetOrigin, Angles = pose.NetAngles, ModelScale = 1f };
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement = placement;
        inputs.BoneMask = UsedByAnything;
        inputs.Sequence = sequence;
        inputs.Cycle = cycle;
        inputs.CurTime = now;
        inputs.PoseParameter = Defaults();
        inputs.AutoplayIkAlways = true;
        inputs.FrameCount = 7;
        inputs.Previous = previous;
        inputs.PreviousCount = previous?.Length ?? 0;

        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        Assert.True(BoneLibrary.AccumulatePose(Hdr, inputs, pos, q));
        BoneMatrix3x4[] world = new BoneMatrix3x4[Hdr.NumBones];
        BoneLibrary.PoseToWorldProcedural(Hdr, placement, pos, q, UsedByAnything, now, null, null, world);
        return world;
    }

    /// <summary>Ramp from 0, built at 1.0, then Steep from 1.0, built at <paramref name="now"/>.</summary>
    private static (BoneEntityState S, float RampCycle) ChangeToSteep(ClientBoneDriver d, BoneEntityState s,
                                                                      float now, int frameAtChange = 0)
    {
        d.SetPlayingSequence(s, Model, Ramp, 1f, true, 0f);
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1.0f, BoneConVars.Stock));
        float rampCycle = s.SequenceCycle(1.0f);
        d.SetPlayingSequence(s, Model, Steep, 1f, false, 1.0f);
        Assert.True(d.EnsureBones(s, Pose(frameAtChange), UsedByAnything, now, BoneConVars.Stock));
        return (s, rampCycle);
    }

    [Fact]
    public void ADriverSequenceChangeBlendsThePreviousSequenceOut()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        (_, float rampCycle) = ChangeToSteep(d, s, 1.05f);

        float weight = StockFadeout(0.25f, 1.0f, 1.05f);
        float cycle = StockClampCycle(rampCycle + (1.05f - 1.0f) * 1f * CycleRate(Ramp), true);
        BoneMatrix3x4[] want = Reference(Steep, s.SequenceCycle(1.05f), 1.05f, [new BoneLayerPose(Ramp, cycle, weight)]);
        BoneMatrix3x4[] plain = Reference(Steep, s.SequenceCycle(1.05f), 1.05f);

        Assert.NotEqual(plain[0], want[0]);
        Assert.Equal(want[0], s.CachedBones[0]);
    }

    [Fact]
    public void ADriverRestartOfTheSameSequenceQueuesIt()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.SetPlayingSequence(s, Model, Ramp, 1f, true, 0f);
        d.EnsureBones(s, Pose(), UsedByAnything, 1.0f, BoneConVars.Stock);
        d.SetPlayingSequence(s, Model, Ramp, 1f, true, 1.0f);
        d.EnsureBones(s, Pose(), UsedByAnything, 1.05f, BoneConVars.Stock);
        Assert.Equal((2, Ramp), (s.Transitions.Count, s.Transitions[0].Sequence));
    }

    [Fact]
    public void ADriverTeleportFrameDoesNotBlend()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        ChangeToSteep(d, s, 1.05f, frameAtChange: 1);
        Assert.Equal(1, s.Transitions.Count);
        Assert.Equal(Reference(Steep, s.SequenceCycle(1.05f), 1.05f)[0], s.CachedBones[0]);
    }

    [Fact]
    public void ADriverTransitionKeysOnTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        ChangeToSteep(d, s, 1.05f);
        d.EnsureBones(s, Pose(), UsedByAnything, 1.06f, BoneConVars.Stock);
        Assert.Equal(1.06f, s.CacheKey.AutoplayTime);
    }

    [Fact]
    public void ADriverModelChangeDropsTheQueue()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        ChangeToSteep(d, s, 1.05f);
        Assert.Equal(2, s.Transitions.Count);
        d.RefreshModelPoseState(s, OtherModel);
        Assert.Equal(0, s.Transitions.Count);
    }

    [Fact]
    public void ADriverWithNoChangeBuildsTheBaseSequenceAlone()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.SetPlayingSequence(s, Model, Ramp, 1f, true, 0f);
        d.EnsureBones(s, Pose(), UsedByAnything, 1.0f, BoneConVars.Stock);
        d.EnsureBones(s, Pose(), UsedByAnything, 1.05f, BoneConVars.Stock);
        Assert.Equal(1, s.Transitions.Count);
        Assert.Equal(Reference(Ramp, s.SequenceCycle(1.05f), 1.05f)[0], s.CachedBones[0]);
    }
}
