//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Projectiles;

/// <summary>
/// The constants of the milestone projectiles (Stage 2.8), each cited where
/// stock defines it.
/// </summary>
public static class TFProjectileDefs
{
    /// <summary><c>TF_ROCKET_RADIUS</c> (<c>tf_weaponbase_rocket.h:26</c>): the radius used on others.</summary>
    public const float RocketRadius = 146f;

    /// <summary>
    /// <c>TF_ROCKET_RADIUS_FOR_RJS</c> (<c>tf_weaponbase_rocket.h:25</c>,
    /// <c>110 × 1.1</c>): the radius the shooter's own pass uses, so a rocket
    /// jump stays the stock launcher's whatever the launcher's radius.
    /// </summary>
    public const float RocketRadiusForRocketJumps = 110.0f * 1.1f;

    /// <summary>The rocket's launch speed (<c>CTFBaseRocket::Create</c>, <c>tf_weaponbase_rocket.cpp</c>).</summary>
    public const float RocketSpeed = 1100f;

    /// <summary>The pipe's launch speed (<c>TF_GRENADE_LAUNCER_MIN_VEL</c>, <c>tf_weapon_grenadelauncher.cpp:83</c>).</summary>
    public const float PipeSpeed = 1200f;

    /// <summary>The script's speed, or stock's when the script names none.</summary>
    public static float Speed(float scriptSpeed, float stock) => scriptSpeed > 0f ? scriptSpeed : stock;

    /// <summary><c>TF_GRENADE_JUMP_RADIUS</c> (<c>tf_weaponbase_grenadeproj.cpp:300</c>): the thrower's own pass.</summary>
    public const float GrenadeJumpRadius = 146f;

    /// <summary><c>TF_WEAPON_GRENADE_DETONATE_TIME</c> (<c>tf_weapon_grenade_pipebomb.cpp:400</c>): a pipe's fuse.</summary>
    public const float PipeFuse = 2.0f;

    /// <summary>
    /// <c>TF_WEAPON_PIPEBOMB_TIMER_DMG_REDUCTION</c> (<c>tf_weapon_grenade_pipebomb.cpp:47</c>):
    /// a pipe is created at this share of its damage and an enemy contact restores the full
    /// damage (<c>CTFGrenadePipebombProjectile::Create</c>, <c>PipebombTouch</c>).
    /// </summary>
    public const float PipeTimerDamageScale = 0.6f;

    /// <summary><c>tf_grenadelauncher_livetime</c>'s default (<c>tf_weapon_grenade_pipebomb.cpp:51</c>): a sticky's arm time.</summary>
    public const float StickyArmTime = 0.8f;

    /// <summary>
    /// <c>tf_sticky_radius_ramp_time</c>'s default (<c>tf_gamerules.cpp:859</c>):
    /// the window after the arm time over which an air-detonated sticky's
    /// radius ramps from <see cref="StickyAirDetRadius"/> to full
    /// (<c>GetDamageRadius</c>, <c>tf_weapon_grenade_pipebomb.cpp:1426-1429</c>).
    /// </summary>
    public const float StickyRadiusRampTime = 2.0f;

    /// <summary>
    /// <c>tf_sticky_airdet_radius</c>'s default (<c>tf_gamerules.cpp:860</c>):
    /// the share of its radius a never-touched sticky has when it detonates
    /// in the air (the same <c>GetDamageRadius</c> arm).
    /// </summary>
    public const float StickyAirDetRadius = 0.85f;

    /// <summary><c>TF_WEAPON_PIPEBOMB_COUNT</c> (<c>tf_shareddefs.h:391</c>): the stickies a launcher keeps.</summary>
    public const int MaxStickies = 8;

    /// <summary><c>TF_PIPEBOMB_MIN_CHARGE_VEL</c> (<c>tf_weapon_pipebomblauncher.h:28</c>).</summary>
    public const float StickyMinChargeSpeed = 900f;

    /// <summary><c>TF_PIPEBOMB_MAX_CHARGE_VEL</c> (<c>tf_weapon_pipebomblauncher.h:29</c>).</summary>
    public const float StickyMaxChargeSpeed = 2400f;

