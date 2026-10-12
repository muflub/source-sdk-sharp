using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The debug channel invitation's bytes on the wire (plan_devapi_interfaces P4):
/// golden, written while the game still encoded it through UserMessageWriter,
/// so moving the codec into the dev API cannot change what a client reads.
/// </summary>
public class DebugChannelInvitationWireTests
{
    internal static readonly DebugChannelInvitation Known = new(1, 27246, "0123456789abcdef0123456789abcdef");

    /// <summary>Protocol 1 and port 27246 as little-endian ints, then the nonce and its NUL.</summary>
    internal const string KnownHex =
        "01000000" + "6e6a0000" +
        "3031323334353637383961626364656630313233343536373839616263646566" + "00";

    internal static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    internal static byte[] FromHex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void The_invitation_writes_its_golden_bytes()
    {
        Span<byte> buffer = stackalloc byte[DebugChannelProtocol.MaxInvitationBytes];
        var writer = new UserMessageWriter(buffer);
        // The game's old path, written out: two ints and a string through its
        // user-message writer. The dev API's span codec must match it (below).
        writer.WriteInt(Known.Protocol);
        writer.WriteInt(Known.Port);
        writer.WriteString(Known.Nonce);
        Assert.False(writer.Overran);
        Assert.Equal(KnownHex, Hex(buffer[..writer.Length]));
    }

    [Fact]
    public void The_golden_bytes_read_back_and_damaged_ones_are_refused()
    {
        Assert.True(DebugChannelInvitation.TryRead(FromHex(KnownHex), out var read, out _));
        Assert.Equal(Known, read);

        Assert.False(DebugChannelInvitation.TryRead(FromHex(KnownHex + "41"), out _, out string past));
        Assert.Contains("past its three declared fields", past, StringComparison.Ordinal);

        Assert.False(DebugChannelInvitation.TryRead(FromHex(KnownHex[..10]), out _, out string cut));
        Assert.Equal("the invitation ended mid-field", cut);
    }

    [Fact]
    public void The_debug_apis_span_codec_writes_and_reads_the_same_bytes()
    {
        Span<byte> buffer = stackalloc byte[64];
        int written = Known.Write(buffer);
        Assert.Equal(KnownHex, Hex(buffer[..written]));
        Assert.Equal(-1, Known.Write(stackalloc byte[written - 1]));

        Assert.True(DebugChannelInvitation.TryRead(FromHex(KnownHex), out var read, out _));
        Assert.Equal(Known, read);
        Assert.False(DebugChannelInvitation.TryRead(FromHex(KnownHex + "41"), out _, out _));
    }

    [Fact]
    public void The_games_own_spellings_match_the_protocol()
    {
        // The game no longer names the protocol (§5.5); it keeps its own copies
        // of the two things it must agree with, and these hold them equal.
        Assert.Equal(DebugChannelProtocol.ClientParam, SourceSharp.Client.DebugApiClientLoader.EnableParam);
        Assert.Equal(DebugChannelProtocol.MaxInvitationBytes, SourceSharp.Shared.DebugChannelInvite.MaxBytes);
        Assert.Equal(DebugChannelProtocol.MessageName, SourceSharp.Shared.DebugChannelInviteMessage.MessageName);
    }
}
