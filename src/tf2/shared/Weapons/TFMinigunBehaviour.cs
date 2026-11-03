//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The heavy's minigun: <c>CTFMinigun</c>'s spin-up state machine
/// (<c>shared/tf/tf_weapon_minigun.cpp</c>, <c>SharedAttack</c> :226-470).
///
/// <para>
/// Either trigger winds the gun up (<c>WindUp</c>, :749-782: <c>TF_COND_AIMING</c>
/// on, which the movement rules turn into the heavy's aiming speed) for
/// <c>TF_MINIGUN_SPINUP_TIME</c> 0.75 s; then the primary fires and the
/// secondary spins. Firing is <c>CTFWeaponBaseGun::PrimaryAttack</c> each fire
/// delay while the pool lasts, at a damage and spread that ramp in over the
/// first second spun (<c>GetProjectileDamage</c> :996-1013, damage
/// <c>RemapValClamped(spin, 0.2, 1, 0.5, 1)</c>; <c>GetWeaponSpread</c>
/// :1019-1037, spread <c>RemapValClamped(spin, 0, 1, 1.5, 1)</c>). An empty
/// pool dry-fires. With both triggers let go the gun winds down once its idle
/// time passes (<c>WeaponIdle</c> :899-920, <c>WindDown</c> :848-893: aiming
/// off, idle two seconds on).
/// </para>
///
/// <para>
/// The core's own attack is vetoed: the state machine runs every command in
/// <see cref="ItemPostFrame"/>, reading the triggers from the weapon's
/// <see cref="WeaponNetState.LastButtons"/>. Its predicted state: the state in
/// <see cref="WeaponNetState.BehaviourMode"/> (bits 8-10), the rapid-fire crit
/// clocks in <see cref="WeaponNetState.BehaviourTimeA"/>/<c>B</c>, and in
/// <see cref="WeaponNetState.BehaviourMeter"/> the wind-up's start
/// (<c>m_flStartedWindUpAt</c>; after a dry fire, the firing start less the
/// spin-up, which gives <c>Max(windup - spinup, firing)</c> the same answer),
/// and in <see cref="WeaponNetState.BehaviourTimeC"/>/<see cref="WeaponNetState.BehaviourMeterB"/>
/// the spun drain's clock and accumulator (<c>m_flLastAmmoDrainTime</c>,
/// <c>m_flAccumulatedAmmoDrain</c>).
/// </para>
/// </summary>
public class TFMinigunBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>MinigunState_t</c> (<c>tf_weapon_minigun.h:28-35</c>).</summary>
    public enum State
    {
        Idle = 0,
        StartFiring,
        Firing,
        Spinning,
        DryFire,
    }

    /// <summary><c>TF_MINIGUN_SPINUP_TIME</c> (:35).</summary>
    public const float SpinUpTime = 0.75f;

    /// <summary><c>TF_MINIGUN_PENALTY_PERIOD</c> (:36).</summary>
    public const float PenaltyPeriod = 1f;

    private const int StateMask = 7 << ModeKindShift;

    // Past every idle a spun minigun sets itself (0.1, 0.2, 0.75 s).
    private const float IdlePushedOut = 2.5f;

    /// <summary>The weapon's minigun state.</summary>
    public static State StateOf(WeaponNetState weapon) => (State)((weapon.BehaviourMode & StateMask) >> ModeKindShift);

    /// <summary>
    /// <c>SetWeaponState</c> (<c>tf_weapon_minigun.cpp:471-484</c>): a change
    /// out of the unspun states restarts the spun drain's clock and rings the
    /// ring of fire's own half a second ahead (:477-479) — the arm holds that
    /// one, so the transition says so.
    /// </summary>
    private void SetState(in WeaponAttackContext attack, State state)
    {
        var weapon = attack.Weapon;
        if (StateOf(weapon) == state)
            return;

        if (StateOf(weapon) is State.Idle or State.StartFiring or State.DryFire)
        {
            weapon.BehaviourTimeC = attack.Now;
            Parts.Effects.Rearmed(in attack);
        }

        weapon.BehaviourMode = (weapon.BehaviourMode & ~StateMask) | ((int)state << ModeKindShift);
    }

    /// <summary>How long the gun has spun, net of the spin-up (<c>Max(GetWindUpDuration() - spinup, GetFiringDuration())</c>).</summary>
    public static float SpinTime(WeaponNetState weapon, float now) =>
        weapon.BehaviourMeter > 0f ? now - weapon.BehaviourMeter - SpinUpTime : -SpinUpTime;

    /// <summary><c>GetProjectileDamage</c>'s ramp over the first second.</summary>
    public static float DamageScale(float spinTime) =>
        spinTime < PenaltyPeriod ? CritBucket.RemapValClamped(spinTime, 0.2f, PenaltyPeriod, 0.5f, 1f) : 1f;

    /// <summary><c>GetWeaponSpread</c>'s ramp over the first second.</summary>
    public static float SpreadScale(float spinTime) =>
        spinTime < PenaltyPeriod ? CritBucket.RemapValClamped(spinTime, 0f, PenaltyPeriod, 1.5f, 1f) : 1f;

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;

    /// <summary>The spin is the minigun's own secondary (<c>CTFMinigun::SecondaryAttack</c>, tf_weapon_minigun.cpp:489), not the class skill; the frame tail above runs it.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    protected override bool HoldsAiming(in WeaponAttackContext attack) => StateOf(attack.Weapon) != State.Idle;

    protected override float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) =>
        script.Primary.Damage * DamageScale(SpinTime(attack.Weapon, attack.Now));

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            var weapon = attack.Weapon;
            bool primary = (weapon.LastButtons & (int)InputButtons.Attack) != 0;
            bool secondary = (weapon.LastButtons & (int)InputButtons.Attack2) != 0;

            // The trigger on an empty pool is asked whatever the timer says:
            // the machine answers it with its own empty click, which holds the
            // timer, and stock's HandleFireOnEmpty (:978-991) turns a firing or
            // spinning gun to a dry fire then, as the attack below does.
            if ((primary && (weapon.NextPrimaryAttack <= attack.Now || weapon.Reserve <= 0))
                || (!primary && secondary && weapon.NextSecondaryAttack <= attack.Now))
                SharedAttack(in attack, script, spinOnly: !primary);
            else if (!primary && !secondary)
                WeaponIdle(in attack);
        }

        base.ItemPostFrame(in attack);
    }

    private void SharedAttack(in WeaponAttackContext attack, TFWeaponScript script, bool spinOnly)
    {
        var weapon = attack.Weapon;
        float now = attack.Now;

        switch (StateOf(weapon))
        {
            case State.Idle:
                WindUp(in attack);
                // The item's wind-up (tf_weapon_minigun.cpp:259-260); the
                // damage and spread ramps below keep the plain constant, as
                // stock's GetWindUpDuration() - TF_MINIGUN_SPINUP_TIME does
                // (:1001, :1024).
                float spinUp = Parts.Hooks.ModifySpinUpTime(Context(in attack, script), SpinUpTime);
                weapon.NextPrimaryAttack = now + spinUp;
                weapon.NextSecondaryAttack = now + spinUp;
                weapon.TimeWeaponIdle = now + spinUp;
                break;

            case State.StartFiring:
                if (weapon.NextPrimaryAttack <= now)
                {
                    SetState(in attack, spinOnly ? State.Spinning : State.Firing);
                    weapon.NextSecondaryAttack = weapon.NextPrimaryAttack = weapon.TimeWeaponIdle = now + 0.1f;
                }
                break;

            case State.Firing:
                if (spinOnly)
                    SetState(in attack, State.Spinning);

                if (StateOf(weapon) == State.Spinning)
                {
                    weapon.NextSecondaryAttack = weapon.NextPrimaryAttack = weapon.TimeWeaponIdle = now + 0.1f;
                }
                else if (weapon.Reserve <= 0)
                {
                    SetState(in attack, State.DryFire);
                }
                else
                {
                    // m_flStartedFiringAt, folded into the wind-up start.
                    if (weapon.BehaviourMeter <= 0f)
                        weapon.BehaviourMeter = now - SpinUpTime;
                    Fire(in attack, script);
                    weapon.TimeWeaponIdle = now + 0.2f;
                }
                break;

            case State.DryFire:
                weapon.BehaviourMeter = 0f;
                if (weapon.Reserve > 0)
                    SetState(in attack, State.Firing);
                else if (spinOnly)
                    SetState(in attack, State.Spinning);
                break;

            case State.Spinning:
                if (!spinOnly)
                    SetState(in attack, weapon.Reserve > 0 ? State.Firing : State.DryFire);
                break;
        }

        // The ring of fire (stock's tail first, tf_weapon_minigun.cpp:438-448):
        // with a pool and the gun past its wind-up — firing, spinning or dry —
        // the item's plasma rings every command the clock allows.
        if (weapon.Reserve > 0 && StateOf(weapon) > State.StartFiring)
        {
            int ring = Parts.Hooks.RingOfFireDamage(Context(in attack, script), 0);
            if (ring != 0 && attack.EffectsLive)
                Parts.Effects.RingOfFire(in attack, ring);
        }

        // The spun gun's drain (stock's tail, tf_weapon_minigun.cpp:438-468):
        // with a pool and the gun spinning or firing, the item's rounds a
        // second accrue over the commands and cut whole rounds past one
        // (:456-464); the clock rode here from the last state change.
        if (weapon.Reserve > 0 && StateOf(weapon) is State.Firing or State.Spinning)
        {
            float perSecond = Parts.Hooks.AmmoDrainPerSecond(Context(in attack, script), 0f);
            if (perSecond > 0f)
            {
                weapon.BehaviourMeterB += perSecond * (now - weapon.BehaviourTimeC);
                weapon.BehaviourTimeC = now;

                if (weapon.BehaviourMeterB > 1f)
                {
                    int cut = (int)weapon.BehaviourMeterB;
                    weapon.Reserve = Math.Max(0, weapon.Reserve - cut);
                    weapon.BehaviourMeterB -= cut;
                }
            }
        }
    }

    /// <summary><c>CTFWeaponBaseGun::PrimaryAttack</c> for one minigun shot.</summary>
    private void Fire(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return;

        weapon.Reserve -= Math.Min(weapon.Reserve, Math.Max(1, script.Primary.AmmoPerShot));
        var shot = ModifyShot(in attack, default);
        float spread = TFWeaponTypes.SpreadDegrees(script.Primary.Spread * SpreadScale(SpinTime(weapon, attack.Now)));

        SendAnim(in attack, WeaponAnim.Primary);
        if (attack.EffectsLive)
            attack.Arms.FireBullets(attack.Type, Math.Max(1, script.Primary.BulletsPerShot), spread, shot);

        weapon.NextPrimaryAttack = attack.Now + FireDelay(in attack, script);
        Parts.Hooks.AfterFire(in context, in attack);

        // The item's projectile sweep (tf_weapon_minigun.cpp:374-391), on the
        // shot's own crit.
        int projectiles = Parts.Hooks.AttackEnemyProjectiles(in context, 0);
        if (projectiles > 0 && attack.EffectsLive)
            Parts.Effects.AttackEnemyProjectiles(in attack, projectiles, shot.Critical);
    }

    /// <summary>
    /// <c>CTFMinigun::SendWeaponAnim</c> (<c>tf_weapon_minigun.cpp:945-973</c>),
    /// the firing gate at :961-970: a send of the fire anim that finds it
    /// already playing is answered there — “If we're already playing the fire
    /// anim, let it continue. It loops.” Every other anim, and the first send
    /// of this one, reaches the base. The gate is what keeps the machine's
    /// per-shot <see cref="Fire"/> send from restarting the anim at fire rate:
    /// the restart frame is the pose that hangs the merged gun out of view for
    /// the whole burst. (The CLIENT_DLL arm of stock's override, :947-958,
    /// arms the barrel's spin velocity; the barrel's rotation rides its own
    /// deferred row.)
    /// </summary>
    /// <summary>
    /// Stock compares the ACTIVITIES: playing and sent both
    /// <c>ACT_VM_PRIMARYATTACK</c> (<c>tf_weapon_minigun.cpp:965</c>). The
    /// viewmodel's playing sequence is the one last sent, so matching the
    /// sent anim's mapped sequence against it says the same thing here: both
    /// sides of stock's comparison are the looping fire anim.
    /// </summary>
    public override bool AnimContinuesOnResend(in WeaponAttackContext attack, WeaponAnim anim) =>
        anim == WeaponAnim.Primary &&
        attack.ViewModel.Sequence == attack.Arms.LookupSequence(anim);

    protected override void SendAnim(in WeaponAttackContext attack, WeaponAnim anim)
    {
        if (AnimContinuesOnResend(in attack, anim))
            return;

        base.SendAnim(in attack, anim);
    }

    private void WindUp(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        SetState(in attack, State.StartFiring);
        if (Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions && attack.Command.OwnerIndex > 0)
            conditions.Add(TFCond.Aiming);
        if (weapon.BehaviourMeter <= 0f)
            weapon.BehaviourMeter = attack.Now;
    }

    private void WeaponIdle(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if (StateOf(weapon) == State.Idle)
            return;

        // The machine's idle animation runs first and, once the idle time has
        // passed, pushes it IdleRepeat (5 s) out; a spun minigun never sets its
        // own more than a second ahead, so an idle that far out passed this command.
        bool passed = attack.Now >= weapon.TimeWeaponIdle || weapon.TimeWeaponIdle - attack.Now > IdlePushedOut;
        if (!passed)
            return;

        // WindDown.
        SetState(in attack, State.Idle);
        ClearAiming(attack.Command.OwnerIndex);
        weapon.TimeWeaponIdle = attack.Now + 2f;
        weapon.BehaviourMeter = 0f;
    }
}
