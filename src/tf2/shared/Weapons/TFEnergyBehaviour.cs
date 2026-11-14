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
/// The energy family (<c>CTFRaygun</c> and kin: the Righteous Bison, the
/// Pomson 6000, the Cow Mangler 5000 — <c>tf_weapon_raygun.cpp</c>,
/// <c>tf_weapon_particle_cannon.cpp</c>). The tank is the weapon's own count:
/// energy rides <see cref="WeaponNetState.Clip"/> in whole shots, the cap the
/// stock <c>Energy_GetMaxEnergy</c> (<c>tf_weaponbase.cpp:6357-6367</c>) cuts
/// out of <c>ENERGY_WEAPON_MAX_CHARGE</c> over the shot's cost — twenty over
/// five, four rounds, through the item's <c>mult_clipsize_upgrade</c>. One
/// shot spends one round (<c>Energy_Drain</c>, the raygun header's cost of
/// five — <c>energy_weapon_no_drain>0</c> costs none); a full tank is stock's
/// <c>FullyCharged</c>, an empty one its <c>HasEnergy</c> false.
///
/// <para>
/// The bison and the pomson fire as the press says (<c>PrimaryAttack</c>):
/// one ring at the script's delay, its hit the penetrating twenty or the
/// single-hit sixty off <c>energy_weapon_penetration</c>
/// (<c>tf_weapon_raygun.cpp:359-370</c>). The mangler holds instead
/// (<c>CTFParticleCannon::SecondaryAttack</c>, the charge's begin on
/// <see cref="WeaponNetState.BehaviourMeter"/> under the fully-charged door),
/// refuses the plain press while charging, and its ball — whole-max spent —
/// leaves by itself at <c>TF_PARTICLE_MAX_CHARGE_TIME</c>, 2 s
/// (<c>tf_weapon_particle_cannon.h:26</c>), whatever the buttons do since.
/// </para>
///
/// <para>
/// The meter refills on the idle frames (<c>Energy_Recharge</c> through the
/// reload tick, <c>tf_weaponbase.cpp:2043-2074</c>): one whole shot per the
/// reload's animation duration kept in
/// <see cref="WeaponNetState.BehaviourTimeC"/>, held triggers and owed shots
/// pausing it. With <c>energy_weapon_no_ammo</c> set the tank stands alone —
/// the top-up draws on nothing; without it a reserve pays round for round.
/// </para>
/// </summary>
public class TFEnergyBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>ENERGY_WEAPON_MAX_CHARGE</c> (tf_weaponbase.h).</summary>
    public const float MaxChargeEnergy = 20f;

    /// <summary>The shot's rounds (<c>GetEnergyShotCost</c>, tf_weapon_raygun.h:52-63).</summary>
    public const float DefaultShotCost = 5f;

    /// <summary>The recharge's rounds (<c>GetEnergyRechargeCost</c>, the same header).</summary>
    public const float DefaultRechargeCost = 5f;

    /// <summary><c>TF_PARTICLE_MAX_CHARGE_TIME</c> (tf_weapon_particle_cannon.h:26).</summary>
    public const float MaxHoldTime = 2f;

    /// <summary>The ring's hit when its owner pierces (<c>GetDamage</c>, tf_weapon_raygun.cpp:359-370).</summary>
    public const float RingPenetratingDamage = 20f;

    /// <summary>The ring's hit when it does not.</summary>
    public const float RingSingleHitDamage = 60f;

    /// <summary>The mangler is the family's charging half (<c>CTFParticleCannon</c>).</summary>
    private static bool Charges(TFWeaponScript script) => script.Id == TFWeaponId.ParticleCannon;

    /// <summary>The ring half: the bison and the pomson (<c>CTFRaygun</c> and its subclass).</summary>
    private static bool IsRing(TFWeaponScript script) => script.Id is TFWeaponId.Raygun or TFWeaponId.DrgPomson;

    /// <summary>
    /// The tank's whole shots (<c>Energy_GetMaxEnergy</c>'s count, and the
    /// machine's own empty check reads it through here): twenty energy over
    /// the shot's cost, truncated — the item's <c>mult_clipsize_upgrade</c>
    /// scales it last, stock casting the scaled count into the int member.
    /// With no drain the tank caps at the count the recharge's five gives.
    /// </summary>
    public override int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex)
    {
        if (ScriptOf(in type) is not { } script)
            return clipSize;

        var context = ContextFor(in type, script, ownerIndex);
        float cost = Parts.Hooks.EnergyShotCost(in context, DefaultShotCost);
        int shots = cost > 0f ? (int)(MaxChargeEnergy / cost) : (int)(MaxChargeEnergy / DefaultRechargeCost);
        return Parts.Hooks.ModifyEnergyShots(in context, shots);
    }

    /// <summary>
    /// The ring's hit: stock's <c>GetDamage</c> answers the penetrating
    /// twenty when the owner's item pierces and the single-hit sixty when it
    /// does not (tf_weapon_raygun.cpp:359-370) — the bison's shipped schema
    /// carries <c>energy_weapon_penetration</c>, the pomson's does not. The
    /// ball keeps the script's damage, stock's ball storing its own at the
    /// launch (<c>ModifyProjectile</c>).
    /// </summary>
    protected override float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) =>
        IsRing(script)
            ? Parts.Hooks.EnergyPenetrates(Context(in attack, script)) ? RingPenetratingDamage : RingSingleHitDamage
            : script.Primary.Damage;

    /// <summary>
    /// The press, with the veto asked before anything spends (stock's door
    /// in every <c>PrimaryAttack</c>). Out of energy the machine's reload arm
    /// takes the press; charging, the mangler answers nothing until the
    /// release; else one shot leaves.
    /// </summary>
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return false;

        // HasEnergy: one whole round at least, unless the item's
        // energy_weapon_no_drain made the shot cost nothing at all. An empty
        // tank hands the press to the machine's reload (:1812's Reload arm),
        // which is also what tops a dry weapon back up when nothing is held.
        float cost = Parts.Hooks.EnergyShotCost(in context, DefaultShotCost);
        if (weapon.Clip <= 0 && cost > 0f)
            return true;

        // The charge holds the plain shot off (:124's early return while
        // m_flChargeBeginTime runs); the release fires it in the tail.
        if (Charges(script) && weapon.BehaviourMeter > 0f)
            return false;

        Launch(in attack, script, in context);
        return false;
    }

    /// <summary>
    /// The mangler's charge (<c>CTFParticleCannon::SecondaryAttack</c> —
    /// :184's fully-charged door, the fire-delay gate, the stamp; the ring
    /// half keeps the base's class skill). Stock reloads a half-full tank on
    /// this press; ours stands on the machine's own reload arms, the idle
    /// frames refilling either way.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script || !Charges(script))
        {
            base.SecondaryAttack(in attack);
            return;
        }

        var weapon = attack.Weapon;
        int max = ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex);

        // FullyCharged (:184-188): a half tank takes the top-up, not the draw.
        if (weapon.Clip < max)
            return;
        if (weapon.NextPrimaryAttack > attack.Now || weapon.BehaviourMeter > 0f)
            return;

        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context))
            return;

        // The charge begins (:190-197): the stamp, the anim, TF_COND_AIMING
        // for the held sight line.
        weapon.BehaviourMeter = attack.Now;
        SendAnim(in attack, WeaponAnim.Primary);
        if (attack.Command.OwnerIndex > 0
            && Parts.Owners.ConditionsOf(attack.Command.OwnerIndex) is { } conditions)
            conditions.Add(TFCond.Aiming);
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            var weapon = attack.Weapon;

            // The charge has no release (:153-165): it runs its two seconds
            // whatever the buttons do, then fires itself.
            if (Charges(script) && weapon.BehaviourMeter > 0f
                && attack.Now - weapon.BehaviourMeter >= MaxHoldTime)
                Launch(in attack, script, Context(in attack, script));

            Recharge(in attack, script);
        }

        base.ItemPostFrame(in attack);
    }

    /// <summary>The held sight line: the aim cond rides the charge and clears with it.</summary>
    protected override bool HoldsAiming(in WeaponAttackContext attack) =>
        attack.Weapon.BehaviourMeter > 0f;

    /// <summary>One shot: its charge, its spend, its sound and its effects — the shape every TF launcher's launch keeps.</summary>
    private void Launch(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var weapon = attack.Weapon;
        float cost = Parts.Hooks.EnergyShotCost(in context, DefaultShotCost);

        // The charge the held button built, clamped as stock's
        // GetCurrentCharge clamps it; the release that starts with none (a
        // ring's plain press) carries none too.
        float charge = Charges(script) && weapon.BehaviourMeter > 0f
            ? MathF.Min(attack.Now - weapon.BehaviourMeter, MaxHoldTime)
            : 0f;
        bool charged = Charges(script) && charge > 0f;
        weapon.BehaviourMeter = 0f;

        // The spend (:6369-6403): a drained shot for the plain press, the
        // whole tank for the charged ball, nothing at all when the item
        // answers energy_weapon_no_drain.
        if (cost > 0f)
            weapon.Clip = charged ? 0 : Math.Max(0, weapon.Clip - 1);

        var shot = ModifyShot(in attack, default);

        SendAnim(in attack, WeaponAnim.Primary);
        // PlayWeaponShootSound (:467-477): a charged shot's sound was the charge's.
        if (!charged)
            PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge);

        weapon.NextPrimaryAttack = attack.Now + FireDelay(in attack, script);
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack;
        Parts.Hooks.AfterFire(in context, in attack);

        // The tail every stock shot ends with (tf_weaponbase_gun.cpp:330-368):
        // the muzzle flash the script names, live arms only.
        if (attack.EffectsLive && ShouldDoMuzzleFlash && script.MuzzleFlashParticleEffect.Length > 0)
            attack.Arms.MuzzleFlash(attack.Type, script.MuzzleFlashParticleEffect);
    }

    /// <summary>
    /// The meter's tick (<c>Energy_Recharge</c>, <c>tf_weaponbase.cpp:2043-</c>
    /// through the reload tick): one whole shot per the reload animation's own
    /// duration — the script's fire delay stands in when the viewmodel names
    /// none — paced on <see cref="WeaponNetState.BehaviourTimeC"/>, paused by
    /// a held trigger or an owed shot as stock pauses it while the weapon is
    /// busy. With <c>energy_weapon_no_ammo</c> set the top-up draws on nothing;
    /// else a round of the reserve pays each round of the tank.
    /// </summary>
    private void Recharge(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        int max = ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex);
        if (max <= 0 || weapon.Clip >= max)
            return;

        // Neither trigger down, nothing owed (:2451's idle door).
        if ((weapon.LastButtons & ((int)InputButtons.Attack | (int)InputButtons.Attack2)) != 0)
            return;
        if (weapon.NextPrimaryAttack > attack.Now)
            return;

        float duration = attack.Arms.AnimDuration(WeaponAnim.Reload);
        if (duration <= 0f)
            duration = script.Primary.TimeFireDelay;
        duration = MathF.Max(ModifyReloadTime(attack.Type, duration, attack.Command.OwnerIndex), 0.00001f);

        if (weapon.BehaviourTimeC <= 0f)
        {
            weapon.BehaviourTimeC = attack.Now + duration;
            return;
        }

        if (attack.Now < weapon.BehaviourTimeC)
            return;

        weapon.Clip += 1;
        var context = Context(in attack, script);
        if (!Parts.Hooks.EnergyNoAmmo(in context) && weapon.Reserve > 0)
            weapon.Reserve -= 1;
        weapon.BehaviourTimeC = attack.Now + duration;
    }
}
