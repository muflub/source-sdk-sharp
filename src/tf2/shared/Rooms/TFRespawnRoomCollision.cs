//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Rules;

namespace TF2.Shared.Rooms;

/// <summary>
/// The one answer both sides give to "does a trace hit this respawn-room
/// visualizer": stock's <c>CFuncRespawnRoomVisualizer::ShouldCollide</c>
/// (func_respawnroom.cpp:494-523) and <c>C_FuncRespawnRoomVisualizer::ShouldCollide</c>
/// (c_func_respawnroom.cpp:69-96), the same switch. Nobody is stopped while the
/// round is in its win state or the wall has no team; a player-movement trace
/// is stopped only by a mask carrying the wall's own team's contents bit (the
/// mover's mask carries the OTHER team's, so an enemy is blocked and a
/// teammate passes); a trace in any other group passes.
/// </summary>
public static class TFRespawnRoomCollision
{
    public static bool Collides(bool teamWin, int wallTeam, CollisionGroup group, int contentsMask)
    {
        // Respawn rooms are open in win state.
        if (teamWin)
            return false;

        if (wallTeam == (int)TFTeam.Unassigned)
            return false;

        if (group == TFTeamCollision.PlayerMovement)
        {
            return wallTeam switch
            {
                (int)TFTeam.Blue => (contentsMask & TFContents.BlueTeam) != 0,
                (int)TFTeam.Red => (contentsMask & TFContents.RedTeam) != 0,
                _ => true,
            };
        }

        return false;
    }
}
