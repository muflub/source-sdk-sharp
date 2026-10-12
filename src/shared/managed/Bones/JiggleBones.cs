//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// CJiggleBones (public/jigglebones.cpp, linked from tier3.a) ported to
// managed for plan physics_bones_native_zero step 1.0: the spring a jiggle
// bone is drawn with. It is Valve library code with no engine state in it --
// a per-entity list of per-bone spring states and one integrator -- so it is
// ported rather than kept as an outbound call.
//
// What arrives differently, and why:
//
//   * cl_jiggle_bone_framerate_cutoff (a ConVar, default 20) arrives as the
//     framerateCutoff parameter: no convar in managed code.
//   * The client-only debug overlay arms (cl_jiggle_bone_debug*) are not
//     ported: tier3.a is built without CLIENT_DLL, so the linked native body
//     has none of them either.
//   * mstudiojigglebone_t is read once into JiggleBoneParams.
//   * Vector::NormalizeInPlace is stock's rsqrtss form (len^2 + 1e-10, one
//     Newton step); NormalizeInPlace below is the scalar 1/sqrtf of that same
//     quantity, so the last ulp can differ -- the divergence BoneSetupMath's
//     header already names for VectorNormalize.
//   * The math overloads stock resolves (atan2, acos, sin, cos, fabs) are the
//     float ones; `2.0f * M_PI - angle` is computed in double and rounded,
//     as the double M_PI forces.
//
// State lives in the JiggleBones instance the caller keeps across frames
// (stock: a member of the entity), never in a static.
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Vector = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace SourceSharp.Shared.Bones;

