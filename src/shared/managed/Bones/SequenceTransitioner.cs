//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// Stock's client sequence transitions: CSequenceTransitioner
// (game/shared/sequence_Transitioner.cpp), the C_AnimationLayer fields and
// GetFadeout it runs on (game/client/animationlayer.h), and the decisions of
// C_BaseAnimating::MaintainSequenceTransitions (c_baseanimating.cpp
// 1815-1864). When the playing sequence changes, or restarts, the one that
// was playing is queued and keeps playing on its own clock while its weight
// fades out; the pose build blends each queued sequence over the current
// one. So a change does not pop.
//
// One instance per entity, in its BoneEntityState: the queue and the parity
// it last acted on are that entity's (SS0001: nothing here is static). The
// accumulate is the caller's (ClientBoneDriver hands the list Maintain fills
// to BoneLibrary.AccumulatePose, which blends it after the base sequence and
// before the layers, StandardBlendingRules' order).
//
// What feeds it, on this hierarchy:
//   - m_nNewSequenceParity is BoneEntityState.SequenceStarts, which every
//     new sequence and every restart bumps (SetPlayingSequence), as stock's
//     ResetSequenceInfo bumps the parity;
//   - IsNoInterpolationFrame() is the block's InterpolationFrame having moved
//     since the last build (TakeNoInterpolationFrame). Stock compares it with
//     m_ubOldInterpolationFrame, which PreDataUpdate catches up; ours is caught
//     up by the client's interpolation pass, which runs before any bone build,
//     so the build keeps its own copy;
//   - prediction->InPrediction() is Maintain's inPrediction. The driver passes
//     false: the client's prediction replay (ClientPlayerPrediction) runs the
//     movement and the arsenal's phase 1, never a bone build, and its writes
//     (ClientSsViewModel.Present) only move forward, so there is no
//     predicted parity to roll back.
//
// Not ported: C_BaseAnimating::ClientSideAnimationChanged's own
// CheckForSequenceChange (c_baseanimating.cpp 5121). Run again by the next
// build before UpdateCurrent, it is a no-op (the tail it leaves has a zero
// m_flLayerAnimtime), so the build's call decides alone. GetBlendedLinearVelocity
// (5434-5456), the queue's other reader, has no caller on this hierarchy.
//=============================================================================//

using System;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// One queued sequence: the <c>C_AnimationLayer</c> fields a
/// <c>CSequenceTransitioner</c> uses.
/// </summary>
public struct TransitionBlend
{
    /// <summary><c>m_nSequence</c>.</summary>
    public int Sequence;

    /// <summary><c>m_flCycle</c>: the cycle at <see cref="LayerAnimtime"/>.</summary>
    public float Cycle;

    /// <summary><c>m_flPlaybackRate</c>.</summary>
    public float PlaybackRate;

    /// <summary><c>m_flWeight</c>: the fade's weight at the last update.</summary>
    public float Weight;

    /// <summary><c>m_flLayerAnimtime</c>: when it was last the current
    /// sequence; 0 means it never was drawn.</summary>
    public float LayerAnimtime;

    /// <summary><c>m_flLayerFadeOuttime</c>: how long the fade takes.</summary>
    public float LayerFadeOuttime;

    /// <summary>
    /// <c>C_AnimationLayer::GetFadeout</c>: 1 at the change, falling along
    /// a smoothstep to 0 after <see cref="LayerFadeOuttime"/>; 0 for no fade
    /// time, 1 while the clock is behind the change. Past the fade it is
    /// negative (the caller trims at 0 or below).
    /// </summary>
    public readonly float GetFadeout(float curTime)
    {
        float s;

        if (LayerFadeOuttime <= 0.0f)
        {
            s = 0;
        }
        else
        {
            // 1.0 is a double in stock: the subtraction is done in double and
            // stored to float, which for these operands is the float result.
            s = (float)(1.0 - (curTime - LayerAnimtime) / LayerFadeOuttime);

            if (s > 0 && s <= 1.0)
            {
                // do a nice spline curve
                s = 3 * s * s - 2 * s * s * s;
            }
            else if (s > 1.0f)
            {
                // Shouldn't happen, but maybe curtime is behind animtime?
                s = 1.0f;
            }
        }

        return s;
    }
}

