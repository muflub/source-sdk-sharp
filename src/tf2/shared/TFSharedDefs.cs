//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

// TF2's shared constants (tf_shareddefs.h, tf_item_constants.h) and the name
// tables of tf_shareddefs.cpp, compiled into both sides. Every enum member
// mirrors a stock enumerator: the stock prefix (TF_COND_, TF_WEAPON_,
// TF_CLASS_, TF_AMMO_, OBJ_, LOADOUT_POSITION_, TF_TEAM_) is dropped and the
// rest is PascalCase, with one rename (HEAVYWEAPONS -> HeavyWeapons) and the
// count members kept. The tables are switches (SS0010: no static tables in a
// mod). TFSharedDefsTests parses the pristine sources and holds every name,
// value and table entry here to them.

/// <summary>Teams: shareddefs.h's TEAM_UNASSIGNED/TEAM_SPECTATOR, then tf_shareddefs.h's anonymous team enum.</summary>
public enum TFTeam
{
    Unassigned = 0,
    Spectator = 1,
    Red = 2,
    Blue = 3,
    Count = 4,
}

/// <summary><c>ETFClass</c>: note the stock order (Sniper is 2, Soldier 3).</summary>
public enum TFClass
{
    Undefined = 0,
    Scout = 1,
    Sniper = 2,
    Soldier = 3,
    Demoman = 4,
    Medic = 5,
    HeavyWeapons = 6,
    Pyro = 7,
    Spy = 8,
    Engineer = 9,
    Civilian = 10,
    CountAll = 11,
    Random = 12,
}

