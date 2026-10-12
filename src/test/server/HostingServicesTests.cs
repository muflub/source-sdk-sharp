using System.Net;
using System.Net.Sockets;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's hosting services (plan_host_sdk §2.3, §2.8): client actions,
/// the clients' hook driver, the bound-port reader, the console and chat.
/// </summary>
public sealed class HostingServicesTests
{
    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    private sealed class Info : IClientInfo
    {
        public ulong SteamId64(int entIndex) => entIndex == 3 ? 76561197960287930UL : 0;
        public string? Address(int entIndex) => entIndex == 3 ? "203.0.113.7:27005" : null;
    }

    //-------------------------------------------------------------------------
    // EngineClients
    //-------------------------------------------------------------------------

    [Fact]
    public void Kick_runs_kickid_with_the_userid_and_a_reason_that_cannot_start_a_second_command()
    {
        var engine = new RecordingEngine()
            .Returns("Players.GetHandle", new EntityHandle(77))
            .Returns("Players.GetUserId", 12);
        var clients = new EngineClients(new Info(), engine, engine, engine);

        clients.Kick(3, "bye;quit\n\"x\"");

        var call = Assert.Single(engine.Calls, c => c.Member == "Console.ExecuteServerCommand");
        Assert.Equal("kickid 12 bye quit  x ", call.Arg(0));
    }

    [Fact]
    public void Kick_of_an_empty_slot_does_nothing()
    {
        var engine = new RecordingEngine().Returns("Players.GetHandle", EntityHandle.None);
        var clients = new EngineClients(new Info(), engine, engine, engine);

        clients.Kick(9, "bye");

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Console.ExecuteServerCommand");
        Assert.Contains(engine.Calls, c => c.Member == "Players.GetHandle");   // the stimulus reached it
    }

    [Fact]
    public void ClientCommand_sends_to_the_players_handle()
    {
        var engine = new RecordingEngine().Returns("Players.GetHandle", new EntityHandle(77));
        var clients = new EngineClients(new Info(), engine, engine, engine);

        clients.ClientCommand(3, "retry");

        var call = Assert.Single(engine.Calls, c => c.Member == "Console.SendClientCommand");
        Assert.Equal(new EntityHandle(77), call.Arg(0));
        Assert.Equal("retry", call.Arg(1));
    }

    [Fact]
    public void SteamId_and_address_come_from_the_engines_reads()
    {
        var engine = new RecordingEngine();
        var clients = new EngineClients(new Info(), engine, engine, engine);

        Assert.Equal(76561197960287930UL, clients.SteamId64(3));
        Assert.Equal("203.0.113.7:27005", clients.Address(3));
        Assert.Equal(0UL, clients.SteamId64(4));
    }

    [Fact]
    public void Reconnect_sends_the_core_message_to_an_occupied_slot_only()
    {
        var engine = new RecordingEngine().Returns("Players.GetHandle", new EntityHandle(77));
        new EngineClients(new Info(), engine, engine, engine).Reconnect(3);
        var empty = new RecordingEngine().Returns("Players.GetHandle", EntityHandle.None);
        new EngineClients(new Info(), empty, empty, empty).Reconnect(9);

        var sent = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("SourceSharp:Reconnect", sent.Arg(0));
        Assert.Equal(0, sent.Arg(1));
        Assert.Equal(3, sent.Arg(2));
        Assert.DoesNotContain(empty.Calls, c => c.Member == "UserMessages.Send");
    }

    private sealed class CountingReconnect : IClientReconnect
    {
        public int Count { get; private set; }
        public void Reconnect() => Count++;
    }

    [Fact]
    public void The_reconnect_message_reconnects_the_client()
    {
        var reconnect = new CountingReconnect();

        ((IUserMessage)new SourceSharp.Shared.ReconnectMessage(reconnect)).OnReceived(new UserMessageReader([]));

        Assert.Equal(1, reconnect.Count);
    }

    //-------------------------------------------------------------------------
    // ClientLifecycleDriver
    //-------------------------------------------------------------------------

