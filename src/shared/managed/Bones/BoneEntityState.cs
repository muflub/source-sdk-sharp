//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The per-entity bone state the two standalone shells keep (plan
// physics_bones_native_zero step 1.0b): C_StandaloneEntity's and
// CStandaloneEntity's bone members, as one instance per entity, held by the
// BoneEntityStates service under the entity's handle. Nothing here is static
// (SS0001): an entity's skeleton, its pose memory (the IK latch, the jiggle
// springs) and its clocks are the entity's.
//
// The constructor is both shells' constructors: every key never-valid, the
// merge source -1, every layer slot unfilled with a rate of 1, the base
// sequence's rate 1, the scale 1, the pose defaults owed to model -1. Each
// value that is not a zero is one a zeroed block would get wrong.
//
// The small rules that read only this state live here too: the cycle
// arithmetic (stock's C_BaseAnimating cycle clamp, BoneLibraryMath.Cycle),
// the scale refusal, the merge-source reset, the layer count. The decisions
// that need a model or the engine are ClientBoneDriver's and ServerBoneDriver's.
//
// The base sequence's clock (Sequence, PlaybackRate, AnimTime) is the
// engine block's Client tail in native; here the state owns it and a later
// step mirrors it into the block.
//=============================================================================//

using System;
using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Shared.Bones;

/// <summary>What <see cref="BoneEntityState.SetModelScale"/> did.</summary>
public enum ModelScaleChange
{
    /// <summary>Zero or less: refused, nothing changed (the caller warns).</summary>
    Refused,

    /// <summary>The scale already held: nothing changed.</summary>
    Unchanged,

    /// <summary>Stored, and the bone cache invalidated.</summary>
    Changed,
}

/// <summary>One standalone entity's bone state (both sides' shell members).</summary>
public sealed class BoneEntityState
{
    /// <summary>studio.h <c>MAXSTUDIOPOSEPARAM</c>.</summary>
    public const int MaxPoseParameters = BoneLibrary.MaxStudioPoseParam;

    /// <summary><c>SS_RAGDOLLID_NONE</c>.</summary>
    public const uint RagdollNone = 0u;

    // ---- the skeleton and its key ------------------------------------------

    /// <summary><c>m_nBoneMaskAsked</c>: every mask ever asked, accumulated.</summary>
    public int BoneMaskAsked;

    /// <summary><c>m_BoneCacheKey</c>: what <see cref="CachedBones"/> was built from.</summary>
    public BoneCacheKey CacheKey;

    /// <summary><c>m_CachedBones</c>: bone-to-world, one per model bone.</summary>
    public BoneMatrix3x4[] CachedBones = [];

    /// <summary><c>m_bAttachmentsValid</c>.</summary>
    public bool AttachmentsValid;

    /// <summary><c>m_Attachments</c>: attachment-to-world, index 0 = attachment 1.</summary>
    public BoneMatrix3x4[] Attachments = [];

    /// <summary><c>m_flModelScale</c>: 1, never 0 (it multiplies every bone).</summary>
    public float ModelScale = 1.0f;

    // ---- per-model facts (RefreshModelPoseState) ----------------------------

    /// <summary><c>m_nPoseDefaultsFor</c>: the model the facts below were read
    /// for; -1 because 0 is a real model index.</summary>
    public int PoseDefaultsFor = -1;

    /// <summary><c>m_bModelHasAutoplay</c>.</summary>
    public bool ModelHasAutoplay;

    /// <summary><c>m_bModelHasJiggleBones</c> (client).</summary>
    public bool ModelHasJiggleBones;

    /// <summary><c>m_nModelIkChains</c> (client).</summary>
    public int ModelIkChains;

    /// <summary><c>m_flPoseParameter</c>: normalised, defaulted from the model.</summary>
    public readonly float[] PoseParameter = new float[MaxPoseParameters];

    /// <summary><c>m_nPoseGeneration</c>: advances exactly when a stored pose value changes.</summary>
    public int PoseGeneration;

    // ---- pose memory the cache key cannot see (client) ----------------------

    /// <summary><c>m_pIk</c>: kept, because a planted foot is a memory.</summary>
    public CIKContext? Ik;

    /// <summary><c>m_pJiggleBones</c>: the springs, integrated across frames.</summary>
    public JiggleBones? Jiggle;

    /// <summary><c>m_hRagdoll</c>: the ragdoll table id, <see cref="RagdollNone"/> for none.</summary>
    public uint Ragdoll = RagdollNone;

    // ---- bone merge (client) -------------------------------------------------

