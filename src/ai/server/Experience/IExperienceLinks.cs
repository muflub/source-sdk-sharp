namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// A game's links beside the level's graph as the experience keys them (lane G5e: TF2's blast jumps): the feed builds,
/// with each snapshot's routes, which of them the snapshot's <c>link</c> rows leave out
/// (<see cref="ExperienceRoutes.LinksShut"/>). Optional: a game without links (HL2MP) registers none. Read on the game's
/// thread.
/// </summary>
public interface IExperienceLinks
{
    /// <summary>The level's links with their keys, or null while there are none (being found, no level).</summary>
    ExperienceLinkKeys? Keys { get; }
}
