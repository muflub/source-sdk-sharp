//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Combat;

/// <summary>
/// The resist half of <c>CTFGameRules::ApplyOnDamageAliveModifyRules</c>
/// (<c>shared/tf/tf_gamerules.cpp:7191-7336</c>), the pass the alive rules
/// run over the hit the modify rules made: the damage splits into its crit
/// bonus and the rest, the typed resists take theirs, and the two halves are
/// reassembled (:7284) before the all-sources multiplier.
///
/// <para>
/// <c>mod_pierce_resists_absorbs</c> on the hit's weapon (:7113, :7200) stands
/// every typed arm down; the final <c>mult_dmgtaken</c> (:7327) is outside
/// that gate and takes anyway.
/// </para>
///
/// <para>
/// Not here: the medic resist and absorb (<c>CheckMedicResist</c>), the
/// MvM, rune and feign-death arms, <c>set_dmgtype_ignite</c>,
/// <c>uber_on_damage_taken</c>, <c>disguise_no_burn</c>, rage, hype and
/// snare arms, and the reduction sounds —
/// <c>docs/plans/tf2/deferred.tsv</c> names them.
/// </para>
/// </summary>
public static class TFDamageAliveRules
{
    /// <summary>The spun-up gate's fraction (<c>&lt;= 0.5f</c>, :7266).</summary>
    public const float HalfHealth = 0.5f;

    /// <summary><c>tf_feign_death_damage_scale</c>'s stock default
    /// (<c>tf_gamerules.cpp:689</c>): the scale a half-full cloak meter
    /// answers on the feign arm.</summary>
    public const float FeignDeathDamageScale = 0.35f;

    /// <summary><c>tf_feign_death_activate_damage_scale</c>'s stock default
    /// (<c>tf_gamerules.cpp:688</c>): the scale while the watch's ready bit
    /// still answers.</summary>
    public const float FeignDeathActivateDamageScale = 0.25f;

    /// <summary>
    /// The hit after the resists. <paramref name="hit"/> is the modify rules'
    /// answer, its <see cref="TFHitResult.Bonus"/> the crit portion stock
    /// keeps in <c>info.GetDamageBonus()</c>; <paramref name="input"/> is the
    /// hit as dealt, whose weapon the pierce arm reads, whose victim the
    /// <paramref name="victimItem"/> attributes belong to and whose
    /// <see cref="TFHit.VictimItem"/> is the weapon the victim holds.
    /// <paramref name="victimClass"/> is the victim's class, the cloaked-spy
    /// gate's; <paramref name="stealthReduction"/> is
    /// <c>tf_stealth_damage_reduction</c>. <paramref name="spunUp"/> is the
    /// heavy-and-aiming gate's answer over the hit as it entered (:7266);
    /// <paramref name="fromSentry"/> whether the hit's inflictor is the
    /// sentry itself (:7330-7337).
    /// </summary>
    public static float Resist(TFHitResult hit, in TFHit input, AttributeHooks? hooks, IAttributeProvider? victimItem,
                               TFClass victimClass, float stealthReduction,
                               bool spunUp = false, bool fromSentry = false)
        => Resist(hit, in input, hooks, victimItem, victimClass, stealthReduction, spunUp, fromSentry,
                  cloakMeter: 100f, feignReady: false, health: 0f, out _);

