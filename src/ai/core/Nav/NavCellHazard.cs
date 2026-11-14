namespace SourceSharp.Ai;

/// <summary>
/// How deadly each cell of one graph is for one side (plan_bots_teams_tf2_part_g G.4, lane G4a): the
/// seconds a death is expected to cost a body for each second it spends in the cell, from what players
/// did there (<see cref="ExperienceRoutes"/>). A route search (<see cref="FlowField.Build"/>) makes a
/// step into a cell dearer by its share of that, at the searching bot's own weight, and never more than
/// <see cref="MaxScale"/> times the step: a cost, never a wall. Immutable once made, so a field built on
/// a worker reads it as it was published.
/// </summary>
public sealed class NavCellHazard
{
    /// <summary>
    /// The most a cell's hazard multiplies a step into it, over the step's own cost: a cell as deadly as
    /// can be costs four times its walk, so a deadly bin on the only way through makes that way dearer
    /// and leaves it open.
    /// </summary>
    public const float MaxScale = 3f;

    private readonly float[] _seconds;

    /// <param name="graph">The graph whose cells <paramref name="seconds"/> are, one value a cell.</param>
    /// <param name="seconds">Each cell's seconds of death a second, 0 where nobody died; the array is kept, not copied.</param>
    public NavCellHazard(NavGraph graph, float[] seconds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(seconds);
        if (seconds.Length != graph.CellCount)
            throw new ArgumentException($"{seconds.Length} values for {graph.CellCount} cells", nameof(seconds));
        _seconds = seconds;
        GraphHash = graph.Hash;
        CellCount = graph.CellCount;
        int costed = 0;
        foreach (float s in seconds)
            costed += s > 0f ? 1 : 0;
        Costed = costed;
    }

    /// <summary>The hash of the graph it was made over.</summary>
    public ulong GraphHash { get; }

    public int CellCount { get; }

    /// <summary>How many cells carry a cost.</summary>
    public int Costed { get; }

    /// <summary>What it holds, in bytes: one float a cell.</summary>
    public long Bytes => (long)CellCount * sizeof(float);

    /// <summary>Whether it was made over <paramref name="graph"/>.</summary>
    public bool IsOver(NavGraph graph) => graph is not null && graph.Hash == GraphHash && graph.CellCount == CellCount;

    /// <summary>The seconds of death a second spent in <paramref name="cell"/> costs; 0 off the graph.</summary>
    public float SecondsAt(int cell) => (uint)cell < (uint)_seconds.Length ? _seconds[cell] : 0f;

    /// <summary>
    /// What a step into <paramref name="cell"/> costs over its own, as a share of it, for a body of
    /// <paramref name="weight"/>: its hazard times the weight, at most <see cref="MaxScale"/>.
    /// </summary>
    public float Scale(int cell, float weight) => MathF.Min(MaxScale, weight * SecondsAt(cell));
}
