using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the extract cluster (<c>BoneSetupExtract</c>): the anim-value
/// decode arms, the raw/anim/no-rot arms of the bone quaternion and position
/// decoders, the decompressed-IK-error decode, the chain builder, the
/// zeroframe blend, and <c>CalcAnimation</c>'s walk. Expectations are exact
/// where the storage is exact (short * scale, half-representable rows);
/// codec-quantized raw payloads compare at a tolerance.
/// </summary>
public class BonesExtractTests
{
    private const float Scale = 1.0f / 32768.0f;
    private const int AllBones = unchecked((int)0xFFFFFFFF);

    private static short S(float turns) => (short)Math.Round(turns / Scale);

    /// <summary>The packed (valid,total) header short: low byte valid, high
    /// byte total — the reader reads them as separate bytes.</summary>
    private static short H(int valid, int total) => (short)(valid | (total << 8));

    // ---- fixtures ----------------------------------------------------------


    /// <summary>A raw wire short (unit scale): the stream stores these
    /// verbatim; multiply by the scale the decoder is called with.</summary>
    private static short R(int raw) => (short)raw;
    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    private static MstudioAnim FirstAnim(StudioHdr hdr, int frame = 0)
    {
        int f = frame;
        return hdr.PAnim(hdr.AnimDesc(0), ref f)
            ?? throw new InvalidOperationException("fixture emitted no anim record");
    }

    private static Quaternion NormalizeNew(float x, float y, float z, float w)
    {
        float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
        return new Quaternion(x / len, y / len, z / len, w / len);
    }

    // ---- ExtractAnimValue (stock 267-336 / 339-369) ------------------------

    [Fact]
    public void ExtractAnimValue_NullStream_ZerosBoth()
    {
        BoneSetupExtract.ExtractAnimValue(3, AnimValueStream.Null, 1f, out float v1, out float v2);
        Assert.Equal(0f, v1);
        Assert.Equal(0f, v2);
        BoneSetupExtract.ExtractAnimValue(3, AnimValueStream.Null, 1f, out float v);
        Assert.Equal(0f, v);
    }

