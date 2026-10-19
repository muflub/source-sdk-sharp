using TF2.Descent.Server;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Host.Sdk;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// Dying in TF2Descent (plan_descent_death §4 M2, phase P2), against a
/// recording fake link: a hosted death keeps the body, shows the death screen,
/// and records with the host at once; the hop's Ready waits while the screen
/// shows and respawn stays refused; an unhosted death leaves the stock cycle
/// alone; a failing RecordDeath is retried with back-off and, after 60 s,
/// disconnects the player.
/// </summary>
public sealed class DeathServiceTests
{
    private const string Steam = "76561197960287930";
    private const string Model = "models/humans/group03/male_01.mdl";
    private static readonly EntityHandle Player = new(7u);
    private static readonly EntityHandle Other = new(8u);
    private static readonly EntityHandle Body = new(99u);
    private static readonly Vector DiedAt = new(10f, 20f, 30f);

    //-------------------------------------------------------------------------
    // Fakes. Like ServiceFlowTests' own: every link task already complete, so
    // a flow runs to its end inside the call that starts it.
    //-------------------------------------------------------------------------

    private sealed class FakeLink : IHostLink
    {
        public List<string> Calls { get; } = new();
        public HostingMode ModeIs { get; set; } = HostingMode.Hosted;
        public Queue<HostResult<BootInfo>> BootAnswers { get; } = new();
        public HostResult<int> MapReadyAnswer { get; set; } = HostResult<int>.Success(1);
        public JoinOutcome JoinAnswer { get; set; } = new(true, null, "c-1", "soldier", "hero", 0);

