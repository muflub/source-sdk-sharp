using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One map a vote or the admin menu may change to: its name, the modes that
/// need a map's entities it can play (null when its entities could not be
/// read, as a TF2 map's compressed lump), and whether its <c>.nav3d</c> is
/// installed (bots play only on a map with one).
/// </summary>
public sealed record MapCandidate(string Map, IReadOnlyList<string>? Modes, bool HasNav)
{
    /// <summary>The text after a map's name when it has no navigation.</summary>
    public const string NoNavNote = " [no nav]";

    /// <summary>What the menus show: the map, the modes it is known to play, and a note when it has no nav.</summary>
    public string Label
        => Map + (Modes is { Count: > 0 } modes ? $" ({string.Join(", ", modes)})" : "") + (HasNav ? "" : NoNavNote);
}

/// <summary>
/// The maps a vote or the admin menu offers (plan_bots_teams_tf2 D.3): the
/// map cycle's maps (<c>mapcyclefile</c>, stock's <c>IsMapInMapCycle</c>) that
/// are installed on the GAME paths (stock's <c>VotableMap</c>), each with the
/// modes it can play from the catalog and its nav state. The cycle can name a
/// map that is not installed; it is no candidate.
/// </summary>
public sealed class MapCandidates(IEngineFiles files, MapCycleFileConVar mapCycle, GameModeCatalog catalog, SvModeNextConVar modeNext)
{
    /// <summary>Every installed map of the cycle, in cycle order, once each.</summary>
    public IReadOnlyList<MapCandidate> All()
        => [.. Intermission.ReadCycle(files, mapCycle.Value)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Where(Installed)
                           .Select(map => new MapCandidate(map, catalog.EntityModesOn(map), files.GameFileSize(NavPath(map)) >= 0))];

    /// <summary>
    /// The candidates that can play <paramref name="modeId"/>: a mode that
    /// plays anywhere keeps them all; one that needs entities drops a map
    /// known to lack them and keeps one whose entities are unknown.
    /// </summary>
    public IReadOnlyList<MapCandidate> For(string? modeId)
    {
        var all = All();
        if (modeId is not { Length: > 0 } || catalog.Find(modeId) is not { RequiresAnyOf.Count: > 0 })
            return all;
        return [.. all.Where(c => c.Modes is null || c.Modes.Contains(modeId, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// What players may vote to play next: the candidates of the mode a vote
    /// kept for the coming levels (<c>sv_mode_next</c>), or every candidate
    /// when none is kept and each map picks its own.
    /// </summary>
    public IReadOnlyList<MapCandidate> Votable() => For(modeNext.Value);

    /// <summary>The map's bsp is on the GAME paths.</summary>
    private bool Installed(string map)
        => map.Length > 0 && !map.Contains("..", StringComparison.Ordinal) && !map.Contains('/', StringComparison.Ordinal)
           && files.GameFileSize($"maps/{map}.bsp") >= 0;

    /// <summary>Where the AI looks for a map's navigation (its NavLoader's path).</summary>
    public static string NavPath(string map) => $"maps/{map}.nav3d";
}