/// <summary><c>ETFCond</c>: the condition ids, <c>Invalid</c> (-1) to <c>Last</c>.</summary>
public enum TFCond
{
    Invalid = -1,
    Aiming = 0,
    Zoomed = 1,
    Disguising = 2,
    Disguised = 3,
    Stealthed = 4,
    Invulnerable = 5,
    Teleported = 6,
    Taunting = 7,
    InvulnerableWearingoff = 8,
    StealthedBlink = 9,
    SelectedToTeleport = 10,
    Critboosted = 11,
    Tmpdamagebonus = 12,
    FeignDeath = 13,
    Phase = 14,
    Stunned = 15,
    Offensebuff = 16,
    ShieldCharge = 17,
    DemoBuff = 18,
    EnergyBuff = 19,
    Radiusheal = 20,
    HealthBuff = 21,
    Burning = 22,
    HealthOverhealed = 23,
    Urine = 24,
    Bleeding = 25,
    Defensebuff = 26,
    MadMilk = 27,
    Megaheal = 28,
    Regenondamagebuff = 29,
    Markedfordeath = 30,
    Nohealingdamagebuff = 31,
    SpeedBoost = 32,
    CritboostedPumpkin = 33,
    CritboostedUserBuff = 34,
    CritboostedDemoCharge = 35,
    SodapopperHype = 36,
    CritboostedFirstBlood = 37,
    CritboostedBonusTime = 38,
    CritboostedCtfCapture = 39,
    CritboostedOnKill = 40,
    CannotSwitchFromMelee = 41,
    DefensebuffNoCritBlock = 42,
    Reprogrammed = 43,
    CritboostedRageBuff = 44,
    DefensebuffHigh = 45,
    SniperchargeRageBuff = 46,
    DisguiseWearingoff = 47,
    MarkedfordeathSilent = 48,
    DisguisedAsDispenser = 49,
    Sapped = 50,
    InvulnerableHideUnlessDamaged = 51,
    InvulnerableUserBuff = 52,
    HalloweenBombHead = 53,
    HalloweenThriller = 54,
    RadiushealOnDamage = 55,
    CritboostedCardEffect = 56,
    InvulnerableCardEffect = 57,
    MedigunUberBulletResist = 58,
    MedigunUberBlastResist = 59,
    MedigunUberFireResist = 60,
    MedigunSmallBulletResist = 61,
    MedigunSmallBlastResist = 62,
    MedigunSmallFireResist = 63,
    StealthedUserBuff = 64,
    MedigunDebuff = 65,
    StealthedUserBuffFading = 66,
    BulletImmune = 67,
    BlastImmune = 68,
    FireImmune = 69,
    PreventDeath = 70,
    MvmBotStunRadiowave = 71,
    HalloweenSpeedBoost = 72,
    HalloweenQuickHeal = 73,
    HalloweenGiant = 74,
    HalloweenTiny = 75,
    HalloweenInHell = 76,
    HalloweenGhostMode = 77,
    MinicritboostedOnKill = 78,
    ObscuredSmoke = 79,
    ParachuteActive = 80,
    Blastjumping = 81,
    HalloweenKart = 82,
    HalloweenKartDash = 83,
    BalloonHead = 84,
    MeleeOnly = 85,
    SwimmingCurse = 86,
    FreezeInput = 87,
    HalloweenKartCage = 88,
    Donotuse0 = 89,
    RuneStrength = 90,
    RuneHaste = 91,
    RuneRegen = 92,
    RuneResist = 93,
    RuneVampire = 94,
    RuneReflect = 95,
    RunePrecision = 96,
    RuneAgility = 97,
    Grapplinghook = 98,
    GrapplinghookSafefall = 99,
    GrapplinghookLatched = 100,
    GrapplinghookBleeding = 101,
    AfterburnImmune = 102,
    RuneKnockout = 103,
    RuneImbalance = 104,
    CritboostedRuneTemp = 105,
    PasstimeInterception = 106,
    SwimmingNoEffects = 107,
    Purgatory = 108,
    RuneKing = 109,
    RunePlague = 110,
    RuneSupernova = 111,
    Plague = 112,
    KingBuffed = 113,
    TeamGlows = 114,
    KnockedIntoAir = 115,
    CompetitiveWinner = 116,
    CompetitiveLoser = 117,
    HealingDebuff = 118,
    PasstimePenaltyDebuff = 119,
    GrappledToPlayer = 120,
    GrappledByPlayer = 121,
    ParachuteDeployed = 122,
    Gas = 123,
    BurningPyro = 124,
    Rocketpack = 125,
    LostFooting = 126,
    AirCurrent = 127,
    HalloweenHellHeal = 128,
    PowerupmodeDominant = 129,
    ImmuneToPushback = 130,
    Last = 131,
}

