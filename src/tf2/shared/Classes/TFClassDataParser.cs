//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// <c>TFPlayerClassData_t::ParseData</c> (tf_classdata.cpp): one class
/// script's <c>PlayerClass</c> block to a <see cref="TFClassData"/>. Every
/// read is stock's <c>KeyValues</c> getter with stock's default (keys are
/// case-insensitive, an absent number reads 0, an absent name reads ""), and
/// every string is clipped as <c>Q_strncpy</c> into stock's 128-byte buffers
/// clips it. Unknown keys are ignored, as stock ignores them.
///
/// <para>
/// One addition: a script without <c>speed_max</c> or <c>health_max</c>
/// still reads 0 there, as stock's does, but says so in the log; stock is
/// silent, and a class that cannot move or has no health is otherwise found
/// only by playing it.
/// </para>
/// </summary>
public static class TFClassDataParser
{
    /// <summary><c>TF_NAME_LENGTH</c> and <c>MAX_PLAYERCLASS_SOUND_LENGTH</c> (tf_classdata.h): both 128.</summary>
    public const int NameLength = 128;

    /// <summary>The death sounds a script names: <c>DEATH_SOUND_GENERIC..EXPLOSION</c>.</summary>
    public const int DeathSoundCount = 4;

    /// <param name="playerClass">The class the script is for.</param>
    /// <param name="data">The script's root block (<c>PlayerClass</c>).</param>
    /// <param name="log">Where a missing <c>speed_max</c>/<c>health_max</c> is reported.</param>
    /// <param name="origin">The script's name, for the log.</param>
    public static TFClassData Parse(TFClass playerClass, KeyValuesText data, ILog log, string origin)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(log);

        WarnIfAbsent(data, "speed_max", log, origin);
        WarnIfAbsent(data, "health_max", log, origin);

        var weapons = ImmutableArray.CreateBuilder<TFWeaponId>(TFClassLimits.WeaponCount);
        for (int i = 0; i < TFClassLimits.WeaponCount; i++)
            weapons.Add(TFWeaponNames.IdOf(String(data, "weapon" + (i + 1).ToString(CultureInfo.InvariantCulture))));

        // Stock fills m_aAmmoMax[1..] only when the block exists; without it
        // every entry keeps the constructor's TF_AMMO_DUMMY (0).
        var ammo = TFClassData.Filled((int)TFAmmo.Count, 0).ToBuilder();
        if (data.FindPath("AmmoMax") is { } ammoBlock)
        {
            for (var a = TFAmmo.Primary; a < TFAmmo.Count; a++)
                ammo[(int)a] = ammoBlock.GetInt(TFAmmoNames.Name(a), 0);
        }

        var buildables = ImmutableArray.CreateBuilder<TFObjectType>(TFClassLimits.BlueprintCount);
        for (int i = 0; i < TFClassLimits.BlueprintCount; i++)
            buildables.Add(TFObjectNames.IdOf(String(data, "buildable" + (i + 1).ToString(CultureInfo.InvariantCulture))));

        return new TFClassData
        {
            Class = playerClass,
            IsParsed = true,
            Name = Clip(String(data, "name")),
            Model = Clip(String(data, "model")),
            ModelHwm = Clip(String(data, "model_hwm")),
            ModelHands = Clip(String(data, "model_hands")),
            LocalizeName = Clip(String(data, "localize_name")),
            MaxSpeed = data.GetFloat("speed_max", 0f),
            MaxHealth = data.GetInt("health_max", 0),
            MaxArmor = data.GetInt("armor_max", 0),
            Weapons = weapons.MoveToImmutable(),
            Grenades = [TFWeaponNames.IdOf(String(data, "grenade1")), TFWeaponNames.IdOf(String(data, "grenade2"))],
            AmmoMax = ammo.MoveToImmutable(),
            Buildables = buildables.MoveToImmutable(),
            DontDoAirwalk = data.GetInt("DontDoAirwalk", 0) > 0,
            DontDoNewJump = data.GetInt("DontDoNewJump", 0) > 0,
            ThirdPersonOffset = new Vector(
                data.GetFloat("cameraoffset_forward", 0f),
                data.GetFloat("cameraoffset_right", 0f),
                data.GetFloat("cameraoffset_up", 0f)),
            DeathSounds =
            [
                Clip(String(data, "sound_death", "Player.Death")),
                Clip(String(data, "sound_crit_death", "TFPlayer.CritDeath")),
                Clip(String(data, "sound_melee_death", "Player.MeleeDeath")),
                Clip(String(data, "sound_explosion_death", "Player.ExplosionDeath")),
            ],
        };
    }

    /// <summary>
    /// <c>Q_strncpy( dst, src, 128 )</c>: at most 127 bytes and the NUL. The
    /// shipped scripts are ASCII; a longer UTF-8 value is cut at a byte, as
    /// stock cuts it.
    /// </summary>
    public static string Clip(string value)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes < NameLength)
            return value;
        return Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value), 0, NameLength - 1);
    }

    private static void WarnIfAbsent(KeyValuesText data, string key, ILog log, string origin)
    {
        if (data.FindPath(key) is null)
            log.Warning($"[TF2/Classes] {origin} has no \"{key}\": it reads 0, stock's GetFloat/GetInt default\n");
    }

    private static string String(KeyValuesText data, string key, string defaultValue = "")
        => data.GetString(key, defaultValue) ?? defaultValue;
}