    /// <summary><c>m_nBoneMergeSource</c>: the source's entity index, -1 for none.</summary>
    public int BoneMergeSource = -1;

    /// <summary><c>m_BoneMergeKey</c>.</summary>
    public BoneMergeKey MergeKey;

    /// <summary><c>m_MergeBoneMap</c>: own bone to source bone, -1 unmatched.</summary>
    public short[] MergeBoneMap = [];

    /// <summary><c>m_bInBoneMerge</c>: the re-entry guard.</summary>
    public bool InBoneMerge;

    // ---- the base sequence's clock ------------------------------------------

    /// <summary>Block().Client.Sequence.</summary>
    public int Sequence;

    /// <summary>Block().Client.PlaybackRate: 1, never 0 (0 is a statue).</summary>
    public float PlaybackRate = 1.0f;

    /// <summary>Block().Client.AnimTime: when the sequence started.</summary>
    public float AnimTime;

    /// <summary><c>m_flSeqDuration</c> / <c>m_flPoseSeqDuration</c>.</summary>
    public float SeqDuration;

    /// <summary><c>m_bSeqLooping</c> / <c>m_bPoseSeqLooping</c>.</summary>
    public bool SeqLooping;

    /// <summary>
    /// <c>SetCycle</c> on a sequence nothing advances: the cycle stays where it
    /// was set (a wall charger's drain), until the next sequence starts.
    /// </summary>
    public float? HeldCycle;

    /// <summary><c>m_nSequenceStarts</c> (client): every new sequence and
    /// every restart bumps it, so it is stock's <c>m_nNewSequenceParity</c>.</summary>
    public int SequenceStarts;

    /// <summary><c>m_SequenceTransitioner</c> (client): the sequences a
    /// change is still fading out, and the parity it last acted on.</summary>
    public readonly SequenceTransitioner Transitions = new();

    // ---- layers (client) -----------------------------------------------------

    /// <summary><c>m_Layers</c>.</summary>
    public readonly AnimLayer[] Layers = new AnimLayer[AnimLayerRule.MaxLayers];

    /// <summary><c>m_flLayerStart</c>.</summary>
    public readonly float[] LayerStart = new float[AnimLayerRule.MaxLayers];

    /// <summary><c>m_flLayerRate</c>.</summary>
    public readonly float[] LayerRate = new float[AnimLayerRule.MaxLayers];

    /// <summary><c>m_flLayerDuration</c>.</summary>
    public readonly float[] LayerDuration = new float[AnimLayerRule.MaxLayers];

    /// <summary><c>m_bLayerLooping</c>.</summary>
    public readonly bool[] LayerLooping = new bool[AnimLayerRule.MaxLayers];

    /// <summary><c>m_nLayerGeneration</c>: advances whenever the layer table changes.</summary>
    public int LayerGeneration;

    /// <summary>
    /// The last client frame the engine drew this skeleton: asked it through
    /// SetupBones, or was answered from that frame's batch (plan_physics_bones_perf
    /// A2). The next frame's batch poses the ones drawn the frame before.
    /// </summary>
    public int EngineAskFrame = int.MinValue;

    /// <summary>Both shells' constructors.</summary>
    public BoneEntityState()
    {
        CacheKey.Reset();
        MergeKey.Reset();

        for (int i = 0; i < AnimLayerRule.MaxLayers; i++)
        {
            Layers[i].Reset();
            LayerRate[i] = 1.0f;
        }
    }

    /// <summary><c>InvalidateBoneCache()</c>: the key never-valid, the attachments stale.</summary>
    public void InvalidateBoneCache()
    {
        CacheKey.Reset();
        AttachmentsValid = false;
    }

    /// <summary>
    /// <c>SetModelScale</c>, both sides: zero or less is refused (a skeleton
    /// with no extent draws and traces as nothing; negative mirrors it inside
    /// out), an equal scale is nothing, any other is stored and the cache
    /// goes (the scale is in the key, but a prop that never moves again would
    /// otherwise never be told).
    /// </summary>
    public ModelScaleChange SetModelScale(float scale)
    {
        if (scale <= 0.0f)
        {
            return ModelScaleChange.Refused;
        }

        if (scale == ModelScale)
        {
            return ModelScaleChange.Unchanged;
        }

        ModelScale = scale;
        InvalidateBoneCache();
        return ModelScaleChange.Changed;
    }

    /// <summary><c>SequenceCycle</c> (client) / <c>PoseCycle</c> (server):
    /// the base sequence's cycle at <paramref name="now"/>.</summary>
    public float SequenceCycle(float now)
    {
        return HeldCycle ?? BoneLibraryMath.Cycle(now, AnimTime, PlaybackRate, SeqDuration, SeqLooping);
    }