/// <summary><c>ETFWeaponType</c>: the weapon ids, <c>None</c> to <c>Count</c>.</summary>
public enum TFWeaponId
{
    None = 0,
    Bat = 1,
    BatWood = 2,
    Bottle = 3,
    Fireaxe = 4,
    Club = 5,
    Crowbar = 6,
    Knife = 7,
    Fists = 8,
    Shovel = 9,
    Wrench = 10,
    Bonesaw = 11,
    ShotgunPrimary = 12,
    ShotgunSoldier = 13,
    ShotgunHwg = 14,
    ShotgunPyro = 15,
    Scattergun = 16,
    Sniperrifle = 17,
    Minigun = 18,
    Smg = 19,
    SyringegunMedic = 20,
    Tranq = 21,
    Rocketlauncher = 22,
    Grenadelauncher = 23,
    Pipebomblauncher = 24,
    Flamethrower = 25,
    GrenadeNormal = 26,
    GrenadeConcussion = 27,
    GrenadeNail = 28,
    GrenadeMirv = 29,
    GrenadeMirvDemoman = 30,
    GrenadeNapalm = 31,
    GrenadeGas = 32,
    GrenadeEmp = 33,
    GrenadeCaltrop = 34,
    GrenadePipebomb = 35,
    GrenadeSmokeBomb = 36,
    GrenadeHeal = 37,
    GrenadeStunball = 38,
    GrenadeJar = 39,
    GrenadeJarMilk = 40,
    Pistol = 41,
    PistolScout = 42,
    Revolver = 43,
    Nailgun = 44,
    Pda = 45,
    PdaEngineerBuild = 46,
    PdaEngineerDestroy = 47,
    PdaSpy = 48,
    Builder = 49,
    Medigun = 50,
    GrenadeMirvbomb = 51,
    FlamethrowerRocket = 52,
    GrenadeDemoman = 53,
    SentryBullet = 54,
    SentryRocket = 55,
    Dispenser = 56,
    Invis = 57,
    Flaregun = 58,
    Lunchbox = 59,
    Jar = 60,
    CompoundBow = 61,
    BuffItem = 62,
    PumpkinBomb = 63,
    Sword = 64,
    RocketlauncherDirecthit = 65,
    Lifeline = 66,
    LaserPointer = 67,
    DispenserGun = 68,
    SentryRevenge = 69,
    JarMilk = 70,
    HandgunScoutPrimary = 71,
    BatFish = 72,
    Crossbow = 73,
    Stickbomb = 74,
    HandgunScoutSecondary = 75,
    SodaPopper = 76,
    SniperrifleDecap = 77,
    Raygun = 78,
    ParticleCannon = 79,
    MechanicalArm = 80,
    DrgPomson = 81,
    BatGiftwrap = 82,
    GrenadeOrnamentBall = 83,
    FlaregunRevenge = 84,
    PepBrawlerBlaster = 85,
    Cleaver = 86,
    GrenadeCleaver = 87,
    StickyBallLauncher = 88,
    GrenadeStickyBall = 89,
    ShotgunBuildingRescue = 90,
    Cannon = 91,
    Throwable = 92,
    GrenadeThrowable = 93,
    PdaSpyBuild = 94,
    GrenadeWaterballoon = 95,
    HarvesterSaw = 96,
    Spellbook = 97,
    SpellbookProjectile = 98,
    SniperrifleClassic = 99,
    Parachute = 100,
    Grapplinghook = 101,
    PasstimeGun = 102,
    ChargedSmg = 103,
    BreakableSign = 104,
    Rocketpack = 105,
    Slap = 106,
    JarGas = 107,
    GrenadeJarGas = 108,
    FlameBall = 109,
    Count = 110,
}

/// <summary><c>loadout_positions_t</c> (tf_item_constants.h): the per-class item slots.</summary>
public enum TFLoadoutSlot
{
    Invalid = -1,
    Primary = 0,
    Secondary = 1,
    Melee = 2,
    Utility = 3,
    Building = 4,
    Pda = 5,
    Pda2 = 6,
    Head = 7,
    Misc = 8,
    Action = 9,
    Misc2 = 10,
    Taunt = 11,
    Taunt2 = 12,
    Taunt3 = 13,
    Taunt4 = 14,
    Taunt5 = 15,
    Taunt6 = 16,
    Taunt7 = 17,
    Taunt8 = 18,
    Count = 19,
}

/// <summary>The ten <c>TF_DAMAGE_*</c> defines of tf_shareddefs.h.</summary>
public static class TFDamage
{
    public const float CritChance = 0.02f;
    public const float CritChanceRapid = 0.02f;
    public const float CritDurationRapid = 2.0f;
    public const float CritChanceMelee = 0.15f;
    public const int CritmodMaxtime = 20;
    public const int CritmodMintime = 2;
    public const int CritmodDamage = 800;
    public const int CritmodMaxmult = 6;
    public const float CritMultiplier = 3.0f;
    public const float MinicritMultiplier = 1.35f;
}

/// <summary><c>ETFAmmoType</c>: the TF ammo indices (<c>Dummy</c> keeps the <c>CAmmoDef</c> indices right).</summary>
public enum TFAmmo
{
    Dummy = 0,
    Primary = 1,
    Secondary = 2,
    Metal = 3,
    Grenades1 = 4,
    Grenades2 = 5,
    Grenades3 = 6,
    Count = 7,
}

