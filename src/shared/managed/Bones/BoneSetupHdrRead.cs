//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The hdr-read cluster of <c>bone_setup.cpp</c>, ported to C#:
/// <c>Studio_LocalPoseParameter</c>, <c>Studio_SeqAnims</c>, <c>Studio_CPS</c>,
/// <c>Studio_Duration</c>, <c>Studio_FindAttachment</c> and
/// <c>Studio_CalcDefaultPoseParameters</c>.
///
/// <para>
/// These are pure functions over the <see cref="StudioHdr"/> image and
/// caller-owned pose-parameter arrays: no clock, no convar, no static state
/// (SS0001). Stock's entity/engine reads (<c>entindex</c>,
/// <c>GetModelPtr</c>) never appear in this cluster; the pose-parameter array
/// — stock's <c>CBoneSetup</c> member — arrives as a parameter, as do the
/// resolved seqdesc/sequence index.
/// </para>
///
/// <para>
/// Server-scope divergences (the inventory's ruling, kept native until a
/// client consumer needs them): <c>Studio_SetController</c> /
/// <c>Studio_GetController</c>, <c>Studio_SetPoseParameter</c> /
/// <c>Studio_GetPoseParameter</c>, <c>Studio_MaxFrame</c>, <c>Studio_FPS</c>,
/// the movement family (<c>Studio_AnimPosition</c>,
/// <c>Studio_AnimVelocity</c>, <c>Studio_AnimMovement</c>,
/// <c>Studio_FindAnimDistance</c>, <c>Studio_SeqMovement</c>,
/// <c>Studio_SeqVelocity</c>, <c>Studio_FindSeqDistance</c>),
/// <c>Studio_FindRandomAttachment</c> and <c>Studio_BoneIndexByName</c>.
/// <c>Studio_GetKeyValueText</c>, <c>Studio_RunBoneFlexDrivers</c>,
/// <c>Studio_GetDefaultSurfaceProps</c>, <c>Studio_GetMass</c> and
/// <c>Studio_PrefetchSequence</c> are ported here (the A3 hdr-read lane);
/// the controller/pose-parameter and movement families live in
/// <c>BoneSetupQuery.cs</c>.
/// </para>
///
/// <para>
/// Stock's <c>ss::StrICmp</c> (our <c>ss_string.h</c>, an ASCII fold of
/// <c>V_stricmp</c>) becomes <see cref="string.Equals(string, string,
/// StringComparison)"/> with <see cref="StringComparison.OrdinalIgnoreCase"/>,
/// per the inventory's ruling. Stock's
/// <c>SequencesAvailable()</c> guard in <c>Studio_FindAttachment</c> is
/// always true for a constructed <see cref="StudioHdr"/> —
/// <c>TryCreate</c> fails a hdr whose includes do not resolve, so a live
/// reader has its fold — and the port keeps that fact as the divergence note
/// rather than a runtime check.
/// </para>
/// </summary>
public static class BoneSetupHdrRead
{
    /// <summary><c>studio.h:85</c> — <c>MAXSTUDIOPOSEPARAM</c>. (Pending
    /// promotion into <c>StudioConstants</c>; see needs-hdrread.md.)</summary>
    private const int MAXSTUDIOPOSEPARAM = 24;

