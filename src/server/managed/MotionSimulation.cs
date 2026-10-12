//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>IMotionEvent::simresult_e: how vphysics applies the answer.</summary>
public enum MotionSimResult
{
    Nothing = 0,
    LocalAcceleration = 1,
    LocalForce = 2,
    GlobalAcceleration = 3,
    GlobalForce = 4,
}

/// <summary>
/// One motion controller's parameters and its feedback state: what managed's
/// IMotionEvent answers vphysics from, each tick, for each object it acts on
/// (plan_physics_bones_native_zero 2.4). The fields of the four native
/// controllers of shell_motioncontrollers.cpp in one record, read by kind.
/// </summary>
public sealed class MotionState
{
    public MotionState(MotionControllerKind kind) => Kind = kind;

    public MotionControllerKind Kind { get; }

    // Constant force (CSourceSharpConstantForce).
    public Vector Linear;
    public Vector Angular;
    public bool Local;
    public bool Active;
    public bool Acceleration;

    // Motor (CSourceSharpMotor, stock's CMotorController).
    public Vector Axis;
    public float Speed;
    public float MaxTorque;
    public float InertiaFactor = 1f;
    public float LastSpeed;
    public float LastAcceleration;
    public float LastForce;
    public float Damping = 1f;

    // Keep upright (CSourceSharpKeepUpright, stock's CKeepUpright).
    public Vector WorldGoalAxis = new(0f, 0f, 1f);
    public Vector LocalTestAxis = new(0f, 0f, 1f);
    public float AngularLimit;
    public bool DampAllRotation;

    // Medium (CSourceSharpMedium, stock's CTriggerVPhysicsMotion).
    public float GravityScale = 1f;
    public float AddAirDensity;
    public float LinearLimit;
    public float LinearLimitDelta;
    public float LinearScale = 1f;
    public float MediumAngularLimit;
    public float AngularScale = 1f;
    public float LinearForce;
    public Vector LinearForceAngles;
    public float LinearLimitTime;
    public float LinearLimitStart;
    public float LinearLimitStartTime;
}

/// <summary>
/// The arithmetic of the four controllers, carried from shell_motioncontrollers.cpp
/// (itself phys_controller.cpp's CMotorController and CKeepUpright and
/// triggers.cpp's CTriggerVPhysicsMotion::Simulate) term for term. Pure: given
/// a state, the object's current pose and velocity and a step, an answer.
/// </summary>
public static class MotionSimulation
{
    /// <summary>
    /// One controller's answer for one object. <paramref name="linear"/> and
    /// <paramref name="angular"/> come in zeroed, as vphysics hands them.
    /// </summary>
    public static MotionSimResult Simulate(MotionState state, nint physicsObject, float deltaTime,
                                           IVPhysicsJoints vphysics, IPhysicsJointWorld world,
                                           out Vector linear, out Vector angular)
    {
        switch (state.Kind)
        {
            case MotionControllerKind.ConstantForce:
                return ConstantForce(state, out linear, out angular);
            case MotionControllerKind.Motor:
                return Motor(state, vphysics.PositionMatrix(physicsObject), vphysics.Velocity(physicsObject).Angular,
                             world.Inertia(physicsObject), out linear, out angular);
            case MotionControllerKind.KeepUpright:
                return KeepUpright(state, physicsObject, deltaTime, vphysics, out linear, out angular);
            case MotionControllerKind.Medium:
                return Medium(state, physicsObject, deltaTime, vphysics, world, out linear, out angular);
            default:
                linear = Vector.Zero;
                angular = Vector.Zero;
                return MotionSimResult.Nothing;
        }
    }

    /// <summary>
    /// A steady push, a steady twist, or both. Local means along the thing's
    /// own axes; acceleration means the numbers do not go through the mass.
    /// </summary>
    public static MotionSimResult ConstantForce(MotionState state, out Vector linear, out Vector angular)
    {
        if (!state.Active)
        {
            linear = Vector.Zero;
            angular = Vector.Zero;
            return MotionSimResult.Nothing;
        }

        linear = state.Linear;
        angular = state.Angular;

        if (state.Acceleration)
            return state.Local ? MotionSimResult.LocalAcceleration : MotionSimResult.GlobalAcceleration;

        return state.Local ? MotionSimResult.LocalForce : MotionSimResult.GlobalForce;
    }

