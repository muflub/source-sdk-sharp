using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the procedural-bone cluster (<c>bone_setup.cpp</c>
/// 4586-4985 relocated stock): <c>DoAxisInterpBone</c>,
/// <c>DoQuatInterpBone</c>, <c>DoAimAtBone</c>, <c>CalcProceduralBone</c>,
/// <c>FindController</c>. Expectations are hand-derived from the stock
/// bodies; where a blend collapses through several public
/// <see cref="BoneSetupMath"/> mirrors the fact derives the result from
/// those mirrors over hand-chosen constants (the port's private helpers
/// stay outside). The fixtures pin WORLD matrices directly through
/// <see cref="CBoneAccessor"/> — the accessor array is caller-owned, and
/// <c>CalcProceduralBone</c> does not recompute parents (NP-02's
/// caller-order fact below).
/// </summary>
public class BonesProceduralTests
{
    // studio.h:409; studio.h:103-107 for the proctypes. The port's consts are
    // internal to the shared assembly; the tests carry the literals.
    private const int BoneAlwaysProcedural = 0x04;
    private const int ProcAxisInterp = 1;
    private const int ProcQuatInterp = 2;
    private const int ProcAimAtBone = 3;
    private const int ProcAimAtAttach = 4;
    private const int ProcJiggleBone = 5;

    private const float H = 0.7071067811865476f; // sin45 = cos45 = sqrt(1/2)

    private static readonly BoneMatrix3x4 Rz90 = new()
    {
        Row0 = new Vector4(0f, 1f, 0f, 0f),
        Row1 = new Vector4(-1f, 0f, 0f, 0f),
        Row2 = new Vector4(0f, 0f, 1f, 0f),
    };

    private static readonly BoneMatrix3x4 Rx180 = new()
    {
        Row0 = new Vector4(1f, 0f, 0f, 0f),
        Row1 = new Vector4(0f, -1f, 0f, 0f),
        Row2 = new Vector4(0f, 0f, -1f, 0f),
    };

    private static Quaternion Rz90Quat => new(0f, 0f, H, H);

    private static StudioHdr Make(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.BuildWithProcedural(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    /// <summary>b0 root, b1 (parent 0), b2 (parent 0, always-procedural).</summary>
    private static StudioHdrBuilder ThreeBones()
    {
        var b = new StudioHdrBuilder { WithLinearBones = true };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "proc", Parent = 0, Flags = BoneAlwaysProcedural });
        return b;
    }

    private static CBoneAccessor Accessor(params BoneMatrix3x4[] worlds) => new(worlds);

    private static void AssertMatrix(BoneMatrix3x4 expected, BoneMatrix3x4 actual, float tol = 1e-6f)
    {
        AssertRow(expected.Row0, actual.Row0, tol);
        AssertRow(expected.Row1, actual.Row1, tol);
        AssertRow(expected.Row2, actual.Row2, tol);
    }

    /// <summary>Componentwise: xunit v2 tolerates floats only on scalars.</summary>
    private static void AssertRow(Vector4 expected, Vector4 actual, float tol = 1e-6f)
    {
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
        Assert.Equal(expected.W, actual.W, tol);
    }

    private static void AssertVec(Vector expected, Vector actual, float tol = 1e-6f)
    {
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
    }

    // ------------------------------------------------------------------
    // FindController (stock 4975-4985)
    // ------------------------------------------------------------------

    [Fact]
    public void FindControllerReturnsFirstWithMatchingInputField()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 0, InputField = 2 });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 1, InputField = 5 });
        StudioHdr hdr = Make(b);

        MstudioBoneController? c = BoneSetupProcedural.FindController(hdr, 5);

        Assert.NotNull(c);
        Assert.Equal(5, c!.InputField);
        Assert.Equal(1, c.Bone); // the second controller, matched on inputfield
    }

    [Fact]
    public void FindControllerReturnsNullWhenNoControllerMatches()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 0, InputField = 2 });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 1, InputField = 5 });
        StudioHdr hdr = Make(b);

        Assert.Null(BoneSetupProcedural.FindController(hdr, 3));
    }

    [Fact]
    public void FindControllerReturnsFirstOfDuplicateInputFields()
    {
        // Stock scans 0..numbonecontrollers() and returns on the FIRST hit,
        // so duplicates resolve to the lowest index.
        StudioHdrBuilder b = ThreeBones();
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 0, InputField = 7 });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 1, InputField = 7 });
        StudioHdr hdr = Make(b);

        MstudioBoneController? c = BoneSetupProcedural.FindController(hdr, 7);

        Assert.NotNull(c);
        Assert.Equal(0, c!.Bone);
    }

    // ------------------------------------------------------------------
    // DoAxisInterpBone (stock 4586-4692)
    // ------------------------------------------------------------------

    /// <summary>
    /// Control with a parent: the shipped arm reads the control's WORLD
    /// column and VectorIRotates it by the control parent's world. The
    /// parent's world is Rz(90), so the rotated control vector (0,1,0)
    /// selects (quat[2], pos[2]) while the raw column (1,0,0) would select
    /// the (quat[0],pos[0])-driven x-dominant blend — the arms differ.
    /// Hand-derived: t = 1/(0+1+0) = 1; slerp(q2,q1,0) -> normalize(q2)=Rx90;
    /// slerp(Rx90,q3,0) -> Rx90; p = pos[2] = (0,2,0); concat over the
    /// parent's Rz(90).
    /// </summary>
    [Fact]
    public void AxisInterpControlWithParentRotatesColumnIntoParentSpace()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 1,
                Axis = 0,
                Pos = [new(1, 0, 0), new(7, 7, 7), new(0, 2, 0), new(7, 7, 7), new(0, 0, 3), new(7, 7, 7)],
                Quat =
                [
                    new(0f, 0f, H, H),      // quat[0] Rz90
                    new(0f, H, 0f, -H),     // junk
                    new(H, 0f, 0f, H),      // quat[2] Rx90
                    new(0f, -H, 0f, H),     // junk
                    new(0f, 0f, 0f, 1f),    // quat[4] identity
                    new(0f, H, 0f, H),      // junk
                ],
            },
        });
        StudioHdr hdr = Make(b);
        var acc = Accessor(Rz90, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAxisInterpBone(hdr, 2, acc);

        AssertMatrix(
            new BoneMatrix3x4
            {
                Row0 = new Vector4(0f, 0f, -1f, 2f),
                Row1 = new Vector4(-1f, 0f, 0f, 0f),
                Row2 = new Vector4(0f, 1f, 0f, 0f),
            },
            acc.GetBone(2));
    }

    /// <summary>
    /// Root control: stock's else arm takes the RAW world column, no inverse
    /// rotation. worlds[0] is Rx180 with axis 1, so the raw column is
    /// (0,-1,0): negated a2 = 1 > 0 drives the true blend over
    /// (quat[0], quat[3], quat[4]) — and without the a2 = -a2 negation
    /// a1 + a2 would be -1, falling to the else arm on quat[4]/pos[4].
    /// Hand-derived: t = 1, the first slerp takes its q at t = 0 collapsing
    /// onto quat[3] = Rz90, p = pos[3] = (0,2,0), concat over Rx180 parent.
    /// </summary>
    [Fact]
    public void AxisInterpRootControlTakesRawColumnAndNegatesAxis()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0, // root: parent == -1
                Axis = 1,
                Pos = [new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(0, 2, 0), new(0, 0, 3), new(9, 9, 9)],
                Quat =
                [
                    new(0f, H, 0f, H),      // quat[0] (unused at t = 0)
                    new(0f, -H, 0f, H),     // junk
                    new(H, 0f, 0f, -H),     // junk
                    new(0f, 0f, H, H),      // quat[3] Rz90 — the one the blend lands on
                    new(0f, 0f, 0f, 1f),    // quat[4] identity — the else arm would use this
                    new(0f, H, 0f, H),      // junk
                ],
            },
        });
        StudioHdr hdr = Make(b);
        var acc = Accessor(Rx180, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAxisInterpBone(hdr, 2, acc);

        AssertMatrix(
            new BoneMatrix3x4
            {
                Row0 = new Vector4(0f, -1f, 0f, 0f),
                Row1 = new Vector4(-1f, 0f, 0f, -2f),
                Row2 = new Vector4(0f, 0f, -1f, 0f),
            },
            acc.GetBone(2));
    }

    /// <summary>
    /// The a1 = a2 = 0 fallback: control 0 is Rx180 and axis 2 gives the raw
    /// column (0,0,-1), so a1 = a2 = 0 and a3 negates to -1: stock's else arm
    /// runs with q3 = quat[5], p3 = pos[5] and p = *p3.
    /// QuaternionSlerp(*q3, *q3, 0) — the "??? no quat copy" line — is the
    /// align plus both scale arms of mathlib's SlerpNoAlign: p == q can never
    /// flip (a = 0 <= b) and every arm collapses to sclp = 1, sclq = 0 at
    /// t = 0, so it degenerates to a RAW copy. The non-unit quat[5] reaches
    /// QuaternionMatrix un-normalised (stock's QuaternionNormalize is an
    /// Assert, compiled out in release): rows (−1,−2,0)/(2,−1,0)/(0,0,1),
    /// then negated rows 1-2 under the parent's Rx180. A port that
    /// normalised would write clean Rz(90) rows instead.
    /// </summary>
    [Fact]
    public void AxisInterpElseArmSlerpDegeneratesToRawQuat5Copy()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0,
                Axis = 2, // Rx180 column 2 = (0,0,-1): a3 negated, a1 = a2 = 0
                Pos = [new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(0, 0, 5)],
                Quat =
                [
                    new(0f, H, 0f, H),
                    new(0f, -H, 0f, H),
                    new(H, 0f, 0f, -H),
                    new(0f, 0f, H, H),
                    new(H, 0f, 0f, H),
                    new(0f, 0f, 1f, 1f), // quat[5]: non-unit — the copy must keep the junk
                ],
            },
        });
        StudioHdr hdr = Make(b);
        var acc = Accessor(Rx180, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAxisInterpBone(hdr, 2, acc);
        // QuaternionMatrix((0,0,1,1), (0,0,5)): Row0 (−1,−2,0,0),
        // Row1 (2,−1,0,0), Row2 (0,0,1,5); Rx180 negates rows 1-2.
        AssertMatrix(
            new BoneMatrix3x4
            {
                Row0 = new Vector4(-1f, -2f, 0f, 0f),
                Row1 = new Vector4(-2f, 1f, 0f, 0f),
                Row2 = new Vector4(0f, 0f, -1f, -5f),
            },
            acc.GetBone(2),
            1e-5f);
    }

    /// <summary>
    /// The true three-way blend: control column (0.6,0.8,0) (root control's
    /// raw column), t = 1/1.4, slerp(q2,q1,0.6/1.4) at a non-trivial t, then
    /// slerp with q3 at t = 0, p the weighted (pos[0],pos[2],pos[4]) sum,
    /// all concatenated over the parent's world. Expected recomputed from
    /// the public mirrors over the same constants.
    /// </summary>
    [Fact]
    public void AxisInterpMidpointBlendWeighsByReciprocalSum()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0,
                Axis = 0,
                Pos = [new(1, 0, 0), new(7, 7, 7), new(0, 2, 0), new(7, 7, 7), new(0, 0, 3), new(7, 7, 7)],
                Quat =
                [
                    new(0f, 0f, H, H),      // quat[0] Rz90
                    new(0f, H, 0f, -H),
                    new(H, 0f, 0f, H),      // quat[2] Rx90
                    new(0f, -H, 0f, H),
                    new(0f, 0f, 0f, 1f),    // quat[4] identity
                    new(0f, H, 0f, H),
                ],
            },
        });
        StudioHdr hdr = Make(b);
        BoneMatrix3x4 world = new()
        {
            // column 0 = (0.6,0.8,0); also the procedural bone's parent matrix.
            Row0 = new Vector4(0.6f, 1f, 0f, 0f),
            Row1 = new Vector4(0.8f, 0f, 1f, 0f),
            Row2 = new Vector4(0f, 0f, 1f, 0f),
        };
        var acc = Accessor(world, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAxisInterpBone(hdr, 2, acc);

        float a1 = 0.6f, a2 = 0.8f, a3 = 0.0f;
        float t = 1.0f / (a1 + a2 + a3);
        Quaternion tmp2 = BoneSetupMath.QuaternionSlerp(new Quaternion(H, 0f, 0f, H), Rz90Quat, a1 / (a1 + a2));
        Quaternion v = BoneSetupMath.QuaternionSlerp(tmp2, Quaternion.Identity, a3 * t);
        Vector p = BoneSetupMath.VectorMA(
            BoneSetupMath.VectorMA(BoneSetupMath.VectorScale(new Vector(1, 0, 0), a1 * t), a2 * t, new Vector(0, 2, 0)),
            a3 * t,
            new Vector(0, 0, 3));
        AssertMatrix(BoneSetupMath.ConcatTransforms(world, BoneSetupMath.QuaternionMatrix(v, p)), acc.GetBone(2));
    }

    // ------------------------------------------------------------------
    // DoQuatInterpBone (stock 4700-4770)
    // ------------------------------------------------------------------

    /// <summary>Identity control: control = bone 1 (parent 0, parent world I),
    /// both worlds identity -> controlmatrix I, src = identity quaternion.</summary>
    private static StudioHdrBuilder QuatFixture(StudioHdrBuilder.QuatInterpDef q, int controlBone = 1)
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcQuatInterp,
            QuatInterp = WithControl(q, controlBone),
        });
        return b;
    }

    /// <summary>QuatInterpDef is a class, not a record: assign, don't `with`.</summary>
    private static StudioHdrBuilder.QuatInterpDef WithControl(StudioHdrBuilder.QuatInterpDef q, int controlBone)
    {
        q.Control = controlBone;
        return q;
    }

    private static StudioHdrBuilder.QuatTriggerDef MatchingTrigger(Quaternion quat, Vector pos, float invTol) =>
        new() { Trigger = Quaternion.Identity, Quat = quat, Pos = pos, InvTolerance = invTol };

    /// <summary>
    /// numtriggers = 0 leaves scale at 0, so the <= 0.001 arm runs
    /// AngleMatrix(QuaternionAngles(trigger[0].quat), trigger[0].pos) even
    /// though no trigger was ever scored — the record is still read.
    /// </summary>
    [Fact]
    public void QuatInterpZeroTriggersFallsToAngleMatrixOfFirstTrigger()
    {
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            NumTriggers = 0,
            Triggers = [MatchingTrigger(Rz90Quat, new(5, 0, 0), 1f)],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = Accessor(BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        BoneMatrix3x4 expected = BoneSetupMath.ConcatTransforms(
            BoneMatrix3x4.Identity,
            BoneSetupMath.AngleMatrix(BoneSetupMath.QuaternionAngles(Rz90Quat), new(5, 0, 0)));
        AssertMatrix(expected, acc.GetBone(2), 1e-5f); // the NB-06 degree round-trip
    }

    /// <summary>
    /// One trigger whose <c>trigger</c> quaternion equals src: dot = 1,
    /// acos(1) = 0, weight = 1 whatever inv_tolerance; scale = 1 so the
    /// accumulate runs at s = 1 and reproduces trigger.quat/pos exactly.
    /// QuaternionAlign against the zero accumulator does not flip (a = b = 1).
    /// </summary>
    [Fact]
    public void QuatInterpIdentityTriggerAccumulatesExactly()
    {
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            Triggers = [MatchingTrigger(Rz90Quat, new(2, 0, 0), 1f)],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = Accessor(BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        AssertMatrix(BoneSetupMath.QuaternionMatrix(Rz90Quat, new(2, 0, 0)), acc.GetBone(2));
    }

    /// <summary>
    /// The weight formula 1 - 2*acos(dot)*inv_tolerance (double, stored to
    /// float) with inv_tolerance deciding: trigger A matches (weight 1);
    /// trigger B sits 45 degrees from src with inv_tolerance 1/pi, giving
    /// weight 1 - (pi/2)*(1/pi)*... = 0.5, so the accumulator is the 2/3 +
    /// 1/3 weighted sum. Dropping the inv_tolerance term would make weight B
    /// 1 - pi/2 (negative, clamped out) — a different matrix.
    /// </summary>
    [Fact]
    public void QuatInterpWeightScalesWithInvTolerance()
    {
        Quaternion ortho = new(0f, H, 0f, H); // 45 deg from identity on y
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            Triggers =
            [
                MatchingTrigger(Rz90Quat, new(2, 0, 0), 1f),
                new() { Trigger = ortho, Quat = new(H, 0f, 0f, H), Pos = new(0, 6, 0), InvTolerance = 1f / MathF.PI },
            ],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = Accessor(BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        float wA = (float)(1.0 - (2.0 * Math.Acos(1.0) * 1.0));
        float dotB = MathF.Abs(BoneSetupMath.QuaternionDotProduct(ortho, Quaternion.Identity));
        float wB = (float)(1.0 - (2.0 * Math.Acos(dotB) * (double)(1f / MathF.PI)));
        wB = Math.Max(0f, wB);
        float scale = 1.0f / (wA + wB);
        Quaternion q = BoneSetupMath.QuaternionAlign(Rz90Quat, default);
        q = new Quaternion(q.X + (wA * scale) * Rz90Quat.X, q.Y + (wA * scale) * Rz90Quat.Y,
            q.Z + (wA * scale) * Rz90Quat.Z, q.W + (wA * scale) * Rz90Quat.W);
        q = BoneSetupMath.QuaternionAlign(new Quaternion(H, 0f, 0f, H), q);
        q = new Quaternion(q.X + (wB * scale) * H, q.Y, q.Z, q.W + (wB * scale) * H); // B.Quat = (H,0,0,H)
        Vector pos = new((wA * scale) * 2f + (wB * scale) * 0f, (wB * scale) * 6f, 0f);
        AssertMatrix(BoneSetupMath.QuaternionMatrix(q, pos), acc.GetBone(2));
    }

    /// <summary>
    /// A strongly negative raw weight (dot 0, inv_tolerance 10 -> 1 - 10*pi)
    /// clamps to 0: excluded from the scale sum and from the accumulator, so
    /// the answer is trigger A alone at s = 1. Without the clamp, scale would
    /// go negative and even trigger A's contribution would flip sign.
    /// </summary>
    [Fact]
    public void QuatInterpNegativeWeightClampsToZero()
    {
        Quaternion ortho = new(0f, H, 0f, -H); // dot with identity = H...
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            Triggers =
            [
                MatchingTrigger(Rz90Quat, new(2, 0, 0), 1f),
                new() { Trigger = new Quaternion(0f, 1f, 0f, 0f), Quat = new(-H, 0f, 0f, H), Pos = new(0, 6, 0), InvTolerance = 10f },
            ],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = Accessor(BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        // Trigger B: dot = |QDot((0,1,0,0), I)| = 0 -> weight = 1 - 10*pi < 0
        // -> clamped 0. Result is trigger A at s = 1.
        AssertMatrix(BoneSetupMath.QuaternionMatrix(Rz90Quat, new(2, 0, 0)), acc.GetBone(2));
        _ = ortho;
    }

    /// <summary>
    /// Antipodal trigger: the second trigger's target quaternion is -q of the
    /// first's. QuaternionAlign(pTrigger.quat, quat) sees |p - q|^2 >
    /// |p + q|^2 and FLIPS the running accumulator's sign, so the two halves
    /// add instead of cancelling. Drop the align and the quat sums to zero:
    /// an identity-looking matrix, not Rz(90).
    /// </summary>
    [Fact]
    public void QuatInterpAntipodalTriggerAlignFlipAddsInsteadOfCancels()
    {
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            Triggers =
            [
                MatchingTrigger(Rz90Quat, new(2, 0, 0), 1f),
                MatchingTrigger(new Quaternion(0f, 0f, -H, -H), new(4, 0, 0), 1f),
            ],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = Accessor(BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        // weights 1,1 -> scale 1/2. After A: running = 0.5·Rz90. For B the
        // align compares |(-q) - running|^2 = 2.25 > |(-q) + running|^2 =
        // 0.25 and FLIPS the running half: -0.5·Rz90 + 0.5·(-Rz90) = -Rz90.
        // The written quat is (0,0,-H,-H), antipodal to Rz90 — the same
        // rotation, so the rows are mirror Rz(90) with pos (3,0,0).
        AssertMatrix(
            BoneSetupMath.QuaternionMatrix(new Quaternion(0f, 0f, -H, -H), new(3, 0, 0)),
            acc.GetBone(2),
            1e-5f);
    }

    // ------------------------------------------------------------------
    // DoAimAtBone (stock 4785-4925)
    // ------------------------------------------------------------------

    private static StudioHdrBuilder AimFixture(StudioHdrBuilder.AimAtDef aim, Quaternion? boneQuat = null)
    {
        StudioHdrBuilder b = ThreeBones();
        b.Bones[2].Quat = boneQuat ?? Quaternion.Identity;
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAimAtBone,
            AimAt = aim,
        });
        return b;
    }

    /// <summary>
    /// The aim vector equals the bone's user aim axis: cross is zero, the
    /// angle is acosf(1) = 0, the rotation is identity, and the up gate
    /// (1 - |dot(up, aim)| > FLT_EPSILON) is FALSE for parallel up, so the
    /// bone keeps its basepos — QuaternionMatrix(identity, aimWorldPosition),
    /// NOT a matrix that moves the bone to the target.
    /// </summary>
    [Fact]
    public void AimAtAlignedIdentityKeepsBaseposNotTarget()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 1,
            AimVector = new(0, 0, 1),
            UpVector = new(0, 0, 1),
            BasePos = Vector.Zero,
        };
        StudioHdr hdr = Make(AimFixture(aim));
        BoneMatrix3x4 target = BoneMatrix3x4.Identity;
        target.Position = new(0, 0, 5);
        var acc = Accessor(BoneMatrix3x4.Identity, target, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAimAtBone(hdr, 2, acc, null);

        AssertRow(new Vector4(1f, 0f, 0f, 0f), acc.GetBone(2).Row0);
        AssertRow(new Vector4(0f, 1f, 0f, 0f), acc.GetBone(2).Row1);
        AssertRow(new Vector4(0f, 0f, 1f, 0f), acc.GetBone(2).Row2);
    }

    /// <summary>
    /// Real aim 90 degrees off-axis: basepos (0,0,0) at the origin, target at
    /// (0,10,0), user aim (1,0,0). aimVector (0,1,0); axis = cross((1,0,0),
    /// (0,1,0)) = (0,0,1) [operand order pinned]; angle = acosf(0) = pi/2 ->
    /// rotation Rz(90). The up gate fires (dot = 0), pUp == pParentUp (both
    /// (0,0,1): Rz keeps z) so the inner test takes the else arm, upRotation
    /// is identity, and QuaternionMult leaves aimRotation alone. The written
    /// matrix is the mirror's QuaternionMatrix(Rz90Quat, (0,0,0)) — the
    /// operand order is what makes these Rz(+90), not Rz(−90).
    /// </summary>
    [Fact]
    public void AimAtRealNinetyDegreeAimProducesHandDerivedRz90Rows()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 1,
            AimVector = new(1, 0, 0),
            UpVector = new(0, 0, 1),
            BasePos = Vector.Zero,
        };
        StudioHdr hdr = Make(AimFixture(aim));
        BoneMatrix3x4 target = BoneMatrix3x4.Identity;
        target.Position = new(0, 10, 0);
        var acc = Accessor(BoneMatrix3x4.Identity, target, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAimAtBone(hdr, 2, acc, null);

        // The mirror's Rz(90) rows — (0,-1,0)/(1,0,0)/(0,0,1): swapping the
        // CrossProduct operands flips these to (0,1,0)/(-1,0,0)/(0,0,1).
        AssertMatrix(BoneSetupMath.QuaternionMatrix(Rz90Quat, Vector.Zero), acc.GetBone(2), 1e-5f);
    }

    /// <summary>
    /// The up-arm roll: user aim (1,0,0), target (0,10,0) -> aimVector
    /// (0,1,0), aimRotation Rz(90). The bone's own local matrix is Ry(90)
    /// (BoneDef.Quat), so pParentUp = normalise(rotate((0,0,1), Ry90) minus
    /// its component along (0,1,0)) = (-1,0,0) while pUp = (0,0,1); the inner
    /// 1 - |dot| gate FIRES: upAngle = (double)acos(0) = pi/2,
    /// upAxis = cross(pUp, pParentUp) = (0,-1,0) [operand order pinned],
    /// boneRotation = upRotation * aimRotation = Ry(-90) * Rz(90).
    /// </summary>
    [Fact]
    public void AimAtUpArmRollsByCrossOfProjectedUps()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 1,
            AimVector = new(1, 0, 0),
            UpVector = new(0, 0, 1),
            BasePos = Vector.Zero,
        };
        StudioHdr hdr = Make(AimFixture(aim, boneQuat: new Quaternion(0f, H, 0f, H)));
        BoneMatrix3x4 target = BoneMatrix3x4.Identity;
        target.Position = new(0, 10, 0);
        var acc = Accessor(BoneMatrix3x4.Identity, target, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAimAtBone(hdr, 2, acc, null);

        // Re-derive pUp/pParentUp through the public mirrors over the same
        // constants, then roll with the hand-written cross product.
        Quaternion aimRot = new(0f, 0f, H, H);
        Vector pUp = BoneSetupMath.VectorRotate(new(0f, 0f, 1f), BoneSetupMath.QuaternionMatrix(aimRot));
        pUp = BoneSetupMath.VectorNormalize(pUp);
        BoneMatrix3x4 boneLocalToWorld = BoneSetupMath.ConcatTransforms(
            BoneMatrix3x4.Identity,
            BoneSetupMath.AngleMatrix(BoneSetupMath.QuaternionAngles(new Quaternion(0f, H, 0f, H)), Vector.Zero));
        Vector tmpPPU = BoneSetupMath.VectorRotate(new(0f, 0f, 1f), boneLocalToWorld);
        Vector ppUp = BoneSetupMath.VectorNormalize(
            new Vector(
                tmpPPU.X - 0f * (0f * tmpPPU.X + 1f * tmpPPU.Y + 0f * tmpPPU.Z),
                tmpPPU.Y - 1f * (0f * tmpPPU.X + 1f * tmpPPU.Y + 0f * tmpPPU.Z),
                tmpPPU.Z - 0f * (0f * tmpPPU.X + 1f * tmpPPU.Y + 0f * tmpPPU.Z)));
        Assert.True(1f - MathF.Abs(BoneSetupMath.Dot(pUp, ppUp)) > BoneSetupMath.FltEpsilon);
        float upAngle = (float)Math.Acos(BoneSetupMath.Dot(pUp, ppUp));
        Vector upAxis = BoneSetupMath.VectorNormalize(new Vector(
            pUp.Y * ppUp.Z - pUp.Z * ppUp.Y,
            pUp.Z * ppUp.X - pUp.X * ppUp.Z,
            pUp.X * ppUp.Y - pUp.Y * ppUp.X));
        float r = BoneSetupMath.DegToRad * (BoneSetupMath.RadToDeg * upAngle) * 0.5f;
        Quaternion upRot = new(upAxis.X * MathF.Sin(r), upAxis.Y * MathF.Sin(r), upAxis.Z * MathF.Sin(r), MathF.Cos(r));
        Quaternion expectedRot = BoneSetupMath.QuaternionMult(upRot, aimRot);
        AssertMatrix(BoneSetupMath.QuaternionMatrix(expectedRot, Vector.Zero), acc.GetBone(2), 1e-4f);
    }

    /// <summary>
    /// aim_constraint off = the stock convar's commented-out body: copy the
    /// parent's transform and overwrite the translation with basepos pushed
    /// through that same transform (stock's VectorTransform + MatrixSetColumn
    /// pair). Parent rows (0,1,0)/(−1,0,0)/(0,0,1) at (5,6,7), basepos (1,2,3):
    /// mirror VectorTransform gives (2+5, −1+6, 3+7) = (7,5,10); the aim bone is never read.
    /// DIVERGENCE (parameter-only), ledger NP-06.
    /// </summary>
    [Fact]
    public void AimAtConstraintDisabledCopiesParentAndOffsetsBasepos()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 1,
            AimVector = new(1, 0, 0),
            UpVector = new(0, 0, 1),
            BasePos = new(1, 2, 3),
        };
        StudioHdrBuilder b = AimFixture(aim);
        b.Procedurals[0].ProcType = ProcAimAtAttach; // never consulted by the dead arm
        StudioHdr hdr = Make(b);
        BoneMatrix3x4 parent = Rz90;
        parent.Position = new(5, 6, 7);
        BoneMatrix3x4 unusedTarget = BoneMatrix3x4.Identity;
        unusedTarget.Position = new(100, 0, 0);
        var acc = Accessor(parent, unusedTarget, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAimAtBone(hdr, 2, acc, hdr, aimConstraintEnabled: false);

        BoneMatrix3x4 e = acc.GetBone(2);
        AssertRow(Rz90.Row0 with { W = 7f }, e.Row0, 1e-6f);
        AssertRow(Rz90.Row1 with { W = 5f }, e.Row1, 1e-6f);
        AssertRow(Rz90.Row2 with { W = 10f }, e.Row2, 1e-6f);
    }

    /// <summary>
    /// AIMATATTACH: the target is ConcatTransforms(world[attachment.LocalBone],
    /// attachment.Local), not the aim bone's world. Attachment 0 hangs on bone
    /// 1 at local (0,5,0); bone 1 itself sits at the ORIGIN, so reading the
    /// aim cell as a bone index would yield the zero aim vector and an
    /// identity rotation — the Rz(90) rows prove the attachment arm ran.
    /// Up is parallel to the aim axis, so the plain aim-only else arm applies.
    /// </summary>
    [Fact]
    public void AimAtAttachTargetsConcatOfAttachmentBoneAndLocal()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 0, // the ATTACHMENT index under the hdr selector
            AimVector = new(1, 0, 0),
            UpVector = new(1, 0, 0),
            BasePos = Vector.Zero,
        };
        StudioHdrBuilder b = ThreeBones();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef
        {
            Name = "attach0",
            LocalBone = 1,
            Local = new BoneMatrix3x4
            {
                Row0 = new Vector4(1f, 0f, 0f, 0f),
                Row1 = new Vector4(0f, 1f, 0f, 5f),
                Row2 = new Vector4(0f, 0f, 1f, 0f),
            },
        });
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAimAtAttach,
            AimAt = aim,
        });
        StudioHdr hdr = Make(b);
        BoneMatrix3x4 boneAtOrigin = BoneMatrix3x4.Identity; // bone 1: aim-at-bone would read this
        var acc = Accessor(BoneMatrix3x4.Identity, boneAtOrigin, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoAimAtBone(hdr, 2, acc, hdr);

        AssertMatrix(BoneSetupMath.QuaternionMatrix(Rz90Quat, Vector.Zero), acc.GetBone(2), 1e-5f);
    }

    // ------------------------------------------------------------------
    // CalcProceduralBone (stock 4932-4965)
    // ------------------------------------------------------------------

    private static CBoneAccessor SentinelAccessor()
    {
        BoneMatrix3x4[] w = [BoneMatrix3x4.Identity, BoneMatrix3x4.Identity, BoneMatrix3x4.Identity];
        w[2].Row0 = new Vector4(9f, 9f, 9f, 9f);
        w[2].Row1 = new Vector4(9f, 9f, 9f, 9f);
        w[2].Row2 = new Vector4(9f, 9f, 9f, 9f);
        return Accessor(w);
    }

    /// <summary>Dispatch arm 1: run the axis interp, report true.</summary>
    [Fact]
    public void CalcProceduralDispatchesAxisInterpAndReportsTrue()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0,
                Axis = 0, // identity column (1,0,0): true arm, collapses onto quat[0]/pos[0]
                Pos = [new(1, 0, 0), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7)],
                Quat = [Rz90Quat, new(0f, H, 0f, -H), new(H, 0f, 0f, H), new(0f, -H, 0f, H), new(0f, H, 0f, H), new(0f, H, 0f, H)],
            },
        });
        StudioHdr hdr = Make(b);
        var acc = SentinelAccessor();

        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        AssertMatrix(BoneSetupMath.QuaternionMatrix(Rz90Quat, new(1, 0, 0)), acc.GetBone(2)); // parent world I
    }

    /// <summary>Dispatch arm 2: run the quat interp (zero triggers -> the
    /// AngleMatrix arm writes), report true.</summary>
    [Fact]
    public void CalcProceduralDispatchesQuatInterpAndReportsTrue()
    {
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            NumTriggers = 0,
            Triggers = [MatchingTrigger(Rz90Quat, new(5, 0, 0), 1f)],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        var acc = SentinelAccessor();

        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        BoneMatrix3x4 expected = BoneSetupMath.AngleMatrix(BoneSetupMath.QuaternionAngles(Rz90Quat), new(5, 0, 0));
        AssertMatrix(expected, acc.GetBone(2), 1e-5f);
    }

    /// <summary>
    /// Dispatch arm 3 passes the NULL selector: the target must be the aim
    /// BONE's world even though an attachment table exists (arm 4's shape).
    /// The attachment would sit at (0,5,0); the aim bone at (0,10,0) — the
    /// plain-bone read gives the Rz(90) rows from aimVector (0,1,0).
    /// </summary>
    [Fact]
    public void CalcProceduralAimatBonePassesNullSelectorReadsAimAsBone()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 1,
            AimVector = new(1, 0, 0),
            UpVector = new(1, 0, 0),
            BasePos = Vector.Zero,
        };
        StudioHdrBuilder b = AimFixture(aim);
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef
        {
            Name = "trap",
            LocalBone = 1,
            Local = new BoneMatrix3x4
            {
                Row0 = new Vector4(1f, 0f, 0f, 0f),
                Row1 = new Vector4(0f, 1f, 0f, 5f),
                Row2 = new Vector4(0f, 0f, 1f, 0f),
            },
        });
        StudioHdr hdr = Make(b);
        BoneMatrix3x4 target = BoneMatrix3x4.Identity;
        target.Position = new(0, 10, 0);
        var acc = Accessor(BoneMatrix3x4.Identity, target, BoneMatrix3x4.Identity);

        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        // Bone target (0,10,0) -> aimVector (0,1,0) -> Rz(90) rows.
        AssertRow(new Vector4(0f, -1f, 0f, 0f), acc.GetBone(2).Row0, 1e-5f);
        AssertRow(new Vector4(1f, 0f, 0f, 0f), acc.GetBone(2).Row1, 1e-5f);
    }

    /// <summary>Dispatch arm 4 passes the hdr: attachment arm, true.</summary>
    [Fact]
    public void CalcProceduralAimatAttachPassesHdrSelectorReadsAttachment()
    {
        var aim = new StudioHdrBuilder.AimAtDef
        {
            Parent = 0,
            Aim = 0,
            AimVector = new(1, 0, 0),
            UpVector = new(1, 0, 0),
            BasePos = Vector.Zero,
        };
        StudioHdrBuilder b = ThreeBones();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef
        {
            Name = "attach0",
            LocalBone = 1,
            Local = new BoneMatrix3x4
            {
                Row0 = new Vector4(1f, 0f, 0f, 0f),
                Row1 = new Vector4(0f, 1f, 0f, 5f),
                Row2 = new Vector4(0f, 0f, 1f, 0f),
            },
        });
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAimAtAttach,
            AimAt = aim,
        });
        StudioHdr hdr = Make(b);
        var acc = SentinelAccessor();

        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        AssertRow(new Vector4(0f, -1f, 0f, 0f), acc.GetBone(2).Row0, 1e-5f); // Rz(90) rows (mirror convention)
    }

    /// <summary>proctype 0 is not a procedure: false, nothing written.</summary>
    [Fact]
    public void CalcProceduralRejectsProctypeZeroWithoutTouchingBone()
    {
        StudioHdrBuilder b = ThreeBones();
        StudioHdr hdr = Make(b);
        var acc = SentinelAccessor();

        Assert.False(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        AssertRow(new Vector4(9f, 9f, 9f, 9f), acc.GetBone(2).Row0);
    }

    /// <summary>
    /// JIGGLEBONE (5) has no Do-function in the stock switch — false, and the
    /// bone keeps its sentinel (the DoJiggleBone port belongs to its row).
    /// </summary>
    [Fact]
    public void CalcProceduralRejectsJiggleProctype()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcJiggleBone,
        // no payload: the dispatch's default arm never reads the procedure
        });
        StudioHdr hdr = Make(b);
        var acc = SentinelAccessor();

        Assert.False(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        AssertRow(new Vector4(9f, 9f, 9f, 9f), acc.GetBone(2).Row0);
    }

    /// <summary>
    /// The flag gate: BONE_ALWAYS_PROCEDURAL absent means no procedure runs
    /// even with a valid axis-interp payload wired — false, sentinel intact.
    /// </summary>
    [Fact]
    public void CalcProceduralRequiresAlwaysProceduralFlag()
    {
        StudioHdrBuilder b = ThreeBones();
        b.Bones[2].Flags = 0; // no BONE_ALWAYS_PROCEDURAL
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0,
                Axis = 0,
                Pos = [new(1, 0, 0), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7)],
                Quat = [Rz90Quat, new(0f, H, 0f, -H), new(H, 0f, 0f, H), new(0f, -H, 0f, H), new(0f, H, 0f, H), new(0f, H, 0f, H)],
            },
        });
        StudioHdr hdr = Make(b);
        var acc = SentinelAccessor();

        Assert.False(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc));
        AssertRow(new Vector4(9f, 9f, 9f, 9f), acc.GetBone(2).Row0);
    }

    /// <summary>
    /// The caller-contract fact (ledger NP-02): CalcProceduralBone has no
    /// parent-first recursion — a child whose procedure reads its parent's
    /// result only sees the fresh parent matrix if the CALLER computed the
    /// parent first. Chain: bone 0 is an axis-interp bone (parent -1 -> the
    /// concat lands on world[-1]... no: parent is bone 3, a static root);
    /// bone 1 (parent 0) is an axis-interp child. Computing 1 then 0 leaves
    /// the child reading the STALE parent; 0 then 1 leaves it reading the
    /// fresh one — reversing the order changes the child's result.
    /// </summary>
    [Fact]
    public void CalcProceduralHasNoRecursionSoCallerMustOrderParentsFirst()
    {
        StudioHdrBuilder b = new() { WithLinearBones = true };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "anchor", Parent = -1 }); // 3? no: index 0
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "mid", Parent = 0 });     // bone 0 below? keep simple:
        // rebuild explicitly with four bones:
        b.Bones.Clear();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1 });            // 0: anchor for worlds
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "parent", Parent = 0, Flags = BoneAlwaysProcedural }); // 1
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "child", Parent = 1, Flags = BoneAlwaysProcedural });  // 2
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "anchor2", Parent = -1 });          // 3: parent-of-parent anchor
        // bone 1: axis interp, root control 0 (raw column of world[0]) -> writes concat over world[3].
        // bone 2: axis interp, root control 3, column (1,0,0) -> writes concat over world[1] (the parent).
        var quatSlot0 = new Quaternion(0f, 0f, H, H);
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 1,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 0,
                Axis = 0,
                Pos = [new(1, 0, 0), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7)],
                Quat = [quatSlot0, new(0f, H, 0f, -H), new(H, 0f, 0f, H), new(0f, -H, 0f, H), new(0f, H, 0f, H), new(0f, H, 0f, H)],
            },
        });
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 2,
            ProcType = ProcAxisInterp,
            AxisInterp = new StudioHdrBuilder.AxisInterpDef
            {
                Control = 3,
                Axis = 0,
                Pos = [new(0, 1, 0), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7), new(7, 7, 7)],
                Quat = [new(H, 0f, 0f, H), new(0f, H, 0f, -H), new(0f, 0f, H, -H), new(0f, -H, 0f, H), new(0f, H, 0f, H), new(0f, H, 0f, H)],
            },
        });
        StudioHdr hdr = Make(b);

        // worlds: 0 and 3 identity; bone 1 starts stale (sentinel), bone 2 whatever.
        var acc = Accessor(
            BoneMatrix3x4.Identity,
            new BoneMatrix3x4 { Row0 = new(9, 9, 9, 9), Row1 = new(9, 9, 9, 9), Row2 = new(9, 9, 9, 9) },
            new BoneMatrix3x4 { Row0 = new(5, 5, 5, 5), Row1 = new(5, 5, 5, 5), Row2 = new(5, 5, 5, 5) },
            BoneMatrix3x4.Identity);
        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc)); // child FIRST: stale parent
        BoneMatrix3x4 childStale = acc.GetBone(2);
        BoneMatrix3x4 staleSentinel = new() { Row0 = new(9, 9, 9, 9), Row1 = new(9, 9, 9, 9), Row2 = new(9, 9, 9, 9) };
        AssertMatrix(BoneSetupMath.ConcatTransforms(staleSentinel, BoneSetupMath.QuaternionMatrix(new Quaternion(H, 0f, 0f, H), new(0, 1, 0))), childStale);

        var acc2 = Accessor(
            BoneMatrix3x4.Identity,
            new BoneMatrix3x4 { Row0 = new(9, 9, 9, 9), Row1 = new(9, 9, 9, 9), Row2 = new(9, 9, 9, 9) },
            new BoneMatrix3x4 { Row0 = new(5, 5, 5, 5), Row1 = new(5, 5, 5, 5), Row2 = new(5, 5, 5, 5) },
            BoneMatrix3x4.Identity);
        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 1, acc2)); // parent first
        BoneMatrix3x4 freshParent = acc2.GetBone(1);
        Assert.True(BoneSetupProcedural.CalcProceduralBone(hdr, 2, acc2)); // then child
        AssertMatrix(BoneSetupMath.ConcatTransforms(freshParent, BoneSetupMath.QuaternionMatrix(new Quaternion(H, 0f, 0f, H), new(0, 1, 0))), acc2.GetBone(2));
        Assert.NotEqual(childStale.Row0, acc2.GetBone(2).Row0);
    }

    // ---- the decoded per-bone tables (plan_physics_bones_perf A3) ---------------------

    [Fact]
    public void ASealedHeadersBoneFlagsCarryTheFoldsAttachmentOverlay()
    {
        // The include hangs an attachment off "b0", which the base model
        // uses only for hitboxes: the fold ORs BONE_USED_BY_ATTACHMENT in,
        // and the table a sealed header reads from must carry it.
        StudioHdrBuilder g = new();
        g.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = StudioConstants.BONE_USED_BY_ATTACHMENT });
        g.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "tip", LocalBone = 0, Local = BoneMatrix3x4.Identity });
        g.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", AnimTable = [0], Weights = [1f] });
        g.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a", NumFrames = 1, Fps = 30f });

        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = StudioConstants.BONE_USED_BY_HITBOX });
        b.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "g", Name = "grp" });
        b.AddIncludeImage("grp", g.Build());
        StudioHdr hdr = Make(b);

        Assert.True(hdr.IsVirtual);
        Assert.Equal(StudioConstants.BONE_USED_BY_HITBOX | StudioConstants.BONE_USED_BY_ATTACHMENT, hdr.BoneFlags(0));
        Assert.Equal(StudioConstants.BONE_USED_BY_HITBOX, hdr.BoneFlagsBase(0));
    }

    [Fact]
    public void TheDecodedQuatInterpReadsItsOwnControlBone()
    {
        // Two triggers too narrow to blend (inv_tolerance 10): the one that
        // matches the CONTROL bone's local rotation wins outright. Bone 1 is
        // turned 90 about z under an unturned root, so the z-90 trigger
        // (pos x 1) wins; read off the root instead, the identity one would
        // (pos y 9).
        var def = new StudioHdrBuilder.QuatInterpDef
        {
            Triggers =
            [
                new() { Trigger = Rz90Quat, Quat = Quaternion.Identity, Pos = new(1, 0, 0), InvTolerance = 10f },
                new() { Trigger = Quaternion.Identity, Quat = Quaternion.Identity, Pos = new(0, 9, 0), InvTolerance = 10f },
            ],
        };
        StudioHdr hdr = Make(QuatFixture(def));
        BoneMatrix3x4 turned = BoneSetupMath.QuaternionMatrix(Rz90Quat, Vector.Zero);
        var acc = Accessor(BoneMatrix3x4.Identity, turned, BoneMatrix3x4.Identity);

        BoneSetupProcedural.DoQuatInterpBone(hdr, 2, acc);

        BoneMatrix3x4 expected = BoneSetupMath.ConcatTransforms(
            acc.GetBone(hdr.BoneParent(2)), BoneSetupMath.QuaternionMatrix(Quaternion.Identity, new(1, 0, 0)));
        AssertMatrix(expected, acc.GetBone(2));
    }
}
