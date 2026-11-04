extern alias client;

using SourceSharp.Abi;

using Xunit;

namespace TF2.Tests.Commands;

/// <summary>A typed command the registry could not bind is skipped at load with one warning: these name it instead.</summary>
public sealed class CommandBindingCensusTests
{
    [Fact]
    public void EveryTF2ServerCommandBinds()
    {
        var (typed, unbound) = ConCommandLines.Census(typeof(TF2.Server.Commands.ForceWinCommand).Assembly);
        Assert.True(typed > 5, $"{typed}");
        Assert.Empty(unbound);
    }

    [Fact]
    public void EveryTF2ClientCommandBinds()
    {
        var (typed, unbound) = client::SourceSharp.Abi.ConCommandLines.Census(typeof(client::TF2.Client.Ui.TFVoiceMenus).Assembly);
        Assert.True(typed > 5, $"{typed}");
        Assert.Empty(unbound);
    }
}
