using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine-echo suppression: a managed print that the engine echoes back
/// through the installed display func must reach listeners once, not twice
/// (docs/todo.md's console capture item).
/// </summary>
public sealed class ConsoleLinesEchoTests
{
    [Fact]
    public void A_print_forwarded_under_suppression_is_delivered_once()
    {
        var lines = new ConsoleLines { EngineFeedAttached = true };
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        // EngineSide.Msg's shape: say the line, then hand it to the engine,
        // which hands it straight back on this thread through the func.
        lines.Line("hello\n", isWarning: false);
        lines.SuppressEcho("hello\n", () => lines.Line("hello\n", isWarning: false));

        Assert.Equal(["hello\n"], seen);
    }

    [Fact]
    public void Suppression_consumes_once_and_later_lines_still_arrive()
    {
        var lines = new ConsoleLines { EngineFeedAttached = true };
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        lines.SuppressEcho("hi\n", () => lines.Line("hi\n", isWarning: false));
        lines.Line("hi\n", isWarning: false);

        Assert.Equal(["hi\n"], seen);
    }

    [Fact]
    public void A_different_line_under_suppression_is_not_consumed()
    {
        var lines = new ConsoleLines { EngineFeedAttached = true };
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        lines.SuppressEcho("sent\n", () => { });
        lines.Line("other\n", isWarning: false);

        Assert.Equal(["other\n"], seen);
    }

    [Fact]
    public void The_echo_may_arrive_without_its_trailing_newline()
    {
        // The display func can hand the text trimmed; the match is the same
        // line either way.
        var lines = new ConsoleLines { EngineFeedAttached = true };
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        lines.SuppressEcho("same\n", () => lines.Line("same", isWarning: false));

        Assert.Empty(seen);
    }

    [Fact]
    public void Without_an_attached_feed_forwarding_just_runs()
    {
        // Nothing echoes back without the display func, so the bookkeeping is
        // skipped; every line still delivers.
        var lines = new ConsoleLines();
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        lines.SuppressEcho("x\n", () => lines.Line("x\n", isWarning: false));

        Assert.Equal(["x\n", "x\n"], seen);
    }

    [Fact]
    public void Another_threads_line_is_never_consumed_by_the_suppression()
    {
        var lines = new ConsoleLines { EngineFeedAttached = true };
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);

        lines.SuppressEcho("shared\n", () =>
        {
            var t = new Thread(() => lines.Line("shared\n", isWarning: false));
            t.Start();
            t.Join();
        });

        Assert.Equal(["shared\n"], seen);
    }
}
