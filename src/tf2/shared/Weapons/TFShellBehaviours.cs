//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Shared;

/// <summary>
/// The launchers (rocket launcher, grenade launcher, stickybomb launcher,
/// syringe gun): the shared half of <c>CTFWeaponBaseGun::PrimaryAttack</c>
/// (<c>tf_weaponbase_gun.cpp:95-200</c>) — the round spent, the crit rolled,
/// the next shot a fire delay from now — with the projectile itself asked of
/// the server (<see cref="ITFWeaponEffects.Projectile"/>, Stage 2.8). The
/// core's own attack is vetoed: it has no door for a TF projectile.
///
/// <para>
/// The stickybomb launcher charges instead of firing
/// (<c>CTFPipebombLauncher::PrimaryAttack</c> / <c>ItemPostFrame</c>,
/// <c>tf_weapon_pipebomblauncher.cpp:156-235</c>): a press starts the charge,
/// and the bomb leaves on release or when <c>TF_PIPEBOMB_MAX_CHARGE_TIME</c>
/// (4 s) is reached, carrying the seconds charged; the secondary detonates
/// (<c>SecondaryAttack</c> :428, the server's). The charge's start is
/// <see cref="WeaponNetState.BehaviourMeter"/>.
/// </para>
/// </summary>
public class TFProjectileBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>TF_PIPEBOMB_MAX_CHARGE_TIME</c> (<c>tf_weapon_pipebomblauncher.h:21</c>).</summary>
    public const float MaxChargeTime = 4f;

    /// <summary>
    /// The charge window's hook (<c>stickybomb_charge_rate</c>,
    /// <c>CTFPipebombLauncher::GetChargeMaxTime</c>,
    /// tf_weapon_pipebomblauncher.h:75) and the charged shot's damage scale
    /// (<c>stickybomb_charge_damage_increase</c>, <c>LaunchGrenade</c>,
    /// tf_weapon_pipebomblauncher.cpp:311-318).
    /// </summary>
    public const string ChargeRateAttribute = "stickybomb_charge_rate";
    public const string ChargeDamageAttribute = "stickybomb_charge_damage_increase";

    /// <summary>
    /// The Loose Cannon's charge window (<c>grenade_launcher_mortar_mode</c>,
    /// <c>CTFGrenadeLauncher::GetMortarDetonateTimeLength</c>,
    /// <c>tf_weapon_grenadelauncher.cpp:559-564</c>): the seconds the held
    /// shot waits before the misfire launches it — and the fuse the ball is
    /// born under, the window less the hold.
    /// </summary>
    public const string MortarModeAttribute = "grenade_launcher_mortar_mode";

    /// <summary>
    /// <c>FlareGunTypes_t</c> (<c>tf_weapon_flaregun.h:23-29</c>), what
    /// <see cref="FlareModeAttribute"/> names: the plain flare, the
    /// Detonator's, the Grordbort gun's and the Scorch Shot's.
    /// </summary>
    public const int FlareNormal = 0;
    public const int FlareDetonate = 1;
    public const int FlareGrordbort = 2;
    public const int FlareScorch = 3;

    /// <summary>The flare gun's mode attribute (<c>GetFlareGunType</c>, tf_weapon_flaregun.h:63's <c>CALL_ATTRIB_HOOK_INT</c>).</summary>
    public const string FlareModeAttribute = "set_weapon_mode";

    /// <summary>The energy weapon's empty pool (<c>energy_weapon_no_ammo</c>): the shot spends nothing.</summary>
    public const string NoAmmoAttribute = "energy_weapon_no_ammo";

    /// <summary>
    /// The vacuum's half-second, both of stock's clocks: the press's refire
    /// (<c>CTFFlareGun_Revenge::SecondaryAttack</c>, tf_weapon_flaregun.cpp:515)
    /// and the span between extinguishes (<c>m_fLastExtinguishTime</c>'s gate,
    /// :522).
    /// </summary>
    public const float ManmelterRefire = 0.5f;

    private static bool Charges(TFWeaponScript script) => script.Id is TFWeaponId.Pipebomblauncher or TFWeaponId.Cannon;

    /// <summary>
    /// GetChargeMaxTime (<c>tf_weapon_pipebomblauncher.h:75</c>): the stock 4 s
    /// through the firing item's <see cref="ChargeRateAttribute"/> — the
    /// window every charge arm of the launch runs on, the auto-release's
    /// (<c>PrimaryAttack</c>, :219) as well as the launch's own clamp. The
    /// cannon's window is its own hook instead (<see cref="MortarModeAttribute"/>,
    /// <c>CTFGrenadeLauncher::GetChargeMaxTime</c> over
    /// <c>GetMortarDetonateTimeLength</c>, <c>tf_weapon_grenadelauncher.cpp:543-546</c>):
    /// no attribute, no charge (<c>CanCharge</c>, :518-526), the press firing
    /// as the plain launch does.
    /// </summary>
    private float ChargeWindow(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var item = Context(in attack, script).Item;
        return script.Id == TFWeaponId.Cannon
            ? _attributes.Float(0f, MortarModeAttribute, item)
            : _attributes.Float(MaxChargeTime, ChargeRateAttribute, item);
    }

    /// <summary>The Manmelter, stock's <c>CTFFlareGun_Revenge</c> over the shell's id split.</summary>
    private static bool IsManmelter(TFWeaponScript script) => script.Id == TFWeaponId.FlaregunRevenge;

    /// <summary>The Short Circuit, stock's <c>CTFMechanicalArm</c>.</summary>
    private static bool IsShortCircuit(TFWeaponScript script) => script.Id == TFWeaponId.MechanicalArm;

    /// <summary>
    /// The orb's price, metal (<c>tf_mecharm_orb_cost</c>,
    /// <c>tf_weapon_mechanical_arm.cpp:48</c>): the count
    /// <c>ShockAttack</c> gates and spends (:109-133).
    /// </summary>
    public const int MechanicalArmCost = 65;

    /// <summary>The empty refire the water arm books (<c>SecondaryAttack</c>, :238).</summary>
    public const float MechanicalArmEmptyRefire = 0.67f;

    /// <summary>
    /// The shock attack, stock's own order (:222-292): the metal purse and
    /// the eyes under water refuse with the empty sound and the 0.67 s
    /// refire (:232-241) — the purse is this weapon's reserve, the metal the
    /// machine's pool prices; <c>CanAttack</c> silently refuses (:243); the
    /// count under the price is the empty sound and nothing else, stock's
    /// own else (:289-292) booking no refire; the shot is the SPECIAL3
    /// sound (:248) and, live, the orb, then both attacks wait the shot's
    /// 0.67 out (:305-306), the held button one orb per refire and not one
    /// a tick.
    /// </summary>
    private void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        var context = Context(in attack, script);

        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
        {
            PlaySound(in attack, script, WeaponScriptSound.Empty);
            weapon.NextSecondaryAttack = attack.Now + MechanicalArmEmptyRefire;
            return;
        }

        if (!CanAttack(in attack, in context))
            return;

        if (weapon.Reserve < MechanicalArmCost)
        {
            PlaySound(in attack, script, WeaponScriptSound.Empty);
            return;
        }
        weapon.Reserve -= MechanicalArmCost;

        PlaySound(in attack, script, WeaponScriptSound.Special3);
        if (attack.EffectsLive)
            Parts.Effects.ShockAttack(in attack, script);

        weapon.NextPrimaryAttack = attack.Now + MechanicalArmEmptyRefire;
        weapon.NextSecondaryAttack = attack.Now + MechanicalArmEmptyRefire;
    }

    /// <summary>
    /// The jars: the stock <c>CTFJar</c> family's pair whose shot is the meter
    /// itself (<c>tf_weapon_jar.h:41</c>, the milk its second face), with the
    /// gas face beside them (<c>CTFJarGas</c>, <c>tf_weapon_jar_gas.cpp</c>):
    /// the plain jars' purse is the effect bar, the gas's the COMBO meter —
    /// for both the count the press spends is the bar's refill, the
    /// <c>OnResourceMeterFilled</c>' grenade back (:102-109) standing in the
    /// same place the plain jar's regen grants its round.
    /// </summary>
    private static bool IsJar(TFWeaponScript script) =>
        script.Id is TFWeaponId.Jar or TFWeaponId.JarMilk or TFWeaponId.JarGas;

    /// <summary>
    /// The stickybomb launcher's own secondary is the detonation, not the
    /// class skill (<c>CTFPipebombLauncher::SecondaryAttack</c>,
    /// <c>tf_weapon_pipebomblauncher.cpp:428-469</c>), which the frame tail
    /// below runs every command the trigger is down; the other launchers keep
    /// the base's (<c>CTFGrenadeLauncher::SecondaryAttack</c>,
    /// <c>tf_weapon_grenadelauncher.cpp:491-502</c>).
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            // The Short Circuit's own secondary is the shock attack
            // (CTFMechanicalArm::SecondaryAttack,
            // tf_weapon_mechanical_arm.cpp:222-292); no class skill stands
            // behind the engineer's, as none behind the Manmelter's.
            if (IsShortCircuit(script))
            {
                ShockAttack(in attack, script);
                return;
            }

            if (Charges(script))
                return;

            // The Manmelter's press is the frame tail's below — stock's
            // CTFFlareGun_Revenge::SecondaryAttack is the charge's start and
            // stamp alone (:495-516), which the arm under the held trigger
            // carries; no class skill stands behind the pyro's secondary.
            if (IsManmelter(script))
                return;
        }

        base.SecondaryAttack(in attack);
    }

    /// <summary>
    /// The jar's count, restocked before the machine's own empty check reads
    /// it: stock grants the round when the regen's time is past
    /// (<c>CTFWeaponBase::CheckEffectBarRegen</c>,
    /// <c>tf_weaponbase.cpp:6544-6562</c>), and the frame that follows sees a
    /// count of one. Ours derives both purses from the bar at the head of the
    /// command — <see cref="WeaponLogic"/> refuses an empty weapon before it
    /// ever reaches the veto below, so a grant in the veto would be a round
    /// too late (<c>CTFJar::PrimaryAttack</c>'s own first refusal,
    /// <c>tf_weapon_jar.cpp:114-128</c>, is the count gate). The pool is the
    /// count the shipped script spends (clip_size -1 over TF_AMMO_GRENADES1,
    /// the fixture); the clip joins it for a clipful shape.
    /// </summary>
    public override void ItemPreFrame(in WeaponAttackContext attack, int buttons)
    {
        if (ScriptOf(attack.Type) is not { } script || !IsJar(script))
            return;

        var weapon = attack.Weapon;
        (weapon.Clip, weapon.Reserve) = TFItemMeter.RegenFinished(weapon) ? (1, 1) : (0, 0);
    }

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return false;

        // The machine reloads an empty clip itself, after this veto: say yes to it.
        if (weapon.Clip == 0 && ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex) > 0)
            return true;

        // CanCharge (tf_weapon_grenadelauncher.cpp:518-526): a window of none is the plain launch.
        if (Charges(script) && ChargeWindow(in attack, script) > 0f)
        {
            if (weapon.BehaviourMeter <= 0f)
            {
                weapon.BehaviourMeter = attack.Now;
                SendAnim(in attack, WeaponAnim.Primary);
            }
            else if (attack.Now - weapon.BehaviourMeter >= ChargeWindow(in attack, script))
            {
                Launch(in attack, script, in context);
            }
            return false;
        }

        Launch(in attack, script, in context);
        return false;
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            var weapon = attack.Weapon;
            if (Charges(script))
            {
                bool held = (weapon.LastButtons & (int)InputButtons.Attack) != 0;

                if (weapon.BehaviourMeter > 0f
                    && ((!held && weapon.Clip > 0) || attack.Now - weapon.BehaviourMeter >= ChargeWindow(in attack, script)))
                    Launch(in attack, script, Context(in attack, script));

                if ((weapon.LastButtons & (int)InputButtons.Attack2) != 0 && attack.EffectsLive)
                    Parts.Effects.Detonate(in attack, script);
            }
            else if (IsManmelter(script))
                ManmelterFrame(in attack, script);
            else if (FlareModeOf(in attack, script) == FlareDetonate
                     && (weapon.LastButtons & (int)InputButtons.Attack2) != 0 && attack.EffectsLive)
            {
                // The Detonator's alt (CTFFlareGun::SecondaryAttack,
                // tf_weapon_flaregun.cpp:147-173): CanAttack's gate is the
                // sticky's shape here too, the frame tail running it every
                // command the trigger is down.
                Parts.Effects.Detonate(in attack, script);
            }
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The held gun's mode, stock's <c>GetFlareGunType</c> (tf_weapon_flaregun.h:63).</summary>
    private int FlareModeOf(in WeaponAttackContext attack, TFWeaponScript script) =>
        (int)_attributes.Float(0f, FlareModeAttribute, Context(in attack, script).Item);

    /// <summary>
    /// The Manmelter's frames (CTFFlareGun::ItemPostFrame :252-267 over
    /// CTFFlareGun_Revenge::ChargePostFrame, tf_weapon_flaregun.cpp:518-582):
    /// trigger up, <c>StopCharge</c>; held, the charge starts once under the
    /// re-entry gate the press keeps (:497, :515 — <see
    /// cref="WeaponNetState.BehaviourTimeA"/>, stock's m_flChargeBeginTime),
    /// and the vacuum asks past the last extinguish by the half-second — the
    /// stamp lands on an extinguish alone (:547's m_fLastExtinguishTime,
    /// <see cref="WeaponNetState.BehaviourTimeB"/>), so a held beam with
    /// nobody to save re-asks each command, stock's frame gate too.
    /// </summary>
    private void ManmelterFrame(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;

        if ((weapon.LastButtons & (int)InputButtons.Attack2) == 0)
        {
            weapon.BehaviourTimeA = 0f;
            return;
        }

        if (weapon.BehaviourTimeA == 0f)
            weapon.BehaviourTimeA = attack.Now;
        if (weapon.NextSecondaryAttack <= attack.Now)
            weapon.NextSecondaryAttack = attack.Now + ManmelterRefire;

        if (attack.Now > weapon.BehaviourTimeB + ManmelterRefire && attack.EffectsLive
            && Parts.Effects.ExtinguishBeam(in attack, script))
            weapon.BehaviourTimeB = attack.Now;
    }

    private void Launch(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var weapon = attack.Weapon;

        // GetChargeMaxTime again (tf_weapon_pipebomblauncher.h:75): the window
        // the held charge clamps to, the same number the auto-release and the
        // speed remap run on.
        float window = Charges(script) ? ChargeWindow(in attack, script) : MaxChargeTime;
        float held = weapon.BehaviourMeter > 0f ? attack.Now - weapon.BehaviourMeter : 0f;
        float charge = MathF.Min(held, window);
        weapon.BehaviourMeter = 0f;

        // GetAmmoPerShot's chain (tf_weaponbase_gun.cpp:458-471) over the
        // launch's pool: the item's mod_ammo_per_shot — the Short Circuit's
        // five (CTFMechanicalArm::PrimaryAttack's CALL_ATTRIB_HOOK_INT over
        // RemoveAmmo, tf_weapon_mechanical_arm.cpp:467-469) — the stock gate
        // reads its own zero-answer override and spends the attr regardless;
        // the shell prices the shot and refuses under it, the base gun's
        // :123-131 arm.
        int cost = PrimaryAmmoPerShot(attack.Type, attack.Command.OwnerIndex, script.Primary.AmmoPerShot);

        // The energy weapon's pool is none (the Manmelter's
        // energy_weapon_no_ammo): the shot spends nothing, the clip standing
        // where the deploy put it for the machine's empty gate.
        if (_attributes.Int(0, NoAmmoAttribute, context.Item) == 0)
        {
            if (ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex) > 0)
            {
                if (weapon.Clip < cost)
                    return;
                weapon.Clip -= cost;
            }
            else
            {
                if (weapon.Reserve < cost)
                    return;
                weapon.Reserve -= cost;
            }
        }

        // The jar's second purse empties with the round: the bar restart
        // stock's FireJar opens (StartEffectBarRegen, tf_weapon_jar.cpp:135),
        // the meter back at zero for the regen the frame tail runs; the gas
        // face's spend is the same zero under RemoveJarGas
        // (tf_weapon_jar_gas.cpp:255-265), the grenade behind it the purse
        // arm above spends.
        if (IsJar(script))
            TFItemMeter.Use(weapon);

        var shot = ModifyShot(in attack, default);

        // LaunchGrenade's charged damage (tf_weapon_pipebomblauncher.cpp:311-318):
        // the item's scale over the held fraction of the window — stock's
        // GetCurrentCharge, unclamped — laid on the post-mult_dmg damage the
        // projectile is born with.
        if (Charges(script) && window > 0f)
        {
            float chargeDamage = _attributes.Float(1f, ChargeDamageAttribute, context.Item);
            if (chargeDamage != 1f)
                shot = shot with { Damage = shot.Damage + shot.Damage * (chargeDamage - 1f) * (held / window) };
        }

        SendAnim(in attack, WeaponAnim.Primary);
        PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge);

        weapon.NextPrimaryAttack = attack.Now + FireDelay(in attack, script);
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack;
        Parts.Hooks.AfterFire(in context, in attack);

        // The launch ends as every stock shot of a gun does, in the tail
        // DoFireEffects of CTFWeaponBaseGun::FireProjectile
        // (tf_weaponbase_gun.cpp:357, the case block's common end :330-368):
        // the muzzle flash, unless the weapon vetoes it
        // (ShouldDoMuzzleFlash, :1008-1018 — no launcher does). The rocket
        // launcher's shipped script names no effect, so its ask is the empty
        // name's no-op, as stock's is.
        if (attack.EffectsLive && ShouldDoMuzzleFlash && script.MuzzleFlashParticleEffect.Length > 0)
            attack.Arms.MuzzleFlash(attack.Type, script.MuzzleFlashParticleEffect);

        // The Manmelter's shot spends a banked crit
        // (CTFFlareGun_Revenge::PrimaryAttack, tf_weapon_flaregun.cpp:486-492):
        // one off, floored at zero, and the condition with it — the setter's
        // sync (CTFPlayerShared::SetRevengeCrits,
        // tf_player_shared.cpp:14483-14486), this gun the active weapon the
        // check names at both ends of the count.
        if (IsManmelter(script)
            && Parts.PlayerData.Predicted(attack.Command.OwnerIndex) is { } bank)
        {
            bank.RevengeCrits = Math.Max(0, bank.RevengeCrits - 1);
            if (bank.RevengeCrits == 0)
                Parts.Owners.ConditionsOf(attack.Command.OwnerIndex)?.Remove(TFCond.Critboosted);
        }
    }
}

