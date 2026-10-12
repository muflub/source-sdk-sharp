//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>What one splash effect is: fx_water.h's three.</summary>
public enum ClientSplashKind
{
    /// <summary>FX_GunshotSplash( origin, normal, scale ).</summary>
    GunshotSplash,

    /// <summary>FX_GunshotSlimeSplash( origin, normal, scale ).</summary>
    GunshotSlimeSplash,

    /// <summary>FX_WaterRipple( origin, scale, &amp;color, lifetime, alpha ).</summary>
    WaterRipple,
}

/// <summary>One effect of a body entering water, with the arguments fx_water.h takes.</summary>
public readonly record struct ClientSplashEffect(ClientSplashKind Kind, Vector Origin, Vector Normal, float Scale,
                                                 Vector Color, float Lifetime, float Alpha);

/// <summary>The reads a client splash makes.</summary>
public interface IClientSplashWorld
{
    /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
    int PointContents(Vector point);

    /// <summary><c>FX_GetSplashLighting</c>: the water's colour and luminosity at a point.</summary>
    (Vector Color, float Luminosity) SplashLighting(Vector point);
}

/// <summary>
/// Stock's client <c>PhysicsSplash</c> (game/client/physics.cpp) as a planner
/// (plan_client_novalve C6, cluster 9; lane c6-input): a ragdoll part crossing
/// a fluid's surface at 25 in/s or faster throws a gunshot splash (over 150
/// in/s) or a ripple, and four more at the corners of its footprint. The
/// footprint is the box the part's collide spans, at the ENTITY's transform,
/// along a tangent (the entity axis after the one best matching the normal,
/// crossed back onto the plane) and the binormal -- client and server
/// physics each carry their own copy of this geometry in stock, and so do
/// their ports. Kept as stock has them: the four fast splashes are at the
/// CENTRE, not at their corners (the corner point is drawn and unused); and in
/// slime a slow body makes nothing. The random draws are stock's, in its
/// order; an argument list with two draws (the corner ripple) is taken left to
/// right.
/// </summary>
public static class ClientSplashPlan
{
    /// <summary>FVPHYSICS_PART_OF_RAGDOLL.</summary>
    private const ushort PartOfRagdoll = 0x0008;

    /// <summary>CONTENTS_SLIME.</summary>
    private const int ContentsSlime = 0x10;

    /// <param name="axes">The entity's world axes, EntityToWorldTransform's three columns.</param>
    /// <param name="extentAlong">CollideGetExtent: the part's collide's farthest world point along a direction, at the entity's transform.</param>
    public static List<ClientSplashEffect> Plan(ushort gameFlags, Vector velocity, Vector normal, float dist,
                                                ReadOnlySpan<Vector> axes, Func<Vector, Vector> extentAlong,
                                                IClientSplashWorld world, IUniformRandom random)
    {
        var effects = new List<ClientSplashEffect>();

        //FIXME: For now just allow ragdolls for E3 - jdw
        if ((gameFlags & PartOfRagdoll) == 0)
            return effects;

        float impactSpeed = velocity.Length;
        if (impactSpeed < 25.0f)
            return effects;

        // Find the local axis that best matches the water surface normal
        int bestAxis = 0;
        float bestDot = -1f;
        for (int i = 0; i < 3; i++)
        {
            float dot = MathF.Abs(axes[i].Dot(normal));
            if (dot > bestDot)
            {
                bestDot = dot;
                bestAxis = i;
            }
        }

        Vector tangent = axes[(bestAxis + 1) % 3];
        Vector binormal = normal.Cross(tangent).Normalized;
        tangent = binormal.Cross(normal).Normalized;

        // The object's extents in that basis, a box centred on the plane.
        Vector tanMin = extentAlong(tangent * -1f), tanMax = extentAlong(tangent);
        Vector binMin = extentAlong(binormal * -1f), binMax = extentAlong(binormal);

        float min0 = tanMin.Dot(tangent), max0 = tanMax.Dot(tangent);
        float min1 = binMin.Dot(binormal), max1 = binMax.Dot(binormal);
        float center0 = 0.5f * (min0 + max0), center1 = 0.5f * (min1 + max1);

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

        int contents = world.PointContents(centerPoint - new Vector(0, 0, 2));
        bool inSlime = (contents & ContentsSlime) != 0;

        Vector color = Vector.Zero;
        float luminosity = 1.0f;
        if (!inSlime)
            (color, luminosity) = world.SplashLighting(centerPoint + normal * 8.0f);

        ClientSplashKind gunshot = inSlime ? ClientSplashKind.GunshotSlimeSplash : ClientSplashKind.GunshotSplash;
        if (impactSpeed > 150)
            effects.Add(new ClientSplashEffect(gunshot, centerPoint, normal, random.RandomFloat(8, 10), Vector.Zero, 0f, 0f));
        else if (!inSlime)
            effects.Add(new ClientSplashEffect(ClientSplashKind.WaterRipple, centerPoint, normal, 1.5f, color, 1.5f, luminosity));

        for (int i = 0; i < 4; i++)
        {
            // RandomVector( -32, 32 ): x, y and z drawn in that order; z dropped.
            float x = random.RandomFloat(-32f, 32f), y = random.RandomFloat(-32f, 32f);
            _ = random.RandomFloat(-32f, 32f);
            Vector point = new Vector(x, y, 0f) + corner[i];

            if (impactSpeed > 150)
            {
                // At the centre, as stock has it: `point` is not what it passes.
                effects.Add(new ClientSplashEffect(gunshot, centerPoint, normal, random.RandomFloat(4, 6), Vector.Zero, 0f, 0f));
            }
            else if (!inSlime)
            {
                float scale = random.RandomFloat(0.25f, 0.5f);
                float alpha = random.RandomFloat(0.5f, 1.0f);
                effects.Add(new ClientSplashEffect(ClientSplashKind.WaterRipple, point, normal, scale, color, luminosity, alpha));
            }
        }

        return effects;
    }
}