    // -----------------------------------------------------------------
    // Studio_LocalPoseParameter (stock 1682-1768)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_LocalPoseParameter( const CStudioHdr *, const float
    /// poseParameter[], mstudioseqdesc_t &amp;, int iSequence, int
    /// iLocalIndex, float &amp;flSetting, int &amp;index )</c> — oracle
    /// 1682-1768. Resolves the sequence's local pose-key axis
    /// <paramref name="iLocalIndex"/> to a (setting, index) pair against the
    /// shared pose parameter: loop-wrapped by the poseparam's
    /// <c>loop</c> period (oracle 1703-1708, the stock
    /// <c>flValue - loop * floor((flValue + shift) / loop)</c> form), then
    /// either the linear blend-table arm (posekeyindex 0: local
    /// paramstart/paramend window, clamp to [0,1], and for groupsize &gt; 2
    /// the index estimate with the last-cell pull-back) or the pose-key
    /// search arm (advance <c>index</c> while the segment blend exceeds 1,
    /// then clamp). A null hdr or a shared pose parameter of -1 answers
    /// (0, 0) like stock's early arms.
    /// </summary>
    public static void LocalPoseParameter(
        StudioHdr? hdr,
        float[] poseParameter,
        MstudioSeqDesc seqdesc,
        int iSequence,
        int iLocalIndex,
        out float flSetting,
        out int index)
    {
        if (hdr is null)
        {
            // oracle 1684-1689
            flSetting = 0;
            index = 0;
            return;
        }

        int iPose = hdr.GetSharedPoseParameter(iSequence, ParamIndex(seqdesc, iLocalIndex));

        if (iPose == -1)
        {
            // oracle 1691-1696
            flSetting = 0;
            index = 0;
            return;
        }

        MstudioPoseParamDesc Pose = hdr.PoseParameter(iPose);

        float flValue = poseParameter[iPose];

        // oracle 1703-1708: if (Pose.loop) { wrap = (start+end)/2 + loop/2;
        //                              shift = loop - wrap;
        //                    flValue -= loop * floor((flValue + shift) / loop); }
        if (Pose.Loop != 0.0f)
        {
            float wrap = (Pose.Start + Pose.End) / 2.0f + Pose.Loop / 2.0f;
            float shift = Pose.Loop - wrap;

            flValue = flValue - Pose.Loop * Floor((flValue + shift) / Pose.Loop);
        }

        int groupSize = GroupSize(seqdesc, iLocalIndex);

        if (seqdesc.PoseKeyIndex == 0)
        {
            // oracle 1710-1733: the linear arm
            float flLocalStart = (seqdesc.ParamStart(iLocalIndex) - Pose.Start) / (Pose.End - Pose.Start);
            float flLocalEnd = (seqdesc.ParamEnd(iLocalIndex) - Pose.Start) / (Pose.End - Pose.Start);

            // convert into local range
            flSetting = (flValue - flLocalStart) / (flLocalEnd - flLocalStart);

            // clamp.  This shouldn't ever need to happen if it's looping.
            if (flSetting < 0)
            {
                flSetting = 0;
            }

            if (flSetting > 1)
            {
                flSetting = 1;
            }

            index = 0;
            if (groupSize > 2)
            {
                // estimate index
                index = (int)(flSetting * (groupSize - 1));
                if (index == groupSize - 1)
                {
                    index = groupSize - 2;
                }

                flSetting = flSetting * (groupSize - 1) - index;
            }
        }
        else
        {
            // oracle 1734-1767: the pose-key arm.  flValue is promoted to
            // the poseparam's own range, then the segment search walks
            // `index` forward while the blend exceeds 1 (stock's commented
            // backward arm is dead code and stays out).
            flValue = flValue * (Pose.End - Pose.Start) + Pose.Start;
            index = 0;

            while (true)
            {
                float key = seqdesc.PoseKey(iLocalIndex, index);
                flSetting = (flValue - key) / (seqdesc.PoseKey(iLocalIndex, index + 1) - key);

                if (index < groupSize - 2 && flSetting > 1.0f)
                {
                    index++;
                    continue;
                }

                break;
            }

            // clamp.
            if (flSetting < 0.0f)
            {
                flSetting = 0.0f;
            }

            if (flSetting > 1.0f)
            {
                flSetting = 1.0f;
            }
        }
    }