/// <summary><c>ObjectType_t</c>: the engineer's (and spy's) buildings.</summary>
public enum TFObjectType
{
    Dispenser = 0,
    Teleporter = 1,
    Sentrygun = 2,
    AttachmentSapper = 3,
    Last = 4,
}

/// <summary>The per-class array sizes of tf_shareddefs.h (<c>TF_PLAYER_*_COUNT</c>).</summary>
public static class TFClassLimits
{
    public const int WeaponCount = 6;
    public const int GrenadeCount = 2;
    public const int BuildableCount = 3;
    public const int BlueprintCount = 6;
}

/// <summary><c>GetTFEncryptionKey()</c> (tf_shareddefs.cpp): the ICE key TF2's <c>.ctx</c> scripts are sealed with.</summary>
public static class TFEncryption
{
    public const string Key = "E2NcUkG2";
}

/// <summary>
/// <c>g_aWeaponNames</c> and <c>GetWeaponId</c> (tf_shareddefs.cpp): the
/// script spelling of each weapon id. Stock's last entry is misspelt
/// <c>TF_WEPON_FLAME_BALL</c>, so <c>TF_WEAPON_FLAME_BALL</c> does not resolve
/// there either; kept, since class and weapon scripts are read through it.
/// </summary>
public static class TFWeaponNames
{
    /// <summary><c>g_aWeaponNames[id]</c>.</summary>
    public static string Name(TFWeaponId id) => id switch
    {
        TFWeaponId.None => "TF_WEAPON_NONE",
        TFWeaponId.Bat => "TF_WEAPON_BAT",
        TFWeaponId.BatWood => "TF_WEAPON_BAT_WOOD",
        TFWeaponId.Bottle => "TF_WEAPON_BOTTLE",
        TFWeaponId.Fireaxe => "TF_WEAPON_FIREAXE",
        TFWeaponId.Club => "TF_WEAPON_CLUB",
        TFWeaponId.Crowbar => "TF_WEAPON_CROWBAR",
        TFWeaponId.Knife => "TF_WEAPON_KNIFE",
        TFWeaponId.Fists => "TF_WEAPON_FISTS",
        TFWeaponId.Shovel => "TF_WEAPON_SHOVEL",
        TFWeaponId.Wrench => "TF_WEAPON_WRENCH",
        TFWeaponId.Bonesaw => "TF_WEAPON_BONESAW",
        TFWeaponId.ShotgunPrimary => "TF_WEAPON_SHOTGUN_PRIMARY",
        TFWeaponId.ShotgunSoldier => "TF_WEAPON_SHOTGUN_SOLDIER",
        TFWeaponId.ShotgunHwg => "TF_WEAPON_SHOTGUN_HWG",
        TFWeaponId.ShotgunPyro => "TF_WEAPON_SHOTGUN_PYRO",
        TFWeaponId.Scattergun => "TF_WEAPON_SCATTERGUN",
        TFWeaponId.Sniperrifle => "TF_WEAPON_SNIPERRIFLE",
        TFWeaponId.Minigun => "TF_WEAPON_MINIGUN",
        TFWeaponId.Smg => "TF_WEAPON_SMG",
        TFWeaponId.SyringegunMedic => "TF_WEAPON_SYRINGEGUN_MEDIC",
        TFWeaponId.Tranq => "TF_WEAPON_TRANQ",
        TFWeaponId.Rocketlauncher => "TF_WEAPON_ROCKETLAUNCHER",
        TFWeaponId.Grenadelauncher => "TF_WEAPON_GRENADELAUNCHER",
        TFWeaponId.Pipebomblauncher => "TF_WEAPON_PIPEBOMBLAUNCHER",
        TFWeaponId.Flamethrower => "TF_WEAPON_FLAMETHROWER",
        TFWeaponId.GrenadeNormal => "TF_WEAPON_GRENADE_NORMAL",
        TFWeaponId.GrenadeConcussion => "TF_WEAPON_GRENADE_CONCUSSION",
        TFWeaponId.GrenadeNail => "TF_WEAPON_GRENADE_NAIL",
        TFWeaponId.GrenadeMirv => "TF_WEAPON_GRENADE_MIRV",
        TFWeaponId.GrenadeMirvDemoman => "TF_WEAPON_GRENADE_MIRV_DEMOMAN",
        TFWeaponId.GrenadeNapalm => "TF_WEAPON_GRENADE_NAPALM",
        TFWeaponId.GrenadeGas => "TF_WEAPON_GRENADE_GAS",
        TFWeaponId.GrenadeEmp => "TF_WEAPON_GRENADE_EMP",
        TFWeaponId.GrenadeCaltrop => "TF_WEAPON_GRENADE_CALTROP",
        TFWeaponId.GrenadePipebomb => "TF_WEAPON_GRENADE_PIPEBOMB",
        TFWeaponId.GrenadeSmokeBomb => "TF_WEAPON_GRENADE_SMOKE_BOMB",
        TFWeaponId.GrenadeHeal => "TF_WEAPON_GRENADE_HEAL",
        TFWeaponId.GrenadeStunball => "TF_WEAPON_GRENADE_STUNBALL",
        TFWeaponId.GrenadeJar => "TF_WEAPON_GRENADE_JAR",
        TFWeaponId.GrenadeJarMilk => "TF_WEAPON_GRENADE_JAR_MILK",
        TFWeaponId.Pistol => "TF_WEAPON_PISTOL",
        TFWeaponId.PistolScout => "TF_WEAPON_PISTOL_SCOUT",
        TFWeaponId.Revolver => "TF_WEAPON_REVOLVER",
        TFWeaponId.Nailgun => "TF_WEAPON_NAILGUN",
        TFWeaponId.Pda => "TF_WEAPON_PDA",
        TFWeaponId.PdaEngineerBuild => "TF_WEAPON_PDA_ENGINEER_BUILD",
        TFWeaponId.PdaEngineerDestroy => "TF_WEAPON_PDA_ENGINEER_DESTROY",
        TFWeaponId.PdaSpy => "TF_WEAPON_PDA_SPY",
        TFWeaponId.Builder => "TF_WEAPON_BUILDER",
        TFWeaponId.Medigun => "TF_WEAPON_MEDIGUN",
        TFWeaponId.GrenadeMirvbomb => "TF_WEAPON_GRENADE_MIRVBOMB",
        TFWeaponId.FlamethrowerRocket => "TF_WEAPON_FLAMETHROWER_ROCKET",
        TFWeaponId.GrenadeDemoman => "TF_WEAPON_GRENADE_DEMOMAN",
        TFWeaponId.SentryBullet => "TF_WEAPON_SENTRY_BULLET",
        TFWeaponId.SentryRocket => "TF_WEAPON_SENTRY_ROCKET",
        TFWeaponId.Dispenser => "TF_WEAPON_DISPENSER",
        TFWeaponId.Invis => "TF_WEAPON_INVIS",
        TFWeaponId.Flaregun => "TF_WEAPON_FLAREGUN",
        TFWeaponId.Lunchbox => "TF_WEAPON_LUNCHBOX",
        TFWeaponId.Jar => "TF_WEAPON_JAR",
        TFWeaponId.CompoundBow => "TF_WEAPON_COMPOUND_BOW",
        TFWeaponId.BuffItem => "TF_WEAPON_BUFF_ITEM",
        TFWeaponId.PumpkinBomb => "TF_WEAPON_PUMPKIN_BOMB",
        TFWeaponId.Sword => "TF_WEAPON_SWORD",
        TFWeaponId.RocketlauncherDirecthit => "TF_WEAPON_ROCKETLAUNCHER_DIRECTHIT",
        TFWeaponId.Lifeline => "TF_WEAPON_LIFELINE",
        TFWeaponId.LaserPointer => "TF_WEAPON_LASER_POINTER",
        TFWeaponId.DispenserGun => "TF_WEAPON_DISPENSER_GUN",
        TFWeaponId.SentryRevenge => "TF_WEAPON_SENTRY_REVENGE",
        TFWeaponId.JarMilk => "TF_WEAPON_JAR_MILK",
        TFWeaponId.HandgunScoutPrimary => "TF_WEAPON_HANDGUN_SCOUT_PRIMARY",
        TFWeaponId.BatFish => "TF_WEAPON_BAT_FISH",
        TFWeaponId.Crossbow => "TF_WEAPON_CROSSBOW",
        TFWeaponId.Stickbomb => "TF_WEAPON_STICKBOMB",
        TFWeaponId.HandgunScoutSecondary => "TF_WEAPON_HANDGUN_SCOUT_SECONDARY",
        TFWeaponId.SodaPopper => "TF_WEAPON_SODA_POPPER",
        TFWeaponId.SniperrifleDecap => "TF_WEAPON_SNIPERRIFLE_DECAP",
        TFWeaponId.Raygun => "TF_WEAPON_RAYGUN",
        TFWeaponId.ParticleCannon => "TF_WEAPON_PARTICLE_CANNON",
        TFWeaponId.MechanicalArm => "TF_WEAPON_MECHANICAL_ARM",
        TFWeaponId.DrgPomson => "TF_WEAPON_DRG_POMSON",
        TFWeaponId.BatGiftwrap => "TF_WEAPON_BAT_GIFTWRAP",
        TFWeaponId.GrenadeOrnamentBall => "TF_WEAPON_GRENADE_ORNAMENT_BALL",
        TFWeaponId.FlaregunRevenge => "TF_WEAPON_FLAREGUN_REVENGE",
        TFWeaponId.PepBrawlerBlaster => "TF_WEAPON_PEP_BRAWLER_BLASTER",
        TFWeaponId.Cleaver => "TF_WEAPON_CLEAVER",
        TFWeaponId.GrenadeCleaver => "TF_WEAPON_GRENADE_CLEAVER",
        TFWeaponId.StickyBallLauncher => "TF_WEAPON_STICKY_BALL_LAUNCHER",
        TFWeaponId.GrenadeStickyBall => "TF_WEAPON_GRENADE_STICKY_BALL",
        TFWeaponId.ShotgunBuildingRescue => "TF_WEAPON_SHOTGUN_BUILDING_RESCUE",
        TFWeaponId.Cannon => "TF_WEAPON_CANNON",
        TFWeaponId.Throwable => "TF_WEAPON_THROWABLE",
        TFWeaponId.GrenadeThrowable => "TF_WEAPON_GRENADE_THROWABLE",
        TFWeaponId.PdaSpyBuild => "TF_WEAPON_PDA_SPY_BUILD",
        TFWeaponId.GrenadeWaterballoon => "TF_WEAPON_GRENADE_WATERBALLOON",
        TFWeaponId.HarvesterSaw => "TF_WEAPON_HARVESTER_SAW",
        TFWeaponId.Spellbook => "TF_WEAPON_SPELLBOOK",
        TFWeaponId.SpellbookProjectile => "TF_WEAPON_SPELLBOOK_PROJECTILE",
        TFWeaponId.SniperrifleClassic => "TF_WEAPON_SNIPERRIFLE_CLASSIC",
        TFWeaponId.Parachute => "TF_WEAPON_PARACHUTE",
        TFWeaponId.Grapplinghook => "TF_WEAPON_GRAPPLINGHOOK",
        TFWeaponId.PasstimeGun => "TF_WEAPON_PASSTIME_GUN",
        TFWeaponId.ChargedSmg => "TF_WEAPON_CHARGED_SMG",
        TFWeaponId.BreakableSign => "TF_WEAPON_BREAKABLE_SIGN",
        TFWeaponId.Rocketpack => "TF_WEAPON_ROCKETPACK",
        TFWeaponId.Slap => "TF_WEAPON_SLAP",
        TFWeaponId.JarGas => "TF_WEAPON_JAR_GAS",
        TFWeaponId.GrenadeJarGas => "TF_WEAPON_GRENADE_JAR_GAS",
        TFWeaponId.FlameBall => "TF_WEPON_FLAME_BALL",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "g_aWeaponNames has no such entry"),
    };

    /// <summary><c>GetWeaponId</c>: a case-insensitive match, else <see cref="TFWeaponId.None"/>.</summary>
    public static TFWeaponId IdOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return TFWeaponId.None;

        for (var id = TFWeaponId.None; id < TFWeaponId.Count; id++)
        {
            if (string.Equals(name, Name(id), StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return TFWeaponId.None;
    }
}

/// <summary><c>g_aAmmoNames</c> / <c>GetAmmoName</c> (tf_shareddefs.cpp): the AmmoMax keys of a class script.</summary>
public static class TFAmmoNames
{
    public static string Name(TFAmmo ammo) => ammo switch
    {
        TFAmmo.Dummy => "DUMMY AMMO",
        TFAmmo.Primary => "TF_AMMO_PRIMARY",
        TFAmmo.Secondary => "TF_AMMO_SECONDARY",
        TFAmmo.Metal => "TF_AMMO_METAL",
        TFAmmo.Grenades1 => "TF_AMMO_GRENADES1",
        TFAmmo.Grenades2 => "TF_AMMO_GRENADES2",
        TFAmmo.Grenades3 => "TF_AMMO_GRENADES3",
        _ => throw new ArgumentOutOfRangeException(nameof(ammo), ammo, "g_aAmmoNames has no such entry"),
    };
}

/// <summary>
/// <c>g_ObjectInfos[].m_pObjectName</c> and <c>GetBuildableId</c>
/// (tf_shareddefs.cpp): a class script's <c>buildableN</c> spelling.
/// </summary>
public static class TFObjectNames
{
    public static string Name(TFObjectType type) => type switch
    {
        TFObjectType.Dispenser => "OBJ_DISPENSER",
        TFObjectType.Teleporter => "OBJ_TELEPORTER",
        TFObjectType.Sentrygun => "OBJ_SENTRYGUN",
        TFObjectType.AttachmentSapper => "OBJ_ATTACHMENT_SAPPER",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "g_ObjectInfos has no such entry"),
    };

    /// <summary><c>GetBuildableId</c>: a case-insensitive match, else <see cref="TFObjectType.Last"/>.</summary>
    public static TFObjectType IdOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return TFObjectType.Last;

        for (var type = TFObjectType.Dispenser; type < TFObjectType.Last; type++)
        {
            if (string.Equals(name, Name(type), StringComparison.OrdinalIgnoreCase))
                return type;
        }

        return TFObjectType.Last;
    }
}

