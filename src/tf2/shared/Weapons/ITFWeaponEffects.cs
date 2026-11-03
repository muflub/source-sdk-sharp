using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The server's arms for what a TF weapon does that the core's arms have no
/// door for: launching a TF projectile (Stage 2.8), a flamethrower's flames
/// (2.11), a medigun's beam (2.9) and a sniper rifle's zoom (the player's
/// field of view). The shared behaviour decides every number, spends the
/// ammunition and rolls the crit on both sides; it calls these only when
/// <see cref="WeaponAttackContext.EffectsLive"/>, and only the server's
/// implementation acts. A single owner per side, registered
/// <c>As = [typeof(ITFWeaponEffects)]</c>; none is <see cref="NoTFWeaponEffects"/>.
/// </summary>
public interface ITFWeaponEffects
{
    /// <summary>
    /// Launch the mode's projectile (<see cref="TFWeaponModeData.Projectile"/>) at
    /// <paramref name="shot"/>'s damage and crit; <paramref name="charge"/> is the
    /// seconds a stickybomb was charged (its launch speed), 0 for the rest.
    /// </summary>
    void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge);

    /// <summary>The stickybomb launcher's secondary: detonate this owner's stickybombs.</summary>
    void Detonate(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>One flamethrower tick's flames at <paramref name="shot"/>'s damage and crit.</summary>
    void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot);

    /// <summary>The flamethrower's airblast: the deflect, the push and the extinguish.</summary>
    void AirBlast(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>The medigun's beam this command: held or let go.</summary>
    void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held);

    /// <summary>The sniper rifle's scope went in or out.</summary>
    void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed);

    /// <summary>
    /// <c>CTFPlayer::DoClassSpecialSkill</c>
    /// (<c>tf_player_shared.cpp:12330-12403</c>): the class's special a
    /// weapon's secondary reaches — the spy's cloak toggle and the demoman's
    /// sticky detonation. Answers whether one ran.
    /// </summary>
    bool ClassSpecialSkill(in WeaponAttackContext attack);

    /// <summary>
    /// The PDA's trigger: the owner goes back to the item held before this
    /// (<c>CTFWeaponPDA::PrimaryAttack</c>'s <c>SelectLastItem</c>,
    /// <c>shared/tf/tf_weapon_pda.cpp:97</c> over
    /// <c>CBasePlayer::SelectLastItem</c>, <c>shared/baseplayer_shared.cpp:862-871</c>
    /// — the core's <c>Arsenal.SwitchLast</c> is the same walk).
    /// </summary>
    void SelectLast(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>
    /// The melee smack: the swing's trace and, on a hit, the hit itself —
    /// stock's <c>CTFWeaponBaseMelee::Smack</c> → <c>DoSwingTraceInternal</c>
    /// → <c>OnSwingHit</c> → <c>DoMeleeDamage</c>
    /// (<c>shared/tf/tf_weaponbase_melee.cpp</c>), at <paramref name="shot"/>'s
    /// damage and crit flag, over <paramref name="swing"/>'s trace.
    /// </summary>
    void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace);

    /// <summary>
    /// The minigun's projectile sweep
    /// (<c>CTFMinigun::AttackEnemyProjectiles</c>,
    /// <c>shared/tf/tf_weapon_minigun.cpp:564-654</c>), asked on each fired
    /// shot the <c>attack_projectiles</c> item arms — stock re-reads the
    /// attribute per victim, :628, and the value is the same. <paramref name="projectiles"/>
    /// is it; <paramref name="critical"/> the shot's own crit, whose
    /// <c>m_bCritShot</c> halves the hits a kill needs (:629).
    /// </summary>
    void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical);

    /// <summary>
    /// The minigun's ring of fire (<c>CTFMinigun::RingOfFireAttack</c>,
    /// <c>shared/tf/tf_weapon_minigun.cpp:498-558</c>), asked on every command
    /// the <c>ring_of_fire_while_aiming</c> item arms while the gun is spun
    /// past its wind-up (:440-448); <paramref name="damage"/> is the item's
    /// value, the plasma each ringed enemy takes.
    /// </summary>
    void RingOfFire(in WeaponAttackContext attack, int damage);

    /// <summary>
    /// The spun clock's reset (<c>SetWeaponState</c>, :475-480): leaving the
    /// unspun states also rings <c>m_flNextRingOfFireAttackTime</c> half a
    /// second ahead, beside the drain clock it keeps.
    /// </summary>
    void Rearmed(in WeaponAttackContext attack);
}

/// <summary>
/// The swing <see cref="ITFWeaponEffects.Melee"/> traces: the reach and the
/// hull box, what the behaviour decided for this smack — stock's
/// <c>DoSwingTraceInternal</c> reads them off the weapon
/// (<c>tf_weaponbase_melee.cpp:392-425</c>) — and whether the hit asks the
/// <c>crit_from_behind</c> arm (:819-833).
/// </summary>
public readonly record struct SwingTrace(float Range, float Hull, bool CritFromBehind);

/// <summary>No effects: what a side without Stage 2's arms does (the shot is spent and nothing appears).</summary>
public sealed class NoTFWeaponEffects : ITFWeaponEffects
{
    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) { }
    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) { }
    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }
    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) { }
    public bool ClassSpecialSkill(in WeaponAttackContext attack) => false;
    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) { }
    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) { }
    public void RingOfFire(in WeaponAttackContext attack, int damage) { }
    public void Rearmed(in WeaponAttackContext attack) { }
}
