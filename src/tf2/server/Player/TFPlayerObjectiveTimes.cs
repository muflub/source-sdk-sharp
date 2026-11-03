//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Player;

/// <summary>
/// <c>CBaseMultiplayerPlayer</c>'s last-objective time
/// (<c>SetLastObjectiveTime</c> / <c>GetTimeSinceLastObjective</c>,
/// server/player.h:845-846, <c>m_flLastObjectiveTime</c> initialised to
/// <c>-1</c> at player.cpp:654): when the player last touched something the
/// game mode considers an objective. The capture area writes it for every
/// capper and every blocker each think
/// (<c>CTriggerAreaCapture::CaptureThink</c>,
/// server/trigger_area_capture.cpp:369 and :382).
///
/// <para>
/// Stock answers the time against <c>gpGlobals->curtime</c> inside the getter;
/// the managed side is passed the clock's value on both calls, as everywhere
/// else in the port the caller supplies the tick's time.
/// </para>
/// </summary>
public interface ITFPlayerObjectiveTimes
{
    /// <summary><c>SetLastObjectiveTime(flTime)</c>: the player touched an objective at <paramref name="time"/>.</summary>
    void SetLastObjectiveTime(EntityHandle player, float time);

    /// <summary>
    /// <c>GetTimeSinceLastObjective()</c>: how long ago, or
    /// <see cref="TFPlayerObjectiveTimes.NoObjective"/> for a player who never touched one.
    /// </summary>
    float GetTimeSinceLastObjective(int playerIndex, float now);
}

/// <summary>
/// The server's <see cref="ITFPlayerObjectiveTimes"/>: the times by player
/// index, cleared when the level ends and when the player leaves.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerObjectiveTimes)])]
public sealed class TFPlayerObjectiveTimes : ITFPlayerObjectiveTimes, IOrderedHook, IGameLifecycle, IClientLifecycle
{
    /// <summary><c>m_flLastObjectiveTime</c>'s never-set sentinel (player.cpp:654).</summary>
    private const float Never = -1f;

    /// <summary>What <c>GetTimeSinceLastObjective</c> answers a player who never touched one.</summary>
    public const float NoObjective = 999f;

    private readonly Dictionary<int, float> _lastTime = new();

    public int HookOrder => TFPlayerOrders.ObjectiveTimes;

    public int LifecycleOrder => TFPlayerOrders.ObjectiveTimes;

    public int ClientLifecycleOrder => TFPlayerOrders.ObjectiveTimes;

    public void SetLastObjectiveTime(EntityHandle player, float time) => _lastTime[player.EntryIndex] = time;

    public float GetTimeSinceLastObjective(int playerIndex, float now)
        => _lastTime.TryGetValue(playerIndex, out float last) && last != Never ? now - last : NoObjective;

    void IGameLifecycle.OnLevelShutdown() => _lastTime.Clear();

    void IClientLifecycle.OnClientDisconnected(int entIndex) => _lastTime.Remove(entIndex);
}
