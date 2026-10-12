using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The payload model over a live message, <see cref="JalopyRadar"/>: the code
/// the build actually generated, the dispatch into the typed handler, and the
/// send that finds the key from the handler. The generator itself is covered
/// over source text in <see cref="UserMessagePayloadGeneratorTests"/>.
/// </summary>
public class UserMessagePayloadTests
{
    private static byte[] Written(JalopyRadar payload)
    {
        var buffer = new byte[UserMessageSender.MaxPayloadBytes];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits");
        return buffer[..n];
    }

    [Fact]
    public void TheJalopyLayoutIsPinned()
    {
        // The layout, stated once: a change to it has to change this line.
        Assert.Equal(new byte[] { 7, 1 }, Written(new JalopyRadar { Contacts = 7, FastUpdate = true }));
    }

    [Fact]
    public void EveryMemberSurvivesTheRoundTrip()
    {
        var reader = new UserMessageReader(Written(new JalopyRadar { Contacts = 42, FastUpdate = true }));
        var got = JalopyRadar.Read(ref reader);

        Assert.False(reader.Overran);
        Assert.Equal((42, true), (got.Contacts, got.FastUpdate));
    }

    [Fact]
    public void TheHandlerIsHandedTheTypedPayload()
    {
        var handler = new UpdateJalopyRadarMessage();
        var seen = new List<JalopyRadar>();
        handler.Fired += seen.Add;

        ((IUserMessage)handler).OnReceived(new UserMessageReader(new byte[] { 3, 0 }));

        var only = Assert.Single(seen);
        Assert.Equal((3, false), (only.Contacts, only.FastUpdate));
    }

    [Fact]
    public void AShortPayloadIsNotDispatched()
    {
        var handler = new UpdateJalopyRadarMessage();
        int fired = 0;
        handler.Fired += _ => fired++;

        ((IUserMessage)handler).OnReceived(new UserMessageReader(new byte[] { 3 }));
        Assert.Equal(0, fired);

        ((IUserMessage)handler).OnReceived(new UserMessageReader(new byte[] { 3, 1 }));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SendingAPayloadUsesItsHandlersKey()
    {
        var engine = Mock();

        Assert.True(((IUserMessageSender)engine).Send(new JalopyRadar { Contacts = 5 }, player: 2));

        var sent = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("UpdateJalopyRadar", sent.Arg(0));
        Assert.Equal(2, sent.Arg(2));
        Assert.Equal(new byte[] { 5, 0 }, (byte[])sent.Arg(4)!);
    }

    [Fact]
    public void APayloadOverTheLimitIsNotSent()
    {
        // CUserMessagePayload's capacity check, carried over: a payload the
        // engine's 255 bytes cannot hold is refused before it leaves, whole.
        var engine = Mock();

        Assert.False(((IUserMessageSender)engine).Send(new HudText { Name = new string('x', 300) }, player: 1));
        Assert.True(((IUserMessageSender)engine).Send(new HudText { Name = new string('x', 254) }, player: 1));

        var sent = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal(255, ((byte[])sent.Arg(4)!).Length);
    }
}
