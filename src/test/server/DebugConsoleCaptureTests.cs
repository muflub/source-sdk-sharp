using System.Text.Json;

using Debug.Api;
using SourceSharp.Abi;
using SourceSharp.Server;
using Debug.Api.Server.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /command?capture=1 over a MANAGED command: the dev API builds a FRESH
/// instance of the command class whose log and engine console are a sink,
/// runs that on the game thread, and answers with what the command printed.
/// No echo markers, no file tail, no wait for a closing line — the capture is
/// synchronous because the command is managed code (user ruling 2026-10-07:
/// command capture is all managed logic, no native code). Commands the managed
/// registry does not own keep the old engine-side path: `DebugApiCaptureTests`
/// holds those facts over an unregistered name.
/// </summary>
public sealed class DebugConsoleCaptureTests
{
    private const string TokenName = "ss_capture_token";
    private const string ChattyName = "ss_capture_chatty";
    private const string ArgsName = "ss_capture_args";
    private const string RefusedName = "ss_capture_refused";
    private const string NativeBodiedName = "ss_capture_native_bodied";

    /// <summary>Every line the game's own log saw, so forwarding is observable.</summary>
    private readonly RecordingLog _live = new();

    /// <summary>The command lines the engine-side console was handed.</summary>
    private readonly List<string> _executed = [];

    private sealed record Made(ConCommandRegistry Registry, List<string> Warnings);

    // ---- The commands under test -----------------------------------------
    // Public, and their names are this class's own: the registry's assembly
    // scan is the real registration path, so nothing else may claim these.

    /// <summary>Prints a token minted in its constructor: a reused instance repeats it.</summary>
    [ConCommand(TokenName)]
    public sealed class TokenCommand : IConCommand
    {
        private readonly ILog _log;
        private readonly string _token = Guid.NewGuid().ToString("N")[..8];
        public TokenCommand(ILog log) => _log = log;
        public void Execute(string args) => _log.Msg($"token {_token}\n");
    }

    /// <summary>Prints through both channels a command can print with: its log and the console.</summary>
    [ConCommand(ChattyName)]
    public sealed class ChattyCommand : IConCommand
    {
        private readonly ILog _log;
        private readonly IEngineConsole _console;
        public ChattyCommand(ILog log, IEngineConsole console) => (_log, _console) = (log, console);

        public void Execute(string args)
        {
            _log.Msg("via the log\n");
            _log.Warning("warned\n");
            _console.Msg("via the engine console\n");
            _console.Warning("engine warned\n");
        }
    }

    /// <summary>Reports the raw argument string its Execute was handed.</summary>
    [ConCommand(ArgsName)]
    public sealed class ArgsCommand : IConCommand
    {
        private readonly ILog _log;
        public ArgsCommand(ILog log) => _log = log;
        public void Execute(string args) => _log.Msg($"args[{args}]\n");
    }

    /// <summary>Declared but unbuildable: its dependency is in nobody's container,
    /// so RegisterAll refuses it and the registry owns no runnable instance.</summary>
    [ConCommand(RefusedName)]
    public sealed class RefusedCommand : IConCommand
    {
        public RefusedCommand(INotRegistered dependency) { }
        public void Execute(string args) => throw new InvalidOperationException("must never run");
    }

    /// <summary>A dependency type deliberately absent from every container.</summary>
    public sealed class INotRegistered;

