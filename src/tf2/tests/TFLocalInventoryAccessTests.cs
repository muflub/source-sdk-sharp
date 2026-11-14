using SourceSharp.Abi;

using TF2.Server.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The local inventory's gates (<see cref="ITFLocalInventoryGate"/>): TF2's
/// own game registers none and is open; a further mod's gate closes it for
/// the players it names, and a broken gate closes nothing.
/// </summary>
public sealed class TFLocalInventoryAccessTests
{
    private readonly RecordingLog _log = new();

    private sealed class Gate(Func<int, bool> open, string why = "") : ITFLocalInventoryGate
    {
        public bool IsOpen(int entIndex, out string reason)
        {
            reason = why;
            return open(entIndex);
        }
    }

    private sealed class Throwing : ITFLocalInventoryGate
    {
        public bool IsOpen(int entIndex, out string reason) => throw new InvalidOperationException("broken gate");
    }

    private TFLocalInventoryAccess Access(params ITFLocalInventoryGate[] gates)
        => new(new Rules.ListHooks<ITFLocalInventoryGate>(gates), _log);

    [Fact]
    public void WithNoGateTheInventoryIsOpen()
    {
        Assert.True(Access().IsOpen(3, out string reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void OneClosedGateClosesItWithItsReason()
    {
        var access = Access(new Gate(_ => true), new Gate(i => i != 3, "a hero's kit"));

        Assert.False(access.IsOpen(3, out string reason));
        Assert.Equal("a hero's kit", reason);
        Assert.True(access.IsOpen(4, out _));
    }

    [Fact]
    public void AGateThatThrowsClosesNothingAndIsNamed()
    {
        Assert.True(Access(new Throwing()).IsOpen(3, out _));
        Assert.Contains(_log.Lines, l => l.Text.Contains(nameof(Throwing)) && l.Text.Contains("broken gate"));
    }
}
