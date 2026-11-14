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

    /// <summary>The words drawn beside the bar (<c>meter_label</c>, stock's HUD string hook, <c>tf_hud_itemeffectmeter.cpp</c>).</summary>
    public const string LabelAttribute = "meter_label";

    /// <summary>
    /// The recharge the effect-bar weapons answer in stock's own override
    /// (<c>InternalGetEffectBarRechargeTime</c>): the jar's 20.1 s
    /// (<c>tf_weapon_jar.h:59</c>) and the cleaver's 5.1 (<c>:120</c>) — the
    /// seconds a weapon charges by when its item names no
    /// <see cref="ChargeRateAttribute"/>, which the jars' never do.
    /// </summary>
    public static float EffectBarSeconds(TFWeaponId id) => id switch
    {
        TFWeaponId.Jar or TFWeaponId.JarMilk => 20.1f,
        TFWeaponId.Cleaver => 5.1f,
        _ => 0f,
    };

    /// <summary>The carried bar, 0..1.</summary>
    public static float MeterOf(WeaponNetState weapon) => weapon.ItemMeter;

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

        // Stock reads the rate off the item with two hooks: the charge
        // seconds off a base 0, the mod as a percentage on a base 1
        // (tf_player_shared.cpp:7881-7884). An effect-bar weapon whose item
        // names none charges at its own override's answer (see
        // <see cref="EffectBarSeconds"/>) — the jar's bar, no attribute.
        float seconds = hooks.Float(0f, ChargeRateAttribute, item)
            * hooks.Float(1f, ChargeRateModAttribute, item);
        return seconds > 0f ? seconds : EffectBarSeconds(weaponId);
    }

    /// <summary>
    /// Fill the bar over the item's charge seconds, the mod stretching them
    /// (stock's <c>flMeterChargeRate * flMeterChargeMult</c>, <c>:7881-7884</c>),
    /// held at full as stock holds 100 (<c>:7891-7899</c>). No rate, no meter:
    /// the item does not recharge on the tick (stock's <c>continue</c>, <c>:7886-7887</c>).
    /// </summary>
    public static void Tick(WeaponNetState weapon, float dt, AttributeHooks hooks,
                            ITFWeaponOwners owners, int playerIndex, string weaponName,
                            TFWeaponId weaponId = TFWeaponId.None)
    {
        float seconds = ChargeSeconds(hooks, owners, playerIndex, weaponName, weaponId);

        if (seconds <= 0f)
            seconds = EffectBarSeconds(weaponId);
        if (seconds <= 0f)
            return;                                   // stock's skip (:7886-7887)

        weapon.ItemMeter = Math.Min(1f, weapon.ItemMeter + dt / seconds);
    }
}
