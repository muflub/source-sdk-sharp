using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The crossing counters on <c>GET /status</c> — the half of step 0c's gate a
/// person actually reads (<c>debug-api status</c>).
///
/// <para>
/// Routed directly rather than through a socket, the way <c>DebugApiTests</c>
/// drives every other route: the socket adds nothing to what is being checked
/// here and would make these facts depend on a listener the build agent may not
/// be able to bind.
/// </para>
///
/// <para>
/// <b>Every fact opens its own interval.</b> The counters are process-wide, and
/// the assembly runs facts one at a time (<c>AssemblyInfo.cs</c>), so a
/// <see cref="_crossings.Reset"/> here scopes the poll to what this fact did.
/// </para>
/// </summary>
public class CrossingsStatusTests
{
    // This test class's own counters (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's counts.
    private readonly Crossings _crossings = new();

    /// <summary>
    /// NOT BEHIND THE GAME THREAD. The counters are lock-free, so the poll
    /// answers from a game that has stopped ticking — which is exactly the
    /// session somebody would be reading them in. A crossings field that only
    /// appeared when the game was healthy would be missing whenever it mattered.
    /// </summary>
    [Fact]
    public void StatusCarriesTheCountersWithNoGameThreadAtAll()
    {
        string body = Status();

        Assert.Contains("\"ticking\":false", body);
        Assert.Contains("\"crossings\":", body);
    }

    /// <summary>
    /// Inbound only, said in the payload. A consumer that read the total as the
    /// whole boundary cost would be reading a number true of half of it; the
    /// managed-&gt;native direction belongs to step 0e's engine-boundary probes.
    /// </summary>
    [Fact]
    public void StatusSaysTheCountersAreInboundOnly()
    {
        Assert.Contains("\"direction\":\"inbound\"", Status());
    }

    [Fact]
    public void StatusReportsAnOpItHasSeen()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerFrame);
        _crossings.Count(CrossingOp.ServerFrame);

        Assert.Contains("\"ServerFrame\":2", Status());
    }

    /// <summary>
    /// Only the ops that have been entered. 162 ops polled once a second by
    /// every open page, 140 of them zero, is how a field stops being read — and
    /// an op absent from <c>ops</c> has a count of zero, not an unknown one.
    /// </summary>
    [Fact]
    public void StatusOmitsOpsNothingHasEntered()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerFrame);

        Assert.DoesNotContain("\"ClientFrame\"", Status());
    }

    /// <summary>
    /// The denominator every budget in the plan is stated over, as its own field
    /// rather than something the consumer has to know an op name to find.
    /// </summary>
    [Fact]
    public void StatusCarriesTheServerFrameDenominator()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerFrame);
        _crossings.Count(CrossingOp.ServerFrame);
        _crossings.Count(CrossingOp.ServerFrame);

        Assert.Contains("\"serverFrames\":3", Status());
    }

    [Fact]
    public void StatusCarriesTheClientFrameDenominator()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ClientFrame);

        Assert.Contains("\"clientFrames\":1", Status());
    }

    [Fact]
    public void StatusCarriesTheTotal()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerFrame);
        _crossings.Count(CrossingOp.ServerThink);

        Assert.Contains("\"total\":2", Status());
    }

    /// <summary>
    /// Two polls with different reset counts are not two points on one curve. A
    /// consumer that subtracted them across a reset would report a negative
    /// rate, so the interval identity travels with the numbers.
    /// </summary>
    [Fact]
    public void StatusCarriesTheResetCount()
    {
        _crossings.Reset();

        Assert.Contains($"\"resets\":{_crossings.Resets}", Status());
    }

    [Fact]
    public void StatusCarriesHowManyOpsThereAre()
    {
        Assert.Contains($"\"opCount\":{Crossings.OpCount}", Status());
    }

    /// <summary>The help page names what the route now answers.</summary>
    [Fact]
    public void TheHelpPageMentionsTheCounters()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());
        HttpSocketServer.TryParse("GET / HTTP/1.1\r\n\r\n", out var request);

        Assert.Contains("crossing", server.Route(request).Body);
    }

    private string Status()
    {
        var server = NeverPumped.Server(_crossings);
        HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n\r\n", out var request);

        return server.Route(request).Body;
    }
}
