using TF2.Descent.Server;
using TF2.Descent.Shared;
using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The game-side flows of spec §4-6 against a recording host link: HostService
/// (boot, map ready, failure), PlayerService (join, leave, lease loss) and
/// CommandService (the service's commands).
/// </summary>
public sealed partial class ServiceFlowTests
{
    //-------------------------------------------------------------------------
    // Fakes. The link's tasks are already complete, so every flow runs to its
    // end inside the call that starts it.
    //-------------------------------------------------------------------------

    private sealed class FakeLink : IHostLink
    {
        public List<string> Calls { get; } = new();
        public HostingMode ModeIs { get; set; } = HostingMode.Hosted;
        public Queue<HostResult<BootInfo>> BootAnswers { get; } = new();
        public HostResult<int> MapReadyAnswer { get; set; } = HostResult<int>.Success(1);
        public JoinOutcome JoinAnswer { get; set; } = new(true, null, "c-1", "soldier", "hero", 0);
        public int PlayersReported { get; private set; } = -1;

        public HostingMode Mode() => ModeIs == HostingMode.Hosted ? HostingMode.Hosted
            : ModeIs == HostingMode.Unhosted ? HostingMode.Unhosted
            : throw new InvalidOperationException("no hosting service is configured");

        public void Start() => Calls.Add("start");
        public Task<HostResult<BootInfo>> Booting(string map) { Calls.Add("booting " + map); return Task.FromResult(BootAnswers.Dequeue()); }
        public Task<HostResult<int>> MapReady(int port, string map) { Calls.Add($"mapready {port} {map}"); return Task.FromResult(MapReadyAnswer); }
        public void Players(int count) => PlayersReported = count;
        /// <summary>Who the host knows behind a peer (the gateway's validated SteamID); a peer not here falls back to the engine's id, as the SDK does.</summary>
        public Dictionary<string, string> HostIds { get; } = new();

