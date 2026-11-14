namespace SourceSharp.Ai;

/// <summary>How a link is crossed: the reach an agent needs for it (<see cref="NavLinks.ReachFor"/>).</summary>
public enum NavLinkKind : byte
{
    /// <summary>A blast jump (TF2's Soldier, plan_bots_teams_tf2_part_g G.5): taken only with <see cref="JumpReach.Blast"/>.</summary>
    Blast = 0,
}

/// <summary>
/// One link: a way from cell <paramref name="From"/> to cell <paramref name="To"/> that is no
/// edge of the graph (a blast jump), how it is crossed, what it takes (the seconds from its
/// first command to the touch, the health it costs, the share of perturbed tries that make it),
/// and <paramref name="Id"/>, which names it in its finder's own table (the recipe).
/// </summary>
public readonly record struct NavLink(int From, int To, NavLinkKind Kind, float Seconds, float Health, float Robustness, int Id)
{
    /// <summary>
    /// Where the body stands at the link's first command (a blast jump's run-up start), which a
    /// follower leads a bot to; default (unset): the centre of <see cref="From"/>.
    /// </summary>
    public AiVector Start { get; init; }
}

/// <summary>
/// What a follower says of the link its route takes next (lane G5c-2b): the link's index in
/// <see cref="NavLinks"/> (-1: the next step is no link), the set's <see cref="NavLinks.Hash"/>, the
/// objective the route goes to, and whether the bot stands at the link's start, ready for it.
/// The follower only says it: whether the link is taken now is the brain's. The default, of no
/// set (<see cref="Links"/> 0), is no link.
/// </summary>
public readonly record struct NavLinkStep(int Link, ulong Links, AiVector Objective, bool AtStart)
{
    /// <summary>No link next.</summary>
    public static NavLinkStep None => default;

    public bool IsLink => Links != 0UL && Link >= 0;
}

/// <summary>
/// What a link costs a route search, in the graph's units (a walked unit: an edge costs its
/// length and what its kind adds). A link is time, so it is turned into units at the speed of the
/// body that takes it. The weights, and why (plan_bots_teams_tf2_part_g G.5 step 5: "its air
/// time, plus its health cost in seconds, plus a penalty for low robustness"):
/// </summary>
public sealed record NavLinkCosts
{
    /// <summary>
    /// The units a second of a link is worth: the run speed of the body that takes it (a
    /// Soldier's 240 for TF2's blast jumps), so a link of N seconds weighs what N seconds of walking do.
    /// </summary>
    public required float RunSpeed { get; init; }

    /// <summary>
    /// The seconds a hit point is worth. A blast jump spends 27 to 42 of a Soldier's 200 and
    /// nothing gives it back on the way; at 0.03 a jump of 30 health weighs 0.9 s more than its
    /// flight, so a jump is taken where it saves about a second over the walk or more, and not to
    /// shave a few tenths. A ruling's number, not a measurement: a bot that heals faster (a
    /// dispenser beside its route) would weigh it less.
    /// </summary>
    public float HealthSeconds { get; init; } = 0.03f;

    /// <summary>
    /// The seconds a missed jump costs: it comes down short or elsewhere and the bot walks from
    /// there or jumps again. Weighed by the share of tries that miss (one less the robustness), so
    /// a link kept at the pass's floor of 0.6 carries 1.6 s and one that always lands carries none.
    /// </summary>
    public float MissSeconds { get; init; } = 4f;

    /// <summary>The link's weight in seconds: its own, its health's and its misses'.</summary>
    public float Seconds(in NavLink link) => link.Seconds + (link.Health * HealthSeconds) + ((1f - link.Robustness) * MissSeconds);

    /// <summary>The link's cost in the graph's units.</summary>
    public float Cost(in NavLink link) => Seconds(link) * RunSpeed;
}