    /// <summary><c>AnimLayerCycle</c>: a layer's cycle on its own clock; 0
    /// for a slot out of range.</summary>
    public float AnimLayerCycle(int slot, float now)
    {
        if (slot < 0 || slot >= AnimLayerRule.MaxLayers)
        {
            return 0.0f;
        }

        return BoneLibraryMath.Cycle(now, LayerStart[slot], LayerRate[slot], LayerDuration[slot], LayerLooping[slot]);
    }

    /// <summary>
    /// <c>ActiveAnimLayers</c>: the slots carrying something. No sequence
    /// range test (it is asked without a model): a layer naming a sequence
    /// the model lacks is counted here and dropped at accumulation.
    /// </summary>
    public int ActiveAnimLayers()
    {
        int active = 0;

        for (int i = 0; i < AnimLayerRule.MaxLayers; i++)
        {
            if (Layers[i].Sequence >= 0 && Layers[i].Weight > 0.0f)
            {
                active++;
            }
        }

        return active;
    }

    /// <summary>
    /// <c>PoseParametersForDuration</c>: this entity's pose once its defaults
    /// were read for the model worn now, else null (the model's own defaults:
    /// until then the array holds the previous model's pose or zeroes).
    /// </summary>
    public float[]? PoseParametersForDuration(int modelIndex)
    {
        return PoseDefaultsFor == modelIndex ? PoseParameter : null;
    }

    /// <summary>
    /// <c>SetBoneMergeSource</c>: a new source makes the map and the cached
    /// pose belong to the old pairing; both keys go never-valid and the next
    /// ask rebuilds. The same source is nothing.
    /// </summary>
    public void SetBoneMergeSource(int sourceEntIndex)
    {
        if (BoneMergeSource == sourceEntIndex)
        {
            return;
        }

        BoneMergeSource = sourceEntIndex;
        MergeKey.Reset();
        CacheKey.Reset();
        AttachmentsValid = false;
    }

    /// <summary>
    /// The bone cache as <c>CUtlVector::SetCount</c> / <c>std::vector::resize</c>
    /// leave it: <paramref name="count"/> slots, the surviving prefix kept.
    /// </summary>
    public void ResizeCachedBones(int count)
    {
        if (CachedBones.Length != count)
        {
            Array.Resize(ref CachedBones, count);
        }
    }
}

/// <summary>
/// The per-entity bone states, keyed by entity handle: the service both
/// drivers' callers fetch an entity's <see cref="BoneEntityState"/> from.
/// </summary>
public sealed class BoneEntityStates
{
    private readonly Dictionary<EntityHandle, BoneEntityState> _states = [];

    /// <summary>How many entities hold state.</summary>
    public int Count => _states.Count;

    /// <summary>The entity's state, made on first ask.</summary>
    public BoneEntityState GetOrCreate(EntityHandle entity)
    {
        if (!_states.TryGetValue(entity, out BoneEntityState? state))
        {
            state = new BoneEntityState();
            _states.Add(entity, state);
        }

        return state;
    }

    /// <summary>The entity's state, if it has any.</summary>
    public bool TryGet(EntityHandle entity, out BoneEntityState? state)
    {
        return _states.TryGetValue(entity, out state);
    }

    /// <summary>The entity is gone: its springs and latches go with it.</summary>
    public bool Remove(EntityHandle entity)
    {
        return _states.Remove(entity);
    }

    /// <summary>Every entity's state, walked without a snapshot (the frame's batch).</summary>
    public Dictionary<EntityHandle, BoneEntityState>.Enumerator GetEnumerator() => _states.GetEnumerator();

    /// <summary>Every entity's state with its handle, as a snapshot (a reload's carry).</summary>
    public List<(EntityHandle Handle, BoneEntityState State)> All()
    {
        var all = new List<(EntityHandle, BoneEntityState)>(_states.Count);

        foreach (KeyValuePair<EntityHandle, BoneEntityState> pair in _states)
        {
            all.Add((pair.Key, pair.Value));
        }

        return all;
    }

    /// <summary>Level shutdown: every entity's state goes.</summary>
    public void Clear()
    {
        _states.Clear();
    }

    /// <summary>
    /// <c>SourceSharp_StandaloneClientsInvalidateBoneCaches</c>: every entity's
    /// cache never-valid (the engine flushed the model cache). Answers how many.
    /// </summary>
    public int InvalidateAll()
    {
        foreach (BoneEntityState state in _states.Values)
        {
            state.InvalidateBoneCache();
        }

        return _states.Count;
    }
}
