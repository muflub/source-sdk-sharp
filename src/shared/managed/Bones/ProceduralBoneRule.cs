//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of src/shared/native/procedural_bone_rule.h (plan
// physics_bones_native_zero step 1.0): which bones the pose does NOT decide.
//
// A bone flagged BONE_ALWAYS_PROCEDURAL is one of two different things, and
// the flag alone cannot tell them apart:
//
//   proctype AXISINTERP / QUATINTERP / AIMATBONE / AIMATATTACH: CalcProceduralBone
//     writes the bone and answers TRUE (Computed);
//   proctype JIGGLE: CalcProceduralBone writes NOTHING and answers FALSE; the
//     caller builds a goal matrix and a spring kept across frames writes it
//     (Jiggle).
//
// A proctype the engine does not implement answers None: CalcProceduralBone's
// default arm returns false for it, so drawing it from the pose is a pose
// rather than a hole.
//
// The three model-level predicates are stock's conditions: a model with
// autoplay layers, jiggle bones or IK chains needs the clock every frame (a
// spring and a latch carry state the cache key cannot see); a model wants an
// IK context only with chains and only unscaled (the solver marks bones as
// computed, and a scale applied afterwards would skip them); a context
// solves only on a living model, never a ragdoll.
//=============================================================================//

namespace SourceSharp.Shared.Bones;

/// <summary><c>ProceduralBoneKind_t</c>: who writes this bone's matrix.</summary>
public enum ProceduralBoneKind
{
    /// <summary>bone_setup's pose, concatenated onto the parent.</summary>
    None = 0,

    /// <summary><c>CalcProceduralBone</c> writes it and answers true.</summary>
    Computed,

    /// <summary>The jiggle spring writes it from a caller-built goal.</summary>
    Jiggle,
}

/// <summary>The procedural-bone predicates of procedural_bone_rule.h.</summary>
public static class ProceduralBoneRule
{
    /// <summary>studio.h <c>BONE_ALWAYS_PROCEDURAL</c>.</summary>
    public const int BoneAlwaysProcedural = 0x04;

    /// <summary>studio.h <c>STUDIO_PROC_AXISINTERP</c>.</summary>
    public const int ProcAxisInterp = 1;

    /// <summary>studio.h <c>STUDIO_PROC_QUATINTERP</c>.</summary>
    public const int ProcQuatInterp = 2;

    /// <summary>studio.h <c>STUDIO_PROC_AIMATBONE</c>.</summary>
    public const int ProcAimAtBone = 3;

    /// <summary>studio.h <c>STUDIO_PROC_AIMATATTACH</c>.</summary>
    public const int ProcAimAtAttach = 4;

    /// <summary>studio.h <c>STUDIO_PROC_JIGGLE</c>.</summary>
    public const int ProcJiggle = 5;

    /// <summary><c>ProceduralBone_Kind</c>: which kind a bone is, from its
    /// flags and its proctype.</summary>
    public static ProceduralBoneKind Kind(int boneFlags, int procType)
    {
        if ((boneFlags & BoneAlwaysProcedural) == 0)
        {
            return ProceduralBoneKind.None;
        }

        return procType switch
        {
            ProcAxisInterp or ProcQuatInterp or ProcAimAtBone or ProcAimAtAttach => ProceduralBoneKind.Computed,
            ProcJiggle => ProceduralBoneKind.Jiggle,
            _ => ProceduralBoneKind.None,
        };
    }

    /// <summary><c>ProceduralBone_NeedsRealTime</c>: whether a model must be
    /// re-posed every frame whatever else is true of it.</summary>
    public static bool NeedsRealTime(bool modelHasAutoplay, bool modelHasJiggleBones, bool modelHasIkChains)
    {
        return modelHasAutoplay || modelHasJiggleBones || modelHasIkChains;
    }

    /// <summary><c>ProceduralBone_WantsIk</c>: whether this model wants an IK
    /// context at all (chains, and not scaled).</summary>
    public static bool WantsIk(int ikChains, bool modelScaled)
    {
        return ikChains > 0 && !modelScaled;
    }

    /// <summary><c>ProceduralBone_SolvesIk</c>: whether a built context
    /// solves this frame (not on a ragdoll).</summary>
    public static bool SolvesIk(bool hasContext, bool ragdoll)
    {
        return hasContext && !ragdoll;
    }
}
