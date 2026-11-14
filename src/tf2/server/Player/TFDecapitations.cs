//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Player;

/// <summary>
/// The Bazaar Bargain's bank (<c>CTFSniperRifleDecap::OnPlayerKill</c>,
/// tf_weapon_sniperrifle.cpp:1722-1733): a kill the decapitation rifle's
/// headshot dealt banks its owner one decapitation, the count the rifle's
/// charge rate scales by (<c>SniperRifleChargeRateMod</c> :1742-1745 through
/// <c>GetCount</c> :1750-1759). The count rides the carrier's
/// <see cref="TFPlayerNetState.Decapitations"/> — the sword's bank shares it
/// (stock's one <c>m_iDecapitations</c>) — and stock zeroes it only at the
/// shared's init, so nothing here clears it: the death walks that do
/// (<see cref="TFSwordHeads"/>) run their own counts.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDecapitations(TFPlayerCarriers carriers, TFDamageRecords records)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>
    /// The player band's death walk, after the sword's heads
    /// (<see cref="TFPlayerOrders.SwordHeads"/>) and before the records'
    /// clear (<see cref="TFCombatDeaths"/>' +704): the kill's record is what
    /// the guard reads, and the clear forgets it.
    /// </summary>
    public int HookOrder => TFPlayerOrders.DecapHeads;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (death.Attacker != EntityHandle.None
            && death.Attacker != death.Player
            && records.LastHit(death.Player.EntryIndex) is { } hit
            && hit.Attacker == death.Attacker
            && hit.Weapon == TFWeaponId.SniperrifleDecap
            && hit.Custom == TFDamageCustom.Headshot
            && carriers.Get(death.Attacker.EntryIndex) is { } killer)
            killer.Decapitations += 1;
    }
}
