//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What one weapon does when it is fired.
///
/// Damage per pellet, not per shot — a shotgun that did its damage once would
/// be a rifle with a spread pattern drawn on it, and the difference shows the
/// moment somebody is hit by half a cone.
/// </summary>
/// <param name="Name">The entity classname, which is how a weapon is identified everywhere else.</param>
/// <param name="Interval">Seconds between shots — the fire rate the stock attack path reads.</param>
/// <param name="Pellets">
/// Bullets per trigger pull. One for everything that is not a shotgun, and it
/// costs one ROUND however many pellets it fires — a shotgun that spent seven
/// would empty its clip in a single pull.
/// </param>
/// <param name="Spread">
/// Half-angle of the cone, in degrees: half of stock's VECTOR_CONE_N DEGREES
/// (the SMG1's 5 DEGREES is 2.5 here). Zero fires perfectly straight, as the
/// 357's vec3_origin does; a behaviour may answer its own (the pistol's).
/// </param>
public readonly record struct WeaponType(
    string Name,
    float Interval,
    int Pellets = 1,
    float Spread = 0f)
{
    /// <summary>
    /// The entity this weapon throws instead of firing bullets, or empty for a
    /// hitscan weapon.
    ///
    /// A weapon is one or the other, not both: a grenade launcher that also
    /// fired a bullet down the middle would be two weapons wearing one model.
    /// </summary>
    public string Projectile { get; init; } = "";

    /// <summary>How fast it leaves the hand, in units per second.</summary>
    public float LaunchSpeed { get; init; }

    /// <summary>
    /// Whether what it launches is thrown or aimed.
    ///
    /// A thrown projectile is lofted and shoved: it goes roughly where you are
    /// looking, arcs, and the arc is the point — a grenade thrown flat at the
    /// floor is a grenade dropped at your feet. An aimed one leaves along the
    /// exact line of sight at a fixed speed, because a bolt that landed a
    /// little below the crosshair is a broken crossbow rather than a heavy one.
    ///
    /// <para>
    /// It also decides HOW it is set moving. A thrown projectile is a physics
    /// prop and takes an impulse; an aimed one flies under
    /// <see cref="ScriptedEntityKind.Projectile"/> and has its velocity set
    /// outright. Shoving the second would be applying an impulse to a physics
    /// object it does not have, which is silently nothing at all.
    /// </para>
    /// </summary>
    public bool Thrown { get; init; }

    /// <summary>
    /// What the other trigger does, or null for a weapon with one.
    ///
    /// Most weapons have none, and that is the honest default rather than an
    /// omission: a pistol's right mouse button does nothing in Half-Life
    /// either.
    /// </summary>
    public SecondaryFire? Secondary { get; init; }

    //-------------------------------------------------------------------------
    // What used to come from scripts/weapon_<name>.txt (Phase 10).
    //
    // The scripts were the C++ weapon class's way of getting per-name data into
    // one generic shell. The hierarchy weapon reads THIS table on both sides
    // instead — the same one-list rule as the ammo table and the fire rates —
    // so the script files stop being an authority. Values below are quoted from
    // the shipped scripts, not invented.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Rounds a full clip holds, or -1 for a weapon with no clip — which fires
    /// straight from its reserve pool (frag, RPG) or needs none (crowbar).
    /// </summary>
    public int ClipSize { get; init; } = -1;

    /// <summary>
    /// The ammo this weapon's primary fire spends, by the name
    /// <see cref="AmmoTypes"/> registered — or empty for a melee weapon.
    /// </summary>
    public string AmmoName { get; init; } = "";

    /// <summary>The pool the alt-fire draws on, when it has one of its own.</summary>
    public string SecondaryAmmoName { get; init; } = "";

    /// <summary>The v_ model the local carrier sees.</summary>
    public string ViewModel { get; init; } = "";

    /// <summary>The w_ model everyone else sees, and the pickup on the ground.</summary>
    public string WorldModel { get; init; } = "";

    /// <summary>Seconds after a deploy before the first shot may fire.</summary>
    public float DeployTime { get; init; } = 0.5f;

    /// <summary>Soundscript entries, from the script's SoundData block.</summary>
    public string FireSound { get; init; } = "";
    public string EmptySound { get; init; } = "";
    public string ReloadSound { get; init; } = "";

    /// <summary>
    /// A melee weapon's reach and per-hit damage. Melee is the one attack whose
    /// damage is not the ammunition's, because there is no ammunition.
    /// </summary>
    public float MeleeRange { get; init; }
    public float MeleeDamage { get; init; }

    /// <summary>
    /// What a swing that LANDS sounds like, or empty for none: the script's
    /// melee_hit, which stock plays from <c>CBaseHL2MPBludgeonWeapon::Hit</c>
    /// only for a weapon whose <c>PlayFleshyHittySoundOnHit</c> answers true.
    /// The stunstick's does; the crowbar's does not, so its row leaves this
    /// empty although its script names a melee_hit.
    /// </summary>
    public string MeleeHitSound { get; init; } = "";

    /// <summary>
    /// The client effect a landed swing dispatches at the point it struck, or
    /// empty for none — the stunstick's <c>StunstickImpact</c>, its glow quad
    /// and sparks (<c>CWeaponStunStick::ImpactEffect</c>). Dispatched beside
    /// the ordinary impact trace, not instead of it.
    /// </summary>
    public string MeleeImpactEffect { get; init; } = "";

    /// <summary>
    /// Played on the carrier when the weapon is drawn, or empty: the
    /// stunstick's <c>Weapon_StunStick.Activate</c>, from
    /// <c>CWeaponStunStick::Deploy</c>'s <c>SetStunState( true )</c>.
    /// </summary>
    public string DeploySound { get; init; } = "";

    /// <summary>
    /// Played on the carrier when the weapon is put away or dropped, or empty:
    /// the stunstick's <c>Weapon_StunStick.Deactivate</c>
    /// (<c>SetStunState( false )</c>, from both <c>Holster</c> and <c>Drop</c>).
    /// </summary>
    public string HolsterSound { get; init; } = "";

    /// <summary>
    /// Whether the weapon is sparked on its draw — the stunstick's
    /// <c>g_pEffects-&gt;Sparks</c> in <c>SetStunState( true )</c>.
    /// </summary>
    public bool SparksOnDeploy { get; init; }

    /// <summary>
    /// Whether a dropped one is simply gone instead of left as a pickup:
    /// <c>CWeaponStunStick::Drop</c> calls <c>UTIL_Remove( this )</c>, so a
    /// stunstick carried by somebody who dies leaves nothing behind.
    /// </summary>
    public bool RemovedOnDrop { get; init; }

    /// <summary>
    /// How much of the secondary pool a first pickup brings: the script's
    /// default_clip2, for a weapon with no secondary clip, which stock's
    /// <c>Weapon_Equip</c> hands straight to the pool (the SLAM's three). A
    /// duplicate pickup brings the same, as <c>Weapon_EquipAmmoOnly</c> does.
    /// </summary>
    public int DefaultSecondaryAmmo { get; init; }

    /// <summary>
    /// The script's default_clip: what a fresh weapon carries, which
    /// <c>Weapon_Equip</c> puts in the clip and, past the clip's size, in the
    /// pool (the crossbow's 1 + 4, the AR2's 30 + 30) -- all of it in the pool
    /// for a weapon with no clip (the RPG's 3). A duplicate pickup is worth the
    /// same (<c>Weapon_EquipAmmoOnly</c>). Zero is the clip's size, or one round
    /// for a weapon with no clip.
    /// </summary>
    public int DefaultClip { get; init; }

    /// <summary>
    /// Which machine runs the row: the generic one, or a weapon whose command
    /// is its own (<see cref="WeaponKind"/>).
    /// </summary>
    public WeaponKind Kind { get; init; }

    /// <summary>Whether this row swings rather than shoots.</summary>
    public bool IsMelee => Kind == WeaponKind.Standard && AmmoName.Length == 0 && Projectile.Length == 0;

    /// <summary>
    /// The KEY to the behaviour this row asks the machine to run, or empty for
    /// stock — answered through <see cref="IWeaponBehaviourSource"/> and
    /// resolved by <see cref="WeaponBehaviours.For"/>.
    ///
    /// <para>
    /// A key and not a type or an index because the table is APPEND-ONLY: a row
    /// is identified by its classname, and the behaviour belongs to the mod that
    /// wrote the row, which is loaded — in the general case — by neither side's
    /// compiler. Naming it lets a core row opt into a mod's behaviour as readily
    /// as a mod row does, and an unresolved key is stock rather than a broken
    /// gun. A row is free to name one nobody registered; the machine says so
    /// once, in the log, and runs stock.
    /// </para>
    /// </summary>
    public string Behaviour { get; init; } = "";
}

