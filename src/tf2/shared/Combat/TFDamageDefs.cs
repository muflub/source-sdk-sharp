//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Combat;

/// <summary>
/// TF's damage-type aliases (<c>tf_shareddefs.h:1162-1175</c>): TF reuses
/// HL2 bits it never deals for its own meanings, so a TF hit's
/// <see cref="DamageType"/> is read through these names.
/// </summary>
public static class TFDamageBits
{
    /// <summary><c>DMG_USE_HITLOCATIONS</c> (<c>DMG_AIRBOAT</c>, 1 &lt;&lt; 25): the shot may headshot.</summary>
    public const DamageType UseHitLocations = (DamageType)(1 << 25);

    /// <summary><c>DMG_HALF_FALLOFF</c> (<c>DMG_RADIATION</c>).</summary>
    public const DamageType HalfFalloff = DamageType.Radiation;

    /// <summary><c>DMG_CRITICAL</c> (<c>DMG_ACID</c>): the hit is a crit (full or mini).</summary>
    public const DamageType Critical = DamageType.Acid;

    /// <summary><c>DMG_RADIUS_MAX</c> (<c>DMG_ENERGYBEAM</c>).</summary>
    public const DamageType RadiusMax = DamageType.EnergyBeam;

    /// <summary><c>DMG_IGNITE</c> (<c>DMG_PLASMA</c>): the hit sets the victim alight.</summary>
    public const DamageType Ignite = DamageType.Plasma;

    /// <summary><c>DMG_USEDISTANCEMOD</c> (<c>DMG_SLOWBURN</c>): the hit ramps up near and falls off far.</summary>
    public const DamageType UseDistanceMod = DamageType.SlowBurn;

    /// <summary><c>DMG_NOCLOSEDISTANCEMOD</c> (<c>DMG_POISON</c>): the near ramp-up is damped.</summary>
    public const DamageType NoCloseDistanceMod = DamageType.Poison;

    /// <summary><c>DMG_MELEE</c> (<c>DMG_BLAST_SURFACE</c>).</summary>
    public const DamageType Melee = DamageType.BlastSurface;
}

/// <summary>
/// <c>ETFDmgCustom</c> (<c>tf_shareddefs.h:1179-1270</c>): the kill's custom
/// kind, carried beside the damage (<c>CTakeDamageInfo::m_iDamageCustom</c>)
/// and sent as <c>player_death</c>'s <c>customkill</c>. Values only append in
/// stock ("insert new items here to avoid breaking demos"), so the client's
/// kill feed reads them as numbers.
/// </summary>
public enum TFDamageCustom
{
    None = 0,
    Headshot,
    Backstab,
    Burning,
    WrenchFix,
    Minigun,
    Suicide,
    TauntatkHadouken,
    BurningFlare,
    TauntatkHighNoon,
    TauntatkGrandSlam,
    PenetrateMyTeam,
    PenetrateAllPlayers,
    TauntatkFencing,
    PenetrateNonburningTeammate,
    TauntatkArrowStab,
    Telefrag,
    BurningArrow,
    Flyingburn,
    PumpkinBomb,
    Decapitation,
    TauntatkGrenade,
    Baseball,
    ChargeImpact,
    TauntatkBarbarianSwing,
    AirStickyBurst,
    DefensiveSticky,
    Pickaxe,
    RocketDirecthit,
    TauntatkUberslice,
    PlayerSentry,
    StandardSticky,
    ShotgunRevengeCrit,
    TauntatkEngineerGuitarSmash,
    Bleeding,
    GoldWrench,
    CarriedBuilding,
    ComboPunch,
    TauntatkEngineerArmKill,
    FishKill,
    TriggerHurt,
    DecapitationBoss,
    StickbombExplosion,
    AegisRound,
    FlareExplosion,
    BootsStomp,
    Plasma,
    PlasmaCharged,
    PlasmaGib,
    PracticeSticky,
    EyeballRocket,
    HeadshotDecapitation,
    TauntatkArmageddon,
    FlarePellet,
    Cleaver,
    CleaverCrit,
    SapperRecorderDeath,
    MerasmusPlayerBomb,
    MerasmusGrenade,
    MerasmusZap,
    MerasmusDecapitation,
    CannonballPush,
    TauntatkAllclassGuitarRiff,
    Throwable,
    ThrowableKill,
    SpellTeleport,
    SpellSkeleton,
    SpellMirv,
    SpellMeteor,
    SpellLightning,
    SpellFireball,
    SpellMonoculus,
    SpellBlastjump,
    SpellBats,
    SpellTiny,
    Kart,
    GiantHammer,
    RuneReflect,
    DragonsFuryIgnite,
    DragonsFuryBonusBurning,
    SlapKill,
    Croc,
    TauntatkGasblast,
    AxtinguisherBoosted,
    KrampusMelee,
    KrampusRanged,
    TauntatkTrickshot,
    End,
}

/// <summary>
/// <c>CTakeDamageInfo::ECritType</c> (<c>shared/takedamageinfo.h</c>): what
/// kind of crit a hit is, sent as <c>player_death</c>'s <c>crit_type</c>.
/// </summary>
public enum TFCritType
{
    None = 0,
    Mini = 1,
    Full = 2,
}

/// <summary>
/// The burning and assist defines of <c>tf_shareddefs.h</c> the damage layer
/// reads (<c>:664-667</c>, <c>:923-924</c>), and the fall rule's
/// <c>TF_PLAYER_MAX_SAFE_FALL_SPEED</c> (<c>tf_gamerules.cpp:13364</c>).
/// </summary>
public static class TFCombatDefs
{
    /// <summary><c>TF_BURNING_FREQUENCY</c>: seconds between afterburn ticks.</summary>
    public const float BurningFrequency = 0.5f;

    /// <summary><c>TF_BURNING_FLAME_LIFE</c>.</summary>
    public const float BurningFlameLife = 10.0f;

    /// <summary><c>TF_BURNING_FLAME_LIFE_PYRO</c>: an immune victim's fixed burn.</summary>
    public const float BurningFlameLifePyro = 0.25f;

    /// <summary><c>TF_BURNING_DMG</c>: one afterburn tick's damage.</summary>
    public const int BurningDamage = 4;

    /// <summary><c>TF_BLEEDING_FREQUENCY</c> (<c>tf_shareddefs.h:670</c>): seconds between bleed ticks.</summary>
    public const float BleedingFrequency = 0.5f;

    /// <summary><c>TF_BLEEDING_DMG</c> (<c>tf_shareddefs.h:671</c>): one bleed tick's damage.</summary>
    public const int BleedingDamage = 4;

    /// <summary><c>TF_TIME_ASSIST_KILL</c>: how recent a second damager is to assist a kill.</summary>
    public const float TimeAssistKill = 3.0f;

    /// <summary><c>TF_TIME_SUICIDE_KILL_CREDIT</c>: how recent a damager is to be credited a suicide.</summary>
    public const float TimeSuicideKillCredit = 10.0f;

    /// <summary><c>TF_PLAYER_MAX_SAFE_FALL_SPEED</c>: the fastest landing that does no damage.</summary>
    public const float PlayerMaxSafeFallSpeed = 650f;
}
