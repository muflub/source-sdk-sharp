using TF2.Descent.Server;
using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Host.Sdk;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The SDK thread and the two queues between it and the game thread
/// (plan_host_sdk §2.9), and how the server decides to run hosted or not
/// (decision 6.1).
/// </summary>
public sealed class HostLinkTests
{
    private sealed class CommandLine(params string[] args) : ILocalCommandLine
    {
        public IReadOnlyList<string> Args { get; } = args;
    }

    private sealed class Env(Dictionary<string, string>? vars = null) : IProcessEnvironment
    {
        public string? GetVariable(string name) => vars?.GetValueOrDefault(name);
    }

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Mod : IModInfo
    {
        public string Id => "tf2_descent";
        public string GameDir => "/nowhere";
        public string ManagedDir => "/nowhere";
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private sealed class NoHooks<T> : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => [];
        public void Each(string what, Action<T> call) { }
    }

    private static HostLink Link(GameThread game, ILocalCommandLine? args = null) =>
        new(args ?? new CommandLine(), new Env(), game, game, new NullLog(), new PathModuleSource(new Mod()), new NoHooks<IHostConnectionHook>(),
            new NoHooks<IHostCommandHook>(), new NoHooks<ILeaseHook>(), new NoHooks<IHopReadyHold>());

    //-------------------------------------------------------------------------
    // Hosted or unhosted
    //-------------------------------------------------------------------------

    [Fact]
    public void Host_settings_mean_hosted()
    {
        Assert.Equal(HostingMode.Hosted, HostLink.Decide(["-hostlocal", "inproc"], _ => null, dedicated: true));
        Assert.Equal(HostingMode.Hosted, HostLink.Decide([], k => k == "SOURCESHARP_HOST_SERVICE" ? "svc:5001" : null, dedicated: true));
    }

    [Fact]
    public void The_retired_descent_variables_no_longer_name_a_service()
    {
        // The SDK reads only SOURCESHARP_HOST_* (host 939c8b3, no fallback): a
        // pod still setting DESCENT_SERVICE must be refused, not booted unhosted.
        Assert.Throws<InvalidOperationException>(
            () => HostLink.Decide([], k => k == "DESCENT_SERVICE" ? "svc:5001" : null, dedicated: true));
    }

    [Fact]
    public void The_unhosted_flag_alone_means_unhosted()
    {
        Assert.Equal(HostingMode.Unhosted, HostLink.Decide([HostLink.UnhostedFlag], _ => null, dedicated: true));
    }

    [Fact]
    public void Neither_is_refused_naming_both_ways_out()
    {
        // A pod whose Secret is broken has no settings and no flag: it must
        // fail loudly, not quietly run unhosted.
        var e = Assert.Throws<InvalidOperationException>(() => HostLink.Decide([], _ => null, dedicated: true));
        Assert.Contains("-hostlocal", e.Message);
        Assert.Contains(HostLink.UnhostedFlag, e.Message);
    }

    [Fact]
    public void A_listen_server_with_no_host_settings_is_unhosted_and_never_refused()
    {
        // The P6 live run: a player's client loads server.so too (the client
        // engine runs a listen server), found neither -unhosted nor host
        // settings, called it a hosting failure and quit the client before it
        // could connect. Only a dedicated server must fail loudly.
        Assert.Equal(HostingMode.Unhosted, HostLink.Decide([], _ => null, dedicated: false));
    }

    [Fact]
    public void A_listen_server_given_host_settings_is_still_hosted()
    {
        Assert.Equal(HostingMode.Hosted, HostLink.Decide(["-hostlocal", "x.json"], _ => null, dedicated: false));
    }

    [Fact]
    public void Both_are_refused()
    {
        var e = Assert.Throws<InvalidOperationException>(
            () => HostLink.Decide([HostLink.UnhostedFlag], k => k == "SOURCESHARP_HOST_INSTANCE_ID" ? "i-1" : null, dedicated: true));
        Assert.Contains("together", e.Message);
    }

    //-------------------------------------------------------------------------
    // The SDK thread
    //-------------------------------------------------------------------------

    private static void RunFramesUntil(GameThread game, Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            game.RunFrame(new NullLog());
            Thread.Sleep(1);
        }

