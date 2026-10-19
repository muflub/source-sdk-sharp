using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using Debug.Api;
using SourceSharp.Server;
using Debug.Api.Server.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /wait: a check the game thread runs once per server tick, answering the
/// request that made it (debug-api lane, item 1). The test is the game thread
/// here: it moves the tick and pumps.
/// </summary>
public class DebugApiWaitTests
{
    /// <summary>
    /// A request on a thread of its own, as a long-poll session runs one. Not a
    /// Task: the test must block on it while being the game thread itself.
    /// </summary>
    private sealed class Pending<T>
    {
        private readonly ManualResetEventSlim _done = new(false);
        private T? _result;
        private readonly Func<string>? _progress;

        /// <param name="progress">Where the work had got to, for the message when it never finishes.</param>
        public Pending(Func<T> work, Func<string>? progress = null)
        {
            _progress = progress;
            new Thread(() => { _result = work(); _done.Set(); }) { IsBackground = true }.Start();
        }

        public bool IsCompleted => _done.IsSet;
        public bool Wait(int milliseconds) => _done.Wait(milliseconds);
        public bool Wait(TimeSpan timeout) => _done.Wait(timeout);
        public T Result => _done.Wait(TimeSpan.FromSeconds(10))
            ? _result!
            : throw new TimeoutException(_progress is null ? "no reply" : "no reply; the request got as far as: " + _progress());
    }

    private sealed class Rig : IDisposable
    {
        public FakeDebugWorld World { get; } = new() { Tick = 100 };
        public DebugApiServer Server { get; }
        public int IoPending { get; set; }

        public Rig()
        {
            Server = DebugApiServer.ForTest(TestDebugHooks.Server(
                world: World,
                counters: () => DebugStatus.ZeroCounters() with
                {
                    Io = DebugStatus.ZeroCounters().Io with { Pending = IoPending },
                }));
        }

        /// <summary>Starts a request on its own thread, as a long-poll session would, and waits until it is registered.</summary>
        public Pending<HttpReply> Start(string path)
        {
            int before = Server.WaitsInFlight;
            var reply = new Pending<HttpReply>(() => DebugApiGolden.Get(Server, path));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Server.WaitsInFlight == before && !reply.IsCompleted && DateTime.UtcNow < deadline)
                Thread.Sleep(1);
            return reply;
        }

        /// <summary>One game frame: the managed frame's pump and the engine think's, at a new tick.</summary>
        public void Frame(Action<FakeDebugWorld>? change = null)
        {
            World.Tick++;
            change?.Invoke(World);
            Server.Pump();
            Server.Pump();
            World.EndFrame();
        }

        /// <summary>Runs frames until the reply comes, or the limit; the reply.</summary>
        public JsonElement Drive(Pending<HttpReply> reply, int frames = 50, Action<int, FakeDebugWorld>? change = null)
        {
            Server.Pump(); // the registration tick: its first evaluation
            for (int i = 1; i <= frames && !reply.Wait(5); i++)
                Frame(w => change?.Invoke(i, w));

            Assert.True(reply.Wait(TimeSpan.FromSeconds(5)), "the wait never answered");
            return Parse(reply.Result);
        }

        public static JsonElement Parse(HttpReply reply)
        {
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement.Clone();
            return root;
        }

