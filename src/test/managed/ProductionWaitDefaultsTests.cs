extern alias client;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using System;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The waits the game runs with. Tests shorten these per instance (NeverPumped
/// and friends) so the suite does not sit out real timers; these facts are
/// what keeps that from reaching the game: an edit to a default is a change to
/// runtime behaviour and has to be made here as well, on purpose.
/// </summary>
public class ProductionWaitDefaultsTests
{
    [Fact]
    public void TheDebugApiServerWaitsFiveSecondsForTheGameThread()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        Assert.Equal(TimeSpan.FromSeconds(5), server.WorkTimeout);
    }

    [Fact]
    public void TheDebugApiServerWaitsHalfASecondForStatus()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        Assert.Equal(TimeSpan.FromMilliseconds(500), server.StatusTimeout);
    }

    [Fact]
    public void TheConsoleTailPollsEveryTwoHundredMilliseconds()
    {
        using var tail = new ConsoleTail(new LogRing(8), "/nonexistent/console.log", new RecordingLog().Warning);
        Assert.Equal(TimeSpan.FromMilliseconds(200), tail.PollInterval);
    }

    [Fact]
    public void TheDebugChannelPollsEveryQuarterSecond()
    {
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        Assert.Equal(TimeSpan.FromMilliseconds(250), client.PollInterval);
    }

    [Fact]
    public void TheTestServerIsShorterThanTheGamesSoTheSplitIsReal()
    {
        // NeverPumped's server must actually differ from the production one, or
        // the facts above would pin the value tests run with.
        using var fast = NeverPumped.Server();
        using var real = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        Assert.True(fast.WorkTimeout < real.WorkTimeout);
    }
}