/// <summary>
/// The class name tables of tf_shareddefs.cpp: <c>g_aRawPlayerClassNames</c>
/// (the script file names: "demoman", "heavyweapons") and
/// <c>g_aPlayerClassNames_NonLocalized</c> ("Demoman", "Heavy").
/// </summary>
public static class TFClassNames
{
    public static string Raw(TFClass tfClass) => tfClass switch
    {
        TFClass.Undefined => "undefined",
        TFClass.Scout => "scout",
        TFClass.Sniper => "sniper",
        TFClass.Soldier => "soldier",
        TFClass.Demoman => "demoman",
        TFClass.Medic => "medic",
        TFClass.HeavyWeapons => "heavyweapons",
        TFClass.Pyro => "pyro",
        TFClass.Spy => "spy",
        TFClass.Engineer => "engineer",
        TFClass.Civilian => "civilian",
        TFClass.CountAll => "",
        TFClass.Random => "random",
        _ => throw new ArgumentOutOfRangeException(nameof(tfClass), tfClass, "g_aRawPlayerClassNames has no such entry"),
    };

    public static string NonLocalized(TFClass tfClass) => tfClass switch
    {
        TFClass.Undefined => "Undefined",
        TFClass.Scout => "Scout",
        TFClass.Sniper => "Sniper",
        TFClass.Soldier => "Soldier",
        TFClass.Demoman => "Demoman",
        TFClass.Medic => "Medic",
        TFClass.HeavyWeapons => "Heavy",
        TFClass.Pyro => "Pyro",
        TFClass.Spy => "Spy",
        TFClass.Engineer => "Engineer",
        TFClass.Civilian => "Civilian",
        TFClass.CountAll => "",
        TFClass.Random => "Random",
        _ => throw new ArgumentOutOfRangeException(nameof(tfClass), tfClass, "g_aPlayerClassNames_NonLocalized has no such entry"),
    };
}

