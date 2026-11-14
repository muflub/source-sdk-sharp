//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

/// <summary>
/// Which TF behaviour runs a weapon: the stock class hierarchy collapsed to
/// the behaviours this port ships (plan §2 point 4: behaviours, not classes).
/// A row's <see cref="SourceSharp.Shared.WeaponType.Behaviour"/> is the kind's key.
/// </summary>
public enum TFWeaponKind
{
    /// <summary>A hitscan gun that reloads its whole clip (pistol, SMG, revolver).</summary>
    Gun,
    /// <summary>A hitscan gun that reloads one shell at a time (scattergun, the shotguns).</summary>
    Shotgun,
    Minigun,
    SniperRifle,
    /// <summary>
    /// The Classic (<c>CTFSniperRifleClassic</c>, tf_weapon_sniperrifle.cpp:1764):
    /// the sniper rifle that charges on the press and fires on the release
    /// (<see cref="TFSniperRifleClassicBehaviour"/>).
    /// </summary>
    SniperRifleClassic,
    /// <summary>
    /// The spy's revolver (<c>CTFRevolver</c>): the gun with the accuracy
    /// clock — <c>PrimaryAttack</c> stamps it and <c>GetWeaponCrosshairScale</c>
    /// blooms the crosshair off its age under the item's <c>set_weapon_mode</c>
    /// 1 (<c>tf_weapon_revolver.cpp:157, 207-223</c>).
    /// </summary>
    Revolver,
    Melee,
    /// <summary>A launcher whose shot is a projectile entity (Stage 2.8): rockets, pipes, stickies, syringes.</summary>
    Projectile,
    /// <summary>
    /// The compound bow (<c>CTFCompoundBow : CTFPipebombLauncher</c>,
    /// tf_weapon_compound_bow.h:20): held to draw, released to loose an arrow
    /// at the charge's speed and damage.
    /// </summary>
    Bow,
    Flamethrower,
    /// <summary>
    /// The Dragon's Fury (<c>CTFWeaponFlameBall : CTFFlameThrower</c>,
    /// tf_weapon_dragons_fury.h:27): the held fire is a ball on the full
    /// charge meter and the blast is the flamethrower's, gated and scaled by
    /// the meter's own recharge (<see cref="TFFlameBallBehaviour"/>).
    /// </summary>
    FlameBall,
    Medigun,
    /// <summary>
    /// The lunchbox family (<c>CTFLunchBox</c>): the primary taunt-eats and the
    /// secondary throws the plate (<c>tf_weapon_lunchbox.cpp</c>).
    /// </summary>
    Lunchbox,
    /// <summary>A PDA (<c>WeaponType "pda"</c>): its trigger hands the owner back to the previous item.</summary>
    Pda,
    /// <summary>
    /// The builder family (<c>CTFWeaponBuilder</c>): the click-to-place half
    /// of build — the blueprint out at the crosshair, +attack placing it,
    /// +attack2 turning it (<c>tf_weapon_builder.cpp</c>). The sapper is the
    /// family's attachment arm — the same shell carrying the class's buildable
    /// (<c>ManageBuilderWeapons</c>' retarget, <c>SetSubType</c>,
    /// <c>tf_player.cpp:4309-4316, 4627-4631</c>) — and its place is its own
    /// use, the sapper seam (<see cref="TFWeaponParts.Sapper"/>).
    /// </summary>
    Builder,
    /// <summary>
    /// The soldier's buff item (<c>CTFBuffItem</c>): +attack with a full rage
    /// meter deploys the item's buff to the team in reach
    /// (<c>tf_weapon_buff_item.cpp:116-135</c>,
    /// <c>tf_player_shared.cpp:13753-13796</c>).
    /// </summary>
    BuffItem,
    /// <summary>
    /// The Thermal Thruster (<c>CTFRocketPack</c>, tf_weapon_rocketpack.cpp):
    /// the press pops its owner off the ground and lands the flight force at
    /// the launch delay, spending half the item meter
    /// (<see cref="TFRocketPackBehaviour"/>).
    /// </summary>
    RocketPack,
    /// <summary>
    /// The energy-weapon family (<c>CTFRaygun</c> and kin): no ammo pool —
    /// energy in whole shots (<c>Energy_GetMaxEnergy</c>,
    /// <c>tf_weaponbase.cpp:6357-6416</c>), spent per shot and recharged on the
    /// weapon-idle ticks. The bison, the pomson and the Cow Mangler.
    /// </summary>
    Energy,
    /// <summary>A weapon with no attack of its own (the invis watch).</summary>
    Inert,
    /// <summary>
    /// The Wrangler (<c>CTFLaserPointer : CTFWeaponBaseGun</c>,
    /// tf_weapon_laser_pointer.h:47): no shot of its own — the triggers
    /// drive the builder's sentry (<see cref="TFWranglerBehaviour"/>).
    /// </summary>
    LaserPointer,
}