/// <summary>studio.h's <c>mstudiojigglebone_t</c> (140 bytes), read once.</summary>
public readonly struct JiggleBoneParams
{
    /// <summary>studio.h <c>JIGGLE_IS_FLEXIBLE</c>.</summary>
    public const int IsFlexible = 0x01;
    /// <summary>studio.h <c>JIGGLE_IS_RIGID</c>.</summary>
    public const int IsRigid = 0x02;
    /// <summary>studio.h <c>JIGGLE_HAS_YAW_CONSTRAINT</c>.</summary>
    public const int HasYawConstraint = 0x04;
    /// <summary>studio.h <c>JIGGLE_HAS_PITCH_CONSTRAINT</c>.</summary>
    public const int HasPitchConstraint = 0x08;
    /// <summary>studio.h <c>JIGGLE_HAS_ANGLE_CONSTRAINT</c>.</summary>
    public const int HasAngleConstraint = 0x10;
    /// <summary>studio.h <c>JIGGLE_HAS_LENGTH_CONSTRAINT</c>.</summary>
    public const int HasLengthConstraint = 0x20;
    /// <summary>studio.h <c>JIGGLE_HAS_BASE_SPRING</c>.</summary>
    public const int HasBaseSpring = 0x40;
    /// <summary>studio.h <c>JIGGLE_IS_BOING</c>.</summary>
    public const int IsBoing = 0x80;

    public int Flags { get; init; }
    public float Length { get; init; }
    public float TipMass { get; init; }
    public float YawStiffness { get; init; }
    public float YawDamping { get; init; }
    public float PitchStiffness { get; init; }
    public float PitchDamping { get; init; }
    public float AlongStiffness { get; init; }
    public float AlongDamping { get; init; }
    public float AngleLimit { get; init; }
    public float MinYaw { get; init; }
    public float MaxYaw { get; init; }
    public float YawFriction { get; init; }
    public float YawBounce { get; init; }
    public float MinPitch { get; init; }
    public float MaxPitch { get; init; }
    public float PitchFriction { get; init; }
    public float PitchBounce { get; init; }
    public float BaseMass { get; init; }
    public float BaseStiffness { get; init; }
    public float BaseDamping { get; init; }
    public float BaseMinLeft { get; init; }
    public float BaseMaxLeft { get; init; }
    public float BaseLeftFriction { get; init; }
    public float BaseMinUp { get; init; }
    public float BaseMaxUp { get; init; }
    public float BaseUpFriction { get; init; }
    public float BaseMinForward { get; init; }
    public float BaseMaxForward { get; init; }
    public float BaseForwardFriction { get; init; }
    public float BoingImpactSpeed { get; init; }
    public float BoingImpactAngle { get; init; }
    public float BoingDampingRate { get; init; }
    public float BoingFrequency { get; init; }
    public float BoingAmplitude { get; init; }

    /// <summary>
    /// The bone's <c>pProcedure()</c> read as a jiggle record: false when the
    /// bone is out of range, is not a jiggle bone, or carries no record (a
    /// caller then draws it from the pose).
    /// </summary>
    public static bool TryRead(StudioHdr hdr, int bone, out JiggleBoneParams parameters)
    {
        parameters = default;

        if (bone < 0 || bone >= hdr.NumBones)
        {
            return false;
        }

        MstudioBone record = hdr.Bone(bone);
        byte[] image = record.RawImage;

        if (ProceduralBoneRule.Kind(hdr.BoneFlags(bone), record.ProcType) != ProceduralBoneKind.Jiggle)
        {
            return false;
        }

        int procIndex = record.ProcIndex;

        if (procIndex == 0)
        {
            return false;
        }

        int at = record.RawAddress + procIndex;
        float F(int field) => BinaryPrimitives.ReadSingleLittleEndian(image.AsSpan(at + 4 * field, 4));

        parameters = new JiggleBoneParams
        {
            Flags = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at, 4)),
            Length = F(1),
            TipMass = F(2),
            YawStiffness = F(3),
            YawDamping = F(4),
            PitchStiffness = F(5),
            PitchDamping = F(6),
            AlongStiffness = F(7),
            AlongDamping = F(8),
            AngleLimit = F(9),
            MinYaw = F(10),
            MaxYaw = F(11),
            YawFriction = F(12),
            YawBounce = F(13),
            MinPitch = F(14),
            MaxPitch = F(15),
            PitchFriction = F(16),
            PitchBounce = F(17),
            BaseMass = F(18),
            BaseStiffness = F(19),
            BaseDamping = F(20),
            BaseMinLeft = F(21),
            BaseMaxLeft = F(22),
            BaseLeftFriction = F(23),
            BaseMinUp = F(24),
            BaseMaxUp = F(25),
            BaseUpFriction = F(26),
            BaseMinForward = F(27),
            BaseMaxForward = F(28),
            BaseForwardFriction = F(29),
            BoingImpactSpeed = F(30),
            BoingImpactAngle = F(31),
            BoingDampingRate = F(32),
            BoingFrequency = F(33),
            BoingAmplitude = F(34),
        };
        return true;
    }
}

/// <summary>jigglebones.h's <c>JiggleData</c>: one bone's spring, kept across frames.</summary>
public sealed class JiggleData
{
    public int Bone;
    public float LastUpdate;
    public Vector BasePos;
    public Vector BaseLastPos;
    public Vector BaseVel;
    public Vector BaseAccel;
    public Vector TipPos;
    public Vector TipVel;
    public Vector TipAccel;
    public Vector LastLeft;
    public Vector LastBoingPos;
    public Vector BoingDir;
    public Vector BoingVelDir;
    public float BoingSpeed;
    public float BoingTime;
    public int UseGoalMatrixCount;
    public int UseJiggleBoneCount;

    /// <summary><c>JiggleData::Init</c>: the spring at rest at the goal.
    /// The two use-counters are not touched.</summary>
    public void Init(int initBone, float currentTime, Vector initBasePos, Vector initTipPos)
    {
        Bone = initBone;
        LastUpdate = currentTime;

        BasePos = initBasePos;
        BaseLastPos = BasePos;
        BaseVel = Vector.Zero;
        BaseAccel = Vector.Zero;

        TipPos = initTipPos;
        TipVel = Vector.Zero;
        TipAccel = Vector.Zero;

        LastLeft = Vector.Zero;

        LastBoingPos = initBasePos;
        BoingDir = new Vector(0.0f, 0.0f, 1.0f);
        BoingVelDir = Vector.Zero;
        BoingSpeed = 0.0f;
        BoingTime = 0.0f;
    }
}

