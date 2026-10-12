//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// CBoneSetup — bone_setup.cpp's per-model pose accumulator, ported to C#:
// the ctor (2333-2339), AccumulatePose (2393-2457), AddSequenceLayers
// (2125-2211), AddLocalLayers (2218-2286), CalcPoseSingle (1922-2116) and
// CalcAutoplaySequences (4457-4493), plus the InitPose thunk (2305-2308).
//
// The stock class's four fields are the instance state below; there is no
// static state anywhere (SS0001). Stock's scratch pools are rents from the
// BoneScratch the caller hands the constructor (see BoneSetupBlend's note;
// that one sentence documents the CBoneSetupMemoryPool rows 136/137 for
// this lane too).
//
// Port-parameterized inputs (port contract: no clock, no convar, no hidden
// state):
//   * anim_3wayblend (the stock ConVar read at oracle 1838/2066) arrives as
//     the bool animThreeWayBlend on CalcPoseSingle/AccumulatePose/AddSequenceLayers/
//     AddLocalLayers/CalcAutoplaySequences;
//   * flStall — stock's CalcAnimation block-stall blend, computed from the
//     mdlcache clock — arrives as a float (same shape BoneSetupExtract
//     already uses), defaulting to 0 like a model whose blocks all answered.
//   * The pose-debugger arm (oracle 2413-2419) is CLIENT_DLL: the server
//     build never compiles it, so the ctor takes no poseDebugger argument
//     and AccumulatePose has no debugger call. A future client port injects
//     an IPoseDebugger-typed service here.
//
// The IBoneSetup thunks (TSV rows 32-36) are CBoneSetup's own public
// surface — InitPose / AccumulatePose / CalcAutoplaySequences — per the
// batch ruling: no separate IBoneSetup type in the managed port. The
// CalcBoneAdj and GetStudioHdr thunks are the client-only / hdr-read rows
//
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// Stock's <c>CBoneSetup</c>: the four members
/// <c>m_pStudioHdr</c>/<c>m_boneMask</c>/<c>m_flPoseParameter</c>/<c>m_pPoseDebugger</c>
/// become the fields below (the debugger is client-only, see file header).
/// Instance state only; every service-like input arrives per call.
/// </summary>
public sealed class CBoneSetup
{
    private readonly StudioHdr m_pStudioHdr;
    private readonly int m_boneMask;
    private readonly float[] m_poseParameter;
    private readonly BoneScratch? m_scratch;

    /// <summary>
    /// <c>CBoneSetup::CBoneSetup</c> — oracle 2333-2339: stores the hdr,
    /// bone mask and pose-parameter array (by reference, as stock's pointer
    /// does — the caller owns the array and its updates are seen, matching
    /// stock's aliasing). Stock's fourth argument, <c>pPoseDebugger</c>, is
    /// CLIENT_DLL-only and dropped per the file header.
    /// </summary>
    public CBoneSetup(StudioHdr studioHdr, int boneMask, float[] poseParameter, BoneScratch? scratch = null)
    {
        m_pStudioHdr = studioHdr;
        m_boneMask = boneMask;
        m_poseParameter = poseParameter;
        m_scratch = scratch;
    }

    /// <summary>Stock's <c>m_pStudioHdr</c>, exposed for callers that build
    /// scratch over the same model (the size of every pose array).</summary>
    public StudioHdr StudioHdr => m_pStudioHdr;

    /// <summary>Stock's <c>m_boneMask</c>.</summary>
    public int BoneMask => m_boneMask;

    /// <summary>Stock's <c>m_flPoseParameter</c> (the aliased array).</summary>
    public float[] PoseParameter => m_poseParameter;

    // -----------------------------------------------------------------
    // InitPose thunk (stock IBoneSetup::InitPose 2305-2308)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>IBoneSetup::InitPose</c> — oracle 2305-2308: the one-line thunk
    /// into the free <c>::InitPose</c> with the setup's hdr and mask.
    /// </summary>
    public void InitPose(Vector[] pos, Quaternion[] q) =>
        BoneSetupBlend.InitPose(m_pStudioHdr, pos, q, m_boneMask);

