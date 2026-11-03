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
/// One hit as the TF damage rules read it: the damage and its bits as dealt,
/// what the world says about the two players, and the attacker's weapon.
/// </summary>
/// <param name="Damage">The damage as dealt (a crit is flagged, not multiplied: <c>TFWeaponBehaviour.Critical</c>).</param>
/// <param name="Bits">The damage type, with <see cref="TFDamageBits.Critical"/> when the weapon rolled a crit.</param>
/// <param name="Custom">The kill's custom kind (<c>m_iDamageCustom</c>).</param>
/// <param name="SelfDamage">The attacker is the victim (<c>pAttacker == pVictimBaseEntity</c>).</param>
/// <param name="AttackerIsPlayer">The attacker is a player (the weapon arms and the variance's weapon switch need one).</param>
/// <param name="Weapon">The attacker's weapon id, <see cref="TFWeaponId.None"/> for none.</param>
/// <param name="Distance">The two players' <c>WorldSpaceCenter</c> distance; null when there is no attacker to measure from.</param>
/// <param name="Victim">The victim's conditions.</param>
/// <param name="Attacker">The attacker's conditions (empty for a non-player).</param>
/// <param name="WeaponItem">The attacker's weapon as an attribute provider, the econ arms' walk root; null when there is no item to ask.</param>
/// <param name="SameClass">Attacker and victim wear the same class (<c>GetPlayerClass()-&gt;GetClassIndex()</c> equal).</param>
/// <param name="VictimWaterLevel">The victim's water level, 0 for out (<c>GetWaterLevel</c>); WL_NOTINWATER is 0.</param>
/// <param name="DeflectedProjectile">The inflictor is a rocket or regular pipe a deflection sent back (<c>pBaseRocket/pBaseGrenade-&gt;GetDeflected()</c> with the grenade's <c>ShouldMiniCritOnReflect()</c>, tf_gamerules.cpp:6159).</param>
/// <param name="VictimEyeAngles">The victim's eye angles, pitch yaw roll (<c>EyeAngles</c>); null when unreadable.</param>
/// <param name="ToVictim">The victim's origin minus the attacker's (<c>GetAbsOrigin</c> difference); null without an attacker position.</param>
/// <param name="AttackerAirborne">The attacker is in air due to an explosion (<c>CTFPlayer::InAirDueToExplosion</c>, tf_player.h:599).</param>
/// <param name="VictimAirborne">The victim is in air due to an explosion, the same read.</param>
/// <param name="DirectRocketHit">The inflictor is a rocket whose enemy is this victim, its owner carrying
/// rocket_specialist (<c>tf_gamerules.cpp:6299-6306</c>): the long-range falloff is skipped.</param>
/// <param name="VictimItem">The victim's ACTIVE weapon as an attribute provider, stock's <c>pVictim-&gt;GetActiveTFWeapon()</c> as the resists' ON_OTHER root; null when they hold none.</param>
public readonly record struct TFHit(float Damage, DamageType Bits, TFDamageCustom Custom, bool SelfDamage,
                                    bool AttackerIsPlayer, TFWeaponId Weapon, float? Distance,
                                    TFCondMask Victim, TFCondMask Attacker, IAttributeProvider? WeaponItem = null,
                                    bool SameClass = false, int VictimWaterLevel = 0, bool DeflectedProjectile = false,
                                    Vector? VictimEyeAngles = null, Vector? ToVictim = null,
                                    bool AttackerAirborne = false, bool VictimAirborne = false,
                                    bool DirectRocketHit = false,
                                    IAttributeProvider? VictimItem = null);

/// <summary>
/// What the rules made of a hit: the damage to take, the bits and kind it now
/// carries, and the crit portion of the damage (<c>info.SetDamageBonus</c>,
/// :6786-6789) — the split the alive rules' resists reassemble.
/// </summary>
public readonly record struct TFHitResult(float Damage, DamageType Bits, TFCritType Crit, TFDamageCustom Custom,
                                          float Bonus = 0f);

