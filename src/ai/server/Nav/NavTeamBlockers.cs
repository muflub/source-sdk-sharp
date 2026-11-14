namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// A volume solid to every game team but <paramref name="Team"/>'s own players, as a player who
/// knows the map knows it (TF's respawn room visualizer, plan_bots_teams_tf2 C.7): a game's
/// <see cref="INavProfile.TeamSolids"/> reads them from the map's entities.
/// </summary>
public readonly record struct TeamSolid(AiVector Mins, AiVector Maxs, int Team);

/// <summary>
/// The cells each team's players cannot enter for the level's <see cref="TeamSolid"/>s: a cell
/// is blocked for a team when a body standing on it would overlap a volume solid to that team.
/// Made once at level start, immutable, one <see cref="NavCellBlocks"/> per team a volume blocks;
/// a player's obstacle state carries its team's (<see cref="NavObstacleState.TeamCells"/>).
/// </summary>
public sealed class NavTeamBlockers
{
    private readonly Dictionary<int, NavCellBlocks> _byTeam;

    private NavTeamBlockers(Dictionary<int, NavCellBlocks> byTeam) => _byTeam = byTeam;

    /// <summary>No volume blocks any team.</summary>
    public static NavTeamBlockers None => new([]);

    /// <summary>How many cells each team is kept out of, by team.</summary>
    public IReadOnlyDictionary<int, int> BlockedCells => _byTeam.ToDictionary(p => p.Key, p => p.Value.Count().Blocked);

    /// <summary>The cells <paramref name="team"/>'s players cannot enter, or null when none.</summary>
    public NavCellBlocks? For(int team) => _byTeam.GetValueOrDefault(team);

    /// <summary>
    /// The blocked cells of every playing team for <paramref name="solids"/> on
    /// <paramref name="graph"/>, for a body of <paramref name="numbers"/>: a cell whose
    /// footprint comes within half the hull of a volume's and whose floor lies from a
    /// standing body's height under the volume to its top.
    /// </summary>
    public static NavTeamBlockers From(NavGraph graph, IReadOnlyList<TeamSolid> solids, NavNumbers numbers)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(solids);
        var byTeam = new Dictionary<int, byte[]>();
        float reach = numbers.HullWidth * 0.5f;
        var near = new List<int>();
        foreach (TeamSolid solid in solids)
        {
            graph.CellsNear(solid.Mins.X - reach, solid.Mins.Y - reach, solid.Maxs.X + reach, solid.Maxs.Y + reach, near);
            for (int team = TeamRule.FirstGameTeam; team <= TeamRule.LastGameTeam; team++)
            {
                if (team == solid.Team)
                    continue;
                foreach (int c in near)
                {
                    NavCell cell = graph.Cell(c);
                    bool overlaps = cell.MaxX > solid.Mins.X - reach && cell.MinX < solid.Maxs.X + reach
                                    && cell.MaxY > solid.Mins.Y - reach && cell.MinY < solid.Maxs.Y + reach
                                    && cell.FloorZ >= solid.Mins.Z - numbers.StandHeight && cell.FloorZ <= solid.Maxs.Z;
                    if (!overlaps)
                        continue;
                    if (!byTeam.TryGetValue(team, out byte[]? cells))
                        byTeam[team] = cells = new byte[graph.CellCount];
                    cells[c] = NavCellBlocks.Blocked;
                }
            }
        }

        return new NavTeamBlockers(byTeam.ToDictionary(p => p.Key, p => new NavCellBlocks(p.Value)));
    }
}
