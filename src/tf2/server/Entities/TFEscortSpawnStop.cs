//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;

namespace TF2.Server.Entities;

/// <summary>
/// The spawn-time stop of the escort schedule: <c>CBaseMultiplayerPlayer::Spawn</c>
/// calls <c>StopScoringEscortPoints()</c> before the base Spawn
/// (basemultiplayerplayer.cpp:230-237), so a respawned player scores no
/// further points from the schedule their old life held — the next
/// <c>StartScoringEscortPoints</c>, the area's touch arm, renews it. The
/// schedule itself lives on the areas (<see cref="TriggerAreaCapture"/>; stock
/// keeps it on the player), so the spawn walks them as stock's per-player
/// state stood on one: <c>StopScoringEscortPoints</c> unschedules the player
/// and leaves the accumulator standing, as stock's stop does
/// (:356-359 — the think context goes, nothing else).
/// </summary>
[SideService(Side.Server)]
public sealed class TFEscortSpawnStop(IObjectiveEntities entities) : IPlayerSpawnHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.EscortSpawnStop;

    public void OnSpawning(EntityHandle player, float now)
    {
        foreach (var area in entities.Areas)
            area.StopScoringEscortPoints(player);
    }

    public void OnSpawned(EntityHandle player, float now) { }
}
