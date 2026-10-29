//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// GetBreakableDamage (props.cpp): the damage a breakable takes from a blow,
/// by its modifiers. Bullets times the bullet modifier (twice it for
/// buckshot), clubs times the club one, blasts times the explosive one, and
/// a slash that crushes (a propeller trap) ten times.
/// </summary>
public static class BreakableDamage
{
    public static float Scale(float damage, DamageType type, PropDamageModifiers modifiers)
    {
        if ((type & DamageType.Bullet) != 0)
            damage *= (type & DamageType.Buckshot) != 0 ? modifiers.Bullet * 2f : modifiers.Bullet;

        if ((type & DamageType.Club) != 0)
            damage *= modifiers.Club;

        if ((type & DamageType.Blast) != 0)
            damage *= modifiers.Explosive;

        if ((type & DamageType.Slash) != 0 && (type & DamageType.Crush) != 0)
            damage *= 10f;

        return damage;
    }
}