/// <summary>
/// The flamethrower's shell (<c>CTFFlameThrower::PrimaryAttack</c>,
/// <c>tf_weapon_flamethrower.cpp:760-935</c>): a flame tick every
/// <c>tf_flamethrower_new_flame_fire_delay</c> (0.02 s, which replaces the
/// script's delay, :822-825) at <c>tf_flamethrower_damage_per_tick</c> 13
/// through the chain and the rapid-fire crit, using
/// <c>TF_FLAMETHROWER_AMMO_PER_SECOND_PRIMARY_ATTACK</c> 14 a second through a
/// remainder kept to two digits (:912-929). The flames are the server's
/// (<see cref="ITFWeaponEffects.Flames"/>, Stage 2.11). Stock spends the ammo
/// on the server only; here both sides spend it (the remainder is
/// <see cref="WeaponNetState.BehaviourMeter"/>, predicted). The airblast is Stage 2.11's.
///
/// <para>
/// The weapon state rides <see cref="WeaponNetState.BehaviourMode"/> beside
/// the shell's own fields, stock's networked <c>m_iWeaponState</c>
/// (<c>FlameState_t</c>, <c>tf_weapon_flamethrower.h</c>): the machine of
/// <c>PrimaryAttack</c> :743-787 — idle, the 0.16 s start (:753, in
/// <see cref="WeaponNetState.BehaviourTimeC"/>), firing — the release home to
/// idle (<c>ItemPostFrame</c> :604-611, the SECONDARY arm stock exempts
/// kept), the blast's SECONDARY (<c>FireAirBlast</c> :978), and the last
/// tick's crit (<c>m_bCritFire</c>, :844) on the bit beside them, which the
/// client's flame colour reads. Stock's <c>SetWeaponState</c> arms on the
/// flame manager's firing gate (:1102-1112) and the forward-pull conds wait
/// with those features.
/// </para>
/// </summary>
public class TFFlamethrowerBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>FlameState_t</c> (<c>tf_weapon_flamethrower.h</c>).</summary>
    public enum FlameState
    {
        Idle = 0,
        StartFiring,
        Firing,
        Secondary,
    }

    /// <summary><c>FT_STATE_*</c> state bits in <see cref="WeaponNetState.BehaviourMode"/>.</summary>
    private const int StateMask = 7 << ModeKindShift;

    /// <summary><c>m_bCritFire</c>, the bit beside the state (:844).</summary>
    public const int CritLatchMask = 1 << (ModeKindShift + 3);

    /// <summary>The start's window (<c>m_flStartFiringTime</c>, :753 — 5 frames at 30 fps).</summary>
    public const float StartFiringDelay = 0.16f;

    /// <summary>The weapon's flame state.</summary>
    public static FlameState StateOf(WeaponNetState weapon) => (FlameState)((weapon.BehaviourMode & StateMask) >> ModeKindShift);

    /// <summary>Whether the last tick burnt as a crit — the flame colour's bit.</summary>
    public static bool CritLatched(WeaponNetState weapon) => (weapon.BehaviourMode & CritLatchMask) != 0;

    private static void SetState(WeaponNetState weapon, FlameState state) =>
        weapon.BehaviourMode = (weapon.BehaviourMode & ~StateMask) | ((int)state << ModeKindShift);

    public const float FlameFireDelay = 0.02f;
    public const float DamagePerTick = 13f;
    public const float AmmoPerSecond = 14f;

    /// <summary><c>tf_flamethrower_burstammo</c> (<c>tf_weapon_flamethrower.cpp:70</c>): the airblast's ammunition before the item's scale.</summary>
    public int AirBlastAmmo => Parts.FlamethrowerBurstammo;

    /// <summary>
    /// Stock's <c>iAmmoPerShot</c> (<c>tf_weapon_flamethrower.cpp:1192-1194</c>):
    /// the burst ammo through the item's <c>mult_airblast_cost</c>. The blast's
    /// check and its spend both see this one number.
    /// </summary>
    public int AirBlastCost(in WeaponAttackContext attack) =>
        ScriptOf(attack.Type) is { } script
            ? Parts.Hooks.ModifyAirBlastCost(Context(in attack, script), Parts.FlamethrowerBurstammo)
            : Parts.FlamethrowerBurstammo;

    /// <summary><c>FireAirBlast</c>'s refires (<c>tf_weapon_flamethrower.cpp:1044-1045</c>).</summary>
    public const float AirBlastRefire = 0.75f;

    /// <summary>The burning loop's gesture re-arm (<c>tf_weapon_flamethrower.cpp:780</c>, "fewer than 45!").</summary>
    public const float SustainedAnimRefire = 1.4f;
    public const float AirBlastPrimaryRefire = 1.0f;

    protected override float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) => DamagePerTick;

    /// <summary>The airblast is the flamethrower's own secondary (<c>CTFFlameThrower::SecondaryAttack</c>), not the class skill.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    /// <summary>
    /// The secondary is the airblast (<c>CTFFlameThrower::SecondaryAttack</c>
    /// through <c>FireAirBlast</c>, <c>tf_weapon_flamethrower.cpp:1152-1208</c>,
    /// :965-1049): refused inside the refire delay, with the eyes under the
    /// water (:1175-1176, before the ammunition is even counted) and short of
    /// the ammunition, and on a blast the tick is not also thrown this frame
    /// (<c>m_bFiredSecondary</c>, :556/:595).
    /// </summary>
    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        // The release (ItemPostFrame :599, :604-611): no trigger, or no ammo
        // under a held one, over a burning state comes home to idle, the crit
        // with it — the SECONDARY state stock exempts stays.
        if (((attack.Weapon.LastButtons & (int)InputButtons.Attack) == 0 || attack.Weapon.Reserve <= 0)
            && StateOf(attack.Weapon) is not (FlameState.Idle or FlameState.Secondary))
        {
            // The release's POST (ItemPostFrame :606-607), before the home.
            attack.Arms.GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger.WindDown);
            SetState(attack.Weapon, FlameState.Idle);
            attack.Weapon.BehaviourMode &= ~CritLatchMask;
        }

        if ((attack.Weapon.LastButtons & (int)InputButtons.Attack2) != 0
            && ScriptOf(attack.Type) is { } script)
        {
            // SecondaryAttack's buff arm (:1196-1200): a set_buff_type item's
            // secondary is the rage, not the blast — the Phlogistinator's, the
            // attribute the airblast's CanAirBlast (:353-363) silences beside.
            int buffType = _attributes.Int(0, TFRageMeter.BuffTypeAttribute,
                                           Parts.Owners.ItemOf(attack.Command.OwnerIndex, attack.Type.Name));
            if (buffType != 0 ? RagePress(in attack, buffType) : AirBlast(in attack, script))
                return;
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The rage press's hold (<c>UseRage</c>'s :1140 server stamp;
    /// the client's :1143 is unconditional, and the predicted field is the
    /// client's shape).</summary>
    public const float RageRefire = 1f;

    /// <summary>
    /// The rage press, <c>UseRage</c> (:1120-1147) behind
    /// <c>SecondaryAttack</c>'s shared gates: the refused re-press (:1164) and
    /// the eyes under the water (:1175) consume nothing, the meter short of
    /// full is <c>IsRageFull</c>'s no (:1122), and a full one asks the taunt
    /// whose arm lights the rage — the effects' <see cref="ITFWeaponEffects.UseRage"/>.
    /// </summary>
    private bool RagePress(in WeaponAttackContext attack, int buffType)
    {
        var weapon = attack.Weapon;
        if (weapon.NextSecondaryAttack > attack.Now)
            return false;

        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            return false;

        if (Parts.PlayerData.Predicted(attack.Command.OwnerIndex) is not { } state
            || state.RageMeter < TFRageMeter.FullMeter)
            return false;

        weapon.NextSecondaryAttack = attack.Now + RageRefire;
        Parts.Effects.UseRage(in attack, buffType);
        return true;
    }

    protected bool AirBlast(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        if (weapon.NextSecondaryAttack > attack.Now)
            return false;

        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            return false;

        // The one scaled number of stock's :1194: the reserve check and the
        // spend both see it.
        int ammo = AirBlastCost(in attack);

        if (weapon.Reserve < ammo)
            return false;

        weapon.Reserve -= ammo;

        // The blast's state (FireAirBlast :978); the client answers it with
        // the blast's puff, the flame stopped. Its SECONDARY ask rides the
        // same arm (:981, beside the SendWeaponAnim).
        attack.Arms.GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger.Secondary);
        SetState(weapon, FlameState.Secondary);

        if (attack.EffectsLive)
            Parts.Effects.AirBlast(in attack, script);

        weapon.NextSecondaryAttack = attack.Now + AirBlastRefire;
        weapon.NextPrimaryAttack = attack.Now + AirBlastPrimaryRefire;
        return true;
    }

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack) || weapon.Reserve <= 0)
        {
            // The refusal answers idle (PrimaryAttack :710-717).
            SetState(weapon, FlameState.Idle);
            return false;
        }

        // The state machine (PrimaryAttack :743-787): the start's window is
        // 0.16 s (:753, our <see cref="StartFiringDelay"/> in BehaviourTimeC),
        // the firing state after it (:768-771). The firing gesture's clock is
        // BehaviourTimeC too — stock's m_flNextPrimaryAttackAnim (:771, :780),
        // a member of its own — so the rapid-fire crit's clocks keep
        // BehaviourTimeA/B (m_flCritTime, m_flLastCritCheckTime) to themselves.
        // The player animation events and the min-burst window wait with the
        // player-anim layer.
        switch (StateOf(weapon))
        {
            case FlameState.Idle:
                // Just started, play PRE (:749) and start looping view model anim.
                attack.Arms.GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger.WindUp);
                weapon.BehaviourTimeC = attack.Now + StartFiringDelay;
                SetState(weapon, FlameState.StartFiring);
                break;

            case FlameState.StartFiring when attack.Now > weapon.BehaviourTimeC:
                SetState(weapon, FlameState.Firing);
                weapon.BehaviourTimeC = attack.Now;
                break;

            case FlameState.Firing when attack.Now >= weapon.BehaviourTimeC:
                // The loop's own gesture, 1.4 s at a time (:777-781): the
                // ATTACK_PRIMARY the per-command ask cannot cadence.
                attack.Arms.GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger.SustainedFire);
                weapon.BehaviourTimeC = attack.Now + SustainedAnimRefire;
                break;
        }

        var shot = ModifyShot(in attack, default);

        // m_bCritFire, the latch the client's flame colour reads (:844).
        if (shot.Critical)
            weapon.BehaviourMode |= CritLatchMask;
        else
            weapon.BehaviourMode &= ~CritLatchMask;

        // "Don't attack if we're underwater" (:827-908): the tick is not
        // thrown, but the ammo and the refires below still are (:910-931).
        if (attack.EffectsLive && !Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            Parts.Effects.Flames(in attack, script, shot);

        float remainder = weapon.BehaviourMeter + AmmoPerSecond * FlameFireDelay;
        int spend = (int)remainder;
        if (spend > 0)
        {
            weapon.Reserve = Math.Max(0, weapon.Reserve - spend);
            remainder -= spend;
            remainder = (int)(remainder * 100) / 100f;
        }
        weapon.BehaviourMeter = remainder;

        SendAnim(in attack, WeaponAnim.Primary);
        weapon.NextPrimaryAttack = attack.Now + FlameFireDelay;
        weapon.TimeWeaponIdle = attack.Now + FlameFireDelay;
        Parts.Hooks.AfterFire(in context, in attack);
        return false;
    }
}

