namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What finding a level's links answered: the set, whether it came from the cache, the thread CPU it took, and the pass's own count of candidates and flights (the cached pass's for a file).</summary>
public sealed record JumpLinkOutcome(JumpLinkSet Set, bool FromCache, double CpuMs, int Candidates, int Flights, double PassMs, string? CacheError)
{
    /// <summary>The plan the program flies for each link, index for index (<see cref="JumpLinkSet.PlanOf"/>; lane G5c-2b).</summary>
    public IReadOnlyList<JumpPlan> Plans { get; init; } = [];
}

/// <summary>
/// Finding a level's blast links once (lane G5c-2a): its cache file read, or the pass run and the
/// file written. Made on the game's thread with what only the game's thread may make (the
/// simulators read the class table), then <see cref="Run"/> on a worker, never in a decide: the
/// envelope's flights and the pass's searches are all its own.
/// </summary>
/// <param name="path">The cache file beside the graph's, or null for none.</param>
/// <param name="cpu">The thread's CPU clock in microseconds, or null for none.</param>
/// <param name="skip">A place the pass proposes no landing in (lane G6b), or null for none; <paramref name="inputsKey"/> must hold it.</param>
public sealed class JumpLinkWork(NavGraph graph, VoxelColumns columns, BlastJumpSim sim, BlastJumpSim openAir, JumpPilotLimits limits, JumpSearchSettings search,
                                 JumpPlacesSettings places, IReadOnlyList<int> seeds, NavLinkCosts costs, JumpLinkCache cache, string? path, string inputsKey,
                                 Func<double>? cpu, JumpSkip? skip = null)
{
    public NavGraph Graph => graph;

    /// <summary>The links, from the file when it holds this graph's of these inputs, else from the pass (which <paramref name="cancel"/> stops between candidates, throwing).</summary>
    public JumpLinkOutcome Run(CancellationToken cancel)
    {
        double start = cpu?.Invoke() ?? 0d;
        if (path is not null && cache.TryRead(path, graph, inputsKey) is { } cached)
        {
            JumpLinkSet read = JumpLinkSet.Of(graph, cached.Links, costs);
            return new JumpLinkOutcome(read, true, Ms(start), cached.Candidates, cached.Flights, cached.PassMs, null) { Plans = Plans(read, cancel) };
        }

        JumpEnvelope reach = JumpEnvelope.Measure(new JumpFlier(openAir, limits), search);
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, limits.RunSpeed);
        var pass = new JumpPlaces(graph, columns, new JumpSearch(new JumpFlier(sim, limits), walk, search), walk, reach, places, seeds, skip);
        JumpPlacesResult found = pass.Find(cancel);
        cancel.ThrowIfCancellationRequested();
        double passMs = Ms(start);
        var file = new JumpLinkFile(found.Links, found.Candidates.Count, found.Flights + reach.Flights, passMs);
        string? error = null;
        if (path is not null)
        {
            try
            {
                cache.Write(path, graph, inputsKey, file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = e.Message;
            }
        }

        JumpLinkSet set = JumpLinkSet.Of(graph, found.Links, costs);
        return new JumpLinkOutcome(set, false, Ms(start), file.Candidates, file.Flights, passMs, error) { Plans = Plans(set, cancel) };
    }

    /// <summary>The plans the program flies for <paramref name="set"/>'s links: one flight of each recipe from its start, for the touch, the apex and the time in the air.</summary>
    public JumpPlan[] Plans(JumpLinkSet set, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(set);
        var flier = new JumpFlier(sim, limits);
        var plans = new JumpPlan[set.Links.Count];
        for (int i = 0; i < plans.Length; i++)
        {
            cancel.ThrowIfCancellationRequested();
            JumpLink j = set.JumpOf(i);
            plans[i] = set.PlanOf(i, flier.Fly(j.Recipe, j.Recipe.Start(), search.MaxCommands));
        }

        return plans;
    }

    private double Ms(double start) => cpu is null ? 0d : (cpu() - start) / 1000d;
}
