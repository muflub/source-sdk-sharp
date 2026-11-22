using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//=============================================================================
// The server's implementations of the hosting contracts in
// SourceSharp.Abi/ServerHostingContracts.cs (plan_host_sdk §2.8).
//=============================================================================

/// <summary>The connected clients and the actions on one, over the engine's own reads.</summary>
public sealed class EngineClients(IClientInfo info, IEnginePlayers players, IEngineConsole console, IUserMessageSender messages) : IConnectedClients
{
    public ulong SteamId64(int entIndex) => info.SteamId64(entIndex);

    public string? Address(int entIndex) => info.Address(entIndex);

    public string? ClientConVar(int entIndex, string name) => info.ClientConVar(entIndex, name);

    // Null, as IClientInfo's default body answered before plan_shim_areas_split
    // phase 4 moved the interface to an interface assembly (no bodies): this
    // class never forwarded the latency, and that phase changes no behaviour.
    // PlayerResource reads the latency through the side's IClientInfo itself.
    public float? AvgLatency(int entIndex) => null;

    public int UserId(int entIndex)
    {
        var handle = players.GetHandle(entIndex);
        return handle.Equals(EntityHandle.None) ? -1 : players.GetUserId(handle);
    }

    public void Kick(int entIndex, string reason)
    {
        int userId = UserId(entIndex);
        if (userId < 0)
            return;

        console.ExecuteServerCommand($"kickid {userId} {ConsoleText(reason)}");
    }

    public void ClientCommand(int entIndex, string command)
    {
        var handle = players.GetHandle(entIndex);
        if (!handle.Equals(EntityHandle.None))
            console.SendClientCommand(handle, command);
    }

    public void Reconnect(int entIndex)
    {
        if (players.GetHandle(entIndex).Equals(EntityHandle.None))
            return;

        messages.Send(new Reconnect(), entIndex);
    }

    /// <summary>
    /// Text safe inside one console command: no newline or ';' to start a
    /// second command, no quote to end the argument early.
    /// </summary>
    public static string ConsoleText(string text) =>
        new(text.Select(c => c is '\n' or '\r' or ';' or '"' ? ' ' : c).ToArray());
}

/// <summary>
/// Calls every registered <see cref="IClientLifecycle"/>, as LifecycleDriver
/// does for <see cref="IGameLifecycle"/>: in a unique order, each call isolated.
///
/// <para>
/// A DEPARTURE IS THE ENGINE'S <c>player_disconnect</c>, NOT THE PLAYER'S
/// REMOVAL. A level change takes every client out of the game first
/// (Host_Changelevel: InactivateClients, each through ClientDisconnect, which
/// removes the player entity) and shuts the level down after, so the removal
/// cannot tell a level change from a client leaving; the event can: the
/// engine fires it in CGameClient::Disconnect only, before ClientDisconnect,
/// while the player still stands. Stock's own listeners for a real departure
/// hear that event (tf_gamestats, tf_gc_server); its ClientDisconnect skips
/// the rules on g_fGameOver, a flag only the rules' own map end sets.
/// </para>
///
/// <para>
/// The event names a user id, so the driver keeps who is in each slot: the
/// engine's user id when the client is put in the server and again when it is
/// active. A level change keeps the connection and its id, so a client that
/// leaves while loading the next level is still found by it. A slot whose
/// client went with no event (the engine's same-address reconnect) shows as a
/// new id in it: the old occupant's departure is reported then, first. The
/// engine drops its fake clients with a level silently (InactivateClients
/// clears them): a bot is forgotten at the level's shutdown, unreported, as
/// the level change it is.
/// </para>
/// </summary>
public sealed class ClientLifecycleDriver
{
    private readonly IReadOnlyList<IClientLifecycle> _participants;
    private readonly ILog _log;
    private readonly IConnectedClients _clients;

    /// <summary>The client in a slot: the engine's user id for its connection, and whether it is a bot.</summary>
    private readonly record struct Occupant(int UserId, bool Bot);

    private readonly Dictionary<int, Occupant> _occupants = new();