/// <summary>The convars the modify rules read, as values (the live ones are the damage layer's, <c>TFDamageLayer</c>).</summary>
/// <param name="DamageRange"><c>tf_damage_range</c>: the variance's half-width as a fraction of the damage.</param>
/// <param name="DisableSpread"><c>tf_damage_disablespread</c>: no random spread (the band's floor plus the spread).</param>
/// <param name="CritFalloff"><c>tf_weapon_criticals_distance_falloff</c>.</param>
/// <param name="MiniCritFalloff"><c>tf_weapon_minicrits_distance_falloff</c>.</param>
public readonly record struct TFDamageSettings(float DamageRange, bool DisableSpread,
                                               bool CritFalloff = false, bool MiniCritFalloff = false)
{
    /// <summary>Every convar at its stock default (<c>tf_damage_range</c> 0.5, the spread off).</summary>
    public static TFDamageSettings Defaults => new(0.5f, DisableSpread: true);
}

/// <summary>
/// <c>CTFGameRules::ApplyOnDamageModifyRules</c>
/// (<c>shared/tf/tf_gamerules.cpp:5914-7103</c>) for the arms that read only
/// the hit, the two players' conditions and the attacker's weapon id, in
/// stock's order:
///
/// <para>
/// — THE CRIT TYPE. A hit flagged <c>DMG_CRITICAL</c> is a full crit
///   (:5949-5952). Not self-damage (:6096), and not a crit already, the
///   victim's jarate, marked-for-death and PASS Time penalty mini-crit it
///   (:6126-6139), else the attacker's buff banner (:6140-6151), else a
///   deflected rocket's blast (:6159-6164), else the attacker's crit-a-cola
///   (:6177-6182) (<see cref="TFConditionEffects"/>).
/// </para>
/// <para>
/// — THE DEFENSE BUFFS (:6470-6512): not a backstab nor crush damage, the
///   battalion's backup takes every crit away; then its high variant takes
///   75% of the damage, the others 35%.
/// </para>
/// <para>
/// — THE RANGE (:6523-6800), for anything but self-damage, drowning and
///   falls: with <c>DMG_USEDISTANCEMOD</c> the band's centre is
///   <c>RemapValClamped(distance / 512, 0, 2, 1, 0)</c> (the sniper rifles'
///   optimum is 2.5 times further), damped past the middle to 0.5-0.65 under
///   <c>DMG_NOCLOSEDISTANCEMOD</c>, and the band is the centre ± 0.1. A full
///   crit never takes the near bonus and a mini-crit never the far penalty
///   (:6531-6536). The value is a random pick in the band, or its floor plus
///   0.1 with <c>tf_damage_disablespread</c> (default 1); the variance is
///   <c>SimpleSplineRemapValClamped(value, 0, 1, -R, R)</c> for <c>R</c> the
///   damage times <c>tf_damage_range</c> (0.5), with the rocket launchers'
///   near bonus halved, the grenade launchers' range a fifth, and the
///   scattergun family's near bonus half again (:6610-6648).
/// </para>
/// <para>
/// — THE CRIT MULTIPLIER (:6682-6799): a mini-crit adds
///   <c>(TF_DAMAGE_MINICRIT_MULTIPLIER - 1)</c> of the ranged damage and
///   flags <c>DMG_CRITICAL</c>; a full crit adds
///   <c>(TF_DAMAGE_CRIT_MULTIPLIER - 1)</c> of it. Self-damage, drowning and
///   falls take neither: the whole block is skipped for them (:6523).
/// </para>
///
/// <para>
/// Not here: the arms keyed on item attributes (the econ hooks: crit vs
/// conditions, crits/minicrits promoted or demoted, damage vs players and
/// same class, non-burning crits, the axtinguisher, ...), on the rest of the
/// projectile identity (reflected grenades, flares, the cleaver, sentries),
/// on powerups, and the stats and effects (see-crit, bonus effects, speech,
/// assists); <c>docs/plans/tf2/deferred.tsv</c> names them. The alive rules'
/// resists are <see cref="TFDamageAliveRules"/>; invulnerability is
/// the damage rules' refusal (<c>TFDamageRules</c>).
/// </para>
/// </summary>
public static class TFDamageModifyRules
{
    /// <summary>The variance band's half-width (<c>flRandomDamageSpread</c>, :6541).</summary>
    public const float RandomDamageSpread = 0.10f;

    /// <summary>The optimal distance of a ranged hit (<c>flOptimalDistance</c>, :6548).</summary>
    public const float OptimalDistance = 512f;