    /// <summary>A console double that registers (outside the game, refused) and records warnings.</summary>
    private sealed class RecordingConsole(List<string> warnings) : IEngineConsole
    {
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => -1;
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => -1;

        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => "";
        public void Msg(string message) => warnings.Add("msg:" + message);
        public void Warning(string message) => warnings.Add("warn:" + message);
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry)
        {
            entry = default;
            return false;
        }
    }

    /// <summary>
    /// The registry the capture reads, filled through the REAL
    /// <see cref="ConCommandRegistry.RegisterAll"/> pass — the same door the
    /// module uses — over an engine that refuses every registration (outside
    /// the game every id is -1, and the registry keeps refused commands, so
    /// the capture still reaches this test's names).
    /// </summary>
    private Made Make()
    {
        var registry = new ConCommandRegistry();
        var warnings = new List<string>();
        registry.RegisterAll(typeof(DebugConsoleCaptureTests).Assembly,
                             new RecordingConsole(warnings), new ServiceContainer());
        return new(registry, warnings);
    }

    private DebugApiServer Serve(Made made, string chatter = "")
    {
        // Production's shape: the module's ILog prints through the side's
        // ConsoleLines, and the dev API's DebugLog taps those lines when
        // subscribed. The capture's sink forwards to the live ILog, so a
        // forwarded line crosses this composition's console too — which is
        // what puts it in the ring the reply's LOG feed reads, and so what
        // makes the dedup fact observable rather than vacuous.
        var lines = new ConsoleLines();
        _live.Line = (text, warning) => lines.Line(text, warning);

        var hooks = TestDebugHooks.Server(
            log: _live,
            lines: lines,
            world: new FakeDebugWorld(),
            console: new DebugConsoleHook(() => [], () => [], _ => null, () => [], _ => null,
                                          _executed.Add),
            commandRun: new DebugCommandRun(made.Registry, () => new ServiceContainer(), _live));

        // Subscribed by Start, so the listening message and the chatter are
        // in the ring before the request — the reply must exclude them by its
        // own window, not by nobody having been listening.
        // -debugapisocket, as every started test server does: on the fixed TCP
        // port a second server (another test class, a game, a peer's run) wins
        // the bind and Start answers false ("the test server could not listen").
        var server = new DebugApiServer(["-debugapisocket"], hooks, TestDebugApiServers.OwnSocket());
        Assert.True(server.Start(), "the test server could not listen");
        if (chatter != "")
            hooks.Log.Msg(chatter + "\n");
        return server;
    }

    private static JsonElement Captured(DebugApiServer server, string command)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, $"/command?capture=1&c={Uri.EscapeDataString(command)}").Body);
        return doc.RootElement.GetProperty("captured").Clone();
    }

    private static string[] Lines(JsonElement captured, string feed)
        => captured.GetProperty(feed).EnumerateArray().Select(l => l.GetString()!).ToArray();

    // ---- The managed path --------------------------------------------------

    [Fact]
    public void A_managed_command_is_answered_from_its_own_prints()
    {
        var made = Make();
        using var server = Serve(made, chatter: "game chatter before the command");

        var captured = Captured(server, ChattyName);

        // Both channels a command prints with — log AND engine console — in
        // the order printed. A command printing with console.Msg must not
        // capture empty, or the answer would lie about it.
        Assert.Equal(["via the log", "warned", "via the engine console", "engine warned"],
                     Lines(captured, "console"));
        // Scoped to the command: no `] command` echo, no chatter from before.
        Assert.DoesNotContain(Lines(captured, "console"),
                              l => l.Contains("]") || l.Contains("chatter"));
        // And the synchronous sink means there was nothing to wait for.
        Assert.True(captured.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void A_managed_capture_never_hands_the_console_a_command_line()
    {
        var made = Make();
        using var server = Serve(made);

        Captured(server, TokenName);

        // No command line, no echo markers: the capture ran an INSTANCE, not
        // a command line. The console was never reached at all.
        Assert.Empty(_executed);
    }

    [Fact]
    public void Each_capture_runs_a_fresh_instance_of_the_command()
    {
        var made = Make();
        using var server = Serve(made);

        string[] first = Lines(Captured(server, TokenName), "console");
        string[] second = Lines(Captured(server, TokenName), "console");

        // The token is minted per instance. A capture that reused the
        // registered singleton — the state a hot-reloaded entity learned to
        // fear — would repeat it.
        string token1 = Assert.Single(first);
        string token2 = Assert.Single(second);
        Assert.Matches(@"^token [0-9a-f]{8}$", token1);
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void The_words_after_the_command_reach_the_instance_as_args()
    {
        var made = Make();
        using var server = Serve(made);

        var captured = Captured(server, ArgsName + " one two");

        // The raw remainder after the first space, exactly as the console
        // hands an Execute.
        Assert.Equal(["args[one two]"], Lines(captured, "console"));
    }

    [Fact]
    public void What_the_command_printed_still_reaches_the_game_log()
    {
        var made = Make();
        using var server = Serve(made);

        Captured(server, ChattyName);

        // The sink is not a wall: an onlooker reading the console (and
        // console.log through -condebug) saw this command's output before
        // capture changed, and must still see it.
        Assert.Contains("via the log", _live.Text);
        Assert.Contains("via the engine console", _live.Text);
    }

    [Fact]
    public void Lines_the_sink_forwarded_are_not_duplicated_in_the_log_feed()
    {
        var made = Make();
        using var server = Serve(made);

        var captured = Captured(server, ChattyName);

        // Forwarded for the onlookers, deduplicated for the reply: the log
        // feed holds what else logged (the `] command` echo), never the same
        // line twice across the two feeds.
        Assert.DoesNotContain("via the log", Lines(captured, "log"));
        Assert.DoesNotContain("via the engine console", Lines(captured, "log"));
        Assert.Contains(Lines(captured, "log"), l => l.Contains(ChattyName, StringComparison.Ordinal));
    }

    // ---- The fallback: names the managed registry does not run --------------

    [Fact]
    public void A_command_the_registry_does_not_own_still_runs_as_a_console_line()
    {
        var made = Make();
        using var server = Serve(made);

        var captured = Captured(server, "no_such_command_at_all");

        // The old path for engine commands: the command between its two
        // echo markers, exactly three console lines.
        Assert.Equal(3, _executed.Count);
        Assert.Equal("no_such_command_at_all", _executed[1]);
        Assert.All([_executed[0], _executed[2]],
                   marker => Assert.Matches(@"^echo [A-Za-z0-9_]+$", marker));
        // With no console file the reply says what it saw, rather than an
        // empty list that would read as "printed nothing".
        Assert.False(captured.GetProperty("complete").GetBoolean());
        Assert.StartsWith("managed log only", captured.GetProperty("source").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_class_the_registry_refused_is_a_console_line_too()
    {
        var made = Make();

        // RegisterAll refused it for its unresolvable dependency — the
        // registry owns no runnable instance, so the capture has no instance
        // to run and must not claim one.
        Assert.Contains(made.Warnings,
                        w => w.Contains(RefusedName, StringComparison.Ordinal)
                          && w.Contains("cannot be built", StringComparison.Ordinal));

        using var server = Serve(made);
        _ = Captured(server, RefusedName);

        Assert.Equal(3, _executed.Count);
        Assert.Equal(RefusedName, _executed[1]);
    }

    [Fact]
    public void A_native_bodied_carrier_is_never_run_as_a_managed_command()
    {
        // The commands the engine dispatches natively (RegisterNativeBodied)
        // are managed names whose OUTPUT is native: running their managed
        // carrier into a sink would answer "no body" and capture none of the
        // real counters. The registry must hand the capture nothing for them.
        var registry = new ConCommandRegistry();
        var warnings = new List<string>();
        registry.RegisterNativeBodied([new ConCommandAttribute(NativeBodiedName) { Help = "native output" }],
                                      new RecordingConsole(warnings));
        Assert.NotNull(registry.ByName(NativeBodiedName));   // it IS a registered name

        var run = new DebugCommandRun(registry, () => new ServiceContainer(), new RecordingLog());

        Assert.Null(run.TryRunManaged(NativeBodiedName));
    }
}
