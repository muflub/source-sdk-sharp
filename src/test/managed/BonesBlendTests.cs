using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the blend cluster of bone_setup.cpp (the static helpers of
/// <see cref="BoneSetupBlend"/>). Expectations are closed-form numbers and
/// quaternions derived by hand from the oracle bodies (1249-1365,
/// 1373-1522, 1531-1627, 1632-1677, 1782-1812, 1816-1830, 1840-1915,
/// 4499-4577); the fixtures use the shared <see cref="StudioHdrBuilder"/>.
/// Bone flags are set explicitly (0x1 = the mask every fact passes) because
/// <c>StudioHdr.BoneFlags</c> gates the whole cluster.
/// </summary>
public class BonesBlendTests
{
    private const int Mask = 0x1;
    private const int AllBones = unchecked((int)0xFFFFFFFF);

    private const int StudioWorld = 0x4000;   // studio.h:3092
    private const int StudioPost = 0x0010;    // studio.h:3082

    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    /// <summary>RotX via the axis-X (0,0,sin,cos) form.</summary>
    private static Quaternion RotX(float deg)
    {
        double r = deg * Math.PI / 180.0;
        return new Quaternion((float)Math.Sin(r / 2.0), 0f, 0f, (float)Math.Cos(r / 2.0));
    }

    /// <summary>RotY via the axis-Y (0,sin,0,cos) form.</summary>
    private static Quaternion RotY(float deg)
    {
        double r = deg * Math.PI / 180.0;
        return new Quaternion(0f, (float)Math.Sin(r / 2.0), 0f, (float)Math.Cos(r / 2.0));
    }

    private static void AssertVec(Vector expected, Vector actual, int precision = 5)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }

    private static void AssertQuat(Quaternion expected, Quaternion actual, int precision = 5)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
        Assert.Equal(expected.W, actual.W, precision);
    }

    private static void AssertMtx(BoneMatrix3x4 expected, BoneMatrix3x4 actual, int precision = 5)
    {
        Vector4 e0 = expected.Row0, e1 = expected.Row1, e2 = expected.Row2;
        Vector4 a0 = actual.Row0, a1 = actual.Row1, a2 = actual.Row2;
        Assert.Equal(e0.X, a0.X, precision);
        Assert.Equal(e0.Y, a0.Y, precision);
        Assert.Equal(e0.Z, a0.Z, precision);
        Assert.Equal(e1.X, a1.X, precision);
        Assert.Equal(e1.Y, a1.Y, precision);
        Assert.Equal(e1.Z, a1.Z, precision);
        Assert.Equal(e2.X, a2.X, precision);
        Assert.Equal(e2.Y, a2.Y, precision);
        Assert.Equal(e2.Z, a2.Z, precision);
        AssertVec(expected.Position, actual.Position, precision);
    }

    // ---- fixtures ---------------------------------------------------------

    /// <summary>Two-bone chain: b0 root (flags 0x1), b1 child at (10,0,0)
    /// (extra flag 0x2 so a 0x3 mask reaches both).</summary>
    private static StudioHdr ChainHdr(StudioHdrBuilder.SeqDef seq)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(10f, 0f, 0f), Flags = 0x3 });
        b.Sequences.Add(seq);
        return Build(b);
    }

    // ---- SlerpBones (stock 1373-1522) --------------------------------------

    private static void SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2)
    {
        q1 = [RotX(10f), RotX(20f)];
        q2 = [RotX(50f), RotX(80f)];
        pos1 = [new Vector(1f, 2f, 3f), new Vector(10f, 0f, 0f)];
        pos2 = [new Vector(7f, 8f, 9f), new Vector(10f, 4f, 0f)];
    }

    [Fact]
    public void SlerpBones_WithZeroWeight_KeepsDestinationPose()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0f, AllBones);

        AssertQuat(RotX(10f), q1[0]);
        AssertQuat(RotX(20f), q1[1]);
        AssertVec(new Vector(1f, 2f, 3f), pos1[0], 0);
        AssertVec(new Vector(10f, 0f, 0f), pos1[1], 0);
    }

    [Fact]
    public void SlerpBones_ClampsWeightAboveOne_ToCopySource()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 1.5f, AllBones);

        AssertQuat(q2[0], q1[0]);
        AssertQuat(q2[1], q1[1]);
        AssertVec(pos2[0], pos1[0], 0);
        AssertVec(pos2[1], pos1[1], 0);
    }

    [Fact]
    public void SlerpBones_WithWeightOne_CopiesSource()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 1f, AllBones);

        AssertQuat(q2[0], q1[0]);
        AssertVec(pos2[1], pos1[1], 0);
    }

    [Fact]
    public void SlerpBones_IgnoresBonesWithZeroWeight()
    {
        // bone 1 carries weight 0: its line must stay exactly at the seed.
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 0f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.5f, AllBones);

        AssertQuat(RotX(20f), q1[1]);
        AssertVec(new Vector(10f, 0f, 0f), pos1[1], 0);
        // the weighted bone did move, so the run was not a wholesale no-op
        AssertQuat(RotX(30f), q1[0]); // slerp(RotX50, RotX10, 0.5) on the X axis
    }

    [Fact]
    public void SlerpBones_MidpointBlendsByWeightScaledSlerp()
    {
        // s = 0.5 with weights (1, 0.3): bone 0 blends at t = 0.5, bone 1 at
        // t = 0.15 (own weight); positions lerp with the same weights.
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 0.3f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.5f, AllBones);

        AssertQuat(BoneSetupMath.QuaternionSlerp(RotX(50f), RotX(10f), 0.5f), q1[0]);
        AssertVec(new Vector(4f, 5f, 6f), pos1[0]); // (1,2,3)*0.5 + (7,8,9)*0.5
        AssertQuat(BoneSetupMath.QuaternionSlerp(RotX(80f), RotX(20f), 0.85f), q1[1]);
        AssertVec(new Vector(10f, 0.6f, 0f), pos1[1]); // (10,0,0)*0.85 + (10,4,0)*0.15
    }

    [Fact]
    public void SlerpBones_DeltaArm_AccumulatesQuatAndAddsPoses()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f, 1f],
            Flags = StudioConstants.STUDIO_DELTA,
        });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.4f, AllBones);

        // non-POST delta: QuaternionSM(s2, q2, q1); positions add.
        AssertQuat(BoneSetupMath.QuaternionSM(0.4f, RotX(50f), RotX(10f)), q1[0]);
        AssertVec(BoneSetupMath.VectorMA(new Vector(1f, 2f, 3f), 0.4f, new Vector(7f, 8f, 9f)), pos1[0]);
    }

    [Fact]
    public void SlerpBones_PostDeltaArm_UsesQuaternionMA()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f, 1f],
            Flags = StudioConstants.STUDIO_DELTA | StudioPost,
        });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.4f, AllBones);

        AssertQuat(BoneSetupMath.QuaternionMA(RotX(10f), 0.4f, RotX(50f)), q1[0]);
    }

    [Fact]
    public void SlerpBones_FixedAlignment_SlerpsWithoutAntipodalFlip()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 | StudioConstants.BONE_FIXED_ALIGNMENT });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);

        Quaternion neg = new(-RotX(60f).X, 0f, 0f, -RotX(60f).W);
        Quaternion[] q1 = [RotX(60f)];
        Vector[] pos1 = [new Vector(2f, 0f, 0f)];
        Quaternion[] q2 = [neg];
        Vector[] pos2 = [new Vector(6f, 0f, 0f)];

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.5f, AllBones);

        // q2 is exactly antipodal to q1 (cosom = -1), so stock's
        // QuaternionSlerpNoAlign takes its antipodal arm: the perpendicular
        // of q (qt = (-q.y, q.x, -q.w, q.z)), scaled at t = s1 = 0.5 by
        // sin/cos of 45°, with index 3 left at q.z (the loop runs to 3, not
        // 4). Literal expectation: p = (-0.5,0,0,-0.866), q = (0.5,0,0,0.866)
        //   -> (-0.3536, 0.3536, -0.6124, 0).
        // The aligned QuaternionSlerp would flip q2's sign and land
        // (0, 0, 0, -0.866) — these literals fail on that answer.
        Assert.Equal(-0.3535534f, q1[0].X, 5);
        Assert.Equal(0.3535534f, q1[0].Y, 5);
        Assert.Equal(-0.6123724f, q1[0].Z, 5);
        Assert.Equal(0f, q1[0].W, 5);
        AssertVec(new Vector(4f, 0f, 0f), pos1[0]);
    }

    [Fact]
    public void SlerpBones_WorldSpace_RoutesThroughWorldChainBackSolve()
    {
        // STUDIO_WORLD on a two-bone chain. The local arm would blend the
        // child by slerp(q2[1], q1[1], 0.85); the world arm instead back-
        // solves the child from the blended WORLD chain — a different answer
        // — which is the observable routing evidence.
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f, 1f],
            Flags = StudioWorld,
        });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);
        // Non-collinear child: with both quats about X, world back-solve and
        // the local arm both reduce to an angle lerp and agree — the routing
        // evidence below needs rotations about different axes.
        q2[1] = RotY(80f);

        BoneSetupBlend.SlerpBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.15f, AllBones);

        // ---- hand-derived expectations -------------------------------------
        // Literals below come from an independent stock-semantics run (a
        // transcription of bone_setup.cpp:1373-1522 plus the mathlib bodies
        // it calls), NOT from BoneSetupMath.  They are the evidence; the
        // port helpers are the subject.
        //
        // Stock's loop keeps ONE dest/src/target matrix set across iterations
        // (the CBoneBitList computed-lists, bone_setup.cpp:1369-1371), so
        // bone 0 is marked in pass 0 and pass 1's dest chain REUSES
        // destBoneToWorld[0] as built from the ORIGINAL q1[0] = RotX 10 — not
        // the blended root the root arm wrote.  The back-solve then inverts
        // targetBoneToWorld[0], which pass 0 wrote with the BLENDED quat.
        //
        // root (n == -1): targetW0 = AngleMatrix(slerp(RotX10, RotX50, 0.15))
        // at destPos (1,2,3); q1[0] = MatrixAngles(targetW0).Q.
        AssertQuat(new Quaternion(0.13917309f, 0f, 0f, 0.990268f), q1[0]);

        // child: destQ = Mult(RotX10, RotX20), destPos = (10,0,0) transformed
        // by the cached (original-root) parent; srcQ = Mult(RotX50, RotY80);
        // targetQ = slerp(destQ, srcQ, 0.15)
        //   = (0.27831173, 0.09514085, 0.04436491, 0.9547368);
        // targetW1 = the STOCK target route — AngleMatrix(targetQ) via the
        // implicit RadianEuler(Quaternion) ctor (vector.h:1736 ->
        // QuaternionAngles, mathlib_base.cpp:2191) — NOT QuaternionMatrix;
        // local = concat(invert(targetW0), targetW1), q1[1] = its Q.
        AssertQuat(new Quaternion(0.1427295f, 0.10038934f, 0.030692115f, 0.984179f), q1[1]);

        // Routing discriminators — each alternative answer is far away:
        //   local arm (slerp(q2[1], q1[1], 1 - s2)):
        //     (0.15125313, 0.1049763, 0, 0.98290503) — Z differs by 0.031
        //   QuaternionMatrix target route (no RadianEuler round-trip):
        //     (0.14272957, 0.100389354, 0.030692104, 0.984179) — ulp-level,
        //     so the guard runs at precision 7 (full float32 precision) to
        //     keep the bit-parity contract observable.
        Assert.NotEqual(0f, q1[1].Z, 3);
        Assert.NotEqual(0.15125313f, q1[1].X, 3);
        Assert.NotEqual(0.14272957f, q1[1].X, 7);

        // local bone length lerps by s2 (stock's Lerp(s,a,b) = a+(b-a)s):
        AssertVec(new Vector(10f, 0.6f, 0f), pos1[1]);
    }
    [Fact]
    public void BlendBones_WithZeroWeight_IsNoOp()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.BlendBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0f, AllBones);

        AssertQuat(RotX(10f), q1[0]);
        AssertVec(new Vector(1f, 2f, 3f), pos1[0], 0);
    }

    [Fact]
    public void BlendBones_WithFullWeight_CopiesOnlyWeightedBones()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 0f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);

        BoneSetupBlend.BlendBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 1f, AllBones);

        AssertQuat(q2[0], q1[0]);
        AssertVec(pos2[0], pos1[0], 0);
        // bone 1 has weight 0: untouched despite s >= 1
        AssertQuat(RotX(20f), q1[1]);
        AssertVec(new Vector(10f, 0f, 0f), pos1[1], 0);
    }

    [Fact]
    public void BlendBones_Midpoint_BlendsQuatAndLerpsPos()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        SlerpSeed(out Quaternion[] q1, out Vector[] pos1, out Quaternion[] q2, out Vector[] pos2);
        // snapshot the sources: BlendBones overwrites q1/pos1 in place, so
        // the expected blend must be built from pre-call copies.
        Quaternion[] q1Before = [q1[0], q1[1]];
        BoneSetupBlend.BlendBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.3f, AllBones);

        // s2 = 0.3, s1 = 0.7: q3 = QuaternionBlend(q2, q1, s1); pos lerp.
        AssertQuat(BoneSetupMath.QuaternionBlend(q2[0], q1Before[0], 0.7f), q1[0]);
        AssertVec(new Vector(2.8f, 3.8f, 4.8f), pos1[0]); // (1,2,3)*0.7 + (7,8,9)*0.3
    }

    [Fact]
    public void BlendBones_FixedAlignment_BlendsWithoutAntipodalFlip()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 | StudioConstants.BONE_FIXED_ALIGNMENT });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);

        // q2 = RotX(300) sits on the opposite hemisphere from q1 = RotX(60)
        // (dot = -0.5): the aligned QuaternionBlend would negate q1 before
        // lerping and answer a different quaternion than the NoAlign arm.
        Quaternion q1Src = RotX(60f);
        Quaternion[] q1 = [q1Src];
        Vector[] pos1 = [new Vector(2f, 0f, 0f)];
        Quaternion[] q2 = [RotX(300f)];
        Vector[] pos2 = [new Vector(6f, 0f, 0f)];

        BoneSetupBlend.BlendBones(hdr, q1, pos1, hdr.SeqDesc(0), 0, q2, pos2, 0.85f, AllBones);

        // s2 = 0.85, s1 = 0.15. The fixed-alignment bone routes through
        // QuaternionBlendNoAlign; the aligned arm differs for this pair, so
        // the equality-vs-difference pair is the routing evidence.
        AssertQuat(BoneSetupMath.QuaternionBlendNoAlign(q2[0], q1Src, 0.15f), q1[0]);
        Assert.NotEqual(BoneSetupMath.QuaternionBlend(q2[0], q1Src, 0.15f).X, q1[0].X, 4);
        AssertVec(new Vector(5.4f, 0f, 0f), pos1[0]); // 2*0.15 + 6*0.85
    }

    // ---- ScaleBones (stock 1632-1677) ---------------------------------------

    [Fact]
    public void ScaleBones_ScalesPositionDontOffsetTowardZero()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        Quaternion[] q1 = [Quaternion.Identity, Quaternion.Identity];
        Vector[] pos1 = [new Vector(2f, 4f, 6f), Vector.Zero];

        BoneSetupBlend.ScaleBones(hdr, q1, pos1, 0, 0.7f, AllBones);

        // stock (oracle 1674): pos1 = VectorScale(pos1, s2), NOT a lerp of
        // pos1 toward zero by (1 - s2) — the offset misread gives 0.3.
        AssertVec(new Vector(1.4f, 2.8f, 4.2f), pos1[0]);
        // identity stays identity after the identity blend + normalize.
        AssertQuat(Quaternion.Identity, q1[0]);
    }

    // The three facts below seed bone 0 with the NEGATIVE-w form of RotX(40)
    // — (0.34202015, 0, 0, -0.9396926) — because ScaleBones' stock call site
    // (bone_setup.cpp:1743) is QuaternionIdentityBlend(q1[i], s1, q1[i]) with
    // all three operands ALIASED (mathlib_base.cpp:1580): the branch tests
    // qt.w, which under aliasing still holds the ORIGINAL w (index 3 is not
    // touched until the branch's own store), and multiplies the ORIGINAL
    // p.w.  A port that tests the SCALED w reads the wrong operand, and only
    // a negative-w seed tells the two readings apart.

    [Fact]
    public void ScaleBones_NegativeWAtZeroScale_FadesToNegativeIdentity()
    {
        // s = 0 -> t = s1 = 1, sclp = 1 - t = 0.  Stock's aliased body:
        // xyz = p.xyz * 0 = 0; the branch tests the ORIGINAL w = -0.9396926
        // < 0 -> w = p.w * 0 - 1 = -1; normalize((0,0,0,-1)) -> NEGATIVE
        // identity (0,0,0,-1), which is stock's answer, not +identity.
        // The buggy non-aliased reading scales first (w = -0.0), and
        // -0.0 < 0.0 is false, so it takes the plus arm and lands on
        // +identity — this fact is red on that operand bug.
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        Quaternion[] q1 = [RotX(40f) with { W = -RotX(40f).W }, Quaternion.Identity];
        Vector[] pos1 = [new Vector(1f, 1f, 1f), Vector.Zero];

        BoneSetupBlend.ScaleBones(hdr, q1, pos1, 0, 0f, AllBones);

        AssertQuat(new Quaternion(0f, 0f, 0f, -1f), q1[0]);
        AssertVec(Vector.Zero, pos1[0]); // VectorScale(pos, s2 = 0)
    }

    [Fact]
    public void ScaleBones_NegativeWAtNegativeScale_KeepsStockBranchSign()
    {
        // s = -0.5 (ScaleBones does NOT clamp s; stock 1661-1675 has no
        // clamp, unlike SlerpBones' s <= 0 early return) -> t = 1.5,
        // sclp = -0.5.  Stock's aliased body tests the ORIGINAL w < 0:
        //   x = 0.34202015 * -0.5 = -0.17101007
        //   w = -0.9396926 * -0.5 - 1.5 = 0.4698463 - 1.5 = -1.0301537
        //   normalize: r = sqrt(0.0292444 + 1.0612166) = 1.0442515,
        //            inv = 0.9576 — x = -0.16376331, w = -0.9864997,
        //   y = z = -0.0 (scaled zeros; the float sign of zero is not an
        //       observable — float equality treats -0 == 0).
        // The buggy scaled-w reading instead tests p.w * sclp = +0.4698463,
        // which is NOT < 0, so it takes the plus arm: w = 0.4698463 + 1.5 =
        // 1.9698463, normalizing to (-0.08648861, -0, -0, +0.9962528) —
        // every component differs.  Literals from the independent
        // stock-semantics scratch run (bone_setup.cpp:1743, 1580-1599).
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        Quaternion[] q1 = [RotX(40f) with { W = -RotX(40f).W }, Quaternion.Identity];
        Vector[] pos1 = [new Vector(1f, 1f, 1f), Vector.Zero];

        BoneSetupBlend.ScaleBones(hdr, q1, pos1, 0, -0.5f, AllBones);

        AssertQuat(new Quaternion(-0.16376331f, 0f, 0f, -0.9864997f), q1[0]);
        AssertVec(new Vector(-0.5f, -0.5f, -0.5f), pos1[0]);
    }

    [Fact]
    public void ScaleBones_NegativeWMidScale_BlendsTowardNegativeIdentity()
    {
        // s = 0.7 -> t = s1 = 0.3, sclp = 0.7.  Both readings take the minus
        // arm here (original w and scaled w are both negative), so this fact
        // pins the VALUE: x = 0.34202015 * 0.7 = 0.2394141, and the
        // original-w test (-0.9396926 < 0) gives
        // w = -0.9396926 * 0.7 - 0.3 = -0.9577848; normalize
        // r = sqrt(0.0573191 + 0.9173784) = 0.9872542, inv = 1.0129105 ->
        // x = 0.24250503, w = -0.9701502 (literals from the independent
        // stock-semantics run).  A branch sign-flip (plus instead of minus)
        // gives w = -0.3577848, which this fact's compare rejects.
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 1f] });
        Quaternion[] q1 = [RotX(40f) with { W = -RotX(40f).W }, Quaternion.Identity];
        Vector[] pos1 = [new Vector(1f, 1f, 1f), Vector.Zero];

        BoneSetupBlend.ScaleBones(hdr, q1, pos1, 0, 0.7f, AllBones);

        AssertQuat(new Quaternion(0.24250503f, 0f, 0f, -0.9701502f), q1[0]);
        AssertVec(BoneSetupMath.VectorScale(new Vector(1f, 1f, 1f), 0.7f), pos1[0]);
    }

    [Fact]
    public void ScaleBones_IgnoresUnweightedBones()
    {
        StudioHdr hdr = ChainHdr(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f, 0f] });
        Quaternion[] q1 = [RotX(30f), RotX(45f)];
        Vector[] pos1 = [new Vector(1f, 0f, 0f), new Vector(9f, 9f, 9f)];

        BoneSetupBlend.ScaleBones(hdr, q1, pos1, 0, 0.5f, AllBones);

        AssertQuat(RotX(45f), q1[1]);
        AssertVec(new Vector(9f, 9f, 9f), pos1[1], 0);
    }

    // ---- BoneSetupMath quaternion helpers, hand-pinned ----------------------
    // (bone_setup.cpp file-locals 1235-1311; the SlerpBones delta/post-delta
    // facts above can only be as good as these pins, so the expectations here
    // come from the independent stock-semantics run, not BoneSetupMath.)

    private static readonly Quaternion A37 = RotX(37f);  // (0.31730467,0,0,0.94832367)
    private static readonly Quaternion B61 = RotY(61f);  // (0,0.5075384,0,0.8616292)

    [Fact]
    public void QuaternionSM_MatchesHandDerivedMidpoint()
    {
        // stock 1235: QuaternionSM(s,p,q) = normalize(Mult(QuaternionScale(p,s), q)).
        // s = 0.5 makes QuaternionScale nonlinear (asin/sin of the half
        // hemisphere angle), so the closed form "(p+q)/2 normalised" is NOT
        // the answer: hand-run gives
        //   (0.13850045, 0.50093853, 0.081583, 0.8504249).
        // (0.31730467/2, 0.5075384/2, 0, (0.94832367+0.8616292)/2) normalised
        // would give z = 0 — the nonzero z is the nonlinearity's fingerprint.
        Quaternion got = BoneSetupMath.QuaternionSM(0.5f, A37, B61);
        AssertQuat(new Quaternion(0.13850045f, 0.50093853f, 0.081583f, 0.8504249f), got);
    }

    [Fact]
    public void QuaternionSM_EndpointsCollapseToNormalizations()
    {
        // s = 0: QuaternionScale(p,0) = (0,0,0,+1) for w > 0, so the product
        // is q and the result is exactly normalize(q):
        //   (0, 0.5075384, 0, 0.8616292).
        // s = 1: scale(p,1) reproduces p through asin/sin, product p*q:
        //   (0.27339888, 0.48131067, 0.16104424, 0.8171033).
        AssertQuat(new Quaternion(0f, 0.5075384f, 0f, 0.8616292f), BoneSetupMath.QuaternionSM(0f, A37, B61));
        AssertQuat(new Quaternion(0.27339888f, 0.48131067f, 0.16104424f, 0.8171033f),
            BoneSetupMath.QuaternionSM(1f, A37, B61));
    }

    [Fact]
    public void QuaternionMA_EndpointsAndMidMatchHandDerived()
    {
        // stock 1262: QuaternionMA(p,s,q) = normalize(Mult(p, QuaternionScale(q,s)))
        // — the scale rides on q, the OTHER operand of SM.  s = 0 collapses to
        // normalize(p) = (0.31730467, 0, 0, 0.94832367); s = 1 to the p*q
        // product (0.27339897, 0.48131055, 0.16104425, 0.8171033) — note the
        // y/z ulps differ from SM(1,...): scale(q,1) and scale(p,1) are
        // different round-trips, pinning which operand carries the scale.
        // Midpoint: (0.30613154, 0.2494387, 0.083461024, 0.91493076).
        AssertQuat(new Quaternion(0.31730467f, 0f, 0f, 0.94832367f), BoneSetupMath.QuaternionMA(A37, 0f, B61));
        AssertQuat(new Quaternion(0.27339897f, 0.48131055f, 0.16104425f, 0.8171033f),
            BoneSetupMath.QuaternionMA(A37, 1f, B61));
        AssertQuat(new Quaternion(0.30613154f, 0.2494387f, 0.083461024f, 0.91493076f),
            BoneSetupMath.QuaternionMA(A37, 0.5f, B61));
    }

    [Fact]
    public void QuaternionAccumulate_AlignsBeforeSumming()
    {
        // stock 1290: q2 = QuaternionAlign(p,q); return p + s*q2.  Align
        // compares a = sum (p-q)^2 against b = sum (p+q)^2 and negates q
        // when a > b (negative hemisphere).  Seed q as -B61: a > b there, so
        // align restores +B61 and
        //   Accumulate(A37, 0.3, -B61) = (0.31730467, 0.15226153, 0, 1.2068124).
        // Skipping the align sums the raw negated q instead:
        //   (0.31730467, -0.15226153, 0, 0.6898349) — y flips sign and w
        //   moves by 0.52, both observable at precision 5.
        AssertQuat(new Quaternion(0.31730467f, 0.15226153f, 0f, 1.2068124f),
            BoneSetupMath.QuaternionAccumulate(A37, 0.3f, -B61));
        // Already-aligned q takes the a <= b arm: align is the identity and
        // the sum is the plain p + 0.3*q — the same literal, produced by the
        // other branch:
        AssertQuat(new Quaternion(0.31730467f, 0.15226153f, 0f, 1.2068124f),
            BoneSetupMath.QuaternionAccumulate(A37, 0.3f, B61));
    }

    [Fact]
    public void SimpleSpline_MatchesClosedForm3t2Minus2t3()
    {
        // mathlib_base SimpleSpline (mathlib.h:1146): vs = t*t; return
        // 3*vs - 2*vs*t (int literals promote; plain float math).
        //   S(0) = 0, S(1) = 3-2 = 1, S(0.5) = 0.75-0.25 = 0.5 (exact binary).
        //   S(0.25) = 3/16 - 2/64 = 0.1875 - 0.03125 = 0.15625 (exact binary).
        //   S(1/3): t = 0.33333334 (float), 3t^2 = 0.33333337, 2t^3 =
        //          0.07407410 -> 0.25925928 = float 7/27, NOT 4/27: the
        //          4/27 = t^3-2t^2+t shape is the Hermite ease
        //          (HermiteSpline's SimpleSpline-like p0+p1 arm), not this
        //          function; the closed form of the stock text is 3t^2-2t^3.
        Assert.Equal(0f, BoneSetupMath.SimpleSpline(0f));
        Assert.Equal(1f, BoneSetupMath.SimpleSpline(1f));
        Assert.Equal(0.5f, BoneSetupMath.SimpleSpline(0.5f));
        Assert.Equal(0.15625f, BoneSetupMath.SimpleSpline(0.25f));
        Assert.Equal(0.25925928f, BoneSetupMath.SimpleSpline(1f / 3f), 7);
    }

    // ---- InitPose (stock 1782-1812) -----------------------------------------

    [Fact]
    public void InitPose_SeedsFromBoneRecordsAndRespectsMask()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Pos = new Vector(1f, 2f, 3f), Quat = RotX(15f), Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(4f, 5f, 6f), Quat = RotX(45f), Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b2", Parent = 0, Pos = new Vector(7f, 8f, 9f), Flags = 0x2 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero, Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];
        BoneSetupBlend.InitPose(hdr, pos, q, 0x1);

        AssertVec(new Vector(1f, 2f, 3f), pos[0], 0);
        AssertQuat(RotX(15f), q[0]);
        AssertVec(new Vector(4f, 5f, 6f), pos[1], 0);
        AssertQuat(RotX(45f), q[1]);
        // masked out: untouched
        AssertVec(Vector.Zero, pos[2], 0);
        AssertQuat(Quaternion.Identity, q[2], 0);
    }

    [Fact]
    public void InitPose_PrefersLinearBoneTablesWhenHdr2Present()
    {
        StudioHdrBuilder b = new();
        b.WithLinearBones = true;
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Pos = new Vector(1f, 2f, 3f), Quat = RotX(15f), Flags = 0x1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [new Vector(-99f, -99f, -99f)];
        Quaternion[] q = [Quaternion.Identity];
        BoneSetupBlend.InitPose(hdr, pos, q, 0x1);

        // The SoA tables exist and carry the values the port must have read
        // through (the builder mirrors Bones into them; BoneFlags already
        // routes through them, proving the tables are live in this image).
        MstudioLinearBone lb = hdr.LinearBones()
            ?? throw new InvalidOperationException("fixture emitted no linearbones");
        AssertVec(lb.Position(0), pos[0], 0);
        AssertQuat(lb.Quaternion(0), q[0], 5);
        AssertVec(new Vector(1f, 2f, 3f), pos[0], 0);
    }

    // ---- PoseIsAllZeros (stock 1816-1830) ------------------------------------

    [Fact]
    public void PoseIsAllZeros_ReflectsAnimFlagAndGuardsBadIndex()
    {
        // Single sequence, two blend cells: a second seqdesc record would
        // hit the builder's interleaved-seqdesc bug (see needs-blend.md).
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "zero", NumFrames = 1, Flags = StudioConstants.STUDIO_ALLZEROS });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "real", NumFrames = 1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 2,
            GroupSize0 = 1,
            GroupSize1 = 2,
            AnimTable = [0, 1],
        });
        StudioHdr hdr = Build(b);

        Assert.True(BoneSetupBlend.PoseIsAllZeros(hdr, 0, hdr.SeqDesc(0), 0, 0));
        Assert.False(BoneSetupBlend.PoseIsAllZeros(hdr, 0, hdr.SeqDesc(0), 0, 1));
    }

    // ---- Calc3WayBlendIndices (stock 1840-1915) ------------------------------

    /// <summary>3x3 blend cell so odd cells can address i0+2 / i1+2
    /// (stock only calls the 3-way with i0/i1 clamped to g-2, so a 2x2
    /// table could never reach the odd arms).</summary>
    private static MstudioSeqDesc ThreeByThreeSeq()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "g",
            NumBlends = 9,
            GroupSize0 = 3,
            GroupSize1 = 3,
            AnimTable = [0, 1, 2, 3, 4, 5, 6, 7, 8],
            Weights = [1f],
        });
        StudioHdr hdr = Build(b);
        return hdr.SeqDesc(0);
    }
    [Fact]
    public void Calc3WayBlendIndices_EvenCell_BothSidesOfDiagonal()
    {
        MstudioSeqDesc seq = ThreeByThreeSeq();
        int[] anims = new int[3];
        float[] w = new float[3];

        // even, s0 > s1 (arm B): corners (0,0),(1,0),(1,1);
        // w0 = 1 - s0, w1 = s0 - s1.
        BoneSetupBlend.Calc3WayBlendIndices(0, 0, 0.7f, 0.3f, seq, anims, w);
        Assert.Equal(seq.Anim(0, 0), anims[0]);
        Assert.Equal(seq.Anim(1, 0), anims[1]);
        Assert.Equal(seq.Anim(1, 1), anims[2]);
        Assert.Equal(0.3f, w[0], 5);
        Assert.Equal(0.4f, w[1], 5);
        Assert.Equal(0.3f, w[2], 5);

        // even, s0 <= s1 (arm C): corners (1,1),(0,1),(0,0);
        // w0 = s0, w1 = s1 - s0.
        BoneSetupBlend.Calc3WayBlendIndices(0, 0, 0.3f, 0.7f, seq, anims, w);
        Assert.Equal(seq.Anim(1, 1), anims[0]);
        Assert.Equal(seq.Anim(0, 1), anims[1]);
        Assert.Equal(seq.Anim(0, 0), anims[2]);
        Assert.Equal(0.3f, w[0], 5);
        Assert.Equal(0.4f, w[1], 5);
        Assert.Equal(0.3f, w[2], 5);
    }

    [Fact]
    public void Calc3WayBlendIndices_OddCell_BothSidesOfAntiDiagonal()
    {
        MstudioSeqDesc seq = ThreeByThreeSeq();
        int[] anims = new int[3];
        float[] w = new float[3];

        // odd, s0 + s1 > 1 (arm D): corners (1,0),(1,1),(0,1);
        // w0 = 1 - s1, w1 = s0 - 1 + s1.
        BoneSetupBlend.Calc3WayBlendIndices(1, 0, 0.8f, 0.5f, seq, anims, w);
        Assert.Equal(seq.Anim(2, 0), anims[0]);
        Assert.Equal(seq.Anim(2, 1), anims[1]);
        Assert.Equal(seq.Anim(1, 1), anims[2]);
        Assert.Equal(0.5f, w[0], 5);
        Assert.Equal(0.3f, w[1], 5);
        Assert.Equal(0.2f, w[2], 5);

        // odd, s0 + s1 <= 1 (arm A): corners (0,1),(0,0),(1,0);
        // w0 = s1, w1 = 1 - s0 - s1.
        BoneSetupBlend.Calc3WayBlendIndices(0, 1, 0.2f, 0.3f, seq, anims, w);
        Assert.Equal(seq.Anim(0, 2), anims[0]);
        Assert.Equal(seq.Anim(0, 1), anims[1]);
        Assert.Equal(seq.Anim(1, 1), anims[2]);
        Assert.Equal(0.3f, w[0], 5);
        Assert.Equal(0.5f, w[1], 5);
        Assert.Equal(0.2f, w[2], 5);
    }

    [Fact]
    public void Calc3WayBlendIndices_ClampsNearDiagonalToZero()
    {
        MstudioSeqDesc seq = ThreeByThreeSeq();
        int[] anims = new int[3];
        float[] w = new float[3];

        // even, s0 barely over s1: w1 = 0.0005 < 0.001 -> clamped to 0,
        // w2 closes the sum.
        BoneSetupBlend.Calc3WayBlendIndices(0, 0, 0.5005f, 0.5f, seq, anims, w);
        Assert.Equal(0.4995f, w[0], 5);
        Assert.Equal(0f, w[1], 5);
        Assert.Equal(0.5005f, w[2], 5);
    }

    // ---- Studio_BuildMatrices (stock 4499-4577) ------------------------------

    /// <summary>b0 root / b1 child at (10,0,0) / b2 sibling at (0,5,0);
    /// flags 0x1 so the mask reaches all three.</summary>
    private static StudioHdr MatricesHdr()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(10f, 0f, 0f), Quat = Quaternion.Identity, Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b2", Parent = 0, Pos = new Vector(0f, 5f, 0f), Quat = Quaternion.Identity, Flags = 0x1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0] });
        return Build(b);
    }

    [Fact]
    public void StudioBuildMatrices_AllBones_ConcatsParentChain()
    {
        StudioHdr hdr = MatricesHdr();
        Vector[] pos = [new Vector(1f, 0f, 0f), new Vector(10f, 0f, 0f), new Vector(0f, 5f, 0f)];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];
        BoneMatrix3x4[] world = new BoneMatrix3x4[3];

        BoneAngle angles = new(0f, 25f, 0f);
        Vector origin = new(100f, 0f, 0f);
        BoneSetupBlend.StudioBuildMatrices(hdr, angles, origin, pos, q, -1, 1f, world, AllBones);

        BoneMatrix3x4 modelToWorld = BoneSetupMath.AngleMatrix(angles, origin);
        BoneMatrix3x4 w0 = BoneSetupMath.ConcatTransforms(modelToWorld, BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(1f, 0f, 0f)));
        AssertMtx(w0, world[0]);
        BoneMatrix3x4 w1 = BoneSetupMath.ConcatTransforms(w0, BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(10f, 0f, 0f)));
        AssertMtx(w1, world[1]);
        // reversed chain order (b2,b1,b0) still lays parents before children:
        BoneMatrix3x4 w2 = BoneSetupMath.ConcatTransforms(w0, BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(0f, 5f, 0f)));
        AssertMtx(w2, world[2]);
    }

    [Fact]
    public void StudioBuildMatrices_SingleBone_LeavesSiblingsUnwritten()
    {
        StudioHdr hdr = MatricesHdr();
        Vector[] pos = [Vector.Zero, new Vector(10f, 0f, 0f), new Vector(0f, 5f, 0f)];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];
        BoneMatrix3x4[] world = new BoneMatrix3x4[3];
        BoneMatrix3x4 sentinel = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(-1f, -1f, -1f));
        world[2] = sentinel;

        BoneSetupBlend.StudioBuildMatrices(hdr, default, Vector.Zero, pos, q, 1, 1f, world, AllBones);

        // chain = b1,b0: ancestors computed, sibling untouched.
        AssertMtx(sentinel, world[2], 0);
        // pos[0] is zero, so the child lands at its local offset:
        AssertVec(new Vector(10f, 0f, 0f), world[1].Position, 4);
    }

    [Fact]
    public void StudioBuildMatrices_NonUnitScale_RescalesOffsetAndBasis()
    {
        StudioHdr hdr = MatricesHdr();
        Vector[] pos = [Vector.Zero, new Vector(10f, 0f, 0f), Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];
        BoneMatrix3x4[] world = new BoneMatrix3x4[3];

        BoneAngle angles = new(0f, 90f, 0f);
        Vector origin = new(10f, 0f, 0f);
        BoneSetupBlend.StudioBuildMatrices(hdr, angles, origin, pos, q, -1, 2f, world, AllBones);

        // Mirror: m = AngleMatrix(angles, origin); the offset arm re-anchors
        // the translation on origin scaled about it, and scales each basis
        // row by flScale (the W components of the rows are untouched).
        BoneMatrix3x4 m = BoneSetupMath.AngleMatrix(angles, origin);
        Vector off = m.Position;
        off = (off - origin) * 2f + origin;
        m.Position = off;
        BoneMatrix3x4 scaled = ScaleRows(m, 2f);
        BoneMatrix3x4 w0 = BoneSetupMath.ConcatTransforms(scaled, BoneSetupMath.QuaternionMatrix(Quaternion.Identity, Vector.Zero));
        AssertMtx(w0, world[0]);
        // the basis really grew: row lengths double vs scale 1
        BoneMatrix3x4[] world1 = new BoneMatrix3x4[3];
        BoneSetupBlend.StudioBuildMatrices(hdr, angles, origin, pos, q, -1, 1f, world1, AllBones);
        Vector4 u = world1[0].Row0, v = world[0].Row0;
        Assert.Equal(
            (u.X * u.X + u.Y * u.Y + u.Z * u.Z) * 4f,
            (v.X * v.X + v.Y * v.Y + v.Z * v.Z),
            3);
    }

    private static BoneMatrix3x4 ScaleRows(BoneMatrix3x4 m, float s)
    {
        BoneMatrix3x4 r = m;
        Vector4 r0 = r.Row0, r1 = r.Row1, r2 = r.Row2;
        r.Row0 = new Vector4(r0.X * s, r0.Y * s, r0.Z * s, r0.W);
        r.Row1 = new Vector4(r1.X * s, r1.Y * s, r1.Z * s, r1.W);
        r.Row2 = new Vector4(r2.X * s, r2.Y * s, r2.Z * s, r2.W);
        return r;
    }

    [Fact]
    public void StudioBuildMatrices_SkipsBonesOutsideMask()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0x1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(10f, 0f, 0f), Flags = 0x2 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero, new Vector(10f, 0f, 0f)];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        BoneMatrix3x4[] world = new BoneMatrix3x4[2];
        BoneMatrix3x4 stale = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(-7f, -7f, -7f));
        world[1] = stale;

        BoneSetupBlend.StudioBuildMatrices(hdr, default, Vector.Zero, pos, q, -1, 1f, world, 0x1);

        AssertMtx(stale, world[1], 0); // masked out: keeps the stale matrix
        AssertVec(Vector.Zero, world[0].Position, 4);
    }

    /// <summary>Every weight ramp in the extract and pose clusters funnels
    /// through this one cubic; no ramp fact pins its shape, so it is pinned
    /// here directly.</summary>
    [Fact]
    public void SimpleSpline_IsTheCubicEase()
    {
        Assert.Equal(0.0f, BoneSetupMath.SimpleSpline(0.0f));
        Assert.Equal(1.0f, BoneSetupMath.SimpleSpline(1.0f));
        Assert.Equal(0.5f, BoneSetupMath.SimpleSpline(0.5f));
        // 3t²-2t³ at 0.4 = 0.352 — an off-midpoint value catches the
        // t*(3-2t) and 3t²-2t² typos the endpoints cannot.
        Assert.Equal(0.352f, BoneSetupMath.SimpleSpline(0.4f), 6);
    }
}
