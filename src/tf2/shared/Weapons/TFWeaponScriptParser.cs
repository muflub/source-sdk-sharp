//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// <c>CTFWeaponInfo::Parse</c> (<c>shared/tf/tf_weapon_parse.cpp:61-230</c>):
/// the base keys through the core's <see cref="WeaponScriptParser"/>
/// (<c>FileWeaponInfo_t::Parse</c>), then TF's. Key spellings, defaults and
/// the secondary mode's inheritance are stock's, typo included: the idle-empty
/// time is read as <c>"TimeIdleEmpy"</c>, so a script's <c>"TimeIdleEmpty"</c>
/// is never read and the field stays 0 (:75, :113).
/// </summary>
public static class TFWeaponScriptParser
{
    /// <summary>Parses the <c>WeaponData</c> block of the script named <paramref name="weaponName"/>.</summary>
    public static TFWeaponScript Parse(KeyValuesText data, string weaponName)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(weaponName);

        var primary = new TFWeaponModeData
        {
            Damage = data.GetInt("Damage", 0),
            Range = data.GetFloat("Range", 8192f),
            BulletsPerShot = data.GetInt("BulletsPerShot", 0),
            Spread = data.GetFloat("Spread", 0f),
            PunchAngle = data.GetFloat("PunchAngle", 0f),
            TimeFireDelay = data.GetFloat("TimeFireDelay", 0f),
            TimeIdle = data.GetFloat("TimeIdle", 0f),
            TimeIdleEmpty = data.GetFloat("TimeIdleEmpy", 0f),
            TimeReloadStart = data.GetFloat("TimeReloadStart", 0f),
            TimeReload = data.GetFloat("TimeReload", 0f),
            DrawCrosshair = data.GetInt("DrawCrosshair", 1) > 0,
            AmmoPerShot = data.GetInt("AmmoPerShot", 1),
            UseRapidFireCrits = data.GetInt("UseRapidFireCrits", 0) != 0,
            Projectile = TFProjectileNames.IdOf(Text(data, "ProjectileType", "projectile_none")),
            ProjectileSpeed = data.GetFloat("ProjectileSpeed", 0f),
            SmackDelay = data.GetFloat("SmackDelay", 0.2f),
        };

        // Each secondary key defaults to the primary's value, except the rapid
        // fire flag (its own 0) and the projectile, which keeps the primary's
        // only when the secondary spelling matches nothing. The spelling's
        // default is "projectile_none", the name of entry 0 (TF_PROJECTILE_NONE)
        // by its own default in the primary read, so an absent key reads NONE
        // (:118-132; the SDK does not define the names table itself).
        var secondary = new TFWeaponModeData
        {
            Damage = data.GetInt("Secondary_Damage", primary.Damage),
            Range = data.GetFloat("Secondary_Range", primary.Range),
            BulletsPerShot = data.GetInt("Secondary_BulletsPerShot", primary.BulletsPerShot),
            Spread = data.GetFloat("Secondary_Spread", primary.Spread),
            PunchAngle = data.GetFloat("Secondary_PunchAngle", primary.PunchAngle),
            TimeFireDelay = data.GetFloat("Secondary_TimeFireDelay", primary.TimeFireDelay),
            TimeIdle = data.GetFloat("Secondary_TimeIdle", primary.TimeIdle),
            TimeIdleEmpty = data.GetFloat("Secondary_TimeIdleEmpy", primary.TimeIdleEmpty),
            TimeReloadStart = data.GetFloat("Secondary_TimeReloadStart", primary.TimeReloadStart),
            TimeReload = data.GetFloat("Secondary_TimeReload", primary.TimeReload),
            DrawCrosshair = data.GetInt("Secondary_DrawCrosshair", primary.DrawCrosshair ? 1 : 0) > 0,
            AmmoPerShot = data.GetInt("Secondary_AmmoPerShot", primary.AmmoPerShot),
            UseRapidFireCrits = data.GetInt("Secondary_UseRapidFireCrits", 0) != 0,
            Projectile = SecondaryProjectile(Text(data, "Secondary_ProjectileType", "projectile_none"), primary.Projectile),
            ProjectileSpeed = 0f,
            SmackDelay = data.GetFloat("Secondary_SmackDelay", 0.2f),
        };

