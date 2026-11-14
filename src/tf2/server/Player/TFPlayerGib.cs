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
/// Mann vs. Machine refusal (this mod has no MvM), and the feign-death roll,
/// which lives in the feign's own death path. The <c>crit_kill_will_gib</c>
/// and <c>crit_on_hard_hit</c> walks ride on the death hook, over the killing
/// weapon's item. The <c>DMG_HALF_FALLOFF</c> arm of the blast gate has no bit
/// in <see cref="DamageType"/> to spell it with, so the gate is blast alone.
/// </para>
/// </summary>
public static class TFPlayerGibRule
{
    /// <summary>
    /// The gib answer for a death by <paramref name="bits"/> of damage that
    /// left <paramref name="health"/>, under <c>tf_playergib</c>
    /// <paramref name="mode"/> — in stock's order, each arm falling through to
    /// the next; <paramref name="gibOnCrit"/> is the killing weapon's walked
    /// <c>crit_kill_will_gib</c> and <paramref name="critOnHardHit"/> its
    /// <c>crit_on_hard_hit</c>, whose set skips the blast-only gate (:10950-10956)
    /// and leaves the hard hit itself to decide.
    /// </summary>
    public static bool ShouldGib(int mode, DamageType bits, TFCritType crit, TFDamageCustom custom, int health,
                                 bool gibOnCrit = false, bool critOnHardHit = false)
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

        // The hard-hit gate: anything but a blast leaves a body (:10953) —
        // unless the killing weapon's crit_on_hard_hit asked the gate skipped,
        // hard hits of any damage then gibbing on :10963.
        if (!critOnHardHit && (bits & DamageType.Blast) == 0)
            return false;

        if (crit != TFCritType.None)                // :10959
            return true;

        if (health <= -10)                          // :10963, past the gib threshold
            return true;

        return false;                               // :10974
    }
}

