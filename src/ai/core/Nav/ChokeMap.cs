using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>A place most routes must pass: a cell, where it is, and its share of the map's busiest route count (1 the busiest).</summary>
public readonly record struct ChokePoint(CellId Cell, AiVector Position, float Score);

/// <summary>
/// The map's choke points (plan_hl2mp_ai §5.5, user request 2026-11-02): which
/// cells most routes must pass. Route betweenness between the places people go, the
/// map's spawns and pickups (or, given none, cells spread over the graph): from each
/// of up to <see cref="Sources"/> of them every cheapest route to each other is counted
/// once, shared equally among its ties (Brandes' dependency accumulation on the
/// directed graph, every door open). A doorway or
/// a corridor takes all of the routes between its two sides on a few cells; a
/// room spreads them over many. Route counts alone rate the open floor where a big
/// room's routes converge before its door, so a place's routes are divided by how
/// open it is (its <see cref="Clearance"/>: the walk to the nearest cell with a wall
/// beside it). Smoothed over a cell and its neighbours, so one lucky cell of a tie
/// is not a choke, and normalised to the busiest. Pure over the graph and
/// deterministic: the sources are fixed by the cell count and each one's sum is added
/// in their order, whatever thread ran it.
/// It is map knowledge (§5.0): a bot knows the layout before it plays.
/// </summary>
public sealed class ChokeMap
{
    /// <summary>What changes when the computation does: a cached map of another revision is not read.</summary>
    public const int Revision = 1;

    /// <summary>How many route sources the estimate samples, at most.</summary>
    public const int Sources = 96;

    /// <summary>How many separate choke points <see cref="Top"/> lists.</summary>
    public const int TopCount = 16;

    /// <summary>Two choke points are at least this far apart: a corridor is one choice, not ten.</summary>
    public const float TopSeparation = 384f;

    /// <summary>A cell under this share of the busiest is no choke point at all.</summary>
    public const float TopFloor = 0.05f;

    /// <summary>The clearance at which a place's routes count half: about a doorway's half-width.</summary>
    public const float ClearanceHalf = 48f;

    private readonly float[] _score;

    public ChokeMap(float[] score, ImmutableArray<ChokePoint> top)
    {
        ArgumentNullException.ThrowIfNull(score);
        _score = score;
        Top = top;
    }

    /// <summary>The separate choke points, busiest first.</summary>
    public ImmutableArray<ChokePoint> Top { get; }

    public int CellCount => _score.Length;

    /// <summary>The choke point <paramref name="i"/> of <see cref="Top"/>.</summary>
    public ChokePoint Value(int i) => Top[i];

    /// <summary>A cell's share of the busiest cell's routes, 0..1.</summary>
    public float ScoreOf(int cell) => _score[cell];

    /// <summary>The scores as a cache writes them.</summary>
    public ReadOnlySpan<float> Scores => _score;

