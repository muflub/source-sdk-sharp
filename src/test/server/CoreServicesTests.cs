using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The generic core services a mod reaches by injection (plan_host_sdk §2.8):
/// the game-thread queue and frame stamp, frame-thread delays, the mod's
/// identity, and hook dispatch.
/// </summary>
public sealed class CoreServicesTests
{
    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    //-------------------------------------------------------------------------
    // GameThread
    //-------------------------------------------------------------------------

    [Fact]
    public void RunFrame_stamps_the_frame_and_runs_work_posted_from_another_thread()
    {
        long now = 0;
        var thread = new GameThread(() => now);
        int ranOn = -1;
        var poster = new Thread(() => thread.Post(() => ranOn = Environment.CurrentManagedThreadId));
        poster.Start();
        poster.Join();

        Assert.Equal(0, thread.LastFrameMs);
        now = 1234;
        int ran = thread.RunFrame(new ListLog());

        Assert.Equal(1, ran);
        Assert.Equal(1234, thread.LastFrameMs);
        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
        Assert.Equal(0, thread.Backlog);
    }

    [Fact]
    public void RunFrame_stops_at_its_budget_and_keeps_the_rest_for_the_next_frame()
    {
        var thread = new GameThread { Budget = TimeSpan.Zero };
        int ran = 0;
        for (int i = 0; i < 3; i++)
            thread.Post(() => ran++);

        thread.RunFrame(new ListLog());

        Assert.Equal(1, ran);          // a zero budget still runs one item
        Assert.Equal(2, thread.Backlog);
    }

    [Fact]
    public void A_throwing_item_is_logged_and_the_rest_still_run()
    {
        // A budget this fact cannot exhaust. It is about the throw, not the
        // budget: with the default 4 ms a first-chance exception and its log
        // line, under a loaded box, can use the frame up and leave the second
        // item for the next one (seen in a full make check, engineapi lane).
        var thread = new GameThread { Budget = TimeSpan.FromMinutes(1) };
        var log = new ListLog();
        bool second = false;
        thread.Post(() => throw new InvalidOperationException("boom"));
        thread.Post(() => second = true);

        thread.RunFrame(log);

        Assert.True(second);
        Assert.Contains(log.Lines, l => l.Contains("boom"));
    }

    [Fact]
    public void A_throwing_item_that_spends_the_budget_leaves_the_rest_for_the_next_frame()
    {
        // The mechanism behind the flake above, made deterministic: an
        // exhausted budget defers the rest; it does not drop it.
        var thread = new GameThread { Budget = TimeSpan.Zero };
        bool second = false;
        thread.Post(() => throw new InvalidOperationException("boom"));
        thread.Post(() => second = true);

        thread.RunFrame(new ListLog());
        Assert.False(second);

        thread.RunFrame(new ListLog());
        Assert.True(second);
    }

    //-------------------------------------------------------------------------
    // FrameScheduler
    //-------------------------------------------------------------------------

    [Fact]
    public void A_delay_completes_in_the_first_tick_at_or_after_its_due_time()
    {
        long now = 100;
        var scheduler = new FrameScheduler(() => now);
        var delay = scheduler.Delay(TimeSpan.FromMilliseconds(50));

        now = 149;
        scheduler.Tick();
        Assert.False(delay.IsCompleted);

        now = 150;
        Assert.Equal(1, scheduler.Tick());
        Assert.True(delay.IsCompletedSuccessfully);
    }

