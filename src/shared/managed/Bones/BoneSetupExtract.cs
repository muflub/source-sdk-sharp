//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Numerics;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The animation-extraction cluster of <c>bone_setup.cpp</c>, ported to C#:
/// <c>ExtractAnimValue</c> (both overloads), <c>CalcBoneQuaternion</c> and
/// <c>CalcBonePosition</c> (core plus the linear-bone dispatch),
/// <c>CalcDecompressedAnimation</c>, <c>CalcLocalHierarchyAnimation</c>,
/// <c>CalcZeroframeData</c>, <c>CalcVirtualAnimation</c>,
/// <c>CalcAnimation</c>, and <c>BuildBoneChain</c>.
///
/// <para>
/// These are pure functions over hdr bytes and caller-owned spans: no clock,
/// no convar, no static state (SS0001). Every time- or setting-dependent
/// input arrives as a parameter, per the port contract — stock's
/// <c>flStall</c> (which it computes from <c>zeroframestalltime</c> mutable
/// state and the clock, bone_setup.cpp:1063) is precomputed by the service
/// and passed in; <c>g_MatrixPool</c> becomes a caller-supplied scratch span.
/// </para>
///
/// <para>
/// All line citations are to the pristine oracle
/// (<c>src/public/bone_setup.cpp</c> at valve-sdk.lock 22288b91). Stock reads
/// past the end of a malformed stream (its release asserts are no-ops); here
/// a read outside the image is UB mirrored as far as it is defined: the
/// value is whatever the byte-level reader answers, and a stream whose
/// bracketing record is missing answers 0, which is what the stock's
/// <c>Assert(0); v1 = v2 = 0; return;</c> arm leaves.
/// </para>
/// </summary>
public static class BoneSetupExtract
{

    // -----------------------------------------------------------------
    // BoneBitList — stock's CBoneBitList (CBitVec<MAXSTUDIOBONES>)
    // -----------------------------------------------------------------

    /// <summary>
    /// Stock's <c>CBoneBitList</c> (bone_setup.h:37): a cleared-per-use bone
    /// bit vector; instance state, never a static (SS0001).
    /// </summary>
    public sealed class BoneBitList
    {
        private readonly uint[] _bits = new uint[StudioConstants.MAXSTUDIOBONES / 32];

        public void Clear() => Array.Clear(_bits, 0, _bits.Length);

        public bool IsBoneMarked(int bone) =>
            (_bits[bone >> 5] & (1u << (bone & 31))) != 0;

        public void MarkBone(int bone) => _bits[bone >> 5] |= 1u << (bone & 31);
    }

    // -----------------------------------------------------------------
    // ExtractAnimValue (stock 267-336 / 339-369)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>ExtractAnimValue( int frame, mstudioanimvalue_t *panimvalue,
    /// float scale, float &amp;v1, float &amp;v2 )</c> — oracle 267-336.
    /// The <c>total==1 &amp;&amp; valid==1</c> fast path reads record
    /// <c>[1]</c> into <b>both</b> outputs; the walk advances the cursor by
    /// <c>valid + 1</c> records while <c>total &lt;= k</c>; the ramp then
    /// either interpolates inside the bracketing pair or pulls the blend
    /// from the next list's first record.
    /// </summary>
    public static void ExtractAnimValue(
        int frame, AnimValueStream panimvalue, float scale, out float v1, out float v2)
    {
        if (panimvalue.IsNull)
        {
            v1 = v2 = 0f;
            return;
        }

        // oracle 278: ( total == 1 ) && ( valid == 1 )  ->  v1 = v2 = panimvalue[1].value * scale
        if (panimvalue.Total(0) == 1 && panimvalue.Valid(0) == 1)
        {
            v1 = v2 = panimvalue.Value(1, scale);
            return;
        }

        int k = frame;
        int c = 0;

        // oracle 287-296: while (panimvalue->num.total <= k)
        // { k -= total; panimvalue += valid + 1; if (total == 0) { v1 = v2 = 0; return; } }
        while (panimvalue.Total(c) <= k)
        {
            k -= panimvalue.Total(c);
            c += panimvalue.Valid(c) + 1;
            if (panimvalue.Total(c) == 0)
            {
                // stock's Assert(0) arm: running off the stream is bad
                v1 = v2 = 0f;
                return;
            }
        }

        if (panimvalue.Valid(c) > k)
        {
            // oracle 300: has valid animation data. Stock indexes through the
            // ADVANCED pointer: all post-walk reads are relative to record c.
            v1 = panimvalue.Value(c + k + 1, scale);
            if (panimvalue.Valid(c) > k + 1)
            {
                // oracle 305: has valid animation blend data
                v2 = panimvalue.Value(c + k + 2, scale);
            }
            else if (panimvalue.Total(c) > k + 1)
            {
                // oracle 311: data repeats, no blend
                v2 = v1;
            }
            else
            {
                // oracle 317: pull blend from first data block in next list
                v2 = panimvalue.Value(c + panimvalue.Valid(c) + 2, scale);
            }
        }
        else
        {
            // oracle 324: get last valid data block
            v1 = panimvalue.Value(c + panimvalue.Valid(c), scale);
            if (panimvalue.Total(c) > k + 1)
            {
                v2 = v1;
            }
            else
            {
                v2 = panimvalue.Value(c + panimvalue.Valid(c) + 2, scale);
            }
        }
    }

