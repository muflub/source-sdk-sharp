//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// ik cluster: CIKSolver (oracle 2563-2668), Studio_SolveIK all three
// overloads (2690-2705, 2714-2747, 2778-2871), Studio_AlignIKMatrix
// (2752-2771), Studio_IKRuleWeight both overloads (2875-2912, 2915-2940),
// Studio_IKShouldLatch (2947-2963), Studio_IKTail (2970-2986),
// Studio_IKAnimationError (2994-3035), Studio_IKSequenceError (3043-3209),
// and the free BuildBoneChain-rebuild helper SolveBone (3501-3518).
//
// Divergences, all forced by the contract's no-hidden-state rule:
//   * stock's debugLine stubs (#if 1 no-op block, oracle 2674-2688) are not
//     ported -- they observe nothing;
//   * CIKSolver is a class with per-instance Mfwd/Minv in stock. The port
//     keeps the same float[3][3]-style representation (flat float[9]) so the
//     summation order of rot()/defineM() is line-for-line stock's;
//   * stock Studio_IKRuleWeight/ShouldLatch/IKTail take `int &iFrame,
//     float &fraq` out params; the port uses `out`;
//   * stock reads seqdesc/poseParameter/panim from the CBoneSetup owning the
//     context in some paths; anything like that arrives as a parameter here.

using System;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// <c>class CIKSolver</c> (oracle 2563-2668): Ken Perlin's closed-form
/// two-link IK. Given thigh->effector vector <c>P</c> and a preferred knee
/// direction <c>D</c>, build the orthonormal basis M whose x axis is unit(P)
/// and whose y axis leans toward D, rotate P onto +x, solve the planar
/// triangle for the knee, rotate back. Accept iff the planar foot distance
/// <c>d</c> satisfies <c>(r - B) &lt; d &lt; A</c>.
/// </summary>
/// <remarks>
/// The float[] representation is deliberate: <see cref="Rot"/> must sum in
/// stock's order, and Vector3 operator order is not stock's. Instance state
/// is the two 3x3 matrices, exactly as stock's <c>Mfwd</c>/<c>Minv</c>
/// members; one solver is one solve, so callers new-up per use.
/// </remarks>
public sealed class CikSolver
{
    // float Mfwd[3][3]; float Minv[3][3];  (row-major)
    private readonly float[] _mfwd = new float[9];
    private readonly float[] _minv = new float[9];

    /// <summary><c>CIKSolver::findD</c> (2587-2589).</summary>
    public static float FindD(float a, float b, float c) => (c + (a * a - b * b) / c) / 2;

    /// <summary><c>CIKSolver::findE</c> (2590).</summary>
    public static float FindE(float a, float d) => MathF.Sqrt(a * a - d * d);

    /// <summary><c>CIKSolver::solve</c> (2600-2611). <c>P</c>, <c>D</c> are
    /// length-3 (thigh->foot, preferred knee dir); <c>Q</c> receives the
    /// thigh-space knee. Returns the stock accept test.</summary>
    public bool Solve(float a, float b, float[] p, float[] d, float[] q)
    {
        float[] r = new float[3];
        DefineM(p, d);
        Rot(_minv, p, r);
        float rr = Length(r);
        float dd = FindD(a, b, rr);
        float e = FindE(a, dd);
        float[] s = new float[] { dd, e, 0f };
        Rot(_mfwd, s, q);
        return dd > (rr - b) && dd < a;
    }