/// <summary>
/// The Dragon's Fury's shell (<c>CTFWeaponFlameBall : CTFFlameThrower</c>,
/// <c>tf_weapon_dragons_fury.h:27</c>): the held fire is one ball on the full
/// charge meter and no clip — <c>HasFullCharge</c> gates the attack ahead of
/// the ammunition count and <c>CanAttack</c>
/// (<c>tf_weapon_dragons_fury.cpp:91-99</c>), the ball spends one round off
/// the reserve, and the meter resets with the shot (:125-126). The blast is
/// the flamethrower's, asked through the same full-pressure gate and leaving
/// the refill on the penalty (:196-234). Stock sets no refire clock of its own
/// (:260): the refill pace of the meter is the fire rate, not a clip's cadence.
/// </summary>
public class TFFlameBallBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>The empty-click pause of the underwater refusal (<c>:266</c>).</summary>
    public const float UnderwaterPause = 0.2f;

    /// <summary>
    /// <c>HasFullCharge</c> (<c>:236-243</c>): the carried meter at full.
    /// </summary>
    public static bool HasFullCharge(WeaponNetState weapon) => weapon.ItemMeter >= 1f;

    /// <summary>
    /// The held fire (<c>PrimaryAttack</c> :83-132 through
    /// <c>FireProjectile</c> :134-194), gated in stock's own order — the
    /// underwater refusal of <c>ItemPostFrame</c> :262-268 ahead of the meter
    /// (:91), the ammo count (:94-96) and <c>CanAttack</c> (:98). A fired ball
    /// spends one round off the reserve (the clipless <c>RemoveProjectileAmmo</c>),
    /// plays its single or crit sound off the crit the launcher carries
    /// (:180, :184), asks the server entity of
    /// <see cref="ITFWeaponEffects.Projectile"/> and hands the meter back to
    /// zero (:125-126). No next-fire clock is set: the refill pace of the meter
    /// is the fire rate, stock's gate at :260 leaving it there.
    /// </summary>
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;

        // "This weapon doesn't fire underwater" (:262-268): the empty click
        // and the quarter-second pause, before the meter is even asked.
        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
        {
            PlaySound(in attack, script, WeaponScriptSound.Empty);
            weapon.NextPrimaryAttack = attack.Now + UnderwaterPause;
            return false;
        }

        if (!HasFullCharge(weapon) || weapon.Reserve <= 0)
            return false;

        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return false;

        int cost = Math.Max(1, script.Primary.AmmoPerShot);
        if (weapon.Reserve < cost)
            return false;
        weapon.Reserve -= cost;

        var shot = ModifyShot(in attack, default);

        SendAnim(in attack, WeaponAnim.Primary);
        PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge: 0f);

        TFItemMeter.Use(weapon);
        Parts.Hooks.AfterFire(in context, in attack);
        return false;
    }

    /// <summary>
    /// The charged blast (<c>SecondaryAttack</c> :196-234): full pressure first
    /// (:199), then the blast of <c>BaseClass::SecondaryAttack</c> (:220) — the
    /// flame's own, whose cost reads the burst ammo through the item's
    /// <c>mult_airblast_cost</c> (:212-214), checked and spent as one number
    /// (:217-218) — and behind its success the penalty on the refill and the
    /// meter back to zero (:228-229). Stock's caller gates the whole arm on
    /// <c>CanAttack</c> (:254); ours asks here. The machine's Attack2 arm
    /// already holds the blast to its refire clock, the flame's clocks stamped
    /// here as <c>FireAirBlast</c> stamps them.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if (!HasFullCharge(weapon) || weapon.NextSecondaryAttack > attack.Now)
            return;

        if (ScriptOf(attack.Type) is not { } script)
            return;

        // Stock's caller gate on CanAttack (:254), ours at the item's own arm.
        if (!CanAttack(in attack, Context(in attack, script)))
            return;

        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            return;

        // The one scaled number of the blast (:212-218): the check and the
        // spend see the same figure through the item's hook.
        int ammo = Parts.Hooks.ModifyAirBlastCost(Context(in attack, script), Parts.FlamethrowerBurstammo);
        if (weapon.Reserve < ammo)
            return;
        weapon.Reserve -= ammo;

        // The flame's blast (:220's BaseClass::SecondaryAttack), the state and
        // the refires its own arm carries (:978, :1044-1045).
        attack.Arms.GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger.Secondary);
        if (attack.EffectsLive)
            Parts.Effects.AirBlast(in attack, script);

        weapon.NextSecondaryAttack = attack.Now + TFFlamethrowerBehaviour.AirBlastRefire;
        weapon.NextPrimaryAttack = attack.Now + TFFlamethrowerBehaviour.AirBlastPrimaryRefire;

        // Stock's DF leaves the refill on the penalty (:228), and the blast
        // spends the charge it was asked with (:229's SetItemChargeMeter(0)).
        weapon.FlameBallChargeScale = TFItemMeter.RechargePenalty;
        TFItemMeter.Use(weapon);
    }
}