    // -----------------------------------------------------------------
    // CalcBoneAdj thunk (stock IBoneSetup::CalcBoneAdj 2322-2326)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>IBoneSetup::CalcBoneAdj</c> — oracle 2322-2326: the one-line thunk
    /// into the free <c>::CalcBoneAdj</c> with the setup's hdr, its mask,
    /// and the caller's controller array (stock reads
    /// <c>m_pStudioHdr</c>/<c>m_boneMask</c>; the controllers arrive as the
    /// parameter they are in <c>IBoneSetup</c>).
    /// </summary>
    public void CalcBoneAdj(Vector[] pos, Quaternion[] q, ReadOnlySpan<float> controllers) =>
        BoneSetupAdj.CalcBoneAdj(m_pStudioHdr, pos, q, controllers, m_boneMask);

    // -----------------------------------------------------------------
    // CalcPoseSingle (stock 1922-2116)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CalcPoseSingle</c> — oracle 1922-2116: fills pos/q with one
    /// sequence's pose and returns whether any real (non-all-zeros)
    /// animation contributed. Resolves the two blend coordinates
    /// (<c>Studio_LocalPoseParameter</c>), fixes up the cycle
    /// (<c>STUDIO_REALTIME</c> from <c>flTime * CPS</c>,
    /// <c>STUDIO_CYCLEPOSE</c> from the shared pose parameter, else loop
    /// wrap or clamp), then walks the s0/s1 cell table: single animations,
    /// two-way blends, the zero-cell <c>ScaleBones</c> arms, and the
    /// mid-cell four-corner arm — bilinear two-step when
    /// <paramref name="animThreeWayBlend"/> is false (stock ConVar
    /// <c>anim_3wayblend</c>, oracle 1838/2066) or the triangle blend via
    /// <c>Calc3WayBlendIndices</c> when true. The four
    /// <c>g_VectorPool</c>/<c>g_QaternionPool</c> blocks and
    /// <c>CalcAnimation</c>'s matrix scratch are per-call rents (rows
    /// 136/137).
    /// <para>
    /// <paramref name="seqdesc"/> is passed by reference because stock
    /// reassigns the caller's reference when <c>sequence</c> is out of
    /// range (oracle 1941-1945).
    /// </para>
    /// </summary>
    public static bool CalcPoseSingle(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        ref MstudioSeqDesc seqdesc,
        int sequence,
        float cycle,
        float[] poseParameter,
        int boneMask,
        float flTime,
        bool animThreeWayBlend,
        float flStall = 0.0f,
        BoneScratch? scratchPool = null)
    {
        BoneScratch.Mark mark = scratchPool?.Save() ?? default;

        try
        {
            return CalcPoseSingleRented(pStudioHdr, pos, q, ref seqdesc, sequence, cycle, poseParameter, boneMask, flTime,
                                        animThreeWayBlend, flStall, scratchPool);
        }
        finally
        {
            scratchPool?.Restore(mark);
        }
    }