/// <summary>Which machine runs a weapon row's commands.</summary>
public enum WeaponKind
{
    /// <summary>
    /// <see cref="WeaponLogic"/>'s own: fire, alt-fire, reload and idle, read
    /// from the row's numbers. Every weapon but one.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The SLAM's (<see cref="SlamMachine"/>): a satchel thrown or a tripmine
    /// planted on the primary, the detonator on the secondary, and an idle
    /// that carries the throw out — stock's <c>CWeapon_SLAM::ItemPostFrame</c>,
    /// which shares nothing with the generic one but the clocks.
    /// </summary>
    Slam = 1,
}

/// <summary>
/// The other trigger.
///
/// A separate record rather than a second set of fields on
/// <see cref="WeaponType"/>, because the two are not the same shape. A primary
/// attack always spends the clip and is bounded by a fire rate the ENGINE
/// holds, since the stock attack path reads it during a shot. A secondary
/// spends a different ammunition pool with no clip in front of it, and nothing
/// native needs to know its rate.
/// </summary>
/// <param name="Interval">Seconds between alt-fires.</param>
/// <param name="AmmoCost">
/// How much of the secondary pool one press spends. The shotgun is the reason
/// this is not always one: both barrels is two shells.
/// </param>
public readonly record struct SecondaryFire(float Interval, int AmmoCost = 1)
{
    /// <summary>
    /// Bullets per press, for an alt-fire that shoots rather than launches.
    /// </summary>
    public int Pellets { get; init; }

    /// <summary>Half-angle of the cone, in degrees.</summary>
    public float Spread { get; init; }

    /// <summary>What it launches, or empty for one that shoots.</summary>
    public string Projectile { get; init; } = "";

    public float LaunchSpeed { get; init; }

    /// <summary>Lofted like a throw rather than sent along the line of sight.</summary>
    public bool Thrown { get; init; }

    /// <summary>
    /// Whether it spends the CLIP as well as the secondary pool.
    ///
    /// The shotgun's double blast does: it is the same two shells the primary
    /// would have fired one at a time, so it has no separate ammunition and
    /// takes from the clip instead. Everything else here draws on a pool the
    /// primary never touches.
    /// </summary>
    public bool FromClip { get; init; }

    /// <summary>The alt-fire's own soundscript entry (the script's double_shot).</summary>
    public string Sound { get; init; } = "";
}

