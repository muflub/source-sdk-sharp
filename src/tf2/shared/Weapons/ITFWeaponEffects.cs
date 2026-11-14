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

    /// <summary>
    /// The Manmelter's vacuum (the secondary's beam): the reached burning
    /// teammate put out, the revenge crit banked and the heal paid. Answers
    /// whether a burn went out — the beam's clock stamps on one alone
    /// (<c>m_fLastExtinguishTime</c>, <c>tf_weapon_flaregun.cpp:547</c>).
    /// </summary>
    bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>The medigun's beam this command: held or let go.</summary>
    void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held);

    /// <summary>The sniper rifle's scope went in or out.</summary>
    void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed);

    /// <summary>
    /// <c>CTFPlayer::MaybeDrawRailgunBeam</c>
    /// (<c>tf_player_shared.cpp:10094-10135</c>): the fired shot of a
    /// <c>sniper_fires_tracer</c> item draws the rail particle between the
    /// muzzle and the shot's end, team-coloured, the classic rifle's mode
    /// taking the distortion trail.
    /// </summary>
    void Tracer(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>
    /// <c>CTFPlayer::DoClassSpecialSkill</c>
    /// (<c>tf_player_shared.cpp:12330-12403</c>): the class's special a
    /// weapon's secondary reaches — the spy's cloak toggle and the demoman's
    /// sticky detonation. Answers whether one ran.
    /// </summary>
    bool ClassSpecialSkill(in WeaponAttackContext attack);

    /// <summary>
    /// <c>CTFPlayer::EndClassSpecialSkill</c>
    /// (<c>tf_player_shared.cpp:12389-12416</c>): the class's special wound
    /// back — the melee swing's ask (<c>tf_weaponbase_melee.cpp:201</c>),
    /// the charged demoman's shield grant before the swing's crit helpers.
    /// </summary>
    void EndClassSpecialSkill(in WeaponAttackContext attack);

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
    /// The swing's start, when a clean miss would hurt its owner: stock's
    /// <c>Swing</c> notes the living enemies inside its loose reach
    /// (<c>tf_weaponbase_melee.cpp:305-319</c>), and one still alive at the
    /// smack spares the owner the miss tax (:742-753).
    /// </summary>
    void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange);

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

    /// <summary>
    /// <c>CTFPlayer::SpeakWeaponFire</c>
    /// (<c>tf_player.cpp:20158-20183</c>): the owner speaks this concept —
    /// an empty one is the plain fire concept behind its own clock; a named
    /// one always speaks (the minigun's wind and fire concepts,
    /// <c>tf_weapon_minigun.cpp:293-301</c>).
    /// </summary>
    void SpeakWeaponFire(in WeaponAttackContext attack, string concept);

    /// <summary>
    /// <c>CTFPlayer::ClearWeaponFireScene</c> (:20185-20188): the gate opens,
    /// so the next plain fire ask speaks again at once.
    /// </summary>
    void ClearWeaponFireScene(in WeaponAttackContext attack);

    /// <summary>
    /// The minigun's firing concept (<c>tf_weapon_minigun.cpp:336-340</c>):
    /// <c>MP_CONCEPT_MINIGUN_FIREWEAPON</c>, once past its five-second gate.
    /// </summary>
    void FiringSpeech(in WeaponAttackContext attack);

    /// <summary>
    /// The lunchbox's eat (<c>CTFPlayer::Taunt</c> by the sandvich's ask,
    /// tf_weapon_lunchbox.cpp:182): the taunt scene, the
    /// <c>TF_COND_TAUNTING</c> condition and the heavy's bite clock arm there.
    /// </summary>
    void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script);

    /// <summary>
    /// The lunchbox's throw (<c>CTFLunchBox::SecondaryAttack</c>'s body,
    /// tf_weapon_lunchbox.cpp:199-288): the health kit behind
    /// <paramref name="mode"/>'s plate, the previous throw removed, the
    /// owner's 0.3 s pickup delay set.
    /// </summary>
    void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode);
    /// One buff pulse (<c>CTFPlayerShared::PulseRageBuff</c>,
    /// <c>tf_player_shared.cpp:13923-14019</c>): the buff named by
    /// <paramref name="buffType"/>'s <c>set_buff_type</c> reaches the owner's
    /// living teammates inside the buff's reach, the deployer among them,
    /// for the pulse's seconds, stock's 1.2 (<c>tf_player_shared.cpp:14006</c>).
    /// </summary>
    void DeployBuff(in WeaponAttackContext attack, int buffType);

    /// <summary>
    /// The deploy's horn (<c>CTFBuffItem::BlowHorn</c>,
    /// <c>tf_weapon_buff_item.cpp:170-222</c>): the held item's horn sound —
    /// the Concheror's conch (:186-189), the Buff Banner's bugle (:191-201)
    /// and the Battalion's horn (:202-212), the Blue horn for Blue and the
    /// Red horn for everyone else — asked on the live run when the press
    /// holds (:134's hold).
    /// </summary>
    void BlowBuffHorn(in WeaponAttackContext attack, int buffType);

    /// <summary>
    /// The deploy's raise (<c>CTFBuffItem::RaiseFlag</c>, :227-256): the
    /// <c>deploy_buff_banner</c> event with the buff type and the owner's
    /// userid goes before the buff's raise (:242), the flag sound follows it
    /// (:251), and the battle cry rides the voice lane (:252).
    /// </summary>
    void RaiseBuffBanner(in WeaponAttackContext attack, int buffType);

    /// <summary>
    /// The rage press (<c>CTFFlameThrower::UseRage</c> through
    /// <c>SecondaryAttack</c>'s buff arm, <c>tf_weapon_flamethrower.cpp:1196
    /// -1200</c>): the taunt whose <c>OnTauntSucceeded</c> arm — a pyro with
    /// this very weapon out, the meter the burn arms filled and the plain
    /// taunt's scene — lights the rage (<c>tf_player.cpp:17897-17921</c>,
    /// <c>ActivateRageBuff</c>, tf_player_shared.cpp:13753). The press's own
    /// gates are the behaviour's; this is the ask.
    /// </summary>
    void UseRage(in WeaponAttackContext attack, int buffType);
}

