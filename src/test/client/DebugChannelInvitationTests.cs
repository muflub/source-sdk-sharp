using Debug.Api;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The invitation, and the one rule the whole debug channel is shaped around.
///
/// <para>
/// A server offers a client a control channel. The obvious protocol — the server
/// sends a host and a port, the client dials what it was sent — makes every
/// server a user joins able to aim that user's debug socket at a machine of the
/// server's choosing. So the invitation carries a protocol number, a port and a
/// nonce, and the cases below hold it to carrying nothing else: not by asking a
/// reader to be disciplined, but by refusing a payload that has anything after
/// the three declared fields.
/// </para>
/// </summary>
public class DebugChannelInvitationTests
{
    /// <summary>
    /// A delegate that takes the writer BY REF, because <c>UserMessageWriter</c>
    /// is a ref struct and cannot be a generic argument — <c>Action&lt;T&gt;</c>
    /// will not take one.
    /// </summary>
    private delegate void WritePayload(ref UserMessageWriter writer);

    private static byte[] Payload(WritePayload write)
    {
        var buffer = new byte[256];
        var writer = new UserMessageWriter(buffer);

        write(ref writer);

        return buffer[..writer.Length];
    }

    /// <summary>What a correct server sends: three fields and the payload ends.</summary>
    private static byte[] Good(string nonce = "0123456789abcdef0123456789abcdef",
                               int port = 27110,
                               int protocol = DebugChannelProtocol.Version)
    {
        var invitation = new DebugChannelInvitation(protocol, port, nonce);
        return Payload((ref UserMessageWriter w) =>
        {
            // The wire format the dev API's codec writes (DebugChannelInvitationWireTests).
            w.WriteInt(invitation.Protocol);
            w.WriteInt(invitation.Port);
            w.WriteString(invitation.Nonce);
        });
    }

    /// <summary>An invitation with something appended after its three fields.</summary>
    private static byte[] WithTrailer(Action<Trailer> append)
    {
        var trailer = new Trailer();
        append(trailer);

        return Payload((ref UserMessageWriter w) =>
        {
            w.WriteInt(DebugChannelProtocol.Version);
            w.WriteInt(27110);
            w.WriteString("0123456789abcdef0123456789abcdef");

            if (trailer.Text is not null)
                w.WriteString(trailer.Text);

            if (trailer.Byte is not null)
                w.WriteByte(trailer.Byte.Value);
        });
    }

    private sealed class Trailer
    {
        public string? Text;
        public byte? Byte;
    }

    private static bool Read(byte[] payload, out DebugChannelInvitation invitation, out string refusal)
    {
        return DebugChannelInvitation.TryRead(payload, out invitation, out refusal);
    }

    [Fact]
    public void AWellFormedInvitationRoundTrips()
    {
        Assert.True(Read(Good(port: 27115), out var invitation, out string refusal),
            $"a three-field invitation is accepted; it said '{refusal}'");

        Assert.True(invitation.Port == 27115, "the port the server named survives the trip");
    }

    [Fact]
    public void TheNonceSurvivesTheTripUnchanged()
    {
        Read(Good("aaaaaaaabbbbbbbbccccccccdddddddd"), out var invitation, out _);

        Assert.True(invitation.Nonce == "aaaaaaaabbbbbbbbccccccccdddddddd",
            "the nonce arrives byte for byte, because the handshake compares it exactly");
    }

    //-------------------------------------------------------------------------
    // THE RULE.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The case the whole design exists to make impossible: a server appending
    /// an address.
    ///
    /// <para>
    /// It is written as a HOSTNAME rather than as an anonymous trailer on
    /// purpose. This is the field somebody will one day add, with a reason —
    /// NAT, a proxy, a server that knows its own external address — and this
    /// case is here to fail on the day they do.
    /// </para>
    /// </summary>
    [Fact]
    public void AnInvitationThatAppendsAHostnameIsRefusedOutright()
    {
        byte[] payload = WithTrailer(t => t.Text = "attacker.example.com");

        Assert.True(!Read(payload, out _, out _),
            "a server may not tell a client where to dial: an invitation carrying a host " +
            "is refused, nonce and all");
    }

