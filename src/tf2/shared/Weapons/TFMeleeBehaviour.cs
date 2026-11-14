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

    /// <summary>
    /// The swing in the air is the charge's mini-crit (<c>m_bMiniCrit</c>,
    /// tf_weaponbase_melee.cpp:208-215): the grant was a mini-crit, so the
    /// smack's hit flags <c>DMG_RADIUS_MAX</c> and the damage rules mini-crit
    /// it — never overriding a roll the swing's own helpers made.
    /// </summary>
    public const int ModeSwingMini = 4 << ModeKindShift;

    /// <summary>
    /// The Gunslinger's punch combo, the count the smacks keep in these two
    /// mode bits (<c>CTFRobotArm</c>'s <c>m_iComboCount</c>,
    /// tf_weapon_wrench.cpp:498-518): the enemy players the last swings hit,
    /// zeroed by any swing that met anything else and by a combo older than
    /// <see cref="ComboTimeout"/>. Its clock — the last counted hit — is
    /// <see cref="WeaponNetState.BehaviourTimeB"/>, the melee's idle B; the
    /// bits ride the mode word's clear at the deploy, where stock's member
    /// survives one, the count a fresh arm starts again.
    /// </summary>
    public const int ComboCountMask = 0x18 << ModeKindShift;

    /// <summary><c>ROBOARM_COMBO_TIMEOUT</c> (tf_weapon_wrench.cpp:32).</summary>
    public const float ComboTimeout = 1f;

    /// <summary>The count bits' low end, over the kind's own three.</summary>
    public const int ComboCountShift = ModeKindShift + 3;

    /// <summary>The count in the mode word, 0..2 once the third's own hit reset it.</summary>
    public static int ComboOf(WeaponNetState weapon) => (weapon.BehaviourMode & ComboCountMask) >> ComboCountShift;

    /// <summary>The Gunslinger's row, the wrench family's arm — stock's
    /// <c>CTFRobotArm</c> under <c>CTFWrench</c> (tf_weapon_wrench.h:66), whose
    /// id answer is the wrench's, so the row's name is the test.</summary>
    public static bool IsRobotArm(in TFWeaponScript script) => IsRobotArmName(script.Name);

    /// <summary>The row-name test over any spelling of it.</summary>
    public static bool IsRobotArmName(string name) =>
        string.Equals(name, TFWeaponTypes.WrenchArm, StringComparison.Ordinal);

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

        // CTFRobotArm::PrimaryAttack's two arms (tf_weapon_wrench.cpp:468-485),
        // ahead of the roll: a combo older than its second is no combo, and
        // the third swing grants the full melee crit the roll below spends —
        // the count the smack's hit kept (<see cref="TF2.Server.Weapons.TFMeleeEffects"/>).
        if (IsRobotArm(in script))
        {
            var combo = attack.Weapon;
            if (attack.Now - combo.BehaviourTimeB > ComboTimeout)
                combo.BehaviourMode &= ~ComboCountMask;

            if (ComboOf(combo) == 2
                && Parts.PlayerData?.Predicted(attack.Command.OwnerIndex) is { } grant)
                grant.NextMeleeCrit = (int)TFMeleeCrit.Crit;
        }

        // The swing ends the class's special first (tf_weaponbase_melee.cpp
        // :201): the charged demoman's shield grants its crit for THIS swing's
        // helpers, the roll below the first reader.
        Parts.Effects.EndClassSpecialSkill(in attack);

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
        // The third-person swing, hit or miss, at its start (Smash's
        // DoAnimationEvent, tf_weaponbase_melee.cpp:348-351).
        attack.Arms.SwingShown();
        if (attack.EffectsLive && Parts.Hooks.HitSelfOnMiss(in context))
            Parts.Effects.MeleeSwingStarted(in attack, Parts.Hooks.ModifyMeleeRange(in context, attack.Type.MeleeRange));

        PlaySound(in attack, script, crit ? WeaponScriptSound.Burst : WeaponScriptSound.MeleeMiss);

        // The mini latch (tf_weaponbase_melee.cpp:208-215): the grant the
        // swing's end made, read after it — the smack's bit.
        bool mini = (Parts.PlayerData?.Predicted(attack.Command.OwnerIndex)?.NextMeleeCrit
                     ?? (int)TFMeleeCrit.NoCrit) == (int)TFMeleeCrit.MiniCrit;

        weapon.BehaviourMode = (weapon.BehaviourMode & ~(ModeSwinging | ModeSwingCrit | ModeSwingMini))
                             | ModeSwinging | (crit ? ModeSwingCrit : 0) | (mini ? ModeSwingMini : 0);
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

    /// <summary>
    /// <c>ItemPreFrame</c> (<c>tf_weaponbase_melee.cpp:354-368</c>): the held
    /// fists' <c>self_mark_for_death</c> seconds ride onto the owner every
    /// command — stock's <c>AddCond(TF_COND_MARKEDFORDEATH_SILENT, iSelfMark)</c>
    /// refreshed on each ask; <c>Holster</c>'s twin (:153-158) marks when the
    /// fists go away, same number, same store. Hook semantics: the chain's
    /// ask starts at zero like stock's <c>CALL_ATTRIB_HOOK_INT</c>.
    /// </summary>
    public override void ItemPreFrame(in WeaponAttackContext attack, int buttons)
    {
        if (attack.EffectsLive && attack.Command.OwnerIndex > 0 && ScriptOf(attack.Type) is { } script)
        {
            int seconds = Parts.Hooks.SelfMarkForDeath(Context(in attack, script), 0);
            if (seconds != 0 && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
                conditions.Add(TFCond.MarkedfordeathSilent, seconds);
        }

        base.ItemPreFrame(in attack, buttons);
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if ((weapon.BehaviourMode & ModeSwinging) != 0 && attack.Now > weapon.BehaviourTimeA
            && ScriptOf(attack.Type) is { } script)
        {
            bool crit = (weapon.BehaviourMode & ModeSwingCrit) != 0;
            bool mini = (weapon.BehaviourMode & ModeSwingMini) != 0;
            weapon.BehaviourMode &= ~(ModeSwinging | ModeSwingCrit | ModeSwingMini);

            var context = Context(in attack, script);
            float damage = Parts.Hooks.ModifyDamage(in context, script.Primary.Damage);

            // crit_does_no_damage (tf_weaponbase_melee.cpp:989-1002): with the
            // held item's arm set, a crit blow lands with nothing behind it —
            // stock's silent crit inside GetMeleeDamage, right after the
            // mult_dmg scale (:987). The crit bit of the swing or its type is
            // the gate; a mini's DMG_RADIUS_MAX is not DMG_CRITICAL, so the
            // charge's mini keeps its damage.
            if (crit && Parts.Hooks.CritDoesNoDamage(in context))
                damage = 0f;
            // The swing DoSwingTraceInternal traces: the reach and the hull,
            // each through the chain (tf_weaponbase_melee.cpp:399, :420), and
            // whether the hit asks the behind-crit arm (:819-820) or a clean
            // miss hurts its own owner (:755-763). The two laugh arms of the
            // Holiday Punch ride along too (:884-927), their crit and item gates
            // answered at the hit where stock asks them.
            var trace = new SwingTrace(Parts.Hooks.ModifyMeleeRange(in context, attack.Type.MeleeRange),
                                       Parts.Hooks.ModifyMeleeBounds(in context, SwingHull),
                                       Parts.Hooks.CritFromBehind(in context),
                                       Parts.Hooks.HitSelfOnMiss(in context),
                                       Parts.Hooks.CritForcesVictimToLaugh(in context),
                                       Parts.Hooks.TickleEnemiesWieldingSameWeapon(in context));
            if (attack.EffectsLive)
            {
                // The melee's sapper arm (tf_weaponbase_melee.cpp:469-494): a held
                // weapon bearing set_dmg_apply_to_sapper lets the swing reach the
                // sapper riding a friendly building, at the swing's own blow.
                if (Parts.Hooks.DmgApplyToSapper(in context) && Parts.Sapper is { } state)
                    state.MeleeSmack(attack.Command.OwnerIndex, trace.Range, damage);

                // The crit rolls over the mini (:855-863): the full crit's bit,
                // else the charge's mini-crit as DMG_RADIUS_MAX.
                Parts.Effects.Melee(in attack, script,
                                    crit ? Critical(damage)
                                         : mini ? new ShotDamage(damage, Mini: true)
                                                : new ShotDamage(damage),
                                    in trace);
            }

            // The smack spent the grant (tf_weaponbase_melee.cpp:384), hit or
            // miss — the next swing rolls plain unless the charge ends again.
            if (Parts.PlayerData?.Predicted(attack.Command.OwnerIndex) is { } spent)
                spent.NextMeleeCrit = (int)TFMeleeCrit.NoCrit;
        }

        base.ItemPostFrame(in attack);
    }
}
