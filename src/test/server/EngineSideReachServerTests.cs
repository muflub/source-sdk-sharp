using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's reach into EngineSide only comes down
/// (<see cref="EngineSideReachCensus"/>).
/// </summary>
public class EngineSideReachServerTests
{
    private const string BaselineFile = "engineside-reach-server.tsv";

    [Fact]
    public void TheServersReachIntoEngineSideIsAtItsBaseline()
    {
        string[] reach = EngineSideReachCensus.Reach(typeof(EngineSide).Assembly, typeof(ServerModule).Assembly);

        Assert.True(reach.Length > 100, $"the scan saw {reach.Length} pairs");
        EngineSideReachCensus.AssertAtBaseline(reach, EngineSideReachCensus.Baseline(BaselineFile), BaselineFile);
    }

    [Fact]
    public void ANewReachAndARetiredOneAreEachRefused()
    {
        string[] baseline = ["A\tget_Log", "B\tget_Areas"];

        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            EngineSideReachCensus.AssertAtBaseline(["A\tget_Log", "B\tget_Areas", "C\tget_Log"], baseline, BaselineFile));
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            EngineSideReachCensus.AssertAtBaseline(["A\tget_Log"], baseline, BaselineFile));
        EngineSideReachCensus.AssertAtBaseline(["A\tget_Log", "B\tget_Areas"], baseline, BaselineFile);
    }
}
