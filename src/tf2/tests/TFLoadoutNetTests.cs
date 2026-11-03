using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The loadout wire pair (D2): the client's one-byte ask over a
/// SourceSharp net message (<see cref="LoadoutAsk"/>), and the server's
/// reply as a TF2 user message payload whose members ride the generator's
/// byte/uint/ulong kinds.
/// </summary>
public sealed class TFLoadoutNetTests
{
    [Fact]
    public void TheAskIsOneClassByte()
    {
        byte[] payload = LoadoutAsk.Write((byte)TFClass.Soldier);

        Assert.Equal([(byte)TFClass.Soldier], payload);
        Assert.True(LoadoutAsk.TryRead(payload, out byte cls));
        Assert.Equal((byte)TFClass.Soldier, cls);
    }

    [Fact]
    public void AnAskThatIsNotOneByteIsRefused()
    {
        Assert.False(LoadoutAsk.TryRead([], out _));
        Assert.False(LoadoutAsk.TryRead([(byte)TFClass.Soldier, 9], out _));
    }

    [Fact]
    public void TheReplyRoundTrips()
    {
        var reply = new LoadoutReply
        {
            Class = (byte)TFClass.Spy,
            EquippedSlots = [1, 2, 6, 4, 7, 8, 10, 9],
            EquippedDefindexes = [4, 735, 27, 735, 0, 0, 0, 0],
            OwnedDefindexes = [4, 735],
            OwnedSlots = [1, 6],
        };

        var buffer = new byte[256];
        int written = reply.Write(buffer);
        Assert.True(written > 0);

        var reader = new UserMessageReader(buffer.AsSpan(0, written));
        var got = LoadoutReply.Read(ref reader);
        Assert.Equal(reply.Class, got.Class);
        Assert.Equal(reply.EquippedSlots, got.EquippedSlots);
        Assert.Equal(reply.EquippedDefindexes, got.EquippedDefindexes);
        Assert.Equal(reply.OwnedDefindexes, got.OwnedDefindexes);
        Assert.Equal(reply.OwnedSlots, got.OwnedSlots);
    }

    [Fact]
    public void TheReplyAtItsMostFitsTheEngineLimit()
    {
        // The fixed cost of the fullest reply: a class with all eight
        // panels and thirteen owned items is under the 255-byte cap, so a
        // send cannot fail.
        var reply = new LoadoutReply
        {
            Class = 3,
            EquippedSlots = Enumerable.Repeat((byte)0, LoadoutReply.MaxSlots).ToArray(),
            EquippedDefindexes = Enumerable.Repeat(7u, LoadoutReply.MaxSlots).ToArray(),
            OwnedDefindexes = Enumerable.Repeat(7u, LoadoutReply.MaxOwned).ToArray(),
            OwnedSlots = Enumerable.Repeat((byte)0, LoadoutReply.MaxOwned).ToArray(),
        };

        var buffer = new byte[256];
        // Class, then each array's count byte and elements: 1 + (1+8) +
        // (1+32) + (1+52) + (1+13).
        Assert.Equal(1 + 1 + 8 + 1 + 32 + 1 + 52 + 1 + 13, reply.Write(buffer));
    }

    [Fact]
    public void TheReplyRefusesCountsOverItsMaxima()
    {
        var buffer = new byte[256];
        buffer[0] = 3;                                  // Class
        buffer[1] = LoadoutReply.MaxSlots + 1;          // EquippedSlots' count, one over

        var reader = new UserMessageReader(buffer);
        _ = LoadoutReply.Read(ref reader);
        Assert.True(reader.Overran, "a count over the most is refused, not read");
    }
}
