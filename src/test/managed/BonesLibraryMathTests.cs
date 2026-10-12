using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="BoneLibraryMath"/> and the data structs, the managed
/// twin of bone_library.h's layer 1. The cases are the native gtest's
/// (src/test/native/bone_library_test.cpp: BoneLibraryCycle,
/// BoneLibraryPoseSource, BoneLibraryClampSequence, BoneLibraryScale,
/// BoneLibraryAttachment, BoneLibraryMerge, BoneLibraryInputs), one behaviour
/// each. Its source-text facts (isolation, the unscaled attachment fallback)
/// read the native files and stay native; the fallback's behaviour is a fact
/// in <c>BonesLibraryTests</c>.
/// </summary>
public class BonesLibraryMathTests
{
    private static readonly Vector KOrigin = new(-3843.0f, 2894.0f, 40.0f);
    private static readonly BoneAngle KAngles = new(12.0f, -47.0f, 3.0f);

    private static BoneMatrix3x4 Placed(float scale)
    {
        BonePlacement placement = default;
        placement.Reset();
        placement.Origin = KOrigin;
        placement.Angles = KAngles;
        placement.ModelScale = scale;
        return BoneLibraryMath.PlacementToWorld(placement);
    }

    private static float At(in BoneMatrix3x4 m, int r, int c)
    {
        Vector4 row = r == 0 ? m.Row0 : r == 1 ? m.Row1 : m.Row2;
        return row[c];
    }

    private static BoneMatrix3x4 Marked(float f)
    {
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = new Vector(f, f + 1.0f, f + 2.0f);
        return m;
    }

    // ---- Cycle -------------------------------------------------------------

    [Theory]
    [InlineData(0.0f, true)]
    [InlineData(-1.0f, false)]
    public void ADurationlessSequenceIsAtCycleZero(float duration, bool looping)
    {
        Assert.Equal(0.0f, BoneLibraryMath.Cycle(10.0f, 0.0f, 1.0f, duration, looping));
    }

    [Fact]
    public void AClockBeforeTheStartIsCycleZero()
    {
        Assert.Equal(0.0f, BoneLibraryMath.Cycle(1.0f, 5.0f, 1.0f, 2.0f, true));
    }

    [Fact]
    public void HalfWayThroughIsAHalf()
    {
        Assert.Equal(0.5f, BoneLibraryMath.Cycle(1.0f, 0.0f, 1.0f, 2.0f, false));
    }

    [Fact]
    public void TheRateScalesTheClock()
    {
        Assert.Equal(0.5f, BoneLibraryMath.Cycle(0.5f, 0.0f, 2.0f, 2.0f, false));
    }

    [Fact]
    public void ALoopingSequenceWraps()
    {
        Assert.Equal(0.25f, BoneLibraryMath.Cycle(4.5f, 0.0f, 1.0f, 2.0f, true));
    }

    [Theory]
    [InlineData(4.5f)]
    [InlineData(400.0f)]
    public void AFinishedOneShotHoldsItsLastFrame(float now)
    {
        Assert.Equal(0.999f, BoneLibraryMath.Cycle(now, 0.0f, 1.0f, 2.0f, false));
    }

    [Fact]
    public void ALoopingSequenceIsNotHeldAtTheLastFrame()
    {
        // 3.99 / 2 = 1.995 -> wraps to 0.995 (a one-shot would hold 0.999).
        float c = (3.99f - 0.0f) * 1.0f / 2.0f;
        Assert.Equal(c - (int)c, BoneLibraryMath.Cycle(3.99f, 0.0f, 1.0f, 2.0f, true));
    }

    // ---- PoseSource --------------------------------------------------------

    [Fact]
    public void AViewmodelIsPosedAtTheEyeItWasPinnedTo()
    {
        Vector feet = new(-4116.0f, 4600.0f, 1.92f);
        Vector eye = new(-4116.0f, 4600.0f, 65.92f);
        BonePlacement placement = default;
        placement.Reset();
        BoneLibraryMath.PoseSource(true, feet, new BoneAngle(0f, 0f, 0f), eye, new BoneAngle(25f, -90f, 0f), ref placement);
        Assert.Equal((eye, 25f, -90f, 0f),
                     (placement.Origin, placement.Angles.Pitch, placement.Angles.Yaw, placement.Angles.Roll));
    }

    [Fact]
    public void EverythingElseIsPosedWhereTheNetworkPutIt()
    {
        Vector net = new(1234.0f, -567.0f, 89.0f);
        Vector render = new(1300.0f, -500.0f, 90.0f);
        BonePlacement placement = default;
        placement.Reset();
        BoneLibraryMath.PoseSource(false, net, new BoneAngle(0f, 45f, 0f), render, new BoneAngle(0f, 50f, 0f), ref placement);
        Assert.Equal((net, 45f), (placement.Origin, placement.Angles.Yaw));
    }

    [Fact]
    public void PoseSourceLeavesTheScaleAlone()
    {
        BonePlacement placement = default;
        placement.ModelScale = 2.5f;
        BoneLibraryMath.PoseSource(true, Vector.One, default, Vector.One, default, ref placement);
        Assert.Equal(2.5f, placement.ModelScale);
    }

