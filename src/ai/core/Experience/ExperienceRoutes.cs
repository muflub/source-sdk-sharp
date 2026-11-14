namespace SourceSharp.Ai;

/// <summary>
/// What a snapshot of the experience makes of one graph's routes (plan_bots_teams_tf2_part_g G.4, lane
/// G4a): for each role (a TF2 team) whose players died somewhere, a <see cref="NavCellHazard"/> over
/// the graph, from the deaths and seconds of that role's players of every stage and class. Built once a
/// snapshot, off the game's thread, and published with it; a role with no death has no field, so
/// HL2MP (no store) and an empty store route exactly as before. With the level's links as the experience keys them
/// (lane G5e), it also says which of them the snapshot's <c>link</c> rows leave out (<see cref="LinksShut"/>,
/// <see cref="LinkTrust"/>), so the bots' searches stop taking a link that keeps failing at the same snapshot as the
/// rest of what the snapshot says reaches them.
/// </summary>
public sealed class ExperienceRoutes
{
    /// <summary>
    /// The seconds one death costs a body on its way: TF2's respawn wave (10 s on ctf_2fort) and the
    /// walk back to where it fell. A ruling's number, not a measurement: it turns a hazard (deaths a
    /// second present) into route seconds (seconds lost a second spent).
    /// </summary>
    public const float DeathSeconds = 20f;

    private readonly Dictionary<int, NavCellHazard> _byRole;

    private ExperienceRoutes(MapExperience of, NavGraph? graph, Dictionary<int, NavCellHazard> byRole, double buildMicros, ExperienceLinkKeys? keys = null,
                             NavLinkMask? shut = null)
    {
        Of = of;
        Graph = graph;
        _byRole = byRole;
        BuildMicros = buildMicros;
        LinkKeys = keys;
        LinksShut = shut;
    }

    /// <summary>No routes: no snapshot, or no graph.</summary>
    public static ExperienceRoutes None => new(MapExperience.Empty, null, new Dictionary<int, NavCellHazard>(), 0d);

    /// <summary>The level's links as the experience keys them, as these were built with; null for none (no links, or a game without).</summary>
    public ExperienceLinkKeys? LinkKeys { get; }

    /// <summary>The links of <see cref="LinkKeys"/> the snapshot leaves out, or null with no keys.</summary>
    public NavLinkMask? LinksShut { get; }

    /// <summary>The snapshot these were made from.</summary>
    public MapExperience Of { get; }

    /// <summary>The graph these are over, or null for none.</summary>
    public NavGraph? Graph { get; }

    /// <summary>The CPU time the build took, in microseconds, as the builder's clock read it (0 without one).</summary>
    public double BuildMicros { get; }

    /// <summary>The roles that have a field, in order.</summary>
    public IReadOnlyList<int> Roles => [.. _byRole.Keys.Order()];

    /// <summary>The field of <paramref name="role"/>'s players, or null where none of them died.</summary>
    public NavCellHazard? For(int role) => _byRole.GetValueOrDefault(role);

    /// <summary>The cells that carry a cost, summed over the roles.</summary>
    public int Costed => _byRole.Values.Sum(h => h.Costed);

    /// <summary>What the fields hold, in bytes.</summary>
    public long Bytes => _byRole.Values.Sum(h => h.Bytes);

    /// <summary>
    /// The fields of <paramref name="snapshot"/> over <paramref name="graph"/>: one per role with a death
    /// row, each cell's value the hazard of its bin (<see cref="ExperienceView.Hazard"/>) times
    /// <see cref="DeathSeconds"/>. A cell's bin is the one its floor's middle is in, where a player
    /// standing in it is sampled. <paramref name="cpuMicros"/> times the build, or nothing. With <paramref name="links"/>,
    /// the links its rows leave out (<see cref="LinkTrust.Mask"/>).
    /// </summary>
    public static ExperienceRoutes Build(MapExperience snapshot, NavGraph graph, Func<double>? cpuMicros = null, ExperienceLinkKeys? links = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(graph);
        double start = cpuMicros?.Invoke() ?? 0d;
        var byRole = new Dictionary<int, NavCellHazard>();
        foreach (int role in snapshot.Cells.Where(c => c.Value.Deaths > 0d).Select(c => c.Key.Context.Role).Distinct().Order())
        {
            ExperienceView view = snapshot.For(new ExperienceContext(ExperienceContext.Any, role, ExperienceContext.Any));
            var deadly = new Dictionary<WorldBin, float>();
            foreach (CellKey key in snapshot.Cells.Keys)
            {
                if (key.Context.Role == role && !deadly.ContainsKey(key.Bin) && view.Hazard(key.Bin) is > 0d and double h)
                    deadly[key.Bin] = (float)(h * DeathSeconds);
            }

            if (deadly.Count == 0)
                continue;
            var seconds = new float[graph.CellCount];
            for (int c = 0; c < seconds.Length; c++)
                seconds[c] = deadly.GetValueOrDefault(BinOf(graph.Cell(c)));
            byRole[role] = new NavCellHazard(graph, seconds);
        }

        NavLinkMask? shut = links is null ? null : LinkTrust.Mask(snapshot, links);
        return new ExperienceRoutes(snapshot, graph, byRole, (cpuMicros?.Invoke() ?? 0d) - start, links, shut);
    }

    /// <summary>The bin a cell's players are counted in: the one holding the middle of its floor.</summary>
    public static WorldBin BinOf(in NavCell cell) => WorldBin.Of(cell.Centre);
}
