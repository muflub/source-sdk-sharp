//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed twin of C_StandaloneEntity's bone decisions
// (src/client/native/shell_standalone_client.cpp; plan
// physics_bones_native_zero step 1.0b): EnsureBones, RefreshModelPoseState,
// MergeBonesFrom, EnsureAttachments, SetupBones' copy, TestHitboxes,
// SetPlayingSequence and SetAnimLayer. The shape is stock's
// C_BaseAnimating::SetupBones -> StandardBlendingRules -> BuildTransformations
// over the 1.0 BoneLibrary, with this hierarchy's cache in front of it.
//
// What it decides, in order (EnsureBones):
//
//   1. No studio model: false, and nothing recorded -- not even the mask.
//   2. The mask widens BEFORE the lookup (vertex bones and attachment bones
//      are disjoint; a cache built to one caller's mask is rebuilt by the
//      other every frame).
//   3. The per-model facts refresh BEFORE the key, because the refresh can
//      advance the pose generation the key carries.
//   4. The key: model, mask, sequence, cycle, pose + layer generations, the
//      clock only for a model that needs real time (autoplay, jiggle, IK) or
//      runs a layer, the placement (render pose for the local viewmodel), the
//      scale. A merging entity and a ragdoll NEVER hit: each depends on
//      something the key cannot see (the source's pose, the solver).
//   5. The build: a non-poseable model (static prop, no sequences) writes
//      the scaled placement into every bone. Otherwise the pose accumulates
//      (the base sequence, then the sequences a change is still fading out
//      -- SequenceTransitioner, BoneEntityState.Transitions -- then the
//      layers sorted by order; the client's autoplay IK shape), the IK
//      context (made once per entity, only for an unscaled model with chains)
//      updates and solves unless the entity is a ragdoll body, the ragdoll's
//      solver writes the bones it owns, the springs are made on the model's
//      say-so, and PoseToWorldProcedural honours every bone already placed.
//   6. The merge runs after the own pose, so unmatched bones keep meaning
//      something -- unless there are none: a follower the source names
//      every bone of IS the source's skeleton, so its own pose is not built
//      at all (plan_physics_bones_perf A1; MergeEveryBone).
//   7. The key stored records the mask EVALUATED, not the one asked.
//
// Everything the native code reaches through the engine is an
// IClientBoneWorld call; the clock and the convars are arguments; the
// entity's memory is its BoneEntityState. The driver keeps no state.
//
// Not ported: the verbosity Msg text is the native's, but where it goes is
// IClientBoneWorld.Say's business.
//=============================================================================//

using System;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>C_StandaloneEntity's bone decisions over the 1.0 BoneLibrary.</summary>
public sealed class ClientBoneDriver
{
    /// <summary><c>SS_RAGDOLL_MAX_ELEMENTS</c>.</summary>
    public const int RagdollMaxElements = 24;

    private readonly IClientBoneWorld _world;

    // The builds' pose tables, reused (BoneScratch), and the layer table one
    // build hands the library (no build runs inside another's accumulate, so
    // one table serves). Scratch, not memory: nothing in either outlives the
    // build that filled it, which is what "the driver keeps no state" means.
    private readonly BoneScratch _scratch = new();
    private readonly BoneLayerPose[] _layers = new BoneLayerPose[AnimLayerRule.MaxLayers];

    // The queued sequences one build blends (SequenceTransitioner.Maintain
    // fills it, growing it when a queue is longer): scratch like _layers.
    private BoneLayerPose[] _previous = new BoneLayerPose[4];

    public ClientBoneDriver(IClientBoneWorld world)
    {
        _world = world;
    }

    /// <summary>The scratch this driver's builds rent from (the facts: reuse, not growth).</summary>
    public BoneScratch Scratch => _scratch;

