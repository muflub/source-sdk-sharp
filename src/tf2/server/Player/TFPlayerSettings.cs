//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

// The per-player settings half of stock's CTeamplayRules::ClientSettingsChanged
// (tf_gamerules.cpp:10275-10281): the convars the game caches onto the player
// when their userinfo changes. The name half is the core PlayerNames'; the
// flip-viewmodels word waits for the viewmodels that would read it.

/// <summary>
/// The player's <c>fov_desired</c>, clamped to the view's band as stock's
/// <c>SetDefaultFOV</c> takes it — 75, the floor stock clamps to regardless
/// of the convar's own 20, to MAX_FOV's 90 (<c>shareddefs.h:274</c>) —
/// written onto the player's record for the view to answer unzoomed with.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerSettings(IConnectedClients? clients = null, IEnginePlayers? players = null) : IClientLifecycle
{
    /// <summary>Stock's clamp, the convar's floor being 20 but the server's 75 (tf_gamerules.cpp:10277).</summary>
    public const int Floor = 75;

    /// <summary>MAX_FOV (shareddefs.h:274), the convar's ceiling too.</summary>
    public const int Ceiling = 90;

    public int ClientLifecycleOrder => TFPlayerOrders.Settings;

    public void OnClientSettingsChanged(int entIndex)
    {
        if (players is null)
            return;

        var handle = players.GetHandle(entIndex);
        if (handle.Equals(EntityHandle.None))
            return;

        players.SetDefaultFov(handle, Fov(clients?.ClientConVar(entIndex, "fov_desired")));
    }

    /// <summary>The clamped desire; a value the client never sent is the floor, as atoi's 0 clamps to.</summary>
    public static int Fov(string? desired) =>
        int.TryParse(desired, out int fov) ? Math.Clamp(fov, Floor, Ceiling) : Floor;
}
