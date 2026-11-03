//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Client.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The scoreboard details grid's counts, off the live <c>tf_player_manager</c>
/// (stock reads each of UpdatePlayerDetails's arms, tf_clientscoreboard.cpp:
/// 1880-1964, off the player's shared stats or <c>g_TF_PR</c>; this build
/// carries the round's counts on the resource — see the netstate). Support is
/// <c>CalcPlayerSupportScore</c>'s client arm summed here
/// (tf_gamerules.cpp:17072-17075).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFScoreboardDetails)])]
public sealed class TFScoreboardDetails(TFClientPlayerResources resources) : ITFScoreboardDetails
{
    public TFScoreboardPlayerDetails Of(int playerIndex)
    {
        if (resources.Live is not { } resource)
            return default;                  // no resource: the grid reads zeros

        var state = resource.State;
        int bonus = state.BonusPoints[playerIndex];
        return new TFScoreboardPlayerDetails(
            Kills: state.TotalScore[playerIndex],
            Assists: state.KillAssists[playerIndex],
            Destruction: state.BuildingsDestroyed[playerIndex],
            Captures: state.Captures[playerIndex],
            Defenses: state.Defenses[playerIndex],
            Dominations: state.Dominations[playerIndex],
            Revenge: state.Revenges[playerIndex],
            Healing: state.Healing[playerIndex],
            Invulns: state.Invulns[playerIndex],
            Teleports: state.Teleports[playerIndex],
            Headshots: state.Headshots[playerIndex],
            Backstabs: state.Backstabs[playerIndex],
            Bonus: bonus,
            Support: state.DamageAssist[playerIndex] + state.HealingAssist[playerIndex] +
                     state.DamageBlocked[playerIndex] + bonus * 25,
            Damage: state.Damage[playerIndex]);
    }
}
