//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Player;

/// <summary>
/// The winners' crits. Two of stock's: <c>CTFGameRules::SetWinningTeam</c>
/// gives every living player of the winning team
/// <c>TF_COND_CRITBOOSTED_BONUS_TIME</c> for <c>GetBonusRoundTime + 1</c>
/// seconds (tf_gamerules.cpp:9568-9592), and <c>CalcIsAttackCritical</c>
/// crits every shot of the winning team while the round is in
/// <c>GR_STATE_TEAM_WIN</c> (tf_weaponbase.cpp:1563), which
/// <see cref="TFRoundWinners"/> answers the weapons. A stalemate win
/// (<see cref="TFTeam.Unassigned"/>) boosts nobody.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRoundWinCrits(TFRoundWinners winners, ITeamRoundRoster roster, ITFPlayerConditions conditions)
    : ITeamRoundListener, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.RoundWinCrits;

    public void OnStateChanged(RoundState from, RoundState to)
    {
        if (to != RoundState.TeamWin)
            winners.WinningTeam = TFTeam.Unassigned;
    }

    public void OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds)
    {
        winners.WinningTeam = team;
        if (team == TFTeam.Unassigned)
            return;

        foreach (var player in roster.Players(team))
        {
            if (roster.IsAlive(player))
                conditions.Add(player, TFCond.CritboostedBonusTime, bonusSeconds);
        }
    }
}
