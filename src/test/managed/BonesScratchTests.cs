using System;
using System.Collections;
using System.Reflection;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the skeleton build's reused scratch (plan_physics_bones_perf A3):
/// <see cref="BoneScratch"/>'s rents, the IK contexts and targets a build
/// rents instead of constructing, and the record views a header makes once.
/// The rule they hold is that a build over a reused scratch is the build
/// over fresh arrays, value for value.
/// </summary>
public class BonesScratchTests
{
    // ---- the rents ---------------------------------------------------------------

    [Fact]
    public void ARentAfterARestoreIsTheSameArrayZeroed()
    {
        BoneScratch scratch = new();
        BoneScratch.Mark mark = scratch.Save();
        Vector[] first = scratch.Vectors();
        first[5] = new Vector(1f, 2f, 3f);
        scratch.Restore(mark);

        Vector[] again = scratch.Vectors();

        Assert.Same(first, again);
        Assert.Equal(Vector.Zero, again[5]);
    }

    [Fact]
    public void RentsInsideARentAreDistinctArrays()
    {
        BoneScratch scratch = new();
        BoneMatrix3x4[] outer = scratch.Matrices();
        BoneScratch.Mark mark = scratch.Save();
        BoneMatrix3x4[] inner = scratch.Matrices();
        scratch.Restore(mark);

        Assert.NotSame(outer, inner);
        Assert.Same(inner, scratch.Matrices());
    }

    [Fact]
    public void ARentedBitListComesBackEmpty()
    {
        BoneScratch scratch = new();
        BoneScratch.Mark mark = scratch.Save();
        scratch.BitList().MarkBone(3);
        scratch.Restore(mark);

        Assert.False(scratch.BitList().IsBoneMarked(3));
    }

    // ---- a build over a reused scratch is the build over fresh arrays -------------------

