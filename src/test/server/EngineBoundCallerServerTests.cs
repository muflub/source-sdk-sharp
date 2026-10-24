using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's product code calls the side's bound slots only in the
/// acquisition layer (<see cref="EngineBoundCallerCensus"/>).
/// </summary>
public class EngineBoundCallerServerTests
{
    [Fact]
    public void OnlyTheAcquisitionLayerCallsTheServersBoundSlots()
    {
        var (outside, all) = EngineBoundCallerCensus.Callers(typeof(EngineSide).Assembly, typeof(ServerModule).Assembly);

        Assert.Contains("SourceSharp.Abi.EngineServices", all);
        Assert.True(outside.Length == 0, $"{outside.Length}:\n" + string.Join("\n", outside));
    }
}