    /// <summary>
    /// The same pass with the Dead Ringer's half of the spy's arm (:7389-7429)
    /// in reach: <paramref name="cloakMeter"/> is the victim's cloak meter and
    /// <paramref name="feignReady"/> the watch's ready bit, both read from the
    /// predicted carrier before the call (the bit is still armed at the
    /// scale's time — stock clears it later, in the hit's own tail,
    /// tf_player.cpp:9678); <paramref name="health"/> is the victim's health,
    /// the save check's floor (:7413). <paramref name="savedFromDeath"/>
    /// answers whether the scale saved the spy from the hit (:7412-7422), the
    /// fact that makes the watch's save event.
    /// </summary>
    public static float Resist(TFHitResult hit, in TFHit input, AttributeHooks? hooks, IAttributeProvider? victimItem,
                               TFClass victimClass, float stealthReduction, bool spunUp, bool fromSentry,
                               float cloakMeter, bool feignReady, float health, out bool savedFromDeath)
    {
        float damage = hit.Damage;
        savedFromDeath = false;
        if (hooks is null)
            return damage;

        float bonus = hit.Bonus;
        float baseDamage = damage - bonus;

        // Crit-A-Cola and Steak Sandwich — only increase normal damage
        // (:6792-6797): the buffed victim's own multiplier rides the base
        // half ahead of every typed resist, and a crit or mini-crit hit
        // skips it whole. CALL_ATTRIB_HOOK_FLOAT_ON_OTHER on the victim is
        // this repo's victimItem provider.
        if (hit.Crit == TFCritType.None && input.Victim.Has(TFCond.EnergyBuff))
            baseDamage = hooks.Float(baseDamage, "energy_buff_dmg_taken_multiplier", victimItem);

        // The pierce, asked once (:7112-7113): the typed resists (:7209) and
        // the held weapon's final ones (:7375) stand down behind the same
        // gate.
        int pierce = hooks.Int(0, "mod_pierce_resists_absorbs", input.WeaponItem);

        if (pierce == 0)
        {
            // The crit resist takes its share of the bonus only (:7211-7217):
            // "Reduce only the crit portion of the damage with crit resist".
            if ((hit.Bits & TFDamageBits.Critical) != 0)
                bonus = hooks.Float(bonus, "mult_dmgtaken_from_crit", victimItem);

            // Flaregun hits carry both bits, as stock's own comment asks
            // (:7219: "Should we only ever apply one of these?").
            if ((hit.Bits & (DamageType.Burn | TFDamageBits.Ignite)) != 0)
            {
                baseDamage = hooks.Float(baseDamage, "mult_dmgtaken_from_fire", victimItem);

                // The weapon the victim holds (:7223), beside the victim's own.
                baseDamage = hooks.Float(baseDamage, "mult_dmgtaken_from_fire_active", input.VictimItem);
            }

            // The attacker's held weapon's damage vs burning (:7229-7232): any
            // hit on a burning victim, the attacker a player — the hit's own
            // type none of it.
            if (input.AttackerIsPlayer && input.Victim.Has(TFCond.Burning))
                baseDamage = hooks.Float(baseDamage, "mult_dmg_vs_burning", input.WeaponItem);

            // A blast the victim dealt itself takes no resist; only another's
            // does (:7238-7242). MvM's always-reduce arm rides its mode.
            if ((hit.Bits & DamageType.Blast) != 0 && !input.SelfDamage)
                baseDamage = hooks.Float(baseDamage, "mult_dmgtaken_from_explosions", victimItem);

            if ((hit.Bits & (DamageType.Bullet | DamageType.Buckshot)) != 0)
                baseDamage = hooks.Float(baseDamage, "mult_dmgtaken_from_bullets", victimItem);

            if ((hit.Bits & TFDamageBits.Melee) != 0)
                baseDamage = hooks.Float(baseDamage, "mult_dmgtaken_from_melee", victimItem);

            // The spun-up heavy's resist (:7266-7274), the gate over the hit
            // as it entered; the sound stock plays on the change is the
            // deferred half.
            if (spunUp)
                baseDamage = hooks.Float(baseDamage, "spunup_damage_resistance", victimItem);
        }

        // Stomp flRealDamage with resist adjusted values (:7283-7284), then the
        // all-sources multiplier on the whole (:7326-7327) and the held
        // weapon's (:7328).
        damage = baseDamage + bonus;
        damage = hooks.Float(damage, "mult_dmgtaken", victimItem);
        damage = hooks.Float(damage, "mult_dmgtaken_active", input.VictimItem);

        // The sentry's arm (:7330-7337): the inflictor the gun itself, after
        // the multiplier as stock runs it.
        if (fromSentry)
            damage = hooks.Float(damage, "dmg_from_sentry_reduced", victimItem);

        // The held weapon's final resists (:7375-7387): a club is the melee
        // arm's alone, everything ranged the ranged arm's (stock's else-if).
        if (pierce == 0 && input.VictimItem is not null)
        {
            if ((hit.Bits & DamageType.Club) != 0)
                damage = hooks.Float(damage, "dmg_from_melee", input.VictimItem);
            else if ((hit.Bits & (DamageType.Blast | DamageType.Bullet | DamageType.Buckshot
                                  | TFDamageBits.Ignite | DamageType.Sonic)) != 0)
                damage = hooks.Float(damage, "dmg_from_ranged", input.VictimItem);
        }

        // The cloaked spy (:7390-7428): a spy's hit that is no telefrag, not
        // while taunting and no pierce. The feign arm of the pair (:7393-7423)
        // answers while the feign holds or the watch's ready bit still stands:
        // the scale rides the cloak meter's place on the 50-to-0 range, from
        // the feign scale to the stealth reduction (:7396), and while the bit
        // is armed the activate scale stands in its place (:7399-7401). Only
        // behind the feign does the plain stealth arm answer (:7425-7428).
        if (victimClass == TFClass.Spy
            && hit.Custom != TFDamageCustom.Telefrag
            && !input.Victim.Has(TFCond.Taunting)
            && pierce == 0)
        {
            if (input.Victim.Has(TFCond.FeignDeath) || feignReady)
            {
                float scale = TFDamageModifyRules.RemapValClamped(cloakMeter, 50f, 0f,
                                                                  FeignDeathDamageScale, stealthReduction);
                if (feignReady)
                    scale = FeignDeathActivateDamageScale;

                float before = damage;
                damage *= scale;

                // The save the scale won (:7413): the hit would have killed
                // as it stood and does not as scaled.
                savedFromDeath = before >= health && damage < health;
            }
            else if (input.Victim.Has(TFCond.Stealthed))
            {
                damage *= stealthReduction;
            }
        }

        return damage;
    }
}