/// <summary>
/// The <c>TF_STUN_*</c> macros (tf_shareddefs.h:1331-1342): what a stun does
/// to the stunned player. <c>TF_STUN_BOTH</c> is stock's macro for
/// <see cref="Movement"/> with <see cref="Controls"/>; the consumers of these
/// bits (the movement slow, the loser state, the stun sounds) live with their
/// own rows (docs/plans/tf2/deferred.tsv).
/// </summary>
[Flags]
public enum TFStunFlags
{
    /// <summary><c>TF_STUN_NONE</c>.</summary>
    None = 0,

    /// <summary><c>TF_STUN_MOVEMENT</c>: the player's movement is slowed by the stun amount.</summary>
    Movement = 1 << 0,

    /// <summary><c>TF_STUN_CONTROLS</c>: the player loses their controls.</summary>
    Controls = 1 << 1,

    /// <summary><c>TF_STUN_MOVEMENT_FORWARD_ONLY</c>.</summary>
    MovementForwardOnly = 1 << 2,

    /// <summary><c>TF_STUN_SPECIAL_SOUND</c>: the "big stun" sound and event flag.</summary>
    SpecialSound = 1 << 3,

    /// <summary><c>TF_STUN_DODGE_COOLDOWN</c>.</summary>
    DodgeCooldown = 1 << 4,

