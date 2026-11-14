//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Combat;

using TF2.Shared;

using TF2.Shared.Combat;

using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The kill that refills the demoman's charge (<c>CTFPlayer::Event_KilledOther</c>'s
/// demoman arm, tf_player.cpp:11258-11275): an enemy's death the killer's own
/// blow finished — a melee hit, or the charge's bash — pays the killer's
/// <c>kill_refills_meter</c> fraction straight onto the meter. The attribute is
/// the player-level walk (<c>CALL_ATTRIB_HOOK_FLOAT</c> on the wearer), so the
/// Claidheamohmor's sword and every worn item's provide answer it; the meter's
/// 0..100 is the shared field's own range (tf_player_shared.h:1102). The rune
/// arm's 0.2 (tf_player.cpp:11267-11270) waits for the powerup mode, as the
/// regen's does; the kill_refills_meter event behind it (:11277-11287) is the
/// achievements', not the meter's.
/// </summary>
[SideService(Side.Server)]
public sealed class TFChargeMeterRefills(TFPlayerCarriers carriers, ITFWeaponOwners owners, AttributeHooks attributes,
                                         TFDamageRecords records, IEnginePlayers players)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>The player band's death walk, beside the heads and before the records' clear.</summary>
    public int HookOrder => TFPlayerOrders.ChargeRefills;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (death.Attacker == EntityHandle.None || death.Attacker == death.Player)
            return;

        int attackerIndex = death.Attacker.EntryIndex;
        int victimIndex = death.Player.EntryIndex;

        // The killing blow is the killer's own (the record the layer noted);
        // an enemy victim, and a melee hit or the charge's bash (:11260, :11272).
        if (records.LastHit(victimIndex) is not { } hit
            || hit.Attacker != death.Attacker
            || owners.ClassOf(attackerIndex) != TFClass.Demoman                        // :11258
            || players.GetTeam(death.Player) == players.GetTeam(death.Attacker))       // :11260
            return;

        if ((hit.Bits & TFDamageBits.Melee) == 0 && hit.Custom != TFDamageCustom.ChargeImpact)
            return;                                                                   // :11272

        float refill = attributes.Float(0f, "kill_refills_meter", owners.ManagerOf(attackerIndex));   // :11266
        if (refill <= 0f || carriers.Get(attackerIndex) is not { } state)
            return;

        state.ChargeMeter = Math.Clamp(state.ChargeMeter + refill * 100f, 0f, 100f);  // :11274
    }
}
