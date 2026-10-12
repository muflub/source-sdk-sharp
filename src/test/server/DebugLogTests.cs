using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's IDebugLog (G9; plan_devapi_interfaces §5.8 Q3): a subscription
/// over Log's process-wide tap, never a replaced sink.
/// </summary>
public class DebugLogTests
{
    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = [];

        public void Msg(string message) => Lines.Add("msg:" + message);

        public void Warning(string message) => Lines.Add("warn:" + message);
    }

    // The console lines this fact's DebugLogs tap (statics to zero: it was the
    // static Log's process-wide sink), with a known tap installed first.
    private readonly ConsoleLines _lines = new();

    private void WithSink(Action<string, bool> sink, Action body)
    {
        _lines.Tap = sink;
        body();
    }

    [Fact]
    public void Msg_and_warning_go_to_the_server_console()
    {
        var console = new ListLog();
        var log = new DebugLog(console, _lines);

        log.Msg("a\n");
        log.Warning("b\n");

        Assert.Equal(["msg:a\n", "warn:b\n"], console.Lines);
    }

    [Fact]
    public void A_subscriber_sees_each_line_and_the_previous_sink_still_does()
    {
        var before = new List<string>();
        var seen = new List<string>();

        WithSink((t, w) => before.Add(t), () =>
        {
            var log = new DebugLog(new ListLog(), _lines);
            using (log.Subscribe((t, w) => seen.Add((w ? "W " : "M ") + t)))
            {
                _lines.Line("one", false);
                _lines.Line("two", true);
            }
        });

        Assert.Equal(["M one", "W two"], seen);
        Assert.Equal(["one", "two"], before);
    }

    [Fact]
    public void The_last_unsubscription_puts_the_previous_sink_back()
    {
        Action<string, bool> previous = (_, _) => { };

        WithSink(previous, () =>
        {
            var log = new DebugLog(new ListLog(), _lines);
            var first = log.Subscribe((_, _) => { });
            var second = log.Subscribe((_, _) => { });

            Assert.NotSame(previous, _lines.Tap);

            first.Dispose();
            Assert.NotSame(previous, _lines.Tap);
            Assert.Equal(1, log.Subscribers);

            second.Dispose();
            second.Dispose();
            Assert.Same(previous, _lines.Tap);
            Assert.Equal(0, log.Subscribers);
        });
    }

    [Fact]
    public void Two_instances_both_see_every_line_and_neither_takes_the_other_down()
    {
        var a = new List<string>();
        var b = new List<string>();

        WithSink((_, _) => { }, () =>
        {
            var one = new DebugLog(new ListLog(), _lines);
            var two = new DebugLog(new ListLog(), _lines);
            var subA = one.Subscribe((t, _) => a.Add(t));
            var subB = two.Subscribe((t, _) => b.Add(t));

            _lines.Line("both", false);

            // The first instance leaves while the second's tap is in front of it.
            subA.Dispose();
            _lines.Line("only two", false);

            subB.Dispose();
        });

        Assert.Equal(["both"], a);
        Assert.Equal(["both", "only two"], b);
    }
}