        return new TFWeaponScript
        {
            Base = WeaponScriptParser.Parse(data, weaponName),
            Id = TFWeaponNames.IdOf(weaponName),
            Primary = primary,
            Secondary = secondary,
            Category = Category(Text(data, "WeaponType", "")),
            Grenade = data.GetInt("Grenade", 0) != 0,
            DamageRadius = data.GetFloat("DamageRadius", 0f),
            PrimerTime = data.GetFloat("PrimerTime", 0f),
            SuppressGrenadeTimer = data.GetInt("PlayGrenTimer", 1) <= 0,
            LowerWeapon = data.GetInt("LowerMainWeapon", 0) != 0,
            HasTeamSkinsViewmodel = data.GetInt("HasTeamSkins_Viewmodel", 0) != 0,
            HasTeamSkinsWorldmodel = data.GetInt("HasTeamSkins_Worldmodel", 0) != 0,
            DoInstantEjectBrass = data.GetInt("DoInstantEjectBrass", 1) != 0,
            BrassModel = Clipped(data, "BrassModel"),
            MuzzleFlashModel = Clipped(data, "MuzzleFlashModel"),
            MuzzleFlashModelDuration = data.GetFloat("MuzzleFlashModelDuration", 0.2f),
            MuzzleFlashParticleEffect = Clipped(data, "MuzzleFlashParticleEffect"),
            TracerEffect = Clipped(data, "TracerEffect"),
            ExplosionSound = Clipped(data, "ExplosionSound"),
            ExplosionEffect = Clipped(data, "ExplosionEffect"),
            ExplosionPlayerEffect = Clipped(data, "ExplosionPlayerEffect"),
            ExplosionWaterEffect = Clipped(data, "ExplosionWaterEffect"),
            DontDrop = data.GetInt("DontDrop", 0) > 0,
            IconInactive = HudIcon(data, "weapon"),
            IconActive = HudIcon(data, "weapon_s"),
        };
    }

    /// <summary>One named <c>TextureData</c> entry (hud.cpp:118-141): the
    /// file and the pixel rect; no file, no texture, as stock's null
    /// <c>pszFormat</c> arm leaves the entry unread.</summary>
    private static TFHudIcon? HudIcon(KeyValuesText data, string entry)
    {
        var texture = data.Find("TextureData")?.Find(entry);
        string? file = texture?.GetString("file", null);
        return file is null ? null : new TFHudIcon(file,
            texture!.GetInt("x", 0), texture.GetInt("y", 0),
            texture.GetInt("width", 0), texture.GetInt("height", 0));
    }

    /// <summary>The <c>"WeaponType"</c> words (:134-166), compared case-sensitively as <c>Q_strcmp</c> does.</summary>
    public static TFWeaponCategory Category(string word) => word switch
    {
        "secondary" => TFWeaponCategory.Secondary,
        "melee" => TFWeaponCategory.Melee,
        "grenade" => TFWeaponCategory.Grenade,
        "building" => TFWeaponCategory.Building,
        "pda" => TFWeaponCategory.Pda,
        "item1" => TFWeaponCategory.Item1,
        "item2" => TFWeaponCategory.Item2,
        _ => TFWeaponCategory.Primary,
    };

    // The secondary loop starts from the primary's projectile and overwrites it
    // with the first name that matches; "projectile_none" is entry 0.
    private static TFProjectile SecondaryProjectile(string word, TFProjectile primary) =>
        word == "projectile_none" ? TFProjectile.None
        : TFProjectileNames.IdOf(word) is var id && id != TFProjectile.None ? id
        : primary;

    private static string Text(KeyValuesText data, string key, string fallback) =>
        data.GetString(key, fallback) ?? fallback;

    // The string fields stock copies into its 128-byte buffers (Q_strncpy).
    private static string Clipped(KeyValuesText data, string key) =>
        TFClassDataParser.Clip(Text(data, key, ""));
}