    // ---- ClampSequence -----------------------------------------------------

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void ASequenceInRangeIsKept(int asked)
    {
        Assert.Equal(asked, BoneLibraryMath.ClampSequence(asked, 8));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(900)]
    public void ASequenceOffTheEndFoldsToTheReferencePose(int asked)
    {
        Assert.Equal(0, BoneLibraryMath.ClampSequence(asked, 8));
    }

    [Fact]
    public void ANegativeSequenceFoldsToTheReferencePose()
    {
        Assert.Equal(0, BoneLibraryMath.ClampSequence(-1, 8));
    }

    [Fact]
    public void AModelWithNoSequencesAnswersZero()
    {
        Assert.Equal(0, BoneLibraryMath.ClampSequence(3, 0));
    }

    // ---- Scale -------------------------------------------------------------

    [Fact]
    public void ScaleOneChangesNothing()
    {
        BoneMatrix3x4 direct = BoneSetupMath.AngleMatrix(KAngles, KOrigin);
        Assert.Equal(direct, Placed(1.0f));
    }

    [Fact]
    public void TheTranslationColumnIsUntouched()
    {
        Assert.Equal(KOrigin, Placed(2.5f).Position);
    }

    [Fact]
    public void AllThreeRotationRowsAreMultiplied()
    {
        BoneMatrix3x4 plain = Placed(1.0f);
        BoneMatrix3x4 scaled = Placed(2.5f);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(At(plain, r, c) * 2.5f, At(scaled, r, c));
            }
        }
    }

    [Fact]
    public void AChildBoneGrowsAwayFromItsParentByExactlyTheScale()
    {
        BoneMatrix3x4 boneLocal = BoneMatrix3x4.Identity;
        boneLocal.Position = new Vector(10.0f, 0.0f, 0.0f);
        Vector plain = BoneSetupMath.ConcatTransforms(Placed(1.0f), boneLocal).Position - KOrigin;
        Vector scaled = BoneSetupMath.ConcatTransforms(Placed(2.5f), boneLocal).Position - KOrigin;
        Assert.Equal(plain.Length() * 2.5f, scaled.Length(), 1e-3f);
    }

    [Fact]
    public void ScaleRotationAtOneLeavesTheMatrixBitForBit()
    {
        BoneMatrix3x4 m = Marked(7f);
        m.Row0.Y = -0.0f;
        BoneMatrix3x4 before = m;
        BoneLibraryMath.ScaleRotation(ref m, 1.0f);
        Assert.Equal(BitConverter.SingleToInt32Bits(before.Row0.Y), BitConverter.SingleToInt32Bits(m.Row0.Y));
    }

    // ---- ComposeAttachment -------------------------------------------------

    [Fact]
    public void ANormalAttachmentIsTheBoneTimesItsLocal()
    {
        BoneMatrix3x4 bone = Placed(1.0f);
        BoneMatrix3x4 local = BoneSetupMath.AngleMatrix(new BoneAngle(0f, 90f, 0f), new Vector(4f, 5f, 6f));
        Assert.Equal(BoneSetupMath.ConcatTransforms(bone, local),
                     BoneLibraryMath.ComposeAttachment(bone, local, worldAlign: false));
    }

    [Fact]
    public void AWorldAlignedAttachmentKeepsThePosition()
    {
        BoneMatrix3x4 bone = Placed(1.0f);
        BoneMatrix3x4 local = BoneSetupMath.AngleMatrix(new BoneAngle(30f, 90f, 15f), new Vector(4f, 5f, 6f));
        Assert.Equal(BoneSetupMath.VectorTransform(new Vector(4f, 5f, 6f), bone),
                     BoneLibraryMath.ComposeAttachment(bone, local, worldAlign: true).Position);
    }

    [Fact]
    public void AWorldAlignedAttachmentDropsTheRotation()
    {
        BoneMatrix3x4 bone = Placed(1.0f);
        BoneMatrix3x4 local = BoneSetupMath.AngleMatrix(new BoneAngle(30f, 90f, 15f), new Vector(4f, 5f, 6f));
        BoneMatrix3x4 aligned = BoneLibraryMath.ComposeAttachment(bone, local, worldAlign: true);
        aligned.Position = Vector.Zero;
        Assert.Equal(BoneMatrix3x4.Identity, aligned);
    }

    [Fact]
    public void TheTwoBranchesDoNotAgreeOnARotatedBone()
    {
        BoneMatrix3x4 bone = Placed(1.0f);
        BoneMatrix3x4 local = BoneSetupMath.AngleMatrix(new BoneAngle(30f, 90f, 15f), new Vector(4f, 5f, 6f));
        BoneAngle a = BoneSetupMath.MatrixAngles(BoneLibraryMath.ComposeAttachment(bone, local, true));
        BoneAngle p = BoneSetupMath.MatrixAngles(BoneLibraryMath.ComposeAttachment(bone, local, false));
        Vector diff = new(p.Pitch - a.Pitch, p.Yaw - a.Yaw, p.Roll - a.Roll);
        Assert.True(diff.Length() > 1.0f, $"angles differ by {diff.Length()}");
    }

    // ---- ApplyMergedBones --------------------------------------------------

    [Fact]
    public void AMappedBoneTakesTheSourcesMatrix()
    {
        short[] map = [2, 0, 1];
        BoneMatrix3x4[] source = [Marked(10f), Marked(20f), Marked(30f)];
        BoneMatrix3x4[] own = [Marked(90f), Marked(91f), Marked(92f)];
        int copied = BoneLibraryMath.ApplyMergedBones(map, source, own);
        Assert.Equal((3, source[2], source[0], source[1]), (copied, own[0], own[1], own[2]));
    }

    [Fact]
    public void AnUnmatchedBoneKeepsItsOwnPose()
    {
        short[] map = [0, -1, 2];
        BoneMatrix3x4[] source = [Marked(10f), Marked(20f), Marked(30f)];
        BoneMatrix3x4[] own = [Marked(90f), Marked(91f), Marked(92f)];
        int copied = BoneLibraryMath.ApplyMergedBones(map, source, own);
        Assert.Equal((2, Marked(91f)), (copied, own[1]));
    }

    [Fact]
    public void AnEntryPastTheSourcesBonesIsSkippedAndNotRead()
    {
        short[] map = [7, 0];
        BoneMatrix3x4[] source = [Marked(10f), Marked(20f)];
        BoneMatrix3x4[] own = [Marked(90f), Marked(91f)];
        int copied = BoneLibraryMath.ApplyMergedBones(map, source, own);
        Assert.Equal((1, Marked(90f), source[0]), (copied, own[0], own[1]));
    }

    [Fact]
    public void TheWalkStopsAtTheShorterOfTheMapAndTheOwnBones()
    {
        short[] map = [0, 1, 2, 3];
        BoneMatrix3x4[] source = [Marked(10f), Marked(20f), Marked(30f), Marked(40f)];
        BoneMatrix3x4[] own = [Marked(90f), Marked(91f)];
        int copied = BoneLibraryMath.ApplyMergedBones(map, source, own);
        Assert.Equal((2, source[1]), (copied, own[1]));
    }

    [Fact]
    public void AnEmptyMapCopiesNothing()
    {
        BoneMatrix3x4[] own = [Marked(90f)];
        Assert.Equal((0, Marked(90f)), (BoneLibraryMath.ApplyMergedBones([], [Marked(10f)], own), own[0]));
    }

    [Fact]
    public void AnEmptySourceCopiesNothing()
    {
        short[] map = [0];
        BoneMatrix3x4[] own = [Marked(90f)];
        Assert.Equal((0, Marked(90f)), (BoneLibraryMath.ApplyMergedBones(map, [], own), own[0]));
    }

    [Fact]
    public void AnEmptyOwnArrayCopiesNothing()
    {
        short[] map = [0];
        Assert.Equal(0, BoneLibraryMath.ApplyMergedBones(map, [Marked(10f)], Span<BoneMatrix3x4>.Empty));
    }

    // ---- the data structs --------------------------------------------------

    [Fact]
    public void PlacementResetIsTheOriginUnturnedAtSizeOne()
    {
        BonePlacement p = new() { Origin = Vector.One, Angles = new BoneAngle(1f, 2f, 3f), ModelScale = 4f };
        p.Reset();
        Assert.Equal((Vector.Zero, 0f, 0f, 0f, 1.0f),
                     (p.Origin, p.Angles.Pitch, p.Angles.Yaw, p.Angles.Roll, p.ModelScale));
    }

    [Fact]
    public void InputsResetLeavesTheServersAutoplayShape()
    {
        BoneBuildInputs inputs = new() { AutoplayIkAlways = true, FrameCount = 9 };
        inputs.Reset();
        Assert.Equal((false, 0), (inputs.AutoplayIkAlways, inputs.FrameCount));
    }

    [Fact]
    public void InputsResetLeavesNoLayers()
    {
        BoneBuildInputs inputs = new() { Layers = [new BoneLayerPose(1, 0.5f, 1f)], LayerCount = 1 };
        inputs.Reset();
        Assert.Equal((true, 0), (inputs.Layers is null, inputs.LayerCount));
    }

    [Fact]
    public void InputsResetLeavesNoPoseParametersAndAUnitPlacement()
    {
        BoneBuildInputs inputs = new() { PoseParameter = new float[24], BoneMask = 5, Sequence = 3, Cycle = 0.5f, CurTime = 9f, HasAutoplay = true };
        inputs.Reset();
        Assert.Equal((true, 0, 0, 0f, 0f, false, 1.0f),
                     (inputs.PoseParameter is null, inputs.BoneMask, inputs.Sequence, inputs.Cycle,
                      inputs.CurTime, inputs.HasAutoplay, inputs.Placement.ModelScale));
    }

    [Fact]
    public void InputsResetTakesTheThreeWayBlendConVarDefault()
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        Assert.True(inputs.AnimThreeWayBlend); // anim_3wayblend "1"
    }
}