    public sealed class Watcher : IClientLifecycle
    {
        public List<string> Seen { get; } = new();
        public int ClientLifecycleOrder => ModStages.First + 1;
        public string? OnClientConnecting(string name, string address) { Seen.Add("connecting " + name); return name == "banned" ? "no" : null; }
        public void OnClientActive(int entIndex) => Seen.Add("active " + entIndex);
        public void OnClientDisconnected(int entIndex) => Seen.Add("left " + entIndex);
    }

    public sealed class Thrower : IClientLifecycle
    {
        public int ClientLifecycleOrder => ModStages.First;
        public string? OnClientConnecting(string name, string address) => throw new InvalidOperationException("hook broke");
    }

    public sealed class MessageClaimer : IClientLifecycle
    {
        public List<string> Seen { get; } = new();
        public int ClientLifecycleOrder => ModStages.First + 3;
        public bool OnClientMessage(int entIndex, string key, ReadOnlySpan<byte> payload)
        {
            if (key == "boom")
                throw new InvalidOperationException("message broke");
            Seen.Add($"{entIndex} {key} {Convert.ToHexString(payload)}");
            return key == "mine";
        }
    }

    [Fact]
    public void A_client_net_message_is_claimed_by_the_participant_whose_key_it_is()
    {
        var c = new ServiceContainer();
        c.Add<MessageClaimer>();
        var log = new ListLog();
        var driver = new ClientLifecycleDriver(c, log);

        Assert.True(driver.Message(3, "mine", [0xAB, 0x01]));
        Assert.False(driver.Message(3, "other", []));
        Assert.False(driver.Message(4, "boom", [1]));

        Assert.Equal(["3 mine AB01", "3 other "], c.Require<MessageClaimer>().Seen);
        Assert.Contains(log.Lines, l => l.Contains("message broke"));
        Assert.Contains(log.Lines, l => l.Contains("'other'") && l.Contains("not claimed"));
    }

    private static (ClientLifecycleDriver Driver, Watcher Watcher, ListLog Log) Driver()
    {
        var c = new ServiceContainer();
        c.Add<Watcher>();
        c.Add<Thrower>();
        var log = new ListLog();
        return (new ClientLifecycleDriver(c, log), c.Require<Watcher>(), log);
    }

    [Fact]
    public void Connecting_returns_the_first_refusal_and_a_throwing_hook_does_not_refuse()
    {
        var (driver, watcher, log) = Driver();

        Assert.Null(driver.Connecting("ok", "1.2.3.4:5"));
        Assert.Equal("no", driver.Connecting("banned", "1.2.3.4:5"));
        Assert.Equal(["connecting ok", "connecting banned"], watcher.Seen);
        Assert.Contains(log.Lines, l => l.Contains("hook broke"));
    }

    [Fact]
    public void A_player_removal_is_a_disconnect_except_while_a_level_shuts_down()
    {
        var (driver, watcher, _) = Driver();

        driver.PlayerRemoved(3);
        driver.LevelShuttingDown = true;
        driver.PlayerRemoved(4);
        driver.LevelShuttingDown = false;
        driver.Active(5);

        Assert.Equal(["left 3", "active 5"], watcher.Seen);
    }

    //-------------------------------------------------------------------------
    // ProcBoundPort
    //-------------------------------------------------------------------------

    private const string UdpTable =
        "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode ref pointer drops\n" +
        "  101: 00000000:6987 00000000:0000 07 00000000:00000000 00:00000000 00000000  1000        0 5551 2 0000000000000000 0\n" +
        "  102: 00000000:6988 00000000:0000 07 00000000:00000000 00:00000000 00000000  1000        0 5552 2 0000000000000000 0\n" +
        "  103: 0100007F:1F90 00000000:0000 07 00000000:00000000 00:00000000 00000000  1000        0 9999 2 0000000000000000 0\n";

    [Fact]
    public void The_udp_table_yields_the_ports_of_this_process_sockets_only()
    {
        var ports = ProcBoundPort.UdpPortsOf(UdpTable, new HashSet<ulong> { 5551, 5552 }).ToArray();

        Assert.Equal([27015, 27016], ports);
    }

