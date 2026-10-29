extern alias client;
using PredictionComponent = client::SourceSharp.Abi.PredictionComponent;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using PredictionFieldError = client::SourceSharp.Abi.PredictionFieldError;
using PredictionErrorRecord = client::SourceSharp.Abi.PredictionErrorRecord;
using System.Diagnostics;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using Debug.Api;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The channel end to end, over a real socket: a listener on an ephemeral port,
/// a real <c>ClientWebSocket</c> dialling it, and the client's prediction ring
/// arriving in the server's mirror.
///
/// <para>
/// A REAL SOCKET AND NOT A DOUBLE, deliberately. Everything above this file
/// checks a decision — a refusal rule, a length bound, a flag — and none of them
/// would notice a handshake this tree's own hand-rolled server got subtly wrong,
/// or a frame the two sides split differently. The port is 0, so the machine
/// picks one and two runs at once cannot collide.
/// </para>
/// </summary>
public class DebugChannelSessionTests
{
    /// <summary>Generous, because it is only ever reached by a failing case.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The client's drain interval for a case that waits on polls. The game polls
    /// every 250 ms; the cases wait for polls, not for time, so a short one waits
    /// for the same polls sooner.
    /// </summary>
    private static readonly TimeSpan QuickPoll = TimeSpan.FromMilliseconds(20);