    // -----------------------------------------------------------------
    // Studio_SeqAnims (stock 5441-5471)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_SeqAnims( const CStudioHdr *, mstudioseqdesc_t &amp;, int
    /// iSequence, const float poseParameter[], mstudioanimdesc_t
    /// *panim[4], float *weight )</c> — oracle 5441-5471. Resolves the four
    /// blend-cell anims and their bilinear weights from the two local pose
    /// axes. A null hdr or <c>iSequence</c> at/past
    /// <see cref="StudioHdr.NumSequences"/> zeroes all four weights (stock
    /// leaves <c>panim</c> untouched there; the port answers null entries so
    /// no caller can walk a stale array). Each entry is
    /// <c>pAnimdesc(iRelativeAnim(iSequence, seqdesc.anim(x, y)))</c> — the
    /// fold's group remap composed with the blend-table lookup (whose own
    /// clamps live in <see cref="MstudioSeqDesc.Anim"/>).
    /// </summary>
    public static void SeqAnims(
        StudioHdr hdr,
        MstudioSeqDesc seqdesc,
        int iSequence,
        float[] poseParameter,
        out MstudioAnimDesc[] panim,
        out float[] weight)
    {
        SeqAnims(hdr, seqdesc, iSequence, poseParameter, out SeqAnimCell cell);

        panim = new MstudioAnimDesc[4];
        weight = new float[4];

        for (int i = 0; i < 4; i++)
        {
            panim[i] = cell.Anim(i)!;
            weight[i] = cell.Weight(i);
        }
    }

    /// <summary>
    /// <see cref="SeqAnims(StudioHdr, MstudioSeqDesc, int, float[], out MstudioAnimDesc[], out float[])"/>
    /// into a value cell: no arrays, for the readers a build runs every
    /// pass (Studio_CPS; plan_physics_bones_perf A3).
    /// </summary>
    internal static void SeqAnims(
        StudioHdr hdr,
        MstudioSeqDesc seqdesc,
        int iSequence,
        float[] poseParameter,
        out SeqAnimCell cell)
    {
        cell = default;

        if (hdr is null || iSequence >= hdr.NumSequences)
        {
            // oracle 5445-5449: weight[0..3] = 0
            return;
        }

        int i0 = 0, i1 = 0;
        float s0 = 0, s1 = 0;

        LocalPoseParameter(hdr, poseParameter, seqdesc, iSequence, 0, out s0, out i0);
        LocalPoseParameter(hdr, poseParameter, seqdesc, iSequence, 1, out s1, out i1);

        cell.A0 = hdr.AnimDesc(hdr.IRelativeAnim(iSequence, seqdesc.Anim(i0, i1)));
        cell.W0 = (1 - s0) * (1 - s1);

        cell.A1 = hdr.AnimDesc(hdr.IRelativeAnim(iSequence, seqdesc.Anim(i0 + 1, i1)));
        cell.W1 = (s0) * (1 - s1);

        cell.A2 = hdr.AnimDesc(hdr.IRelativeAnim(iSequence, seqdesc.Anim(i0, i1 + 1)));
        cell.W2 = (1 - s0) * (s1);

        cell.A3 = hdr.AnimDesc(hdr.IRelativeAnim(iSequence, seqdesc.Anim(i0 + 1, i1 + 1)));
        cell.W3 = (s0) * (s1);

        // oracle 5470: Assert(weights >= 0) — the clamp above guarantees it;
        // stock's debug assert is a no-op in the port.
    }

    /// <summary>The four blend-cell anims and their weights, as values.</summary>
    internal struct SeqAnimCell
    {
        public MstudioAnimDesc? A0, A1, A2, A3;
        public float W0, W1, W2, W3;

        public readonly MstudioAnimDesc? Anim(int i) => i switch { 0 => A0, 1 => A1, 2 => A2, _ => A3 };

