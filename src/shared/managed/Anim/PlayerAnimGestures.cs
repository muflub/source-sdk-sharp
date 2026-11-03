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
    /// <c>FireBolt</c> does not, nor HL2MP's physcannon (its punt and its pull
    /// in weapon_physcannon.cpp call no SetAnimation at all).
    /// </summary>
    public static bool ShotShowsGesture(string weaponClass)
        => Suffix(weaponClass) is not null && weaponClass is not ("weapon_crossbow" or "weapon_physcannon");

    /// <summary>
    /// Whether a melee swing shows the attack gesture: <c>CBaseHL2MPBludgeonWeapon::Swing</c>
    /// calls <c>SetAnimation( PLAYER_ATTACK1 )</c> on a hit and a miss alike,
    /// for the crowbar and the stunstick (their acttables' MELEE row).
    /// </summary>
    public static bool SwingShowsGesture(string weaponClass) => weaponClass is "weapon_crowbar" or "weapon_stunstick";

    /// <summary>
    /// Whether an alt-fire paid from the secondary pool shows the attack
    /// gesture: the SMG1's grenade (<c>CWeaponSMG1::SecondaryAttack</c>'s
    /// "player shoot animation"); the AR2's energy ball calls none.
    /// </summary>
    public static bool SecondaryShowsGesture(string weaponClass) => weaponClass == "weapon_smg1";

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