    /// <summary><c>CIKSolver::defineM</c> (2619-2645): Minv's rows are
    /// X = unit(P), Y = unit(D - D.X X), Z = X x Y; Mfwd = Minv^T.</summary>
    public void DefineM(float[] p, float[] d)
    {
        // float *X = Minv[0], *Y = Minv[1], *Z = Minv[2];
        for (int i = 0; i < 3; i++)
        {
            _minv[i] = p[i];
        }
        Normalize(_minv.AsSpan(0, 3));

        float dDotX = Dot(d, _minv.AsSpan(0, 3));
        for (int i = 0; i < 3; i++)
        {
            _minv[3 + i] = d[i] - dDotX * _minv[i];
        }
        Normalize(_minv.AsSpan(3, 3));

        Cross(_minv.AsSpan(0, 3), _minv.AsSpan(3, 3), _minv.AsSpan(6, 3));

        // Mfwd = (Minv)T, since transposing inverts a rotation matrix.
        for (int i = 0; i < 3; i++)
        {
            _mfwd[i * 3 + 0] = _minv[0 * 3 + i];
            _mfwd[i * 3 + 1] = _minv[1 * 3 + i];
            _mfwd[i * 3 + 2] = _minv[2 * 3 + i];
        }
    }

    /// <summary><c>CIKSolver::dot</c> (2649).</summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    /// <summary><c>CIKSolver::length</c> (2651).</summary>
    public static float Length(ReadOnlySpan<float> v) => MathF.Sqrt(Dot(v, v));

    /// <summary><c>CIKSolver::normalize</c> (2653-2657): divides by the
    /// length; stock does NOT guard zero, so the zero vector divides by zero
    /// (NaN in, NaN out) exactly as the C++ does.</summary>
    public static void Normalize(Span<float> v)
    {
        float norm = Length(v);
        for (int i = 0; i < 3; i++)
        {
            v[i] /= norm;
        }
    }

    /// <summary><c>CIKSolver::cross</c> (2659-2663).</summary>
    public static void Cross(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> c)
    {
        c[0] = a[1] * b[2] - a[2] * b[1];
        c[1] = a[2] * b[0] - a[0] * b[2];
        c[2] = a[0] * b[1] - a[1] * b[0];
    }

    /// <summary><c>CIKSolver::rot</c> (2665-2668): <c>dst[i] = dot(M[i], src)</c>.</summary>
    public static void Rot(float[] m, ReadOnlySpan<float> src, Span<float> dst)
    {
        for (int i = 0; i < 3; i++)
        {
            dst[i] = Dot(m.AsSpan(3 * i, 3), src);
        }
    }
}

/// <summary>Free ik-cluster functions: the <c>Studio_*</c> IK helpers of
/// bone_setup.cpp plus the free <c>SolveBone</c>. All static, no state.</summary>
public static class BoneSetupIk
{
    /// <summary><c>#define KNEEMAX_EPSILON 0.9998</c> (oracle 2712).</summary>
    public const float KNEEMAX_EPSILON = 0.9998f;

    // -----------------------------------------------------------------
    // Studio_SolveIK (chain overload, stock 2690-2705)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_SolveIK( mstudioikchain_t *, Vector &amp;targetFoot,
    /// matrix3x4_t *pBoneToWorld )</c> (2690-2705). A recorded kneeDir on
    /// link 0 picks the 6-arg form (with the current knee position/direction
    /// from boneToWorld); otherwise the 4-arg knee-deriving form.</summary>
    public static bool StudioSolveIk(
        MstudioIkChain pikchain, Vector targetFoot, Span<BoneMatrix3x4> boneToWorld)
    {
        if (BoneSetupMath.VectorLengthSqr(pikchain.Link(0).KneeDir) > 0.0f)
        {
            // FIXME (stock): knee length should be as long as the legs
            Vector targetKneeDir = BoneSetupMath.VectorRotate(
                pikchain.Link(0).KneeDir, boneToWorld[pikchain.Link(0).Bone]);
            Vector targetKneePos = boneToWorld[pikchain.Link(1).Bone].Position;
            return StudioSolveIk(
                pikchain.Link(0).Bone, pikchain.Link(1).Bone, pikchain.Link(2).Bone,
                targetFoot, targetKneePos, targetKneeDir, boneToWorld);
        }
        else
        {
            return StudioSolveIk(
                pikchain.Link(0).Bone, pikchain.Link(1).Bone, pikchain.Link(2).Bone,
                targetFoot, boneToWorld);
        }
    }

