extern alias client;
using PredictionFieldError = client::SourceSharp.Abi.PredictionFieldError;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What <c>/prediction</c> answers once a debug channel exists, and — the part
/// that matters more — what it answers when one exists and a client is already
/// in this process.
///
/// <para>
/// THE LISTEN SERVER MUST NOT CHANGE. When both halves share a process the ring
/// in <c>SourceSharp.Abi</c> IS the client's ring: the same storage location, no
/// socket, no copy, nothing a frame behind. A channel that happened to be open
/// as well must not become the route the data takes, and the case below is what
/// holds that — it puts a DIFFERENT record in each ring and asks which one comes
/// back.
/// </para>
/// </summary>
public class DebugChannelRouteTests
{
    // A client in this process, stamped: its build is its own side state's
    // (statics to zero), which nothing on the server side can reach.
    private readonly BuildInfo _clientBuild = new();

    // And its prediction ring, likewise its own composition's.
    private readonly PredictionErrorRing _clientRing = new();

    private static PredictionErrorRecord Record(long sequence, string className)
        => new(sequence, 1, 3, className, 8, 4, 0, 0, DateTime.UnixEpoch,
               Array.Empty<PredictionFieldError>());

    private static HttpReply Reply(DebugApiServer server, string path)
    {
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }

    /// <summary>A stamp that is merely KNOWN, which is all the route asks about.</summary>
    private static BuildHalf Stamped()
    {
        var builder = new BuildStampBuilder();
        builder.Add("debug-channel-route-test", new byte[] { 9, 8, 7, 6 }, DateTime.UnixEpoch);

        return new BuildHalf(builder.Build(), GitProvenance.Unknown, "test", false);
    }

    private static DebugChannelListener Listening()
    {
        var listener = new DebugChannelListener(0, false, _ => { }, _ => { });
        listener.Start();
        return listener;
    }

