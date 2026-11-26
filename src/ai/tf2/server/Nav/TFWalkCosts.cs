using System.Collections.Concurrent;
using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// A walk's cost as <see cref="TFWalkCosts"/> answers it: <see cref="Cost"/> in the graph's units (an edge costs its length
/// and what its kind adds). <see cref="Whole"/>: the cheapest path's own cost. Else the search ran out of its budget and
/// <see cref="Cost"/> is a lower bound on it (<see cref="NavPathfinder.LastBound"/>); infinite when there is no way or
/// either end is off the graph. <see cref="Expanded"/> and <see cref="Microseconds"/> are the search's work, 0 for a kept answer.
/// </summary>
public readonly record struct TFWalk(float Cost, bool Whole, int Expanded, long Microseconds, bool Kept)
{
    /// <summary>No way, or an end off the graph.</summary>
    public static TFWalk None => new(float.PositiveInfinity, true, 0, 0, false);
}

/// <summary>
/// The walking graph's route costs for TF2's deciders (round 2's S8: a teleporter's way against the walk), one service for
/// every bot: a pool of pathfinders (each sized to the graph: four arrays of its cells, about 3.6 MB on cp_granary, so not
/// one a bot), and the answers kept for <see cref="KeptTicks"/> by both ends' cells, so a team's bots weighing one
/// entrance search its long walks once. Every search is capped (its budget is the caller's); one that runs out answers a
/// lower bound, not "no walk". Deciders run on the worker pool, so the pool and the kept answers are thread-safe.
/// Every door is open (a team's own doors open for it; an enemy's would be found shut on the way).
/// </summary>
[SideService(Side.Server)]
public sealed class TFWalkCosts
{
    /// <summary>An answer is kept this many ticks (30 s at 66.7 ticks a second) for the same graph and ends.</summary>
    public const int KeptTicks = 2000;

    /// <summary>The kept answers past which the oldest are cleared.</summary>
    public const int KeptMost = 4096;

    private sealed class Search(NavPathfinder finder, NavObstacleState obstacles, List<CellId> cells, NavGraph graph)
    {
        public NavPathfinder Finder { get; } = finder;

        public NavObstacleState Obstacles { get; } = obstacles;

        public List<CellId> Cells { get; } = cells;

        public NavGraph Graph { get; } = graph;
    }

    private readonly ConcurrentBag<Search> _pool = [];
    private readonly ConcurrentDictionary<(ulong Graph, int From, int To, int Budget), (TFWalk Walk, int Tick)> _kept = new();
    private long _searches, _expanded, _microseconds, _keptHits;

    /// <summary>Searches made, cells they expanded, microseconds they took, and answers served kept: the service's work, for the harness.</summary>
    public (long Searches, long Expanded, long Microseconds, long KeptHits) Work
        => (Interlocked.Read(ref _searches), Interlocked.Read(ref _expanded), Interlocked.Read(ref _microseconds), Interlocked.Read(ref _keptHits));

    /// <summary>
    /// The walk from <paramref name="from"/> to <paramref name="to"/> over <paramref name="graph"/>, expanding at most
    /// <paramref name="budget"/> cells, at tick <paramref name="now"/>; kept for <see cref="KeptTicks"/>.
    /// </summary>
    public TFWalk Walk(NavGraph graph, AiVector from, AiVector to, int budget, int now)
    {
        ArgumentNullException.ThrowIfNull(graph);
        CellId a = graph.FindCell(from + new AiVector(0f, 0f, 1f)), b = graph.FindCell(to + new AiVector(0f, 0f, 1f));
        if (!a.IsValid || !b.IsValid)
            return TFWalk.None;
        var key = (graph.Hash, a.Index, b.Index, budget);
        if (_kept.TryGetValue(key, out (TFWalk Walk, int Tick) kept) && now - kept.Tick <= KeptTicks && now >= kept.Tick)
        {
            Interlocked.Increment(ref _keptHits);
            return kept.Walk with { Expanded = 0, Microseconds = 0, Kept = true };
        }

        Search search = Take(graph);
        try
        {
            long started = Stopwatch.GetTimestamp();
            PathStatus status = search.Finder.Find(graph, a, b, ClearanceClass.Stand, search.Obstacles, search.Cells, budget, out float cost);
            long us = (long)Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            int expanded = search.Finder.LastExpanded;
            TFWalk walk = status switch
            {
                PathStatus.Found => new TFWalk(cost, true, expanded, us, false),
                PathStatus.OutOfBudget => new TFWalk(search.Finder.LastBound, false, expanded, us, false),
                _ => TFWalk.None with { Expanded = expanded, Microseconds = us },
            };
            Interlocked.Increment(ref _searches);
            Interlocked.Add(ref _expanded, expanded);
            Interlocked.Add(ref _microseconds, us);
            if (_kept.Count >= KeptMost)
                _kept.Clear();
            _kept[key] = (walk, now);
            return walk;
        }
        finally
        {
            _pool.Add(search);
        }
    }

    /// <summary>A pooled search over <paramref name="graph"/>; one made for it when the pool has none (or only another graph's).</summary>
    private Search Take(NavGraph graph)
    {
        while (_pool.TryTake(out Search? search))
        {
            if (ReferenceEquals(search.Graph, graph))
                return search;
        }

        return new Search(new NavPathfinder(), new NavObstacleState(graph.ObstacleCount) { OpenDoors = true }, [], graph);
    }
}
