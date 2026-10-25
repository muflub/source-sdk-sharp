using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's reach into EngineSide only comes down
/// (<see cref="EngineSideReachCensus"/>; the census's own arms are the server's).
/// </summary>
public class EngineSideReachClientTests
{
    private const string BaselineFile = "engineside-reach-client.tsv";

    [Fact]
    public void TheClientsReachIntoEngineSideIsAtItsBaseline()
    {
        string[] reach = EngineSideReachCensus.Reach(typeof(EngineSide).Assembly, typeof(ClientModule).Assembly,
                                                     typeof(SourceSharp.Client.Vgui.UiComposition).Assembly);

        Assert.True(reach.Length > 30, $"the scan saw {reach.Length} pairs");
        EngineSideReachCensus.AssertAtBaseline(reach, EngineSideReachCensus.Baseline(BaselineFile), BaselineFile);
    }
}