    private static bool CalcPoseSingleRented(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        ref MstudioSeqDesc seqdesc,
        int sequence,
        float cycle,
        float[] poseParameter,
        int boneMask,
        float flTime,
        bool animThreeWayBlend,
        float flStall,
        BoneScratch? scratchPool)
    {
        bool bResult = true;

        Vector[] pos2 = BoneScratch.Vectors(scratchPool);
        Quaternion[] q2 = BoneScratch.Quaternions(scratchPool);
        Vector[] pos3 = BoneScratch.Vectors(scratchPool);
        Quaternion[] q3 = BoneScratch.Quaternions(scratchPool);

        if (sequence >= pStudioHdr.NumSequences)
        {
            sequence = 0;
            seqdesc = pStudioHdr.SeqDesc(sequence);
        }

        int i0 = 0, i1 = 0;
        float s0 = 0, s1 = 0;

        BoneSetupHdrRead.LocalPoseParameter(pStudioHdr, poseParameter, seqdesc, sequence, 0, out s0, out i0);
        BoneSetupHdrRead.LocalPoseParameter(pStudioHdr, poseParameter, seqdesc, sequence, 1, out s1, out i1);

        if ((seqdesc.Flags & BoneSetupBlend.STUDIO_REALTIME) != 0)
        {
            float cps = BoneSetupHdrRead.Cps(pStudioHdr, seqdesc, sequence, poseParameter);
            cycle = flTime * cps;
            cycle = cycle - (int)cycle;
        }
        else if ((seqdesc.Flags & BoneSetupBlend.STUDIO_CYCLEPOSE) != 0)
        {
            int iPose = pStudioHdr.GetSharedPoseParameter(sequence, seqdesc.CyclePoseIndex);
            cycle = iPose != -1 ? poseParameter[iPose] : 0.0f;
        }
        else if (cycle < 0 || cycle >= 1)
        {
            if ((seqdesc.Flags & StudioConstants.STUDIO_LOOPING) != 0)
            {
                cycle = cycle - (int)cycle;
                if (cycle < 0)
                {
                    cycle += 1;
                }
            }
            else
            {
                cycle = Math.Clamp(cycle, 0.0f, 1.0f); // stock clamp(cycle, 0.f, 1.f)
            }
        }

        // CalcAnimation's matrix scratch (its stock pool block), shared by
        // this call's extractions; each extraction gets a fresh memo list.
        Span<BoneMatrix3x4> scratch = BoneScratch.MemoMatrices(scratchPool); // read only behind each extraction's bit list

        if (s0 < 0.001f)
        {
            if (s1 < 0.001f)
            {
                if (BoneSetupBlend.PoseIsAllZeros(pStudioHdr, sequence, seqdesc, i0, i1))
                {
                    bResult = false;
                }
                else
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask, flStall, scratch, scratchPool);
                }
            }
            else if (s1 > 0.999f)
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
            }
            else
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask, flStall, scratch, scratchPool);
                CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
            }
        }
        else if (s0 > 0.999f)
        {
            if (s1 < 0.001f)
            {
                if (BoneSetupBlend.PoseIsAllZeros(pStudioHdr, sequence, seqdesc, i0 + 1, i1))
                {
                    bResult = false;
                }
                else
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask, flStall, scratch, scratchPool);
                }
            }
            else if (s1 > 0.999f)
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
            }
            else
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask, flStall, scratch, scratchPool);
                CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
            }
        }
        else
        {
            if (s1 < 0.001f)
            {
                if (BoneSetupBlend.PoseIsAllZeros(pStudioHdr, sequence, seqdesc, i0 + 1, i1))
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask, flStall, scratch, scratchPool);
                    BoneSetupBlend.ScaleBones(pStudioHdr, q, pos, sequence, 1.0f - s0, boneMask);
                }
                else if (BoneSetupBlend.PoseIsAllZeros(pStudioHdr, sequence, seqdesc, i0, i1))
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask, flStall, scratch, scratchPool);
                    BoneSetupBlend.ScaleBones(pStudioHdr, q, pos, sequence, s0, boneMask);
                }
                else
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask, flStall, scratch, scratchPool);
                    CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask, flStall, scratch, scratchPool);

                    BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);
                }
            }
            else if (s1 > 0.999f)
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);
            }
            else if (!animThreeWayBlend)
            {
                CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask, flStall, scratch, scratchPool);
                CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask, flStall, scratch, scratchPool);
                BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);

                CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                CalcAnimation(pStudioHdr, pos3, q3, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask, flStall, scratch, scratchPool);
                BoneSetupBlend.BlendBones(pStudioHdr, q2, pos2, seqdesc, sequence, q3, pos3, s0, boneMask);

                BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
            }
            else
            {
                int[] iAnimIndices = new int[3];
                float[] weight = new float[3];

                BoneSetupBlend.Calc3WayBlendIndices(i0, i1, s0, s1, seqdesc, iAnimIndices, weight);

                if (weight[1] < 0.001f)
                {
                    // on diagonal
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, iAnimIndices[0], cycle, boneMask, flStall, scratch, scratchPool);
                    CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, iAnimIndices[2], cycle, boneMask, flStall, scratch, scratchPool);
                    BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, weight[2] / (weight[0] + weight[2]), boneMask);
                }
                else
                {
                    CalcAnimation(pStudioHdr, pos, q, seqdesc, sequence, iAnimIndices[0], cycle, boneMask, flStall, scratch, scratchPool);
                    CalcAnimation(pStudioHdr, pos2, q2, seqdesc, sequence, iAnimIndices[1], cycle, boneMask, flStall, scratch, scratchPool);
                    BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, weight[1] / (weight[0] + weight[1]), boneMask);

                    CalcAnimation(pStudioHdr, pos3, q3, seqdesc, sequence, iAnimIndices[2], cycle, boneMask, flStall, scratch, scratchPool);
                    BoneSetupBlend.BlendBones(pStudioHdr, q, pos, seqdesc, sequence, q3, pos3, weight[2], boneMask);
                }
            }
        }

        return bResult;
    }

    /// <summary>One <c>CalcAnimation</c> extraction with its own fresh
    /// memo list over the caller's shared matrix scratch (stock constructs
    /// a local <c>CBoneBitList</c> per call inside the pool block).</summary>
    private static void CalcAnimation(
        StudioHdr pStudioHdr, Vector[] pos, Quaternion[] q, MstudioSeqDesc seqdesc,
        int sequence, int animation, float cycle, int boneMask, float flStall,
        Span<BoneMatrix3x4> scratch, BoneScratch? scratchPool) =>
        BoneSetupExtract.CalcAnimation(
            pStudioHdr, pos, q, seqdesc, sequence, animation, cycle, boneMask,
            flStall, scratch, BoneScratch.BitList(scratchPool));

    // -----------------------------------------------------------------
    // AddSequenceLayers (stock 2125-2211)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CBoneSetup::AddSequenceLayers</c> — oracle 2125-2211: the
    /// non-<c>STUDIO_AL_LOCAL</c> autolayers. Per layer the ramp
    /// <c>s</c> indexes on the parent cycle (or, with
    /// <c>STUDIO_AL_POSE</c>, on the layer's shared pose parameter scaled
    /// to its range through <c>iRelativeSeq</c>/<c>GetSharedPoseParameter</c>);
    /// outside [start, end) the layer is skipped, the peak/tail sides ramp
    /// linearly, <c>STUDIO_AL_SPLINE</c> runs the ramp through
    /// <c>SimpleSpline</c>, and the weight is the XFADE ratio,
    /// <c>STUDIO_AL_NOBLEND</c>'s raw ramp, or <c>flWeight * s</c>. The
    /// layer cycle is remapped onto [0,1] over the layer window when the
    /// layer rides the parent cycle. Then <c>AccumulatePose</c> on the
    /// relative sequence.
    /// </summary>
    public void AddSequenceLayers(
        Vector[] pos,
        Quaternion[] q,
        MstudioSeqDesc seqdesc,
        int sequence,
        float cycle,
        float flWeight,
        float flTime,
        CIKContext? pIKContext,
        bool animThreeWayBlend,
        float flStall = 0.0f)
    {
        for (int i = 0; i < seqdesc.NumAutoLayers; i++)
        {
            MstudioAutoLayer? pLayer = seqdesc.PAutoLayer(i);
            if (pLayer is null)
            {
                break;
            }

            if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_LOCAL) != 0)
            {
                continue;
            }

            float layerCycle = cycle;
            float layerWeight = flWeight;

            if (pLayer.Start != pLayer.End)
            {
                float s = 1.0f;
                float index;

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_POSE) == 0)
                {
                    index = cycle;
                }
                else
                {
                    int iSequence = m_pStudioHdr.IRelativeSeq(sequence, pLayer.ISequence);
                    int iPose = m_pStudioHdr.GetSharedPoseParameter(iSequence, pLayer.IPose);
                    if (iPose != -1)
                    {
                        MstudioPoseParamDesc Pose = m_pStudioHdr.PoseParameter(iPose);
                        index = m_poseParameter[iPose] * (Pose.End - Pose.Start) + Pose.Start;
                    }
                    else
                    {
                        index = 0;
                    }
                }

                if (index < pLayer.Start)
                {
                    continue;
                }

                if (index >= pLayer.End)
                {
                    continue;
                }

                if (index < pLayer.Peak && pLayer.Start != pLayer.Peak)
                {
                    s = (index - pLayer.Start) / (pLayer.Peak - pLayer.Start);
                }
                else if (index > pLayer.Tail && pLayer.End != pLayer.Tail)
                {
                    s = (pLayer.End - index) / (pLayer.End - pLayer.Tail);
                }

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_SPLINE) != 0)
                {
                    s = BoneSetupMath.SimpleSpline(s);
                }

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_XFADE) != 0 && index > pLayer.Tail)
                {
                    layerWeight = (s * flWeight) / (1 - flWeight + s * flWeight);
                }
                else if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_NOBLEND) != 0)
                {
                    layerWeight = s;
                }
                else
                {
                    layerWeight = flWeight * s;
                }

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_POSE) == 0)
                {
                    layerCycle = (cycle - pLayer.Start) / (pLayer.End - pLayer.Start);
                }
            }

            int iSequence2 = m_pStudioHdr.IRelativeSeq(sequence, pLayer.ISequence);
            AccumulatePose(pos, q, iSequence2, layerCycle, layerWeight, flTime, pIKContext, animThreeWayBlend, flStall);
        }
    }

    // -----------------------------------------------------------------
    // AddLocalLayers (stock 2218-2286)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CBoneSetup::AddLocalLayers</c> — oracle 2218-2286: the
    /// <c>STUDIO_AL_LOCAL</c> twin, gated on the parent sequence's
    /// <c>STUDIO_LOCAL</c> flag. The ramp indexes on the parent cycle
    /// directly (no pose-parameter arm), same peak/tail/Spline/XFade/
    /// NoBlend weight arms, and the layer cycle is always remapped onto
    /// the layer window.
    /// </summary>
    public void AddLocalLayers(
        Vector[] pos,
        Quaternion[] q,
        MstudioSeqDesc seqdesc,
        int sequence,
        float cycle,
        float flWeight,
        float flTime,
        CIKContext? pIKContext,
        bool animThreeWayBlend,
        float flStall = 0.0f)
    {
        if ((seqdesc.Flags & BoneSetupBlend.STUDIO_LOCAL) == 0)
        {
            return;
        }

        for (int i = 0; i < seqdesc.NumAutoLayers; i++)
        {
            MstudioAutoLayer? pLayer = seqdesc.PAutoLayer(i);
            if (pLayer is null)
            {
                break;
            }

            if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_LOCAL) == 0)
            {
                continue;
            }

            float layerCycle = cycle;
            float layerWeight = flWeight;

            if (pLayer.Start != pLayer.End)
            {
                float s = 1.0f;

                if (cycle < pLayer.Start)
                {
                    continue;
                }

                if (cycle >= pLayer.End)
                {
                    continue;
                }

                if (cycle < pLayer.Peak && pLayer.Start != pLayer.Peak)
                {
                    s = (cycle - pLayer.Start) / (pLayer.Peak - pLayer.Start);
                }
                else if (cycle > pLayer.Tail && pLayer.End != pLayer.Tail)
                {
                    s = (pLayer.End - cycle) / (pLayer.End - pLayer.Tail);
                }

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_SPLINE) != 0)
                {
                    s = BoneSetupMath.SimpleSpline(s);
                }

                if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_XFADE) != 0 && cycle > pLayer.Tail)
                {
                    layerWeight = (s * flWeight) / (1 - flWeight + s * flWeight);
                }
                else if ((pLayer.Flags & BoneSetupBlend.STUDIO_AL_NOBLEND) != 0)
                {
                    layerWeight = s;
                }
                else
                {
                    layerWeight = flWeight * s;
                }

                layerCycle = (cycle - pLayer.Start) / (pLayer.End - pLayer.Start);
            }

            int iSequence = m_pStudioHdr.IRelativeSeq(sequence, pLayer.ISequence);
            AccumulatePose(pos, q, iSequence, layerCycle, layerWeight, flTime, pIKContext, animThreeWayBlend, flStall);
        }
    }

    // -----------------------------------------------------------------
    // AccumulatePose (stock 2393-2457; thunk rows 32-36 land here)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CBoneSetup::AccumulatePose</c> — oracle 2393-2457: accumulates
    /// one sequence over an existing pose. Clamps <c>flWeight</c> to
    /// [0,1] (stock's Assert-and-clamp; stock's <c>Assert(flWeight ≥ 0
    /// ≤ 1)</c> is a release no-op), rejects negative sequences, seeds the
    /// scratch pose when the sequence is <c>STUDIO_LOCAL</c>, extracts via
    /// <see cref="CalcPoseSingle"/>, adds the local layers at weight 1,
    /// then <c>SlerpBones</c> merges the scratch into pos/q at
    /// <c>flWeight</c> (stock's comment: "this weight is wrong, the IK
    /// rules won't composite at the correct intensity"). IK locks
    /// (<c>AddSequenceLocks</c> before, <c>SolveSequenceLocks</c> after,
    /// on a private <c>CIKContext</c> initialised at identity — stock's
    /// <c>vec3_angle, vec3_origin, 0.0, 0</c> Init) bracket the pose when
    /// the sequence has locks; dependencies join the caller's context in
    /// between, then the sequence layers accumulate directly on pos/q.
    /// <para>
    /// The pose-debugger arm (oracle 2413-2419) is CLIENT_DLL: absent here,
    /// per the file header.
    /// </para>
    /// <para>
    /// Stock's stack <c>pos2[MAXSTUDIOBONES]</c>/<c>q2[MAXSTUDIOBONES]</c>
    /// and the <c>CIKContext seq_ik</c> are locals; the arrays are rented
    /// (rows 136/137).
    /// </para>
    /// </summary>
    public void AccumulatePose(
        Vector[] pos,
        Quaternion[] q,
        int sequence,
        float cycle,
        float flWeight,
        float flTime,
        CIKContext? pIKContext,
        bool animThreeWayBlend,
        float flStall = 0.0f)
    {
        BoneScratch.Mark mark = m_scratch?.Save() ?? default;

        try
        {
            AccumulatePoseRented(pos, q, sequence, cycle, flWeight, flTime, pIKContext, animThreeWayBlend, flStall);
        }
        finally
        {
            m_scratch?.Restore(mark);
        }
    }

    private void AccumulatePoseRented(
        Vector[] pos,
        Quaternion[] q,
        int sequence,
        float cycle,
        float flWeight,
        float flTime,
        CIKContext? pIKContext,
        bool animThreeWayBlend,
        float flStall)
    {
        Vector[] pos2 = BoneScratch.Vectors(m_scratch);
        Quaternion[] q2 = BoneScratch.Quaternions(m_scratch);

        // Assert( flWeight >= 0.0f && flWeight <= 1.0f );
        // This shouldn't be necessary, but the Assert should help us catch
        // whoever is screwing this up (oracle 2406-2408).
        flWeight = Math.Clamp(flWeight, 0.0f, 1.0f);

        if (sequence < 0)
        {
            return;
        }

        // #ifdef CLIENT_DLL pose-debugger arm — never compiled in the
        // server build (oracle 2413-2419); see the file header.

        MstudioSeqDesc seqdesc = m_pStudioHdr.SeqDesc(sequence);

        // add any IK locks to prevent extremities from moving
        CIKContext? seqIk = null;
        if (seqdesc.NumIKLocks != 0)
        {
            seqIk = BoneScratch.IkContext(m_scratch);
            // local space relative so absolute position doesn't matter
            // (stock's Init(vec3_angle, vec3_origin, 0.0, 0)).
            // INTEGRATE: CIKContext (parallel lane)
            seqIk.Init(m_pStudioHdr, BoneMatrix3x4.Identity, m_boneMask, 0.0f, 0);
            // INTEGRATE: CIKContext (parallel lane)
            seqIk.AddSequenceLocks(sequence, pos, q);
        }

        if ((seqdesc.Flags & BoneSetupBlend.STUDIO_LOCAL) != 0)
        {
            BoneSetupBlend.InitPose(m_pStudioHdr, pos2, q2, m_boneMask);
        }

        if (CalcPoseSingle(m_pStudioHdr, pos2, q2, ref seqdesc, sequence, cycle, m_poseParameter, m_boneMask, flTime, animThreeWayBlend, flStall, m_scratch))
        {
            // this weight is wrong, the IK rules won't composite at the
            // correct intensity (stock comment, oracle 2438)
            AddLocalLayers(pos2, q2, seqdesc, sequence, cycle, 1.0f, flTime, pIKContext, animThreeWayBlend, flStall);
            BoneSetupBlend.SlerpBones(m_pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, flWeight, m_boneMask, m_scratch);
        }

        if (pIKContext is not null)
        {
            // INTEGRATE: CIKContext (parallel lane)
            pIKContext.AddDependencies(sequence, m_poseParameter, flWeight, cycle);
        }

        AddSequenceLayers(pos, q, seqdesc, sequence, cycle, flWeight, flTime, pIKContext, animThreeWayBlend, flStall);

        if (seqdesc.NumIKLocks != 0)
        {
            // INTEGRATE: CIKContext (parallel lane)
            seqIk!.SolveSequenceLocks(sequence, pos, q);
        }
    }

    // -----------------------------------------------------------------
    // CalcAutoplaySequences (stock 4457-4493)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>CBoneSetup::CalcAutoplaySequences</c> — oracle 4457-4493: locks
    /// the autoplay chains on the incoming pose, then accumulates every
    /// <c>STUDIO_AUTOPLAY</c> sequence of the autoplay list at weight 1,
    /// its cycle the fractional part of <c>flRealTime * CPS</c>, and
    /// finally solves the autoplay locks.
    /// <para>
    /// <c>GetAutoplayList</c> (studio_generic_io.cpp:110) caches the list
    /// in a file-global rbtree keyed on the hdr pointer — static state
    /// SS0001 forbids. The port's header holds its own
    /// (<see cref="StudioHdr.AutoplayList"/>, read once), and stock's
    /// virtual-model arm (which reads the fold's precomputed
    /// <c>m_autoplaySequences</c>) is matched by scanning the fold-routed
    /// global sequence table.
    /// </para>
    /// </summary>
    public void CalcAutoplaySequences(
        Vector[] pos,
        Quaternion[] q,
        float flRealTime,
        CIKContext? pIKContext,
        bool animThreeWayBlend,
        float flStall = 0.0f)
    {
        // INTEGRATE: CIKContext (parallel lane)
        pIKContext?.AddAutoplayLocks(pos, q);

        ReadOnlySpan<ushort> pList = m_pStudioHdr.AutoplayList();
        for (int i = 0; i < pList.Length; i++)
        {
            int sequenceIndex = pList[i];
            MstudioSeqDesc seqdesc = m_pStudioHdr.SeqDesc(sequenceIndex);
            if ((seqdesc.Flags & BoneSetupBlend.STUDIO_AUTOPLAY) != 0)
            {
                float cps = BoneSetupHdrRead.Cps(m_pStudioHdr, seqdesc, sequenceIndex, m_poseParameter);
                float cycle = flRealTime * cps;
                cycle = cycle - (int)cycle;

                AccumulatePose(pos, q, sequenceIndex, cycle, 1.0f, flRealTime, pIKContext, animThreeWayBlend, flStall);
            }
        }

        // INTEGRATE: CIKContext (parallel lane)
        pIKContext?.SolveAutoplayLocks(pos, q);
    }
}