/// <summary>jigglebones.h's <c>CJiggleBones</c>: an entity's jiggle springs.</summary>
public sealed class JiggleBones
{
    /// <summary><c>cl_jiggle_bone_framerate_cutoff</c>'s default.</summary>
    public const float DefaultFramerateCutoff = 20.0f;

    private readonly List<JiggleData> _jiggleBoneState = new();

    /// <summary>
    /// <c>CJiggleBones::GetJiggleData</c>: this bone's spring, made at rest on
    /// first ask (jiggling for at least 16 frames).
    /// </summary>
    public JiggleData GetJiggleData(int bone, float currentTime, Vector initBasePos, Vector initTipPos)
    {
        foreach (JiggleData existing in _jiggleBoneState)
        {
            if (existing.Bone == bone)
            {
                return existing;
            }
        }

        JiggleData data = new();
        data.Init(bone, currentTime, initBasePos, initTipPos);

        // Start out using jiggle bones for at least 16 frames.
        data.UseGoalMatrixCount = 0;
        data.UseJiggleBoneCount = 16;

        // AddToHead: the newest spring is found first.
        _jiggleBoneState.Insert(0, data);
        return data;
    }

    /// <summary>
    /// <c>CJiggleBones::BuildJiggleTransformations</c>: spring physics from
    /// the goal matrix the pose would give this bone into its drawn matrix.
    /// <paramref name="framerateCutoff"/> is <c>cl_jiggle_bone_framerate_cutoff</c>.
    /// </summary>
    public void BuildJiggleTransformations(int boneIndex, float currentTime, in JiggleBoneParams jiggleInfo,
                                           in BoneMatrix3x4 goalMX, ref BoneMatrix3x4 boneMX,
                                           float framerateCutoff, bool coordSystemIsFlipped = false)
    {
        Vector goalBasePosition = goalMX.Position;

        Vector goalLeft = Column(goalMX, 0);
        Vector goalUp = Column(goalMX, 1);
        Vector goalForward = Column(goalMX, 2);

        // compute goal tip position
        Vector goalTip = goalBasePosition + jiggleInfo.Length * goalForward;

        JiggleData data = GetJiggleData(boneIndex, currentTime, goalBasePosition, goalTip);

        // if frames have been skipped since our last update, we were likely
        // disabled and re-enabled, so re-init
        const float timeTolerance = 0.5f;

        if (currentTime - data.LastUpdate > timeTolerance)
        {
            data.Init(boneIndex, currentTime, goalBasePosition, goalTip);
        }

        if (IsZero(data.LastLeft))
        {
            data.LastLeft = goalLeft;
        }

        // Limit the step to avoid blowups; below the frame-rate cutoff draw
        // the goal instead, in runs (32 frames) so the two do not flicker.
        float deltaT = currentTime - data.LastUpdate;

        const float thousandHZ = 0.001f;
        bool maxDeltaT = deltaT < thousandHZ;
        bool useGoalMatrix = framerateCutoff <= 0.0f || deltaT > (1.0f / framerateCutoff);

        if (useGoalMatrix)
        {
            data.UseGoalMatrixCount = 32;
        }
        else if (data.UseGoalMatrixCount > 0)
        {
            useGoalMatrix = true;
            data.UseGoalMatrixCount--;
        }
        else
        {
            data.UseJiggleBoneCount = 32;
        }

        if (data.UseJiggleBoneCount > 0)
        {
            data.UseJiggleBoneCount--;
            data.UseGoalMatrixCount = 0;
            useGoalMatrix = false;
        }

        if (maxDeltaT)
        {
            deltaT = thousandHZ;
        }
        else if (useGoalMatrix)
        {
            boneMX = goalMX;
            return;
        }

        // lastUpdate here, so skipped jiggle bones re-init when they return
        data.LastUpdate = currentTime;

        //
        // Bone tip flex
        //
        if ((jiggleInfo.Flags & (JiggleBoneParams.IsFlexible | JiggleBoneParams.IsRigid)) != 0)
        {
            // gravity, in global space
            data.TipAccel.Z -= jiggleInfo.TipMass;

            if ((jiggleInfo.Flags & JiggleBoneParams.IsFlexible) != 0)
            {
                // decompose into local coordinates
                Vector error = goalTip - data.TipPos;

                Vector localError = new(
                    BoneSetupMath.Dot(goalLeft, error),
                    BoneSetupMath.Dot(goalUp, error),
                    BoneSetupMath.Dot(goalForward, error));

                float localVelX = BoneSetupMath.Dot(goalLeft, data.TipVel);
                float localVelY = BoneSetupMath.Dot(goalUp, data.TipVel);

                float yawAccel = jiggleInfo.YawStiffness * localError.X - jiggleInfo.YawDamping * localVelX;
                float pitchAccel = jiggleInfo.PitchStiffness * localError.Y - jiggleInfo.PitchDamping * localVelY;

                if ((jiggleInfo.Flags & JiggleBoneParams.HasLengthConstraint) != 0)
                {
                    // drive tip towards goal tip position
                    data.TipAccel += yawAccel * goalLeft + pitchAccel * goalUp;
                }
                else
                {
                    // allow flex along length of spring
                    float localVelZ = BoneSetupMath.Dot(goalForward, data.TipVel);
                    float alongAccel = jiggleInfo.AlongStiffness * localError.Z - jiggleInfo.AlongDamping * localVelZ;

                    data.TipAccel += yawAccel * goalLeft + pitchAccel * goalUp + alongAccel * goalForward;
                }
            }

            // simple euler integration
            data.TipVel += data.TipAccel * deltaT;
            data.TipPos += data.TipVel * deltaT;

            // clear this timestep's accumulated accelerations
            data.TipAccel = Vector.Zero;

            if ((jiggleInfo.Flags & (JiggleBoneParams.HasYawConstraint | JiggleBoneParams.HasPitchConstraint)) != 0)
            {
                ApplyYawPitchConstraints(data, jiggleInfo, goalMX, goalBasePosition, goalLeft, goalUp, goalForward);
            }

            // needed for matrix assembly below
            Vector forward = data.TipPos - goalBasePosition;
            NormalizeInPlace(ref forward);

            if ((jiggleInfo.Flags & JiggleBoneParams.HasAngleConstraint) != 0)
            {
                // enforce max angular error
                float dot = BoneSetupMath.Dot(forward, goalForward);
                float angleBetween = MathF.Acos(dot);

                if (dot < 0.0f)
                {
                    // 2.0f * M_PI is a double product: the subtraction is in double.
                    angleBetween = (float)(2.0 * Math.PI - angleBetween);
                }

                if (angleBetween > jiggleInfo.AngleLimit)
                {
                    float maxBetween = jiggleInfo.Length * MathF.Sin(jiggleInfo.AngleLimit);

                    Vector delta = goalTip - data.TipPos;
                    NormalizeInPlace(ref delta);

                    data.TipPos = goalTip - maxBetween * delta;

                    forward = data.TipPos - goalBasePosition;
                    NormalizeInPlace(ref forward);
                }
            }

            if ((jiggleInfo.Flags & JiggleBoneParams.HasLengthConstraint) != 0)
            {
                // enforce spring length
                data.TipPos = goalBasePosition + jiggleInfo.Length * forward;

                // zero velocity along forward bone axis
                data.TipVel -= BoneSetupMath.Dot(data.TipVel, forward) * forward;
            }

            // Build bone matrix to align along current tip direction
            Vector left;
            Vector up;

            if (coordSystemIsFlipped)
            {
                left = BoneSetupMath.CrossProduct(forward, goalUp);
                NormalizeInPlace(ref left);
                up = BoneSetupMath.CrossProduct(left, forward);
            }
            else
            {
                left = BoneSetupMath.CrossProduct(goalUp, forward);
                NormalizeInPlace(ref left);
                up = BoneSetupMath.CrossProduct(forward, left);
            }

            boneMX = FromColumns(left, up, forward, goalBasePosition);
        }

        //
        // Bone base flex
        //
        if ((jiggleInfo.Flags & JiggleBoneParams.HasBaseSpring) != 0)
        {
            // gravity
            data.BaseAccel.Z -= jiggleInfo.BaseMass;

            // simple spring
            Vector error = goalBasePosition - data.BasePos;
            data.BaseAccel += jiggleInfo.BaseStiffness * error - jiggleInfo.BaseDamping * data.BaseVel;

            data.BaseVel += data.BaseAccel * deltaT;
            data.BasePos += data.BaseVel * deltaT;

            // clear this timestep's accumulated accelerations
            data.BaseAccel = Vector.Zero;

            // constrain to limits
            error = data.BasePos - goalBasePosition;
            Vector localError = new(
                BoneSetupMath.Dot(goalLeft, error),
                BoneSetupMath.Dot(goalUp, error),
                BoneSetupMath.Dot(goalForward, error));

            Vector localVel = new(
                BoneSetupMath.Dot(goalLeft, data.BaseVel),
                BoneSetupMath.Dot(goalUp, data.BaseVel),
                BoneSetupMath.Dot(goalForward, data.BaseVel));

            // horizontal constraint
            if (localError.X < jiggleInfo.BaseMinLeft)
            {
                localError.X = jiggleInfo.BaseMinLeft;
                data.BaseAccel -= jiggleInfo.BaseLeftFriction * (localVel.Y * goalUp + localVel.Z * goalForward);
            }
            else if (localError.X > jiggleInfo.BaseMaxLeft)
            {
                localError.X = jiggleInfo.BaseMaxLeft;
                data.BaseAccel -= jiggleInfo.BaseLeftFriction * (localVel.Y * goalUp + localVel.Z * goalForward);
            }

            if (localError.Y < jiggleInfo.BaseMinUp)
            {
                localError.Y = jiggleInfo.BaseMinUp;
                data.BaseAccel -= jiggleInfo.BaseUpFriction * (localVel.X * goalLeft + localVel.Z * goalForward);
            }
            else if (localError.Y > jiggleInfo.BaseMaxUp)
            {
                localError.Y = jiggleInfo.BaseMaxUp;
                data.BaseAccel -= jiggleInfo.BaseUpFriction * (localVel.X * goalLeft + localVel.Z * goalForward);
            }

            if (localError.Z < jiggleInfo.BaseMinForward)
            {
                localError.Z = jiggleInfo.BaseMinForward;
                data.BaseAccel -= jiggleInfo.BaseForwardFriction * (localVel.X * goalLeft + localVel.Y * goalUp);
            }
            else if (localError.Z > jiggleInfo.BaseMaxForward)
            {
                localError.Z = jiggleInfo.BaseMaxForward;
                data.BaseAccel -= jiggleInfo.BaseForwardFriction * (localVel.X * goalLeft + localVel.Y * goalUp);
            }

            data.BasePos = goalBasePosition + localError.X * goalLeft + localError.Y * goalUp + localError.Z * goalForward;

            // fix up velocity (Vector / float is a multiply by 1.0f / b)
            data.BaseVel = (data.BasePos - data.BaseLastPos) * (1.0f / deltaT);
            data.BaseLastPos = data.BasePos;

            if ((jiggleInfo.Flags & (JiggleBoneParams.IsFlexible | JiggleBoneParams.IsRigid)) == 0)
            {
                // no tip flex - use bone's goal orientation
                boneMX = goalMX;
            }

            // update bone position
            boneMX.Position = data.BasePos;
        }
        else if ((jiggleInfo.Flags & JiggleBoneParams.IsBoing) != 0)
        {
            Boing(data, jiggleInfo, goalMX, goalBasePosition, goalLeft, goalUp, goalForward, deltaT, ref boneMX);
        }
        else if ((jiggleInfo.Flags & (JiggleBoneParams.IsFlexible | JiggleBoneParams.IsRigid)) == 0)
        {
            // no flex at all - just use goal matrix
            boneMX = goalMX;
        }
    }

