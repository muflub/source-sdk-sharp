using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The server's generic marker seam: one player's screen, and nobody else's.</summary>
public class PlayerMarkersTests
{
    private sealed class Sender : IUserMessageSender
    {
        public readonly List<(string Key, int Player, bool Reliable)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true) => Sent.Add((key, player, reliable));
    }

    /// <summary>A marker goes to the slot it names, reliably, under its own key.</summary>
    [Fact]
    public void AMarkerGoesToOneSlot()
    {
        var sender = new Sender();
        Assert.True(new PlayerMarkers(sender).Show(3, new Vector(1f, 2f, 3f), "Kit", PlayerMarkerKind.Exact, 0f, 10f));
        Assert.Equal((UserMessageKey.Of(typeof(PlayerMarkerMessage)), 3, true), Assert.Single(sender.Sent));
    }

    /// <summary>Slot 0 is the send path's broadcast: a marker for nobody is refused, never shown to all.</summary>
    [Fact]
    public void SlotZeroIsRefusedNeverABroadcast()
    {
        var sender = new Sender();
        Assert.False(new PlayerMarkers(sender).Show(0, new Vector(1f, 2f, 3f), "Kit", PlayerMarkerKind.Exact, 0f, 10f));
        Assert.Empty(sender.Sent);
    }
}
