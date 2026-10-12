using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The console-display binding: what the native forwarder hands managed for
/// one engine print (docs/todo.md's console capture item). The thunk's
/// contract is (UTF-8 text, warning flag) -> ConsoleLines.Line; the flag is
/// ColorPrint's decorated channel, and the construction itself — area acquire
/// — marks the side's feed attached.
/// </summary>
public sealed unsafe class ConsoleDisplayBindingTests
{
    [Fact]
    public void An_engine_print_lands_in_the_sides_lines_as_a_message()
    {
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));

        var binding = new ConsoleDisplayBinding(new Crossings(), lines);
        byte[] utf8 = "engine said hello\n\0"u8.ToArray();
        fixed (byte* p = utf8)
        {
            binding.ConsoleLine(p, 0);
        }

        Assert.Equal([("engine said hello\n", false)], seen);
    }

    [Fact]
    public void A_warning_print_lands_flagged_as_a_warning()
    {
        var lines = new ConsoleLines();
        var seen = new List<(string Text, bool Warning)>();
        lines.Tap = (text, warning) => seen.Add((text, warning));

        var binding = new ConsoleDisplayBinding(new Crossings(), lines);
        byte[] utf8 = "careful\n\0"u8.ToArray();
        fixed (byte* p = utf8)
        {
            binding.ConsoleLine(p, 1);
        }

        Assert.Equal([("careful\n", true)], seen);
    }

    [Fact]
    public void A_null_pointer_is_not_dispatched()
    {
        var lines = new ConsoleLines();
        int calls = 0;
        lines.Tap = (_, _) => calls++;

        var binding = new ConsoleDisplayBinding(new Crossings(), lines);
        binding.ConsoleLine(null, 0);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Building_the_binding_alone_does_not_mark_the_side_as_feeding()
    {
        // console-batch: "attached" means the forwarder's buffer has arrived
        // (ConsoleBufferAttach; ConsoleLinesBatchTests), not that the area
        // was built: without the buffer no engine print reaches the side.
        var lines = new ConsoleLines();

        _ = new ConsoleDisplayBinding(new Crossings(), lines);

        Assert.False(lines.EngineFeedAttached);
    }
}