        public HostingMode Mode() => ModeIs;
        public void Start() => Calls.Add("start");
        public Task<HostResult<BootInfo>> Booting(string map) { Calls.Add("booting " + map); return Task.FromResult(BootAnswers.Dequeue()); }
        public Task<HostResult<int>> MapReady(int port, string map) { Calls.Add($"mapready {port} {map}"); return Task.FromResult(MapReadyAnswer); }
        public void Players(int count) => Calls.Add($"players {count}");
        public Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className)
        {
            Calls.Add($"join {peer} {engineSteamId} {boot.InstanceKind} {className}");
            return Task.FromResult(JoinAnswer with { SteamId = engineSteamId, ClientAddr = "198.51.100.9:27005" });
        }
        public Task<bool> Checkpoint(string steamId) { Calls.Add("checkpoint " + steamId); return Task.FromResult(true); }
        public Task<bool> Leave(string steamId, string peer) { Calls.Add($"leave {steamId} {peer}"); return Task.FromResult(true); }
        public void ReplyExec(string commandId, string output) => Calls.Add($"reply {commandId} {output}");
        public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot)
        {
            Calls.Add($"travel {steamId} {direction} {boot.InstanceKind}");
            return Task.FromResult(new TravelAnswer(TravelState.Started, TimeSpan.Zero, ""));
        }
        public Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket) => Task.FromResult(new TicketAnswer(TicketOutcome.Ok));
        public void Dispose() => Calls.Add("dispose");

        // The death lane (M1). The queue answers RecordDeath; an empty queue
        // answers true, so a fact says out loud when the host fails. A throw
        // is the unreachable host; a refusal is false.
        public Queue<bool> RecordAnswers { get; } = new();
        public List<(string SteamId, Vector Position)> Recorded { get; } = new();
        public bool AlwaysFails { get; set; }
        public bool ThrowOnRecord { get; set; }

        public Task<bool> RecordDeath(string steamId, Vector position)
        {
            if (ThrowOnRecord)
                return Task.FromException<bool>(new InvalidOperationException("the host is unreachable"));
            Recorded.Add((steamId, position));
            return Task.FromResult(!AlwaysFails && (RecordAnswers.Count == 0 || RecordAnswers.Dequeue()));
        }

        public LootAnswer LootAnswer { get; set; } = new(true, 3, "");
        public List<string> Looted { get; } = new();
        public Task<LootAnswer> LootCorpse(string steamId) { Looted.Add(steamId); return Task.FromResult(LootAnswer); }
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

    private sealed class FakeScreen : IScreenEffects
    {
        public List<(EntityHandle Player, int R, int G, int B, int A, float Duration, float Hold, FadeFlags Flags)> Fades { get; } = new();
        public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                          ShakeCommand command = ShakeCommand.Start, bool airShake = false) { }
        public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags)
            => Fades.Add((player, r, g, b, a, duration, holdTime, flags));
    }

    private sealed class Port(int? port) : IBoundPort { public int? Read() => port; }

    private sealed class NeverScheduler : IFrameScheduler
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) => new TaskCompletionSource().Task;
    }

    /// <summary>Delays complete at once and move the shared clock: the 60 s retry road terminates deterministically.</summary>
    private sealed class VirtualScheduler : IFrameScheduler
    {
        public long NowMs { get; set; }
        public List<TimeSpan> Delays { get; } = new();
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default)
        {
            Delays.Add(delay);
            NowMs += (long)delay.TotalMilliseconds;
            return Task.CompletedTask;
        }
    }

    /// <summary>Delays stay pending until released: the fact that asks what the hop waits on.</summary>
    private sealed class ManualScheduler : IFrameScheduler
    {
        private readonly List<TaskCompletionSource> _pending = new();
        public List<TimeSpan> Delays { get; } = new();
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default)
        {
            Delays.Add(delay);
            var done = new TaskCompletionSource();
            _pending.Add(done);
            return done.Task;
        }
        public void ReleaseAll() { foreach (var done in _pending.ToArray()) done.SetResult(); _pending.Clear(); }
    }

    private sealed class FakeConsole : IServerConsole
    {
        public string Execute(string command) => "ok";
        public void Quit(int exitCode) { }
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

    private sealed class Bodies(params ScriptedEntity[] bodies) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => bodies.FirstOrDefault(b => b.Handle == handle);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => Array.Empty<ScriptedEntity>();
        public bool IsRegistered(string className) => className == EntityNames.SsRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Array.Empty<ScriptedClassInfo>();
    }

    private sealed class None : IHostBootHook
    {
        public void OnBooted(BootInfo info) { }
        public void OnLive() { }
        public void OnHostFailed(string why) { }
    }

    private static BootInfo Boot() => new(ModuleAnswer.Known, 42, "level", 3, "h", false);

    private sealed record Kit(
        FakeLink Link, HostService Host, PlayerService Players, FakeClients Clients,
        Corpses Corpses, SsRagdoll Body, FakeScreen Screen, RecordingEngine Net, ListLog Log);

    /// <summary>
    /// The world of a death: this level booted (hosted only), player 3 joined
    /// as "hero" on the host's SteamID, and the body a stock death would leave
    /// stands ready — <c>PlayerDeaths.OnKilled</c> makes it with
    /// <c>Corpses.Make</c> and hands the handle to the hook, so the fact makes
    /// it the same way and hands it over in <see cref="Die"/>.
    /// </summary>
    private static Kit World(HostingMode mode = HostingMode.Hosted)
    {
        var log = new ListLog();
        var link = new FakeLink { ModeIs = mode };
        var host = new HostService(link, new Port(27016), new NeverScheduler(), new FakeConsole(),
                                   new Hooks<IHostBootHook>(new None()), log);
        host.OnPostInit();
        if (mode == HostingMode.Hosted)
        {
            link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot()));
            host.OnLevelInit("tf2_descent_depth3");
        }

        var clients = new FakeClients();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        var net = new RecordingEngine();
        var players = new PlayerService(link, host, clients, new ClientMessages(net), new NeverScheduler(), log);
        if (mode == HostingMode.Hosted)
            players.OnClientActive(3);    // FakeLink's join is already complete

        var engine = new RecordingEngine();
        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("Entities.GetOrigin", DiedAt);
        engine.Returns("Entities.GetAngles", default(Vector));
        engine.Returns("Entities.GetVelocity", default(Vector));
        engine.Returns("Entities.Create", Body);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetIndex", 3);
        var body = new SsRagdoll(engine) { Handle = Body };
        body.BindEngine(engine);
        body.State.BindLocal();
        var corpses = new Corpses(log, engine, engine, new Bodies(body), engine);
        Assert.Equal(Body, corpses.Make(Player));

        return new Kit(link, host, players, clients, corpses, body, new FakeScreen(), net, log);
    }

    private static DeathService Deaths(Kit w, IFrameScheduler? scheduler = null)
    {
        var sched = scheduler ?? new VirtualScheduler();
        return new DeathService(w.Link, w.Host, w.Players, w.Corpses, w.Screen, new ClientMessages(w.Net),
                                w.Clients, sched, w.Log)
        {
            Clock = sched switch
            {
                VirtualScheduler v => () => v.NowMs,
                _ => () => 0L,    // the manual scheduler stands the clock still at the death
            },
        };
    }

    private static void Die(DeathService deaths) =>
        deaths.OnPlayerDied(new PlayerDeath(Player, 3, Other, DiedAt, Body));

    // The notice payload is its text, NUL-terminated (ClientMessageTests pins
    // the layout); the sender records (key, length, player, reliable, bytes).
    private static string NoticeText(EngineCall call) =>
        System.Text.Encoding.UTF8.GetString((byte[])call.Arg(4)!).TrimEnd('\0');

    private static List<EngineCall> Notices(RecordingEngine net) =>
        net.Calls.Where(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice").ToList();

    //-------------------------------------------------------------------------
    // The hosted death: keep, screen, record at once
    //-------------------------------------------------------------------------

    [Fact]
    public void A_hosted_death_keeps_the_body_on_the_ground()
    {
        var w = World();
        var deaths = Deaths(w);

        Die(deaths);
        w.Body.OnThink();     // the lifetime is up; a kept body does not care

        Assert.False(w.Body.Gone);
    }

    [Fact]
    public void An_unhosted_death_leaves_the_stock_cycle_untouched()
    {
        // The same death with no hosting service: the body still goes at its
        // lifetime, nothing is shown, nothing is recorded, and the gate says
        // nothing. World()'s hosted arm is the positive control for each.
        var w = World(HostingMode.Unhosted);
        var deaths = Deaths(w);

        Die(deaths);
        w.Body.OnThink();

        Assert.True(w.Body.Gone);
        Assert.Empty(w.Screen.Fades);
        Assert.Empty(Notices(w.Net));
        Assert.Empty(w.Link.Recorded);
        Assert.Empty(w.Clients.Calls);
        Assert.True(deaths.MayRespawn(Player, 3));
    }

    [Fact]
    public void The_death_screen_is_a_dark_red_stay_out_fade_for_the_dead_player_only()
    {
        var w = World();
        var deaths = Deaths(w);

        Die(deaths);

        var fade = Assert.Single(w.Screen.Fades);
        Assert.Equal(Player, fade.Player);
        Assert.True(fade.R > 0, "a dark red has a red component");
        Assert.Equal(0, fade.G);
        Assert.Equal(0, fade.B);
        Assert.Equal(255, fade.A);
        Assert.True(fade.Flags.HasFlag(FadeFlags.StayOut));
    }

    [Fact]
    public void The_dead_player_is_told_and_the_party_hears_the_name()
    {
        var w = World();
        var deaths = Deaths(w);

        Die(deaths);

        var toPlayer = Notices(w.Net).Where(c => (int)c.Arg(2)! == 3).Select(NoticeText).ToArray();
        Assert.Contains("You have died", toPlayer);
        Assert.Contains("Returning to town…", toPlayer);

        // The party line goes to everyone (player 0) and names the dead by the
        // name the join answered with, not the SteamID.
        Assert.Contains(Notices(w.Net).Where(c => (int)c.Arg(2)! == 0).Select(NoticeText),
                        t => t == "hero has died");
    }

    [Fact]
    public void The_death_is_recorded_at_once_with_where_they_died()
    {
        var w = World();
        var deaths = Deaths(w);

        Die(deaths);      // synchronous: the record is out before this returns

        var record = Assert.Single(w.Link.Recorded);
        Assert.Equal(Steam, record.SteamId);
        Assert.Equal(DiedAt, record.Position);
    }

    //-------------------------------------------------------------------------
    // The respawn gate
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dead_player_cannot_respawn_while_anyone_else_can()
    {
        var w = World();
        var sched = new VirtualScheduler();
        var deaths = Deaths(w, sched);

        Die(deaths);
        sched.NowMs += 90_000;   // past even the disconnect timeout: the gate is not a timer

        Assert.False(deaths.MayRespawn(Player, 3));
        Assert.True(deaths.MayRespawn(Other, 5));
    }

    [Fact]
    public void The_new_level_finds_its_players_alive()
    {
        // The same-process hop to town runs OnLevelInit here, not a leave:
        // without the clear the town finds its arrival permanently dead.
        var w = World();
        var deaths = Deaths(w);

        Die(deaths);
        Assert.False(deaths.MayRespawn(Player, 3));   // the gate closed

        deaths.OnLevelInit("tf2_descent_town");
        Assert.True(deaths.MayRespawn(Player, 3));
    }

    //-------------------------------------------------------------------------
    // RecordDeath failing: retry, then the 60 s disconnect
    //-------------------------------------------------------------------------

    [Fact]
    public void A_failed_RecordDeath_is_retried_until_the_host_answers()
    {
        var w = World();
        w.Link.RecordAnswers.Enqueue(false);
        w.Link.RecordAnswers.Enqueue(false);
        var deaths = Deaths(w);

        Die(deaths);

        Assert.Equal(3, w.Link.Recorded.Count);      // refused, refused, accepted
        Assert.DoesNotContain(w.Clients.Calls, c => c.StartsWith("kick", StringComparison.Ordinal));
    }

    [Fact]
    public void A_RecordDeath_the_host_never_answers_disconnects_the_player_after_60_s()
    {
        var w = World();
        w.Link.AlwaysFails = true;
        var sched = new VirtualScheduler();
        var deaths = Deaths(w, sched);

        Die(deaths);

        Assert.Contains("kick 3 Could not reach the hosting service", w.Clients.Calls);
        var retriedFor = sched.Delays.Aggregate(TimeSpan.Zero, (a, d) => a + d);
        Assert.True(retriedFor.TotalSeconds >= 60,
                    $"retried for {retriedFor}, not the 60 s the plan rules");
        Assert.Equal(TimeSpan.FromSeconds(1), sched.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), sched.Delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(4), sched.Delays[2]);
        Assert.True(sched.Delays.All(d => d.TotalSeconds <= 8), "the back-off caps at 8 s");
        Assert.Single(w.Screen.Fades);    // the screen stays up; it is not re-shown per retry
    }

    [Fact]
    public void A_host_that_throws_is_retried_like_one_that_refuses()
    {
        var w = World();
        w.Link.ThrowOnRecord = true;
        var deaths = Deaths(w);

        Die(deaths);

        // A throw is not an answer: the same 60 s road as a refusal, and it
        // does not escape the hook that caught it.
        Assert.Contains("kick 3 Could not reach the hosting service", w.Clients.Calls);
    }

    [Fact]
    public void A_player_who_leaves_during_a_retry_stops_being_asked()
    {
        var w = World();
        w.Link.AlwaysFails = true;
        var manual = new ManualScheduler();
        var deaths = Deaths(w, manual);

        Die(deaths);                              // call 1 refused; the loop waits on a Delay
        deaths.OnClientDisconnected(3);           // the player leaves while it waits
        manual.ReleaseAll();

        Assert.Single(w.Link.Recorded);           // no further asks after the leave
        Assert.DoesNotContain(w.Clients.Calls, c => c.StartsWith("kick", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------
    // The hop holds its Ready for the screen (M2.3)
    //-------------------------------------------------------------------------

    [Fact]
    public void The_hop_holds_its_Ready_until_the_screen_has_shown_for_3_s()
    {
        var w = World();
        var manual = new ManualScheduler();
        var deaths = Deaths(w, manual);

        Die(deaths);                              // recorded at once: only the screen waits
        var hold = ((IHopReadyHold)deaths).BeforeReady(Steam);

        Assert.False(hold.IsCompleted);
        var delay = Assert.Single(manual.Delays);
        Assert.Equal(TimeSpan.FromSeconds(3), delay);

        manual.ReleaseAll();
        Assert.True(hold.IsCompleted);
    }

    [Fact]
    public void A_hop_for_the_undead_is_never_held()
    {
        var w = World();
        var sched = new VirtualScheduler();
        var deaths = Deaths(w, sched);

        Die(deaths);
        sched.NowMs = 3_000;      // the screen has already shown its 3 s

        Assert.True(((IHopReadyHold)deaths).BeforeReady(Steam).IsCompleted);
        Assert.Empty(sched.Delays);

        // A hop for a living player does not even look at the dead list.
        Assert.True(((IHopReadyHold)deaths).BeforeReady("76561197960287931").IsCompleted);
    }

    //-------------------------------------------------------------------------
    // The wiring that asks
    //-------------------------------------------------------------------------

    [Fact]
    public void The_registrar_dispatches_the_death_hold()
    {
        // Without this row the container hands HostLink no hold and the screen
        // never delays a Ready in the game — the LevelPrefetch/LevelHop class
        // of bug HookOrderTests was written around.
        var registry = new RecordingRegistry();
        new TF2DescentServerRegistrar().Register(registry);

        Assert.Contains(typeof(IHopReadyHold), registry.Hooks);
    }

    private sealed class RecordingRegistry : ISideServiceRegistry
    {
        public List<Type> Hooks { get; } = new();
        public void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService { }
        public void AddInstance<TService>(TService instance) where TService : class { }
        public void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class { }
        public void AddHooks<THook>() where THook : class => Hooks.Add(typeof(THook));
    }
}
