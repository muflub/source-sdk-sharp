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

/// <summary>What one afterburn think asked for: a tick of damage, the fire put out, or nothing.</summary>
/// <param name="TickDamage">The tick's damage (0 for no tick).</param>
/// <param name="Custom">The tick's custom kind: burning, or the flare's or arrow's burning.</param>
/// <param name="Extinguish">The fire went out (spent, or the victim is in water to the waist).</param>
public readonly record struct TFBurnThink(float TickDamage, TFDamageCustom Custom, bool Extinguish);

/// <summary>
/// One player's afterburn: <c>CTFPlayerShared::Burn</c>
/// (<c>shared/tf/tf_player_shared.cpp:6622-6771</c>) and the burning arm of
/// <c>ConditionGameRulesThink</c> (:2676-2681, :2768-2958), the server's.
///
/// <para>
/// IGNITING (<see cref="Ignite"/>): nothing for the dead, nor the phased
/// (<c>TF_COND_PHASE</c>, the PASS Time interception). A first ignition adds
/// <c>TF_COND_BURNING</c> and <c>TF_COND_HEALING_DEBUFF</c>, schedules the first
/// tick <c>TF_BURNING_FREQUENCY</c> (0.5 s) away and starts the duration at
/// the weapon's initial afterburn (<see cref="InitialAfterburnDuration"/>:
/// the flamethrower's 3 s). Every ignition then adds the weapon's rate on hit
/// (<see cref="AfterburnRateOnHit"/>: the flamethrower's 0.4 s, the flare
/// gun's 7.5 s), or the caller's burning time; an immune victim (a pyro not
/// <c>TF_COND_BURNING_PYRO</c>, <c>TF_COND_AFTERBURN_IMMUNE</c>) burns a fixed
/// <c>TF_BURNING_FLAME_LIFE_PYRO</c> instead. The duration is clamped to
/// <c>tf_afterburn_max_duration</c> (10 s, :188).
/// </para>
/// <para>
/// BURNING (<see cref="Think"/>): healed (<c>TF_COND_HEALTH_BUFF</c>), the
/// duration runs down three times as fast (:2676-2681). Spent, or in water to
/// the waist, the fire goes out (:2774-2789). Otherwise, once a tick is due,
/// a player that is not a pyro (or a pyro burning as one) takes
/// <c>TF_BURNING_DMG</c> (4) as <c>DMG_BURN | DMG_PREVENT_PHYSICS_FORCE</c> from
/// the burn's attacker, the next tick is 0.5 s on and the duration 0.5 s less
/// (:2895-2959). The degree multipliers are stock's (both 1, :191-192).
/// </para>
///
/// <para>
/// The item-attribute arms are read as stock reads them
/// (<c>tf_player_shared.cpp:6669-6733, 2898</c>): the immune arm's sources
/// (<see cref="ImmunityAttrib"/>) walked and ORed per source, the burn
/// weapon's <c>mult_wpn_burntime</c> on the flame life and
/// <c>mult_wpn_burndmg</c> on each tick's damage. The truce, the demo
/// shield's arm, the powerups, the achievements, sounds and the extinguish
/// events wait for what they read (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
public sealed class TFBurn
{
    /// <summary><c>tf_afterburn_max_duration</c> (<c>tf_player_shared.cpp:188</c>).</summary>
    public const float MaxDuration = 10f;

    /// <summary><c>tf_afterburn_duration_ratio_second_degree</c> (:189).</summary>
    public const float SecondDegreeRatio = 0.4f;

    /// <summary><c>tf_afterburn_duration_ratio_third_degree</c> (:190).</summary>
    public const float ThirdDegreeRatio = 0.8f;

    /// <summary><c>tf_afterburn_mult_second_degree</c> (:191).</summary>
    public const float SecondDegreeMult = 1f;

    /// <summary><c>tf_afterburn_mult_third_degree</c> (:192).</summary>
    public const float ThirdDegreeMult = 1f;