    /// <summary>
    /// A remote-detonate pipe adds <c>DMG_USEDISTANCEMOD</c> within this
    /// age (<c>tf_weapon_grenade_pipebomb.cpp:155</c>, <c>&lt; 5.0</c>).
    /// </summary>
    public const float StickyYoungTime = 5f;

    /// <summary>
    /// The stickybomb's <c>GetDamageType</c>
    /// (<c>tf_weapon_grenade_pipebomb.cpp:148-162</c>): within 5 s of its
    /// creation the remote-detonate pipe ORs <c>DMG_USEDISTANCEMOD</c> onto
    /// its row.
    /// </summary>
    public static DamageType YoungStickyBits(DamageType bits, float age)
        => age < StickyYoungTime ? bits | TFDamageBits.UseDistanceMod : bits;

    /// <summary>
    /// The stickybomb's launch speed after <paramref name="charge"/> seconds:
    /// <c>CTFPipebombLauncher::GetProjectileSpeed</c>
    /// (<c>tf_weapon_pipebomblauncher.cpp:353-362</c>),
    /// <c>RemapValClamped(charge, 0, TF_PIPEBOMB_MAX_CHARGE_TIME, 900, 2400)</c>.
    /// </summary>
    public static float StickySpeed(float charge)
        => TFRadiusDamage.RemapClamped(charge, 0f, TFProjectileBehaviour.MaxChargeTime,
                                       StickyMinChargeSpeed, StickyMaxChargeSpeed);

    /// <summary>
    /// What a grenade is born with: <c>CTFWeaponBaseGrenadeProj::InitGrenade</c>'s
    /// <c>SetGravity( 0.4f )</c> (<c>tf_weaponbase_grenadeproj.cpp:245</c>) —
    /// not the <c>TF_WEAPON_PIPEBOMB_GRAVITY</c> define (0.5,
    /// <c>tf_weapon_grenade_pipebomb.cpp:43</c>), which the spawn path never reads.
    /// </summary>
    public const float PipebombGravity = 0.4f;

    /// <summary>InitGrenade's <c>SetFriction( 0.2f )</c> (<c>tf_weaponbase_grenadeproj.cpp:246</c>).</summary>
    public const float PipebombFriction = 0.2f;

    /// <summary><c>ARROW_MODEL</c>'s regular spelling (<c>tf_projectile_arrow.cpp</c>'s model table).</summary>
    public const string ArrowModel = "models/weapons/w_models/w_arrow.mdl";

    /// <summary>
    /// The bow's charge window: <c>CTFCompoundBow::GetChargeMaxTime</c>'s base
    /// (<c>tf_weapon_compound_bow.cpp:269-275</c>, <c>ApplyFireDelay( 1.0f )</c>).
    /// </summary>
    public const float ArrowChargeTime = 1f;

    /// <summary>The bow's flat arrow damage (<c>tf_weapon_compound_bow.cpp:295</c>, <c>flBaseDamage = 50.f</c>).</summary>
    public const float ArrowBaseDamage = 50f;

    /// <summary>
    /// The bow's shot (<c>CBaseCombatWeapon</c>-side of
    /// <c>CTFCompoundBow::GetProjectileDamage</c>,
    /// <c>tf_weapon_compound_bow.cpp:292-301</c>): the flat base plus the
    /// script's projectile damage scaled by the charge fraction.
    /// </summary>
    public static float ArrowDamage(float scriptDamage, float charge)
        => ArrowBaseDamage + scriptDamage * TFRadiusDamage.RemapClamped(charge, 0f, ArrowChargeTime, 0f, 1f);

    /// <summary>
    /// The arrow's launch speed after <paramref name="charge"/> seconds:
    /// <c>CTFCompoundBow::GetProjectileSpeed</c>
    /// (<c>tf_weapon_compound_bow.cpp:306-309</c>),
    /// <c>RemapValClamped(charge, 0, GetChargeMaxTime, 1800, 2600)</c>.
    /// </summary>
    public static float ArrowSpeed(float charge)
        => TFRadiusDamage.RemapClamped(charge, 0f, ArrowChargeTime, ArrowMinChargeSpeed, ArrowMaxChargeSpeed);