    private static bool Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < Patience)
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    private static PredictionErrorRecord Record(long sequence, string className = "player")
        => new(sequence, 412, 12, className, 120, 8, 56, 0, DateTime.UnixEpoch,
               new[]
               {
                   new PredictionFieldError("Origin", 2, 56, 12, 0.0625f, false, 8,
                       new[] { new PredictionComponent(64, 60.75, 3.25) }),
               });

    private static DebugChannelListener Listening(DebugChannelNonces? nonces = null)
    {
        var listener = new DebugChannelListener(0, false, _ => { }, _ => { }, nonces);
        listener.Start();
        return listener;
    }

    /// <summary>
    /// A listener, and a test for "the server's session has the client", which
    /// is what <c>Ask</c> needs and <c>client.Attached</c> is not.
    ///
    /// <para>
    /// The handshake reply reaches the client BEFORE the server registers the
    /// session it is about to start, so a case that asked as soon as the
    /// client said it was attached could find nobody to ask. It lost that race
    /// under load, once in 21 runs. The server logs the client's hello from
    /// inside the session, after registering it, so seeing that line means
    /// <c>Ask</c> will reach it.
    /// </para>
    /// </summary>
    private static DebugChannelListener ListeningForSessions(out Func<bool> sessionUp)
    {
        var hellos = new System.Runtime.CompilerServices.StrongBox<int>();

        var listener = new DebugChannelListener(0, false,
            line =>
            {
                if (line.Contains("a client attached", StringComparison.Ordinal))
                    Interlocked.Increment(ref hellos.Value);
            },
            _ => { });

        listener.Start();
        sessionUp = () => Volatile.Read(ref hellos.Value) > 0;
        return listener;
    }

    //-------------------------------------------------------------------------
    // The whole route.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnInvitedClientAttaches()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(invitation);

        // BOTH ENDS, and the client's half is the one that matters here.
        // listener.EverAttached is set the moment the server decides to upgrade,
        // so on its own it would stay true even if the 101 this side wrote were
        // one a real client refuses — which is exactly what mutating the
        // RFC 6455 accept computation produces.
        Assert.True(Until(() => listener.EverAttached && client.Attached),
            "a client holding a nonce this server minted completes the handshake, and a " +
            "handshake is only complete when the dialling end says so too");
    }

    [Fact]
    public void TheClientsPredictionRingReachesTheServersMirror()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        var ring = new PredictionErrorRing(8);
        ring.Add(Record(1));

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: ring));
        client.Attach(invitation);

        Assert.True(Until(() => listener.Mirror.Count == 1),
            "this is the gap the channel exists to close: on a dedicated server the client's " +
            "prediction data had no route to the dev API at all");
    }

    [Fact]
    public void ARecordArrivesWithItsNumbersIntactRatherThanOnlyItsCount()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        var ring = new PredictionErrorRing(8);
        ring.Add(Record(1));

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: ring));
        client.Attach(invitation);

        Until(() => listener.Mirror.Count == 1);

        var arrived = listener.Mirror.Since(0);

        Assert.True(arrived.Count == 1
                 && arrived[0].ClassName == "player"
                 && arrived[0].Fields[0].Components[0].Delta == 3.25,
            "a count that arrived with the wrong numbers in it would make the remote report " +
            "say something different from the local one for the same game");
    }

    [Fact]
    public void RecordsAddedAfterTheChannelIsUpAreSentToo()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        var ring = new PredictionErrorRing(8);

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: ring))
        {
            PollInterval = QuickPoll,
        };
        client.Attach(invitation);

        Until(() => listener.EverAttached);
        ring.Add(Record(1));

        Assert.True(Until(() => listener.Mirror.Count == 1),
            "the ring is drained on a timer for the whole life of the session, not once at the " +
            "handshake");
    }

    [Fact]
    public void TheSameRecordIsNotSentTwice()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        var ring = new PredictionErrorRing(8);
        ring.Add(Record(1));

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: ring))
        {
            PollInterval = QuickPoll,
        };
        client.Attach(invitation);

        Until(() => listener.Mirror.Count == 1);
        Thread.Sleep(QuickPoll * 5);   // several poll intervals

        Assert.True(listener.Mirror.Count == 1,
            "the cursor advances past what was sent; a poll that re-sent the ring would fill " +
            "the mirror with copies and make every count meaningless");
    }

    //-------------------------------------------------------------------------
    // THE NONCE, over a real socket.
    //-------------------------------------------------------------------------

    [Fact]
    public void AClientPresentingANonceThisServerNeverMintedIsRefused()
    {
        using var listener = Listening();

        // Never minted. Well-formed, so this is the table's refusal and not the
        // shape check's.
        var forged = new DebugChannelInvitation(
            DebugChannelProtocol.Version, listener.Port, "ffffffffffffffffffffffffffffffff");

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(forged);

        Assert.True(Until(() => listener.Status().Refused >= 1) && !listener.EverAttached,
            "the port is reachable by anything that can route to the box; the nonce is what " +
            "ties a connection to a client this server actually invited");
    }

    [Fact]
    public void AnInvitationCannotBeRedeemedTwiceOverTheSocket()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        using var first = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        first.Attach(invitation);
        Until(() => listener.EverAttached);

        int refusedBefore = listener.Status().Refused;

        using var second = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        second.Attach(invitation);

        Assert.True(Until(() => listener.Status().Refused > refusedBefore),
            "a nonce is spent when it is presented, so a captured invitation is worth nothing " +
            "to whoever captured it");
    }

    [Fact]
    public void AServerThatIsNotListeningInvitesNobody()
    {
        var listener = Listening();
        listener.Dispose();

        Assert.True(listener.Invite() is null,
            "an invitation names a channel; there is no honest one to mint once the channel " +
            "is gone");
    }

    //-------------------------------------------------------------------------
    // LEVEL CHANGE. The part this tree has already paid for getting wrong.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Disposing the client stops its task, rather than only signalling it.
    ///
    /// <para>
    /// A task still on the stack holds every assembly on it alive, so a channel
    /// merely signalled would pin the client's collectible load context and make
    /// every managed reload leak the build it replaced.
    /// </para>
    /// </summary>
    [Fact]
    public void DisposingTheClientTakesItsTaskOffTheStack()
    {
        using var listener = Listening();

        var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(listener.Invite()!.Value);
        Until(() => client.Attached);

        client.Dispose();

        // StoppedCleanly and NOT Running, and the difference is the whole case.
        // Dispose drops its reference to the task, so Running reads false the
        // instant it is called whether or not anything stopped — a teardown that
        // only asserted that would pass over a loop still on the stack, which is
        // the defect it is supposed to be about.
        Assert.True(client.StoppedCleanly,
            "Dispose waits for the loop; 'signalled' is not 'gone', and a level change that " +
            "left one running is the same family of defect as a world that outlived its level");
    }

    [Fact]
    public void DisposingTheClientEndsTheServersSessionToo()
    {
        using var listener = Listening();

        var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(listener.Invite()!.Value);
        Until(() => listener.Status().Sessions == 1);

        client.Dispose();

        Assert.True(Until(() => listener.Status().Sessions == 0),
            "the far end notices the socket close and its session task comes off the stack; a " +
            "session per level change that never ended would accumulate across a session");
    }

    [Fact]
    public void ALevelChangeInvalidatesInvitationsTheServerHadOutstanding()
    {
        using var listener = Listening();
        var invitation = listener.Invite()!.Value;

        listener.LevelShutdown();

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(invitation);

        Assert.True(Until(() => listener.Status().Refused >= 1) && !listener.EverAttached,
            "an offer made for the level that ended is not still good for the level that " +
            "follows");
    }

    [Fact]
    public void ANewLevelMintsAnInvitationTheOldOneCannotStandInFor()
    {
        using var listener = Listening();
        var before = listener.Invite()!.Value;

        listener.LevelShutdown();

        var after = listener.Invite()!.Value;

        Assert.True(before.Nonce != after.Nonce,
            "each level's offer is its own; the case above would also pass against a server " +
            "that had simply stopped inviting, and this is what says it has not");
    }

    [Fact]
    public void DisposingTheListenerGivesThePortBack()
    {
        var listener = Listening();
        int port = listener.Port;

        listener.Dispose();

        using var again = new DebugChannelListener(port, false, _ => { }, _ => { });

        Assert.True(again.Port == port,
            "the socket is closed rather than abandoned, so a reload can bind the same port");
    }

    /// <summary>
    /// The accept thread is JOINED, which the port coming back does not say.
    ///
    /// <para>
    /// Closing the socket releases the port whether or not the thread parked in
    /// <c>Accept</c> ever returned — so the case above passes over a thread
    /// still on the stack, and a thread on the stack is what keeps a collectible
    /// load context from unloading. That is a separate fact and needs a separate
    /// answer.
    /// </para>
    /// </summary>
    [Fact]
    public void DisposingTheListenerJoinsItsAcceptThreadAndItsSessions()
    {
        var listener = Listening();

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(listener.Invite()!.Value);
        Until(() => listener.Status().Sessions == 1);

        listener.Dispose();

        Assert.True(listener.StoppedCleanly,
            "with a session live at the moment of teardown, both the accept thread and that " +
            "session have to come off the stack inside the timeout");
    }

    //-------------------------------------------------------------------------
    // The command surface, over the wire.
    //-------------------------------------------------------------------------

    [Fact]
    public void ADeclaredOperationReachesTheClientAndIsHonoured()
    {
        using var listener = ListeningForSessions(out var sessionUp);

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(listener.Invite()!.Value);
        Until(() => client.Attached && sessionUp());

        Assert.True(client.CommandsHonoured == 0,
            "nothing has asked it for anything yet, which is what makes the next case a " +
            "measurement rather than a reading of a counter that was already set");

        listener.Ask(DebugChannelCommand.Ping);

        Assert.True(Until(() => client.CommandsHonoured == 1),
            "the channel really is command and control and not only telemetry: a named " +
            "operation crosses it and the client acts on it");
    }

    /// <summary>
    /// The client's prediction ring can be cleared from the server, which is one
    /// of the four things a server may ask for and a useful one: a reader
    /// watching a remote client wants a clean window without leaving their desk.
    /// </summary>
    [Fact]
    public void ResetPredictionRingClearsTheClientsRing()
    {
        using var listener = Listening();

        var ring = new PredictionErrorRing(8);
        ring.Add(Record(1));

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: ring));
        client.Attach(listener.Invite()!.Value);
        Until(() => listener.Mirror.Count == 1);

        Assert.True(ring.Count == 1, "the ring holds the record that was just mirrored");

        listener.Ask(DebugChannelCommand.ResetPredictionRing);

        Assert.True(Until(() => ring.Count == 0),
            "the operation runs on the CLIENT's own ring, which is the state the server has " +
            "no other way to reach");
    }

    /// <summary>
    /// THE BOUND, over the wire: a byte that names no declared operation is
    /// dropped by the client rather than switched on.
    ///
    /// <para>
    /// The listener's <c>Ask</c> cannot express this — its parameter is the
    /// enum, which is the point — so the frame is written by hand, the way
    /// something that was not this server would have to.
    /// </para>
    /// </summary>
    [Fact]
    public void AByteThatNamesNoDeclaredOperationIsRefusedByTheClient()
    {
        using var listener = ListeningForSessions(out var sessionUp);

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, _ => { }, ring: new PredictionErrorRing(8)));
        client.Attach(listener.Invite()!.Value);
        Until(() => client.Attached && sessionUp());

        listener.AskRawForTest(new byte[] { (byte)DebugChannelMessage.Command, 99 });

        Assert.True(Until(() => client.CommandsRefused == 1) && client.CommandsHonoured == 0,
            "the command surface is an enumeration and the refusal is where the bytes are " +
            "understood, not in a switch that happens to have no case");
    }
}
