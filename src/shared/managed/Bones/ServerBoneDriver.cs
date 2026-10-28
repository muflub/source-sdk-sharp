//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of CStandaloneEntity's skeleton
// (src/server/native/shell_standalone_hierarchy.cpp; plan
// physics_bones_native_zero step 1.0b): RefreshModelPoseState, EnsureBones,
// EnsureAttachments, GetAttachment, BoneCount, SetPoseSequence, PoseCycle and
// TestHitboxes. Stock's CBaseAnimating::SetupBones / GetAttachment /
// TestHitboxes shape over the 1.0 BoneLibrary.
//
// Where it differs from the client's driver, each on purpose:
//
//   * The studio header is opened FIRST: a model with none refuses before the
//     mask widens.
//   * No pose generation (nothing on this side moves a pose parameter) and
//     no layers, IK, springs, ragdolls or merges: the whole build is
//     BoneLibrary.BuildBones, the server's autoplay shape (an autoplay IK
//     context only with one of its own: never, here).
//   * A refused build CLEARS the cache, so the attachment reads see "no
//     skeleton" rather than a half-written buffer.
//   * Attachments are placed at scale 1: the bones carry the scale, and the
//     placement is only the fallback for an unevaluated bone.
//   * SetPoseSequence reads its duration at the model's DEFAULT pose and
//     invalidates the cache; the client reads it at its own pose and does not.
//
// The clock (gpGlobals->curtime) and anim_3wayblend arrive as arguments; the
// entity's memory is its BoneEntityState. The driver keeps no state.
//=============================================================================//

using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>CStandaloneEntity's skeleton decisions over the 1.0 BoneLibrary.</summary>
public sealed class ServerBoneDriver
{
    private readonly IBoneModels _models;

    // The builds' pose tables, reused (BoneScratch): scratch, not memory --
    // nothing in it outlives the build that rented it.
    private readonly BoneScratch _scratch = new();

    public ServerBoneDriver(IBoneModels models)
    {
        _models = models;
    }

    /// <summary>
    /// <c>StandaloneServer_RefreshModelPoseState</c>: whether the model
    /// animates itself and its pose defaults, once per model. Recorded first
    /// and unconditionally; a model with no studio data keeps the pose it had.
    /// </summary>
    public void RefreshModelPoseState(BoneEntityState s, int modelIndex)
    {
        if (s.PoseDefaultsFor == modelIndex)
        {
            return;
        }

        s.PoseDefaultsFor = modelIndex;
        s.ModelHasAutoplay = false;

        StudioHdr? hdr = _models.OpenStudioHdr(modelIndex);

        if (hdr is null)
        {
            return;
        }

        BoneLibrary.ModelFacts(hdr, out s.ModelHasAutoplay, s.PoseParameter);
    }

    /// <summary>
    /// <c>EnsureBones</c>: the skeleton for <paramref name="boneMask"/> in
    /// <see cref="BoneEntityState.CachedBones"/>, from the cache when the key
    /// still stands.
    /// </summary>
    public bool EnsureBones(BoneEntityState s, in ServerBonePose pose, int boneMask, float currentTime,
                            in BoneConVars cvars)
    {
        StudioHdr? hdr = _models.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return false;
        }

        s.BoneMaskAsked |= boneMask;

        RefreshModelPoseState(s, pose.ModelIndex);

        float cycle = s.SequenceCycle(currentTime);

        BoneCacheKey wanted = default;
        wanted.Reset();
        wanted.ModelIndex = pose.ModelIndex;
        wanted.BoneMask = boneMask;
        wanted.Sequence = s.Sequence;
        wanted.Cycle = cycle;
        wanted.Origin = pose.AbsOrigin;
        wanted.Angles = new Vector(pose.AbsAngles.Pitch, pose.AbsAngles.Yaw, pose.AbsAngles.Roll);
        wanted.PoseGeneration = 0;
        wanted.AutoplayTime = BoneCacheKey.AutoplayClock(s.ModelHasAutoplay, currentTime);
        wanted.ModelScale = s.ModelScale;

        if (s.CachedBones.Length > 0 && BoneCacheKey.IsValidFor(s.CacheKey, wanted))
        {
            return true;
        }

        int bones = hdr.NumBones;

        if (bones <= 0 || bones > StudioConstants.MAXSTUDIOBONES)
        {
            return false;
        }

        s.ResizeCachedBones(bones);

        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement.Origin = pose.AbsOrigin;
        inputs.Placement.Angles = pose.AbsAngles;
        inputs.Placement.ModelScale = s.ModelScale;
        inputs.BoneMask = s.BoneMaskAsked;
        inputs.Sequence = s.Sequence;
        inputs.Cycle = cycle;
        inputs.CurTime = currentTime;
        inputs.HasAutoplay = s.ModelHasAutoplay;
        inputs.PoseParameter = s.PoseParameter;
        inputs.AnimThreeWayBlend = cvars.AnimThreeWayBlend;
        inputs.Scratch = _scratch;

