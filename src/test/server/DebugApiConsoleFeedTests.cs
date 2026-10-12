using System.Text.Json;

using Debug.Api;
using SourceSharp.Server;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dev API's console feed off the in-process engine print, not the
/// -condebug file tail (docs/todo.md's console capture item): once a side's
/// display func is attached, an engine print reaches /console and a capture
/// without any console file, and the tail stops following.
/// </summary>
public sealed class DebugApiConsoleFeedTests
{
    /// <summary>
    /// A server whose hooks' log taps the same lines the console-display
    /// binding feeds, with <paramref name="feed"/> the forwarder's line buffer
    /// when a display func is attached (handed over through the binding, as
    /// native does at install), or null when none is.
    /// </summary>
    private static DebugApiServer Serve(ConsoleLines lines, TestConsoleBuffer? feed)
    {
        // -debugapisocket, as every started test server does: a second server
        // on the fixed TCP port loses the bind, Start returns false, and the
        // lines printed before its first Pump are never subscribed.
        var server = new DebugApiServer(["-debugapisocket"],
            TestDebugHooks.Server(log: NullLog.Instance, lines: lines), TestDebugApiServers.OwnSocket());
        if (feed is not null)
        {
            Attach(lines, feed);
        }

        Assert.True(server.Start(), "the test server could not listen");
        return server;
    }

    private static unsafe void Attach(ConsoleLines lines, TestConsoleBuffer buffer) =>
        new ConsoleDisplayBinding(new Crossings(), lines).ConsoleBufferAttach((void*)buffer.Address);

    private static string[] Ring(DebugApiServer server)
    {
        server.Pump();
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, "/console").Body);
        return doc.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("text").GetString()!).ToArray();
    }

    [Fact]
    public void An_engine_print_reaches_console_without_condebug()
    {
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        using var server = Serve(lines, buffer);

        // One engine print, buffered by the forwarder and drained on the
        // side's frame.
        buffer.Append("playershadow: enabled=1\n");
        lines.Drain();

        // The ring trims the newline: each line is its own record (LogRing).
        Assert.Contains("playershadow: enabled=1", Ring(server));
    }

    [Fact]
    public void A_warning_keeps_its_flag_in_the_ring()
    {
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        using var server = Serve(lines, buffer);
        buffer.Append("careful\n", warning: true);
        lines.Drain();

        server.Pump();
        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, "/console").Body);
        var warning = doc.RootElement.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("text").GetString() == "careful");
        Assert.True(warning.GetProperty("warning").GetBoolean());
    }

    [Fact]
    public void Engine_and_managed_lines_share_one_ring()
    {
        // Both kinds arrive through the side's ConsoleLines; with the feed
        // attached the ring gets every one of them, from either source.
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        using var server = Serve(lines, buffer);

        buffer.Append("the engine's first\n");
        lines.Line("[SourceSharp] mine\n", isWarning: false);
        buffer.Append("the engine's second\n");
        lines.Drain();

        // In print order: the managed line drained the engine's first ahead
        // of itself (console-batch: one sequence for both sources).
        var seen = Ring(server).ToList();
        int first = seen.IndexOf("the engine's first"), mine = seen.IndexOf("[SourceSharp] mine"),
            second = seen.IndexOf("the engine's second");
        Assert.True(first >= 0 && mine > first && second > mine,
            $"ring order: {string.Join(" | ", seen)}");
    }

    [Fact]
    public void A_captured_command_sees_engine_lines_with_no_console_file()
    {
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        var executed = new List<string>();
        var console = new DebugConsoleHook(
            () => [], () => [], _ => null, () => [], _ => null,
            command =>
            {
                executed.Add(command);
                // The engine answers an echo itself; its print lands in the
                // forwarder's buffer, exactly as the installed one's does,
                // and nothing drains it until the capture does.
                if (command.StartsWith("echo ", StringComparison.Ordinal))
                {
                    buffer.Append(command["echo ".Length..] + "\n");
                }
                else
                {
                    buffer.Append("the command printed\n");
                }
            });
        using var server = new DebugApiServer(["-debugapisocket"],
            TestDebugHooks.Server(log: NullLog.Instance, lines: lines, console: console), TestDebugApiServers.OwnSocket())
        {
            CaptureTimeout = TimeSpan.FromMilliseconds(300),
        };
        Attach(lines, buffer);
        Assert.True(server.Start(), "the test server could not listen");

        using var doc = JsonDocument.Parse(DebugApiGolden.Pumped(server, "/command?capture=1&c=cs_sv_playershadow").Body);
        var captured = doc.RootElement.GetProperty("captured");

        Assert.True(captured.GetProperty("complete").GetBoolean());
        Assert.Contains("the command printed",
            captured.GetProperty("console").EnumerateArray().Select(l => l.GetString()!).ToArray());
        Assert.DoesNotContain("debugapi_capture_",
            string.Join('\n', captured.GetProperty("console").EnumerateArray().Select(l => l.GetString()!)));
    }

    [Fact]
    public void Without_a_feed_the_tail_still_answers()
    {
        // The fallback the old world runs on (and the tests'): no display
        // func, so nothing is subscribed and /console says it follows nothing.
        var lines = new ConsoleLines();
        using var server = Serve(lines, feed: null);
        lines.Line("this went nowhere\n", isWarning: false);

        Assert.DoesNotContain("this went nowhere", Ring(server));
    }
}

/// <summary>A log that swallows the API's own lines.</summary>
file sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    public void Msg(string message)
    {
    }

    public void Warning(string message)
    {
    }
}
