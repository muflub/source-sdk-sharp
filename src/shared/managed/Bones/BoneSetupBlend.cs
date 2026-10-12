//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The blend cluster of bone_setup.cpp, ported to C#: WorldSpaceSlerp,
// SlerpBones, BlendBones, ScaleBones, InitPose, PoseIsAllZeros,
// Calc3WayBlendIndices and Studio_BuildMatrices.
//
// Every body is a line port of the stock original; the free functions write
// through the q1/pos1 output parameters exactly as stock does. No mutable
// statics (SS0001), no clock, no convar.
//
// Stock's scratch pools (rows 136/137: g_MatrixPool, g_VectorPool,
// g_QaternionPool, CBoneSetupMemoryPool<T>) are rents from a BoneScratch
// the caller hands down -- an instance free-list, never a static one,
// because SS0001 forbids the static state a pool is; with none, a fresh
// array. That one sentence documents the rows everywhere they are used.
//
// The seqdesc flag bits this cluster reads that <see cref="StudioConstants"/>
// does not carry are declared locally (with their studio.h lines) and
// listed in needs-blend.md for promotion into the shared constants table.
//
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The pose-blend helpers of <c>bone_setup.cpp</c>: the slerp/blend/scale
/// family over the <c>pos[]</c>/<c>q[]</c> pose arrays, the pose seeding,
/// the 3-way blend triangle, and the world-matrix chain builder.
/// </summary>
public static class BoneSetupBlend
{
    // Stock's seqdesc/autolayer flag bits this cluster reads that
    // StudioConstants does not carry (promotion listed in needs-blend.md).
    internal const int STUDIO_AUTOPLAY = 0x0008;  // studio.h:3081
    internal const int STUDIO_POST = 0x0010;      // studio.h:3082
    internal const int STUDIO_CYCLEPOSE = 0x0080; // studio.h:3085
    internal const int STUDIO_REALTIME = 0x0100;  // studio.h:3086
    internal const int STUDIO_LOCAL = 0x0200;     // studio.h:3087
    internal const int STUDIO_WORLD = 0x4000;     // studio.h:3092

    // autolayer flags (studio.h:3100-3108)
    internal const int STUDIO_AL_SPLINE = 0x0040;
    internal const int STUDIO_AL_XFADE = 0x0080;
    internal const int STUDIO_AL_NOBLEND = 0x0200;
    internal const int STUDIO_AL_LOCAL = 0x1000;
    internal const int STUDIO_AL_POSE = 0x4000;

    // -----------------------------------------------------------------
    // Per-call scratch rent (CBoneSetupMemoryPool rows 136/137)
    // -----------------------------------------------------------------

    // Stock's CBoneSetupMemoryPool<T>::Alloc (oracle 53-62) is a static
    // free-list of T[MAXSTUDIOBONES] blocks. SS0001 forbids the static pool,
    // so the free-list is an instance, BoneScratch, which a skeleton service
    // owns and hands down; every member below that says "rents" takes its
    // scratch from it (a fresh array when the caller passes none).

    // -----------------------------------------------------------------
    // Virtual-group weight map (stock's pSeqGroup->boneMap dispatch)
    // -----------------------------------------------------------------

    /// <summary>
    /// Stock's <c>pSeqGroup</c>: with no virtual model, <see cref="Map"/> is
    /// null and the bone index reads the weight table directly; with one it
    /// is the sequence's <c>virtualgroup_t::boneMap</c> (base bone -&gt;
    /// group-local bone, -1 when the group lacks the bone). The weight list
    /// always belongs to <c>seqdesc</c> itself (the record lives in that
    /// group), so <c>seqdesc.weight( j )</c> is the mapped read.
    /// </summary>
    internal readonly struct SeqGroupMap
    {
        public SeqGroupMap(int[]? map) => Map = map;

        public readonly int[]? Map;

        public bool IsGroup => Map is not null;
    }