    [Fact]
    public void AnInvitationThatAppendsAnIpAddressIsRefusedTheSameWay()
    {
        byte[] payload = WithTrailer(t => t.Text = "203.0.113.9");

        Assert.True(!Read(payload, out _, out _),
            "an address in any form is still an address, and is refused");
    }

    /// <summary>
    /// A single stray byte is enough.
    ///
    /// <para>
    /// This case is what makes the rule structural rather than a filter over
    /// strings that look like hosts. A refusal that only fired on something
    /// host-shaped would be a filter written over the value it is checking —
    /// this tree's own most-repeated test defect — and would be one encoding
    /// away from being bypassed.
    /// </para>
    /// </summary>
    [Fact]
    public void EvenOneByteAfterTheDeclaredFieldsIsEnoughToRefuse()
    {
        byte[] payload = WithTrailer(t => t.Byte = 0x41);

        Assert.True(!Read(payload, out _, out _),
            "the payload must be exactly exhausted; an unknown trailer is not tolerated, " +
            "because a tolerated field is one edit away from an honoured one");
    }

    [Fact]
    public void TheRefusalSaysWhyRatherThanOnlyThatItFailed()
    {
        byte[] payload = WithTrailer(t => t.Text = "attacker.example.com");

        Read(payload, out _, out string refusal);

        Assert.True(refusal.Contains("host", StringComparison.Ordinal)
                 && refusal.Contains("address", StringComparison.Ordinal),
            "the refusal names the rule it is enforcing, because the next reader will be " +
            $"someone adding a host field; it said '{refusal}'");
    }

    [Fact]
    public void ARefusedInvitationYieldsNoNonceAtAll()
    {
        byte[] payload = WithTrailer(t => t.Text = "attacker.example.com");

        Read(payload, out var invitation, out _);

        Assert.True(invitation.Nonce is null or "",
            "a refusal must not leave a usable invitation behind; the whole thing is dropped");
    }

    //-------------------------------------------------------------------------
    // The other refusals, so a malformed offer never becomes a connection.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnInvitationThatEndsMidFieldIsRefused()
    {
        Assert.True(!Read(new byte[] { 1, 0, 0 }, out _, out _),
            "a truncated invitation is refused rather than read as zeroes");
    }

    [Fact]
    public void AnInvitationFromAnotherProtocolVersionIsRefused()
    {
        Assert.True(!Read(Good(protocol: DebugChannelProtocol.Version + 1), out _, out _),
            "the two halves of a game can be different builds; a version this side does not " +
            "speak is refused rather than guessed at");
    }

    [Fact]
    public void APortOutsideTheRangeIsRefused()
    {
        Assert.True(!Read(Good(port: 0), out _, out _),
            "zero is not a port a client can dial");
    }

    [Fact]
    public void APortAboveTheRangeIsRefused()
    {
        Assert.True(!Read(Good(port: 70000), out _, out _),
            "a port past 65535 came from something that is not this protocol");
    }

    [Fact]
    public void ANonceOfTheWrongLengthIsRefused()
    {
        Assert.True(!Read(Good("cafebabe"), out _, out _),
            "a nonce is 32 hex characters; anything else was not minted here");
    }

    [Fact]
    public void ANonceWithNonHexCharactersIsRefused()
    {
        Assert.True(!Read(Good("0123456789abcdef0123456789abcdeZ"), out _, out _),
            "the right length is not enough — a nonce is hex, and the last character here is not");
    }

    [Fact]
    public void AnUppercaseNonceIsRefusedBecauseTheTableIsKeyedExactly()
    {
        Assert.True(!Read(Good("0123456789ABCDEF0123456789ABCDEF"), out _, out _),
            "the nonce table compares ordinally, so a case-shifted nonce would never match " +
            "and is better refused here than looked up and missed");
    }

