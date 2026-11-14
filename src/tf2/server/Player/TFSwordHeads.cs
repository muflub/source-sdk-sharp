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
using TF2.Server.Medigun;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The demoman's heads (<c>CTFSword::OnDecapitation</c> through the decapiting
/// sword's <c>player_death</c> listener, tf_weapon_sword.cpp:330-412): a kill
/// the sword dealt takes the victim's heads as well and one more, and pays
/// the attacker — the speed walk reads the count on the next command (stock
/// calls <c>TeamFortress_SetSpeed</c> here), the health grant is +15 under
/// the buffed maximum, and the buff lands.
///
/// <para>
/// The listener's own guard, as the death's record carries it
/// (<see cref="TFDamageRecords"/>): the attacker's hit, the sword's id — the
/// taunt swing's arm (TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING, :404) has no
/// kill path here yet — and the sword decapitating at all
/// (<c>CanDecapitate</c>'s <c>decapitate_type</c>). Stock's demoman gate and
/// <c>GetBestOverhealDecayMult</c> seeding ride the same arm; the decay
/// multiplier has no carrier word yet (the medigun keeps the per-healer
/// entry). The count dies with its holder: stock resets it on the spawn's
/// shared init (the reset's site is the loadout's to name); here at the
/// death, after the gain — the same per-life count one step earlier.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFSwordHeads(TFPlayerCarriers carriers, ITFWeaponOwners owners, AttributeHooks attributes,
                                 TFDamageRecords records, ITFPlayerResupply resupply, ITFPlayerHealing healing,
                                 IEngineEntities entities)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>
    /// The player band's death walk, before the records' clear
    /// (<see cref="TFCombatDeaths"/>' +704): the kill's record is what the
    /// guard reads, and the clear forgets it.
    /// </summary>
    public int HookOrder => TFPlayerOrders.SwordHeads;

    public void OnPlayerDied(PlayerDeath death)
    {
        int victimIndex = death.Player.EntryIndex;

        if (death.Attacker != EntityHandle.None
            && death.Attacker != death.Player
            && records.LastHit(victimIndex) is { } hit
            && hit.Attacker == death.Attacker
            && hit.Weapon == TFWeaponId.Sword
            && owners.ClassOf(death.Attacker.EntryIndex) == TFClass.Demoman
            && owners.ItemOf(death.Attacker.EntryIndex, TFSwordHook.SwordScript) is { } sword
            && attributes.Int(0, "decapitate_type", sword) != 0
            && carriers.Get(death.Attacker.EntryIndex) is { } killer)
        {
            // Off with their heads: the victim's pile and one more.
            killer.Decapitations += (carriers.Get(victimIndex)?.Decapitations ?? 0) + 1;

            if (entities.GetHealth(death.Attacker) < healing.MaxBuffedHealth(death.Attacker))
                resupply.TakeHealth(death.Attacker, TFSwordHook.HealthPerHead, ignoreMaxHealth: true);

            if (carriers.ConditionsOf(death.Attacker.EntryIndex) is { } conditions
                && !conditions.Has(TFCond.DemoBuff))
                conditions.Add(TFCond.DemoBuff);
        }

        // The count dies with its holder.
        if (carriers.Get(victimIndex) is { } victim)
            victim.Decapitations = 0;
    }
}