    /// <summary>Generous, because it is only ever reached by a failing case.</summary>
    private static bool Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    //-------------------------------------------------------------------------
    // The listen server (plan_devapi_interfaces §5.8, Q1, the user's ruling):
    // the server's /prediction shows server-side data only. A client in this
    // process reaches it the way a remote one does, over the debug channel;
    // its own view is the client dev API's (P3, /client/{slot}/prediction).
    // The server never reads the client's ring: under per-side Abi it cannot.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A LIVE CHANNEL WITH A CLIENT ATTACHED TO IT, and a client in this process
    /// with a record of its own: the channel answers, never the local ring.
    ///
    /// <para>
    /// The remote really connects and really pushes a record, because a channel
    /// nobody had attached to would be skipped by the route anyway.
    /// </para>
    /// </summary>
    [Fact]
    public void A_client_in_this_process_is_not_read_even_when_a_channel_is_attached()
    {
        using var listener = Listening();

        var remoteRing = new PredictionErrorRing(8);
        remoteRing.Add(Record(1, "ss_from_the_socket"));

        using var remote = new Debug.Api.DebugChannelClient(
            "127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: remoteRing));

        _clientBuild.SetManaged(Stamped(), Array.Empty<string>());
        _clientRing.Add(Record(1, "ss_in_this_process"));

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        remote.Attach(server.InviteDebugChannel()!.Value);

        Assert.True(Until(() => listener.Mirror.Count == 1),
            "the remote really attached and really pushed");

        string body = Reply(server, "/prediction").Body;

        Assert.Contains("ss_from_the_socket", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ss_in_this_process", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_in_this_process_with_no_channel_leaves_the_server_with_nothing_to_report()
    {
        _clientBuild.SetManaged(Stamped(), Array.Empty<string>());
        _clientRing.Add(Record(1, "ss_in_this_process"));

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        Assert.Equal(NoChannelJson, Reply(server, "/prediction").Body);
    }

    //-------------------------------------------------------------------------
    // The dedicated server, which is the gap this closes.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE GAP THIS CLOSES, end to end and through a real socket.
    ///
    /// <para>
    /// A record is put in a REMOTE client's ring, that client dials the channel
    /// it was invited to, and the server's <c>/prediction</c> answers with it.
    /// That is the whole feature in one case, and it is deliberately not written
    /// against the mirror directly: a mirror filled by the test would prove the
    /// route reads it and nothing about anything getting into it.
    /// </para>
    /// </summary>
    [Fact]
    public void WithNoClientHereARemoteClientsRingIsWhatAnswers()
    {
        using var listener = Listening();

        var remoteRing = new PredictionErrorRing(8);
        remoteRing.Add(Record(1, "ss_from_the_socket"));

        using var remote = new Debug.Api.DebugChannelClient(
            "127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: remoteRing));

        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        // The invitation the server module would have sent down the game's
        // own reliable message stream.
        remote.Attach(server.InviteDebugChannel()!.Value);

        Assert.True(Until(() => Reply(server, "/prediction").Body
                                    .Contains("ss_from_the_socket", StringComparison.Ordinal)),
            "on a dedicated server the client is another process, and this is the only " +
            "route its prediction data has");
    }

    [Fact]
    public void WithNoClientHereAndNoChannelTheRouteStillSaysWhyRatherThanAnsweringEmpty()
    {
        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        Assert.True(Reply(server, "/prediction").Body.Contains("\"available\":false",
                                                               StringComparison.Ordinal),
            "'no client here' and 'prediction is holding perfectly' produce identical " +
            "empty tables and mean opposite things");
    }

    [Fact]
    public void AChannelNobodyHasAttachedToIsReportedAsUnavailableAndSaysWhatToDo()
    {
        using var listener = Listening();

        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        string body = Reply(server, "/prediction").Body;

        Assert.True(body.Contains("\"available\":false", StringComparison.Ordinal)
                 && body.Contains(DebugChannelProtocol.ClientParam, StringComparison.Ordinal),
            "a server listening with nobody attached is a third state again, and the note " +
            "names the flag the client is missing");
    }

    [Fact]
    public void AResetClearsTheMirrorAsWellAsTheLocalRing()
    {
        using var listener = Listening();
        listener.Mirror.Add(Record(1, "ss_from_the_socket").ToWire());

        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        Reply(server, "/prediction?reset=1");

        Assert.True(listener.Mirror.Count == 0,
            "a reader who asked for a clean window and got only one ring cleared would " +
            "read the other one's history as the new window");
    }

    //-------------------------------------------------------------------------
    // Golden JSON for /prediction (plan_devapi_interfaces P2, group G11),
    // written before the route stopped reading the in-process client's ring.
    //-------------------------------------------------------------------------

    internal static string MaskTimes(string json)
        => System.Text.RegularExpressions.Regex.Replace(json, "\"atUtc\":\"[^\"]*\"", "\"atUtc\":\"\"");

    internal const string NoChannelJson =
        """
        {"available":false,"note":"this server holds no prediction of its own — prediction is client-side; a client's own report is at /client/{slot}/prediction, through this server's debug channel (start it with -debuguiserve, and the client with -debugui)","records":[]}
        """;

    internal const string NeverAttachedJson =
        """
        {"available":false,"note":"no client has attached to the debug channel on ADDRESS yet — a client must be started with -debugui before it will accept an invitation; once one has, its own report is at /client/{slot}/prediction","records":[]}
        """;

    internal const string MirrorJson =
        """
        {"available":true,"capacity":256,"held":1,"nextSequence":2,"observed":1,"mispredicted":1,"toleratedOnly":0,"unit":"observed counts compares that found any difference; a prediction that held byte for byte is never recorded","records":[{"sequence":1,"command":1,"typeId":3,"class":"ss_golden_mirror","blobBytes":8,"standingBytes":4,"firstDiffOffset":0,"toleratedFields":0,"mispredicted":true,"atUtc":"","fields":[]}]}
        """;

    [Fact]
    public void Prediction_renders_exactly_without_a_client_with_an_idle_channel_and_from_the_mirror()
    {
        _clientBuild.SetManaged(BuildHalf.Unknown, Array.Empty<string>());

        using (var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()))
            Assert.Equal(NoChannelJson, Reply(server, "/prediction").Body);

        using (var idle = Listening())
        using (var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = idle })
            Assert.Equal(NeverAttachedJson, Reply(server, "/prediction").Body.Replace(idle.Address, "ADDRESS"));

        using var listener = Listening();
        listener.Mirror.Add(Record(1, "ss_golden_mirror").ToWire());
        using var remote = new Debug.Api.DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        using (var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener })
        {
            remote.Attach(server.InviteDebugChannel()!.Value);
            Assert.True(Until(() => listener.EverAttached));
            Assert.Equal(MirrorJson, MaskTimes(Reply(server, "/prediction").Body));
        }
    }

    //-------------------------------------------------------------------------
    // /debugchannel.
    //-------------------------------------------------------------------------

    [Fact]
    public void AServerThatDidNotOptInSaysSoRatherThanReportingAnEmptyChannel()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        string body = Reply(server, "/debugchannel").Body;

        Assert.True(body.Contains("\"listening\":false", StringComparison.Ordinal)
                 && body.Contains(DebugChannelProtocol.ServerParam, StringComparison.Ordinal),
            "off and quiet look the same from outside; the reply names the flag that turns " +
            "it on");
    }