    // -----------------------------------------------------------------
    // Studio_SolveIK (knee-deriving overload, stock 2714-2747)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_SolveIK( int iThigh, int iKnee, int iFoot, Vector
    /// &amp;targetFoot, matrix3x4_t *pBoneToWorld )</c> (2714-2747): no knee
    /// direction preference, so derive one from the current pose -- bail when
    /// the leg is too straight (beyond KNEEMAX_EPSILON) to tell which way the
    /// knee faces, else take the knee's offset from the thigh->foot chord.</summary>
    public static bool StudioSolveIk(
        int iThigh, int iKnee, int iFoot, Vector targetFoot, Span<BoneMatrix3x4> boneToWorld)
    {
        Vector worldThigh = boneToWorld[iThigh].Position;
        Vector worldKnee = boneToWorld[iKnee].Position;
        Vector worldFoot = boneToWorld[iFoot].Position;

        Vector ikFoot = targetFoot - worldThigh;
        Vector ikKnee = worldKnee - worldThigh;

        float l1 = (worldKnee - worldThigh).Length();
        float l2 = (worldFoot - worldKnee).Length();
        float l3 = (worldFoot - worldThigh).Length();

        // leg too straight to figure out knee?
        if (l3 > (l1 + l2) * KNEEMAX_EPSILON)
        {
            return false;
        }

        Vector ikHalf = (worldFoot - worldThigh) * (l1 / l3);

        // FIXME (stock): what to do when the knee is completely straight?
        Vector ikKneeDir = ikKnee - ikHalf;
        ikKneeDir = BoneSetupMath.VectorNormalize(ikKneeDir);

        return StudioSolveIk(iThigh, iKnee, iFoot, targetFoot, worldKnee, ikKneeDir, boneToWorld);
    }

