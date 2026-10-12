//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The query cluster of <c>bone_setup.cpp</c>, ported to C#: the controller
/// and pose-parameter encode/decode pair (<c>Studio_SetController</c>,
/// <c>Studio_GetController</c>, <c>Studio_SetPoseParameter</c>,
/// <c>Studio_GetPoseParameter</c>), the sequence frame-rate queries
/// (<c>Studio_MaxFrame</c>, <c>Studio_FPS</c>), the animation movement family
/// (<c>Studio_AnimPosition</c>, <c>Studio_AnimVelocity</c>,
/// <c>Studio_AnimMovement</c>, <c>Studio_FindAnimDistance</c>) and their
/// sequence-level weighted wrappers (<c>Studio_SeqMovement</c>,
/// <c>Studio_SeqVelocity</c>, <c>Studio_FindSeqDistance</c>), plus
/// <c>Studio_FindRandomAttachment</c> and <c>Studio_BoneIndexByName</c>.
///
/// <para>
/// Every one is pure over the <see cref="StudioHdr"/> image plus injected
/// arguments: no clock, no convar, no engine RNG, no static state (SS0001).
/// The engine-flavoured inputs become parameters — the attachment draw is an
/// already-reduced <c>randomIndex</c>, and the pose arrays stay caller-owned,
/// exactly as stock's are.
/// </para>
///
/// <para>
/// Stock's <c>QAngle</c> outputs in this cluster only ever touch the
/// <c>y</c> (yaw) component — the others are <c>Init()</c>-zeroed and never
/// written — so the yaw travels as a bare <see cref="float"/> (NQ-06). Stock
/// leaves the movement outputs UNINITIALISED on every false-returning path
/// (it is a caller-aliasing bug the C++ tolerates because the caller tests the
/// bool); the port answers zero there, which is the safe value for a C#
/// <c>out</c> parameter and is called out per method below.
/// </para>
/// </summary>
public static class BoneSetupQuery
{
    /// <summary>STUDIO_XR | STUDIO_YR | STUDIO_ZR — the rotational controller
    /// bits whose value wraps at 360 (stock's type test).</summary>
    private const int RotationalTypes =
        StudioConstants.STUDIO_XR | StudioConstants.STUDIO_YR | StudioConstants.STUDIO_ZR;

    // -----------------------------------------------------------------
    // Studio_SetController (stock 5122-5171)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_SetController( const CStudioHdr *, int iController, float
    /// flValue, float &amp;ctlValue )</c> — our 5122-5171. Ranged value in,
    /// clamped ranged value out; <paramref name="ctlValue"/> gets the 0..1
    /// encoding.
    /// </summary>
    /// <remarks>
    /// The arms, in stock order:
    /// <list type="bullet">
    /// <item>null hdr → return <paramref name="flValue"/> and leave
    /// <paramref name="ctlValue"/> exactly as the caller passed it (stock
    /// assigns nothing on this path, hence <c>ref</c> and not <c>out</c>).</item>
    /// <item>no controller whose <c>inputfield</c> is
    /// <paramref name="iController"/> → <c>ctlValue = 0</c>, return
    /// <paramref name="flValue"/> unchanged.</item>
    /// <item>rotational (XR/YR/ZR): the "ugly hack" negates the value when
    /// <c>end &lt; start</c>; then either the non-wrapping window
    /// (<c>start + 359 &gt;= end</c>: shove the value into
    /// <c>[mid - 180, mid + 180]</c> with one ±360, mid being
    /// <c>(start + end) / 2</c>) or the free-loop arm (<c>start + 359 &lt;
    /// end</c>: <c>flValue -= (int)(flValue/360)*360</c> above 360, and
    /// <c>flValue += ((int)(flValue/-360) + 1)*360</c> below 0). Both casts
    /// are C truncation toward zero and the pair is NOT symmetric: 720.5
    /// comes back by 720 (int part 2), -361.5 by +720 (the int of 1.0041… is
    /// 1, and the +1 is forced).</item>
    /// <item>the encode is two independent ifs (a NaN survives unclamped and
    /// poisons the return exactly as stock's does); the decode is the lerp
    /// <c>(1 - ctl) * start + ctl * end</c>; and the invert negates the RESULT
    /// a second time for a rotational controller with <c>end &lt; start</c>,
    /// so that arm round-trips the caller's sign.</item>
    /// </list>
    /// <para>The controller lookup is stock's file-static <c>FindController</c>
    /// inlined as <see cref="FindControllerLocal"/>: the FIRST record whose
    /// <c>inputfield</c> matches wins (a model may share one inputfield across
    /// bones, so several records can match and only the first is used).</para>
    /// </remarks>
    public static float SetController(
        StudioHdr? pStudioHdr, int iController, float flValue, ref float ctlValue)
    {
        if (pStudioHdr is null)
        {
            return flValue;
        }

        MstudioBoneController? pbonecontroller = FindControllerLocal(pStudioHdr, iController);
        if (pbonecontroller is null)
        {
            ctlValue = 0;
            return flValue;
        }

        // wrap 0..360 if it's a rotational controller
        if ((pbonecontroller.Type & RotationalTypes) != 0)
        {
            // ugly hack, invert value if end < start
            if (pbonecontroller.End < pbonecontroller.Start)
            {
                flValue = -flValue;
            }

            // does the controller not wrap? Stock's 359.0, / 2.0 and 360.0
            // literals are doubles: the gate sum, both midpoint thresholds
            // and the free-loop revolution arithmetic run in double (the
            // float operands widen); the ±360 window shoves stay float
            // (int literal).
            if (pbonecontroller.Start + 359.0 >= pbonecontroller.End)
            {
                if (flValue > ((pbonecontroller.Start + pbonecontroller.End) / 2.0) + 180)
                {
                    flValue = flValue - 360;
                }

                if (flValue < ((pbonecontroller.Start + pbonecontroller.End) / 2.0) - 180)
                {
                    flValue = flValue + 360;
                }
            }
            else
            {
                if (flValue > 360)
                {
                    flValue = (float)(flValue - (int)(flValue / 360.0) * 360.0);
                }
                else if (flValue < 0)
                {
                    flValue = (float)(flValue + (int)((flValue / -360.0) + 1) * 360.0);
                }
            }
        }

        ctlValue = (flValue - pbonecontroller.Start) / (pbonecontroller.End - pbonecontroller.Start);
        if (ctlValue < 0)
        {
            ctlValue = 0;
        }

        if (ctlValue > 1)
        {
            ctlValue = 1;
        }

        // stock: (1.0 - ctlValue) * start + ctlValue * end — a double lerp
        // (ctl * end stays float and widens for the add), one rounding.
        float flReturnVal = (float)((1.0 - ctlValue) * pbonecontroller.Start + ctlValue * pbonecontroller.End);

        // ugly hack, invert value if a rotational controller and end < start
        if ((pbonecontroller.Type & RotationalTypes) != 0 &&
            pbonecontroller.End < pbonecontroller.Start)
        {
            flReturnVal *= -1.0f;
        }

        return flReturnVal;
    }