    /// <summary>
    /// Stock's <c>pVModel ? pVModel->pSeqGroup( sequence ) : NULL</c>,
    /// resolved through the fold surface
    /// (<see cref="StudioHdr.SeqGroup"/> / <see cref="StudioHdr.GroupBoneMap"/>).
    /// </summary>
    internal static SeqGroupMap ResolveSeqGroup(StudioHdr pStudioHdr, int sequence)
    {
        if (!pStudioHdr.IsVirtual)
        {
            return new SeqGroupMap(null);
        }

        return new SeqGroupMap(pStudioHdr.GroupBoneMap(pStudioHdr.SeqGroup(sequence)));
    }

    /// <summary>Stock's <c>seqdesc.weight( j )</c> under the map: the
    /// group-local index when folded, the global index otherwise; 0 for a
    /// bone the fold group does not have (<c>boneMap[i] == -1</c>).</summary>
    internal static float WeightAt(in SeqGroupMap g, MstudioSeqDesc seqdesc, int i)
    {
        int j = g.Map is null ? i : g.Map[i];
        if (j < 0)
        {
            return 0.0f;
        }

        return seqdesc.Weight(j);
    }

    // -----------------------------------------------------------------
    // WorldSpaceSlerp (stock 1249-1365)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>WorldSpaceSlerp</c> — oracle 1249-1365: blends q1,pos1 with
    /// q2,pos2 in world space, result written into q1/pos1. Per masked
    /// bone: the parent weight <c>s1</c> and own weight <c>s2</c> come from
    /// the sequence weight table (mapped through the fold's boneMap); both
    /// at 1 copies, <c>s2 &gt; 0</c> builds both poses' bone chains under a
    /// fake identity root, slerps the bone's world quaternion, writes it
    /// back through the parent's inverse, and lerps the local bone length.
    /// The three <c>g_MatrixPool</c> blocks (<c>srcBoneToWorld</c>,
    /// <c>destBoneToWorld</c>, <c>targetBoneToWorld</c>) are per-call rents
    /// (rows 136/137).
    /// </summary>
    public static void WorldSpaceSlerp(
        StudioHdr pStudioHdr,
        Quaternion[] q1,
        Vector[] pos1,
        MstudioSeqDesc seqdesc,
        int sequence,
        Quaternion[] q2,
        Vector[] pos2,
        float s,
        int boneMask,
        BoneScratch? scratch = null)
    {
        BoneScratch.Mark mark = scratch?.Save() ?? default;

        try
        {
            WorldSpaceSlerpRented(pStudioHdr, q1, pos1, seqdesc, sequence, q2, pos2, s, boneMask, scratch);
        }
        finally
        {
            scratch?.Restore(mark);
        }
    }