    /// <summary>
    /// <c>RefreshModelPoseState</c>: the per-model facts, once per model.
    /// Recorded before the work and unconditionally, so a model with no
    /// studio data is not re-asked every frame. A model that stops having IK
    /// chains drops its solver; the pose defaults are copied in and the
    /// generation advances once, only if a value moved.
    /// </summary>
    public void RefreshModelPoseState(BoneEntityState s, int modelIndex)
    {
        if (s.PoseDefaultsFor == modelIndex)
        {
            return;
        }

        s.PoseDefaultsFor = modelIndex;

        // OnNewModel's "remove transition animations playback"
        // (c_baseanimating.cpp 1026): the queue names the old model's sequences.
        s.Transitions.RemoveAll();

        s.ModelHasAutoplay = false;
        s.ModelHasJiggleBones = false;
        s.ModelIkChains = 0;

        if (!_world.HasStudioModel(modelIndex))
        {
            return;
        }

        StudioHdr? hdr = _world.OpenStudioHdr(modelIndex);

        if (hdr is null)
        {
            return;
        }

        s.ModelHasAutoplay = hdr.CountAutoplaySequences() > 0;

        for (int i = 0; i < hdr.NumBones; i++)
        {
            if (ProceduralBoneRule.Kind(hdr.BoneFlags(i), hdr.Bone(i).ProcType) == ProceduralBoneKind.Jiggle)
            {
                s.ModelHasJiggleBones = true;
                break;
            }
        }

        s.ModelIkChains = hdr.NumIkChains;

        // A solver latched onto the last model's bones answers about a
        // skeleton that is gone.
        if (s.ModelIkChains <= 0 && s.Ik is not null)
        {
            s.Ik = null;
        }

        float[] defaults = new float[BoneEntityState.MaxPoseParameters];
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, defaults, BoneEntityState.MaxPoseParameters);

        bool changed = false;

        for (int i = 0; i < BoneEntityState.MaxPoseParameters; i++)
        {
            if (s.PoseParameter[i] == defaults[i])
            {
                continue;
            }

            s.PoseParameter[i] = defaults[i];
            changed = true;
        }

