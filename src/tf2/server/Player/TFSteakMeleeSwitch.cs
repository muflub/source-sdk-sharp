//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// <c>OnRemoveTaunting</c>'s steak arm (<c>tf_player_shared.cpp:4062-4077</c>,
/// GAME_DLL): the Buffalo Steak's eat ends with the heavy put to his melee, where
/// the lock (<c>TF_COND_CANNOT_SWITCH_FROM_MELEE</c>, which
/// <see cref="TFMeleeBehaviour.CanHolster"/> enforces) holds him; with no melee to
/// switch to, the safety net takes the buff and the lock away.
///
/// <para>
/// Stock gates the arm on <c>m_bBiteEffectWasApplied</c>, which the steak's bite
/// sets (<c>tf_weapon_lunchbox.cpp:401</c>) and this very removal clears
/// (:4079). The only other source of the lock is the Mannpower knockout rune
/// (<c>entity_rune.cpp:279</c>), which this build has not, so the lock standing
/// at a taunt's end is the steak's: the flag's question is the lock's.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFSteakMeleeSwitch(ITFMapPlayers players, Arsenal arsenal, TFWeaponScripts scripts)
    : ITFConditionHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.SteakMeleeSwitch;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Taunting || !players.InCond(player, TFCond.CannotSwitchFromMelee))
            return;

        // Weapon_GetSlot( TF_WPN_TYPE_MELEE ) (:4066): the carried melee.
        foreach (var held in arsenal.Holdings(player))
        {
            if (scripts.GetQuiet(held.ClassName) is { Category: TFWeaponCategory.Melee })
            {
                arsenal.Switch(player, held.ClassName);                  // :4069
                return;
            }
        }

        // The safety net (:4071-4075).
        players.RemoveCond(player, TFCond.EnergyBuff);
        players.RemoveCond(player, TFCond.CannotSwitchFromMelee);
    }
}