    /// <summary>Stock's speed ends, in <c>GetProjectileSpeed</c>'s remap (:308).</summary>
    public const float ArrowMinChargeSpeed = 1800f;
    public const float ArrowMaxChargeSpeed = 2600f;

    /// <summary>
    /// How far the arrow falls, after <paramref name="charge"/> seconds:
    /// <c>CTFCompoundBow::GetProjectileGravity</c>
    /// (<c>tf_weapon_compound_bow.cpp:314-317</c>),
    /// <c>RemapValClamped(charge, 0, GetChargeMaxTime, 0.5, 0.1)</c>.
    /// </summary>
    public static float ArrowGravity(float charge)
        => TFRadiusDamage.RemapClamped(charge, 0f, ArrowChargeTime, 0.5f, 0.1f);

    /// <summary>
    /// InitGrenade's <c>SetElasticity( 0.45f )</c>
    /// (<c>tf_weaponbase_grenadeproj.cpp:247</c>) — not the
    /// <c>TF_WEAPON_PIPEBOMB_ELASTICITY</c> define (the same 0.45), which no
    /// reader reaches. What the elasticity buys is
    /// <c>VPhysicsUpdate</c>'s ray-cast bounce off a player
    /// (<c>tf_weaponbase_grenadeproj.cpp:784</c>).
    /// </summary>
    public const float PipebombElasticity = 0.45f;

    /// <summary>
    /// <c>tf_pipebomb_deflect_reset_time</c>'s default
    /// (<c>tf_weapon_grenade_pipebomb.cpp:954</c>): how long a deflection's
    /// count rides the sticky (<c>IncrementDeflected</c>'s reset,
    /// <c>:1100-1108</c>).
    /// </summary>
    public const float PipebombDeflectResetTime = 10.0f;

    /// <summary>
    /// <c>CBaseProjectile::GetCollideWithTeammatesDelay</c>'s default
    /// (<c>baseprojectile.h:54</c>): a projectile does not collide with its
    /// own team for this long after spawning. The grenades do not override it.
    /// </summary>
    public const float CollideWithTeammatesDelay = 0.25f;

    /// <summary><c>TF_WEAPON_PIPEGRENADE_MODEL</c> (<c>tf_weapon_grenade_pipebomb.cpp</c>).</summary>
    public const string PipeModel = "models/weapons/w_models/w_grenade_grenadelauncher.mdl";

    /// <summary><c>TF_WEAPON_PIPEBOMB_MODEL</c> (<c>tf_weapon_grenade_pipebomb.cpp:395</c>).</summary>
    public const string StickyModel = "models/weapons/w_models/w_stickybomb.mdl";

    /// <summary><c>SYRINGE_MODEL</c> (<c>tf_projectile_nail.cpp:28</c>).</summary>
    public const string SyringeModel = "models/weapons/w_models/w_syringe_proj.mdl";

    /// <summary><c>SYRINGE_GRAVITY</c> (<c>tf_projectile_nail.cpp:46</c>).</summary>
    public const float SyringeGravity = 0.3f;

    /// <summary><c>SYRINGE_VELOCITY</c> (<c>tf_projectile_nail.cpp:47</c>); the script's ProjectileSpeed says the same.</summary>
    public const float SyringeSpeed = 1000f;

    /// <summary>
    /// The syringe's hit (<c>tf_weapon_syringegun</c>'s script
    /// <c>ProjectileDamage</c>): the medic's nail carries no radius, its whole
    /// damage lands on what it touches.
    /// </summary>
    public const float SyringeDamage = 10f;

    /// <summary>
    /// <c>FireNail</c>'s <c>flSpread</c> (<c>tf_weaponbase_gun.cpp:612</c>):
    /// the syringe cone's half-angle in degrees, drawn on the launch angles'
    /// pitch and yaw — plus the weapon's <c>GetProjectileSpread</c>, which the
    /// syringe gun leaves at 0.
    /// </summary>
    public const float SyringeSpread = 1.5f;

    /// <summary><c>CTFBaseRocket</c>'s model (<c>models/weapons/w_models/w_rocket.mdl</c>).</summary>
    public const string RocketModel = "models/weapons/w_models/w_rocket.mdl";

