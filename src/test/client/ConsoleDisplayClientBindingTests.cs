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
    /// <summary>
    /// The one-line slot left the client's area in the serialised ABI pass
    /// (2026-10-20): no row, no contract member. The buffer hand-over is the
    /// positive arm.
    /// </summary>
    [Fact]
    public void The_one_line_slot_is_gone_and_the_buffer_hand_over_stays()
    {
        string tsv = File.ReadAllText(RepoLayout.Repo("docs", "managed-interfaces.tsv"));
        string contract = File.ReadAllText(RepoLayout.Repo("src", "client", "interop", "ManagedAreas.g.cs"));

        Assert.DoesNotContain("IManagedConsoleOutputClient\tClientConsoleLine\t", tsv);
        Assert.DoesNotContain(" ClientConsoleLine(", contract);
        Assert.Contains("IManagedConsoleOutputClient\tClientConsoleBufferAttach\t", tsv);
        Assert.Contains(" ClientConsoleBufferAttach(", contract);
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