        public void Dispose() => Server.Dispose();
    }

    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;
    private static int WaitedTicks(JsonElement e) => e.GetProperty("waitedTicks").GetInt32();

    [Fact]
    public void Ticks_resolves_after_exactly_that_many_server_ticks()
    {
        using var rig = new Rig();
        var e = rig.Drive(rig.Start("/wait?ticks=3"));
        Assert.Equal("met", Status(e));
        Assert.Equal(3, WaitedTicks(e));
        Assert.Equal(103, e.GetProperty("tick").GetInt32());
    }

    [Fact]
    public void Pumps_without_a_tick_do_not_count()
    {
        using var rig = new Rig();
        var reply = rig.Start("/wait?ticks=1");
        for (int i = 0; i < 20; i++)
            rig.Server.Pump();

        Assert.False(reply.Wait(50), "a wait counted pumps, not ticks");
        Assert.Equal(1, WaitedTicks(rig.Drive(reply)));
    }

    [Fact]
    public void A_field_wait_is_met_on_the_tick_the_field_matches()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40, name: "crate");
        var e = rig.Drive(rig.Start("/wait?field=origin.z&name=crate&op=lt&value=-5"),
                          change: (i, w) => { if (i == 4) w[0x10028u].Origin = new DebugVec3(0, 0, -10); });
        Assert.Equal("met", Status(e));
        Assert.Equal(4, WaitedTicks(e));
        Assert.Equal("-10", e.GetProperty("value").GetString());
    }

    [Fact]
    public void A_condition_true_at_the_start_is_met_without_waiting_a_tick()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40).Health = 30;
        var e = rig.Drive(rig.Start("/wait?field=health&index=40&op=le&value=50"));
        Assert.Equal(0, WaitedTicks(e));
    }

    [Fact]
    public void A_field_wait_that_never_matches_times_out_at_its_tick_budget()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40);
        var reply = rig.Start("/wait?field=health&index=40&op=eq&value=1&timeout=4");
        var e = rig.Drive(reply);
        Assert.Equal("timeout", Status(e));
        Assert.Equal(4, WaitedTicks(e));
        Assert.Equal(408, reply.Result.Status);
    }

    [Fact]
    public void A_game_that_runs_no_ticks_hits_the_wall_clock_cap_and_says_so()
    {
        using var rig = new Rig();
        var reply = rig.Start("/wait?ticks=5&wall=0.05");
        Assert.True(reply.Wait(TimeSpan.FromSeconds(5)), "the wall cap did not end the wait");
        var e = Rig.Parse(reply.Result);
        Assert.Equal("wallclock", Status(e));
        Assert.Equal(408, reply.Result.Status);
        Assert.Equal(0, rig.Server.WaitsInFlight);
    }

    [Fact]
    public void Changed_compares_with_the_first_reading()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40).Health = 100;
        var e = rig.Drive(rig.Start("/wait?field=health&index=40&op=changed"),
                          change: (i, w) => { if (i == 3) w[0x10028u].Health = 90; });
        Assert.Equal(3, WaitedTicks(e));
    }

    [Fact]
    public void A_field_wait_on_an_entity_that_is_removed_fails_rather_than_timing_out()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40);
        var reply = rig.Start("/wait?field=health&index=40&op=eq&value=1");
        var e = rig.Drive(reply, change: (i, w) => { if (i == 2) w.Remove(0x10028u); });
        Assert.Equal("failed", Status(e));
        Assert.Equal(409, reply.Result.Status);
    }

    [Fact]
    public void Exists_is_met_when_the_named_entity_appears()
    {
        using var rig = new Rig();
        var e = rig.Drive(rig.Start("/wait?exists=1&name=late"),
                          change: (i, w) => { if (i == 2) w.Add(0x30030u, 48, name: "late"); });
        Assert.Equal(2, WaitedTicks(e));
        Assert.Equal(48, e.GetProperty("value").GetProperty("index").GetInt32());
    }

    [Fact]
    public void Gone_is_met_on_the_tick_the_entity_is_freed()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40, name: "crate");
        var e = rig.Drive(rig.Start("/wait?gone=1&name=crate"),
                          change: (i, w) => { if (i == 3) w.Remove(0x10028u); });
        // Removed during frame 3 and freed at its end; seen gone at frame 4.
        Assert.Equal("met", Status(e));
        Assert.Equal(4, WaitedTicks(e));
    }

    [Fact]
    public void Gone_for_something_absent_at_the_start_is_met_at_once_and_says_so()
    {
        using var rig = new Rig();
        var e = rig.Drive(rig.Start("/wait?gone=1&name=nothing"));
        Assert.Equal(0, WaitedTicks(e));
        Assert.Equal("absent at the start", e.GetProperty("value").GetString());
    }

    [Fact]
    public void An_event_wait_ignores_events_before_it_was_made_and_answers_the_matching_seq()
    {
        using var rig = new Rig();
        var log = rig.Server.EventsForTest;
        log.Add(EntityEventKind.Output, "trigger_multiple", "OnStartTouch", "fired", 1f, 41, 0x20029u); // seq 0, before
        var e = rig.Drive(rig.Start("/wait?event=output&name=OnStartTouch"),
                          change: (i, _) =>
                          {
                              if (i == 2) log.Add(EntityEventKind.Output, "func_door", "OnOpen", "fired", 2f);       // seq 1
                              if (i == 3) log.Add(EntityEventKind.Output, "trigger_multiple", "OnStartTouch", "fired", 3f); // seq 2
                          });
        Assert.Equal(3, WaitedTicks(e));
        Assert.Equal(2, e.GetProperty("seq").GetInt64());
    }

    [Fact]
    public void An_event_wait_from_an_earlier_since_sees_what_already_happened()
    {
        using var rig = new Rig();
        rig.Server.EventsForTest.Add(EntityEventKind.Removed, "prop_physics", "", "gone", 1f, -1, 0x10028u);
        var e = rig.Drive(rig.Start($"/wait?event=removed&handle={0x10028u}&since=0"));
        Assert.Equal(0, WaitedTicks(e));
        Assert.Equal(0, e.GetProperty("seq").GetInt64());
    }

    [Fact]
    public void Settled_needs_the_speed_under_eps_for_the_named_number_of_ticks()
    {
        using var rig = new Rig();
        rig.World.Add(0x10028u, 40).Velocity = new DebugVec3(0, 0, -100);
        var e = rig.Drive(rig.Start("/wait?settled=1&index=40&eps=1&for=3"),
                          change: (i, w) => { if (i == 2) w[0x10028u].Velocity = default; });
        // Still at frames 2, 3 and 4.
        Assert.Equal(4, WaitedTicks(e));
    }

    [Fact]
    public void Settled_is_met_at_once_by_an_object_that_reports_asleep()
    {
        using var rig = new Rig();
        var crate = rig.World.Add(0x10028u, 40);
        crate.Velocity = new DebugVec3(0, 0, -100);
        crate.Asleep = true;
        Assert.Equal(0, WaitedTicks(rig.Drive(rig.Start("/wait?settled=1&index=40"))));
    }

    [Fact]
    public void Census_is_met_when_the_count_reaches_the_target()
    {
        using var rig = new Rig();
        rig.World.Add(1, 1);
        var e = rig.Drive(rig.Start("/wait?census=3&class=prop_physics"),
                          change: (i, w) => { if (i is 2 or 5) w.Add((uint)(100 + i), 10 + i); });
        Assert.Equal(5, WaitedTicks(e));
        Assert.Equal(3, e.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Io_empty_waits_for_the_queue_to_drain()
    {
        using var rig = new Rig { IoPending = 2 };
        var e = rig.Drive(rig.Start("/wait?io=empty"),
                          change: (i, _) => { if (i == 3) rig.IoPending = 0; });
        Assert.Equal(3, WaitedTicks(e));
    }

    [Fact]
    public void Two_conditions_are_refused()
    {
        using var rig = new Rig();
        var reply = DebugApiGolden.Get(rig.Server, "/wait?ticks=3&io=empty");
        Assert.Equal(400, reply.Status);
        Assert.Contains("not ticks and io", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void No_condition_is_refused()
    {
        using var rig = new Rig();
        Assert.Equal(400, DebugApiGolden.Get(rig.Server, "/wait").Status);
    }

    [Fact]
    public void Disposing_the_server_cancels_a_wait_in_flight()
    {
        var rig = new Rig();
        var reply = rig.Start("/wait?ticks=1000&wall=60");
        rig.Dispose();
        Assert.True(reply.Wait(TimeSpan.FromSeconds(2)), "a wait outlived its server");
        Assert.Equal("cancelled", Status(Rig.Parse(reply.Result)));
    }

    [Theory]
    [InlineData("/wait", true)]
    [InlineData("/player/teleport", true)]
    [InlineData("/entities/remove", true)]
    [InlineData("/command?capture=1", true)]
    [InlineData("/command", false)]
    [InlineData("/status", false)]
    public void Only_the_routes_that_wait_are_served_off_the_accept_loop(string path, bool longPoll)
    {
        Assert.True(HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request));
        Assert.Equal(longPoll, DebugApiServer.IsLongPoll(request));
    }

    //-------------------------------------------------------------------------
    // The transport: a long request must not hold the accept loop.
    //-------------------------------------------------------------------------

    // stage: where the request is, for a fact's message if it never comes back
    // (the long-request fact once timed out on a Windows runner with no more
    // to go on than "no reply").
    //
    // The receive timeout is what makes a server that stops answering a failed
    // fact rather than a hung test host: without it a Windows runner sat in
    // Receive until the hang collector aborted the whole run, five minutes
    // later, with no word of what the server had seen.
    private static string Ask(string socketPath, string path, Action<string>? stage = null)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.ReceiveTimeout = 15_000;
        socket.SendTimeout = 15_000;
        stage?.Invoke("connecting");
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        stage?.Invoke("connected, sending");
        socket.Send(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\n\r\n"));
        stage?.Invoke("sent, 0 bytes back");
        var buffer = new byte[4096];
        var all = new StringBuilder();
        int n;
        while ((n = socket.Receive(buffer)) > 0)
        {
            all.Append(Encoding.UTF8.GetString(buffer, 0, n));
            stage?.Invoke($"sent, {all.Length} bytes back and no end of stream: {all}");
        }
        return all.ToString();
    }

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int Dup(int fd);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    [LinuxOnlyFact("dup(2) gives the server's socket a second holder, as a child process's inherited handle does on Windows")]
    public void A_closed_reply_ends_its_stream_while_another_handle_holds_the_socket()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-close-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            Socket served = listener.Accept();
            int other = Dup((int)served.Handle);
            Assert.True(other >= 0, $"dup failed: errno {Marshal.GetLastPInvokeError()}");
            try
            {
                served.Send(Encoding.ASCII.GetBytes("reply"));
                HttpSocketServer.CloseClient(served);

                client.ReceiveTimeout = 2000;
                var buffer = new byte[16];
                Assert.Equal(5, client.Receive(buffer));
                Assert.Equal(0, client.Receive(buffer));
            }
            finally
            {
                CloseFd(other);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_long_request_does_not_hold_the_others()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-wait-{Guid.NewGuid():N}.sock");
        using var release = new ManualResetEventSlim(false);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var http = new HttpSocketServer(new UnixDomainSocketEndPoint(path),
            request => request.Path == "/slow"
                ? (release.Wait(TimeSpan.FromSeconds(10)) ? new HttpReply(200, "{\"slow\":true}") : new HttpReply(500, "{}"))
                : new HttpReply(200, "{\"fast\":true}"),
            errors.Enqueue, request => request.Path == "/slow");
        try
        {
            http.Start();
            string slowStage = "not started";
            var slow = new Pending<string>(() => Ask(path, "/slow", s => Volatile.Write(ref slowStage, s)),
                                           () => Volatile.Read(ref slowStage));
            Thread.Sleep(50);

            string fast;
            try
            {
                fast = Ask(path, "/fast");
            }
            catch (SocketException e)
            {
                throw new Xunit.Sdk.XunitException(
                    $"the fast request got no reply ({e.SocketErrorCode}); the server said: "
                    + (errors.IsEmpty ? "nothing" : string.Join(" | ", errors)));
            }

            Assert.Contains("\"fast\":true", fast, StringComparison.Ordinal);
            Assert.False(slow.IsCompleted, "the slow request was not still waiting");

            release.Set();
            Assert.Contains("\"slow\":true", slow.Result, StringComparison.Ordinal);
        }
        finally
        {
            release.Set();
            http.Dispose();
            File.Delete(path);
        }
    }

    /// <summary>
    /// A failed accept is one connection's failure, not the server's: the loop
    /// says so and keeps answering. It used to return on any exception from
    /// Accept, and every client after that waited in the backlog for a reply
    /// that never came.
    /// </summary>
    [Fact]
    public void A_failed_accept_does_not_stop_the_server()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-wait-{Guid.NewGuid():N}.sock");
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int accepts = 0;
        var http = new HttpSocketServer(new UnixDomainSocketEndPoint(path),
            _ => new HttpReply(200, "{\"ok\":true}"),
            errors.Enqueue, isLongPoll: null,
            accept: listener => Interlocked.Increment(ref accepts) == 1
                ? throw new SocketException((int)SocketError.ConnectionReset)
                : listener.Accept());
        try
        {
            http.Start();

            Assert.Contains("\"ok\":true", Ask(path, "/after"), StringComparison.Ordinal);
            Assert.True(Volatile.Read(ref accepts) >= 2, "the stand-in accept was used, and failed once first");
            Assert.Contains(errors, e => e.StartsWith("accept failed:", StringComparison.Ordinal));
        }
        finally
        {
            http.Dispose();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Closing the listener still ends the loop: Dispose's join returns at once
    /// and reports nothing, so the retry is only for a server that is running.
    /// </summary>
    [Fact]
    public void Dispose_still_ends_the_loop_without_an_error()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-wait-{Guid.NewGuid():N}.sock");
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var http = new HttpSocketServer(new UnixDomainSocketEndPoint(path), _ => new HttpReply(200, "{}"), errors.Enqueue);
        http.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        http.Dispose();
        File.Delete(path);

        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(900), $"Dispose took {clock.Elapsed}");
        Assert.Empty(errors);
    }

    [Fact]
    public void Dispose_joins_the_long_request_threads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ss-wait-{Guid.NewGuid():N}.sock");
        using var release = new ManualResetEventSlim(false);
        var http = new HttpSocketServer(new UnixDomainSocketEndPoint(path),
            _ => { release.Wait(TimeSpan.FromSeconds(10)); return new HttpReply(200, "{}"); },
            _ => { }, _ => true);
        http.Start();
        var slow = new Pending<string>(() => Ask(path, "/slow"));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (http.Sessions == 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(1);
        Assert.Equal(1, http.Sessions);

        release.Set();
        http.Dispose();
        File.Delete(path);
        Assert.Equal(0, http.Sessions);
        Assert.True(slow.Wait(TimeSpan.FromSeconds(5)));
    }
}