    /// <summary>
    /// The rocket's muzzle offset along the shooter's (forward, right, up):
    /// <c>vecOffset</c>, <c>CTFWeaponBaseGun::FireRocket</c>,
    /// <c>tf_weaponbase_gun.cpp:521</c>.
    /// </summary>
    public const float RocketMuzzleForward = 23.5f;
    public const float RocketMuzzleRight = 12f;
    public const float RocketMuzzleUp = -3f;

    /// <summary>The rocket's crouched muzzle height (<c>GetFlags() &amp; FL_DUCKING</c>, :522-525).</summary>
    public const float RocketMuzzleUpDucked = 8f;

    /// <summary>
    /// The syringe gun's muzzle offset (<c>CTFWeaponBaseGun::FireNail</c>'s
    /// <c>vecOffset</c>, <c>tf_weaponbase_gun.cpp:620</c>).
    /// </summary>
    public const float SyringeMuzzleForward = 16f;
    public const float SyringeMuzzleRight = 6f;
    public const float SyringeMuzzleUp = -8f;

    /// <summary>
    /// The pipe's and the sticky's muzzle offset (<c>FirePipeBomb</c>'s
    /// <c>vecSrc</c>, <c>tf_weaponbase_gun.cpp:668-674</c>). Stock flips the
    /// 8 right with <c>IsViewModelFlipped()</c> — a per-view client preference
    /// this port does not read (docs/divergences.md).
    /// </summary>
    public const float PipeMuzzleForward = 16f;
    public const float PipeMuzzleRight = 8f;
    public const float PipeMuzzleUp = -6f;

    /// <summary>
    /// The pipe's and the sticky's launch tilt: the shared <c>vecVelocity</c>
    /// is <c>forward * speed + up * 200</c> plus the jitter below
    /// (<c>FirePipeBomb</c>, <c>tf_weaponbase_gun.cpp:687</c>).
    /// </summary>
    public const float PipeLaunchUp = 200f;

    /// <summary>And its ±10 u/s jitter on right and up (:687-688), off the engine stream.</summary>
    public const float PipeLaunchJitter = 10f;

    /// <summary>
    /// The pipes' shared spin: <c>AngularImpulse(600, randInt(-1200, 1200), 0)</c>
    /// (<c>FirePipeBomb</c>, <c>tf_weaponbase_gun.cpp:694</c>), which
    /// <c>ApplyLocalAngularVelocityImpulse</c> swizzles into an angular
    /// velocity (<c>angles = (impulse.y, impulse.z, impulse.x)</c>,
    /// <c>vector.h:2198</c>) — roll 600°/s and a pitch tumble.
    /// loch-n-load's <c>grenade_no_spin</c> zeroes it (deferred.tsv).
    /// </summary>
    public const float PipeSpinRoll = 600f;
    public const int PipeSpinPitchMax = 1200;
}

/// <summary>
/// TF's blast geometry: <c>CTFRadiusDamageInfo</c> and
/// <c>CTFGameRules::RadiusDamage</c> (<c>shared/tf/tf_gamerules.cpp:5625-5892</c>,
/// <c>tf_gamerules.h:228-262</c>) for what a victim takes from where it
/// stands, and <c>CTFPlayer::ApplyPushFromDamage</c>
/// (<c>server/tf/tf_player.cpp:10232-10475</c>) with its <c>DamageForce</c>
/// (:8661-8671) for how far the blast throws a player. Pure arithmetic over
/// values: the traversal that finds the victims and the trace that can block
/// one are the caller's.
///
/// <para>
/// What this leaves to the damage layer (Stage 2.7, <c>TFDamageModifyRules</c>):
/// the range ramp-up and fall-off of <c>DMG_USEDISTANCEMOD</c>, and the crit
/// and mini-crit multipliers. A blast hands each victim the distance-falloff
/// damage below with the crit flag, untripled, as stock's radius damage does.
/// </para>
/// </summary>
public static class TFRadiusDamage
{
    /// <summary>
    /// The share of their own blast a demoman's weapons deal their owner
    /// (<c>ApplyToEntity</c>, :5822-5833).
    /// </summary>
    public const float GrenadeSelfDamageScale = 0.75f;

