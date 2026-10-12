//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Server;

/// <summary>
/// The scoreboard's ping, as stock computes it: UTIL_GetPlayerConnectionInfo's
/// correction of the channel's average latency (util.cpp) and CPlayerResource's
/// cadence and smoothing (player_resource.cpp).
/// </summary>
public static class PlayerPing
{
    /// <summary>CPlayerResource sets pings every this many thinks (20 x 0.1 s = 2 s).</summary>
    public const int ThinksPerUpdate = 20;

    /// <summary>What stock keeps of the last ping, and takes of the new one.</summary>
    public const float Keep = 0.8f, Take = 0.2f;

    /// <summary>Stock's bounds: no ping under 5 ms is shown, none over 1000.</summary>
    public const int Min = 5, Max = 1000;

    /// <summary>
    /// One sample, in ms: the average outgoing latency less half a command
    /// interval and one tick (the client's own delay), less half a tick more
    /// (GoldSrc parity), truncated and clamped to 5..1000.
    /// </summary>
    public static int Sample(float avgLatencySeconds, int cmdRate, float tickInterval)
    {
        int rate = Math.Max(1, cmdRate);
        float latency = avgLatencySeconds - (0.5f / rate + tickInterval);
        latency -= 0.5f * tickInterval;
        int ping = (int)(latency * 1000.0f);
        return Math.Clamp(ping, Min, Max);
    }

    /// <summary>The smoothed ping: <c>ping = Keep * old + Take * sample</c>, truncated as stock's int assignment.</summary>
    public static int Smooth(int old, int sample) => (int)(Keep * old + Take * sample);

    /// <summary>Whether a think (counted from 1, as stock increments before it asks) is a ping update.</summary>
    public static bool IsUpdate(int thinkCount) => thinkCount % ThinksPerUpdate == 0;
}