/// <summary>
/// The medigun's shell: no ammunition and no swing (the core would swing a
/// row without ammo); the trigger held or let go is told to the server every
/// command (<see cref="ITFWeaponEffects.Heal"/>, Stage 2.9's beam, target and
/// charge). The charge LEVEL is the carried weapon's own state
/// (<see cref="WeaponNetState.BehaviourMeter"/>, stock's m_flChargeLevel on
/// DT_WeaponMedigun): the server writes the meter at every command the gun
/// answers, and the client's replay of a command advances it here between
/// the server's words — stock's predicted <c>CWeaponMedigun</c> runs the
/// same build and drain in its own prediction.
/// </summary>
public class TFMedigunBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>Stock's weapon_medigun_chargerelease_rate default (tf_weapon_medigun.cpp:77);
    /// the release rate's convar is still to come (docs/divergences.md).</summary>
    public const float ChargeReleaseRate = 8f;    // seconds of release to an empty meter

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;

    /// <summary>The medigun's secondary is its own (<c>CWeaponMedigun::SecondaryAttack</c>, tf_weapon_medigun.cpp:1813), not the class skill.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            bool held = (attack.Weapon.LastButtons & (int)InputButtons.Attack) != 0;

            // The replayed half — the target pick stock runs on both sides
            // (tf_weapon_medigun.cpp:1214) — every pass, before the charge:
            // a pass that wrote no beam would stand judged against the
            // server's word. The server's own answer holds no replay: the
            // whole frame is its arm's.
            Parts.MedigunPrediction.Frame(in attack, script, held);

            if (attack.EffectsLive)
                Parts.Effects.Heal(in attack, script, held);
            else
                PredictCharge(in attack, Parts.PlayerData);
        }

        // The tail's stamp, every command the gun answers: the server's arm
        // closes with the release clock at this command (the dt the drain and
        // the compare run on), so the replay — the release's own write below
        // is this same value — stamps it too, on the first prediction and
        // every pass, or a held gun's clock stands at the decode.
        attack.Weapon.BehaviourTimeB = attack.Now;

        base.ItemPostFrame(in attack);
    }

    /// <summary>
    /// The replay's charge: the build while the predicted beam is up, the
    /// drain while the predicted release is on, at the plain rates — the
    /// halved crawl near the buffed max and the healers' share are the
    /// server's word, which corrects the meter at every decode. A command
    /// whose dt is the server's own clock (fresh out of a decode) or is
    /// otherwise outside a command interval pays nothing: the clocks are
    /// written back on this machine's own from then on.
    /// </summary>
    private void PredictCharge(in WeaponAttackContext attack, ITFPlayerData data)
    {
        if (data.Predicted(attack.Command.OwnerIndex) is not { } carrier)
            return;

        var weapon = attack.Weapon;
        float now = attack.Now;
        if (carrier.ChargeReleasing)
        {
            float dt = now - weapon.BehaviourTimeB;
            if (dt is < 0f or > 1f)
                dt = 0f;
            weapon.BehaviourMeter = Math.Max(0f, weapon.BehaviourMeter - dt / ChargeReleaseRate);
        }
        else if (carrier.HealTargetIndex >= 0)
        {
            float dt = now - weapon.BehaviourTimeA;
            if (dt is < 0f or > 1f)
                dt = 0f;
            weapon.BehaviourTimeA = now;
            // The setup triple reads the same gate the server's build does
            // (tf_weapon_medigun.cpp:1347-1350); the crawl and the healers'
            // share stay the server's word, corrected at every decode.
            float setup = Parts.RoundSetup.InSetupWithRoundTimer ? 3f : 1f;
            weapon.BehaviourMeter = Math.Min(1f, weapon.BehaviourMeter + dt / Parts.MedigunChargeRate * setup);
        }
    }
}

/// <summary>
/// A PDA (<c>WeaponType "pda"</c>): its trigger has no shot — the attack is
/// vetoed and the owner instead goes back to the item held before this,
/// <c>CTFWeaponPDA::PrimaryAttack</c>'s one call to
/// <c>SelectLastItem</c> (<c>shared/tf/tf_weapon_pda.cpp:89-98</c>). The
/// secondary (the class special skill) and the spy's menu ride their lanes.
/// </summary>
public class TFPDABehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (attack.EffectsLive && ScriptOf(attack.Type) is { } script)
            Parts.Effects.SelectLast(in attack, script);

        return false;
    }
}

/// <summary>
/// A weapon with no attack of its own — the sapper and the invisibility
/// watch (the PDAs' trigger has the item switch, the builder its own kind):
/// every attack is vetoed (the core would swing a row without ammo). Their
/// uses are Stage 2.12's.
/// </summary>
public class TFInertBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;
}
