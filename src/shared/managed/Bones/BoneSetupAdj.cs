//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The controller-adjustment cluster of <c>bone_setup.cpp</c>, ported to C#:
/// <c>CalcBoneAdj</c>, <c>CalcBoneDerivatives</c>,
/// <c>CalcBoneVelocityFromDerivative</c>, <c>SetupSingleBoneMatrix</c> and
/// <c>Studio_CalcBoneToBoneTransform</c> (inventory rows
/// <c>CalcBoneAdj</c>/<c>CalcBoneDerivatives</c>/
/// <c>CalcBoneVelocityFromDerivative</c>/<c>SetupSingleBoneMatrix</c>/
/// <c>Studio_CalcBoneToBoneTransform</c>).
///
/// <para>
/// Every one is a pure function over the <see cref="StudioHdr"/> image and
/// caller-owned bone arrays: no clock, no convar, no static state (SS0001).
/// The ragdoll pair (<see cref="CalcBoneDerivatives"/> and
/// <see cref="CalcBoneVelocityFromDerivative"/>) is what
/// <c>ragdoll_shared</c> asks the engine boundary for; the engine hands the
/// matrices in and reads the velocity back — the decision-free shape the
/// port keeps. The derivatives' <c>dt</c> is stock's parameter, not a clock
/// read: the caller owns time.
/// </para>
///
/// <para>
/// Stock reads <c>controllers[inputfield]</c> with no bounds check; so does
/// this, and a malformed inputfield throws rather than reading adjacent
/// memory. The reader rejects a controller table that does not fit the image
/// (<c>StudioHdr.TryCreate</c>), so a well-formed hdr never reaches that arm.
/// </para>
/// </summary>
public static class BoneSetupAdj
{
    /// <summary>
    /// <c>CalcBoneAdj</c> — our 2539-2591 (oracle 2462-2514): one pass over
    /// the bonecontroller table; each controller whose bone passes the mask
    /// takes <c>controllers[inputfield]</c>, clamps it to [0,1], maps it into
    /// <c>[start, end]</c>, then rotates (XR/YR/ZR, through
    /// <see cref="BoneSetupMath.QuaternionSM"/> at weight 1) or translates
    /// (X/Y/Z, an in-place add) that bone.
    /// </summary>
    /// <remarks>
    /// The clamp is stock's two ifs, not a <see cref="Math.Clamp"/> call:
    /// stock clamps with two independent comparisons and a NaN setting falls
    /// through <em>unclamped</em>, which the mapping then poisons exactly as
    /// stock's does. The mask test is <c>boneFlags(k) &amp; boneMask</c> — the
    /// hdr's linearbones-aware cache, not the raw bone record.
    /// </remarks>
    public static void CalcBoneAdj(
        StudioHdr pStudioHdr,
        Vector[] pos,
        Quaternion[] q,
        ReadOnlySpan<float> controllers,
        int boneMask)
    {
        for (int j = 0; j < pStudioHdr.NumBoneControllers; j++)
        {
            MstudioBoneController pbonecontroller = pStudioHdr.BoneController(j);
            int k = pbonecontroller.Bone;

            if ((pStudioHdr.BoneFlags(k) & boneMask) != 0)
            {
                int i = pbonecontroller.InputField;
                float value = controllers[i];
                if (value < 0)
                {
                    value = 0;
                }

                if (value > 1.0f)
                {
                    value = 1.0f;
                }

                // stock: (1.0 - value) * start + value * end — the 1.0 is a
                // DOUBLE literal, so the (1.0 - value) * start term runs in
                // double, the value * end term is float, and the sum rounds
                // once on the float assignment.
                value = (float)(
                    (1.0 - (double)value) * (double)pbonecontroller.Start
                    + (value * pbonecontroller.End));

                Vector posK = pos[k];
                Quaternion qK = q[k];

                switch (pbonecontroller.Type & StudioConstants.STUDIO_TYPES)
                {
                    case StudioConstants.STUDIO_XR:
                    {
                        // stock a0.Init(value * (M_PI / 180.0), 0, 0): M_PI is
                        // a double macro (mathlib.h:291), so the product runs
                        // in double and rounds once into the float field.
                        RadianEuler a0 = new(
                            (float)(value * (Math.PI / 180.0)), 0f, 0f);
                        qK = BoneSetupMath.QuaternionSM(
                            1.0f, BoneSetupMath.AngleQuaternion(a0), qK);
                        break;
                    }

                    case StudioConstants.STUDIO_YR:
                    {
                        RadianEuler a0 = new(
                            0f, (float)(value * (Math.PI / 180.0)), 0f);
                        qK = BoneSetupMath.QuaternionSM(
                            1.0f, BoneSetupMath.AngleQuaternion(a0), qK);
                        break;
                    }

                    case StudioConstants.STUDIO_ZR:
                    {
                        RadianEuler a0 = new(
                            0f, 0f, (float)(value * (Math.PI / 180.0)));
                        qK = BoneSetupMath.QuaternionSM(
                            1.0f, BoneSetupMath.AngleQuaternion(a0), qK);
                        break;
                    }

                    case StudioConstants.STUDIO_X:
                        posK.X += value;
                        break;

                    case StudioConstants.STUDIO_Y:
                        posK.Y += value;
                        break;

                    case StudioConstants.STUDIO_Z:
                        posK.Z += value;
                        break;

                    // STUDIO_LX..STUDIO_LINEAR (0x40-0x1000) sit inside
                    // STUDIO_TYPES but have no stock arm (the switch ends at
                    // STUDIO_Z, no default): the bone is left untouched.
                    default:
                        break;
                }

                pos[k] = posK;
                q[k] = qK;
            }
        }
    }

