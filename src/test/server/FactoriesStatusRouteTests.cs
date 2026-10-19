using System.Text.Json;
using Debug.Api;
using SourceSharp.Server;
using Debug.Api.Server.Interfaces;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The <c>factories</c> block of <c>/status</c> — the managed side's view of the
/// NATIVE entity factory dictionary, reachable since the ABI 242 slots landed.
///
/// <para>
/// <b>WHY IT IS A ROUTE AND NOT A LOG LINE.</b> The `sub-factory` lane's
/// handover names this as the one thing it could not do: "assert the managed
/// table against the real dictionary in an automated gate — that is the same
/// slot". <c>dumpentityfactories</c> counts the dictionary on the NATIVE side of
/// the wire and <c>native</c> here counts it on the MANAGED side, so a gate can
/// hold the two against each other. A grep over console output cannot: it passes
/// on a renamed line and cannot fail on a deleted one.
/// </para>
///
/// <para>
/// <b><c>available</c> IS NOT <c>native == 0</c>, AND THAT IS THE WHOLE POINT.</b>
/// False means the dictionary has not been asked — the slots are unbound, which
/// is what every build before ABI 242 and every client reports, and
/// <c>EntityFactories.OwnerOf</c> reads it as "assume native". True with an empty
/// table means it answered NOTHING, which refuses classnames that work. A reader
/// that collapses the two has lost the only distinction here that changes what
/// the game does, so both are on the wire.
/// </para>
/// </summary>
public class FactoriesStatusRouteTests
{
    private static JsonElement Factories(
        Func<(bool, int, int, int, int)>? report = null)
    {
        // G7: the report reaches the dev API as the server's IDebugStatus counters.
        var server = NeverPumped.Server(TestDebugHooks.Server(counters: () =>
        {
            if (report is null)
                return DebugStatus.ZeroCounters();
            var (available, native, refused, times, unanswered) = report();
            return DebugStatus.ZeroCounters() with { Factories = new DebugFactories(available, native, refused, times, unanswered) };
        }));

        HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n\r\n", out var request);

        return JsonDocument.Parse(server.Route(request).Body)
                           .RootElement.GetProperty("factories");
    }

    /// <summary>
    /// With nobody reporting — a client, or a build whose native half does not
    /// fill the slots — the route says the dictionary was NOT ASKED.
    /// </summary>
    [Fact]
    public void AnUnreportedDictionaryIsUnavailableRatherThanEmpty()
        => Assert.False(Factories().GetProperty("available").GetBoolean());

    [Fact]
    public void AnUnreportedDictionaryCountsZero()
        => Assert.Equal(0, Factories().GetProperty("native").GetInt32());

    [Fact]
    public void TheReportedCountReachesTheWire()
        => Assert.Equal(156,
            Factories(() => (true, 156, 3, 36, 3)).GetProperty("native").GetInt32());

    [Fact]
    public void TheReportedAvailabilityReachesTheWire()
        => Assert.True(
            Factories(() => (true, 156, 3, 36, 3)).GetProperty("available").GetBoolean());

    /// <summary>
    /// AN AVAILABLE BUT EMPTY TABLE IS REPORTABLE, and reads differently from an
    /// unavailable one. If these two produced the same JSON there would be no
    /// point carrying <c>available</c> at all.
    /// </summary>
    [Fact]
    public void AnEmptyButAskedDictionaryIsDistinguishableFromAnUnaskedOne()
    {
        var asked = Factories(() => (true, 0, 0, 0, 0));
        var unasked = Factories();

        Assert.True(asked.GetProperty("available").GetBoolean());
        Assert.False(unasked.GetProperty("available").GetBoolean());
        Assert.Equal(asked.GetProperty("native").GetInt32(),
                     unasked.GetProperty("native").GetInt32());
    }

    /// <summary>
    /// The refusal ledger crosses as a PAIR: distinct classnames, and the total
    /// number of asks. On dm_lockdown that is 3 and 36 — two <c>Create</c> calls
    /// per residue block — and the count is stable across map reloads where the
    /// tally doubles, so either alone answers a different question.
    /// </summary>
    [Fact]
    public void TheRefusalLedgerCrossesAsCountAndTally()
    {
        var f = Factories(() => (true, 156, 3, 36, 3));

        Assert.Equal(3, f.GetProperty("refused").GetInt32());
        Assert.Equal(36, f.GetProperty("refusalTimes").GetInt32());
    }

    [Fact]
    public void TheMapsUnansweredClassCountCrosses()
        => Assert.Equal(3,
            Factories(() => (true, 156, 3, 36, 3)).GetProperty("unanswered").GetInt32());

    /// <summary>
    /// The five numbers are carried in their own fields and not folded together
    /// — a report whose fields all read the same value would pass every fact
    /// above that checks one of them.
    /// </summary>
    [Fact]
    public void TheFiveFieldsAreDistinct()
    {
        var f = Factories(() => (true, 11, 22, 33, 44));

        Assert.Equal(11, f.GetProperty("native").GetInt32());
        Assert.Equal(22, f.GetProperty("refused").GetInt32());
        Assert.Equal(33, f.GetProperty("refusalTimes").GetInt32());
        Assert.Equal(44, f.GetProperty("unanswered").GetInt32());
    }
}
