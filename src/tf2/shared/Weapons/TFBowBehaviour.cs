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

    /// <summary>The draw's pull, a sound of its own every bow plays (:76, :237).</summary>
    public const string PullSound = "Weapon_CompoundBow.SinglePull";

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

    /// <summary>
    /// <see cref="WeaponNetState.BehaviourMode"/>'s anim-round latch, stock's
    /// <c>m_bReloadedThroughAnimEvent</c>: the fire animation's increment round
    /// counted once (<c>tf_weapon_compound_bow.cpp:446-453</c>, cleared each
    /// fire at :171-173).
    /// </summary>
    public const int ModeIncremented = 1 << (ModeKindShift + 2);

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

        // The draw (:202-252): the begin, the wind, the condition.
        weapon.BehaviourMeter = attack.Now;
        SendAnim(in attack, WeaponAnim.Charge);
        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
            conditions.Add(TFCond.Aiming);

        // The tail of the draw (:212-248): the refire rate through the bow's
        // own speed chain — ApplyFireDelay(1.0) then ApplyRefireSpeedModifications,
        // both through ChargeWindow — inverted when there is one, stock's pull
        // pitch and playback rate both riding it. Both hands take the rate
        // through the one viewmodel rate this machine carries; the pull sounds
        // once through the command at the rate's pitch — EffectsLive being the
        // server's run and the client's first prediction, stock's
        // IsFirstTimePredicted behind the PAS-minus-shooter (:243-248). The
        // pull is its own named sound, not the script's launch (:237).
        float rate = ChargeWindow(in attack, script);
        if (rate > 0f)
            rate = 1f / rate;
        attack.ViewModel.PlaybackRate = rate;
        if (attack.EffectsLive)
            attack.Arms.Sound(PullSound, rate);

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
        var weapon = attack.Weapon;

        // The fire animation's increment round (:446-449), dispatched when the
        // sequence has played to its event: strictly after the due time, so the
        // launch's own tick leaves the refill for the next command and the
        // round falls while the shot's fire delay is still running.
        if (weapon.AnimEvent != 0 && attack.Now > weapon.AnimEventAt)
        {
            weapon.AnimEvent = 0;
            IncrementFromPool(in attack);
        }

        if (ScriptOf(attack.Type) is { } script)
        {
            // The loose (the pipebomb base's ItemPostFrame :167-170,
            // m_bNoAutoRelease making its force-release arm dead :171-178):
            // the trigger up with a round and a draw on.
            bool held = (weapon.LastButtons & (int)InputButtons.Attack) != 0;
            if (weapon.BehaviourMeter > 0f && !held && weapon.Clip > 0)
                Launch(in attack, script, Context(in attack, script));

            // Both buttons up, the draw again allowed (:457-460).
            if ((weapon.LastButtons & ((int)InputButtons.Attack | (int)InputButtons.Attack2)) == 0)
                weapon.BehaviourMode &= ~ModeNoFire;

            // The anim round spent and nothing left to fire, the hands take the
            // next best weapon (:446-453's switch) — before the frame's idle
            // asks, the way stock's returns first.
            if ((weapon.BehaviourMode & ModeIncremented) != 0
                && weapon.Clip <= 0 && weapon.Reserve <= 0
                && WeaponLogic.SwitchIfExhausted(attack.Arms, weapon, attack.Now, attack.EffectsLive, hasAnyAmmo: false))
                return;

            // Back to idle once the fire animation has played out under a
            // released trigger (:462-466): the primary's own sequence, finished,
            // hands the ask to the retag in SendAnim.
            bool finished = attack.Now >= weapon.SequenceEndsAt;
            if (finished
                && (weapon.LastButtons & ((int)InputButtons.Attack | (int)InputButtons.Attack2)) == 0
                && attack.ViewModel.Sequence == attack.Arms.LookupSequence(WeaponAnim.Primary))
                SendAnim(in attack, WeaponAnim.Idle);

            // And the full charge held on its animation holds the idle too
            // (:468-471) — the retag picks which idle it becomes (:401-427).
            if (finished && Drawn(weapon)
                && attack.Now - weapon.BehaviourMeter >= ChargeWindow(in attack, script))
                SendAnim(in attack, WeaponAnim.Idle);
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The drawn bow holds <c>TF_COND_AIMING</c>: the frame tail clears it off an emptied draw, the one job stock's launch (:165), lower (:348) and holster (:376) each do by hand.</summary>
    protected override bool HoldsAiming(in WeaponAttackContext attack) => Drawn(attack.Weapon);

    /// <summary>The launch. Stock's <c>LaunchGrenade</c> (:105-174) less the player-animation events (the anim layer's) and the stats call.</summary>
    private void Launch(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var weapon = attack.Weapon;
        float window = ChargeWindow(in attack, script);
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

        // The increment round rides the fire animation's AE_WPN_INCREMENTAMMO
        // (:446): one per fire, its event due after the anim's length; stock's
        // latch clears each fire (:171-173) so the round counts again next
        // shell. Both sides write it alike — the anim state is the replay's too.
        weapon.AnimEvent = EventCatalog.IndexForName("AE_WPN_INCREMENTAMMO");
        weapon.AnimEventAt = attack.Now + attack.Arms.AnimDuration(WeaponAnim.Primary);
        weapon.BehaviourMode &= ~ModeIncremented;

        PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge);

        // The fire delay is the script's 2 s through the chain (:142-143), the
        // bow's speed arms on top (:145), and the idle half the rate again
        // (:162-163): rate = base / delay.
        float delay = ApplyRefireSpeed(in attack, script, FireDelay(in attack, script));
        weapon.NextPrimaryAttack = attack.Now + delay;
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack
            + (delay > 0f ? 0.5f * script.Primary.TimeFireDelay / delay : 0f);
        Parts.Hooks.AfterFire(in context, in attack);
    }

    /// <summary>
    /// The draw's window: one second through the fire-delay chain then the
    /// bow's speed arms — <c>GetChargeMaxTime</c> takes the same chain the fire
    /// delay's refire speed does (:212, :272-273, :508).
    /// </summary>
    private float ChargeWindow(in WeaponAttackContext attack, TFWeaponScript script) =>
        ApplyRefireSpeed(in attack, script,
                         ModifyFireInterval(attack.Type, ChargeTime, attack.Command.OwnerIndex));

    /// <summary>
    /// <c>ApplyRefireSpeedModifications</c> (:508): the item's
    /// <c>fast_reload</c> over the value; the master sniper's remap or the
    /// haste rune's cut on top of that, whichever the owner's carries.
    /// </summary>
    private float ApplyRefireSpeed(in WeaponAttackContext attack, TFWeaponScript script, float rate)
    {
        var context = Context(in attack, script);
        rate = _attributes.Float(rate, "fast_reload", context.Item);

        int master = _attributes.Int(0, "ability_master_sniper", context.Item);
        if (master != 0)
            return MasterSniperSpeed(master);

        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex)?.Has(TFCond.RuneHaste) == true)
            rate *= 0.4f;
        return rate;
    }

    /// <summary>Stock's <c>RemapValClamped(iMaster, 1, 2, 0.6f, 0.3f)</c> (:508): the master sniper's own speed.</summary>
    private static float MasterSniperSpeed(int level) =>
        0.6f + (Math.Clamp(level, 1, 2) - 1) * (0.3f - 0.6f);

    /// <summary>
    /// <c>IncrementAmmo</c> behind the fire animation's event (:446-453): once
    /// per fire the latch holds it, one round climbs from the pool into the
    /// clip — stock's <c>m_bReloadedThroughAnimEvent</c>, cleared each fire
    /// (:171-173) and set here on the mode bits.
    /// </summary>
    private void IncrementFromPool(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;

        if ((weapon.BehaviourMode & ModeIncremented) != 0)
            return;
        weapon.BehaviourMode |= ModeIncremented;

        int clipMax = ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex);
        if (clipMax <= 0 || weapon.Clip >= clipMax || weapon.Reserve <= 0)
            return;

        weapon.Clip += 1;
        weapon.Reserve -= 1;
    }

    /// <summary>
    /// Stock's <c>SendWeaponAnim</c> retag (:396-427): while a draw is on, an
    /// idle ask becomes the bow's own idle — inside the tired window the plain
    /// held one (<see cref="WeaponAnim.IdleBow"/>), past it the pair that
    /// alternates in until it settles on <see cref="WeaponAnim.IdleBowFull"/>
    /// (:401-427). Then the sequence's end is tracked, so the frame's finished
    /// checks (:462, :469) read it.
    /// </summary>
    protected override void SendAnim(in WeaponAttackContext attack, WeaponAnim anim)
    {
        var weapon = attack.Weapon;

        if (anim == WeaponAnim.Idle && Drawn(weapon))
        {
            anim = attack.Now - weapon.BehaviourMeter >= TiredAfter
                ? attack.ViewModel.Sequence == attack.Arms.LookupSequence(WeaponAnim.IdleBowFull)
                  || attack.ViewModel.Sequence == attack.Arms.LookupSequence(WeaponAnim.IdleBowChargeFull)
                    ? WeaponAnim.IdleBowFull
                    : WeaponAnim.IdleBowChargeFull
                : WeaponAnim.IdleBow;
        }

        base.SendAnim(in attack, anim);

        weapon.SequenceEndsAt = attack.Now + attack.Arms.AnimDuration(anim);
    }

    /// <summary><c>CalcIsAttackCriticalHelper</c> (:631-640): the boosted crit, nothing else rolls.</summary>
    protected override TFCritOwner KindCritPolicy(in WeaponAttackContext attack, TFCritOwner policy) =>
        policy with { CanFireCriticalShot = policy.CanFireCriticalShot && policy.CritBoosted };
}