/// <summary>
/// How each weapon fires, declared here and registered natively at startup.
///
/// What is NOT here any more is damage. That belongs to the ammunition, which
/// is how stock does it — FireBullets asks the ammo definition — so two weapons
/// sharing a type hit equally hard and differ by rate of fire, spread and
/// pellet count. See <see cref="AmmoTypes"/>.
///
/// <para>
/// These numbers are held natively once registered, because the stock attack
/// path asks for them DURING a shot. One generic shell serves every weapon
/// name, so none of this can be inherited from a per-weapon C++ class the way
/// stock does; this table is what replaces those classes.
/// </para>
/// </summary>
public static class WeaponTypes
{
    public static readonly ImmutableArray<WeaponType> All =
    [
        // Melee. Its script gives it no ammo type, so the attack path treats it
        // as a weapon that does not use clips.
        new(EntityNames.WeaponCrowbar, Interval: 0.5f)
        {
            ViewModel   = "models/weapons/v_crowbar.mdl",
            WorldModel  = "models/weapons/w_crowbar.mdl",
            FireSound   = "Weapon_Crowbar.Single",
            // Stock's sk_plr_dmg_crowbar and CWeaponCrowbar's 75-unit reach.
            MeleeRange  = 75f,
            MeleeDamage = 10f,
        },

        // GetFireRate() is 0.5 held; clicked, the pistol behaviour lets it
        // refire after PISTOL_FASTEST_REFIRE_TIME (weapon_pistol.cpp, hl2mp).
        new(EntityNames.WeaponPistol,   Interval: 0.5f, Spread: 0.5f)
        {
            Behaviour   = Hl2mpWeaponBehaviours.Pistol,
            ClipSize    = 18,
            AmmoName    = "Pistol",
            ViewModel   = "models/weapons/v_pistol.mdl",
            WorldModel  = "models/weapons/w_pistol.mdl",
            FireSound   = "Weapon_Pistol.Single",
            EmptySound  = "Weapon_Pistol.Empty",
            ReloadSound = "Weapon_Pistol.Reload",
        },

        new(EntityNames.Weapon357,      Interval: 0.75f)
        {
            ClipSize    = 6,
            AmmoName    = "357",
            ViewModel   = "models/weapons/v_357.mdl",
            WorldModel  = "models/weapons/w_357.mdl",
            FireSound   = "Weapon_357.Single",
            // The 357's own script borrows the pistol's dry click, so this
            // does too rather than inventing an entry the soundscripts lack.
            EmptySound  = "Weapon_Pistol.Empty",
        },
        // The SMG launches a grenade, on the ammunition dm_lockdown already
        // scatters six boxes of: stock's grenade_ar2, a contact grenade aimed
        // along the eyes at 1000, by CWeaponSMG1::SecondaryAttack's rules.
        new(EntityNames.WeaponSmg1,     Interval: 0.075f, Spread: 2.5f)
        {
            Behaviour = Hl2mpWeaponBehaviours.Smg1,
            Secondary = new SecondaryFire(Interval: 1.0f)
            {
                Projectile = EntityNames.GrenadeAr2,
                LaunchSpeed = 1000f,
                Sound = "Weapon_SMG1.Double",
            },
            ClipSize    = 45,
            AmmoName    = "SMG1",
            SecondaryAmmoName = "SMG1_Grenade",
            ViewModel   = "models/weapons/v_smg1.mdl",
            WorldModel  = "models/weapons/w_smg1.mdl",
            FireSound   = "Weapon_SMG1.Single",
            EmptySound  = "Weapon_SMG1.Empty",
            ReloadSound = "Weapon_SMG1.Reload",
        },

        // The AR2's alt-fire is stock's energy ball, and now it is one: a ball
        // that ricochets off the world and keeps going. It stood in as a rocket
        // for a while, which was the same shot without the thing that makes it
        // interesting — a rocket that hits a wall is over.
        new(EntityNames.WeaponAr2,      Interval: 0.10f, Spread: 1.5f)
        {
            DefaultClip = 60,   // weapon_ar2.txt's default_clip
            Secondary = new SecondaryFire(Interval: 1.5f)
            {
                Projectile = EntityNames.PropCombineBall,

                // Slower than the round it replaces, and it has to be: the
                // whole point is a shot you can watch come back off a wall, and
                // at 1800 the first bounce happens before anyone has seen the
                // first leg of it.
                LaunchSpeed = 1000f,
                Sound = "Weapon_IRifle.Single",
            },
            ClipSize    = 30,
            AmmoName    = "AR2",
            SecondaryAmmoName = "AR2AltFire",
            ViewModel   = "models/weapons/v_irifle.mdl",
            WorldModel  = "models/weapons/w_irifle.mdl",
            FireSound   = "Weapon_AR2.Single",
            EmptySound  = "Weapon_IRifle.Empty",
            ReloadSound = "Weapon_AR2.Reload",
        },

        // The one that needs a pellet count. Seven pellets of buckshot at eight
        // damage each is 56 at point blank and much less past the spread, which
        // is the behaviour a shotgun is FOR.
        new(EntityNames.WeaponShotgun, Interval: 0.90f, Pellets: 7, Spread: 5f)
        {
            // CWeaponShotgun's own ItemPostFrame: pump, shell-by-shell reload,
            // both barrels on the primary's clock (Hl2mpWeaponBehaviours).
            Behaviour = Hl2mpWeaponBehaviours.Shotgun,
            // Both barrels. Twice the pellets for twice the shells, out of the
            // CLIP rather than a pool of its own — it is the same buckshot the
            // primary fires, spent two at a time. Wider, because a double blast
            // that were merely twice as strong would have no drawback and would
            // simply be the better button.
            Secondary = new SecondaryFire(Interval: 1.5f, AmmoCost: 2)
            {
                Pellets = 12,
                // GetBulletSpread is VECTOR_CONE_10DEGREES for both barrels.
                Spread = 5f,
                FromClip = true,
                Sound = "Weapon_Shotgun.Double",
            },
            ClipSize    = 6,
            AmmoName    = "Buckshot",
            ViewModel   = "models/weapons/v_shotgun.mdl",
            WorldModel  = "models/weapons/w_shotgun.mdl",
            FireSound   = "Weapon_Shotgun.Single",
            EmptySound  = "Weapon_Shotgun.Empty",
            ReloadSound = "Weapon_Shotgun.Reload",
        },

        // Aimed, not thrown: both leave along the line of sight rather than
        // arcing, which is what separates them from the grenade below.
        new(EntityNames.WeaponCrossbow, Interval: 0.75f)
        {
            DefaultClip = 5,   // weapon_crossbow.txt's default_clip
            // CWeaponCrossbow's fire, reload, scope and charge (the behaviour).
            Behaviour = Hl2mpWeaponBehaviours.Crossbow,
            Projectile = "crossbow_bolt",

            // Stock's BOLT_AIR_VELOCITY. Fast enough to feel instant across a
            // room and slow enough to have to be led across a map — that gap IS
            // the crossbow, and stock already picked the number that gets it.
            LaunchSpeed = 3500f,
            ClipSize    = 1,
            AmmoName    = "XBowBolt",
            ViewModel   = "models/weapons/v_crossbow.mdl",
            WorldModel  = "models/weapons/w_crossbow.mdl",
            // The script has no empty sound: a dry trigger is silent.
            FireSound   = "Weapon_Crossbow.Single",
            ReloadSound = "Weapon_Crossbow.Reload",
        },

        new(EntityNames.WeaponRpg, Interval: 2.0f)
        {
            DefaultClip = 3,   // weapon_rpg.txt's default_clip
            // CWeaponRPG: one guided missile at a time, the laser, the reload
            // its missile's death starts. The missile picks its own speeds
            // (Rocket: 300 lofted, then RPG_SPEED); this row's is RPG_SPEED for
            // a mod that launches rpg_missile through the stock arms.
            Behaviour = Hl2mpWeaponBehaviours.Rpg,
            Projectile = "rpg_missile",
            LaunchSpeed = 1500f,
            AmmoName    = "RPG_Round",
            ViewModel   = "models/weapons/v_rpg.mdl",
            WorldModel  = "models/weapons/w_rocket_launcher.mdl",
            FireSound   = "Weapon_RPG.Single",
            EmptySound  = "Weapon_SMG1.Empty",
        },

        new(EntityNames.WeaponFrag, Interval: 1.0f)
        {
            // CWeaponFrag's pin, hold and three throws. Its script's single
            // and double shots are common/null.wav, which WeaponSound never
            // plays: GetParametersForSound finds no script by that name.
            Behaviour = Hl2mpWeaponBehaviours.Frag,
            Projectile = "npc_grenade_frag",
            LaunchSpeed = 900f,
            Thrown = true,
            AmmoName    = "Grenade",
            ViewModel   = "models/weapons/v_grenade.mdl",
            WorldModel  = "models/weapons/w_grenade.mdl",
        },

        // APPENDED, as every row after the first nine must be: the index is the
        // wire format. CWeaponStunStick (weapon_stunstick.cpp): a bludgeon with
        // STUNSTICK_RANGE 75 and STUNSTICK_REFIRE 0.8, forty damage a hit
        // whatever the activity, and an empty SecondaryAttack — so no
        // Secondary here. Its script names the swing Weapon_StunStick.Swing,
        // and stock plays the hit sound itself because PlayFleshyHittySoundOnHit
        // answers true.
        new(EntityNames.WeaponStunstick, Interval: 0.8f)
        {
            ViewModel         = "models/weapons/v_stunstick.mdl",
            WorldModel        = "models/weapons/w_stunbaton.mdl",
            FireSound         = "Weapon_StunStick.Swing",
            MeleeHitSound     = "Weapon_StunStick.Melee_Hit",
            MeleeImpactEffect = "StunstickImpact",
            DeploySound       = "Weapon_StunStick.Activate",
            HolsterSound      = "Weapon_StunStick.Deactivate",
            SparksOnDeploy    = true,
            RemovedOnDrop     = true,
            MeleeRange        = 75f,
            MeleeDamage       = 40f,
        },

        // CWeapon_SLAM (weapon_slam.cpp): no primary ammo, the "slam" pool as
        // its secondary, three of it on the first pickup, and its own machine.
        // The interval is the generic machine's and unused by the SLAM's,
        // whose clocks are its viewmodel's sequence durations; it is non-zero
        // because every row must be able to fire. No FireSound: the throw's
        // and the detonator's sounds are the machine's own.
        new(EntityNames.WeaponSlam, Interval: 1.0f)
        {
            Kind                 = WeaponKind.Slam,
            SecondaryAmmoName    = "slam",
            DefaultSecondaryAmmo = 3,
            ViewModel            = "models/weapons/v_slam.mdl",
            WorldModel           = "models/weapons/w_slam.mdl",
        },

        // The gravity gun (stock HL2MP gives one to every player). APPENDED,
        // because a row's position is its wire index. It spends nothing and its
        // command is its own (PhysCannonBehaviour): secondary pulls and holds,
        // primary punts. The interval is stock's 0.5 between punts, which the
        // behaviour writes itself; the deploy is immediate, as stock's Deploy
        // sets both triggers to now.
        new(EntityNames.WeaponPhyscannon, Interval: 0.5f)
        {
            ViewModel   = "models/weapons/v_physcannon.mdl",
            WorldModel  = "models/weapons/w_Physics.mdl",
            DeployTime  = 0f,
            FireSound   = PhysCannonSounds.Launch,
            EmptySound  = PhysCannonSounds.DryFire,
            ReloadSound = PhysCannonSounds.Charge,
            Behaviour   = PhysCannonBehaviour.Key,
        },
    ];
}