    /// <summary><c>tf_damageforcescale_other</c>'s default (<c>tf_player.cpp:190</c>).</summary>
    public const float ForceScaleOther = 6.0f;

    /// <summary><c>tf_damageforcescale_self_soldier_rj</c>'s default (<c>tf_player.cpp:191</c>): a soldier off the ground.</summary>
    public const float ForceScaleSelfSoldierJump = 10.0f;

    /// <summary><c>tf_damageforcescale_self_soldier_badrj</c>'s default (<c>tf_player.cpp:192</c>): a soldier on the ground.</summary>
    public const float ForceScaleSelfSoldierGrounded = 5.0f;

    /// <summary><c>tf_damagescale_self_soldier</c> (<c>tf_player.cpp:194</c>).</summary>
    public const float DamageScaleSelfSoldier = 0.6f;

    /// <summary><c>tf_damageforcescale_pyro_jump</c>'s default (<c>tf_player.cpp:193</c>): the detonator's flare jump.</summary>
    public const float ForceScalePyroJump = 8.5f;

    /// <summary><c>DAMAGE_FORCE_SCALE_SELF</c> (<c>tf_player.cpp:147</c>): every other self blast (stickies).</summary>
    public const float ForceScaleSelf = 9f;

    /// <summary><c>DamageForce</c>'s ceiling (<c>tf_player.cpp:8665</c>).</summary>
    public const float MaxForce = 1000f;

    /// <summary>The height a crouched player's self push is measured with (<c>ApplyPushFromDamage</c>, :10248-10255).</summary>
    public const float CrouchForceHeight = 55f;

    /// <summary>How far below the inflictor the push pretends it is, so a body flies upward (<c>OnTakeDamage_Alive</c>).</summary>
    public const float PushLift = 10f;

    /// <summary>
    /// <c>CTFRadiusDamageInfo::CalculateFalloff</c> (:5720-5753): the share of
    /// the damage left at the radius's edge. <c>DMG_RADIUS_MAX</c> is none,
    /// <c>DMG_HALF_FALLOFF</c> half, else stock's <c>damage / radius</c> (a
    /// ratio stock then multiplies the damage by, kept as written), and a zero
    /// radius keeps it all. The precision rune's zeroing is deferred
    /// (<c>docs/plans/tf2/deferred.tsv</c>); the weapon item's
    /// <c>mult_dmg_falloff</c> is the <see cref="Falloff(DamageType, float,
    /// float, float)"/> overload.
    /// </summary>
    public static float Falloff(DamageType bits, float damage, float radius)
    {
        if ((bits & TFDamageBits.RadiusMax) != 0)
            return 0f;
        if ((bits & TFDamageBits.HalfFalloff) != 0)
            return 0.5f;
        return radius != 0f ? damage / radius : 1f;
    }

    /// <summary>
    /// The falloff with the weapon's <c>mult_dmg_falloff</c> read
    /// (<c>CalculateFalloff</c>, :5741-5744): the hook starts at 1 — anything
    /// else reads as <c>1 × attribute</c> (the Percentage format multiplies,
    /// and it lands on the running value, not on a 1) — and stock ADDS the
    /// whole mod to the falloff: a 0.5 mod on a half-falloff blast leaves
    /// none at the edge.
    /// </summary>
    public static float Falloff(DamageType bits, float damage, float radius, float falloffMod)
    {
        float falloff = Falloff(bits, damage, radius);
        return falloffMod != 1f ? falloff + falloffMod : falloff;
    }

    /// <summary>
    /// The damage at <paramref name="distance"/> from the blast (<c>ApplyToEntity</c>,
    /// :5813): <c>RemapValClamped(distance, 0, radius, damage, damage × falloff)</c>.
    /// </summary>
    public static float AtDistance(float damage, float radius, float falloff, float distance)
        => RemapClamped(distance, 0f, radius, damage, damage * falloff);