    /// <summary><c>TF_STUN_NO_EFFECTS</c>: no stun particles.</summary>
    NoEffects = 1 << 5,

    /// <summary><c>TF_STUN_LOSER_STATE</c>: the loser-state animation.</summary>
    LoserState = 1 << 6,

    /// <summary><c>TF_STUN_BY_TRIGGER</c>: the stun came from a trigger, not a weapon.</summary>
    ByTrigger = 1 << 7,

    /// <summary><c>TF_STUN_SOUND</c>.</summary>
    Sound = 1 << 8,
}

/// <summary>
/// <c>TFStatType_t</c> (tf_gamestats_shared.h:23-71): a player's stats, the
/// round's and the accumulated, which the scoreboard and the score read.
/// Append only, as stock's note says.
/// </summary>
public enum TFStat
{
    Undefined = 0,
    ShotsHit,
    ShotsFired,
    Kills,
    Deaths,
    Damage,
    Captures,
    Defenses,
    Dominations,
    Revenge,
    Pointsscored,
    Buildingsdestroyed,
    Headshots,
    Playtime,
    Healing,
    Invulns,
    Killassists,
    Backstabs,
    Healthleached,
    Buildingsbuilt,
    Maxsentrykills,
    Teleports,
    Firedamage,
    BonusPoints,
    Blastdamage,
    Damagetaken,
    Healthkits,
    Ammokits,
    Classchanges,
    Crits,
    Suicides,
    CurrencyCollected,
    DamageAssist,
    HealingAssist,
    DamageBoss,
    DamageBlocked,
    DamageRanged,
    DamageRangedCritRandom,
    DamageRangedCritBoosted,
    Revived,
    Throwablehit,
    Throwablekill,
    KillstreakMax,
    KillsRunecarrier,
    Flagreturns,
    Total,
}