    // -----------------------------------------------------------------
    // Studio_SolveIK (full 6-arg form, stock 2778-2871)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_SolveIK( int iThigh, int iKnee, int iFoot, Vector
    /// &amp;targetFoot, Vector &amp;targetKneePos, Vector &amp;targetKneeDir,
    /// matrix3x4_t *pBoneToWorld )</c> (2778-2871). Exaggerates the knee
    /// target along <c>targetKneeDir</c> by (footDistance - min(l1,l2) +
    /// l1+l2) * 100, clamps the foot vector into the reachable annulus
    /// (KNEEMAX_EPSILON outside, an ~80-degree bend inside), then runs
    /// <see cref="CikSolver.Solve"/> and, on accept, realigns the thigh/knee
    /// axes and slams the knee/foot positions.</summary>
    public static bool StudioSolveIk(
        int iThigh, int iKnee, int iFoot, Vector targetFoot,
        Vector targetKneePos, Vector targetKneeDir, Span<BoneMatrix3x4> boneToWorld)
    {
        Vector worldThigh = boneToWorld[iThigh].Position;
        Vector worldKnee = boneToWorld[iKnee].Position;
        Vector worldFoot = boneToWorld[iFoot].Position;

        Vector ikFoot, ikTargetKnee, ikKnee;

        ikFoot = targetFoot - worldThigh;
        ikKnee = targetKneePos - worldThigh;

        float l1 = (worldKnee - worldThigh).Length();
        float l2 = (worldFoot - worldKnee).Length();

        // exaggerate knee targets for legs that are nearly straight
        // FIXME (stock): should be configurable, and the ikKnee should be
        // from the original animation, not modified.
        float d = (targetFoot - worldThigh).Length() - MathF.Min(l1, l2);
        d = MathF.Max(l1 + l2, d);
        // FIXME (stock): too short knee directions cause trouble
        d = d * 100;

        ikTargetKnee = ikKnee + targetKneeDir * d;

        // too far away? (0.9998 is about 1 degree)
        if (ikFoot.Length() > (l1 + l2) * KNEEMAX_EPSILON)
        {
            ikFoot = BoneSetupMath.VectorNormalize(ikFoot);
            ikFoot = ikFoot * ((l1 + l2) * KNEEMAX_EPSILON);
        }

        // too close?
        // limit distance to about an 80 degree knee bend
        float minDist = MathF.Max(MathF.Abs(l1 - l2) * 1.15f, MathF.Min(l1, l2) * 0.15f);
        if (ikFoot.Length() < minDist)
        {
            // too close to get an accurate vector, just use original vector
            ikFoot = worldFoot - worldThigh;
            ikFoot = BoneSetupMath.VectorNormalize(ikFoot);
            ikFoot = ikFoot * minDist;
        }

        float[] pFoot = { ikFoot.X, ikFoot.Y, ikFoot.Z };
        float[] pTargetKnee = { ikTargetKnee.X, ikTargetKnee.Y, ikTargetKnee.Z };
        float[] pKnee = new float[3];

        CikSolver ik = new();
        if (ik.Solve(l1, l2, pFoot, pTargetKnee, pKnee))
        {
            ikKnee = new Vector(pKnee[0], pKnee[1], pKnee[2]);
            ikFoot = new Vector(pFoot[0], pFoot[1], pFoot[2]);

            ref BoneMatrix3x4 mWorldThigh = ref boneToWorld[iThigh];
            ref BoneMatrix3x4 mWorldKnee = ref boneToWorld[iKnee];
            ref BoneMatrix3x4 mWorldFoot = ref boneToWorld[iFoot];

            // build transformation matrix for thigh
            StudioAlignIkMatrix(ref mWorldThigh, ikKnee);
            StudioAlignIkMatrix(ref mWorldKnee, ikFoot - ikKnee);

            mWorldKnee.Position = ikKnee + worldThigh;
            mWorldFoot.Position = ikFoot + worldThigh;

            return true;
        }

        return false;
    }

    // -----------------------------------------------------------------
    // Studio_AlignIKMatrix (stock 2752-2771)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_AlignIKMatrix( matrix3x4_t &amp;mMat, const Vector
    /// &amp;vAlignTo )</c> (2752-2771): realign the basis so the X axis points
    /// along <c>vAlignTo</c>; Y = Z_old x X (normalised), Z = X x Y. Position
    /// untouched. FIXME (stock): no check for X being too near to Z.</summary>
    public static void StudioAlignIkMatrix(ref BoneMatrix3x4 mMat, Vector vAlignTo)
    {
        // Column 0 (X) becomes the vector.
        Vector tmp1 = BoneSetupMath.VectorNormalize(vAlignTo);
        mMat.Row0.X = tmp1.X; mMat.Row1.X = tmp1.Y; mMat.Row2.X = tmp1.Z;

        // Column 1 (Y) is the cross of the vector and column 2 (Z).
        Vector tmp3 = new(mMat.Row0.Z, mMat.Row1.Z, mMat.Row2.Z);
        Vector tmp2 = BoneSetupMath.VectorNormalize(BoneSetupMath.CrossProduct(tmp3, tmp1));
        mMat.Row0.Y = tmp2.X; mMat.Row1.Y = tmp2.Y; mMat.Row2.Y = tmp2.Z;

        // Column 2 (Z) is the cross of columns 0 (X) and 1 (Y).
        Vector tmp3b = BoneSetupMath.CrossProduct(tmp1, tmp2);
        mMat.Row0.Z = tmp3b.X; mMat.Row1.Z = tmp3b.Y; mMat.Row2.Z = tmp3b.Z;
    }

