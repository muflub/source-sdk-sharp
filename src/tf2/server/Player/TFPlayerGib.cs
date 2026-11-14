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
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// Whether a death's wound tears the body apart — stock's
/// <c>CTFPlayer::ShouldGib</c> (tf_player.cpp:10914-10975), asked once per
/// death at the ragdoll's making and carried to the client as
/// <c>DT_TFRagdoll</c>'s <c>m_bGib</c> (see <see cref="RagdollNetState.Gib"/>).
///
/// <para>
/// Arms this port does not carry, named rather than silently dropped: the
/// Mann vs. Machine refusal (this mod has no MvM); the
/// <c>crit_on_hard_hit</c> attribute (its absence makes the hard-hit gate below
/// permanent, which is stock's own behaviour at the attribute's default); and
/// the feign-death roll, which lives in the feign's own death path. The
/// <c>crit_kill_will_gib</c> walk rides on the death hook, over the killing
/// weapon's item. The
/// <c>DMG_HALF_FALLOFF</c> arm of the blast gate has no bit in
/// <see cref="DamageType"/> to spell it with, so the gate is blast alone.
/// </para>
/// </summary>
public static class TFPlayerGibRule
{
    /// <summary>
    /// The gib answer for a death by <paramref name="bits"/> of damage that
    /// left <paramref name="health"/>, under <c>tf_playergib</c>
    /// <paramref name="mode"/> — in stock's order, each arm falling through to
    /// the next; <paramref name="gibOnCrit"/> is the killing weapon's walked
    /// <c>crit_kill_will_gib</c>.
    /// </summary>
    public static bool ShouldGib(int mode, DamageType bits, TFCritType crit, TFDamageCustom custom, int health,
                                 bool gibOnCrit = false)
    {
        // tf_playergib != 1 decides alone: 0 never gibbing, 2 always (:10914-10923).
        if (mode != 1)
            return mode > 1;

        // The explode suicide's mark (tf_player.cpp:10930): stock keeps a
        // member, this port a damage bit the command ORs on
        // (PlayerCheatCommands.Explode) — the death reads it here once either
        // way, and a corpse does not die twice to un-consume it.
        if ((bits & DamageType.AlwaysGib) != 0)
            return true;

        // A crit from a weapon holding crit_kill_will_gib always gibs
        // (tf_player.cpp:10936-10943), the walk answered by the death hook.
        if (crit != TFCritType.None && gibOnCrit)
            return true;

        if (custom == TFDamageCustom.Croc)          // :10945
            return true;

        // The hard-hit gate with no attribute set: anything but a blast leaves
        // a body (:10953).
        if ((bits & DamageType.Blast) == 0)
            return false;

        if (crit != TFCritType.None)                // :10959
            return true;

        if (health <= -10)                          // :10963, past the gib threshold
            return true;

        return false;                               // :10974
    }
}

/// <summary>
/// Writes the death's gib answer onto the body the death left, the way
/// stock's <c>CreateTFPlayerRagdoll</c> does (<c>pRagdoll-&gt;m_bGib = bGib</c>,
/// tf_player.cpp:15619). It reads the killing blow from the same record the
/// death notice does, so it stands before <c>TFCombatDeaths</c> clears them.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerGib(TFPlayerGibConVar playerGib, TFDamageRecords records, IEngineEntities entities,
    IEntityDirectory directory, AttributeHooks? hooks = null, ITFWeaponOwners? owners = null)
    : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.PlayerGib;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (directory.Get(death.Body) is not SsRagdoll body)
            return;

        // No recorded hit (a death the damage layer did not see) gibing on
        // stock's fall-through: an unrecorded blow is none of the arms.
        TFHitRecord? hit = records.LastHit(death.EntIndex);

        // The killing weapon's crit_kill_will_gib, stock's ON_OTHER hook over
        // the hit's weapon entity (tf_player.cpp:10939-10940): the killing
        // player's item under the hit's weapon name answers the walk.
        bool gibOnCrit = hit is { Crit: not TFCritType.None } w && hooks is { } walk && owners is { } holder
                         && walk.Int(0, "crit_kill_will_gib", holder.ItemOf(w.Attacker.EntryIndex, w.WeaponName)) > 0;

        body.State.Gib = TFPlayerGibRule.ShouldGib(playerGib.Value,
            hit?.Bits ?? DamageType.None, hit?.Crit ?? TFCritType.None,
            hit?.Custom ?? TFDamageCustom.None, entities.GetHealth(death.Player), gibOnCrit);
    }
}
