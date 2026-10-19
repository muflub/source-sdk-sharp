namespace SourceSharp.Abi;

//=============================================================================
// Server-side core services for hosting a game (plan_host_sdk §2.8). Generic:
// what any mod needs to tell a hosting service who is connected, act on its
// commands, and report the port it serves on.
//=============================================================================

/// <summary>What the engine knows about one connected client, by entity index.</summary>
public interface IClientInfo
{
    /// <summary>The SteamID64 the engine authenticated, or 0 when there is none yet.</summary>
    ulong SteamId64(int entIndex);

    /// <summary>The address string the engine's network channel has, unchanged; null for a bot or an empty slot.</summary>
    string? Address(int entIndex);

    /// <summary>
    /// The client's average outgoing latency in seconds
    /// (<c>GetPlayerNetInfo(entIndex)->GetAvgLatency(FLOW_OUTGOING)</c>), or
    /// null for an empty slot or a client with no net channel (a bot).
    /// </summary>
    float? AvgLatency(int entIndex);

    /// <summary>A client's convar as it told the server (<c>GetClientConVarValue</c>), or null.</summary>
    string? ClientConVar(int entIndex, string name);
}
