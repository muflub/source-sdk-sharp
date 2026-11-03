//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The weapon type a TF script names (<c>"WeaponType"</c>): the unnamed enum
/// of <c>shared/tf/tf_item_constants.h:17-36</c>, the values a script spells.
/// </summary>
public enum TFWeaponCategory
{
    Primary = 0,
    Secondary = 1,
    Melee = 2,
    Grenade = 3,
    Building = 4,
    Pda = 5,
    Item1 = 6,
    Item2 = 7,
}

/// <summary>
/// <c>ProjectileType_t</c> (<c>shared/tf/tf_shareddefs.h:546-583</c>): what a
/// weapon mode fires.
/// </summary>
public enum TFProjectile
{
    None = 0,
    Bullet,
    Rocket,
    Pipebomb,
    PipebombRemote,
    Syringe,
    Flare,
    Jar,
    Arrow,
    FlameRocket,
    JarMilk,
    HealingBolt,
    EnergyBall,
    EnergyRing,
    PipebombPractice,
    Cleaver,
    StickyBall,
    Cannonball,
    BuildingRepairBolt,
    FestiveArrow,
    Throwable,
    Spell,
    FestiveJar,
    FestiveHealingBolt,
    BreadmonsterJarate,
    BreadmonsterMadmilk,
    Grapplinghook,
    SentryRocket,
    BreadMonster,
    JarGas,
    FlameBall,
    Count,
}

/// <summary>
/// The script spellings of <see cref="TFProjectile"/>. Stock matches the
/// <c>"ProjectileType"</c> value against <c>g_szProjectileNames[]</c>
/// (<c>tf_weapon_parse.cpp:84-92</c>), which <c>tf_shareddefs.h:585</c>
/// declares and the SDK does not define: the spellings below are the shipped
/// scripts' own (every <c>ProjectileType</c> the 78 <c>scripts/tf_weapon_*</c>
/// files write), each the entry its name and weapon make it.
/// </summary>
public static class TFProjectileNames
{
    /// <summary>The projectile a script spelling names; anything else is <see cref="TFProjectile.None"/>, as stock's unmatched loop leaves it.</summary>
    public static TFProjectile IdOf(string? name) => name switch
    {
        "projectile_bullet" => TFProjectile.Bullet,
        "projectile_rocket" => TFProjectile.Rocket,
        "projectile_pipe" => TFProjectile.Pipebomb,
        "projectile_pipe_remote" => TFProjectile.PipebombRemote,
        "projectile_syringe" => TFProjectile.Syringe,
        "projectile_flare" => TFProjectile.Flare,
        "projectile_jar" => TFProjectile.Jar,
        "projectile_arrow" => TFProjectile.Arrow,
        "projectile_jar_milk" => TFProjectile.JarMilk,
        "projectile_healing_bolt" => TFProjectile.HealingBolt,
        "projectile_energy_ball" => TFProjectile.EnergyBall,
        "projectile_energy_ring" => TFProjectile.EnergyRing,
        "projectile_cleaver" => TFProjectile.Cleaver,
        "projectile_cannonball" => TFProjectile.Cannonball,
        "projectile_throwable" => TFProjectile.Throwable,
        "projectile_jar_gas" => TFProjectile.JarGas,
        _ => TFProjectile.None,
    };
}

/// <summary>
/// One fire mode of a TF weapon script: <c>WeaponData_t</c>
/// (<c>shared/tf/tf_weapon_parse.h</c>), as <c>CTFWeaponInfo::Parse</c>
/// fills <c>m_WeaponData[TF_WEAPON_PRIMARY_MODE]</c> and the
/// <c>Secondary_</c> copy (<c>tf_weapon_parse.cpp:68-132</c>).
/// </summary>
public readonly record struct TFWeaponModeData
{
    public int Damage { get; init; }
    public float Range { get; init; }
    public int BulletsPerShot { get; init; }
    public float Spread { get; init; }
    public float PunchAngle { get; init; }
    public float TimeFireDelay { get; init; }
    public float TimeIdle { get; init; }
    public float TimeIdleEmpty { get; init; }
    public float TimeReloadStart { get; init; }
    public float TimeReload { get; init; }
    public bool DrawCrosshair { get; init; }
    public int AmmoPerShot { get; init; }
    public bool UseRapidFireCrits { get; init; }
    public TFProjectile Projectile { get; init; }
    public float ProjectileSpeed { get; init; }
    public float SmackDelay { get; init; }
}

/// <summary>
/// One <c>TextureData</c> entry of a weapon script: the file and the pixel
/// rectangle within it, stock's <c>CHudTexture</c> as
/// <c>LoadHudTextures</c> fills it (hud.cpp:118-141) — the sub-rect
/// <c>DrawSelf</c> scales to the box it is given
/// (hud.cpp:590-609, <c>DrawTexturedSubRect</c>).
/// </summary>
public sealed record TFHudIcon(string File, int X, int Y, int Wide, int Tall);

