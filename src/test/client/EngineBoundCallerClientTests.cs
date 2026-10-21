using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's product code calls the side's bound slots only in the
/// acquisition layer (<see cref="EngineBoundCallerCensus"/>).
/// </summary>
public class EngineBoundCallerClientTests
{
    [Fact]
    public void OnlyTheAcquisitionLayerCallsTheClientsBoundSlots()
    {
        var (outside, all) = EngineBoundCallerCensus.Callers(typeof(EngineSide).Assembly, typeof(ClientModule).Assembly,
                                                             typeof(SourceSharp.Client.Vgui.UiComposition).Assembly);

        Assert.Contains("SourceSharp.Abi.EngineServices", all);
        Assert.True(outside.Length == 0, $"{outside.Length}:\n" + string.Join("\n", outside));
    }
}