        public Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className)
        {
            Calls.Add($"join {peer} {engineSteamId} {boot.InstanceKind} {className}");
            if (!JoinAnswer.Joined)
                return Task.FromResult(JoinAnswer);

            string id = HostIds.GetValueOrDefault(peer, engineSteamId);
            return Task.FromResult(HostLink.HostSteamId(new PlayerJoin(true, "", "s", "") { SteamId = id }) is null
                ? new JoinOutcome(false, HostLink.NoSteamId)
                : JoinAnswer with { SteamId = id, ClientAddr = "198.51.100.9:27005" });
        }
        public Task<bool> Checkpoint(string steamId) { Calls.Add("checkpoint " + steamId); return Task.FromResult(true); }
        public Task<bool> Leave(string steamId, string peer) { Calls.Add($"leave {steamId} {peer}"); return Task.FromResult(true); }
        public void ReplyExec(string commandId, string output) => Calls.Add($"reply {commandId} {output}");
        public TravelAnswer TravelAnswer { get; set; } = new(TravelState.Started, TimeSpan.Zero, "");
        public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot)
        {
            Calls.Add($"travel {steamId} {direction} {boot.InstanceKind}");
            return Task.FromResult(TravelAnswer);
        }
        public TicketAnswer TicketAnswer { get; set; } = new(TicketOutcome.Ok);
        public List<(string SteamId, string Nonce, byte[] Ticket)> Tickets { get; } = new();
        public Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket)
        {
            Tickets.Add((steamId, nonce, ticket));
            return Task.FromResult(TicketAnswer);
        }
        // The death lane (M1): this fake records, like the others.
        public List<(string SteamId, SourceSharp.Abi.Vector Position)> Recorded { get; } = new();
        public Task<bool> RecordDeath(string steamId, SourceSharp.Abi.Vector position)
        {
            Calls.Add($"record {steamId}");
            Recorded.Add((steamId, position));
            return Task.FromResult(true);
        }
        public LootAnswer LootAnswer { get; set; } = new(true, 0, "");
        public Task<LootAnswer> LootCorpse(string steamId) { Calls.Add($"loot {steamId}"); return Task.FromResult(LootAnswer); }
        public void Dispose() => Calls.Add("dispose");
    }

    private sealed class FakeClients : IConnectedClients
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;

        public List<string> Calls { get; } = new();
        public Dictionary<int, (ulong Steam, string? Address)> Slots { get; } = new();
        public ulong SteamId64(int entIndex) => Slots.TryGetValue(entIndex, out var s) ? s.Steam : 0;
        public string? Address(int entIndex) => Slots.TryGetValue(entIndex, out var s) ? s.Address : null;
        public int UserId(int entIndex) => entIndex + 100;
        public void Kick(int entIndex, string reason) => Calls.Add($"kick {entIndex} {reason}");
        public void ClientCommand(int entIndex, string command) => Calls.Add($"command {entIndex} {command}");
        public void Reconnect(int entIndex) => Calls.Add($"reconnect {entIndex}");
    }

    private sealed class Port(int? port) : IBoundPort { public int? Read() => port; }

    private sealed class NowScheduler : IFrameScheduler
    {
        public int Delays { get; private set; }
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) { Delays++; return Task.CompletedTask; }
    }

    private sealed class NeverScheduler : IFrameScheduler
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) => new TaskCompletionSource().Task;
    }

    private sealed class FakeConsole : IServerConsole
    {
        public List<string> Calls { get; } = new();
        public string Execute(string command) { Calls.Add("exec " + command); return "output of " + command; }
        public void Quit(int exitCode) => Calls.Add("quit " + exitCode);
    }

    private sealed class FakeChat : IChat
    {
        public List<string> Lines { get; } = new();
        public void PrintAll(string text) => Lines.Add("all: " + text);
        public void Print(int entIndex, string text) => Lines.Add($"{entIndex}: {text}");
    }

    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    private sealed class Hooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class BootWatcher : IHostBootHook
    {
        public List<string> Seen { get; } = new();
        public void OnBooted(BootInfo info) => Seen.Add("booted " + info.InstanceKind);
        public void OnLive() => Seen.Add("live");
        public void OnHostFailed(string why) => Seen.Add("failed");
    }

    private static BootInfo Boot(string kind = "hub", int depth = 0) => new(ModuleAnswer.Known, 42, kind, depth, "h", false);

    private static HostResult<T> Refused<T>(HostError error) => HostResult<T>.Refused(new HostRefusal(error, "reason", "detail"));

    private sealed record World(FakeLink Link, HostService Host, FakeConsole Console, BootWatcher Watcher, NowScheduler Scheduler, ListLog Log);

    private static World Host(HostingMode mode, int? port = 27016)
    {
        var link = new FakeLink { ModeIs = mode };
        var console = new FakeConsole();
        var watcher = new BootWatcher();
        var scheduler = new NowScheduler();
        var log = new ListLog();
        var host = new HostService(link, new Port(port), scheduler, console, new Hooks<IHostBootHook>(watcher), log);
        return new World(link, host, console, watcher, scheduler, log);
    }

    //-------------------------------------------------------------------------
    // HostService
    //-------------------------------------------------------------------------

    [Fact]
    public void Unhosted_never_starts_the_sdk_or_boots()
    {
        var w = Host(HostingMode.Unhosted);

        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");

        Assert.Empty(w.Link.Calls);
        Assert.Equal(HostingMode.Unhosted, w.Host.Mode);
    }

    [Fact]
    public void No_configuration_fails_loudly_and_quits_non_zero()
    {
        var w = Host((HostingMode)99);

        w.Host.OnPostInit();

        Assert.Equal(["quit 1"], w.Console.Calls);
        Assert.Contains(w.Log.Lines, l => l.StartsWith("W:[TF2Descent] HOSTING FAILED", StringComparison.Ordinal));
        Assert.Equal(["failed"], w.Watcher.Seen);
    }

    [Fact]
    public void Hosted_boots_each_level_retrying_while_unreachable()
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(Refused<BootInfo>(HostError.Unreachable));
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot("hub")));

        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");

        Assert.Equal(["start", "booting tf2_descent_town", "booting tf2_descent_town"], w.Link.Calls);
        Assert.Equal(1, w.Scheduler.Delays);
        Assert.Equal("hub", w.Host.Boot!.InstanceKind);
        Assert.Equal(["booted hub"], w.Watcher.Seen);
    }

    [Fact]
    public void A_boot_refusal_that_retrying_cannot_fix_quits_non_zero()
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(Refused<BootInfo>(HostError.ContractVersion));

        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");

        Assert.Null(w.Host.Boot);
        Assert.Equal(["quit 1"], w.Console.Calls);
        Assert.Contains(w.Log.Lines, l => l.Contains("ContractVersion"));
    }

    [Fact]
    public void Map_ready_reports_the_bound_port_and_goes_live()
    {
        var w = Host(HostingMode.Hosted, port: 27016);
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot()));

        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");
        w.Host.OnLevelReady("tf2_descent_town");

        Assert.Contains("mapready 27016 tf2_descent_town", w.Link.Calls);
        Assert.True(w.Host.Live);
        Assert.Equal(["booted hub", "live"], w.Watcher.Seen);
    }

    [Fact]
    public void An_unknown_bound_port_is_never_guessed()
    {
        var w = Host(HostingMode.Hosted, port: null);
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot()));

        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");
        w.Host.OnLevelReady("tf2_descent_town");

        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("mapready", StringComparison.Ordinal));
        Assert.Equal(["quit 1"], w.Console.Calls);
    }

    //-------------------------------------------------------------------------
    // PlayerService
    //-------------------------------------------------------------------------

    private static (World W, PlayerService Players, FakeClients Clients, RecordingEngine Net) Players(string kind = "hub", bool booted = true)
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(booted ? HostResult<BootInfo>.Success(Boot(kind, kind == "hub" ? 0 : 3)) : Refused<BootInfo>(HostError.Unreachable));
        w.Host.OnPostInit();
        if (booted)
            w.Host.OnLevelInit("tf2_descent_town");
        var clients = new FakeClients();
        var net = new RecordingEngine();
        var players = new PlayerService(w.Link, w.Host, clients, new ClientMessages(net), new NeverScheduler(), w.Log);
        return (w, players, clients, net);
    }

    [Fact]
    public void Connections_are_refused_before_boot_and_while_draining()
    {
        var (_, before, _, _) = Players(booted: false);
        Assert.Contains("starting", before.OnClientConnecting("p", "1.2.3.4:5"));

        var (_, players, _, _) = Players();
        Assert.Null(players.OnClientConnecting("p", "1.2.3.4:5"));
        players.Draining = true;
        Assert.Contains("closing", players.OnClientConnecting("p", "1.2.3.4:5"));
    }

    [Fact]
    public void An_active_client_joins_and_its_leave_releases_it()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");

        players.OnClientActive(3);

        Assert.Contains("join 203.0.113.7:27005 76561197960287930 hub soldier", w.Link.Calls);
        Assert.Equal(1, w.Link.PlayersReported);
        var told = Assert.Single(net.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("TF2.Descent:instance", told.Arg(0));
        Assert.Equal(3, told.Arg(2));
        Assert.NotNull(players.BySteamId("76561197960287930"));

        players.OnClientDisconnected(3);

        Assert.Contains("leave 76561197960287930 203.0.113.7:27005", w.Link.Calls);
        Assert.Equal(0, w.Link.PlayersReported);
    }

    [Fact]
    public void A_bot_is_not_a_player_of_the_service()
    {
        var (w, players, _, _) = Players();

        players.OnClientActive(5);    // no address: a bot

        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("join", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hosted_player_is_keyed_on_the_hosts_steamid_never_the_engines()
    {
        // Hosted, the engine runs with sv_lan 1: it reports a LAN id, and the
        // gateway's validated SteamID comes back from the join.
        var (w, players, clients, _) = Players();
        const ulong engineId = 90071992547409921UL;
        const string real = "76561197960287930";
        clients.Slots[3] = (engineId, "127.0.0.2:27005");
        w.Link.HostIds["127.0.0.2:27005"] = real;

        players.OnClientActive(3);

        Assert.Contains($"join 127.0.0.2:27005 {engineId} hub soldier", w.Link.Calls);
        Assert.Equal(3, players.BySteamId(real)?.EntIndex);
        Assert.Null(players.BySteamId(engineId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        players.OnClientDisconnected(3);

        Assert.Contains($"leave {real} 127.0.0.2:27005", w.Link.Calls);
    }

    [Fact]
    public void An_engine_without_steam_still_joins_a_player_the_host_knows()
    {
        var (w, players, clients, _) = Players();
        clients.Slots[3] = (0, "127.0.0.2:27005");
        w.Link.HostIds["127.0.0.2:27005"] = "76561197960287930";

        players.OnClientActive(3);

        Assert.DoesNotContain(clients.Calls, c => c.StartsWith("kick 3", StringComparison.Ordinal));
        Assert.Equal(1, w.Link.PlayersReported);
        Assert.NotNull(players.BySteamId("76561197960287930"));
    }

    [Fact]
    public void A_kick_and_a_lost_lease_find_the_player_by_the_hosts_steamid()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (90071992547409921UL, "127.0.0.2:27005");
        w.Link.HostIds["127.0.0.2:27005"] = "76561197960287930";
        players.OnClientActive(3);
        var commands = new CommandService(w.Link, players, clients, new ClientMessages(net), new FakeChat(), w.Console, w.Log);

        commands.OnKick(new KickCommand("k1", "76561197960287930", "banned"));
        players.OnLeaseLost("76561197960287930", LeaseLostReason.Revoked, "taken elsewhere");

        Assert.Contains("kick 3 banned", clients.Calls);
        Assert.Contains(clients.Calls, c => c.StartsWith("kick 3 Your character", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("76561197960287930", "76561197960287930")]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData("STEAM_0:1:1", null)]
    [InlineData("-5", null)]
    public void A_join_names_the_player_only_by_a_real_steamid(string answered, string? expected) =>
        Assert.Equal(expected, HostLink.HostSteamId(new PlayerJoin(true, "", "s", "") { SteamId = answered }));

    [Fact]
    public void An_unauthenticated_or_refused_client_is_kicked_with_the_reason()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (0, "203.0.113.7:27005");
        clients.Slots[4] = (76561197960287931UL, "203.0.113.8:27005");

        players.OnClientActive(3);    // no engine id, and a peer the host does not know
        w.Link.JoinAnswer = new JoinOutcome(false, "single_presence");
        players.OnClientActive(4);

        Assert.Contains(clients.Calls, c => c.StartsWith("kick 3 Steam authentication", StringComparison.Ordinal));
        Assert.Contains("kick 4 single_presence", clients.Calls);
        Assert.Equal(-1, w.Link.PlayersReported);
    }

    [Fact]
    public void A_lost_lease_removes_and_kicks_the_player()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);

        players.OnLeaseLost("76561197960287930", LeaseLostReason.Revoked, "taken elsewhere");

        Assert.Contains(clients.Calls, c => c.StartsWith("kick 3", StringComparison.Ordinal) && c.Contains("Revoked"));
        Assert.Null(players.BySteamId("76561197960287930"));
        Assert.Equal(0, w.Link.PlayersReported);
    }

    //-------------------------------------------------------------------------
    // ReauthService (SDK 2.6.0 Reauth): the host asks, the client answers
    //-------------------------------------------------------------------------

    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const ulong GatewayGs = 90071992547409920UL;

    private static (World W, ReauthService Reauth, RecordingEngine Net) Reauth()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (0, "127.0.0.2:27005");
        w.Link.HostIds["127.0.0.2:27005"] = "76561197960287930";
        clients.Slots[4] = (0, "127.0.0.3:27005");
        w.Link.HostIds["127.0.0.3:27005"] = "76561197960287931";
        players.OnClientActive(3);
        players.OnClientActive(4);
        return (w, new ReauthService(w.Link, players, new ClientMessages(net), w.Log), net);
    }

    private static ReauthNotice Asked(string steamId = "76561197960287930", string nonce = Nonce) =>
        new(steamId, nonce, GatewayGs, DateTimeOffset.UnixEpoch.AddMinutes(1));

    [Fact]
    public void A_reauth_asks_that_players_client_and_its_ticket_goes_to_the_host_under_the_nonce()
    {
        var (w, reauth, net) = Reauth();
        byte[] ticket = [0x14, 0, 0, 0, 0xAB, 0xCD, 0xEF];

        reauth.OnReauth(Asked());

        var asked = Assert.Single(net.Calls, c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:reauth");
        Assert.Equal(3, asked.Arg(2));
        Assert.Equal(1, reauth.PendingCount);

        Assert.True(reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write(Nonce, ticket)));

        var sent = Assert.Single(w.Link.Tickets);
        Assert.Equal(("76561197960287930", Nonce), (sent.SteamId, sent.Nonce));
        Assert.Equal(ticket, sent.Ticket);
        Assert.Equal(0, reauth.PendingCount);
        Assert.Contains(w.Log.Lines, l => l.Contains("re-authenticated"));
    }

    [Fact]
    public void A_ticket_nobody_asked_for_another_clients_answer_and_a_stale_nonce_are_not_submitted()
    {
        var (w, reauth, _) = Reauth();
        byte[] ticket = [1, 2, 3];

        Assert.True(reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write(Nonce, ticket)));     // not asked
        reauth.OnReauth(Asked());
        Assert.True(reauth.OnClientMessage(4, TicketReply.Key, TicketReply.Write(Nonce, ticket)));     // not the asked client
        Assert.True(reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write("ffff", ticket)));    // not the asked nonce
        Assert.True(reauth.OnClientMessage(3, TicketReply.Key, [9, 9]));                               // malformed

        Assert.Empty(w.Link.Tickets);
        Assert.Equal(1, reauth.PendingCount);
        Assert.False(reauth.OnClientMessage(3, "someone_else:key", [1]));                             // not ours to claim
    }

    [Fact]
    public void A_later_reauth_supersedes_the_earlier_nonce()
    {
        var (w, reauth, _) = Reauth();

        reauth.OnReauth(Asked(nonce: "aaaa"));
        reauth.OnReauth(Asked(nonce: "bbbb"));
        reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write("aaaa", [1]));
        reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write("bbbb", [2]));

        Assert.Equal("bbbb", Assert.Single(w.Link.Tickets).Nonce);
    }

    [Fact]
    public void A_reauth_for_someone_not_here_sends_nothing_and_a_leaver_is_forgotten()
    {
        var (w, reauth, net) = Reauth();

        reauth.OnReauth(Asked("76561197960287999"));
        Assert.DoesNotContain(net.Calls, c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:reauth");

        reauth.OnReauth(Asked());
        reauth.OnClientDisconnected(3);
        Assert.Equal(0, reauth.PendingCount);
    }

    [Theory]
    [InlineData(TicketOutcome.Denied)]
    [InlineData(TicketOutcome.Timeout)]
    public void A_ticket_the_host_does_not_accept_is_logged_with_its_outcome(TicketOutcome outcome)
    {
        var (w, reauth, _) = Reauth();
        w.Link.TicketAnswer = new TicketAnswer(outcome);

        reauth.OnReauth(Asked());
        reauth.OnClientMessage(3, TicketReply.Key, TicketReply.Write(Nonce, [1, 2]));

        Assert.Contains(w.Log.Lines, l => l.StartsWith("W:", StringComparison.Ordinal) && l.Contains(outcome.ToString()));
    }

    [Fact]
    public void A_nonce_that_cannot_travel_is_not_sent_to_the_client()
    {
        var (_, reauth, net) = Reauth();

        reauth.OnReauth(Asked(nonce: "has space"));

        Assert.DoesNotContain(net.Calls, c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:reauth");
        Assert.Equal(0, reauth.PendingCount);
    }

    //-------------------------------------------------------------------------
    // CommandService
    //-------------------------------------------------------------------------

    private static (World W, PlayerService Players, FakeClients Clients, CommandService Commands, FakeChat Chat, RecordingEngine Net) Commands()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        var chat = new FakeChat();
        return (w, players, clients, new CommandService(w.Link, players, clients, new ClientMessages(net), chat, w.Console, w.Log), chat, net);
    }

    [Fact]
    public void Kick_kicks_the_player_the_service_names()
    {
        var (_, _, clients, commands, _, _) = Commands();

        commands.OnKick(new KickCommand("k1", "76561197960287930", "cheating"));

        Assert.Contains("kick 3 cheating", clients.Calls);
    }

    [Fact]
    public void Exec_runs_the_command_and_replies_with_its_output()
    {
        var (w, _, _, commands, _, _) = Commands();

        commands.OnExec("e1", "status");

        Assert.Contains("exec status", w.Console.Calls);
        Assert.Contains("reply e1 output of status", w.Link.Calls);
    }

    [Fact]
    public void Retry_reconnects_the_client_and_treats_the_player_as_left()
    {
        var (w, players, clients, commands, _, _) = Commands();

        commands.OnRetry(new RetryCommand("r1", "76561197960287930"));

        Assert.Contains("reconnect 3", clients.Calls);
        Assert.Contains("leave 76561197960287930 203.0.113.7:27005", w.Link.Calls);
        Assert.Null(players.BySteamId("76561197960287930"));
    }

    [Fact]
    public void Shutdown_leaves_everyone_then_disposes_the_sdk_then_quits_zero()
    {
        var (w, _, _, commands, _, _) = Commands();

        commands.OnShutdown(new ShutdownCommand("s1", "maintenance"));

        var tail = w.Link.Calls.SkipWhile(c => !c.StartsWith("leave", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["leave 76561197960287930 203.0.113.7:27005", "dispose"], tail);
        Assert.Equal(["quit 0"], w.Console.Calls);
    }

    [Fact]
    public void Drain_refuses_new_players_and_tells_the_current_ones()
    {
        var (_, players, _, commands, chat, _) = Commands();

        commands.OnDrain(new DrainCommand("d1", "update"));

        Assert.True(players.Draining);
        Assert.Contains(chat.Lines, l => l.StartsWith("all: This server is closing (update)", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // TravelService (the mine cage, the shaft, a level's stairs)
    //-------------------------------------------------------------------------

    private static (World W, TravelService Travel, RecordingEngine Net) Travel(HostingMode mode = HostingMode.Hosted)
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        net.Calls.ToList();   // the join's instance message is already recorded; facts look past it
        if (mode != HostingMode.Hosted)
            w.Link.ModeIs = mode;
        var host = w.Host;
        return (w, new TravelService(w.Link, host, players, new ClientMessages(net), w.Log), net);
    }

    private static int Notices(RecordingEngine net) =>
        net.Calls.Count(c => c.Member == "UserMessages.Send" && (string?)c.Arg(0) == "TF2.Descent:notice");

    [Fact]
    public void A_transition_asks_the_service_for_the_activating_player_and_tells_them()
    {
        var (w, travel, net) = Travel();

        travel.Request(3, TravelDirection.Down);

        Assert.Contains("travel 76561197960287930 Down hub", w.Link.Calls);
        Assert.Equal(1, Notices(net));
    }

    [Theory]
    [InlineData(TravelState.Waiting)]
    [InlineData(TravelState.Refused)]
    public void Waiting_and_refused_answers_are_shown_to_the_player(TravelState state)
    {
        var (w, travel, net) = Travel();
        w.Link.TravelAnswer = new TravelAnswer(state, TimeSpan.FromSeconds(4), "no_capacity");

        travel.Request(3, TravelDirection.Down);

        Assert.Equal(1, Notices(net));
    }

    [Fact]
    public void A_player_who_is_not_joined_or_a_server_unhosted_asks_nothing()
    {
        var (w, travel, _) = Travel();

        travel.Request(9, TravelDirection.Down);   // no such player

        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("travel", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // Same-process hops (docs/sdk-integration.md §6): the process changes
    // level and becomes the target instance, and the players join again.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_same_process_hop_releases_every_held_player_a_cross_process_one_only_its_own()
    {
        string[] held = ["a", "b", "c"];

        Assert.Equal(held, HostLink.Leaving(sameProcess: true, "b", held));
        Assert.Equal(["b"], HostLink.Leaving(sameProcess: false, "b", held));
    }

    [Fact]
    public void A_client_active_before_the_levels_boot_joins_when_the_boot_answers()
    {
        var (w, players, clients, _) = Players(booted: false);
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");

        players.OnClientActive(3);
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("join", StringComparison.Ordinal));

        w.Link.BootAnswers.Clear();
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot("level", 1)));
        w.Host.OnLevelInit("tf2_descent-1-a");
        players.OnBooted(w.Host.Boot!);

        Assert.Contains("join 203.0.113.7:27005 76561197960287930 level soldier", w.Link.Calls);
        Assert.NotNull(players.BySteamId("76561197960287930"));
    }

    [Fact]
    public void The_level_the_process_left_is_forgotten_without_a_second_leave()
    {
        var (w, players, clients, _) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        Assert.NotNull(players.BySteamId("76561197960287930"));
        w.Link.Calls.Clear();

        // The hop released them already; the new level's boot answers.
        players.OnBooted(Boot("level", 1));

        Assert.Null(players.BySteamId("76561197960287930"));
        Assert.Equal(0, w.Link.PlayersReported);

        players.OnClientDisconnected(3);
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("leave", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // A lease lost to an unreachable service (plan_host_sdk P9, criterion 4):
    // a service restart longer than the lease TTL must not drop the player.
    //-------------------------------------------------------------------------

    private const string Steam = "76561197960287930";

    [Fact]
    public void A_lease_lost_to_an_unreachable_service_holds_the_player_and_re_leases_them()
    {
        var (w, players, clients, net) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        w.Link.Calls.Clear();

        players.OnLeaseLost(Steam, LeaseLostReason.Unreachable, "no heartbeat");

        Assert.Empty(clients.Calls);
        Assert.Contains(w.Link.Calls, c => c.StartsWith("join 203.0.113.7:27005 " + Steam, StringComparison.Ordinal));
        Assert.False(players.IsSuspended(Steam));
        Assert.NotNull(players.BySteamId(Steam));
        Assert.Equal(2, Notices(net));    // held, then back
    }

    [Fact]
    public void A_refused_re_lease_keeps_holding_and_the_connection_coming_back_retries()
    {
        var (w, players, clients, _) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);

        w.Link.JoinAnswer = new JoinOutcome(false, "Unreachable: unreachable");
        players.OnLeaseLost(Steam, LeaseLostReason.Unreachable, "no heartbeat");

        Assert.True(players.IsSuspended(Steam));
        Assert.Null(players.BySteamId(Steam));
        Assert.Empty(clients.Calls);

        w.Link.JoinAnswer = new JoinOutcome(true, null, "c-1", "soldier", "hero", 0);
        players.OnConnectionChanged(true);

        Assert.False(players.IsSuspended(Steam));
        Assert.NotNull(players.BySteamId(Steam));
    }

    [Fact]
    public void A_player_still_held_when_the_bound_runs_out_is_kicked()
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot()));
        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");
        var clients = new FakeClients();
        var players = new PlayerService(w.Link, w.Host, clients, new ClientMessages(new RecordingEngine()),
                                        new NowScheduler(), w.Log);
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);

        w.Link.JoinAnswer = new JoinOutcome(false, "Unreachable: unreachable");
        players.OnLeaseLost(Steam, LeaseLostReason.Unreachable, "no heartbeat");

        Assert.False(players.IsSuspended(Steam));
        Assert.Contains(clients.Calls, c => c.StartsWith("kick 3 ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(LeaseLostReason.Stale)]
    [InlineData(LeaseLostReason.Revoked)]
    public void A_lease_someone_else_holds_or_the_service_revoked_kicks_at_once(LeaseLostReason reason)
    {
        var (w, players, clients, _) = Players();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        players.OnClientActive(3);
        w.Link.Calls.Clear();

        players.OnLeaseLost(Steam, reason, "gone");

        Assert.Contains(clients.Calls, c => c.StartsWith("kick 3 ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("join", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // tf2_descent_travel: a hop driven by the service's Exec (P9 criterion 3).
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("3 down", true, 3, TravelDirection.Down)]
    [InlineData("3 UP", true, 3, TravelDirection.Up)]
    [InlineData("0 down", false, 0, TravelDirection.Down)]
    [InlineData("3", false, 0, TravelDirection.Down)]
    [InlineData("3 sideways", false, 0, TravelDirection.Down)]
    [InlineData("x down", false, 0, TravelDirection.Down)]
    public void The_travel_command_takes_a_slot_and_a_direction(string args, bool ok, int slot, TravelDirection direction)
    {
        Assert.Equal(ok, TravelCommand.TryParse(args, out int s, out var d));
        Assert.Equal((slot, direction), (s, d));
    }

    [Fact]
    public void The_travel_command_asks_for_travel_as_a_transition_would()
    {
        var (w, travel, _) = Travel();
        w.Link.Calls.Clear();

        new TravelCommand(travel, w.Log).Execute("3 down");

        Assert.Contains("travel 76561197960287930 Down hub", w.Link.Calls);
    }
}