    /// <summary>The sniper rifles' optimal distance factor (:6561).</summary>
    public const float SniperOptimalScale = 2.5f;

    /// <summary>The battalion's backup high variant's factor (:6496).</summary>
    public const float DefenseBuffHighScale = 0.25f;

    /// <summary>The battalion's backup factor on anything but a sentry (:6509).</summary>
    public const float DefenseBuffScale = 0.65f;

    /// <summary>The behind-tests' dot: the way to the victim along its facing, right behind (:6057).</summary>
    public const float BehindDot = 0.0f;

    /// <summary>The backattack's dot, 75° from centre (:6327).</summary>
    public const float BackattackDot = 0.259f;

    /// <summary>The backattack's reach, squared (<c>Square( 512.0f )</c>, :6320).</summary>
    public const float BackattackRangeSquared = 512f * 512f;

    /// <summary>
    /// The hit after the modify rules. <paramref name="random"/> is
    /// <c>RandomFloat(min, max)</c>, asked only when the spread is enabled.
    /// <paramref name="hooks"/> is the econ walk, asked only when the hit
    /// carries a weapon item.
    /// </summary>
    public static TFHitResult Apply(in TFHit hit, in TFDamageSettings settings, Func<float, float, float> random,
                                    AttributeHooks? hooks = null)
    {
        float damage = hit.Damage;
        DamageType bits = hit.Bits;
        var crit = TFCritType.None;
        var effects = new TFConditionEffects();

        // Damage type was already crit (flares, headshots, the weapon's roll).
        if ((bits & TFDamageBits.Critical) != 0)
            crit = TFCritType.Full;

        // The weapon's econ crit arms (:5958-6042): or_crit_vs_playercond and
        // or_crit_vs_not_playercond, an or'd mask whose bits are conditions of
        // stock's condition_to_attribute_translation table
        // (tf_shareddefs.cpp:249-274) tested against the victim. Either hit is
        // a full crit; the disguised-crit client flag rides the crit itself.
        if (hooks is not null && hit.WeaponItem is not null
            && (CritVsCondition(hooks.Int(0, "or_crit_vs_playercond", hit.WeaponItem), hit.Victim, want: true)
                || CritVsCondition(hooks.Int(0, "or_crit_vs_not_playercond", hit.WeaponItem), hit.Victim, want: false)))
        {
            bits |= TFDamageBits.Critical;
            crit = TFCritType.Full;
        }

        // crit_vs_wet_players (:5991-6012): jarate, milk or gas on the victim,
        // or the victim in the water. The exited-recently prong has no clock
        // here and the breakable sign's SetBroken is the weapon's, not the
        // hit's (docs/plans/tf2/deferred.tsv).
        if (hooks is not null && hit.WeaponItem is not null
            && hooks.Int(0, "crit_vs_wet_players", hit.WeaponItem) != 0
            && ((hit.Victim.Has(TFCond.Urine) || hit.Victim.Has(TFCond.MadMilk) || hit.Victim.Has(TFCond.Gas))
                || hit.VictimWaterLevel > 0))
        {
            bits |= TFDamageBits.Critical;
            crit = TFCritType.Full;
        }

        // axtinguisher_properties (:6044-6070): on a burning victim the hit
        // from behind — the way to the victim along the victim's facing, within
        // 90° of right behind — is a full crit; any other the mini-crit.
        if (hooks is not null && hit.WeaponItem is not null
            && hooks.Int(0, "axtinguisher_properties", hit.WeaponItem) != 0
            && hit.Victim.Has(TFCond.Burning)
            && hit.VictimEyeAngles is { } eyeAngles && hit.ToVictim is { } toVictim)
        {
            if (new Vector(toVictim.X, toVictim.Y, 0f).Normalized.Dot(Angles.Forward(eyeAngles)) > BehindDot)
            {
                bits |= TFDamageBits.Critical;
                crit = TFCritType.Full;
            }
            else
            {
                crit = TFCritType.Mini;
            }
        }

        // crit_while_airborne (:6073-6086): an attacker knocked into the air by
        // an explosion crits while up there — full, over the axtinguisher's
        // pick and under the mini-crit ladder. No powerup mode is ported, so
        // the arm's !IsPowerupMode gate is vacuous (docs/plans/tf2/deferred.tsv
        // row 63 keeps the mode's own arms).
        if (hooks is not null && hit.WeaponItem is not null
            && hooks.Int(0, "crit_while_airborne", hit.WeaponItem) != 0
            && hit.AttackerAirborne)
        {
            bits |= TFDamageBits.Critical;
            crit = TFCritType.Full;
        }

        // Mini-crits, never on oneself.
        if (!hit.SelfDamage && crit == TFCritType.None)
        {
            TFCondMask victim = hit.Victim, attacker = hit.Attacker;
            if (effects.TakesMiniCrits(in victim)
                || (hit.AttackerIsPlayer && effects.DealsMiniCrits(in attacker)))
                crit = TFCritType.Mini;
            // Reflected rockets and grenades (:6159-6164): the blast of a
            // projectile a deflection sent back mini-crits, between the buffed
            // attacker and the crit drink as stock orders the ladder. The
            // grenade arm gates on ShouldMiniCritOnReflect
            // (tf_weapon_grenade_pipebomb.cpp:167-170), the non-remote
            // detonate modes — the pipe, never the sticky.
            else if (hit.DeflectedProjectile)
                crit = TFCritType.Mini;
            else if (hit.AttackerIsPlayer && attacker.Has(TFCond.EnergyBuff))
                crit = TFCritType.Mini;
            // The chain's (:6247-6262): a weapon forcing mini-crits on burning
            // victims, the hit no burn tick itself.
            else if (hooks is not null && hit.WeaponItem is not null
                && hooks.Int(0, "or_minicrit_vs_playercond_burning", hit.WeaponItem) == 1
                && victim.Has(TFCond.Burning) && (bits & DamageType.Burn) == 0)
                crit = TFCritType.Mini;
            // mini_crit_airborne (:6264-6278): a victim knocked into the air by
            // an enemy blast mini-crits — the chain's next arm, before the
            // backattack's own if.
            else if (hooks is not null && hit.WeaponItem is not null
                && hooks.Int(0, "mini_crit_airborne", hit.WeaponItem) == 1
                && hit.VictimAirborne)
                crit = TFCritType.Mini;

            // closerange_backattack_minicrits (:6314-6333): still inside the
            // no-crit gate, a player's hit inside 512 units whose way to the
            // victim runs along the victim's facing, within 75° of right
            // behind, mini-crits.
            if (hit.AttackerIsPlayer && hooks is not null && hit.WeaponItem is not null
                && hooks.Int(0, "closerange_backattack_minicrits", hit.WeaponItem) == 1
                && hit.ToVictim is { } backToVictim && hit.VictimEyeAngles is { } backEyeAngles
                && backToVictim.LengthSquared < BackattackRangeSquared
                && new Vector(backToVictim.X, backToVictim.Y, 0f).Normalized.Dot(Angles.Forward(backEyeAngles)) > BackattackDot)
                crit = TFCritType.Mini;
        }

        // The weapon's promotion and multipliers (:6338-6353, :6394-6432),
        // between the crit decision and the defense buffs as stock orders
        // them: a mini-crit the weapon promotes is a full one; the damage
        // scales by vs-players, again vs the same class, and — for a victim
        // not burning, unless the attacker is crit-boosted — a refused crit
        // and the non-burning multiplier.
        if (hooks is not null && hit.WeaponItem is not null)
        {
            if (crit == TFCritType.Mini && hooks.Int(0, "minicrits_become_crits", hit.WeaponItem) == 1)
            {
                crit = TFCritType.Full;
                bits |= TFDamageBits.Critical;
            }

            damage = hooks.Float(damage, "mult_dmg_vs_players", hit.WeaponItem);

            if (hit.SameClass)
                damage = hooks.Float(damage, "mult_dmg_vs_same_class", hit.WeaponItem);

            if (!hit.Victim.Has(TFCond.Burning))
            {
                if ((bits & TFDamageBits.Critical) != 0 && hit.AttackerIsPlayer
                    && !effects.IsCritBoosted(hit.Attacker, clientSide: false)
                    && hooks.Int(0, "set_nocrit_vs_nonburning", hit.WeaponItem) != 0)
                {
                    bits &= ~TFDamageBits.Critical;
                    crit = TFCritType.None;
                }

                damage = hooks.Float(damage, "mult_dmg_vs_nonburning", hit.WeaponItem);
            }
        }

        // The defense buffs, unless a backstab or crush damage.
        if (hit.Custom != TFDamageCustom.Backstab && (bits & DamageType.Crush) == 0)
        {
            if (hit.Victim.Has(TFCond.Defensebuff))
            {
                // No crits of any kind.
                crit = TFCritType.None;
                bits &= ~TFDamageBits.Critical;
            }

            if (hit.Victim.Has(TFCond.DefensebuffHigh))
                damage *= DefenseBuffHighScale;
            else if (hit.Victim.Has(TFCond.Defensebuff) || hit.Victim.Has(TFCond.DefensebuffNoCritBlock))
                damage *= DefenseBuffScale;
        }

        bool fullCrit = (bits & TFDamageBits.Critical) != 0;
        float bonus = 0f;

        if (!hit.SelfDamage && (bits & (DamageType.Drown | DamageType.Fall)) == 0)
        {
            bool mini = crit == TFCritType.Mini;
            bool distanceMod = (bits & TFDamageBits.UseDistanceMod) != 0;
            bool forceCritFalloff = distanceMod && ((fullCrit && settings.CritFalloff) || (mini && settings.MiniCritFalloff));
            bool shortRangeIncrease = !fullCrit || mini;

            // The rocket specialist's direct hit ignores the long-range arm
            // whole (bIgnoreLongRangeDmgEffects,
            // tf_gamerules.cpp:6299-6306, gated at :6536).
            bool longRangeDecrease = !hit.DirectRocketHit && (forceCritFalloff || (!fullCrit && !mini));

            // The variance's width is the hit as dealt (info.GetDamage()), not the buffed local.
            float variance = Variance(hit.Damage, bits, hit.Weapon, hit.AttackerIsPlayer,
                                      distanceMod ? hit.Distance : null, shortRangeIncrease, settings, random);
            if ((shortRangeIncrease && variance > 0f) || longRangeDecrease)
                damage += variance;

            if (mini)
            {
                bonus = (TFDamage.MinicritMultiplier - 1f) * damage;
                bits |= TFDamageBits.Critical;
            }
            else if (fullCrit)
            {
                bonus = (TFDamage.CritMultiplier - 1f) * damage;
            }

            damage += bonus;
        }

        return new TFHitResult(damage, bits, crit, hit.Custom, bonus);
    }

