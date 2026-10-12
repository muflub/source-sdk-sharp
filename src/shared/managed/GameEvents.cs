using SourceSharp.Abi;

namespace SourceSharp.Shared;

// Game event classes, declared in shared code so both sides see the same set —
// the server the authoritative events, the client the networked copies.
//
// These replace what CEventLog did in C++. That base class existed to listen for
// a fixed set of events and write them to the log; subscribing to the same
// events from managed code covers the requirement without inheriting the rest of
// its behaviour.
//
// Each class is an INSTANCE held by the side's service container
// (GameEventRegistry.AddEventsTo) and does two things: decode the raw event
// once into typed data, and raise a C# event that other code subscribes to by
// taking IGameEvent<TArgs> through its constructor. Subscribers therefore never
// touch GameEventReader, which is a ref struct precisely because it must not
// outlive the callback. They were static classes with static events; an
// instance per container means a subscription reaches one module's events and
// no other's, and a test raising one reaches only what it built.
//
// NOTE these fire on BOTH sides. A subscriber that should act only once — award
// a point, write a stat — has to check IEngineHost.ActiveSide.
//
// Unlike user messages, order carries no meaning: the engine dispatches by name,
// so classes can be added, removed or reordered freely.

/// <summary>Someone connected. Fires before they are in the game.</summary>
public readonly record struct PlayerConnectArgs(
    string Name, int UserId, bool IsBot, string Address);

[GameEvent("player_connect")]
public sealed class PlayerConnectEvent : IGameEvent<PlayerConnectArgs>
{
    /// <summary>Raised when a player connects.</summary>
    public event Action<PlayerConnectArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerConnectArgs args) => Fired?.Invoke(args);

    /// <summary>Called by the registry. Decodes, then raises.</summary>
    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerConnectArgs(
            reader.GetString("name"),
            reader.GetInt("userid"),
            reader.GetInt("bot") != 0,
            reader.GetString("address")));
    }
}

/// <summary>Someone left, with the reason the engine recorded.</summary>
public readonly record struct PlayerDisconnectArgs(
    string Name, int UserId, string Reason);

[GameEvent("player_disconnect")]
public sealed class PlayerDisconnectEvent : IGameEvent<PlayerDisconnectArgs>
{
    public event Action<PlayerDisconnectArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerDisconnectArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerDisconnectArgs(
            reader.GetString("name"),
            reader.GetInt("userid"),
            reader.GetString("reason")));
    }
}

/// <summary>A death. The event a real mod hangs scoring off.</summary>
public readonly record struct PlayerDeathArgs(
    int UserId, int AttackerUserId, string Weapon, bool Headshot)
{
    /// <summary>
    /// True when the kill should credit the attacker. The engine reports a
    /// suicide as attacker == victim and a world death as attacker == 0, and
    /// neither should score — so this is worth answering once here rather than
    /// in every subscriber.
    /// </summary>
    public bool IsCredited => AttackerUserId != 0 && AttackerUserId != UserId;
}

[GameEvent("player_death")]
public sealed class PlayerDeathEvent : IGameEvent<PlayerDeathArgs>
{
    public event Action<PlayerDeathArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerDeathArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerDeathArgs(
            reader.GetInt("userid"),
            reader.GetInt("attacker"),
            reader.GetString("weapon"),
            reader.GetBool("headshot")));
    }
}

/// <summary>A player finished spawning in and is playable.</summary>
public readonly record struct PlayerActivateArgs(int UserId);

[GameEvent("player_activate")]
public sealed class PlayerActivateEvent : IGameEvent<PlayerActivateArgs>
{
    public event Action<PlayerActivateArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerActivateArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerActivateArgs(reader.GetInt("userid")));
}

/// <summary>
/// A team change, including the initial assignment: whether the game chose
/// the team, whether it is to go unannounced, and the player's name.
/// </summary>
public readonly record struct PlayerTeamArgs(
    int UserId, int Team, int OldTeam, bool Disconnect, bool AutoTeam = false, bool Silent = false, string Name = "");

[GameEvent("player_team")]
public sealed class PlayerTeamEvent : IGameEvent<PlayerTeamArgs>
{
    public event Action<PlayerTeamArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerTeamArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerTeamArgs(
            reader.GetInt("userid"),
            reader.GetInt("team"),
            reader.GetInt("oldteam"),
            reader.GetBool("disconnect"),
            reader.GetBool("autoteam"),
            reader.GetBool("silent"),
            reader.GetString("name")));
    }
}

/// <summary>Someone connected, as a client hears it (no address).</summary>
public readonly record struct PlayerConnectClientArgs(string Name, int Index, int UserId, string NetworkId, bool IsBot);

[GameEvent("player_connect_client")]
public sealed class PlayerConnectClientEvent : IGameEvent<PlayerConnectClientArgs>
{
    public event Action<PlayerConnectClientArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerConnectClientArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerConnectClientArgs(reader.GetString("name"), reader.GetInt("index"), reader.GetInt("userid"),
                                             reader.GetString("networkid"), reader.GetInt("bot") != 0));
}

/// <summary>A player's name changed.</summary>
public readonly record struct PlayerChangeNameArgs(int UserId, string OldName, string NewName);

[GameEvent("player_changename")]
public sealed class PlayerChangeNameEvent : IGameEvent<PlayerChangeNameArgs>
{
    public event Action<PlayerChangeNameArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerChangeNameArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerChangeNameArgs(reader.GetInt("userid"), reader.GetString("oldname"), reader.GetString("newname")));
}

/// <summary>A server convar marked FCVAR_NOTIFY changed.</summary>
public readonly record struct ServerCvarArgs(string Name, string Value);

[GameEvent("server_cvar")]
public sealed class ServerCvarEvent : IGameEvent<ServerCvarArgs>
{
    public event Action<ServerCvarArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(ServerCvarArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new ServerCvarArgs(reader.GetString("cvarname"), reader.GetString("cvarvalue")));
}

/// <summary>A new map, as the client mode fires it client-side at level init (stock ClientModeShared::LevelInit).</summary>
public readonly record struct GameNewMapArgs(string MapName);

[GameEvent("game_newmap")]
public sealed class GameNewMapEvent : IGameEvent<GameNewMapArgs>
{
    public event Action<GameNewMapArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(GameNewMapArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader) => Raise(new GameNewMapArgs(reader.GetString("mapname")));
}