    public ClientLifecycleDriver(ServiceContainer services, ILog log, IConnectedClients clients)
    {
        var found = services.ResolveAll<IClientLifecycle>();
        StageUniqueness.Require(found, p => p.ClientLifecycleOrder, "ClientLifecycleOrder", nameof(LifecycleStage));
        _participants = found.OrderBy(p => p.ClientLifecycleOrder).ToArray();
        _log = log;
        _clients = clients;
    }

    public IReadOnlyList<IClientLifecycle> Participants => _participants;

    /// <summary>Hears the engine's <c>player_disconnect</c>: the one sign that a client left.</summary>
    public void Listen(IGameEvent<PlayerDisconnectArgs> disconnects)
        => disconnects.Fired += e => Disconnected(e.UserId);

    /// <summary>The client in <paramref name="entIndex"/> was put in the server: it occupies the slot.</summary>
    public void PutInServer(int entIndex) => Occupy(entIndex);

    /// <summary>
    /// A hot reload put this driver under a running level: the clients in the
    /// slots up to <paramref name="maxClients"/> are the ones it knows.
    /// </summary>
    public void Carried(int maxClients)
    {
        for (int i = 1; i <= maxClients; i++)
            Occupy(i);
    }

    /// <summary>
    /// The level shuts down. The clients stay connected through it, and are not
    /// told anything; the engine's bots do not stay (InactivateClients clears
    /// them without a disconnect), so they are forgotten here.
    /// </summary>
    public void LevelShutdown()
    {
        foreach (var slot in _occupants.Where(o => o.Value.Bot).Select(o => o.Key).ToArray())
            _occupants.Remove(slot);
    }

    /// <summary>
    /// The engine's <c>player_disconnect</c> for <paramref name="userId"/>: the
    /// slot it was in is reported as a departure. An id the driver never saw in
    /// a slot was no client in the game, and nothing is told.
    /// </summary>
    public void Disconnected(int userId)
    {
        foreach (var (slot, occupant) in _occupants)
        {
            if (occupant.UserId != userId)
                continue;

            _occupants.Remove(slot);
            Departed(slot);
            return;
        }
    }

    /// <summary>
    /// Records who is in <paramref name="entIndex"/>; a different id there than
    /// before means the client before left with no event, and is reported first.
    /// </summary>
    private void Occupy(int entIndex)
    {
        int userId = _clients.UserId(entIndex);
        if (userId < 0)
            return;

        if (_occupants.TryGetValue(entIndex, out var was) && was.UserId != userId)
        {
            _log.Msg($"[SourceSharp/Clients] client {entIndex} (user {was.UserId}) left with no disconnect: user {userId} has the slot\n");
            Departed(entIndex);
        }

        _occupants[entIndex] = new Occupant(userId, _clients.Address(entIndex) is null);
    }

    private void Departed(int entIndex)
    {
        _log.Msg($"[SourceSharp/Clients] client {entIndex} left: {_participants.Count} participant(s) told\n");
        Each("OnClientDisconnected", p => p.OnClientDisconnected(entIndex));
    }

    /// <summary>The first participant's refusal, or null to admit. A throwing participant does not refuse.</summary>
    public string? Connecting(string name, string address)
    {
        foreach (var p in _participants)
        {
            try
            {
                if (p.OnClientConnecting(name, address) is { } reason)
                    return reason;
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp] {p.GetType().Name}.OnClientConnecting threw: {e}\n");
            }
        }

        return null;
    }

    public void Active(int entIndex)
    {
        Occupy(entIndex);
        Each("OnClientActive", p => p.OnClientActive(entIndex));
    }

    /// <summary>The client's userinfo changed; every participant reads its own convars.</summary>
    public void SettingsChanged(int entIndex) => Each("OnClientSettingsChanged", p => p.OnClientSettingsChanged(entIndex));

    /// <summary>Whether a participant claimed the client's net message; a throwing participant does not claim it.</summary>
    public bool Message(int entIndex, string key, ReadOnlySpan<byte> payload)
    {
        foreach (var p in _participants)
        {
            try
            {
                if (p.OnClientMessage(entIndex, key, payload))
                    return true;
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp] {p.GetType().Name}.OnClientMessage threw: {e}\n");
            }
        }

        _log.Warning($"[SourceSharp] client {entIndex}'s message '{key}' ({payload.Length} bytes) was not claimed\n");
        return false;
    }

    private void Each(string what, Action<IClientLifecycle> call)
    {
        foreach (var p in _participants)
        {
            try
            {
                call(p);
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp] {p.GetType().Name}.{what} threw: {e}\n");
            }
        }
    }
}

