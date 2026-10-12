using System.Text.Json;

using Debug.Api;
using SourceSharp.Server;
using SourceSharp.Server.Interface;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The exit record (debug-api lane, item 5): native records who asked the
/// process to end; ExitRecord decides which fact was the reason.
/// </summary>
public sealed class DebugApiExitRecordTests : IDisposable
{
    private sealed class FakeProcess : IDebugProcess
    {
        public DebugExitFacts Facts { get; set; } = Armed();
        public List<string> Armings { get; } = [];
        public int Disarms { get; private set; }

        /// <summary>Reads after the last Disarm: a record written then would be facts from a dead recorder.</summary>
        public int ReadsSinceDisarm { get; private set; }

        public void Arm(string recordPath) => Armings.Add(recordPath);
        public DebugExitFacts Read() { ReadsSinceDisarm++; return Facts; }
        public void Disarm() { Disarms++; ReadsSinceDisarm = 0; }
    }

    private static DebugExitFacts Armed(int nowMs = 1000) => DebugProcess.NoFacts with { Armed = true, SignalsArmed = true, NowMs = nowMs };

    private static string? NoSender(int pid) => null;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ss-exit-{Guid.NewGuid():N}");
    private string RecordPath => Path.Combine(_dir, "1234.exit.json");

    public DebugApiExitRecordTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    //-------------------------------------------------------------------------
    // What counts as a quit.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("quit", true)]
    [InlineData("exit", true)]
    [InlineData("QUIT", true)]
    [InlineData("echo a; quit", true)]
    [InlineData("quit_prompt", false)]
    [InlineData("say quit", false)]
    [InlineData("status", false)]
    public void A_quit_command_is_quit_or_exit_as_any_statement(string command, bool quits)
        => Assert.Equal(quits, ExitRecord.IsQuitCommand(command));

    [Theory]
    [InlineData("] quit", true)]
    [InlineData("] exit\n", true)]
    [InlineData("quit", false)]
    [InlineData("] say quit", false)]
    public void A_typed_quit_is_the_consoles_echo_of_one(string line, bool typed)
        => Assert.Equal(typed, ExitRecord.IsTypedQuit(line));

