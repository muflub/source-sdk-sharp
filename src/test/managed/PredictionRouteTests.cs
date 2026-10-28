extern alias client;
using PredictionComponent = client::SourceSharp.Abi.PredictionComponent;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using PredictionFieldError = client::SourceSharp.Abi.PredictionFieldError;
using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The /prediction route: the client's prediction misses, reported down the
/// server's socket.
///
/// <para>
/// THE ROUTE IS THE ARCHITECTURE MADE VISIBLE. Prediction runs on the client,
/// the dev API is built only by the server module, and there is one socket per
/// process. The client's ring is its own composition's (statics to zero), so
/// the server can reach it only through the debug channel. The cases below
/// hold that arrangement to its two obligations: it must answer without the
/// game thread, and it must never let an absent client read as a healthy one.
/// </para>
/// </summary>
public class PredictionRouteTests
{
    // A client in this process, stamped: its build is its own side state's
    // (statics to zero), which nothing on the server side can reach.
    private readonly BuildInfo _clientBuild = new();

    // And its prediction ring, likewise its own composition's.
    private readonly PredictionErrorRing _clientRing = new();

    //-------------------------------------------------------------------------
    // The JSON, over a ring the test owns. No globals.
    //-------------------------------------------------------------------------

    private static PredictionFieldError Origin(double delta, float tolerance = 0.0625f)
        => new("Origin", 2, 56, 12, tolerance, Math.Abs(delta) <= tolerance && tolerance > 0f, 8,
               new[]
               {
                   new PredictionComponent(0, 0, 0),
                   new PredictionComponent(0, 0, 0),
                   new PredictionComponent(64, 64 - delta, delta),
               });

    private static PredictionErrorRecord Record(long sequence, int command,
                                                params PredictionFieldError[] fields)
    {
        int standing = 0;
        int tolerated = 0;

        foreach (var field in fields)
        {
            if (field.Tolerated)
                tolerated++;
            else
                standing += field.DifferingBytes;
        }

        return new PredictionErrorRecord(sequence, command, 12, "player", 120,
            standing, 56, tolerated, DateTime.UnixEpoch, fields);
    }

    private static PredictionErrorRing RingWith(params PredictionErrorRecord[] records)
    {
        var ring = new PredictionErrorRing(64);

        foreach (var record in records)
            ring.Add(record);

        return ring;
    }

    [Fact]
    public void AQuietRingIsReportedAsAvailableAndEmptyRatherThanAsMissing()
    {
        // The good state, and it must read as good. A client whose prediction is
        // holding produces no records at all, and a reply that looked like a
        // failure to answer would send a reader hunting for a broken tool.
        string body = PredictionJson.Report((new PredictionErrorRing(8)).ToMirror(), 0, 64);

        Assert.Contains("\"available\":true", body);
        Assert.Contains("\"held\":0", body);
        Assert.Contains("\"records\":[]", body);
    }

    [Fact]
    public void ThePayloadSaysWhatItIsCountingBecauseTheDenominatorIsNotPackets()
    {
        // A compare that HELD byte for byte is never recorded, so "observed" is
        // not the packet count and "mispredicted/observed" is not the game's
        // misprediction rate. A reader who assumes otherwise reads a healthy
        // game as a broken one, so the reply says so itself.
        string body = PredictionJson.Report((new PredictionErrorRing(8)).ToMirror(), 0, 64);

        Assert.Contains("held byte for byte is never recorded", body);
    }

    [Fact]
    public void TheTotalsAreSentApartFromTheRecordsTheRingStillHolds()
    {
        // A rate computed from a ring that has forgotten most of a session would
        // read as though the game had only ever done as many compares as the
        // ring is deep. The totals have to survive the eviction.
        var ring = new PredictionMirror(2);

        for (int i = 0; i < 9; i++)
            ring.Add(Record(i, 1000 + i, Origin(4.0)).ToWire());

        string body = PredictionJson.Report(ring, 0, 64);

        Assert.Contains("\"observed\":9", body);
        Assert.Contains("\"held\":2", body);
    }

