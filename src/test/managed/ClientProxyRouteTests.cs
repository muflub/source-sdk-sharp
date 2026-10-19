extern alias client;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_isolation §2.6, rulings 11-12: one dev-API socket per process,
/// the server's. A client's own state is asked for at /client/{slot}/..., and
/// the server proxies the question over the debug channel to the session the
/// slot's invitation opened.
/// </summary>
public class ClientProxyRouteTests
{
    private static HttpReply Reply(DebugApiServer server, string path)
    {
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }

    private static DebugChannelListener Listening()
    {
        var listener = new DebugChannelListener(0, false, _ => { }, _ => { });
        listener.Start();
        return listener;
    }

    private static bool Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    [Fact]
    public void A_client_invited_for_a_slot_answers_that_slots_status_route()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)), "the client attached in slot 3");

        Assert.Equal("{\"slots\":[3]}", Reply(server, "/clients").Body);

        var status = Reply(server, "/client/3/status");
        Assert.Equal(200, status.Status);
        Assert.Contains("\"side\":\"client\"", status.Body);
        Assert.Contains("\"crossings\":", status.Body);
        Assert.Equal(1, client.QueriesAnswered);
    }

    /// <summary>
    /// The listen server's prediction view (Q1; P3b): the client's own report,
    /// proxied, exactly as the client renders its ring, and not the server's
    /// mirror. The ring holds a record pushed nowhere, so only the client has it.
    /// </summary>
    [Fact]
    public void A_client_answers_its_own_prediction_report_through_the_slots_route()
    {
        var ring = new PredictionErrorRing(8);
        ring.Add(new PredictionErrorRecord(5, 9, 3, "ss_only_on_the_client", 8, 4, 0, 0, DateTime.UnixEpoch, []));

        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(ring: ring));

        client.Attach(server.InviteDebugChannel(2)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(2)), "the client attached in slot 2");

        var reply = Reply(server, "/client/2/prediction");
        Assert.Equal(200, reply.Status);

        // What the client renders for its ring is what the server would render
        // for a mirror holding the same records: one renderer, both halves.
        string expected = PredictionJson.Report(ring.ToMirror(), 0, PredictionMirror.DefaultCapacity);
        Assert.Equal(expected, reply.Body);
        Assert.Contains("ss_only_on_the_client", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_client_question_lists_every_known_one()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());
        var reply = Reply(server, "/client/1/nonsense");
        Assert.Equal(404, reply.Status);
        Assert.Equal("{\"error\":\"no such client question\",\"question\":\"nonsense\",\"expected\":[" + KnownQuestions + "]}", reply.Body);
    }

    /// <summary>The route table, as a 404 lists it: each route's commit extends it (plan_client_novalve §4.4).</summary>
    private const string KnownQuestions = "\"status\",\"prediction\",\"frame\",\"entities\",\"nettables\",\"entity/{n}\",\"reload\",\"reload/trigger\",\"netgraph\",\"absences\",\"ui\",\"ui/fonts\",\"ui/menu\",\"ui/event\",\"proxies\",\"viewcam\",\"render\",\"render/compare\",\"particles\",\"input\",\"input/inject\"";

    [Fact]
    public void Every_known_question_is_one_the_client_answers()
    {
        // The server's table and the codec's closed set agree: a row the
        // client would refuse at the wire is a route that can only time out.
        Assert.All(DebugApiServer.ClientQuestionTable, q => Assert.Contains(q.Query, DebugChannelCodec.Answerable));
        Assert.Equal(DebugChannelCodec.Answerable.Order(), DebugApiServer.ClientQuestionTable.Select(q => q.Query).Distinct().Order());
    }

    [Fact]
    public void A_slot_with_no_client_answers_not_found()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)));

        Assert.Equal(404, Reply(server, "/client/4/status").Status);
        Assert.Equal(0, client.QueriesAnswered);
    }

    [Fact]
    public void A_question_outside_the_closed_set_never_reaches_the_client()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)));

        Assert.Equal(404, Reply(server, "/client/3/exec").Status);
        Assert.Equal(0, client.QueriesAnswered);
    }

    [Fact]
    public void Without_a_debug_channel_the_route_says_how_to_get_one()
    {
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server());

        var reply = Reply(server, "/client/1/status");
        Assert.Equal(503, reply.Status);
        Assert.Contains("-debuguiserve", reply.Body);
    }

    [Theory]
    [InlineData("/client/3/status", true, 3, "status")]
    [InlineData("/client/0/status", false, 0, "")]
    [InlineData("/client/x/status", false, 0, "")]
    [InlineData("/client/3", false, 0, "")]
    [InlineData("/client/3/", false, 0, "")]
    [InlineData("/client/3/status/more", true, 3, "status/more")]
    [InlineData("/client/3/entity/12", true, 3, "entity/12")]
    public void The_client_path_names_a_slot_and_a_question(string path, bool ok, int slot, string question)
    {
        Assert.Equal(ok, DebugApiServer.TryClientPath(path, out int s, out string q));
        Assert.Equal((slot, question), (s, q));
    }

    [Theory]
    [InlineData("status", true)]
    [InlineData("prediction", true)]
    [InlineData("status/more", false)]
    [InlineData("exec", false)]
    [InlineData("status/", false)]
    public void Only_a_spelling_in_the_table_names_a_question(string question, bool ok)
        => Assert.Equal(ok, DebugApiServer.TryClientQuestion(question, new Dictionary<string, string>(), out _, out _));

    [Fact]
    public void A_question_with_an_extra_segment_never_reaches_the_client()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)));

        // The positive arm first: the same slot answers its spelling.
        Assert.Equal(200, Reply(server, "/client/3/status").Status);
        Assert.Equal(1, client.QueriesAnswered);

        var reply = Reply(server, "/client/3/status/more");
        Assert.Equal(404, reply.Status);
        Assert.Contains("\"expected\":[", reply.Body);
        Assert.Equal(1, client.QueriesAnswered);
    }

    [Fact]
    public void A_nonce_redeems_to_the_slot_it_was_minted_for()
    {
        var nonces = new DebugChannelNonces();
        string five = nonces.Mint(5);
        string seven = nonces.Mint(7);

        Assert.True(nonces.Redeem(seven, out int slot));
        Assert.Equal(7, slot);
        Assert.True(nonces.Redeem(five, out slot));
        Assert.Equal(5, slot);

        // Spent: a replay neither succeeds nor names a slot.
        Assert.False(nonces.Redeem(five, out slot));
        Assert.Equal(0, slot);
    }

    private sealed class OneFrame : Debug.Api.Client.Interfaces.IDebugClientFrame
    {
        public int Reads;

        public Debug.Api.Client.Interfaces.DebugClientFrame Read()
        {
            Reads++;
            return new(7, 0, "Start", [], 0, 0, 0, 0, 0, false, default, 0, 0);
        }
    }

    [Fact]
    public void A_game_thread_question_is_answered_by_the_pump()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        var frame = new OneFrame();
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, frame: frame));

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)));

        // The game thread, as the client module's Frame would pump it.
        using var stop = new CancellationTokenSource();
        var game = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                client.Pump();
                Thread.Sleep(1);
            }
        }) { IsBackground = true };
        game.Start();
        try
        {
            var reply = Reply(server, "/client/3/frame");
            Assert.Equal(200, reply.Status);
            Assert.StartsWith("{\"frames\":7,", reply.Body, StringComparison.Ordinal);
        }
        finally
        {
            stop.Cancel();
            game.Join();
        }

        Assert.Equal(1, frame.Reads);
        Assert.Equal(1, client.PumpedAnswers);
    }

    [Fact]
    public void Without_a_frame_a_game_thread_question_says_so()
    {
        using var listener = Listening();
        using var server = new DebugApiServer(new string[] { "srcds" }, TestDebugHooks.Server()) { DebugChannelForTest = listener };
        var frame = new OneFrame();
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, frame: frame))
        {
            GameThreadTimeout = TimeSpan.FromMilliseconds(200),
        };

        client.Attach(server.InviteDebugChannel(3)!.Value);
        Assert.True(Until(() => listener.AttachedSlots().Contains(3)));

        var reply = Reply(server, "/client/3/frame");
        Assert.Equal(503, reply.Status);
        Assert.Equal(DebugChannelClient.NoFrameBody, reply.Body);

        // Read on the socket's thread it never was.
        Assert.Equal(0, frame.Reads);
    }
}