        if (changed)
        {
            s.PoseGeneration++;
        }
    }

    /// <summary>
    /// <c>EnsureBones</c>: the skeleton for <paramref name="boneMask"/> at
    /// <paramref name="currentTime"/> in <see cref="BoneEntityState.CachedBones"/>,
    /// from the cache when the key still stands. False when the entity has no
    /// skeleton to give (the caller falls back to the entity transform).
    /// </summary>
    public bool EnsureBones(BoneEntityState s, in ClientBonePose pose, int boneMask, float currentTime,
                            in BoneConVars cvars)
    {
        if (!_world.HasStudioModel(pose.ModelIndex))
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
        wanted.PoseGeneration = s.PoseGeneration + s.LayerGeneration;
        wanted.AutoplayTime = BoneCacheKey.AutoplayClock(
            ProceduralBoneRule.NeedsRealTime(s.ModelHasAutoplay, s.ModelHasJiggleBones, s.ModelIkChains > 0)
            || s.ActiveAnimLayers() > 0 || s.Transitions.Count > 1,
            currentTime);

        BonePlacement placement = default;
        BoneLibraryMath.PoseSource(pose.ViewModel, pose.NetOrigin, pose.NetAngles,
                                   pose.RenderOrigin, pose.RenderAngles, ref placement);
        placement.ModelScale = s.ModelScale;

        wanted.Origin = placement.Origin;
        wanted.Angles = new Vector(placement.Angles.Pitch, placement.Angles.Yaw, placement.Angles.Roll);
        wanted.ModelScale = s.ModelScale;

        if (s.BoneMergeSource < 0 && !_world.IsRagdollBody(s.Ragdoll)
            && s.CachedBones.Length > 0 && BoneCacheKey.IsValidFor(s.CacheKey, wanted))
        {
            return true;
        }

        StudioHdr? hdr = _world.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return false;
        }

        int bones = hdr.NumBones;

        if (bones <= 0 || bones > StudioConstants.MAXSTUDIOBONES)
        {
            return false;
        }

        s.ResizeCachedBones(bones);

        // A follower whose every bone the source names is the source's
        // skeleton, whole: its own build would be overwritten bone for bone,
        // so it is not run (stock's BuildTransformations skips a merged bone
        // the same way). The source answers from its cache when it was
        // already posed at this clock -- its own draw this frame, or another
        // follower's.
        bool mapRefreshed = false;

        if (s.BoneMergeSource < 0 || !MergeEveryBone(s, hdr, pose.ModelIndex, currentTime, cvars, out mapRefreshed))
        {
            if (!BoneLibrary.IsPoseable(hdr))
            {
                // The static-prop branch: bone 0 IS the entity transform, scaled.
                BoneMatrix3x4 scaled = BoneLibraryMath.PlacementToWorld(placement);

                for (int i = 0; i < bones; i++)
                {
                    s.CachedBones[i] = scaled;
                }
            }
            else if (!BuildPosedBones(s, hdr, placement, pose, cycle, currentTime, cvars))
            {
                return false;
            }

            if (s.BoneMergeSource >= 0)
            {
                MergeBonesFrom(s, hdr, pose.ModelIndex, currentTime, cvars, mapRefreshed);
            }
        }

        s.CacheKey = wanted;
        s.CacheKey.BoneMask = s.BoneMaskAsked;

        s.AttachmentsValid = false;
        return true;
    }

    /// <summary>EnsureBones' poseable branch: accumulate, IK, ragdoll, springs, matrices.</summary>
    private bool BuildPosedBones(BoneEntityState s, StudioHdr hdr, in BonePlacement placement,
                                 in ClientBonePose pose, float cycle, float currentTime, in BoneConVars cvars)
    {
        BoneScratch.Mark mark = _scratch.Save();

        try
        {
            return BuildPosedBonesRented(s, hdr, placement, pose, cycle, currentTime, cvars);
        }
        finally
        {
            _scratch.Restore(mark);
        }
    }

    private bool BuildPosedBonesRented(BoneEntityState s, StudioHdr hdr, in BonePlacement placement,
                                       in ClientBonePose pose, float cycle, float currentTime, in BoneConVars cvars)
    {
        Vector[] pos = _scratch.Vectors();
        Quaternion[] q = _scratch.Quaternions();

        // Made BEFORE the accumulate, which files the sequence's IK rules
        // into it; kept, because a planted foot is a memory. Never for a
        // scaled model: the solver's bones would be the ones that did not grow.
        if (ProceduralBoneRule.WantsIk(s.ModelIkChains, s.ModelScale != 1.0f) && s.Ik is null)
        {
            s.Ik = new CIKContext();
        }

        s.Ik?.Init(hdr, BoneSetupMath.AngleMatrix(placement.Angles, placement.Origin),
                   s.BoneMaskAsked, currentTime, pose.FrameCount);

        // The layers in the table's order, not the slots'.
        BoneLayerPose[] layers = _layers;
        Array.Clear(layers);
        Span<int> order = stackalloc int[AnimLayerRule.MaxLayers];
        int active = AnimLayerRule.Sort(s.Layers, hdr.NumSequences, order);

        for (int i = 0; i < active; i++)
        {
            int slot = order[i];
            layers[i] = new BoneLayerPose(s.Layers[slot].Sequence, s.AnimLayerCycle(slot, currentTime),
                                          AnimLayerRule.Weight(s.Layers[slot]));
        }

        // MaintainSequenceTransitions, at StandardBlendingRules' point: after
        // the base sequence's cycle is known, before the layers. Never in
        // prediction on this hierarchy (see SequenceTransitioner's header).
        bool noInterpolation = s.Transitions.TakeNoInterpolationFrame(pose.InterpolationFrame);
        int previousCount = s.Transitions.Maintain(hdr, BoneLibraryMath.ClampSequence(s.Sequence, hdr.NumSequences),
                                                   cycle, s.PlaybackRate, s.SequenceStarts, noInterpolation,
                                                   inPrediction: false, currentTime, s.PoseParameter,
                                                   ref _previous);

        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.Placement = placement;
        inputs.BoneMask = s.BoneMaskAsked;
        inputs.Sequence = s.Sequence;
        inputs.Previous = _previous;
        inputs.PreviousCount = previousCount;
        inputs.Cycle = cycle;
        inputs.CurTime = currentTime;
        inputs.HasAutoplay = s.ModelHasAutoplay;
        inputs.Layers = layers;
        inputs.LayerCount = active;
        inputs.PoseParameter = s.PoseParameter;
        inputs.AutoplayIkAlways = true;
        inputs.FrameCount = pose.FrameCount;
        inputs.AnimThreeWayBlend = cvars.AnimThreeWayBlend;
        inputs.Scratch = _scratch;

        if (!BoneLibrary.AccumulatePose(hdr, inputs, pos, q, s.Ik))
        {
            return false;
        }

        // UpdateTargets then SolveDependencies, stock's order; the marks keep
        // the matrix build off the bones the solver placed. Never on a ragdoll.
        BoneSetupExtract.BoneBitList boneComputed = _scratch.BitList();

        if (ProceduralBoneRule.SolvesIk(s.Ik is not null, _world.IsRagdollBody(s.Ragdoll)))
        {
            s.Ik!.UpdateTargets(pos, q, s.CachedBones, boneComputed);
            s.Ik.SolveDependencies(pos, q, s.CachedBones, boneComputed);
        }

        // The solver's bones for a jointed body: written, marked, and every
        // bone the physics does not own still hangs off its moved parent.
        bool ragdollPosed = false;

        if (s.Ragdoll != BoneEntityState.RagdollNone)
        {
            Span<int> owned = stackalloc int[RagdollMaxElements];
            int ownedCount = _world.WriteRagdollBones(s.Ragdoll, s.CachedBones, owned);

            for (int i = 0; i < ownedCount && i < RagdollMaxElements; i++)
            {
                boneComputed.MarkBone(owned[i]);
                ragdollPosed = true;
            }
        }

        // The springs, on the model's say-so: their state outlives the call.
        if (s.ModelHasJiggleBones && s.Jiggle is null)
        {
            s.Jiggle = new JiggleBones();
        }

        BoneLibrary.PoseToWorldProcedural(hdr, placement, pos, q, s.BoneMaskAsked, currentTime,
                                          s.Ik is not null || ragdollPosed ? boneComputed : null,
                                          s.Jiggle, s.CachedBones, cvars.JiggleFramerateCutoff);
        return true;
    }

    /// <summary>
    /// <c>MergeBonesFrom</c>: EF_BONEMERGE's follower half. The source's
    /// matrices over every own bone that shares a name with one of the
    /// source's. The name map is rebuilt only when the pair of models
    /// changed, and its key is stored only for a source that could be read
    /// (a map of -1 vouched for forever is a merge that never recovers).
    /// Re-entry (a merge cycle) keeps the inner ask's own pose.
    /// </summary>
    public void MergeBonesFrom(BoneEntityState s, StudioHdr ownHdr, int ownModelIndex, float currentTime,
                               in BoneConVars cvars) =>
        MergeBonesFrom(s, ownHdr, ownModelIndex, currentTime, cvars, false);

    // mapRefreshed: this ask's whole-merge test already refreshed the map
    // against the same source, so an unreadable source is not opened (and
    // said) twice in one ask.
    private void MergeBonesFrom(BoneEntityState s, StudioHdr ownHdr, int ownModelIndex, float currentTime,
                                in BoneConVars cvars, bool mapRefreshed)
    {
        if (s.InBoneMerge)
        {
            return;
        }

        BoneMergeSourceView source = _world.ResolveMergeSource(s.BoneMergeSource);

        if (source.IsSelf)
        {
            return;
        }

        if (source.Model == 0)
        {
            if (cvars.Verbosity > 0)
            {
                _world.Say($"[SourceSharp] bonemerge: entity {s.BoneMergeSource} has no renderable/model to merge from");
            }

            return;
        }

        if (!mapRefreshed)
        {
            RefreshMergeMap(s, ownHdr, ownModelIndex, source, cvars);
        }

        // BONE_USED_BY_ANYTHING: this side cannot see the source's .qc flags.
        BoneScratch.Mark mark = _scratch.Save();
        BoneMatrix3x4[] sourceBones = _scratch.Matrices();
        bool haveSourceBones;

        s.InBoneMerge = true;

        try
        {
            haveSourceBones = _world.SourceSetupBones(s.BoneMergeSource, sourceBones,
                                                      BoneLibrary.BoneUsedByAnything, currentTime);

            if (haveSourceBones)
            {
                BoneMatrix3x4[] own = _scratch.Matrices();
                int bones = Math.Min(own.Length, s.CachedBones.Length);
                s.CachedBones.AsSpan(0, bones).CopyTo(own);

                BoneLibraryMath.ApplyMergedBones(s.MergeBoneMap, sourceBones, s.CachedBones);
                BoneLibraryMath.ReparentUnmergedBones(ownHdr, s.MergeBoneMap, own.AsSpan(0, bones), s.CachedBones);
            }
        }
        finally
        {
            s.InBoneMerge = false;
            _scratch.Restore(mark);
        }

        if (!haveSourceBones && cvars.Verbosity > 0)
        {
            _world.Say($"[SourceSharp] bonemerge: source entity {s.BoneMergeSource} refused SetupBones");
        }
    }

    /// <summary>
    /// The name map from the follower's bones to the source's, rebuilt only
    /// when the pair of models changed; its key is stored only for a source
    /// that could be read (a map of -1 vouched for forever is a merge that
    /// never recovers).
    /// </summary>
    private void RefreshMergeMap(BoneEntityState s, StudioHdr ownHdr, int ownModelIndex, in BoneMergeSourceView source,
                                 in BoneConVars cvars)
    {
        BoneMergeKey wanted = new() { OwnModelIndex = ownModelIndex, SourceModel = source.Model };

        if (BoneMergeKey.IsValidFor(s.MergeKey, wanted))
        {
            return;
        }

        if (s.MergeBoneMap.Length != ownHdr.NumBones)
        {
            Array.Resize(ref s.MergeBoneMap, Math.Max(ownHdr.NumBones, 0));
        }

        StudioHdr? sourceHdr = _world.OpenStudioHdrForModel(source.Model);

        int matched = BoneLibrary.BuildMergeMap(ownHdr, sourceHdr, s.MergeBoneMap);

        if (sourceHdr is not null)
        {
            s.MergeKey = wanted;
        }

        if (cvars.Verbosity > 0)
        {
            _world.Say($"[SourceSharp] bonemerge: map built, {matched} of {ownHdr.NumBones} bone(s) matched source entity {s.BoneMergeSource}");
        }
    }

    /// <summary>
    /// The whole-skeleton merge (plan_physics_bones_perf A1): when the map
    /// names a source bone for EVERY own bone, the follower's skeleton is the
    /// source's, bone for bone, and its own pose would be built only to be
    /// overwritten. Copies the source's bones and answers true; false -- and
    /// nothing written -- when the merge cannot be whole: a re-entered merge
    /// (a cycle keeps the inner ask's own pose), a ragdoll body (the solver
    /// owns bones), a source that is this entity or has no model, a bone the
    /// source does not name, or a source that refuses SetupBones. The caller
    /// then builds the own pose and merges over it, as before.
    /// </summary>
    private bool MergeEveryBone(BoneEntityState s, StudioHdr ownHdr, int ownModelIndex, float currentTime,
                                in BoneConVars cvars, out bool mapRefreshed)
    {
        mapRefreshed = false;

        if (s.InBoneMerge || s.Ragdoll != BoneEntityState.RagdollNone)
        {
            return false;
        }

        BoneMergeSourceView source = _world.ResolveMergeSource(s.BoneMergeSource);

        if (source.IsSelf || source.Model == 0)
        {
            return false;
        }

        RefreshMergeMap(s, ownHdr, ownModelIndex, source, cvars);
        mapRefreshed = true;

        int bones = ownHdr.NumBones;

        if (s.MergeBoneMap.Length != bones)
        {
            return false;
        }

        for (int i = 0; i < bones; i++)
        {
            if ((uint)s.MergeBoneMap[i] >= StudioConstants.MAXSTUDIOBONES)
            {
                return false;
            }
        }

        BoneScratch.Mark mark = _scratch.Save();
        s.InBoneMerge = true;

        try
        {
            BoneMatrix3x4[] sourceBones = _scratch.Matrices();

            if (!_world.SourceSetupBones(s.BoneMergeSource, sourceBones, BoneLibrary.BoneUsedByAnything, currentTime))
            {
                return false;
            }

            BoneLibraryMath.ApplyMergedBones(s.MergeBoneMap, sourceBones, s.CachedBones);
            return true;
        }
        finally
        {
            s.InBoneMerge = false;
            _scratch.Restore(mark);
        }
    }

    /// <summary>
    /// <c>EnsureAttachments</c>: attachment-to-world off the cached bones,
    /// rebuilt only after the bones moved. Placed at the NETWORKED pose with
    /// the real scale (the library applies it only through the bones).
    /// Marked valid whatever comes back.
    /// </summary>
    public bool EnsureAttachments(BoneEntityState s, in ClientBonePose pose, float currentTime, in BoneConVars cvars)
    {
        if (s.AttachmentsValid)
        {
            return s.Attachments.Length > 0;
        }

        if (!EnsureBones(s, pose, StudioConstants.BONE_USED_BY_ATTACHMENT, currentTime, cvars))
        {
            return false;
        }

        StudioHdr? hdr = _world.HasStudioModel(pose.ModelIndex) ? _world.OpenStudioHdr(pose.ModelIndex) : null;

        if (hdr is null)
        {
            return false;
        }

        int count = hdr.NumGlobalAttachments;

        if (s.Attachments.Length != count)
        {
            Array.Resize(ref s.Attachments, Math.Max(count, 0));
        }

        s.AttachmentsValid = true;

        BonePlacement placement = new() { Origin = pose.NetOrigin, Angles = pose.NetAngles, ModelScale = s.ModelScale };

        BoneLibrary.BuildAttachments(hdr, s.CachedBones, placement, s.Attachments);

        return count > 0;
    }

    /// <summary>
    /// <c>SetupBones</c>: fill the cache and copy it out. A null
    /// <paramref name="boneToWorldOut"/> only fills (answers whether there are
    /// bones); otherwise every slot past the model's bones -- or every slot,
    /// with no skeleton -- is <paramref name="worldTransform"/>, and the
    /// answer is true.
    /// </summary>
    public bool SetupBones(BoneEntityState s, in ClientBonePose pose, BoneMatrix3x4[]? boneToWorldOut,
                           int boneMask, float currentTime, in BoneConVars cvars,
                           in BoneMatrix3x4 worldTransform)
    {
        bool haveBones = EnsureBones(s, pose, boneMask, currentTime, cvars);

        if (boneToWorldOut is null)
        {
            return haveBones;
        }

        int maxBones = boneToWorldOut.Length;
        int copy = haveBones ? Math.Min(s.CachedBones.Length, maxBones) : 0;

        for (int i = 0; i < copy; i++)
        {
            boneToWorldOut[i] = s.CachedBones[i];
        }

        for (int i = copy; i < maxBones; i++)
        {
            boneToWorldOut[i] = worldTransform;
        }

        return true;
    }

    /// <summary>
    /// <c>TestHitboxes</c>: a ray against this entity's posed hitboxes, the
    /// bones built under BONE_USED_BY_HITBOX. True means "tested", not "hit".
    /// </summary>
    public bool TestHitboxes(BoneEntityState s, in ClientBonePose pose, int hitboxSet, in BoneRay ray,
                             int contentsMask, Vector collisionOrigin, BoneTrace tr,
                             SurfacePropReader? surfaceProps, float currentTime, in BoneConVars cvars)
    {
        StudioHdr? hdr = _world.HasStudioModel(pose.ModelIndex) ? _world.OpenStudioHdr(pose.ModelIndex) : null;

        if (hdr is null)
        {
            return false;
        }

        if (!EnsureBones(s, pose, StudioConstants.BONE_USED_BY_HITBOX, currentTime, cvars))
        {
            return false;
        }

        return BoneLibrary.TraceHitboxes(surfaceProps, hdr, hitboxSet, ray, contentsMask,
                                         s.CachedBones, collisionOrigin, s.ModelScale, tr);
    }

    /// <summary>
    /// <c>SetPlayingSequence</c>: the sequence playing now keeps its clock
    /// (only the rate moves) unless the caller restarts it; a new one starts
    /// at <paramref name="now"/> and reads its duration and loop flag once,
    /// at this entity's real pose.
    /// </summary>
    public void SetPlayingSequence(BoneEntityState s, int modelIndex, int sequence, float playbackRate,
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
        s.SequenceStarts++;

        s.SeqDuration = 0.0f;
        s.SeqLooping = false;

        StudioHdr? hdr = _world.OpenStudioHdr(modelIndex);

        if (hdr is not null)
        {
            BoneLibrary.SequenceFacts(hdr, sequence, s.PoseParametersForDuration(modelIndex),
                                      out s.SeqDuration, out s.SeqLooping);
        }
    }

    /// <summary>
    /// <c>SetAnimLayer</c>: one layer set or switched off. Any change to the
    /// table advances the layer generation; a slot keeps its clock for the
    /// same sequence unless restarted; switched off, its clock goes too.
    /// </summary>
    public void SetAnimLayer(BoneEntityState s, int modelIndex, int slot, int sequence, float weight,
                             float playbackRate, int order, bool restart, float now)
    {
        if (slot < 0 || slot >= AnimLayerRule.MaxLayers)
        {
            return;
        }

        ref AnimLayer layer = ref s.Layers[slot];

        bool sameSequence = layer.Sequence == sequence;

        if (!sameSequence || layer.Weight != weight || layer.Order != order || restart)
        {
            s.LayerGeneration++;
        }

        layer.Sequence = sequence;
        layer.Weight = weight;
        layer.Order = order;

        s.LayerRate[slot] = playbackRate > 0.0f ? playbackRate : 1.0f;

        if (sequence < 0)
        {
            s.LayerStart[slot] = 0.0f;
            s.LayerDuration[slot] = 0.0f;
            s.LayerLooping[slot] = false;
            return;
        }

        if (sameSequence && !restart)
        {
            return;
        }

        s.LayerStart[slot] = now;
        s.LayerDuration[slot] = 0.0f;
        s.LayerLooping[slot] = false;

        StudioHdr? hdr = _world.OpenStudioHdr(modelIndex);

        if (hdr is not null)
        {
            BoneLibrary.SequenceFacts(hdr, sequence, s.PoseParametersForDuration(modelIndex),
                                      out s.LayerDuration[slot], out s.LayerLooping[slot]);
        }
    }
}
