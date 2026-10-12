using System;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ClientBoneDriver.MergeBonesFrom"/> through
/// EnsureBones: EF_BONEMERGE's follower half, hand-derived from
/// C_StandaloneEntity::MergeBonesFrom (shell_standalone_client.cpp
/// 2062-2184) and SetBoneMergeSource (2047-2060). The follower is a static
/// prop whose bones are its placement, so a merged bone is visibly the
/// source's and an unmerged one visibly its own.
/// </summary>
public class BonesClientMergeTests
{
    private const int Own = 1;
    private const int SourceEnt = 12;
    private const nint SourceModel = 0x55;

    private static BoneConVars Quiet => BoneConVars.Stock;

    private static BoneConVars Verbose => new(true, JiggleBones.DefaultFramerateCutoff, 1);

    private static ClientBonePose Pose() => new()
    {
        ModelIndex = Own,
        NetOrigin = new Vector(100f, 0f, 0f),
        NetAngles = new BoneAngle(0f, 30f, 0f),
    };

    private static BoneMatrix3x4 OwnPlacement => BoneLibraryMath.PlacementToWorld(
        new BonePlacement { Origin = new Vector(100f, 0f, 0f), Angles = new BoneAngle(0f, 30f, 0f), ModelScale = 1f });

    /// <summary>Follower "pelvis, hand, jaw" merging from a source "hand,
    /// pelvis" whose bone i is a translation of 10*(i+1) along x.</summary>
    private static (FakeBoneWorld W, ClientBoneDriver D, BoneEntityState S) Rig(StudioHdr? sourceHdr = null)
    {
        FakeBoneWorld w = new();
        w.Models[Own] = NamedStatic("pelvis", "hand", "jaw");
        w.Sources[SourceEnt] = new BoneMergeSourceView(false, SourceModel);
        w.SourceModels[SourceModel] = sourceHdr ?? NamedStatic("hand", "pelvis");
        w.SourceBones = (_, bones, _, _) =>
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = Translation(10f * (i + 1), 0f, 0f);
            }

