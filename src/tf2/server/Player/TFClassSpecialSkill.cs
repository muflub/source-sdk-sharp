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
/// The arm not ported: the demoman's shield
/// (<c>GetEquippedDemoShield</c>'s <c>DoSpecialAction</c> — no wearables
/// until Stage 2.13, deferred there). Stock's rune-charge and
/// Passtime-ball gates have no port counterpart.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFClassSkillEffects)])]
public sealed class TFClassSpecialSkill(ITFPlayerData players, IEnginePlayers enginePlayers,
                                        ITFSpyState? spies = null, TFStickyTray? tray = null,
                                        ITFBuildingPickup? pickup = null)
    : ITFClassSkillEffects
{
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
                // (:12372-12391): detonate the owner's tray. Stock asks
                // Weapon_OwnsThisID first; only a stickybomb launcher's owner
                // ever has stickies registered, so the tray answers the same.
                tray?.DetonateAll(ownerIndex);
                return true;

            case (int)TFClass.Engineer:
                // TryToPickupBuilding (:12392-12397, the lift at
                // tf_player_shared.cpp:12536).
                return pickup?.TryPickup(enginePlayers.GetHandle(ownerIndex)) ?? false;

            default:
                return false;
        }
    }
}
