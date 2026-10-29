//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of bone_library.h's LAYER 1 (plan
// physics_bones_native_zero step 1.0): pure arithmetic over floats and
// matrices, no studio data, plus the data structs the library is fed.
//
// The rules, each a decision somebody could make differently:
//
//   Cycle      no duration -> 0 (never a NaN into bone_setup); a clock before
//              the start -> 0; a looping sequence wraps, a one-shot holds at
//              0.999 (stock's clamp).
//   ClampSequence  an index this model cannot resolve folds to 0, the
//              reference pose, never a walk off the sequence table.
//   ScaleRotation  the scale multiplies the 3x3 block and leaves the
//              translation column alone; applied once at the root it is
//              exact for every bone below.
//   ComposeAttachment  world-aligned keeps the bone-space position taken
//              through the bone and drops the orientation (identity basis).
//   ApplyMergedBones   a map entry naming a bone the source does not hold, or
//              a negative one, is skipped, never read; the walk stops at the
//              shorter of the map and the own bones.
//
// Float fidelity follows BoneSetupMath's rule: operand order is the native
// expression's, and every literal here is F-suffixed in the native source,
// so nothing promotes to double.
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// <c>BonePlacement_t</c>: where the skeleton is put, which is the whole of
/// what an entity contributes to a bone build.
/// </summary>
public struct BonePlacement
{
    /// <summary><c>vecOrigin</c>.</summary>
    public Vector Origin;

    /// <summary><c>angRotation</c>.</summary>
    public BoneAngle Angles;

    /// <summary><c>flModelScale</c>.</summary>
    public float ModelScale;

    /// <summary><c>Reset()</c>: origin and angles zero, scale 1.</summary>
    public void Reset()
    {
        Origin = Vector.Zero;
        Angles = default;
        ModelScale = 1.0f;
    }
}

/// <summary>
/// <c>BoneLayerPose_t</c>: one blended layer on top of the base sequence.
/// The order of an array of these is the blend order.
/// </summary>
public struct BoneLayerPose
{
    public int Sequence;
    public float Cycle;
    public float Weight;

    public BoneLayerPose(int sequence, float cycle, float weight)
    {
        Sequence = sequence;
        Cycle = cycle;
        Weight = weight;
    }
}

/// <summary>
/// <c>BoneBuildInputs_t</c>: everything bone_setup is fed, as plain data.
/// The native struct's pointer/count pairs are an array and a count here
/// (<see cref="Layers"/>/<see cref="LayerCount"/>), and a null
/// <see cref="PoseParameter"/> still means "the model's own defaults".
/// </summary>
public struct BoneBuildInputs
{
    /// <summary><c>placement</c>.</summary>
    public BonePlacement Placement;

    /// <summary><c>nBoneMask</c>: which bones to evaluate.</summary>
    public int BoneMask;

    /// <summary><c>nSequence</c>; clamped against the model in the build.</summary>
    public int Sequence;

    /// <summary><c>flCycle</c>.</summary>
    public float Cycle;

    /// <summary><c>flCurTime</c>: the clock for autoplay layers and IK.</summary>
    public float CurTime;

    /// <summary><c>bHasAutoplay</c>: a fact about the model
    /// (<c>BoneLibrary.ModelFacts</c>).</summary>
    public bool HasAutoplay;

    /// <summary>
    /// The sequences a transition is fading out (the client's
    /// <c>MaintainSequenceTransitions</c>, c_baseanimating.cpp 1848-1863):
    /// blended after the base sequence and before <see cref="Layers"/>, in
    /// array order, which is newest first. Null on the server, which has no
    /// transitioner.
    /// </summary>
    public BoneLayerPose[]? Previous;

    /// <summary>How many of <see cref="Previous"/> to blend.</summary>
    public int PreviousCount;

    /// <summary><c>pLayers</c>: blended after the base sequence, before autoplay.</summary>
    public BoneLayerPose[]? Layers;