    /// <summary>The immune arm's attribute (<c>tf_player_shared.cpp:6674-6718</c>): walked once per source — the active weapon, the demo shield, the snipers' wearables — and ORed as stock ORs the three.</summary>
    public const string ImmunityAttrib = "afterburn_immunity";

    /// <summary>The burn weapon's time arm (<c>tf_player_shared.cpp:6733</c>).</summary>
    public const string BurnTimeAttrib = "mult_wpn_burntime";

    /// <summary>The burn weapon's tick arm, before the degree multipliers (<c>tf_player_shared.cpp:2898</c>).</summary>
    public const string BurnDamageAttrib = "mult_wpn_burndmg";

    /// <summary><c>WL_Waist</c> (<c>shared/shareddefs.h</c>): water this deep puts the fire out.</summary>
    public const int WaterLevelWaist = 2;

    /// <summary>The damage type of an afterburn tick (:2939).</summary>
    public const DamageType TickDamageType = DamageType.Burn | DamageType.PreventPhysicsForce;

    /// <summary><c>m_flFlameBurnTime</c>: when the next tick is due.</summary>
    public float FlameBurnTime { get; private set; }

    /// <summary><c>m_flAfterburnDuration</c>: the seconds of fire left.</summary>
    public float AfterburnDuration { get; private set; }

    /// <summary><c>m_hBurnAttacker</c>.</summary>
    public EntityHandle Attacker { get; private set; }

    /// <summary><c>m_hBurnWeapon</c>'s id.</summary>
    public TFWeaponId Weapon { get; private set; }

    /// <summary>The burn weapon's name, for the kill feed.</summary>
    public string WeaponName { get; private set; } = "";

    /// <summary>
    /// <c>GetInitialAfterburnDuration</c>: the flamethrower's 3 s
    /// (<c>tf_weapon_flamethrower.cpp:62</c>), the bow's 7.5 s
    /// (<c>tf_weapon_compound_bow.cpp:610-615</c>); 0 for the rest
    /// (<c>tf_weaponbase.h:557</c>).
    /// </summary>
    public static float InitialAfterburnDuration(TFWeaponId weapon) => weapon switch
    {
        TFWeaponId.Flamethrower => 3f,
        TFWeaponId.CompoundBow => 7.5f,
        _ => 0f,
    };

    /// <summary>
    /// <c>GetAfterburnRateOnHit</c>: the flamethrower's 0.4 s
    /// (<c>tf_weapon_flamethrower.cpp:54</c>), the flare gun's 7.5 s
    /// (<c>tf_weapon_flaregun.cpp:65</c>); 0 for the rest (<c>tf_weaponbase.h:558</c>).
    /// </summary>
    public static float AfterburnRateOnHit(TFWeaponId weapon) => weapon switch
    {
        TFWeaponId.Flamethrower => 0.4f,
        TFWeaponId.Flaregun => 7.5f,
        _ => 0f,
    };

