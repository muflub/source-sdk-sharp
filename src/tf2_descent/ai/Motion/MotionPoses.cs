//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai;

namespace TF2.Descent.Ai.Motion;

/// <summary>How a body is carried: standing, running on the ground, or in the air.</summary>
public enum MotionGait : byte
{
    Stand = 0,
    Run,
    Air,
}

/// <summary>The four dials a robot body's net state carries (plan_descent_logic 3.7): the move blend and the torso's twist and bend.</summary>
public readonly record struct MotionPose(float MoveX, float MoveY, float BodyYaw, float BodyPitch);

/// <summary>
/// A robot body's pose from its motion, as the player animation state poses a
/// player (the robot models carry the player animations, plan 3.11 row 2).
///
/// <para>
/// <b>The move blend</b> is the nine-way one: the velocity's yaw relative to
/// the body's facing gives <c>x = cos</c>, <c>y = -sin</c>, stretched so the
/// larger is one, then shrunk by the speed's share of the robot's own speed
/// (the stock arm shrinks by the sequence's authored ground speed, which the
/// server cannot ask: CR11). A body that is not moving has both at zero.
/// </para>
///
/// <para>
/// <b>The torso</b> turns to the look the feet have not reached: the yaw
/// dial is the negated difference of the look's yaw and the facing, the pitch
/// dial the look's elevation (the negated engine pitch), each clamped to the
/// dial's reach.
/// </para>
/// </summary>
public static class MotionPoses
{
    /// <summary>The move blend for a floor-plane velocity of (<paramref name="vx"/>, <paramref name="vy"/>) under a body facing <paramref name="facing"/>.</summary>
    public static (float X, float Y) Move(float vx, float vy, float facing, float maxSpeed, float stillSpeed)
    {
        float speed = MathF.Sqrt(vx * vx + vy * vy);
        if (speed <= stillSpeed || maxSpeed <= 0f)
            return (0f, 0f);

        float yaw = MotionAngles.Difference(MathF.Atan2(vy, vx) * (180f / MathF.PI), facing) * (MathF.PI / 180f);
        float x = MathF.Cos(yaw);
        float y = -MathF.Sin(yaw);
        float stretch = MathF.Max(MathF.Abs(x), MathF.Abs(y));
        if (stretch > 0f)
        {
            x /= stretch;
            y /= stretch;
        }

        float share = MathF.Min(1f, speed / maxSpeed);
        return (x * share, y * share);
    }

    /// <summary>The torso dials for a look at yaw <paramref name="lookYaw"/> and elevation <paramref name="lookElevation"/> from a body facing <paramref name="facing"/>.</summary>
    public static (float Yaw, float Pitch) Torso(float lookYaw, float lookElevation, float facing, float yawLimit, float pitchLimit)
    {
        float yaw = -MotionAngles.Difference(lookYaw, facing);
        return (Math.Clamp(yaw, -yawLimit, yawLimit), Math.Clamp(lookElevation, -pitchLimit, pitchLimit));
    }

    /// <summary>The gait a body is in: the air off the ground, else running above <paramref name="runSpeed"/>, else standing.</summary>
    public static MotionGait Gait(bool onGround, AiVector velocity, float runSpeed) =>
        !onGround ? MotionGait.Air : velocity.Length2D > runSpeed ? MotionGait.Run : MotionGait.Stand;
}
