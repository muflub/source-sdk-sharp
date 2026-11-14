//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// The Thermal Thruster (<c>CTFRocketPack</c>, <c>tf_weapon_rocketpack.cpp</c>):
/// the press arms a launch (<c>InitiateLaunch</c>, :210-250), the frame after the
/// press pops the owner off the ground (<c>PreLaunch</c>, :483-516), and at the
/// launch delay the packed flight force lands (<c>Launch</c>, :521-548, with
/// <c>CalcRocketForceFromPlayer</c> :384-448 and <c>RocketLaunchPlayer</c>
/// :453-478). The launch spends half the item meter (stock's 50 of 100, the
/// <c>tf_rocketpack_cost</c> convar :62), and a press below half the meter is
/// refused (<c>IsRocketPackReady</c>, tf_player_shared.h:477). A second launch
/// while the <c>TF_COND_ROCKETPACK</c> cond stands needs the item's
/// <c>thermal_thruster_air_launch</c> (:196-202); the landing ring of fire reads
/// the same cond after the flight (<see cref="TFDamageLayer"/>).
///
/// <para>
/// Stock's <c>ItemPostFrame</c> replaces its base outright (:752-841): the
/// thruster fires no shot of its own, so <see cref="BeforePrimaryAttack"/>
/// vetoes the machine's shot and the press runs the launch chain itself. The
/// two clocks merge into the secondary one here: stock's <c>m_flRefireTime</c>
/// (+0.5 in <c>Launch</c>, :544) is always inside the secondary clock's
/// +1.2 (<c>tf_rocketpack_refire_delay</c>, :820), so one clock gates both
/// presses with the same answer. The toggle's transition arms (:760-787) wait
/// for the deploy/holster seam, like every wearable's.
/// </para>
/// </summary>
public sealed class TFRocketPackBehaviour(TFWeaponParts parts, AttributeHooks? attributes = null)
    : TFWeaponBehaviour(parts, attributes)
{
    /// <summary><c>tf_rocketpack_launch_delay</c>'s default (:63): the wait
    /// between the pop and the packed force.</summary>
    public const float LaunchDelaySeconds = 0.65f;

    /// <summary><c>tf_rocketpack_refire_delay</c>'s default (:64): the press
    /// clock, stamped at the pop (:820).</summary>
    public const float RefireDelaySeconds = 1.2f;

    /// <summary>The hold clock a second press pays while one launch is armed (:230).</summary>
    public const float HoldClock = 0.1f;

    /// <summary>The pop off the ground, stock's impulse (:501).</summary>
    public const float PopSpeed = 350f;

    /// <summary>The flight force's base, <c>flForce</c> (:390).</summary>
    public const float LaunchSpeed = 450f;

    /// <summary><c>flPushScale</c>, the horizontal multiplier (:391).</summary>
    public const float PushScale = 1.8f;

    /// <summary>The vertical scale of a launch from the ground (:392).</summary>
    public const float GroundedLiftScale = 0.7f;

    /// <summary>The vertical scale of a launch in the air (:392): less up while airborne.</summary>
    public const float AirborneLiftScale = 0.25f;

    /// <summary>The ready half of the meter: stock's 50 of a 100-point bar (tf_player_shared.h:477).</summary>
    public const float ReadyCharge = 0.5f;

    /// <summary>The launch's cost against the meter, the <c>tf_rocketpack_cost</c> 50 of 100 (:62).</summary>
    public const float ChargeCost = 0.5f;

    /// <summary><c>m_bLaunchedFromGround</c>, taken at the pop (:817).</summary>
    public const int LaunchPendingBit = 1 << ModeKindShift;

    /// <summary>The ground flag the launch reads, stock's :817.</summary>
    public const int LaunchedFromGroundBit = 1 << (ModeKindShift + 1);

    /// <summary>The item's air-launch attribute (:199).</summary>
    public const string AirLaunchAttribute = "thermal_thruster_air_launch";

    /// <summary>The landing's shutdown sound, the ground arm's own
    /// (<c>Weapon_RocketPack.BoostersShutdown</c>, tf_player_shared.cpp:3148).</summary>
    public const string BoostersShutdownSound = "Weapon_RocketPack.BoostersShutdown";

    /// <summary>The landing's thud, the ground arm's other
    /// (<c>Weapon_RocketPack.Land</c>, tf_player_shared.cpp:3149).</summary>
    public const string LandSound = "Weapon_RocketPack.Land";

    /// <summary>Stock's launch direction is the eyes'; its pitch window is ±15° (:386).</summary>
    private const float PitchWindow = 0.2588190f; // sin 15°, the vertical part a level-enough aim can carry

    /// <summary>The thruster has no shot of its own: stock's own <c>ItemPostFrame</c>
    /// consumes the press (:827-830) and never reaches the base's gun fire.</summary>
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;

    /// <summary>The press edge, stock's own arms inside <c>ItemPostFrame</c>
    /// (:806-830): while one launch is armed a fresh jump cancels it (:808-814),
    /// else a fresh attack arms the next one (:827-830). The buttons' previous
    /// command is still in <see cref="WeaponNetState.LastButtons"/> here.</summary>
    public override void ItemPreFrame(in WeaponAttackContext attack, int buttons)
    {
        WeaponNetState weapon = attack.Weapon;
        int previous = weapon.LastButtons;

        if ((weapon.BehaviourMode & LaunchPendingBit) != 0)
        {
            if (IsFreshPress(buttons, previous, InputButtons.Jump))
                CancelArm(in attack);
            return;
        }

        if (IsFreshPress(buttons, previous, InputButtons.Attack)
            || IsFreshPress(buttons, previous, InputButtons.Attack2))
            Press(in attack);
    }

    /// <summary>Alt-fire is the same press (:827 reads both triggers).</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) => Press(in attack);

    /// <summary>The waiting arms (:806-826): the pop at the armed frame, then
    /// the flight at its delay.</summary>
    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        base.ItemPostFrame(in attack); // the meter's refill tail, the always-tail of every kind

        WeaponNetState weapon = attack.Weapon;
        if ((weapon.BehaviourMode & LaunchPendingBit) != 0)
        {
            Pop(in attack);
        }
        else if (weapon.BehaviourTimeA > 0f && weapon.BehaviourTimeA <= attack.Now)
        {
            Launch(in attack);
        }
    }

    /// <summary>The press gate, stock's <c>CanFire</c> (:156-205): the press
    /// clock (:193, its +0.5 twin :183 inside it), then the half-meter ready bar
    /// (:186), then the air-launch attribute behind the standing cond
    /// (:196-202); a press while armed only holds the clock (:228-231).</summary>
    private void Press(in WeaponAttackContext attack)
    {
        WeaponNetState weapon = attack.Weapon;
        float now = attack.Now;
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0)
            return;

        if (weapon.NextSecondaryAttack > now)
            return;

        if (weapon.ItemMeter < ReadyCharge)
            return;

        if (Parts.Owners.ConditionsOf(owner) is { } conditions
            && conditions.Has(TFCond.Rocketpack)
            && _attributes.Int(0, AirLaunchAttribute, Parts.Owners.ItemOf(owner, attack.Type.Name)) == 0)
            return;

        if ((weapon.BehaviourMode & LaunchPendingBit) != 0)
        {
            weapon.NextSecondaryAttack = now + HoldClock;
            return;
        }

        weapon.BehaviourMode |= LaunchPendingBit; // m_flInitLaunchTime = curtime (:247)
    }

    /// <summary>The cancel while armed (:811-813): the armed flag off, the
    /// hold clock the launch delay's length, the parachute off.</summary>
    private void CancelArm(in WeaponAttackContext attack)
    {
        WeaponNetState weapon = attack.Weapon;
        weapon.BehaviourMode &= ~LaunchPendingBit;
        weapon.NextSecondaryAttack = attack.Now + LaunchDelaySeconds;

        if (Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
            conditions.Remove(TFCond.ParachuteActive);
    }

    /// <summary>The pop (<c>PreLaunch</c> :483-516 with its armed-frame arms
    /// :817-820): the ground flag, the attack anim, the fall cancelled and the
    /// 350 up (:492-502), the parachute on (:503), then the flight deadline and
    /// the press clock.</summary>
    private void Pop(in WeaponAttackContext attack)
    {
        WeaponNetState weapon = attack.Weapon;
        float now = attack.Now;
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0)
            return;

        weapon.BehaviourMode &= ~LaunchPendingBit; // m_flInitLaunchTime = 0 (:819)

        // m_bLaunchedFromGround = FL_ONGROUND (:817)
        if (!Parts.Owners.OffGround(owner))
            weapon.BehaviourMode |= LaunchedFromGroundBit;
        else
            weapon.BehaviourMode &= ~LaunchedFromGroundBit;

        if (attack.EffectsLive)
            SendAnim(in attack, WeaponAnim.Primary); // ACT_VM_PRIMARYATTACK (:490)

        // Negate any fall, then the pop (:493-502): the predicted half.
        Vector popped = PopVelocity(attack.Arms.Carrier?.Velocity ?? Vector.Zero);
        if (attack.Arms.Carrier is { } carrier)
            carrier.Velocity = popped;
        Parts.PlayerData.ThrusterLaunch(owner, popped); // ApplyAbsVelocityImpulse (:502)

        if (Parts.Owners.ConditionsOf(owner) is { } conditions)
            conditions.Add(TFCond.ParachuteActive); // (:503)

        weapon.BehaviourTimeA = now + LaunchDelaySeconds;    // m_flLaunchTime (:513)
        weapon.NextSecondaryAttack = now + RefireDelaySeconds; // (:820)
    }

    /// <summary>The flight (<c>Launch</c> :521-548 with
    /// <c>RocketLaunchPlayer</c> :453-478): the deadline off and the parachute
    /// off (:529-530), the force from the eyes and the ground flag (:384-448),
    /// the cond and the stun under its guard (:455-463), the velocity itself
    /// (:465-477), half the meter spent (:547).</summary>
    private void Launch(in WeaponAttackContext attack)
    {
        WeaponNetState weapon = attack.Weapon;
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0)
            return;

        weapon.BehaviourTimeA = 0f; // m_flLaunchTime = 0 (:529)

        if (Parts.Owners.ConditionsOf(owner) is { } conditions)
        {
            conditions.Remove(TFCond.ParachuteActive); // (:530)

            // AddCond TF_COND_ROCKETPACK and the half-second movement stun, both
            // under the cond's absence guard (:455-461); the stun's server half is
            // the seam's, like the velocity's.
            if (!conditions.Has(TFCond.Rocketpack))
                conditions.Add(TFCond.Rocketpack);
        }

        if (!attack.Arms.Aim(out _, out Vector forward))
            return;

        bool launchedFromGround = (weapon.BehaviourMode & LaunchedFromGroundBit) != 0;
        Vector force = LaunchForce(forward, launchedFromGround);

        // The preserve-convars default to zeroing the carrier first (:465-475),
        // so the flight force is the whole velocity (:477).
        if (attack.Arms.Carrier is { } carrier)
            carrier.Velocity = force;
        Parts.PlayerData.ThrusterLaunch(owner, force); // SetAbsVelocity + ApplyAbsVelocityImpulse (:465-477)

        // charge -= cost, the meter's own clamp (:547 over the 0..100 clamp).
        weapon.ItemMeter = Math.Max(0f, weapon.ItemMeter - ChargeCost);
    }

    /// <summary>Whether this command's trigger is newly held (:827's
    /// <c>m_afButtonPressed</c>).</summary>
    private static bool IsFreshPress(int buttons, int previous, InputButtons button) =>
        ((int)button & buttons) != 0 && ((int)button & previous) == 0;

    /// <summary>The fall cancelled and the pop added (:493-502): no downward
    /// speed survives the launch's first half.</summary>
    public static Vector PopVelocity(Vector velocity) =>
        new(velocity.X, velocity.Y, (velocity.Z < 0f ? 0f : velocity.Z) + PopSpeed);

    /// <summary>The flight force (:384-448): along the eyes at 450 × 1.8 with
    /// the vertical from the ground scale — 0.7 from the ground, 0.25 in the
    /// air — and a grounded launch aiming down flattens its direction first
    /// (:425-431). The ground flag counts only with a near-level aim (:386).</summary>
    public static Vector LaunchForce(Vector forward, bool launchedFromGround)
    {
        bool grounded = launchedFromGround && Math.Abs(forward.Z) <= PitchWindow; // bOnGround (:386)

        Vector direction = forward;
        if (grounded && direction.Z < 0f)
            direction = new Vector(direction.X, direction.Y, 0f); // (:425-428)

        Vector push = direction.Normalized * LaunchSpeed * PushScale; // (:430)
        float liftScale = grounded ? GroundedLiftScale : AirborneLiftScale; // (:392)
        return new Vector(push.X, push.Y, push.Z + LaunchSpeed * liftScale); // (:431)
    }
}