/// <summary>
/// Links a route search may take beside a graph's edges (lane G5c-2a, Shape A: the links live
/// beside the graph, as its choke points do, so the graph, its hash, its cache and every recording
/// stay as they were). Immutable once built, for one graph (its hash and cell count are checked by
/// whoever searches with both), indexed by the cell a link leaves and the cell it reaches.
///
/// <para>
/// A link may be cheaper than the straight line between its cells (a jump flies faster than a
/// walk), which the straight-line estimate of a search assumes no step is. So the set says the
/// least cost per straight-line unit any of its links has (<see cref="EstimateScale"/>, never over
/// 1): a search that may take them scales its estimate by it and stays a cheapest-path search.
/// </para>
/// </summary>
public sealed class NavLinks
{
    private readonly NavLink[] _links;
    private readonly float[] _cost;
    private readonly int[] _outStart;
    private readonly int[] _out;
    private readonly int[] _inStart;
    private readonly int[] _in;

    private NavLinks(NavLink[] links, float[] cost, int cellCount, ulong graphHash, float scale)
    {
        _links = links;
        _cost = cost;
        CellCount = cellCount;
        GraphHash = graphHash;
        EstimateScale = scale;
        foreach (NavLink l in links)
            Reaches |= ReachFor(l.Kind);
        (_out, _outStart) = Csr(cellCount, links, static l => l.From);
        (_in, _inStart) = Csr(cellCount, links, static l => l.To);
        Hash = ComputeHash();
    }

    /// <summary>
    /// The set of <paramref name="links"/> over <paramref name="graph"/>, each costed by
    /// <paramref name="costs"/>, in the order given (the order is a link's index). A link whose
    /// cell is not the graph's is refused.
    /// </summary>
    public static NavLinks Build(NavGraph graph, IReadOnlyList<NavLink> links, NavLinkCosts costs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(costs);
        var copy = new NavLink[links.Count];
        var cost = new float[links.Count];
        float scale = 1f;
        for (int i = 0; i < copy.Length; i++)
        {
            NavLink l = links[i];
            if ((uint)l.From >= (uint)graph.CellCount || (uint)l.To >= (uint)graph.CellCount)
                throw new ArgumentException($"link {i} joins cells {l.From} and {l.To} of a graph of {graph.CellCount}", nameof(links));
            copy[i] = l;
            cost[i] = costs.Cost(l);
            if (!(cost[i] >= 0f))
                throw new ArgumentException($"link {i} costs {cost[i]}", nameof(links));
            float straight = graph.Cell(l.From).Centre.DistanceTo(graph.Cell(l.To).Centre);
            if (straight > 0f)
                scale = MathF.Min(scale, cost[i] / straight);
        }

        return new NavLinks(copy, cost, graph.CellCount, graph.Hash, scale);
    }

    /// <summary>
    /// The arguments of <see cref="AbilityCall"/> from here up name a link, not a plan of the
    /// game's: a brain that begins link N sends <see cref="Argument"/>(N), so a follower holding at
    /// its start knows the jump has been begun. A game's own arguments stay under it.
    /// </summary>
    public const int ArgumentBase = 1 << 22;