    // -----------------------------------------------------------------
    // Studio_IKRuleWeight, anim-rule overload (stock 2875-2912)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKRuleWeight( mstudioikrule_t &amp;, const
    /// mstudioanimdesc_t *, float flCycle, int &amp;iFrame, float &amp;fraq )</c>
    /// (2875-2912): the [0,1] influence ramp over the cycle with the frame
    /// window the error data must be sampled at. The C++ passes iFrame/fraq
    /// by reference through uninitialised and only reads them after every
    /// branch assigns; the port makes them <c>out</c>.</summary>
    public static float StudioIkRuleWeight(
        MstudioIkRule ikRule, MstudioAnimDesc panim, float flCycle,
        out int iFrame, out float fraq)
    {
        if (ikRule.End > 1.0f && flCycle < ikRule.Start)
        {
            flCycle = flCycle + 1.0f;
        }

        float value = 0.0f;
        fraq = (panim.NumFrames - 1) * (flCycle - ikRule.Start) + ikRule.IStart;
        iFrame = (int)fraq;
        fraq = fraq - iFrame;

        if (flCycle < ikRule.Start)
        {
            iFrame = ikRule.IStart;
            fraq = 0.0f;
            return 0.0f;
        }
        else if (flCycle < ikRule.Peak)
        {
            value = (flCycle - ikRule.Start) / (ikRule.Peak - ikRule.Start);
        }
        else if (flCycle < ikRule.Tail)
        {
            return 1.0f;
        }
        else if (flCycle < ikRule.End)
        {
            value = 1.0f - ((flCycle - ikRule.Tail) / (ikRule.End - ikRule.Tail));
        }
        else
        {
            fraq = (panim.NumFrames - 1) * (ikRule.End - ikRule.Start) + ikRule.IStart;
            iFrame = (int)fraq;
            fraq = fraq - iFrame;
        }

        return BoneSetupMath.SimpleSpline(value);
    }

    // -----------------------------------------------------------------
    // Studio_IKRuleWeight, context-rule overload (stock 2915-2940)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKRuleWeight( ikcontextikrule_t &amp;, float flCycle )</c>
    /// (2915-2940): the same ramp without the frame window -- note the tail
    /// branch has no final <c>else</c>, so cycles at/past end keep value 0
    /// and the function returns SimpleSpline(0) = 0.</summary>
    public static float StudioIkRuleWeight(IkContextIkRule ikRule, float flCycle)
    {
        if (ikRule.End > 1.0f && flCycle < ikRule.Start)
        {
            flCycle = flCycle + 1.0f;
        }

        float value = 0.0f;
        if (flCycle < ikRule.Start)
        {
            return 0.0f;
        }
        else if (flCycle < ikRule.Peak)
        {
            value = (flCycle - ikRule.Start) / (ikRule.Peak - ikRule.Start);
        }
        else if (flCycle < ikRule.Tail)
        {
            return 1.0f;
        }
        else if (flCycle < ikRule.End)
        {
            value = 1.0f - ((flCycle - ikRule.Tail) / (ikRule.End - ikRule.Tail));
        }

        return 3.0f * value * value - 2.0f * value * value * value;
    }

    // -----------------------------------------------------------------
    // Studio_IKShouldLatch (stock 2947-2963)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKShouldLatch( ikcontextikrule_t &amp;, float flCycle )</c>
    /// (2947-2963). Stock returns bool and the only caller multiplies it by
    /// flWeight; the port returns the 1f/0f directly so the arithmetic reads
    /// the same. True from peak up to (not including) end, with the same
    /// end &gt; 1 wrap.</summary>
    public static float StudioIkShouldLatch(IkContextIkRule ikRule, float flCycle)
    {
        if (ikRule.End > 1.0f && flCycle < ikRule.Start)
        {
            flCycle = flCycle + 1.0f;
        }

        if (flCycle < ikRule.Peak)
        {
            return 0.0f;
        }
        else if (flCycle < ikRule.End)
        {
            return 1.0f;
        }

        return 0.0f;
    }

