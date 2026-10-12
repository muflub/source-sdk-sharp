//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of bone_library.cpp's LAYER 2 (plan
// physics_bones_native_zero step 1.0): the studio walking that turns plain
// inputs into matrices, over the managed bone_setup port (CBoneSetup,
// CIKContext, BoneSetupBlend.StudioBuildMatrices, BoneSetupTrace.TraceToStudio).
// No entity appears in any signature, no clock is read, no cache is kept:
// the caller owns its storage and its staleness rule (BoneCacheKey).
//
// What differs from the native shape, and why:
//
//   * A native pointer/count pair is a span here; an EMPTY span is the
//     native NULL (both answer "nothing to read/write").
//   * CStudioHdr::IsValid() is "constructed": a StudioHdr exists only after
//     TryCreate validated and folded it, so the native IsValid() arm is the
//     null check. SequencesAvailable() is likewise always true -- the fold
//     resolves every include at construction or the hdr is refused.
//   * pHitboxSet(i) in stock asserts its index and otherwise reads past the
//     table; here an index outside [0, numhitboxsets) answers "no set".
//   * The anim_3wayblend ConVar bone_setup reads arrives as
//     BoneBuildInputs.AnimThreeWayBlend; the surface-props interface the trace
//     names materials from arrives as a SurfacePropReader.
//   * pos/q shorter than the model's bone count are refused (false) rather
//     than written past.
//
// AttachmentLocalSpace (bone_library.cpp 390-405) is not ported: it has no
// caller in either build.
//=============================================================================//

using System;
using System.Diagnostics.CodeAnalysis;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>bone_library.cpp layer 2: the bone build over a <see cref="StudioHdr"/>.</summary>
public static partial class BoneLibrary
{
    /// <summary>studio.h <c>MAXSTUDIOPOSEPARAM</c>.</summary>
    public const int MaxStudioPoseParam = 24;

    /// <summary>studio.h <c>STUDIOHDR_FLAGS_STATIC_PROP</c>.</summary>
    public const int StudioHdrFlagsStaticProp = 0x00000010;

    /// <summary>studio.h <c>ATTACHMENT_FLAG_WORLD_ALIGN</c>.</summary>
    public const uint AttachmentFlagWorldAlign = 0x10000;

    /// <summary>
    /// <c>BoneLibrary_ModelFacts</c>: the per-model facts, read once per model.
    /// Writes its outputs before anything can fail (no autoplay, zeroed
    /// defaults). <paramref name="poseDefaults"/> may be null; when given it
    /// must hold <see cref="MaxStudioPoseParam"/> floats.
    /// </summary>
    public static bool ModelFacts(StudioHdr? hdr, out bool hasAutoplay, float[]? poseDefaults)
    {
        // Written before anything can fail, so a caller that ignores the
        // answer reads a model with no autoplay and zeroed parameters.
        hasAutoplay = false;

        if (poseDefaults is not null)
        {
            Array.Clear(poseDefaults, 0, MaxStudioPoseParam);
        }

        if (hdr is null)
        {
            return false;
        }

        hasAutoplay = hdr.CountAutoplaySequences() > 0;

        if (poseDefaults is not null)
        {
            BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, poseDefaults, MaxStudioPoseParam);
        }