    /// <summary><c>condition_to_attribute_translation</c>'s length (<c>tf_shareddefs.cpp:249-274</c>).</summary>
    private const int ConditionCount = 21;

    /// <summary>
    /// <c>condition_to_attribute_translation</c> (<c>tf_shareddefs.cpp:249-274</c>):
    /// bit <c>i</c> of a crit-vs-condition mask is this condition — a switch,
    /// not a table (managed keeps no state in statics).
    /// </summary>
    private static TFCond ConditionOf(int bit) => bit switch
    {
        0 => TFCond.Burning,
        1 => TFCond.Aiming,
        2 => TFCond.Zoomed,
        3 => TFCond.Disguising,
        4 => TFCond.Disguised,
        5 => TFCond.Stealthed,
        6 => TFCond.Invulnerable,
        7 => TFCond.Teleported,
        8 => TFCond.Taunting,
        9 => TFCond.InvulnerableWearingoff,
        10 => TFCond.StealthedBlink,
        11 => TFCond.SelectedToTeleport,
        12 => TFCond.Critboosted,
        13 => TFCond.Tmpdamagebonus,
        14 => TFCond.FeignDeath,
        15 => TFCond.Phase,
        16 => TFCond.Stunned,
        17 => TFCond.HealthBuff,
        18 => TFCond.HealthOverhealed,
        19 => TFCond.Urine,
        20 => TFCond.EnergyBuff,
        _ => TFCond.Invalid,
    };

