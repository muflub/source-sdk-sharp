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
/// </summary>
public sealed class ClientLifecycleDriver : IPlayerRemovals
{
    private readonly IReadOnlyList<IClientLifecycle> _participants;
    private readonly ILog _log;

    public ClientLifecycleDriver(ServiceContainer services, ILog log)
    {
        var found = services.ResolveAll<IClientLifecycle>();
        StageUniqueness.Require(found, p => p.ClientLifecycleOrder, "ClientLifecycleOrder", nameof(LifecycleStage));
        _participants = found.OrderBy(p => p.ClientLifecycleOrder).ToArray();
        _log = log;
    }

    public IReadOnlyList<IClientLifecycle> Participants => _participants;

    /// <summary>
    /// Set while a level shuts down: every player entity is removed then, and
    /// the clients stay connected, so a removal is not a disconnect.
    /// </summary>
    public bool LevelShuttingDown { get; set; }

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

    public void Active(int entIndex) => Each("OnClientActive", p => p.OnClientActive(entIndex));

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

    /// <summary>A player entity went away; reported only when it was a client leaving.</summary>
    public void PlayerRemoved(int entIndex)
    {
        if (LevelShuttingDown || entIndex <= 0)
            return;

        Each("OnClientDisconnected", p => p.OnClientDisconnected(entIndex));
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
public sealed class ProcBoundPort(IEngineConsole console) : IBoundPort
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

        return Choose(ports, Cvar("hostport"), [Cvar("tv_port"), Cvar("clientport")]);
    }

    private int? Cvar(string name) =>
        int.TryParse(console.FindConVarValue(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

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
