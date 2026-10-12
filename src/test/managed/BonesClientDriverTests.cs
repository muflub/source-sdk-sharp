using System;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ClientBoneDriver"/>'s own build: RefreshModelPoseState,
/// EnsureBones, EnsureAttachments, SetupBones, TestHitboxes,
/// SetPlayingSequence and SetAnimLayer. Each is hand-derived from
/// C_StandaloneEntity at the 1.0b base (shell_standalone_client.cpp
/// 1615-1960, 2023-2045, 2189-2238, 4864-4972, 5102-5184). Where a fact
/// compares bones, the expectation is the native body's own sequence of
/// BoneLibrary calls, written out again here (Reference); a fact that a
/// branch matters also shows the two arms differ.
/// </summary>
public class BonesClientDriverTests
{
    private const int Model = 1;

    private static BoneConVars Cv => BoneConVars.Stock;

    private static (FakeBoneWorld W, ClientBoneDriver D, BoneEntityState S) Rig(StudioHdr? hdr)
    {
        FakeBoneWorld w = new();
        w.Models[Model] = hdr;
        return (w, new ClientBoneDriver(w), new BoneEntityState());
    }

    private static ClientBonePose Pose(Vector? origin = null, bool viewModel = false) => new()
    {
        ModelIndex = Model,
        ViewModel = viewModel,
        NetOrigin = origin ?? new Vector(100f, 0f, 0f),
        NetAngles = new BoneAngle(0f, 30f, 0f),
        RenderOrigin = new Vector(-5f, -6f, -7f),
        RenderAngles = new BoneAngle(0f, 90f, 0f),
        FrameCount = 7,
    };

    private static BonePlacement Placement(in ClientBonePose pose, float scale = 1f) =>
        new() { Origin = pose.NetOrigin, Angles = pose.NetAngles, ModelScale = scale };