    /// <summary>
    /// <c>ExtractAnimValue( int frame, mstudioanimvalue_t *panimvalue,
    /// float scale, float &amp;v1 )</c> — oracle 339-369. No fast path, no
    /// ramp: the bracketing list's <c>[k+1]</c>, or its last valid record.
    /// </summary>
    public static void ExtractAnimValue(
        int frame, AnimValueStream panimvalue, float scale, out float v1)
    {
        if (panimvalue.IsNull)
        {
            v1 = 0f;
            return;
        }

        int k = frame;
        int c = 0;
        while (panimvalue.Total(c) <= k)
        {
            k -= panimvalue.Total(c);
            c += panimvalue.Valid(c) + 1;
            if (panimvalue.Total(c) == 0)
            {
                v1 = 0f;
                return;
            }
        }

        // stock indexes through the advanced pointer: relative to record c
        v1 = panimvalue.Valid(c) > k
            ? panimvalue.Value(c + k + 1, scale)
            : panimvalue.Value(c + panimvalue.Valid(c), scale);
    }

    // -----------------------------------------------------------------
    // CalcBoneQuaternion (stock 374-474 core; 476-489 dispatch)
    // -----------------------------------------------------------------

    /// <summary>
    /// The 9-arg <c>CalcBoneQuaternion</c> core — oracle 374-474. Raw-rot
    /// arms first, then the no-rot arm (delta → identity, else baseQuat),
    /// then the two-angle blend when <c>s &gt; 0.001</c> and the angles
    /// differ, else a single <c>AngleQuaternion</c>. Non-delta records offset
    /// by <c>baseRot</c>; the fixed-alignment align is gated on
    /// <c>!(flags &amp; STUDIO_ANIM_DELTA) &amp;&amp; (iBaseFlags &amp;
    /// BONE_FIXED_ALIGNMENT)</c>.
    /// </summary>
    /// <remarks>
    /// The old WIP's whole-blend block fabricated a slerp ramp; stock's lerp
    /// arm is <c>QuaternionBlend</c> (align-then-no-align-blend, oracle
    /// vmath.c) — kept here through <see cref="BoneSetupMath.QuaternionBlend"/>.
    /// The stock <c>STUDIO_ANIM_QUATERNION</c> name is an X360 leftover never
    /// compared here (oracle has no such compare in this function).
    /// </remarks>
    public static void CalcBoneQuaternion(
        int frame,
        float s,
        Quaternion baseQuat,
        RadianEuler baseRot,
        Vector baseRotScale,
        int iBaseFlags,
        Quaternion baseAlignment,
        MstudioAnim panim,
        out Quaternion q)
    {
        int flags = panim.Flags;

        if ((flags & StudioConstants.STUDIO_ANIM_RAWROT) != 0)
        {
            q = panim.RawQuaternion!.Value;
            return;
        }

        if ((flags & StudioConstants.STUDIO_ANIM_RAWROT2) != 0)
        {
            q = panim.RawQuaternion!.Value;
            return;
        }

        if ((flags & StudioConstants.STUDIO_ANIM_ANIMROT) == 0)
        {
            if ((flags & StudioConstants.STUDIO_ANIM_DELTA) != 0)
            {
                q = Quaternion.Identity; // Init(0,0,0,1)
            }
            else
            {
                q = baseQuat;
            }

            return;
        }

        AnimValueStream rv0 = panim.RotStream(0);
        AnimValueStream rv1 = panim.RotStream(1);
        AnimValueStream rv2 = panim.RotStream(2);

        bool delta = (flags & StudioConstants.STUDIO_ANIM_DELTA) != 0;

        if (s > 0.001f)
        {
            RadianEuler angle1, angle2;

            ExtractAnimValue(frame, rv0, baseRotScale.X, out float a1x, out float a2x);
            ExtractAnimValue(frame, rv1, baseRotScale.Y, out float a1y, out float a2y);
            ExtractAnimValue(frame, rv2, baseRotScale.Z, out float a1z, out float a2z);

            if (!delta)
            {
                a1x += baseRot.X; a2x += baseRot.X;
                a1y += baseRot.Y; a2y += baseRot.Y;
                a1z += baseRot.Z; a2z += baseRot.Z;
            }

            angle1 = new RadianEuler(a1x, a1y, a1z);
            angle2 = new RadianEuler(a2x, a2y, a2z);

            if (angle1.X != angle2.X || angle1.Y != angle2.Y || angle1.Z != angle2.Z)
            {
                // oracle 435-448: AngleQuaternion both, QuaternionBlend(q1, q2, s, q)
                Quaternion q1 = BoneSetupMath.AngleQuaternion(angle1);
                Quaternion q2 = BoneSetupMath.AngleQuaternion(angle2);
                q = BoneSetupMath.QuaternionBlend(q1, q2, s);
            }
            else
            {
                q = BoneSetupMath.AngleQuaternion(angle1);
            }
        }
        else
        {
            ExtractAnimValue(frame, rv0, baseRotScale.X, out float ax);
            ExtractAnimValue(frame, rv1, baseRotScale.Y, out float ay);
            ExtractAnimValue(frame, rv2, baseRotScale.Z, out float az);

            if (!delta)
            {
                ax += baseRot.X;
                ay += baseRot.Y;
                az += baseRot.Z;
            }

            q = BoneSetupMath.AngleQuaternion(new RadianEuler(ax, ay, az));
        }

        // oracle 469-472: align to unified bone
        if (!delta && (iBaseFlags & StudioConstants.BONE_FIXED_ALIGNMENT) != 0)
        {
            q = BoneSetupMath.QuaternionAlign(baseAlignment, q);
        }
    }

