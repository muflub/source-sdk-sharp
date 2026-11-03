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
/// Every TF melee weapon (bat, bottle, fire axe, kukri, knife, fists, shovel,
/// wrench, bonesaw): <c>CTFWeaponBaseMelee</c>'s swing and smack
/// (<c>shared/tf/tf_weaponbase_melee.cpp</c>).
///
/// <para>
/// THE SWING (<c>PrimaryAttack</c> → <c>Swing</c>, :187-323) rolls the crit at
/// once (the melee helpers: 15%, the melee seed mask), sets the next attack a
/// fire delay away and the idle after that plus <c>TimeIdleEmpty</c>, plays
/// the swing animation and the whoosh (<c>BURST</c> for a crit, else
/// <c>MELEE_MISS</c>), and arms the smack <c>SmackDelay</c> later
/// (<c>GetSmackTime</c>, :792-795). The core's immediate swing is vetoed: the
/// behaviour runs the swing itself.
/// </para>
/// <para>
/// THE SMACK (<c>ItemPostFrame</c>, :374-389, strictly after the smack time)
/// is the hit: the TF effects' melee arm at the script's damage through the
/// chain, flagged (not tripled: the damage rules' job) when the swing rolled
/// a crit. The trace is <c>DoSwingTraceInternal</c>'s — the line, then the
/// 18-unit hull.
/// </para>
///
/// <para>
/// Its predicted state: <see cref="WeaponNetState.BehaviourMode"/> bits
/// <see cref="ModeSwinging"/> and <see cref="ModeSwingCrit"/>, and
/// <see cref="WeaponNetState.BehaviourTimeA"/>, the smack time.
/// </para>
/// </summary>
public class TFMeleeBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>A swing is in the air: its smack is due at <see cref="WeaponNetState.BehaviourTimeA"/>.</summary>
    public const int ModeSwinging = 1 << ModeKindShift;

    /// <summary>The swing in the air rolled a crit.</summary>
    public const int ModeSwingCrit = 2 << ModeKindShift;

    /// <summary><c>DoSwingTraceInternal</>'s swing box, 18 to each side (tf_weaponbase_melee.cpp:394-396).</summary>
    public const float SwingHull = 18f;

    protected override bool RollsAsMelee => true;

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return false;

        Swing(in attack, script, in context, secondary: false);
        Parts.Hooks.AfterFire(in context, in attack);

        // The machine's own swing would hit now: the smack is ours.
        return false;
    }

    /// <summary>
    /// <c>CTFFists::SecondaryAttack</c> (<c>tf_weapon_fists.cpp:74-96</c>):
    /// the heavy's right click is the second fist — the same Punch through the
    /// same swing, the weapon mode on the other side of the anim hook
    /// (:164), the clocks <c>Punch</c> arms from the primary's (:119). Every
    /// other melee keeps the base's debounced class skill.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script || script.Id != TFWeaponId.Fists)
        {
            base.SecondaryAttack(in attack);
            return;
        }

        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return;

        Swing(in attack, script, in context, secondary: true);
        Parts.Hooks.AfterFire(in context, in attack);
    }

    private void Swing(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context,
                       bool secondary)
    {
        var weapon = attack.Weapon;
        bool crit = RollCrit(in attack, script, in context);

        float delay = FireDelay(in attack, script);
        weapon.NextPrimaryAttack = attack.Now + delay;
        weapon.NextSecondaryAttack = attack.Now + delay;
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack + script.Primary.TimeIdleEmpty;

        SendAnim(in attack, SwingAnim(script, crit, secondary));
        PlaySound(in attack, script, crit ? WeaponScriptSound.Burst : WeaponScriptSound.MeleeMiss);

        weapon.BehaviourMode = (weapon.BehaviourMode & ~(ModeSwinging | ModeSwingCrit))
                             | ModeSwinging | (crit ? ModeSwingCrit : 0);
        weapon.BehaviourTimeA = attack.Now + script.Primary.SmackDelay;
    }

    /// <summary>
    /// The swing's viewmodel anim, the base's <c>DoViewModelAnimation</c>
    /// (<c>tf_weaponbase_melee.cpp:328-342</c>): the crit's SWINGHARD, else
    /// the plain HITCENTER — and the fists' override
    /// (<c>tf_weapon_fists.cpp:154-178</c>) answering by the weapon mode
    /// instead, the left hook or the right.
    /// </summary>
    private static WeaponAnim SwingAnim(TFWeaponScript script, bool crit, bool secondary)
    {
        if (crit)
            return WeaponAnim.SwingHard;

        if (script.Id == TFWeaponId.Fists)
            return secondary ? WeaponAnim.HitRight : WeaponAnim.HitLeft;

        return WeaponAnim.HitCenter;
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if ((weapon.BehaviourMode & ModeSwinging) != 0 && attack.Now > weapon.BehaviourTimeA
            && ScriptOf(attack.Type) is { } script)
        {
            bool crit = (weapon.BehaviourMode & ModeSwingCrit) != 0;
            weapon.BehaviourMode &= ~(ModeSwinging | ModeSwingCrit);

            var context = Context(in attack, script);
            float damage = Parts.Hooks.ModifyDamage(in context, script.Primary.Damage);
            // The swing DoSwingTraceInternal traces: the reach and the hull,
            // each through the chain (tf_weaponbase_melee.cpp:399, :420), and
            // whether the hit asks the behind-crit arm (:819-820).
            var trace = new SwingTrace(Parts.Hooks.ModifyMeleeRange(in context, attack.Type.MeleeRange),
                                       Parts.Hooks.ModifyMeleeBounds(in context, SwingHull),
                                       Parts.Hooks.CritFromBehind(in context));
            if (attack.EffectsLive)
                Parts.Effects.Melee(in attack, script, crit ? Critical(damage) : new ShotDamage(damage), in trace);
        }

        base.ItemPostFrame(in attack);
    }
}
