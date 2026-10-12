extern alias client;
using PredictionComponent = client::SourceSharp.Abi.PredictionComponent;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using PredictionFieldError = client::SourceSharp.Abi.PredictionFieldError;
using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The framing that carries a remote client's prediction ring to the server's
/// dev API, and the bounds that keep it from being a way to exhaust one.
/// </summary>
public class DebugChannelCodecTests
{
    private static PredictionFieldError Origin(double delta, float tolerance = 0.0625f)
        => new("Origin", 2, 56, 12, tolerance, false, 8,
               new[]
               {
                   new PredictionComponent(1, 2, -1),
                   new PredictionComponent(0, 0, 0),
                   new PredictionComponent(64, 64 - delta, delta),
               });

    private static PredictionErrorRecord Record(long sequence = 7, int command = 412)
        => new(sequence, command, 12, "ss_player", 120, 8, 56, 1, DateTime.UnixEpoch,
               new[] { Origin(3.25) });

    //-------------------------------------------------------------------------
    // The round trip. What the mirror ring on the server has to be able to say.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARecordSurvivesTheRoundTripWithItsSequence()
    {
        byte[] frame = DebugChannelCodec.PredictionBatch((new[] { Record(sequence: 91) }).ToWire());

        Assert.True(DebugChannelCodec.TryReadPredictionBatch(frame, out var back)
                 && back[0].Sequence == 91,
            "a follower asks for everything after a sequence, so the sequence has to survive");
    }

    [Fact]
    public void TheAcknowledgedCommandSurvivesBecauseItIsTheAxisOfTheDriftView()
    {
        byte[] frame = DebugChannelCodec.PredictionBatch((new[] { Record(command: 8123) }).ToWire());

        DebugChannelCodec.TryReadPredictionBatch(frame, out var back);

        Assert.True(back[0].Command == 8123, "the command a compare judged is the x axis of the view");
    }