/// <summary>The kind of each TF weapon, and the behaviour keys the rows name.</summary>
public static class TFWeaponKinds
{
    /// <summary>The <see cref="SourceSharp.Shared.WeaponType.Behaviour"/> key of a kind.</summary>
    public static string Key(TFWeaponKind kind) => kind switch
    {
        TFWeaponKind.Gun => "tf2.gun",
        TFWeaponKind.Shotgun => "tf2.shotgun",
        TFWeaponKind.Minigun => "tf2.minigun",
        TFWeaponKind.SniperRifle => "tf2.sniperrifle",
        TFWeaponKind.SniperRifleClassic => "tf2.sniperrifle_classic",
        TFWeaponKind.Revolver => "tf2.revolver",
        TFWeaponKind.Melee => "tf2.melee",
        TFWeaponKind.Projectile => "tf2.projectile",
        TFWeaponKind.Bow => "tf2.bow",
        TFWeaponKind.Flamethrower => "tf2.flamethrower",
        TFWeaponKind.FlameBall => "tf2.flameball",
        TFWeaponKind.Medigun => "tf2.medigun",
        TFWeaponKind.Lunchbox => "tf2.lunchbox",
        TFWeaponKind.Pda => "tf2.pda",
        TFWeaponKind.Builder => "tf2.builder",
        TFWeaponKind.BuffItem => "tf2.buff",
        TFWeaponKind.RocketPack => "tf2.rocketpack",
        TFWeaponKind.Energy => "tf2.energy",
        TFWeaponKind.Inert => "tf2.inert",
        TFWeaponKind.LaserPointer => "tf2.laserpointer",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The kind of the weapon a script describes: by its weapon id where the
    /// stock class has behaviour of its own, else by the script's weapon type
    /// (a melee script is a melee weapon, a bullet gun is a gun).
    /// </summary>
    public static TFWeaponKind Of(TFWeaponScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        switch (script.Id)
        {
            // The wrench family: the Gunslinger's item2 script is a wrench all the same.
            case TFWeaponId.Wrench:
                return TFWeaponKind.Melee;
            case TFWeaponId.Scattergun:
            // The Soda Popper and the Baby Face's Blaster: CTFScatterGun
            // subclasses of their own (tf_weapon_shotgun.h:154, :170), the
            // hype pair.
            case TFWeaponId.SodaPopper:
            case TFWeaponId.PepBrawlerBlaster:
            case TFWeaponId.ShotgunPrimary:
            case TFWeaponId.ShotgunSoldier:
            case TFWeaponId.ShotgunHwg:
            case TFWeaponId.ShotgunPyro:
                return TFWeaponKind.Shotgun;
            // The Rescue Ranger: CTFShotgunBuildingRescue : CTFShotgun
            // (tf_weapon_shotgun.h:183), the shotgun shell behind the bolt.
            case TFWeaponId.ShotgunBuildingRescue:
                return TFWeaponKind.Projectile;
            case TFWeaponId.Minigun:
                return TFWeaponKind.Minigun;
            case TFWeaponId.Sniperrifle:
            // The Bazaar Bargain: the decapitation rifle, the base charge arm
            // with the head count's rate (CTFSniperRifleDecap's
            // SniperRifleChargeRateMod, tf_weapon_sniperrifle.cpp:1742-1745).
            case TFWeaponId.SniperrifleDecap:
                return TFWeaponKind.SniperRifle;
            // The Classic: the release-fire rifle of its own class
            // (CTFSniperRifleClassic, tf_weapon_sniperrifle.cpp:1764).
            case TFWeaponId.SniperrifleClassic:
                return TFWeaponKind.SniperRifleClassic;
            case TFWeaponId.Revolver:
                return TFWeaponKind.Revolver;
            case TFWeaponId.Rocketlauncher:
            // The Direct Hit's jump rockets: the stock jump weapon rides the
            // rocket launcher's firing (CTFWeaponJumpRocketLauncher).
            case TFWeaponId.RocketlauncherDirecthit:
            case TFWeaponId.Grenadelauncher:
            case TFWeaponId.Pipebomblauncher:
            case TFWeaponId.SyringegunMedic:
            case TFWeaponId.Flaregun:
            // The Short Circuit: CTFMechanicalArm : CTFWeaponBaseGun
            // (tf_weapon_mechanical_arm.h), the engineer's secondary whose
            // shot is the orb entity its own secondary throws.
            case TFWeaponId.MechanicalArm:
                return TFWeaponKind.Projectile;
            // The Manmelter: CTFFlareGun_Revenge : CTFFlareGun
            // (tf_weapon_flaregun.h), the vacuum riding the flare gun's shell.
            case TFWeaponId.FlaregunRevenge:
            // The jars (CTFJar : CTFWeaponBaseGun, tf_weapon_jar.h:41): a
            // grenade lobbed at the meter, the milk its second face.
            case TFWeaponId.Jar:
            case TFWeaponId.JarMilk:
            // The Flying Guillotine and its Promo skin: the scout's secondary
            // throwing the bouncing cleaver (tf_projectile_cleaver) at the
            // effect-bar meter — the jar pair's shell, the meter's charge
            // the cleaver's own (TFItemMeter.EffectBarSeconds).
            case TFWeaponId.Cleaver:
            // The Crusader's Crossbow: CTFCrossbow : CTFRocketLauncher
            // (tf_weapon_rocketlauncher.cpp:670), the launcher's projectile
            // behaviour behind the healing bolt.
            case TFWeaponId.Crossbow:
                return TFWeaponKind.Projectile;
            case TFWeaponId.CompoundBow:
                return TFWeaponKind.Bow;
            case TFWeaponId.Flamethrower:
                return TFWeaponKind.Flamethrower;
            // The Dragon's Fury: the flame's shell behind the meter gates
            // (CTFWeaponFlameBall : CTFFlameThrower, tf_weapon_dragons_fury.h:27).
            case TFWeaponId.FlameBall:
                return TFWeaponKind.FlameBall;
            case TFWeaponId.Medigun:
                return TFWeaponKind.Medigun;
            case TFWeaponId.Builder:
                return TFWeaponKind.Builder;
            case TFWeaponId.Lunchbox:
                return TFWeaponKind.Lunchbox;
            case TFWeaponId.BuffItem:
                return TFWeaponKind.BuffItem;
            // The Thermal Thruster: CTFRocketPack, its own ItemPostFrame that
            // fires no shot (tf_weapon_rocketpack.cpp:752-841).
            case TFWeaponId.Rocketpack:
                return TFWeaponKind.RocketPack;
            // The Wrangler: the engineer's unlock, held only as an equipped
            // item — the triggers command the sentry (CTFLaserPointer's
            // overrides, tf_weapon_laser_pointer.cpp:192-237), no shot of
            // the pointer's own.
            case TFWeaponId.LaserPointer:
                return TFWeaponKind.LaserPointer;
            // The energy family: the raygun (the bison's ring) and its pomson
            // subclass (CTFDRGPomson : CTFRaygun, tf_weapon_raygun.h:84), and
            // the Cow Mangler's charged ball (CTFParticleCannon,
            // tf_weapon_particle_cannon.cpp). Energy, not the ammo pool:
            // UsesPrimaryAmmo is false (tf_weaponbase.cpp:640).
            case TFWeaponId.Raygun:
            case TFWeaponId.DrgPomson:
            case TFWeaponId.ParticleCannon:
                return TFWeaponKind.Energy;
        }

        return script.Category switch
        {
            TFWeaponCategory.Melee => TFWeaponKind.Melee,
            TFWeaponCategory.Primary or TFWeaponCategory.Secondary
                when script.Primary.Projectile == TFProjectile.Bullet => TFWeaponKind.Gun,
            // Stock's GetWeaponId answers TF_WEAPON_NONE for a script name
            // outside g_aWeaponNames (tf_shareddefs.cpp:1211-1217) and the
            // weapon still fires what its script declares: a bullet gun the
            // arm above, a launcher (its ProjectileType names the shot, as
            // the Air Strike's projectile_rocket does for the soldier's
            // airdrop primary, whose class inherits the rocket launcher —
            // CTFRocketLauncher_AirStrike : CTFRocketLauncher,
            // tf_weapon_rocketlauncher.h:109) the projectile shell.
            TFWeaponCategory.Primary or TFWeaponCategory.Secondary => TFWeaponKind.Projectile,
            // The builder family's weapon type: the sapper's script, whose id
            // is none — the family's shell carries its place (the sapper seam).
            TFWeaponCategory.Building => TFWeaponKind.Builder,
            TFWeaponCategory.Pda => TFWeaponKind.Pda,
            _ => TFWeaponKind.Inert,
        };
    }

    /// <summary>
    /// Whether the weapon reloads one round at a time (<c>m_bReloadsSingly</c>,
    /// set by the shotgun, rocket, grenade and stickybomb launcher constructors:
    /// <c>tf_weapon_shotgun.cpp:68</c>, <c>tf_weapon_rocketlauncher.cpp:159</c>,
    /// <c>tf_weapon_grenadelauncher.cpp:100</c>,
    /// <c>tf_weapon_pipebomblauncher.cpp:86</c>). The crossbow inherits the
    /// rocket launcher's flag with its class
    /// (<c>CTFCrossbow : CTFRocketLauncher</c>, tf_weapon_rocketlauncher.h:166).
    /// </summary>
    public static bool ReloadsSingly(TFWeaponId id) => id is TFWeaponId.Scattergun
        or TFWeaponId.ShotgunPrimary or TFWeaponId.ShotgunSoldier or TFWeaponId.ShotgunHwg or TFWeaponId.ShotgunPyro
        or TFWeaponId.ShotgunBuildingRescue
        or TFWeaponId.Rocketlauncher or TFWeaponId.RocketlauncherDirecthit
        or TFWeaponId.Grenadelauncher or TFWeaponId.Pipebomblauncher
        or TFWeaponId.Crossbow;

    /// <summary>
    /// The same answer for a script whose id is none: stock decides it by the
    /// C++ class, and a class outside <c>g_aWeaponNames</c> still inherits its
    /// base's flag — the Air Strike's <c>CTFRocketLauncher_AirStrike</c> takes
    /// the rocket launcher's <c>m_bReloadsSingly = true</c> with its class
    /// (<c>tf_weapon_rocketlauncher.h:109</c> over the ctor's :159). Here the
    /// class shows through the script's shape: a none-id mode that fires
    /// rockets is the launcher family, and reloads one round at a time. A
    /// none-id bullet gun keeps the base's whole-clip answer (the sapper's
    /// shell, whose melee-class base never sets the flag).
    /// </summary>
    public static bool ReloadsSingly(TFWeaponScript script) =>
        ReloadsSingly(script.Id)
        || script.Id == TFWeaponId.None && script.Primary.Projectile == TFProjectile.Rocket;
}