    // -----------------------------------------------------------------
    // Studio_GetController (stock 5182-5192)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_GetController( const CStudioHdr *, int iController, float
    /// ctlValue )</c> — our 5182-5192. The 0..1 encoding back into the
    /// controller's range: <c>ctl * (end - start) + start</c>. Null hdr and a
    /// missing controller both answer 0 (stock's two early returns); neither
    /// applies the rotational invert, so this is Set's decode without the sign
    /// hack.
    /// </summary>
    public static float GetController(StudioHdr? pStudioHdr, int iController, float ctlValue)
    {
        if (pStudioHdr is null)
        {
            return 0.0f;
        }

        MstudioBoneController? pbonecontroller = FindControllerLocal(pStudioHdr, iController);
        if (pbonecontroller is null)
        {
            return 0;
        }

        return ctlValue * (pbonecontroller.End - pbonecontroller.Start) + pbonecontroller.Start;
    }

    // -----------------------------------------------------------------
    // Studio_SetPoseParameter (stock 5231-5260)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_SetPoseParameter( const CStudioHdr *, int iParameter, float
    /// flValue, float &amp;ctlValue )</c> — our 5231-5260. Ranged pose value
    /// in, clamped ranged value out; <paramref name="ctlValue"/> gets the 0..1
    /// encoding.
    /// </summary>
    /// <remarks>
    /// <para>Index gate first: <c>iParameter &lt; 0</c> or at/past
    /// <c>GetNumPoseParameters()</c> returns 0 and leaves
    /// <paramref name="ctlValue"/> exactly as the caller passed it (stock
    /// assigns nothing on that path either — <c>ref</c>, not <c>out</c>).
    /// Stock derefs the hdr unguarded here, so a null hdr is the port's
    /// <see cref="NullReferenceException"/> arm: the managed stand-in for
    /// stock's crash, not a divergence.</para>
    /// <para>The loop wrap is stock's three lines verbatim — <c>wrap =
    /// (start + end) / 2 + loop / 2</c>, <c>shift = loop - wrap</c>,
    /// <c>value -= loop * floor((value + shift) / loop)</c>. It folds any
    /// value into a width-<c>loop</c> window centred on
    /// <c>(start + end + loop) / 2</c>, so it is NOT a modulo about zero: for
    /// start 0, end 10, loop 10 the window is [0, 10) and 25 lands on 5 while
    /// -0.5 lands on 9.5. A negative <c>loop</c> is stock's data (the gate
    /// only tests nonzero) and walks the window the other way.</para>
    /// <para>The encode is stock's two independent ifs again — a NaN
    /// <paramref name="flValue"/> stays unclamped and poisons the return
    /// exactly as stock's does. Stock's two <c>Assert(IsFinite(...))</c> are
    /// debug no-ops and stay out.</para>
    /// </remarks>
    public static float SetPoseParameter(
        StudioHdr pStudioHdr, int iParameter, float flValue, ref float ctlValue)
    {
        if (iParameter < 0 || iParameter >= pStudioHdr.NumPoseParameters)
        {
            return 0;
        }

        MstudioPoseParamDesc PoseParam = pStudioHdr.PoseParameter(iParameter);

        if (PoseParam.Loop != 0.0f)
        {
            // stock's / 2.0 literals: the centre sums in double, rounded once
            // on the float store.
            float wrap = (float)((PoseParam.Start + PoseParam.End) / 2.0 + PoseParam.Loop / 2.0);
            float shift = PoseParam.Loop - wrap;

            flValue = flValue - PoseParam.Loop * MathF.Floor((flValue + shift) / PoseParam.Loop);
        }

        ctlValue = (flValue - PoseParam.Start) / (PoseParam.End - PoseParam.Start);

        if (ctlValue < 0)
        {
            ctlValue = 0;
        }

        if (ctlValue > 1)
        {
            ctlValue = 1;
        }

        return ctlValue * (PoseParam.End - PoseParam.Start) + PoseParam.Start;
    }