    /// <summary>
    /// <c>CalcBoneDerivatives</c> — our 2600-2620 (oracle 2521-2541): the
    /// ragdoll's velocity feed. Linear velocity is the translation delta over
    /// <c>dt</c> (<c>dt &gt; 0</c> or scale stays 1); angular velocity is the
    /// delta rotation's axis times its (scaled) angle.
    /// </summary>
    public static void CalcBoneDerivatives(
        out Vector velocity,
        out Vector angVel,
        in BoneMatrix3x4 prev,
        in BoneMatrix3x4 current,
        float dt)
    {
        // stock 2523-2527: float scale = 1.0; ... scale = 1.0 / dt — a double
        // division, rounded once on the float store.
        float scale = 1.0f;
        if (dt > 0)
        {
            scale = (float)(1.0 / dt);
        }

        (BoneAngle startAngles, Vector startPosition) = ReadAnglesPosition(prev);
        (BoneAngle endAngles, Vector endPosition) = ReadAnglesPosition(current);

        velocity = new Vector(
            (endPosition.X - startPosition.X) * scale,
            (endPosition.Y - startPosition.Y) * scale,
            (endPosition.Z - startPosition.Z) * scale);

        (Vector deltaAxis, float deltaAngle) =
            BoneSetupMath.RotationDeltaAxisAngle(startAngles, endAngles);
        angVel = BoneSetupMath.VectorScale(deltaAxis, deltaAngle * scale);
    }

    /// <summary>
    /// <c>CalcBoneVelocityFromDerivative</c> — our 2624-2640 (oracle
    /// 2543-2557): push the stored derivative through a new orientation. The
    /// current matrix answers the local velocity and the axis-angle (the
    /// exponential map) through the same two
    /// <c>MatrixAngles</c> overloads stock uses; both local vectors are then
    /// rotated into world by <c>AngleMatrix(vecAngles)</c>.
    /// </summary>
    public static void CalcBoneVelocityFromDerivative(
        BoneAngle vecAngles,
        out Vector velocity,
        out Vector angVel,
        in BoneMatrix3x4 current)
    {
        (Quaternion q, Vector vecLocalVelocity) =
            BoneSetupMath.MatrixAnglesQuaternion(current);
        (Vector localAngVel, float angle) = BoneSetupMath.QuaternionAxisAngle(q);
        localAngVel = BoneSetupMath.VectorScale(localAngVel, angle);

        BoneMatrix3x4 matAngles = BoneSetupMath.AngleMatrix(vecAngles);
        velocity = BoneSetupMath.VectorTransform(vecLocalVelocity, matAngles);
        angVel = BoneSetupMath.VectorTransform(localAngVel, matAngles);
    }