    [Fact]
    public void Choose_sets_aside_the_engines_other_ports_then_takes_the_only_one_left()
    {
        // The engine walked past a taken 27015 to 27016; HLTV is on 27020.
        Assert.Equal(27016, ProcBoundPort.Choose([27016, 27020], requested: 27015, others: [27020, null]));
    }

    [Fact]
    public void Choose_prefers_the_requested_port_among_several_and_refuses_to_guess_otherwise()
    {
        Assert.Equal(27015, ProcBoundPort.Choose([27015, 26900], requested: 27015, others: []));
        Assert.Null(ProcBoundPort.Choose([27016, 26900], requested: 27015, others: []));
        Assert.Null(ProcBoundPort.Choose([], requested: 27015, others: []));
    }

    [Fact]
    public void The_proc_reader_finds_a_real_udp_socket_this_process_bound()
    {
        // Against the kernel, not a fixture: bind a socket and look for it.
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;

        var inodes = ProcBoundPort.SocketInodes("/proc/self/fd");
        var ports = ProcBoundPort.UdpPortsOf(File.ReadAllText("/proc/self/net/udp"), inodes).ToArray();

        Assert.NotEmpty(inodes);
        Assert.Contains(port, ports);
    }

    //-------------------------------------------------------------------------
    // ServerConsole, Chat
    //-------------------------------------------------------------------------

    private sealed class SpeakingConsole(RecordingEngine inner, EngineSide side) : IEngineConsole
    {
        private IEngineConsole Inner => inner;
        public void ExecuteServerCommand(string command)
        {
            Inner.ExecuteServerCommand(command);
            side.Msg($"ran {command}\n");   // the real funnel every console line takes: this side's
        }
        public string? FindConVarValue(string name) => Inner.FindConVarValue(name);
        public string GetConVarValue(int id) => Inner.GetConVarValue(id);
        public void Msg(string message) => Inner.Msg(message);
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => Inner.RegisterConCommand(name, help, flags);
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => Inner.RegisterConVar(name, defaultValue, help, flags, min, max);
        public void SendClientCommand(EntityHandle player, string command) => Inner.SendClientCommand(player, command);
        public void SetConVarValue(int id, string value) => Inner.SetConVarValue(id, value);
        public void SetConVarValueByName(string name, string value) => Inner.SetConVarValueByName(name, value);
        public int Snapshot() => Inner.Snapshot();
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) => Inner.SnapshotEntry(index, out entry);
        public void Warning(string message) => Inner.Warning(message);
    }

    [Fact]
    public void Execute_returns_what_the_command_printed()
    {
        var side = new EngineSide(Side.Server);
        var console = new ServerConsole(new SpeakingConsole(new RecordingEngine(), side), side.Lines);

        Assert.Equal("ran status\n", console.Execute("status"));
    }

    [Fact]
    public void A_capture_takes_every_line_numbered_inside_it_and_nests()
    {
        string inner = "", outer;
        var side = new EngineSide(Side.Server);
        outer = side.Lines.Capture(() =>
        {
            var other = new Thread(() => side.Msg("from another thread\n"));
            other.Start();
            other.Join();
            side.Msg("mine\n");
            inner = side.Lines.Capture(() => side.Warning("nested\n"));
        });

        // Scoped by number, not thread (console-batch ruling, 2026-10-11):
        // the other thread printed inside the outer range, so it is captured.
        Assert.Equal("nested\n", inner);
        Assert.Equal("from another thread\nmine\nnested\n", outer);
    }

    [Fact]
    public void Chat_sends_SayText_from_the_server_to_one_player_or_all()
    {
        var engine = new RecordingEngine();
        var chat = new Chat(engine);

        chat.PrintAll("server closing");
        chat.Print(3, "hi");

        var sends = engine.Calls.Where(c => c.Member == "UserMessages.Send").ToArray();
        Assert.Equal(2, sends.Length);
        Assert.Equal("SayText", sends[0].Arg(0));
        Assert.Equal(1 + "server closing\n".Length + 1 + 1, sends[0].Arg(1));   // speaker, text\0, chat flag
        Assert.Equal(0, sends[0].Arg(2));
        Assert.Equal(3, sends[1].Arg(2));
    }
}