    [Fact]
    public void ExtractAnimValue_TotalOneValidOneFastPath_ReadsRecordOneIntoBoth()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(1, 1), S(0.5f), S(0.9f)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        BoneSetupExtract.ExtractAnimValue(0, s, Scale, out float v1, out float v2);
        // stock 278-283: BOTH outputs take record [1], never a ramp off [0]
        Assert.Equal(0.5f, v1, 5);
        Assert.Equal(0.5f, v2, 5);
    }

    [Fact]
    public void ExtractAnimValue_SingleRef_ReadsBracketingRecordAndLastValid()
    {
        // The 4-arg overload (stock 339-369) has no (1,1) fast path and no
        // ramp: frame 0 on a (1,1) list still lands on record [k+1] = [1].
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(1, 1), S(0.5f), S(0.9f)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        BoneSetupExtract.ExtractAnimValue(0, s, Scale, out float v);
        Assert.Equal(0.5f, v, 5); // rec[1] * scale = 16384/32768

        // walk arm: list0 (total 2) is consumed by frame 2, list1 (3,3)
        // brackets k=0 -> Value(c+k+1) = list1's first record = 20
        StudioHdrBuilder b2 = new();
        b2.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b2.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), R(10), R(11), H(3, 3), R(20), R(21), R(22)] },
            },
        });
        StudioHdr hdr2 = Build(b2);
        AnimValueStream s2 = FirstAnim(hdr2).PosStream(0);
        BoneSetupExtract.ExtractAnimValue(2, s2, 1f, out float vw);

        // last-valid arm: valid(2) <= k(3) -> repeat Value(c+valid) = 11
        StudioHdrBuilder b3 = new();
        b3.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b3.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 5), R(10), R(11)] },
            },
        });
        StudioHdr hdr3 = Build(b3);
        AnimValueStream s3 = FirstAnim(hdr3).PosStream(0);
        BoneSetupExtract.ExtractAnimValue(3, s3, 1f, out float vl);
    }

    [Fact]
    public void ExtractAnimValue_WalksListsUntilBracketingList()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                // list0 consumes frames 0-1, list1 frames 2-4
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), R(10), R(11), H(3, 3), R(20), R(21), R(22)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        BoneSetupExtract.ExtractAnimValue(2, s, 1f, out float v1, out float v2);
        // walked list0 (total 2 <= 2); bracketing list1 record k+1 = [1]
        Assert.Equal(20f, v1, 3);
        Assert.Equal(21f, v2, 3);
    }

    [Fact]
    public void ExtractAnimValue_LastValidFrameNoBlend_KeepsV1()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                // valid 2 < total 5: frames 2,3,4 repeat the last valid record
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 5), R(10), R(11)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        BoneSetupExtract.ExtractAnimValue(3, s, 1f, out float v1, out float v2);
        // stock 324-329: last valid record Value(valid); repeats within the list
        Assert.Equal(11f, v1, 3);
        Assert.Equal(11f, v2, 3);
    }

    [Fact]
    public void ExtractAnimValue_BracketEndPullsBlendFromNextList()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), R(10), R(11), H(2, 2), R(20), R(21)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        BoneSetupExtract.ExtractAnimValue(1, s, 1f, out float v1, out float v2);
        // k=1: valid(2) > 1 but NOT > k+1=2, and total !> k+1 -> stock 317:
        // the blend comes from the NEXT list's first record: Value(valid+2).
        Assert.Equal(11f, v1, 3);
        Assert.Equal(20f, v2, 3);
    }

    [Fact]
    public void ExtractAnimValue_OffEndTerminator_Zeros()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), R(10), R(11)] },
            },
        });
        StudioHdr hdr = Build(b);
        AnimValueStream s = FirstAnim(hdr).PosStream(0);

        // frame 5 walks past the only list; the next header reads (0,0) ->
        // stock's Assert(0) arm: v1 = v2 = 0.
        BoneSetupExtract.ExtractAnimValue(5, s, 1f, out float v1, out float v2);
        Assert.Equal(0f, v1);
        Assert.Equal(0f, v2);
    }

    // ---- CalcBoneQuaternion (stock 374-489) --------------------------------

    [Fact]
    public void CalcBoneQuaternion_RawRot_ReturnsDecodedQuaternion()
    {
        Quaternion want = NormalizeNew(0f, 0f, 0.70710678f, 0.70710678f);
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_RAWROT, RawQuaternion = want },
            },
        });
        StudioHdr hdr = Build(b);

        BoneSetupExtract.CalcBoneQuaternion(0, 0.5f, hdr.Bone(0), null, FirstAnim(hdr), out Quaternion q);
        // Wire-exact expectations, hand-derived (never the port's own decode):
        // the stored quat is (0,0,h,h) about Z, so x=y=0 quantise to the 32768
        // bias and decode to 0; Quantize(h,16384,32767) = floor(f32(h*16384)
        // + .5) = 11585 -> z = 11585/16384 = 0.70709228515625; the decode
        // reconstructs w = sqrt(1 - z^2) = 0.707121312. s = .5 with equal
        // records + zero baseRot leaves it untouched through the blend.
        Assert.Equal(0f, q.X, 6);
        Assert.Equal(0f, q.Y, 6);
        Assert.Equal(0.70709228515625f, q.Z, 6);
        Assert.Equal(0.707121312f, q.W, 6);
    }

    [Fact]
    public void CalcBoneQuaternion_NoRotArm_DeltaAnswersIdentity_NonDeltaBaseQuat()
    {
        Quaternion rest = NormalizeNew(0f, 0f, 0.70710678f, 0.70710678f);
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Quat = rest });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "delta",
            NumFrames = 2,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_DELTA } },
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "static",
            NumFrames = 2,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = 0 } },
        });
        StudioHdr hdr = Build(b);

        int f = 0;
        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdr.Bone(0), null, hdr.PAnim(hdr.AnimDesc(0), ref f)!, out Quaternion qd);
        Assert.Equal(Quaternion.Identity, qd); // delta, no rot: Init(0,0,0,1)

        f = 0;
        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdr.Bone(0), null, hdr.PAnim(hdr.AnimDesc(1), ref f)!, out Quaternion qs);
        Assert.Equal(rest.X, qs.X, 6);
        Assert.Equal(rest.Z, qs.Z, 6);
        Assert.Equal(rest.W, qs.W, 6);
    }

    private static StudioHdr RotBlendRig(bool delta)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, RotScale = new Vector(Scale, Scale, Scale) });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = 0,
                    Flags = StudioConstants.STUDIO_ANIM_ANIMROT | (delta ? StudioConstants.STUDIO_ANIM_DELTA : 0),
                    Rot0 = [H(2, 2), S(0.5f), S(0.75f)],
                },
            },
        });
        return Build(b);
    }

    [Fact]
    public void CalcBoneQuaternion_AnimRot_BlendsFrameAngles()
    {
        StudioHdr hdr = RotBlendRig(delta: true);

        BoneSetupExtract.CalcBoneQuaternion(0, 0.5f, hdr.Bone(0), null, FirstAnim(hdr), out Quaternion q);
        // Hand-derived from stock AngleQuaternion (mathlib_base.cpp:2016) +
        // QuaternionBlend (1545): q1 = (sin .25,0,0,cos .25),
        // q2 = (sin .375,0,0,cos .375) (same hemisphere, align keeps q2); the
        // lerp of two unit quats in one hemisphere normalises to the
        // half-sum angle: (sin .3125, 0, 0, cos .3125).
        Assert.Equal(0.307438523f, q.X, 5);
        Assert.Equal(0f, q.Y, 5);
        Assert.Equal(0f, q.Z, 5);
        Assert.Equal(0.951567948f, q.W, 5);
    }

    [Fact]
    public void CalcBoneQuaternion_AnimRot_ZeroFraction_SingleAngleQuaternion()
    {
        StudioHdr hdr = RotBlendRig(delta: true);

        // s <= 0.001: the fast single-value decode, no blend
        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdr.Bone(0), null, FirstAnim(hdr), out Quaternion q);
        // single-value decode: angle.x = rec[1]*rotscale.x = .5 ->
        // AngleQuaternion gives (sin .25, 0, 0, cos .25)
        Assert.Equal(0.247403964f, q.X, 6);
        Assert.Equal(0f, q.Y, 6);
        Assert.Equal(0f, q.Z, 6);
        Assert.Equal(0.968912423f, q.W, 6);
    }

    [Fact]
    public void CalcBoneQuaternion_NonDelta_AddsBaseRotBeforeBlend()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, RotScale = new Vector(Scale, Scale, Scale), Rot = new Vector(0f, 0f, 0.25f) });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMROT, Rot2 = [H(2, 2), S(0.5f), S(0.5f)] },
            },
        });
        StudioHdr hdr = Build(b);

        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdr.Bone(0), null, FirstAnim(hdr), out Quaternion q);
        // Rot2 stream + non-delta baseRot.z: angle.z = .5 + .25 = .75 ->
        // AngleQuaternion gives (0, 0, sin .375, cos .375)
        Assert.Equal(0f, q.X, 6);
        Assert.Equal(0f, q.Y, 6);
        Assert.Equal(0.366272539f, q.Z, 6);
        Assert.Equal(0.930507600f, q.W, 6);
    }

    [Fact]
    public void CalcBoneQuaternion_FixedAlignment_AppliesAlignmentOnlyForNonDelta()
    {
        // (sin .25, 0, 0, -cos .25): sits in the opposite hemisphere of the
        // decoded (sin .25, 0, 0, cos .25), so the align gate must flip q to
        // its negation. An axis-unit align would tie a==b here and make the
        // fact pass with or without the align call.
        Quaternion align = new(0.247403964f, 0f, 0f, -0.968912423f);
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            RotScale = new Vector(Scale, Scale, Scale),
            Flags = StudioConstants.BONE_FIXED_ALIGNMENT,
            QAlignment = align,
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "nondelta",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMROT, Rot0 = [H(2, 2), S(0.5f), S(0.5f)] },
            },
        });
        StudioHdr hdr = Build(b);

        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdr.Bone(0), null, FirstAnim(hdr), out Quaternion aligned);
        // stock QuaternionAlign (mathlib_base.cpp:1509): a = 4cos^2(.25) >
        // b = 4sin^2(.25) -> qt = -q. Expect the negated plain quaternion;
        // returning align verbatim or skipping the call gives a different X.
        Assert.Equal(-0.247403964f, aligned.X, 5);
        Assert.Equal(0f, aligned.Y, 5);
        Assert.Equal(0f, aligned.Z, 5);
        Assert.Equal(-0.968912423f, aligned.W, 5);

        // delta suppresses the align (oracle 469-472 gates on !delta)
        StudioHdr hdrD = RotBlendRig(delta: true);
        StudioHdrBuilder bd = new();
        bd.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            RotScale = new Vector(Scale, Scale, Scale),
            Flags = StudioConstants.BONE_FIXED_ALIGNMENT,
            QAlignment = align,
        });
        bd.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "delta",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMROT | StudioConstants.STUDIO_ANIM_DELTA, Rot0 = [H(2, 2), S(0.5f), S(0.5f)] },
            },
        });
        StudioHdr hdrDelta = Build(bd);
        BoneSetupExtract.CalcBoneQuaternion(0, 0f, hdrDelta.Bone(0), null, FirstAnim(hdrDelta), out Quaternion notAligned);
        Assert.Equal(0.247403964f, notAligned.X, 5);
        Assert.Equal(0.968912423f, notAligned.W, 5);
    }

    // ---- CalcBonePosition (stock 498-566) ----------------------------------

    [Fact]
    public void CalcBonePosition_RawPos_ReturnsDecodedVector()
    {
        Vector want = new(10f, -3.5f, 0.25f); // half-representable
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_RAWPOS, RawPosition = want } },
        });
        StudioHdr hdr = Build(b);

        BoneSetupExtract.CalcBonePosition(0, 0f, hdr.Bone(0), null, FirstAnim(hdr), out Vector pos);
        Assert.Equal(want, pos);
    }

    [Fact]
    public void CalcBonePosition_NoAnimPosArm_DeltaZero_NonDeltaBasePos()
    {
        Vector rest = new(1f, 2f, 3f);
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Pos = rest });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "d",
            NumFrames = 2,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_DELTA } },
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "n",
            NumFrames = 2,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = 0 } },
        });
        StudioHdr hdr = Build(b);

        int f = 0;
        BoneSetupExtract.CalcBonePosition(0, 0f, hdr.Bone(0), null, hdr.PAnim(hdr.AnimDesc(0), ref f)!, out Vector zeroed);
        Assert.Equal(Vector.Zero, zeroed);

        f = 0;
        BoneSetupExtract.CalcBonePosition(0, 0f, hdr.Bone(0), null, hdr.PAnim(hdr.AnimDesc(1), ref f)!, out Vector based);
        Assert.Equal(rest, based);
    }

    [Fact]
    public void CalcBonePosition_AnimPos_BlendsPairThenAddsBasePos()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            Pos = new Vector(100f, 100f, 100f),
            PosScale = new Vector(Scale, Scale, Scale),
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), S(0.5f), S(0.75f)] },
            },
        });
        StudioHdr hdr = Build(b);

        BoneSetupExtract.CalcBonePosition(0, 0.5f, hdr.Bone(0), null, FirstAnim(hdr), out Vector pos);
        // stock 527-533: v1*(1-s)+v2*s per axis, then (non-delta) + basePos
        // rec[1]=16384/32768=.5, rec[2]=24576/32768=.75: blend .5*.5+.75*.5 =
        // .625 (exact in binary), then + basePos 100 -> 100.625. Null y/z
        // streams leave basePos untouched on those axes.
        Assert.Equal(100.625f, pos.X, 6);
        Assert.Equal(100f, pos.Y, 6);
        Assert.Equal(100f, pos.Z, 6);
    }

    [Fact]
    public void CalcBonePosition_BlendArm_TakesStocksDoubleOneMinusS()
    {
        // Stock 531: pos[j] = v1 * (1.0 - s) + v2 * s — the double literal
        // promotes the subtract and the v1 product (v2 * s stays float, then
        // widens for the add); one rounding to float on the store. Shorts
        // 28462 / -32759 at scale 0x3b168543 decode to exactly
        // v1 = 65.37042236328125, v2 = -75.23960876464844; at
        // s = 0.4245191812515259 the double chain rounds to 0x40b5b877, the
        // all-float chain to 0x40b5b874.
        float scale = BitConverter.Int32BitsToSingle(0x3b168543);
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            PosScale = new Vector(scale, scale, scale),
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = [H(2, 2), R(28462), R(-32759)] },
            },
        });
        StudioHdr hdr = Build(b);
        BoneSetupExtract.ExtractAnimValue(0, FirstAnim(hdr).PosStream(0), scale, out float v1, out float v2);
        Assert.Equal(65.37042236328125f, v1); // the stimulus decodes as intended
        Assert.Equal(-75.23960876464844f, v2);

        BoneSetupExtract.CalcBonePosition(0, 0.4245191812515259f, hdr.Bone(0), null, FirstAnim(hdr), out Vector pos);

        Assert.Equal(0x40b5b877, BitConverter.SingleToInt32Bits(pos.X));
    }

    // ---- CalcDecompressedAnimation (stock 613-659) --------------------------

    [Fact]
    public void CalcDecompressedAnimation_ZeroFraction_FastArms()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            IkRules =
            {
                new StudioHdrBuilder.IkRuleDef
                {
                    Bone = 0,
                    CompressedError = new StudioHdrBuilder.CompressedIkErrorDef
                    {
                        Scale = [2f, 1f, 1f, 0f, 0f, Scale], // px raw 3 * 2 = 6; rz wire-short * 1/32768 = radians
                        Streams =
                        [
                            [H(1, 1), R(3)],     // px record [1]: raw 3, scale 2
                            null, null,        // py, pz null -> 0
                            null, null,        // rx, ry zero
                            [H(1, 1), S(0.5f)],// rz
                        ],
                    },
                },
            },
        });
        StudioHdr hdr = Build(b);
        MstudioCompressedIkError err = hdr.AnimDesc(0).PIKRule(0)!.PCompressedError()!;

        BoneSetupExtract.CalcDecompressedAnimation(err, 0, 0f, out Vector pos, out Quaternion q);
        Assert.Equal(6f, pos.X, 4); // 3 * scale 2
        Assert.Equal(0f, pos.Y);
        Assert.Equal(0f, pos.Z);
        // rz = wire-short 16384 * (1/32768) = .5 rad -> stock AngleQuaternion
        // (mathlib_base.cpp:2016) = (sin .25, 0, 0, cos .25)
        Assert.Equal(0f, q.X, 6);
        Assert.Equal(0f, q.Y, 6);
        Assert.Equal(0.247403964f, q.Z, 6);
        Assert.Equal(0.968912423f, q.W, 6);
    }

    [Fact]
    public void CalcDecompressedAnimation_FractionWithDifferentAngles_Blends()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            IkRules =
            {
                new StudioHdrBuilder.IkRuleDef
                {
                    Bone = 0,
                    CompressedError = new StudioHdrBuilder.CompressedIkErrorDef
                    {
                        Scale = [1f, 1f, 1f, 1f, 1f, Scale], // only rz decodes; the rest are null streams
                        Streams =
                        [
                            null, null, null,
                            null, null,
                            [H(2, 2), S(0.5f), S(0.75f)],
                        ],
                    },
                },
            },
        });
        StudioHdr hdr = Build(b);
        MstudioCompressedIkError err = hdr.AnimDesc(0).PIKRule(0)!.PCompressedError()!;

        BoneSetupExtract.CalcDecompressedAnimation(err, 0, 0.5f, out Vector _, out Quaternion q);
        // AngleQuaternion(.5) blended at .5 with AngleQuaternion(.75)
        // (same hemisphere) normalises to the half-sum angle:
        // (0, 0, sin .3125, cos .3125)
        Assert.Equal(0f, q.X, 5);
        Assert.Equal(0f, q.Y, 5);
        Assert.Equal(0.307438523f, q.Z, 5);
        Assert.Equal(0.951567948f, q.W, 5);
    }

    // ---- BuildBoneChain (stock 3468-3495) ----------------------------------

    private static (StudioHdr Hdr, Vector[] Pos, Quaternion[] Q) TwoBoneRig(Quaternion rootQuat)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "kid", Parent = 0, Pos = new Vector(10f, 0f, 0f) });
        StudioHdr hdr = Build(b);
        // pos[] is the bone-local armature output: the root sits at the origin,
        // the entity's z=5 lives in the rootxform (stock's split).
        Vector[] pos = [new Vector(0f, 0f, 0f), new Vector(10f, 0f, 0f)];
        // the caller supplies the root's quaternion; the kid stays identity so
        // the asserted world pose is a pure parent-concat result.
        Quaternion[] q = [rootQuat, Quaternion.Identity];
        return (hdr, pos, q);
    }

    [Fact]
    public void BuildBoneChain_ConcatsThroughParent_AndUsesRootXform()
    {
        // yaw +90 deg about Z as a hand-written quaternion (sin45, cos45),
        // and the entity rootxform as literal rows: identity basis, z=5.
        (StudioHdr hdr, Vector[] pos, Quaternion[] q) = TwoBoneRig(new Quaternion(0f, 0f, 0.70710678f, 0.70710678f));
        BoneMatrix3x4 root = new()
        {
            Row0 = new Vector4(1f, 0f, 0f, 0f),
            Row1 = new Vector4(0f, 1f, 0f, 0f),
            Row2 = new Vector4(0f, 0f, 1f, 5f),
        };
        BoneMatrix3x4[] w = new BoneMatrix3x4[2];
        BoneSetupExtract.BoneBitList computed = new();

        BoneSetupExtract.BuildBoneChain(hdr, root, pos, q, 1, w, computed);

        // root yawed 90 deg about Z: kid local (10,0,0) lands world (0,10,5).
        // Hand-derived: Rz(90) rows (m01 = -sin, m10 = +sin), translation =
        // Rz(90)*(10,0,0) + (0,0,5).
        Assert.Equal(0f, w[1].Position.X, 3);
        Assert.Equal(10f, w[1].Position.Y, 3);
        Assert.Equal(5f, w[1].Position.Z, 3);
        // the root's own slot pins the rootxform: identity basis concat with
        // the yaw, translation (0,0,5).
        Assert.Equal(0f, w[0].Row0.X, 3);
        Assert.Equal(-1f, w[0].Row0.Y, 3);
        Assert.Equal(0f, w[0].Row0.W, 3);
        Assert.Equal(1f, w[0].Row1.X, 3);
        Assert.Equal(0f, w[0].Row1.Y, 3);
        Assert.Equal(0f, w[0].Row1.W, 3);
        Assert.Equal(1f, w[0].Row2.Z, 3);
        Assert.Equal(5f, w[0].Row2.W, 3);
        // the kid inherits the parent's basis rows
        Assert.Equal(0f, w[1].Row0.X, 3);
        Assert.Equal(-1f, w[1].Row0.Y, 3);
        Assert.Equal(1f, w[1].Row1.X, 3);
        Assert.Equal(1f, w[1].Row2.Z, 3);
    }

    [Fact]
    public void BuildBoneChain_MemoSkipsMarkedBone()
    {
        (StudioHdr hdr, Vector[] pos, Quaternion[] q) = TwoBoneRig(Quaternion.Identity);
        BoneMatrix3x4[] w = new BoneMatrix3x4[2];
        w[1] = BoneMatrix3x4.Identity; // pre-seeded: stock's early return must leave the slot's contents
        BoneSetupExtract.BoneBitList computed = new();
        computed.MarkBone(1);
        BoneSetupExtract.BuildBoneChain(hdr, BoneMatrix3x4.Identity, pos, q, 1, w, computed);
        // stock's early return leaves the slot untouched: assert against the
        // identity LITERALS, not the implementation's own Identity property.
        // Recomputation would overwrite Row0.W (kid local x=10) with 10.
        Assert.Equal(new Vector4(1f, 0f, 0f, 0f), w[1].Row0);
        Assert.Equal(new Vector4(0f, 1f, 0f, 0f), w[1].Row1);
        Assert.Equal(new Vector4(0f, 0f, 1f, 0f), w[1].Row2);
    }

    // ---- CalcZeroframeData (stock 776-877) ---------------------------------

    [Fact]
    public void CalcZeroframeData_SingleFrame_BlendsPosAndRotByWeight()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            Flags = StudioConstants.BONE_HAS_SAVEFRAME_POS | StudioConstants.BONE_HAS_SAVEFRAME_ROT,
        });
        StudioHdrBuilder.AnimDef anim = new()
        {
            Name = "a",
            NumFrames = 2,
            ZeroFrameCount = 1,
            ZeroFramePos = { [0] = [new Vector(10f, 0f, 0f)] },
            ZeroFrameRot = { [0] = [NormalizeNew(0f, 0f, 0.70710678f, 0.70710678f)] },
        };
        b.Anims.Add(anim);
        StudioHdr hdr = Build(b);

        Vector[] pos = [new Vector(0f, 0f, 0f)];
        Quaternion[] q = [Quaternion.Identity];
        BoneSetupExtract.CalcZeroframeData(hdr, hdr, null, hdr.AnimDesc(0), 0f, pos, q, AllBones, 0.5f);

        // 0*(1-.5) + row*.5: 10*.5 = 5 exactly; y/z untouched by a zero row.
        Assert.Equal(5f, pos[0].X, 6);
        Assert.Equal(0f, pos[0].Y, 6);
        Assert.Equal(0f, pos[0].Z, 6);
        // QuaternionBlend(Identity, (0,0,h,h), .5) (mathlib_base.cpp:1545,
        // dot > 0 so no flip) then QuaternionNormalize: the lerp is
        // (0,0,h/2,(1+h)/2) with |lerp|^2 = (1+h)/2 exactly, giving
        // (0, 0, sin(pi/8), cos(pi/8)).
        Assert.Equal(0f, q[0].X, 6);
        Assert.Equal(0f, q[0].Y, 6);
        Assert.Equal(0.3826834324f, q[0].Z, 5);
        Assert.Equal(0.9238795325f, q[0].W, 5);
    }

    [Fact]
    public void CalcZeroframeData_BoneMaskedOut_StrideStillAdvances()
    {
        // Bone 0 carries the mask bit, bone 1 does not; both carry SAVEFRAME_POS.
        // Masking bone 1 must not shift bone 0's read (stock keeps pData +=
        // outside the gate), and bone 1 stays untouched.
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = StudioConstants.BONE_HAS_SAVEFRAME_POS | StudioConstants.BONE_USED_BY_HITBOX });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = -1, Flags = StudioConstants.BONE_HAS_SAVEFRAME_POS });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            ZeroFrameCount = 1,
            ZeroFramePos = { [0] = [new Vector(10f, 0f, 0f)], [1] = [new Vector(20f, 0f, 0f)] },
        });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcZeroframeData(hdr, hdr, null, hdr.AnimDesc(0), 0f, pos, q, StudioConstants.BONE_USED_BY_HITBOX, 1f);

        Assert.Equal(10f, pos[0].X, 3); // bone 0 applied from its own row
        Assert.Equal(0f, pos[1].X, 6);  // bone 1 masked: untouched
    }

    [Fact]
    public void CalcZeroframeData_MultiFrame_HermiteWindow()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = StudioConstants.BONE_HAS_SAVEFRAME_POS });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 4,
            ZeroFrameCount = 3,
            ZeroFrameSpan = 1,
            // asymmetric rows so a symmetric (uniform-10) bug can't satisfy the literal
            ZeroFramePos = { [0] = [new Vector(0f, 0f, 0f), new Vector(10f, 0f, 0f), new Vector(30f, 0f, 0f)] },
        });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        BoneSetupExtract.CalcZeroframeData(hdr, hdr, null, hdr.AnimDesc(0), 1.5f, pos, q, AllBones, 1f);

        // Hand-derived from stock HermiteSpline (mathlib_base.cpp:1712):
        // t=.5 -> tSq=.25, tCube=.125; b1 = 3t-2t^3 = 1, b2 = t^3-t^2 = -.25,
        // b3 = t^2-t^3 = .25, b4 = t^2-2t^3+t = .5 with p0=0, p1=10, p2=30:
        // 10*1 + 30*(-.25) + 0*.25 + 10*.5 = 18.75.
        Assert.Equal(18.75f, pos[0].X, 6);
        Assert.Equal(0f, pos[0].Y, 6);
        Assert.Equal(0f, pos[0].Z, 6);
    }

    // ---- CalcAnimation (stock 1031-1159) ------------------------------------

    [Fact]
    public void CalcAnimation_WithRecords_DecodesWalkAndPreInitialsOthers()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = StudioConstants.BONE_USED_BY_HITBOX });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(5f, 0f, 0f), Quat = new Quaternion(0f, 0f, 0.70710678f, 0.70710678f), Flags = StudioConstants.BONE_USED_BY_HITBOX });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            // only bone 0 carries a record: bone 1 must take the rest-pose arm
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = 0,
                    Flags = StudioConstants.STUDIO_ANIM_RAWPOS | StudioConstants.STUDIO_ANIM_RAWROT,
                    RawPosition = new Vector(1f, 2f, 3f),
                    RawQuaternion = Quaternion.Identity,
                },
            },
        });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcAnimation(
            hdr, pos, q, hdr.SeqDesc(0), 0, 0, 0f, AllBones, 0f,
            new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList());

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]); // walk arm (float4 rawpos: bit-exact)
        Assert.Equal(new Vector(5f, 0f, 0f), pos[1]); // non-delta rest-pose arm
        // bone 0's RAWROT of identity is a bit-exact (32768,32768,16384) wire
        // -> (0,0,0,1); bone 1 has no record and must echo its OWN float4
        // quat, so a wrong-record read (bone 0's identity) goes red.
        Assert.Equal(Quaternion.Identity, q[0]);
        Assert.Equal(0f, q[1].X, 6);
        Assert.Equal(0f, q[1].Y, 6);
        Assert.Equal(0.70710678f, q[1].Z, 6);
        Assert.Equal(0.70710678f, q[1].W, 6);
    }

    [Fact]
    public void CalcAnimation_MissingBlock_PreInitialisesThenZeroframesAtOne()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0",
            Parent = -1,
            Pos = new Vector(1f, 1f, 1f),
            Flags = StudioConstants.BONE_HAS_SAVEFRAME_POS,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 3,
            SectionFrames = 2,
            Sections = [(-1, 0), (-1, 0), (-1, 0)], // block -1 = "needs recompile" -> pAnim null
            ZeroFrameCount = 1,
            ZeroFramePos = { [0] = [new Vector(9f, 0f, 0f)] },
        });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        BoneSetupExtract.CalcAnimation(
            hdr, pos, q, hdr.SeqDesc(0), 0, 0, 1f, AllBones, 0f,
            new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList());

        // non-delta pre-init puts (1,1,1); zeroframe at weight 1 replaces it
        Assert.Equal(9f, pos[0].X, 3);
        Assert.Equal(0f, pos[0].Y, 6);
    }

    // ---- CalcLocalHierarchyAnimation (stock 664-768) ------------------------

    private static StudioHdrBuilder LocalHierarchyHdr(int iNewParent, float tail = 1f, short[]? rot = null)
    {
        // b0 root at (10,0,0); b1 child rest-pose (5,0,0). The anim records
        // bone 0 only (RAWPOS (1,2,3)), so bone 1 takes the pre-init arm
        // (5,0,0) unless the hierarchy override replaces it. The hierarchy
        // record's compressed error is px raw 20 * scale 1 = (20,0,0), rot
        // zero unless `rot` gives the three raw RadianEuler shorts (scale
        // 0.001 rad) — iStart 0 so frame 0 decodes at cycle 0.
        short[]?[] streams = rot is null
            ? [new short[] { H(1, 1), R(20) }, null, null, null, null, null]
            :
            [
                new short[] { H(1, 1), R(20) }, null, null,
                new short[] { H(1, 1), rot[0] }, new short[] { H(1, 1), rot[1] }, new short[] { H(1, 1), rot[2] },
            ];
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0", Parent = -1, Pos = new Vector(10f, 0f, 0f),
            Flags = StudioConstants.BONE_USED_BY_HITBOX,
        });
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b1", Parent = 0, Pos = new Vector(5f, 0f, 0f),
            Flags = StudioConstants.BONE_USED_BY_HITBOX,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = 0,
                    Flags = StudioConstants.STUDIO_ANIM_RAWPOS,
                    RawPosition = new Vector(1f, 2f, 3f),
                },
            },
            Hierarchy =
            {
                new StudioHdrBuilder.LocalHierarchyDef
                {
                    IBone = 1,
                    INewParent = iNewParent,
                    Start = 0f, Peak = 0f, Tail = tail, End = 1f, // tail-peak controls the ramp
                    IStart = 0,
                    LocalAnim = new StudioHdrBuilder.CompressedIkErrorDef
                    {
                        Scale = [1f, 1f, 1f, 0.001f, 0.001f, 0.001f],
                        Streams = streams,
                    },
                },
            },
        });
        return b;
    }

    [Fact]
    public void CalcAnimation_LocalHierarchy_ReplacesBoneWithComposedError()
    {
        // Gate open (iNewParent = 0, both bones masked): the override builds
        // boneToWorld from the walked pose, hangs the decompressed error
        // under bone 0's world matrix, then back-solves through bone 0's
        // inverse: local = translate(20,0,0), weight 1 -> pos[1] = (20,0,0)
        // hard. The rest-pose 5 would be the answer without the override.
        StudioHdr hdr = Build(LocalHierarchyHdr(iNewParent: 0));

        Vector[] pos = [Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcAnimation(
            hdr, pos, q, hdr.SeqDesc(0), 0, 0, 0f, AllBones, 0f,
            new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList());

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]); // walk arm unaffected
        Assert.Equal(20f, pos[1].X, 4); // the override, not the rest-pose 5
        Assert.Equal(0f, pos[1].Y, 5);
        Assert.Equal(Quaternion.Identity, q[1]);
    }

    [Fact]
    public void CalcAnimation_LocalHierarchy_MinusOneNewParent_SkipsOverride()
    {
        // Stock reads boneFlags(iNewParent) unguarded (oracle 1146-1151);
        // for -1 that int-before-the-array read answers "masked out", so
        // the override never runs and bone 1 keeps the pre-init rest pose.
        StudioHdr hdr = Build(LocalHierarchyHdr(iNewParent: -1));

        Vector[] pos = [Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcAnimation(
            hdr, pos, q, hdr.SeqDesc(0), 0, 0, 0f, AllBones, 0f,
            new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList());

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]);
        Assert.Equal(new Vector(5f, 0f, 0f), pos[1]); // pre-init arm survives
    }

    [Fact]
    public void CalcLocalHierarchyAnimation_PartialWeight_LerpsBackSolveOntoPose()
    {
        // tail - peak = 0.5 < 1 opens the ramp; cycle 0.7 lands on the tail
        // arm: weight = (1-0.7)/(1-0.5) = 0.6, splined to 3t^2-2t^3 = 0.648.
        // The back-solved local transform is translate(20,0,0) (the parent
        // chain cancels), so pos[1] = VectorLerp(p1, pos[1], 0.648) =
        // 20 + (10 - 20) * 0.648 = 13.52 — a partial blend, not the weight-1
        // hard write (20) nor the untouched input (10).
        StudioHdr hdr = Build(LocalHierarchyHdr(iNewParent: 0, tail: 0.5f));
        MstudioLocalHierarchy pHierarchy = hdr.AnimDesc(0).PHierarchy(0)!;

        Vector[] pos = [new Vector(2f, 0f, 0f), new Vector(10f, 0f, 0f)];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcLocalHierarchyAnimation(
            hdr, new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList(),
            pos, q, pHierarchy, iBone: 1, iNewParent: 0, cycle: 0.7f, iFrame: 0, flFraq: 0f, AllBones);

        Assert.Equal(13.52f, pos[1].X, 3);
        Assert.Equal(0f, pos[1].Y, 5);
    }

    [Fact]
    public void CalcLocalHierarchyAnimation_LocalXform_TakesStocksRadianEulerRoute()
    {
        // Stock 720-721: AngleMatrix( localQ, localPos, localXform ) with a
        // Quaternion resolves only through the implicit RadianEuler(Quaternion)
        // ctor (vector.h:1736-1739): QuaternionAngles, then AngleMatrix of
        // the euler — the NB-06 route (docs/plans/needs-blend.md). It is not
        // bit-identical to QuaternionMatrix(localQ, localPos). Bone 0 sits at
        // the identity, so the back-solve through its inverse is exact and
        // q[1] is MatrixAngles of localXform itself.
        StudioHdr hdr = Build(LocalHierarchyHdr(iNewParent: 0, rot: [400, 700, 1000]));
        MstudioLocalHierarchy pHierarchy = hdr.AnimDesc(0).PHierarchy(0)!;
        BoneSetupExtract.CalcDecompressedAnimation(
            pHierarchy.PLocalAnim()!, 0, 0f, out Vector localPos, out Quaternion localQ);
        (Quaternion stockQ, _) = BoneSetupMath.MatrixAnglesQuaternion(
            BoneSetupMath.AngleMatrix(BoneSetupMath.QuaternionAngles(localQ), localPos));
        (Quaternion directQ, _) = BoneSetupMath.MatrixAnglesQuaternion(
            BoneSetupMath.QuaternionMatrix(localQ, localPos));
        Assert.NotEqual(directQ, stockQ); // the input tells the two routes apart

        Vector[] pos = [Vector.Zero, new Vector(5f, 0f, 0f)];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneSetupExtract.CalcLocalHierarchyAnimation(
            hdr, new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList(),
            pos, q, pHierarchy, iBone: 1, iNewParent: -1, cycle: 0f, iFrame: 0, flFraq: 0f, AllBones);

        Assert.Equal(stockQ, q[1]);
        Assert.Equal(20f, pos[1].X, 4);
    }

    // ---- CalcVirtualAnimation (stock 884-1023, through CalcAnimation) -------

    [Fact]
    public void CalcAnimation_VirtualModel_RoutesThroughGroupHdrAndBoneMaps()
    {
        // The base hdr carries only the bone table (b0 at (1,1,1)); the
        // sequence and anim live in the resolved include. A fold is the
        // only route to them: the pre-init reads the sequence group's rest
        // pose, the walk decodes the anim group's RAWPOS (7,8,9) through
        // animMasterBone/seqBoneMap. Without the fold SeqDesc(0) has no
        // sequence at all.
        StudioHdrBuilder g = new();
        g.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0", Parent = -1, Flags = StudioConstants.BONE_USED_BY_HITBOX,
        });
        g.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = 0,
                    Flags = StudioConstants.STUDIO_ANIM_RAWPOS,
                    RawPosition = new Vector(7f, 8f, 9f),
                },
            },
        });
        g.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        byte[] groupBytes = g.Build();

        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b0", Parent = -1, Pos = new Vector(1f, 1f, 1f),
            Flags = StudioConstants.BONE_USED_BY_HITBOX,
        });
        b.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "g", Name = "grp" });
        b.AddIncludeImage("grp", groupBytes);
        StudioHdr hdr = Build(b);

        Assert.True(hdr.IsVirtual); // the fold took
        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        BoneSetupExtract.CalcAnimation(
            hdr, pos, q, hdr.SeqDesc(0), 0, 0, 0f, AllBones, 0f,
            new BoneMatrix3x4[hdr.NumBones], new BoneSetupExtract.BoneBitList());

        Assert.Equal(new Vector(7f, 8f, 9f), pos[0]); // group anim walk, folded
    }

    // ---- virtualmodel fold: pose-parameter widening (studio_virtualmodel
    // .cpp:472-486) — the fold's ONLY mutation of a record's contents ------

    [Fact]
    public void Fold_DuplicatePoseParameter_WideningPersistsToGlobalRead()
    {
        // Base "turn" (0,1); include A (-5,5); include B (10,20). Stock
        // mutates the winner's record in place, so the ranges grow
        // monotonically: after A widens the winner to (-5,5), B compares
        // against THAT and lands (-5,20). Written to a discarded view (the
        // old bug — PoseParamLocal news a fresh record per call), the first
        // fold's widening never reaches PoseParameter (raw (0,1) reads) and
        // B's arm compares the raw (0,1), collapsing the range to (0,20).
        StudioHdrBuilder a = new();
        a.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        a.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "turn", Start = -5f, End = 5f });
        StudioHdrBuilder bld = new();
        bld.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        bld.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "turn", Start = 0f, End = 1f });
        bld.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        bld.AddIncludeImage("grpA", a.Build());
        StudioHdr twoGroup = Build(bld);
        Assert.True(twoGroup.IsVirtual);

        // First fold alone: winner widened from (0,1) toward (-5,5). The
        // buggy fold reads back the raw image pair.
        Assert.Equal(1, twoGroup.NumPoseParameters);
        Assert.Equal(-5f, twoGroup.PoseParameter(0).Start);
        Assert.Equal(5f, twoGroup.PoseParameter(0).End);

        // Three groups: the arm that proves persistence across fold steps.
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "turn", Start = 10f, End = 20f });
        StudioHdrBuilder bld3 = new();
        bld3.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        bld3.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "turn", Start = 0f, End = 1f });
        bld3.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        bld3.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "b", Name = "grpB" });
        bld3.AddIncludeImage("grpA", a.Build());
        bld3.AddIncludeImage("grpB", b.Build());
        StudioHdr three = Build(bld3);

        Assert.Equal(1, three.NumPoseParameters); // one winner
        Assert.Equal(-5f, three.PoseParameter(0).Start); // not the raw 0
        Assert.Equal(20f, three.PoseParameter(0).End);   // not the collapsed 5-from-A-only, not 1
        // The loop field is never widened: still the winner's image value.
        Assert.Equal(0f, three.PoseParameter(0).Loop);
        // Reads are idempotent (the accessor re-stamps a fresh view).
        Assert.Equal(-5f, three.PoseParameter(0).Start);
        Assert.Equal(20f, three.PoseParameter(0).End);
        // The winner's own LOCAL record stays the pristine image value —
        // the overlay is read-time state on the global accessor only.
        Assert.Equal(0f, three.PoseParamLocal(0).Start);
        Assert.Equal(1f, three.PoseParamLocal(0).End);
        // A non-folded hdr is untouched by the overlay machinery.
        StudioHdr plain = Build(a);
        Assert.False(plain.IsVirtual);
        Assert.Equal(-5f, plain.PoseParameter(0).Start);
        Assert.Equal(5f, plain.PoseParameter(0).End);
    }

    [Fact]
    public void Fold_ZeroKneeBaseChain_AdoptsIncludeKneeDir()
    {
        // studio_virtualmodel.cpp:573-592: at the end of AppendIKLocks, an
        // include with the SAME chain count copies its link-0 kneeDir into
        // every base chain whose own is zero. Studio_SolveIK
        // (bone_setup.cpp:2690-2705) branches on LengthSqr > 0, so without
        // the copy the fold silently changes every knee from "use the
        // authored knee" to "derive it from the two bone lengths".
        // Base chain: link-0 kneeDir (0,0,0). Include chain: (1,0,0).
        // Second include chain (10,10,10) must NOT overwrite: stock mutates
        // the base image in place, so after the first copy the zero-test
        // fails for the second include (first supplier wins).
        static StudioHdrBuilder Chain(float kneeX)
        {
            StudioHdrBuilder b = new();
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
            b.IkChains.Add(new StudioHdrBuilder.IkChainDef
            {
                Name = "leg",
                Links = { (0, new Vector(kneeX, 0f, 0f)), (0, Vector.Zero), (0, Vector.Zero) },
            });
            return b;
        }

        StudioHdrBuilder baseModel = Chain(0f);
        baseModel.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        baseModel.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "b", Name = "grpB" });
        baseModel.AddIncludeImage("grpA", Chain(1f).Build());
        baseModel.AddIncludeImage("grpB", Chain(10f).Build());
        StudioHdr hdr = Build(baseModel);
        Assert.True(hdr.IsVirtual);

        // The base chain reads the include's knee — the exact Vector3 the
        // Studio_SolveIK branch consumes. (Buggy fold: reads (0,0,0).)
        Assert.Equal(new Vector(1f, 0f, 0f), hdr.IkChain(0).Link(0).KneeDir);
        // The include's own standalone hdr keeps its knee regardless.
        Assert.Equal(new Vector(1f, 0f, 0f), Build(Chain(1f)).IkChain(0).Link(0).KneeDir);
        // A non-zero base knee survives the fold untouched.
        StudioHdrBuilder baseKept = Chain(2f);
        baseKept.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        baseKept.AddIncludeImage("grpA", Chain(1f).Build());
        StudioHdr kept = Build(baseKept);
        Assert.Equal(new Vector(2f, 0f, 0f), kept.IkChain(0).Link(0).KneeDir);
        // Mismatched chain counts: the copy is gated on equal numikchains
        // (stock's guard). A base with 1 chain, include with 2: no copy.
        StudioHdrBuilder baseOne = Chain(0f);
        baseOne.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        StudioHdrBuilder twoChains = Chain(1f);
        twoChains.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "arm",
            Links = { (0, new Vector(0f, 1f, 0f)), (0, Vector.Zero), (0, Vector.Zero) },
        });
        baseOne.AddIncludeImage("grpA", twoChains.Build());
        StudioHdr mismatch = Build(baseOne);
        Assert.Equal(Vector.Zero, mismatch.IkChain(0).Link(0).KneeDir);
    }

    [Fact]
    public void Fold_IncludeTree_TwoDeep_OrdersGroupsDepthFirst_LosingDuplicateWinner()
    {
        // stock AppendModels (studio_virtualmodel.cpp:119-137) creates a
        // group and appends the model's fold members, then RECURSES into
        // that include before the next sibling gets a group. With base ->
        // {A, B} and A -> {A1}, the group order is root, A, A1, B — so
        // A1's duplicate sequence "shared" is appended before B's and
        // wins (no STUDIO_OVERRIDE: later duplicates map to the winner).
        // The old FIFO queue ordered root, A, B, A1: B's sequence won and
        // A1's merely mapped to it. ActWeight is the per-model witness of
        // which hdr the global entry routes to (SeqDesc follows
        // VirtualEntry.Group).
        StudioHdrBuilder a1 = new();
        a1.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        a1.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "shared", ActWeight = 11, AnimTable = [0], Weights = [1f],
        });

        StudioHdrBuilder a = new();
        a.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        a.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a1", Name = "grpA1" });
        a.AddIncludeImage("grpA1", a1.Build());

        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "shared", ActWeight = 22, AnimTable = [0], Weights = [1f],
        });

        StudioHdrBuilder baseModel = new();
        baseModel.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        baseModel.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "a", Name = "grpA" });
        baseModel.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "b", Name = "grpB" });
        baseModel.AddIncludeImage("grpA", a.Build());
        baseModel.AddIncludeImage("grpB", b.Build());
        // The fold resolves every name — the nested A1 included — through
        // the ROOT's resolver (one mdlcache lookup per name).
        baseModel.AddIncludeImage("grpA1", a1.Build());
        StudioHdr hdr = Build(baseModel);
        Assert.True(hdr.IsVirtual);
        // One global sequence: the duplicates collapsed.
        Assert.Equal(1, hdr.NumSequences);
        // DFS: A1 (deep, earlier in the walk) owns it — FIFO gave 22.
        Assert.Equal(11, hdr.SeqDesc(0).ActWeight);
    }

    // ---- fold label matching: stock's stricmp (studio_virtualmodel.cpp
    //      177, 263, 329, 396, 459) folds ASCII letters only. Each fixture
    //      pairs a non-ASCII case pair (é U+00E9 / É U+00C9, UTF-8 C3 A9 /
    //      C3 89), which OrdinalIgnoreCase would merge and stricmp keeps
    //      apart, with an ASCII case pair both merge (the control).

    private static StudioHdrBuilder FoldModel(params string[] bones)
    {
        StudioHdrBuilder b = new();
        for (int i = 0; i < bones.Length; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = bones[i], Parent = i - 1 < 0 ? -1 : 0 });
        }

        return b;
    }

    private static StudioHdr FoldWith(StudioHdrBuilder baseModel, StudioHdrBuilder include)
    {
        baseModel.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "inc", Name = "grpInc" });
        baseModel.AddIncludeImage("grpInc", include.Build());
        StudioHdr hdr = Build(baseModel);
        Assert.True(hdr.IsVirtual);
        return hdr;
    }

    [Fact]
    public void Fold_SequenceLabels_FoldAsciiOnly()
    {
        StudioHdrBuilder baseModel = FoldModel("b0");
        StudioHdrBuilder inc = FoldModel("b0");
        foreach ((StudioHdrBuilder m, string label) in new[] { (baseModel, "runé"), (baseModel, "walk"), (inc, "runÉ"), (inc, "WALK") })
        {
            m.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = label, AnimTable = [0], Weights = [1f] });
        }

        // runé, walk, runÉ: WALK folds onto walk, runÉ stays its own.
        Assert.Equal(3, FoldWith(baseModel, inc).NumSequences);
    }

    [Fact]
    public void Fold_AnimationNames_FoldAsciiOnly()
    {
        StudioHdrBuilder baseModel = FoldModel("b0");
        StudioHdrBuilder inc = FoldModel("b0");
        foreach ((StudioHdrBuilder m, string name) in new[] { (baseModel, "idleé"), (baseModel, "pose"), (inc, "idleÉ"), (inc, "POSE") })
        {
            m.Anims.Add(new StudioHdrBuilder.AnimDef { Name = name, NumFrames = 1 });
        }

        Assert.Equal(3, FoldWith(baseModel, inc).NumAnims);
    }

    [Fact]
    public void Fold_AttachmentNames_FoldAsciiOnly()
    {
        StudioHdrBuilder baseModel = FoldModel("b0");
        StudioHdrBuilder inc = FoldModel("b0");
        foreach ((StudioHdrBuilder m, string name) in new[] { (baseModel, "tipé"), (baseModel, "hand"), (inc, "tipÉ"), (inc, "HAND") })
        {
            m.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = name, LocalBone = 0 });
        }

        Assert.Equal(3, FoldWith(baseModel, inc).NumGlobalAttachments);
    }

    [Fact]
    public void Fold_PoseParameterNames_FoldAsciiOnly()
    {
        StudioHdrBuilder baseModel = FoldModel("b0");
        StudioHdrBuilder inc = FoldModel("b0");
        foreach ((StudioHdrBuilder m, string name) in new[] { (baseModel, "aimé"), (baseModel, "move"), (inc, "aimÉ"), (inc, "MOVE") })
        {
            m.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = name, Start = 0f, End = 1f });
        }

        Assert.Equal(3, FoldWith(baseModel, inc).NumPoseParameters);
    }

    [Fact]
    public void Fold_BoneNames_FoldAsciiOnly()
    {
        // The bone map is observed through AppendAttachments' skip: an
        // include attachment whose bone found no base match (masterBone -1)
        // is dropped. armÉ must not map onto armé; LEG maps onto leg.
        StudioHdrBuilder baseModel = FoldModel("b0", "armé", "leg");
        StudioHdrBuilder inc = FoldModel("b0", "armÉ", "LEG");
        inc.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "onArm", LocalBone = 1 });
        inc.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "onLeg", LocalBone = 2 });

        StudioHdr hdr = FoldWith(baseModel, inc);
        Assert.Equal(1, hdr.NumGlobalAttachments);
        Assert.Equal("onLeg", hdr.Attachment(0).Name);
    }
}
