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

    /// <summary>
    /// The energy drain's telling (<c>ApplyPostHitEffects</c>' tail,
    /// <c>tf_weaponbase.cpp:5619-5635</c>): the particle over the victim and
    /// the sound on them. The colours are stock's weapon-particle constants
    /// (<c>tf_weaponbase.h:256-259</c>), the owner's team picking the row.
    /// </summary>
    public const string EnergyDrainEffect = "drg_pomson_impact_drain";
    public const string EnergyDrainSound = "Weapon_Pomson.DrainedVictim";

    /// <summary>The weapon-particle colours (<c>tf_weaponbase.h:256-259</c>).</summary>
    public static Vector ParticleWeaponRed1 => new(0.72f, 0.22f, 0.23f);
    public static Vector ParticleWeaponRed2 => new(0.5f, 0.18f, 0.125f);
    public static Vector ParticleWeaponBlue1 => new(0.345f, 0.52f, 0.635f);
    public static Vector ParticleWeaponBlue2 => new(0.145f, 0.427f, 0.55f);

    /// <summary>
    /// The Short Circuit's orb, the shared file's own constants
    /// (<c>tf_weapon_mechanical_arm.cpp:46-51</c>): the reach, the flight, the
    /// price, the zap and its clock, the life.
    /// </summary>
    public const float MechanicalArmOrbSize = 100f;
    public const float MechanicalArmOrbSpeed = 700f;
    public const int MechanicalArmOrbCost = 65;
    public const int MechanicalArmOrbZapTargets = 2;
    public const float MechanicalArmOrbZapDamage = 15f;
    public const float MechanicalArmOrbLifetime = 1.2f;

    /// <summary>The orb think's cadence and the zap's refire (<c>OrbThink</c>, :890, <c>CheckForPlayers</c>, :803).</summary>
    public const float MechanicalArmOrbThink = 0.1f;
    public const float MechanicalArmOrbZapRefire = 0.15f;

    /// <summary>
    /// The orb's muzzle (<c>CTFMechanicalArm::SecondaryAttack</c>,
    /// <c>tf_weapon_mechanical_arm.cpp:260</c>): forward 40, right 15 (the
    /// mirrored view flips it), up −10, off the eye.
    /// </summary>
    public const float MechanicalArmMuzzleForward = 40f;
    public const float MechanicalArmMuzzleRight = 15f;
    public const float MechanicalArmMuzzleUp = -10f;

    /// <summary>The pipe's launch speed (<c>TF_GRENADE_LAUNCER_MIN_VEL</c>, <c>tf_weapon_grenadelauncher.cpp:83</c>).</summary>
    public const float PipeSpeed = 1200f;

    /// <summary>The script's speed, or stock's when the script names none.</summary>
    public static float Speed(float scriptSpeed, float stock) => scriptSpeed > 0f ? scriptSpeed : stock;

    /// <summary><c>tf_fireball_speed</c>'s default (<c>tf_projectile_dragons_fury.cpp:34</c>): the ball's straight flight.</summary>
    public const float BallSpeed = 3000f;

    /// <summary><c>tf_fireball_distance</c>'s default (<c>tf_projectile_dragons_fury.cpp:33</c>): the ball's reach.</summary>
    public const float BallDistance = 500f;

    /// <summary><c>tf_fireball_max_lifetime</c>'s default (<c>tf_projectile_dragons_fury.cpp:40</c>): the ball's life.</summary>
    public const float BallLifetime = 0.5f;

    /// <summary><c>tf_fireball_burn_duration</c>'s default (<c>tf_projectile_dragons_fury.cpp:36</c>): the burn the ball lights for.</summary>
    public const float BallBurnDuration = 2f;

    /// <summary><c>tf_fireball_burning_bonus</c>'s default (<c>tf_projectile_dragons_fury.cpp:39</c>): the bonus onto a burning victim's hit.</summary>
    public const float BallBurnBonus = 3f;

    /// <summary><c>tf_fireball_damage</c>'s default (<c>tf_projectile_dragons_fury.cpp:35</c>): the ball's shot behind the item's hook.</summary>
    public const float BallBaseDamage = 25f;

    /// <summary>The ball's model, empty by design (<c>tf_projectile_dragons_fury.cpp:61, 167</c>).</summary>
    public const string BallModel = "models/empty.mdl";

    /// <summary>The ball's muzzle (<c>tf_weapon_dragons_fury.cpp:152-159</c>): forward 3, right 7 (the mirrored view flips it), up −9.</summary>
    public const float BallMuzzleForward = 3f;
    public const float BallMuzzleRight = 7f;
    public const float BallMuzzleUp = -9f;

    /// <summary><c>tf_scout_stunball_base_speed</c>'s default (<c>tf_weapon_bat.cpp:108</c>): the ornament's leave.</summary>
    public const float OrnamentSpeed = 3000f;

    /// <summary><c>DEFAULT_ORNAMENT_EXPLODE_RADIUS</c> (<c>tf_weapon_bat.cpp:31</c>): the ornament's blast.</summary>
    public const float OrnamentExplodeRadius = 50f;

    /// <summary>
    /// <c>DEFAULT_ORNAMENT_EXPLODE_DAMAGE_MULT</c> (<c>tf_weapon_bat.cpp:32</c>):
    /// the blast is this share of the shot (<c>:1312</c>).
    /// </summary>
    public const float OrnamentExplodeDamageMult = 0.9f;

    /// <summary><c>FLIGHT_TIME_TO_MAX_STUN</c> (<c>tf_weapon_bat.cpp:708</c>): past this aloft, the hit is a crit.</summary>
    public const float OrnamentLongFlight = 0.8f;

    /// <summary>The ornament's bleed (<c>tf_weapon_bat.cpp:1172</c>): five seconds on the struck player.</summary>
    public const float OrnamentBleedSeconds = 5f;

    /// <summary>
    /// The ornament's aloft time (<c>CTFStunBall::Spawn</c>'s context think,
    /// <c>tf_weapon_bat.cpp:672</c>): fifteen seconds and <c>SUB_Remove</c>,
    /// struck or not.
    /// </summary>
    public const float OrnamentLifetime = 15f;

    /// <summary>The ornament's model (<c>TF_WEAPON_BALL_ORNAMENT_WORLD_MODEL</c>, <c>tf_weapon_bat.h</c>).</summary>
    public const string OrnamentModel = "models/weapons/c_models/c_xms_festive_ornament.mdl";

    /// <summary>
    /// The ornament's flight, the grenade base's own numbers
    /// (<c>CTFWeaponBaseGrenadeProj::InitGrenade</c>,
    /// <c>tf_weaponbase_grenadeproj.cpp:245-247</c>): gravity 0.4, friction 0.2.
    /// </summary>
    public const float OrnamentGravity = 0.4f;
    public const float OrnamentFriction = 0.2f;

    /// <summary>
    /// <c>GetBallDynamics</c>'s throw direction (<c>tf_weapon_bat.cpp:240-242</c>):
    /// ten forward parts to one up, normalized, at
    /// <see cref="OrnamentSpeed"/> — a lob, not a shot.
    /// </summary>
    public const float OrnamentAimForward = 10f;
    public const float OrnamentAimUp = 1f;

    /// <summary>
    /// <c>GetBallDynamics</c>' birth (<c>tf_weapon_bat.cpp:236</c>): 50 up of the
    /// feet and 32 along the sight, at scale 1 — the eye stands 64 over the
    /// feet, so the seam's eye-anchored birth takes the remainder
    /// (<see cref="TFProjectileEffects.Born"/>'s note).
    /// </summary>
    public const float OrnamentMuzzleForward = 32f;
    public const float OrnamentMuzzleUp = -14f;

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

    /// <summary>
    /// The grenade-launcher modes (<c>tf_weapon_grenade_pipebomb.h:22-27</c>):
    /// what <c>CTFGrenadePipebombProjectile::Create</c> translates a projectile
    /// type into before the spawn (:437-458) — the sticky entity is born
    /// remote, the practice and cannonball launches stamped over.
    /// </summary>
    public const int GrenadeModeRegular = 0;
    public const int GrenadeModeRemoteDetonate = 1;
    public const int GrenadeModeRemoteDetonatePractice = 2;
    public const int GrenadeModeCannonball = 3;

    /// <summary>Create's translation (<c>tf_weapon_grenade_pipebomb.cpp:437-458</c>).</summary>
    public static int PipebombMode(TFProjectile projectile) => projectile switch
    {
        TFProjectile.PipebombRemote => GrenadeModeRemoteDetonate,
        TFProjectile.PipebombPractice => GrenadeModeRemoteDetonatePractice,
        TFProjectile.Cannonball => GrenadeModeCannonball,
        _ => GrenadeModeRegular,
    };

    /// <summary>
    /// The pipebomb launcher's <c>set_detonate_mode</c> values
    /// (<c>tf_weapon_pipebomblauncher.h:23-25</c>), read by
    /// <c>CTFGrenadeLauncher::GetDetonateMode</c>
    /// (<c>tf_weapon_grenadelauncher.cpp:481-486</c>): the detonate arm
    /// (<see cref="TFProjectileEffects.Detonate"/>) and, at AIR, the
    /// launched pipe's wall shatter.
    /// </summary>
    public const int DetonateModeStandard = 0;
    public const int DetonateModeDot = 1;
    public const int DetonateModeAir = 2;

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
    /// <c>RemapValClamped(charge, 0, GetChargeMaxTime, 900, 2400)</c> — the
    /// window <see cref="TFProjectileBehaviour.MaxChargeTime"/> stocks, the
    /// launcher's <c>stickybomb_charge_rate</c> hook bends.
    /// </summary>
    public static float StickySpeed(float charge, float chargeMax = TFProjectileBehaviour.MaxChargeTime)
        => TFRadiusDamage.RemapClamped(charge, 0f, chargeMax,
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
    /// The arrow's — and the healing bolt's — muzzle offset
    /// (<c>CTFWeaponBaseGun::FireArrow</c>'s <c>vecOffset</c>,
    /// <c>tf_weaponbase_gun.cpp:784</c>): no ducked height, stock's arrow
    /// spawn reads none.
    /// </summary>
    public const float ArrowMuzzleForward = 23.5f;
    public const float ArrowMuzzleRight = -8f;
    public const float ArrowMuzzleUp = -3f;

    /// <summary>
    /// The crossbow's flight, its fixed charge answer: stock's
    /// <c>CTFCrossbow::GetProjectileSpeed</c> and <c>.GetProjectileGravity</c>
    /// (<c>tf_weapon_rocketlauncher.cpp:751-762</c>) remap the constant 0.75 —
    /// "Temp, if we want to ramp" — so the speed is
    /// <c>RemapValClamped(0.75, 0, 1, 1800, 2600)</c> and the gravity
    /// <c>RemapValClamped(0.75, 0, 1, 0.5, 0.1)</c>.
    /// </summary>
    public const float CrossbowSpeed = 2400f;
    public const float CrossbowGravity = 0.2f;

    /// <summary>
    /// The Rescue Ranger's bolt, <c>MODEL_ARROW_BUILDING_REPAIR</c>'s spelling
    /// (<c>g_pszArrowModels</c>, <c>tf_shareddefs.cpp:1145</c>): the claw the
    /// <c>TF_PROJECTILE_BUILDING_REPAIR_BOLT</c> spawn dresses
    /// (<c>tf_projectile_arrow.cpp:204-207</c>).
    /// </summary>
    public const string RepairClawModel = "models/weapons/w_models/w_repair_claw.mdl";

    /// <summary>The claw's mend particle, per team (<c>CLAW_REPAIR_EFFECT_</c>, <c>tf_projectile_arrow.cpp:50-51</c>).</summary>
    public const string RepairClawHealRed = "repair_claw_heal_red";
    public const string RepairClawHealBlue = "repair_claw_heal_blue";

    /// <summary>
    /// The rescue bolt's flight (<c>CTFShotgunBuildingRescue::GetProjectileSpeed</c>
    /// and <c>.GetProjectileGravity</c>, <c>tf_weapon_shotgun.cpp:507-516</c>):
    /// the same constant-0.75 remap the crossbow's run, so the same answers —
    /// the aliases name the reader, the shared constants the number.
    /// </summary>
    public const float RescueBoltSpeed = CrossbowSpeed;
    public const float RescueBoltGravity = CrossbowGravity;

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

    /// <summary><c>TF_WEAPON_PIPEBOMB2_MODEL</c> (<c>tf_weapon_grenade_pipebomb.cpp:396</c>): the practice bomb's shell.</summary>
    public const string StickyPracticeModel = "models/weapons/w_models/w_stickybomb2.mdl";

    /// <summary><c>TF_WEAPON_PIPEBOMBD_MODEL</c> (<c>tf_weapon_pipebomblauncher.cpp:31</c>): the defensive bomb's shell.</summary>
    public const string StickyDefensiveModel = "models/weapons/w_models/w_stickybomb_d.mdl";

    /// <summary><c>TF_WEAPON_CANNONBALL_MODEL</c> (<c>tf_weapon_grenade_pipebomb.cpp:394</c>).</summary>
    public const string CannonballModel = "models/weapons/w_models/w_cannonball.mdl";

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

    /// <summary>
    /// The energy family's launch: rings at the bison's 1200 and balls at the
    /// mangler's 1100 (<c>GetProjectileSpeed</c>, tf_weapon_raygun.cpp:214-217,
    /// tf_weapon_particle_cannon.cpp:72-75), both flying flat
    /// (<c>GetProjectileGravity</c>, :222-225, :80-83). Stock draws no model —
    /// the ring and ball fly as their particle colours, so the flying entity's
    /// own model is the empty one.
    /// </summary>
    public const float EnergyRingSpeed = 1200f;
    public const float EnergyBallSpeed = 1100f;
    public const float EnergyGravity = 0f;
    public const string EnergyModel = "";

    /// <summary>The energy shot's muzzle offset (<c>FireEnergyBall</c>'s <c>vecOffset</c>, tf_weaponbase_gun.cpp:557-561).</summary>
    public const float EnergyMuzzleForward = 23.5f;
    public const float EnergyMuzzleRight = -8f;
    public const float EnergyMuzzleUp = -3f;

    /// <summary>The energy shot's crouched muzzle height (:558-561).</summary>
    public const float EnergyMuzzleUpDucked = 8f;

    /// <summary><c>CTFBaseRocket</c>'s model (<c>models/weapons/w_models/w_rocket.mdl</c>).</summary>
    public const string RocketModel = "models/weapons/w_models/w_rocket.mdl";

    /// <summary><c>FLARE_MODEL</c> (<c>tf_projectile_flare.cpp:18</c>).</summary>
    public const string FlareModel = "models/weapons/w_models/w_flaregun_shell.mdl";

    /// <summary><c>FLARE_GRAVITY</c> (<c>tf_projectile_flare.cpp:19</c>).</summary>
    public const float FlareGravity = 0.3f;

    /// <summary><c>FLARE_SPEED</c> (<c>tf_projectile_flare.cpp:20</c>), the base of <c>GetProjectileSpeed</c> (:617-623).</summary>
    public const float FlareSpeed = 2000f;

    /// <summary>How long a world-struck flare sticks before it goes (<c>m_flImpactTime</c>, <c>tf_projectile_flare.cpp:356</c>).</summary>
    public const float FlareStickTime = 0.1f;

    /// <summary>
    /// <c>TF_FLARE_DET_RADIUS</c> (<c>tf_weaponbase_rocket.h:27</c>): the
    /// detonating flare's blast, the <c>mult_explosion_radius</c> hook over it
    /// (<c>GetRadius</c>, <c>tf_projectile_flare.cpp:452-457</c>).
    /// </summary>
    public const float FlareDetRadius = 110f;

    /// <summary>
    /// <c>TF_FLARE_RADIUS_FOR_FJS</c> (<c>tf_weaponbase_rocket.h:28</c>): the
    /// radius the flare's own pass always runs at (<c>Explode_Air</c>'s
    /// <c>CTFRadiusDamageInfo</c>, <c>tf_projectile_flare.cpp:420</c>).
    /// </summary>
    public const float FlareJumpRadius = 100f;

    /// <summary>
    /// The flare gun's muzzle offset (<c>FireFlare</c>'s <c>vecOffset</c>,
    /// <c>tf_weaponbase_gun.cpp:746</c>) — the rocket's numbers again.
    /// </summary>
    public const float FlareMuzzleForward = 23.5f;
    public const float FlareMuzzleRight = 12f;
    public const float FlareMuzzleUp = -3f;

    /// <summary>The flare's crouched muzzle height (<c>GetFlags() &amp; FL_DUCKING</c>, :747-750).</summary>
    public const float FlareMuzzleUpDucked = 8f;

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

    /// <summary>The jars' models: <c>TF_WEAPON_PEEJAR_MODEL</c> and <c>TF_WEAPON_MILKJAR_MODEL</c> (<c>tf_weapon_jar.cpp:86, 88</c>), the spawned jar's <c>SetModel</c>s (:283, :854).</summary>
    public const string JarModel = "models/weapons/c_models/urinejar.mdl";
    public const string JarMilkModel = "models/workshop/weapons/c_models/c_madmilk/c_madmilk.mdl";

    /// <summary>
    /// The faces the held item's <c>override_projectile_type</c> picks
    /// (<c>SetCustomPipebombModel</c>, <c>tf_weapon_jar.cpp:261-284</c>, the
    /// milk's own :836-855): the festive jarate and bread-monster strings
    /// stock's switch sets (:87, :278, :849).
    /// </summary>
    public const string JarFestiveModel = "models/weapons/c_models/c_xms_urinejar.mdl";
    public const string JarBreadmonsterModel = "models/weapons/c_models/c_breadmonster/c_breadmonster.mdl";
    public const string JarMilkBreadmonsterModel = "models/weapons/c_models/c_breadmonster/c_breadmonster_milk.mdl";

    /// <summary><c>TF_JAR_LAUNCH_SPEED</c> (<c>tf_weapon_jar.h:31</c>), the base of <c>GetProjectileSpeed</c> (:103-109).</summary>
    public const float JarSpeed = 1000f;

    /// <summary><c>JAR_EXPLODE_RADIUS</c> (<c>tf_weapon_jar.h:34</c>), the splashed radius.</summary>
    public const float JarRadius = 200f;

    /// <summary>The jar's seconds on a victim: <c>JarExplode</c>'s <c>AddCond(·, 10.0f, ·)</c> (<c>tf_weapon_jar.cpp:479</c>).</summary>
    public const float JarDuration = 10f;

    /// <summary>The sniper rifle's jarate splash (<c>TE_TFParticleEffect</c>'s "peejar_impact_small", tf_player.cpp:10689).</summary>
    public const string JarRifleImpactEffect = "peejar_impact_small";

    /// <summary>The jar's spin: <c>GetAngularImpulse</c>'s roll about x (<c>tf_weapon_jar.h:66</c>).</summary>
    public const float JarSpinRoll = 300f;

    /// <summary>The impact particle each face sheds (<c>JarExplode</c>'s effect, <c>tf_weapon_jar.cpp:458-462</c>).</summary>
    public const string JarImpactEffect = "peejar_impact";
    public const string JarMilkImpactEffect = "peejar_impact_milk";

    /// <summary><c>TF_WEAPON_PEEJAR_EXPLODE_SOUND</c> (<c>tf_weapon_jar.h:35</c>), broadcast at the break.</summary>
    public const string JarExplodeSound = "Jar.Explode";
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
