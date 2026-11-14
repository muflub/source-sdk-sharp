using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The level's blast links as they are published (lane G5c-2b): <see cref="TFJumps"/> sets them when its worker has
/// them and empties them with the level, and the navigation profile hands them to a Soldier's searches
/// (<see cref="Nav.TFNavProfile.LinksFor"/>). Its own service so the profile, which the navigation is built with,
/// need not hold the jumps, which hold the navigation. Read from any thread.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBlastLinks
{
    private volatile JumpLinkSet? _set;

    /// <summary>The level's links with their jumps, or null while they are being found and for no level.</summary>
    public JumpLinkSet? Set
    {
        get => _set;
        set => _set = value;
    }
}
