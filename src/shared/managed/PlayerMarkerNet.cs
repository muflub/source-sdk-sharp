using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>How a marker shows.</summary>
public enum PlayerMarkerKind : byte
{
    /// <summary>A place: a ping on the minimap and a label in the world, through walls.</summary>
    Exact,

    /// <summary>A rough area: a large soft circle on the minimap, and nothing in the world.</summary>
    Area,
}

/// <summary>Shows one player a place on the map: where, what it is called, how, and for how long.</summary>
public partial struct PlayerMarker
{
    public partial Vector Position { get; set; }

    public partial byte Kind { get; set; }

    /// <summary>An area's radius, world units; 0 for a place.</summary>
    public partial float Radius { get; set; }

    public partial float Seconds { get; set; }

    [UserMessageString(MaxLength = 32)]
    public partial string Label { get; set; }
}

/// <summary>What a marker's arrival tells the client.</summary>
public interface IPlayerMarkerHook
{
    void OnPlayerMarker(PlayerMarker marker);
}

[UserMessage("PlayerMarker")]
public sealed class PlayerMarkerMessage : IUserMessage<PlayerMarker>
{
    private readonly IHooks<IPlayerMarkerHook>? _hooks;

    /// <summary>The no-container fallback's, and the server's: a marker goes nowhere.</summary>
    public PlayerMarkerMessage()
    {
    }

    [Inject]
    public PlayerMarkerMessage(IHooks<IPlayerMarkerHook>? hooks) => _hooks = hooks;

    public void OnReceived(PlayerMarker message)
        => _hooks?.Each(nameof(IPlayerMarkerHook.OnPlayerMarker), hook => hook.OnPlayerMarker(message));
}