    /// <summary><c>nLayers</c>.</summary>
    public int LayerCount;

    /// <summary><c>bAutoplayIkAlways</c>: the client builds an autoplay IK
    /// context unconditionally; the server only with an IK context of its
    /// own. False is the server's shape.</summary>
    public bool AutoplayIkAlways;

    /// <summary><c>nFrameCount</c>: the autoplay context's frame number.</summary>
    public int FrameCount;

    /// <summary><c>pPoseParameter</c>: MAXSTUDIOPOSEPARAM floats, or null
    /// for the model's own defaults (zero is NOT neutral).</summary>
    public float[]? PoseParameter;

    /// <summary>The <c>anim_3wayblend</c> ConVar bone_setup reads, arriving
    /// as data (port contract: no convar). Stock's default is 1.</summary>
    public bool AnimThreeWayBlend;

    /// <summary>The scratch the build rents its pose tables from (the
    /// caller's, kept between builds); null rents fresh arrays.</summary>
    public BoneScratch? Scratch;

    /// <summary><c>Reset()</c>: the server's shape, no layers, no pose
    /// parameters; <see cref="AnimThreeWayBlend"/> at the ConVar's default.</summary>
    public void Reset()
    {
        Placement.Reset();
        BoneMask = 0;
        Sequence = 0;
        Cycle = 0.0f;
        CurTime = 0.0f;
        HasAutoplay = false;
        Previous = null;
        PreviousCount = 0;
        Layers = null;
        LayerCount = 0;
        AutoplayIkAlways = false;
        FrameCount = 0;
        PoseParameter = null;
        AnimThreeWayBlend = true;
        Scratch = null;
    }
}

/// <summary>bone_library.h LAYER 1: pure arithmetic, no studio data.</summary>
public static class BoneLibraryMath
{
    /// <summary>
    /// <c>BoneLibrary_PoseSource</c>: the networked pose, except for the local
    /// viewmodel pass, which is placed at its render pose. The scale is not
    /// touched.
    /// </summary>
    public static void PoseSource(bool viewModelPass,
                                  Vector netOrigin, BoneAngle netAngles,
                                  Vector renderOrigin, BoneAngle renderAngles,
                                  ref BonePlacement placement)
    {
        placement.Origin = viewModelPass ? renderOrigin : netOrigin;
        placement.Angles = viewModelPass ? renderAngles : netAngles;
    }

    /// <summary>
    /// <c>BoneLibrary_Cycle</c>: where in a sequence a clock has got to, from
    /// <paramref name="startCycle"/> at <paramref name="start"/>. A clock not
    /// yet started stands at its start cycle; a negative rate plays backwards
    /// (CDynamicProp's reversed transitions). A looping sequence keeps the
    /// fraction; a one-shot holds at 0 or its last frame, 0.999.
    /// </summary>
    public static float Cycle(float now, float start, float rate, float duration, bool looping, float startCycle = 0.0f)
    {
        if (duration <= 0.0f)
        {
            return 0.0f;
        }

        float elapsed = now - start;
        float cycle = startCycle + (elapsed > 0.0f ? elapsed : 0.0f) * rate / duration;

        if (looping)
        {
            return cycle - MathF.Floor(cycle);
        }

        if (cycle < 0.0f)
        {
            return 0.0f;
        }

        // MIN( flCycle, 0.999f ): the macro's (a) < (b) ? (a) : (b).
        return cycle < 0.999f ? cycle : 0.999f;
    }

    /// <summary><c>BoneLibrary_ClampSequence</c>: the sequence this model can be asked about.</summary>
    public static int ClampSequence(int asked, int numSeq)
    {
        if (asked > 0 && asked < numSeq)
        {
            return asked;
        }

        return 0;
    }

