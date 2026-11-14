//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// The charge a carried item runs on — stock's per-slot item charge meters,
/// <c>CTFPlayerShared::UpdateItemChargeMeters</c>
/// (<c>tf_player_shared.cpp:7832-7904</c>). The item's
/// <c>item_meter_charge_rate</c> attribute is the seconds to a full charge,
/// <c>mult_item_meter_charge_rate</c> stretches them (:7881-7884), and stock
/// fills the bar <c>frametime * 100 / rate</c> each tick, held at full
/// (:7891-7900). Ours is the same bar scaled to 0..1 on
/// <see cref="WeaponNetState.ItemMeter"/>, which survives deploys as stock's
/// per-slot array does. A use spends the charge and leaves the bar at zero;
/// an empty bar refuses the use until it charges again. Stock's
/// <c>kill_refills_meter</c> (a kill refilling the Demoman's melee charge,
/// <c>tf_player.cpp:11258-11274</c>) is a second meter with its own rules
/// and is not this bar.
/// </summary>
public static class TFItemMeter
{
    /// <summary>Seconds to a full charge; absent or zero is no meter — stock's skip (<c>:7886</c>).</summary>
    public const string ChargeRateAttribute = "item_meter_charge_rate";

    /// <summary>Multiplies the charge seconds, default 1: the slow-charge half of the pair (<c>:7881</c>).</summary>
    public const string ChargeRateModAttribute = "mult_item_meter_charge_rate";

    /// <summary>
    /// Which of the meter kinds the item runs (<c>item_meter_charge_type</c>,
    /// stock's <c>attrib_meter_type_t</c> read at <c>:7881</c>): the timed
    /// refill walks only for TIME and COMBO (:7882), SIMPLE's refill is the
    /// item's own code and NONE's none. Stock's ordinals
    /// (<c>tf_shareddefs.h</c>): NONE 0, TIME 1, SIMPLE 2, COMBO 3. The two
    /// shipped pyro meters carry TIME (the Thermal Thruster's 30 s bar) and
    /// COMBO (the Gas Passer's 60 s one).
    /// </summary>
    public const string ChargeTypeAttribute = "item_meter_charge_type";

    /// <summary><c>ATTRIBUTE_METER_TYPE_TIME</c>: the bar fills on the clock.</summary>
    public const int ChargeTypeTime = 1;

    /// <summary><c>ATTRIBUTE_METER_TYPE_COMBO</c>: the timed bar with a count beside it.</summary>
    public const int ChargeTypeCombo = 3;

    /// <summary>The words drawn beside the bar (<c>meter_label</c>, stock's HUD string hook, <c>tf_hud_itemeffectmeter.cpp</c>).</summary>
    public const string LabelAttribute = "meter_label";

    /// <summary>
    /// The recharge the effect-bar weapons answer in stock's own override
    /// (<c>InternalGetEffectBarRechargeTime</c>): the jar's 20.1 s
    /// (<c>tf_weapon_jar.h:59</c>), the cleaver's 5.1 (<c>:120</c>) and the
    /// Wrap Assassin's ball bat's 10 (<c>tf_weapon_bat.h:91</c>) — the
    /// seconds a weapon charges by when its item names no
    /// <see cref="ChargeRateAttribute"/>, which the jars' never do.
    /// </summary>
    public static float EffectBarSeconds(TFWeaponId id) => id switch
    {
        TFWeaponId.Jar or TFWeaponId.JarMilk => 20.1f,
        TFWeaponId.Cleaver => 5.1f,
        TFWeaponId.BatGiftwrap => 10f,
        _ => 0f,
    };

    /// <summary>
    /// The effect bar's percentage stretch (<c>effectbar_recharge_rate</c>,
    /// <c>CTFWeaponBase::GetEffectBarRechargeTime</c>,
    /// tf_weaponbase.h:629): the attr rides the override's base, the gate
    /// <c>HasEffectBarRegeneration</c> reads the base alone (:628).
    /// </summary>
    public const string EffectBarRechargeAttribute = "effectbar_recharge_rate";

    /// <summary>The carried bar, 0..1.</summary>
    public static float MeterOf(WeaponNetState weapon) => weapon.ItemMeter;

    /// <summary>
    /// The effect-bar family's charge seconds: the override's base under the
    /// percentage attr, <c>GetEffectBarRechargeTime</c>'s whole body
    /// (tf_weaponbase.h:629). A weapon whose id answers no base has no bar,
    /// the gate's <c>HasEffectBarRegeneration</c> reading the base alone
    /// (:628).
    /// </summary>
    public static float EffectBarRecharge(AttributeHooks hooks, IAttributeProvider? item, TFWeaponId id)
        => EffectBarSeconds(id) * hooks.Float(1f, EffectBarRechargeAttribute, item);

    /// <summary>An empty bar refuses the use; anything charged on it is spendable.</summary>
    public static bool CanUse(WeaponNetState weapon) => weapon.ItemMeter > 0f;