    /// <summary>The yaw and pitch limits: the tip clipped onto the limit
    /// plane, its velocity zeroed (stock removed the friction/bounce).</summary>
    private static void ApplyYawPitchConstraints(JiggleData data, in JiggleBoneParams jiggleInfo,
                                                 in BoneMatrix3x4 goalMX, Vector goalBasePosition,
                                                 Vector goalLeft, Vector goalUp, Vector goalForward)
    {
        // find components of spring vector in local coordinate system
        Vector along = data.TipPos - goalBasePosition;
        Vector localAlong = new(
            BoneSetupMath.Dot(goalLeft, along),
            BoneSetupMath.Dot(goalUp, along),
            BoneSetupMath.Dot(goalForward, along));

        if ((jiggleInfo.Flags & JiggleBoneParams.HasYawConstraint) != 0)
        {
            // enforce yaw constraints in local XZ plane
            float yawError = MathF.Atan2(localAlong.X, localAlong.Z);

            bool isAtLimit = false;
            float yaw = 0.0f;

            if (yawError < jiggleInfo.MinYaw)
            {
                isAtLimit = true;
                yaw = jiggleInfo.MinYaw;
            }
            else if (yawError > jiggleInfo.MaxYaw)
            {
                isAtLimit = true;
                yaw = jiggleInfo.MaxYaw;
            }

            if (isAtLimit)
            {
                (float sy, float cy) = BoneSetupMath.SinCos(yaw);

                // yaw matrix: columns left (cy, 0, -sy), up (0, 1, 0), forward (sy, 0, cy)
                BoneMatrix3x4 yawMatrix = new()
                {
                    Row0 = new Vector4(cy, 0f, sy, 0f),
                    Row1 = new Vector4(0f, 1.0f, 0f, 0f),
                    Row2 = new Vector4(-sy, 0f, cy, 0f),
                };

                BoneMatrix3x4 limitMatrix = BoneSetupMath.ConcatTransforms(goalMX, yawMatrix);
                Vector limitUp = Column(limitMatrix, 1);
                Vector limitForward = Column(limitMatrix, 2);
                Vector limitLeft = Column(limitMatrix, 0);

                Vector limitAlong = new(
                    BoneSetupMath.Dot(limitLeft, along),
                    BoneSetupMath.Dot(limitUp, along),
                    BoneSetupMath.Dot(limitForward, along));

                // clip to limit plane
                data.TipPos = goalBasePosition + limitAlong.Y * limitUp + limitAlong.Z * limitForward;

                // removed friction and velocity clipping against constraint -
                // was causing simulation blowups (stock, MSB 12/9/2010)
                data.TipVel = Vector.Zero;

                // update along vectors for use by pitch constraint
                along = data.TipPos - goalBasePosition;
                localAlong = new Vector(
                    BoneSetupMath.Dot(goalLeft, along),
                    BoneSetupMath.Dot(goalUp, along),
                    BoneSetupMath.Dot(goalForward, along));
            }
        }

        if ((jiggleInfo.Flags & JiggleBoneParams.HasPitchConstraint) != 0)
        {
            // enforce pitch constraints in local YZ plane
            float pitchError = MathF.Atan2(localAlong.Y, localAlong.Z);

            bool isAtLimit = false;
            float pitch = 0.0f;

            if (pitchError < jiggleInfo.MinPitch)
            {
                isAtLimit = true;
                pitch = jiggleInfo.MinPitch;
            }
            else if (pitchError > jiggleInfo.MaxPitch)
            {
                isAtLimit = true;
                pitch = jiggleInfo.MaxPitch;
            }

            if (isAtLimit)
            {
                (float sp, float cp) = BoneSetupMath.SinCos(pitch);

                // pitch matrix: columns left (1, 0, 0), up (0, cp, -sp), forward (0, sp, cp)
                BoneMatrix3x4 pitchMatrix = new()
                {
                    Row0 = new Vector4(1.0f, 0f, 0f, 0f),
                    Row1 = new Vector4(0f, cp, sp, 0f),
                    Row2 = new Vector4(0f, -sp, cp, 0f),
                };

                BoneMatrix3x4 limitMatrix = BoneSetupMath.ConcatTransforms(goalMX, pitchMatrix);
                Vector limitLeft = Column(limitMatrix, 0);
                Vector limitUp = Column(limitMatrix, 1);
                Vector limitForward = Column(limitMatrix, 2);

                Vector limitAlong = new(
                    BoneSetupMath.Dot(limitLeft, along),
                    BoneSetupMath.Dot(limitUp, along),
                    BoneSetupMath.Dot(limitForward, along));

                // clip to limit plane
                data.TipPos = goalBasePosition + limitAlong.X * limitLeft + limitAlong.Z * limitForward;

                data.TipVel = Vector.Zero;
            }
        }
    }

