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
    /// <summary>
    /// The one-line slot left the area in the serialised ABI pass
    /// (2026-10-20): no row, no contract member. The buffer hand-over, the
    /// interface's other slot, is the positive arm.
    /// </summary>
    [Fact]
    public void The_one_line_slot_is_gone_and_the_buffer_hand_over_stays()
    {
        string tsv = File.ReadAllText(RepoLayout.Repo("docs", "managed-interfaces.tsv"));
        string contract = File.ReadAllText(RepoLayout.Repo("src", "server", "interop", "ManagedAreas.g.cs"));

        Assert.DoesNotContain("IManagedConsoleOutput\tConsoleLine\t", tsv);
        Assert.DoesNotContain(" ConsoleLine(", contract);
        Assert.Contains("IManagedConsoleOutput\tConsoleBufferAttach\t", tsv);
        Assert.Contains(" ConsoleBufferAttach(", contract);
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