    [Fact]
    public void TheClassNameSurvives()
    {
        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire()), out var back);

        Assert.True(back[0].ClassName == "ss_player",
            "a report that could not name the class would send a reader to the type id table");
    }

    [Fact]
    public void APerComponentDeltaSurvivesExactly()
    {
        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire()), out var back);

        Assert.True(back[0].Fields[0].Components[2].Delta == 3.25,
            "the whole point of the visualiser is per-component deltas; a lossy hop would " +
            "make a remote client's report say something different from a local one's");
    }

    [Fact]
    public void TheDeclaredToleranceSurvivesSoTheRatioStillMeansSomething()
    {
        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire()), out var back);

        Assert.True(back[0].Fields[0].Tolerance == 0.0625f,
            "ToleranceRatio is computed from it, and a tolerance that did not survive would " +
            "turn every ratio into null");
    }

    [Fact]
    public void ToleratedIsCarriedAsItsOwnFactAndNotInferred()
    {
        var record = new PredictionErrorRecord(1, 1, 3, "ss_thing", 8, 0, 0, 1, DateTime.UnixEpoch,
            new[] { new PredictionFieldError("Velocity", 2, 0, 12, 0.5f, true, 4,
                                             Array.Empty<PredictionComponent>()) });

        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { record }).ToWire()), out var back);

        Assert.True(back[0].Fields[0].Tolerated,
            "a forgiven difference and a standing one mean opposite things and must not be " +
            "re-derived at the far end from numbers that were rounded on the way");
    }

    [Fact]
    public void TheTimestampSurvivesAsUtc()
    {
        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire()), out var back);

        Assert.True(back[0].TimeUtc == DateTime.UnixEpoch && back[0].TimeUtc.Kind == DateTimeKind.Utc,
            "the JSON renders it as an ISO instant, so a kind that arrived Unspecified would " +
            "be rendered as though it were local time");
    }

    [Fact]
    public void AStringFieldWithNoComponentsRoundTripsAsHavingNone()
    {
        var record = new PredictionErrorRecord(1, 1, 3, "ss_thing", 8, 4, 0, 0, DateTime.UnixEpoch,
            new[] { new PredictionFieldError("Name", 4, 0, 8, 0f, false, 4,
                                             Array.Empty<PredictionComponent>()) });

        DebugChannelCodec.TryReadPredictionBatch(
            DebugChannelCodec.PredictionBatch((new[] { record }).ToWire()), out var back);

        Assert.True(back[0].Fields[0].Components.Count == 0,
            "subtracting text produces a number that means nothing; an empty list is the " +
            "honest answer and it has to survive as one");
    }

    //-------------------------------------------------------------------------
    // The bounds. These bytes come off a socket.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A batch of one more than the limit, WITH EVERY RECORD ACTUALLY THERE.
    ///
    /// <para>
    /// The first version of this handed over a count of a million and five bytes,
    /// and it passed with the bound deleted — because the read then failed on the
    /// first record instead. It was testing truncation under the name of the
    /// bound. A batch that would parse perfectly if the limit were gone is what
    /// makes the limit the thing under test.
    /// </para>
    /// </summary>
    [Fact]
    public void ABatchOfMoreRecordsThanTheLimitIsRefusedEvenThoughEveryOneOfThemIsThere()
    {
        // One record's bytes, taken from a batch the writer built and stripped of
        // its kind byte and count.
        byte[] one = DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire())[5..];

        int count = DebugChannelCodec.MaxRecordsPerBatch + 1;
        var frame = new List<byte> { (byte)DebugChannelMessage.Prediction };
        frame.AddRange(BitConverter.GetBytes(count));

        for (int i = 0; i < count; i++)
            frame.AddRange(one);

        Assert.True(!DebugChannelCodec.TryReadPredictionBatch(frame.ToArray(), out _),
            "a count read off the wire and used to size a list is how a debug tool becomes a " +
            "way to exhaust a process; the limit is what refuses this, because everything the " +
            "count promised really is in the frame");
    }

    [Fact]
    public void ABatchExactlyAtTheLimitIsAccepted()
    {
        byte[] one = DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire())[5..];

        int count = DebugChannelCodec.MaxRecordsPerBatch;
        var frame = new List<byte> { (byte)DebugChannelMessage.Prediction };
        frame.AddRange(BitConverter.GetBytes(count));

        for (int i = 0; i < count; i++)
            frame.AddRange(one);

        Assert.True(DebugChannelCodec.TryReadPredictionBatch(frame.ToArray(), out var records)
                 && records.Count == count,
            "the limit is a limit and not an off-by-one refusal of the largest batch a " +
            "correct client will actually send");
    }

    [Fact]
    public void ABatchWhoseRecordsRunOffTheEndIsRefusedWhole()
    {
        byte[] frame = new byte[5];
        frame[0] = (byte)DebugChannelMessage.Prediction;
        BitConverter.TryWriteBytes(frame.AsSpan(1), 3);

        Assert.True(!DebugChannelCodec.TryReadPredictionBatch(frame, out var records)
                 && records.Count == 0,
            "a truncated batch yields nothing at all, rather than the records that happened to " +
            "fit before the frame ran out");
    }

    [Fact]
    public void AStringClaimingMoreBytesThanTheFrameHoldsIsRefused()
    {
        byte[] good = DebugChannelCodec.PredictionBatch((new[] { Record() }).ToWire());
        byte[] frame = (byte[])good.Clone();

        // The class name's length prefix. It sits after the kind byte, the
        // count, the sequence, the command and the type id.
        int at = 1 + 4 + 8 + 4 + 4;
        BitConverter.TryWriteBytes(frame.AsSpan(at), 200);

        Assert.True(DebugChannelCodec.TryReadPredictionBatch(good, out _)
                 && !DebugChannelCodec.TryReadPredictionBatch(frame, out _),
            "the length is checked against what is actually there before the slice, and the " +
            "unedited frame parsing is what says this case edited the right four bytes");
    }

    [Fact]
    public void AnEmptyFrameIsNotAKind()
    {
        Assert.True(DebugChannelCodec.KindOf(Array.Empty<byte>()) is null,
            "a zero-length frame has no kind byte to read, and reading one would be the first " +
            "out-of-range access on this path");
    }

    //-------------------------------------------------------------------------
    // THE COMMAND SURFACE. Bounded, and bounded where the bytes are understood.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryDeclaredCommandRoundTrips()
    {
        foreach (var command in new[]
                 {
                     DebugChannelCommand.Ping,
                     DebugChannelCommand.ResetPredictionRing,
                     DebugChannelCommand.SendPredictionNow,
                     DebugChannelCommand.Disconnect,
                 })
        {
            Assert.True(DebugChannelCodec.TryReadCommand(DebugChannelCodec.Command(command), out var back)
                     && back == command,
                $"{command} is one of the four operations a server may ask for");
        }
    }

    /// <summary>
    /// A byte that is not one of the four is refused, not passed through.
    ///
    /// <para>
    /// The bound on what a server may ask for is only a bound if it is enforced
    /// where the bytes are understood. Handing an unknown value to a switch and
    /// relying on it having no case is a bound that survives exactly as long as
    /// nobody adds a default.
    /// </para>
    /// </summary>
    [Fact]
    public void ACommandByteThatIsNotOneOfTheFourIsRefused()
    {
        byte[] frame = { (byte)DebugChannelMessage.Command, 99 };

        Assert.True(!DebugChannelCodec.TryReadCommand(frame, out _),
            "the command surface is an enumeration, and a value outside it is not a command");
    }

    [Fact]
    public void ZeroIsNotACommandEitherBecauseTheEnumStartsAtOne()
    {
        byte[] frame = { (byte)DebugChannelMessage.Command, 0 };

        Assert.True(!DebugChannelCodec.TryReadCommand(frame, out _),
            "the operations are numbered from one so a zeroed buffer is not a valid command");
    }

    [Fact]
    public void ACommandFrameWithNoCommandByteIsRefused()
    {
        Assert.True(!DebugChannelCodec.TryReadCommand(
                new[] { (byte)DebugChannelMessage.Command }, out _),
            "a command frame that stopped after its kind byte has nothing to honour");
    }

    //-------------------------------------------------------------------------
    // The router, which is what a session does with a frame.
    //-------------------------------------------------------------------------
    private sealed class Sink : IDebugChannelSink
    {
        public readonly List<WirePredictionRecord> Records = new();
        public readonly List<string> Malformed = new();
        public int Hellos;

        void IDebugChannelSink.Prediction(IReadOnlyList<WirePredictionRecord> records)
            => Records.AddRange(records);

        void IDebugChannelSink.Hello(int protocol, string build) => Hellos++;

        void IDebugChannelSink.Malformed(string why) => Malformed.Add(why);

        public readonly List<(int Request, int Status, string Body)> Replies = new();

        void IDebugChannelSink.Reply(int requestId, int status, string body) => Replies.Add((requestId, status, body));
    }

    //-------------------------------------------------------------------------
    // Queries and replies (plan_side_isolation §2.6): the server's dev API
    // proxying /client/{slot}/... over the channel.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_query_reads_back_its_request_id_and_question()
    {
        Assert.True(DebugChannelCodec.TryReadQuery(DebugChannelCodec.Query(41, DebugChannelQuery.Status),
                                                   out int request, out var query, out string argument));
        Assert.Equal(41, request);
        Assert.Equal(DebugChannelQuery.Status, query);
        Assert.Equal("", argument);
    }

    [Fact]
    public void A_query_without_an_argument_writes_the_bytes_it_always_did()
    {
        // The frame before arguments existed: kind, request id, question.
        Assert.Equal("050900000001", DebugChannelWireHex(DebugChannelCodec.Query(9, DebugChannelQuery.Status)));
        Assert.Equal("050900000001", DebugChannelWireHex(DebugChannelCodec.Query(9, DebugChannelQuery.Status, "")));
    }

    [Fact]
    public void A_query_argument_reads_back()
    {
        byte[] frame = DebugChannelCodec.Query(5, DebugChannelQuery.Prediction, "player");
        Assert.True(DebugChannelCodec.TryReadQuery(frame, out int request, out var query, out string argument));
        Assert.Equal((5, DebugChannelQuery.Prediction, "player"), (request, query, argument));
    }

    [Fact]
    public void A_query_argument_longer_than_its_bound_is_refused_on_read()
    {
        // Hand-built: a writer cuts to the bound, so only a hostile sender
        // produces this; the reader must refuse it rather than allocate.
        var frame = new List<byte>(DebugChannelCodec.Query(5, DebugChannelQuery.Status));
        frame.AddRange(BitConverter.GetBytes(DebugChannelCodec.MaxStringBytes + 1));
        frame.AddRange(new byte[DebugChannelCodec.MaxStringBytes + 1]);

        Assert.False(DebugChannelCodec.TryReadQuery(frame.ToArray(), out _, out _, out _));
        // The positive arm: the same frame at the bound is accepted.
        var at = new List<byte>(DebugChannelCodec.Query(5, DebugChannelQuery.Status));
        at.AddRange(BitConverter.GetBytes(DebugChannelCodec.MaxStringBytes));
        at.AddRange(new byte[DebugChannelCodec.MaxStringBytes]);
        Assert.True(DebugChannelCodec.TryReadQuery(at.ToArray(), out _, out _, out _));
    }

    [Fact]
    public void A_query_with_bytes_after_its_argument_is_refused()
    {
        byte[] frame = [.. DebugChannelCodec.Query(1, DebugChannelQuery.Status, "7"), 0];

        Assert.False(DebugChannelCodec.TryReadQuery(frame, out _, out _, out _));
    }

    private static string DebugChannelWireHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void A_question_outside_the_closed_set_is_refused()
    {
        byte[] frame = DebugChannelCodec.Query(1, DebugChannelQuery.Status);
        frame[^1] = 0xEE;

        Assert.False(DebugChannelCodec.TryReadQuery(frame, out _, out _, out _));
    }

    [Fact]
    public void A_query_with_trailing_bytes_is_refused()
    {
        byte[] frame = [.. DebugChannelCodec.Query(1, DebugChannelQuery.Status), 0];

        Assert.False(DebugChannelCodec.TryReadQuery(frame, out _, out _, out _));
    }

    [Fact]
    public void A_reply_carries_a_body_longer_than_a_string_field()
    {
        string body = "{\"x\":\"" + new string('a', 10_000) + "\"}";

        Assert.True(DebugChannelCodec.TryReadReply(DebugChannelCodec.Reply(7, 200, body),
                                                   out int request, out int status, out string read));
        Assert.Equal((7, 200, body), (request, status, read));
    }

    [Fact]
    public void A_reply_reaches_the_sink_and_a_query_from_a_client_does_not()
    {
        var sink = new Sink();

        DebugChannelRouter.Route(DebugChannelCodec.Reply(3, 200, "{}"), sink);
        DebugChannelRouter.Route(DebugChannelCodec.Query(4, DebugChannelQuery.Status), sink);

        Assert.Equal([(3, 200, "{}")], sink.Replies);
        Assert.Single(sink.Malformed);
    }

    [Fact]
    public void APredictionBatchReachesTheSink()
    {
        var sink = new Sink();

        DebugChannelRouter.Route(
            DebugChannelCodec.PredictionBatch((new[] { Record(), Record(sequence: 8) }).ToWire()), sink);

        Assert.True(sink.Records.Count == 2, "both records are handed over, in one batch");
    }

    [Fact]
    public void AHelloReachesTheSink()
    {
        var sink = new Sink();

        DebugChannelRouter.Route(DebugChannelCodec.Hello("20:31 abcdef0"), sink);

        Assert.True(sink.Hellos == 1, "the opening message says which build is on the other end");
    }

    /// <summary>
    /// Commands travel server to client and only that way.
    ///
    /// <para>
    /// A client sending one is a client trying to drive the server. The channel
    /// is asymmetric on purpose: the server collects, the client reports, and
    /// the four operations run on the client.
    /// </para>
    /// </summary>
    [Fact]
    public void ACommandArrivingFromAClientIsRefusedByTheServer()
    {
        var sink = new Sink();

        DebugChannelRouter.Route(DebugChannelCodec.Command(DebugChannelCommand.Ping), sink);

        Assert.True(sink.Malformed.Count == 1 && sink.Records.Count == 0,
            "commands go one way; one arriving the other way is dropped and said out loud");
    }

    [Fact]
    public void AnUnknownFrameKindIsDroppedRatherThanGuessedAt()
    {
        var sink = new Sink();

        DebugChannelRouter.Route(new byte[] { 200, 1, 2, 3 }, sink);

        Assert.True(sink.Malformed.Count == 1, "a kind this side does not know is not a payload");
    }
}
