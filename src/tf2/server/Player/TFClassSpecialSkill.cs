//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The class special skill a weapon's secondary reaches
/// (<c>CTFPlayer::DoClassSpecialSkill</c>,
/// <c>tf_player_shared.cpp:12330-12403</c>): the spy's cloak toggle, the
/// demoman's sticky detonation and the engineer's building lift, whichever
/// weapon is in the hands — the revolver's, the knife's, the sapper's, the
/// grenade launcher's, the wrench's (<c>tf_weaponbase_gun.cpp:245</c>,
/// <c>tf_weaponbase_melee.cpp:244</c>, <c>tf_weapon_knife.cpp:304</c>,
/// <c>tf_weapon_pda.cpp:114</c>, <c>tf_weapon_grenadelauncher.cpp:499</c>).
///
/// <para>
/// The demoman's shield arm is the charge: the pipebomb launcher owned
/// detonates as ever, else the worn demoshield's <c>DoSpecialAction</c>
/// (<see cref="TFShieldCharge"/>) starts the charge.
/// Stock's rune-charge and Passtime-ball gates have no port counterpart.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFClassSkillEffects)])]
public sealed class TFClassSpecialSkill(ITFPlayerData players, IEnginePlayers enginePlayers,
                                        ITFSpyState? spies = null, TFStickyTray? tray = null,
                                        ITFBuildingPickup? pickup = null,
                                        ITFWeaponOwners? owners = null,
                                        ITFShieldCharge? shields = null)
    : ITFClassSkillEffects
{
    /// <summary>The pipebomb launcher's weapon row, the owned item the demoman arm asks for (the trimmed schema's <c>item_class</c>).</summary>
    internal const string PipebombLauncherItem = "tf_weapon_pipebomblauncher";

    public bool DoSpecialSkill(int ownerIndex)
    {
        // The alive gate stock opens the skill with (:12332-12333); a dead
        // player's weapon runs no frame anyway.
        if (ownerIndex <= 0 || !enginePlayers.IsAlive(enginePlayers.GetHandle(ownerIndex)))
            return false;

        switch (players.Predicted(ownerIndex)?.PlayerClass ?? 0)
        {
            case (int)TFClass.Spy:
                // The taunting and m_flStealthNextChangeTime gates are the
                // watch's own (:12357-12367); the slice arms both.
                return spies?.ActivateInvisibilityWatch(ownerIndex) ?? false;

            case (int)TFClass.Demoman:
                // The pipebomb launcher's secondary from ANY weapon
                // (:12372-12391): stock asks Weapon_OwnsThisID first, and the
                // launcher-less demoman's shield takes the press instead
                // (:12363-12368, DoSpecialAction). The owned launcher is the
                // item behind its weapon row; the shield arm is the charge.
                if (owners?.ItemOf(ownerIndex, PipebombLauncherItem) is not null)
                    tray?.DetonateAll(ownerIndex);
                else
                    shields?.TryStart(ownerIndex);
                return true;

            case (int)TFClass.Engineer:
                // TryToPickupBuilding (:12392-12397, the lift at
                // tf_player_shared.cpp:12536).
                return pickup?.TryPickup(enginePlayers.GetHandle(ownerIndex)) ?? false;

            default:
                return false;
        }
    }

    /// <summary>
    /// <c>CTFPlayer::EndClassSpecialSkill</c> (tf_player_shared.cpp:12389-12416),
    /// the melee swing's ask (tf_weaponbase_melee.cpp:201): the living
    /// demoman whose launcher is elsewhere has the worn shield end its
    /// charge — the grant the swing's own crit helpers read (:12401-12404).
    /// Every other class's swing ends nothing, stock's empty arms.
    /// </summary>
    public void EndSpecialSkill(int ownerIndex)
    {
        if (ownerIndex <= 0 || !enginePlayers.IsAlive(enginePlayers.GetHandle(ownerIndex)))
            return;                                                        // :12391-12392

        if ((TFClass)(players.Predicted(ownerIndex)?.PlayerClass ?? 0) != TFClass.Demoman)
            return;                                                        // :12394

        if (owners?.ItemOf(ownerIndex, PipebombLauncherItem) is not null)
            return;                                                        // :12398-12399

        shields?.EndSpecialAction(ownerIndex);                             // :12401-12404
    }
}
