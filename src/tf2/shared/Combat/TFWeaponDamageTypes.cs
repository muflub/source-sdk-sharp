//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared;

namespace TF2.Shared.Combat;

/// <summary>
/// The damage type each weapon deals: <c>g_aWeaponDamageTypes</c>
/// (<c>tf_shareddefs.cpp:715-828</c>), one <c>DMG_*</c> set per
/// <c>TF_WEAPON_*</c> in the same order as the <see cref="TFWeaponId"/>
/// enum, read by <c>CTFWeaponBase::GetDamageType</c>. A hit from a TF weapon
/// carries these bits — the rocket's blast falls off by half
/// (<c>DMG_HALF_FALLOFF</c>), the shotgun's point blank is not damped (no
/// <c>DMG_NOCLOSEDISTANCEMOD</c>), the flamethrower's hit lights fires — so
/// the damage layer reads the weapon's own set over whatever the caller
/// passed, and the projectiles hand their blasts theirs.
///
/// <para>
/// A switch, not a table: managed code keeps no state in statics. The oracle
/// fact (<c>TFWeaponDamageTypesTests</c>) holds every entry against the
/// pristine file, positionally against the enum.
/// </para>
/// </summary>
public static class TFWeaponDamageTypes
{
    /// <summary>The weapon's own bits (<c>g_aWeaponDamageTypes[weapon]</c>).</summary>
    public static DamageType Of(TFWeaponId weapon) => weapon switch
    {
        TFWeaponId.Bat or TFWeaponId.BatWood or TFWeaponId.Bottle or TFWeaponId.Fireaxe
            or TFWeaponId.Club or TFWeaponId.Crowbar or TFWeaponId.Fists or TFWeaponId.Shovel
            or TFWeaponId.Wrench or TFWeaponId.GrenadeStunball or TFWeaponId.GrenadeJar
            or TFWeaponId.GrenadeJarMilk or TFWeaponId.PumpkinBomb or TFWeaponId.Sword
            or TFWeaponId.Lifeline or TFWeaponId.LaserPointer or TFWeaponId.BatFish
            or TFWeaponId.Stickbomb or TFWeaponId.BatGiftwrap or TFWeaponId.GrenadeOrnamentBall
            or TFWeaponId.BreakableSign or TFWeaponId.Slap
            => DamageType.Club,

        TFWeaponId.Knife or TFWeaponId.Bonesaw or TFWeaponId.GrenadeCleaver
            or TFWeaponId.HarvesterSaw
            => DamageType.Slash,

        TFWeaponId.ShotgunPrimary or TFWeaponId.ShotgunSoldier or TFWeaponId.ShotgunHwg
            or TFWeaponId.ShotgunPyro or TFWeaponId.Scattergun or TFWeaponId.SentryRevenge
            or TFWeaponId.SodaPopper or TFWeaponId.PepBrawlerBlaster
            or TFWeaponId.ShotgunBuildingRescue
            => DamageType.Buckshot | TFDamageBits.UseDistanceMod,

        TFWeaponId.Sniperrifle or TFWeaponId.CompoundBow or TFWeaponId.Crossbow
            or TFWeaponId.SniperrifleDecap or TFWeaponId.SniperrifleClassic
            => DamageType.Bullet | TFDamageBits.UseHitLocations,

        TFWeaponId.Minigun or TFWeaponId.Smg or TFWeaponId.Pistol or TFWeaponId.PistolScout
            or TFWeaponId.Revolver or TFWeaponId.MechanicalArm or TFWeaponId.HandgunScoutSecondary
            or TFWeaponId.ChargedSmg
            => DamageType.Bullet | TFDamageBits.UseDistanceMod,

        TFWeaponId.SyringegunMedic or TFWeaponId.Nailgun or TFWeaponId.Raygun
            or TFWeaponId.DrgPomson
            => DamageType.Bullet | TFDamageBits.UseDistanceMod | TFDamageBits.NoCloseDistanceMod
               | DamageType.PreventPhysicsForce,

        TFWeaponId.Tranq
            => DamageType.Bullet | TFDamageBits.UseDistanceMod | DamageType.PreventPhysicsForce
               | DamageType.Paralyze,

        TFWeaponId.Rocketlauncher or TFWeaponId.Grenadelauncher
            or TFWeaponId.RocketlauncherDirecthit or TFWeaponId.ParticleCannon
            => DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod,

        TFWeaponId.Pipebomblauncher or TFWeaponId.GrenadePipebomb
            => DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.NoCloseDistanceMod,

        TFWeaponId.Flamethrower => TFDamageBits.Ignite | DamageType.PreventPhysicsForce,

        TFWeaponId.GrenadeNormal or TFWeaponId.GrenadeMirv or TFWeaponId.GrenadeMirvDemoman
            or TFWeaponId.GrenadeDemoman or TFWeaponId.Cannon
            => DamageType.Blast | TFDamageBits.HalfFalloff,

        TFWeaponId.GrenadeConcussion => DamageType.Sonic | TFDamageBits.HalfFalloff,

        TFWeaponId.GrenadeNail => DamageType.Bullet | TFDamageBits.HalfFalloff,

        TFWeaponId.GrenadeNapalm => DamageType.Burn | TFDamageBits.RadiusMax,

        TFWeaponId.GrenadeGas => DamageType.Poison | TFDamageBits.HalfFalloff,

        TFWeaponId.GrenadeEmp => DamageType.Blast | TFDamageBits.HalfFalloff
                                 | DamageType.PreventPhysicsForce,

        TFWeaponId.GrenadeMirvbomb => DamageType.Blast,

        TFWeaponId.FlamethrowerRocket => DamageType.Blast | TFDamageBits.Ignite
                                         | TFDamageBits.RadiusMax,

        TFWeaponId.SentryBullet => DamageType.Bullet,

        TFWeaponId.SentryRocket => DamageType.Blast,

        TFWeaponId.Flaregun or TFWeaponId.FlaregunRevenge
            => DamageType.Bullet | TFDamageBits.Ignite,

        TFWeaponId.HandgunScoutPrimary
            => DamageType.Buckshot | DamageType.Bullet | TFDamageBits.UseDistanceMod,

        TFWeaponId.Throwable or TFWeaponId.GrenadeThrowable or TFWeaponId.PdaSpyBuild
            or TFWeaponId.GrenadeWaterballoon or TFWeaponId.DispenserGun
            => DamageType.Bullet,

        TFWeaponId.Pda or TFWeaponId.PdaEngineerBuild or TFWeaponId.PdaEngineerDestroy
            or TFWeaponId.PdaSpy or TFWeaponId.Builder or TFWeaponId.Medigun
            => DamageType.Bullet,

        TFWeaponId.FlameBall => DamageType.Generic | DamageType.PreventPhysicsForce,

        // TF_WEAPON_NONE and every weapon stock deals DMG_GENERIC: the
        // caltrop, smoke bomb, heal grenade, jar, lunch box, dispenser, invis,
        // buff item, cleaver, spellbook, parachute, grappling hook, PASS Time
        // gun, rocket pack and the gas jars.
        _ => DamageType.Generic,
    };
}