/// <summary>
/// Writes the death's body flags onto the body the death left, the way
/// stock's <c>CreateTFPlayerRagdoll</c> does (<c>pRagdoll-&gt;m_bGib = bGib</c>,
/// <c>pRagdoll-&gt;m_bBecomeAsh = ...</c>, tf_player.cpp:15619, :15629): the
/// gib rule's answer, the killing weapon's <c>ragdolls_become_ash</c>, and the
/// fade the plasma stamp starts. It reads the killing blow from the same record
/// the death notice does, so it stands before <c>TFCombatDeaths</c> clears them.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerGib(TFPlayerGibConVar playerGib, TFDamageRecords records, IEngineEntities entities,
    IEntityDirectory directory, IGameClock clock, AttributeHooks? hooks = null, ITFWeaponOwners? owners = null)
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

        (bool gibOnCrit, bool critOnHardHit) = KillingWeaponWalks(death.EntIndex);

        body.State.CritOnHardHit = critOnHardHit;

        // The killing weapon's ragdolls_become_ash, stock's ON_OTHER hook over
        // the same weapon beside the gib answer's own (tf_player.cpp:12723-12727,
        // :15629): the death the Shooting Star's rifle makes. Independent of the
        // gib, as stock's two fields are — the client's ash arm refuses a gibed
        // body itself. Stamped first, as stock's order is (:15849), because the
        // plasma arm reads it.
        bool ash = hit is { } ashHit && hooks is { } ashWalk && owners is { } ashHolder
                   && ashWalk.Int(0, "ragdolls_become_ash",
                                  ashHolder.ItemOf(ashHit.Attacker.EntryIndex, ashHit.WeaponName)) > 0;
        body.State.Ash = ash;

        // ragdolls_plasma_effect, stock's second walk beside the ash's
        // (:15851-15859): the kill asks a plasma corpse, which the client fades
        // instead of gibbing it (c_tf_player.cpp:1202-1210). The MiniBoss arm
        // of that client gate has no case here (this mod has no minibosses), so
        // ash alone keeps the fade away; the gib is off either way (:1209).
        bool plasma = hit is { } plasmaHit && hooks is { } plasmaWalk && owners is { } plasmaHolder
                      && plasmaWalk.Int(0, "ragdolls_plasma_effect",
                                        plasmaHolder.ItemOf(plasmaHit.Attacker.EntryIndex, plasmaHit.WeaponName)) > 0;

        body.State.Gib = !plasma && TFPlayerGibRule.ShouldGib(playerGib.Value,
            hit?.Bits ?? DamageType.None, hit?.Crit ?? TFCritType.None,
            hit?.Custom ?? TFDamageCustom.None, entities.GetHealth(death.Player), gibOnCrit, critOnHardHit);

        // The networked half of m_bDissolving (c_tf_player.cpp:1206): the fade
        // starts at once, and the client's existing dissolve arm reads it at
        // creation (ClientSsRagdoll.OnDataChanged). Ash keeps the fade away
        // even under plasma (:1204), as stock's own ash check does.
        if (plasma && !ash)
            body.State.DissolveStart = clock.Now;

        // The Spy-cicle's freeze_backstab_victim, stock's other backstab arm in the death
        // walk (tf_player.cpp:12697-12704): only a hit whose custom kind is the backstab
        // asks the killing weapon, and a weapon holding the attribute turns the corpse to
        // ice — m_bIceRagdoll, networked beside the ash mark (:433, :462, :15628). The
        // client's ice arm (sound, frost, freeze timers) reads the flag at creation,
        // c_tf_player.cpp:736-742. Independent of the gib and the ash, as stock's fields are.
        body.State.Ice = hit is { Custom: TFDamageCustom.Backstab } iceHit && hooks is { } iceWalk && owners is { } iceHolder
                      && iceWalk.Int(0, "freeze_backstab_victim",
                                     iceHolder.ItemOf(iceHit.Attacker.EntryIndex, iceHit.WeaponName)) > 0;

        // The killing weapon's set_turn_to_gold, stock's ON_OTHER hook over the
        // killer's weapon (tf_player.cpp:12717-12721 — pKillerWeapon, where the
        // ash arm reads the swing's; this record serves both), stamped the same
        // way (:15627): the Golden Wrench's gilded corpse.
        body.State.Gold = hit is { } goldHit && hooks is { } goldWalk && owners is { } goldHolder
                       && goldWalk.Int(0, "set_turn_to_gold",
                                       goldHolder.ItemOf(goldHit.Attacker.EntryIndex, goldHit.WeaponName)) > 0;
    }

    /// <summary>
    /// Whether the last hit a player carries tears his body when it lands —
    /// stock's <c>bGib = ShouldGib(info)</c> over the recorded blow
    /// (tf_player.cpp:15729-15731), the killing weapon's walks and the health
    /// the victim had left. The feign's corpse asks it of the hit that
    /// triggered the feign, the blow its <c>m_bGib</c wears
    /// (<see cref="TFFeignRagdoll"/>, :15816).
    /// </summary>
    public bool LastHitGibs(EntityHandle victim)
    {
        (bool gibOnCrit, bool critOnHardHit) = KillingWeaponWalks(victim.EntryIndex);
        TFHitRecord? hit = records.LastHit(victim.EntryIndex);

        return TFPlayerGibRule.ShouldGib(playerGib.Value,
            hit?.Bits ?? DamageType.None, hit?.Crit ?? TFCritType.None,
            hit?.Custom ?? TFDamageCustom.None, entities.GetHealth(victim), gibOnCrit, critOnHardHit);
    }

    /// <summary>
    /// The killing weapon's crit_kill_will_gib and crit_on_hard_hit, stock's
    /// ON_OTHER hooks over the hit's weapon entity (tf_player.cpp:10939-10940,
    /// :10948-10949): the killing player's item under the hit's weapon name
    /// answers both walks; the second is also the ragdoll's m_bCritOnHardHit
    /// (:15630), the client's red mist.
    /// </summary>
    private (bool GibOnCrit, bool CritOnHardHit) KillingWeaponWalks(int victim)
    {
        TFHitRecord? hit = records.LastHit(victim);

        bool critOnHardHit = hit is { } hard && hooks is { } hardWalk && owners is { } hardHolder
                          && hardWalk.Int(0, "crit_on_hard_hit",
                                          hardHolder.ItemOf(hard.Attacker.EntryIndex, hard.WeaponName)) > 0;
        bool gibOnCrit = hit is { Crit: not TFCritType.None } w && hooks is { } walk && owners is { } holder
                         && walk.Int(0, "crit_kill_will_gib", holder.ItemOf(w.Attacker.EntryIndex, w.WeaponName)) > 0;

        return (gibOnCrit, critOnHardHit);
    }
}
