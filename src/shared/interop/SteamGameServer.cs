namespace SourceSharp.Abi;

/// <summary>
/// The Steam game server, as one server load holds it: the server
/// composition's <c>SteamGameServerFlat</c> (Server.Interop), handed to the
/// engine host binding by constructor.
///
/// <para>
/// AN INSTANCE PER LOAD, NOT A STATIC (P1 run 3, the user's ruling). Everything
/// it holds is a native value that can be derived again: the library handle
/// (dlopen is refcounted and answers the same handle), its exports (dlsym
/// answers the same addresses) and the game server pointer (the accessor
/// answers the live one once the server is activated). So a reloaded module's
/// instance binds on its first use and needs no new activation.
/// </para>
/// </summary>
public interface ISteamGameServer
{
    /// <summary>Clear, then fetch the game server interface afresh. True when there is a game server.</summary>
    bool Reacquire();

    /// <summary>Drop the interface; the next activation fetches it again.</summary>
    void Clear();

    /// <summary>ISteamGameServer::GetGameplayStats, when there is a game server.</summary>
    void RequestGameplayStats();

    /// <summary>ISteamGameServer::BLoggedOn; false when there is no game server.</summary>
    bool LoggedOn();

    /// <summary>Whether there is a game server to talk to right now (binding on first use).</summary>
    bool HasGameServer { get; }

    /// <summary>Why the library or an export could not be bound; empty once bound.</summary>
    string WhyUnbound { get; }
}

/// <summary>
/// A side with no Steam game server: the client, and every container whose
/// composition registers none. Each answer is what an absent game server
/// answers.
/// </summary>
public sealed class NoSteamGameServer : ISteamGameServer
{
    public bool Reacquire() => false;
    public void Clear() { }
    public void RequestGameplayStats() { }
    public bool LoggedOn() => false;
    public bool HasGameServer => false;
    public string WhyUnbound => "this side has no Steam game server";
}
