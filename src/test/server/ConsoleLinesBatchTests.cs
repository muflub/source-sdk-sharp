using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The console feed after console-batch: the engine's prints sit in the
/// side's native line buffer and <see cref="ConsoleLines"/> drains them; every
/// line, native or managed, takes its number from the buffer's one counter and
/// is handed on in number order.
/// </summary>
public sealed class ConsoleLinesBatchTests
{
    private static (ConsoleLines Lines, List<string> Seen) Attached(TestConsoleBuffer buffer)
    {
        var lines = new ConsoleLines();
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);
        lines.AttachBuffer(buffer.Reader());
        return (lines, seen);
    }

    [Fact]
    public void An_engine_print_waits_in_the_buffer_until_a_drain()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        buffer.Append("engine line\n");
        Assert.Empty(seen);

        lines.Drain();
        Assert.Equal(["engine line\n"], seen);
    }

    [Fact]
    public void Interleaved_engine_and_managed_prints_come_out_in_print_order()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        buffer.Append("engine 1\n");
        buffer.Append("engine 2\n");
        lines.Line("managed 3\n", isWarning: false);
        buffer.Append("engine 4\n");
        lines.Line("managed 5\n", isWarning: false);
        buffer.Append("engine 6\n");
        lines.Drain();

        Assert.Equal(["engine 1\n", "engine 2\n", "managed 3\n", "engine 4\n", "managed 5\n", "engine 6\n"], seen);
    }

    [Fact]
    public void A_managed_print_takes_its_number_from_the_shared_counter()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, _) = Attached(buffer);

        buffer.Append("engine\n");                       // 1
        lines.Line("managed\n", isWarning: false);       // 2
        buffer.Append("engine again\n");                 // 3

        Assert.Equal(3u, buffer.Sequence);
        Assert.Equal(3u, lines.Sequence);
    }

    [Fact]
    public void Order_and_capture_hold_across_the_uint32_wrap()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);
        buffer.Sequence = 0xFFFFFFFEu;

        string captured = lines.Capture(() =>
        {
            buffer.Append("before the wrap\n");          // 0xFFFFFFFF
            lines.Line("at the wrap\n", isWarning: false); // 0
            buffer.Append("after the wrap\n");           // 1
        });

        Assert.Equal("before the wrap\nat the wrap\nafter the wrap\n", captured);
        Assert.Equal(["before the wrap\n", "at the wrap\n", "after the wrap\n"], seen);
        Assert.True(NativeConsoleBuffer.After(1u, 0xFFFFFFFFu));
        Assert.False(NativeConsoleBuffer.After(0xFFFFFFFFu, 1u));
    }

    [Fact]
    public void A_capture_answers_exactly_its_range()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        buffer.Append("printed before, never drained\n");
        string captured = lines.Capture(() =>
        {
            buffer.Append("the command's engine line\n");
            lines.Line("the command's managed line\n", isWarning: false);
        });
        buffer.Append("printed after\n");
        lines.Drain();

        Assert.Equal("the command's engine line\nthe command's managed line\n", captured);
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void A_capture_drains_before_it_answers()
    {
        // /command?capture=1's path: the engine runs the command on this
        // thread and its prints land in the buffer; the answer must hold them
        // without waiting for a frame.
        using var buffer = new TestConsoleBuffer();
        var (lines, _) = Attached(buffer);

        string captured = lines.Capture(() => buffer.Append("playershadow: enabled=1\n"));

        Assert.Equal("playershadow: enabled=1\n", captured);
    }

    [Fact]
    public void A_capture_takes_another_threads_line_inside_its_range()
    {
        // The console-batch ruling (2026-10-11): scoped by number, not thread.
        using var buffer = new TestConsoleBuffer();
        var (lines, _) = Attached(buffer);

        string captured = lines.Capture(() =>
        {
            var other = new Thread(() => lines.Line("from another thread\n", isWarning: false));
            other.Start();
            other.Join();
        });

        Assert.Equal("from another thread\n", captured);
    }

    [Fact]
    public void The_engines_echo_of_a_managed_print_is_delivered_once()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        // EngineSide.Msg's shape: say the line, then hand it to the engine,
        // which prints it into the buffer synchronously.
        lines.Line("hello\n", isWarning: false);
        lines.SuppressEcho("hello\n", () => buffer.Append("hello\n"));
        lines.Drain();

        Assert.Equal(["hello\n"], seen);
    }

    [Fact]
    public void An_echo_window_consumes_one_copy_and_keeps_other_engine_lines()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        lines.Line("hi\n", isWarning: false);
        lines.SuppressEcho("hi\n", () =>
        {
            buffer.Append("unrelated\n");
            buffer.Append("hi");
        });
        buffer.Append("hi\n");     // a later, real print of the same text
        lines.Drain();

        Assert.Equal(["hi\n", "unrelated\n", "hi\n"], seen);
    }

    [Fact]
    public void An_engine_line_printed_before_the_echo_window_is_never_consumed()
    {
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);

        buffer.Append("same\n");                        // before the managed print
        lines.Line("same\n", isWarning: false);         // drains the engine's first
        lines.SuppressEcho("same\n", () => buffer.Append("same\n"));

        Assert.Equal(["same\n", "same\n"], seen);
    }

    [Fact]
    public void An_engine_print_between_the_managed_print_and_its_echo_is_not_taken_for_the_echo()
    {
        // The window opens at the number taken when the echo is armed: a
        // print of the same text that landed before that (here a warning, so
        // the two can be told apart) is a real line, and the copy the engine
        // prints back inside the window is the one consumed.
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));
        lines.AttachBuffer(buffer.Reader());

        lines.Line("same\n", isWarning: false);
        buffer.Append("same\n", warning: true);
        lines.SuppressEcho("same\n", () => buffer.Append("same\n"));

        Assert.Equal([("same\n", false), ("same\n", true)], seen);
    }

    [Fact]
    public void A_full_buffer_is_said_once_as_a_warning_and_counted()
    {
        using var buffer = new TestConsoleBuffer(halfBytes: 64);
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));
        lines.AttachBuffer(buffer.Reader());

        Assert.True(buffer.Append("0123456789012345678901234567890123456789\n"));   // 56 of 64 bytes
        Assert.False(buffer.Append("does not fit\n"));
        Assert.False(buffer.Append("nor this\n"));
        lines.Drain();

        Assert.Equal(2, seen.Count);
        Assert.False(seen[0].Warning);
        Assert.True(seen[1].Warning);
        Assert.Contains("lost 2 engine line(s)", seen[1].Text);
        Assert.Equal(2, lines.LostLines);
    }

    [Fact]
    public void Numbers_run_on_across_a_reload_and_the_next_load_gets_the_undrained_lines()
    {
        // Two loads over the forwarder's one buffer, as cs_sv_reload makes them.
        using var buffer = new TestConsoleBuffer();
        var (first, firstSeen) = Attached(buffer);
        first.Line("first load\n", isWarning: false);
        buffer.Append("printed just before the gap\n");
        uint lastOfFirst = first.Sequence;

        var (second, secondSeen) = Attached(buffer);
        second.Line("second load\n", isWarning: false);

        Assert.Equal(["first load\n"], firstSeen);
        Assert.Equal(["printed just before the gap\n", "second load\n"], secondSeen);
        Assert.True(NativeConsoleBuffer.After(second.Sequence, lastOfFirst));
    }

    [Fact]
    public void A_drain_waits_for_a_print_holding_the_lock()
    {
        // The positive arm of the lock: a drain does not read a half a print
        // is still copying into.
        using var buffer = new TestConsoleBuffer();
        var (lines, seen) = Attached(buffer);
        buffer.HoldLock();

        using var done = new ManualResetEventSlim();
        var drain = new Thread(() => { lines.Drain(); done.Set(); });
        drain.Start();
        Assert.False(done.Wait(50), "the drain read past a held lock");
        buffer.ReleaseLock();
        Assert.True(done.Wait(5000), "the drain never finished after the release");
        drain.Join();
        Assert.Empty(seen);
    }

    [Fact]
    public void The_binding_attaches_the_buffer_counts_its_op_and_marks_the_feed()
    {
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        var crossings = new Crossings();
        var binding = new ConsoleDisplayBinding(crossings, lines);
        Assert.False(lines.EngineFeedAttached);

        unsafe
        {
            binding.ConsoleBufferAttach((void*)buffer.Address);
        }
        buffer.Append("through the attached buffer\n");
        string captured = lines.Capture(() => { });

        Assert.True(lines.EngineFeedAttached);
        Assert.Equal(1L, crossings.Read(CrossingOp.ServerConsoleBufferAttach));
        Assert.Equal("", captured);
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);
        buffer.Append("after\n");
        lines.Drain();
        Assert.Equal(["after\n"], seen);
    }

    [Fact]
    public void A_null_buffer_is_not_attached()
    {
        var lines = new ConsoleLines();
        var crossings = new Crossings();
        var binding = new ConsoleDisplayBinding(crossings, lines);

        unsafe
        {
            binding.ConsoleBufferAttach(null);
        }

        Assert.False(lines.EngineFeedAttached);
        Assert.Equal(0L, crossings.Read(CrossingOp.ServerConsoleBufferAttach));
    }

    [Fact]
    public void The_layout_is_the_one_console_line_buffer_h_declares()
    {
        // console_line_buffer_test pins the native side to the same literals.
        Assert.Equal(0, NativeConsoleBuffer.SeqOffset);
        Assert.Equal(4, NativeConsoleBuffer.OverflowOffset);
        Assert.Equal(8, NativeConsoleBuffer.LockOffset);
        Assert.Equal(12, NativeConsoleBuffer.ActiveOffset);
        Assert.Equal(16, NativeConsoleBuffer.UsedOffset);
        Assert.Equal(24, NativeConsoleBuffer.HalfBytesOffset);
        Assert.Equal(32, NativeConsoleBuffer.HeaderBytes);
        Assert.Equal(12, NativeConsoleBuffer.RecordBytes);
        Assert.Equal(0, NativeConsoleBuffer.RecordSeqOffset);
        Assert.Equal(4, NativeConsoleBuffer.RecordLengthOffset);
        Assert.Equal(8, NativeConsoleBuffer.RecordWarningOffset);
    }
}
