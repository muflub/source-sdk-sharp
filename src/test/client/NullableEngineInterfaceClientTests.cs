using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's product assemblies (its logic, its interop and the UI host)
/// hold no nullable engine interface outside the acquisition layer
/// (<see cref="NullableEngineInterfaceCensus"/>).
/// </summary>
public class NullableEngineInterfaceClientTests
{
    [Fact]
    public void TheClientHoldsTakesAndAnswersNoNullableEngineInterface()
    {
        var (nullable, mentions) = NullableEngineInterfaceCensus.Scan(NullableEngineInterfaceCensus.TypesOf(
            typeof(ClientModule).Assembly, typeof(EngineClient).Assembly, typeof(SourceSharp.Client.Vgui.UiComposition).Assembly));

        Assert.True(mentions > 30, $"the scan saw {mentions} members naming an engine interface");
        Assert.True(nullable.Count == 0, $"{nullable.Count}:\n" + string.Join("\n", nullable));
    }
}
