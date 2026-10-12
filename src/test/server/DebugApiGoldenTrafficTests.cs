using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /nettraffic (plan_devapi_interfaces P2, group G6), written
/// before the traffic report moved behind IDebugNetTraffic.
/// </summary>
public class DebugApiGoldenTrafficTests
{
    internal static readonly NetTrafficReport Report = new(
        true, 12.5, 3,
        [
            new NetTrafficClass("ss_\"beam\"", 10, 4, 6, 9, 1.234, 0.5,
                [new NetTrafficField("Health", 2, 0.75), new NetTrafficField("Where", 3, 1.5)]),
            new NetTrafficClass("ss_idle", 7, 0, 0, 0, 0, double.NaN, []),
        ]);

    internal const string UnavailableJson =
        "{\"available\":false,\"note\":\"this build's server module does not report payload traffic\",\"classes\":[]}";

    internal const string SampledJson =
        """
        {"available":true,"sampling":true,"elapsedSeconds":12.5,"trackedEntities":3,"bitsPerChangedWord":39,"windowSeconds":10,"unit":"a changed payload dword costs 39 bits on the wire; one changed BYTE costs the whole word it sits in","fieldsNote":"a word holding two changed fields is charged to both, so the per-field column can exceed the class total","classes":[{"className":"ss_\"beam\"","entityFramesSeen":10,"entityFramesChanged":4,"wordsTouched":6,"bytesChanged":9,"bytesPerSecond":1.23,"recentBytesPerSecond":0.5,"wordFill":0.38,"fields":[{"name":"Health","words":2,"bytesPerSecond":0.75},{"name":"Where","words":3,"bytesPerSecond":1.5}]},{"className":"ss_idle","entityFramesSeen":7,"entityFramesChanged":0,"wordsTouched":0,"bytesChanged":0,"bytesPerSecond":0,"recentBytesPerSecond":0,"wordFill":0,"fields":[]}]}
        """;

    internal const string IdleJson =
        """
        {"available":true,"sampling":false,"note":"not sampling — set cs_sv_nettable_profile 1; figures below are whatever was measured last","elapsedSeconds":12.5,"trackedEntities":3,"bitsPerChangedWord":39,"windowSeconds":10,"unit":"a changed payload dword costs 39 bits on the wire; one changed BYTE costs the whole word it sits in","fieldsNote":"a word holding two changed fields is charged to both, so the per-field column can exceed the class total","classes":[{"className":"ss_\"beam\"","entityFramesSeen":10,"entityFramesChanged":4,"wordsTouched":6,"bytesChanged":9,"bytesPerSecond":1.23,"recentBytesPerSecond":0.5,"wordFill":0.38,"fields":[{"name":"Health","words":2,"bytesPerSecond":0.75},{"name":"Where","words":3,"bytesPerSecond":1.5}]},{"className":"ss_idle","entityFramesSeen":7,"entityFramesChanged":0,"wordsTouched":0,"bytesChanged":0,"bytesPerSecond":0,"recentBytesPerSecond":0,"wordFill":0,"fields":[]}]}
        """;

    private static DebugApiServer Server(Func<bool> sampling, Action? reset = null)
    {
        // G6: the report reaches the dev API through the server's IDebugNetTraffic.
        return NeverPumped.Server(TestDebugHooks.Server(traffic: () => Report with { Sampling = sampling() },
                                                      resetTraffic: reset));
    }

    [Fact]
    public void A_sampled_report_renders_exactly()
    {
        using var server = Server(() => true);
        Assert.Equal(SampledJson, Get(server, "/nettraffic").Body);
    }

    [Fact]
    public void An_idle_report_says_it_is_not_sampling()
    {
        using var server = Server(() => false);
        Assert.Equal(IdleJson, Get(server, "/nettraffic").Body);
    }

    [Fact]
    public void Reset_is_reachable_from_the_route_and_only_when_asked()
    {
        int resets = 0;
        using var server = Server(() => true, () => resets++);
        _ = Get(server, "/nettraffic");
        Assert.Equal(0, resets);
        Assert.Equal(SampledJson, Get(server, "/nettraffic?reset=1").Body);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void An_unwired_route_says_it_is_unavailable()
    {
        using var server = NeverPumped.Server();
        Assert.Equal(UnavailableJson, Get(server, "/nettraffic").Body);
    }
}