    /// <summary>
    /// The <c>mstudiobone_t</c> dispatch overload — oracle 476-489. The
    /// linear-bones-vs-bone choice happens here at the call site: linear
    /// bones answer quat/rot/rotscale/<b>flags</b>/qalignment from their own
    /// tables (stock reads <c>pLinearBones->flags()</c>, not the hdr's
    /// boneFlags cache), the bone record from its fields. The record selected
    /// is <c>pBone[panim.Bone]</c> / <c>pLinearBones-&gt;*(panim.Bone)</c>.
    /// </summary>
    public static void CalcBoneQuaternion(
        int frame,
        float s,
        MstudioBone bone,
        MstudioLinearBone? linearBones,
        MstudioAnim panim,
        out Quaternion q)
    {
        int boneIndex = panim.Bone;
        if (linearBones is not null)
        {
            CalcBoneQuaternion(
                frame, s,
                linearBones.Quaternion(boneIndex),
                new RadianEuler(linearBones.Rotation(boneIndex)),
                linearBones.RotationScale(boneIndex),
                linearBones.Flags(boneIndex),
                linearBones.QuaternionAlignment(boneIndex),
                panim, out q);
        }
        else
        {
            CalcBoneQuaternion(
                frame, s,
                bone.Quaternion,
                new RadianEuler(bone.Rotation),
                bone.RotationScale,
                bone.Flags,
                bone.QuaternionAlignment,
                panim, out q);
        }
    }

    // -----------------------------------------------------------------
    // CalcBonePosition (stock 498-550 core; 553-566 dispatch)
    // -----------------------------------------------------------------

    /// <summary>
    /// The 5-arg <c>CalcBonePosition</c> core — oracle 498-550. RAWPOS reads
    /// the decoded vector; no ANIMPOS answers delta-zero or basePos; the
    /// 2-value arm is <c>v1 * (1 - s) + v2 * s</c> per component (stock's own
    /// expression, not a lerp call); non-delta adds <c>basePos</c> last.
    /// </summary>
    public static void CalcBonePosition(
        int frame,
        float s,
        Vector basePos,
        Vector baseBoneScale,
        MstudioAnim panim,
        out Vector pos)
    {
        int flags = panim.Flags;

        if ((flags & StudioConstants.STUDIO_ANIM_RAWPOS) != 0)
        {
            pos = panim.RawPosition!.Value;
            return;
        }
        else if ((flags & StudioConstants.STUDIO_ANIM_ANIMPOS) == 0)
        {
            if ((flags & StudioConstants.STUDIO_ANIM_DELTA) != 0)
            {
                pos = Vector.Zero;
            }
            else
            {
                pos = basePos;
            }

            return;
        }

        if (s > 0.001f)
        {
            // oracle 527-533: per-component ExtractAnimValue 2-ref, then
            // pos[j] = v1 * (1.0 - s) + v2 * s — the double literal promotes
            // the subtract and the v1 product; v2 * s stays float and widens
            // for the add; one rounding on the store.
            ExtractAnimValue(frame, panim.PosStream(0), baseBoneScale.X, out float v1, out float v2);
            float x = (float)(v1 * (1.0 - s) + v2 * s);
            ExtractAnimValue(frame, panim.PosStream(1), baseBoneScale.Y, out v1, out v2);
            float y = (float)(v1 * (1.0 - s) + v2 * s);
            ExtractAnimValue(frame, panim.PosStream(2), baseBoneScale.Z, out v1, out v2);
            float z = (float)(v1 * (1.0 - s) + v2 * s);
            pos = new Vector(x, y, z);
        }
        else
        {
            // oracle 537-541: fast single-value decode straight into pos[j]
            ExtractAnimValue(frame, panim.PosStream(0), baseBoneScale.X, out float px);
            ExtractAnimValue(frame, panim.PosStream(1), baseBoneScale.Y, out float py);
            ExtractAnimValue(frame, panim.PosStream(2), baseBoneScale.Z, out float pz);
            pos = new Vector(px, py, pz);
        }

        if ((flags & StudioConstants.STUDIO_ANIM_DELTA) == 0)
        {
            pos.X += basePos.X;
            pos.Y += basePos.Y;
            pos.Z += basePos.Z;
        }
    }

    /// <summary>
    /// The <c>mstudiobone_t</c> dispatch overload — oracle 553-566.
    /// </summary>
    public static void CalcBonePosition(
        int frame,
        float s,
        MstudioBone bone,
        MstudioLinearBone? linearBones,
        MstudioAnim panim,
        out Vector pos)
    {
        int boneIndex = panim.Bone;
        if (linearBones is not null)
        {
            CalcBonePosition(
                frame, s,
                linearBones.Position(boneIndex),
                linearBones.PositionScale(boneIndex),
                panim, out pos);
        }
        else
        {
            CalcBonePosition(frame, s, bone.Position, bone.PositionScale, panim, out pos);
        }
    }

