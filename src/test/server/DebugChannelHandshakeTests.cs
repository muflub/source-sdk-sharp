using Debug.Api;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The nonce, and the handshake that spends it.
///
/// <para>
/// The channel's port is a TCP port, and on a dedicated server that means
/// anything able to route to the box can open a connection to it. The nonce is
/// what ties a connection back to a client this server actually invited: it
/// travelled to that client inside the game's own reliable message stream, over
/// the connection the client already authenticated to join.
/// </para>
///
/// <para>
/// Both halves are driven directly here rather than through a socket, for
/// <c>HttpSocketServer.TryParse</c>'s reason: the decision is the part worth
/// checking and it needs no port to check.
/// </para>
/// </summary>
public class DebugChannelHandshakeTests
{
    //-------------------------------------------------------------------------
    // The nonces.
    //-------------------------------------------------------------------------

    [Fact]
    public void AMintedNonceIsShapedLikeOne()
    {
        Assert.True(DebugChannelProtocol.IsWellFormedNonce(new DebugChannelNonces().Mint()),
            "what the server mints is what the invitation reader will accept");
    }

    [Fact]
    public void TwoMintedNoncesDiffer()
    {
        var nonces = new DebugChannelNonces();

        Assert.True(nonces.Mint() != nonces.Mint(),
            "a nonce that repeated would let one client's invitation be redeemed by another");
    }

    [Fact]
    public void AMintedNonceIsRedeemedOnce()
    {
        var nonces = new DebugChannelNonces();

        Assert.True(nonces.Redeem(nonces.Mint()), "the client this was minted for gets in");
    }

    [Fact]
    public void ANonceCannotBeRedeemedTwice()
    {
        var nonces = new DebugChannelNonces();
        string nonce = nonces.Mint();

        nonces.Redeem(nonce);

        Assert.True(!nonces.Redeem(nonce),
            "single use is what makes replaying a captured invitation worthless");
    }

    [Fact]
    public void ANonceThisServerNeverMintedIsRefused()
    {
        Assert.True(!new DebugChannelNonces().Redeem("0123456789abcdef0123456789abcdef"),
            "a well-formed nonce is not an invitation; only one this server issued is");
    }

    [Fact]
    public void AnAbsentNonceIsRefused()
    {
        Assert.True(!new DebugChannelNonces().Redeem(null),
            "a connection that presented nothing is not an invited client");
    }

    [Fact]
    public void AMalformedNonceIsRefusedWithoutTouchingTheTable()
    {
        Assert.True(!new DebugChannelNonces().Redeem("not-a-nonce"),
            "the shape is checked first so an arbitrary blob never becomes a key");
    }

    /// <summary>
    /// Ten minutes later, the offer is gone.
    ///
    /// <para>
    /// TEN MINUTES AND NOT <c>Lifetime + 1s</c>, deliberately. The first version
    /// of this advanced the clock by the constant it was checking, so widening
    /// <see cref="DebugChannelNonces.Lifetime"/> to ten years moved the test's
    /// own clock ten years and it stayed green — a case written over the value
    /// under test, which is the defect this tree keeps finding in its own suite.
    /// Two absolute times encode the documented window instead, and either
    /// direction of change breaks one of them.
    /// </para>
    /// </summary>
    [Fact]
    public void ANonceThatHasAgedOutIsRefused()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nonces = new DebugChannelNonces(() => clock);

        string nonce = nonces.Mint();
        clock = clock.AddMinutes(10);

