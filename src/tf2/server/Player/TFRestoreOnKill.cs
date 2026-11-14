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

using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The kill that restores the killer's health
/// (<c>CTFPlayer::OnKilledOther_Effects</c>' restore arm,
/// tf_player.cpp:11509-11543, off <c>Event_KilledOther</c>): the killing
/// weapon's <c>restore_health_on_kill</c> — a percentage of the holder's
/// unhindered maximum, one health over (stock's bizarre-short-by-one fudge),
/// capped at half as much again the maximum, taken past it
/// (<c>DMG_IGNORE_MAXHEALTH</c>). The Half-Zatoichi's 35% is the row the lane
/// ports it for (items_working.tsv:357); the spy's cloak and the speed boost
/// arms beside it (:11513-11521, :11567-11572) are their rows' to name.
///
/// <para>
/// The rune bonus stock subtracts from the base has no carrier until the
/// powerup mode, so the base is the whole maximum; and the killer's held
/// item answers the walk, as <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> roots it
/// at the weapon record's own row.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFRestoreOnKill(ITFWeaponOwners owners, AttributeHooks attributes,
                                    TFDamageRecords records, ITFPlayerResupply resupply,
                                    IEngineEntities entities)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>The restoring attribute, on the killing weapon's item.</summary>
    public const string RestoreAttrib = "restore_health_on_kill";

    /// <summary>The cap, stock's <c>GetMaxHealth() * 1.5</c> (tf_player.cpp:11536).</summary>
    public const float BuffedMaxScale = 1.5f;

    /// <summary>The player band's death walk, beside the heads, before the records' clear.</summary>
    public int HookOrder => TFPlayerOrders.RestoreOnKill;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (death.Attacker == EntityHandle.None || death.Attacker == death.Player)
            return;

        int attackerIndex = death.Attacker.EntryIndex;

        // The killing blow is the killer's own, dealt by a weapon whose item
        // carries the percentage (the cast stock's dynamic_cast stands for).
        if (records.LastHit(death.Player.EntryIndex) is not { } hit
            || hit.Attacker != death.Attacker
            || owners.ItemOf(attackerIndex, hit.WeaponName) is not { } weapon)
            return;

        int percentage = attributes.Int(0, RestoreAttrib, weapon);
        if (percentage <= 0)
            return;

        int health = entities.GetHealth(death.Attacker);
        int restoreMax = resupply.MaxHealth(death.Attacker);
        int target = (int)(percentage / 100f * restoreMax) + 1;                       // :11531-11534
        int delta = Math.Max(Math.Min(health + target, (int)(restoreMax * BuffedMaxScale)) - health, 0);
        if (delta > 0)
            resupply.TakeHealth(death.Attacker, delta, ignoreMaxHealth: true);        // :11539
    }
}