    /// <summary>
    /// <c>SetupSingleBoneMatrix</c> — our 639-679 (oracle 571-611): one
    /// frame's local matrix for one bone, straight from the sequence's first
    /// anim (no blend, no pose parameter, <c>s == 0</c>).
    /// </summary>
    /// <remarks>
    /// Stock walks the anim's <c>pNext</c> chain for the record whose
    /// <c>bone</c> is <c>iBone</c>; found <em>and</em> the sequence weight is
    /// positive, the anim answers. Otherwise a <c>STUDIO_DELTA</c> anim is
    /// rest pose (identityquat, origin) and a non-delta anim falls back to
    /// the bone record's own rest transform. The chain walk and
    /// <c>iLocalFrame</c> section adjustment go through
    /// <see cref="StudioHdr.PAnim"/> like every other cluster.
    /// </remarks>
    public static BoneMatrix3x4 SetupSingleBoneMatrix(
        StudioHdr pOwnerHdr,
        int nSequence,
        int iFrame,
        int iBone)
    {
        MstudioSeqDesc seqdesc = pOwnerHdr.SeqDesc(nSequence);
        MstudioAnimDesc animdesc = pOwnerHdr.AnimDesc(seqdesc.Anim(0, 0));
        int iLocalFrame = iFrame;
        MstudioAnim? panim = pOwnerHdr.PAnim(animdesc, ref iLocalFrame);
        float s = 0;
        MstudioBone pbone = pOwnerHdr.Bone(iBone);

        // search for bone
        while (panim is not null && panim.Bone != iBone)
        {
            panim = panim.Next();
        }

        Quaternion boneQuat;
        Vector bonePos;

        // look up animation if found, if not, initialize
        if (panim is not null && seqdesc.Weight(iBone) > 0)
        {
            BoneSetupExtract.CalcBoneQuaternion(
                iLocalFrame, s, pbone, null, panim, out boneQuat);
            BoneSetupExtract.CalcBonePosition(
                iLocalFrame, s, pbone, null, panim, out bonePos);
        }
        else if ((animdesc.Flags & StudioConstants.STUDIO_DELTA) != 0)
        {
            boneQuat = Quaternion.Identity;
            bonePos = Vector.Zero;
        }
        else
        {
            boneQuat = pbone.Quaternion;
            bonePos = pbone.Position;
        }

        return BoneSetupMath.QuaternionMatrix(boneQuat, bonePos);
    }

    /// <summary>
    /// <c>Studio_CalcBoneToBoneTransform</c> — our 1841-1850 (oracle
    /// 1770-1777): the pose-space transform from one bone to another — the
    /// input bone's <c>poseToBone</c> inverted, re-based into the output
    /// bone's pose space. Ragdoll_shared's ask (inventory: the file header's
    /// "answer ragdoll_shared" pair, with the derivatives).
    /// </summary>
    public static BoneMatrix3x4 CalcBoneToBoneTransform(
        StudioHdr pStudioHdr,
        int inputBoneIndex,
        int outputBoneIndex)
    {
        BoneMatrix3x4 inputToPose =
            BoneSetupMath.MatrixInvert(pStudioHdr.Bone(inputBoneIndex).PoseToBone);
        return BoneSetupMath.ConcatTransforms(
            pStudioHdr.Bone(outputBoneIndex).PoseToBone, inputToPose);
    }

    /// <summary>
    /// Stock's <c>MatrixAngles(matrix, QAngle&amp;, Vector&amp;)</c> wrapper
    /// (mathlib.h:848-852): the degrees arm of the float* extraction, then
    /// the translation column.
    /// </summary>
    private static (BoneAngle Angles, Vector Position) ReadAnglesPosition(
        in BoneMatrix3x4 m) =>
        (BoneSetupMath.MatrixAngles(m), m.Position);
}
