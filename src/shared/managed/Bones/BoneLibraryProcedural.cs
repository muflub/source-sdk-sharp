//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of bone_library.cpp's LAYER 3, the client's half (plan
// physics_bones_native_zero step 1.0): C_BaseAnimating::BuildTransformations
// rather than Studio_BuildMatrices, and CBoneMergeCache::UpdateCache's map.
//
// PoseToWorldProcedural walks the bones in order, as Studio_BuildMatrices
// does, but honours the three kinds of bone the pose does not decide:
//
//   * a COMPUTED procedural bone (ProceduralBoneRule) is written by
//     CalcProceduralBone from the bones around it -- and only when it answers
//     true; otherwise it falls through to the pose;
//   * a bone the caller has already placed (an IK chain's, a ragdoll's: the
//     BoneBitList) is not written again;
//   * a JIGGLE bone is a spring: the goal the pose would give it goes through
//     the caller's JiggleBones (kept across frames), or is drawn as-is when
//     the caller has no spring state or the bone carries no record.
//
// Bones outside the mask are skipped and their slots left as the caller left
// them. The scale is applied once, on the root's placement. The
// cl_jiggle_bone_framerate_cutoff ConVar arrives as a parameter.
//
// Compiled into both sides like the rest of Shared; only the client calls it.
//=============================================================================//

using System;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

public static partial class BoneLibrary
{
    /// <summary>studio.h <c>BONE_USED_BY_ANYTHING</c>.</summary>
    public const int BoneUsedByAnything = 0x0007FF00;

    /// <summary>
    /// <c>BoneLibrary_PoseToWorldProcedural</c>: the pose into world matrices,
    /// honouring procedural, already-placed and jiggle bones. The array's
    /// length is the native nMaxBones. Answers the model's bone count (NOT
    /// the number written), or 0 when refused.
    /// </summary>
    public static int PoseToWorldProcedural(StudioHdr? hdr, in BonePlacement placement,
                                            Vector[]? pos, Quaternion[]? q,
                                            int boneMask, float curTime,
                                            BoneSetupExtract.BoneBitList? boneComputed,
                                            JiggleBones? jiggleBones,
                                            BoneMatrix3x4[]? boneToWorld,
                                            float framerateCutoff = JiggleBones.DefaultFramerateCutoff)
    {
        if (hdr is null || pos is null || q is null || boneToWorld is null)
        {
            return 0;
        }

        int bones = hdr.NumBones;

        // Refused rather than truncated, for BuildBones' reason.
        if (bones <= 0 || bones > boneToWorld.Length || bones > StudioConstants.MAXSTUDIOBONES
            || bones > pos.Length || bones > q.Length)
        {
            return 0;
        }

        // Where the model is, at the size it is worn: the one matrix the
        // scale belongs on.
        BoneMatrix3x4 entityToWorld = BoneLibraryMath.PlacementToWorld(placement);

        // CalcProceduralBone and the springs read through an accessor over
        // the caller's array, everything readable and writable: the mask
        // discipline is the loop below.
        CBoneAccessor accessor = new(boneToWorld);
        accessor.SetReadableBones(BoneUsedByAnything);
        accessor.SetWritableBones(BoneUsedByAnything);

        for (int i = 0; i < bones; i++)
        {
            // Bones outside the mask are not evaluated.
            if ((hdr.BoneFlags(i) & boneMask) == 0)
            {
                continue;
            }

            ProceduralBoneKind kind = ProceduralBoneRule.Kind(hdr.BoneFlags(i), hdr.Bone(i).ProcType);

            // The four kinds CalcProceduralBone writes, in bone order, after
            // the bones it reads.
            if (kind == ProceduralBoneKind.Computed && BoneSetupProcedural.CalcProceduralBone(hdr, i, accessor))
            {
                continue;
            }

            // A bone the caller has already placed: an IK chain's, a ragdoll's.
            if (boneComputed is not null && boneComputed.IsBoneMarked(i))
            {
                continue;
            }

            BoneMatrix3x4 boneMatrix = BoneSetupMath.QuaternionMatrix(q[i], pos[i]);

            // Where this bone would sit if the animation were the whole story.
            int parent = hdr.BoneParent(i);
            BoneMatrix3x4 goal = parent == -1
                ? BoneSetupMath.ConcatTransforms(entityToWorld, boneMatrix)
                : BoneSetupMath.ConcatTransforms(accessor.GetBone(parent), boneMatrix);

            if (kind != ProceduralBoneKind.Jiggle)
            {
                accessor.GetBoneForWrite(i) = goal;
                continue;
            }

            // The spring: the bone lags behind its goal and swings past it, on
            // real time. A bone with no record, or a caller with no spring
            // state, is drawn from the pose rather than left unwritten.
            if (jiggleBones is null || !JiggleBoneParams.TryRead(hdr, i, out JiggleBoneParams jiggle))
            {
                accessor.GetBoneForWrite(i) = goal;
                continue;
            }

            jiggleBones.BuildJiggleTransformations(i, curTime, jiggle, goal,
                                                   ref accessor.GetBoneForWrite(i), framerateCutoff,
                                                   coordSystemIsFlipped: false);
        }

        return bones;
    }

    /// <summary>
    /// <c>BoneLibrary_BuildMergeMap</c>: for each of <paramref name="own"/>'s
    /// bones, the <paramref name="source"/> bone sharing its name, or -1 (every
    /// entry -1 when there is no source). The span's length is the native
    /// nMaxBones. Answers how many matched.
    /// </summary>
    public static int BuildMergeMap(StudioHdr? own, StudioHdr? source, Span<short> map)
    {
        if (own is null || map.IsEmpty)
        {
            return 0;
        }

        int bones = own.NumBones;

        if (bones <= 0 || bones > map.Length)
        {
            return 0;
        }

        int matched = 0;

        for (int i = 0; i < bones; i++)
        {
            int sourceBone = source is not null ? BoneSetupQuery.BoneIndexByName(source, own.Bone(i).Name) : -1;

            map[i] = (short)sourceBone;

            if (sourceBone >= 0)
            {
                matched++;
            }
        }

        return matched;
    }
}
