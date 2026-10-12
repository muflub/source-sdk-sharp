//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One entry of an object's friction snapshot, as FindMaxContact reads it:
/// the object on the other side, whether it can move and how heavy it is, how
/// hard it presses, and where.
/// </summary>
public readonly record struct FrictionContact(nint Other, bool OtherMoveable, float OtherMass,
                                              float NormalForce, Vector Point, Vector Normal);

/// <summary>The contact FindMaxContact chose: who, where, and the force along the normal.</summary>
public readonly record struct MaxContact(nint Other, Vector Position, Vector Force);

/// <summary>
/// Stock's <c>FindMaxContact</c> (game/server/physics.cpp:628): the decider
/// managed's <see cref="PhysicsSolver"/> asks (plan_physics_bones_native_zero
/// 2.1; native find_max_contact.h, its twin since B1, is deleted). ShouldFreezeObject
/// asks it for the moveable object, heavier than the frozen one, pressing on it
/// past a threshold.
///
/// <para>
/// STOCK'S QUIRK, KEPT: the running maximum starts at the threshold and is never
/// raised, so of several contacts over it the LAST one walked wins, not the
/// strongest.
/// </para>
/// </summary>
public static class PhysicsMaxContact
{
    /// <summary>The chosen contact, or null when none qualifies.</summary>
    public static MaxContact? Find(float mass, float minForce, ReadOnlySpan<FrictionContact> contacts)
    {
        float maxForce = minForce;   // never raised: stock's quirk, see above
        MaxContact? found = null;

        foreach (ref readonly FrictionContact c in contacts)
        {
            if (c.OtherMoveable && c.OtherMass > mass && c.NormalForce > maxForce)
                found = new MaxContact(c.Other, c.Point, c.Normal * c.NormalForce);
        }

        return found;
    }
}