/// <summary>
/// A parsed TF weapon script: <c>CTFWeaponInfo</c>, the base
/// <c>FileWeaponInfo_t</c> keys (<see cref="WeaponScript"/>, the core's
/// <c>WeaponScriptParser</c>) plus the TF keys of
/// <c>CTFWeaponInfo::Parse</c> (<c>shared/tf/tf_weapon_parse.cpp:61-230</c>).
/// </summary>
public sealed class TFWeaponScript
{
    /// <summary>The script's <c>FileWeaponInfo_t</c> half.</summary>
    public required WeaponScript Base { get; init; }

    /// <summary><c>m_WeaponData[TF_WEAPON_PRIMARY_MODE]</c>.</summary>
    public TFWeaponModeData Primary { get; init; }

    /// <summary><c>m_WeaponData[TF_WEAPON_SECONDARY_MODE]</c>: the <c>Secondary_</c> keys, each defaulting to the primary's.</summary>
    public TFWeaponModeData Secondary { get; init; }

    /// <summary><c>m_iWeaponType</c>; a script that names none (or another word) is <see cref="TFWeaponCategory.Primary"/>, the constructor's 0.</summary>
    public TFWeaponCategory Category { get; init; }

    public bool Grenade { get; init; }
    public float DamageRadius { get; init; }
    public float PrimerTime { get; init; }
    public bool SuppressGrenadeTimer { get; init; }
    public bool LowerWeapon { get; init; }
    public bool HasTeamSkinsViewmodel { get; init; }
    public bool HasTeamSkinsWorldmodel { get; init; }
    public bool DoInstantEjectBrass { get; init; }
    public string BrassModel { get; init; } = "";
    public string MuzzleFlashModel { get; init; } = "";
    public float MuzzleFlashModelDuration { get; init; }
    public string MuzzleFlashParticleEffect { get; init; } = "";
    public string TracerEffect { get; init; } = "";
    public string ExplosionSound { get; init; } = "";
    public string ExplosionEffect { get; init; } = "";
    public string ExplosionPlayerEffect { get; init; } = "";
    public string ExplosionWaterEffect { get; init; } = "";
    public bool DontDrop { get; init; }

    /// <summary>
    /// The drop weight of <c>CTFPlayer::DropAmmoPack</c>
    /// (tf_player.cpp:13040-13058): when the active weapon carries
    /// <see cref="DontDrop"/> — or there is no active weapon — the pack
    /// dresses as the heaviest droppable weapon the player holds. Stock
    /// parses the key once, in the SHARED <c>CWeaponInfo::Parse</c>
    /// (<c>iWeight = pKeyValuesData-&gt;GetInt( "weight", 0 )</c>,
    /// weapon_parse.cpp:392), and the TF scan reads that inherited member
    /// through <c>GetTFWpnData().iWeight</c> — so this is <see cref="Base"/>'s
    /// <c>Weight</c>, read the same way, never parsed twice. The scan starts
    /// <c>iWeight</c> at <c>-1</c> with a strict <c>&gt;</c>, so an
    /// unweighted row (0) can still win when nothing heavier exists, and
    /// equal weights go to whoever the inventory walks first.
    /// </summary>
    public int Weight => Base.Weight;

    /// <summary>The weapon id the name spells (<c>GetWeaponId</c>); <see cref="TFWeaponId.None"/> for a weapon with no id of its own (the sapper).</summary>
    public TFWeaponId Id { get; init; }

    /// <summary>The script's name (stock's <c>szClassName</c>, the file the database was asked for).</summary>
    public string Name => Base.ClassName;

    /// <summary>
    /// The selection's inactive icon, the script's <c>TextureData</c>
    /// <c>weapon</c> entry — the red team's art, <c>GetSpriteInactive</c>
    /// (weapons_resource.cpp:85-100 hangs the parsed pair off
    /// <c>iconInactive</c>/<c>iconActive</c>). The base class leaves the hud
    /// sprites to the renderer that reads them
    /// (<see cref="WeaponScript"/>'s note); the selection hud is ours, so
    /// the pair lives here.
    /// </summary>
    public TFHudIcon? IconInactive { get; init; }

    /// <summary>The selection's active icon, the <c>weapon_s</c> entry — the blue team's, <c>GetSpriteActive</c>.</summary>
    public TFHudIcon? IconActive { get; init; }

    /// <summary>A sound category's script sound, or empty.</summary>
    public string Sound(WeaponScriptSound category) => Base.Sounds[(int)category];
}
