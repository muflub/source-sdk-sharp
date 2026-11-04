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
/// The compound bow (the Huntsman): <c>CTFCompoundBow</c>'s draw and loose
/// (<c>shared/tf/tf_weapon_compound_bow.cpp</c>). The trigger held starts the
/// draw (<c>PrimaryAttack</c> :202-253): the charge's begin is
/// <see cref="WeaponNetState.BehaviourMeter"/>, the viewmodel winds
/// (<see cref="WeaponAnim.Charge"/>, the PULLBACK ask stock's
/// <c>SendWeaponAnim</c> retags :396-399) and the owner takes
/// <c>TF_COND_AIMING</c> (:251) — the drawn bow's 160
/// (<see cref="TFMaxSpeed.BowAiming"/>, the condition's arm). Nothing fires
/// while held: stock's <c>m_bNoAutoRelease</c> leaves the pipebomb base's
/// force-release dead, the draw runs past its charge window and the tired
/// shot is the release's. The trigger up looses
/// (<c>CTFPipebombLauncher::ItemPostFrame</c> :167-170, the base the bow
/// keeps): one arrow at the charge — <see cref="TFRadiusDamage"/>'s damage,
/// speed and gravity — to the fire delay the script's 2 s through the chain
/// (<c>LaunchGrenade</c> :142-143, the idle half the rate again :162-163).
///
/// <para>
/// The secondary lowers (<c>SecondaryAttack</c> :330 → <c>LowerBow</c>
/// :338-358): the charge dropped, <c>TF_COND_AIMING</c> with it in the frame
/// tail, the next draw a second away and <c>m_bNoFire</c> set — the bit that
/// holds the draw off until both buttons are up (<c>ItemPostFrame</c>
/// :457-460). A refused attack empties the draw the same way
/// (<c>PrimaryAttack</c> :196-200).
/// </para>
///
/// <para>
/// A draw five seconds old fires wild (<c>GetProjectileFireSetup</c>
/// :490-503, <c>TF_ARROW_MAX_CHARGE_TIME</c>): the tired launch is marked on
/// the weapon (<see cref="ModeTired"/>) for the arrow's spawn to jitter the
/// aim ±6° in pitch and yaw — <see cref="TFProjectileEffects"/>' arrow case,
/// the draws seeded as every launch's are.
/// </para>
///
/// <para>
/// The crit is the boosted only (<c>CalcIsAttackCriticalHelper</c> :631-640)
/// and the muzzle flash stock's false (<c>ShouldDoMuzzleFlash</c>,
/// tf_weapon_compound_bow.h:73) — the launch asks neither. The bow's own
/// reload, the fire animation's AE_WPN_INCREMENTAMMO round, and the
/// charge-idle pair wait in docs/plans/tf2/deferred.tsv.
/// </para>
/// </summary>
public class TFBowBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>The draw's charge window: <c>GetChargeMaxTime</c>'s ApplyFireDelay(1.0) (:269-276).</summary>
    public const float ChargeTime = 1f;

    /// <summary>
    /// <c>TF_ARROW_MAX_CHARGE_TIME</c>: the draw's age past which the shot is
    /// the tired one — also <c>GetChargeForceReleaseTime</c>'s 5
    /// (tf_weapon_compound_bow.h:71), dead under <c>m_bNoAutoRelease</c>.
    /// </summary>
    public const float TiredAfter = 5f;

    /// <summary>The tired shot's aim jitter, each way of pitch and yaw (:499, :501).</summary>
    public const float TiredAim = 6f;

    /// <summary><see cref="WeaponNetState.BehaviourMode"/>'s <c>m_bNoFire</c> (networked in stock, tf_weapon_compound_bow.h:104).</summary>
    public const int ModeNoFire = 1 << ModeKindShift;

    /// <summary><see cref="WeaponNetState.BehaviourMode"/>'s tired-launch latch, the arrow spawn's jitter bit.</summary>
    public const int ModeTired = 1 << (ModeKindShift + 1);

    /// <summary>Whether the last launch was a tired one.</summary>
    public static bool Tired(WeaponNetState weapon) => (weapon.BehaviourMode & ModeTired) != 0;

    /// <summary>Whether an arrow is nocked: the charge on the meter, as <c>LowerBow</c>'s door (:340) and <c>OwnerCanJump</c>'s (:659) read it.</summary>
    public static bool Drawn(WeaponNetState weapon) => weapon.BehaviourMeter > 0f;

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;

        // m_bNoFire holds the draw off till both buttons are up (:193-194).
        if ((weapon.BehaviourMode & ModeNoFire) != 0)
            return false;

        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
        {
            // The refused attack empties the draw (:196-200); the condition
            // follows the meter down in the frame tail.
            weapon.BehaviourMeter = 0f;
            return false;
        }

        // Drawn already, the held trigger is the else of :254-263 — the charge
        // clamped at its use, nothing to do.
        if (weapon.BehaviourMeter > 0f)
            return false;

        // The draw (:202-252): the begin, the wind, the condition. The pull's
        // sound — pitch by the charge window the rate arms turn — waits in
        // docs/plans/tf2/deferred.tsv with the emitter's pitch.
        weapon.BehaviourMeter = attack.Now;
        SendAnim(in attack, WeaponAnim.Charge);
        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
            conditions.Add(TFCond.Aiming);

        return false;
    }

    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;

        // LowerBow's no-arrow door (:340-341).
        if (weapon.BehaviourMeter <= 0f)
            return;

        // LowerBow (:343-357): the charge gone — the frame tail takes
        // TF_COND_AIMING with it — the next draw a second off (:352) and
        // m_bNoFire till both buttons are up (:354, :457-460); the dry fire is
        // the role's ACT_ITEM2_VM_DRYFIRE (:357, the table's row).
        weapon.BehaviourMeter = 0f;
        weapon.BehaviourMode |= ModeNoFire;
        weapon.NextPrimaryAttack = attack.Now + 1f;
        SendAnim(in attack, WeaponAnim.DryFire);
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            var weapon = attack.Weapon;

            // The loose (the pipebomb base's ItemPostFrame :167-170,
            // m_bNoAutoRelease making its force-release arm dead :171-178):
            // the trigger up with a round and a draw on.
            bool held = (weapon.LastButtons & (int)InputButtons.Attack) != 0;
            if (weapon.BehaviourMeter > 0f && !held && weapon.Clip > 0)
                Launch(in attack, script, Context(in attack, script));

            // Both buttons up, the draw again allowed (:457-460).
            if ((weapon.LastButtons & ((int)InputButtons.Attack | (int)InputButtons.Attack2)) == 0)
                weapon.BehaviourMode &= ~ModeNoFire;
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The drawn bow holds <c>TF_COND_AIMING</c>: the frame tail clears it off an emptied draw, the one job stock's launch (:165), lower (:348) and holster (:376) each do by hand.</summary>
    protected override bool HoldsAiming(in WeaponAttackContext attack) => Drawn(attack.Weapon);

    /// <summary>The launch. Stock's <c>LaunchGrenade</c> (:105-174) less the player-animation events (the anim layer's) and the stats call.</summary>
    private void Launch(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var weapon = attack.Weapon;
        float window = ChargeWindow(in attack);
        float drawn = attack.Now - weapon.BehaviourMeter;

        // The charge as stock's GetCurrentCharge clamps it (:281-287), and the
        // tired mark the arrow's spawn reads (:494-502).
        float charge = MathF.Min(drawn, window);
        weapon.BehaviourMeter = 0f;
        weapon.BehaviourMode = drawn >= TiredAfter ? weapon.BehaviourMode | ModeTired
                                                   : weapon.BehaviourMode & ~ModeTired;

        var shot = ModifyShot(in attack, default);

        // The round, as the pipebomb base's launch spends it (the sticky's
        // shape): the clip's one, else the pool's.
        int cost = Math.Max(1, script.Primary.AmmoPerShot);
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

        SendAnim(in attack, WeaponAnim.Primary);
        PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge);

        // The fire delay is the script's 2 s through the chain (:142-143) and
        // the idle half the rate again (:162-163): rate = base / delay.
        float delay = FireDelay(in attack, script);
        weapon.NextPrimaryAttack = attack.Now + delay;
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack
            + (delay > 0f ? 0.5f * script.Primary.TimeFireDelay / delay : 0f);
        Parts.Hooks.AfterFire(in context, in attack);
    }

    /// <summary>The draw's window: one second through the fire-delay arms, as <c>GetChargeMaxTime</c> takes the same chain the delay does (:272-273).</summary>
    private float ChargeWindow(in WeaponAttackContext attack) =>
        ModifyFireInterval(attack.Type, ChargeTime, attack.Command.OwnerIndex);

    /// <summary><c>CalcIsAttackCriticalHelper</c> (:631-640): the boosted crit, nothing else rolls.</summary>
    protected override TFCritOwner KindCritPolicy(in WeaponAttackContext attack, TFCritOwner policy) =>
        policy with { CanFireCriticalShot = policy.CanFireCriticalShot && policy.CritBoosted };
}
