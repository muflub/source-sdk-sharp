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
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;

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
        var driver = new ClientLifecycleDriver(c, log, new Slots());

        Assert.True(driver.Message(3, "mine", [0xAB, 0x01]));
        Assert.False(driver.Message(3, "other", []));
        Assert.False(driver.Message(4, "boom", [1]));

        Assert.Equal(["3 mine AB01", "3 other "], c.Require<MessageClaimer>().Seen);
        Assert.Contains(log.Lines, l => l.Contains("message broke"));
        Assert.Contains(log.Lines, l => l.Contains("'other'") && l.Contains("not claimed"));
    }

    /// <summary>The engine's user id in each slot (-1 for none), and which slots are bots (no address).</summary>
    private sealed class Slots : IConnectedClients
    {
        public Dictionary<int, int> Users { get; } = new();
        public HashSet<int> Bots { get; } = new();

        public int UserId(int entIndex) => Users.TryGetValue(entIndex, out int user) ? user : -1;
        public string? Address(int entIndex) => Users.ContainsKey(entIndex) && !Bots.Contains(entIndex) ? "203.0.113.7:27005" : null;
        public ulong SteamId64(int entIndex) => 0;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private static (ClientLifecycleDriver Driver, Watcher Watcher, ListLog Log, Slots Slots) Driver()
    {
        var c = new ServiceContainer();
        c.Add<Watcher>();
        c.Add<Thrower>();
        var log = new ListLog();
        var slots = new Slots();
        slots.Users[1] = 2;
        slots.Users[3] = 12;
        return (new ClientLifecycleDriver(c, log, slots), c.Require<Watcher>(), log, slots);
    }

    [Fact]
    public void Connecting_returns_the_first_refusal_and_a_throwing_hook_does_not_refuse()
    {
        var (driver, watcher, log, _) = Driver();

        Assert.Null(driver.Connecting("ok", "1.2.3.4:5"));
        Assert.Equal("no", driver.Connecting("banned", "1.2.3.4:5"));
        Assert.Equal(["connecting ok", "connecting banned"], watcher.Seen);
        Assert.Contains(log.Lines, l => l.Contains("hook broke"));
    }

    [Fact]
    public void A_level_change_takes_the_client_out_of_the_game_without_a_departure()
    {
        var (driver, watcher, _, _) = Driver();
        driver.PutInServer(1);
        driver.Active(1);

        // The engine's changelevel, in its order (host.cpp Host_Changelevel):
        // InactivateClients takes each client out of the game, ClientDisconnect
        // removing its player entity, and only then does the level shut down.
        // The client comes back on the next level with the same connection.
        driver.LevelShutdown();
        driver.PutInServer(1);
        driver.Active(1);

        Assert.Equal(["active 1", "active 1"], watcher.Seen);
    }

    [Fact]
    public void The_engines_disconnect_reports_the_slot_its_user_id_is_in()
    {
        var (driver, watcher, _, _) = Driver();
        driver.Active(1);
        driver.Active(3);

        driver.Disconnected(12);

        Assert.Equal(["active 1", "active 3", "left 3"], watcher.Seen);
    }

    [Fact]
    public void A_departure_is_reported_once()
    {
        var (driver, watcher, _, _) = Driver();
        driver.Active(3);

        driver.Disconnected(12);
        driver.Disconnected(12);

        Assert.Equal(["active 3", "left 3"], watcher.Seen);
    }

    [Fact]
    public void A_client_that_leaves_while_the_next_level_loads_is_reported()
    {
        var (driver, watcher, _, slots) = Driver();
        driver.Active(3);
        driver.LevelShutdown();
        slots.Users.Remove(3);      // no player in the slot while the client loads

        driver.Disconnected(12);

        Assert.Equal(["active 3", "left 3"], watcher.Seen);
    }

    [Fact]
    public void A_client_put_in_the_server_and_never_active_is_reported_when_it_leaves()
    {
        var (driver, watcher, _, _) = Driver();
        driver.PutInServer(3);

        driver.Disconnected(12);

        Assert.Equal(["left 3"], watcher.Seen);
    }

    [Fact]
    public void A_user_id_no_slot_holds_reports_nothing()
    {
        var (driver, watcher, _, _) = Driver();
        driver.Active(3);

        driver.Disconnected(99);

        Assert.Equal(["active 3"], watcher.Seen);
    }

    [Fact]
    public void A_new_user_id_in_a_slot_reports_the_client_before_it_first()
    {
        var (driver, watcher, log, slots) = Driver();
        driver.Active(3);

        // The engine's same-address reconnect: ClientDisconnect, no event, a new connection.
        slots.Users[3] = 15;
        driver.PutInServer(3);
        driver.Active(3);

        Assert.Equal(["active 3", "left 3", "active 3"], watcher.Seen);
        Assert.Contains(log.Lines, l => l.Contains("client 3 (user 12) left with no disconnect: user 15 has the slot"));
    }

    [Fact]
    public void A_bot_the_level_change_drops_is_forgotten_unreported()
    {
        var (driver, watcher, _, slots) = Driver();
        slots.Users[5] = 20;
        slots.Bots.Add(5);
        driver.Active(5);
        driver.Active(3);

        driver.LevelShutdown();
        slots.Users[5] = 21;        // the next level's bot, in the same slot
        driver.PutInServer(5);
        driver.Disconnected(12);    // the human stayed known

        Assert.Equal(["active 5", "active 3", "left 3"], watcher.Seen);
    }

    [Fact]
    public void After_a_hot_reload_the_clients_in_the_slots_are_heard_leaving()
    {
        var (driver, watcher, _, _) = Driver();

        driver.Carried(8);
        driver.Disconnected(2);

        Assert.Equal(["left 1"], watcher.Seen);
    }

    [Fact]
    public void The_driver_hears_the_engines_player_disconnect_event()
    {
        var (driver, watcher, _, _) = Driver();
        var disconnects = new SourceSharp.Shared.PlayerDisconnectEvent();
        driver.Listen(disconnects);
        driver.Active(3);

        disconnects.Raise(new SourceSharp.Shared.PlayerDisconnectArgs("someone", 12, "Kicked by Console"));

        Assert.Equal(["active 3", "left 3"], watcher.Seen);
    }

    [Fact]
    public void A_reported_departure_is_said_with_how_many_participants_heard_it()
    {
        var (driver, _, log, _) = Driver();
        driver.Active(3);

        driver.Disconnected(12);

        Assert.Single(log.Lines, l => l == "[SourceSharp/Clients] client 3 left: 2 participant(s) told\n");
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

    [LinuxOnlyFact("reads /proc, which only Linux has")]
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
        public IReadOnlyList<string> CompleteCommandLine(string line) => [];
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
        var side = TestSides.Create(Side.Server);
        var console = new ServerConsole(new SpeakingConsole(new RecordingEngine(), side), side.Lines);

        Assert.Equal("ran status\n", console.Execute("status"));
    }

    [Fact]
    public void A_capture_takes_every_line_numbered_inside_it_and_nests()
    {
        string inner = "", outer;
        var side = TestSides.Create(Side.Server);
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