    private static void WorldSpaceSlerpRented(
        StudioHdr pStudioHdr,
        Quaternion[] q1,
        Vector[] pos1,
        MstudioSeqDesc seqdesc,
        int sequence,
        Quaternion[] q2,
        Vector[] pos2,
        float s,
        int boneMask,
        BoneScratch? scratch)
    {
        // make fake root transform (oracle 1265: SetIdentityMatrix)
        BoneMatrix3x4 rootXform = BoneMatrix3x4.Identity;

        BoneMatrix3x4[] srcBoneToWorld = BoneScratch.Matrices(scratch);
        BoneMatrix3x4[] destBoneToWorld = BoneScratch.Matrices(scratch);
        BoneMatrix3x4[] targetBoneToWorld = BoneScratch.Matrices(scratch);

        BoneSetupExtract.BoneBitList srcBoneComputed = BoneScratch.BitList(scratch);
        BoneSetupExtract.BoneBitList destBoneComputed = BoneScratch.BitList(scratch);
        BoneSetupExtract.BoneBitList targetBoneComputed = BoneScratch.BitList(scratch);

        SeqGroupMap pSeqGroup = ResolveSeqGroup(pStudioHdr, sequence);

        for (int i = 0; i < pStudioHdr.NumBones; i++)
        {
            // skip unused bones
            if ((pStudioHdr.BoneFlags(i) & boneMask) == 0)
            {
                continue;
            }

            // stock reads pbone[i].parent from the bone records here, not
            // the linear-bone routing.
            int n = pStudioHdr.Bone(i).Parent;
            float s1 = 0.0f; // weight of parent for q2, pos2
            float s2;        // weight for q2, pos2
            if (pSeqGroup.IsGroup)
            {
                int j = pSeqGroup.Map![i];
                if (j >= 0)
                {
                    s2 = s * seqdesc.Weight(j); // blend in based on this bone's weight
                    if (n != -1)
                    {
                        s1 = s * seqdesc.Weight(pSeqGroup.Map[n]);
                    }
                }
                else
                {
                    s2 = 0.0f;
                }
            }
            else
            {
                s2 = s * seqdesc.Weight(i); // blend in based on this bone's weight
                if (n != -1)
                {
                    s1 = s * seqdesc.Weight(n);
                }
            }

            if (s1 == 1.0f && s2 == 1.0f)
            {
                pos1[i] = pos2[i];
                q1[i] = q2[i];
            }
            else if (s2 > 0.0f)
            {
                BoneSetupExtract.BuildBoneChain(
                    pStudioHdr, rootXform, pos1, q1, i, destBoneToWorld, destBoneComputed);
                BoneSetupExtract.BuildBoneChain(
                    pStudioHdr, rootXform, pos2, q2, i, srcBoneToWorld, srcBoneComputed);

                (Quaternion srcQ, Vector srcPos) = BoneSetupMath.MatrixAnglesQuaternion(srcBoneToWorld[i]);
                (Quaternion destQ, Vector destPos) = BoneSetupMath.MatrixAnglesQuaternion(destBoneToWorld[i]);

                Quaternion targetQ = BoneSetupMath.QuaternionSlerp(destQ, srcQ, s2);
                // Stock (bone_setup.cpp:1410) calls
                // AngleMatrix( targetQ, destPos, targetBoneToWorld[i] ) with
                // targetQ a Quaternion.  The only matching overloads are
                // AngleMatrix(const RadianEuler&, const Vector&, matrix3x4_t&)
                // and AngleMatrix(const QAngle&, const Vector&, matrix3x4_t&)
                // (mathlib.h:793-796); Quaternion -> RadianEuler is the
                // implicit non-explicit ctor RadianEuler(Quaternion const&)
                // (vector.h:1736-1739 -> QuaternionAngles(q, RadianEuler&),
                // mathlib_base.cpp:2191), while Quaternion -> QAngle would
                // need a two-step user conversion and never resolves.  So
                // stock's route is QuaternionAngles (QuaternionMatrix +
                // MatrixAngles + the Init(RAD2DEG(z), RAD2DEG(x),
                // RAD2DEG(y)) reorder, mathlib.h:854-859) into
                // AngleMatrix(RadianEuler) (mathlib_base.cpp:1180, which
                // re-writes degrees via RAD2DEG per component), then
                // AngleMatrix(QAngle) (1194).  It is NOT bit-identical to
                // building the matrix straight from the quaternion with
                // QuaternionMatrix: the float-degree round-trip perturbs
                // the basis in the last ulps.  Mirror the stock route:
                targetBoneToWorld[i] = BoneSetupMath.AngleMatrix(
                    BoneSetupMath.QuaternionAngles(targetQ), destPos);

                // back solve
                if (n == -1)
                {
                    (q1[i], Vector _) = BoneSetupMath.MatrixAnglesQuaternion(targetBoneToWorld[i]);
                }
                else
                {
                    BoneMatrix3x4 worldToBone = BoneSetupMath.MatrixInvert(targetBoneToWorld[n]);
                    BoneMatrix3x4 local = BoneSetupMath.ConcatTransforms(worldToBone, targetBoneToWorld[i]);
                    (q1[i], Vector _) = BoneSetupMath.MatrixAnglesQuaternion(local);

                    // blend bone lengths (local space). Stock's Lerp(s,a,b)
                    // is a + (b-a)*s (mathlib.h), i.e. VectorLerp.
                    pos1[i] = BoneSetupMath.VectorLerp(pos1[i], pos2[i], s2);
                }
            }
        }
    }

