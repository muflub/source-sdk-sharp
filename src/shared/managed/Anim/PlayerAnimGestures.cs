//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The HL2MP weapons' third-person shot and reload gestures: each weapon's
/// <c>m_acttable</c> rows for <c>ACT_HL2MP_GESTURE_RANGE_ATTACK</c> and
/// <c>ACT_HL2MP_GESTURE_RELOAD</c> (game/shared/hl2mp/weapon_*.cpp), and which
/// weapons' code shows them (<c>SetAnimation( PLAYER_ATTACK1 )</c> in their
/// fire, <c>PLAYER_RELOAD</c> in <c>DefaultReload</c>).
/// </summary>
public static class PlayerAnimGestures
{
    /// <summary>The acttable's suffix for a weapon class, or null for a class with no table.</summary>
    public static string? Suffix(string weaponClass) => weaponClass switch
    {
        "weapon_pistol" or "weapon_357" => "PISTOL",
        "weapon_smg1" => "SMG1",
        "weapon_ar2" => "AR2",
        "weapon_shotgun" => "SHOTGUN",
        "weapon_rpg" => "RPG",
        "weapon_frag" => "GRENADE",
        "weapon_crossbow" => "CROSSBOW",
        "weapon_slam" => "SLAM",
        "weapon_crowbar" or "weapon_stunstick" => "MELEE",
        "weapon_physcannon" => "PHYSGUN",
        _ => null,
    };

    /// <summary>
    /// Whether a shot shows the attack gesture: every weapon whose fire calls
    /// <c>SetAnimation( PLAYER_ATTACK1 )</c>, which the crossbow's
    /// <c>FireBolt</c> does not.
    /// </summary>
    public static bool ShotShowsGesture(string weaponClass) => Suffix(weaponClass) is not null && weaponClass != "weapon_crossbow";

    /// <summary>
    /// Whether a reload shows the reload gesture: every weapon that reloads
    /// through <c>DefaultReload</c>, which the shotgun's shell-by-shell
    /// <c>StartReload</c> does not.
    /// </summary>
    public static bool ReloadShowsGesture(string weaponClass) => Suffix(weaponClass) is not null && weaponClass != "weapon_shotgun";

    /// <summary>
    /// The gesture a client plays for an event on a player holding a weapon
    /// class: <c>Weapon_TranslateActivity( ACT_HL2MP_GESTURE_RANGE_ATTACK )</c>
    /// or <c>( ACT_HL2MP_GESTURE_RELOAD )</c>; null for any other event or a
    /// weapon with no table.
    /// </summary>
    public static string? Activity(PlayerAnimEvent e, string weaponClass)
    {
        if (Suffix(weaponClass) is not { } suffix)
            return null;
        return e switch
        {
            PlayerAnimEvent.AttackPrimary => "ACT_HL2MP_GESTURE_RANGE_ATTACK_" + suffix,
            PlayerAnimEvent.Reload => "ACT_HL2MP_GESTURE_RELOAD_" + suffix,
            _ => null,
        };
    }
}
