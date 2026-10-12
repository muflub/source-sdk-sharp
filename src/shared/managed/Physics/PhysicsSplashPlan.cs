//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>One effect a body entering water throws: "watersplash" or "waterripple".</summary>
public readonly record struct SplashEffect(string Name, Vector Origin, Vector Normal, QAngle Angles, float Scale,
                                           bool InSlime);

/// <summary>
/// Stock's <c>PhysicsSplash</c> (game/server/physics_fx.cpp) as a planner: what
/// to dispatch for a body crossing a fluid's surface, from the plane, the
/// body's axes and its collide's extents along the plane, its velocity and
/// mass (plan_physics_managed B2.2, the fluid slots).
///
/// <para>
/// The body's footprint on the surface is the box its collide spans along a
/// tangent (its axis least aligned with the normal's best match, crossed
/// back onto the plane) and the binormal. Faster and heavier through a larger
/// footprint is a bigger splash; very slow is a ripple; slower still nothing.
/// A ragdoll part splashes nothing (stock's own effect there is commented
/// out). A footprint over 500 square units adds one to four splashes at the
/// corners. The random draws are taken in stock's order from the engine's
/// stream.
/// </para>
/// </summary>
public static class PhysicsSplashPlan
{
    public const string Splash = "watersplash";
    public const string Ripple = "waterripple";

    /// <param name="axes">The body's world axes, the position matrix's three columns.</param>
    /// <param name="extentAlong">CollideGetExtent: the collide's farthest world point along a direction.</param>
    /// <param name="randomFloat">RandomFloat( lo, hi ).</param>
    /// <param name="randomInt">RandomInt( lo, hi ), inclusive.</param>
    public static List<SplashEffect> Plan(Vector normal, float dist, ReadOnlySpan<Vector> axes,
                                          Func<Vector, Vector> extentAlong, Vector velocity, float mass,
                                          bool partOfRagdoll, bool inSlime,
                                          Func<float, float, float> randomFloat, Func<int, int, int> randomInt)
    {
        var effects = new List<SplashEffect>();

        // BestAxisMatchingNormal, then the axis after it as the tangent.
        int best = 0;
        float bestDot = -1f;
        for (int i = 0; i < 3; i++)
        {
            float dot = MathF.Abs(axes[i].Dot(normal));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        Vector tangent = axes[(best + 1) % 3];
        Vector binormal = normal.Cross(tangent).Normalized;
        tangent = binormal.Cross(normal).Normalized;

        Vector tanMin = extentAlong(tangent * -1f), tanMax = extentAlong(tangent);
        Vector binMin = extentAlong(binormal * -1f), binMax = extentAlong(binormal);

        float min0 = tanMin.Dot(tangent), max0 = tanMax.Dot(tangent);
        float min1 = binMin.Dot(binormal), max1 = binMax.Dot(binormal);
        float center0 = 0.5f * (min0 + max0), center1 = 0.5f * (min1 + max1);
        float extent0 = max0 - center0, extent1 = max1 - center1;

        Vector centerPoint = tangent * center0 + binormal * center1 + normal * dist;
        Vector axis0 = tangent * (max0 - center0);
        Vector axis1 = binormal * (max1 - center1);
        Span<Vector> corner =
        [
            centerPoint - axis0 - axis1,
            centerPoint + axis0 - axis1,
            centerPoint + axis0 + axis1,
            centerPoint - axis0 + axis1,
        ];

        // A ragdoll part: stock's effect here is commented out.
        if (partOfRagdoll)
            return effects;

        float rawSpeed = -normal.Dot(velocity);
        float speed = rawSpeed * rawSpeed * extent0 * extent1 * (1f / 2500000f) * mass * 0.01f;
        speed = Math.Clamp(speed, 0f, 50f);

        bool rippleOnly = false;
        if (speed <= 0.35f)
        {
            if (speed <= 0.1f)
                return effects;
            rippleOnly = true;
        }

        float size = RemapVal(speed, 0.35f, 50f, 8f, 18f);
        float radius = extent0 * extent1;
        string name = rippleOnly ? Ripple : Splash;
        QAngle angles = VectorAngles(normal);

        effects.Add(new SplashEffect(name, centerPoint, normal, angles, size + randomFloat(0f, 2f), inSlime));

        if (radius > 500f)
        {
            int splashes = randomInt(1, 4);
            for (int i = 0; i < splashes && i < corner.Length; i++)
            {
                // RandomVector( -4, 4 ): x, y and z drawn in that order; z dropped.
                float x = randomFloat(-4f, 4f), y = randomFloat(-4f, 4f);
                _ = randomFloat(-4f, 4f);
                Vector point = new Vector(x, y, 0f) + corner[i];
                effects.Add(new SplashEffect(name, point, normal, angles, size + randomFloat(-3f, 1f), inSlime));
            }
        }

        return effects;
    }

    /// <summary>RemapVal: unclamped linear map of <paramref name="value"/> from [a, b] onto [c, d].</summary>
    private static float RemapVal(float value, float a, float b, float c, float d)
        => b == a ? (value >= b ? d : c) : c + (d - c) * (value - a) / (b - a);

    /// <summary>Stock's VectorAngles( forward, angles ): pitch and yaw of a direction, no roll.</summary>
    private static QAngle VectorAngles(Vector forward)
    {
        float yaw, pitch;
        if (forward.Y == 0f && forward.X == 0f)
        {
            yaw = 0f;
            pitch = forward.Z > 0f ? 270f : 90f;
        }
        else
        {
            yaw = MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);
            if (yaw < 0f)
                yaw += 360f;

            float tmp = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            pitch = MathF.Atan2(-forward.Z, tmp) * (180f / MathF.PI);
            if (pitch < 0f)
                pitch += 360f;
        }

        return new QAngle(pitch, yaw, 0f);
    }
}