    /// <summary>The boing: a squash-and-stretch sinusoid along the last
    /// impact direction, re-aimed on a fast change of velocity.</summary>
    private static void Boing(JiggleData data, in JiggleBoneParams jiggleInfo, in BoneMatrix3x4 goalMX,
                              Vector goalBasePosition, Vector goalLeft, Vector goalUp, Vector goalForward,
                              float deltaT, ref BoneMatrix3x4 boneMX)
    {
        // estimate velocity
        Vector vel = goalBasePosition - data.LastBoingPos;

        data.LastBoingPos = goalBasePosition;

        float speed = NormalizeInPlace(ref vel);
        if (speed < 0.00001f)
        {
            vel = new Vector(0f, 0f, 1.0f);
            speed = 0.0f;
        }
        else
        {
            speed /= deltaT;
        }

        data.BoingTime += deltaT;

        // if velocity changed a lot, we impacted and should *boing*
        const float minSpeed = 5.0f;
        const float minReBoingTime = 0.5f;
        if ((speed > minSpeed || data.BoingSpeed > minSpeed) && data.BoingTime > minReBoingTime)
        {
            if (MathF.Abs(data.BoingSpeed - speed) > jiggleInfo.BoingImpactSpeed
                || BoneSetupMath.Dot(vel, data.BoingVelDir) < jiggleInfo.BoingImpactAngle)
            {
                data.BoingTime = 0.0f;
                data.BoingDir = -vel;
            }
        }

        data.BoingVelDir = vel;
        data.BoingSpeed = speed;

        float damping = 1.0f - (jiggleInfo.BoingDampingRate * data.BoingTime);
        if (damping < 0.01f)
        {
            // boing has entirely damped out
            boneMX = goalMX;
            return;
        }

        damping *= damping;
        damping *= damping;

        float flex = jiggleInfo.BoingAmplitude * MathF.Cos(jiggleInfo.BoingFrequency * data.BoingTime) * damping;

        float squash = 1.0f + flex;
        float stretch = 1.0f - flex;

        boneMX = FromColumns(goalLeft, goalUp, goalForward, Vector.Zero);

        // build transform into "boing space", where Z is along primary boing axis
        Vector boingSide;
        if (MathF.Abs(data.BoingDir.X) < 0.9f)
        {
            boingSide = BoneSetupMath.CrossProduct(data.BoingDir, new Vector(1.0f, 0f, 0f));
        }
        else
        {
            boingSide = BoneSetupMath.CrossProduct(data.BoingDir, new Vector(0f, 0f, 1.0f));
        }

        NormalizeInPlace(ref boingSide);

        Vector boingOtherSide = BoneSetupMath.CrossProduct(data.BoingDir, boingSide);

        // rows: side, other side, boing direction
        BoneMatrix3x4 xfrmToBoingCoordsMX = new()
        {
            Row0 = new Vector4(boingSide.X, boingSide.Y, boingSide.Z, 0.0f),
            Row1 = new Vector4(boingOtherSide.X, boingOtherSide.Y, boingOtherSide.Z, 0.0f),
            Row2 = new Vector4(data.BoingDir.X, data.BoingDir.Y, data.BoingDir.Z, 0.0f),
        };

        // squash and stretch in "boing space"
        BoneMatrix3x4 boingMX = new()
        {
            Row0 = new Vector4(squash, 0.0f, 0.0f, 0.0f),
            Row1 = new Vector4(0.0f, squash, 0.0f, 0.0f),
            Row2 = new Vector4(0.0f, 0.0f, stretch, 0.0f),
        };

        // back from boing space (inverse is transpose since orthogonal)
        BoneMatrix3x4 xfrmFromBoingCoordsMX = new()
        {
            Row0 = new Vector4(xfrmToBoingCoordsMX.Row0.X, xfrmToBoingCoordsMX.Row1.X, xfrmToBoingCoordsMX.Row2.X, 0.0f),
            Row1 = new Vector4(xfrmToBoingCoordsMX.Row0.Y, xfrmToBoingCoordsMX.Row1.Y, xfrmToBoingCoordsMX.Row2.Y, 0.0f),
            Row2 = new Vector4(xfrmToBoingCoordsMX.Row0.Z, xfrmToBoingCoordsMX.Row1.Z, xfrmToBoingCoordsMX.Row2.Z, 0.0f),
        };

        // put it all together (MatrixMultiply is ConcatTransforms)
        BoneMatrix3x4 xfrmMX = BoneSetupMath.ConcatTransforms(xfrmToBoingCoordsMX, boingMX);
        xfrmMX = BoneSetupMath.ConcatTransforms(xfrmMX, xfrmFromBoingCoordsMX);
        boneMX = BoneSetupMath.ConcatTransforms(boneMX, xfrmMX);

        boneMX.Position = goalBasePosition;
    }

