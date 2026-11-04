using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;

using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>A typed command the registry could not bind is skipped at load with one warning: this names it instead.</summary>
public sealed class CommandBindingCensusTests
{
    [Fact]
    public void EveryAiCommandBinds()
    {
        var (typed, unbound) = ConCommandLines.Census(typeof(AiBotAddCommand).Assembly);
        Assert.True(typed > 10, $"{typed}");
        Assert.Empty(unbound);
    }
}