    // -----------------------------------------------------------------
    // Studio_GetPoseParameter (stock 5268-5278)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_GetPoseParameter( const CStudioHdr *, int iParameter, float
    /// ctlValue )</c> — our 5268-5278. The 0..1 encoding back into the
    /// parameter's range: <c>ctl * (end - start) + start</c>, and 0 for an
    /// out-of-range index (the same gate as <see cref="SetPoseParameter"/>).
    /// No loop wrap: the wrap is an encode-time-only step, so decoding an
    /// encoded value from a looping parameter returns the wrapped value, not
    /// the one that went in.
    /// </summary>
    public static float GetPoseParameter(StudioHdr pStudioHdr, int iParameter, float ctlValue)
    {
        if (iParameter < 0 || iParameter >= pStudioHdr.NumPoseParameters)
        {
            return 0;
        }

        MstudioPoseParamDesc PoseParam = pStudioHdr.PoseParameter(iParameter);

        return ctlValue * (PoseParam.End - PoseParam.Start) + PoseParam.Start;
    }

    // -----------------------------------------------------------------
    // Studio_MaxFrame (stock 5610-5634)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_MaxFrame( const CStudioHdr *, int iSequence, const float
    /// poseParameter[] )</c> — our 5610-5634. The weighted last-frame index
    /// across the four blend cells: every cell with <c>weight &gt; 0</c>
    /// contributes <c>numframes * weight</c>; the sum loses its last frame
    /// when it exceeds 1 (frames are 0-based across a full cycle), and the
    /// <c>+ 0.01</c> float-to-int cast truncates toward zero — which is why
    /// stock's FIXME calls the rounding-down "sometimes": a weighted sum of
    /// 9.99 answers 9, one of 10.01 answers 10. Rounding instead of
    /// truncating would answer 10 for both.
    /// </summary>
    /// <remarks>
    /// The weight gate is <c>&gt; 0</c> here (as in <see cref="Fps"/>) while
    /// the sequence trio below uses stock's truthiness gate
    /// <c>if (weight[i])</c>; <see cref="BoneSetupHdrRead.SeqAnims"/> clamps
    /// its settings to [0,1] so the two are observably identical, and each is
    /// ported as its function wrote it. A cell the reader cannot produce
    /// (an unresolvable blend cell → null entry) is DIVERGENCE
    /// (malformed-data only, NQ-09): stock derefs the null <c>panim[i]</c>;
    /// the port skips it. Well-formed hdrs never reach that arm.
    /// </remarks>
    public static int MaxFrame(StudioHdr pStudioHdr, int iSequence, float[] poseParameter)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        BoneSetupHdrRead.SeqAnims(pStudioHdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        float maxFrame = 0;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] > 0 && panim[i] is not null)
            {
                maxFrame += panim[i]!.NumFrames * weight[i];
            }
        }

        if (maxFrame > 1)
        {
            maxFrame -= 1;
        }

        // FIXME (stock): why does the weights sometimes not exactly add it
        // 1.0 and this sometimes rounds down? Mirrored: truncating cast of
        // stock's double sum (the 0.01 literal is a double).
        return (int)(maxFrame + 0.01);
    }

    // -----------------------------------------------------------------
    // Studio_FPS (stock 5642-5662)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_FPS( const CStudioHdr *, int iSequence, const float
    /// poseParameter[] )</c> — our 5642-5662. Frames per second: the weighted
    /// sum of the cells' <c>fps</c>, positive weight only. Unlike
    /// <see cref="BoneSetupHdrRead.Cps"/> there is no <c>numframes &gt; 1</c>
    /// gate — a one-frame anim counts at its full fps here, because this is
    /// frames/second and not cycles/second.
    /// </summary>
    public static float Fps(StudioHdr pStudioHdr, int iSequence, float[] poseParameter)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        BoneSetupHdrRead.SeqAnims(pStudioHdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        float t = 0;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] > 0 && panim[i] is not null)
            {
                t += panim[i]!.Fps * weight[i];
            }
        }

        return t;
    }

    // -----------------------------------------------------------------
    // Studio_AnimPosition (stock 5710-5772)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_AnimPosition( mstudioanimdesc_t *, float flCycle, Vector
    /// &amp;vecPos, QAngle &amp;vecAngle )</c> — our 5710-5772. The position
    /// and yaw accumulated from the animation's origin up to cycle
    /// <paramref name="flCycle"/>; false when the anim is not a movement anim
    /// (<c>nummovements == 0</c>), which still leaves both outputs ZEROED —
    /// stock's <c>Init()</c> pair runs before that gate.
    /// </summary>
    /// <remarks>
    /// <para>The cycle first folds into [0,1) with a truncating loop count:
    /// <c>1.5 → iLoops 1, cycle 0.5</c>; <c>-0.25 → iLoops -1, cycle
    /// 0.75</c> — stock's <c>(int)fl - 1</c> for negatives is a floor, not the
    /// truncate its positive arm uses, so <c>-1.25 → iLoops -2</c> ((int) of
    /// -1.25 is -1, minus 1). The frame is <c>cycle * (numframes - 1)</c>.</para>
    /// <para>Movement blocks are then walked in table order. While a block's
    /// <c>endframe</c> is still ahead of the frame, the else arm advances
    /// <c>prevframe</c>, <c>vecPos</c> and <c>vecAngle.y</c> to that block's
    /// settled totals. The block containing the frame interpolates from
    /// THERE: <c>f</c> is the within-block fraction, the distance follows the
    /// constant-acceleration curve <c>v0*f + 0.5*(v1 - v0)*f*f</c> along
    /// <c>vector</c>, and yaw is <c>vecAngle.y * (1 - f) + angle * f</c> —
    /// i.e. the previous block's angle decays into this block's full angle,
    /// NOT a lerp from zero. (Mirroring the expression is what makes a
    /// multi-block anim's mid-flight yaw right; the single-block case, where
    /// the previous angle is the Init() 0, reads like a lerp from zero.)</para>
    /// <para>The loop bonus: with <c>iLoops != 0</c> the FULL-cycle totals of
    /// the LAST movement block (<c>pMovement(nummovements - 1)</c> —
    /// <c>position</c>/<c>angle</c>, the block's settled totals, not its
    /// <c>vector</c>) are added <c>iLoops</c> times.</para>
    /// <para>A frame past the last block's <c>endframe</c> exhausts the loop
    /// and answers false with the last block's accumulated totals still in the
    /// outputs — stock's fall-through, deliberately preserved.</para>
    /// </remarks>
    public static bool AnimPosition(
        MstudioAnimDesc panim, float flCycle, out Vector vecPos, out float vecAngleYaw)
    {
        float prevframe = 0;
        vecPos = Vector.Zero;
        vecAngleYaw = 0f;

        int nummovements = panim.NumMovements;
        if (nummovements == 0)
        {
            return false;
        }

        int iLoops = 0;
        if (flCycle > 1.0f)
        {
            iLoops = (int)flCycle;
        }
        else if (flCycle < 0.0f)
        {
            iLoops = (int)flCycle - 1;
        }

        flCycle = flCycle - iLoops;

        float flFrame = flCycle * (panim.NumFrames - 1);

        for (int i = 0; i < nummovements; i++)
        {
            MstudioMovement pmove = Movement(panim, i);

            if (pmove.EndFrame >= flFrame)
            {
                float f = (flFrame - prevframe) / (pmove.EndFrame - prevframe);

                // stock's 0.5 literal carries the curve term in double; v0 * f
                // stays float and widens for the add; one rounding.
                float d = (float)(pmove.V0 * f + 0.5 * (pmove.V1 - pmove.V0) * f * f);

                vecPos = vecPos + pmove.Vector * d;
                vecAngleYaw = vecAngleYaw * (1 - f) + pmove.Angle * f;
                if (iLoops != 0)
                {
                    MstudioMovement pmoveAnim = Movement(panim, nummovements - 1);
                    vecPos = vecPos + pmoveAnim.Position * iLoops;
                    vecAngleYaw = vecAngleYaw + iLoops * pmoveAnim.Angle;
                }

                return true;
            }
            else
            {
                prevframe = pmove.EndFrame;
                vecPos = pmove.Position;
                vecAngleYaw = pmove.Angle;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------
    // Studio_AnimVelocity (stock 5773-5806)
    /// -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_AnimVelocity( mstudioanimdesc_t *, float flCycle, Vector
    /// &amp;vecVelocity )</c> — our 5773-5806. Instantaneous inches/second at
    /// a point in the cycle, relative to identity orientation; false when no
    /// movement block covers the frame, where stock leaves its by-reference
    /// output UNSET — the port answers zero (safe-value divergence; the caller
    /// tests the bool, exactly as stock's callers do).
    /// </summary>
    /// <remarks>
    /// <para>Two details distinguish this from <see cref="AnimPosition"/>,
    /// both stock's:</para>
    /// <list type="bullet">
    /// <item>the frame folds by truncating modulo — <c>flFrame - (int)(flFrame
    /// / (numframes - 1))</c> — not by the cycle-level loop count, so the
    /// wrap happens on the frame axis and there is no whole-cycle bonus;</item>
    /// <item>there is no <c>nummovements == 0</c> gate at all — an empty table
    /// simply exhausts the loop and answers false, which is why the output
    /// zeroing here mirrors stock's absence rather than a gate.</item>
    /// </list>
    /// <para>The block velocity is the LINEAR blend <c>v0 * (1 - f) + v1 *
    /// f</c> (inches per block, endpoint-anchored, unlike the position's
    /// <c>d</c> curve), rescaled to inches/second by <c>* fps / (endframe -
    /// prevframe)</c>: the block's own frame span is the dt, so a block twice
    /// as long halves the velocity it reports at the same blend. The result
    /// scales <c>pmove-&gt;vector</c>.</para>
    /// </remarks>
    public static bool AnimVelocity(MstudioAnimDesc panim, float flCycle, out Vector vecVelocity)
    {
        float prevframe = 0;
        vecVelocity = Vector.Zero;

        float flFrame = flCycle * (panim.NumFrames - 1);
        flFrame = flFrame - (int)(flFrame / (panim.NumFrames - 1));

        int nummovements = panim.NumMovements;
        for (int i = 0; i < nummovements; i++)
        {
            MstudioMovement pmove = Movement(panim, i);

            if (pmove.EndFrame >= flFrame)
            {
                float f = (flFrame - prevframe) / (pmove.EndFrame - prevframe);

                float vel = pmove.V0 * (1 - f) + pmove.V1 * f;
                // scale from per block to per sec velocity
                vel = vel * panim.Fps / (pmove.EndFrame - prevframe);

                vecVelocity = pmove.Vector * vel;
                return true;
            }
            else
            {
                prevframe = pmove.EndFrame;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------
    // Studio_AnimMovement (stock 5812-5836)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_AnimMovement( mstudioanimdesc_t *, float flCycleFrom, float
    /// flCycleTo, Vector &amp;deltaPos, QAngle &amp;deltaAngle )</c> — our
    /// 5812-5836. The delta between two points of the cycle, re-based so the
    /// <c>from</c> pose sits at the origin facing 0: the two accumulated
    /// positions are differenced, the yaw delta is <c>endA - startA</c>, and
    /// the position delta is rotated back by <c>-startA</c>
    /// (<see cref="VectorYawRotate"/> — the one place this cluster needs a
    /// sin/cos pair, hence NQ-02).
    /// </summary>
    /// <remarks>
    /// Returns false ONLY for a movement-less anim; when blocks exist stock
    /// always answers true, discarding both <see cref="AnimPosition"/> bools —
    /// so a cycle pair outside the block coverage still returns true with the
    /// fall-through totals differenced. Outputs are zeroed on the false arm
    /// (stock leaves them unset).
    /// </remarks>
    public static bool AnimMovement(
        MstudioAnimDesc panim, float flCycleFrom, float flCycleTo,
        out Vector deltaPos, out float deltaAngleYaw)
    {
        if (panim.NumMovements == 0)
        {
            deltaPos = Vector.Zero;
            deltaAngleYaw = 0f;
            return false;
        }

        AnimPosition(panim, flCycleFrom, out Vector startPos, out float startA);
        AnimPosition(panim, flCycleTo, out Vector endPos, out float endA);

        Vector tmp = endPos - startPos;
        deltaAngleYaw = endA - startA;
        VectorYawRotate(tmp, -startA, out deltaPos);
        return true;
    }

    // -----------------------------------------------------------------
    // Studio_FindAnimDistance (stock 5839-5879)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_FindAnimDistance( mstudioanimdesc_t *, float flDist )</c> —
    /// our 5839-5879. The cycle at which the anim has covered
    /// <paramref name="flDist"/> inches of movement. Blocks are consumed at
    /// their AVERAGE speed <c>(v0 + v1) * 0.5</c>; inside the block that
    /// reaches the remaining distance, the constant-acceleration equation
    /// <c>d = v0*t + 0.5*(v1 - v0)*t^2</c> is solved for the block fraction
    /// <c>t</c> with <see cref="SolveQuadratic"/>(<c>0.5*(v1 - v0), v0,
    /// -dist</c>) and <c>root1</c> maps back through the block's frame span,
    /// then to cycles at <c>1 / (numframes - 1)</c> per frame. Non-positive
    /// distance answers 0; running out of blocks answers a full 1.0.
    /// </summary>
    /// <remarks>
    /// Stock's post-<c>SolveQuadratic</c> <c>return 0.0</c> arm is unreachable
    /// for any finite <c>flDist &gt; 0</c>: the gate requires <c>(v0 + v1)/2
    /// &gt;= flDist</c>, and the discriminant <c>v0^2 + 2*(v1 - v0)*flDist</c>
    /// is minimised at exactly that boundary, where it equals
    /// <c>(v0 - 2*flDist)^2 &gt;= 0</c>. It stays in the port because stock
    /// wrote it (a NaN distance reaches it through neither branch test), and
    /// it has no fact for that reason.
    /// </remarks>
    public static float FindAnimDistance(MstudioAnimDesc panim, float flDist)
    {
        float prevframe = 0;

        if (flDist <= 0)
        {
            return 0.0f;
        }

        int nummovements = panim.NumMovements;
        for (int i = 0; i < nummovements; i++)
        {
            MstudioMovement pmove = Movement(panim, i);

            float flMove = (float)((pmove.V0 + pmove.V1) * 0.5);

            if (flMove >= flDist)
            {
                // d = V0 * t + 1/2 (V1-V0) * t^2
                if (SolveQuadratic((float)(0.5 * (pmove.V1 - pmove.V0)), pmove.V0, -flDist,
                        out float root1, out float root2))
                {
                    float cpf = (float)(1.0 / (panim.NumFrames - 1)); // cycles per frame

                    return (prevframe + root1 * (pmove.EndFrame - prevframe)) * cpf;
                }

                return 0.0f;
            }
            else
            {
                flDist -= flMove;
                prevframe = pmove.EndFrame;
            }
        }

        return 1.0f;
    }

    // -----------------------------------------------------------------
    // Studio_SeqMovement (stock 5883-5926)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_SeqMovement( const CStudioHdr *, int iSequence, float
    /// flCycleFrom, float flCycleTo, const float poseParameter[], Vector
    /// &amp;deltaPos, QAngle &amp;deltaAngles )</c> — our 5883-5926. The
    /// between-cycle delta across the four blend cells: each cell with
    /// NONZERO weight contributes its anim's movement scaled by its weight,
    /// into both <paramref name="deltaPos"/> and (stock's own FIXME)
    /// <paramref name="deltaAngleYaw"/>. A cell whose anim is non-delta with
    /// no movement blocks, in a sequence whose own bone-0 weight is positive,
    /// counts as found without moving anything — the static-prop arm. Outputs
    /// start zeroed (stock's <c>Init()</c> pair).
    /// </summary>
    public static bool SeqMovement(
        StudioHdr pStudioHdr, int iSequence, float flCycleFrom, float flCycleTo,
        float[] poseParameter, out Vector deltaPos, out float deltaAngleYaw)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        BoneSetupHdrRead.SeqAnims(pStudioHdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        deltaPos = Vector.Zero;
        deltaAngleYaw = 0f;

        bool found = false;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] != 0f && panim[i] is not null)
            {
                MstudioAnimDesc anim = panim[i]!;
                if (AnimMovement(anim, flCycleFrom, flCycleTo, out Vector localPos, out float localAngle))
                {
                    found = true;
                    deltaPos = deltaPos + localPos * weight[i];
                    // FIXME (stock): this makes no sense
                    deltaAngleYaw = deltaAngleYaw + localAngle * weight[i];
                }
                else if ((anim.Flags & StudioConstants.STUDIO_DELTA) == 0
                         && anim.NumMovements == 0 && seqdesc.Weight(0) > 0.0f)
                {
                    found = true;
                }
            }
        }

        return found;
    }

    // -----------------------------------------------------------------
    // Studio_SeqVelocity (stock 5932-5960)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_SeqVelocity( const CStudioHdr *, int iSequence, float
    /// flCycle, const float poseParameter[], Vector &amp;vecVelocity )</c> —
    /// our 5932-5960. Instantaneous velocity: each nonzero-weight cell adds
    /// its anim's velocity scaled by its weight, and the sum reports found
    /// only if at least one cell's velocity was computable. Zeroed output
    /// (stock's <c>Init()</c>).
    /// </summary>
    public static bool SeqVelocity(
        StudioHdr pStudioHdr, int iSequence, float flCycle, float[] poseParameter,
        out Vector vecVelocity)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        BoneSetupHdrRead.SeqAnims(pStudioHdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        vecVelocity = Vector.Zero;

        bool found = false;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] != 0f && panim[i] is not null)
            {
                if (AnimVelocity(panim[i]!, flCycle, out Vector vecLocalVelocity))
                {
                    vecVelocity = vecVelocity + vecLocalVelocity * weight[i];
                    found = true;
                }
            }
        }

        return found;
    }

    // -----------------------------------------------------------------
    // Studio_FindSeqDistance (stock 5966-5986)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_FindSeqDistance( const CStudioHdr *, int iSequence, const
    /// float poseParameter[], float flDist )</c> — our 5966-5986. Each
    /// nonzero-weight cell answers its own distance-to-cycle and the answers
    /// blend by weight. There is no found flag: a 0.0 answer is a legal
    /// cycle, both for a non-movement sequence and for one that covers the
    /// distance before frame 1.
    /// </summary>
    public static float FindSeqDistance(
        StudioHdr pStudioHdr, int iSequence, float[] poseParameter, float flDist)
    {
        MstudioSeqDesc seqdesc = pStudioHdr.SeqDesc(iSequence);
        BoneSetupHdrRead.SeqAnims(pStudioHdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        float flCycle = 0;
        for (int i = 0; i < 4; i++)
        {
            if (weight[i] != 0f && panim[i] is not null)
            {
                float flLocalCycle = FindAnimDistance(panim[i]!, flDist);
                flCycle = flCycle + flLocalCycle * weight[i];
            }
        }

        return flCycle;
    }

    // -----------------------------------------------------------------
    // Studio_FindRandomAttachment (stock 6014-6042)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_FindRandomAttachment( const CStudioHdr *, const char
    /// *pAttachmentName )</c> — our 6014-6042. Every attachment whose name
    /// CONTAINS <paramref name="pAttachmentName"/> as a substring
    /// (<c>strstr</c>: case-SENSITIVE, unlike
    /// <see cref="BoneSetupHdrRead.FindAttachment"/>'s whole-name ASCII-fold
    /// compare, and an empty needle matches everything, as stock's
    /// <c>strstr(name, "")</c> is non-null). The candidate list is index-
    /// ordered and <paramref name="randomIndex"/> picks one.
    /// </summary>
    /// <remarks>
    /// Stock draws <c>RandomInt(0, size - 1)</c> from the engine RNG; SS0001
    /// forbids an ambient RNG, so the already-reduced draw is a parameter
    /// (NQ-01). An index outside the candidate list is a caller error stock
    /// cannot express (its own draw is always in range) and answers -1 —
    /// DIVERGENCE (malformed input only); the port never wraps or throws on a
    /// number stock would have picked itself. Null hdr, or no substring match
    /// at all, answers -1 like stock. The scan bound is
    /// <c>NumGlobalAttachments</c> — the CStudioHdr-routed count stock's
    /// <c>GetNumAttachments()</c> answers, i.e. the merged list for a folded
    /// (virtual-model) hdr.
    /// </remarks>
    public static int FindRandomAttachment(StudioHdr? pStudioHdr, string pAttachmentName, int randomIndex)
    {
        if (pStudioHdr is null)
        {
            return -1;
        }

        // First move them all matching attachments into a list
        int capacity = pStudioHdr.NumGlobalAttachments;
        Span<int> matchingAttachments = capacity <= 256 ? stackalloc int[capacity] : new int[capacity];
        int numMatching = 0;
        for (int i = 0; i < capacity; i++)
        {
            if (StrStr(pStudioHdr.Attachment(i).Name, pAttachmentName))
            {
                matchingAttachments[numMatching++] = i;
            }
        }

        // Then randomly return one of the attachments
        if (numMatching > 0)
        {
            if (randomIndex < 0 || randomIndex >= numMatching)
            {
                return -1; // DIVERGENCE (malformed input only): see remarks
            }

            return matchingAttachments[randomIndex];
        }

        return -1;
    }

    // -----------------------------------------------------------------
    // Studio_BoneIndexByName (stock 6044-6074)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>Studio_BoneIndexByName( const CStudioHdr *, const char *pName )</c>
    /// — our 6044-6074. Bone index by name, case-insensitive over the ASCII
    /// fold, -1 when no bone carries the name.
    /// </summary>
    /// <remarks>
    /// Stock binary-searches the compiler-maintained
    /// <c>bonetablebynameindex</c> sort
    /// (<c>GetBoneTableSortedByName()</c>, studio.h:2357). The managed reader
    /// does not expose that table (no <c>docs/bones-offsets.txt</c> entry —
    /// NQ-05), so this is the order-equivalent linear scan over
    /// <c>pBone(i)-&gt;pszName()</c>. It answers exactly what the binary
    /// search answers for any model whose bone names are unique (every
    /// compiler-emitted mdl); the two can only disagree on a malformed model
    /// whose table is not a true sort of its names, where the binary search
    /// may miss a name the scan still finds — DIVERGENCE (malformed-data
    /// only), and the scan's answer is the one a caller wants.
    /// </remarks>
    public static int BoneIndexByName(StudioHdr? pStudioHdr, string pName)
    {
        if (pStudioHdr is null)
        {
            return -1;
        }

        int numbones = pStudioHdr.NumBones;
        for (int i = 0; i < numbones; i++)
        {
            if (AsciiEquals(pStudioHdr.Bone(i).Name, pName))
            {
                return i;
            }
        }

        return -1;
    }

    // -----------------------------------------------------------------
    // private helpers (pending promotion; see docs/plans/needs-query.md)
    // -----------------------------------------------------------------

    /// <summary>
    /// Stock's file-static <c>FindController</c> (our 5099-5109): the first
    /// controller record whose <c>inputfield</c> equals
    /// <paramref name="iController"/>, or null. The linear scan over
    /// <c>numbonecontrollers()</c> means the FIRST match wins when several
    /// controllers share an inputfield. A4's
    /// <c>BoneSetupProcedural.FindController</c> is the same stock body; this
    /// is the query lane's private copy so the cluster does not depend on a
    /// sibling lane's file (NQ-07).
    /// </summary>
    private static MstudioBoneController? FindControllerLocal(StudioHdr pStudioHdr, int iController)
    {
        // find first controller that matches the index
        int numbonecontrollers = pStudioHdr.NumBoneControllers;
        for (int i = 0; i < numbonecontrollers; i++)
        {
            MstudioBoneController p = pStudioHdr.BoneController(i);
            if (p.InputField == iController)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>panim-&gt;pMovement(i)</c>: <c>movementindex</c> is record-relative
    /// and the table walks a 44-byte stride. A movement-less anim whose index
    /// somehow still counts movements is stock's NULL-<c>pmove</c> deref
    /// (undefined behaviour, NQ-08); the port throws, which is the
    /// malformed-data arm <see cref="StudioHdr.TryCreate"/> already refuses
    /// to reach.
    /// </summary>
    private static MstudioMovement Movement(MstudioAnimDesc panim, int i) =>
        panim.PMovement(i) ?? throw new InvalidOperationException("movement table absent");

    /// <summary>
    /// mathlib_base.cpp:1433-1452 <c>VectorYawRotate</c>, non-aliased arm: the
    /// pair <c>SinCos(DEG2RAD(flYaw))</c> taken from
    /// <see cref="BoneSetupMath.SinCos"/> (the compiled sincosf pair stock
    /// calls — promoted to public by the 2026-10-08 ruling rather than
    /// duplicating it here; NQ-11), then the 2x2 rotation on xy with z
    /// carried through. Stock's alias-guard recursion is unreachable here:
    /// the port is a pure function of its arguments.
    /// </summary>
    private static void VectorYawRotate(Vector input, float flYaw, out Vector output)
    {
        (float sy, float cy) = BoneSetupMath.SinCos(BoneSetupMath.DegToRad * flYaw);

        output.X = input.X * cy - input.Y * sy;
        output.Y = input.X * sy + input.Y * cy;
        output.Z = input.Z;
    }

    /// <summary>
    /// mathlib_base.cpp:1310-1339 <c>SolveQuadratic</c>: the linear arm
    /// (<c>a == 0 &amp;&amp; b != 0</c> — both roots <c>-c/b</c>), the all-zero
    /// arm (both roots 0), the <c>a == b == 0, c != 0</c> refusal, and the
    /// discriminant arms (<c>tmp &lt; 0</c> refuses, else
    /// <c>(-b ± sqrt(tmp)) / (2a)</c> with <c>root1</c> taking <c>+sqrt</c>).
    /// The <c>4.0f</c>/<c>2.0f</c> literals keep stock's float arithmetic;
    /// <see cref="MathF.Sqrt"/> is IEEE-exact like <c>sqrtf</c>.
    /// </summary>
    private static bool SolveQuadratic(
        float a, float b, float c, out float root1, out float root2)
    {
        if (a == 0)
        {
            if (b != 0)
            {
                // no x^2 component, it's a linear system
                root1 = root2 = -c / b;
                return true;
            }

            if (c == 0)
            {
                // all zero's
                root1 = root2 = 0;
                return true;
            }

            root1 = 0;
            root2 = 0;
            return false;
        }

        float tmp = b * b - 4.0f * a * c;

        if (tmp < 0)
        {
            // imaginary number, bah, no solution.
            root1 = 0;
            root2 = 0;
            return false;
        }

        tmp = MathF.Sqrt(tmp);
        root1 = (-b + tmp) / (2.0f * a);
        root2 = (-b - tmp) / (2.0f * a);
        return true;
    }

    /// <summary><c>strstr(haystack, needle) != NULL</c>: the raw C substring
    /// test — case-SENSITIVE, with the empty needle matching (stock's
    /// <c>strstr(name, "")</c> is non-null). Operand order matters: it is the
    /// attachment name that gets searched (NQ-03).</summary>
    private static bool StrStr(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.Ordinal);

    /// <summary><c>ss::StrICmp(a, b) == 0</c> (ss_string.h:187 — ASCII-only
    /// fold via <c>AsciiLower</c>), the same reading as
    /// <see cref="BoneSetupHdrRead"/>'s private copy, duplicated pending
    /// promotion (NQ-04). Also the include fold's label match in
    /// <see cref="StudioHdr"/> (stock's <c>stricmp</c>,
    /// studio_virtualmodel.cpp). NOT
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which also folds
    /// non-ASCII letters that stock leaves alone.</summary>
    internal static bool AsciiEquals(string a, string b)
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
}