    // -----------------------------------------------------------------
    // SlerpBones (stock 1373-1522)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>SlerpBones</c> — oracle 1373-1522: blends q1,pos1 with q2,pos2 at
    /// <paramref name="s"/>, writing into q1/pos1. 0 keeps q1,pos1; &gt; 1
    /// clamps to 1 (q2,pos2). <c>STUDIO_WORLD</c> routes to
    /// <see cref="WorldSpaceSlerp"/>; <c>STUDIO_DELTA</c> accumulates
    /// (<c>STUDIO_POST</c> through <c>QuaternionMA(q1, s2, q2)</c>, else
    /// <c>QuaternionSM(s2, q2, q1)</c>) and adds the positions; otherwise
    /// the slerp arm honours <c>BONE_FIXED_ALIGNMENT</c> with
    /// <c>QuaternionSlerpNoAlign</c>. The stock <c>stackalloc</c> weight
    /// table stays a per-call local.
    /// </summary>
    public static void SlerpBones(
        StudioHdr pStudioHdr,
        Quaternion[] q1,
        Vector[] pos1,
        MstudioSeqDesc seqdesc, // source of q2 and pos2
        int sequence,
        Quaternion[] q2,
        Vector[] pos2,
        float s,
        int boneMask,
        BoneScratch? scratch = null)
    {
        if (s <= 0.0f)
        {
            return;
        }

        if (s > 1.0f)
        {
            s = 1.0f;
        }

        if ((seqdesc.Flags & STUDIO_WORLD) != 0)
        {
            WorldSpaceSlerp(pStudioHdr, q1, pos1, seqdesc, sequence, q2, pos2, s, boneMask, scratch);
            return;
        }

        SeqGroupMap pSeqGroup = ResolveSeqGroup(pStudioHdr, sequence);

        // Build weightlist for all bones (oracle 1406-1432, stock's stackalloc table)
        int nBoneCount = pStudioHdr.NumBones;
        // Stock's stackalloc table, on the stack here too (zeroed, as new was).
        Span<float> pS2 = stackalloc float[StudioConstants.MAXSTUDIOBONES];
        for (int i = 0; i < nBoneCount; i++)
        {
            // skip unused bones
            if ((pStudioHdr.BoneFlags(i) & boneMask) == 0)
            {
                pS2[i] = 0.0f;
                continue;
            }

            pS2[i] = s * WeightAt(pSeqGroup, seqdesc, i); // blend in based on this bone's weight
        }

        if ((seqdesc.Flags & StudioConstants.STUDIO_DELTA) != 0)
        {
            for (int i = 0; i < nBoneCount; i++)
            {
                float s2 = pS2[i];
                if (s2 <= 0.0f)
                {
                    continue;
                }

                if ((seqdesc.Flags & STUDIO_POST) != 0)
                {
                    q1[i] = BoneSetupMath.QuaternionMA(q1[i], s2, q2[i]);
                }
                else
                {
                    q1[i] = BoneSetupMath.QuaternionSM(s2, q2[i], q1[i]);
                }

                // FIXME: are these correct? (stock comment, oracle 1453/1469)
                pos1[i] = new Vector(
                    pos1[i].X + pos2[i].X * s2,
                    pos1[i].Y + pos2[i].Y * s2,
                    pos1[i].Z + pos2[i].Z * s2);
            }

            return;
        }

        for (int i = 0; i < nBoneCount; i++)
        {
            float s2 = pS2[i];
            if (s2 <= 0.0f)
            {
                continue;
            }

            float s1 = 1.0f - s2;

            Quaternion q3;
            if ((pStudioHdr.BoneFlags(i) & StudioConstants.BONE_FIXED_ALIGNMENT) != 0)
            {
                q3 = BoneSetupMath.QuaternionSlerpNoAlign(q2[i], q1[i], s1);
            }
            else
            {
                q3 = BoneSetupMath.QuaternionSlerp(q2[i], q1[i], s1);
            }

            q1[i] = q3;

            pos1[i] = new Vector(
                pos1[i].X * s1 + pos2[i].X * s2,
                pos1[i].Y * s1 + pos2[i].Y * s2,
                pos1[i].Z * s1 + pos2[i].Z * s2);
        }
    }