    private static BoneBuildInputs Inputs(StudioHdr hdr, float now, BoneScratch? scratch, BoneLayerPose[]? layers = null,
                                          bool autoplay = false)
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement.Origin = new Vector(10f, 20f, 30f);
        inputs.Placement.Angles = new BoneAngle(0f, 45f, 0f);
        inputs.BoneMask = AllBits;
        inputs.Cycle = now * 0.37f % 1f;
        inputs.CurTime = now;
        inputs.HasAutoplay = autoplay;
        inputs.AutoplayIkAlways = true;
        inputs.Layers = layers;
        inputs.LayerCount = layers?.Length ?? 0;
        inputs.FrameCount = 7;
        inputs.Scratch = scratch;
        return inputs;
    }

    private static BoneMatrix3x4[] Build(StudioHdr hdr, in BoneBuildInputs inputs, bool ik)
    {
        BoneMatrix3x4[] bones = new BoneMatrix3x4[hdr.NumBones];
        CIKContext? ctx = null;

        if (ik)
        {
            ctx = new CIKContext();
            ctx.Init(hdr, BoneSetupMath.AngleMatrix(inputs.Placement.Angles, inputs.Placement.Origin), inputs.BoneMask,
                     inputs.CurTime, 7);
        }

        Assert.Equal(hdr.NumBones, BoneLibrary.BuildBones(hdr, inputs, bones, ctx));
        return bones;
    }

    [Fact]
    public void BuildsThroughOneScratchMatchFreshBuildsModelAfterModel()
    {
        BoneScratch scratch = new();
        StudioHdr leg = Leg(), layered = LayeredChain(), autoplay = AutoplayChain();
        BoneLayerPose[] layers = [new BoneLayerPose(1, 0.3f, 0.5f), new BoneLayerPose(2, 0.8f, 0.25f)];
        int compared = 0;

        foreach (float now in new[] { 1.1f, 2.35f, 3.6f })
        {
            Assert.Equal(Build(leg, Inputs(leg, now, null), true), Build(leg, Inputs(leg, now, scratch), true));
            Assert.Equal(Build(layered, Inputs(layered, now, null, layers), false),
                         Build(layered, Inputs(layered, now, scratch, layers), false));
            Assert.Equal(Build(autoplay, Inputs(autoplay, now, null, autoplay: true), false),
                         Build(autoplay, Inputs(autoplay, now, scratch, autoplay: true), false));
            compared += 3;
        }

        // The stimulus reached the scratch: it holds tables, and the three
        // models' builds did not all read the same pose.
        Assert.True(scratch.Allocated > 0);
        Assert.Equal(9, compared);
        Assert.NotEqual(Build(layered, Inputs(layered, 1.1f, null, layers), false)[0].Position,
                        Build(layered, Inputs(layered, 1.1f, null), false)[0].Position);
    }

    [Fact]
    public void RepeatedBuildsRentTheTablesTheFirstBuildMade()
    {
        BoneScratch scratch = new();
        StudioHdr autoplay = AutoplayChain();

        Build(autoplay, Inputs(autoplay, 1f, scratch, autoplay: true), false);
        int after1 = scratch.Allocated;

        for (int i = 0; i < 5; i++)
            Build(autoplay, Inputs(autoplay, 1f + i, scratch, autoplay: true), false);

        Assert.True(after1 > 0);
        Assert.Equal(after1, scratch.Allocated);
    }

    // ---- a context and its targets reset to new --------------------------------------

    private static void SameFields(object expected, object actual, string path = "")
    {
        foreach (FieldInfo f in expected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? e = f.GetValue(expected), a = f.GetValue(actual);

            if (f.Name.Contains("Held", StringComparison.Ordinal) || f.Name.Contains("spare", StringComparison.Ordinal))
            {
                continue; // the storage a reset keeps, by design
            }

            if (e is ICollection ec && a is ICollection ac)
            {
                Assert.True(ec.Count == ac.Count, $"{path}{f.Name}: {ec.Count} vs {ac.Count}");
                continue;
            }

            Assert.True(Equals(e, a), $"{path}{f.Name}: {e} vs {a}");
        }
    }

    [Fact]
    public void ATargetResetToNewHoldsWhatANewTargetHolds()
    {
        CikTarget used = new();

        // Every public field written to something other than its default.
        foreach (FieldInfo f in typeof(CikTarget).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            object v = f.FieldType == typeof(int) ? 7 : f.FieldType == typeof(float) ? 7f
                : f.FieldType == typeof(bool) ? true : f.FieldType == typeof(string) ? "x"
                : f.FieldType == typeof(Vector) ? new Vector(7f) : f.FieldType == typeof(Quaternion) ? new Quaternion(7f, 7f, 7f, 7f)
                : f.FieldType == typeof(BoneAngle) ? new BoneAngle(7f, 7f, 7f)
                : throw new InvalidOperationException($"no stimulus for {f.FieldType} {f.Name}");
            f.SetValue(used, v);
        }

        used.ResetToNew();

        SameFields(new CikTarget(), used);
    }

    [Fact]
    public void AContextResetToNewHoldsWhatANewContextHolds()
    {
        StudioHdr leg = Leg();
        CIKContext used = new();
        used.Init(leg, BoneMatrix3x4.Identity, AllBits, 2f, 9);
        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        used.AddDependencies(0, new float[BoneLibrary.MaxStudioPoseParam], 1f, 0.4f);

        used.ResetToNew();

        SameFields(new CIKContext(), used);
        _ = (pos, q);
    }

    [Fact]
    public void AResetContextInitsTargetsAsAFreshOneDoes()
    {
        StudioHdr leg = Leg();
        CIKContext reused = new();
        reused.Init(leg, BoneMatrix3x4.Identity, AllBits, 2f, 9);
        reused.ResetToNew();
        reused.Init(leg, BoneMatrix3x4.Identity, AllBits, 3f, 10);

        CIKContext fresh = new();
        fresh.Init(leg, BoneMatrix3x4.Identity, AllBits, 3f, 10);

        FieldInfo targets = typeof(CIKContext).GetField("_m_target", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var a = (CikTarget[])targets.GetValue(fresh)!;
        var b = (CikTarget[])targets.GetValue(reused)!;

        Assert.Equal(12, a.Length);
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
            SameFields(a[i], b[i], $"target {i}.");
    }

    // ---- a header's views, made once ---------------------------------------------------

    [Fact]
    public void ABoneViewIsMadeOnce()
    {
        StudioHdr chain = Chain();

        Assert.Same(chain.Bone(2), chain.Bone(2));
        Assert.Equal("hand", chain.Bone(2).Name);
    }

    [Fact]
    public void TheAutoplayListIsTheAutoplaySequencesInTableOrder()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Anims.Add(Ramp(0, 3, 30f, 40, "steep"));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "steep", AnimTable = [1], Flags = StudioConstants.STUDIO_AUTOPLAY });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "plain", AnimTable = [1], Flags = 0 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "again", AnimTable = [1], Flags = StudioConstants.STUDIO_AUTOPLAY });
        StudioHdr hdr = Make(b);

        Assert.Equal(new ushort[] { 1, 3 }, hdr.AutoplayList().ToArray());
        Assert.Equal(2, hdr.CountAutoplaySequences());
    }
}