        return true;
    }

    /// <summary><c>BoneLibrary_IsPoseable</c>: not a static prop, and has sequences.</summary>
    public static bool IsPoseable(StudioHdr? hdr)
    {
        if (hdr is null)
        {
            return false;
        }

        // SequencesAvailable() is true for every constructed hdr (file header).
        return (hdr.Flags & StudioHdrFlagsStaticProp) == 0 && hdr.NumSequences > 0;
    }

    /// <summary>
    /// <c>BoneLibrary_AccumulatePose</c>: the base sequence, then the layers
    /// in array order, then the autoplay layers on the real-time clock with a
    /// LOCAL IK context when the caller has one or asks for one.
    /// </summary>
    public static bool AccumulatePose(StudioHdr? hdr, in BoneBuildInputs inputs,
                                      Vector[]? pos, Quaternion[]? q, CIKContext? ik = null)
    {
        if (hdr is null || pos is null || q is null)
        {
            return false;
        }

        if (!IsPoseable(hdr))
        {
            return false;
        }

        if (pos.Length < hdr.NumBones || q.Length < hdr.NumBones)
        {
            return false;
        }

        // The model's own defaults when the caller has none: zero is the
        // START of each parameter's range, not its middle.
        float[]? pose = inputs.PoseParameter;

        if (pose is null)
        {
            pose = new float[MaxStudioPoseParam];
            BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, MaxStudioPoseParam);
        }

        int sequence = BoneLibraryMath.ClampSequence(inputs.Sequence, hdr.NumSequences);
        bool threeWay = inputs.AnimThreeWayBlend;

        CBoneSetup boneSetup = new(hdr, inputs.BoneMask, pose, inputs.Scratch);
        boneSetup.InitPose(pos, q);
        boneSetup.AccumulatePose(pos, q, sequence, inputs.Cycle, 1.0f, inputs.CurTime, ik, threeWay);

        // The sequences a transition is fading out, newest first, AFTER the
        // base and BEFORE the layers: StandardBlendingRules runs
        // MaintainSequenceTransitions between the two (c_baseanimating.cpp
        // 1988-1992). Clamped against this model like the layers.
        BoneLayerPose[]? previous = inputs.Previous;

        for (int i = 0; i < inputs.PreviousCount && previous is not null && i < previous.Length; i++)
        {
            BoneLayerPose blend = previous[i];

            boneSetup.AccumulatePose(pos, q,
                                     BoneLibraryMath.ClampSequence(blend.Sequence, hdr.NumSequences),
                                     blend.Cycle, blend.Weight, inputs.CurTime, ik, threeWay);
        }

        // The layers, in array order, BEFORE the autoplay pass, on the
        // caller's own IK context; each sequence clamped against this model
        // (a model swap can land a frame before the new layer does).
        BoneLayerPose[]? layers = inputs.Layers;

        for (int i = 0; i < inputs.LayerCount && layers is not null && i < layers.Length; i++)
        {
            BoneLayerPose layer = layers[i];

            boneSetup.AccumulatePose(pos, q,
                                     BoneLibraryMath.ClampSequence(layer.Sequence, hdr.NumSequences),
                                     layer.Cycle, layer.Weight, inputs.CurTime, ik, threeWay);
        }

        // The autoplay layers, under a SEPARATE local context: handed the
        // caller's, their rules mix with the playing sequence's. Whether there
        // is a context at all when the caller has none is the caller's answer
        // (AutoplayIkAlways: the client's shape builds one unconditionally).
        if (inputs.HasAutoplay)
        {
            if (ik is not null || inputs.AutoplayIkAlways)
            {
                BoneScratch.Mark mark = inputs.Scratch?.Save() ?? default;

                try
                {
                    CIKContext autoIk = BoneScratch.IkContext(inputs.Scratch);
                    autoIk.Init(hdr,
                                BoneSetupMath.AngleMatrix(inputs.Placement.Angles, inputs.Placement.Origin),
                                inputs.BoneMask, inputs.CurTime, inputs.FrameCount);
                    boneSetup.CalcAutoplaySequences(pos, q, inputs.CurTime, autoIk, threeWay);
                }
                finally
                {
                    inputs.Scratch?.Restore(mark);
                }
            }
            else
            {
                boneSetup.CalcAutoplaySequences(pos, q, inputs.CurTime, null, threeWay);
            }
        }

        return true;
    }

    /// <summary>
    /// <c>BoneLibrary_PoseToWorld</c>: Studio_BuildMatrices over the whole
    /// skeleton with the placement's scale. The span's length is the native
    /// nMaxBones. Answers the model's bone count, or 0 when refused.
    /// </summary>
    public static int PoseToWorld(StudioHdr? hdr, in BonePlacement placement,
                                  Vector[]? pos, Quaternion[]? q, int boneMask,
                                  Span<BoneMatrix3x4> boneToWorld)
    {
        if (hdr is null || pos is null || q is null || boneToWorld.IsEmpty)
        {
            return 0;
        }

        int bones = hdr.NumBones;

        if (bones <= 0 || bones > boneToWorld.Length || bones > StudioConstants.MAXSTUDIOBONES)
        {
            return 0;
        }

        // The scale is Studio_BuildMatrices' third fact about the entity: the
        // attachments hang off these matrices.
        BoneSetupBlend.StudioBuildMatrices(hdr, placement.Angles, placement.Origin,
                                           pos, q, -1, placement.ModelScale,
                                           boneToWorld, boneMask);

        return bones;
    }

    /// <summary><c>BoneLibrary_BoneIsWritten</c>: whether a build writes this
    /// bone (every bone of a model it does not pose; else the mask's).</summary>
    public static bool BoneIsWritten(StudioHdr? hdr, int bone, int boneMask)
    {
        if (hdr is null)
        {
            return false;
        }

        if (bone < 0 || bone >= hdr.NumBones)
        {
            return false;
        }

        // The static-prop branch writes every bone, mask or no mask.
        if (!IsPoseable(hdr))
        {
            return true;
        }

        return (hdr.BoneFlags(bone) & boneMask) != 0;
    }

    /// <summary>
    /// <c>BoneLibrary_BuildBones</c>: bone-to-world for every bone the mask
    /// names; the static-prop branch writes every bone with the scaled
    /// placement. Answers the model's bone count (NOT the number written), or
    /// 0 when refused.
    /// </summary>
    public static int BuildBones(StudioHdr? hdr, in BoneBuildInputs inputs,
                                 Span<BoneMatrix3x4> boneToWorld, CIKContext? ik = null)
    {
        if (hdr is null || boneToWorld.IsEmpty)
        {
            return 0;
        }

        int bones = hdr.NumBones;

        // Refused rather than truncated: a half-built skeleton is a model with
        // its legs at the world origin.
        if (bones <= 0 || bones > boneToWorld.Length || bones > StudioConstants.MAXSTUDIOBONES)
        {
            return 0;
        }

        if (!IsPoseable(hdr))
        {
            // The static-prop branch: bone 0 IS the entity transform, and it
            // is scaled here by hand (Studio_BuildMatrices scales the other
            // branch).
            BoneMatrix3x4 scaled = BoneLibraryMath.PlacementToWorld(inputs.Placement);

            for (int i = 0; i < bones; i++)
            {
                boneToWorld[i] = scaled;
            }

            return bones;
        }

        BoneScratch? scratch = inputs.Scratch;
        BoneScratch.Mark mark = scratch?.Save() ?? default;

        try
        {
            Vector[] pos = BoneScratch.Vectors(scratch);
            Quaternion[] q = BoneScratch.Quaternions(scratch);

            if (!AccumulatePose(hdr, inputs, pos, q, ik))
            {
                return 0;
            }

            return PoseToWorld(hdr, inputs.Placement, pos, q, inputs.BoneMask, boneToWorld);
        }
        finally
        {
            scratch?.Restore(mark);
        }
    }

    /// <summary>
    /// <c>BoneLibrary_BuildAttachments</c>: attachment-to-world, index 0
    /// holding attachment 1. An attachment whose bone this build did not
    /// evaluate collapses onto the UNSCALED entity transform. The bones
    /// span's length is the native nBones.
    /// </summary>
    public static int BuildAttachments(StudioHdr? hdr, ReadOnlySpan<BoneMatrix3x4> bones,
                                       in BonePlacement placement,
                                       Span<BoneMatrix3x4> attachments)
    {
        if (hdr is null || attachments.IsEmpty)
        {
            return 0;
        }

        int count = hdr.NumGlobalAttachments;

        if (count <= 0 || count > attachments.Length)
        {
            return 0;
        }

        // UNSCALED: every attachment that is not this fallback is already
        // scaled by the bone it hangs off.
        BoneMatrix3x4 entityToWorld = BoneSetupMath.AngleMatrix(placement.Angles, placement.Origin);

        for (int i = 0; i < count; i++)
        {
            MstudioAttachment attachment = hdr.Attachment(i);
            int bone = hdr.GetAttachmentBone(i);

            // A bone this build did not evaluate, or one out of range.
            if (bones.IsEmpty || bone < 0 || bone >= bones.Length)
            {
                attachments[i] = entityToWorld;
                continue;
            }

            attachments[i] = BoneLibraryMath.ComposeAttachment(
                bones[bone], attachment.Local, (attachment.Flags & AttachmentFlagWorldAlign) != 0);
        }

        return count;
    }

    /// <summary><c>BoneLibrary_SequenceFacts</c>: a sequence's duration at the
    /// given pose (null: the model's defaults) and whether it loops.</summary>
    public static bool SequenceFacts(StudioHdr? hdr, int sequence, float[]? poseParameter,
                                     out float duration, out bool looping)
    {
        duration = 0.0f;
        looping = false;

        if (hdr is null)
        {
            return false;
        }

        if (sequence < 0 || sequence >= hdr.NumSequences)
        {
            return false;
        }

        // The model's own defaults when the caller has none; a zeroed array
        // reads every blended duration at one end of its range.
        if (poseParameter is null)
        {
            poseParameter = new float[MaxStudioPoseParam];
            BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, poseParameter, MaxStudioPoseParam);
        }

        duration = BoneSetupHdrRead.Duration(hdr, sequence, poseParameter);
        looping = (hdr.SeqDesc(sequence).Flags & StudioConstants.STUDIO_LOOPING) != 0;
        return true;
    }

    /// <summary><c>BoneLibrary_BoneCount</c>.</summary>
    public static int BoneCount(StudioHdr? hdr) => hdr is null ? 0 : hdr.NumBones;

    /// <summary><c>BoneLibrary_AttachmentCount</c>.</summary>
    public static int AttachmentCount(StudioHdr? hdr) => hdr is null ? 0 : hdr.NumGlobalAttachments;

    /// <summary><c>BoneLibrary_LookupAttachment</c>: ONE-based; 0 is "none".</summary>
    public static int LookupAttachment(StudioHdr? hdr, string? name)
    {
        if (hdr is null || string.IsNullOrEmpty(name))
        {
            return 0;
        }

        // -1 + 1 is 0, the engine's own "no attachment".
        return BoneSetupHdrRead.FindAttachment(hdr, name) + 1;
    }

    /// <summary><c>BoneLibrary_AttachmentName</c>: a one-based attachment's
    /// name; empty and false when there is none. (The native buffer and its
    /// truncation are the caller's concern on the far side of the ABI.)</summary>
    public static bool AttachmentName(StudioHdr? hdr, int oneBasedAttachment, out string name)
    {
        name = string.Empty;

        if (hdr is null)
        {
            return false;
        }

        if (oneBasedAttachment < 1 || oneBasedAttachment > hdr.NumGlobalAttachments)
        {
            return false;
        }

        name = hdr.Attachment(oneBasedAttachment - 1).Name;
        return true;
    }

    /// <summary>
    /// <c>BoneLibrary_HitboxCentre</c>: one hitbox's centre in the world and
    /// the bone (and its contents) it hangs off. The bone matrix already
    /// carries the scale; it is NOT applied again.
    /// </summary>
    public static bool HitboxCentre(StudioHdr? hdr, int hitboxSet, int hitbox,
                                    ReadOnlySpan<BoneMatrix3x4> bones, int boneMask,
                                    out Vector centre, out int bone, out int contents)
    {
        centre = Vector.Zero;
        bone = -1;
        contents = 0;

        if (hdr is null || bones.IsEmpty)
        {
            return false;
        }

        if (!TryHitboxSet(hdr, hitboxSet, out MstudioHitboxSet? set))
        {
            return false;
        }

        if (hitbox < 0 || hitbox >= set.NumHitboxes)
        {
            return false;
        }

        MstudioBBox box = set.Hitbox(hitbox);

        if (box.Bone < 0 || box.Bone >= bones.Length)
        {
            return false;
        }

        if (!BoneIsWritten(hdr, box.Bone, boneMask))
        {
            return false;
        }

        // NO SCALE: the bone matrix already carries it.
        Vector local = (box.BbMin + box.BbMax) * 0.5f;

        centre = BoneSetupMath.VectorTransform(local, bones[box.Bone]);
        bone = box.Bone;

        // The contents of the bone the box hangs off: what TraceToStudio
        // filters each box on.
        contents = hdr.Bone(box.Bone).Contents;
        return true;
    }

    /// <summary>
    /// <c>BoneLibrary_TraceHitboxes</c>: a ray against the posed hitboxes.
    /// True when the model had a hitbox set to test (NOT "the ray hit": that
    /// is the trace's fraction). Bones the build did not supply test against
    /// bone 0.
    /// </summary>
    public static bool TraceHitboxes(SurfacePropReader? surfaceProps, StudioHdr? hdr, int hitboxSet,
                                     in BoneRay ray, int contentsMask,
                                     ReadOnlySpan<BoneMatrix3x4> bones,
                                     Vector origin, float scale, BoneTrace tr)
    {
        if (hdr is null || bones.IsEmpty)
        {
            return false;
        }

        // No hitboxes is a FALSE and not a failure: the caller falls back to
        // the bounding box.
        if (!TryHitboxSet(hdr, hitboxSet, out MstudioHitboxSet? set) || set.NumHitboxes == 0)
        {
            return false;
        }

        // TraceToStudio reads one matrix per model bone. Bones this build did
        // not supply test against bone 0, the entity's own transform: the
        // choice was a wrong answer or a crash in a trace.
        int numBones = Math.Min(hdr.NumBones, StudioConstants.MAXSTUDIOBONES);
        int walk = Math.Min(bones.Length, numBones);
        BoneMatrix3x4[] hitboxBones = new BoneMatrix3x4[numBones];

        for (int i = 0; i < walk; i++)
        {
            hitboxBones[i] = bones[i];
        }

        for (int i = walk; i < numBones; i++)
        {
            hitboxBones[i] = bones[0];
        }

        // Stock dereferences the surface-props interface unconditionally; a
        // caller with none reads index 0, the trace's own initial value.
        SurfacePropReader reader = surfaceProps ?? NoSurfaceProps;

        if (!BoneSetupTrace.TraceToStudio(hdr, set, hitboxBones, ray, contentsMask, origin, scale, tr, reader))
        {
            return true;
        }

        // The material the hit landed on, named from the BONE the hitbox
        // hangs off.
        MstudioBBox box = set.Hitbox(tr.HitBox);

        if (box.Bone < 0 || box.Bone >= hdr.NumBones)
        {
            return true;
        }

        tr.SurfaceName = BoneSetupTrace.StudioSurfaceName;
        tr.SurfaceFlags = BoneSetupTrace.SurfHitbox;

        if (surfaceProps is not null)
        {
            tr.SurfaceProps = surfaceProps(hdr, box.Bone);
        }

        return true;
    }

    /// <summary><c>pHitboxSet(i)</c>, refusing an index outside the table
    /// (stock asserts it and otherwise reads past the end).</summary>
    private static bool TryHitboxSet(StudioHdr hdr, int index,
                                     [NotNullWhen(true)] out MstudioHitboxSet? set)
    {
        set = index >= 0 && index < hdr.NumHitboxSets ? hdr.HitboxSet(index) : null;
        return set is not null;
    }

    private static int NoSurfaceProps(StudioHdr hdr, int boneIndex) => 0;
}
