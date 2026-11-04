using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Shows one player a marker on their own screen: a ping on the minimap, and for a place a label
/// in the world seen through walls. Generic: a mod calls it with a position, a label, a kind and a
/// lifetime, and the core knows nothing of why. Nobody else's screen shows it, a spectator's included.
/// </summary>
public interface IPlayerMarkers
{
    /// <summary>Sends the marker to the player in <paramref name="player"/> alone.</summary>
    bool Show(int player, Vector position, string label, PlayerMarkerKind kind, float radius, float seconds);
}

/// <summary><see cref="IPlayerMarkers"/> over the user message: a reliable message to that one slot.</summary>
public sealed class PlayerMarkers(IUserMessageSender messages) : IPlayerMarkers
{
    public bool Show(int player, Vector position, string label, PlayerMarkerKind kind, float radius, float seconds)
    {
        // Slot 0 is a broadcast on the send path: a marker for nobody in particular is no marker.
        if (player <= 0)
            return false;
        return messages.Send(new PlayerMarker { Position = position, Kind = (byte)kind, Radius = radius, Seconds = seconds, Label = label }, player, reliable: true);
    }
}