    [Fact]
    public void An_awaiting_continuation_runs_inside_Tick_on_the_ticking_thread()
    {
        // On a plain thread: no SynchronizationContext and the default
        // TaskScheduler, which is what the engine's game thread is. xUnit's own
        // scheduler would move the continuation off the test thread.
        int tickingThread = -1, continuedOn = -1;
        bool insideTick = false, sawInsideTick = false;
        Exception? failure = null;

        var game = new Thread(() =>
        {
            try
            {
                long now = 0;
                var scheduler = new FrameScheduler(() => now);

                async Task Wait()
                {
                    await scheduler.Delay(TimeSpan.FromMilliseconds(1));
                    continuedOn = Environment.CurrentManagedThreadId;
                    sawInsideTick = insideTick;
                }

                tickingThread = Environment.CurrentManagedThreadId;
                var waiting = Wait();
                now = 5;
                insideTick = true;
                scheduler.Tick();
                insideTick = false;
                waiting.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        game.Start();
        Assert.True(game.Join(TimeSpan.FromSeconds(10)));

        Assert.Null(failure);
        Assert.Equal(tickingThread, continuedOn);
        Assert.True(sawInsideTick, "the continuation ran inside Tick, not after it");
    }

    [Fact]
    public void A_cancelled_delay_is_cancelled_and_leaves_the_queue_at_the_next_tick()
    {
        var scheduler = new FrameScheduler(() => 0);
        using var cts = new CancellationTokenSource();
        var delay = scheduler.Delay(TimeSpan.FromHours(1), cts.Token);

        cts.Cancel();

        Assert.True(delay.IsCanceled);
        scheduler.Tick();
        Assert.Equal(0, scheduler.Waiting);
    }

    //-------------------------------------------------------------------------
    // ModInfo
    //-------------------------------------------------------------------------

    [Fact]
    public void ModInfo_reads_what_Bootstrap_published()
    {
        var data = new Dictionary<string, object?>
        {
            [ModInfo.Keys.Id] = "tf2_descent",
            [ModInfo.Keys.GameDir] = "/game/mods/tf2_descent",
            [ModInfo.Keys.ManagedDir] = "/game/bin/managed",
            [ModInfo.Keys.ServerAssemblies] = "SourceSharp.Server;TF2.Descent.Server",
            [ModInfo.Keys.ClientAssemblies] = "SourceSharp.Client;TF2.Descent.Client",
        };
        var info = new ModInfo(k => data.GetValueOrDefault(k));

        Assert.Equal("tf2_descent", info.Id);
        Assert.Equal("/game/mods/tf2_descent", info.GameDir);
        Assert.Equal("/game/bin/managed", info.ManagedDir);
        Assert.Equal(["SourceSharp.Server", "TF2.Descent.Server"], info.AssembliesFor(Side.Server));
        Assert.Equal(["SourceSharp.Client", "TF2.Descent.Client"], info.AssembliesFor(Side.Client));
    }

    [Fact]
    public void ModInfo_names_a_key_Bootstrap_never_published()
    {
        var info = new ModInfo(_ => null);

        var e = Assert.Throws<InvalidOperationException>(() => info.GameDir);
        Assert.Contains(ModInfo.Keys.GameDir, e.Message);
    }

    //-------------------------------------------------------------------------
    // Hooks<T>
    //-------------------------------------------------------------------------

    public interface IPing { void Ping(List<string> seen); }

    public sealed class Late : IPing, IOrderedHook
    {
        public int HookOrder => 20;
        public void Ping(List<string> seen) => seen.Add("late");
    }

    public sealed class Early : IPing, IOrderedHook
    {
        public int HookOrder => 10;
        public void Ping(List<string> seen) => seen.Add("early");
    }

    public sealed class Throws : IPing, IOrderedHook
    {
        public int HookOrder => 15;
        public void Ping(List<string> seen) => throw new InvalidOperationException("ping failed");
    }

    public sealed class AlsoTen : IPing, IOrderedHook
    {
        public int HookOrder => 10;
        public void Ping(List<string> seen) { }
    }

    private static ServiceContainer With(params Type[] hooks)
    {
        var c = new ServiceContainer();
        foreach (var h in hooks)
            c.Add(h, h);
        return c;
    }

    [Fact]
    public void Hooks_calls_every_registered_implementation_in_order_and_isolates_a_throw()
    {
        var log = new ListLog();
        var hooks = new Hooks<IPing>(With(typeof(Late), typeof(Throws), typeof(Early)), log);
        var seen = new List<string>();

        hooks.Each("Ping", h => h.Ping(seen));

        Assert.Equal(["early", "late"], seen);
        Assert.Contains(log.Lines, l => l.Contains("ping failed") && l.Contains(nameof(Throws)));
    }

    [Fact]
    public void Hooks_refuses_two_implementations_at_one_order_naming_both()
    {
        var hooks = new Hooks<IPing>(With(typeof(Early), typeof(AlsoTen)), new ListLog());

        var e = Assert.ThrowsAny<Exception>(() => hooks.All);
        Assert.Contains(nameof(Early), e.Message);
        Assert.Contains(nameof(AlsoTen), e.Message);
    }

    [Fact]
    public void Hooks_resolves_lazily_so_a_hook_registered_after_it_is_found()
    {
        var c = new ServiceContainer();
        var hooks = new Hooks<IPing>(c, new ListLog());
        c.Add<Early>();

        Assert.Single(hooks.All);
    }
}