    /// <summary>The ability argument that names link <paramref name="index"/>.</summary>
    public static int Argument(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, AbilityCall.MaxArgument - ArgumentBase);
        return ArgumentBase + index;
    }

    /// <summary>The link an ability argument names, or -1 for one that names none.</summary>
    public static int LinkOf(int argument) => argument >= ArgumentBase && argument <= AbilityCall.MaxArgument ? argument - ArgumentBase : -1;

    /// <summary>No link, over <paramref name="graph"/>.</summary>
    public static NavLinks Empty(NavGraph graph) => Build(graph, [], new NavLinkCosts { RunSpeed = 1f });

    /// <summary>The cell count of the graph the set is over.</summary>
    public int CellCount { get; }

    /// <summary>The hash of the graph the set is over (<see cref="NavGraph.Hash"/>).</summary>
    public ulong GraphHash { get; }

    /// <summary>The least of every link's cost over the straight line between its cells' centres, and 1: what a search's straight-line estimate is scaled by.</summary>
    public float EstimateScale { get; }

    /// <summary>The reaches its links need between them: an agent with none of them is admitted no link.</summary>
    public JumpReach Reaches { get; }

    /// <summary>A stable FNV-1a hash of every link and its cost, and the graph's hash: what a recording would name the set by.</summary>
    public ulong Hash { get; }

    public int Count => _links.Length;

    public NavLink this[int index] => _links[index];

    /// <summary>Link <paramref name="index"/>'s cost in the graph's units.</summary>
    public float CostOf(int index) => _cost[index];

    /// <summary>The indices of the links leaving <paramref name="cell"/>.</summary>
    public ReadOnlySpan<int> From(int cell) => _out.AsSpan(_outStart[cell], _outStart[cell + 1] - _outStart[cell]);

    /// <summary>The indices of the links reaching <paramref name="cell"/> (a field's search runs backwards).</summary>
    public ReadOnlySpan<int> To(int cell) => _in.AsSpan(_inStart[cell], _inStart[cell + 1] - _inStart[cell]);

    /// <summary>Whether the set is over <paramref name="graph"/>.</summary>
    public bool IsOver(NavGraph graph) => graph is not null && graph.Hash == GraphHash && graph.CellCount == CellCount;

    /// <summary>The reach an agent needs to take a link of <paramref name="kind"/>.</summary>
    public static JumpReach ReachFor(NavLinkKind kind) => kind switch
    {
        NavLinkKind.Blast => JumpReach.Blast,
        _ => (JumpReach)0xFF,
    };

    /// <summary>
    /// Whether an agent of <paramref name="cls"/> whose state is <paramref name="obstacles"/> may
    /// take link <paramref name="index"/>: it has the link's reach, the health the link costs is
    /// no more than it may spend (<see cref="NavObstacleState.LinkHealth"/>), both cells admit its
    /// body, and the cell it reaches is not blocked.
    /// </summary>
    public bool Admits(NavGraph graph, int index, ClearanceClass cls, NavObstacleState obstacles)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(obstacles);
        NavLink l = _links[index];
        JumpReach needs = ReachFor(l.Kind);
        return (obstacles.Reach & needs) == needs
               && l.Health <= obstacles.LinkHealth
               && graph.Cell(l.From).Admits(cls) && graph.Cell(l.To).Admits(cls)
               && !obstacles.CellBlocked(l.To);
    }

    private static (int[] Order, int[] Start) Csr(int cells, NavLink[] links, Func<NavLink, int> key)
    {
        var start = new int[cells + 1];
        foreach (NavLink l in links)
            start[key(l) + 1]++;
        for (int i = 0; i < cells; i++)
            start[i + 1] += start[i];
        var fill = (int[])start.Clone();
        var order = new int[links.Length];
        for (int i = 0; i < links.Length; i++)
            order[fill[key(links[i])]++] = i;
        return (order, start);
    }

    private ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }

        void MixF(float f) => Mix(BitConverter.SingleToInt32Bits(f));

        Mix((int)GraphHash);
        Mix((int)(GraphHash >> 32));
        Mix(CellCount);
        Mix(_links.Length);
        for (int i = 0; i < _links.Length; i++)
        {
            NavLink l = _links[i];
            Mix(l.From);
            Mix(l.To);
            Mix((int)l.Kind);
            MixF(l.Seconds);
            MixF(l.Health);
            MixF(l.Robustness);
            Mix(l.Id);
            MixF(l.Start.X);
            MixF(l.Start.Y);
            MixF(l.Start.Z);
            MixF(_cost[i]);
        }

        return h;
    }
}

/// <summary>
/// Where a level's links are published (as <see cref="ChokeSlot"/> publishes its choke points):
/// empty until the worker that finds them is done; read from any thread.
/// </summary>
public sealed class NavLinkSlot
{
    private volatile NavLinks? _links;

    /// <summary>The links, or null while they are being found (or none could be).</summary>
    public NavLinks? Links
    {
        get => _links;
        set => _links = value;
    }
}
