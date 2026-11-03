//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;

namespace TF2.Shared;

/// <summary><c>DeathSoundType_t</c>'s first four (tf_classdata.h): the sounds a class script names.</summary>
public enum TFDeathSound
{
    Generic = 0,
    Crit = 1,
    Melee = 2,
    Explosion = 3,
}

/// <summary>
/// <c>TFPlayerClassData_t</c> (tf_classdata.h): one class's script, as
/// <see cref="TFClassDataParser"/> read it. Immutable; the table hands the
/// same instance to every reader.
///
/// <para>
/// The arrays keep stock's sizes and indices: <see cref="Weapons"/> is
/// <c>m_aWeapons[TF_PLAYER_WEAPON_COUNT]</c> (<c>weapon1</c> is index 0),
/// <see cref="AmmoMax"/> is <c>m_aAmmoMax[TF_AMMO_COUNT]</c> indexed by
/// <see cref="TFAmmo"/> (index 0, <c>TF_AMMO_DUMMY</c>, is always 0), and
/// <see cref="Buildables"/> is <c>m_aBuildable[TF_PLAYER_BLUEPRINT_COUNT]</c>.
/// The death sounds are parsed on both sides; stock parses them only in the
/// game DLL (<c>#ifdef GAME_DLL</c>), and nothing on the client reads them.
/// The MvM variants (<c>AddAdditionalPlayerDeathSounds</c>) are not ported.
/// </para>
/// </summary>
public sealed record TFClassData
{
    /// <summary>The class this is the data of.</summary>
    public required TFClass Class { get; init; }

    /// <summary><c>m_bParsed</c>: false for a class whose script was missing or unreadable, and for Undefined.</summary>
    public required bool IsParsed { get; init; }

    /// <summary><c>m_szClassName</c> (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary><c>m_szModelName</c> (<c>model</c>).</summary>
    public required string Model { get; init; }

    /// <summary><c>m_szHWMModelName</c> (<c>model_hwm</c>).</summary>
    public required string ModelHwm { get; init; }

    /// <summary><c>m_szHandModelName</c> (<c>model_hands</c>).</summary>
    public required string ModelHands { get; init; }

    /// <summary><c>m_szLocalizableName</c> (<c>localize_name</c>).</summary>
    public required string LocalizeName { get; init; }

    /// <summary><c>m_flMaxSpeed</c> (<c>speed_max</c>).</summary>
    public required float MaxSpeed { get; init; }

    /// <summary><c>m_nMaxHealth</c> (<c>health_max</c>).</summary>
    public required int MaxHealth { get; init; }

    /// <summary><c>m_nMaxArmor</c> (<c>armor_max</c>).</summary>
    public required int MaxArmor { get; init; }

    /// <summary><c>m_aWeapons</c>: <c>weapon1..6</c>, <see cref="TFWeaponId.None"/> where unset.</summary>
    public required ImmutableArray<TFWeaponId> Weapons { get; init; }

    /// <summary><c>m_aGrenades</c>: <c>grenade1</c>, <c>grenade2</c>.</summary>
    public required ImmutableArray<TFWeaponId> Grenades { get; init; }

    /// <summary><c>m_aAmmoMax</c>, indexed by <see cref="TFAmmo"/>: the <c>AmmoMax</c> block.</summary>
    public required ImmutableArray<int> AmmoMax { get; init; }

    /// <summary><c>m_aBuildable</c>: <c>buildable1..6</c>, <see cref="TFObjectType.Last"/> where unset.</summary>
    public required ImmutableArray<TFObjectType> Buildables { get; init; }

    /// <summary><c>m_bDontDoAirwalk</c>.</summary>
    public required bool DontDoAirwalk { get; init; }

    /// <summary><c>m_bDontDoNewJump</c>.</summary>
    public required bool DontDoNewJump { get; init; }

    /// <summary><c>m_vecThirdPersonOffset</c>: (<c>cameraoffset_forward</c>, <c>_right</c>, <c>_up</c>).</summary>
    public required Vector ThirdPersonOffset { get; init; }

    /// <summary><c>m_szDeathSound[0..3]</c>, indexed by <see cref="TFDeathSound"/>.</summary>
    public required ImmutableArray<string> DeathSounds { get; init; }

    /// <summary>The most this class carries of <paramref name="ammo"/> (<c>m_aAmmoMax[ammo]</c>).</summary>
    public int MaxAmmo(TFAmmo ammo)
    {
        if (ammo < TFAmmo.Dummy || ammo >= TFAmmo.Count)
            throw new ArgumentOutOfRangeException(nameof(ammo), ammo, "TF_AMMO_COUNT bounds m_aAmmoMax");
        return AmmoMax[(int)ammo];
    }

    /// <summary><c>GetDeathSound( nType )</c> for the four a script names.</summary>
    public string DeathSound(TFDeathSound type) => DeathSounds[(int)type];

    /// <summary>
    /// What <c>TFPlayerClassData_t</c>'s constructor leaves: the data of a
    /// class whose script stock never parsed (no weapons, no buildables, no
    /// ammo, empty strings).
    /// </summary>
    public static TFClassData Missing(TFClass playerClass) => new()
    {
        Class = playerClass,
        IsParsed = false,
        Name = string.Empty,
        Model = string.Empty,
        ModelHwm = string.Empty,
        ModelHands = string.Empty,
        LocalizeName = string.Empty,
        MaxSpeed = 0f,
        MaxHealth = 0,
        MaxArmor = 0,
        Weapons = Filled(TFClassLimits.WeaponCount, TFWeaponId.None),
        Grenades = Filled(TFClassLimits.GrenadeCount, TFWeaponId.None),
        AmmoMax = Filled((int)TFAmmo.Count, 0),
        Buildables = Filled(TFClassLimits.BlueprintCount, TFObjectType.Last),
        DontDoAirwalk = false,
        DontDoNewJump = false,
        ThirdPersonOffset = new Vector(0f, 0f, 0f),
        DeathSounds = Filled(TFClassDataParser.DeathSoundCount, string.Empty),
    };

    /// <summary>
    /// <c>CTFPlayerClassDataMgr::Init</c>'s special case: Undefined has no
    /// script, but a name, a localize name and the scout model, since an
    /// undefined player still needs one.
    /// </summary>
    public static TFClassData Undefined => Missing(TFClass.Undefined) with
    {
        Name = "undefined",
        Model = "models/player/scout.mdl",
        LocalizeName = "undefined",
    };

    internal static ImmutableArray<T> Filled<T>(int count, T value)
    {
        var builder = ImmutableArray.CreateBuilder<T>(count);
        for (int i = 0; i < count; i++)
            builder.Add(value);
        return builder.MoveToImmutable();
    }
}