    /// <summary><c>BoneLibrary_ScaleRotation</c>: the 3x3 block times the
    /// scale, the translation column untouched.</summary>
    public static void ScaleRotation(ref BoneMatrix3x4 m, float scale)
    {
        if (scale == 1.0f)
        {
            return;
        }

        // VectorScale( m[r], s, m[r] ) on each row: the row's first three
        // floats, never its translation.
        m.Row0.X *= scale;
        m.Row0.Y *= scale;
        m.Row0.Z *= scale;
        m.Row1.X *= scale;
        m.Row1.Y *= scale;
        m.Row1.Z *= scale;
        m.Row2.X *= scale;
        m.Row2.Y *= scale;
        m.Row2.Z *= scale;
    }

    /// <summary><c>BoneLibrary_PlacementToWorld</c>: the transform a root
    /// bone concatenates onto.</summary>
    public static BoneMatrix3x4 PlacementToWorld(in BonePlacement placement)
    {
        BoneMatrix3x4 m = BoneSetupMath.AngleMatrix(placement.Angles, placement.Origin);
        ScaleRotation(ref m, placement.ModelScale);
        return m;
    }

    /// <summary><c>BoneLibrary_ComposeAttachment</c>: one attachment's world
    /// transform from the bone it hangs off.</summary>
    public static BoneMatrix3x4 ComposeAttachment(in BoneMatrix3x4 boneToWorld,
                                                  in BoneMatrix3x4 attachmentLocal,
                                                  bool worldAlign)
    {
        if (!worldAlign)
        {
            return BoneSetupMath.ConcatTransforms(boneToWorld, attachmentLocal);
        }

        Vector worldBonePos = BoneSetupMath.VectorTransform(attachmentLocal.Position, boneToWorld);
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = worldBonePos;
        return m;
    }

    /// <summary>
    /// The bones a merge did not name, under the parents it moved:
    /// <c>C_BaseAnimating::BuildTransformations</c>' order for a bone-merged
    /// entity, which copies the matching bones from the source first and then
    /// builds every other bone under its parent, so a child the source does
    /// not name (a w_ model's weapon bone, under the hand) follows it. Each
    /// such bone keeps the transform the own build gave it under its parent
    /// (<paramref name="own"/>, the build before the merge), under that parent
    /// as merged (<paramref name="bones"/>), in bone order, so a chain follows
    /// whole. A root bone keeps its own place.
    /// </summary>
    public static void ReparentUnmergedBones(StudioHdr hdr, ReadOnlySpan<short> map,
                                             ReadOnlySpan<BoneMatrix3x4> own, Span<BoneMatrix3x4> bones)
    {
        int count = Math.Min(Math.Min(hdr.NumBones, map.Length), Math.Min(own.Length, bones.Length));

        for (int i = 0; i < count; i++)
        {
            int parent = hdr.BoneParent(i);

            if (map[i] >= 0 || parent < 0 || parent >= i)
            {
                continue;
            }

            BoneMatrix3x4 local = BoneSetupMath.ConcatTransforms(BoneSetupMath.MatrixInvert(own[parent]), own[i]);
            bones[i] = BoneSetupMath.ConcatTransforms(bones[parent], local);
        }
    }

    /// <summary>
    /// <c>BoneLibrary_ApplyMergedBones</c>: the source's matrices over the
    /// mapped bones. The spans' lengths are the native counts; an empty span
    /// is the native null and copies nothing. Answers how many bones were
    /// overwritten.
    /// </summary>
    public static int ApplyMergedBones(ReadOnlySpan<short> map,
                                       ReadOnlySpan<BoneMatrix3x4> sourceBones,
                                       Span<BoneMatrix3x4> ownBones)
    {
        if (map.IsEmpty || sourceBones.IsEmpty || ownBones.IsEmpty)
        {
            return 0;
        }

        int bones = map.Length < ownBones.Length ? map.Length : ownBones.Length;
        int copied = 0;

        for (int i = 0; i < bones; i++)
        {
            int sourceBone = map[i];

            if (sourceBone < 0 || sourceBone >= sourceBones.Length)
            {
                continue;
            }

            ownBones[i] = sourceBones[sourceBone];
            copied++;
        }

        return copied;
    }
}