/// <summary>
/// The server socket's real port, from /proc: this process' socket inodes
/// (/proc/self/fd), matched against the kernel's UDP tables
/// (/proc/self/net/udp, udp6).
/// </summary>
public sealed class ProcBoundPort(HostPortConVar hostPort, TvPortConVar tvPort, ClientPortConVar clientPort) : IBoundPort
{
    public int? Read()
    {
        var inodes = SocketInodes("/proc/self/fd");
        var ports = new List<int>();

        foreach (var table in new[] { "/proc/self/net/udp", "/proc/self/net/udp6" })
        {
            if (File.Exists(table))
                ports.AddRange(UdpPortsOf(File.ReadAllText(table), inodes));
        }

        return Choose(ports, Port(hostPort), [Port(tvPort), Port(clientPort)]);
    }

    private static int? Port(IntConVar convar) => convar.HasValue ? convar.Value : null;

    /// <summary>The inodes of the sockets in <paramref name="fdDir"/>.</summary>
    public static HashSet<ulong> SocketInodes(string fdDir)
    {
        var inodes = new HashSet<ulong>();

        foreach (var fd in Directory.EnumerateFileSystemEntries(fdDir))
        {
            string? target;
            try
            {
                target = new FileInfo(fd).LinkTarget;
            }
            catch (IOException)
            {
                continue;   // closed between the listing and the read
            }

            // "socket:[123456]"
            if (target is not null && target.StartsWith("socket:[", StringComparison.Ordinal)
                && ulong.TryParse(target.AsSpan(8, target.Length - 9), out ulong inode))
                inodes.Add(inode);
        }

        return inodes;
    }

    /// <summary>
    /// The local ports of the rows of a /proc/net/udp table whose inode is in
    /// <paramref name="inodes"/>. Columns: sl local_address rem_address st
    /// tx:rx tr:tm retrnsmt uid timeout inode ...; local_address is HEX_IP:HEX_PORT.
    /// </summary>
    public static IEnumerable<int> UdpPortsOf(string table, IReadOnlySet<ulong> inodes)
    {
        foreach (var line in table.Split('\n').Skip(1))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 10 || !ulong.TryParse(cols[9], out ulong inode) || !inodes.Contains(inode))
                continue;

            int colon = cols[1].LastIndexOf(':');
            if (colon > 0 && int.TryParse(cols[1].AsSpan(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int port))
                yield return port;
        }
    }

    /// <summary>
    /// The game port among <paramref name="bound"/>: the ports of the engine's
    /// other sockets are set aside; then one port left is the answer, and among
    /// several the one the server asked for wins. Otherwise null: refusing to
    /// guess is the point, since a guess is how a wrong port reaches a host.
    /// </summary>
    public static int? Choose(IReadOnlyCollection<int> bound, int? requested, IReadOnlyCollection<int?> others)
    {
        var candidates = bound.Distinct().Where(p => !others.Contains(p)).ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        if (requested is { } asked && candidates.Contains(asked))
            return asked;

        return null;
    }
}

/// <summary>The console as a hosting service drives it.</summary>
public sealed class ServerConsole(IEngineConsole console, ConsoleLines lines) : IServerConsole
{
    public string Execute(string command) =>
        lines.Capture(() => console.ExecuteServerCommand(command));

    public void Quit(int exitCode)
    {
        // The engine's own quit runs its shutdown (Steam, the game DLL, the
        // managed unload); the exit status is the process', set first.
        Environment.ExitCode = exitCode;
        console.ExecuteServerCommand("quit");
    }
}

/// <summary>Chat lines through the stock SayText message (client 0: the server speaking).</summary>
public sealed class Chat(IUserMessageSender messages) : IChat
{
    public void PrintAll(string text) => Send(text, player: 0);

    public void Print(int entIndex, string text) => Send(text, entIndex);

    private void Send(string text, int player)
    {
        var line = text.EndsWith('\n') ? text : text + "\n";
        // The speaker is the server (0), and it is not a player's chat line.
        messages.Send(new SayText { Client = 0, Text = line, Chat = false }, player);
    }
}
