//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// A player's death on TF's scoreboard: the victim's <c>TFSTAT_DEATHS</c>
/// (<c>CTFGameStats::Event_PlayerKilled</c>, tf_gamestats.cpp:1547-1552) and
/// the attacker's kill, or suicide when they killed themselves
/// (<c>CBasePlayer::Event_KilledOther</c>, player.cpp:9598-9609, reached from
/// the victim's <c>CBaseCombatCharacter::Event_Killed</c> through
/// <c>info.GetAttacker()</c>, basecombatcharacter.cpp:1627; its
/// <c>CTFGameStats::Event_PlayerKilledOther</c> books <c>TFSTAT_KILLS</c>,
/// tf_gamestats.cpp:1292-1300, and <c>Event_PlayerSuicide</c>
/// <c>TFSTAT_SUICIDES</c>, :1280-1287). The kill is a point of
/// <c>CalcPlayerScore</c> (<see cref="TFPlayerStats.ScoreKill"/>). An attacker
/// that is not a player (the world, a prop) books nothing.
/// </summary>
[SideService(Side.Server)]
public sealed class TFKillStats(ITFPlayerStats stats, IEnginePlayers players) : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.KillStats;

    public void OnPlayerDied(PlayerDeath death)
    {
        stats.Increment(death.Player, TFStat.Deaths);

        EntityHandle attacker = death.Attacker;
        if (attacker == EntityHandle.None || players.GetHandle(attacker.EntryIndex) != attacker)
            return;

        stats.Increment(attacker, attacker == death.Player ? TFStat.Suicides : TFStat.Kills);
    }
}