        if (BoneLibrary.BuildBones(hdr, inputs, s.CachedBones) != bones)
        {
            // Refused rather than half-built.
            s.CachedBones = [];
            return false;
        }

        s.CacheKey = wanted;
        s.CacheKey.BoneMask = s.BoneMaskAsked;

        s.AttachmentsValid = false;
        return true;
    }

    /// <summary>
    /// <c>StandaloneServer_EnsureAttachments</c>: attachment-to-world off the
    /// cached bones, placed at scale 1 (the bones carry the scale). Marked
    /// valid whatever comes back, so a model with none is not re-walked.
    /// </summary>
    public bool EnsureAttachments(BoneEntityState s, in ServerBonePose pose, float currentTime, in BoneConVars cvars)
    {
        if (s.AttachmentsValid)
        {
            return s.Attachments.Length > 0;
        }

        if (!EnsureBones(s, pose, StudioConstants.BONE_USED_BY_ATTACHMENT, currentTime, cvars))
        {
            return false;
        }

        StudioHdr? hdr = _models.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return false;
        }

        int count = hdr.NumGlobalAttachments;

        if (s.Attachments.Length != count)
        {
            System.Array.Resize(ref s.Attachments, System.Math.Max(count, 0));
        }

        s.AttachmentsValid = true;

        BonePlacement placement = default;
        placement.Reset();
        placement.Origin = pose.AbsOrigin;
        placement.Angles = pose.AbsAngles;

        return BoneLibrary.BuildAttachments(hdr, s.CachedBones, placement, s.Attachments) > 0;
    }

    /// <summary>
    /// <c>GetAttachment</c>: a ONE-based attachment's origin and angles. The
    /// entity's own place is written before anything can fail.
    /// </summary>
    public bool GetAttachment(BoneEntityState s, in ServerBonePose pose, int attachment, float currentTime,
                              in BoneConVars cvars, out Vector origin, out BoneAngle angles)
    {
        origin = pose.AbsOrigin;
        angles = pose.AbsAngles;

        if (!EnsureAttachments(s, pose, currentTime, cvars))
        {
            return false;
        }

        if (attachment < 1 || attachment > s.Attachments.Length)
        {
            return false;
        }

        angles = BoneSetupMath.MatrixAngles(s.Attachments[attachment - 1]);
        origin = s.Attachments[attachment - 1].Position;
        return true;
    }

    /// <summary><c>BoneCount</c>: the model's bones (0 without a model), and
    /// how many are cached.</summary>
    public int BoneCount(BoneEntityState s, int modelIndex, out int cached)
    {
        cached = s.CachedBones.Length;
        return BoneLibrary.BoneCount(_models.OpenStudioHdr(modelIndex));
    }

    /// <summary>
    /// <c>SetPoseSequence</c>: the client's rule (the playing sequence keeps
    /// its clock unless restarted) on the server's clock; the duration is read
    /// at the model's DEFAULT pose, and the cache goes.
    /// </summary>
    public void SetPoseSequence(BoneEntityState s, int modelIndex, int sequence, float playbackRate,
                                bool restart, float now)
    {
        if (playbackRate <= 0.0f)
        {
            playbackRate = 1.0f;
        }

        if (sequence < 0)
        {
            sequence = 0;
        }

        if (sequence == s.Sequence && !restart)
        {
            s.PlaybackRate = playbackRate;
            return;
        }

        s.Sequence = sequence;
        s.PlaybackRate = playbackRate;
        s.AnimTime = now;
        s.HeldCycle = null;

        s.SeqDuration = 0.0f;
        s.SeqLooping = false;

        StudioHdr? hdr = _models.OpenStudioHdr(modelIndex);

        if (hdr is not null)
        {
            BoneLibrary.SequenceFacts(hdr, sequence, null, out s.SeqDuration, out s.SeqLooping);
        }

        s.InvalidateBoneCache();
    }

    /// <summary><c>PoseCycle</c>: the server clock's cycle (the arithmetic is
    /// the library's, the clock is this side's).</summary>
    public float PoseCycle(BoneEntityState s, float now)
    {
        return s.SequenceCycle(now);
    }

    /// <summary>
    /// <c>TestHitboxes</c>: a ray against the posed hitboxes, bones built
    /// under BONE_USED_BY_HITBOX, at the entity's own origin and scale. True
    /// means "tested", not "hit".
    /// </summary>
    public bool TestHitboxes(BoneEntityState s, in ServerBonePose pose, int hitboxSet, in BoneRay ray,
                             int contentsMask, BoneTrace tr, SurfacePropReader? surfaceProps,
                             float currentTime, in BoneConVars cvars)
    {
        StudioHdr? hdr = _models.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return false;
        }

        if (!EnsureBones(s, pose, StudioConstants.BONE_USED_BY_HITBOX, currentTime, cvars))
        {
            return false;
        }

        return BoneLibrary.TraceHitboxes(surfaceProps, hdr, hitboxSet, ray, contentsMask,
                                         s.CachedBones, pose.AbsOrigin, s.ModelScale, tr);
    }
}