    //-------------------------------------------------------------------------
    // GATE ONE: the client's own opt-in, and the negative.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A client that did not ask for a debug channel does not even READ a
    /// server's offer of one.
    ///
    /// <para>
    /// The stronger statement than "never dials", and the reason it is worth
    /// making: a handler that parsed the invitation, resolved a host and then
    /// declined would satisfy "never dials" while still having a code path from
    /// a server's message to a socket in it.
    /// </para>
    /// </summary>
    [Fact]
    public void WithoutTheClientsOwnOptInTheInvitationIsNotEvenParsed()
    {
        // The handler's own hook, unset: what a client without -debugui holds.
        var handler = new DebugChannelInviteMessage();
        long before = handler.PayloadsRead;

        ((IUserMessage)handler).OnReceived(new UserMessageReader(Wire(Good())));

        Assert.True(handler.PayloadsRead == before,
            "no -debugui on this client's own command line means the invitation's payload " +
            "is never read: there is no path from a server's message to a socket");
    }

    [Fact]
    public void WithTheOptInAValidInvitationIsHandedOn()
    {
        var seen = new List<DebugChannelInvitation>();
        var handler = new DebugChannelInviteMessage { Accepted = bytes => Read(bytes, seen) };

        ((IUserMessage)handler).OnReceived(new UserMessageReader(Wire(Good(port: 27119))));

        Assert.True(seen.Count == 1 && seen[0].Port == 27119,
            "a client that DID opt in accepts the offer, so the negative above is about the " +
            "gate and not about a handler that never works");
    }

    [Fact]
    public void WithTheOptInAnInvitationCarryingAHostIsStillNotHandedOn()
    {
        var seen = new List<DebugChannelInvitation>();
        var handler = new DebugChannelInviteMessage { Accepted = bytes => Read(bytes, seen) };

        byte[] payload = WithTrailer(t => t.Text = "attacker.example.com");

        ((IUserMessage)handler).OnReceived(new UserMessageReader(Wire(payload)));

        Assert.True(seen.Count == 0,
            "opting in is not consenting to be steered: an invitation with an address in it " +
            "is refused even by a client that wanted a channel");
    }

    [Fact]
    public void TwoHandlersDoNotShareAnOptInOrACount()
    {
        // The property the static hook and counter lacked: one module opting in
        // is not another module opting in, and one handler's reads are its own.
        var seen = new List<DebugChannelInvitation>();
        var optedIn = new DebugChannelInviteMessage { Accepted = bytes => Read(bytes, seen) };
        var notOptedIn = new DebugChannelInviteMessage();

        ((IUserMessage)notOptedIn).OnReceived(new UserMessageReader(Wire(Good())));
        Assert.True(seen.Count == 0 && notOptedIn.PayloadsRead == 0,
                    "a handler with no hook of its own reads nothing, whoever else opted in");

        ((IUserMessage)optedIn).OnReceived(new UserMessageReader(Wire(Good())));
        Assert.True(seen.Count == 1 && optedIn.PayloadsRead == 1 && notOptedIn.PayloadsRead == 0,
                    "the opted-in handler reads and hands on, and counts only its own read");
    }

    /// <summary>The invitation as the user message carries it: a count byte, then its bytes.</summary>
    private static byte[] Wire(byte[] invitation)
    {
        var buffer = new byte[UserMessageSender.MaxPayloadBytes];
        int n = new DebugChannelInvite { Invitation = invitation }.Write(buffer);
        Assert.True(n > 0, "the invitation fits its payload");
        return buffer[..n];
    }

    /// <summary>
    /// What the client's dev API does with the bytes the handler hands over
    /// (plan_devapi_interfaces §5.5): the game no longer parses them, so the
    /// refusal these facts hold now happens on the far side of Accepted.
    /// </summary>
    private static void Read(ReadOnlySpan<byte> bytes, List<DebugChannelInvitation> seen)
    {
        if (DebugChannelInvitation.TryRead(bytes, out var invitation, out _))
            seen.Add(invitation);
    }

    [Fact]
    public void The_client_debug_api_refuses_an_invitation_carrying_a_host_and_never_dials()
    {
        var warnings = new List<string>();
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(_ => { }, warnings.Add, ring: new PredictionErrorRing(8)));

        client.Attach(WithTrailer(t => t.Text = "attacker.example.com").AsSpan());

        Assert.Contains(warnings, w => w.Contains("invitation refused", StringComparison.Ordinal));
        Assert.Equal(0, client.DialAttempts);
        Assert.False(client.Running);
    }
}
