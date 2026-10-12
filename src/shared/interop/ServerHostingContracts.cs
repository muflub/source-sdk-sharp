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
    float? AvgLatency(int entIndex) => null;

    /// <summary>A client's convar as it told the server (<c>GetClientConVarValue</c>), or null.</summary>
    string? ClientConVar(int entIndex, string name) => null;
}

/// <summary>The connected clients, and what the server can do to one.</summary>
public interface IConnectedClients : IClientInfo
{
    /// <summary>The engine's userid for the client, or -1 for an empty slot.</summary>
    int UserId(int entIndex);

    /// <summary>Disconnects the client, showing <paramref name="reason"/>.</summary>
    void Kick(int entIndex, string reason);

    /// <summary>Runs <paramref name="command"/> on the client's console, as the engine's ClientCommand does.</summary>
    void ClientCommand(int entIndex, string command);

    /// <summary>
    /// Tells the client to reconnect (the core's SsReconnect message): its route
    /// now points at another server, and reconnecting lands it there.
    /// </summary>
    void Reconnect(int entIndex);
}

/// <summary>
/// A client's life on this server, as a hook: implement it on a registered
/// service and <c>ClientLifecycleDriver</c> calls it (plan_host_sdk §2.11).
/// </summary>
public interface IClientLifecycle
{
    /// <summary>Unique among implementations; <see cref="ModStages.First"/> + n for a mod.</summary>
    int ClientLifecycleOrder => 0;

    /// <summary>A client asks to connect. Return a reason to refuse it, or null to admit it.</summary>
    string? OnClientConnecting(string name, string address) => null;

    /// <summary>The client is fully in the game: the engine's "client active".</summary>
    void OnClientActive(int entIndex) { }

    /// <summary>The client left, by any route. Not called for a level change.</summary>
    void OnClientDisconnected(int entIndex) { }

    /// <summary>
    /// The client sent a SourceSharp net message under <paramref name="key"/>
    /// (its side's <c>IEngineNetworking.SendServerMessage</c>). Return true to
    /// claim it; the first participant to claim it wins. The bytes are the
    /// client's: parse them as untrusted input, and copy what outlives the call.
    /// </summary>
    bool OnClientMessage(int entIndex, string key, ReadOnlySpan<byte> payload) => false;
}

/// <summary>
/// Told when a player entity is removed. ClientLifecycleDriver turns that into
/// <see cref="IClientLifecycle.OnClientDisconnected"/> unless a level is
/// shutting down, when every player entity goes and the clients stay.
/// </summary>
public interface IPlayerRemovals
{
    void PlayerRemoved(int entIndex);
}

/// <summary>The UDP port the server's socket actually bound, read from the kernel.</summary>
public interface IBoundPort
{
    /// <summary>
    /// The port, or null when it cannot be told (no socket yet, or more than
    /// one candidate). Never the requested <c>-port</c>, which the engine walks
    /// past silently when it is taken.
    /// </summary>
    int? Read();
}

/// <summary>The server console, as a hosting service drives it.</summary>
public interface IServerConsole
{
    /// <summary>
    /// Runs <paramref name="command"/> now and returns the managed console
    /// output it produced. Native output is not captured (decision 6.2).
    /// </summary>
    string Execute(string command);

    /// <summary>Asks the engine to shut down cleanly and the process to exit with <paramref name="exitCode"/>.</summary>
    void Quit(int exitCode);
}

/// <summary>Text to players' chat.</summary>
public interface IChat
{
    void PrintAll(string text);

    void Print(int entIndex, string text);
}