    /// <summary>
    /// The regen's completion, the count's one: the effect-bar grant lands
    /// when the recharge time is past (<c>CheckEffectBarRegen</c>'s
    /// <c>m_flEffectBarRegenTime &lt; now</c>, <c>tf_weaponbase.cpp:6557-6561</c>)
    /// — not at the bar's first float, which the item meters' own
    /// <see cref="CanUse"/> spends on. The jars' gate.
    /// </summary>
    public static bool RegenFinished(WeaponNetState weapon) => weapon.ItemMeter >= 1f;

    /// <summary>The use spends the whole bar.</summary>
    public static void Use(WeaponNetState weapon) => weapon.ItemMeter = 0f;

    /// <summary>
    /// The charge's seconds for a player's named weapon: the item's rate
    /// under the mod, else the effect-bar override's answer for the weapon
    /// — the one question both the running tick and the held-aside restore
    /// ask (<see cref="IWeaponBehaviour.ItemMeterSeconds"/>'s TF answer), so
    /// a meter charges at one rate whichever hand holds it.
    /// </summary>
    public static float ChargeSeconds(AttributeHooks hooks, ITFWeaponOwners owners, int playerIndex,
                                      string weaponName, TFWeaponId weaponId)
    {
        IAttributeProvider? item = owners.ItemOf(playerIndex, weaponName);

        // Stock walks the timed refill only under a TIME or COMBO charge
        // type, and only then reads the rate and its mod
        // (tf_player_shared.cpp:7881-7892). The shipped meters carry one of
        // the two; a harness item naming just the rate keeps charging —
        // hooks.Int cannot tell an absent attribute from an explicit NONE,
        // and the rate-only harness is the lane's standing representation.
        // SIMPLE and a named NONE leave the timed answer, so the effect-bar
        // override answers instead — the jar's bar, no attribute.
        float seconds = hooks.Float(0f, ChargeRateAttribute, item)
            * hooks.Float(1f, ChargeRateModAttribute, item);
        if (seconds > 0f)
        {
            int chargeType = hooks.Int(0, ChargeTypeAttribute, item);
            if (chargeType == ChargeTypeTime || chargeType == ChargeTypeCombo || chargeType == 0)
                return seconds;
        }
        return EffectBarRecharge(hooks, item, weaponId);
    }

    /// <summary>
    /// The item's own multiplier when it has set none — stock's ctor answers
    /// 1 (<c>tf_weapon_dragons_fury.cpp:62</c>), and our zero field says the
    /// same thing the stock default does for every metered item.
    /// </summary>
    public const float RechargeNormal = 1f;

    /// <summary>
    /// The penalty a blast puts on the refill
    /// (<c>tf_fireball_airblast_recharge_penalty</c>'s default,
    /// <c>tf_weapon_dragons_fury.cpp:57, 228</c>).
    /// </summary>
    public const float RechargePenalty = 0.5f;

    /// <summary>
    /// The boost a hit on the launcher puts on the refill
    /// (<c>tf_fireball_hit_recharge_boost</c>'s default,
    /// <c>tf_weapon_dragons_fury.cpp:58, 302</c>).
    /// </summary>
    public const float HitRechargeBoost = 1.5f;

    /// <summary>
    /// Fill the bar over the item's charge seconds, the mod stretching them
    /// (stock's <c>flMeterChargeRate * flMeterChargeMult</c>, <c>:7881-7884</c>)
    /// and the weapon's multiplier under them (<c>:7901</c>'s
    /// <c>GetMeterMultiplier()</c>), held at full as stock holds 100
    /// (<c>:7891-7899</c>). The fill crossing the bar to full hands the
    /// multiplier back to normal — stock's interval-crossing ask
    /// (<c>:7854-7863</c>) lands on the Dragon's Fury as its meter's one
    /// interval, and <c>OnResourceMeterFilled</c> resets the scale to 1
    /// (<c>tf_weapon_dragons_fury.cpp:280-287</c>). No rate, no meter:
    /// the item does not recharge on the tick (stock's <c>continue</c>, <c>:7886-7887</c>).
    /// </summary>
    public static void Tick(WeaponNetState weapon, float dt, AttributeHooks hooks,
                            ITFWeaponOwners owners, int playerIndex, string weaponName,
                            TFWeaponId weaponId = TFWeaponId.None)
    {
        float seconds = ChargeSeconds(hooks, owners, playerIndex, weaponName, weaponId);

        if (seconds <= 0f)
            seconds = EffectBarRecharge(hooks, owners.ItemOf(playerIndex, weaponName), weaponId);
        if (seconds <= 0f)
            return;                                   // stock's skip (:7886-7887)

        float scale = weapon.FlameBallChargeScale > 0f ? weapon.FlameBallChargeScale : RechargeNormal;
        float before = weapon.ItemMeter;
        weapon.ItemMeter = Math.Min(1f, before + dt * scale / seconds);

        // The crossing of the interval — ours is the one to full, the meter's
        // whole bar being its charge interval for this family — answers
        // OnResourceMeterFilled (:7860-7862), which resets the scale (:282).
        if (before < 1f && weapon.ItemMeter >= 1f)
            weapon.FlameBallChargeScale = RechargeNormal;
    }
}
