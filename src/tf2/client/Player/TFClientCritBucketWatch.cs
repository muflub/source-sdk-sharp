//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;

namespace TF2.Client.Player;

/// <summary>
/// The client's half of stock's <c>CBaseCombatWeapon</c> constructor
/// (basecombatweapon_shared.cpp:103-106): a fresh weapon entity starts its
/// crit bucket full (<c>m_flCritTokenBucket =
/// tf_weapon_criticals_bucket_default</c>). Stock refills it because the
/// respawn's kit re-creates the entity; our weapons are forgotten and
/// re-given as new entities at a recycled index, so the entry keyed by that
/// index must be forgotten too — and <see cref="TFCritBuckets"/> reseeds it
/// full on the next ask. The arsenal reports the adoption (
/// <see cref="ILocalWeaponWatcher"/>); the server side of the same reset is
/// <c>TFPlayerSpawns</c>' ask at spawn.
/// </summary>
[SideService(Side.Client, As = [typeof(ILocalWeaponWatcher)])]
public sealed class TFClientCritBucketWatch(TFCritBuckets buckets) : ILocalWeaponWatcher
{
    public void LocalWeaponSpawned(int weaponIndex) => buckets.Reset(weaponIndex);
}