/// <summary>
/// The swing <see cref="ITFWeaponEffects.Melee"/> traces: the reach and the
/// hull box, what the behaviour decided for this smack — stock's
/// <c>DoSwingTraceInternal</c> reads them off the weapon
/// (<c>tf_weaponbase_melee.cpp:392-425</c>) — whether the hit asks the
/// <c>crit_from_behind</c> arm (:819-833) and whether a clean miss hurts its
/// own owner, the <c>hit_self_on_miss</c> arm (:755-763), whether the held item
/// asks the forced laugh (<c>crit_forces_victim_to_laugh</c>, :884-901, the
/// crit gate itself at the hit), and whether it tickles same-weaponed enemies
/// (<c>tickle_enemies_wielding_same_weapon</c>, :903-927).
/// </summary>
public readonly record struct SwingTrace(float Range, float Hull, bool CritFromBehind, bool SelfOnMiss = false,
                                         bool ForcesLaugh = false, bool TicklesSameWeapon = false);

/// <summary>No effects: what a side without Stage 2's arms does (the shot is spent and nothing appears).</summary>
public sealed class NoTFWeaponEffects : ITFWeaponEffects
{
    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) { }
    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) { }
    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) { }

    public bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script) => false;
    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }
    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) { }
    public void Tracer(in WeaponAttackContext attack, TFWeaponScript script) { }
    public bool ClassSpecialSkill(in WeaponAttackContext attack) => false;
    public void EndClassSpecialSkill(in WeaponAttackContext attack) { }
    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) { }

    public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange) { }
    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) { }
    public void RingOfFire(in WeaponAttackContext attack, int damage) { }
    public void Rearmed(in WeaponAttackContext attack) { }
    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) { }
    public void ClearWeaponFireScene(in WeaponAttackContext attack) { }
    public void FiringSpeech(in WeaponAttackContext attack) { }
    public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script) { }
    public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) { }
    public void DeployBuff(in WeaponAttackContext attack, int buffType) { }
    public void BlowBuffHorn(in WeaponAttackContext attack, int buffType) { }
    public void RaiseBuffBanner(in WeaponAttackContext attack, int buffType) { }

    public void UseRage(in WeaponAttackContext attack, int buffType) { }
}