    /// <summary>
    /// stock's CMotorController::Simulate: drives toward a target angular
    /// speed, with its resistance damping. The BUGBUG is Valve's own: "This
    /// heuristic is a little flaky".
    /// </summary>
    public static MotionSimResult Motor(MotionState state, Float3x4 matrix, Vector currentRotAxis, Vector inertia,
                                        out Vector linear, out Vector angular)
    {
        linear = Vector.Zero;
        angular = Vector.Zero;

        if (state.Speed == 0f)
            return MotionSimResult.Nothing;

        Vector motorAxisLs = JointMath.IRotate(state.Axis, matrix);

        float currentSpeed = currentRotAxis.Dot(motorAxisLs);
        float inertiaAlong = DotProductAbs(inertia, motorAxisLs);

        // Absolute acceleration, not integrated over the step.
        float accel = state.Speed - currentSpeed;
        float rotForce = accel * inertiaAlong * state.InertiaFactor;

        // BUGBUG: This heuristic is a little flaky
        // UNDONE: Make a better heuristic for speed control
        if (MathF.Abs(state.LastAcceleration) > 0f)
        {
            float deltaSpeed = currentSpeed - state.LastSpeed;

            if (deltaSpeed * accel > 0f)
            {
                // Gaining on it, but resistance is eating some of the force.
                float factor = deltaSpeed / state.LastAcceleration;
                factor = 1f - Clamp(factor, 0f, 1f);
                rotForce += state.LastForce * factor * state.Damping;
            }
            else if (currentSpeed != 0f)
            {
                // A steady state that is not the target?
                float increase = deltaSpeed / state.LastAcceleration;
                if (MathF.Abs(increase) < 0.05f)
                    rotForce += state.LastForce * state.Damping;
            }
        }

        if (state.MaxTorque != 0f)
            rotForce = Clamp(rotForce, -state.MaxTorque, state.MaxTorque);

        state.LastForce = rotForce;
        state.LastAcceleration = inertiaAlong != 0f ? rotForce / inertiaAlong : 0f;
        state.LastSpeed = currentSpeed;

        angular = motorAxisLs * rotForce;
        return MotionSimResult.LocalForce;
    }

    /// <summary>
    /// phys_controller.cpp's ComputeRotSpeedToAlignAxes: the torque that brings
    /// one axis onto another without overshooting.
    /// </summary>
    public static Vector ComputeRotSpeedToAlignAxes(Vector testAxis, Vector alignAxis, Vector currentSpeed,
                                                    float damping, float scale, float maxSpeed)
    {
        Vector rotationAxis = JointMath.Cross(testAxis, alignAxis);

        // atan2() is well defined, so do a Dot & Cross instead of asin(Cross)
        float cosine = testAxis.Dot(alignAxis);
        float sine = MoveVec.Normalize(ref rotationAxis);
        float angle = MathF.Atan2(sine, cosine);

        angle = angle * (180f / MathF.PI);
        Vector result = rotationAxis * scale * angle;
        result -= rotationAxis * damping * currentSpeed.Dot(rotationAxis);

        float len = MoveVec.Normalize(ref result);

        if (len > maxSpeed)
            len = maxSpeed;

        return result * len;
    }

    /// <summary>stock's CKeepUpright::Simulate.</summary>
    public static MotionSimResult KeepUpright(MotionState state, nint physicsObject, float deltaTime,
                                              IVPhysicsJoints vphysics, out Vector linear, out Vector angular)
    {
        linear = Vector.Zero;
        angular = Vector.Zero;

        if (!state.Active || deltaTime <= 0f)
            return MotionSimResult.Nothing;

        Vector angVel = vphysics.Velocity(physicsObject).Angular;
        Float3x4 matrix = vphysics.PositionMatrix(physicsObject);

        Vector currentLocalTargetAxis = JointMath.IRotate(state.WorldGoalAxis, matrix);

        float invDeltaTime = 1f / deltaTime;

        // Damping everything cancels the object's existing spin as well as
        // righting it.
        float damping = state.DampAllRotation ? 0f : 1f;

        angular = ComputeRotSpeedToAlignAxes(state.LocalTestAxis, currentLocalTargetAxis, angVel,
                                             damping, invDeltaTime, state.AngularLimit);

        if (state.DampAllRotation)
            angular -= angVel;

        angular *= invDeltaTime;
        return MotionSimResult.LocalAcceleration;
    }

    /// <summary>
    /// The medium's speed limit as it stands: a limit can arrive over a
    /// period, and this is the interpolation (RemapVal over the ramp).
    /// </summary>
    public static float CurrentLinearLimit(MotionState state, float curTime)
    {
        if (state.LinearLimitTime == 0f)
            return state.LinearLimit;

        float dt = curTime - state.LinearLimitStartTime;

        if (dt >= state.LinearLimitTime)
        {
            state.LinearLimitTime = 0f;
            return state.LinearLimit;
        }

        dt /= state.LinearLimitTime;
        return RemapVal(dt, 0f, 1f, state.LinearLimitStart, state.LinearLimit);
    }

