using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// Where the routes the brains' snapshot makes of the level's graph are published (lane G4a,
/// <see cref="ExperienceRoutes"/>): <see cref="ExperienceService"/> sets them with the snapshot they were
/// built from, on the game's thread, and the game's navigation profile hands a side's field to each of
/// its players' searches. Its own service so the profile, which the navigation is built with, need not
/// hold the feed, which holds the navigation (as <c>TFBlastLinks</c>). Read from any thread.
/// </summary>
[SideService(Side.Server)]
public sealed class ExperienceRouteSlot
{
    private volatile ExperienceRoutes _current = ExperienceRoutes.None;

    /// <summary>The routes of the snapshot the brains read now; none while there is no store, graph or death.</summary>
    public ExperienceRoutes Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
