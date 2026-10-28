using System;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ServerBoneDriver"/>, hand-derived from
/// CStandaloneEntity's skeleton at the 1.0b base
/// (shell_standalone_hierarchy.cpp 173-403 and 455-568). The server opens
/// the studio header on every ask, so "the cache answered" is shown by a
/// sentinel written over a cached bone surviving the ask.
/// </summary>
public class BonesServerDriverTests
{
    private const int Model = 1;

    private static BoneConVars Cv => BoneConVars.Stock;

    private static (FakeBoneWorld W, ServerBoneDriver D, BoneEntityState S) Rig(StudioHdr? hdr)
    {
        FakeBoneWorld w = new();
        if (hdr is not null)
        {
            w.Models[Model] = hdr;
        }

        return (w, new ServerBoneDriver(w), new BoneEntityState());
    }

    private static ServerBonePose Pose(Vector? origin = null, float yaw = 30f) => new()
    {
        ModelIndex = Model,
        AbsOrigin = origin ?? new Vector(100f, 0f, 0f),
        AbsAngles = new BoneAngle(0f, yaw, 0f),
    };

    /// <summary>Builds once, then plants a sentinel over bone 0.</summary>
    private static void BuildAndMark(ServerBoneDriver d, BoneEntityState s, float now = 1f)
    {
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, now, Cv));
        s.CachedBones[0] = Sentinel;
    }

    private static float[] Defaults(StudioHdr hdr)
    {
        float[] pose = new float[BoneLibrary.MaxStudioPoseParam];
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, BoneLibrary.MaxStudioPoseParam);
        return pose;
    }

    // ---- RefreshModelPoseState -------------------------------------------------

    [Fact]
    public void RefreshRecordsAModelWithNoStudioDataSoItIsNotReasked()
    {
        (FakeBoneWorld w, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        s.ModelHasAutoplay = true;
        d.RefreshModelPoseState(s, Model);
        d.RefreshModelPoseState(s, Model);
        Assert.Equal((Model, false, 1), (s.PoseDefaultsFor, s.ModelHasAutoplay, w.Opens));
    }

    [Fact]
    public void RefreshReadsWhetherTheModelAutoplays()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(AutoplayChain());
        d.RefreshModelPoseState(s, Model);
        Assert.True(s.ModelHasAutoplay);
    }

    [Fact]
    public void RefreshCopiesThePoseDefaultsWithNoGeneration()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(PosedChain());
        d.RefreshModelPoseState(s, Model);
        Assert.Equal((0.25f, 0.5f, 0), (s.PoseParameter[0], s.PoseParameter[23], s.PoseGeneration));
    }

    [Fact]
    public void RefreshOverAnUnreadableModelKeepsThePose()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        s.PoseParameter[0] = 0.75f;
        d.RefreshModelPoseState(s, Model);
        Assert.Equal(0.75f, s.PoseParameter[0]);
    }

    // ---- EnsureBones: the cache ------------------------------------------------

    [Fact]
    public void NoStudioHeaderRefusesBeforeTheMaskWidens()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        Assert.False(d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv));
        Assert.Equal((0, -1), (s.BoneMaskAsked, s.PoseDefaultsFor));
    }

    [Fact]
    public void TheMaskAccumulatesAndTheKeyRecordsTheMaskEvaluated()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        d.EnsureBones(s, Pose(), UsedByAttachment, 1f, Cv);
        Assert.Equal((UsedByHitbox | UsedByAttachment, UsedByHitbox | UsedByAttachment),
                     (s.BoneMaskAsked, s.CacheKey.BoneMask));
    }

    [Fact]
    public void ARepeatedAskIsAnsweredFromTheCache()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv));
        Assert.Equal(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void AMovedEntityRebuilds()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        d.EnsureBones(s, Pose(new Vector(101f, 0f, 0f)), UsedByAnything, 1f, Cv);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void ATurnedEntityRebuilds()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        d.EnsureBones(s, Pose(yaw: 31f), UsedByAnything, 1f, Cv);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void ANewCycleRebuilds()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.SeqDuration = 1f;
        s.SeqLooping = true;
        BuildAndMark(d, s, 0.25f);
        d.EnsureBones(s, Pose(), UsedByAnything, 0.5f, Cv);
        Assert.Equal(0.5f, s.CacheKey.Cycle);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void AModelThatDoesNotAutoplayCachesAcrossTime()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s, 1f);
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Cv);
        Assert.Equal(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void AnAutoplayModelKeysOnTheClock()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(AutoplayChain());
        BuildAndMark(d, s, 1f);
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Cv);
        Assert.Equal(2f, s.CacheKey.AutoplayTime);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void ThePoseGenerationIsNotInTheServersKey()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        s.PoseGeneration = 9;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal((0, Sentinel), (s.CacheKey.PoseGeneration, s.CachedBones[0]));
    }

    [Fact]
    public void ANewScaleRebuilds()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        Assert.Equal(ModelScaleChange.Changed, s.SetModelScale(2f));
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(2f, s.CacheKey.ModelScale);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void AnInvalidatedCacheRebuilds()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        BuildAndMark(d, s);
        s.InvalidateBoneCache();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.NotEqual(Sentinel, s.CachedBones[0]);
    }

    [Fact]
    public void ARebuildInvalidatesTheAttachments()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.AttachmentsValid = true;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.False(s.AttachmentsValid);
    }

    // ---- EnsureBones: the build --------------------------------------------------

    private static BoneMatrix3x4[] BuildBones(StudioHdr hdr, in ServerBonePose pose, int mask, float now,
                                              bool autoplay = false, float scale = 1f, float cycle = 0f)
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement = new BonePlacement { Origin = pose.AbsOrigin, Angles = pose.AbsAngles, ModelScale = scale };
        inputs.BoneMask = mask;
        inputs.Cycle = cycle;
        inputs.CurTime = now;
        inputs.HasAutoplay = autoplay;
        inputs.PoseParameter = Defaults(hdr);
        BoneMatrix3x4[] world = new BoneMatrix3x4[hdr.NumBones];
        Assert.Equal(hdr.NumBones, BoneLibrary.BuildBones(hdr, inputs, world));
        return world;
    }

    [Fact]
    public void TheBonesAreTheLibrarysBuildInTheServersShape()
    {
        StudioHdr hdr = Chain();
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.SeqDuration = 1f;
        s.SeqLooping = true;
        d.EnsureBones(s, Pose(), UsedByAnything, 0.25f, Cv);
        Assert.Equal(BuildBones(hdr, Pose(), UsedByAnything, 0.25f, cycle: 0.25f), s.CachedBones);
        Assert.NotEqual(BuildBones(hdr, Pose(), UsedByAnything, 0.25f, cycle: 0f), s.CachedBones);
    }

    [Fact]
    public void AnAutoplayModelIsBuiltWithItsAutoplayLayers()
    {
        StudioHdr hdr = AutoplayChain();
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(hdr);
        d.EnsureBones(s, Pose(), UsedByAnything, 0.02f, Cv);
        Assert.Equal(BuildBones(hdr, Pose(), UsedByAnything, 0.02f, autoplay: true), s.CachedBones);
        Assert.NotEqual(BuildBones(hdr, Pose(), UsedByAnything, 0.02f, autoplay: false), s.CachedBones);
    }

    [Fact]
    public void AStaticPropWritesTheScaledPlacementIntoEveryBone()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(StaticProp());
        s.SetModelScale(3f);
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        BoneMatrix3x4 expected = BoneLibraryMath.PlacementToWorld(
            new BonePlacement { Origin = Pose().AbsOrigin, Angles = Pose().AbsAngles, ModelScale = 3f });
        Assert.Equal([expected, expected, expected], s.CachedBones);
    }

    // ---- EnsureAttachments / GetAttachment ---------------------------------------

    [Fact]
    public void ValidAttachmentsAnswerWithoutABuild()
    {
        (FakeBoneWorld w, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        s.AttachmentsValid = true;
        s.Attachments = new BoneMatrix3x4[1];
        Assert.True(d.EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.Equal(0, w.Opens);
    }

    [Fact]
    public void AttachmentsAreComposedOnTheBones()
    {
        StudioHdr hdr = AttachedChain();
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.SetModelScale(2f);
        Assert.True(d.EnsureAttachments(s, Pose(), 1f, Cv));
        BoneMatrix3x4[] expected = new BoneMatrix3x4[2];
        BonePlacement unscaled = new() { Origin = Pose().AbsOrigin, Angles = Pose().AbsAngles, ModelScale = 1f };
        BoneLibrary.BuildAttachments(hdr, s.CachedBones, unscaled, expected);
        Assert.Equal(expected, s.Attachments);
        Assert.Equal(UsedByAttachment, s.BoneMaskAsked);
    }

    [Fact]
    public void AnAttachmentOnABoneTheModelLacksCollapsesOntoTheUnscaledEntity()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "stray", LocalBone = 7 });
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Make(b));
        s.SetModelScale(2f);
        d.EnsureAttachments(s, Pose(), 1f, Cv);
        Assert.Equal(BoneSetupMath.AngleMatrix(Pose().AbsAngles, Pose().AbsOrigin), s.Attachments[0]);
    }

    [Fact]
    public void AModelWithNoAttachmentsIsMarkedValidAndAnswersFalse()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        Assert.False(d.EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.True(s.AttachmentsValid);
    }

    [Fact]
    public void NoModelHasNoAttachments()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        Assert.False(d.EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.False(s.AttachmentsValid);
    }

    [Fact]
    public void AFailedAttachmentReadAnswersTheEntitysOwnPlace()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        Assert.False(d.GetAttachment(s, Pose(), 1, 1f, Cv, out Vector origin, out BoneAngle angles));
        Assert.Equal((Pose().AbsOrigin, 30f), (origin, angles.Yaw));
    }

    [Fact]
    public void AttachmentZeroAndOnePastTheEndAreNone()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(AttachedChain());
        Assert.False(d.GetAttachment(s, Pose(), 0, 1f, Cv, out _, out _));
        Assert.False(d.GetAttachment(s, Pose(), 3, 1f, Cv, out Vector origin, out _));
        Assert.Equal(Pose().AbsOrigin, origin);
    }

    [Fact]
    public void AnAttachmentIsOneBased()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(AttachedChain());
        Assert.True(d.GetAttachment(s, Pose(), 1, 1f, Cv, out Vector origin, out BoneAngle angles));
        BoneAngle expected = BoneSetupMath.MatrixAngles(s.Attachments[0]);
        Assert.Equal((s.Attachments[0].Position, expected.Pitch, expected.Yaw, expected.Roll),
                     (origin, angles.Pitch, angles.Yaw, angles.Roll));
        Assert.NotEqual(s.Attachments[1].Position, origin);
    }

    [Fact]
    public void AttachmentTwoIsTheSecondRecord()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(AttachedChain());
        Assert.True(d.GetAttachment(s, Pose(), 2, 1f, Cv, out Vector origin, out _));
        Assert.Equal(s.Attachments[1].Position, origin);
        Assert.NotEqual(s.Attachments[0].Position, origin);
    }

    // ---- BoneCount ---------------------------------------------------------------

    [Fact]
    public void BoneCountIsTheModelsAndTheCachesCount()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.CachedBones = new BoneMatrix3x4[2];
        Assert.Equal((3, 2), (d.BoneCount(s, Model, out int cached), cached));
    }

    [Fact]
    public void BoneCountWithNoModelIsZero()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        Assert.Equal(0, d.BoneCount(s, Model, out _));
    }

    // ---- SetPoseSequence / PoseCycle -----------------------------------------------

    [Fact]
    public void ANonPositiveRateAndANegativeSequenceAreNormalised()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Sequence = 3;
        d.SetPoseSequence(s, Model, -4, 0f, false, 2f);
        Assert.Equal((0, 1f), (s.Sequence, s.PlaybackRate));
    }

    [Fact]
    public void AStartLetsGoOfAHeldCycleAndKeepingTheSequenceKeepsIt()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.HeldCycle = 0.4f;
        d.SetPoseSequence(s, Model, 0, 1f, false, 5f);
        Assert.Equal(0.4f, s.HeldCycle);

        d.SetPoseSequence(s, Model, 0, 1f, true, 6f);
        Assert.Null(s.HeldCycle);
    }

    [Fact]
    public void ThePlayingSequenceKeepsItsClockAndItsCache()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        s.AnimTime = 1f;
        d.SetPoseSequence(s, Model, 0, 2f, false, 5f);
        Assert.Equal((1f, 2f, Model), (s.AnimTime, s.PlaybackRate, s.CacheKey.ModelIndex));
    }

    [Fact]
    public void ANewSequenceReadsItsFactsAtTheModelsDefaultsAndInvalidates()
    {
        StudioHdr hdr = Chain(seqFlags: 0);
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(hdr);
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        s.Sequence = 5;
        s.SeqLooping = true;
        d.SetPoseSequence(s, Model, 0, 1f, false, 5f);
        Assert.True(BoneLibrary.SequenceFacts(hdr, 0, null, out float duration, out bool looping));
        Assert.Equal((duration, looping, 5f, -1), (s.SeqDuration, s.SeqLooping, s.AnimTime, s.CacheKey.ModelIndex));
        Assert.True(duration > 0f);
    }

    [Fact]
    public void ALoopingSequenceIsReadAsLooping()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Sequence = 5;
        d.SetPoseSequence(s, Model, 0, 1f, false, 5f);
        Assert.True(s.SeqLooping);
    }

    [Fact]
    public void ARestartRestartsTheClock()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.AnimTime = 1f;
        d.SetPoseSequence(s, Model, 0, 1f, true, 5f);
        Assert.Equal(5f, s.AnimTime);
    }

    [Fact]
    public void ANewSequenceOnAnUnreadableModelHasNoDurationAndStillInvalidates()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        s.Sequence = 5;
        s.SeqDuration = 3f;
        s.CacheKey.ModelIndex = Model;
        s.AttachmentsValid = true;
        d.SetPoseSequence(s, Model, 0, 1f, false, 5f);
        Assert.Equal((0f, -1, false), (s.SeqDuration, s.CacheKey.ModelIndex, s.AttachmentsValid));
    }

    [Fact]
    public void PoseCycleIsTheServerClocksCycle()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.SeqDuration = 2f;
        s.AnimTime = 1f;
        s.SeqLooping = true;
        Assert.Equal(0.5f, d.PoseCycle(s, 4f));
    }

    // ---- TestHitboxes --------------------------------------------------------------

    [Fact]
    public void NoModelHasNoHitboxes()
    {
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(null);
        BoneRay ray = BoneRay.FromPoints(new Vector(-1000f, 0f, 0f), new Vector(1000f, 0f, 0f));
        Assert.False(d.TestHitboxes(s, Pose(), 0, ray, -1, new BoneTrace(), null, 1f, Cv));
    }

    [Fact]
    public void TheRayIsTestedAgainstTheHitboxBonesAtTheEntitysOriginAndScale()
    {
        StudioHdr hdr = HitboxChain();
        (_, ServerBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.SetModelScale(2f);
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        Vector hand = s.CachedBones[2].Position;
        BoneRay ray = BoneRay.FromPoints(hand + new Vector(0f, 0f, 100f), hand - new Vector(0f, 0f, 100f));
        BoneTrace tr = new();
        bool tested = d.TestHitboxes(s, Pose(), 0, ray, -1, tr, null, 1f, Cv);

        BoneTrace expected = new();
        bool expectedTested = BoneLibrary.TraceHitboxes(null, hdr, 0, ray, -1, s.CachedBones, Pose().AbsOrigin, 2f, expected);
        Assert.Equal((expectedTested, expected.Fraction, expected.HitBox), (tested, tr.Fraction, tr.HitBox));
        Assert.Equal(UsedByHitbox, s.BoneMaskAsked);
        Assert.True(tr.Fraction < 1f, "the ray must reach the box");
    }
}