    /// <summary>The arm's mask against the victim: any listed condition present (<paramref name="want"/>) or absent.</summary>
    private static bool CritVsCondition(int mask, TFCondMask victim, bool want)
    {
        for (int i = 0; mask != 0 && i < ConditionCount; i++)
        {
            if ((mask & (1 << i)) != 0 && victim.Has(ConditionOf(i)) == want)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The range variance (:6537-6648): the amount added to (near) or taken
    /// from (far) the damage before any crit, for a hit whose distance mod
    /// applies when <paramref name="distance"/> is given.
    /// </summary>
    public static float Variance(float damage, DamageType bits, TFWeaponId weapon, bool attackerIsPlayer,
                                 float? distance, bool shortRangeIncrease, in TFDamageSettings settings,
                                 Func<float, float, float> random)
    {
        float randomDamage = damage * settings.DamageRange;
        float min = 0.5f - RandomDamageSpread;
        float max = 0.5f + RandomDamageSpread;

        if (distance is { } d)
        {
            float optimal = IsSniperRifle(weapon) ? OptimalDistance * SniperOptimalScale : OptimalDistance;
            float center = Center(MathF.Max(1f, d), optimal, (bits & TFDamageBits.NoCloseDistanceMod) != 0,
                                  shortRangeIncrease, out bool moved);
            if (moved)
            {
                min = MathF.Max(0f, center - RandomDamageSpread);
                max = MathF.Min(1f, center + RandomDamageSpread);
            }
        }

        float value = settings.DisableSpread ? min + RandomDamageSpread : random(min, max);

        if (attackerIsPlayer)
        {
            switch (weapon)
            {
                // Rocket launchers only have half the bonus at short range.
                case TFWeaponId.Rocketlauncher:
                case TFWeaponId.RocketlauncherDirecthit:
                case TFWeaponId.ParticleCannon:
                    if (value > 0.5f)
                        randomDamage *= 0.5f;
                    break;
                case TFWeaponId.Pipebomblauncher:
                case TFWeaponId.Grenadelauncher:
                case TFWeaponId.Cannon:
                case TFWeaponId.Stickbomb:
                    if ((bits & TFDamageBits.NoCloseDistanceMod) == 0)
                        randomDamage *= 0.2f;
                    break;
                // The scattergun gets a 50% bonus at short range.
                case TFWeaponId.Scattergun:
                case TFWeaponId.SodaPopper:
                case TFWeaponId.PepBrawlerBlaster:
                    if (value > 0.5f)
                        randomDamage *= 1.5f;
                    break;
            }
        }

        return SimpleSplineRemapValClamped(value, 0f, 1f, -randomDamage, randomDamage);
    }

    /// <summary>
    /// The band's centre at <paramref name="distance"/> (:6566-6580):
    /// <c>RemapValClamped(distance / optimal, 0, 2, 1, 0)</c>, damped to
    /// <c>RemapVal(c, 0.5, 1, 0.5, 0.65)</c> past the middle with
    /// <paramref name="noClose"/>. <paramref name="moved"/> is false where
    /// stock leaves the band at the middle: a near hit that may not ramp up.
    /// </summary>
    public static float Center(float distance, float optimal, bool noClose, bool shortRangeIncrease, out bool moved)
    {
        float center = RemapValClamped(distance / optimal, 0f, 2f, 1f, 0f);
        moved = (center > 0.5f && shortRangeIncrease) || center <= 0.5f;
        if (moved && noClose && center > 0.5f)
            center = RemapVal(center, 0.5f, 1.0f, 0.5f, 0.65f);
        return center;
    }

    /// <summary><c>WeaponID_IsSniperRifle</c> (<c>tf_weaponbase.cpp:6328-6336</c>).</summary>
    public static bool IsSniperRifle(TFWeaponId weapon) =>
        weapon is TFWeaponId.Sniperrifle or TFWeaponId.SniperrifleDecap or TFWeaponId.SniperrifleClassic;

    /// <summary><c>RemapVal</c> (<c>mathlib.h</c>).</summary>
    public static float RemapVal(float val, float a, float b, float c, float d) =>
        a == b ? (val >= b ? d : c) : c + (d - c) * (val - a) / (b - a);

    /// <summary><c>RemapValClamped</c> (<c>mathlib.h</c>).</summary>
    public static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float t = Math.Clamp((val - a) / (b - a), 0f, 1f);
        return c + (d - c) * t;
    }

    /// <summary><c>SimpleSplineRemapValClamped</c> (<c>mathlib.h</c>).</summary>
    public static float SimpleSplineRemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float t = Math.Clamp((val - a) / (b - a), 0f, 1f);
        float sq = t * t;
        return c + (d - c) * (3 * sq - 2 * sq * t);
    }
}