            return true;
        };
        BoneEntityState s = new();
        s.SetBoneMergeSource(SourceEnt);
        return (w, new ClientBoneDriver(w), s);
    }

    [Fact]
    public void EachBoneSharingANameReadsTheSourcesMatrix()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = Rig();
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet));
        Assert.Equal([Translation(20f, 0f, 0f), Translation(10f, 0f, 0f), OwnPlacement], s.CachedBones);
    }

    [Fact]
    public void TheSourceIsAskedForEveryBone()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.EnsureBones(s, Pose(), UsedByHitbox, 1f, Quiet);
        Assert.Equal(BoneLibrary.BoneUsedByAnything, w.LastSourceMask);
    }

    [Fact]
    public void TheMapIsBuiltOncePerPairOfModels()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Quiet);
        Assert.Equal((1, 2), (w.SourceModelOpens, w.SourceSetupBonesCalls));
        Assert.Equal((Own, SourceModel), (s.MergeKey.OwnModelIndex, s.MergeKey.SourceModel));
    }

    [Fact]
    public void ASourceWearingANewModelRebuildsTheMap()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);
        w.Sources[SourceEnt] = new BoneMergeSourceView(false, 0x66);
        w.SourceModels[0x66] = NamedStatic("jaw");
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Quiet);
        Assert.Equal(2, w.SourceModelOpens);
        Assert.Equal([OwnPlacement, OwnPlacement, Translation(10f, 0f, 0f)], s.CachedBones);
    }

    [Fact]
    public void AnUnreadableSourceMapsNothingAndIsAskedAgainNextTime()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        w.SourceModels[SourceModel] = null;
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);
        Assert.Equal([OwnPlacement, OwnPlacement, OwnPlacement], s.CachedBones);
        Assert.Equal(-1, s.MergeKey.OwnModelIndex);

        w.SourceModels[SourceModel] = NamedStatic("hand", "pelvis");
        d.EnsureBones(s, Pose(), UsedByAnything, 2f, Quiet);
        Assert.Equal(2, w.SourceModelOpens);
        Assert.Equal(Translation(20f, 0f, 0f), s.CachedBones[0]);
    }

    [Fact]
    public void ASelfMergeKeepsTheOwnPoseAndAsksNothing()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        w.Sources[SourceEnt] = new BoneMergeSourceView(true, SourceModel);
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Verbose);
        Assert.Equal((0, 0, 0), (w.SourceModelOpens, w.SourceSetupBonesCalls, w.Said.Count));
        Assert.Equal([OwnPlacement, OwnPlacement, OwnPlacement], s.CachedBones);
    }

    [Fact]
    public void ASourceWithNoModelKeepsTheOwnPose()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        w.Sources.Clear();
        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet));
        Assert.Equal((0, 0), (w.SourceSetupBonesCalls, w.Said.Count));
        Assert.Equal([OwnPlacement, OwnPlacement, OwnPlacement], s.CachedBones);
    }

    [Fact]
    public void ASourceWithNoModelIsSaidWhenVerbose()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        w.Sources.Clear();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Verbose);
        Assert.Equal(["[SourceSharp] bonemerge: entity 12 has no renderable/model to merge from"], w.Said);
    }

    [Fact]
    public void TheMapsShapeIsSaidWhenVerbose()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Verbose);
        Assert.Equal(["[SourceSharp] bonemerge: map built, 2 of 3 bone(s) matched source entity 12"], w.Said);
    }

    [Fact]
    public void ASourceRefusingSetupBonesKeepsTheOwnPose()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        w.SourceBones = (_, bones, _, _) =>
        {
            bones[0] = Translation(1f, 1f, 1f);
            return false;
        };
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Verbose);
        Assert.Equal([OwnPlacement, OwnPlacement, OwnPlacement], s.CachedBones);
        Assert.Equal("[SourceSharp] bonemerge: source entity 12 refused SetupBones", w.Said[^1]);
    }

    [Fact]
    public void AMergeCycleKeepsTheInnerAsksOwnPoseInsteadOfRecursing()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig();
        int inner = 0;
        w.SourceBones = (_, bones, mask, time) =>
        {
            // The source's pose asks for this entity's: the same state, re-entered.
            inner++;
            Assert.True(d.EnsureBones(s, Pose(), mask, time, Quiet));
            bones[0] = Translation(10f, 0f, 0f);
            bones[1] = Translation(20f, 0f, 0f);
            return true;
        };
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);
        Assert.Equal((1, 1), (inner, w.SourceSetupBonesCalls));
        Assert.False(s.InBoneMerge);
        Assert.Equal(Translation(20f, 0f, 0f), s.CachedBones[0]);
    }

    [Fact]
    public void NoMergeSourceAsksTheEntityListNothing()
    {
        (FakeBoneWorld w, ClientBoneDriver d, _) = Rig();
        BoneEntityState plain = new();
        d.EnsureBones(plain, Pose(), UsedByAnything, 1f, Quiet);
        Assert.Equal(0, w.SourceSetupBonesCalls);
    }

    // ---- the whole-skeleton merge (plan_physics_bones_perf A1) --------------------------

    /// <summary>The IK leg as the follower, merging from a source that names
    /// all of its bones (<paramref name="names"/>) or some.</summary>
    private static (FakeBoneWorld W, ClientBoneDriver D, BoneEntityState S) LegRig(params string[] names)
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = Rig(NamedStatic(names));
        w.Models[Own] = Leg();
        return (w, d, s);
    }

    [Fact]
    public void AFollowerTheSourceNamesWhollyBuildsNoPoseOfItsOwn()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = LegRig("b3", "b2", "b1", "b0");

        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet));

        // Every bone is the source's (b0 is the source's bone 3: x 40), and
        // the leg's IK solver -- which its own build makes -- was never made.
        Assert.Equal([Translation(40f, 0f, 0f), Translation(30f, 0f, 0f), Translation(20f, 0f, 0f),
                      Translation(10f, 0f, 0f)], s.CachedBones);
        Assert.Null(s.Ik);
    }

    [Fact]
    public void AFollowerTheSourceNamesInPartBuildsItsOwnPoseUnderTheMerge()
    {
        (_, ClientBoneDriver d, BoneEntityState s) = LegRig("b3", "b2", "b1");

        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet));

        Assert.NotNull(s.Ik);
        Assert.Equal(Translation(10f, 0f, 0f), s.CachedBones[3]);
        Assert.NotEqual(Translation(40f, 0f, 0f), s.CachedBones[0]);
    }

    [Fact]
    public void AWholeMergeAsksTheSourceAgainOnEveryAsk()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = LegRig("b3", "b2", "b1", "b0");
        float x = 10f;
        w.SourceBones = (_, bones, _, _) =>
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = Translation(x * (i + 1), 0f, 0f);
            }

            return true;
        };

        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);
        x = 100f; // the source moved; the follower's own inputs did not
        d.EnsureBones(s, Pose(), UsedByAnything, 1f, Quiet);

        Assert.Equal((2, Translation(400f, 0f, 0f)), (w.SourceSetupBonesCalls, s.CachedBones[0]));
    }

    [Fact]
    public void AWholeMergeTheSourceRefusesFallsBackToTheOwnPose()
    {
        (FakeBoneWorld w, ClientBoneDriver d, BoneEntityState s) = LegRig("b3", "b2", "b1", "b0");
        w.SourceBones = (_, _, _, _) => false;

        Assert.True(d.EnsureBones(s, Pose(), UsedByAnything, 1f, Verbose));

        Assert.NotNull(s.Ik);
        Assert.Contains(w.Said, m => m.Contains("refused SetupBones", StringComparison.Ordinal));
        Assert.Single(w.Said, m => m.Contains("refused SetupBones", StringComparison.Ordinal));
    }
}
