//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>One teammate the push-apart may push away from: where they stand and their hull.</summary>
public readonly record struct PushawayBody(Vector Origin, Vector Mins, Vector Maxs);

/// <summary>The player being pushed: what <see cref="TeammatePushaway.Adjust"/> reads of them.</summary>
/// <param name="Origin">Their origin (feet).</param>
/// <param name="Mins">Their hull's mins.</param>
/// <param name="Maxs">Their hull's maxs.</param>
/// <param name="Velocity">Their velocity.</param>
/// <param name="ViewYaw">The command's view yaw (stock flattens the pitch away).</param>
/// <param name="MaxSpeed">Their max speed.</param>
/// <param name="DuckingOnGround">FL_DUCKING and on the ground: stock lets the push reach 4/3 of the max speed then.</param>
public readonly record struct PushawaySelf(Vector Origin, Vector Mins, Vector Maxs, Vector Velocity, float ViewYaw,
                                           float MaxSpeed, bool DuckingOnGround);

/// <summary>
/// The soft push-apart between overlapping teammates who walk through each
/// other (plan A.7): a port of <c>C_TFPlayer::AvoidPlayers</c>
/// (c_tf_player.cpp:6315-6576), the players half (the objects half is TF2's
/// buildings, which the core has none of). The first overlapping teammate
/// adds a sideways move to the command, stronger the deeper the overlap, so a
/// spawn or a doorway does not leave two players stacked.
///
/// <para>
/// ON THE COMMAND, AS STOCK DOES IT: stock adjusts the user command in
/// <c>CreateMove</c>, so the command the server runs already carries the push
/// and the prediction replays the same command; nothing about the push is
/// predicted separately. The client applies it to the local player's command,
/// and the server to a bot's, whose command it makes itself.
/// </para>
/// </summary>
public static class TeammatePushaway
{
    /// <summary><c>tf_max_separation_force</c>'s default (c_tf_player.cpp:6309): the push at full overlap.</summary>
    public const float MaxSeparationForce = 256f;

    /// <summary>The cap's factor for a ducking player on the ground (c_tf_player.cpp's flCropFraction).</summary>
    public const float DuckCropFraction = 1.33333333f;

    /// <summary>
    /// Adds the push away from the first teammate whose box overlaps the
    /// player's to <paramref name="forwardMove"/> and <paramref name="sideMove"/>,
    /// then scales the pair back inside the input limits, keeping its
    /// direction. False, the moves untouched, when nothing overlaps or the
    /// overlap is too slight to push.
    /// </summary>
    public static bool Adjust(in PushawaySelf self, ReadOnlySpan<PushawayBody> teammates, float maxForce,
                              float forwardLimit, float backLimit, float sideLimit,
                              ref float forwardMove, ref float sideMove)
    {
        // The player's box, around a centre half its height up, as stock builds it.
        Vector selfCenter = self.Origin + new Vector(0f, 0f, 0.5f * (self.Maxs.Z - self.Mins.Z));
        Vector selfMin = self.Mins + selfCenter;
        Vector selfMax = self.Maxs + selfCenter;

        // Find an intersecting player.
        int found = -1;
        for (int i = 0; i < teammates.Length; i++)
        {
            PushawayBody other = teammates[i];
            Vector center = other.Origin + new Vector(0f, 0f, 0.5f * (other.Maxs.Z - other.Mins.Z));
            if (BoxesIntersect(selfMin, selfMax, other.Mins + center, other.Maxs + center))
            {
                found = i;
                break;
            }
        }

        if (found < 0)
            return false;

        // Calculate the push strength and direction.
        PushawayBody hit = teammates[found];
        Vector hitCenter = hit.Origin + (hit.Mins + hit.Maxs) * 0.5f;
        Vector delta = hitCenter - selfCenter;
        Vector radius = hit.Maxs - hit.Mins;
        float avoidRadius = new Vector(radius.X, radius.Y, 0f).Length;

        float pushStrength = RemapValClamped(delta.Length, avoidRadius, 0f, 0f, maxForce);

        // Enough push strength to make a difference?
        if (pushStrength < 0.01f)
            return false;

        Vector push;
        Vector flat = new(self.Velocity.X, self.Velocity.Y, 0f);
        if (flat.X * flat.X + flat.Y * flat.Y > 0.1f)
        {
            // CrossProduct( up, velocity ): sideways to the way they move.
            push = Normalized(new Vector(-flat.Y, flat.X, 0f));
        }
        else
        {
            // Not moving, but still intersecting: the view's right.
            push = Right(self.ViewYaw);
        }

        // Move away from the other player.
        Vector separation = delta.Dot(push) < 0 ? push * pushStrength : push * -pushStrength;

        // The push is no faster than the player may move.
        float maxPlayerSpeed = self.MaxSpeed;
        if (self.DuckingOnGround)
            maxPlayerSpeed *= DuckCropFraction;

        if (separation.Dot(separation) > maxPlayerSpeed * maxPlayerSpeed)
            separation = Normalized(separation) * maxPlayerSpeed;

        Vector forward = Forward(self.ViewYaw);
        Vector right = Right(self.ViewYaw);
        Vector direction = Normalized(separation);

        forwardMove += forward.Dot(direction) * pushStrength;
        sideMove += right.Dot(direction) * pushStrength;

        // Clamp the move to within legal limits, preserving direction.
        float forwardScale = 1f;
        if (forwardMove > MathF.Abs(forwardLimit))
            forwardScale = MathF.Abs(forwardLimit) / forwardMove;
        else if (forwardMove < -MathF.Abs(backLimit))
            forwardScale = MathF.Abs(backLimit) / MathF.Abs(forwardMove);

        float sideScale = 1f;
        if (MathF.Abs(sideMove) > MathF.Abs(sideLimit))
            sideScale = MathF.Abs(sideLimit) / MathF.Abs(sideMove);

        float scale = MathF.Min(forwardScale, sideScale);
        forwardMove *= scale;
        sideMove *= scale;
        return true;
    }

    /// <summary><c>IsBoxIntersectingBox</c>: the boxes overlap or touch on every axis.</summary>
    public static bool BoxesIntersect(in Vector min1, in Vector max1, in Vector min2, in Vector max2)
        => min1.X <= max2.X && max1.X >= min2.X
           && min1.Y <= max2.Y && max1.Y >= min2.Y
           && min1.Z <= max2.Z && max1.Z >= min2.Z;

    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float t = Math.Clamp((val - a) / (b - a), 0f, 1f);
        return c + (d - c) * t;
    }

    private static Vector Normalized(in Vector v)
    {
        float length = v.Length;
        return length > 0f ? v * (1f / length) : Vector.Zero;
    }

    /// <summary><c>AngleVectors</c>' forward for a flattened view (pitch and roll 0).</summary>
    private static Vector Forward(float yaw)
    {
        float r = yaw * (MathF.PI / 180f);
        return new Vector(MathF.Cos(r), MathF.Sin(r), 0f);
    }

    /// <summary><c>AngleVectors</c>' right for a flattened view (pitch and roll 0).</summary>
    private static Vector Right(float yaw)
    {
        float r = yaw * (MathF.PI / 180f);
        return new Vector(MathF.Sin(r), -MathF.Cos(r), 0f);
    }
}
