//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>One contact of the stressed object's friction snapshot, as the stress sum reads it.</summary>
/// <param name="Force">GetNormalForce; a contact at 0 or below is skipped.</param>
/// <param name="Normal">GetSurfaceNormal.</param>
/// <param name="OwnerKey">The other entity's owner, else itself: one slot per pusher.</param>
/// <param name="Moveable">The other object IsMoveable.</param>
/// <param name="Friendly">Liked, nav-ignored or player-held: its force is dropped.</param>
/// <param name="PhysicallyControlled">MOVETYPE_VPHYSICS, or a physically controlled shadow.</param>
/// <param name="IsOwnersGround">The other entity is the subject's ground entity.</param>
/// <param name="Mass">The other object's mass.</param>
public readonly record struct StressContact(float Force, Vector Normal, nint OwnerKey, bool Moveable,
                                            bool Friendly, bool PhysicallyControlled, bool IsOwnersGround,
                                            float Mass);

/// <summary>The stressed object and its environment.</summary>
/// <param name="LastImpulse">The shadow or player controller's last impulse.</param>
/// <param name="Gravity">The environment's gravity, as a length.</param>
/// <param name="Timestep">The simulation timestep.</param>
public readonly record struct StressSubject(float ObjMass, bool ObjMoveable, bool ObjStatic, bool OwnerIsVPhysics,
                                            Vector LastImpulse, float Gravity, float Timestep);

/// <summary>Stock's vphysics_objectstress_t, and the sum it returns (kg).</summary>
public readonly record struct ObjectStress(float ExertedStress, float ReceivedStress, bool HasNonStaticStress,
                                           bool HasLargeObjectContact, float Sum);

/// <summary>
/// Stock's <c>CalculateObjectStress</c> (game/server/physics_impact_damage.cpp:504),
/// the live sum since plan_physics_bones_native_zero 2.3: PlayerShadowStress
/// calls it for each awake player hull. It began as a characterisation twin
/// of native object_stress.h (plan_physics_managed B1), which retired with its
/// caller; its facts carry every case that gtest had.
///
/// <para>
/// Slot 0 is the static world, slot 1 the friendly contacts (cleared), and
/// each pusher has its own slot from 2, keyed by owner. Stock's fixed table of
/// pushers is 64 here and folds any beyond into the last.
/// </para>
/// </summary>
public static class PhysicsObjectStress
{
    /// <summary>Stock's "large object": a pusher this heavy that is not the ground.</summary>
    public const float LargeObjectMass = 500f;

    private const int MaxPushers = 64;

    public static ObjectStress Compute(in StressSubject subject, ReadOnlySpan<StressContact> contacts)
    {
        Span<nint> keys = stackalloc nint[MaxPushers];
        Span<Vector> force = stackalloc Vector[MaxPushers];
        keys.Clear();
        force.Clear();
        int slots = 2;
        bool hasLarge = false;

        foreach (ref readonly StressContact contact in contacts)
        {
            float f = contact.Force;
            if (!(f > 0f))
                continue;

            int slot;
            if (!contact.Moveable)
            {
                slot = 0;
            }
            else if (contact.Friendly)
            {
                slot = 1;
            }
            else
            {
                if (contact.Mass >= LargeObjectMass && !contact.IsOwnersGround)
                    hasLarge = true;

                slot = slots;
                for (int i = slots - 1; i >= 2; --i)
                {
                    if (keys[i] == contact.OwnerKey)
                    {
                        slot = i;
                        break;
                    }
                }

                if (slot == slots)
                {
                    if (slots >= MaxPushers)
                        slot = MaxPushers - 1;
                    else
                        keys[slots++] = contact.OwnerKey;
                }
            }

            // A shadow does not feel another shadow: only a VPhysics owner, or a
            // physically controlled pusher, passes force between them.
            if (slot != 0 && !subject.OwnerIsVPhysics && !contact.PhysicallyControlled)
                f = 0f;

            force[slot] = force[slot] + contact.Normal * f;
        }

        force[1] = Vector.Zero;

        float sum = 0f;
        Vector negative = Vector.Zero, positive = Vector.Zero;
        for (int s = slots - 1; s >= 0; --s)
        {
            sum += force[s].Length;
            negative = negative - Negatives(force[s]);
            positive = positive + Positives(force[s]);
        }

        var inner = new Vector(Larger(positive.X, negative.X), Larger(positive.Y, negative.Y), Larger(positive.Z, negative.Z));
        var outer = new Vector(Smaller(positive.X, negative.X), Smaller(positive.Y, negative.Y), Smaller(positive.Z, negative.Z));

        if (!subject.OwnerIsVPhysics && subject.ObjMoveable)
        {
            float input = subject.Timestep > 0f
                ? subject.LastImpulse.Length * (1f / subject.Timestep) * subject.ObjMass
                : 0f;

            if (input > 0f)
            {
                float innerLength = inner.Length;
                if (innerLength < input)
                {
                    Vector delta = inner * (1f - innerLength / input);
                    inner = inner + delta;
                    float deltaLength = delta.Length;
                    sum -= deltaLength;

                    float outerLength = outer.Length;
                    Vector unit = outerLength > 0f ? outer * (1f / outerLength) : Vector.Zero;
                    outerLength = Math.Max(outerLength - deltaLength, 0f);
                    outer = unit * outerLength;
                }
            }
        }

        float invGravity = subject.Gravity > 0f ? 1f / subject.Gravity : 1f;
        sum *= invGravity;
        inner = inner * invGravity;
        outer = outer * invGravity;

        if (!subject.ObjMoveable)
        {
            outer = inner;
            inner = Vector.Zero;
            if (!subject.ObjStatic)
                sum += subject.ObjMass;
        }
        else if (sum > subject.ObjMass)
        {
            sum = subject.ObjMass + (sum - subject.ObjMass) * 0.5f;
        }

        return new ObjectStress(inner.Length, outer.Length, slots > 2, hasLarge, sum);
    }

    /// <summary>Each axis's magnitude when that axis is negative, else 0 (as a negative sum).</summary>
    private static Vector Negatives(Vector v) => new(Math.Min(v.X, 0f), Math.Min(v.Y, 0f), Math.Min(v.Z, 0f));

    private static Vector Positives(Vector v) => new(Math.Max(v.X, 0f), Math.Max(v.Y, 0f), Math.Max(v.Z, 0f));

    /// <summary>Internal: the excess of the larger side over the smaller.</summary>
    private static float Larger(float positive, float negative)
        => negative < positive ? positive - negative : negative - positive;

    /// <summary>External: the smaller side.</summary>
    private static float Smaller(float positive, float negative)
        => negative < positive ? negative : positive;
}
