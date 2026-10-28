//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The sprites the weapons' client draw effects make beams of, precached as
/// models as the weapons' own <c>Precache</c> did: a beam the client makes
/// by a sprite's NAME finds its index in the networked model table, which only
/// the server fills, so an unprecached one is a beam that never draws.
///
/// <para>
/// <c>CWeaponRPG::Precache</c> (shared/hl2mp/weapon_rpg.cpp): the laser's two
/// beam sprites (the glow, <c>RPG_LASER_SPRITE</c>, is the laser dot's).
/// <c>CWeaponStunStick::Precache</c> (weapon_stunstick.cpp): the lightning its
/// swings and arcs strike, and the three glows its head is drawn with.
/// <c>CWeaponCrossbow::Precache</c> (weapon_crossbow.cpp): the charger's glow
/// and the load's flare, drawn by model index.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "effects/laser1.vmt")]
[Precache(PrecacheKind.Model, "effects/laser1_noz.vmt")]
[Precache(PrecacheKind.Model, "sprites/lgtning.vmt")]
[Precache(PrecacheKind.Model, "sprites/light_glow02_add.vmt")]
[Precache(PrecacheKind.Model, "effects/blueflare1.vmt")]
[Precache(PrecacheKind.Model, "sprites/light_glow02_add_noz.vmt")]
[Precache(PrecacheKind.Model, "sprites/light_glow02_noz.vmt")]
[Precache(PrecacheKind.Model, "sprites/blueflare1.vmt")]
public static class WeaponDrawEffectAssets
{
}