/// <summary>
/// <c>CSequenceTransitioner</c> with <c>MaintainSequenceTransitions</c>'
/// decisions: the queue of sequences an entity's changes are still fading
/// out. The last entry is the current sequence; the ones before it, oldest
/// first, are fading.
/// </summary>
public sealed class SequenceTransitioner
{
    private TransitionBlend[] _queue = [];
    private int _count;

    /// <summary><c>m_nPrevNewSequenceParity</c>: the parity last acted on;
    /// -1 (stock's constructor) so the first build acts on any.</summary>
    public int PrevNewSequenceParity = -1;

    /// <summary>The InterpolationFrame the last build saw (stock's
    /// <c>m_ubOldInterpolationFrame</c>, kept per build). A block starts at 0.</summary>
    public int SeenInterpolationFrame;

    /// <summary><c>m_animationQueue.Count()</c>.</summary>
    public int Count => _count;

    /// <summary><c>m_animationQueue[index]</c>.</summary>
    public TransitionBlend this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _queue[index];
        }
    }

    /// <summary>
    /// <c>CheckForSequenceChange</c>. When the tail was drawn (a non-zero
    /// animtime) and the sequence changed, or <paramref name="forceNewSequence"/>
    /// says it restarted: a STUDIO_SNAP sequence or a no-interpolation frame
    /// drops every queued entry; otherwise the tail fades over the shorter of
    /// its fade-out and the new sequence's fade-in. Either way a new tail is
    /// pushed. The tail is then reset for <see cref="UpdateCurrent"/> to fill.
    /// </summary>
    public void CheckForSequenceChange(StudioHdr? hdr, int curSequence, bool forceNewSequence, bool interpolate)
    {
        // sequence may be set before model is initialized
        if (hdr is null)
        {
            return;
        }

        if (_count == 0)
        {
            AddToTail();
        }

        ref TransitionBlend currentblend = ref _queue[_count - 1];

        if (currentblend.LayerAnimtime != 0.0f
            && (currentblend.Sequence != curSequence || forceNewSequence))
        {
            MstudioSeqDesc seqdesc = hdr.SeqDesc(curSequence);

            // sequence changed
            if ((seqdesc.Flags & StudioConstants.STUDIO_SNAP) != 0 || !interpolate)
            {
                // remove all entries
                _count = 0;
            }
            else
            {
                MstudioSeqDesc prevseqdesc = hdr.SeqDesc(currentblend.Sequence);
                currentblend.LayerFadeOuttime = MathF.Min(prevseqdesc.FadeOutTime, seqdesc.FadeInTime);
            }

            // push previously set sequence
            AddToTail();
            currentblend = ref _queue[_count - 1];
        }

        currentblend.Sequence = -1;
        currentblend.LayerAnimtime = 0.0f;
        currentblend.LayerFadeOuttime = 0.0f;
    }

    /// <summary>
    /// <c>UpdateCurrent</c>: the tail records the current sequence and its
    /// clock; every entry before it takes its fade's weight, and one whose
    /// fade has run out (0 or below) is removed, the rest keeping their order.
    /// </summary>
    public void UpdateCurrent(StudioHdr? hdr, int curSequence, float curCycle, float curPlaybackRate, float curTime)
    {
        // sequence may be set before model is initialized
        if (hdr is null)
        {
            return;
        }

        if (_count == 0)
        {
            AddToTail();
        }

        ref TransitionBlend currentblend = ref _queue[_count - 1];

        // keep track of current sequence
        currentblend.Sequence = curSequence;
        currentblend.LayerAnimtime = curTime;
        currentblend.Cycle = curCycle;
        currentblend.PlaybackRate = curPlaybackRate;

        // calc blending weights for previous sequences
        for (int i = 0; i < _count - 1;)
        {
            float s = _queue[i].GetFadeout(curTime);

            if (s > 0)
            {
                _queue[i].Weight = s;
                i++;
            }
            else
            {
                Remove(i);
            }
        }
    }

    /// <summary><c>RemoveAll</c>: stock's OnNewModel (c_baseanimating.cpp 1026).</summary>
    public void RemoveAll()
    {
        _count = 0;
    }

    /// <summary>
    /// <c>IsNoInterpolationFrame()</c> at a build: whether the block's
    /// <paramref name="interpolationFrame"/> moved since the last build, which
    /// then has seen it.
    /// </summary>
    public bool TakeNoInterpolationFrame(int interpolationFrame)
    {
        bool moved = interpolationFrame != SeenInterpolationFrame;
        SeenInterpolationFrame = interpolationFrame;
        return moved;
    }

    /// <summary>
    /// <c>MaintainSequenceTransitions</c> without its accumulate. In
    /// prediction the parity is taken and nothing else happens. Otherwise
    /// the change check (a parity unlike the last one acted on forces a
    /// restart; a no-interpolation frame snaps), the update, and then every
    /// queued sequence before the tail into <paramref name="previous"/>,
    /// newest first, at its own cycle advanced to <paramref name="curTime"/>
    /// at its own rate (wrapped when it loops, held short of the end when
    /// not) and its fade's weight. Answers how many were written;
    /// <paramref name="previous"/> grows when it is too short.
    /// </summary>
    public int Maintain(StudioHdr? hdr, int sequence, float cycle, float playbackRate, int newSequenceParity,
                        bool noInterpolationFrame, bool inPrediction, float curTime, float[]? poseParameter,
                        ref BoneLayerPose[] previous)
    {
        if (hdr is null)
        {
            return 0;
        }

        if (inPrediction)
        {
            PrevNewSequenceParity = newSequenceParity;
            return 0;
        }

        CheckForSequenceChange(hdr, sequence, newSequenceParity != PrevNewSequenceParity, !noInterpolationFrame);

        PrevNewSequenceParity = newSequenceParity;

        // Update the transition sequence list.
        UpdateCurrent(hdr, sequence, cycle, playbackRate, curTime);

        int count = _count - 1;

        if (count <= 0)
        {
            return 0;
        }

        if (previous.Length < count)
        {
            Array.Resize(ref previous, count);
        }

        int written = 0;

        // process previous sequences
        for (int i = _count - 2; i >= 0; i--)
        {
            TransitionBlend blend = _queue[i];

            float dt = curTime - blend.LayerAnimtime;
            float blendCycle = blend.Cycle + dt * blend.PlaybackRate * CycleRate(hdr, blend.Sequence, poseParameter);
            blendCycle = ClampCycle(blendCycle, IsLooping(hdr, blend.Sequence));

            previous[written++] = new BoneLayerPose(blend.Sequence, blendCycle, blend.Weight);
        }

        return written;
    }

    /// <summary><c>C_BaseAnimating::ClampCycle</c>: a looping cycle wraps
    /// into [0, 1); any other is held in [0, 0.999].</summary>
    public static float ClampCycle(float cycle, bool isLooping)
    {
        if (isLooping)
        {
            // FIXME: does this work with negative framerate?
            cycle -= (int)cycle;

            if (cycle < 0.0f)
            {
                cycle += 1.0f;
            }
        }
        else
        {
            cycle = Math.Clamp(cycle, 0.0f, 0.999f);
        }

        return cycle;
    }

    /// <summary><c>GetSequenceCycleRate</c>: 1 / SequenceDuration, or 0.
    /// A sequence the model lacks has no rate.</summary>
    private static float CycleRate(StudioHdr hdr, int sequence, float[]? poseParameter)
    {
        if (sequence < 0 || sequence >= hdr.NumSequences)
        {
            return 0.0f;
        }

        BoneLibrary.SequenceFacts(hdr, sequence, poseParameter, out float t, out _);
        return t != 0.0f ? 1.0f / t : t;
    }

    private static bool IsLooping(StudioHdr hdr, int sequence) =>
        sequence >= 0 && sequence < hdr.NumSequences
        && (hdr.SeqDesc(sequence).Flags & StudioConstants.STUDIO_LOOPING) != 0;

    private void AddToTail()
    {
        if (_count == _queue.Length)
        {
            Array.Resize(ref _queue, Math.Max(4, _queue.Length * 2));
        }

        _queue[_count++] = default;
    }

    private void Remove(int index)
    {
        Array.Copy(_queue, index + 1, _queue, index, _count - index - 1);
        _count--;
    }
}