    //-------------------------------------------------------------------------
    // The decision.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_quit_sent_through_the_api_is_an_api_command()
    {
        var (reason, detail) = ExitRecord.Decide(Armed(), new QuitRequest("quit", 7, 400), null, NoSender);
        Assert.Equal(ExitRecord.ApiCommand, reason);
        Assert.Contains("\"requestSeq\":7", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signal_names_its_number_and_sender()
    {
        var facts = Armed() with { Signals = 1, FirstSigno = 15, FirstPid = 4242, FirstUid = 1000, FirstAtMs = 300 };
        var (reason, detail) = ExitRecord.Decide(facts, null, null, pid => pid == 4242 ? "bash run-it.sh" : null);
        Assert.Equal(ExitRecord.Signal, reason);
        using var doc = JsonDocument.Parse(detail);
        Assert.Equal("SIGTERM", doc.RootElement.GetProperty("signal").GetString());
        Assert.Equal(4242, doc.RootElement.GetProperty("senderPid").GetInt32());
        Assert.Equal("bash run-it.sh", doc.RootElement.GetProperty("sender").GetString());
    }

    [Fact]
    public void An_api_quit_before_a_signal_is_the_reason()
    {
        var facts = Armed() with { Signals = 1, FirstSigno = 15, FirstPid = 1, FirstAtMs = 500 };
        Assert.Equal(ExitRecord.ApiCommand, ExitRecord.Decide(facts, new QuitRequest("quit", 1, 400), null, NoSender).Reason);
    }

    [Fact]
    public void A_signal_before_an_api_quit_is_the_reason()
    {
        var facts = Armed() with { Signals = 1, FirstSigno = 15, FirstPid = 1, FirstAtMs = 300 };
        Assert.Equal(ExitRecord.Signal, ExitRecord.Decide(facts, new QuitRequest("quit", 1, 400), null, NoSender).Reason);
    }

    [Fact]
    public void A_window_close_is_the_reason_rather_than_the_sdl_quit_it_causes()
    {
        var facts = Armed() with { WindowCloses = 1, WindowCloseAtMs = 200, SdlQuits = 1, SdlQuitAtMs = 210 };
        Assert.Equal(ExitRecord.WindowClose, ExitRecord.Decide(facts, null, null, NoSender).Reason);
    }

    [Fact]
    public void A_signal_is_the_reason_rather_than_the_sdl_quit_it_causes()
    {
        var facts = Armed() with { Signals = 1, FirstSigno = 15, FirstAtMs = 100, SdlQuits = 1, SdlQuitAtMs = 110 };
        Assert.Equal(ExitRecord.Signal, ExitRecord.Decide(facts, null, null, NoSender).Reason);
    }

    [Fact]
    public void A_typed_quit_with_nothing_timed_is_the_console()
    {
        var (reason, detail) = ExitRecord.Decide(Armed(), null, "] quit", NoSender);
        Assert.Equal(ExitRecord.Console, reason);
        Assert.Contains("] quit", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_sdl_quit_alone_is_reported_as_one()
        => Assert.Equal(ExitRecord.SdlQuit, ExitRecord.Decide(Armed() with { SdlQuits = 1 }, null, null, NoSender).Reason);

    [Fact]
    public void Nothing_recorded_is_a_shutdown_nobody_asked_for()
        => Assert.Equal(ExitRecord.Shutdown, ExitRecord.Decide(Armed(), null, null, NoSender).Reason);

    [Fact]
    public void Natives_facts_are_named_in_exit_watch_order()
    {
        int[] raw = [1, 3, 900, 2, 15, 4242, 1000, 120, 2, 77, 0, 130, 1, 140, 0, 0, 2, 0, 1];
        var facts = DebugProcess.FromFacts(raw);
        Assert.True(facts.SdlArmed);
        Assert.Equal(4242, facts.FirstPid);
        Assert.Equal(77, facts.LastPid);
        Assert.Equal(140, facts.SdlQuitAtMs);
        Assert.Equal(1, facts.PrevHup);
    }

    //-------------------------------------------------------------------------
    // The server: /status's quit, and the file.
    //-------------------------------------------------------------------------

    private (DebugApiServer Server, FakeProcess Process) Serve(bool start)
    {
        var process = new FakeProcess();
        var server = new DebugApiServer(start ? ["-debugapisocket"] : [], TestDebugHooks.Server(
            world: new FakeDebugWorld(), process: process,
            console: new DebugConsoleHook(() => [], () => [], _ => null, () => [], _ => null, _ => { })), TestDebugApiServers.OwnSocket())
        {
            ExitRecordPathOverride = RecordPath,
        };

        if (start)
            Assert.True(server.Start(), "the test server could not listen");
        return (server, process);
    }

    private static JsonElement Quit(DebugApiServer server)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, "/status").Body);
        return doc.RootElement.GetProperty("quit").Clone();
    }

    [Fact]
    public void Status_says_no_quit_until_one_is_asked_for()
    {
        var (server, _) = Serve(start: false);
        using var _s = server;
        Assert.Equal(JsonValueKind.Null, Quit(server).ValueKind);
    }

    [Fact]
    public void Status_names_a_quit_sent_through_the_api_as_soon_as_it_is_sent()
    {
        var (server, _) = Serve(start: false);
        using var _s = server;
        _ = DebugApiGolden.Pumped(server, "/command?c=quit");
        Assert.Equal(ExitRecord.ApiCommand, Quit(server).GetProperty("reason").GetString());
    }

    [Fact]
    public void Status_names_a_signal_with_its_sender()
    {
        var (server, process) = Serve(start: false);
        using var _s = server;
        process.Facts = Armed() with { Signals = 1, FirstSigno = 15, FirstPid = 4242, FirstAtMs = 10 };
        var quit = Quit(server);
        Assert.Equal(ExitRecord.Signal, quit.GetProperty("reason").GetString());
        Assert.Equal(4242, quit.GetProperty("detail").GetProperty("senderPid").GetInt32());
    }

    [Fact]
    public void Starting_arms_the_recorders_with_the_record_path()
    {
        var (server, process) = Serve(start: true);
        using var _s = server;
        Assert.Equal([RecordPath], process.Armings);
    }

    [Fact]
    public void Disposing_a_started_server_writes_the_record()
    {
        var (server, process) = Serve(start: true);
        process.Facts = Armed() with { Signals = 1, FirstSigno = 15, FirstPid = 4242, FirstAtMs = 10 };
        server.Dispose();

        using var doc = JsonDocument.Parse(File.ReadAllText(RecordPath));
        Assert.Equal("signal", doc.RootElement.GetProperty("reason").GetString());
        Assert.Equal(Environment.ProcessId, doc.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal("managed shutdown", doc.RootElement.GetProperty("writtenBy").GetString());
    }

    // The SIGTERM-exits-139 fix's managed half (docs/todo.md): a recorder left
    // installed when the module is unmapped is a jump into freed code on the
    // game's last SDL pump. Dispose must take it back out, AFTER the record
    // is written from facts read while it was still live.
    [Fact]
    public void Disposing_a_started_server_disarms_the_recorders_after_the_record()
    {
        var (server, process) = Serve(start: true);
        server.Dispose();

        Assert.Equal(1, process.Disarms);
        Assert.Equal(0, process.ReadsSinceDisarm);   // nothing read the facts after disarming
        Assert.True(File.Exists(RecordPath), "the record must be written before the disarm");
    }

    [Fact]
    public void A_server_that_never_started_disarms_nothing()
    {
        var (server, process) = Serve(start: false);
        server.Dispose();
        Assert.Equal(0, process.Disarms);
    }

    [Fact]
    public void The_record_carries_the_recent_api_commands()
    {
        var (server, _) = Serve(start: true);
        _ = DebugApiGolden.Pumped(server, "/command?c=status");
        _ = DebugApiGolden.Pumped(server, "/command?c=quit");
        server.Dispose();

        using var doc = JsonDocument.Parse(File.ReadAllText(RecordPath));
        var commands = doc.RootElement.GetProperty("recentCommands").EnumerateArray()
                          .Select(c => c.GetProperty("command").GetString()!).ToArray();
        Assert.Equal(["status", "quit"], commands);
        Assert.Equal("api-command", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Starting_removes_a_record_a_reloaded_module_left()
    {
        File.WriteAllText(RecordPath, "{\"reason\":\"shutdown\"}");
        var (server, _) = Serve(start: true);
        using var _s = server;
        Assert.False(File.Exists(RecordPath), "a reload's record outlived the reload");
    }

    [Fact]
    public void A_server_that_never_started_writes_no_record()
    {
        var (server, _) = Serve(start: false);
        server.Dispose();
        Assert.False(File.Exists(RecordPath));
    }
}