        Assert.True(!nonces.Redeem(nonce),
            "an invitation left lying around stops being one; the window is a couple of " +
            "minutes, not the life of the server");
    }

    [Fact]
    public void ANonceThirtySecondsOldIsStillGood()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nonces = new DebugChannelNonces(() => clock);

        string nonce = nonces.Mint();
        clock = clock.AddSeconds(30);

        Assert.True(nonces.Redeem(nonce),
            "the expiry is a real window and not an immediate refusal — a client has to load " +
            "a level between being invited and dialling");
    }

    /// <summary>
    /// LEVEL SHUTDOWN INVALIDATES EVERY OUTSTANDING OFFER.
    ///
    /// <para>
    /// An invitation was made for the level that is ending. Carrying it across
    /// would let a client attach to the next map on the strength of an offer
    /// made to the previous one, which is exactly the shape of defect this tree
    /// has already paid for elsewhere.
    /// </para>
    /// </summary>
    [Fact]
    public void ALevelChangeInvalidatesEveryOutstandingInvitation()
    {
        var nonces = new DebugChannelNonces();
        string nonce = nonces.Mint();

        nonces.Clear();

        Assert.True(!nonces.Redeem(nonce),
            "an offer does not survive the level it was made for");
    }

    [Fact]
    public void TheOutstandingTableIsBounded()
    {
        var nonces = new DebugChannelNonces();

        for (int i = 0; i < DebugChannelNonces.MaxOutstanding * 3; i++)
            nonces.Mint();

        Assert.True(nonces.Outstanding <= DebugChannelNonces.MaxOutstanding,
            "nothing obliges an invited client to connect, so the table grows with the player " +
            "count and not with uptime");
    }

    //-------------------------------------------------------------------------
    // The upgrade.
    //-------------------------------------------------------------------------
    private static string Request(string? nonce = "0123456789abcdef0123456789abcdef",
                                  string path = DebugChannelProtocol.Path,
                                  string upgrade = "websocket",
                                  string version = "13",
                                  string key = "dGhlIHNhbXBsZSBub25jZQ==")
    {
        string nonceLine = nonce is null ? "" : $"{DebugChannelProtocol.NonceHeader}: {nonce}\r\n";

        return $"GET {path} HTTP/1.1\r\n" +
               "Host: 127.0.0.1:27110\r\n" +
               $"Upgrade: {upgrade}\r\n" +
               "Connection: Upgrade\r\n" +
               $"Sec-WebSocket-Key: {key}\r\n" +
               $"Sec-WebSocket-Version: {version}\r\n" +
               nonceLine +
               "\r\n";
    }

    /// <summary>Accepts everything, so a case can be about the request rather than the nonce.</summary>
    private static bool Any(string? nonce) => nonce is not null;

    [Fact]
    public void AGoodRequestWithAGoodNonceIsUpgraded()
    {
        var result = DebugChannelHandshake.Decide(Request(), Any);

        Assert.True(result.Accepted && result.Response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal),
            $"an invited client gets its upgrade; it said '{result.Reason}'");
    }

    /// <summary>
    /// RFC 6455 section 1.3's worked example: the key <c>dGhlIHNhbXBsZSBub25jZQ==</c>
    /// must produce <c>s3pPLMBiTxaQ9kYGzzhZRbK+xOo=</c>. A hand-rolled accept
    /// that got this wrong would be refused by every real client.
    /// </summary>
    [Fact]
    public void TheAcceptHeaderIsTheOneTheRfcSpecifies()
    {
        var result = DebugChannelHandshake.Decide(Request(), Any);

        Assert.True(result.Response.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=",
                                             StringComparison.Ordinal),
            "the accept is computed, not echoed, and RFC 6455's own example is what says it " +
            "is computed correctly");
    }

    /// <summary>
    /// GATE THREE, and it fires before a WebSocket exists on either side.
    /// </summary>
    [Fact]
    public void AConnectionWithNoNonceIsRefusedWithAnUnauthorized()
    {
        var result = DebugChannelHandshake.Decide(Request(nonce: null), Any);

        Assert.True(!result.Accepted && result.Response.StartsWith("HTTP/1.1 401", StringComparison.Ordinal),
            "anything that finds the port and does not present an invitation is turned away " +
            "at the handshake");
    }

    [Fact]
    public void AConnectionWhoseNonceIsNotRedeemedIsRefused()
    {
        var result = DebugChannelHandshake.Decide(Request(), _ => false);

        Assert.True(!result.Accepted,
            "the table is the real gate: a well-formed nonce this server did not issue is " +
            "refused just the same");
    }

    [Fact]
    public void ARefusedConnectionIsToldToClose()
    {
        var result = DebugChannelHandshake.Decide(Request(nonce: null), Any);

        Assert.True(result.Response.Contains("Connection: close", StringComparison.Ordinal),
            "a refusal ends the connection rather than leaving a socket half open");
    }

    [Fact]
    public void ARequestForAnotherPathIsNotFound()
    {
        var result = DebugChannelHandshake.Decide(Request(path: "/admin"), Any);

        Assert.True(!result.Accepted && result.Response.StartsWith("HTTP/1.1 404", StringComparison.Ordinal),
            "there is one path and nothing to route; anything else found the port by accident");
    }

    [Fact]
    public void ARequestThatIsNotAnUpgradeIsRefused()
    {
        var result = DebugChannelHandshake.Decide(Request(upgrade: "h2c"), Any);

        Assert.True(!result.Accepted, "this port speaks one protocol");
    }

    [Fact]
    public void AnOlderWebsocketVersionIsRefusedRatherThanAttempted()
    {
        var result = DebugChannelHandshake.Decide(Request(version: "8"), Any);

        Assert.True(!result.Accepted,
            "the framing here is version 13's; negotiating an older one would produce a " +
            "connection that fails later and less clearly");
    }

    [Fact]
    public void ARequestWithNoKeyIsRefused()
    {
        var result = DebugChannelHandshake.Decide(Request(key: ""), Any);

        Assert.True(!result.Accepted, "there is nothing to compute an accept from");
    }

    [Fact]
    public void AHeaderBlockThatNeverTerminatesIsRefused()
    {
        var result = DebugChannelHandshake.Decide("GET / HTTP/1.1\r\nHost: x", Any);

        Assert.True(!result.Accepted,
            "a request that has not finished arriving is not a request to act on");
    }

    [Fact]
    public void HeaderNamesAreMatchedCaseInsensitivelyLikeEveryOtherHttpImplementation()
    {
        string raw = Request().Replace("Sec-WebSocket-Key:", "sec-websocket-key:", StringComparison.Ordinal)
                              .Replace(DebugChannelProtocol.NonceHeader + ":",
                                       DebugChannelProtocol.NonceHeader.ToLowerInvariant() + ":",
                                       StringComparison.Ordinal);

        Assert.True(DebugChannelHandshake.Decide(raw, Any).Accepted,
            "HTTP header names are case-insensitive, and a client library that lower-cases " +
            "them would otherwise never connect");
    }

    [Fact]
    public void TheNoncePresentedIsTheOneHandedToTheTable()
    {
        string? seen = null;

        DebugChannelHandshake.Decide(Request(nonce: "aaaaaaaabbbbbbbbccccccccdddddddd"),
                                     n => { seen = n; return true; });

        Assert.True(seen == "aaaaaaaabbbbbbbbccccccccdddddddd",
            "the header's value reaches the table unaltered; a trimmed or truncated one would " +
            "never match what was minted");
    }
}