    // -----------------------------------------------------------------
    // BlendBones (stock 1531-1627)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>BlendBones</c> — oracle 1531-1627: per-animation blend between
    /// identical-type poses; writes the result into q1,pos1. 0 keeps
    /// q1,pos1 (stock's <c>Assert(0); return</c> — the Assert is a no-op in
    /// the release build), 1 copies q2,pos2 over the weighted bones; in
    /// between, blends the quaternions (<c>QuaternionBlend</c>, or
    /// <c>QuaternionBlendNoAlign</c> under <c>BONE_FIXED_ALIGNMENT</c>) and
    /// lerps the positions. Only masked bones with
    /// <c>seqdesc.weight( j ) &gt; 0</c> (mapped through the fold's
    /// boneMap) take part.
    /// </summary>
    public static void BlendBones(
        StudioHdr pStudioHdr,
        Quaternion[] q1,
        Vector[] pos1,
        MstudioSeqDesc seqdesc,
        int sequence,
        Quaternion[] q2,
        Vector[] pos2,
        float s,
        int boneMask)
    {
        SeqGroupMap pSeqGroup = ResolveSeqGroup(pStudioHdr, sequence);

        if (s <= 0.0f)
        {
            // Assert(0); // shouldn't have been called (oracle 1554)
            return;
        }
        else if (s >= 1.0f)
        {
            // Assert(0); // shouldn't have been called (oracle 1559)
            for (int i = 0; i < pStudioHdr.NumBones; i++)
            {
                // skip unused bones
                if ((pStudioHdr.BoneFlags(i) & boneMask) == 0)
                {
                    continue;
                }

                if (WeightAt(pSeqGroup, seqdesc, i) > 0.0f)
                {
                    q1[i] = q2[i];
                    pos1[i] = pos2[i];
                }
            }

            return;
        }

        float s2 = s;
        float s1 = 1.0f - s2;

        for (int i = 0; i < pStudioHdr.NumBones; i++)
        {
            // skip unused bones
            if ((pStudioHdr.BoneFlags(i) & boneMask) == 0)
            {
                continue;
            }

            if (WeightAt(pSeqGroup, seqdesc, i) > 0.0f)
            {
                Quaternion q3;
                if ((pStudioHdr.BoneFlags(i) & StudioConstants.BONE_FIXED_ALIGNMENT) != 0)
                {
                    q3 = BoneSetupMath.QuaternionBlendNoAlign(q2[i], q1[i], s1);
                }
                else
                {
                    q3 = BoneSetupMath.QuaternionBlend(q2[i], q1[i], s1);
                }

                q1[i] = q3;
                pos1[i] = new Vector(
                    pos1[i].X * s1 + pos2[i].X * s2,
                    pos1[i].Y * s1 + pos2[i].Y * s2,
                    pos1[i].Z * s1 + pos2[i].Z * s2);
            }
        }
    }

    // -----------------------------------------------------------------
    // ScaleBones (stock 1632-1677)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>ScaleBones</c> — oracle 1632-1677: fades a delta pose toward
    /// rest. Weighted, masked bones get
    /// <c>QuaternionIdentityBlend(q1[i], s1)</c> and
    /// <c>VectorScale(pos1[i], s2)</c> — note the trap: the position is
    /// SCALED by <c>s2</c>, not offset toward zero by a blend; that
    /// asymmetry is stock (oracle 1673-1674). The seqdesc is re-read from
    /// <c>sequence</c> exactly as stock does.
    /// </summary>
    public static void ScaleBones(
        StudioHdr pStudioHdr,
        Quaternion[] q1,
        Vector[] pos1,
        int sequence,
        float s,
        int boneMask)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(sequence);

        SeqGroupMap pSeqGroup = ResolveSeqGroup(pStudioHdr, sequence);

        float s2 = s;
        float s1 = 1.0f - s2;