        public readonly float Weight(int i) => i switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };
    }

    // -----------------------------------------------------------------
    // Studio_CPS (stock 5532-5549)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_CPS( const CStudioHdr *, mstudioseqdesc_t &amp;, int
    /// iSequence, const float poseParameter[] )</c> — oracle 5532-5549. The
    /// weighted frames-per-second over the four blend anims: a cell counts
    /// only with positive weight and <c>numframes &gt; 1</c>, contributing
    /// <c>fps / (numframes - 1) * weight</c>.
    /// </summary>
    public static float Cps(
        StudioHdr hdr, MstudioSeqDesc seqdesc, int iSequence, float[] poseParameter)
    {
        SeqAnims(hdr, seqdesc, iSequence, poseParameter, out SeqAnimCell cell);

        float t = 0;

        for (int i = 0; i < 4; i++)
        {
            // oracle 5541: stock derefs panim[i] unguarded, which only the
            // weight > 0 short-circuit keeps safe — same order here.
            float weight = cell.Weight(i);
            MstudioAnimDesc? panim = cell.Anim(i);

            if (weight > 0 && panim!.NumFrames > 1)
            {
                t += (panim.Fps / (panim.NumFrames - 1)) * weight;
            }
        }

        return t;
    }

    // -----------------------------------------------------------------
    // Studio_Duration (stock 5555-5564)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_Duration( const CStudioHdr *, int iSequence, const float
    /// poseParameter[] )</c> — oracle 5555-5564. Seconds per cycle:
    /// <c>1 / CPS</c>, and 0 when CPS is 0. Like stock, the seqdesc is read
    /// before the guard — stock's <c>pSeqdesc(iSequence)</c> is
    /// unguarded too; callers pass a resolved sequence.
    /// </summary>
    public static float Duration(StudioHdr hdr, int iSequence, float[] poseParameter)
    {
        MstudioSeqDesc seqdesc = hdr.SeqDesc(iSequence);
        float cps = Cps(hdr, seqdesc, iSequence, poseParameter);

        if (cps == 0)
        {
            return 0.0f;
        }

        return 1.0f / cps;
    }

    // -----------------------------------------------------------------
    // Studio_FindAttachment (stock 5842-5857)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_FindAttachment( const CStudioHdr *, const char
    /// *pAttachmentName )</c> — oracle 5842-5857. First attachment whose name
    /// matches case-insensitively; -1 when none does. Stock compares with our
    /// ASCII-only <c>ss::StrICmp</c> (ss_string.h:187, folds only A-Z via
    /// <c>AsciiLower</c>), so the port mirrors that byte fold rather than
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which also folds
    /// non-ASCII letters stock leaves alone (the sibling control is
    /// <c>EffectDispatchTableTests.OnlyAsciiLettersFoldCase</c>). Attachment
    /// names are read byte-per-char, matching the byte loop. The loop bound
    /// is <c>hdr.NumGlobalAttachments</c> — the CStudioHdr-routed count, which
    /// for a folded (virtual-model) hdr is the merged attachment list, as
    /// stock's <c>GetNumAttachments()</c> answers. The
    /// <c>SequencesAvailable()</c> precondition holds for every constructed
    /// <see cref="StudioHdr"/> (see the class doc), so the only null-arm
    /// left is stock's null-hdr answer of -1. An empty name matches an
    /// attachment that itself has the empty name — stock's loop compares
    /// <c>"" </c>to <c>pszName()</c> too, and the first such attachment
    /// index is the answer.
    /// </summary>
    public static int FindAttachment(StudioHdr? hdr, string pAttachmentName)
    {
        if (hdr is null)
        {
            return -1;
        }

        int count = hdr.NumGlobalAttachments;
        for (int i = 0; i < count; i++)
        {
            if (AsciiEquals(pAttachmentName, hdr.Attachment(i).Name))
            {
                return i;
            }
        }

        return -1;
    }

    // -----------------------------------------------------------------
    // Studio_CalcDefaultPoseParameters (stock 5070-5091)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_CalcDefaultPoseParameters( const CStudioHdr *, float
    /// flPoseParameter[], int nCount )</c> — oracle 5070-5091. Seeds the
    /// pose-parameter array: 0.5 everywhere, then per parameter the zero
    /// state <c>-start / (end - start)</c> when its range straddles zero
    /// (<c>start &lt; 0 &amp;&amp; end &gt; 0</c>). Indices past the model's
    /// parameter count keep the 0.5 default; the fill is bounded by both
    /// <paramref name="nCount"/> and <c>MAXSTUDIOPOSEPARAM</c> like stock's
    /// <c>MIN(nCount, MAXSTUDIOPOSEPARAM)</c>.
    /// </summary>
    public static void CalcDefaultPoseParameters(
        StudioHdr hdr, float[] flPoseParameter, int nCount)
    {
        int nPoseCount = hdr.NumPoseParameters;
        int nNumParams = Math.Min(nCount, MAXSTUDIOPOSEPARAM);

        for (int i = 0; i < nNumParams; ++i)
        {
            // Default to middle of the pose parameter range
            flPoseParameter[i] = 0.5f;
            if (i < nPoseCount)
            {
                MstudioPoseParamDesc Pose = hdr.PoseParameter(i);

                // Want to try for a zero state.  If one doesn't exist set it to .5 by default.
                if (Pose.Start < 0.0f && Pose.End > 0.0f)
                {
                    float flPoseDelta = Pose.End - Pose.Start;
                    flPoseParameter[i] = -Pose.Start / flPoseDelta;
                }
            }
        }
    }
    // -----------------------------------------------------------------
    // Studio_GetKeyValueText (stock 5936-5946 / ours 6095-6107)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_GetKeyValueText( const CStudioHdr *, int iSequence )</c> —
    /// our 6095-6107: the per-sequence keyvalue blob, or null.
    /// </summary>
    /// <remarks>
    /// Arms in stock order: null hdr → null (stock's own guard); a sequence
    /// outside <c>[0, GetNumSeq())</c> → null; else
    /// <c>pSeqdesc(i).KeyValueText()</c>, which is itself null when the
    /// record's <c>keyvaluesize</c> is 0 — so the port tells "no blob" from
    /// "empty blob" exactly as stock hands NULL to the
    /// <c>KeyValues::LoadFromBuffer</c> caller. The hdr-level @312/@316
    /// blob is a different table (<see cref="StudioHdr.KeyValueText()"/>),
    /// and stock's <c>SequencesAvailable()</c> guard is always true for a
    /// constructed <see cref="StudioHdr"/> (the file's standing divergence
    /// note), so it is not re-checked here.
    /// </remarks>
    public static string? GetKeyValueText(StudioHdr? pStudioHdr, int iSequence)
    {
        if (pStudioHdr is null)
        {
            return null;
        }

        if (iSequence >= 0 && iSequence < pStudioHdr.NumSequences)
        {
            return pStudioHdr.SeqDesc(iSequence).KeyValueText();
        }

        return null;
    }

    // -----------------------------------------------------------------
    // Studio_GetDefaultSurfaceProps / Studio_GetMass (stock 6077-6089 ours)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_GetDefaultSurfaceProps( CStudioHdr *pstudiohdr )</c> — our
    /// 6077-6081: one line, <c>pstudiohdr-&gt;pszSurfaceProp()</c>, which
    /// physics_shared answers from. Stock has no null guard (a null hdr is
    /// its UB), so the port takes a resolved hdr like its siblings
    /// (<see cref="Cps"/>); the reader's standing <c>CString(0)</c>
    /// convention (NR-05) answers the empty string for a QC with no
    /// <c>$surfaceprop</c>, where stock reads garbage header bytes.
    /// </summary>
    public static string GetDefaultSurfaceProps(StudioHdr pstudiohdr) =>
        pstudiohdr.SurfacePropText();

    /// <summary>
    /// <c>Studio_GetMass( CStudioHdr *pstudiohdr )</c> — our 6084-6088:
    /// <c>pstudiohdr-&gt;mass()</c>, the studiohdr float at @328, which
    /// physics_shared answers from. No null guard in stock: resolved-hdr
    /// convention as in <see cref="GetDefaultSurfaceProps"/>.
    /// </summary>
    public static float GetMass(StudioHdr pstudiohdr) => pstudiohdr.Mass;

    // -----------------------------------------------------------------
    // Studio_PrefetchSequence (stock 6109-6133 ours)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_PrefetchSequence( const CStudioHdr *pStudioHdr, int
    /// iSequence )</c> — our 6109-6133: walks every blend cell of the
    /// sequence and asks whether each cell's anim block is resident;
    /// returns true when the whole sequence loads without a stall. Stock's
    /// <c>pAnim</c> would block on the async loader — the managed reader has
    /// no IO (every answer is local or missing, per the reader's standing
    /// note on <c>PAnim</c>), so "pending" is exactly the block the reader
    /// cannot resolve, and the answer is <c>!pendingload</c> like stock's.
    /// </summary>
    /// <remarks>
    /// Arms in stock order: the seqdesc is read UNGUARDED — stock's
    /// <c>pSeqdesc(iSequence)</c> is unguarded too (like
    /// <see cref="Duration"/>'s standing note); callers pass a resolved
    /// sequence. The walk is the <c>groupsize[0] × groupsize[1]</c> double
    /// loop over <c>seqdesc.anim(i, j)</c> — the raw blend-table cell with
    /// <c>MstudioSeqDesc.Anim</c>'s own stock clamps, indexed straight into
    /// the animdesc table as stock does (no iRelativeAnim compose: stock's
    /// prefetch reads <c>seqdesc.anim(i, j)</c> plain, bone_setup.cpp
    /// ours 6119); each cell's anim block is probed at frame 0 like stock's
    /// <c>int iFrame = 0; pAnim(&amp;iFrame)</c> — the per-cell
    /// frame variable is not threaded between cells.
    /// </remarks>
    public static bool PrefetchSequence(StudioHdr pStudioHdr, int iSequence)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        bool pendingLoad = false;

        int size0 = seqdesc.GroupSize0;
        int size1 = seqdesc.GroupSize1;
        for (int i = 0; i < size0; i++)
        {
            for (int j = 0; j < size1; j++)
            {
                MstudioAnimDesc animdesc = pStudioHdr.AnimDesc(
                    seqdesc.Anim(i, j));
                int iFrame = 0;
                if (pStudioHdr.PAnim(animdesc, ref iFrame) is null)
                {
                    pendingLoad = true;
                }
            }
        }

        // Everything for this sequence is resident?
        return !pendingLoad;
    }

    // -----------------------------------------------------------------
    // Studio_RunBoneFlexDrivers (stock 5976-6048 / ours 6139-6211)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_RunBoneFlexDrivers( float *pflFlexControllerWeights, const
    /// CStudioHdr *, const Vector *pvPositions, const matrix3x4_t
    /// *pBoneToWorld, const matrix3x4_t &amp;mRootToWorld )</c> — our
    /// 6139-6211: each <c>mstudioboneflexdriver_t</c> projects one component
    /// of its bone onto <c>[min, max] → [0, 1]</c> into the flex weight at
    /// its controller's <c>localToGlobal</c> slot.
    /// </summary>
    /// <remarks>
    /// The arms, in stock order:
    /// <list type="bullet">
    /// <item>the walk is over <c>BoneFlexDriverCount()</c>, which answers 0
    /// without a studiohdr2 — the loop then never turns (pinned by the
    /// zero-count reader fact).</item>
    /// <item>a bone with <c>BONE_USED_BY_BONE_MERGE</c> has no usable local
    /// space, so its local matrix is recomputed: parentless bones from the
    /// lazily-inverted <paramref name="rootToWorld"/> (one invert per call,
    /// stock's <c>bRootToWorldInvComputed</c> flag), others from
    /// <c>MatrixInvert(boneToWorld[parent])</c>. Stock's
    /// <c>MatrixMultiply</c> is the literal alias of
    /// <c>ConcatTransforms</c> (mathlib.h:560-563), so both products use
    /// <see cref="BoneSetupMath.ConcatTransforms"/>; the extracted
    /// component is <c>mBoneLocal[component][3]</c>, the row's W.</item>
    /// <item>any other bone reads <c>pvPositions[boneIndex]</c> directly.</item>
    /// <item>per control: the controller resolves in the BASE hdr table
    /// (stock's <c>pFlexcontroller</c> is the hdr's own pair @268/@272 — not
    /// the virtual-model fold); <c>localToGlobal &lt; 0</c> skips the
    /// control; the write target is the controller's own
    /// <c>localToGlobal</c> slot.</item>
    /// </list>
    /// <para>Malformed-data arms (NR-01/NR-02 in needs-hdrread.md): stock
    /// derefs a NULL driver record and an out-of-range
    /// <c>m_nBoneComponent</c> (its asserts compile out in release); the
    /// port skips a null driver — the same NULL the reader already answers
    /// — and throws on a component outside {0,1,2}, which stock reads past
    /// the matrix into the adjacent matrix of the array. A well-formed mdl
    /// cannot reach either arm, so neither is pinned against stock.</para>
    /// </remarks>
    public static void RunBoneFlexDrivers(
        float[] pflFlexControllerWeights,
        StudioHdr pStudioHdr,
        Vector[] pvPositions,
        BoneMatrix3x4[] pBoneToWorld,
        BoneMatrix3x4 mRootToWorld)
    {
        bool rootToWorldInvComputed = false;
        BoneMatrix3x4 rootToWorldInv = default;

        int nBoneFlexDriverCount = pStudioHdr.BoneFlexDriverCount;
        for (int i = 0; i < nBoneFlexDriverCount; i++)
        {
            MstudioBoneFlexDriver? pBoneFlexDriver = pStudioHdr.BoneFlexDriver(i);
            if (pBoneFlexDriver is null)
            {
                // NR-02: stock derefs NULL here (UB); the reader's NULL arm
                // is a malformed table the port skips.
                continue;
            }

            int boneIndex = pBoneFlexDriver.BoneIndex;
            int nControllerCount = pBoneFlexDriver.ControlCount;

            if ((pStudioHdr.BoneFlags(boneIndex) & StudioConstants.BONE_USED_BY_BONE_MERGE) != 0)
            {
                // The local space of a bone-merged bone is not available, so
                // recompute it from boneToWorld (stock's comment, 6152-6154).
                BoneMatrix3x4 mBoneLocal;
                int parent = pStudioHdr.BoneParent(boneIndex);
                if (parent < 0)
                {
                    if (!rootToWorldInvComputed)
                    {
                        rootToWorldInv = BoneSetupMath.MatrixInvert(mRootToWorld);
                        rootToWorldInvComputed = true;
                    }

                    mBoneLocal = BoneSetupMath.ConcatTransforms(
                        rootToWorldInv, pBoneToWorld[boneIndex]);
                }
                else
                {
                    BoneMatrix3x4 parentInv = BoneSetupMath.MatrixInvert(pBoneToWorld[parent]);
                    mBoneLocal = BoneSetupMath.ConcatTransforms(
                        parentInv, pBoneToWorld[boneIndex]);
                }

                for (int j = 0; j < nControllerCount; j++)
                {
                    MstudioBoneFlexDriverControl pController = pBoneFlexDriver.Control(j);
                    MstudioFlexController pFlexController =
                        pStudioHdr.FlexController(pController.FlexControllerIndex);
                    if (pFlexController.LocalToGlobal < 0)
                    {
                        continue;
                    }

                    pflFlexControllerWeights[pFlexController.LocalToGlobal] =
                        RemapValClamped(
                            MatrixComponent(mBoneLocal, pController.BoneComponent),
                            pController.Min, pController.Max, 0.0f, 1.0f);
                }
            }
            else
            {
                // Non-merged bones: the local-space position directly.
                Vector position = pvPositions[boneIndex];
                for (int j = 0; j < nControllerCount; j++)
                {
                    MstudioBoneFlexDriverControl pController = pBoneFlexDriver.Control(j);
                    MstudioFlexController pFlexController =
                        pStudioHdr.FlexController(pController.FlexControllerIndex);
                    if (pFlexController.LocalToGlobal < 0)
                    {
                        continue;
                    }

                    pflFlexControllerWeights[pFlexController.LocalToGlobal] =
                        RemapValClamped(
                            VectorComponent(position, pController.BoneComponent),
                            pController.Min, pController.Max, 0.0f, 1.0f);
                }
            }
        }
    }


    // -----------------------------------------------------------------
    // private hdr-byte helpers (pending promotion; see needs-hdrread.md)
    // -----------------------------------------------------------------

    /// <summary><c>seqdesc.paramindex[iLocalIndex]</c> — the reader exposes
    /// the two halves as <c>ParamIndex0/1</c>.</summary>
    private static int ParamIndex(MstudioSeqDesc seqdesc, int iLocalIndex) =>
        iLocalIndex == 0 ? seqdesc.ParamIndex0 : seqdesc.ParamIndex1;

    /// <summary><c>seqdesc.groupsize[iLocalIndex]</c>.</summary>
    private static int GroupSize(MstudioSeqDesc seqdesc, int iLocalIndex) =>
        iLocalIndex == 0 ? seqdesc.GroupSize0 : seqdesc.GroupSize1;


    /// <summary><c>ss::StrICmp(a, b) == 0</c>: char by char (the names arrive
    /// already decoded), folding only ASCII <c>A</c>-<c>Z</c> exactly like
    /// ss_string.h's <c>AsciiLower</c>. NOT
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which also folds
    /// non-ASCII letters and would match attachment names stock refuses.</summary>
    private static bool AsciiEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (FoldAscii(a[i]) != FoldAscii(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static char FoldAscii(char c) =>
        c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    /// <summary>Stock's <c>floor</c> (bone_setup.cpp:1707). The CLR's
    /// <see cref="MathF.Floor(float)"/> agrees bit-for-bit on the reals C
    /// library <c>floorf</c> answers exactly (it is exact on everything but
    /// subnormals-in-laundering, which pose parameters never carry).</summary>
    private static float Floor(float x) => MathF.Floor(x);

    /// <summary><c>mstudioboneflexdriver_t</c> component extract, merge arm:
    /// <c>mBoneLocal[c][3]</c>. A component outside {0,1,2} is stock's
    /// assert-only guard (its release build reads the next matrix's column
    /// 3); the port throws — NR-02.</summary>
    private static float MatrixComponent(in BoneMatrix3x4 m, int c) => c switch
    {
        0 => m.Row0.W,
        1 => m.Row1.W,
        2 => m.Row2.W,
        _ => throw new InvalidOperationException(
            $"flexdriver bone component {c} outside 0..2 (stock UB)"),
    };

    /// <summary><c>pvPositions[bone][c]</c>, non-merge arm; same malformed
    /// component rule as <see cref="MatrixComponent"/> (NR-02).</summary>
    private static float VectorComponent(Vector v, int c) => c switch
    {
        0 => v.X,
        1 => v.Y,
        2 => v.Z,
        _ => throw new InvalidOperationException(
            $"flexdriver bone component {c} outside 0..2 (stock UB)"),
    };

    /// <summary>mathlib.h:619-627's <c>RemapValClamped</c> verbatim,
    /// including the <c>A == B</c> arm (<c>val &gt;= B ? D : C</c>) and the
    /// NaN behaviour of mathlib's <c>clamp</c> (all comparisons false → the
    /// value passes through; <see cref="Math.Clamp"/> would answer minVal).
    /// Promotion into <c>BoneSetupMath</c> requested — NR-01.</summary>
    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
        {
            return val >= b ? d : c;
        }

        float cVal = (val - a) / (b - a);
        cVal = Clamp(cVal, 0.0f, 1.0f);
        return c + ((d - c) * cVal);
    }

    /// <summary>mathlib.h:130-139's <c>clamp</c>: the inverted-range arm
    /// first, then two independent one-sided compares — NaN passes through,
    /// which is why this is not <see cref="Math.Clamp(float, float, float)"/>.
    /// Promotion into <c>BoneSetupMath</c> requested — NR-01.</summary>
    private static float Clamp(float val, float minVal, float maxVal)
    {
        if (maxVal < minVal)
        {
            return maxVal;
        }
        else if (val < minVal)
        {
            return minVal;
        }
        else if (val > maxVal)
        {
            return maxVal;
        }
        else
        {
            return val;
        }
    }
}