    // -----------------------------------------------------------------
    // Studio_IKTail (stock 2970-2986)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKTail( ikcontextikrule_t &amp;, float flCycle )</c>
    /// (2970-2986): 0 up to and including tail, ramp to 1 by end, and 0 past
    /// end (stock's final <c>return 0.0</c>).</summary>
    public static float StudioIkTail(IkContextIkRule ikRule, float flCycle)
    {
        if (ikRule.End > 1.0f && flCycle < ikRule.Start)
        {
            flCycle = flCycle + 1.0f;
        }

        if (flCycle <= ikRule.Tail)
        {
            return 0.0f;
        }
        else if (flCycle < ikRule.End)
        {
            return (flCycle - ikRule.Tail) / (ikRule.End - ikRule.Tail);
        }

        return 0.0f;
    }

    // -----------------------------------------------------------------
    // Studio_IKAnimationError (stock 2994-3035)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKAnimationError( const CStudioHdr *, mstudioikrule_t
    /// *, const mstudioanimdesc_t *, float flCycle, Vector &amp;pos, Quaternion
    /// &amp;q, float &amp;flWeight )</c> (2994-3035). Samples the rule's baked
    /// error at the ramp's frame window: two-frame <c>mstudioikerror_t</c>
    /// blend, else the compressed stream via
    /// <see cref="BoneSetupExtract.CalcDecompressedAnimation"/>. No data
    /// zeroes the weight and fails (stock's <c>Assert(0)</c> is a no-op).</summary>
    public static bool StudioIkAnimationError(
        MstudioIkRule pRule, MstudioAnimDesc panim, float flCycle,
        out Vector pos, out Quaternion q, out float flWeight)
    {
        flWeight = StudioIkRuleWeight(pRule, panim, flCycle, out int iFrame, out float fraq);
        // Assert( fraq in [0,1) ), Assert( flWeight in [0,1] ) -- no-ops.
        flWeight = Math.Clamp(flWeight, 0.0f, 1.0f);

        if (pRule.Type != StudioConstants.IK_GROUND && flWeight < 0.0001f)
        {
            pos = default;
            q = default;
            return false;
        }

        MstudioIkError? pError = pRule.PError(iFrame);
        if (pError is not null)
        {
            // stock indexes pError[0] and pError[1] off the same record.
            if (fraq < 0.001f)
            {
                q = pError.Quaternion;
                pos = pError.Position;
            }
            else
            {
                MstudioIkError pError1 = pRule.PError(iFrame + 1)!;
                q = BoneSetupMath.QuaternionBlend(pError.Quaternion, pError1.Quaternion, fraq);
                pos = pError.Position * (1.0f - fraq) + pError1.Position * fraq;
            }
            return true;
        }

        MstudioCompressedIkError? pCompressed = pRule.PCompressedError();
        if (pCompressed is not null)
        {
            BoneSetupExtract.CalcDecompressedAnimation(
                pCompressed, iFrame - pRule.IStart, fraq, out pos, out q);
            return true;
        }

        // no data, disable IK rule
        flWeight = 0.0f;
        pos = default;
        q = default;
        return false;
    }

    // -----------------------------------------------------------------
    // Studio_IKSequenceError (stock 3043-3209)
    // -----------------------------------------------------------------

