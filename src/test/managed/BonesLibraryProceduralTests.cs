using System;
using System.Buffers.Binary;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for bone_library.cpp layer 3's managed twin:
/// <see cref="BoneLibrary.PoseToWorldProcedural"/> and
/// <see cref="BoneLibrary.BuildMergeMap"/>. Expectations are derived from the
/// native bodies and cross-checked against the ported pieces they call
/// (Studio_BuildMatrices via PoseToWorld, CalcProceduralBone, JiggleBones).
/// </summary>
public class BonesLibraryProceduralTests
{
    private const int UsedByHitbox = 0x100;
    private const int UsedByAttachment = 0x200;
    private const int UsedByAnything = BoneLibrary.BoneUsedByAnything;
    private const int Proc = ProceduralBoneRule.BoneAlwaysProcedural;

    private static readonly BoneMatrix3x4 Sentinel = new()
    {
        Row0 = new Vector4(7f, 7f, 7f, 7f),
        Row1 = new Vector4(7f, 7f, 7f, 7f),
        Row2 = new Vector4(7f, 7f, 7f, 7f),
    };

    private static StudioHdr Make(StudioHdrBuilder b, bool procedural = false)
    {
        byte[] image = procedural ? b.BuildWithProcedural() : b.Build();
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    /// <summary>root -> arm -> hand, each flagged <paramref name="handFlags"/> for the hand.</summary>
    private static StudioHdr Chain(int handFlags = UsedByAnything)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "arm", Parent = 0, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "hand", Parent = 1, Flags = handFlags });
        return Make(b);
    }

    private static (Vector[] Pos, Quaternion[] Q) ChainPose()
    {
        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        Array.Fill(q, Quaternion.Identity);
        pos[0] = new Vector(1f, 2f, 3f);
        pos[1] = new Vector(0f, 0f, 10f);
        q[1] = BoneSetupMath.AngleQuaternion(new BoneAngle(0f, 90f, 0f));
        pos[2] = new Vector(5f, 0f, 0f);
        return (pos, q);
    }

    private static BonePlacement Placement(float scale = 1f) =>
        new() { Origin = new Vector(100f, 0f, 0f), Angles = new BoneAngle(0f, 30f, 0f), ModelScale = scale };

    private static BoneMatrix3x4[] Sentinels(int n)
    {
        BoneMatrix3x4[] m = new BoneMatrix3x4[n];
        Array.Fill(m, Sentinel);
        return m;
    }

    // ---- refusals ----------------------------------------------------------

    [Fact]
    public void NoModelIsRefused()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        Assert.Equal(0, BoneLibrary.PoseToWorldProcedural(null, Placement(), pos, q, UsedByAnything, 0f, null, null, new BoneMatrix3x4[3]));
    }

    [Fact]
    public void NoOutputIsRefused()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        Assert.Equal(0, BoneLibrary.PoseToWorldProcedural(Chain(), Placement(), pos, q, UsedByAnything, 0f, null, null, null));
    }

    [Fact]
    public void ABufferTooSmallIsRefusedAndUntouched()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = Sentinels(2);
        int n = BoneLibrary.PoseToWorldProcedural(Chain(), Placement(), pos, q, UsedByAnything, 0f, null, null, world);
        Assert.Equal((0, Sentinel), (n, world[0]));
    }

    // ---- ordinary bones ----------------------------------------------------

    [Theory]
    [InlineData(1f)]
    [InlineData(2.5f)]
    public void OrdinaryBonesAgreeWithStudioBuildMatrices(float scale)
    {
        StudioHdr hdr = Chain();
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] expected = new BoneMatrix3x4[3];
        BoneLibrary.PoseToWorld(hdr, Placement(scale), pos, q, UsedByAnything, expected);
        BoneMatrix3x4[] world = new BoneMatrix3x4[3];
        int n = BoneLibrary.PoseToWorldProcedural(hdr, Placement(scale), pos, q, UsedByAnything, 0f, null, null, world);
        Assert.Equal((3, expected[0], expected[1], expected[2]), (n, world[0], world[1], world[2]));
    }

    [Fact]
    public void ABoneOutsideTheMaskIsLeftAsTheCallerLeftIt()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = Sentinels(3);
        BoneLibrary.PoseToWorldProcedural(Chain(handFlags: UsedByHitbox), Placement(), pos, q, UsedByAttachment, 0f, null, null, world);
        Assert.Equal(Sentinel, world[2]);
    }

    [Fact]
    public void ABoneTheCallerAlreadyPlacedIsNotWrittenAgain()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneSetupExtract.BoneBitList computed = new();
        computed.MarkBone(1);
        BoneMatrix3x4[] world = Sentinels(3);
        BoneLibrary.PoseToWorldProcedural(Chain(), Placement(), pos, q, UsedByAnything, 0f, computed, null, world);
        Assert.Equal(Sentinel, world[1]);
    }

    [Fact]
    public void AChildOfAPlacedBoneHangsOffThePlacedMatrix()
    {
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneSetupExtract.BoneBitList computed = new();
        computed.MarkBone(1);
        BoneMatrix3x4 placed = BoneSetupMath.AngleMatrix(new BoneAngle(10f, 20f, 30f), new Vector(-4f, 5f, 6f));
        BoneMatrix3x4[] world = [Sentinel, placed, Sentinel];
        BoneLibrary.PoseToWorldProcedural(Chain(), Placement(), pos, q, UsedByAnything, 0f, computed, null, world);
        Assert.Equal(BoneSetupMath.ConcatTransforms(placed, BoneSetupMath.QuaternionMatrix(q[2], pos[2])), world[2]);
    }

    // ---- computed procedural bones -----------------------------------------

    private static StudioHdr QuatInterpModel()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "twist", Parent = 0, Flags = UsedByAnything | Proc });
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 1,
            ProcType = ProceduralBoneRule.ProcQuatInterp,
            QuatInterp = new StudioHdrBuilder.QuatInterpDef
            {
                Control = 0,
                Triggers = { new StudioHdrBuilder.QuatTriggerDef { InvTolerance = 1f, Pos = new Vector(3f, 0f, 0f) } },
            },
        });
        return Make(b, procedural: true);
    }

    [Fact]
    public void AComputedBoneIsWhatCalcProceduralBoneWrites()
    {
        StudioHdr hdr = QuatInterpModel();
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 0f, null, null, world);

        BoneMatrix3x4[] expected = [world[0], Sentinel];
        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 1, new CBoneAccessor(expected)));
        Assert.Equal(expected[1], world[1]);
    }

    [Fact]
    public void AComputedBoneIsNotTheAnimationsPose()
    {
        StudioHdr hdr = QuatInterpModel();
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 0f, null, null, world);
        Assert.NotEqual(BoneSetupMath.ConcatTransforms(world[0], BoneSetupMath.QuaternionMatrix(q[1], pos[1])), world[1]);
    }

    // ---- jiggle bones ------------------------------------------------------

    /// <summary>root -> tail, the tail flagged procedural with
    /// <paramref name="procType"/> and, when given, a jiggle record.</summary>
    private static StudioHdr TailModel(int procType, float[]? record)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "tail", Parent = 0, Flags = UsedByAnything | Proc });
        byte[] built = b.Build();
        int length = record is null ? 0 : 4 * record.Length;
        byte[] image = new byte[built.Length + length + 4];
        built.CopyTo(image, 0);
        int at = (built.Length + 3) & ~3;
        for (int i = 0; record is not null && i < record.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(at + 4 * i, 4), record[i]);
        }

        int bone1 = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4)) + 216;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 164, 4), procType);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 168, 4), record is null ? 0 : at - bone1);
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    /// <summary>A boing record: amplitude 0.5, nothing else.</summary>
    private static float[] BoingRecord()
    {
        float[] r = new float[35];
        r[0] = BitConverter.Int32BitsToSingle(JiggleBoneParams.IsBoing);
        r[1] = 10f;  // length
        r[34] = 0.5f; // boingAmplitude
        return r;
    }

    private static BoneMatrix3x4 TailGoal(in BoneMatrix3x4 root, Vector[] pos, Quaternion[] q) =>
        BoneSetupMath.ConcatTransforms(root, BoneSetupMath.QuaternionMatrix(q[1], pos[1]));

    [Fact]
    public void AJiggleBoneWithNoSpringStateIsDrawnFromThePose()
    {
        StudioHdr hdr = TailModel(ProceduralBoneRule.ProcJiggle, BoingRecord());
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 10f, null, null, world);
        Assert.Equal(TailGoal(world[0], pos, q), world[1]);
    }

    [Fact]
    public void AJiggleBoneWithNoRecordIsDrawnFromThePose()
    {
        StudioHdr hdr = TailModel(ProceduralBoneRule.ProcJiggle, null);
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 10f, null, new JiggleBones(), world);
        Assert.Equal(TailGoal(world[0], pos, q), world[1]);
    }

    [Fact]
    public void AJiggleBoneGoesThroughTheSpringFromItsGoal()
    {
        StudioHdr hdr = TailModel(ProceduralBoneRule.ProcJiggle, BoingRecord());
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 10f, null, new JiggleBones(), world);

        Assert.True(JiggleBoneParams.TryRead(hdr, 1, out JiggleBoneParams p));
        BoneMatrix3x4 expected = default;
        new JiggleBones().BuildJiggleTransformations(1, 10f, p, TailGoal(world[0], pos, q), ref expected, JiggleBones.DefaultFramerateCutoff);
        Assert.Equal(expected, world[1]);
    }

    [Fact]
    public void TheSpringIsTheCallersAndOutlivesTheCall()
    {
        StudioHdr hdr = TailModel(ProceduralBoneRule.ProcJiggle, BoingRecord());
        (Vector[] pos, Quaternion[] q) = ChainPose();
        JiggleBones springs = new();
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 10f, null, springs, new BoneMatrix3x4[2]);
        Assert.Equal(10f, springs.GetJiggleData(1, 99f, Vector.Zero, Vector.Zero).LastUpdate);
    }

    [Fact]
    public void AnUnknownProcTypeIsDrawnFromThePose()
    {
        StudioHdr hdr = TailModel(9, BoingRecord());
        (Vector[] pos, Quaternion[] q) = ChainPose();
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneLibrary.PoseToWorldProcedural(hdr, Placement(), pos, q, UsedByAnything, 10f, null, new JiggleBones(), world);
        Assert.Equal(TailGoal(world[0], pos, q), world[1]);
    }

    // ---- BuildMergeMap -----------------------------------------------------

    private static StudioHdr Named(params string[] names)
    {
        StudioHdrBuilder b = new();
        for (int i = 0; i < names.Length; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = names[i], Parent = i - 1, Flags = UsedByAnything });
        }

        return Make(b);
    }

    [Fact]
    public void EachBoneReadsTheSourceBoneOfTheSameName()
    {
        short[] map = new short[4];
        int matched = BoneLibrary.BuildMergeMap(Named("pelvis", "spine", "hand"), Named("hand", "pelvis"), map);
        Assert.Equal((2, (short)1, (short)-1, (short)0), (matched, map[0], map[1], map[2]));
    }

    [Fact]
    public void NoSourceMapsEveryBoneToNothing()
    {
        short[] map = [5, 5, 5];
        int matched = BoneLibrary.BuildMergeMap(Named("pelvis", "spine"), null, map);
        Assert.Equal((0, (short)-1, (short)-1, (short)5), (matched, map[0], map[1], map[2]));
    }

    [Fact]
    public void AMapTooSmallIsRefused()
    {
        short[] map = [5];
        int matched = BoneLibrary.BuildMergeMap(Named("pelvis", "spine"), Named("pelvis"), map);
        Assert.Equal((0, (short)5), (matched, map[0]));
    }

    [Fact]
    public void NoOwnModelIsRefused()
    {
        Assert.Equal(0, BoneLibrary.BuildMergeMap(null, Named("pelvis"), new short[4]));
    }
}
