//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared.Rules;

/// <summary>
/// The one answer both sides give to "does a trace hit this TF2 player":
/// stock's <c>CTFPlayer::ShouldCollide</c> (tf_player.cpp:10159-10178) and
/// <c>C_TFPlayer::ShouldCollide</c> (c_tf_player.cpp:7705-7724), the same
/// switch. In a player-movement trace, while <c>tf_avoidteammates</c>
/// stands, a red or blue player is hit only by a mask carrying ITS OWN team's
/// contents bit; the mover's mask (<c>CTFGameMovement::PlayerSolidMask</c>,
/// the movement rules' mask arm) carries only the OTHER team's, so a
/// teammate is refused and an enemy is not. The server's rule asks it over
/// the roster's team, the client's over the player resource's, so the
/// prediction's player clip passes exactly the teammates the server passes.
/// </summary>
public static class TFTeamCollision
{
    /// <summary>The trace group the arm narrows: stock's <c>COLLISION_GROUP_PLAYER_MOVEMENT</c>.</summary>
    public const CollisionGroup PlayerMovement = (CollisionGroup)EngineCollisionGroup.PlayerMovement;

    /// <summary>
    /// Whether a trace of <paramref name="group"/> and
    /// <paramref name="contentsMask"/> hits a player on
    /// <paramref name="team"/>. Stock's switch falls through for a spectator,
    /// an unassigned entity and every non-player: true, for the chain's other
    /// arms and the group matrix to answer as they did.
    /// </summary>
    public static bool Collides(CollisionGroup group, bool avoidTeammates, TFTeam team, int contentsMask)
    {
        if (group != PlayerMovement || !avoidTeammates)
            return true;

        return team switch
        {
            TFTeam.Red => (contentsMask & TFContents.RedTeam) != 0,
            TFTeam.Blue => (contentsMask & TFContents.BlueTeam) != 0,
            _ => true,
        };
    }
}