    /// <summary>
    /// The distance a victim is scored at (<c>ApplyToEntity</c>, :5794-5811):
    /// zero for the entity the projectile hit directly (its <c>GetEnemy()</c>),
    /// a player's nearer of its <c>WorldSpaceCenter</c> and origin, anything
    /// else the blast trace's end.
    /// </summary>
    public static float VictimDistance(Vector source, bool directHit, bool isPlayer,
                                       Vector worldSpaceCenter, Vector origin, Vector traceEnd)
    {
        if (directHit)
            return 0f;
        if (isPlayer)
            return MathF.Min((source - worldSpaceCenter).Length, (source - origin).Length);
        return (source - traceEnd).Length;
    }

    /// <summary>
    /// Whether the weapon deals its owner the reduced share
    /// (<see cref="GrenadeSelfDamageScale"/>): the pipebomb and grenade
    /// launchers, the loose cannon and the caber (:5820-5828).
    /// </summary>
    public static bool ScalesSelfDamage(TFWeaponId weapon) => weapon is TFWeaponId.Pipebomblauncher
        or TFWeaponId.Grenadelauncher or TFWeaponId.Cannon or TFWeaponId.Stickbomb;

    /// <summary>
    /// One victim's damage from <paramref name="blast"/>: the falloff at its
    /// distance, the owner's share for a demoman weapon, nothing below zero.
    /// <paramref name="self"/> is the victim being the attacker.
    /// </summary>
    public static float VictimDamage(in TFBlast blast, float distance, bool self, TFWeaponId weapon)
    {
        float adjusted = AtDistance(blast.Damage, blast.Radius, blast.Falloff, distance);
        if (self && ScalesSelfDamage(weapon))
            adjusted *= GrenadeSelfDamageScale;
        return adjusted > 0f ? adjusted : 0f;
    }

    /// <summary>
    /// <c>CEntitySphereQuery</c> is a box test, so stock keeps only what has a
    /// point within the radius (<c>CalcNearestPoint</c>, :5642-5647): the
    /// nearest point of the box <paramref name="mins"/>..<paramref name="maxs"/>.
    /// </summary>
    public static bool Reaches(Vector source, float radius, Vector mins, Vector maxs)
    {
        var nearest = new Vector(Math.Clamp(source.X, mins.X, maxs.X),
                                 Math.Clamp(source.Y, mins.Y, maxs.Y),
                                 Math.Clamp(source.Z, mins.Z, maxs.Z));
        return (source - nearest).LengthSquared <= radius * radius;
    }

    /// <summary>
    /// <c>DamageForce</c> (<c>tf_player.cpp:8661-8671</c>):
    /// <c>damage × (48 × 48 × 82) / volume × scale</c>, at most 1000.
    /// </summary>
    public static float DamageForce(Vector size, float damage, float scale)
    {
        float force = damage * (48f * 48f * 82f / (size.X * size.Y * size.Z)) * scale;
        return force > MaxForce ? MaxForce : force;
    }

    /// <summary>
    /// <c>OnTakeDamage</c>'s scale on a blast the soldier takes from himself
    /// off the ground and out of the water (<c>bIsSoldierRocketJumping</c>,
    /// <c>tf_player.cpp:8921</c>, <c>:9155-9158</c>): it comes before
    /// <c>ApplyOnDamageModifyRules</c> fixes the damage for force, so the
    /// jump's push takes it as well as its health.
    /// </summary>
    public static float SelfDamageScale(bool soldier, bool blast, bool onGround, bool inWater)
        => soldier && blast && !onGround && !inWater ? DamageScaleSelfSoldier : 1f;

    /// <summary>
    /// The push's scale for a blast on its own owner (<c>ApplyPushFromDamage</c>,
    /// :10245-10306): a soldier's blast throws <see cref="ForceScaleSelfSoldierJump"/>
    /// off the ground and <see cref="ForceScaleSelfSoldierGrounded"/> on it, and a
    /// soldier's non-blast self damage none; the detonator's flare jump
    /// <see cref="ForceScalePyroJump"/>; every other self blast
    /// <see cref="ForceScaleSelf"/>. The weapon's
    /// <c>mult_dmgself_push_force</c> multiplies the result in
    /// <c>TFBlastApplier.Push</c>.
    /// </summary>
    public static float SelfPushScale(bool soldier, bool blast, bool onGround, bool pyroFlareJump)
    {
        if (soldier)
            return blast ? (onGround ? ForceScaleSelfSoldierGrounded : ForceScaleSelfSoldierJump) : 0f;
        return pyroFlareJump ? ForceScalePyroJump : ForceScaleSelf;
    }