    private static float[] Defaults(StudioHdr hdr)
    {
        float[] pose = new float[BoneLibrary.MaxStudioPoseParam];
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, BoneLibrary.MaxStudioPoseParam);
        return pose;
    }

    /// <summary>EnsureBones' poseable branch as the native body sequences it.</summary>
    private static BoneMatrix3x4[] Reference(StudioHdr hdr, in BonePlacement placement, int mask, int sequence,
                                             float cycle, float now, bool ik = false, bool solve = false,
                                             BoneLayerPose[]? layers = null, int ownedBone = -1,
                                             BoneMatrix3x4 ownedMatrix = default, bool hasAutoplay = false)
    {
        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        CIKContext? ctx = ik ? new CIKContext() : null;
        ctx?.Init(hdr, BoneSetupMath.AngleMatrix(placement.Angles, placement.Origin), mask, now, 7);

        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement = placement;
        inputs.BoneMask = mask;
        inputs.Sequence = sequence;
        inputs.Cycle = cycle;
        inputs.CurTime = now;
        inputs.HasAutoplay = hasAutoplay;
        inputs.Layers = layers;
        inputs.LayerCount = layers?.Length ?? 0;
        inputs.PoseParameter = Defaults(hdr);
        inputs.AutoplayIkAlways = true;
        inputs.FrameCount = 7;
        Assert.True(BoneLibrary.AccumulatePose(hdr, inputs, pos, q, ctx));

        BoneMatrix3x4[] world = new BoneMatrix3x4[hdr.NumBones];
        BoneSetupExtract.BoneBitList marks = new();
        if (ctx is not null && solve)
        {
            ctx.UpdateTargets(pos, q, world, marks);
            ctx.SolveDependencies(pos, q, world, marks);
        }

        if (ownedBone >= 0)
        {
            world[ownedBone] = ownedMatrix;
            marks.MarkBone(ownedBone);
        }

        BoneLibrary.PoseToWorldProcedural(hdr, placement, pos, q, mask, now,
                                          ctx is not null || ownedBone >= 0 ? marks : null, null, world);
        return world;
    }

    // ---- RefreshModelPoseState -------------------------------------------------

    [Fact]
    public void RefreshRecordsAModelWithNoStudioDataSoItIsNotReasked()
    {
        FakeBoneWorld w = new();
        ClientBoneDriver d = new(w);
        BoneEntityState s = new() { ModelHasAutoplay = true, ModelIkChains = 3 };
        d.RefreshModelPoseState(s, 42);
        Assert.Equal((42, false, 0, 0), (s.PoseDefaultsFor, s.ModelHasAutoplay, s.ModelIkChains, w.Opens));
    }

    [Fact]
    public void RefreshForTheSameModelDoesNotReopenIt()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.RefreshModelPoseState(s, Model);
        d.RefreshModelPoseState(s, Model);
        Assert.Equal(1, w.Opens);
    }

    [Fact]
    public void RefreshMarksAModelWithAutoplaySequences()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(AutoplayChain());
        d.RefreshModelPoseState(s, Model);
        Assert.True(s.ModelHasAutoplay);
    }

    [Fact]
    public void RefreshMarksAModelWithAJiggleBone()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(JiggleTail());
        d.RefreshModelPoseState(s, Model);
        Assert.True(s.ModelHasJiggleBones);
    }

    [Fact]
    public void RefreshDoesNotMarkAComputedProceduralBoneAsJiggle()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(JiggleTail(ProceduralBoneRule.ProcQuatInterp));
        d.RefreshModelPoseState(s, Model);
        Assert.False(s.ModelHasJiggleBones);
    }

    [Fact]
    public void RefreshCountsTheIkChains()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        d.RefreshModelPoseState(s, Model);
        Assert.Equal(1, s.ModelIkChains);
    }

    [Fact]
    public void RefreshDropsTheSolverOfAModelWithNoChains()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Ik = new CIKContext();
        d.RefreshModelPoseState(s, Model);
        Assert.Null(s.Ik);
    }

    [Fact]
    public void RefreshKeepsTheSolverOfAModelWithChains()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        CIKContext ik = new();
        s.Ik = ik;
        d.RefreshModelPoseState(s, Model);
        Assert.Same(ik, s.Ik);
    }

    [Fact]
    public void RefreshCopiesTheDefaultsAndAdvancesTheGenerationOnce()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(PosedChain());
        d.RefreshModelPoseState(s, Model);
        Assert.Equal((0.25f, 0.5f, 1), (s.PoseParameter[0], s.PoseParameter[1], s.PoseGeneration));
    }

    [Fact]
    public void RefreshOverDefaultsAlreadyHeldKeepsTheGeneration()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        Array.Fill(s.PoseParameter, 0.5f);
        d.RefreshModelPoseState(s, Model);
        Assert.Equal(0, s.PoseGeneration);
    }

    [Fact]
    public void RefreshOverAnUnreadableHeaderKeepsThePoseButRecordsTheModel()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(null);
        d.RefreshModelPoseState(s, Model);
        Assert.Equal((Model, 0f, 0), (s.PoseDefaultsFor, s.PoseParameter[0], s.PoseGeneration));
    }

    // ---- EnsureBones: the cache --------------------------------------------------

    [Fact]
    public void NoStudioModelAnswersFalseAndRecordsNothing()
    {
        FakeBoneWorld w = new();
        BoneEntityState s = new();
        Assert.False(new ClientBoneDriver(w).EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv));
        Assert.Equal((0, -1), (s.BoneMaskAsked, s.PoseDefaultsFor));
    }

    [Fact]
    public void TheMaskAccumulatesAcrossAsks()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        d.EnsureBones(s, Pose(), UsedByAttachment, 1f, Cv);
        Assert.Equal(UsedByHitbox | UsedByAttachment, s.BoneMaskAsked);
    }

    [Fact]
    public void TheStoredKeyRecordsTheMaskEvaluatedNotTheMaskAsked()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        d.EnsureBones(s, Pose(), UsedByAttachment, 1f, Cv);
        Assert.Equal((Model, UsedByHitbox | UsedByAttachment), (s.CacheKey.ModelIndex, s.CacheKey.BoneMask));
    }

    [Fact]
    public void TheFirstAskBuildsOneMatrixPerBone()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv));
        Assert.Equal(3, s.CachedBones.Length);
    }

    [Fact]
    public void ARepeatedAskIsAnsweredFromTheCacheWithoutOpeningTheModel()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv));
        Assert.Equal(opens, w.Opens);
    }

    [Fact]
    public void ANarrowerAskIsAnsweredFromAWiderCache()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        Assert.Equal(opens, w.Opens);
    }

    [Fact]
    public void AMovedEntityRebuilds()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(new Vector(101f, 0f, 0f)), UsedByAnything, 1f, Cv);
        Assert.Equal(opens + 1, w.Opens);
    }

    [Fact]
    public void ANewCycleRebuilds()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.SeqDuration = 1f;
        s.SeqLooping = true;
        d.EnsureBones(s, Pose(), UsedByAnything, 0.25f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 0.5f, Cv);
        Assert.Equal((opens + 1, 0.5f), (w.Opens, s.CacheKey.Cycle));
    }

    [Fact]
    public void AModelThatDoesNotAnimateItselfCachesAcrossTime()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Cv);
        Assert.Equal((opens, 0f), (w.Opens, s.CacheKey.AutoplayTime));
    }

    [Fact]
    public void AnAutoplayModelKeysOnTheClock()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(AutoplayChain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Cv);
        Assert.Equal((opens + 1, 2f), (w.Opens, s.CacheKey.AutoplayTime));
    }

    [Fact]
    public void AJigglingModelKeysOnTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(JiggleTail());
        d.EnsureBones(s, Pose(), UsedByAnything, 3f, Cv);
        Assert.Equal(3f, s.CacheKey.AutoplayTime);
    }

    [Fact]
    public void AModelWithIkChainsKeysOnTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        d.EnsureBones(s, Pose(), AllBits, 3f, Cv);
        Assert.Equal(3f, s.CacheKey.AutoplayTime);
    }

    [Fact]
    public void ARunningLayerKeysOnTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        s.Layers[4] = new AnimLayer { Sequence = 1, Weight = 0.5f };
        d.EnsureBones(s, Pose(), UsedByAnything, 3f, Cv);
        Assert.Equal(3f, s.CacheKey.AutoplayTime);
    }

    [Fact]
    public void TheKeyCarriesThePoseAndLayerGenerationsSummed()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.LayerGeneration = 5;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(1 + 5, s.CacheKey.PoseGeneration); // the refresh's one bump + 5
    }

    [Fact]
    public void ALayerChangeRebuilds()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        s.LayerGeneration++;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(opens + 1, w.Opens);
    }

    [Fact]
    public void ANewScaleRebuilds()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        s.ModelScale = 2f;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal((opens + 1, 2f), (w.Opens, s.CacheKey.ModelScale));
    }

    [Fact]
    public void AMergingEntityNeverCaches()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.BoneMergeSource = 9;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(opens + 1, w.Opens);
    }

    [Fact]
    public void ARagdollBodyNeverCaches()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Ragdoll = 4;
        w.Bodies.Add(4);
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(opens + 1, w.Opens);
    }

    [Fact]
    public void ARagdollIdTheTableNoLongerHoldsCachesAgain()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Ragdoll = 4;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(opens, w.Opens);
    }

    [Fact]
    public void AnInvalidatedCacheRebuilds()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        int opens = w.Opens;
        s.InvalidateBoneCache();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(opens + 1, w.Opens);
    }

    [Fact]
    public void AnUnreadableHeaderOnAMissAnswersFalse()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(null);
        Assert.False(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv));
        Assert.Equal((UsedByAnything, -1), (s.BoneMaskAsked, s.CacheKey.ModelIndex));
    }

    [Fact]
    public void ARebuildInvalidatesTheAttachments()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.AttachmentsValid = true;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.False(s.AttachmentsValid);
    }

    // ---- EnsureBones: placement and the static-prop branch ----------------------

    [Fact]
    public void AStaticPropWritesTheScaledPlacementIntoEveryBone()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(StaticProp());
        s.ModelScale = 2f;
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        BoneMatrix3x4 expected = BoneLibraryMath.PlacementToWorld(Placement(Pose(), 2f));
        Assert.Equal([expected, expected, expected], s.CachedBones);
    }

    [Fact]
    public void TheLocalViewModelIsPlacedAtItsRenderPose()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(StaticProp());
        ClientBonePose pose = Pose(viewModel: true);
        d.EnsureBones(s, pose, UsedByAnything, 1f, Cv);
        BonePlacement render = new() { Origin = pose.RenderOrigin, Angles = pose.RenderAngles, ModelScale = 1f };
        Assert.Equal(BoneLibraryMath.PlacementToWorld(render), s.CachedBones[0]);
        Assert.Equal((pose.RenderOrigin, new Vector(0f, 90f, 0f)), (s.CacheKey.Origin, s.CacheKey.Angles));
    }

    [Fact]
    public void AnythingElseIsPlacedAtItsNetworkedPose()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(StaticProp());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal((Pose().NetOrigin, new Vector(0f, 30f, 0f)), (s.CacheKey.Origin, s.CacheKey.Angles));
    }

    // ---- EnsureBones: the poseable branch ----------------------------------------

    [Fact]
    public void APoseableModelIsTheLibrarysPoseThroughPoseToWorldProcedural()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.SeqDuration = 1f;
        s.SeqLooping = true;
        d.EnsureBones(s, Pose(), UsedByAnything, 0.25f, Cv);
        Assert.Equal(Reference(Chain(), Placement(Pose()), UsedByAnything, 0, 0.25f, 0.25f), s.CachedBones);
    }

    [Fact]
    public void TheBuildEvaluatesTheAccumulatedMask()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Bones[2].Flags = UsedByAttachment;
        StudioHdr hdr = Make(b);
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        d.EnsureBones(s, Pose(), UsedByAttachment, 1f, Cv);
        d.EnsureBones(s, Pose(new Vector(1f, 1f, 1f)), UsedByHitbox, 1f, Cv);
        Assert.Equal(Reference(hdr, Placement(Pose(new Vector(1f, 1f, 1f))), UsedByAttachment | UsedByHitbox, 0, 0f, 1f)[2],
                     s.CachedBones[2]);
        Assert.NotEqual(default, s.CachedBones[2]);
    }

    [Fact]
    public void AnUnscaledModelWithChainsGetsAnIkContextAndKeepsIt()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        d.EnsureBones(s, Pose(), AllBits, 1f, Cv);
        CIKContext? first = s.Ik;
        d.EnsureBones(s, Pose(), AllBits, 2f, Cv);
        Assert.NotNull(first);
        Assert.Same(first, s.Ik);
    }

    [Fact]
    public void AScaledModelGetsNoIkContext()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        s.ModelScale = 1.5f;
        d.EnsureBones(s, Pose(), AllBits, 1f, Cv);
        Assert.Null(s.Ik);
    }

    [Fact]
    public void AModelWithoutChainsGetsNoIkContext()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Null(s.Ik);
    }

    /// <summary>The leg at cycle 0.55: the ground rule's target is live, so
    /// the solve moves the chain (the two references differ).</summary>
    private static (BoneEntityState S, FakeBoneWorld W, ClientBoneDriver D, float Now, float Cycle) LegAtMidRule()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Leg());
        s.SeqDuration = 100f / 30f;
        s.SeqLooping = true;
        float now = 0.55f * s.SeqDuration;
        return (s, w, d, now, s.SequenceCycle(now));
    }

    [Fact]
    public void TheIkContextUpdatesAndSolvesBeforeTheMatrixBuild()
    {
        (BoneEntityState s, _, ClientBoneDriver d, float now, float cycle) = LegAtMidRule();
        d.EnsureBones(s, Pose(), AllBits, now, Cv);

        BoneMatrix3x4[] solved = Reference(Leg(), Placement(Pose()), AllBits, 0, cycle, now, ik: true, solve: true);
        BoneMatrix3x4[] unsolved = Reference(Leg(), Placement(Pose()), AllBits, 0, cycle, now, ik: true, solve: false);
        Assert.NotEqual(unsolved, solved);
        Assert.Equal(solved, s.CachedBones);
    }

    [Fact]
    public void ARagdollBodyIsNotSolved()
    {
        (BoneEntityState s, FakeBoneWorld w, ClientBoneDriver d, float now, float cycle) = LegAtMidRule();
        s.Ragdoll = 3;
        w.Bodies.Add(3);
        d.EnsureBones(s, Pose(), AllBits, now, Cv);
        Assert.Equal(Reference(Leg(), Placement(Pose()), AllBits, 0, cycle, now, ik: true, solve: false), s.CachedBones);
    }

    [Fact]
    public void TheRagdollsBonesAreTheSolversAndTheRestHangOffThem()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Ragdoll = 6;
        BoneMatrix3x4 solver = Translation(1f, 2f, 3f);
        w.Ragdoll = (bones, owned) =>
        {
            bones[1] = solver;
            owned[0] = 1;
            return 1;
        };
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);

        BoneMatrix3x4[] expected = Reference(Chain(), Placement(Pose()), UsedByAnything, 0, 0f, 1f, ownedBone: 1, ownedMatrix: solver);
        Assert.Equal(solver, s.CachedBones[1]);
        Assert.Equal(expected, s.CachedBones);
        Assert.NotEqual(Reference(Chain(), Placement(Pose()), UsedByAnything, 0, 0f, 1f)[2], s.CachedBones[2]);
    }

    [Fact]
    public void NoRagdollIdAsksTheTableForNothing()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(0, w.RagdollWrites);
    }

    [Fact]
    public void ARagdollIdWritesBonesEvenWhenTheTableNoLongerCallsItABody()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Ragdoll = 6;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(1, w.RagdollWrites);
    }

    [Fact]
    public void AJigglingModelGetsSpringsThatOutliveTheBuild()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(JiggleTail());
        d.EnsureBones(s, Pose(), UsedByAnything, 3f, Cv);
        Assert.NotNull(s.Jiggle);
        Assert.Equal(3f, s.Jiggle!.GetJiggleData(1, 99f, Vector.Zero, Vector.Zero).LastUpdate);
    }

    [Fact]
    public void AModelWithoutJiggleBonesGetsNoSprings()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 3f, Cv);
        Assert.Null(s.Jiggle);
    }

    [Fact]
    public void TheLayersBlendInTheTablesOrderNotTheSlots()
    {
        StudioHdr hdr = LayeredChain();
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        for (int slot = 0; slot < 2; slot++)
        {
            s.LayerDuration[slot] = 2f / 30f;
            s.LayerLooping[slot] = true;
        }

        s.Layers[0] = new AnimLayer { Sequence = 1, Weight = 0.5f, Order = 2 };
        s.Layers[1] = new AnimLayer { Sequence = 2, Weight = 0.5f, Order = 1 };
        const float now = 0.01f;
        d.EnsureBones(s, Pose(), UsedByAnything, now, Cv);

        BoneLayerPose slot0 = new(1, s.AnimLayerCycle(0, now), 0.5f);
        BoneLayerPose slot1 = new(2, s.AnimLayerCycle(1, now), 0.5f);
        BoneMatrix3x4[] sorted = Reference(hdr, Placement(Pose()), UsedByAnything, 0, 0f, now, layers: [slot1, slot0]);
        BoneMatrix3x4[] slotOrder = Reference(hdr, Placement(Pose()), UsedByAnything, 0, 0f, now, layers: [slot0, slot1]);
        Assert.NotEqual(slotOrder[0], sorted[0]);
        Assert.Equal(sorted, s.CachedBones);
    }

    [Fact]
    public void AnOutOfRangeLayerIsDroppedFromThePose()
    {
        StudioHdr hdr = Chain();
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.Layers[0] = new AnimLayer { Sequence = 5, Weight = 1f };
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        Assert.Equal(Reference(hdr, Placement(Pose()), UsedByAnything, 0, 0f, 1f), s.CachedBones);
    }

    // ---- EnsureAttachments ---------------------------------------------------------

    [Fact]
    public void ValidAttachmentsAnswerWithoutABuild()
    {
        FakeBoneWorld w = new();
        BoneEntityState s = new() { AttachmentsValid = true, Attachments = new BoneMatrix3x4[2] };
        Assert.True(new ClientBoneDriver(w).EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.Equal(0, w.Opens);
    }

    [Fact]
    public void AttachmentsAreComposedOnTheBonesAtTheNetworkedPose()
    {
        StudioHdr hdr = AttachedChain();
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.ModelScale = 2f;
        Assert.True(d.EnsureAttachments(s, Pose(viewModel: true), 1f, Cv));

        BoneMatrix3x4[] expected = new BoneMatrix3x4[2];
        BonePlacement net = new() { Origin = Pose().NetOrigin, Angles = Pose().NetAngles, ModelScale = 2f };
        BoneLibrary.BuildAttachments(hdr, s.CachedBones, net, expected);
        Assert.Equal(expected, s.Attachments);
        Assert.True(s.AttachmentsValid);
        Assert.Equal(UsedByAttachment, s.BoneMaskAsked);
    }

    [Fact]
    public void AnAttachmentOnABoneTheModelLacksCollapsesOntoTheUnscaledNetworkedPose()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "stray", LocalBone = 7 });
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Make(b));
        s.ModelScale = 2f;
        Assert.True(d.EnsureAttachments(s, Pose(viewModel: true), 1f, Cv));
        Assert.Equal(BoneSetupMath.AngleMatrix(Pose().NetAngles, Pose().NetOrigin), s.Attachments[0]);
    }

    [Fact]
    public void AModelWithNoAttachmentsIsMarkedValidAndAnswersFalse()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        Assert.False(d.EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.True(s.AttachmentsValid);
    }

    [Fact]
    public void NoModelHasNoAttachments()
    {
        BoneEntityState s = new();
        Assert.False(new ClientBoneDriver(new FakeBoneWorld()).EnsureAttachments(s, Pose(), 1f, Cv));
        Assert.False(s.AttachmentsValid);
    }

    // ---- SetupBones ----------------------------------------------------------------

    [Fact]
    public void NoOutputOnlyFillsAndAnswersWhetherThereAreBones()
    {
        BoneEntityState s = new();
        Assert.False(new ClientBoneDriver(new FakeBoneWorld()).SetupBones(s, Pose(), null, UsedByAnything, 1f, Cv, BoneMatrix3x4.Identity));
    }

    [Fact]
    public void TheBonesAreCopiedAndTheSlotsPastThemAreTheEntityTransform()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        BoneMatrix3x4 world = Translation(9f, 9f, 9f);
        BoneMatrix3x4[] output = new BoneMatrix3x4[5];
        Assert.True(d.SetupBones(s, Pose(), output, UsedByAnything, 1f, Cv, world));
        Assert.Equal([s.CachedBones[0], s.CachedBones[1], s.CachedBones[2], world, world], output);
    }

    [Fact]
    public void FewerSlotsThanBonesCopiesOnlyThoseSlots()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        BoneMatrix3x4[] output = new BoneMatrix3x4[2];
        d.SetupBones(s, Pose(), output, UsedByAnything, 1f, Cv, BoneMatrix3x4.Identity);
        Assert.Equal([s.CachedBones[0], s.CachedBones[1]], output);
    }

    [Fact]
    public void NoSkeletonFillsEverySlotWithTheEntityTransformAndAnswersTrue()
    {
        BoneMatrix3x4 world = Translation(9f, 9f, 9f);
        BoneMatrix3x4[] output = [Sentinel, Sentinel];
        Assert.True(new ClientBoneDriver(new FakeBoneWorld()).SetupBones(new BoneEntityState(), Pose(), output, UsedByAnything, 1f, Cv, world));
        Assert.Equal([world, world], output);
    }

    // ---- TestHitboxes --------------------------------------------------------------

    [Fact]
    public void NoModelHasNoHitboxes()
    {
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(0f, 0f, 100f), new Vector(0f, 0f, -100f));
        Assert.False(new ClientBoneDriver(new FakeBoneWorld()).TestHitboxes(new BoneEntityState(), Pose(), 0, ray, -1, Vector.Zero, tr, null, 1f, Cv));
    }

    [Fact]
    public void TheRayIsTestedAgainstTheHitboxBonesAtTheCollisionOrigin()
    {
        StudioHdr hdr = HitboxChain();
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.ModelScale = 2f;
        Vector origin = new(4f, 5f, 6f);
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Cv);
        Vector hand = s.CachedBones[2].Position;
        BoneRay ray = BoneRay.FromPoints(hand + new Vector(0f, 0f, 100f), hand - new Vector(0f, 0f, 100f));
        BoneTrace tr = new();
        bool tested = d.TestHitboxes(s, Pose(), 0, ray, -1, origin, tr, null, 1f, Cv);

        BoneTrace expected = new();
        bool expectedTested = BoneLibrary.TraceHitboxes(null, hdr, 0, ray, -1, s.CachedBones, origin, 2f, expected);
        Assert.Equal((expectedTested, expected.Fraction, expected.HitBox), (tested, tr.Fraction, tr.HitBox));
        Assert.Equal(UsedByHitbox, s.BoneMaskAsked);
        Assert.True(tr.Fraction < 1f, "the ray must reach the box");
    }

    // ---- SetPlayingSequence --------------------------------------------------------

    [Fact]
    public void ANonPositiveRateAndANegativeSequenceAreNormalised()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Sequence = 3;
        d.SetPlayingSequence(s, Model, -4, -1f, false, 2f);
        Assert.Equal((0, 1f), (s.Sequence, s.PlaybackRate));
    }

    [Fact]
    public void ThePlayingSequenceKeepsItsClockAndOnlyTheRateMoves()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.AnimTime = 1f;
        d.SetPlayingSequence(s, Model, 0, 2f, false, 5f);
        Assert.Equal((1f, 2f, 0), (s.AnimTime, s.PlaybackRate, s.SequenceStarts));
    }

    [Fact]
    public void ARestartRestartsTheClockAndCountsAStart()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.AnimTime = 1f;
        d.SetPlayingSequence(s, Model, 0, 1f, true, 5f);
        Assert.Equal((5f, 1), (s.AnimTime, s.SequenceStarts));
    }

    [Fact]
    public void ANewSequenceReadsItsDurationAndLoopFlagOnce()
    {
        StudioHdr hdr = Chain(seqFlags: 0);
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        s.Sequence = 5;
        d.SetPlayingSequence(s, Model, 0, 1f, false, 5f);
        Assert.True(BoneLibrary.SequenceFacts(hdr, 0, null, out float duration, out bool looping));
        Assert.Equal((duration, looping, 5f), (s.SeqDuration, s.SeqLooping, s.AnimTime));
        Assert.True(duration > 0f);
    }

    [Fact]
    public void ALoopingSequenceIsReadAsLooping()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        s.Sequence = 5;
        d.SetPlayingSequence(s, Model, 0, 1f, false, 5f);
        Assert.True(s.SeqLooping);
    }

    [Fact]
    public void ANewSequenceOnAnUnreadableModelHasNoDuration()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(null);
        s.Sequence = 5;
        s.SeqDuration = 3f;
        s.SeqLooping = true;
        d.SetPlayingSequence(s, Model, 0, 1f, false, 5f);
        Assert.Equal((0f, false), (s.SeqDuration, s.SeqLooping));
    }

    [Fact]
    public void ANewSequenceDoesNotInvalidateTheClientsCache()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(Chain());
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Cv);
        s.Sequence = 5;
        d.SetPlayingSequence(s, Model, 0, 1f, false, 5f);
        Assert.Equal(Model, s.CacheKey.ModelIndex);
    }

    // ---- SetAnimLayer --------------------------------------------------------------

    [Fact]
    public void ALayerSlotOutOfRangeIsIgnored()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, AnimLayerRule.MaxLayers, 1, 1f, 1f, 0, false, 1f);
        Assert.Equal(0, s.LayerGeneration);
    }

    [Fact]
    public void ANewLayerAdvancesTheGenerationAndStartsItsClock()
    {
        StudioHdr hdr = LayeredChain();
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(hdr);
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 2f, 3, false, 4f);
        Assert.True(BoneLibrary.SequenceFacts(hdr, 1, null, out float duration, out bool looping));
        Assert.Equal((1, 1, 0.5f, 3), (s.LayerGeneration, s.Layers[2].Sequence, s.Layers[2].Weight, s.Layers[2].Order));
        Assert.Equal((4f, 2f, duration, looping), (s.LayerStart[2], s.LayerRate[2], s.LayerDuration[2], s.LayerLooping[2]));
    }

    [Fact]
    public void AnIdenticalLayerSetKeepsTheGenerationAndTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, false, 4f);
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, false, 9f);
        Assert.Equal((1, 4f), (s.LayerGeneration, s.LayerStart[2]));
    }

    [Fact]
    public void AWeightChangeAdvancesTheGenerationButKeepsTheClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, false, 4f);
        d.SetAnimLayer(s, Model, 2, 1, 0.75f, 1f, 3, false, 9f);
        Assert.Equal((2, 4f), (s.LayerGeneration, s.LayerStart[2]));
    }

    [Fact]
    public void ARestartedLayerRestartsItsClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, false, 4f);
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, true, 9f);
        Assert.Equal((2, 9f), (s.LayerGeneration, s.LayerStart[2]));
    }

    [Fact]
    public void ALayerSwitchedOffLosesItsClock()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 1f, 3, false, 4f);
        d.SetAnimLayer(s, Model, 2, -1, 0f, 1f, 3, false, 9f);
        Assert.Equal((0f, 0f, false, -1), (s.LayerStart[2], s.LayerDuration[2], s.LayerLooping[2], s.Layers[2].Sequence));
    }

    [Fact]
    public void ALayersNonPositiveRateIsOne()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig(LayeredChain());
        d.SetAnimLayer(s, Model, 2, 1, 0.5f, 0f, 3, false, 4f);
        Assert.Equal(1f, s.LayerRate[2]);
    }
}