    /// <summary>
    /// A SERVER THAT OPTED IN AND COULD NOT BIND IS NOT A SERVER THAT DID NOT
    /// OPT IN.
    ///
    /// <para>
    /// A live run said "this server did not opt in" to somebody who had, whose
    /// bind had lost port 27110 to a peer's <c>debug-api ui</c> proxy. They went
    /// looking at their command line, which was correct. The two states have
    /// the same null channel and need different sentences.
    /// </para>
    /// </summary>
    [Fact]
    public void AServerThatOptedInAndCouldNotBindSaysThatRatherThanThatItDidNotOptIn()
    {
        using var held = new DebugChannelListener(0, false, _ => { }, _ => { });
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        // The same port, twice. The second bind is the failure being reported.
        server.StartDebugChannelForTest(held.Port);

        string body = Reply(server, "/debugchannel").Body;

        Assert.True(body.Contains("could not listen", StringComparison.Ordinal)
                 && !body.Contains("did not opt in", StringComparison.Ordinal),
            "the reader is sent to the port, which is where the problem is, and not to their " +
            "command line, which is correct");
    }

    [Fact]
    public void AFailedBindNamesTheFlagThatWouldMoveThePort()
    {
        using var held = new DebugChannelListener(0, false, _ => { }, _ => { });
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        server.StartDebugChannelForTest(held.Port);

        Assert.True(Reply(server, "/debugchannel").Body.Contains(
                        DebugChannelProtocol.ServerPortParam, StringComparison.Ordinal),
            "saying what went wrong without saying what to do about it costs the reader the " +
            "same search either way");
    }

    /// <summary>
    /// The default port is not one of this tree's own.
    ///
    /// <para>
    /// 27100 is the dev API's TCP fallback and 27110 is what <c>./debug-api ui</c>
    /// serves on. The channel defaulted to 27110 until a live run failed to bind
    /// it against a peer's running web UI.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDefaultPortDoesNotCollideWithTheToolsAlreadyInThisTree()
    {
        Assert.True(DebugChannelProtocol.DefaultPort != 27100
                 && DebugChannelProtocol.DefaultPort != 27110,
            "27100 is the dev API's own TCP fallback and 27110 is debug-api-proxy's; a channel " +
            "defaulting to either simply does not come up while the other tool is running");
    }

    [Fact]
    public void AListeningChannelReportsItsAddressAndItsCounters()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        string body = Reply(server, "/debugchannel").Body;

        Assert.True(body.Contains("\"listening\":true", StringComparison.Ordinal)
                 && body.Contains($"\"port\":{listener.Port}", StringComparison.Ordinal)
                 && body.Contains("\"everAttached\":false", StringComparison.Ordinal),
            "the port is what an invitation names, and everAttached is how a reader tells a " +
            "quiet client from an absent one");
    }

    [Fact]
    public void AChannelOnLoopbackSaysItIsNotPublic()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        Assert.True(Reply(server, "/debugchannel").Body.Contains("\"public\":false",
                                                                 StringComparison.Ordinal),
            "whether the channel is reachable from the network is the thing about it most " +
            "worth being able to check from outside");
    }

    [Fact]
    public void TheRouteIsListedInTheHelpSoItCanBeFound()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        // THE WHOLE KEY AND NOT THE PREFIX, for the reason PredictionRouteTests
        // records: "/debugchannel" is a substring of "/debugchannelXX", so a
        // needle of just the path would stay green over a listing that named a
        // route which does not exist.
        Assert.True(Reply(server, "/").Body.Contains("\"GET  /debugchannel\"", StringComparison.Ordinal),
            "a route nobody is told about is a route nobody uses");
    }

    [Fact]
    public void AnInvitationMintedThroughTheDebugApiCarriesTheChannelsPort()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };

        Assert.True(server.InviteDebugChannel()?.Port == listener.Port,
            "the server module sends what the dev API mints, and it has to name the port the " +
            "channel is actually on");
    }

    [Fact]
    public void ADebugApiWithNoChannelInvitesNobody()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        Assert.True(server.InviteDebugChannel() is null,
            "gate two: a server that did not opt in never sends an invitation, so a client's " +
            "own opt-in has nothing to accept");
    }
}