    /// <summary><c>MatrixGetColumn( m, c )</c>.</summary>
    private static Vector Column(in BoneMatrix3x4 m, int c) => new(m.Row0[c], m.Row1[c], m.Row2[c]);

    /// <summary>A matrix whose columns are the three axes and the position.</summary>
    private static BoneMatrix3x4 FromColumns(Vector c0, Vector c1, Vector c2, Vector position) => new()
    {
        Row0 = new Vector4(c0.X, c1.X, c2.X, position.X),
        Row1 = new Vector4(c0.Y, c1.Y, c2.Y, position.Y),
        Row2 = new Vector4(c0.Z, c1.Z, c2.Z, position.Z),
    };

    /// <summary><c>Vector::IsZero()</c>: every component strictly inside ±0.01.</summary>
    private static bool IsZero(Vector v, float tolerance = 0.01f) =>
        v.X > -tolerance && v.X < tolerance &&
        v.Y > -tolerance && v.Y < tolerance &&
        v.Z > -tolerance && v.Z < tolerance;

    /// <summary>
    /// <c>Vector::NormalizeInPlace</c>: stock's rsqrtss body over
    /// <c>len^2 + 1e-10f</c>, answering <c>sqrlen * invlen</c>; here the
    /// reciprocal square root is the scalar 1/sqrtf (file header).
    /// </summary>
    private static float NormalizeInPlace(ref Vector v)
    {
        float sqrlen = BoneSetupMath.VectorLengthSqr(v) + 1.0e-10f;
        float invlen = 1.0f / MathF.Sqrt(sqrlen);
        v = new Vector(v.X * invlen, v.Y * invlen, v.Z * invlen);
        return sqrlen * invlen;
    }
}