    /// <summary>Ramps to a new limit from wherever the last ramp had reached.</summary>
    public static void RampLinearLimit(MotionState state, float target, float duration, float curTime)
    {
        state.LinearLimitStart = CurrentLinearLimit(state, curTime);
        state.LinearLimitStartTime = curTime;
        state.LinearLimit = target;
        state.LinearLimitTime = duration;
    }

    /// <summary>
    /// CTriggerVPhysicsMotion::Simulate, unchanged: given the medium's nine
    /// numbers and one object's velocity, an acceleration. Each neutral value
    /// (a scale of 1, a limit of 0) is the way of not doing that part.
    /// </summary>
    public static MotionSimResult Medium(MotionState state, nint physicsObject, float deltaTime,
                                         IVPhysicsJoints vphysics, IPhysicsJointWorld world,
                                         out Vector linear, out Vector angular)
    {
        linear = Vector.Zero;
        angular = Vector.Zero;
        float curTime = world.CurTime();

        if (state.GravityScale != 1f)
        {
            // assume object already has 1.0 gravities applied to it, so apply the additional amount
            linear.Z -= (state.GravityScale - 1f) * world.Gravity();
        }

        if (state.LinearForce != 0f)
        {
            Vector forceDir = MoveVec.AngleForward(state.LinearForceAngles);
            linear = VectorMA(linear, state.LinearForce, forceDir);
        }

        bool hasAirDensity = state.AddAirDensity != 0f;
        bool hasLinearLimit = CurrentLinearLimit(state, curTime) != 0f;
        bool hasLinearScale = state.LinearScale != 1f;
        bool hasAngularLimit = state.MediumAngularLimit != 0f;
        bool hasAngularScale = state.AngularScale != 1f;

        if (hasAirDensity || hasLinearLimit || hasLinearScale || hasAngularLimit || hasAngularScale)
        {
            (Vector vel, Vector angVel) = vphysics.Velocity(physicsObject);
            vel += linear * deltaTime; // account for gravity scale

            Vector unitVel = vel;
            Vector unitAngVel = angVel;

            float speed = MoveVec.Normalize(ref unitVel);
            float angSpeed = MoveVec.Normalize(ref unitAngVel);

            float speedScale = 0f;
            float angSpeedScale = 0f;

            if (hasAirDensity)
            {
                float linearDrag = -0.5f * state.AddAirDensity * vphysics.LinearDrag(physicsObject, unitVel) * deltaTime;
                if (linearDrag < -1f)
                    linearDrag = -1f;
                speedScale += linearDrag / deltaTime;

                float angDrag = -0.5f * state.AddAirDensity * vphysics.AngularDrag(physicsObject, unitAngVel) * deltaTime;
                if (angDrag < -1f)
                    angDrag = -1f;
                angSpeedScale += angDrag / deltaTime;
            }

            if (CurrentLinearLimit(state, curTime) != 0f && speed > state.LinearLimit)
            {
                float deltaVel = (CurrentLinearLimit(state, curTime) - speed) / deltaTime;
                if (state.LinearLimitDelta != 0f)
                {
                    float maxDeltaVel = -state.LinearLimitDelta / deltaTime;
                    if (deltaVel < maxDeltaVel)
                        deltaVel = maxDeltaVel;
                }
                linear = VectorMA(linear, deltaVel, unitVel);
            }
            if (hasAngularLimit && angSpeed > state.MediumAngularLimit)
            {
                angular += unitAngVel * ((state.MediumAngularLimit - angSpeed) / deltaTime);
            }
            if (hasLinearScale)
            {
                speedScale = ((speedScale + 1f) * state.LinearScale) - 1f;
            }
            if (hasAngularScale)
            {
                angSpeedScale = ((angSpeedScale + 1f) * state.AngularScale) - 1f;
            }
            linear += vel * speedScale;
            angular += angVel * angSpeedScale;
        }

        // Acceleration, not force: a medium slows a heavy crate and a light
        // one by the same proportion.
        return MotionSimResult.GlobalAcceleration;
    }

    /// <summary>mathlib's DotProductAbs.</summary>
    public static float DotProductAbs(Vector v0, Vector v1)
        => MathF.Abs(v0.X * v1.X) + MathF.Abs(v0.Y * v1.Y) + MathF.Abs(v0.Z * v1.Z);

    /// <summary>tier0's clamp template, in its order.</summary>
    private static float Clamp(float val, float min, float max)
    {
        if (max < min)
            return max;
        if (val < min)
            return min;
        if (val > max)
            return max;
        return val;
    }

    /// <summary>mathlib.h's RemapVal.</summary>
    private static float RemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        return c + (d - c) * (val - a) / (b - a);
    }

    /// <summary>mathlib's VectorMA: start + scale * direction, per component.</summary>
    private static Vector VectorMA(Vector start, float scale, Vector direction)
        => new(start.X + scale * direction.X, start.Y + scale * direction.Y, start.Z + scale * direction.Z);
}
