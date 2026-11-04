//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// The item attributes on a TF weapon, the chain's first link: the
/// <c>CALL_ATTRIB_HOOK_*</c> calls of <c>CTFWeaponBase::GetMaxClip1</c>
/// (<c>mult_clipsize</c>, <c>tf_weaponbase.cpp:571-575</c>),
/// <c>ApplyFireDelay</c> (<c>mult_postfiredelay</c>, :1897-1898),
/// <c>SetReloadTimer</c> (<c>mult_reload_time</c>, <c>mult_reload_time_hidden</c>,
/// <c>fast_reload</c> and the manager's <c>hwn_mult_reload_time</c>, :2274-2277),
/// <c>CTFWeaponBaseGun::GetProjectileDamage</c> (<c>mult_dmg</c>), the crit
/// helpers (<c>mult_crit_chance</c>, :1673, :1699), the sniper charge's
/// rate (<c>mult_sniper_charge_per_sec</c>,
/// <c>tf_weapon_sniperrifle.cpp:645</c>), the fully charged shot's bonus
/// (<c>sniper_full_charge_damage_bonus</c>, <c>tf_weapon_sniperrifle.cpp:896-899</c>)
/// and the minigun's wind-up
/// (<c>mult_minigun_spinup_time</c>, <c>tf_weapon_minigun.cpp:259</c>) and the
/// spun drain (<c>uses_ammo_while_aiming</c>, <c>tf_weapon_minigun.cpp:452</c>), through
/// <see cref="AttributeHooks"/> over the item the weapon is and the player's
/// own manager. With no item (no owner named, or a weapon given without one)
/// every value is unchanged.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAttributeWeaponHook(AttributeHooks attributes) : ITFWeaponHook
{
    /// <summary>TF2's weapon band, +200..+299 (the rules own +100..+199: TeamRoundOrders).</summary>
    public const int Order = 1_000_000 - 1_000 + 200;

    public int HookOrder => Order;

    public int ModifyClip(in TFWeaponHookContext weapon, int clip) =>
        clip >= 0 ? attributes.Int(clip, "mult_clipsize", weapon.Item) : clip;

    public float ModifyInterval(in TFWeaponHookContext weapon, float interval) =>
        interval * attributes.Float(1f, "mult_postfiredelay", weapon.Item);

    /// <summary>
    /// The fired shot's pellets (<c>tf_fx_shared.cpp:305</c>'s
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> over <c>nBulletsPerShot</c>, the
    /// value the loop at :307 runs on): the walk over the count as a float,
    /// rounded back as the int member it lands in truncates. With no attribute
    /// the count stands.
    /// </summary>
    public int ModifyPellets(in TFWeaponHookContext weapon, int pellets) =>
        AttributeHooks.RoundFloatToInt(attributes.Float(pellets, "mult_bullets_per_shot", weapon.Item));

    /// <summary>
    /// <c>CTFWeaponBase::SetReloadTimer</c>'s four multiplies
    /// (<c>tf_weaponbase.cpp:2274-2277</c>): the item's
    /// <c>mult_reload_time</c>, <c>mult_reload_time_hidden</c> and
    /// <c>fast_reload</c>, then the player manager's
    /// <c>hwn_mult_reload_time</c> as the <c>ON_OTHER</c> hook takes it. The
    /// floor that follows is the behaviours' (stock's <c>:2319</c>).
    /// </summary>
    public float ModifyReloadTime(in TFWeaponHookContext weapon, float duration) =>
        duration * attributes.Float(1f, "mult_reload_time", weapon.Item)
                 * attributes.Float(1f, "mult_reload_time_hidden", weapon.Item)
                 * attributes.Float(1f, "fast_reload", weapon.Item)
                 * attributes.Float(1f, "hwn_mult_reload_time", weapon.Manager);

    public float ModifyDamage(in TFWeaponHookContext weapon, float damage) =>
        attributes.Float(damage, "mult_dmg", weapon.Item);

    /// <summary><c>AutoFiresFullClip</c>'s attribute (tf_weaponbase.cpp:1871-1876).</summary>
    public bool FiresFullClip(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "auto_fires_full_clip", item) != 0;

    /// <summary>
    /// <c>CTFScatterGun::Reload</c>'s attribute (<c>tf_weapon_shotgun.cpp:299-309</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> over zero, and the member is only
    /// turned off on the set's exactly <c>== 1</c>.
    /// </summary>
    public bool ReloadsWithWholeClip(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "set_scattergun_no_reload_single", item) == 1;

    /// <summary><c>ApplyChargeSpeedModifications</c>' attribute (tf_weapon_sniperrifle.cpp:645).</summary>
    public float ModifyChargeRate(in TFWeaponHookContext weapon, float rate) =>
        attributes.Float(rate, "mult_sniper_charge_per_sec", weapon.Item);

    /// <summary>The fully charged shot's bonus (tf_weapon_sniperrifle.cpp:896-899).</summary>
    public float ModifyFullChargeDamage(in TFWeaponHookContext weapon, float damage) =>
        attributes.Float(damage, "sniper_full_charge_damage_bonus", weapon.Item);

    /// <summary><c>SharedAttack</c>'s wind-up (tf_weapon_minigun.cpp:259-260).</summary>
    public float ModifySpinUpTime(in TFWeaponHookContext weapon, float seconds) =>
        attributes.Float(seconds, "mult_minigun_spinup_time", weapon.Item);

    /// <summary>The spun gun's drain (tf_weapon_minigun.cpp:452-453), the item's whole rounds a second.</summary>
    public float AmmoDrainPerSecond(in TFWeaponHookContext weapon, float perSecond) =>
        perSecond + attributes.Int(0, "uses_ammo_while_aiming", weapon.Item);

    /// <summary>The fired shot's projectile sweep (tf_weapon_minigun.cpp:376-377).</summary>
    public int AttackEnemyProjectiles(in TFWeaponHookContext weapon, int count) =>
        count + attributes.Int(0, "attack_projectiles", weapon.Item);

    /// <summary>The spun command's ring of fire (tf_weapon_minigun.cpp:442-443).</summary>
    public int RingOfFireDamage(in TFWeaponHookContext weapon, int damage) =>
        damage + attributes.Int(0, "ring_of_fire_while_aiming", weapon.Item);

    /// <summary><c>DoSwingTraceInternal</c>'s reach (tf_weaponbase_melee.cpp:420).</summary>
    public float ModifyMeleeRange(in TFWeaponHookContext weapon, float range) =>
        attributes.Float(range, "melee_range_multiplier", weapon.Item);

    /// <summary><c>DoSwingTraceInternal</c>'s swing box (tf_weaponbase_melee.cpp:399).</summary>
    public float ModifyMeleeBounds(in TFWeaponHookContext weapon, float bounds) =>
        attributes.Float(bounds, "melee_bounds_multiplier", weapon.Item);

    /// <summary>The smack's behind-crit ask, the item's <c>crit_from_behind</c> over zero (tf_weaponbase_melee.cpp:819-820).</summary>
    public bool CritFromBehind(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "crit_from_behind", weapon.Item) > 0;

    public TFCritOwner CritPolicy(in TFWeaponHookContext weapon, TFCritOwner policy) =>
        policy with { ChanceScale = policy.ChanceScale * attributes.Float(1f, "mult_crit_chance", weapon.Item) };

    /// <summary>
    /// <c>CTFWeaponBase::Deploy</c>'s multiplier (<c>tf_weaponbase.cpp:1258-</c>):
    /// the incoming item's <c>mult_single_wep_deploy_time</c> scales the draw
    /// always (:1260), and the outgoing item's
    /// <c>mult_switch_from_wep_deploy_time</c> (:1270) only when the outgoing
    /// weapon has been held longer than the switch time — stock compares the
    /// last-deploy stamp against <c>flWeaponSwitchTime</c> (:1262-1266), here
    /// the asked seconds. The stock's grappling-hook latch (:1266) and
    /// powerup-mode knife (:1265) arms wait on their carriers.
    /// </summary>
    public float ModifyDeployTime(in TFWeaponHookContext weapon, float seconds) =>
        seconds * attributes.Float(1f, "mult_single_wep_deploy_time", weapon.Item)
                * (weapon.SecondsSincePreviousDeploy > seconds
                       ? attributes.Float(1f, "mult_switch_from_wep_deploy_time", weapon.PreviousItem)
                       : 1f);
}

