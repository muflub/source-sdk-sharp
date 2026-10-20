using System.Text.Json;

using Debug.Api;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /command?capture=1: the console output a command printed, scoped to that
/// command by markers it echoes around it (debug-api lane, item 3).
/// </summary>
public sealed class DebugApiCaptureTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ss-capture-{Guid.NewGuid():N}.log");
    private readonly List<string> _executed = [];

    /// <summary>What each command prints, as the engine would write it to console.log.</summary>
    private readonly Dictionary<string, string[]> _prints = new()
    {
        ["cs_sv_playershadow"] = ["[SourceSharp] playershadow: enabled=1 built=1 snaps=3", "[SourceSharp] playershadow: live=1"],
        ["quiet"] = [],
    };

    /// <summary>When set, the console never writes the closing marker: a file that stopped.</summary>
    private bool _dropEnd;

    public DebugApiCaptureTests() => File.WriteAllText(_file, "old line before the capture\n");

    public void Dispose() => File.Delete(_file);

    private void Execute(string command)
    {
        _executed.Add(command);
        string[] lines = command.StartsWith("echo ", StringComparison.Ordinal)
            ? [command["echo ".Length..]]
            : _prints.GetValueOrDefault(command, []);

        if (_dropEnd && command.EndsWith("_end", StringComparison.Ordinal))
            return;

        // Something else printing just before the opening marker: the line a
        // capture that read "everything since the command was sent" would take.
        string chatter = command.EndsWith("_begin", StringComparison.Ordinal)
            ? "printed just before the capture began"
            : "unrelated chatter from another thread";
        File.AppendAllLines(_file, [chatter, .. lines]);
    }

    // THE DEADLINE IS THE GAME'S OWN (3 s) for a fact that passes on a hit: the
    // capture returns as soon as its closing marker lands, so a long deadline
    // costs nothing on a fast run, and a loaded runner (server facts run 16 at
    // a time) is not a failure. 300 ms failed once on win-x64 at 6b046c13a with
    // an empty capture. Only the facts that expect no closing marker wait the
    // deadline out, so only they pass a short one.
    private DebugApiServer Serve(bool tail = true, bool waitsTheDeadlineOut = false)
    {
        // -debugapisocket, as every started test server does: on the fixed TCP
        // port a second server (another test class, a game, a peer's run) wins
        // the bind and Start answers false ("the test server could not listen").
        var server = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(
            world: new FakeDebugWorld(),
            console: new DebugConsoleHook(() => [], () => [], _ => null, () => [], _ => null, Execute)), TestDebugApiServers.OwnSocket())
        {
            CaptureTimeout = waitsTheDeadlineOut ? TimeSpan.FromMilliseconds(300) : TimeSpan.FromSeconds(3),
        };

        if (tail)
            server.StartConsoleTailForTest(_file);
        return server;
    }

    private static JsonElement Captured(DebugApiServer server, string command)
    {
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, $"/command?capture=1&c={Uri.EscapeDataString(command)}").Body);
        return doc.RootElement.GetProperty("captured").Clone();
    }

    private static string[] Lines(JsonElement captured, string feed)
        => captured.GetProperty(feed).EnumerateArray().Select(l => l.GetString()!).ToArray();

    [Fact]
    public void The_capture_answers_what_the_command_printed()
    {
        using var server = Serve();
        var captured = Captured(server, "cs_sv_playershadow");
        Assert.Contains("[SourceSharp] playershadow: enabled=1 built=1 snaps=3", Lines(captured, "console"));
        Assert.True(captured.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void The_capture_holds_nothing_printed_before_the_command()
    {
        using var server = Serve();
        Assert.DoesNotContain("printed just before the capture began", Lines(Captured(server, "cs_sv_playershadow"), "console"));
    }

    [Fact]
    public void The_markers_themselves_are_not_part_of_the_capture()
    {
        using var server = Serve();
        Assert.DoesNotContain(Lines(Captured(server, "quiet"), "console"), l => l.Contains("debugapi_capture_"));
    }

    [Fact]
    public void The_command_runs_between_its_two_markers()
    {
        using var server = Serve();
        _ = Captured(server, "quiet");
        Assert.Equal(3, _executed.Count);
        Assert.EndsWith("_begin", _executed[0], StringComparison.Ordinal);
        Assert.Equal("quiet", _executed[1]);
        Assert.EndsWith("_end", _executed[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Each_marker_is_one_console_token()
    {
        // A marker of several words was echoed back reordered in a live run,
        // and the capture never found it.
        using var server = Serve();
        _ = Captured(server, "quiet");
        Assert.All([_executed[0], _executed[2]], marker =>
            Assert.Matches("^echo [A-Za-z0-9_]+$", marker));
    }

    [Fact]
    public void A_closing_marker_that_never_arrives_is_reported_incomplete()
    {
        _dropEnd = true;
        using var server = Serve(waitsTheDeadlineOut: true);
        Assert.False(Captured(server, "cs_sv_playershadow").GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void Without_a_console_file_the_capture_says_it_saw_only_the_managed_log()
    {
        using var server = Serve(tail: false, waitsTheDeadlineOut: true);
        var captured = Captured(server, "cs_sv_playershadow");
        Assert.False(captured.GetProperty("complete").GetBoolean());
        Assert.StartsWith("managed log only", captured.GetProperty("source").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Stamping_a_reply_that_ends_in_a_nested_object_keeps_the_marks_at_the_top()
    {
        using var doc = JsonDocument.Parse(DebugApiServer.Stamped("{\"a\":{\"b\":1}}", (1, 2, 3)));
        Assert.Equal(2, doc.RootElement.GetProperty("eventsFrom").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("a").GetProperty("b").GetInt32());
    }
}
