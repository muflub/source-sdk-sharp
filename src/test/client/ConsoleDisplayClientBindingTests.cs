using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client mirror of the console-display binding: what the native client
/// forwarder hands managed for one engine print.
/// </summary>
public sealed unsafe class ConsoleDisplayClientBindingTests
{
    [Fact]
    public void An_engine_print_lands_in_the_sides_lines_as_a_message()
    {
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));

        var binding = new ConsoleDisplayClientBinding(new Crossings(), lines);
        byte[] utf8 = "client engine said hello\n\0"u8.ToArray();
        fixed (byte* p = utf8)
        {
            binding.ClientConsoleLine(p, 0);
        }

        Assert.Equal([("client engine said hello\n", false)], seen);
    }

    [Fact]
    public void A_warning_print_lands_flagged_as_a_warning()
    {
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));

        var binding = new ConsoleDisplayClientBinding(new Crossings(), lines);
        byte[] utf8 = "careful\n\0"u8.ToArray();
        fixed (byte* p = utf8)
        {
            binding.ClientConsoleLine(p, 1);
        }

        Assert.Equal([("careful\n", true)], seen);
    }

    [Fact]
    public void A_null_pointer_is_not_dispatched()
    {
        var lines = new ConsoleLines();
        int calls = 0;
        lines.Tap = (_, _) => calls++;

        var binding = new ConsoleDisplayClientBinding(new Crossings(), lines);
        binding.ClientConsoleLine(null, 0);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Attaching_the_buffer_marks_the_side_as_feeding_and_its_lines_drain()
    {
        // console-batch: the forwarder's buffer arriving, not the binding's
        // build, is what makes the engine's prints reach the side.
        using var buffer = new TestConsoleBuffer();
        var lines = new ConsoleLines();
        var crossings = new Crossings();
        var binding = new ConsoleDisplayClientBinding(crossings, lines);
        Assert.False(lines.EngineFeedAttached);

        binding.ClientConsoleBufferAttach((void*)buffer.Address);
        var seen = new List<string>();
        lines.Tap = (text, _) => seen.Add(text);
        buffer.Append("client engine line\n");
        lines.Drain();

        Assert.True(lines.EngineFeedAttached);
        Assert.Equal(1L, crossings.Read(CrossingOp.ClientConsoleBufferAttach));
        Assert.Equal(["client engine line\n"], seen);
    }
}
