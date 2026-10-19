//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Ammo the mod defines, in the order both sides register it.
///
/// Shared rather than server-side because the index is the wire format: a
/// weapon's ammo type is networked as its position in this table, so the client
/// resolving that index depends on having built the same table in the same
/// order. That is the same reason the user message list lives here.
///
/// Carry limits are game-balance numbers, which is why a weapon does not get to
/// declare its own.
/// </summary>
public static class AmmoTypes
{
    /// <summary>
    /// Damage types come from Source's DMG_* bits; only the ones actually used
    /// are named, rather than mirroring the whole set.
    /// </summary>
    public static readonly ImmutableArray<AmmoType> All =
    [
        // Damage is per bullet and belongs to the ammo, not the weapon. Two
        // weapons sharing a type hit equally hard and differ by rate of fire,
        // spread and pellets — which is exactly how HL2 tells a pistol from an
        // SMG. The numbers are HL2's own sk_plr_dmg_* defaults.
        //
        // The force is NOT optional, and zero is not a quiet default: it is what
        // pushes a body or a physics prop when a bullet lands, and
        // CalculateBulletDamageForce asserts on a zero one. HL2 derives these
        // from a bullet mass in grains and a muzzle velocity in feet per second,
        // exaggerated three and a half times; these are those numbers, computed
        // — 200 grains at 1225 ft/s for the small arms, 800 at 5000 for the
        // magnum, 400 at 1200 for buckshot.
        new("Pistol",   DamageType.Bullet, MaxCarry: 150) { Damage =  5f, DamageForce =   668f },
        new("SMG1",     DamageType.Bullet, MaxCarry: 225) { Damage =  4f, DamageForce =   668f },
        new("357",      DamageType.Bullet, MaxCarry:  12) { Damage = 40f, DamageForce = 10906f },
        new("Buckshot", DamageType.Bullet, MaxCarry:  30) { Damage =  8f, DamageForce =  1309f },
        new("AR2",      DamageType.Bullet, MaxCarry:  60) { Damage =  8f, DamageForce =   668f },

        // Thrown rather than fired, so it has no impulse and its damage is the
        // explosion's rather than a bullet's. Registered because a weapon whose
        // ammo type is not in this table resolves to an index of -1 and can
        // never have any: weapon_frag looked equipped and simply clicked.
        new("Grenade", DamageType.Blast, MaxCarry: 5),

        // Carried by map items rather than by any weapon this mod fires yet, but
        // the type has to exist before an item can hand it over — GivePlayerAmmo
        // resolves by name and a miss silently gives nothing.
        new("SMG1_Grenade", DamageType.Blast, MaxCarry:  3),
        new("AR2AltFire",   DamageType.Blast, MaxCarry:  3),
        new("RPG_Round",    DamageType.Blast, MaxCarry:  3),

        // APPENDED (Phase 10's weapon battery found it): the crossbow's script
        // names XBowBolt, and an unregistered name is the same silent -1 the
        // Grenade comment above describes — the bolt in the clip fired, the
        // reserve read zero forever, and a reload had nothing to load. Found
        // by driving the reload live, which nothing had done for the crossbow.
        // At the END because the index is the wire format: appending keeps
        // every existing index; inserting would renumber them. HL2's numbers —
        // sk_plr_dmg_crossbow 100, ten carried, 800 grains at 8000 ft/s.
        new("XBowBolt", DamageType.Bullet, MaxCarry: 10) { Damage = 100f, DamageForce = 17450f },
    ];
}