    /// <summary>
    /// The map of <paramref name="graph"/> between <paramref name="places"/>, on the
    /// threads <paramref name="parallel"/> allows. With no place on the graph, between
    /// cells spread over it.
    /// </summary>
    public static ChokeMap Compute(NavGraph graph, IEnumerable<AiVector>? places = null, int parallel = 4, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        int n = graph.CellCount;
        if (n == 0)
            return new ChokeMap([], []);

        var ends = new SortedSet<int>();
        foreach (AiVector p in places ?? [])
        {
            CellId c = graph.FindCell(p + new AiVector(0f, 0f, 1f));
            if (c.IsValid)
                ends.Add(c.Index);
        }

        bool[]? target = null;
        if (ends.Count >= 2)
        {
            target = new bool[n];
            foreach (int c in ends)
                target[c] = true;
        }

        int[] sources = target is not null ? Spread([.. ends]) : SourceCells(n);
        var perSource = new double[sources.Length][];
        Parallel.For(0, sources.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel), CancellationToken = cancel },
                     () => new Scratch(n),
                     (i, _, scratch) =>
                     {
                         perSource[i] = scratch.FromSource(graph, sources[i], target);
                         return scratch;
                     },
                     _ => { });

        var flow = new double[n];
        foreach (double[] one in perSource)
        {
            for (int c = 0; c < n; c++)
                flow[c] += one[c];
        }

        return FromFlow(graph, flow);
    }

    /// <summary>The places people go on a map: its pickups and its spawns.</summary>
    public static IEnumerable<AiVector> PlacesOf(MapKnowledge map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Items.Select(i => i.Position).Concat(map.Spawns);
    }

    /// <summary>Rebuilds a map from its scores, as a cache holds them: <see cref="Top"/> comes from them the same way.</summary>
    public static ChokeMap FromScores(NavGraph graph, float[] score)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(score);
        if (score.Length != graph.CellCount)
            throw new ArgumentException($"{score.Length} scores for {graph.CellCount} cells", nameof(score));
        return new ChokeMap(score, TopOf(graph, score));
    }

    /// <summary>At most <see cref="Sources"/> of <paramref name="cells"/>, evenly through them.</summary>
    private static int[] Spread(int[] cells)
    {
        if (cells.Length <= Sources)
            return cells;
        var some = new int[Sources];
        for (int i = 0; i < Sources; i++)
            some[i] = cells[(long)i * cells.Length / Sources];
        return some;
    }

    /// <summary>Evenly spread over the cell numbers, which run through the map's columns, so over its floors.</summary>
    private static int[] SourceCells(int cells)
    {
        int count = Math.Min(Sources, cells);
        var sources = new int[count];
        for (int i = 0; i < count; i++)
            sources[i] = (int)((long)i * cells / count);
        return sources;
    }

    /// <summary>The map of <paramref name="graph"/> given how many routes cross each cell: how open a place is counts against them.</summary>
    public static ChokeMap FromFlow(NavGraph graph, double[] flow)
    {
        int n = graph.CellCount;
        var smooth = new double[n];
        float[] clearance = Clearance(graph);
        double most = 0d;
        for (int c = 0; c < n; c++)
        {
            double sum = flow[c];
            int count = 1;
            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (e.Type != NavEdgeType.Walk)
                    continue;
                sum += flow[e.To];
                count++;
            }

            smooth[c] = sum / count / (1d + clearance[c] / ClearanceHalf);
            most = Math.Max(most, smooth[c]);
        }

        var score = new float[n];
        for (int c = 0; c < n && most > 0d; c++)
            score[c] = (float)(smooth[c] / most);
        return new ChokeMap(score, TopOf(graph, score));
    }

    /// <summary>
    /// Each cell's walk to the nearest cell with a wall beside it, in units: one with
    /// fewer walks out than most cells of the map has. A wide-open floor's middle is
    /// far from any; a doorway's middle is half its width.
    /// </summary>
    public static float[] Clearance(NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        int n = graph.CellCount;
        var degree = new int[n];
        var histogram = new Dictionary<int, int>();
        for (int c = 0; c < n; c++)
        {
            foreach (NavEdge e in graph.EdgesFrom(c))
                degree[c] += e.Type == NavEdgeType.Walk ? 1 : 0;
            histogram[degree[c]] = histogram.GetValueOrDefault(degree[c]) + 1;
        }

        int open = histogram.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).Select(kv => kv.Key).FirstOrDefault();
        var clearance = new float[n];
        Array.Fill(clearance, float.PositiveInfinity);
        var queue = new PriorityQueue<int, (float, int)>();
        for (int c = 0; c < n; c++)
        {
            if (degree[c] < open)
            {
                clearance[c] = 0f;
                queue.Enqueue(c, (0f, c));
            }
        }

        while (queue.TryDequeue(out int c, out (float At, int) key))
        {
            if (key.At > clearance[c])
                continue;
            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (e.Type != NavEdgeType.Walk)
                    continue;
                float via = clearance[c] + graph.Cell(c).Centre.DistanceTo(graph.Cell(e.To).Centre);
                if (via < clearance[e.To])
                {
                    clearance[e.To] = via;
                    queue.Enqueue(e.To, (via, e.To));
                }
            }
        }

        for (int c = 0; c < n; c++)
        {
            if (float.IsPositiveInfinity(clearance[c]))
                clearance[c] = 0f;
        }

        return clearance;
    }

    private static ImmutableArray<ChokePoint> TopOf(NavGraph graph, float[] score)
    {
        int[] order = [.. Enumerable.Range(0, score.Length).OrderByDescending(c => score[c]).ThenBy(c => c)];
        var top = ImmutableArray.CreateBuilder<ChokePoint>(TopCount);
        foreach (int c in order)
        {
            if (top.Count == TopCount || score[c] < TopFloor)
                break;
            AiVector at = graph.Cell(c).Centre;
            bool apart = true;
            foreach (ChokePoint p in top)
                apart &= p.Position.DistanceTo(at) >= TopSeparation;
            if (apart)
                top.Add(new ChokePoint(new CellId(c), at, score[c]));
        }

        return top.ToImmutable();
    }

    /// <summary>One thread's arrays for one source's cheapest routes.</summary>
    private sealed class Scratch(int cells)
    {
        private const double Tie = 1e-3;
        private readonly double[] _cost = new double[cells];
        private readonly double[] _routes = new double[cells];
        private readonly double[] _dependency = new double[cells];
        private readonly bool[] _settled = new bool[cells];
        private readonly List<int>[] _before = NewLists(cells);
        private readonly int[] _order = new int[cells];
        private readonly PriorityQueue<int, (double, int)> _open = new();

        private static List<int>[] NewLists(int n)
        {
            var lists = new List<int>[n];
            for (int i = 0; i < n; i++)
                lists[i] = new List<int>(2);
            return lists;
        }

        /// <summary>Each cell's share of the cheapest routes from <paramref name="source"/> to each target (every cell with none), its own routes' ends excluded.</summary>
        public double[] FromSource(NavGraph graph, int source, bool[]? target)
        {
            Array.Fill(_cost, double.PositiveInfinity);
            Array.Clear(_routes);
            Array.Clear(_dependency);
            Array.Clear(_settled);
            foreach (List<int> b in _before)
                b.Clear();
            _open.Clear();
            _cost[source] = 0d;
            _routes[source] = 1d;
            _open.Enqueue(source, (0d, source));
            int settled = 0;
            while (_open.TryDequeue(out int c, out _))
            {
                if (_settled[c])
                    continue;
                _settled[c] = true;
                _order[settled++] = c;
                foreach (NavEdge e in graph.EdgesFrom(c))
                {
                    if (_settled[e.To])
                        continue;
                    double via = _cost[c] + e.Cost;
                    if (via < _cost[e.To] - Tie)
                    {
                        _cost[e.To] = via;
                        _routes[e.To] = _routes[c];
                        _before[e.To].Clear();
                        _before[e.To].Add(c);
                        _open.Enqueue(e.To, (via, e.To));
                    }
                    else if (via <= _cost[e.To] + Tie)
                    {
                        _routes[e.To] += _routes[c];
                        _before[e.To].Add(c);
                    }
                }
            }

            for (int i = settled - 1; i >= 0; i--)
            {
                int w = _order[i];
                foreach (int v in _before[w])
                    _dependency[v] += _routes[v] / _routes[w] * ((target is null || target[w] ? 1d : 0d) + _dependency[w]);
            }

            _dependency[source] = 0d;
            var result = new double[_dependency.Length];
            Array.Copy(_dependency, result, result.Length);
            return result;
        }
    }
}

/// <summary>
/// Where a level's <see cref="ChokeMap"/> turns up: computed on a worker while the
/// level runs, so empty at first, and shared by every bot of the map. A brain reads
/// it only after its <see cref="AiEventKind.ChokesKnown"/>, so when it filled does
/// not change what a replay decides.
/// </summary>
public sealed class ChokeSlot(ChokeMap? map = null)
{
    private volatile ChokeMap? _map = map;

    public ChokeMap? Map
    {
        get => _map;
        set => _map = value;
    }

    /// <summary>A replay: the map is worked out the moment the recording says the bots knew it.</summary>
    public void EnsureFor(NavGraph graph, MapKnowledge map) => _map ??= ChokeMap.Compute(graph, ChokeMap.PlacesOf(map));
}