    [Fact]
    public void AFieldIsReportedWithTheToleranceItsDeclarationStates()
    {
        string body = PredictionJson.Report((RingWith(Record(0, 23824, Origin(0.31)))).ToMirror(), 0, 64);

        Assert.Contains("\"name\":\"Origin\"", body);
        Assert.Contains("\"tolerance\":0.0625", body);
    }

    [Fact]
    public void TheToleranceRatioIsCarriedSoDivergenceIsReadRatherThanComputed()
    {
        // 0.5 against a tolerance of 0.0625 is eight times out. The ratio is the
        // one number that is comparable between Origin and Velocity, whose units
        // and limits are not.
        string body = PredictionJson.Report((RingWith(Record(0, 1, Origin(0.5)))).ToMirror(), 0, 64);

        Assert.Contains("\"toleranceRatio\":8", body);
    }

    [Fact]
    public void AFieldWithNoToleranceSendsNullAndNotZero()
    {
        // JSON null, literally. A zero would decode as a number and render as a
        // field sitting comfortably inside a limit it does not have.
        string body = PredictionJson.Report(
            (RingWith(Record(0, 1, Origin(3.0, tolerance: 0f)))).ToMirror(), 0, 64);

        Assert.Contains("\"toleranceRatio\":null", body);
        Assert.DoesNotContain("\"toleranceRatio\":0", body);
    }

    [Fact]
    public void NumbersCarryEnoughPlacesToBeReadAgainstAToleranceOfAnEighthOfASixteenth()
    {
        // THE PRECISION IS PART OF THE ANSWER. Origin's tolerance is 0.0625, so
        // the two decimal places the traffic report uses would round 0.0624 and
        // 0.0626 to the same "0.06" — one inside the limit and one outside,
        // rendered identically. This is the whole view failing quietly.
        string body = PredictionJson.Report((RingWith(Record(0, 1, Origin(0.0624)))).ToMirror(), 0, 64);

        Assert.Contains("0.0624", body);
    }

    [Fact]
    public void AToleratedDifferenceIsReportedAsAHoldAndStillSent()
    {
        // Tolerated drift that is growing is the early warning for drift that
        // will not be. It must be present AND must not be counted as a miss.
        string body = PredictionJson.Report((RingWith(Record(0, 1, Origin(0.03)))).ToMirror(), 0, 64);

        Assert.Contains("\"tolerated\":true", body);
        Assert.Contains("\"mispredicted\":false", body);
        Assert.Contains("\"toleratedOnly\":1", body);
    }

    [Fact]
    public void AByteNoFieldClaimsIsReportedAsPaddingRatherThanAsAKindZeroInt()
    {
        // Kind -1 is not an int. Naming it one would attribute a layout
        // disagreement to a field that did not move.
        var padding = new PredictionFieldError("(padding@57)", -1, 57, 1, 0f, false, 1,
                                               Array.Empty<PredictionComponent>());

        string body = PredictionJson.Report((RingWith(Record(0, 1, padding))).ToMirror(), 0, 64);

        Assert.Contains("\"kindName\":\"padding\"", body);
        Assert.Contains("\"components\":[]", body);
    }

    [Fact]
    public void AFollowerCanAskForOnlyTheRecordsItHasNotSeen()
    {
        var ring = RingWith(Record(0, 100, Origin(4)), Record(1, 101, Origin(4)),
                            Record(2, 102, Origin(4)));

        string body = PredictionJson.Report(ring.ToMirror(), 2, 64);

        Assert.Contains("\"command\":102", body);
        Assert.DoesNotContain("\"command\":100", body);
    }

    [Fact]
    public void ThePayloadNamesTheSequenceAFollowerShouldAskFromNext()
    {
        // Without it a follower has to infer the next cursor from the last
        // record it happened to receive, which is wrong the moment a reply is
        // capped or empty.
        var ring = RingWith(Record(0, 100, Origin(4)), Record(1, 101, Origin(4)));

        Assert.Contains("\"nextSequence\":2", PredictionJson.Report(ring.ToMirror(), 0, 64));
    }