    /// <summary>
    /// <c>Burn</c>: sets the player alight (or feeds the fire). Answers the
    /// conditions as they are afterwards; <paramref name="conditions"/> as
    /// they were. <paramref name="burningTime"/> is the caller's flame life
    /// (stock's <c>flBurningTime</c>), 0 or less for the weapon's.
    /// <paramref name="immunitySources"/> are the victim's immune arm's
    /// providers, stock's three walks in its order, and
    /// <paramref name="burnWeapon"/> the burn weapon's item — null for none,
    /// the arms then unset.
    /// </summary>
    public TFCondMask Ignite(float now, bool alive, TFClass playerClass, TFCondMask conditions,
                             EntityHandle attacker, TFWeaponId weapon, string weaponName, float burningTime = -1f,
                             AttributeHooks? hooks = null, IReadOnlyList<IAttributeProvider>? immunitySources = null,
                             IAttributeProvider? burnWeapon = null)
    {
        if (!alive)
            return conditions;

        if (conditions.Has(TFCond.Phase) || conditions.Has(TFCond.PasstimeInterception))
            return conditions;

        bool immunePyro = playerClass == TFClass.Pyro && !conditions.Has(TFCond.BurningPyro);

        if (!conditions.Has(TFCond.Burning))
        {
            conditions.Set(TFCond.Burning);
            conditions.Set(TFCond.HealingDebuff);
            FlameBurnTime = now + TFCombatDefs.BurningFrequency;
            AfterburnDuration = InitialAfterburnDuration(weapon);
        }

        bool immunity = immunePyro || conditions.Has(TFCond.AfterburnImmune);

        // The active weapon's, the shield's and the wearables' immunities, the
        // three walks of stock's arm (:6672-6720), ORed as it ORs them.
        if (!immunity && hooks is not null && immunitySources is not null)
            foreach (IAttributeProvider source in immunitySources)
                immunity |= hooks.Int(0, ImmunityAttrib, source) != 0;

        float flameLife = AfterburnRateOnHit(weapon);
        if (immunity)
            flameLife = TFCombatDefs.BurningFlameLifePyro;
        else if (burningTime > 0f)
            flameLife = burningTime;

        if (hooks is not null)
            flameLife = hooks.Float(flameLife, BurnTimeAttrib, burnWeapon);

        AfterburnDuration = immunity ? flameLife : AfterburnDuration + flameLife;
        AfterburnDuration = Math.Clamp(AfterburnDuration, 0f, MaxDuration);

        Attacker = attacker;
        Weapon = weapon;
        WeaponName = weaponName;
        return conditions;
    }

    /// <summary>
    /// The burning arm of <c>ConditionGameRulesThink</c> for one frame of
    /// <paramref name="frametime"/>: what to deal, or whether the fire is out.
    /// Nothing when the player is not burning. <paramref name="hooks"/> with
    /// <paramref name="burnWeapon"/>, the burn weapon's item, carry the tick's
    /// <see cref="BurnDamageAttrib"/>; null for none, the arm then unset.
    /// </summary>
    public TFBurnThink Think(float now, float frametime, TFClass playerClass, TFCondMask conditions, int waterLevel,
                             AttributeHooks? hooks = null, IAttributeProvider? burnWeapon = null)
    {
        if (!conditions.Has(TFCond.Burning))
            return default;

        // Healers wear the burn off faster: ( 2 + 1 ) x the reduction.
        if (conditions.Has(TFCond.HealthBuff))
            AfterburnDuration -= 2f * frametime;

        if (AfterburnDuration <= 0f || waterLevel >= WaterLevelWaist)
            return new TFBurnThink(0f, TFDamageCustom.None, Extinguish: true);

        if (now < FlameBurnTime)
            return default;

        float damage = 0f;
        var custom = TFDamageCustom.Burning;
        if (playerClass != TFClass.Pyro || conditions.Has(TFCond.BurningPyro))
        {
            damage = TFCombatDefs.BurningDamage;
            custom = Weapon switch
            {
                TFWeaponId.Flaregun => TFDamageCustom.BurningFlare,
                TFWeaponId.CompoundBow => TFDamageCustom.BurningArrow,
                _ => TFDamageCustom.Burning,
            };

            if (hooks is not null)
                damage = hooks.Float(damage, BurnDamageAttrib, burnWeapon);

            if (AfterburnDuration >= ThirdDegreeRatio * MaxDuration)
                damage *= ThirdDegreeMult;
            else if (AfterburnDuration >= SecondDegreeRatio * MaxDuration)
                damage *= SecondDegreeMult;
        }

        FlameBurnTime = now + TFCombatDefs.BurningFrequency;
        AfterburnDuration -= TFCombatDefs.BurningFrequency;
        return new TFBurnThink(damage, custom, Extinguish: false);
    }

    /// <summary><c>OnRemoveBurning</c>'s server half (:6885-6902): the burn's attacker and weapon forgotten.</summary>
    public void Extinguished()
    {
        Attacker = EntityHandle.None;
        Weapon = TFWeaponId.None;
        WeaponName = "";
        AfterburnDuration = 0f;
        FlameBurnTime = 0f;
    }
}
