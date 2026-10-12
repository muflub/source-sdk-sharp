using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_steam_status</c>: whether this server load can talk to its Steam
/// game server right now. The readout for the P1 run 3 reload ruling: the
/// Steam game server is fetched when Steam activates the server, once per
/// server start, so a <c>cs_sv_reload</c> must leave it answering without a
/// new activation.
/// </summary>
[ConCommand("cs_sv_steam_status",
            Help = "Reports whether this server load reaches its Steam game server (loggedon=yes|no).")]
public sealed class SteamStatusCommand : IConCommand
{
    private readonly IEngineHost _host;
    private readonly IEngineConsole _console;
    private readonly ISteamGameServer _steam;

    /// <summary>Over the load's engine host and its Steam instance, which also says whether it holds a game server and why not.</summary>
    public SteamStatusCommand(IEngineHost host, IEngineConsole console, ISteamGameServer steam)
    {
        _host = host;
        _console = console;
        _steam = steam;
    }

    public void Execute(string args)
        => _console.Msg($"[SourceSharp/Server] steam status: loggedon={(_host.SteamGameServerLoggedOn() ? "yes" : "no")}"
                        + $", gameserver={(_steam.HasGameServer ? "yes" : "no")}"
                        + (_steam.WhyUnbound.Length == 0 ? string.Empty : $", unbound: {_steam.WhyUnbound}")
                        + "\n");
}