    // -----------------------------------------------------------------
    // CalcDecompressedAnimation (stock 613-659)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CalcDecompressedAnimation</c> — oracle 613-659. Six streams
    /// (3 pos, 3 rot-as-RadianEuler) against <c>scale[0..5]</c>; the
    /// <c>fraq &gt; 0.0001</c> arm slerp-blends when any angle differs,
    /// matching <c>CalcBoneQuaternion</c>'s blend gate.
    /// </summary>
    public static void CalcDecompressedAnimation(
        MstudioCompressedIkError pCompressed, int iFrame, float fraq, out Vector pos, out Quaternion q)
    {
        if (fraq > 0.0001f)
        {
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(0), pCompressed.Scale(0), out float p1x, out float p2x);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(1), pCompressed.Scale(1), out float p1y, out float p2y);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(2), pCompressed.Scale(2), out float p1z, out float p2z);
            pos = new Vector(
                p1x * (1f - fraq) + p2x * fraq,
                p1y * (1f - fraq) + p2y * fraq,
                p1z * (1f - fraq) + p2z * fraq);

            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(3), pCompressed.Scale(3), out float a1x, out float a2x);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(4), pCompressed.Scale(4), out float a1y, out float a2y);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(5), pCompressed.Scale(5), out float a1z, out float a2z);

            if (a1x != a2x || a1y != a2y || a1z != a2z)
            {
                Quaternion q1 = BoneSetupMath.AngleQuaternion(new RadianEuler(a1x, a1y, a1z));
                Quaternion q2 = BoneSetupMath.AngleQuaternion(new RadianEuler(a2x, a2y, a2z));
                q = BoneSetupMath.QuaternionBlend(q1, q2, fraq);
            }
            else
            {
                q = BoneSetupMath.AngleQuaternion(new RadianEuler(a1x, a1y, a1z));
            }
        }
        else
        {
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(0), pCompressed.Scale(0), out float px);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(1), pCompressed.Scale(1), out float py);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(2), pCompressed.Scale(2), out float pz);
            pos = new Vector(px, py, pz);

            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(3), pCompressed.Scale(3), out float ax);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(4), pCompressed.Scale(4), out float ay);
            ExtractAnimValue(iFrame, pCompressed.AnimValueStream(5), pCompressed.Scale(5), out float az);
            q = BoneSetupMath.AngleQuaternion(new RadianEuler(ax, ay, az));
        }
    }

    // -----------------------------------------------------------------
    // BuildBoneChain (stock 3468-3495)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>BuildBoneChain</c> — oracle 3468-3495: skip when computed,
    /// <c>QuaternionMatrix(q[i], pos[i])</c>, recurse to the parent, concat.
    /// Stock recursion depth is bounded by the parent chain (≤ 128).
    /// </summary>
    public static void BuildBoneChain(
        StudioHdr pStudioHdr,
        in BoneMatrix3x4 rootxform,
        Vector[] pos,
        Quaternion[] q,
        int iBone,
        Span<BoneMatrix3x4> boneToWorld,
        BoneBitList boneComputed)
    {
        if (boneComputed.IsBoneMarked(iBone))
        {
            return;
        }

        BoneMatrix3x4 bonematrix = BoneSetupMath.QuaternionMatrix(q[iBone], pos[iBone]);

        int parent = pStudioHdr.BoneParent(iBone);
        if (parent == -1)
        {
            boneToWorld[iBone] = BoneSetupMath.ConcatTransforms(rootxform, bonematrix);
        }
        else
        {
            // evil recursive!!! (oracle 3490)
            BuildBoneChain(pStudioHdr, rootxform, pos, q, parent, boneToWorld, boneComputed);
            boneToWorld[iBone] = BoneSetupMath.ConcatTransforms(boneToWorld[parent], bonematrix);
        }

        boneComputed.MarkBone(iBone);
    }

    // -----------------------------------------------------------------
    // CalcLocalHierarchyAnimation (stock 664-768)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CalcLocalHierarchyAnimation</c> — oracle 664-768: the influence
    /// ramp (wrap when <c>end &gt; 1</c>, SimpleSpline), the decompressed
    /// local error at <c>iFrame - iStart</c>, two BuildBoneChain calls with a
    /// fake identity root, then the back-solve through the parent's
    /// boneToWorld with the weight blended in (slerp for the quaternion,
    /// stock's <c>Lerp( weight, p1, pos[i] )</c> for the position — note the
    /// reversed operand order vs mathlib's <c>Lerp(s,a,b) = a + (b-a)*s</c>).
    /// </summary>
    public static void CalcLocalHierarchyAnimation(
        StudioHdr pStudioHdr,
        Span<BoneMatrix3x4> boneToWorld,
        BoneBitList boneComputed,
        Vector[] pos,
        Quaternion[] q,
        MstudioLocalHierarchy pHierarchy,
        int iBone,
        int iNewParent,
        float cycle,
        int iFrame,
        float flFraq,
        int boneMask)
    {
        // make fake root transform (oracle 687: static identity; here a local —
        // the static was thread-affinity debt, not behaviour)
        BoneMatrix3x4 rootXform = BoneMatrix3x4.Identity;

        // FIXME: missing check to see if seq has a weight for this bone
        float weight = 1.0f;

        // oracle 692-714: ramp on the influence
        if (pHierarchy.Tail - pHierarchy.Peak < 1.0f)
        {
            float index = cycle;
            if (pHierarchy.End > 1.0f && index < pHierarchy.Start)
            {
                index += 1.0f;
            }

            if (index < pHierarchy.Start)
            {
                return;
            }

            if (index >= pHierarchy.End)
            {
                return;
            }

            if (index < pHierarchy.Peak && pHierarchy.Start != pHierarchy.Peak)
            {
                weight = (index - pHierarchy.Start) / (pHierarchy.Peak - pHierarchy.Start);
            }
            else if (index > pHierarchy.Tail && pHierarchy.End != pHierarchy.Tail)
            {
                weight = (pHierarchy.End - index) / (pHierarchy.End - pHierarchy.Tail);
            }

            weight = BoneSetupMath.SimpleSpline(weight);
        }

        // oracle 716: pLocalAnim() is the compressed error at this+localanimindex
        MstudioCompressedIkError? pLocalAnim = pHierarchy.PLocalAnim();
        if (pLocalAnim is null)
        {
            // stock reads address 0 (crash); the port returns early instead.
            // Divergence: malformed data only — every qc-produced hierarchy
            // record carries an error.
            return;
        }

        CalcDecompressedAnimation(
            pLocalAnim, iFrame - pHierarchy.IStart, flFraq, out Vector localPos, out Quaternion localQ);

        BuildBoneChain(pStudioHdr, rootXform, pos, q, iBone, boneToWorld, boneComputed);

        // oracle 720-721: AngleMatrix( localQ, localPos, localXform ) with a
        // Quaternion takes the implicit RadianEuler(Quaternion) route — the
        // NB-06 promotion from docs/plans/needs-blend.md, mirrored like the
        // blend lane's WorldSpaceSlerp target. Not bit-identical to
        // QuaternionMatrix(localQ, localPos).
        BoneMatrix3x4 localXform = BoneSetupMath.AngleMatrix(BoneSetupMath.QuaternionAngles(localQ), localPos);

        if (iNewParent != -1)
        {
            BuildBoneChain(pStudioHdr, rootXform, pos, q, iNewParent, boneToWorld, boneComputed);
            boneToWorld[iBone] = BoneSetupMath.ConcatTransforms(boneToWorld[iNewParent], localXform);
        }
        else
        {
            boneToWorld[iBone] = localXform;
        }

        // oracle 735-767: back solve
        int n = pStudioHdr.Bone(iBone).Parent;
        if (n == -1)
        {
            if (weight == 1.0f)
            {
                (Quaternion qo, Vector po) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[iBone]);
                q[iBone] = qo;
                pos[iBone] = po;
            }
            else
            {
                (Quaternion q1, Vector p1) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[iBone]);
                q[iBone] = BoneSetupMath.QuaternionSlerp(q[iBone], q1, weight);
                pos[iBone] = BoneSetupMath.VectorLerp(p1, pos[iBone], weight);
            }
        }
        else
        {
            BoneMatrix3x4 worldToBone = BoneSetupMath.MatrixInvert(boneToWorld[n]);
            BoneMatrix3x4 local = BoneSetupMath.ConcatTransforms(worldToBone, boneToWorld[iBone]);
            if (weight == 1.0f)
            {
                (Quaternion qo, Vector po) = BoneSetupMath.MatrixAnglesQuaternion(local);
                q[iBone] = qo;
                pos[iBone] = po;
            }
            else
            {
                (Quaternion q1, Vector p1) = BoneSetupMath.MatrixAnglesQuaternion(local);
                q[iBone] = BoneSetupMath.QuaternionSlerp(q[iBone], q1, weight);
                pos[iBone] = BoneSetupMath.VectorLerp(p1, pos[iBone], weight);
            }
        }
    }

    // -----------------------------------------------------------------
    // CalcZeroframeData (stock 776-877)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CalcZeroframeData</c> — oracle 776-877. Single-frame rows blend
    /// pos by <c>pos[i] * (1 - flWeight) + p * flWeight</c> and rot by
    /// <c>QuaternionBlend</c>; the multi-frame arm Hermite-splines three
    /// rows. The stride advances unconditionally per SAVEFRAME flag, even
    /// when the bone is masked out (oracle keeps <c>pData +=</c> outside the
    /// gate).
    /// </summary>
    /// <param name="pAnimStudioHdr">The hdr whose bone array the rows are
    /// laid out over (the render hdr; virtual: the anim group's hdr).</param>
    /// <param name="animGroupMasterBone">The anim group's masterBone map;
    /// null = identity (non-virtual).</param>
    public static void CalcZeroframeData(
        StudioHdr pStudioHdr,
        StudioHdr pAnimStudioHdr,
        int[]? animGroupMasterBone,
        MstudioAnimDesc animdesc,
        float fFrame,
        Vector[] pos,
        Quaternion[] q,
        int boneMask,
        float flWeight)
    {
        int pDataBase = animdesc.PZeroFrameData();
        if (pDataBase < 0)
        {
            return; // stock: !pData
        }

        // The rows sit in the animdesc's own hdr image (the anim group's),
        // exactly where stock's pData walks.
        byte[] image = animdesc.RawImage;
        int animNumBones = pAnimStudioHdr.NumBonesBase;
        short zeroframecount = animdesc.ZeroFrameCount;

        if (zeroframecount == 1)
        {
            int pData = pDataBase;
            for (int j = 0; j < animNumBones; j++)
            {
                int i = animGroupMasterBone is not null ? animGroupMasterBone[j] : j;
                int animFlags = pAnimStudioHdr.Bone(j).Flags;

                if ((animFlags & StudioConstants.BONE_HAS_SAVEFRAME_POS) != 0)
                {
                    if (i >= 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                    {
                        Vector pRow = StudioCodec.ReadVector48(image, pData);
                        pos[i] = pos[i] * (1.0f - flWeight) + pRow * flWeight;
                    }

                    pData += 6; // sizeof(Vector48) = 6 (three float16; compiled ground truth)
                }

                if ((animFlags & StudioConstants.BONE_HAS_SAVEFRAME_ROT) != 0)
                {
                    if (i >= 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                    {
                        Quaternion q0 = StudioCodec.ReadQuaternion64(image, pData);
                        q[i] = BoneSetupMath.QuaternionBlend(q[i], q0, flWeight);
                    }

                    pData += 8; // sizeof(Quaternion64) = 8 (compiled ground truth)
                }
            }

            return;
        }

        // oracle 826-838: the Hermite window
        float s1;
        int index = (int)(fFrame / (float)animdesc.ZeroFrameSpan); // stock int = float division
        if (index >= zeroframecount - 1)
        {
            index = zeroframecount - 2;
            s1 = 1.0f;
        }
        else
        {
            s1 = Math.Clamp(
                (fFrame - index * (float)animdesc.ZeroFrameSpan) / (float)animdesc.ZeroFrameSpan,
                0.0f, 1.0f);
        }

        int i0 = Math.Max(index - 1, 0);
        int i1 = index;
        int i2 = Math.Min(index + 1, zeroframecount - 1);

        int p = pDataBase;
        for (int j = 0; j < animNumBones; j++)
        {
            int i = animGroupMasterBone is not null ? animGroupMasterBone[j] : j;
            int animFlags = pAnimStudioHdr.Bone(j).Flags;

            if ((animFlags & StudioConstants.BONE_HAS_SAVEFRAME_POS) != 0)
            {
                if (i >= 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    Vector p0 = StudioCodec.ReadVector48(image, p + 6 * i0);
                    Vector p1 = StudioCodec.ReadVector48(image, p + 6 * i1);
                    Vector p2 = StudioCodec.ReadVector48(image, p + 6 * i2);
                    Vector p3 = BoneSetupMath.HermiteSpline(p0, p1, p2, s1);
                    pos[i] = pos[i] * (1.0f - flWeight) + p3 * flWeight;
                }

                p += 6 * zeroframecount;
            }

            if ((animFlags & StudioConstants.BONE_HAS_SAVEFRAME_ROT) != 0)
            {
                if (i >= 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    Quaternion q0 = StudioCodec.ReadQuaternion64(image, p + 8 * i0);
                    Quaternion q1 = StudioCodec.ReadQuaternion64(image, p + 8 * i1);
                    Quaternion q2 = StudioCodec.ReadQuaternion64(image, p + 8 * i2);
                    if (flWeight == 1.0f)
                    {
                        q[i] = BoneSetupMath.HermiteSpline(q0, q1, q2, s1);
                    }
                    else
                    {
                        Quaternion q3 = BoneSetupMath.HermiteSpline(q0, q1, q2, s1);
                        q[i] = BoneSetupMath.QuaternionBlend(q[i], q3, flWeight);
                    }
                }

                p += 8 * zeroframecount;
            }
        }
    }

    // -----------------------------------------------------------------
    // CalcAnimation (stock 1031-1159) and CalcVirtualAnimation (884-1023)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CalcAnimation</c> — oracle 1031-1159. Decodes one pose of one
    /// animation of one sequence into the caller's pos/q arrays.
    /// </summary>
    /// <param name="flStall">Stock's <c>animdesc.pAnim(..., flStall)</c>
    /// output: the cross-fade weight of the previous zeroframe data. Stock
    /// computes it from <c>zeroframestalltime</c> mutable state and the
    /// clock; the service owns that state and passes the value in
    /// (port contract). When the block is missing stock takes the
    /// zeroframe-only path regardless, so a port reader that never stalls
    /// passes 0 there.</param>
    /// <param name="scratchBoneToWorld">Scratch of <c>hdr.NumBones</c>
    /// matrices — stock's per-thread <c>g_MatrixPool.Alloc()</c>.</param>
    /// <param name="scratchComputed">Fresh <see cref="BoneBitList"/> —
    /// stock's stack <c>CBoneBitList boneComputed;</c>.</param>
    public static void CalcAnimation(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        MstudioSeqDesc seqdesc,
        int sequence,
        int animation,
        float cycle,
        int boneMask,
        float flStall,
        Span<BoneMatrix3x4> scratchBoneToWorld,
        BoneBitList scratchComputed)
    {
        if (pStudioHdr.IsVirtual)
        {
            CalcVirtualAnimation(
                pStudioHdr, pos, q, seqdesc, sequence, animation, cycle, boneMask,
                flStall, scratchBoneToWorld, scratchComputed);
            return;
        }

        MstudioAnimDesc animdesc = pStudioHdr.AnimDesc(animation);
        MstudioLinearBone? pLinearBones = pStudioHdr.LinearBones();

        float fFrame = cycle * (animdesc.NumFrames - 1);
        int iFrame = (int)fFrame;
        float s = fFrame - iFrame;

        int iLocalFrame = iFrame;
        MstudioAnim? panim = pStudioHdr.PAnim(animdesc, ref iLocalFrame);

        if (panim is null)
        {
            // oracle 1071-1092: pre-initialize, then zeroframe at weight 1
            int numbones = pStudioHdr.NumBones;
            for (int i = 0; i < numbones; i++)
            {
                float pweight = seqdesc.Weight(i);
                if (pweight > 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    if ((animdesc.Flags & StudioConstants.STUDIO_DELTA) != 0)
                    {
                        q[i] = Quaternion.Identity;
                        pos[i] = Vector.Zero;
                    }
                    else
                    {
                        MstudioBone pbone = pStudioHdr.Bone(i);
                        q[i] = pbone.Quaternion;
                        pos[i] = pbone.Position;
                    }
                }
            }

            CalcZeroframeData(
                pStudioHdr, pStudioHdr, null, animdesc, fFrame,
                pos, q, boneMask, 1.0f);
            return;
        }

        // oracle 1096-1135: single-pass walk. GetRenderHdr() here is the base
        // hdr itself — this arm never runs for a fold (the virtual branch
        // returned above). BUGBUG (stock comment): the
        // sequence, the anim, and the model can have different bone mappings.
        {
            MstudioAnim? it = panim;
            for (int i = 0; i < pStudioHdr.NumBones; i++)
            {
                if (it is not null && it.Bone == i)
                {
                    if (seqdesc.Weight(i) > 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                    {
                        MstudioBone pbone = pStudioHdr.Bone(i);
                        CalcBoneQuaternion(iLocalFrame, s, pbone, pLinearBones, it, out q[i]);
                        CalcBonePosition(iLocalFrame, s, pbone, pLinearBones, it, out pos[i]);
                    }

                    it = it.Next();
                }
                else if (seqdesc.Weight(i) > 0 && (pStudioHdr.BoneFlags(i) & boneMask) != 0)
                {
                    if ((animdesc.Flags & StudioConstants.STUDIO_DELTA) != 0)
                    {
                        q[i] = Quaternion.Identity;
                        pos[i] = Vector.Zero;
                    }
                    else
                    {
                        MstudioBone pbone = pStudioHdr.Bone(i);
                        q[i] = pbone.Quaternion;
                        pos[i] = pbone.Position;
                    }
                }
            }
        }

        // oracle 1138-1141: cross fade in previous zeroframe data
        if (flStall > 0.0f)
        {
            CalcZeroframeData(
                pStudioHdr, pStudioHdr, null, animdesc, fFrame,
                pos, q, boneMask, flStall);
        }

        // oracle 1143-1159: local hierarchy overrides
        if (animdesc.NumLocalHierarchy != 0)
        {
            for (int i = 0; i < animdesc.NumLocalHierarchy; i++)
            {
                MstudioLocalHierarchy? pHierarchy = animdesc.PHierarchy(i);
                if (pHierarchy is null)
                {
                    break;
                }

                // Stock reads boneFlags(iNewParent) unguarded (oracle
                // 1146-1151): for iNewParent == -1 it reads the int one slot
                // before the m_boneFlags array, which is the zero separator
                // between header vectors — effectively "masked out", so the
                // hierarchy override is skipped. -1 means "reparent to root",
                // which only the virtual arm's own -1 branch handles; the
                // non-virtual gate never passes for it. Mirror that answer
                // instead of indexing a C# int[] out of bounds.
                if (pHierarchy.INewParent != -1
                    && (pStudioHdr.BoneFlags(pHierarchy.IBone) & boneMask) != 0
                    && (pStudioHdr.BoneFlags(pHierarchy.INewParent) & boneMask) != 0)
                {
                    CalcLocalHierarchyAnimation(
                        pStudioHdr, scratchBoneToWorld, scratchComputed, pos, q,
                        pHierarchy, pHierarchy.IBone, pHierarchy.INewParent,
                        cycle, iFrame, s, boneMask);
                }
            }
        }
    }

    /// <summary>
    /// <c>CalcVirtualAnimation</c> — oracle 884-1023, reached through
    /// <see cref="CalcAnimation"/> when the hdr folds includes
    /// (<c>GetVirtualModel()</c>). Bone indexes are base-hdr globals;
    /// <c>masterBone</c> maps group-local → global, <c>boneMap</c> global →
    /// group-local. The sequence weight list is the sequence's own
    /// (group-local indexed); the walk maps through both tables.
    /// </summary>
    private static void CalcVirtualAnimation(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        MstudioSeqDesc seqdesc,
        int sequence,
        int animation,
        float cycle,
        int boneMask,
        float flStall,
        Span<BoneMatrix3x4> scratchBoneToWorld,
        BoneBitList scratchComputed)
    {
        int seqGroup = pStudioHdr.SeqGroup(sequence);
        int baseanimation = pStudioHdr.IRelativeAnim(sequence, animation);
        MstudioAnimDesc animdesc = pStudioHdr.AnimDesc(baseanimation);

        StudioHdr pSeqStudioHdr = pStudioHdr.GroupHdr(seqGroup);
        MstudioLinearBone? pSeqLinearBones = pSeqStudioHdr.LinearBones();
        int animGroup = pStudioHdr.AnimGroup(baseanimation);
        StudioHdr pAnimStudioHdr = pStudioHdr.GroupHdr(animGroup);
        MstudioLinearBone? pAnimLinearBones = pAnimStudioHdr.LinearBones();
        int[] seqBoneMap = pStudioHdr.GroupBoneMap(seqGroup);
        int[] animMasterBone = pStudioHdr.GroupMasterBone(animGroup);

        float fFrame = cycle * (animdesc.NumFrames - 1);
        int iFrame = (int)fFrame;
        float s = fFrame - iFrame;

        int iLocalFrame = iFrame;
        MstudioAnim? panim = pStudioHdr.PAnim(animdesc, ref iLocalFrame);

        // oracle 917-942: pre-initialize from the sequence group's rest pose
        int numbones = pStudioHdr.NumBones;
        for (int i = 0; i < numbones; i++)
        {
            if ((pStudioHdr.BoneFlags(i) & boneMask) != 0)
            {
                int j = seqBoneMap[i];
                if (j >= 0 && seqdesc.Weight(j) > 0.0f)
                {
                    if ((animdesc.Flags & StudioConstants.STUDIO_DELTA) != 0)
                    {
                        q[i] = Quaternion.Identity;
                        pos[i] = Vector.Zero;
                    }
                    else if (pSeqLinearBones is not null)
                    {
                        q[i] = pSeqLinearBones.Quaternion(j);
                        pos[i] = pSeqLinearBones.Position(j);
                    }
                    else
                    {
                        MstudioBone seqBone = pSeqStudioHdr.Bone(j);
                        q[i] = seqBone.Quaternion;
                        pos[i] = seqBone.Position;
                    }
                }
            }
        }

        // oracle 945-949: anim missing -> zeroframe at weight 1
        if (panim is null)
        {
            CalcZeroframeData(
                pStudioHdr, pAnimStudioHdr, animMasterBone, animdesc, fFrame,
                pos, q, boneMask, 1.0f);
            return;
        }

        // oracle 953-975: the walk, mapped through both group tables
        {
            MstudioAnim? it = panim;
            while (it is not null && it.Bone < 255)
            {
                int j = animMasterBone[it.Bone];
                if (j >= 0 && (pStudioHdr.BoneFlags(j) & boneMask) != 0)
                {
                    int k = seqBoneMap[j];
                    if (k >= 0 && seqdesc.Weight(k) > 0.0f)
                    {
                        MstudioBone animBone = pAnimStudioHdr.Bone(it.Bone);
                        CalcBoneQuaternion(iLocalFrame, s, animBone, pAnimLinearBones, it, out q[j]);
                        CalcBonePosition(iLocalFrame, s, animBone, pAnimLinearBones, it, out pos[j]);
                    }
                }

                it = it.Next();
            }
        }

        // oracle 978-981
        if (flStall > 0.0f)
        {
            CalcZeroframeData(
                pStudioHdr, pAnimStudioHdr, animMasterBone, animdesc, fFrame,
                pos, q, boneMask, flStall);
        }

        // oracle 984-1022: hierarchy with masterBone-mapped iBone/iNewParent
        if (animdesc.NumLocalHierarchy != 0)
        {
            for (int i = 0; i < animdesc.NumLocalHierarchy; i++)
            {
                MstudioLocalHierarchy? pHierarchy = animdesc.PHierarchy(i);
                if (pHierarchy is null)
                {
                    break;
                }

                int iBone = animMasterBone[pHierarchy.IBone];
                if (iBone >= 0 && (pStudioHdr.BoneFlags(iBone) & boneMask) != 0)
                {
                    if (pHierarchy.INewParent != -1)
                    {
                        int iNewParent = animMasterBone[pHierarchy.INewParent];
                        if (iNewParent >= 0 && (pStudioHdr.BoneFlags(iNewParent) & boneMask) != 0)
                        {
                            CalcLocalHierarchyAnimation(
                                pStudioHdr, scratchBoneToWorld, scratchComputed, pos, q,
                                pHierarchy, iBone, iNewParent, cycle, iFrame, s, boneMask);
                        }
                    }
                    else
                    {
                        CalcLocalHierarchyAnimation(
                            pStudioHdr, scratchBoneToWorld, scratchComputed, pos, q,
                            pHierarchy, iBone, -1, cycle, iFrame, s, boneMask);
                    }
                }
            }
        }
    }
}
