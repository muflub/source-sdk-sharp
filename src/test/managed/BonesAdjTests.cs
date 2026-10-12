using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the controller-adjustment cluster (<see cref="BoneSetupAdj"/>):
/// CalcBoneAdj's clamp/map/switch arms, the ragdoll derivative pair,
/// SetupSingleBoneMatrix's three initialization arms, and
/// CalcBoneToBoneTransform's invert-and-rebase. Expectations are hand-derived
/// from the stock formulas at the cited ranges (our bone_setup.cpp
/// 2539-2591, 2600-2640, 639-679, 1841-1850), never read off the port.
/// </summary>
public class BonesAdjTests
{
    private const int AllBones = unchecked((int)0xFFFFFFFF);
    private const float SqrtHalf = 0.70710677f; // f32(sqrt(0.5))

    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    /// <summary>Two bones; bone 1 is BONE_USED_BY_HITBOX so a controller on
    /// it passes the mask gate, bone 0 is unflagged.</summary>
    private static StudioHdrBuilder TwoBones()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef
        {
            Name = "b1", Parent = 0, Flags = StudioConstants.BONE_USED_BY_HITBOX,
        });
        return b;
    }

    // ---- CalcBoneAdj (our 2539-2591) ---------------------------------------

    private static StudioHdr OneController(int type, float start, float end)
    {
        StudioHdrBuilder b = TwoBones();
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef
        {
            Bone = 1, Type = type, Start = start, End = end, InputField = 0,
        });
        return Build(b);
    }

    private static (Vector[] Pos, Quaternion[] Q) Adj(
        StudioHdr hdr, float setting, int boneMask = AllBones, Vector? pos0 = null)
    {
        var pos = new[] { pos0 ?? Vector.Zero, pos0 ?? Vector.Zero };
        var q = new[] { Quaternion.Identity, Quaternion.Identity };
        BoneSetupAdj.CalcBoneAdj(hdr, pos, q, new[] { setting }, boneMask);
        return (pos, q);
    }

    /// <summary>STUDIO_XR at setting 0.25 with start 0 / end 90: value = 22.5
    /// degrees; q0 = AngleQuaternion(RadianEuler(0.3927, 0, 0)) =
    /// (sin 11.25°, 0, 0, cos 11.25°); QuaternionSM(1, q0, identity) leaves it
    /// (QuaternionScale at t≈1, QuaternionMult by the aligned identity, and
    /// normalize by radius≈1). The position is untouched by an R arm.</summary>
    [Fact]
    public void CalcBoneAdj_XR_BlendsRestToFullAngle()
    {
        StudioHdr hdr = OneController(StudioConstants.STUDIO_XR, 0f, 90f);
        (Vector[] pos, Quaternion[] q) = Adj(hdr, 0.25f);

        float h = 22.5f * (float)Math.PI / 180.0f / 2.0f; // half-angle of value
        Assert.Equal(MathF.Sin(h), q[1].X, 5);
        Assert.Equal(0f, q[1].Y, 5);
        Assert.Equal(0f, q[1].Z, 5);
        Assert.Equal(MathF.Cos(h), q[1].W, 5);
        Assert.Equal(Vector.Zero, pos[1]);
    }

    /// <summary>The two clamp ifs (not a Clamp call): −0.5 → 0 → value =
    /// start = −67.5; 1.5 → 1 → value = end = 22.5.</summary>
    [Fact]
    public void CalcBoneAdj_ClampsSettingToUnitInterval()
    {
        StudioHdr hdr = OneController(StudioConstants.STUDIO_XR, -67.5f, 22.5f);

        (_, Quaternion[] qLow) = Adj(hdr, -0.5f);
        float hLow = -67.5f * (float)Math.PI / 180.0f / 2.0f;
        Assert.Equal(MathF.Sin(hLow), qLow[1].X, 5);

        (_, Quaternion[] qHigh) = Adj(hdr, 1.5f);
        float hHigh = 22.5f * (float)Math.PI / 180.0f / 2.0f;
        Assert.Equal(MathF.Sin(hHigh), qHigh[1].X, 5);
    }

    /// <summary>The gate is <c>boneFlags(bone) &amp; boneMask</c>: the bone's
    /// HITBOX flag passes an all-ones mask, and BONE_USED_BY_VERTEX_MASK
    /// (0x3FC00 &amp; 0x100 = 0) blocks the controller entirely — array left
    /// verbatim.</summary>
    [Fact]
    public void CalcBoneAdj_MaskGateBlocksController()
    {
        StudioHdr hdr = OneController(StudioConstants.STUDIO_XR, 0f, 90f);

        (_, Quaternion[] qBlocked) = Adj(
            hdr, 0.25f, boneMask: StudioConstants.BONE_USED_BY_VERTEX_MASK);
        Assert.Equal(Quaternion.Identity, qBlocked[1]);
    }

    /// <summary>STUDIO_X adds the mapped value to x only, start 2 / end 6 at
    /// setting 0.5 → (1.0−0.5)·2 + 0.5·6 = 4 (stock's double literal on the
    /// first term; the sum is exact here).</summary>
    [Fact]
    public void CalcBoneAdj_TranslationArmAddsMappedValue()
    {
        StudioHdr hdr = OneController(StudioConstants.STUDIO_X, 2f, 6f);
        (Vector[] pos, _) = Adj(hdr, 0.5f, pos0: new Vector(100f, 100f, 100f));

        Assert.Equal(104f, pos[1].X);
        Assert.Equal(100f, pos[1].Y); // the Y arm belongs to STUDIO_Y only
        Assert.Equal(100f, pos[1].Z);
    }

    /// <summary>Type 0 matches no switch case and stock has no default arm:
    /// the bone is left exactly as it arrived.</summary>
    [Fact]
    public void CalcBoneAdj_TypeMatchesNoCase_LeavesBoneUntouched()
    {
        StudioHdr hdr = OneController(0, 0f, 1f);
        (Vector[] pos, Quaternion[] q) = Adj(hdr, 1f, pos0: new Vector(7f, 8f, 9f));

        Assert.Equal(new Vector(7f, 8f, 9f), pos[1]);
        Assert.Equal(Quaternion.Identity, q[1]);
    }

    // ---- CalcBoneDerivatives (our 2600-2620) -------------------------------

    /// <summary>prev = identity at origin; current = yaw 90 (Rz, the quaternion
    /// (0,0,√½,√½)) at (2,3,4), dt = 0.5 → scale 2: velocity = (4,6,8); the
    /// delta rotation is 90° about (0,0,1): angVel ≈ (0,0,180).</summary>
    [Fact]
    public void CalcBoneDerivatives_PositionDeltaAndSpin()
    {
        BoneMatrix3x4 prev = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, Vector.Zero);
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(
            new Quaternion(0f, 0f, SqrtHalf, SqrtHalf), new Vector(2f, 3f, 4f));

        BoneSetupAdj.CalcBoneDerivatives(out Vector velocity, out Vector angVel, prev, current, 0.5f);

        Assert.Equal(4f, velocity.X, 4);
        Assert.Equal(6f, velocity.Y, 4);
        Assert.Equal(8f, velocity.Z, 4);
        Assert.Equal(0f, angVel.X, 3);
        Assert.Equal(0f, angVel.Y, 3);
        Assert.Equal(180f, angVel.Z, 3);
    }

    /// <summary>dt = 0 keeps scale at 1 (stock's <c>dt &gt; 0</c> guard): the
    /// velocity is the raw translation delta.</summary>
    [Fact]
    public void CalcBoneDerivatives_ZeroDt_UsesScaleOne()
    {
        BoneMatrix3x4 prev = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, Vector.Zero);
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(1f, 0f, 0f));

        BoneSetupAdj.CalcBoneDerivatives(out Vector velocity, out Vector angVel, prev, current, 0f);

        Assert.Equal(new Vector(1f, 0f, 0f), velocity);
        Assert.Equal(0f, angVel.X);
        Assert.Equal(0f, angVel.Y);
        Assert.Equal(0f, angVel.Z);
    }

    /// <summary>Characterization (stock 2526: <c>scale = 1.0 / dt</c>, a
    /// double division rounded to float on the store): no dt in
    /// [0.001, 0.2] (8M samples) rounds differently from the float division.
    /// dt = 1/60f (0x3c888889) gives scale 59.999996f (0x426fffff), so a
    /// unit X step reads that velocity bit for bit.</summary>
    [Fact]
    public void CalcBoneDerivatives_ScaleIsDoubleReciprocal_Characterization()
    {
        BoneMatrix3x4 prev = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, Vector.Zero);
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new Vector(1f, 0f, 0f));

        BoneSetupAdj.CalcBoneDerivatives(
            out Vector velocity, out _, prev, current, BitConverter.Int32BitsToSingle(0x3c888889));

        Assert.Equal(0x426fffff, BitConverter.SingleToInt32Bits(velocity.X));
    }

    // ---- QuaternionAxisAngle (mathlib_base.cpp:1982) — the compiled
    // all-float RAD2DEG chain, characterization-pinned at the ulp
    // boundary (values computed independently, never read off the port) --

    /// <summary>q = (0,0,0,−0.999f): stock's compiled chain is
    /// <c>movss q.w → acosf → mulss 0x42652ee0 (57.2958f) → comiss 180f →
    /// subss 360f</c>. Independently computed: acos(−0.999f)·2 rounds once
    /// to 6.1937356f (0x40c63315); times the float RAD2DEG constant →
    /// 354.8748779296875f (0x43b16ffc) → folded −5.1251220703125f
    /// (0xc0a40100). The double product the port previously ran rounds
    /// ONCE at the end and gives −5.125091552734375f instead — 4.7e-6
    /// away, ~17 ulp: an exact compare separates them. Axis: (0,0,0)
    /// stays (0,0,0) through the len2==0 early-out.</summary>
    [Fact]
    public void QuaternionAxisAngle_QwMinus0999_UsesCompiledFloatChainNotDoubleProduct()
    {
        var (axis, angle) = BoneSetupMath.QuaternionAxisAngle(
            new Quaternion(0f, 0f, 0f, -0.999f));
        Assert.Equal(Vector.Zero, axis);
        Assert.Equal(-5.1251220703125f, angle); // bit-exact vs the double port's -5.125091552734375f
    }

    // ---- CalcBoneVelocityFromDerivative (our 2624-2640) --------------------

    /// <summary>The identity matrix answers a unit-real quaternion:
    /// QuaternionAxisAngle gives angle 0 with the zero axis (the
    /// VectorNormalize len2==0 early-out), and AngleMatrix(0,0,0) is identity,
    /// so both world vectors come out zero.</summary>
    [Fact]
    public void CalcBoneVelocityFromDerivative_Identity_AllZero()
    {
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(Quaternion.Identity, Vector.Zero);

        BoneSetupAdj.CalcBoneVelocityFromDerivative(
            new BoneAngle(0f, 0f, 0f), out Vector velocity, out Vector angVel, current);

        Assert.Equal(Vector.Zero, velocity);
        Assert.Equal(Vector.Zero, angVel);
    }

    /// <summary>The &gt;180 fold arm, aimed at exactly: q = (0,0,sin 100°,
    /// cos 100°) is a 200° rotation, so QuaternionAxisAngle computes 200 &gt;
    /// 180 → −160 with axis (0,0,1). <c>vecAngles = (0,0,0)</c> makes the
    /// world rotation identity, so angVel = axis·angle = (0,0,−160) verbatim
    /// and the zero position gives zero velocity.</summary>
    [Fact]
    public void CalcBoneVelocityFromDerivative_NegativeW_FoldsAngleBelow180()
    {
        float sin100 = MathF.Sin(100f * (float)Math.PI / 180.0f);
        float cos100 = MathF.Cos(100f * (float)Math.PI / 180.0f);
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(
            new Quaternion(0f, 0f, sin100, cos100), Vector.Zero);

        BoneSetupAdj.CalcBoneVelocityFromDerivative(
            new BoneAngle(0f, 0f, 0f), out Vector velocity, out Vector angVel, current);

        Assert.Equal(Vector.Zero, velocity);
        Assert.Equal(0f, angVel.X, 3);
        Assert.Equal(0f, angVel.Y, 3);
        Assert.Equal(-160f, angVel.Z, 3);
    }

    /// <summary>yaw 90 at (5,0,0) with <c>vecAngles = (0,90,0)</c>: the
    /// position is the local velocity, so it rotates x̂ → ŷ as (0,5,0); the
    /// local spin (0,0,90) is on the yaw axis and stays (0,0,90).</summary>
    [Fact]
    public void CalcBoneVelocityFromDerivative_Yaw90_RotatesLocalVectors()
    {
        BoneMatrix3x4 current = BoneSetupMath.QuaternionMatrix(
            new Quaternion(0f, 0f, SqrtHalf, SqrtHalf), new Vector(5f, 0f, 0f));

        BoneSetupAdj.CalcBoneVelocityFromDerivative(
            new BoneAngle(0f, 90f, 0f), out Vector velocity, out Vector angVel, current);

        Assert.Equal(0f, velocity.X, 4);
        Assert.Equal(5f, velocity.Y, 4);
        Assert.Equal(0f, velocity.Z, 4);
        Assert.Equal(0f, angVel.X, 3);
        Assert.Equal(0f, angVel.Y, 3);
        Assert.Equal(90f, angVel.Z, 3);
    }

    // ---- SetupSingleBoneMatrix (our 639-679) -------------------------------

    private static StudioHdrBuilder SingleAnimFixture(
        BoneMatrix3x4? unused,
        int animBone,
        int animFlags,
        Vector rawPosition,
        float[] weights,
        int animDefFlags = 0)
    {
        _ = unused;
        StudioHdrBuilder b = TwoBones();
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a",
            NumFrames = 2,
            Flags = animDefFlags,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = animBone, Flags = animFlags, RawPosition = rawPosition,
                },
            },
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s", NumBlends = 1, AnimTable = [0], Weights = weights,
        });
        return b;
    }

    /// <summary>Bone found, weight 1, RAWPOS anim: the matrix is
    /// QuaternionMatrix(rest quat, rawPos) — the raw position is a Vector48,
    /// and (3,4,5) is half-exact, so the comparison is bit-exact; no rot
    /// stream on a non-delta anim leaves the bone's own rest quat
    /// (identity).</summary>
    [Fact]
    public void SetupSingleBoneMatrix_FoundWithWeight_DecodesRawPos()
    {
        StudioHdrBuilder b = SingleAnimFixture(
            null, 1, StudioConstants.STUDIO_ANIM_RAWPOS, new Vector(3f, 4f, 5f), [1f, 1f]);
        StudioHdr hdr = Build(b);

        BoneMatrix3x4 m = BoneSetupAdj.SetupSingleBoneMatrix(hdr, 0, 0, 1);

        Assert.Equal(new Vector(3f, 4f, 5f), m.Position);
        // the rotation block is the identity's (the position lives in each
        // row's .W, which m.Position already covers above).
        Assert.Equal(new Vector3(1f, 0f, 0f), new Vector3(m.Row0.X, m.Row0.Y, m.Row0.Z));
        Assert.Equal(new Vector3(0f, 1f, 0f), new Vector3(m.Row1.X, m.Row1.Y, m.Row1.Z));
        Assert.Equal(new Vector3(0f, 0f, 1f), new Vector3(m.Row2.X, m.Row2.Y, m.Row2.Z));
    }

    /// <summary>Bone NOT in the chain (the anim only carries bone 0, bone 1
    /// asked) on a STUDIO_DELTA anim: rest pose — identity rotation at the
    /// origin.</summary>
    [Fact]
    public void SetupSingleBoneMatrix_MissingBone_DeltaFallsBackToRestPose()
    {
        StudioHdrBuilder b = SingleAnimFixture(
            null, 0, 0, Vector.Zero, [1f, 1f], animDefFlags: StudioConstants.STUDIO_DELTA);
        StudioHdr hdr = Build(b);

        BoneMatrix3x4 m = BoneSetupAdj.SetupSingleBoneMatrix(hdr, 0, 0, 1);

        Assert.Equal(Vector.Zero, m.Position);
        BoneMatrix3x4 id = BoneSetupMath.QuaternionMatrix(Quaternion.Identity);
        Assert.Equal(id.Row0, m.Row0);
        Assert.Equal(id.Row1, m.Row1);
        Assert.Equal(id.Row2, m.Row2);
    }

    /// <summary>Same missing bone on a NON-delta anim: the bone record's own
    /// rest transform — bone 1 authored at (1,2,3) with a yaw-90 quaternion,
    /// so the rotation block is Rz(90°): rows (0,−1,0), (1,0,0), (0,0,1).</summary>
    [Fact]
    public void SetupSingleBoneMatrix_MissingBone_NonDeltaUsesBoneRest()
    {
        StudioHdrBuilder b = SingleAnimFixture(
            null, 0, 0, Vector.Zero, [1f, 1f]);
        b.Bones[1].Pos = new Vector(1f, 2f, 3f);
        b.Bones[1].Quat = new Quaternion(0f, 0f, SqrtHalf, SqrtHalf);
        StudioHdr hdr = Build(b);

        BoneMatrix3x4 m = BoneSetupAdj.SetupSingleBoneMatrix(hdr, 0, 0, 1);

        Assert.Equal(new Vector(1f, 2f, 3f), m.Position);
        Assert.Equal(0f, m.Row0.X, 5);
        Assert.Equal(-1f, m.Row0.Y, 5);
        Assert.Equal(1f, m.Row1.X, 5);
        Assert.Equal(0f, m.Row1.Y, 5);
        Assert.Equal(1f, m.Row2.Z, 5);
    }

    /// <summary>Found in the chain but the sequence weight is 0: stock skips
    /// the anim (<c>weight &gt; 0</c> gate) and the non-delta anim falls back
    /// to the bone's rest position.</summary>
    [Fact]
    public void SetupSingleBoneMatrix_ZeroWeight_FallsBackToBoneRest()
    {
        StudioHdrBuilder b = SingleAnimFixture(
            null, 1, StudioConstants.STUDIO_ANIM_RAWPOS, new Vector(3f, 4f, 5f), [1f, 0f]);
        b.Bones[1].Pos = new Vector(9f, 8f, 7f);
        StudioHdr hdr = Build(b);

        BoneMatrix3x4 m = BoneSetupAdj.SetupSingleBoneMatrix(hdr, 0, 0, 1);

        Assert.Equal(new Vector(9f, 8f, 7f), m.Position);
    }

    // ---- Studio_CalcBoneToBoneTransform (our 1841-1850) --------------------

    /// <summary>out = poseToBone[output] · invert(poseToBone[input]), hand-
    /// derived: input poseToBone = yaw-90 quaternion (rows (0,−1,0),(1,0,0),
    /// (0,0,1)) at (1,2,3); its inverse transposes the rows to (0,1,0),
    /// (−1,0,0),(0,0,1) with t = −Rᵀ·(1,2,3) = (−2,1,−3). Output poseToBone is
    /// identity at (4,5,6), so the rotation block stays the inverse's and
    /// t = (4,5,6) + (−2,1,−3) = (2,6,3).</summary>
    [Fact]
    public void CalcBoneToBoneTransform_InvertsInputReBasesToOutput()
    {
        StudioHdrBuilder b = TwoBones();
        b.Bones[0].PoseToBone = BoneSetupMath.QuaternionMatrix(
            new Quaternion(0f, 0f, SqrtHalf, SqrtHalf), new Vector(1f, 2f, 3f));
        b.Bones[1].PoseToBone = BoneSetupMath.QuaternionMatrix(
            Quaternion.Identity, new Vector(4f, 5f, 6f));
        StudioHdr hdr = Build(b);

        BoneMatrix3x4 m = BoneSetupAdj.CalcBoneToBoneTransform(hdr, 0, 1);

        Assert.Equal(2f, m.Position.X, 5);
        Assert.Equal(6f, m.Position.Y, 5);
        Assert.Equal(3f, m.Position.Z, 5);
        Assert.Equal(0f, m.Row0.X, 5);
        Assert.Equal(1f, m.Row0.Y, 5);
        Assert.Equal(-1f, m.Row1.X, 5);
        Assert.Equal(0f, m.Row1.Y, 5);
        Assert.Equal(1f, m.Row2.Z, 5);
    }

    // ---- IBoneSetup::CalcBoneAdj thunk (our 2396-2400) ----------------------

    /// <summary>The CBoneSetup thunk forwards the setup's hdr and mask: a
    /// STUDIO_ZR controller at setting 1 (value = end = 90°) rotates bone 1
    /// to (0,0,sin 45°,cos 45°); the setup's own mask (not a parameter) is
    /// what gates, so a vertex-mask setup blocks the same controller.</summary>
    [Fact]
    public void CBoneSetup_CalcBoneAdj_ThunksWithOwnHdrAndMask()
    {
        StudioHdrBuilder b = TwoBones();
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef
        {
            Bone = 1, Type = StudioConstants.STUDIO_ZR, Start = 0f, End = 90f, InputField = 0,
        });
        StudioHdr hdr = Build(b);

        var pos = new[] { Vector.Zero, Vector.Zero };
        var q = new[] { Quaternion.Identity, Quaternion.Identity };
        new CBoneSetup(hdr, AllBones, []).CalcBoneAdj(pos, q, new float[] { 1f });

        Assert.Equal(SqrtHalf, q[1].Z, 5);
        Assert.Equal(SqrtHalf, q[1].W, 5);

        var qBlocked = new[] { Quaternion.Identity, Quaternion.Identity };
        new CBoneSetup(hdr, StudioConstants.BONE_USED_BY_VERTEX_MASK, [])
            .CalcBoneAdj(pos, qBlocked, new float[] { 1f });
        Assert.Equal(Quaternion.Identity, qBlocked[1]);
    }
}
