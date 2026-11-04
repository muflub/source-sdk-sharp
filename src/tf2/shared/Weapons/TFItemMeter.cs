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

    /// <summary>The carried bar, 0..1.</summary>
    public static float MeterOf(WeaponNetState weapon) => weapon.ItemMeter;

    /// <summary>An empty bar refuses the use; anything charged on it is spendable.</summary>
    public static bool CanUse(WeaponNetState weapon) => weapon.ItemMeter > 0f;

    /// <summary>The use spends the whole bar.</summary>
    public static void Use(WeaponNetState weapon) => weapon.ItemMeter = 0f;

    /// <summary>
    /// Fill the bar over the item's charge seconds, the mod stretching them
    /// (stock's <c>flMeterChargeRate * flMeterChargeMult</c>, <c>:7881-7884</c>),
    /// held at full as stock holds 100 (<c>:7891-7899</c>). No rate, no meter:
    /// the item does not recharge on the tick (stock's <c>continue</c>, <c>:7886-7887</c>).
    /// </summary>
    public static void Tick(WeaponNetState weapon, float dt, AttributeHooks hooks,
                            ITFWeaponOwners owners, int playerIndex, string weaponName)
    {
        IAttributeProvider? item = owners.ItemOf(playerIndex, weaponName);

        // Stock reads the rate off the item with two hooks: the charge
        // seconds off a base 0, the mod as a percentage on a base 1
        // (tf_player_shared.cpp:7881-7884).
        float seconds = hooks.Float(0f, ChargeRateAttribute, item)
            * hooks.Float(1f, ChargeRateModAttribute, item);
        if (seconds <= 0f)
            return;                                   // stock's skip (:7886-7887)

        weapon.ItemMeter = Math.Min(1f, weapon.ItemMeter + dt / seconds);
    }
}
