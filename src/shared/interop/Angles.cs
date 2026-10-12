//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// Turning a facing into directions.
///
/// Source stores orientation as pitch/yaw/roll and most things that act on the
/// world need a direction instead — which way a push shoves, which way a trigger
/// faces. Kept here in C# rather than reached for across the boundary: it is
/// arithmetic with no engine state in it, so it can be tested directly.
/// </summary>
public static class Angles
{
    /// <summary>The three axes of a facing: where it looks, its right, and its up.</summary>
    public readonly record struct Basis(Vector Forward, Vector Right, Vector Up);

    /// <summary>
    /// Source's AngleVectors. Pitch is inverted relative to what the sign
    /// suggests — a positive pitch looks DOWN — which is why forward.z is
    /// negated rather than taken directly.
    /// </summary>
    public static Basis Vectors(Vector angles)
    {
        float pitch = angles.X * MathF.PI / 180f;
        float yaw   = angles.Y * MathF.PI / 180f;
        float roll  = angles.Z * MathF.PI / 180f;

        float sp = MathF.Sin(pitch), cp = MathF.Cos(pitch);
        float sy = MathF.Sin(yaw),   cy = MathF.Cos(yaw);
        float sr = MathF.Sin(roll),  cr = MathF.Cos(roll);

        var forward = new Vector(cp * cy, cp * sy, -sp);

        var right = new Vector(
            -1f * sr * sp * cy + -1f * cr * -sy,
            -1f * sr * sp * sy + -1f * cr * cy,
            -1f * sr * cp);

        var up = new Vector(
            cr * sp * cy + -sr * -sy,
            cr * sp * sy + -sr * cy,
            cr * cp);

        return new Basis(forward, right, up);
    }

    /// <summary>Just the facing direction, which is what most callers want.</summary>
    public static Vector Forward(Vector angles) => Vectors(angles).Forward;

    /// <summary>The three axes of a facing that says it is one.</summary>
    public static Basis Vectors(QAngle angles) => Vectors(angles.AsVector());

    /// <summary>Which way a facing points.</summary>
    public static Vector Forward(QAngle angles) => Vectors(angles).Forward;

    /// <summary>
    /// Source's VectorAngles: the pitch and yaw that would point along a
    /// direction. The inverse of <see cref="Forward"/>, and roll is always zero
    /// because a direction does not carry one.
    ///
    /// <para>
    /// The answers are in [0,360) rather than [-180,180], which is what stock
    /// produces and what the engine's own angle interpolation expects. The
    /// straight-up and straight-down case is handled separately because atan2
    /// of two zeroes is not the answer — it is a coin toss between one and the
    /// next, and a projectile fired at the sky would yaw randomly.
    /// </para>
    /// </summary>
    public static Vector FromVector(Vector direction)
    {
        float yaw, pitch;

        if (direction.X == 0f && direction.Y == 0f)
        {
            yaw = 0f;

            // 270 rather than -90, to stay in the same range as the other branch.
            pitch = direction.Z > 0f ? 270f : 90f;
        }
        else
        {
            yaw = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
            if (yaw < 0f)
                yaw += 360f;

            float flat = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            // Negated for the same reason Vectors negates forward.z: a positive
            // pitch looks DOWN.
            pitch = MathF.Atan2(-direction.Z, flat) * 180f / MathF.PI;
            if (pitch < 0f)
                pitch += 360f;
        }

        return new Vector(pitch, yaw, 0f);
    }
}