/// <summary>
/// The crit boost, the chain's second link: <c>CTFPlayerShared::IsCritBoosted</c>'s
/// condition test (<see cref="TFConditionEffects.IsCritBoosted"/>), which the
/// crit helpers ask first (<c>tf_weaponbase.cpp:1591, 1620</c>). The demo
/// charge's boost counts only on a client's copy (not authoritative).
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFCritBoostHook : ITFWeaponHook
{
    /// <summary>After <see cref="TFAttributeWeaponHook"/>, in the weapon band.</summary>
    public const int Order = 1_000_000 - 1_000 + 210;

    private readonly TFConditionEffects _effects = new();

    public int HookOrder => Order;

    public TFCritOwner CritPolicy(in TFWeaponHookContext weapon, TFCritOwner policy) =>
        weapon.Conditions is { } conditions && _effects.IsCritBoosted(conditions.Bits, clientSide: !conditions.Authoritative)
            ? policy with { CritBoosted = true }
            : policy;
}

/// <summary>
/// The king buff's reload arm, the chain's third link:
/// <c>CTFWeaponBase::SetReloadTimer</c>'s condition test takes three quarters
/// of the reload time (<c>tf_weaponbase.cpp:2305-2308</c>). The branch has one
/// other entry — carrying the king rune — which has no state to read yet
/// (deferred.tsv's rune row); the condition is a predicted bit, so a client's
/// replay scales the clock exactly as the server did.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFKingReloadHook : ITFWeaponHook
{
    /// <summary>After the boost's crit arm: stock runs the item arms first, then the condition.</summary>
    public const int Order = 1_000_000 - 1_000 + 215;

    public int HookOrder => Order;

    public float ModifyReloadTime(in TFWeaponHookContext weapon, float duration) =>
        weapon.Conditions is { } conditions && conditions.Has(TFCond.KingBuffed) ? duration * 0.75f : duration;
}