        Assert.True(done(), "timed out waiting for the game thread to see the result");
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a plain thread with no SynchronizationContext
    /// and the default TaskScheduler: what the engine's game thread is. A test
    /// thread is not one (xUnit's scheduler moves continuations elsewhere).
    /// </summary>
    private static void OnGameThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "the game thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException("on the game thread: " + failure);
    }

    [Fact]
    public void Work_runs_on_the_sdk_thread_awaits_stay_there_and_the_result_lands_on_the_game_thread()
    {
        int ranOn = -1, resumedOn = -1, continuedOn = -1, gameThread = -1, sdkThread = -1;

        OnGameThread(() =>
        {
            var game = new GameThread();
            using var link = Link(game);
            link.StartThread();
            gameThread = Environment.CurrentManagedThreadId;
            sdkThread = link.SdkThreadId!.Value;

            async Task Caller()
            {
                await link.OnSdkThread(async () =>
                {
                    ranOn = Environment.CurrentManagedThreadId;
                    await Task.Delay(5);                              // completes on the thread pool...
                    resumedOn = Environment.CurrentManagedThreadId;   // ...and resumes on the SDK thread
                    return 1;
                });
                continuedOn = Environment.CurrentManagedThreadId;
            }

            var call = Caller();
            RunFramesUntil(game, () => call.IsCompleted);
            call.GetAwaiter().GetResult();
        });

        Assert.Equal(sdkThread, ranOn);
        Assert.Equal(sdkThread, resumedOn);
        Assert.NotEqual(gameThread, ranOn);
        Assert.Equal(gameThread, continuedOn);
    }

    [Fact]
    public void A_throw_on_the_sdk_thread_faults_the_task_on_the_game_thread()
    {
        var game = new GameThread();
        using var link = Link(game);
        link.StartThread();

        var call = link.OnSdkThread<int>(() => throw new InvalidOperationException("sdk said no"));
        RunFramesUntil(game, () => call.IsCompleted);

        Assert.Equal("sdk said no", Assert.Throws<AggregateException>(() => call.Wait()).InnerException!.Message);
    }

    [Fact]
    public void Stop_waits_for_its_async_last_step_to_finish()
    {
        var thread = new SdkThread("test-sdk", TimeSpan.FromMilliseconds(5), () => { });
        thread.Start();
        bool finished = false;

        bool stopped = thread.Stop(async () =>
        {
            await Task.Delay(50);
            finished = true;
        }, TimeSpan.FromSeconds(5));

        Assert.True(stopped);
        Assert.True(finished);
    }

    //-------------------------------------------------------------------------
    // The pump follows the game thread's liveness
    //-------------------------------------------------------------------------

    [Fact]
    public void A_stamp_older_than_the_stall_limit_stops_the_pump_and_a_fresh_one_restarts_it()
    {
        long now = 0;
        var game = new GameThread(() => now);
        long clock = 0;
        var link = new HostLink(new CommandLine(), new Env(), game, game, new NullLog(), new PathModuleSource(new Mod()), new NoHooks<IHostConnectionHook>(),
            new NoHooks<IHostCommandHook>(), new NoHooks<ILeaseHook>(), new NoHooks<IHopReadyHold>())
        {
            Clock = () => clock,
        };

        Assert.True(link.GameThreadAlive());        // no frame yet: a first map load

        now = 1_000;
        game.RunFrame(new NullLog());
        clock = 1_000 + 14_999;
        Assert.True(link.GameThreadAlive());

        clock = 1_000 + 15_000;
        Assert.False(link.GameThreadAlive());       // 15 s without a frame: hung

        now = clock;
        game.RunFrame(new NullLog());
        Assert.True(link.GameThreadAlive());
    }

    //-------------------------------------------------------------------------
    // The modules declared at Booting (SDK 2.3.0).
    //-------------------------------------------------------------------------

    private sealed class Build : IGameBuild
    {
        public string Managed => "m1 main@abc";
        public string Native => "n1 main@abc";
    }

    [Fact]
    public void The_mod_and_the_native_build_are_declared_with_their_versions()
    {
        var declared = HostLink.DeclaredModules(new Mod(), new Build()).ToList();

        Assert.Equal(2, declared.Count);
        Assert.Contains(declared, m => m.Kind == "mod" && m.Name == new Mod().Id && m.Version == "m1 main@abc");
        Assert.Contains(declared, m => m.Kind == "engine" && m.Version == "n1 main@abc");
    }

    [Fact]
    public void The_mods_browser_block_becomes_the_sdks_field_for_field()
    {
        var mod = new ModBrowser("Descent ({levels_open} open)", "TF2 Descent", "rpg,coop", "eu", 24, "depth {deepest}",
                                 new Dictionary<string, string> { ["players_by_level"] = "{players_by_level}" });

        var block = HostLink.BrowserBlockOf(mod);

        Assert.Equal(new BrowserBlock("Descent ({levels_open} open)", "TF2 Descent", "rpg,coop", "eu", 24, "depth {deepest}", block.Rules), block);
        Assert.Equal("{players_by_level}", Assert.Single(block.Rules!).Value);
        Assert.Equal("players_by_level", Assert.Single(block.Rules!).Key);
    }

    [Fact]
    public void A_link_given_the_mod_and_its_build_hands_the_sdk_the_declaration()
    {
        var game = new GameThread();
        var withBuild = new HostLink(new CommandLine(), new Env(), game, game, new NullLog(),
                                     new PathModuleSource(new Mod()), new NoHooks<IHostConnectionHook>(),
            new NoHooks<IHostCommandHook>(), new NoHooks<ILeaseHook>(), new NoHooks<IHopReadyHold>(),
                                     mod: new Mod(), build: new Build());
        var without = new HostLink(new CommandLine(), new Env(), game, game, new NullLog(),
                                   new PathModuleSource(new Mod()), new NoHooks<IHostConnectionHook>(),
            new NoHooks<IHostCommandHook>(), new NoHooks<ILeaseHook>(), new NoHooks<IHopReadyHold>());

        Assert.NotNull(withBuild.Options.DeclaredModules);
        Assert.Equal(2, withBuild.Options.DeclaredModules!().Count());
        Assert.Null(without.Options.DeclaredModules);
    }

    //-------------------------------------------------------------------------
    // The pending Reauths (SDK 2.6.0): what HostLink.SubmitTicket submits.
    //-------------------------------------------------------------------------

    private static ReauthRequest Request(string nonce, List<string> submitted, TicketOutcome answer = TicketOutcome.Ok, string steamId = "76561197960287930") =>
        new("c-" + nonce, steamId, 90071992547409920UL, nonce, DateTimeOffset.UnixEpoch, "127.0.0.2:27005", ticket =>
        {
            submitted.Add($"{nonce} {Convert.ToHexString(ticket)}");
            return Task.FromResult(HostResult<TicketOutcome>.Success(answer));
        });

    [Fact]
    public async Task A_ticket_is_submitted_once_under_the_pending_nonce_and_the_hosts_verdict_returned()
    {
        var submitted = new List<string>();
        var pending = new PendingReauths();
        pending.Add(Request("n1", submitted, TicketOutcome.Denied));

        var first = await pending.Submit("76561197960287930", "n1", [0xAB]);
        var again = await pending.Submit("76561197960287930", "n1", [0xAB]);

        Assert.Equal(TicketOutcome.Denied, first.Outcome);
        Assert.Equal(TicketOutcome.Stale, again.Outcome);
        Assert.Equal(["n1 AB"], submitted);
    }

    [Fact]
    public async Task A_superseded_nonce_another_player_and_a_forgotten_one_are_stale_and_never_submitted()
    {
        var submitted = new List<string>();
        var pending = new PendingReauths();
        pending.Add(Request("n1", submitted));
        pending.Add(Request("n2", submitted));
        pending.Add(Request("m1", submitted, steamId: "76561197960287931"));
        pending.Forget("76561197960287931");

        Assert.Equal(TicketOutcome.Stale, (await pending.Submit("76561197960287930", "n1", [1])).Outcome);
        Assert.Equal(TicketOutcome.Stale, (await pending.Submit("76561197960287999", "n2", [1])).Outcome);
        Assert.Equal(TicketOutcome.Stale, (await pending.Submit("76561197960287931", "m1", [1])).Outcome);
        Assert.Empty(submitted);

        Assert.True((await pending.Submit("76561197960287930", "n2", [2])).Ok);
        Assert.Equal(["n2 02"], submitted);
    }

    [Fact]
    public async Task A_refused_submit_carries_the_refusal()
    {
        var pending = new PendingReauths();
        pending.Add(new ReauthRequest("c", "76561197960287930", 1, "n1", DateTimeOffset.UnixEpoch, "p",
            _ => Task.FromResult(HostResult<TicketOutcome>.Refused(new HostRefusal(HostError.Unreachable, "down", "")))));

        var answer = await pending.Submit("76561197960287930", "n1", [1]);

        Assert.Null(answer.Outcome);
        Assert.Contains("down", answer.Refusal);
    }

    private sealed class HoldOrder : IHopReadyHold
    {
        private readonly List<string> _order;
        private readonly string _name;
        private readonly Func<Task>? _wait;
        public HoldOrder(List<string> order, string name, Func<Task>? wait = null)
        {
            (_order, _name, _wait) = (order, name, wait);
        }
        public async Task BeforeReady(string steamId)
        {
            if (_wait is not null)
                await _wait();
            _order.Add(_name);
        }
    }

    [Fact]
    public async Task The_hops_Ready_waits_behind_every_hold()
    {
        // The seam plan §4 M2.3 is judged at: the holds run, in order, and
        // Ready is only asked after the last of them.
        var order = new List<string>();
        var gate = new TaskCompletionSource();
        var h1 = new HoldOrder(order, "hold a");
        var h2 = new HoldOrder(order, "hold b", () => gate.Task);

        var ready = HostLink.ReadyAfterHold([h1, h2], "76561197960287930",
            async () => { order.Add("ready"); await Task.Yield(); return HostResult.Success; }, new NullLog());

        Assert.False(ready.IsCompleted);        // Ready sits behind the second hold
        gate.SetResult();
        Assert.True((await ready).Ok);
        Assert.Equal(["hold a", "hold b", "ready"], order);
    }

    [Fact]
    public async Task A_hold_that_throws_does_not_stop_Ready()
    {
        // The other half of the seam: a broken hold costs the screen, never
        // the acknowledgement — a service waiting on a Ready that never comes
        // is the worse failure.
        var order = new List<string>();
        var broken = new HoldOrder(order, "broken", () => throw new InvalidOperationException("the screen died"));

        var ready = HostLink.ReadyAfterHold([broken, new HoldOrder(order, "good")], "76561197960287930",
            async () => { order.Add("ready"); await Task.Yield(); return HostResult.Success; }, new NullLog());

        Assert.True((await ready).Ok);
        Assert.Equal(["good", "ready"], order);   // the throw was logged, the rest ran
    }
}