    /// <summary>
    /// The size a self push is measured with: a crouched hull counts
    /// <see cref="CrouchForceHeight"/> high (:10248-10255), so a crouch jump
    /// goes higher, as stock's comment says it does.
    /// </summary>
    public static Vector SelfPushSize(Vector size, Vector crouchHullSize)
        => size.X == crouchHullSize.X && size.Y == crouchHullSize.Y && size.Z == crouchHullSize.Z
            ? new Vector(size.X, size.Y, CrouchForceHeight)
            : size;

    /// <summary>
    /// The direction a blast pushes from (<c>OnTakeDamage_Alive</c>): the
    /// inflictor's centre <see cref="PushLift"/> units low, to the victim's
    /// centre, normalized. The push is this times <c>-DamageForce</c>.
    /// </summary>
    public static Vector PushDirection(Vector inflictorCenter, Vector victimCenter)
        => (inflictorCenter - new Vector(0f, 0f, PushLift) - victimCenter).Normalized;

    /// <summary>
    /// A player's push from a blast (<c>ApplyPushFromDamage</c>): both arms
    /// measure on the victim's own OBB (<paramref name="size"/>, their
    /// <c>WorldAlignSize</c>); its own blast at <see cref="SelfPushScale"/>,
    /// the crouch folded by <see cref="SelfPushSize"/>, someone else's at
    /// <see cref="ForceScaleOther"/> (a heavy takes half, :10370-10374).
    /// </summary>
    public static Vector Push(Vector direction, Vector size, float damage, bool self, float selfScale, bool heavy)
    {
        if (self)
            return direction * -DamageForce(size, damage, selfScale);

        Vector force = direction * -DamageForce(size, damage, ForceScaleOther);
        return heavy ? force * 0.5f : force;
    }

    /// <summary>Stock's <c>RemapValClamped</c> (<c>mathlib.h</c>); a degenerate range answers the far end.</summary>
    public static float RemapClamped(float value, float a, float b, float c, float d)
    {
        if (a == b)
            return value >= b ? d : c;
        float t = Math.Clamp((value - a) / (b - a), 0f, 1f);
        return c + (d - c) * t;
    }
}

/// <summary>
/// One blast as <c>CTFRadiusDamageInfo</c> holds it: the damage at the
/// centre, the radius, the bits and the edge share, which stock works out
/// once, in the constructor (<c>tf_gamerules.h:232-244</c>), and keeps for
/// the owner's own pass (<see cref="SelfPass"/>).
/// </summary>
public readonly record struct TFBlast(float Damage, float Radius, DamageType Bits, float Falloff)
{
    /// <summary>A blast of <paramref name="damage"/> over <paramref name="radius"/>, its falloff worked out.</summary>
    public static TFBlast Of(float damage, float radius, DamageType bits)
        => new(damage, radius, bits, TFRadiusDamage.Falloff(bits, damage, radius));

    /// <summary>
    /// The same blast with its edge share re-worked for the weapon's
    /// <c>mult_dmg_falloff</c> (<see cref="TFRadiusDamage.Falloff(DamageType,
    /// float, float, float)"/>, <c>CalculateFalloff</c>'s hook arm).
    /// </summary>
    public TFBlast Falloffed(float falloffMod)
        => this with { Falloff = TFRadiusDamage.Falloff(Bits, Damage, Radius, falloffMod) };

    /// <summary>
    /// The owner's own pass (<c>CTFGameRules::RadiusDamage</c>, :5661-5714):
    /// with a rocket-jump radius set the attacker is skipped in the sweep and
    /// hit after it at that radius and the launcher script's base damage, so a
    /// launcher with other damage or radius still jumps like the stock one.
    /// The falloff stays the blast's.
    /// </summary>
    public TFBlast SelfPass(float rocketJumpRadius, float baseDamage)
        => this with { Radius = rocketJumpRadius, Damage = baseDamage };
}