    [Fact]
    public void AProcessWithNoClientSaysSoRatherThanAnsweringAnEmptyList()
    {
        // THE DISTINCTION THE WHOLE ROUTE TURNS ON. A dedicated server has no
        // client and therefore no prediction; an empty list would read exactly
        // like a client whose prediction is holding perfectly, and the two mean
        // opposite things.
        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        string body = Reply(server, "/prediction").Body;

        Assert.Contains("\"available\":false", body);
        Assert.Contains("prediction is client-side", body);
    }

    /// <summary>
    /// The user's ruling (plan_devapi_interfaces §5.8, Q1): a client in this
    /// process does not make the server read its ring. Its prediction reaches
    /// the server over the debug channel, or is read from the client's own dev
    /// API.
    /// </summary>
    [Fact]
    public void AProcessWithAClientIsStillAnsweredAsServerSideOnly()
    {
        _clientBuild.SetManaged(Stamped(), Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        string body = Reply(server, "/prediction").Body;

        Assert.Contains("\"available\":false", body);
    }

    [Fact]
    public void TheRouteAnswersWithoutWaitingForAGameFrame()
    {
        // A DELIBERATE PROPERTY AND NOT AN ACCIDENT. This is the view somebody
        // keeps open beside a game that is misbehaving, and a game that is
        // hitching is exactly when its frames are worst to wait on. The ring is
        // locked and its records are immutable once made, so nothing here needs
        // the game thread — and this server is never pumped.
        _clientBuild.SetManaged(Stamped(), Array.Empty<string>());

        using var server = NeverPumped.Server();

        // A route that needed the game thread on this never-pumped server
        // answers the timeout instead — /instances is the comparison, and
        // it is here so the case is measuring the difference rather than
        // asserting that a string appears.
        Assert.Contains("timed out", Reply(server, "/instances").Body);

        string body = Reply(server, "/prediction").Body;

        // "available" and not "records", because both answers carry an
        // empty records array -- a case asserting only that would stay green
        // under a route that refused.
        Assert.Contains("\"available\":", body);
        Assert.DoesNotContain("timed out", body);
    }

    /// <summary>
    /// A reset clears what the server holds (its channel mirror: see
    /// DebugChannelRouteTests) and never the client's own ring (Q1).
    /// </summary>
    [Fact]
    public void TheRoutesResetLeavesTheClientsOwnRingAlone()
    {
        _clientBuild.SetManaged(Stamped(), Array.Empty<string>());
        _clientRing.Add(Record(0, 1, Origin(4)));

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        int before = _clientRing.Count;
        Assert.True(before > 0, "the client's ring holds a record, so a reset that reached it would show");
        _ = Reply(server, "/prediction?reset=1");
        Assert.Equal(before, _clientRing.Count);
    }

    [Fact]
    public void TheRouteIsListedInTheHelpSoItCanBeFound()
    {
        // A route nobody is told about is a route nobody uses. /help is where
        // every other one is named.
        //
        // THE WHOLE KEY AND NOT THE PREFIX. "/prediction" is a substring of
        // "/predictionXX", so a case looking only for that stays green while the
        // listing names a route that does not exist — which is how this one was
        // first written and what mutating it caught.
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        Assert.Contains("\"GET  /prediction?since=N&max=M&reset=1\"", Reply(server, "/").Body);
    }

    private static BuildHalf Stamped()
        => new(StampOfThisTest(), GitProvenance.Unknown, "test", false);

    /// <summary>A stamp that is merely KNOWN, which is all the route asks about.</summary>
    private static BuildStamp StampOfThisTest()
    {
        var builder = new BuildStampBuilder();
        builder.Add("prediction-route-test", new byte[] { 1, 2, 3, 4 }, DateTime.UnixEpoch);
        return builder.Build();
    }

    private static HttpReply Reply(DebugApiServer server, string path)
    {
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }
}