        for (int i = 0; i < pStudioHdr.NumBones; i++)
        {
            // skip unused bones
            if ((pStudioHdr.BoneFlags(i) & boneMask) == 0)
            {
                continue;
            }

            if (WeightAt(pSeqGroup, seqdesc, i) > 0.0f)
            {
                q1[i] = QuaternionIdentityBlend(q1[i], s1);
                pos1[i] = BoneSetupMath.VectorScale(pos1[i], s2);
            }
        }
    }

    /// <summary>
    /// mathlib_base.cpp:1580's <c>QuaternionIdentityBlend</c>, ported for its
    /// ONE call site — <c>ScaleBones</c> (bone_setup.cpp:1743) calls it as
    /// <c>QuaternionIdentityBlend( q1[i], s1, q1[i] )</c>: all three operands
    /// alias, so <c>p</c> and <c>qt</c> are the SAME array.  Stock's body is
    /// <c>sclp = 1.0f - t;</c> then
    /// <c>qt.x = p.x * sclp; qt.y = p.y * sclp; qt.z = p.z * sclp;</c> then
    /// <c>if (qt.w &lt; 0.0) qt.w = p.w * sclp - t; else qt.w = p.w * sclp +
    /// t;</c> then <c>QuaternionNormalize( qt );</c>.  Under aliasing the
    /// statement order matters: the <c>qt.x/y/z</c> stores have already run
    /// when the branch reads <c>qt.w</c>, but nothing has touched index 3, so
    /// that test still sees the ORIGINAL <c>p.w</c> — NOT the scaled
    /// <c>p.w * sclp</c> the non-aliased reading suggests — while the
    /// <c>p.w</c> the branch then multiplies is likewise still the original
    /// (the <c>qt.w</c> store that would clobber it happens inside the taken
    /// arm).  Net aliased effect: x/y/z keep their direction scaled by
    /// <c>sclp</c>; the w branch selects on the UNSCALED w and produces
    /// <c>w * sclp ± t</c>; then normalize.  A port that tests the scaled w
    /// reads the wrong operand: at <c>s == 0</c> (so <c>sclp == 0</c>) the
    /// scaled w is <c>+0.0</c>/<c>-0.0</c>, and <c>-0.0 &lt; 0.0</c> is false,
    /// so a negative-w quaternion takes the plus arm, lands on +identity
    /// where stock's unscaled test takes the minus arm and lands on −identity
    /// (<c>xyz*scale − t</c> all zero, <c>w*scale − t = −1</c>).  Not present
    /// in <see cref="BoneSetupMath"/>, so it lives here; promotion ruling in
    /// docs/plans/needs-blend.md (NB-07).
    /// </summary>
    private static Quaternion QuaternionIdentityBlend(Quaternion p, float t)
    {
        float sclp = 1.0f - t;

        // The stores to (qt.x, qt.y, qt.z) happen before the branch, but the
        // branch tests the ORIGINAL w — under ScaleBones' aliased call
        // (bone_setup.cpp:1743) `qt` is `p`, and index 3 is untouched until
        // the branch's own store.  Keep stock's operand exactly.
        float qx = p.X * sclp;
        float qy = p.Y * sclp;
        float qz = p.Z * sclp;
        Quaternion qt = new(qx, qy, qz, p.W < 0.0f ? p.W * sclp - t : p.W * sclp + t);

        BoneSetupMath.QuaternionNormalize(ref qt);
        return qt;
    }
    // -----------------------------------------------------------------
    // InitPose (stock 1782-1812)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>InitPose</c> — oracle 1782-1812: seeds the rest pose for every
    /// masked bone from the linear-bone SoA tables when the model carries
    /// them (<c>pLinearBones()</c>), else from the bone records.
    /// </summary>
    public static void InitPose(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        int boneMask)
    {
        // The rest pose decoded once per header (the same dispatch as below).
        if (pStudioHdr.TryRestPose(out ReadOnlySpan<Vector> restPos, out ReadOnlySpan<Quaternion> restQuat))
        {
            for (int i = 0; i < restPos.Length; i++)
            {
                if ((pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    pos[i] = restPos[i];
                    q[i] = restQuat[i];
                }
            }

            return;
        }

        MstudioLinearBone? pLinearBones = pStudioHdr.LinearBones();
        if (pLinearBones is null)
        {
            for (int i = 0; i < pStudioHdr.NumBones; i++)
            {
                if ((pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    MstudioBone pbone = pStudioHdr.Bone(i);
                    pos[i] = pbone.Position;
                    q[i] = pbone.Quaternion;
                }
            }
        }
        else
        {
            for (int i = 0; i < pStudioHdr.NumBones; i++)
            {
                if ((pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    pos[i] = pLinearBones.Position(i);
                    q[i] = pLinearBones.Quaternion(i);
                }
            }
        }
    }

    // -----------------------------------------------------------------
    // PoseIsAllZeros (stock 1816-1830)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>PoseIsAllZeros</c> — oracle 1816-1830: true when the blend cell's
    /// anim carries <c>STUDIO_ALLZEROS</c> (no real animation data). Stock
    /// derefs <c>pAnimdesc(baseanim)</c> unguarded; an out-of-range cell
    /// answers false here (the shared reader would throw; malformed-data
    /// divergence, recorded in needs-blend.md).
    /// </summary>
    public static bool PoseIsAllZeros(
        StudioHdr pStudioHdr,
        int sequence,
        MstudioSeqDesc seqdesc,
        int i0,
        int i1)
    {
        int baseanim = pStudioHdr.IRelativeAnim(sequence, seqdesc.Anim(i0, i1));
        if ((uint)baseanim >= (uint)pStudioHdr.NumAnims)
        {
            return false;
        }

        return (pStudioHdr.AnimDesc(baseanim).Flags & StudioConstants.STUDIO_ALLZEROS) != 0;
    }

    // -----------------------------------------------------------------
    // Calc3WayBlendIndices (stock 1840-1915)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Calc3WayBlendIndices</c> — oracle 1840-1915: turns the (i0,i1)
    /// blend cell at barycentric (s0,s1) into three anim indices and
    /// barycentric weights. The cell's diagonal is chosen by the parity of
    /// <c>i0 + i1</c>: even triangulates TL→BR (arms B/C on
    /// <c>s0 &gt; s1</c>), odd BL→TR (arms D/A on <c>s0 + s1 &gt; 1</c>).
    /// <c>pWeight[1]</c> clamps to zero near the diagonal and
    /// <c>pWeight[2]</c> closes the sum to one. Pure function of its
    /// arguments — the stock ConVar gate that selects this code lives in
    /// <c>CalcPoseSingle</c>, parameterized as <c>animThreeWayBlend</c>.
    /// </summary>
    public static void Calc3WayBlendIndices(
        int i0, int i1, float s0, float s1, MstudioSeqDesc seqdesc,
        int[] pAnimIndices, float[] pWeight)
    {
        // Figure out which bi-section direction we are using to make triangles.
        bool bEven = ((i0 + i1) & 0x1) == 0;

        int x1, y1;
        int x2, y2;
        int x3, y3;

        // diagonal is between elements 1 & 3
        // TL to BR
        if (bEven)
        {
            if (s0 > s1)
            {
                // B
                x1 = 0; y1 = 0;
                x2 = 1; y2 = 0;
                x3 = 1; y3 = 1;
                pWeight[0] = 1.0f - s0;
                pWeight[1] = s0 - s1;
            }
            else
            {
                // C
                x1 = 1; y1 = 1;
                x2 = 0; y2 = 1;
                x3 = 0; y3 = 0;
                pWeight[0] = s0;
                pWeight[1] = s1 - s0;
            }
        }
        // BL to TR
        else
        {
            float flTotal = s0 + s1;

            if (flTotal > 1.0f)
            {
                // D
                x1 = 1; y1 = 0;
                x2 = 1; y2 = 1;
                x3 = 0; y3 = 1;
                pWeight[0] = 1.0f - s1;
                pWeight[1] = s0 - 1.0f + s1;
            }
            else
            {
                // A
                x1 = 0; y1 = 1;
                x2 = 0; y2 = 0;
                x3 = 1; y3 = 0;
                pWeight[0] = s1;
                pWeight[1] = 1.0f - s0 - s1;
            }
        }

        pAnimIndices[0] = seqdesc.Anim(i0 + x1, i1 + y1);
        pAnimIndices[1] = seqdesc.Anim(i0 + x2, i1 + y2);
        pAnimIndices[2] = seqdesc.Anim(i0 + x3, i1 + y3);

        // clamp the diagonal
        if (pWeight[1] < 0.001f)
        {
            pWeight[1] = 0.0f;
        }

        pWeight[2] = 1.0f - pWeight[0] - pWeight[1];
    }

    // -----------------------------------------------------------------
    // Studio_BuildMatrices (stock 4499-4577)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_BuildMatrices</c> — oracle 4499-4577: builds world
    /// matrices over a bone chain. <c>iBone == -1</c> walks every bone in a
    /// reversed chain list (so the backward pass lays parents before
    /// children); any other bone lists only its parent chain. The
    /// model-to-world <c>rotationmatrix</c> is <c>AngleMatrix(angles,
    /// origin)</c>; outside flScale ±FLT_EPSILON the offset is re-anchored
    /// on origin and the basis rows scale uniformly. The caller's
    /// angles/origin are plain parameters (server-scope ruling); stock's
    /// <c>QAngle</c> arrives as <see cref="BoneAngle"/>. Stock's zeroed
    /// <c>chain[MAXSTUDIOBONES]</c> is a zeroed stack table, as stock's is.
    /// </summary>
    public static void StudioBuildMatrices(
        StudioHdr pStudioHdr,
        BoneAngle angles,
        Vector origin,
        Vector[] pos,
        Quaternion[] q,
        int iBone,
        float flScale,
        Span<BoneMatrix3x4> bonetoworld,
        int boneMask)
    {
        Span<int> chain = stackalloc int[StudioConstants.MAXSTUDIOBONES];
        int chainlength = 0;

        if (iBone < -1 || iBone >= pStudioHdr.NumBones)
        {
            iBone = 0;
        }

        // build list of what bones to use
        if (iBone == -1)
        {
            // all bones
            chainlength = pStudioHdr.NumBones;
            for (int i = 0; i < pStudioHdr.NumBones; i++)
            {
                chain[chainlength - i - 1] = i;
            }
        }
        else
        {
            // only the parent bones
            int i = iBone;
            while (i != -1)
            {
                chain[chainlength++] = i;
                i = pStudioHdr.BoneParent(i);
            }
        }

        // model to world transformation
        BoneMatrix3x4 rotationmatrix = BoneSetupMath.AngleMatrix(angles, origin);

        // Account for a change in scale
        if (flScale < 1.0f - BoneSetupMath.FltEpsilon || flScale > 1.0f + BoneSetupMath.FltEpsilon)
        {
            Vector vecOffset = rotationmatrix.Position;
            vecOffset -= origin;
            vecOffset *= flScale;
            vecOffset += origin;
            rotationmatrix.Position = vecOffset;
            // Scale it uniformly. Stock's VectorScale(rotationmatrix[r],
            // flScale, rotationmatrix[r]) scales the row's three basis
            // components in place; the translation column (W) is a
            // different Vector and untouched — so scale X/Y/Z, keep W.
            System.Numerics.Vector4 r0 = rotationmatrix.Row0;
            System.Numerics.Vector4 r1 = rotationmatrix.Row1;
            System.Numerics.Vector4 r2 = rotationmatrix.Row2;
            rotationmatrix.Row0 = new System.Numerics.Vector4(r0.X * flScale, r0.Y * flScale, r0.Z * flScale, r0.W);
            rotationmatrix.Row1 = new System.Numerics.Vector4(r1.X * flScale, r1.Y * flScale, r1.Z * flScale, r1.W);
            rotationmatrix.Row2 = new System.Numerics.Vector4(r2.X * flScale, r2.Y * flScale, r2.Z * flScale, r2.W);
        }

        for (int j = chainlength - 1; j >= 0; j--)
        {
            int i = chain[j];
            if ((pStudioHdr.BoneFlags(i) & boneMask) != 0)
            {
                BoneMatrix3x4 bonematrix = BoneSetupMath.QuaternionMatrix(q[i], pos[i]);

                if (pStudioHdr.BoneParent(i) == -1)
                {
                    bonetoworld[i] = BoneSetupMath.ConcatTransforms(rotationmatrix, bonematrix);
                }
                else
                {
                    bonetoworld[i] = BoneSetupMath.ConcatTransforms(bonetoworld[pStudioHdr.BoneParent(i)], bonematrix);
                }
            }
        }
    }
}