    /// <summary><c>Studio_IKSequenceError( const CStudioHdr *, mstudioseqdesc_t
    /// &amp;, int iSequence, float flCycle, int iRule, const float
    /// poseParameter[], mstudioanimdesc_t *panim[4], float weight[4],
    /// ikcontextikrule_t &amp;ikRule )</c> (3043-3209). Blends one IK rule
    /// across the four blend cells: the influence window (with the dt = +/-1
    /// seam fix and the post-sum wrap), then the per-type error accumulation
    /// and the latch/release bookkeeping; finally normalises the accumulated
    /// pose and remaps an IK_SELF bone from anim-local to sequence-local.
    /// Stock's <c>seqdesc</c> and <c>poseParameter</c> arguments are read
    /// nowhere in the body; kept for parity and marked unused.</summary>
    public static bool StudioIkSequenceError(
        StudioHdr pStudioHdr, MstudioSeqDesc unusedSeqdesc, int iSequence, float flCycle,
        int iRule, float[] unusedPoseParameter,
        MstudioAnimDesc[] panim, float[] weight,
        out IkContextIkRule ikRule)
    {
        ikRule = new IkContextIkRule();
        // stock: memset + start = peak = tail = end = 0 -- zero fields already.

        MstudioIkRule? prevRule = null;

        // find overall influence
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] != 0f)
            {
                if (iRule >= panim[i].NumIKRules || panim[i].NumIKRules != panim[0].NumIKRules)
                {
                    // Assert(0)
                    goto reject;
                }

                MstudioIkRule? pRule = panim[i].PIKRule(iRule);
                if (pRule is null)
                {
                    goto reject;
                }

                float dt = 0.0f;
                if (prevRule is not null)
                {
                    if (pRule.Start - prevRule.Start > 0.5f)
                    {
                        dt = -1.0f;
                    }
                    else if (pRule.Start - prevRule.Start < -0.5f)
                    {
                        dt = 1.0f;
                    }
                }
                else
                {
                    prevRule = pRule;
                }

                ikRule.Start += (pRule.Start + dt) * weight[i];
                ikRule.Peak += (pRule.Peak + dt) * weight[i];
                ikRule.Tail += (pRule.Tail + dt) * weight[i];
                ikRule.End += (pRule.End + dt) * weight[i];
            }
        }

        if (ikRule.Start > 1.0f)
        {
            ikRule.Start -= 1.0f;
            ikRule.Peak -= 1.0f;
            ikRule.Tail -= 1.0f;
            ikRule.End -= 1.0f;
        }
        else if (ikRule.Start < 0.0f)
        {
            ikRule.Start += 1.0f;
            ikRule.Peak += 1.0f;
            ikRule.Tail += 1.0f;
            ikRule.End += 1.0f;
        }

        ikRule.FlWeight = StudioIkRuleWeight(ikRule, flCycle);
        if (ikRule.FlWeight <= 0.001f)
        {
            // go ahead and allow IK_GROUND rules a virtual looping section
            MstudioIkRule? firstRule = panim[0].PIKRule(iRule);
            if (firstRule is null)
            {
                goto reject;
            }
            if ((panim[0].Flags & StudioConstants.STUDIO_LOOPING) != 0
                && firstRule.Type == StudioConstants.IK_GROUND
                && ikRule.End - ikRule.Start > 0.75f)
            {
                ikRule.FlWeight = 0.001f;
                flCycle = ikRule.End - 0.001f;
            }
            else
            {
                goto reject;
            }
        }

        // Assert( ikRule.flWeight > 0.0f )

        // stock 3125-3126: ikRule.pos.Init() and ikRule.q.Init() — both
        // zero: Quaternion::Init's defaults are all 0.0f (vector.h:1577), so
        // the accumulation starts from the ZERO quaternion, not identity.
        ikRule.Pos = default;
        ikRule.Q = default;

        // find target error
        float total = 0.0f;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] != 0f)
            {
                MstudioIkRule? pRule = panim[i].PIKRule(iRule);
                if (pRule is null)
                {
                    goto reject;
                }

                ikRule.Chain = pRule.Chain;  // FIXME (stock): this is anim local
                ikRule.Bone = pRule.Bone;    // FIXME (stock): this is anim local
                ikRule.Type = pRule.Type;
                ikRule.Slot = pRule.Slot;

                ikRule.Height += pRule.Height * weight[i];
                ikRule.Floor += pRule.Floor * weight[i];
                ikRule.Radius += pRule.Radius * weight[i];
                ikRule.Drop += pRule.Drop * weight[i];
                ikRule.Top += pRule.Top * weight[i];

                // keep track of tail condition
                ikRule.Release += StudioIkTail(ikRule, flCycle) * weight[i];

                // only check rules with error values
                switch (ikRule.Type)
                {
                    case StudioConstants.IK_SELF:
                    case StudioConstants.IK_WORLD:
                    case StudioConstants.IK_GROUND:
                    case StudioConstants.IK_ATTACHMENT:
                    {
                        bool bResult = StudioIkAnimationError(
                            pRule, panim[i], flCycle, out Vector pos1, out Quaternion q1, out float w);
                        _ = w; // stock's local w is discarded after the call

                        if (bResult)
                        {
                            ikRule.Pos = ikRule.Pos + pos1 * weight[i];
                            ikRule.Q = BoneSetupMath.QuaternionAccumulate(ikRule.Q, weight[i], q1);
                            total += weight[i];
                        }
                    }
                    break;
                    default:
                        total += weight[i];
                        break;
                }

                ikRule.Latched = StudioIkShouldLatch(ikRule, flCycle) * ikRule.FlWeight;

                if (ikRule.Type == StudioConstants.IK_ATTACHMENT)
                {
                    ikRule.SzLabel = pRule.AttachmentName;
                }
            }
        }

        if (total <= 0.0001f)
        {
            goto reject;
        }

        if (total < 0.999f)
        {
            ikRule.Pos = ikRule.Pos * (1.0f / total);
            ikRule.Q = BoneSetupMath.QuaternionScale(ikRule.Q, 1.0f / total);
        }

        if (ikRule.Type == StudioConstants.IK_SELF && ikRule.Bone != -1)
        {
            // FIXME (stock): this is anim local, not seq local!
            ikRule.Bone = RemapSeqBone(pStudioHdr, iSequence, ikRule.Bone);
            if (ikRule.Bone == -1)
            {
                goto reject;
            }
        }

        _ = BoneSetupMath.QuaternionNormalize(ref ikRule.Q);
        return true;

    reject:
        ikRule = default!;
        return false;
    }

    /// <summary>Stock's <c>CStudioHdr::RemapSeqBone( iSequence, iLocalBone )</c>:
    /// the fold group's <c>masterBone</c> lookup. Private stand-in until the
    /// hdr-read cluster lands it as a member; see needs-ik.md. Stock indexes
    /// the map unguarded; the port bounds-checks to -1 (malformed anim-local
    /// bone, which only UB'd in C++).</summary>
    private static int RemapSeqBone(StudioHdr pStudioHdr, int iSequence, int iLocalBone)
    {
        int[] masterBone = pStudioHdr.GroupMasterBone(pStudioHdr.SeqGroup(iSequence));
        return iLocalBone >= 0 && iLocalBone < masterBone.Length ? masterBone[iLocalBone] : -1;
    }

    // -----------------------------------------------------------------
    // SolveBone (stock 3501-3518)
    // -----------------------------------------------------------------

    /// <summary><c>SolveBone( const CStudioHdr *, int iBone, matrix3x4_t
    /// *pBoneToWorld, Vector pos[], Quaternion q[] )</c> (3501-3518): turn a
    /// bone's world transform into pos/quat in its parent's bonespace. Used
    /// by <c>CIKContext.SolveLock</c> to write the solved chain back down.</summary>
    public static void SolveBone(
        StudioHdr pStudioHdr, int iBone, Span<BoneMatrix3x4> boneToWorld,
        Vector[] pos, Quaternion[] q)
    {
        int iParent = pStudioHdr.BoneParent(iBone);

        BoneMatrix3x4 worldToBone = BoneSetupMath.MatrixInvert(boneToWorld[iParent]);
        BoneMatrix3x4 local = BoneSetupMath.ConcatTransforms(worldToBone, boneToWorld[iBone]);

        (q[iBone], pos[iBone]) = BoneSetupMath.MatrixAnglesQuaternion(local);
    }
}
