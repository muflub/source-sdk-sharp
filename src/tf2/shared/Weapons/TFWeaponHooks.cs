//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

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
public sealed class TFAttributeWeaponHook(AttributeHooks attributes,
                                          IEngineEntities? entities = null, TFClassTable? classes = null,
                                          ITFPlayerData? players = null, IEnginePlayers? enginePlayers = null) : ITFWeaponHook
{
    /// <summary>TF2's weapon band, +200..+299 (the rules own +100..+199: TeamRoundOrders).</summary>
    public const int Order = 1_000_000 - 1_000 + 200;

    public int HookOrder => Order;

    public int ModifyClip(in TFWeaponHookContext weapon, int clip)
    {
        // The base class's own arm first (GetMaxClip1,
        // basecombatweapon_shared.cpp:367-370): the override IS the answer,
        // the Widowmaker's minus one among them, and only a clip it leaves
        // non-negative meets the multiplier (tf_weaponbase.cpp:571-575).
        int over = attributes.Int(0, "mod_max_primary_clip_override", weapon.Item);
        int resolved = over != 0 ? over : clip;
        return resolved >= 0 ? attributes.Int(resolved, "mult_clipsize", weapon.Item) : resolved;
    }

    /// <summary>
    /// <c>CTFWeaponBaseGun::GetAmmoPerShot</c>'s attribute
    /// (<c>tf_weaponbase_gun.cpp:464-467</c>): a positive count IS the price.
    /// </summary>
    public int PrimaryAmmoPerShot(in TFWeaponHookContext weapon, int perShot)
    {
        int priced = attributes.Int(0, "mod_ammo_per_shot", weapon.Item);
        return priced > 0 ? priced : perShot;
    }

    /// <summary>
    /// <c>m_iPrimaryAmmoType</c>'s spawn arm
    /// (<c>basecombatweapon_shared.cpp:269-275</c>): the item's attribute
    /// points the primary at the metal pool, any non-zero value.
    /// </summary>
    public string? PrimaryAmmoPool(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "mod_use_metal_ammo_type", weapon.Item) != 0 ? TFAmmoNames.Name(TFAmmo.Metal) : null;

    /// <summary>
    /// <c>CTFWeaponBaseGun::PrimaryAttack</c>' clock: the script delay through
    /// <c>ApplyFireDelay</c>' <c>mult_postfiredelay</c>
    /// (<c>tf_weaponbase.cpp:1897-1898</c>), then the blast-jump pair
    /// (<c>tf_weaponbase_gun.cpp:114-121</c>): the owner blasting off takes
    /// <c>rocketjump_attackrate_bonus</c>, from the ground
    /// <c>mul_nonrocketjump_attackrate</c>. Same gated style as
    /// <see cref="ModifyDamage"/>' disguise arm.
    /// </summary>
    public float ModifyInterval(in TFWeaponHookContext weapon, float interval)
    {
        float delay = interval * attributes.Float(1f, "mult_postfiredelay", weapon.Item);   // :1897
        return weapon.Conditions?.Has(TFCond.Blastjumping) == true                            // :114
            ? attributes.Float(delay, "rocketjump_attackrate_bonus", weapon.Item)             // :116
            : attributes.Float(delay, "mul_nonrocketjump_attackrate", weapon.Item);           // :120
    }

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
    /// The airblast's rounds (<c>tf_weapon_flamethrower.cpp:1192-1194</c>): the
    /// burst ammo times the item's <c>mult_airblast_cost</c>, truncated into
    /// stock's <c>int iAmmoPerShot</c>. With no attribute the convar's rounds
    /// stand.
    /// </summary>
    public int ModifyAirBlastCost(in TFWeaponHookContext weapon, int ammo) =>
        (int)(ammo * attributes.Float(1f, "mult_airblast_cost", weapon.Item));

    /// <summary>
    /// <c>CTFWeaponBaseGun::GetWeaponSpread</c>'s multiplier
    /// (<c>tf_weaponbase_gun.cpp:844-847</c>): the held item's
    /// <c>mult_spread_scale</c> over the shot cone's sine, stock's
    /// <c>CALL_ATTRIB_HOOK_FLOAT</c> of the loaded spread — Tomislav and the
    /// Back Scatter wear it as a percentage, so the cone scales whole.
    /// </summary>
    public float ModifySpread(in TFWeaponHookContext weapon, float spread) =>
        attributes.Float(spread, "mult_spread_scale", weapon.Item);

    /// <summary>
    /// <c>CTFWeaponBaseGun::GetWeaponSpread</c>' second arm
    /// (<c>tf_weaponbase_gun.cpp:866-872</c>): the item's
    /// <c>panic_attack_negative</c> walks over one; when it differs, the cone's
    /// sine scales by the owner's health fraction through
    /// <c>RemapValClamped(fraction, 0.2, 0.9, value, 1)</c> — hurt at the
    /// bottom, near-full at the top, the band's ends clamped. The fraction is
    /// the carrier's own health over its class's maximum, stock's
    /// <c>HealthFraction</c> read as the movement rules read it.
    /// </summary>
    public float ModifyPanicAttack(in TFWeaponHookContext weapon, float spread)
    {
        float bonus = attributes.Float(1f, "panic_attack_negative", weapon.Item);   // :867
        if (bonus == 1f)                                                             // :868
            return spread;
        bonus = RemapHealth(bonus, HealthFraction(weapon.OwnerIndex));               // :870
        return spread * bonus;                                                       // :871
    }

    /// <summary>Stock's <c>RemapValClamped(fraction, 0.2f, 0.9f, bonus, 1.0f)</c> (<c>:870</c>).</summary>
    private static float RemapHealth(float bonus, float fraction)
    {
        float t = (fraction - 0.2f) / 0.7f;
        if (t < 0f)
            t = 0f;
        else if (t > 1f)
            t = 1f;
        return bonus + t * (1f - bonus);
    }

    /// <summary>The carrier's own health ratio (:396-401's shape): predicted
    /// state's class opens the table's maximum, a missing layer leaves the
    /// fraction full so the remap lands on the top end — no scale.</summary>
    private float HealthFraction(int ownerIndex)
    {
        if (classes is null || entities is null || enginePlayers is null
            || players?.Predicted(ownerIndex) is not { } state)
            return 1f;
        int maxHealth = classes[state.Class()].MaxHealth;
        // The real handle, serial and all: after a changelevel the slot's
        // handle carries one and a bare index reads another slot through the
        // engine's handle table, so the panic cone would take the wrong band.
        float health = entities.GetHealth(enginePlayers.GetHandle(ownerIndex));
        return maxHealth > 0 ? health / maxHealth : 1f;
    }

    /// <summary>
    /// <c>CTFWeaponBaseGun::GetWeaponSpread</c>' third arm
    /// (<c>tf_weaponbase_gun.cpp:874-882</c>): stock walks
    /// <c>mult_spread_scales_consecutive</c> over a zeroed <c>flScaler</c>; the
    /// walk's non-zero is the arm's gate, the scaler itself the streak's remap
    /// (<see cref="TFWeaponBehaviour.BulletSpread"/>). The Panic Attack wears it.
    /// </summary>
    public float ModifyConsecutiveSpread(in TFWeaponHookContext weapon, float scaler) =>
        attributes.Float(scaler, "mult_spread_scales_consecutive", weapon.Item);

    /// <summary>
    /// <c>IsFixedWeaponSpreadEnabled</c>' weapon arm
    /// (<c>tf_fx_shared.cpp:425-431</c>): the item's <c>fixed_shot_pattern</c>
    /// through stock's non-zero test — the weapon forces the fixed pattern on
    /// even when the convar and the match group ask for none. The Panic Attack
    /// wears it beside its streak.
    /// </summary>
    public bool FixedShotPattern(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "fixed_shot_pattern", item) != 0;

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

    /// <summary>
    /// <c>CTFWeaponBaseGun::GetProjectileDamage</c>'s first two arms
    /// (<c>tf_weaponbase_gun.cpp:909-917</c>): the item's <c>mult_dmg</c> (:910),
    /// then the disguised arm (<c>InCond(TF_COND_DISGUISED)</c> at :913-914, the
    /// Enforcer's multiplier at :916) only while the owner wears the disguise.
    /// </summary>
    public float ModifyDamage(in TFWeaponHookContext weapon, float damage)
    {
        float scaled = attributes.Float(damage, "mult_dmg", weapon.Item);   // :910
        if (weapon.Conditions?.Has(TFCond.Disguised) == true)               // :913-914
            scaled = attributes.Float(scaled, "mult_dmg_disguised", weapon.Item);   // :916
        return scaled;
    }

    /// <summary><c>AutoFiresFullClip</c>'s attribute (tf_weaponbase.cpp:1871-1876).</summary>
    public bool FiresFullClip(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "auto_fires_full_clip", item) != 0;

    /// <summary><c>CanOverload</c>'s attribute (tf_weaponbase.cpp:1887-1893), the same non-zero test.</summary>
    public bool CanOverload(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "can_overload", item) != 0;

    /// <summary>
    /// <c>CTFScatterGun::Reload</c>'s attribute (<c>tf_weapon_shotgun.cpp:299-309</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> over zero, and the member is only
    /// turned off on the set's exactly <c>== 1</c>.
    /// </summary>
    public bool ReloadsWithWholeClip(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "set_scattergun_no_reload_single", item) == 1;

    /// <summary><c>MustBeZoomedToFire</c>' attribute (tf_weapon_sniperrifle.cpp:708-713).</summary>
    public bool OnlyFireZoomed(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "sniper_only_fire_zoomed", item) != 0;

    /// <summary>
    /// <c>MaybeDrawRailgunBeam</c>'s arms (tf_player_shared.cpp:10103-10115):
    /// the hook, its hidden variant asked only when the first is unset, and
    /// last the charge rage buff on the shooter — the rifle this behaviour
    /// answers for is a sniper rifle, so the buff arm holds without the
    /// <c>WeaponID_IsSniperRifle</c> test.
    /// </summary>
    public bool FiresTracer(in TFWeaponHookContext weapon)
    {
        if (weapon.Item is { } item
            && (attributes.Int(0, "sniper_fires_tracer", item) != 0
                || attributes.Int(0, "sniper_fires_tracer_HIDDEN", item) != 0))
            return true;

        return weapon.Conditions?.Has(TFCond.SniperchargeRageBuff) == true;
    }

    /// <summary><c>ApplyChargeSpeedModifications</c>' attribute (tf_weapon_sniperrifle.cpp:645).</summary>
    public float ModifyChargeRate(in TFWeaponHookContext weapon, float rate) =>
        attributes.Float(rate, "mult_sniper_charge_per_sec", weapon.Item);

    /// <summary>The boost buff's seconds (tf_weapon_smg.cpp:164-170, stock from a plain zero).</summary>
    public float MinicritBoostSeconds(in TFWeaponHookContext weapon, float seconds) =>
        attributes.Float(seconds, "minicrit_boost_when_charged", weapon.Item);

    /// <summary>The charged shot's pierce (tf_weapon_sniperrifle.cpp:762-764).</summary>
    public bool PenetratesPlayersWhenCharged(in TFWeaponHookContext weapon) =>
        weapon.Item is { } item && attributes.Int(0, "sniper_penetrate_players_when_charged", item) > 0;

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

    /// <summary>
    /// The melee's sapper arm (<c>tf_weaponbase_melee.cpp:469-494</c>): stock's
    /// <c>CALL_ATTRIB_HOOK_INT</c> of <c>set_dmg_apply_to_sapper</c> over zero;
    /// any non-zero turns the arm on.
    /// </summary>
    public bool DmgApplyToSapper(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "set_dmg_apply_to_sapper", weapon.Item) != 0;

    /// <summary>
    /// The Boston Basher's miss tax (<c>tf_weaponbase_melee.cpp:755-763</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> of <c>hit_self_on_miss</c> gates on
    /// equality with one — the attribute answers a boolean, and only the exact
    /// one turns the arm on.
    /// </summary>
    public bool HitSelfOnMiss(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "hit_self_on_miss", weapon.Item) == 1;

    /// <summary>
    /// The Holiday Punch's silent crit (<c>tf_weaponbase_melee.cpp:989-1002</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> of <c>crit_does_no_damage</c> over
    /// zero; any non-zero zeroes a crit's damage.
    /// </summary>
    public bool CritDoesNoDamage(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "crit_does_no_damage", weapon.Item) > 0;

    /// <summary>
    /// The Holiday Punch's forced laugh (<c>tf_weaponbase_melee.cpp:884-901</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> of
    /// <c>crit_forces_victim_to_laugh</c> over zero.
    /// </summary>
    public bool CritForcesVictimToLaugh(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "crit_forces_victim_to_laugh", weapon.Item) > 0;

    /// <summary>
    /// The Holiday Punch's tickle (<c>tf_weaponbase_melee.cpp:903-927</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> of
    /// <c>tickle_enemies_wielding_same_weapon</c> over zero; no crit gate asks it.
    /// </summary>
    public bool TickleEnemiesWieldingSameWeapon(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "tickle_enemies_wielding_same_weapon", weapon.Item) > 0;

    /// <summary>
    /// The fists' silent mark (<c>tf_weaponbase_melee.cpp:354-368</c>): stock's
    /// <c>CALL_ATTRIB_HOOK_INT</c> of <c>self_mark_for_death</c> starts from
    /// zero, so the held item's seconds ride through.
    /// </summary>
    public int SelfMarkForDeath(in TFWeaponHookContext weapon, int seconds) =>
        attributes.Int(seconds, "self_mark_for_death", weapon.Item);

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

    /// <summary>
    /// The energy shot's cost (<c>GetEnergyShotCost</c>, tf_weapon_raygun.h:52-63):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> of <c>energy_weapon_no_drain</c> over
    /// zero — any non-zero answers a cost of none, so the meter neither spends
    /// nor checks against it. Without the attribute the five-round cost stands.
    /// </summary>
    public float EnergyShotCost(in TFWeaponHookContext weapon, float cost) =>
        attributes.Int(0, "energy_weapon_no_drain", weapon.Item) > 0 ? 0f : cost;

    /// <summary>
    /// The tank's whole shots (<c>GetMaxEnergyShotCount</c>'s
    /// <c>mult_clipsize_upgrade</c> over the count, tf_weapon_raygun.h): stock's
    /// <c>CALL_ATTRIB_HOOK_FLOAT</c> then the cast into the int member — the
    /// product truncated, as <see cref="ModifyAirBlastCost"/> truncates.
    /// </summary>
    public int ModifyEnergyShots(in TFWeaponHookContext weapon, int shots) =>
        (int)(shots * attributes.Float(1f, "mult_clipsize_upgrade", weapon.Item));

    /// <summary>
    /// Whether the ring pierces to a second body (<c>ShouldPenetrate</c>'s
    /// <c>energy_weapon_penetration</c>, tf_weapon_raygun.cpp:359-370): stock's
    /// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> over zero reads the owner's item,
    /// which the hook context's <see cref="TFWeaponHookContext.Item"/> is. Any
    /// non-zero pierces at the light hit's twenty; none takes the sixty.
    /// </summary>
    public bool EnergyPenetrates(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "energy_weapon_penetration", weapon.Item) != 0;

    /// <summary>
    /// Whether the meter stands alone (<c>energy_weapon_no_ammo</c>): stock's
    /// <c>CALL_ATTRIB_HOOK_INT</c> over zero; set, the weapon keeps no second
    /// purse for the recharge to draw on.
    /// </summary>
    public bool EnergyNoAmmo(in TFWeaponHookContext weapon) =>
        attributes.Int(0, "energy_weapon_no_ammo", weapon.Item) != 0;
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
        weapon.Conditions is { } conditions
        && (_effects.IsCritBoosted(conditions.Bits, clientSide: !conditions.Authoritative)
            // The pyro's rage boosts only a primary (tf_player_shared.cpp:8078-8082).
            || (conditions.Has(TFCond.CritboostedRageBuff) && weapon.Script.Category == TFWeaponCategory.Primary))
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
